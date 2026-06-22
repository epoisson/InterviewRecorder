// ============================================================================
// Services/FileManager.cs - File I/O Operations
// ============================================================================
namespace InterviewRecorder.Services
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;
    using NAudio.Wave;

    /// <summary>
    /// Owns the on-disk layout under Documents\InterviewRecordings: session directories, chunk
    /// paths, metadata/log paths, completion markers, and merging chunk WAVs into the final file.
    /// </summary>
    public class FileManager
    {
        private readonly string _baseDirectory;

        public FileManager()
        {
            _baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "InterviewRecordings");
            Directory.CreateDirectory(_baseDirectory);
        }

        /// <summary>Root folder that holds all recording sessions.</summary>
        public string BaseDirectory => _baseDirectory;

        public string CreateSessionDirectory(string sessionId)
        {
            var sessionPath = Path.Combine(_baseDirectory, sessionId);
            Directory.CreateDirectory(sessionPath);
            Directory.CreateDirectory(Path.Combine(sessionPath, "chunks"));
            return sessionPath;
        }

        /// <summary>Lists existing chunk WAVs on disk for a session, in order. Used by crash recovery.</summary>
        public List<string> GetExistingChunkFiles(string sessionOutputPath)
        {
            var chunksDir = Path.Combine(sessionOutputPath, "chunks");
            if (!Directory.Exists(chunksDir)) return new List<string>();
            return Directory.GetFiles(chunksDir, "chunk_*.wav").OrderBy(f => f).ToList();
        }

        /// <summary>Returns the path for a chunk (creating the chunks folder), e.g. chunks/chunk_0003.wav.</summary>
        public string GetChunkPath(string sessionOutputPath, int chunkIndex)
        {
            var chunksDir = Path.Combine(sessionOutputPath, "chunks");
            Directory.CreateDirectory(chunksDir);
            return Path.Combine(chunksDir, $"chunk_{chunkIndex:D4}.wav");
        }

        public string GetMetadataPath(string sessionId)
        {
            return Path.Combine(_baseDirectory, sessionId, "metadata.json");
        }

        public string GetLogPath(string sessionId)
        {
            return Path.Combine(_baseDirectory, sessionId, "session.log");
        }

        /// <summary>Concatenates the session's chunk WAVs into one interview_&lt;timestamp&gt;.wav and returns its path.</summary>
        public async Task<string> MergeChunksAsync(RecordingSession session)
        {
            var finalPath = Path.Combine(session.OutputPath, $"interview_{session.StartTime:yyyyMMdd_HHmmss}.wav");
            
            if (!session.ChunkFiles.Any())
            {
                throw new InvalidOperationException("No chunks to merge.");
            }

            // Writer is created lazily from the first readable chunk's format, so a corrupt or
            // incomplete chunk (e.g. the one being written when the app crashed) is skipped.
            WaveFileWriter? writer = null;
            WaveFormat? format = null;
            try
            {
                foreach (var chunkFile in session.ChunkFiles)
                {
                    WaveFileReader reader;
                    try
                    {
                        reader = new WaveFileReader(chunkFile);
                    }
                    catch
                    {
                        continue; // skip unreadable / partially-written chunk
                    }

                    using (reader)
                    {
                        // Don't mix formats: a chunk recorded under different settings (e.g. config
                        // changed before a crash recovery) would corrupt a raw byte-copy merge. Skip it.
                        if (format != null && !reader.WaveFormat.Equals(format))
                            continue;

                        if (writer == null)
                        {
                            format = reader.WaveFormat;
                            writer = new WaveFileWriter(finalPath, format);
                        }
                        await reader.CopyToAsync(writer);
                    }
                }
            }
            finally
            {
                writer?.Dispose();
            }

            if (writer == null)
            {
                throw new InvalidOperationException("No readable chunks to merge.");
            }

            return finalPath;
        }

        public List<string> FindIncompleteSessionDirectories()
        {
            var directories = Directory.GetDirectories(_baseDirectory);
            var incompleteSessions = new List<string>();

            foreach (var dir in directories)
            {
                var metadataPath = Path.Combine(dir, "metadata.json");
                var completionMarker = Path.Combine(dir, ".completed");
                
                if (File.Exists(metadataPath) && !File.Exists(completionMarker))
                {
                    incompleteSessions.Add(Path.GetFileName(dir));
                }
            }

            return incompleteSessions;
        }

        public void CreateCompletionMarker(string sessionId)
        {
            var markerPath = Path.Combine(_baseDirectory, sessionId, ".completed");
            File.WriteAllText(markerPath, DateTime.Now.ToString("O"));
        }
    }
}

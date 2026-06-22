// ============================================================================
// Services/StateManager.cs - State Persistence
// ============================================================================
namespace InterviewRecorder.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;

    /// <summary>Persists and loads <see cref="RecordingSession"/> state as JSON metadata.</summary>
    public class StateManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly FileManager _fileManager;
        private readonly LogManager _logManager;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        public StateManager(FileManager fileManager, LogManager logManager)
        {
            _fileManager = fileManager;
            _logManager = logManager;
        }

        public async Task SaveStateAsync(RecordingSession session)
        {
            var metadataPath = _fileManager.GetMetadataPath(session.SessionId);

            // Serialize under the chunk-list lock so we don't enumerate ChunkFiles while the
            // capture thread is appending to it ("Collection was modified").
            string json;
            lock (session.ChunkFiles)
            {
                json = JsonSerializer.Serialize(session, JsonOptions);
            }

            // One writer at a time: concurrent saves (per-chunk + stop path) would collide on the file.
            await _saveLock.WaitAsync();
            try
            {
                await File.WriteAllTextAsync(metadataPath, json);
            }
            catch (Exception ex)
            {
                await _logManager.LogAsync($"Failed to save state: {ex.Message}");
                return;
            }
            finally
            {
                _saveLock.Release();
            }

            await _logManager.LogAsync($"State saved: {session.State}");
        }

        public async Task<RecordingSession?> LoadStateAsync(string sessionId)
        {
            var metadataPath = _fileManager.GetMetadataPath(sessionId);
            
            if (!File.Exists(metadataPath))
            {
                throw new FileNotFoundException($"Metadata not found for session {sessionId}");
            }

            var json = await File.ReadAllTextAsync(metadataPath);
            
            return JsonSerializer.Deserialize<RecordingSession>(json);
            
        }

        public async Task MarkSessionCompleteAsync(string sessionId)
        {
            _fileManager.CreateCompletionMarker(sessionId);
            await _logManager.LogAsync($"Session marked complete: {sessionId}");
        }
    }
}

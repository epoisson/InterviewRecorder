namespace InterviewRecorder.Services
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;
    using NAudio.Wave;

    /// <summary>
    /// Owns the active recording: creates the capture for the configured mode, writes incoming
    /// audio to fixed-length WAV chunks, rotates chunks on a timer, and queues each closed chunk
    /// to <see cref="FFmpegService"/> for m4a conversion. Also raises per-block peak levels for
    /// the waveform display.
    /// </summary>
    public class AudioCaptureEngine
    {
        private readonly FileManager _fileManager;
        private readonly LogManager _logManager;
        private readonly FFmpegService _ffmpegService;

        private IAudioCapture? _audioCapture;
        private RecordingSession _session = null!;
        private AudioConfig _config = null!;
        private WaveFormat _chunkFormat = null!;

        private WaveFileWriter? _currentChunkWriter;
        private TimeSpan _chunkDuration;
        private DateTime _chunkStartTime;
        private int _chunkIndex;

        private readonly object _chunkLock = new object();

        // Raised per audio block with the normalized peak amplitude (0..1) for the waveform display.
        public event Action<float>? AudioPeak;

        // Raised when a chunk is opened (number, start) and closed (number, end, duration).
        public event Action<int, DateTime>? ChunkStarted;
        public event Action<int, DateTime, TimeSpan, long>? ChunkClosed;

        private int _currentChunkNumber;

        public AudioCaptureEngine(FileManager fileManager, LogManager logManager, FFmpegService ffmpegService)
        {
            _fileManager = fileManager;
            _logManager = logManager;
            _ffmpegService = ffmpegService;
        }

        /// <summary>Creates the capture for the configured mode, opens the first chunk, and starts recording.</summary>
        public async Task StartRecording(RecordingSession session, AudioConfig config)
        {
            _session = session;
            _config = config;
            _chunkDuration = TimeSpan.FromMinutes(config.ChunkDurationMinutes);
            _chunkIndex = 0;

            CreateAndWireCapture();
            CreateNewChunk();
            await _audioCapture!.StartRecording(session, config);
            await _logManager.LogAsync("Audio engine started");
        }

        private void CreateAndWireCapture()
        {
            _audioCapture = CreateAudioCapture(_config.CaptureMode, _logManager, _config);
            _chunkFormat = _audioCapture.CaptureWaveFormat; // write the WAV in the capture's true format (option A)
            _audioCapture.DataAvailable += OnCaptureDataAvailable;
            _audioCapture.RecordingStopped += OnRecordingStopped;
        }

        private IAudioCapture CreateAudioCapture(AudioCaptureMode mode, LogManager logManager, AudioConfig config)
        {
            return mode switch
            {
                AudioCaptureMode.InputDevice => new MicrophoneCapture(logManager, config),
                AudioCaptureMode.Loopback => new LoopbackCapture(logManager, config),
                AudioCaptureMode.Mix => new MixedCapture(logManager, config),
                _ => throw new NotImplementedException()
            };
        }

        // Capture delivers audio at real time — write it and rotate the chunk when due.
        private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (_chunkLock)
            {
                _currentChunkWriter?.Write(e.Buffer, 0, e.BytesRecorded);

                if (DateTime.Now - _chunkStartTime >= _chunkDuration)
                {
                    var closedPath = CloseChunk();
                    OpenChunk();

                    // Only the compression hand-off is async; the chunk swap above is synchronous, so no audio gap.
                    if (_config.Compression.Enabled && !string.IsNullOrEmpty(closedPath))
                        _ = _ffmpegService.QueueConversion(closedPath, _config.Compression);
                }
            }

            ReportPeaks(e.Buffer, e.BytesRecorded);
        }

        // Emits a few peak values per buffer so the waveform scrolls smoothly regardless of buffer size.
        private void ReportPeaks(byte[] buffer, int bytes)
        {
            var handler = AudioPeak;
            if (handler == null || bytes <= 0) return;

            bool isFloat = _chunkFormat.Encoding == WaveFormatEncoding.IeeeFloat;
            int sampleSize = isFloat ? 4 : 2; // float32 (loopback/mix) vs PCM16 (mic)
            int totalSamples = bytes / sampleSize;
            if (totalSamples == 0) return;

            const int segments = 4;
            int perSegment = Math.Max(1, totalSamples / segments);

            for (int start = 0; start < totalSamples; start += perSegment)
            {
                int end = Math.Min(start + perSegment, totalSamples);
                float peak = 0f;

                for (int s = start; s < end; s++)
                {
                    int idx = s * sampleSize;
                    float a = isFloat
                        ? Math.Abs(BitConverter.ToSingle(buffer, idx))
                        : Math.Abs(BitConverter.ToInt16(buffer, idx) / 32768f);
                    if (a > peak) peak = a;
                }

                handler(peak > 1f ? 1f : peak);
            }
        }

        private void CreateNewChunk()
        {
            lock (_chunkLock) OpenChunk();
        }

        // Caller must hold _chunkLock.
        private void OpenChunk()
        {
            var chunkPath = _fileManager.GetChunkPath(_session.OutputPath, _chunkIndex);
            _currentChunkWriter = new WaveFileWriter(chunkPath, _chunkFormat);
            _chunkStartTime = DateTime.Now;
            _currentChunkNumber = _chunkIndex;
            _session.ChunkFiles.Add(chunkPath);
            _chunkIndex++;
            _ = _logManager.LogAsync($"Created chunk {Path.GetFileName(chunkPath)}");
            ChunkStarted?.Invoke(_currentChunkNumber, _chunkStartTime);
        }

        // Caller must hold _chunkLock.
        private string CloseChunk()
        {
            if (_currentChunkWriter == null) return string.Empty;
            string path = _currentChunkWriter.Filename;
            var endTime = DateTime.Now;
            _currentChunkWriter.Dispose();
            _currentChunkWriter = null;

            long size = 0;
            try { size = new FileInfo(path).Length; } catch { /* size best-effort */ }
            ChunkClosed?.Invoke(_currentChunkNumber, endTime, endTime - _chunkStartTime, size);
            return path;
        }

        private void OnRecordingStopped(object? sender, EventArgs e)
        {
            _ = _logManager.LogAsync("Capture device stopped");
        }

        public void StopRecording()
        {
            _audioCapture?.StopRecording();
            CloseAndQueueChunk();
            _audioCapture?.Dispose();
        }

        public void PauseRecording()
        {
            _audioCapture?.StopRecording();
            CloseAndQueueChunk();
        }

        // Closes the current chunk and queues it for conversion (used by both stop and pause;
        // rotation queues its chunks inline). Late DataAvailable callbacks are safe: the writer
        // is null after close, so their writes are dropped.
        private void CloseAndQueueChunk()
        {
            string closedPath;
            lock (_chunkLock) closedPath = CloseChunk();

            if (_config?.Compression.Enabled == true && !string.IsNullOrEmpty(closedPath))
                _ = _ffmpegService.QueueConversion(closedPath, _config.Compression);
        }

        public async Task ResumeRecording()
        {
            // After recovery the capture isn't created yet; build it on first resume.
            if (_audioCapture == null) CreateAndWireCapture();

            CreateNewChunk(); // continues at the next chunk index, not 0
            await _audioCapture!.ResumeRecording();
        }

        /// <summary>
        /// Prepares the engine to continue a recovered session: stores config and sets the next
        /// chunk index after the existing chunks. Capture is created on resume, not here.
        /// </summary>
        public void InitializeForRecovery(RecordingSession session, AudioConfig config)
        {
            _session = session;
            _config = config;
            _chunkDuration = TimeSpan.FromMinutes(config.ChunkDurationMinutes);
            _chunkIndex = session.ChunkFiles.Count; // resume numbering after recovered chunks
        }
    }
}

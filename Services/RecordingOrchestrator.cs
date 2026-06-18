// ============================================================================
// Updated RecordingOrchestrator.cs - FIXED with better error handling
// ============================================================================
namespace InterviewRecorder.Services
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Timers;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;

    /// <summary>
    /// Coordinates a recording session end to end: start, pause, resume, stop, and crash recovery.
    /// Drives the <see cref="AudioCaptureEngine"/> and <see cref="FFmpegService"/>, tracks session
    /// state, and exposes log, state-change, and audio-level events for the UI.
    /// </summary>
    public class RecordingOrchestrator : IDisposable
    {
        private readonly AudioCaptureEngine _audioCapture;
        private readonly FileManager _fileManager;
        private readonly StateManager _stateManager;
        private readonly RecoveryManager _recoveryManager;
        private readonly LogManager _logManager;
        private readonly ConfigurationManager _configManager;
        private readonly FFmpegService _ffmpegService;
        private RecordingSession? _currentSession;
        private readonly Timer _durationTimer;

        // Duration counts active recording time only, excluding paused periods.
        private TimeSpan _accumulatedDuration;  // recorded time from completed segments
        private DateTime _segmentStartTime;     // start of the current recording segment

        public event Action<string>? LogMessage;
        public event Action? StateChanged;
        public event Action<float>? AudioLevel;
        public event Action<int, DateTime>? ChunkStarted;
        public event Action<int, DateTime, TimeSpan, long>? ChunkClosed;
        public event Action<string>? ConversionStarted;
        public event Action<string, bool>? ConversionCompleted;
        public event Action<string, string, long>? RecordingMerged; // fileName, status, sizeBytes

        public string RecordingsFolder => _fileManager.BaseDirectory;
        public int CurrentInputDeviceId => _configManager.CurrentAudioConfig.InputDevice.DeviceId;
        public IReadOnlyList<string> CurrentChunkFiles =>
            _currentSession?.ChunkFiles ?? (IReadOnlyList<string>)Array.Empty<string>();
        public Task SetInputDeviceAsync(int deviceId) => _configManager.SetInputDeviceAsync(deviceId);

        public RecordingState CurrentState => _currentSession?.State ?? RecordingState.Idle;
        public TimeSpan CurrentDuration => _currentSession?.Duration ?? TimeSpan.Zero;
        public bool IsRecording => CurrentState == RecordingState.Recording;
        public string? CurrentSessionId => _currentSession?.SessionId;
        
        private RecordingOrchestrator(FileManager fileManager, LogManager logManager, ConfigurationManager configManager, RecoveryManager recoveryManager, AudioCaptureEngine audioCapture, FFmpegService ffmpegService)
        {
            _fileManager = fileManager;
            _logManager = logManager;
            _configManager = configManager;
            _recoveryManager = recoveryManager;
            _stateManager = new StateManager(fileManager, logManager);
            _audioCapture = audioCapture;
            _audioCapture.AudioPeak += level => AudioLevel?.Invoke(level);
            _audioCapture.ChunkStarted += (n, start) => ChunkStarted?.Invoke(n, start);
            _audioCapture.ChunkClosed += (n, end, dur, size) =>
            {
                ChunkClosed?.Invoke(n, end, dur, size);
                // Persist state each chunk so a crash can be recovered with an up-to-date chunk list.
                var session = _currentSession;
                if (session != null) _ = _stateManager.SaveStateAsync(session);
            };
            _ffmpegService = ffmpegService;
            _ffmpegService.ConversionStarted += path => ConversionStarted?.Invoke(path);
            _ffmpegService.ConversionCompleted += (path, ok) => ConversionCompleted?.Invoke(path, ok);

            _durationTimer = new Timer(100); // Update every 100ms
            _durationTimer.Elapsed += OnDurationTimerElapsed;
            _logManager.LogMessage += (msg) => LogMessage?.Invoke(msg);            
            _configManager.ConfigurationChanged += OnConfigurationChanged;                        
            CheckFFmpegAvailability();           
        }

        public static async Task<RecordingOrchestrator> Create()
        {           
            var fileManager = new FileManager();
            var logManager = new LogManager(fileManager);                    
            var configManager = await ConfigurationManager.Create(logManager);                    
            var ffmpegService = new FFmpegService(logManager);
            var recoveryManager = new RecoveryManager(fileManager, logManager);
            var audioCapture = new AudioCaptureEngine(fileManager, logManager, ffmpegService);
            return new RecordingOrchestrator(fileManager, logManager, configManager,recoveryManager,audioCapture,ffmpegService);

        }

        public void Dispose()
        {
            _durationTimer?.Stop();
            _durationTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
        
        private void OnDurationTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_currentSession?.State == RecordingState.Recording)
            {
                _currentSession.Duration = _accumulatedDuration + (DateTime.Now - _segmentStartTime);
                StateChanged?.Invoke(); // Notify UI to update
            }
        }

        private async void CheckFFmpegAvailability()
        {
            var config = _configManager.CurrentAudioConfig;
            if (config.Compression.Enabled)
            {
                var available = await _ffmpegService.CheckFFmpegAvailable(config.Compression.FFmpegPath);
                if (!available)
                {
                    await _logManager.LogAsync($"WARNING: FFmpeg not found at '{config.Compression.FFmpegPath}'. Compression will not work.");
                    await _logManager.LogAsync("Please install FFmpeg or update the path in appsettings.json");
                }
                else
                {
                    await _logManager.LogAsync("FFmpeg detected and ready");
                    _ffmpegService.Start();
                }
            }
        }

        private void OnConfigurationChanged(AudioConfig newConfig)
        {
            if (IsRecording)
            {
                // Capture format is fixed when recording starts; changes apply to the next recording.
                LogMessage?.Invoke("Configuration change will apply to the next recording");
            }
            else
            {
                LogMessage?.Invoke("Configuration updated");

                // Restart FFmpeg service if compression settings changed
                if (newConfig.Compression.Enabled)
                {
                    CheckFFmpegAvailability();
                }
            }
        }

        public string GetConfigFilePath() => _configManager.GetConfigFilePath();

        /// <summary>Logs a user interface action as "UI EVENT: &lt;label&gt;".</summary>
        public void LogUiEvent(string label)
        {
            _ = _logManager.LogAsync($"UI EVENT: {label}");
        }

        public async Task<RecordingSession?> CheckForIncompleteSessionAsync()
        {
            return await _recoveryManager.FindIncompleteSessionAsync();            
        }

        public async Task RecoverSessionAsync(string sessionId)
        {
            _currentSession = await _recoveryManager.LoadSessionAsync(sessionId);
            if (_currentSession == null)
            {
                await _logManager.LogAsync($"Session {sessionId} could not be recovered");
                return;
            }

            // Metadata may predate the last chunks (state isn't always saved per chunk), so trust the
            // chunk files on disk. Without this, merge on stop finds no chunks and fails.
            _currentSession.ChunkFiles = _fileManager.GetExistingChunkFiles(_currentSession.OutputPath);

            var config = _configManager.CurrentAudioConfig;
            _audioCapture.InitializeForRecovery(_currentSession, config);

            // Recover into a paused state: keep the session id, resume into the next chunk on demand.
            _currentSession.State = RecordingState.Paused;
            _accumulatedDuration = _currentSession.Duration;
            _segmentStartTime = DateTime.Now;
            await _stateManager.SaveStateAsync(_currentSession);

            await _logManager.LogAsync($"Session {sessionId} recovered (paused) with {_currentSession.ChunkFiles.Count} chunk(s)");

            // Queue any chunks that were never converted (crash before their conversion ran).
            if (config.Compression.Enabled)
            {
                int queued = 0;
                foreach (var wav in _currentSession.ChunkFiles)
                {
                    if (!File.Exists(Path.ChangeExtension(wav, config.Compression.Format)))
                    {
                        _ = _ffmpegService.QueueConversion(wav, config.Compression);
                        queued++;
                    }
                }
                if (queued > 0)
                    await _logManager.LogAsync($"Queued {queued} unconverted chunk(s) for conversion");
            }

            StateChanged?.Invoke();
        }

        /// <summary>Creates a new session, starts capture, and begins the duration timer.</summary>
        public async Task StartRecordingAsync()
        {
            if (_currentSession != null && _currentSession.State != RecordingState.Completed)
            {
                throw new InvalidOperationException("A session is already active. Please stop it first.");
            }
            
            var config = _configManager.CurrentAudioConfig;            
            
            var sessId = Guid.NewGuid().ToString("N");
            _currentSession = new RecordingSession
            {
                SessionId = sessId,
                StartTime = DateTime.Now,
                State = RecordingState.Recording,
                OutputPath = _fileManager.CreateSessionDirectory(sessId),
                SampleRate = config.SampleRate,
                Channels = config.Channels,
                BitsPerSample = config.BitsPerSample
            };
            
            _logManager.InitializeLog(_currentSession.SessionId);
            
            await _stateManager.SaveStateAsync(_currentSession);            
            
            await _audioCapture.StartRecording(_currentSession, config);

            // Start the duration timer
            _accumulatedDuration = TimeSpan.Zero;
            _segmentStartTime = DateTime.Now;
            _durationTimer.Start();
            
            await _logManager.LogAsync($"Recording started: {_currentSession.SessionId}");
            StateChanged?.Invoke();
        }

        public async Task PauseRecordingAsync()
        {
            if (_currentSession?.State != RecordingState.Recording)
            {
                throw new InvalidOperationException("No active recording to pause.");
            }
            
            _currentSession.State = RecordingState.Paused;

            // Bank the elapsed segment and freeze the displayed duration.
            _accumulatedDuration += DateTime.Now - _segmentStartTime;
            _currentSession.Duration = _accumulatedDuration;

            await _stateManager.SaveStateAsync(_currentSession);
            _audioCapture.PauseRecording();

            // Stop the timer when paused
            _durationTimer.Stop();
            
            await _logManager.LogAsync("Recording paused");
            StateChanged?.Invoke();
        }

        public async Task ResumeRecordingAsync()
        {
            if (_currentSession?.State != RecordingState.Paused)
            {
                throw new InvalidOperationException("No paused recording to resume.");
            }

            
            _currentSession.State = RecordingState.Recording;
            await _stateManager.SaveStateAsync(_currentSession);
            await _audioCapture.ResumeRecording();

            // Resume counting from where pause left off.
            _segmentStartTime = DateTime.Now;
            _durationTimer.Start();

            await _logManager.LogAsync("Recording resumed");
            StateChanged?.Invoke();
        }

        /// <summary>Stops capture, drains compression, merges the WAV and m4a chunks, and finalises the session. Returns the final WAV path.</summary>
        public async Task<string> StopRecordingAsync()
        {
            if (_currentSession == null || _currentSession.State == RecordingState.Idle)
            {
                throw new InvalidOperationException("No active recording to stop.");
            }

            // Bank the final segment if we were still recording (not if already paused).
            bool wasRecording = _currentSession.State == RecordingState.Recording;

            // Stop the timer first
            _durationTimer.Stop();

            // Stop audio capture (synchronous, fast)
            _audioCapture.StopRecording();
            if (wasRecording)
                _accumulatedDuration += DateTime.Now - _segmentStartTime;

            _currentSession.State = RecordingState.Stopped;
            _currentSession.EndTime = DateTime.Now;
            _currentSession.Duration = _accumulatedDuration;

            await _logManager.LogAsync("Stopping recording...");
            
            // Stop FFmpeg service and wait for queue to finish (with error handling)
            try
            {
                await _logManager.LogAsync("Waiting for compression to complete...");
                await _ffmpegService.Stop();
            }
            catch (OperationCanceledException)
            {
                // This is expected when cancelling - not an error
                await _logManager.LogAsync("Compression queue stopped (no pending conversions)");
            }
            catch (Exception ex)
            {
                // Log but don't fail - we can still merge chunks
                await _logManager.LogAsync($"Warning during FFmpeg shutdown: {ex.Message}");
            }

            // Merge all chunks into final file
            try
            {
                var config = _configManager.CurrentAudioConfig;

                await _logManager.LogAsync("Merging audio chunks...");
                var finalFile = await _fileManager.MergeChunksAsync(_currentSession);

                // Merge the converted m4a chunks into a single compressed file next to the wav.
                string mergeStatus = "";
                if (config.Compression.Enabled)
                {
                    var m4aChunks = _currentSession.ChunkFiles
                        .Select(wav => Path.ChangeExtension(wav, config.Compression.Format))
                        .Where(File.Exists)
                        .ToList();

                    if (m4aChunks.Count > 0)
                    {
                        var finalM4a = Path.ChangeExtension(finalFile, config.Compression.Format);
                        await _ffmpegService.MergeToM4a(m4aChunks, finalM4a, config.Compression);
                        mergeStatus = File.Exists(finalM4a) ? "Done" : "Failed";
                    }
                    else
                    {
                        await _logManager.LogAsync("No m4a chunks found to merge");
                        mergeStatus = "Failed";
                    }
                }

                long finalSize = 0;
                try { finalSize = new FileInfo(finalFile).Length; } catch { /* size best-effort */ }
                RecordingMerged?.Invoke(Path.GetFileName(finalFile), mergeStatus, finalSize);

                _currentSession.State = RecordingState.Completed;
                await _stateManager.SaveStateAsync(_currentSession);
                await _stateManager.MarkSessionCompleteAsync(_currentSession.SessionId);

                await _logManager.LogAsync($"Recording completed: {Path.GetFileName(finalFile)}");
                StateChanged?.Invoke();

                // Restart FFmpeg service for next recording
                if (config.Compression.Enabled)
                {
                    _ffmpegService.Start();
                }

                return finalFile;
            }
            catch (Exception ex)
            {
                await _logManager.LogAsync($"ERROR during finalization: {ex.Message}");
                _currentSession.State = RecordingState.Stopped; // Not completed
                StateChanged?.Invoke();
                throw;
            }
        }

        public string GetStatusDetails()
        {
            if (_currentSession == null)
            {
                return "No active session.";
            }

            return $"Session ID: {_currentSession.SessionId}\n" +
                   $"State: {_currentSession.State}\n" +
                   $"Started: {_currentSession.StartTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"Duration: {_currentSession.Duration:hh\\:mm\\:ss}\n" +
                   $"Sample Rate: {_currentSession.SampleRate} Hz\n" +
                   $"Channels: {_currentSession.Channels} (Mono)\n" +
                   $"Output Path: {_currentSession.OutputPath}\n" +
                   $"Chunks: {_currentSession.ChunkFiles.Count}";
        }
    }
}

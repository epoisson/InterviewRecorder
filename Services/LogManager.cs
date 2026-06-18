// ============================================================================
// Updated LogManager.cs - With Event
// ============================================================================
namespace InterviewRecorder.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Writes timestamped entries to the session log file and raises a message event for the UI.
    /// File writes are serialised, since logging is called concurrently from several threads.
    /// </summary>
    public class LogManager
    {
        private readonly FileManager _fileManager;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private string _currentLogPath = string.Empty;

        public event Action<string>? LogMessage;

        public LogManager(FileManager fileManager)
        {
            _fileManager = fileManager;            
        }

        public void InitializeLog(string sessionId)
        {
            _currentLogPath = _fileManager.GetLogPath(sessionId);
        }

        public async Task LogAsync(string message)
        {
            LogMessage?.Invoke(message);

            if (string.IsNullOrEmpty(_currentLogPath)) return;

            var timestampedEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";

            // Serialize file writes: LogAsync is called concurrently from the UI, engine and FFmpeg worker.
            await _writeLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_currentLogPath, timestampedEntry);
            }
            catch
            {
                // Never let a logging failure surface as an error / break recording.
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}

// ============================================================================
// Services/RecoveryManager.cs - Crash Recovery
// ============================================================================
namespace InterviewRecorder.Services
{
    using System.Linq;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;

    /// <summary>Finds and loads sessions that were not finalised, for crash recovery on startup.</summary>
    public class RecoveryManager
    {
        private readonly FileManager _fileManager;
        private readonly LogManager _logManager;

        public RecoveryManager(FileManager fileManager, LogManager logManager)
        {
            _fileManager = fileManager;
            _logManager = logManager;
        }

        public async Task<RecordingSession?> FindIncompleteSessionAsync()
        {
            var incompleteSessions = _fileManager.FindIncompleteSessionDirectories();
            
            if (incompleteSessions.Any())
            {
                var sessionId = incompleteSessions.First();
                await _logManager.LogAsync($"Found incomplete session: {sessionId}");
                
                var stateManager = new StateManager(_fileManager, _logManager);
                return await stateManager.LoadStateAsync(sessionId);
            }

            return null;
        }

        public async Task<RecordingSession?> LoadSessionAsync(string sessionId)
        {
            var stateManager = new StateManager(_fileManager, _logManager);
            return await stateManager.LoadStateAsync(sessionId);
        }
    }
}

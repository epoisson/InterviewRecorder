// ============================================================================
// Services/StateManager.cs - State Persistence
// ============================================================================
namespace InterviewRecorder.Services
{
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;

    /// <summary>Persists and loads <see cref="RecordingSession"/> state as JSON metadata.</summary>
    public class StateManager
    {
        private readonly FileManager _fileManager;
        private readonly LogManager _logManager;

        public StateManager(FileManager fileManager, LogManager logManager)
        {
            _fileManager = fileManager;
            _logManager = logManager;
        }

        public async Task SaveStateAsync(RecordingSession session)
        {
            var metadataPath = _fileManager.GetMetadataPath(session.SessionId);
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await File.WriteAllTextAsync(metadataPath, json);
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

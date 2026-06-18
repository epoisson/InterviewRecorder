// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    public class SystemAudioConfig
    {
        public bool Enabled { get; set; } = false;
        public string DeviceId { get; set; } = string.Empty;
    }
}
// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    public class InputDeviceConfig
    {
        public bool Enabled { get; set; } = true;
        public int DeviceId { get; set; } = 0;
    }
}
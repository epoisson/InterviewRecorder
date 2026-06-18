// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    using System;

    public class AudioDeviceInfo
    {
        public int DeviceId { get; set; } = -1;
        public string Name { get; set; } = String.Empty;
        public bool Enabled { get; set; } = false;

    }
}
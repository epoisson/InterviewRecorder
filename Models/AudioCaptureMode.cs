// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    /// <summary>Which audio source a recording captures.</summary>
    public enum AudioCaptureMode
    {
        InputDevice,    // Microphone, line-in, etc.
        Loopback        // System audio output (what you hear)
    }
}
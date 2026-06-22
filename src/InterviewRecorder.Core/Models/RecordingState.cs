// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    /// <summary>Lifecycle state of a recording session.</summary>
    public enum RecordingState
    {
        Idle,
        Recording,
        Paused,
        Stopped,
        Completed
    }
}

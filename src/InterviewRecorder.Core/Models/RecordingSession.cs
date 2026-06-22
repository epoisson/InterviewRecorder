// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>State and metadata for one recording: id, timing, output path, format, and chunk list.</summary>
    public class RecordingSession
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; } = new TimeSpan();
        public RecordingState State { get; set; } = new RecordingState();
        public string OutputPath { get; set; } = string.Empty;
        public List<String> ChunkFiles { get; set; } = [];
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 1;
        public int BitsPerSample { get; set; } = 16;
        public long CurrentPosition { get; set; }
        public DateTime LastStateUpdate { get; set; }
    }
}
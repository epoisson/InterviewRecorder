// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    /// <summary>FFmpeg compression settings: codec, bitrate, output format, and executable path.</summary>
    public class CompressionConfig
    {
        public bool Enabled { get; set; } = true;
        public string Format { get; set; } = "m4a";  // "m4a" or "opus"
        public string Codec { get; set; } = "aac";   // "aac" or "libopus"
        public int Bitrate { get; set; } = 128;  // kbps
        public bool DeleteWavAfterConversion { get; set; } = false;
        public string FFmpegPath { get; set; } = "ffmpeg";  // Path to ffmpeg executable
    }
}
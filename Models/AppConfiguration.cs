// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    using System.Text.Json.Serialization;

    public class AppConfiguration
    {
        // appsettings.json uses the key "AudioConfiguration".
        [JsonPropertyName("AudioConfiguration")]
        public AudioConfig AudioSettings { get; set; } = new AudioConfig();
    }
}
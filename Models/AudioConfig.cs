// ============================================================================
// Models/RecordingSession.cs
// ============================================================================
namespace InterviewRecorder.Models
{
    /// <summary>User-editable audio settings: capture mode, format, chunk length, devices, and compression.</summary>
    public class AudioConfig
    {
        public AudioDeviceInfo SystemAudioDevice { get; set; } = new AudioDeviceInfo();
        public AudioDeviceInfo MicrophoneDevice { get; set; } = new AudioDeviceInfo();
        public float SystemAudioVolume { get; set; } = 1.0f;
        public float MicrophoneVolume { get; set; } = 1.0f;
        public AudioCaptureMode CaptureMode { get; set; }
        public InputDeviceConfig InputDevice { get; set; } = new InputDeviceConfig();
        public SystemAudioConfig SystemAudio { get; set; } = new SystemAudioConfig();
        public int SampleRate { get; set; } = 44100;
        public int Channels { get; set; } = 1;
        public int BitsPerSample { get; set; } = 16;
        public int ChunkDurationMinutes { get; set; } = 1;
        public int AutoSaveIntervalSeconds { get; set; } = 30;
        public CompressionConfig Compression { get; set; } = new CompressionConfig();

    }
}
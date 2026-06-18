namespace InterviewRecorder.Services
{
    using System;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;
    using NAudio.Wave;

    /// <summary>Captures a single input device (microphone) as PCM, forwarding NAudio's events directly.</summary>
    public class MicrophoneCapture : IAudioCapture
    {
        private readonly WaveInEvent _waveIn;
        private readonly LogManager _logManager;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler? RecordingStopped;

        public WaveFormat CaptureWaveFormat => _waveIn.WaveFormat;

        public MicrophoneCapture(LogManager logManager, AudioConfig config)
        {
            _logManager = logManager;

            _waveIn = new WaveInEvent
            {
                DeviceNumber = config.InputDevice.DeviceId,
                WaveFormat = new WaveFormat(config.SampleRate, config.BitsPerSample, config.Channels),
                BufferMilliseconds = 100
            };

            // NAudio delivers PCM at real time — forward it straight to the engine.
            _waveIn.DataAvailable += (s, e) => DataAvailable?.Invoke(this, e);
            _waveIn.RecordingStopped += (s, e) => RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        public Task StartRecording(RecordingSession session, AudioConfig config)
        {
            _waveIn.StartRecording();
            return _logManager.LogAsync($"Microphone capture started ({CaptureWaveFormat.SampleRate}Hz PCM{CaptureWaveFormat.BitsPerSample})");
        }

        public Task PauseRecording()
        {
            _waveIn.StopRecording();
            return _logManager.LogAsync("Microphone capture paused");
        }

        public Task ResumeRecording()
        {
            _waveIn.StartRecording();
            return _logManager.LogAsync("Microphone capture resumed");
        }

        public void StopRecording() => _waveIn.StopRecording();

        public void Dispose()
        {
            _waveIn.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

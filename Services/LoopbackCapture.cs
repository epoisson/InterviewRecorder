namespace InterviewRecorder.Services
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;
    using NAudio.Wave;
    using NAudio.CoreAudioApi;

    /// <summary>Captures system audio output (WASAPI loopback) as float, forwarding NAudio's events directly.</summary>
    public class LoopbackCapture : IAudioCapture
    {
        private readonly WasapiLoopbackCapture _loopbackCapture;
        private readonly LogManager _logManager;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler? RecordingStopped;

        // Real device format (usually Float32 stereo 48k).
        public WaveFormat CaptureWaveFormat => _loopbackCapture.WaveFormat;

        public LoopbackCapture(LogManager logManager, AudioConfig config)
        {
            _logManager = logManager;

            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                   .ElementAtOrDefault(config.SystemAudioDevice.DeviceId)
                         ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _loopbackCapture = new WasapiLoopbackCapture(device);

            // WASAPI delivers float audio at real time — forward it straight to the engine.
            _loopbackCapture.DataAvailable += (s, e) => DataAvailable?.Invoke(this, e);
            _loopbackCapture.RecordingStopped += (s, e) => RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        public Task StartRecording(RecordingSession session, AudioConfig config)
        {
            _loopbackCapture.StartRecording();
            return _logManager.LogAsync($"Loopback capture started ({CaptureWaveFormat.SampleRate}Hz float)");
        }

        public Task PauseRecording()
        {
            _loopbackCapture.StopRecording();
            return _logManager.LogAsync("Loopback capture paused");
        }

        public Task ResumeRecording()
        {
            _loopbackCapture.StartRecording();
            return _logManager.LogAsync("Loopback capture resumed");
        }

        public void StopRecording() => _loopbackCapture.StopRecording();

        public void Dispose()
        {
            _loopbackCapture.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

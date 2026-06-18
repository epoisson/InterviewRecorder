namespace InterviewRecorder.Services
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;
    using NAudio.Wave;
    using NAudio.CoreAudioApi;
    using NAudio.Wave.SampleProviders;

    /// <summary>
    /// Captures microphone and system audio together, mixing them to a single float stream.
    /// Uses a real-time-paced pull loop over a <c>MixingSampleProvider</c> (the one capture mode
    /// that must pull, because two live sources are combined).
    /// </summary>
    public class MixedCapture : IAudioCapture
    {
        private readonly WasapiLoopbackCapture _systemCapture;
        private readonly WaveInEvent _micCapture;
        private readonly BufferedWaveProvider _loopBuffer;
        private readonly BufferedWaveProvider _micBuffer;
        private readonly IWaveProvider _floatWaveProvider;
        private readonly LogManager _logManager;

        private CancellationTokenSource? _cts;
        private Task? _mixTask;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler? RecordingStopped;

        public WaveFormat CaptureWaveFormat { get; }

        public MixedCapture(LogManager logManager, AudioConfig config)
        {
            _logManager = logManager;

            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .ElementAtOrDefault(config.SystemAudioDevice?.DeviceId ?? 0)
                ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _systemCapture = new WasapiLoopbackCapture(device);
            CaptureWaveFormat = _systemCapture.WaveFormat; // float32 stereo

            _micCapture = new WaveInEvent
            {
                DeviceNumber = config.InputDevice.DeviceId,
                WaveFormat = new WaveFormat(CaptureWaveFormat.SampleRate, 16, 1),
                BufferMilliseconds = 100
            };

            // ReadFully=false so empty buffers return 0 (not silence) — lets the mix loop pace to real time.
            _loopBuffer = new BufferedWaveProvider(CaptureWaveFormat) { DiscardOnBufferOverflow = true, ReadFully = false };
            _micBuffer = new BufferedWaveProvider(_micCapture.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = false };

            _systemCapture.DataAvailable += (s, e) => _loopBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _micCapture.DataAvailable += (s, e) => _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _systemCapture.RecordingStopped += (s, e) => RecordingStopped?.Invoke(this, EventArgs.Empty);

            _floatWaveProvider = BuildPipeline();
        }

        private IWaveProvider BuildPipeline()
        {
            ISampleProvider loopSample = _loopBuffer.ToSampleProvider();

            ISampleProvider micSample = _micBuffer.ToSampleProvider();
            micSample = new WdlResamplingSampleProvider(micSample, CaptureWaveFormat.SampleRate);
            micSample = new MonoToStereoSampleProvider(micSample);

            // ReadFully=true so live inputs are never dropped when momentarily empty
            // (with ReadFully=false the mixer permanently removes any input that returns 0).
            var mixer = new MixingSampleProvider(CaptureWaveFormat) { ReadFully = true };
            mixer.AddMixerInput(loopSample);
            mixer.AddMixerInput(micSample);

            return new SampleToWaveProvider(mixer);
        }

        public Task StartRecording(RecordingSession session, AudioConfig config) => StartCaptures();

        public Task ResumeRecording() => StartCaptures();

        private Task StartCaptures()
        {
            _cts = new CancellationTokenSource();
            _systemCapture.StartRecording();
            _micCapture.StartRecording();

            var token = _cts.Token;
            _mixTask = Task.Run(() =>
            {
                byte[] buffer = new byte[8192];
                int bytesPerSecond = CaptureWaveFormat.AverageBytesPerSecond;
                var clock = Stopwatch.StartNew();
                long totalBytes = 0;

                while (!token.IsCancellationRequested)
                {
                    int read = _floatWaveProvider.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, read));
                        totalBytes += read;
                    }

                    // The ReadFully mixer always returns full buffers, so pace output to real
                    // time; otherwise the loop would emit audio (and silence padding) at CPU speed.
                    double dueMs = totalBytes * 1000.0 / bytesPerSecond;
                    double aheadMs = dueMs - clock.Elapsed.TotalMilliseconds;
                    if (aheadMs > 1)
                        Thread.Sleep((int)Math.Min(aheadMs, 50));
                }
            });

            return _logManager.LogAsync($"Mixed capture started ({CaptureWaveFormat.SampleRate}Hz float)");
        }

        public Task PauseRecording()
        {
            StopRecording();
            return _logManager.LogAsync("Mixed capture paused");
        }

        public void StopRecording()
        {
            _cts?.Cancel();
            try { _mixTask?.Wait(500); } catch { /* loop cancelled */ }
            _systemCapture.StopRecording();
            _micCapture.StopRecording();
        }

        public void Dispose()
        {
            StopRecording();
            _systemCapture.Dispose();
            _micCapture.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

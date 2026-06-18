namespace InterviewRecorder.Services
{
    using System;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;
    using NAudio.Wave;

    /// <summary>
    /// An audio source that raises <see cref="DataAvailable"/> with real-time audio in its own
    /// <see cref="CaptureWaveFormat"/>. Implemented per capture mode (microphone, loopback, mix).
    /// </summary>
    public interface IAudioCapture : IDisposable
    {
        Task StartRecording(RecordingSession session, AudioConfig config);
        Task PauseRecording();
        Task ResumeRecording();
        void StopRecording();

        event EventHandler<WaveInEventArgs> DataAvailable;
        event EventHandler RecordingStopped;

        // 🔥 NEW — tells engine what is ACTUALLY coming
        WaveFormat CaptureWaveFormat { get; }
    }

}

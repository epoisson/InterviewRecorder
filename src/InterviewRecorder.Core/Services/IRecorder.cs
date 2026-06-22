namespace InterviewRecorder.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;

    /// <summary>
    /// UI-agnostic recording surface a front-end binds to, so any UI (WPF, console, web)
    /// can drive recording without depending on the concrete <see cref="RecordingOrchestrator"/>.
    /// </summary>
    public interface IRecorder : IDisposable
    {
        event Action<string>? LogMessage;
        event Action? StateChanged;
        event Action<float>? AudioLevel;
        event Action<int, DateTime>? ChunkStarted;
        event Action<int, DateTime, TimeSpan, long>? ChunkClosed;
        event Action<string>? ConversionStarted;
        event Action<string, bool>? ConversionCompleted;
        event Action<string, string, long>? RecordingMerged;

        RecordingState CurrentState { get; }
        TimeSpan CurrentDuration { get; }
        bool IsRecording { get; }
        string? CurrentSessionId { get; }
        IReadOnlyList<string> CurrentChunkFiles { get; }
        string RecordingsFolder { get; }
        int CurrentInputDeviceId { get; }
        string CompressionFormat { get; }

        string GetConfigFilePath();
        string GetStatusDetails();
        void LogUiEvent(string label);

        Task SetInputDeviceAsync(int deviceId);
        Task StartRecordingAsync();
        Task PauseRecordingAsync();
        Task ResumeRecordingAsync();
        Task<string> StopRecordingAsync();
        Task<RecordingSession?> CheckForIncompleteSessionAsync();
        Task RecoverSessionAsync(string sessionId);
    }
}

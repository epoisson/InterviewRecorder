namespace InterviewRecorder.Models
{
    using System;
    using System.ComponentModel;

    /// <summary>One row in the chunks grid: a recorded chunk, or the merged file when complete.</summary>
    public class ChunkInfo : INotifyPropertyChanged
    {
        public string Number { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }

        private DateTime? _endTime;
        public DateTime? EndTime
        {
            get => _endTime;
            set { _endTime = value; OnChanged(nameof(EndTime)); OnChanged(nameof(EndText)); }
        }

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set { _duration = value; OnChanged(nameof(Duration)); OnChanged(nameof(DurationText)); }
        }

        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set { _sizeBytes = value; OnChanged(nameof(SizeBytes)); OnChanged(nameof(SizeText)); }
        }

        public string SizeText => _sizeBytes <= 0
            ? ""
            : _sizeBytes >= 1024 * 1024
                ? $"{_sizeBytes / (1024.0 * 1024.0):0.0} MB"
                : $"{_sizeBytes / 1024.0:0} KB";

        // "", "Processing", "Done", or "Failed".
        private string _conversionStatus = string.Empty;
        public string ConversionStatus
        {
            get => _conversionStatus;
            set { _conversionStatus = value; OnChanged(nameof(ConversionStatus)); OnChanged(nameof(StatusText)); }
        }

        public string StartText => StartTime.ToString("HH:mm:ss");
        public string EndText => _endTime?.ToString("HH:mm:ss") ?? "recording";
        public string DurationText => _duration == TimeSpan.Zero ? "" : _duration.ToString(@"hh\:mm\:ss");

        public string StatusText => _conversionStatus switch
        {
            "Processing" => "⏳ Processing",
            "Done" => "✓ Done",
            "Failed" => "✗ Failed",
            _ => ""
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

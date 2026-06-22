// ============================================================================
// MainWindow.xaml.cs - IMPROVED VERSION with debugging
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using InterviewRecorder.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace InterviewRecorder
{
    public partial class MainWindow : Window
    {
        private IRecorder _orchestrator = null!;
        private DispatcherTimer _durationTimer = null!;
        private bool _isInitialized = false;

        private string? _lastRecordedFile;
        private IWavePlayer? _player;
        private AudioFileReader? _audioReader;

        private const int MaxPeaks = 220;
        private readonly List<float> _peaks = new();
        private DateTime _lastWaveformRender = DateTime.MinValue;

        // Cap the log box so a long session can't grow it without bound. Trim in slack-sized
        // batches so we don't rebuild the text on every single append.
        private const int MaxLogLines = 500;
        private const int LogTrimSlack = 100;

        private readonly ObservableCollection<Models.ChunkInfo> _chunks = new();


        public MainWindow()
        { 
            InitializeComponent();

            ChunksGrid.ItemsSource = _chunks;

            // Disable buttons until initialization completes
            DisableAllButtons();
            
            // Initialize async components after window is loaded
            Loaded += async (s, e) => await InitializeAsync();
        }

        private void DisableAllButtons()
        {
            RecordButton.IsEnabled = false;
            PauseResumeButton.IsEnabled = false;
            PlayButton.IsEnabled = false;
        }

        private async Task InitializeAsync()
        {
            try
            {
                LogMessage("Initializing application...");
                
                // Initialize timer first
                InitializeTimer();
                LogMessage("Timer initialized");
                
                // Initialize recorder and wait for completion
                await InitializeRecorder();
                LogMessage("Recorder initialized");
                
                _isInitialized = true;
                
                // Update UI to reflect idle state
                UpdateUIState();
                
                // Check for recovery after recorder is ready
                await CheckForRecovery();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize application: {ex.Message}\n\n{ex.StackTrace}", 
                    "Initialization Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                LogMessage($"FATAL ERROR during initialization: {ex.Message}");
            }
        }

        private async Task InitializeRecorder()
        {
            _orchestrator = await RecordingOrchestrator.Create();
            _orchestrator.LogMessage += OnLogMessage;
            _orchestrator.StateChanged += OnStateChanged;
            _orchestrator.AudioLevel += OnAudioLevel;
            _orchestrator.ChunkStarted += OnChunkStarted;
            _orchestrator.ChunkClosed += OnChunkClosed;
            _orchestrator.ConversionStarted += OnConversionStarted;
            _orchestrator.ConversionCompleted += OnConversionCompleted;
            _orchestrator.RecordingMerged += OnRecordingMerged;

            PopulateDevices();
            
            // Log config file location
            LogMessage($"Configuration file: {_orchestrator.GetConfigFilePath()}");
            LogMessage("You can edit this file to change audio settings");
        }

        private void InitializeTimer()
        {
            _durationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // Update every 100ms for smoother display
            };
            _durationTimer.Tick += (s, e) => UpdateDuration();
        }

        private async Task CheckForRecovery()
        {
            var incompleteSession = await _orchestrator.CheckForIncompleteSessionAsync();
            if (incompleteSession != null)
            {
                var result = MessageBox.Show(
                    $"Found incomplete session from {incompleteSession.StartTime:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Duration: {incompleteSession.Duration:hh\\:mm\\:ss}\n\n" +
                    "Do you want to continue this session?",
                    "Recover Session",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _orchestrator.RecoverSessionAsync(incompleteSession.SessionId);
                    _orchestrator.LogUiEvent("Recovery mode");
                    PopulateExistingChunks();
                    LogMessage("Session recovered successfully!");
                    UpdateUIState();
                }
            }
        }

        private void OnLogMessage(string message)
        {
            Dispatcher.Invoke(() => LogMessage(message));
        }

        private void OnStateChanged()
        {
            // StateChanged can be raised from the orchestrator's Timer thread, so marshal the
            // whole handler (LogMessage and UpdateUIState both touch UI controls).
            Dispatcher.Invoke(() =>
            {
                LogMessage("State changed event received - updating UI");
                UpdateUIState();
            });
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogTextBox.AppendText($"[{timestamp}] {message}\n");
            TrimLog();
            LogTextBox.ScrollToEnd();
        }

        // Keep the log box bounded over long sessions: once it overshoots the cap by the slack,
        // drop the oldest lines back down to the cap.
        private void TrimLog()
        {
            if (LogTextBox.LineCount <= MaxLogLines + LogTrimSlack) return;

            var text = LogTextBox.Text;
            int linesToRemove = LogTextBox.LineCount - MaxLogLines;
            int idx = 0;
            for (int i = 0; i < linesToRemove; i++)
            {
                int nl = text.IndexOf('\n', idx);
                if (nl < 0) break;
                idx = nl + 1;
            }
            if (idx > 0) LogTextBox.Text = text.Substring(idx);
        }

        private void UpdateUIState()
        {
            if (!_isInitialized || _orchestrator == null)
            {
                LogMessage("UpdateUIState called but not initialized yet");
                return;
            }
            
            var state = _orchestrator.CurrentState;
            var isRecording = state == Models.RecordingState.Recording;
            var isPaused = state == Models.RecordingState.Paused;
            var isIdle = state == Models.RecordingState.Idle || state == Models.RecordingState.Completed;

            LogMessage($"Updating UI - State: {state}, Recording: {isRecording}, Paused: {isPaused}, Idle: {isIdle}");

            // Record / Stop is one toggle button; Pause / Resume is another.
            RecordButton.IsEnabled = true;
            if (isRecording || isPaused)
            {
                RecordButton.Content = "⏹ Stop Recording";
                RecordButton.Style = (Style)FindResource("DangerButton");
            }
            else
            {
                RecordButton.Content = "⏺ Start Recording";
                RecordButton.Style = (Style)FindResource("SuccessButton");
            }

            PauseResumeButton.IsEnabled = isRecording || isPaused;
            if (isPaused)
            {
                PauseResumeButton.Content = "▶ Resume";
                PauseResumeButton.Style = (Style)FindResource("SuccessButton");
            }
            else
            {
                PauseResumeButton.Content = "⏸ Pause";
                PauseResumeButton.Style = (Style)FindResource("WarningButton");
            }

            // Device can only change while idle.
            DeviceMenu.IsEnabled = isIdle;

            // Playback only when not recording and a finished file exists (or it's currently playing, to allow stop).
            PlayButton.IsEnabled = isIdle
                && (_player != null
                    || (!string.IsNullOrEmpty(_lastRecordedFile) && File.Exists(_lastRecordedFile)));

            // Update status text and color
            StatusText.Text = state.ToString();
            StatusText.Foreground = state switch
            {
                Models.RecordingState.Recording => new SolidColorBrush(Colors.Green),
                Models.RecordingState.Paused => new SolidColorBrush(Colors.Orange),
                Models.RecordingState.Idle => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3")),
                _ => new SolidColorBrush(Colors.Gray)
            };

            // Update session ID
            SessionIdText.Text = _orchestrator.CurrentSessionId ?? "No active session";

            // Update timer state
            if (isRecording)
            {
                _durationTimer.Start();
                LogMessage("Duration timer started");
            }
            else
            {
                _durationTimer.Stop();
                LogMessage("Duration timer stopped");
            }

            UpdateDuration();
        }

        private void UpdateDuration()
        {
            if (_orchestrator == null) return;
            DurationText.Text = _orchestrator.CurrentDuration.ToString(@"hh\:mm\:ss");            
        }

        // One button toggles between starting and stopping a recording, based on current state.
        private async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _orchestrator == null)
            {
                MessageBox.Show("Recorder is still initializing. Please wait a moment.", "Not Ready",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var state = _orchestrator.CurrentState;
            bool isActive = state == Models.RecordingState.Recording || state == Models.RecordingState.Paused;

            try
            {
                if (isActive)
                {
                    var result = MessageBox.Show(
                        "Are you sure you want to stop recording?\nThe audio file will be finalized.",
                        "Confirm Stop",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes) return;

                    _orchestrator.LogUiEvent("Stop Recording");
                    LogMessage("Stopping recording and merging chunks...");
                    var finalFile = await _orchestrator.StopRecordingAsync();
                    LogMessage($"Recording completed: {finalFile}");

                    _lastRecordedFile = finalFile;
                    UpdateUIState();

                    MessageBox.Show($"Recording saved successfully!\n\nFile: {finalFile}",
                        "Recording Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    _orchestrator.LogUiEvent("Start Recording");
                    StopPlayback(); // don't keep playing the previous file while recording a new one
                    ClearWaveform();
                    _chunks.Clear();
                    LogMessage("Starting recording...");
                    await _orchestrator.StartRecordingAsync();
                    LogMessage("Recording started successfully");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Recording action failed: {ex.Message}\n\n{ex.StackTrace}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"ERROR: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // One button toggles between pausing and resuming, based on current state.
        private async void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_orchestrator == null) return;

            try
            {
                if (_orchestrator.CurrentState == Models.RecordingState.Recording)
                {
                    _orchestrator.LogUiEvent("Pause");
                    LogMessage("Pausing recording...");
                    await _orchestrator.PauseRecordingAsync();
                }
                else if (_orchestrator.CurrentState == Models.RecordingState.Paused)
                {
                    _orchestrator.LogUiEvent("Resume");
                    LogMessage("Resuming recording...");
                    await _orchestrator.ResumeRecordingAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Pause/resume failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"ERROR: {ex.Message}");
            }
        }

        private void StatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (_orchestrator == null)
            {
                MessageBox.Show("Recorder is still initializing.", "Not Ready", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            _orchestrator.LogUiEvent("Session Details");
            var details = _orchestrator.GetStatusDetails();
            MessageBox.Show(details, "Session Details",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PopulateDevices()
        {
            DeviceMenu.Items.Clear();

            int current = _orchestrator?.CurrentInputDeviceId ?? 0;
            int count = WaveInEvent.DeviceCount;

            for (int i = 0; i < count; i++)
            {
                var item = new MenuItem
                {
                    Header = $"{i}: {WaveInEvent.GetCapabilities(i).ProductName}",
                    IsCheckable = true,
                    IsChecked = i == current,
                    Tag = i
                };
                item.Click += DeviceItem_Click;
                DeviceMenu.Items.Add(item);
            }

            if (count == 0)
                DeviceMenu.Items.Add(new MenuItem { Header = "(no input devices)", IsEnabled = false });

            LogMessage($"Found {count} audio input device(s)");
        }

        private async void DeviceItem_Click(object sender, RoutedEventArgs e)
        {
            if (_orchestrator == null || sender is not MenuItem mi || mi.Tag is not int id) return;

            // The Device menu is disabled unless idle, so this only fires when changing is allowed.
            _orchestrator.LogUiEvent($"Select device: {mi.Header}");
            SyncDeviceChecks(id);
            await _orchestrator.SetInputDeviceAsync(id);
            LogMessage($"Input device selected: {mi.Header}");
        }

        // Keeps exactly one device item ticked.
        private void SyncDeviceChecks(int selectedId)
        {
            foreach (var obj in DeviceMenu.Items)
                if (obj is MenuItem mi && mi.Tag is int id)
                    mi.IsChecked = id == selectedId;
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            _orchestrator?.LogUiEvent(_player != null ? "Stop Playing" : "Play Recording");

            // Toggle: if something is playing, stop it.
            if (_player != null)
            {
                StopPlayback();
                return;
            }

            if (string.IsNullOrEmpty(_lastRecordedFile) || !File.Exists(_lastRecordedFile))
            {
                MessageBox.Show("No recorded file to play yet.", "Nothing to Play",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ClearWaveform();

                _audioReader = new AudioFileReader(_lastRecordedFile);

                // Tap the stream for peaks to drive the waveform during playback.
                var metering = new MeteringSampleProvider(_audioReader, _audioReader.WaveFormat.SampleRate / 50);
                metering.StreamVolume += OnPlaybackVolume;

                _player = new WaveOutEvent();
                _player.PlaybackStopped += OnPlaybackStopped;
                _player.Init(metering);
                _player.Play();

                PlayButton.Content = "⏹ Stop Playing";
                LogMessage($"Playing: {_lastRecordedFile}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to play file: {ex.Message}", "Playback Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"ERROR during playback: {ex.Message}");
                StopPlayback();
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // PlaybackStopped fires on a background thread.
            Dispatcher.Invoke(StopPlayback);
        }

        private void OnPlaybackVolume(object? sender, StreamVolumeEventArgs e)
        {
            float peak = 0f;
            foreach (var v in e.MaxSampleValues)
                if (v > peak) peak = v;

            Dispatcher.BeginInvoke(() => AddPeak(peak));
        }

        private void StopPlayback()
        {
            if (_player != null)
            {
                _player.PlaybackStopped -= OnPlaybackStopped;
                _player.Dispose();
                _player = null;
            }

            _audioReader?.Dispose();
            _audioReader = null;

            PlayButton.Content = "▶ Play Recording";
        }

        // --- Waveform -------------------------------------------------------

        private void OnAudioLevel(float peak)
        {
            // Raised ~40x/sec from capture threads. BeginInvoke (non-blocking) so we never
            // stall the audio thread on the UI; AddPeak throttles the actual redraw.
            Dispatcher.BeginInvoke(() => AddPeak(peak));
        }

        private void AddPeak(float peak)
        {
            _peaks.Add(peak);
            if (_peaks.Count > MaxPeaks)
                _peaks.RemoveRange(0, _peaks.Count - MaxPeaks);

            // Coalesce redraws to ~30 fps; peaks still accumulate, we just don't rebuild the
            // PointCollection on every single sample.
            var now = DateTime.Now;
            if ((now - _lastWaveformRender).TotalMilliseconds < 33) return;
            _lastWaveformRender = now;
            RenderWaveform();
        }

        private void ClearWaveform()
        {
            _peaks.Clear();
            _lastWaveformRender = DateTime.MinValue;
            WaveformShape.Points = new PointCollection();
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderWaveform();

        private void RenderWaveform()
        {
            double w = WaveformCanvas.ActualWidth;
            double h = WaveformCanvas.ActualHeight;
            if (w <= 0 || h <= 0 || _peaks.Count == 0)
                return;

            double mid = h / 2;
            double dx = w / MaxPeaks;

            // Build a symmetric filled shape: top envelope left→right, then bottom envelope right→left.
            var points = new PointCollection(_peaks.Count * 2);

            for (int i = 0; i < _peaks.Count; i++)
                points.Add(new Point(i * dx, mid - _peaks[i] * mid));

            for (int i = _peaks.Count - 1; i >= 0; i--)
                points.Add(new Point(i * dx, mid + _peaks[i] * mid));

            WaveformShape.Points = points;
        }

        // --- Chunks grid ----------------------------------------------------

        private void OnChunkStarted(int number, DateTime start)
        {
            Dispatcher.BeginInvoke(() =>
                _chunks.Add(new Models.ChunkInfo { Number = number.ToString(), StartTime = start }));
        }

        private void OnChunkClosed(int number, DateTime end, TimeSpan duration, long sizeBytes)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var row = _chunks.LastOrDefault(c => c.Number == number.ToString() && c.EndTime == null);
                if (row != null)
                {
                    row.EndTime = end;
                    row.Duration = duration;
                    row.SizeBytes = sizeBytes;
                }
            });
        }

        // Fills the grid from chunk files already on disk (used after crash recovery).
        private void PopulateExistingChunks()
        {
            _chunks.Clear();

            var compressedExt = "." + _orchestrator.CompressionFormat; // e.g. ".m4a" / ".opus"
            foreach (var wav in _orchestrator.CurrentChunkFiles)
            {
                var info = new Models.ChunkInfo { Number = ParseChunkNumber(wav) ?? "?" };

                try
                {
                    var fi = new FileInfo(wav);
                    if (fi.Exists)
                    {
                        info.SizeBytes = fi.Length;
                        // Reconstruct timing from the WAV length and the file's last-write time.
                        var duration = TimeSpan.Zero;
                        try { using var reader = new WaveFileReader(wav); duration = reader.TotalTime; }
                        catch { /* corrupt/partial chunk */ }
                        info.EndTime = fi.LastWriteTime;
                        info.StartTime = fi.LastWriteTime - duration;
                        info.Duration = duration;
                    }
                }
                catch { /* ignore unreadable file metadata */ }

                if (File.Exists(Path.ChangeExtension(wav, compressedExt)))
                    info.ConversionStatus = "Done";

                _chunks.Add(info);
            }
        }

        private void OnConversionStarted(string inputPath) =>
            Dispatcher.BeginInvoke(() => SetChunkStatus(inputPath, "Processing"));

        private void OnConversionCompleted(string inputPath, bool success) =>
            Dispatcher.BeginInvoke(() => SetChunkStatus(inputPath, success ? "Done" : "Failed"));

        private void SetChunkStatus(string inputPath, string status)
        {
            var number = ParseChunkNumber(inputPath);
            if (number == null) return;

            var row = _chunks.FirstOrDefault(c => c.Number == number);
            if (row != null) row.ConversionStatus = status;
        }

        // chunk_0003.wav -> "3" (matches ChunkInfo.Number).
        private static string? ParseChunkNumber(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var parts = name.Split('_');
            return parts.Length == 2 && int.TryParse(parts[1], out var n) ? n.ToString() : null;
        }

        private void OnRecordingMerged(string fileName, string status, long sizeBytes) =>
            Dispatcher.BeginInvoke(() => AddMergedRow(fileName, status, sizeBytes));

        // Adds the merged output file as a final row once the recording is finalised.
        private void AddMergedRow(string fileName, string status, long sizeBytes)
        {
            var start = _chunks.Count > 0 ? _chunks.Min(c => c.StartTime) : DateTime.Now;
            var end = _chunks.Where(c => c.EndTime.HasValue)
                             .Select(c => c.EndTime!.Value)
                             .DefaultIfEmpty(start)
                             .Max();
            var total = TimeSpan.FromTicks(_chunks.Sum(c => c.Duration.Ticks));

            _chunks.Add(new Models.ChunkInfo
            {
                Number = fileName,
                StartTime = start,
                EndTime = end,
                Duration = total,
                SizeBytes = sizeBytes,
                ConversionStatus = status
            });
        }

        // --- Panel show/hide ------------------------------------------------

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _orchestrator?.LogUiEvent("Open Folder");

            // Last recording's session folder, else the recordings root.
            string? folder = !string.IsNullOrEmpty(_lastRecordedFile) && File.Exists(_lastRecordedFile)
                ? Path.GetDirectoryName(_lastRecordedFile)
                : _orchestrator?.RecordingsFolder;

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("No recordings folder yet.", "Open Folder",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }

        private void OpenConfig_Click(object sender, RoutedEventArgs e)
        {
            var path = _orchestrator?.GetConfigFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("Config file not found yet.", "Open Config File",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _orchestrator?.LogUiEvent("Open Config File");
            System.Diagnostics.Process.Start("notepad.exe", path);
        }

        private void PanelToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi)
                _orchestrator?.LogUiEvent($"View: {mi.Header} {(mi.IsChecked ? "shown" : "hidden")}");
            UpdatePanels();
        }

        private void UpdatePanels()
        {
            StatusPanel.Visibility = Vis(ViewStatus);
            ControlsPanel.Visibility = Vis(ViewControls);

            // Waveform, Log and Chunks share one row of star columns; collapse a hidden one's column to 0.
            bool wave = ViewWaveform.IsChecked;
            bool log = ViewLog.IsChecked;
            bool chunks = ViewChunks.IsChecked;

            WaveformPanel.Visibility = Vis(wave);
            ColWave.Width = ColWidth(wave);
            LogPanel.Visibility = Vis(log);
            ColLog.Width = ColWidth(log);
            ChunksPanel.Visibility = Vis(chunks);
            ColChunks.Width = chunks ? new GridLength(2, GridUnitType.Star) : new GridLength(0);

            // A divider only makes sense between two visible neighbours.
            WaveLogSplitter.Visibility = Vis(wave && log);
            LogChunksSplitter.Visibility = Vis(log && chunks);
        }

        private static GridLength ColWidth(bool visible) =>
            visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        private static Visibility Vis(bool visible) =>
            visible ? Visibility.Visible : Visibility.Collapsed;

        private static Visibility Vis(MenuItem mi) =>
            mi.IsChecked ? Visibility.Visible : Visibility.Collapsed;

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _orchestrator?.LogUiEvent("Clear Log");
            LogTextBox.Clear();
        }

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Both Recording and Paused sessions still need finalizing (merge + flush).
            var state = _orchestrator?.CurrentState ?? Models.RecordingState.Idle;
            bool active = state == Models.RecordingState.Recording || state == Models.RecordingState.Paused;

            if (active)
            {
                var result = MessageBox.Show(
                    "A recording is in progress. Do you want to stop and save it before closing?",
                    "Recording Active",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    await _orchestrator!.StopRecordingAsync();
                    Close(); // session is Completed now, so this pass falls through
                    return;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                // "No": fall through and close; OnClosed disposes the orchestrator (releases the device).
            }

            base.OnClosing(e);
        }
        
        protected override void OnClosed(EventArgs e)
        {
            // Stop and dispose of timer
            _durationTimer?.Stop();

            // Stop any playback and release the audio device
            StopPlayback();

            // Dispose of disposable objects
            _orchestrator?.Dispose();
            
            base.OnClosed(e);
        }
    }
}

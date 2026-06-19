// ============================================================================
// Services/ConfigurationManager.cs - Configuration File Management
// ============================================================================
namespace InterviewRecorder.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;

    /// <summary>
    /// Loads, validates, saves, and watches appsettings.json, and exposes the current
    /// <see cref="AudioConfig"/>. Raises a change event when the file is edited.
    /// </summary>
    public class ConfigurationManager : IDisposable
    {
        // Shared so load and save agree: enums as strings ("InputDevice"), tolerant of casing.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _configFilePath;
        private FileSystemWatcher? _fileWatcher;
        private AppConfiguration? _currentConfig = new(); 
        private readonly LogManager _logManager;

        public event Action<AudioConfig>? ConfigurationChanged;

        public AudioConfig CurrentAudioConfig {get {return (_currentConfig??new()).AudioSettings; } }

        private ConfigurationManager(LogManager logManager)
        {
            _logManager = logManager;
            _configFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "InterviewRecordings",
                "appsettings.json");                               
        }
        
        public static async Task<ConfigurationManager> Create(LogManager logManager)
        {   
            var instance = new ConfigurationManager(logManager);
            await instance.InitializeConfigurationAsync();
            await instance.SetupFileWatcherAsync();
            return instance;
        }

        private async Task InitializeConfigurationAsync()
        {
            if (!File.Exists(_configFilePath))
            {
                // Create default configuration
                _currentConfig = new AppConfiguration();
                await SaveConfiguration(_currentConfig);
                await _logManager.LogAsync("Created default configuration file");
            }
            else
            {
                await LoadConfigurationAsync();
            }
        }

        private async Task LoadConfigurationAsync()
        {
            try
            {
                var json = await ReadFileWithRetryAsync(_configFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions);

                if (loaded == null)
                {
                    // Empty/invalid file: keep whatever config we already had rather than wiping it.
                    await _logManager.LogAsync("Configuration file was empty; keeping previous settings");
                    return;
                }

                _currentConfig = loaded;
                await ValidateConfiguration();
                var cfg = _currentConfig.AudioSettings;
                await _logManager.LogAsync(
                    $"Configuration loaded: mode={cfg.CaptureMode}, chunk={cfg.ChunkDurationMinutes}min, " +
                    $"compression={cfg.Compression.Enabled} ({cfg.Compression.Codec} {cfg.Compression.Bitrate}k)");
            }
            catch (Exception ex)
            {
                // Don't clobber a good in-memory config on a transient read/parse failure
                // (e.g. an editor briefly locking the file mid-save).
                await _logManager.LogAsync($"Error loading configuration (keeping previous settings): {ex.Message}");
            }
        }

        // The file may be momentarily locked by the editor that just saved it; retry briefly.
        private static async Task<string> ReadFileWithRetryAsync(string path)
        {
            const int attempts = 5;
            for (int i = 0; ; i++)
            {
                try
                {
                    return await File.ReadAllTextAsync(path);
                }
                catch (IOException) when (i < attempts - 1)
                {
                    await Task.Delay(100);
                }
            }
        }

        private async Task SaveConfiguration(AppConfiguration config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, JsonOptions);
                var directory = Path.GetDirectoryName(_configFilePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    //Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath));
                    await File.WriteAllTextAsync(_configFilePath, json);
                }
            }
            catch (Exception ex)
            {
                await _logManager.LogAsync($"Error saving configuration: {ex.Message}");
            }
        }

        private async Task ValidateConfiguration()
        {
            var config = _currentConfig?.AudioSettings;
            if (config != null) 
            {
                // Validate sample rate
                if (config.SampleRate < 8000 || config.SampleRate > 192000)
                {
                    config.SampleRate = 44100;
                }

                // Validate channels
                if (config.Channels < 1 || config.Channels > 2)
                {
                    config.Channels = 1;
                }

                // Validate bits per sample
                if (config.BitsPerSample != 8 && config.BitsPerSample != 16 && config.BitsPerSample != 24 && config.BitsPerSample != 32)
                {
                    config.BitsPerSample = 16;
                }

                // Validate chunk duration
                if (config.ChunkDurationMinutes < 1 || config.ChunkDurationMinutes > 60)
                {
                    config.ChunkDurationMinutes = 1;
                }

                // Validate auto-save interval
                if (config.AutoSaveIntervalSeconds < 10 || config.AutoSaveIntervalSeconds > 300)
                {
                    config.AutoSaveIntervalSeconds = 30;
                }

                // Validate input device ID
                if (config.InputDevice.Enabled)
                {
                    var deviceCount = NAudio.Wave.WaveInEvent.DeviceCount;
                    if (config.InputDevice.DeviceId < 0 || config.InputDevice.DeviceId >= deviceCount)
                    {
                        config.InputDevice.DeviceId = 0;
                        await _logManager.LogAsync($"Invalid input device ID, reset to 0. Available devices: {deviceCount}");
                    }
                }
                

                // Validate compression settings
                var validFormats = new[] { "m4a", "opus" };
                if (!Array.Exists(validFormats, f => f.Equals(config.Compression.Format, StringComparison.OrdinalIgnoreCase)))
                {
                    config.Compression.Format = "m4a";
                }

                var validCodecs = new[] { "aac", "libopus" };
                if (!Array.Exists(validCodecs, c => c.Equals(config.Compression.Codec, StringComparison.OrdinalIgnoreCase)))
                {
                    config.Compression.Codec = "aac";
                }

                if (string.IsNullOrWhiteSpace(config.Compression.FFmpegPath))
                {
                    config.Compression.FFmpegPath = "ffmpeg";
                }
            }
        }

        private async Task SetupFileWatcherAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_configFilePath);
                var fileName = Path.GetFileName(_configFilePath);
                if (directory != null) {
                    _fileWatcher = new FileSystemWatcher(directory)
                    {
                        Filter = fileName,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                    };

                    _fileWatcher.Changed += OnConfigFileChanged;
                    _fileWatcher.EnableRaisingEvents = true;

                    await _logManager.LogAsync("Configuration file watcher initialized");
                }
            }
            catch (Exception ex)
            {
                await _logManager.LogAsync($"Error setting up file watcher: {ex.Message}");
            }
        }

        private async void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce multiple events
            System.Threading.Thread.Sleep(100);

            try
            {
                await LoadConfigurationAsync();
                await _logManager.LogAsync("Configuration file changed, reloading...");
                
                // Notify subscribers
                if (_currentConfig != null) 
                {
                    ConfigurationChanged?.Invoke(_currentConfig.AudioSettings);
                }
            }
            catch (Exception ex)
            {
                await _logManager.LogAsync($"Error reloading configuration: {ex.Message}");
            }
        }

        public string GetConfigFilePath() => _configFilePath;

        /// <summary>Sets the microphone/input device id and persists it.</summary>
        public async Task SetInputDeviceAsync(int deviceId)
        {
            _currentConfig ??= new AppConfiguration();
            _currentConfig.AudioSettings.InputDevice.DeviceId = deviceId;
            await SaveConfiguration(_currentConfig);
            await _logManager.LogAsync($"Input device set to {deviceId}");
        }

        public void Dispose()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= OnConfigFileChanged;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
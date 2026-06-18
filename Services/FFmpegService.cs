// ============================================================================
// Services/FFmpegService.cs - FFmpeg Integration - FIXED for cancellation
// ============================================================================
namespace InterviewRecorder.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using InterviewRecorder.Models;

    /// <summary>
    /// Runs a single background worker that converts queued WAV chunks to compressed audio
    /// (m4a) via the external FFmpeg process, and merges converted chunks into one file.
    /// </summary>
    public class FFmpegService
    {
        private readonly LogManager _logManager;
        private readonly ConcurrentQueue<ConversionJob> _conversionQueue;
        private CancellationTokenSource _cancellationTokenSource;
        private Task? _conversionWorker;
        private bool _isRunning;
        private volatile bool _drainAndStop;

        // Conversion progress for the chunks grid (keyed by the input WAV path).
        public event Action<string>? ConversionStarted;
        public event Action<string, bool>? ConversionCompleted;

        public FFmpegService(LogManager logManager)
        {
            _logManager = logManager;
            _conversionQueue = new ConcurrentQueue<ConversionJob>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _drainAndStop = false;
            _cancellationTokenSource = new CancellationTokenSource();
            _conversionWorker = Task.Run(() => ProcessConversionQueue(_cancellationTokenSource.Token));
            await _logManager.LogAsync("FFmpeg conversion service started");
        }

        // Drains the queue: lets every pending conversion finish before stopping.
        // (Earlier this cancelled the token, which dropped any not-yet-converted chunk.)
        public async Task Stop()
        {
            if (!_isRunning) return;

            await _logManager.LogAsync("Draining FFmpeg conversion queue...");
            _isRunning = false;
            _drainAndStop = true;

            if (_conversionWorker != null)
            {
                // Generous safety timeout: if a conversion truly hangs, hard-cancel as a fallback.
                var completed = await Task.WhenAny(_conversionWorker, Task.Delay(TimeSpan.FromMinutes(2)));

                if (completed != _conversionWorker)
                {
                    await _logManager.LogAsync("WARNING: FFmpeg drain timed out; cancelling remaining jobs");
                    try { _cancellationTokenSource.Cancel(); } catch (ObjectDisposedException) { }
                }

                try
                {
                    await _conversionWorker;
                    await _logManager.LogAsync("FFmpeg conversion queue drained");
                }
                catch (OperationCanceledException)
                {
                    await _logManager.LogAsync("FFmpeg conversion queue stopped (cancelled)");
                }
                catch (Exception ex)
                {
                    await _logManager.LogAsync($"FFmpeg service stopped with error: {ex.Message}");
                }
            }
        }

        public async Task<bool> CheckFFmpegAvailable(string ffmpegPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var process = Process.Start(startInfo);
                if (process == null)
                {
                    await _logManager.LogAsync($"FFmpeg not found or cannot be called");
                    return false;                  
                }
                using (process)
                {
                    await process.WaitForExitAsync();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                await _logManager.LogAsync($"FFmpeg not found: {ex.Message}");
                return false;
            }
        }

        /// <summary>Enqueues a WAV chunk for background conversion to the configured compressed format.</summary>
        public async Task QueueConversion(string inputWavPath, CompressionConfig config)
        {
            var outputPath = GetOutputPath(inputWavPath, config.Format);
            var job = new ConversionJob(inputWavPath, outputPath, config);

            _conversionQueue.Enqueue(job);
            await _logManager.LogAsync($"Queued conversion: {Path.GetFileName(inputWavPath)} -> {Path.GetFileName(outputPath)}");
        }

        private async Task ProcessConversionQueue(CancellationToken cancellationToken)
        {
            await _logManager.LogAsync("FFmpeg conversion queue worker started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_conversionQueue.TryDequeue(out var job))
                    {
                        await ConvertChunk(job, cancellationToken);
                    }
                    else if (_drainAndStop)
                    {
                        // Queue empty and stop requested → all pending conversions are done.
                        break;
                    }
                    else
                    {
                        try
                        {
                            await Task.Delay(100, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await _logManager.LogAsync($"Error in conversion queue: {ex.Message}");
                    // Continue processing other items
                }
            }

            await _logManager.LogAsync("FFmpeg conversion queue worker stopped gracefully");
        }

        private async Task ConvertChunk(ConversionJob job, CancellationToken cancellationToken)
        {
            try
            {
                // Check cancellation before starting
                if (cancellationToken.IsCancellationRequested)
                    return;
                    
                await _logManager.LogAsync($"Converting: {Path.GetFileName(job.InputPath)}");
                ConversionStarted?.Invoke(job.InputPath);

                var arguments = BuildFFmpegArguments(job.InputPath, job.OutputPath, job.Config);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = job.Config.FFmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    var errorOutput = string.Empty;
                    process.ErrorDataReceived += (s, e) => errorOutput += e.Data;
                    
                    process.Start();
                    process.BeginErrorReadLine();
                    
                    // Wait with cancellation support
                    using (cancellationToken.Register(() => 
                    {
                        try 
                        { 
                            if (!process.HasExited)
                            {
                                process.Kill(); 
                            }
                        } 
                        catch { /* Process may have already exited */ }
                    }))
                    {
                        try
                        {
                            await process.WaitForExitAsync(cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // Process was killed by cancellation, that's ok
                            await _logManager.LogAsync($"Conversion cancelled: {Path.GetFileName(job.InputPath)}");
                            return;
                        }
                    }

                    if (process.ExitCode == 0)
                    {
                        await _logManager.LogAsync($"Converted successfully: {Path.GetFileName(job.OutputPath)}");
                        ConversionCompleted?.Invoke(job.InputPath, true);

                        if (job.Config.DeleteWavAfterConversion && File.Exists(job.InputPath))
                        {
                            File.Delete(job.InputPath);
                            await _logManager.LogAsync($"Deleted WAV: {Path.GetFileName(job.InputPath)}");
                        }
                    }
                    else if (!cancellationToken.IsCancellationRequested)
                    {
                        await _logManager.LogAsync($"FFmpeg conversion failed (exit code {process.ExitCode}): {errorOutput}");
                        ConversionCompleted?.Invoke(job.InputPath, false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await _logManager.LogAsync($"Conversion cancelled: {Path.GetFileName(job.InputPath)}");
            }
            catch (Exception ex)
            {
                await _logManager.LogAsync($"Error converting {Path.GetFileName(job.InputPath)}: {ex.Message}");
                ConversionCompleted?.Invoke(job.InputPath, false);
            }
        }

        private string BuildFFmpegArguments(string inputPath, string outputPath, CompressionConfig config)
        {
            // -i input.wav -codec:a aac -b:a 128k -y output.m4a
            return $"-i \"{inputPath}\" -codec:a {config.Codec} -b:a {config.Bitrate}k -y \"{outputPath}\"";
        }

        /// <summary>
        /// Concatenates already-converted m4a chunks into a single file using the FFmpeg
        /// concat demuxer with stream copy (no re-encode). Inputs must share the same codec/format.
        /// </summary>
        public async Task<string> MergeToM4a(IReadOnlyList<string> inputFiles, string outputPath, CompressionConfig config)
        {
            if (inputFiles == null || inputFiles.Count == 0)
                throw new InvalidOperationException("No m4a chunks to merge.");

            // concat demuxer reads a list file; forward slashes avoid backslash-escaping issues.
            var listPath = Path.Combine(
                Path.GetDirectoryName(outputPath)!,
                $"concat_{Path.GetFileNameWithoutExtension(outputPath)}.txt");

            await File.WriteAllLinesAsync(listPath, inputFiles.Select(f => $"file '{f.Replace("\\", "/")}'"));

            try
            {
                await _logManager.LogAsync($"Merging {inputFiles.Count} m4a chunks -> {Path.GetFileName(outputPath)}");
                var arguments = $"-f concat -safe 0 -i \"{listPath}\" -c copy -y \"{outputPath}\"";

                if (await RunFFmpegAsync(config.FFmpegPath, arguments))
                    await _logManager.LogAsync($"Merged m4a file created: {Path.GetFileName(outputPath)}");
                else
                    await _logManager.LogAsync("Failed to merge m4a chunks");

                return outputPath;
            }
            finally
            {
                try { if (File.Exists(listPath)) File.Delete(listPath); } catch { /* temp cleanup */ }
            }
        }

        private async Task<bool> RunFFmpegAsync(string ffmpegPath, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var error = new StringBuilder();
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                await _logManager.LogAsync($"FFmpeg failed (exit {process.ExitCode}): {error}");

            return process.ExitCode == 0;
        }

        private string GetOutputPath(string inputWavPath, string format)
        {
            var directory = Path.GetDirectoryName(inputWavPath) ?? string.Empty;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputWavPath);
            return Path.Combine(directory, $"{fileNameWithoutExtension}.{format}");
        }

        private record ConversionJob(string InputPath, string OutputPath, CompressionConfig Config);
    }
}

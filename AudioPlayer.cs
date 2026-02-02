using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DosBoxHardDiskActivity;

public class AudioPlayer : IDisposable
{
    private readonly string _basePath;
    private readonly ILogger<AudioPlayer> _logger;
    private Process? _currentProcess;
    private readonly SemaphoreSlim _playbackLock = new(1, 1);
    private bool _isPlaying;
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public bool IsPlaying => _isPlaying;

    public AudioPlayer(ILogger<AudioPlayer> logger, string basePath = ".")
    {
        _logger = logger;
        _basePath = basePath;
    }

    public async Task<TimeSpan> GetAudioDurationAsync(string filename)
    {
        try
        {
            var fullPath = Path.Combine(_basePath, filename);
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Audio file not found: {FullPath}", fullPath);
                return TimeSpan.Zero;
            }

            // Use soxi or ffprobe to get audio duration
            var processStartInfo = new ProcessStartInfo
            {
                FileName = IsLinux ? "soxi" : "ffprobe",
                Arguments = IsLinux 
                    ? $"-D \"{fullPath}\"" 
                    : $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{fullPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                _logger.LogWarning("Failed to get audio duration");
                return TimeSpan.FromSeconds(2); // Default fallback
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();
            
            if (double.TryParse(output.Trim(), out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
            
            _logger.LogWarning("Could not parse audio duration, using default");
            return TimeSpan.FromSeconds(2); // Default fallback
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting audio duration, using default");
            return TimeSpan.FromSeconds(2); // Default fallback
        }
    }

    public async Task<TimeSpan> PlayAudioFileAsync(string filename, CancellationToken cancellationToken = default)
    {
        await _playbackLock.WaitAsync(cancellationToken);
        
        try
        {
            if (_isPlaying)
            {
                _logger.LogDebug("Audio already playing, skipping");
                return TimeSpan.Zero;
            }

            var fullPath = Path.Combine(_basePath, filename);
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Audio file not found: {FullPath}", fullPath);
                return TimeSpan.Zero;
            }

            // Get audio duration first
            var duration = await GetAudioDurationAsync(filename);

            ProcessStartInfo processStartInfo;

            if (IsWindows)
            {
                // Use PowerShell with System.Media.SoundPlayer on Windows
                processStartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"(New-Object System.Media.SoundPlayer '{fullPath}').PlaySync()\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else if (IsLinux)
            {
                // Use aplay (ALSA) on Linux
                processStartInfo = new ProcessStartInfo
                {
                    FileName = "aplay",
                    Arguments = $"-q \"{fullPath}\"", // -q for quiet mode
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }
            else
            {
                _logger.LogWarning("Unsupported platform for audio playback");
                return TimeSpan.Zero;
            }

            _currentProcess = Process.Start(processStartInfo);
            
            if (_currentProcess == null)
            {
                _logger.LogWarning("Failed to start audio playback for {Filename}", filename);
                return TimeSpan.Zero;
            }

            _isPlaying = true;
            _logger.LogInformation("Playing audio: {Filename} (Duration: {Duration}s)", filename, duration.TotalSeconds);
            
            await _currentProcess.WaitForExitAsync(cancellationToken);
            
            _isPlaying = false;
            _currentProcess = null;
            
            return duration;
        }
        catch (OperationCanceledException)
        {
            StopCurrentAudio();
            _isPlaying = false;
            return TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing audio file {Filename}", filename);
            _isPlaying = false;
            return TimeSpan.Zero;
        }
        finally
        {
            _playbackLock.Release();
        }
    }

    public void StopCurrentAudio()
    {
        if (_currentProcess != null && !_currentProcess.HasExited)
        {
            try
            {
                _currentProcess.Kill();
                _currentProcess.WaitForExit(1000);
                _logger.LogDebug("Stopped current audio playback");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping audio");
            }
            finally
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
                _isPlaying = false;
            }
        }
    }

    public void Dispose()
    {
        StopCurrentAudio();
        _playbackLock.Dispose();
    }
}

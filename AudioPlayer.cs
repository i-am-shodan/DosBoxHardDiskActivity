using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DosBoxHardDiskActivity;

public class AudioPlayer : IDisposable
{
    private readonly string _basePath;
    private readonly ILogger<AudioPlayer> _logger;
    private readonly int _volume;
    private Process? _currentProcess;
    private readonly SemaphoreSlim _playbackLock = new(1, 1);
    private bool _isPlaying;
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public bool IsPlaying => _isPlaying;

    public AudioPlayer(ILogger<AudioPlayer> logger, string basePath = ".", int volume = 40)
    {
        _logger = logger;
        _basePath = basePath;
        _volume = Math.Clamp(volume, 0, 100);
    }

    public async Task PlayAudioFileAsync(string filename, CancellationToken cancellationToken = default)
    {
        await _playbackLock.WaitAsync(cancellationToken);
        
        try
        {
            if (_isPlaying)
            {
                _logger.LogDebug("Audio already playing, skipping");
                return;
            }

            var fullPath = Path.Combine(_basePath, filename);
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Audio file not found: {FullPath}", fullPath);
                return;
            }

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
                // Use aplay (ALSA) on Linux with amixer for volume control
                // First set volume using amixer, then play the sound
                var volumePercent = $"{_volume}%";
                processStartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"amixer -q sset PCM {volumePercent} && aplay -q '{fullPath}'\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }
            else
            {
                _logger.LogWarning("Unsupported platform for audio playback");
                return;
            }

            _currentProcess = Process.Start(processStartInfo);
            
            if (_currentProcess == null)
            {
                _logger.LogWarning("Failed to start audio playback for {Filename}", filename);
                return;
            }

            _isPlaying = true;
            _logger.LogInformation("Playing audio: {Filename}", filename);
            
            await _currentProcess.WaitForExitAsync(cancellationToken);
            
            _isPlaying = false;
            _currentProcess = null;
        }
        catch (OperationCanceledException)
        {
            StopCurrentAudio();
            _isPlaying = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing audio file {Filename}", filename);
            _isPlaying = false;
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

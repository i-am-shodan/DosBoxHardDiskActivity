using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DosBoxHardDiskActivity;

public class FileSystemMonitorService : BackgroundService
{
    private readonly ILogger<FileSystemMonitorService> _logger;
    private readonly AppConfiguration _appConfig;
    private readonly SoundsConfiguration _soundsConfig;
    private readonly AudioPlayer _audioPlayer;
    private readonly GpioController _gpioController;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly SemaphoreSlim _activityLock = new(1, 1);
    private bool _isProcessingActivity;
    private readonly Queue<DateTime> _recentActivityEvents = new();
    private readonly TimeSpan _activityWindow = TimeSpan.FromSeconds(3);
    private const int BurstThreshold = 3; // 3+ events in window = sustained activity

    public FileSystemMonitorService(
        ILogger<FileSystemMonitorService> logger,
        IOptions<AppConfiguration> appConfig,
        IOptions<SoundsConfiguration> soundsConfig,
        AudioPlayer audioPlayer,
        GpioController gpioController)
    {
        _logger = logger;
        _appConfig = appConfig.Value;
        _soundsConfig = soundsConfig.Value;
        _audioPlayer = audioPlayer;
        _gpioController = gpioController;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting File System Monitor Service");

        // Expand and validate directories
        var expandedDirectories = new List<string>();
        foreach (var dir in _appConfig.Directories)
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(dir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            
            if (Directory.Exists(expandedPath))
            {
                expandedDirectories.Add(expandedPath);
                _logger.LogInformation("Monitoring directory: {Directory}", expandedPath);
            }
            else
            {
                _logger.LogWarning("Directory not found: {Directory}", expandedPath);
            }
        }

        if (expandedDirectories.Count == 0)
        {
            _logger.LogError("No valid directories to monitor. Service will exit.");
            return;
        }

        // Create file system watchers for each directory
        foreach (var directory in expandedDirectories)
        {
            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.FileName 
                                 | NotifyFilters.DirectoryName 
                                 | NotifyFilters.LastWrite 
                                 | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                // Subscribe to events
                watcher.Created += OnFileSystemActivity;
                watcher.Changed += OnFileSystemActivity;
                watcher.Deleted += OnFileSystemActivity;
                watcher.Renamed += OnFileSystemActivity;

                _watchers.Add(watcher);
                _logger.LogInformation("File system watcher created for: {Directory}", directory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create watcher for directory: {Directory}", directory);
            }
        }

        _logger.LogInformation("File System Monitor Service started successfully. Monitoring {Count} directories.", _watchers.Count);

        // Keep the service running
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("File System Monitor Service is stopping...");
        }
    }

    private async void OnFileSystemActivity(object sender, FileSystemEventArgs e)
    {
        await HandleFileSystemActivityAsync(e);
    }

    private async Task HandleFileSystemActivityAsync(FileSystemEventArgs e)
    {
        // Check if we're already processing activity
        if (!await _activityLock.WaitAsync(0))
        {
            _logger.LogDebug("Activity already being processed, ignoring event");
            return;
        }

        try
        {
            if (_isProcessingActivity)
            {
                _logger.LogDebug("Activity currently playing, ignoring new event");
                return;
            }

            _isProcessingActivity = true;
            
            _logger.LogInformation("File system activity detected: {ChangeType} - {Path}", e.ChangeType, e.FullPath);

            // Track this activity event
            var now = DateTime.UtcNow;
            _recentActivityEvents.Enqueue(now);
            
            // Remove old events outside the window
            while (_recentActivityEvents.Count > 0 && (now - _recentActivityEvents.Peek()) > _activityWindow)
            {
                _recentActivityEvents.Dequeue();
            }
            
            // Determine if this is sustained/burst activity or a single event
            var eventCount = _recentActivityEvents.Count;
            var soundFile = eventCount >= BurstThreshold ? _soundsConfig.LongActivity : _soundsConfig.ShortActivity;
            var activityType = eventCount >= BurstThreshold ? "sustained" : "short";
            
            _logger.LogInformation("Playing {ActivityType} activity sound ({EventCount} events in window)", activityType, eventCount);

            // Start fake GPIO activity
            _gpioController.StartActivity();

            // Play audio and get duration
            var duration = await _audioPlayer.PlayAudioFileAsync(soundFile);

            // Stop fake GPIO activity (this also sets GPIO low)
            _gpioController.StopActivity();

            _isProcessingActivity = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file system activity");
            _gpioController.StopActivity(); // Ensure GPIO activity is stopped on error
            _isProcessingActivity = false;
        }
        finally
        {
            _activityLock.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping File System Monitor Service");

        // Dispose all watchers
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();

        // Stop audio and turn off GPIO
        _audioPlayer.StopCurrentAudio();
        _audioPlayer.Dispose();
        _gpioController.StopActivity();

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _audioPlayer?.Dispose();
        _activityLock?.Dispose();
        base.Dispose();
    }
}

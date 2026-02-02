using System.Device.Gpio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DosBoxHardDiskActivity;

public class GpioController : IDisposable
{
    private readonly ILogger<GpioController> _logger;
    private readonly int _gpioPin;
    private readonly System.Device.Gpio.GpioController? _controller;
    private bool _isInitialized;
    private CancellationTokenSource? _activityCts;
    private Task? _activityTask;
    private readonly Random _random = new();
    private const double MinIntervalSeconds = 0.1;
    private const double MaxIntervalSeconds = 0.5;

    public GpioController(ILogger<GpioController> logger, IOptions<AppConfiguration> config)
    {
        _logger = logger;
        _gpioPin = config.Value.GpioPin;
        
        try
        {
            _controller = new System.Device.Gpio.GpioController();
            _controller.OpenPin(_gpioPin, PinMode.Output);
            _controller.Write(_gpioPin, PinValue.Low);
            _isInitialized = true;
            _logger.LogInformation("GPIO controller initialized on pin {Pin}", _gpioPin);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize GPIO controller. GPIO features will be disabled.");
            _isInitialized = false;
        }
    }

    public void StartActivity()
    {
        if (!_isInitialized || _controller == null)
            return;

        // Stop any existing activity
        StopActivity();

        _activityCts = new CancellationTokenSource();
        _activityTask = Task.Run(async () => await RunFakeActivityAsync(_activityCts.Token));
        _logger.LogInformation("Started fake GPIO activity on pin {Pin}", _gpioPin);
    }

    public void StopActivity()
    {
        if (_activityCts != null)
        {
            _activityCts.Cancel();
            
            try
            {
                _activityTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error waiting for activity task to complete");
            }
            
            _activityCts?.Dispose();
            _activityCts = null;
            _activityTask = null;
        }

        // Ensure GPIO is set low
        SetPinLow();
        _logger.LogInformation("Stopped fake GPIO activity on pin {Pin}", _gpioPin);
    }

    private async Task RunFakeActivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Turn GPIO high
                SetPinHigh();
                
                // Wait for random interval
                var intervalMs = _random.NextDouble() * (MaxIntervalSeconds - MinIntervalSeconds) + MinIntervalSeconds;
                await Task.Delay(TimeSpan.FromSeconds(intervalMs), cancellationToken);
                
                // Turn GPIO low
                SetPinLow();
                
                // Wait for random interval before next pulse
                intervalMs = _random.NextDouble() * (MaxIntervalSeconds - MinIntervalSeconds) + MinIntervalSeconds;
                await Task.Delay(TimeSpan.FromSeconds(intervalMs), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in fake GPIO activity");
        }
    }

    private void SetPinHigh()
    {
        if (_isInitialized && _controller != null)
        {
            try
            {
                _controller.Write(_gpioPin, PinValue.High);
                _logger.LogDebug("GPIO pin {Pin} set to HIGH", _gpioPin);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting GPIO pin HIGH");
            }
        }
    }

    private void SetPinLow()
    {
        if (_isInitialized && _controller != null)
        {
            try
            {
                _controller.Write(_gpioPin, PinValue.Low);
                _logger.LogDebug("GPIO pin {Pin} set to LOW", _gpioPin);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting GPIO pin LOW");
            }
        }
    }

    public void Dispose()
    {
        StopActivity();
        
        if (_isInitialized && _controller != null)
        {
            try
            {
                _controller.Write(_gpioPin, PinValue.Low);
                _controller.ClosePin(_gpioPin);
                _controller.Dispose();
                _logger.LogInformation("GPIO controller disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing GPIO controller");
            }
        }
    }
}

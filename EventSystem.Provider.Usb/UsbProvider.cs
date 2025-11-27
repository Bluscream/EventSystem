using System.Management;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Provider.Usb;

/// <summary>
/// Provider for USB device connection/disconnection events.
/// </summary>
public class UsbProvider : IProvider
{
    private readonly ILogger<UsbProvider>? _logger;
    private readonly ConfigManager? _configManager;
    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;
    private bool _isRunning;
    private UsbConfig? _config;
    private HashSet<string> _knownDevices = new();
    private readonly Dictionary<string, DateTime> _deviceEventTimes = new(); // Track when devices were last processed
    private const int EVENT_DEBOUNCE_MS = 2000; // Ignore duplicate events within 2 seconds

    public string Name => "USB";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // WMI queries don't require elevation
    public event EventHandler<IEvent>? OnEvent;

    public UsbProvider(ILogger<UsbProvider>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadProviderConfig<UsbConfig>(Name) ?? new UsbConfig();
            DetectInitialDevices();
            _logger?.LogInformation("USB provider initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize USB provider");
            throw;
        }
    }

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        try
        {
            var insertQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
            _insertWatcher = new ManagementEventWatcher(insertQuery);
            _insertWatcher.EventArrived += OnDeviceInserted;
            _insertWatcher.Start();

            var removeQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");
            _removeWatcher = new ManagementEventWatcher(removeQuery);
            _removeWatcher.EventArrived += OnDeviceRemoved;
            _removeWatcher.Start();

            _isRunning = true;
            _logger?.LogInformation("USB provider started");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start USB provider");
            throw;
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        _insertWatcher?.Stop();
        _insertWatcher?.Dispose();
        _removeWatcher?.Stop();
        _removeWatcher?.Dispose();
        _isRunning = false;

        _logger?.LogInformation("USB provider stopped");
        return Task.CompletedTask;
    }

    private void DetectInitialDevices()
    {
        var devices = GetUsbDevices();
        _knownDevices = new HashSet<string>(devices);
        _logger?.LogInformation("Detected {Count} initial USB device(s)", devices.Count);
    }

    private List<string> GetUsbDevices()
    {
        var devices = new List<string>();
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_USBControllerDevice");
            foreach (ManagementObject obj in searcher.Get())
            {
                var dependent = obj["Dependent"]?.ToString() ?? "";
                var deviceId = dependent.Split('=').LastOrDefault()?.Trim('"') ?? "";
                if (!string.IsNullOrEmpty(deviceId))
                {
                    devices.Add(deviceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to query USB devices");
        }
        return devices;
    }

    private void OnDeviceInserted(object sender, EventArrivedEventArgs e)
    {
        Task.Delay(1000).Wait(); // Wait for device to be fully registered
        var devices = GetUsbDevices();
        var now = DateTime.UtcNow;
        
        foreach (var device in devices)
        {
            if (!_knownDevices.Contains(device))
            {
                // Check debounce - ignore if we just processed this device
                if (_deviceEventTimes.TryGetValue(device, out var lastTime))
                {
                    var timeSinceLastEvent = (now - lastTime).TotalMilliseconds;
                    if (timeSinceLastEvent < EVENT_DEBOUNCE_MS)
                    {
                        _logger?.LogDebug("Ignoring duplicate device insert event for {DeviceId} (debounce)", device);
                        continue;
                    }
                }

                _knownDevices.Add(device);
                _deviceEventTimes[device] = now;
                OnUsbDeviceConnected(device);
            }
        }
    }

    private void OnDeviceRemoved(object sender, EventArrivedEventArgs e)
    {
        var devices = GetUsbDevices();
        var now = DateTime.UtcNow;
        
        foreach (var device in _knownDevices.ToList())
        {
            if (!devices.Contains(device))
            {
                // Check debounce - ignore if we just processed this device
                if (_deviceEventTimes.TryGetValue(device, out var lastTime))
                {
                    var timeSinceLastEvent = (now - lastTime).TotalMilliseconds;
                    if (timeSinceLastEvent < EVENT_DEBOUNCE_MS)
                    {
                        _logger?.LogDebug("Ignoring duplicate device remove event for {DeviceId} (debounce)", device);
                        continue;
                    }
                }

                _knownDevices.Remove(device);
                _deviceEventTimes[device] = now;
                OnUsbDeviceDisconnected(device);
            }
        }
    }

    private void OnUsbDeviceConnected(string deviceId)
    {
        var evt = new UsbDeviceEvent("OnUsbDeviceConnected", Name, deviceId);
        OnEvent?.Invoke(this, evt);
        _logger?.LogInformation("USB device connected: {DeviceId}", deviceId);
    }

    private void OnUsbDeviceDisconnected(string deviceId)
    {
        var evt = new UsbDeviceEvent("OnUsbDeviceDisconnected", Name, deviceId);
        OnEvent?.Invoke(this, evt);
        _logger?.LogInformation("USB device disconnected: {DeviceId}", deviceId);
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["IsRunning"] = _isRunning,
            ["RequiresElevation"] = RequiresElevation,
            ["KnownDevices"] = _knownDevices.ToList(),
            ["KnownDeviceCount"] = _knownDevices.Count,
            ["Watchers"] = new Dictionary<string, object>
            {
                ["InsertWatcherIsNull"] = _insertWatcher == null,
                ["RemoveWatcherIsNull"] = _removeWatcher == null
            }
        };
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _insertWatcher?.Dispose();
        _removeWatcher?.Dispose();
    }
}

public class UsbDeviceEvent : BaseEvent
{
    public UsbDeviceEvent(string eventType, string providerName, string deviceId)
        : base(eventType, providerName)
    {
        Data["DeviceId"] = deviceId;
    }
}

public class UsbConfig
{
}

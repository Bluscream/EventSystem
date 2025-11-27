using System.Management;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Provider.Screens;

/// <summary>
/// Provider for screen/monitor events.
/// </summary>
public class ScreensProvider : IProvider
{
    private readonly ILogger<ScreensProvider>? _logger;
    private readonly ConfigManager? _configManager;
    private ManagementEventWatcher? _screenWatcher;
    private bool _isRunning;
    private ScreensConfig? _config;
    private HashSet<string> _knownScreens = new();
    private List<string> _lastScreenConfiguration = new(); // Cache last screen configuration

    public string Name => "Screens";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // WMI queries don't require elevation
    public event EventHandler<IEvent>? OnEvent;

    public ScreensProvider(ILogger<ScreensProvider>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadProviderConfig<ScreensConfig>(Name) ?? new ScreensConfig();
            _logger?.LogInformation("Screens provider initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Screens provider");
            throw;
        }
    }

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        try
        {
            // Detect initial screens
            DetectInitialScreens();

            // Watch for screen configuration changes
            var query = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3");
            _screenWatcher = new ManagementEventWatcher(query);
            _screenWatcher.EventArrived += OnScreenEvent;
            _screenWatcher.Start();

            // Also poll screen configuration periodically
            _ = Task.Run(async () =>
            {
                while (_isRunning)
                {
                    await Task.Delay(5000); // Poll every 5 seconds
                    if (_isRunning)
                    {
                        CheckScreenConfiguration();
                    }
                }
            });

            _isRunning = true;
            _logger?.LogInformation("Screens provider started");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Screens provider");
            throw;
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        _screenWatcher?.Stop();
        _screenWatcher?.Dispose();
        _screenWatcher = null;
        _isRunning = false;

        _logger?.LogInformation("Screens provider stopped");
        return Task.CompletedTask;
    }

    private void DetectInitialScreens()
    {
        var screens = GetAllScreens();
        _knownScreens = new HashSet<string>(screens);
        _lastScreenConfiguration = screens.ToList(); // Cache initial configuration
        _logger?.LogInformation("Detected {Count} initial screen(s)", screens.Count);
    }

    private List<string> GetAllScreens()
    {
        var screens = new List<string>();
        try
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown";
                screens.Add(name);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to query screens");
        }
        return screens;
    }

    private void CheckScreenConfiguration()
    {
        var currentScreens = new HashSet<string>(GetAllScreens());
        var currentScreenList = currentScreens.ToList();
        currentScreenList.Sort(); // Sort for consistent comparison
        
        // Check for new screens
        foreach (var screen in currentScreens)
        {
            if (!_knownScreens.Contains(screen))
            {
                OnScreenConnected(screen);
                _knownScreens.Add(screen);
            }
        }

        // Check for disconnected screens
        foreach (var screen in _knownScreens.ToList())
        {
            if (!currentScreens.Contains(screen))
            {
                OnScreenDisconnected(screen);
                _knownScreens.Remove(screen);
            }
        }

        // Only emit configuration changed if the screen list actually changed
        var lastScreenList = _lastScreenConfiguration.ToList();
        lastScreenList.Sort();
        
        if (!currentScreenList.SequenceEqual(lastScreenList))
        {
            if (currentScreens.Count > 0)
            {
                OnScreenConfigurationChanged(currentScreenList);
            }
            _lastScreenConfiguration = currentScreenList;
        }
    }

    private void OnScreenEvent(object sender, EventArrivedEventArgs e)
    {
        // Debounce: Wait a bit for the system to stabilize after screen change
        Task.Delay(500).Wait();
        CheckScreenConfiguration();
    }

    private void OnScreenConnected(string screenName)
    {
        var evt = new ScreenEvent("OnScreenConnected", Name, screenName);
        OnEvent?.Invoke(this, evt);
        _logger?.LogInformation("Screen connected: {ScreenName}", screenName);
    }

    private void OnScreenDisconnected(string screenName)
    {
        var evt = new ScreenEvent("OnScreenDisconnected", Name, screenName);
        OnEvent?.Invoke(this, evt);
        _logger?.LogInformation("Screen disconnected: {ScreenName}", screenName);
    }

    private void OnScreenConfigurationChanged(List<string> screens)
    {
        var evt = new ScreenConfigurationChangedEvent(Name, screens);
        OnEvent?.Invoke(this, evt);
        _logger?.LogDebug("Screen configuration changed: {Count} screen(s)", screens.Count);
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["IsRunning"] = _isRunning,
            ["RequiresElevation"] = RequiresElevation,
            ["PollIntervalMs"] = _config?.PollIntervalMs ?? 5000,
            ["KnownScreens"] = _knownScreens.ToList(),
            ["KnownScreenCount"] = _knownScreens.Count,
            ["Watcher"] = new Dictionary<string, object>
            {
                ["IsNull"] = _screenWatcher == null
            }
        };
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _screenWatcher?.Dispose();
    }
}

public class ScreenEvent : BaseEvent
{
    public ScreenEvent(string eventType, string providerName, string screenName)
        : base(eventType, providerName)
    {
        Data["ScreenName"] = screenName;
    }
}

public class ScreenConfigurationChangedEvent : BaseEvent
{
    public ScreenConfigurationChangedEvent(string providerName, List<string> screens)
        : base("OnScreenConfigurationChanged", providerName)
    {
        Data["ScreenCount"] = screens.Count;
        Data["Screens"] = screens;
        Data["ScreenNames"] = string.Join(", ", screens);
    }
}

public class ScreensConfig
{
    public int PollIntervalMs { get; set; } = 5000;
}

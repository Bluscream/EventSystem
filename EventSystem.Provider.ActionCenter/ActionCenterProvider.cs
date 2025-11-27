using ActionCenterListener;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Provider.ActionCenter;

/// <summary>
/// Provider for Action Center (Toast) notifications.
/// </summary>
public class ActionCenterProvider : IProvider
{
    private readonly ILogger<ActionCenterProvider>? _logger;
    private readonly ConfigManager? _configManager;
    private ActionCenterPoller? _poller;
    private bool _isRunning;
    private ActionCenterConfig? _config;
    private readonly HashSet<long> _processedNotificationIds = new(); // Cache processed notification IDs

    public string Name => "ActionCenter";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // Reading Action Center database doesn't require elevation

    public event EventHandler<IEvent>? OnEvent;

    public ActionCenterProvider(ILogger<ActionCenterProvider>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            // Load configuration
            _config = _configManager?.LoadProviderConfig<ActionCenterConfig>(Name) ?? new ActionCenterConfig();

            _logger?.LogInformation("ActionCenter provider initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize ActionCenter provider");
            throw;
        }
    }

    public Task StartAsync()
    {
        if (_isRunning)
        {
            _logger?.LogWarning("ActionCenter provider is already running");
            return Task.CompletedTask;
        }

        try
        {
            _poller = new ActionCenterPoller(_config?.PollIntervalMs ?? 2000);
            _poller.OnNotification += OnNotification;
            _isRunning = true;

            _logger?.LogInformation("ActionCenter provider started");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start ActionCenter provider");
            throw;
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        try
        {
            _poller?.Dispose();
            _poller = null;
            _isRunning = false;

            _logger?.LogInformation("ActionCenter provider stopped");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error stopping ActionCenter provider");
            throw;
        }
    }

    private void OnNotification(ActionCenterNotification notification)
    {
        try
        {
            // Skip if payload is null (matching original ActionCenterEvents behavior)
            if (notification.Payload == null)
            {
                _logger?.LogDebug("Skipping notification with null payload");
                return;
            }

            // Check if we've already processed this notification
            if (_processedNotificationIds.Contains(notification.NotificationId))
            {
                _logger?.LogDebug("Skipping duplicate notification: {NotificationId}", notification.NotificationId);
                return;
            }

            // Mark as processed
            _processedNotificationIds.Add(notification.NotificationId);

            // Limit cache size to prevent memory issues (keep last 10000 notifications)
            if (_processedNotificationIds.Count > 10000)
            {
                var oldest = _processedNotificationIds.OrderBy(x => x).Take(5000).ToList();
                foreach (var id in oldest)
                {
                    _processedNotificationIds.Remove(id);
                }
            }

            var evt = new ActionCenterNotificationEvent(
                notification,
                Name);

            OnEvent?.Invoke(this, evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing ActionCenter notification. AppId: {AppId}, Timestamp: {Timestamp}", 
                notification?.AppId ?? "null", notification?.Timestamp);
        }
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["IsRunning"] = _isRunning,
            ["RequiresElevation"] = RequiresElevation,
            ["PollIntervalMs"] = _config?.PollIntervalMs ?? 2000,
            ["Poller"] = new Dictionary<string, object>
            {
                ["IsNull"] = _poller == null,
                ["DatabasePath"] = _poller?._dbPath ?? "N/A"
            }
        };
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _poller?.Dispose();
    }
}

/// <summary>
/// Event representing an Action Center notification.
/// </summary>
public class ActionCenterNotificationEvent : BaseEvent
{
    public ActionCenterNotificationEvent(ActionCenterNotification notification, string providerName)
        : base("OnActionCenterNotification", providerName, notification.Timestamp)
    {
        // Extract all notification fields (matching ActionCenterEvents pattern)
        var timestamp = notification.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        var appId = notification.AppId ?? "";
        var toastTitle = notification.Payload?.ToastTitle ?? "";
        var toastBody = notification.Payload?.ToastBody ?? "";
        var payloadRaw = notification.Payload?.RawXml ?? notification.PayloadRaw ?? "";
        var imageCount = notification.Payload?.Images.Count ?? 0;
        var image1 = imageCount > 0 ? notification.Payload!.Images[0] : "";

        // Store all fields matching the original ActionCenterEvents structure
        Data["Timestamp"] = timestamp;
        Data["AppId"] = appId;
        Data["Title"] = notification.Title ?? "";
        Data["Body"] = notification.Body ?? "";
        Data["ToastTitle"] = toastTitle;
        Data["ToastBody"] = toastBody;
        Data["NotificationId"] = notification.NotificationId;
        Data["HandlerId"] = notification.HandlerId;
        Data["ActivityId"] = notification.ActivityId ?? "";
        Data["Type"] = notification.Type ?? "";
        Data["Tag"] = notification.Tag ?? "";
        Data["Group"] = notification.Group ?? "";
        Data["PayloadType"] = notification.PayloadType ?? "";
        Data["PayloadRaw"] = payloadRaw;
        Data["ImageCount"] = imageCount;
        Data["Image1"] = image1;
        
        if (notification.Payload != null)
        {
            Data["ToastApp"] = notification.Payload.ToastApp ?? "";
            Data["IsSilent"] = notification.Payload.IsSilent ?? false;
            
            if (notification.Payload.Images.Count > 0)
            {
                Data["Images"] = notification.Payload.Images;
                
                // Add individual image fields for compatibility
                for (int i = 0; i < notification.Payload.Images.Count && i < 10; i++)
                {
                    Data[$"Image{i + 1}"] = notification.Payload.Images[i];
                }
            }
        }
        
        // Additional metadata
        Data["Order"] = notification.Order;
        Data["DataVersion"] = notification.DataVersion;
        Data["BootId"] = notification.BootId;
        Data["ExpiresOnReboot"] = notification.ExpiresOnReboot;
        if (notification.ExpiryTime.HasValue)
        {
            Data["ExpiryTime"] = notification.ExpiryTime.Value;
        }
    }
}

/// <summary>
/// Configuration for ActionCenter provider.
/// </summary>
public class ActionCenterConfig
{
    public int PollIntervalMs { get; set; } = 2000;
}

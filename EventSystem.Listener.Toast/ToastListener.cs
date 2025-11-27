using Microsoft.Toolkit.Uwp.Notifications;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Listener.Toast;

/// <summary>
/// Listener that shows Windows Toast notifications.
/// </summary>
public class ToastListener : IListener
{
    private readonly ILogger<ToastListener>? _logger;
    private readonly ConfigManager? _configManager;
    private ToastConfig? _config;

    public string Name => "Toast";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // Toast notifications don't require elevation

    public ToastListener(ILogger<ToastListener>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadListenerConfig<ToastConfig>(Name) ?? new ToastConfig();
            _logger?.LogInformation("Toast listener initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Toast listener");
            throw;
        }
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("Toast listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation("Toast listener stopped");
        return Task.CompletedTask;
    }

    public Task HandleEventAsync(IEvent evt)
    {
        if (!IsEnabled) return Task.CompletedTask;

        try
        {
            var title = evt.EventType;
            var message = System.Text.Json.JsonSerializer.Serialize(evt.Data);

            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error showing toast notification");
        }

        return Task.CompletedTask;
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["RequiresElevation"] = RequiresElevation
        };
    }

    public void Dispose()
    {
    }
}

public class ToastConfig
{
}

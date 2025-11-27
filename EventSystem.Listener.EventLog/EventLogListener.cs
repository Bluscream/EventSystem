using System.Diagnostics;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Listener.EventLog;

/// <summary>
/// Listener that writes events to Windows Event Log.
/// </summary>
public class EventLogListener : IListener
{
    private readonly ILogger<EventLogListener>? _logger;
    private readonly ConfigManager? _configManager;
    private EventLogListenerConfig? _config;
    private System.Diagnostics.EventLog? _eventLog;

    public string Name => "EventLog";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => true; // Creating event sources requires administrator privileges

    public EventLogListener(ILogger<EventLogListener>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadListenerConfig<EventLogListenerConfig>(Name) ?? new EventLogListenerConfig();
            if (string.IsNullOrEmpty(_config.LogName))
            {
                _config.LogName = "Application";
            }
            if (string.IsNullOrEmpty(_config.Source))
            {
                _config.Source = "EventSystem";
            }

            // Create event source if it doesn't exist
            if (!System.Diagnostics.EventLog.SourceExists(_config.Source))
            {
                System.Diagnostics.EventLog.CreateEventSource(_config.Source, _config.LogName);
            }

            _eventLog = new System.Diagnostics.EventLog(_config.LogName)
            {
                Source = _config.Source
            };

            _logger?.LogInformation("EventLog listener initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize EventLog listener");
            throw;
        }
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("EventLog listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation("EventLog listener stopped");
        return Task.CompletedTask;
    }

    public Task HandleEventAsync(IEvent evt)
    {
        if (!IsEnabled || _eventLog == null) return Task.CompletedTask;

        try
        {
            var entryType = _config!.EntryType switch
            {
                "Error" => EventLogEntryType.Error,
                "Warning" => EventLogEntryType.Warning,
                _ => EventLogEntryType.Information
            };

            var message = $"[{evt.ProviderName}] {evt.EventType}\n{System.Text.Json.JsonSerializer.Serialize(evt.Data)}";
            _eventLog.WriteEntry(message, entryType, _config.EventId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error writing event to Event Log");
        }

        return Task.CompletedTask;
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["RequiresElevation"] = RequiresElevation,
            ["LogName"] = _config?.LogName ?? "Application",
            ["Source"] = _config?.Source ?? "EventSystem",
            ["EntryType"] = _config?.EntryType ?? "Information",
            ["EventId"] = _config?.EventId ?? 1000,
            ["EventLog"] = new Dictionary<string, object>
            {
                ["IsNull"] = _eventLog == null
            }
        };
    }

    public void Dispose()
    {
        _eventLog?.Dispose();
    }
}

public class EventLogListenerConfig
{
    public string LogName { get; set; } = "Application";
    public string Source { get; set; } = "EventSystem";
    public string EntryType { get; set; } = "Information"; // Information, Warning, Error
    public int EventId { get; set; } = 1000;
}

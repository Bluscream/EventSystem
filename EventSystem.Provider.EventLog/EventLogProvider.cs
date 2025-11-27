using System.Diagnostics;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Provider.EventLog;

/// <summary>
/// Provider for Windows Event Log entries.
/// </summary>
public class EventLogProvider : IProvider
{
    private readonly ILogger<EventLogProvider>? _logger;
    private readonly ConfigManager? _configManager;
    private System.Diagnostics.EventLog? _eventLog;
    private bool _isRunning;
    private EventLogConfig? _config;
    private Dictionary<string, long> _lastEventIds = new();

    public string Name => "EventLog";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // Reading Event Log doesn't require elevation
    public event EventHandler<IEvent>? OnEvent;

    public EventLogProvider(ILogger<EventLogProvider>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadProviderConfig<EventLogConfig>(Name) ?? new EventLogConfig();
            if (string.IsNullOrEmpty(_config.LogName))
            {
                _config.LogName = "Application";
            }
            _logger?.LogInformation("EventLog provider initialized for log: {LogName}", _config.LogName);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize EventLog provider");
            throw;
        }
    }

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        try
        {
            _eventLog = new System.Diagnostics.EventLog(_config!.LogName);
            _eventLog.EntryWritten += OnEventLogEntry;
            _eventLog.EnableRaisingEvents = true;

            // Initialize last event IDs
            foreach (var entry in _eventLog.Entries.Cast<System.Diagnostics.EventLogEntry>().Take(100))
            {
                var key = $"{entry.Source}:{entry.InstanceId}";
                _lastEventIds[key] = entry.Index;
            }

            _isRunning = true;
            _logger?.LogInformation("EventLog provider started");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start EventLog provider");
            throw;
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        if (_eventLog != null)
        {
            _eventLog.EnableRaisingEvents = false;
            _eventLog.EntryWritten -= OnEventLogEntry;
            _eventLog.Dispose();
            _eventLog = null;
        }

        _isRunning = false;
        _logger?.LogInformation("EventLog provider stopped");
        return Task.CompletedTask;
    }

    private void OnEventLogEntry(object sender, System.Diagnostics.EntryWrittenEventArgs e)
    {
        try
        {
            var entry = e.Entry;

            // Filter by event IDs if configured
            if (_config!.EventIds != null && _config.EventIds.Count > 0)
            {
                if (!_config.EventIds.Contains((int)entry.InstanceId))
                {
                    return;
                }
            }

            // Filter by sources if configured
            if (_config.Sources != null && _config.Sources.Count > 0)
            {
                if (!_config.Sources.Contains(entry.Source, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            // Check if we've already seen this event
            // Use a combination of Source, InstanceId, and Index for uniqueness
            var key = $"{entry.Source}:{entry.InstanceId}:{entry.Index}";
            if (_lastEventIds.ContainsKey(key))
            {
                return; // Already processed this exact event
            }

            // Also check by Source:InstanceId to prevent processing same event type multiple times
            var typeKey = $"{entry.Source}:{entry.InstanceId}";
            if (_lastEventIds.TryGetValue(typeKey, out var lastIndex) && entry.Index <= lastIndex)
            {
                return; // Already processed a newer or same event of this type
            }

            _lastEventIds[key] = entry.Index;
            _lastEventIds[typeKey] = entry.Index;

            // Limit cache size to prevent memory issues
            if (_lastEventIds.Count > 10000)
            {
                var oldest = _lastEventIds.OrderBy(kvp => kvp.Value).Take(5000).ToList();
                foreach (var kvp in oldest)
                {
                    _lastEventIds.Remove(kvp.Key);
                }
            }

            var evt = new EventLogEntryEvent(entry, Name, _config!.LogName);
            OnEvent?.Invoke(this, evt);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing EventLog entry");
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
            ["LogName"] = _config?.LogName ?? "Application",
            ["EventIds"] = _config?.EventIds ?? new List<int>(),
            ["Sources"] = _config?.Sources ?? new List<string>(),
            ["LastEventIdsCount"] = _lastEventIds.Count,
            ["EventLog"] = new Dictionary<string, object>
            {
                ["IsNull"] = _eventLog == null
            }
        };
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _eventLog?.Dispose();
    }
}

public class EventLogEntryEvent : BaseEvent
{
    public EventLogEntryEvent(System.Diagnostics.EventLogEntry entry, string providerName, string logName)
        : base("OnEventLogEntry", providerName, entry.TimeGenerated)
    {
        Data["LogName"] = logName;
        Data["Source"] = entry.Source ?? "";
        Data["InstanceId"] = entry.InstanceId;
        Data["EventId"] = entry.InstanceId;
        Data["EntryType"] = entry.EntryType.ToString();
        Data["Message"] = entry.Message ?? "";
        Data["MachineName"] = entry.MachineName ?? "";
        Data["Category"] = entry.Category ?? "";
        Data["Index"] = entry.Index;
        Data["UserName"] = entry.UserName ?? "";
    }
}

public class EventLogConfig
{
    public string LogName { get; set; } = "Application";
    public List<int>? EventIds { get; set; }
    public List<string>? Sources { get; set; }
}

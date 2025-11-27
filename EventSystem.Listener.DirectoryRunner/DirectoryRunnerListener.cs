using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Listener.DirectoryRunner;

/// <summary>
/// Listener that executes files from event directories based on event types.
/// </summary>
public class DirectoryRunnerListener : IListener
{
    private readonly ILogger<DirectoryRunnerListener>? _logger;
    private readonly ConfigManager? _configManager;
    private DirectoryRunnerConfig? _config;
    private readonly List<DirectoryInfo> _rootDirectories = new();

    public string Name => "DirectoryRunner";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // Executing files doesn't require elevation (depends on file permissions)

    public DirectoryRunnerListener(ILogger<DirectoryRunnerListener>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            // Load configuration
            _config = _configManager?.LoadListenerConfig<DirectoryRunnerConfig>(Name) ?? new DirectoryRunnerConfig();

            // Set up root directories
            _rootDirectories.Clear();
            
            if (_config.Roots != null && _config.Roots.Count > 0)
            {
                foreach (var root in _config.Roots)
                {
                    var dir = new DirectoryInfo(root);
                    if (!dir.Exists)
                    {
                        dir.Create();
                        _logger?.LogInformation("Created directory: {Directory}", dir.FullName);
                    }
                    _rootDirectories.Add(dir);
                }
            }
            else
            {
                // Default: CommonApplicationData and UserProfile
                _rootDirectories.Add(new DirectoryInfo(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
                _rootDirectories.Add(new DirectoryInfo(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            }

            _logger?.LogInformation("DirectoryRunner listener initialized with {Count} root directories", 
                _rootDirectories.Count);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize DirectoryRunner listener");
            throw;
        }
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("DirectoryRunner listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation("DirectoryRunner listener stopped");
        return Task.CompletedTask;
    }

    public Task HandleEventAsync(IEvent evt)
    {
        if (!IsEnabled) return Task.CompletedTask;

        try
        {
            // Execute files in directories matching the event type
            ExecuteEvent(evt.EventType, evt);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling event in DirectoryRunner listener");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Execute files from event directories for the given event name.
    /// </summary>
    private void ExecuteEvent(string eventName, IEvent evt)
    {
        if (string.IsNullOrEmpty(eventName)) return;

        _logger?.LogDebug("Executing event: {EventName}", eventName);

        foreach (var root in _rootDirectories)
        {
            try
            {
                var eventsDir = new DirectoryInfo(Path.Combine(root.FullName, _config?.EventsDirectoryName ?? "Events"));
                var eventDir = new DirectoryInfo(Path.Combine(eventsDir.FullName, eventName));

                if (!eventDir.Exists)
                {
                    continue; // Directory doesn't exist, skip
                }

                var files = eventDir.GetFiles("*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    ExecuteFile(file.FullName, evt);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error executing event {EventName} in root {Root}", 
                    eventName, root.FullName);
            }
        }
    }

    /// <summary>
    /// Execute a single file with environment variables from the event.
    /// </summary>
    private void ExecuteFile(string filePath, IEvent evt)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var isBatch = filePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                          filePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            var isShortcut = filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

            var startInfo = new ProcessStartInfo
            {
                FileName = isBatch ? "cmd.exe" : filePath,
                Arguments = isBatch ? $"/c \"{filePath}\"" : "",
                UseShellExecute = isShortcut,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = !isShortcut,
                RedirectStandardError = !isShortcut
            };

            // Build environment variables from event data
            var envVars = new Dictionary<string, string>();
            var prefix = _config?.EnvironmentVariablePrefix ?? "EVENT_";

            // Add standard event properties
            envVars[$"{prefix}EVENT_TYPE"] = evt.EventType;
            envVars[$"{prefix}PROVIDER_NAME"] = evt.ProviderName;
            envVars[$"{prefix}TIMESTAMP"] = evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            envVars[$"{prefix}DATETIME"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Add all event data as environment variables
            foreach (var kvp in evt.Data)
            {
                var key = $"{prefix}{kvp.Key.ToUpperInvariant()}";
                var value = kvp.Value?.ToString() ?? "";
                envVars[key] = value;
            }

            // Set environment variables (not supported for shortcuts)
            if (!isShortcut && envVars.Count > 0)
            {
                foreach (var kvp in envVars)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                    _logger?.LogDebug("Setting environment variable {Key} = {Value}", kvp.Key, kvp.Value);
                }
            }
            else if (isShortcut && envVars.Count > 0)
            {
                _logger?.LogWarning("Environment variables are not supported for shortcuts (.lnk files): {FilePath}", 
                    filePath);
            }

            _logger?.LogInformation("Executing {FilePath} for event {EventType}", filePath, evt.EventType);

            var process = Process.Start(startInfo);
            if (process != null)
            {
                process.EnableRaisingEvents = false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to execute file: {FilePath}", filePath);
        }
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["RequiresElevation"] = RequiresElevation,
            ["EventsDirectoryName"] = _config?.EventsDirectoryName ?? "Events",
            ["EnvironmentVariablePrefix"] = _config?.EnvironmentVariablePrefix ?? "EVENT_",
            ["RootDirectories"] = _rootDirectories.Select(d => d.FullName).ToList(),
            ["RootDirectoryCount"] = _rootDirectories.Count
        };
    }

    public void Dispose()
    {
        _rootDirectories.Clear();
    }
}

/// <summary>
/// Configuration for DirectoryRunner listener.
/// </summary>
public class DirectoryRunnerConfig
{
    /// <summary>
    /// Root directories to search for event directories.
    /// </summary>
    public List<string>? Roots { get; set; }

    /// <summary>
    /// Name of the events directory (default: "Events").
    /// </summary>
    public string EventsDirectoryName { get; set; } = "Events";

    /// <summary>
    /// Prefix for environment variables (default: "EVENT_").
    /// </summary>
    public string EnvironmentVariablePrefix { get; set; } = "EVENT_";
}

using System.Text.Json;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Listener.LogFile;

/// <summary>
/// Listener that writes events to log files.
/// </summary>
public class LogFileListener : IListener
{
    private readonly ILogger<LogFileListener>? _logger;
    private readonly ConfigManager? _configManager;
    private LogFileConfig? _config;
    private StreamWriter? _fileWriter;
    private readonly object _writeLock = new();

    public string Name => "LogFile";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // File writing doesn't require elevation (depends on file permissions)

    public LogFileListener(ILogger<LogFileListener>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadListenerConfig<LogFileConfig>(Name) ?? new LogFileConfig();
            if (string.IsNullOrEmpty(_config.FilePath))
            {
                _config.FilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EventSystem", "logs", "events.log");
            }
            _logger?.LogInformation("LogFile listener initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize LogFile listener");
            throw;
        }
    }

    public Task StartAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_config!.FilePath)!);
            var fileStream = new FileStream(_config.FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _fileWriter = new StreamWriter(fileStream) { AutoFlush = true };
            _logger?.LogInformation("LogFile listener started, writing to: {FilePath}", _config.FilePath);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start LogFile listener");
            throw;
        }
    }

    public Task StopAsync()
    {
        _fileWriter?.Close();
        _fileWriter?.Dispose();
        _fileWriter = null;
        _logger?.LogInformation("LogFile listener stopped");
        return Task.CompletedTask;
    }

    public Task HandleEventAsync(IEvent evt)
    {
        if (!IsEnabled || _fileWriter == null) return Task.CompletedTask;

        lock (_writeLock)
        {
            try
            {
                string line;
                switch (_config!.Format.ToLowerInvariant())
                {
                    case "json":
                        line = JsonSerializer.Serialize(new
                        {
                            evt.EventType,
                            evt.ProviderName,
                            evt.Timestamp,
                            evt.Data
                        });
                        break;
                    case "csv":
                        line = $"{evt.Timestamp:yyyy-MM-dd HH:mm:ss},\"{evt.EventType}\",\"{evt.ProviderName}\",\"{JsonSerializer.Serialize(evt.Data)}\"";
                        break;
                    default: // text
                        line = $"[{evt.Timestamp:yyyy-MM-dd HH:mm:ss}] {evt.ProviderName}.{evt.EventType} - {JsonSerializer.Serialize(evt.Data)}";
                        break;
                }

                _fileWriter.WriteLine(line);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error writing event to log file");
            }
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
            ["FilePath"] = _config?.FilePath ?? "",
            ["Format"] = _config?.Format ?? "json",
            ["FileWriter"] = new Dictionary<string, object>
            {
                ["IsNull"] = _fileWriter == null
            }
        };
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _fileWriter?.Dispose();
    }
}

public class LogFileConfig
{
    public string FilePath { get; set; } = "";
    public string Format { get; set; } = "json"; // json, csv, text
}

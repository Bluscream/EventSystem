using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Provider.Tail;

/// <summary>
/// Provider that tails log files and matches patterns with regex.
/// </summary>
public class TailProvider : IProvider
{
    private readonly ILogger<TailProvider>? _logger;
    private readonly ConfigManager? _configManager;
    private TailConfig? _config;
    private readonly Dictionary<string, FileTailer> _tailers = new();
    private bool _isRunning;

    public string Name => "Tail";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // File reading doesn't require elevation (depends on file permissions)
    public event EventHandler<IEvent>? OnEvent;

    public TailProvider(ILogger<TailProvider>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadProviderConfig<TailConfig>(Name) ?? new TailConfig();
            _logger?.LogInformation("Tail provider initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Tail provider");
            throw;
        }
    }

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        try
        {
            if (_config?.Files == null || _config.Files.Count == 0)
            {
                _logger?.LogWarning("No files configured for Tail provider");
                return Task.CompletedTask;
            }

            foreach (var fileConfig in _config.Files)
            {
                if (!File.Exists(fileConfig.FilePath))
                {
                    _logger?.LogWarning("File does not exist: {FilePath}", fileConfig.FilePath);
                    continue;
                }

                var tailer = new FileTailer(fileConfig, _logger, this);
                _tailers[fileConfig.FilePath] = tailer;
                tailer.Start();
            }

            _isRunning = true;
            _logger?.LogInformation("Tail provider started with {Count} file(s)", _tailers.Count);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Tail provider");
            throw;
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        foreach (var tailer in _tailers.Values)
        {
            tailer.Stop();
        }
        _tailers.Clear();
        _isRunning = false;

        _logger?.LogInformation("Tail provider stopped");
        return Task.CompletedTask;
    }

    internal void OnMatch(string filePath, string pattern, Match match, string line)
    {
        var evt = new TailMatchEvent(Name, filePath, pattern, match, line);
        OnEvent?.Invoke(this, evt);
    }

    public Dictionary<string, object> GetDebug()
    {
        var files = new List<Dictionary<string, object>>();
        foreach (var kvp in _tailers)
        {
            files.Add(new Dictionary<string, object>
            {
                ["FilePath"] = kvp.Key
            });
        }

        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["IsRunning"] = _isRunning,
            ["RequiresElevation"] = RequiresElevation,
            ["Files"] = files,
            ["FileCount"] = _tailers.Count,
            ["ConfiguredFiles"] = _config?.Files?.Select(f => f.FilePath).ToList() ?? new List<string>()
        };
    }

    public void Dispose()
    {
        StopAsync().Wait();
        foreach (var tailer in _tailers.Values)
        {
            tailer.Dispose();
        }
        _tailers.Clear();
    }
}

internal class FileTailer : IDisposable
{
    private readonly FileTailConfig _config;
    private readonly ILogger? _logger;
    private readonly TailProvider _provider;
    private FileStream? _fileStream;
    private StreamReader? _reader;
    private long _lastPosition;
    private bool _running;
    private Task? _tailTask;
    private readonly List<Regex> _patterns = new();
    private readonly HashSet<string> _processedLines = new(); // Cache processed lines to prevent duplicates
    private const int MAX_CACHED_LINES = 10000; // Limit cache size

    public FileTailer(FileTailConfig config, ILogger? logger, TailProvider provider)
    {
        _config = config;
        _logger = logger;
        _provider = provider;

        foreach (var pattern in config.Patterns)
        {
            try
            {
                _patterns.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Invalid regex pattern: {Pattern}", pattern);
            }
        }
    }

    public void Start()
    {
        if (_running) return;

        _running = true;
        _lastPosition = new FileInfo(_config.FilePath).Length;
        _tailTask = Task.Run(TailLoop);
    }

    public void Stop()
    {
        _running = false;
        _tailTask?.Wait(TimeSpan.FromSeconds(5));
        _fileStream?.Dispose();
        _reader?.Dispose();
    }

    private async Task TailLoop()
    {
        while (_running)
        {
            try
            {
                var fileInfo = new FileInfo(_config.FilePath);
                if (!fileInfo.Exists)
                {
                    await Task.Delay(1000);
                    continue;
                }

                if (fileInfo.Length < _lastPosition)
                {
                    // File was truncated or recreated - clear processed lines cache
                    _lastPosition = 0;
                    _processedLines.Clear();
                }

                if (fileInfo.Length > _lastPosition)
                {
                    _fileStream = new FileStream(_config.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _fileStream.Position = _lastPosition;
                    _reader = new StreamReader(_fileStream, Encoding.UTF8, true, 4096, true);

                    string? line;
                    while ((line = await _reader.ReadLineAsync()) != null)
                    {
                        // Create a hash of the line content + position to detect duplicates
                        var lineHash = $"{_lastPosition}:{line.GetHashCode()}";
                        
                        // Skip if we've already processed this line
                        if (_processedLines.Contains(lineHash))
                        {
                            continue;
                        }

                        // Mark as processed
                        _processedLines.Add(lineHash);

                        // Limit cache size
                        if (_processedLines.Count > MAX_CACHED_LINES)
                        {
                            var oldest = _processedLines.Take(5000).ToList();
                            foreach (var hash in oldest)
                            {
                                _processedLines.Remove(hash);
                            }
                        }

                        foreach (var pattern in _patterns)
                        {
                            var match = pattern.Match(line);
                            if (match.Success)
                            {
                                _provider.OnMatch(_config.FilePath, pattern.ToString(), match, line);
                            }
                        }
                    }

                    _lastPosition = _fileStream.Position;
                    _reader.Dispose();
                    _fileStream.Dispose();
                }

                await Task.Delay(_config.PollIntervalMs);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error tailing file: {FilePath}", _config.FilePath);
                await Task.Delay(5000);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

public class TailMatchEvent : BaseEvent
{
    public TailMatchEvent(string providerName, string filePath, string pattern, Match match, string line)
        : base("OnLogFileMatch", providerName)
    {
        Data["FilePath"] = filePath;
        Data["Pattern"] = pattern;
        Data["Line"] = line;
        Data["MatchValue"] = match.Value;
        Data["MatchGroups"] = match.Groups.Values.Select(g => g.Value).ToList();
    }
}

public class TailConfig
{
    public List<FileTailConfig> Files { get; set; } = new();
}

public class FileTailConfig
{
    public string FilePath { get; set; } = "";
    public List<string> Patterns { get; set; } = new();
    public int PollIntervalMs { get; set; } = 1000;
}

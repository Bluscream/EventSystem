using System.Net.Http;
using System.Text;
using System.Text.Json;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Listener.DiscordWebhook;

/// <summary>
/// Listener that sends events to Discord webhooks.
/// </summary>
public class DiscordWebhookListener : IListener
{
    private readonly ILogger<DiscordWebhookListener>? _logger;
    private readonly ConfigManager? _configManager;
    private DiscordWebhookConfig? _config;
    private readonly HttpClient _httpClient;

    public string Name => "DiscordWebhook";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // HTTP requests don't require elevation

    public DiscordWebhookListener(ILogger<DiscordWebhookListener>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
        _httpClient = new HttpClient();
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadListenerConfig<DiscordWebhookConfig>(Name) ?? new DiscordWebhookConfig();
            _logger?.LogInformation("DiscordWebhook listener initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize DiscordWebhook listener");
            throw;
        }
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("DiscordWebhook listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation("DiscordWebhook listener stopped");
        return Task.CompletedTask;
    }

    public async Task HandleEventAsync(IEvent evt)
    {
        if (!IsEnabled || _config == null || string.IsNullOrEmpty(_config.WebhookUrl)) return;

        // Check filters
        if (_config.EventFilters != null && _config.EventFilters.Count > 0)
        {
            if (!_config.EventFilters.Contains(evt.EventType, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
        }

        try
        {
            var embed = new
            {
                title = evt.EventType,
                description = JsonSerializer.Serialize(evt.Data, new JsonSerializerOptions { WriteIndented = true }),
                color = _config.Color ?? 0x00FF00,
                timestamp = evt.Timestamp.ToString("O"),
                footer = new { text = evt.ProviderName }
            };

            var payload = new
            {
                embeds = new[] { embed }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_config.WebhookUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Discord webhook returned status {StatusCode} for event {EventType}", 
                    response.StatusCode, evt.EventType);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending event to Discord webhook");
        }
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["RequiresElevation"] = RequiresElevation,
            ["WebhookUrl"] = _config?.WebhookUrl ?? "",
            ["HasWebhookUrl"] = !string.IsNullOrEmpty(_config?.WebhookUrl),
            ["EventFilters"] = _config?.EventFilters ?? new List<string>(),
            ["EventFilterCount"] = _config?.EventFilters?.Count ?? 0,
            ["Color"] = _config?.Color ?? 0x00FF00,
            ["HttpClient"] = new Dictionary<string, object>
            {
                ["IsNull"] = _httpClient == null
            }
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public class DiscordWebhookConfig
{
    public string WebhookUrl { get; set; } = "";
    public List<string>? EventFilters { get; set; }
    public int? Color { get; set; } = 0x00FF00; // Green default
}

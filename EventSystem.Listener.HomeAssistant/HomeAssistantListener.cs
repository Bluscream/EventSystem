using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Listener.HomeAssistant;

/// <summary>
/// Listener that sends events to Home Assistant.
/// </summary>
public class HomeAssistantListener : IListener
{
    private readonly ILogger<HomeAssistantListener>? _logger;
    private readonly ConfigManager? _configManager;
    private HomeAssistantConfig? _config;
    private readonly HttpClient _httpClient;

    public string Name => "HomeAssistant";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // HTTP requests don't require elevation

    public HomeAssistantListener(ILogger<HomeAssistantListener>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
        _httpClient = new HttpClient();
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadListenerConfig<HomeAssistantConfig>(Name) ?? new HomeAssistantConfig();
            if (string.IsNullOrEmpty(_config.BaseUrl))
            {
                _config.BaseUrl = "http://homeassistant.local:8123";
            }
            _logger?.LogInformation("HomeAssistant listener initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize HomeAssistant listener");
            throw;
        }
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("HomeAssistant listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation("HomeAssistant listener stopped");
        return Task.CompletedTask;
    }

    public async Task HandleEventAsync(IEvent evt)
    {
        if (!IsEnabled || _config == null || string.IsNullOrEmpty(_config.BaseUrl) || string.IsNullOrEmpty(_config.Token)) return;

        try
        {
            var eventType = evt.EventType.ToLowerInvariant().Replace(".", "_");
            var url = $"{_config.BaseUrl.TrimEnd('/')}/api/events/{eventType}";

            var payload = new
            {
                event_type = eventType,
                time_fired = evt.Timestamp,
                origin = "EventSystem",
                data = evt.Data
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.Token);

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("HomeAssistant returned status {StatusCode} for event {EventType}", 
                    response.StatusCode, evt.EventType);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending event to HomeAssistant");
        }
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["RequiresElevation"] = RequiresElevation,
            ["BaseUrl"] = _config?.BaseUrl ?? "http://homeassistant.local:8123",
            ["HasToken"] = !string.IsNullOrEmpty(_config?.Token),
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

public class HomeAssistantConfig
{
    public string BaseUrl { get; set; } = "http://homeassistant.local:8123";
    public string Token { get; set; } = "";
}

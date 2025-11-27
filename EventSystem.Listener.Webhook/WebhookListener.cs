using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Listener.Webhook;

/// <summary>
/// Listener that sends events to generic HTTP webhooks.
/// </summary>
public class WebhookListener : IListener
{
    private readonly ILogger<WebhookListener>? _logger;
    private readonly ConfigManager? _configManager;
    private WebhookConfig? _config;
    private readonly HttpClient _httpClient;

    public string Name => "Webhook";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // HTTP requests don't require elevation

    public WebhookListener(ILogger<WebhookListener>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
        _httpClient = new HttpClient();
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadListenerConfig<WebhookConfig>(Name) ?? new WebhookConfig();
            _logger?.LogInformation("Webhook listener initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Webhook listener");
            throw;
        }
    }

    public Task StartAsync()
    {
        _logger?.LogInformation("Webhook listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger?.LogInformation("Webhook listener stopped");
        return Task.CompletedTask;
    }

    public async Task HandleEventAsync(IEvent evt)
    {
        if (!IsEnabled || _config == null || string.IsNullOrEmpty(_config.Url)) return;

        try
        {
            var payload = BuildPayload(evt);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _config.Url)
            {
                Content = content
            };

            // Add headers
            if (_config.Headers != null)
            {
                foreach (var header in _config.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Webhook returned status {StatusCode} for event {EventType}", 
                    response.StatusCode, evt.EventType);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending event to webhook");
        }
    }

    private string BuildPayload(IEvent evt)
    {
        if (!string.IsNullOrEmpty(_config!.PayloadTemplate))
        {
            // Simple template replacement
            return _config.PayloadTemplate
                .Replace("{{EventType}}", evt.EventType)
                .Replace("{{ProviderName}}", evt.ProviderName)
                .Replace("{{Timestamp}}", evt.Timestamp.ToString("O"))
                .Replace("{{Data}}", JsonSerializer.Serialize(evt.Data));
        }

        return JsonSerializer.Serialize(new
        {
            EventType = evt.EventType,
            ProviderName = evt.ProviderName,
            Timestamp = evt.Timestamp,
            Data = evt.Data
        });
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["RequiresElevation"] = RequiresElevation,
            ["Url"] = _config?.Url ?? "",
            ["HasHeaders"] = _config?.Headers != null && _config.Headers.Count > 0,
            ["HeaderCount"] = _config?.Headers?.Count ?? 0,
            ["HasPayloadTemplate"] = !string.IsNullOrEmpty(_config?.PayloadTemplate),
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

public class WebhookConfig
{
    public string Url { get; set; } = "";
    public Dictionary<string, string>? Headers { get; set; }
    public string? PayloadTemplate { get; set; }
}

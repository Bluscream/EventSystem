using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace EventSystem.Tray.IPC;

/// <summary>
/// Named pipe client for communication with the EventSystem service.
/// </summary>
public class NamedPipeClient : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _clientStream;
    private bool _disposed;

    public NamedPipeClient()
    {
        _pipeName = "EventSystem_IPC";
    }

    /// <summary>
    /// Connect to the named pipe server.
    /// </summary>
    private async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _clientStream?.Dispose();
            _clientStream = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await _clientStream.ConnectAsync(5000, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Send a request and get a response.
    /// </summary>
    public async Task<PipeResponse?> SendRequestAsync(PipeRequest request, CancellationToken cancellationToken = default)
    {
        if (!await ConnectAsync(cancellationToken))
        {
            return new PipeResponse { Success = false, Error = "Failed to connect to service" };
        }

        if (_clientStream == null || !_clientStream.IsConnected)
        {
            return new PipeResponse { Success = false, Error = "Not connected to service" };
        }

        try
        {
            // Send request
            var requestJson = JsonSerializer.Serialize(request);
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            await _clientStream.WriteAsync(requestBytes, 0, requestBytes.Length, cancellationToken);
            await _clientStream.FlushAsync(cancellationToken);

            // Read response
            var buffer = new byte[4096];
            var bytesRead = await _clientStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead == 0)
            {
                return new PipeResponse { Success = false, Error = "No response from service" };
            }

            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return JsonSerializer.Deserialize<PipeResponse>(responseJson);
        }
        catch (Exception ex)
        {
            return new PipeResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get the status of providers and listeners.
    /// </summary>
    public async Task<ServiceStatus?> GetStatusAsync()
    {
        var response = await SendRequestAsync(new PipeRequest { Command = "getstatus" });
        if (response?.Success == true && response.Data != null)
        {
            var json = JsonSerializer.Serialize(response.Data);
            return JsonSerializer.Deserialize<ServiceStatus>(json);
        }
        return null;
    }

    /// <summary>
    /// Toggle a provider on or off.
    /// </summary>
    public async Task<bool> ToggleProviderAsync(string providerName, bool enabled)
    {
        var response = await SendRequestAsync(new PipeRequest
        {
            Command = "toggleprovider",
            Parameters = new Dictionary<string, string>
            {
                { "name", providerName },
                { "enabled", enabled.ToString() }
            }
        });
        return response?.Success ?? false;
    }

    /// <summary>
    /// Toggle a listener on or off.
    /// </summary>
    public async Task<bool> ToggleListenerAsync(string listenerName, bool enabled)
    {
        var response = await SendRequestAsync(new PipeRequest
        {
            Command = "togglelistener",
            Parameters = new Dictionary<string, string>
            {
                { "name", listenerName },
                { "enabled", enabled.ToString() }
            }
        });
        return response?.Success ?? false;
    }

    /// <summary>
    /// Request config reload.
    /// </summary>
    public async Task<bool> ReloadConfigAsync()
    {
        var response = await SendRequestAsync(new PipeRequest { Command = "reloadconfig" });
        return response?.Success ?? false;
    }

    /// <summary>
    /// Request debug dump. This will save the debug file and open it in notepad.
    /// </summary>
    public async Task<string?> GetDebugAsync()
    {
        var response = await SendRequestAsync(new PipeRequest { Command = "getdebug" });
        if (response?.Success == true && response.Data != null)
        {
            var json = JsonSerializer.Serialize(response.Data);
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (data?.TryGetValue("FilePath", out var filePath) == true)
            {
                return filePath?.ToString();
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clientStream?.Dispose();
    }
}

/// <summary>
/// Request structure for IPC communication.
/// </summary>
public class PipeRequest
{
    public string Command { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }
}

/// <summary>
/// Response structure for IPC communication.
/// </summary>
public class PipeResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}

/// <summary>
/// Service status information.
/// </summary>
public class ServiceStatus
{
    public List<ProviderInfo> Providers { get; set; } = new();
    public List<ListenerInfo> Listeners { get; set; } = new();
}

public class ProviderInfo
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class ListenerInfo
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

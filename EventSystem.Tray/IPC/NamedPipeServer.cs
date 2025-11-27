using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace EventSystem.Tray.IPC;

/// <summary>
/// Named pipe server in the tray app that the service can connect to for UI operations.
/// </summary>
public class TrayNamedPipeServer : IDisposable
{
    private readonly string _pipeName;
    private NamedPipeServerStream? _serverStream;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private bool _disposed;

    public TrayNamedPipeServer()
    {
        _pipeName = "EventSystem_Tray_IPC";
    }

    /// <summary>
    /// Start the named pipe server.
    /// </summary>
    public void Start()
    {
        if (_serverTask != null && !_serverTask.IsCompleted)
        {
            return; // Already running
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _serverTask = Task.Run(() => RunServerAsync(_cancellationTokenSource.Token));
    }

    /// <summary>
    /// Stop the named pipe server.
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _serverStream?.Dispose();
        _serverStream = null;

        if (_serverTask != null)
        {
            try
            {
                _serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch { }
        }
    }

    /// <summary>
    /// Main server loop.
    /// </summary>
    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _serverStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await _serverStream.WaitForConnectionAsync(cancellationToken);

                // Handle the connection
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientAsync(_serverStream, cancellationToken);
                    }
                    catch { }
                    finally
                    {
                        _serverStream?.Disconnect();
                        _serverStream?.Dispose();
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Handle a client connection.
    /// </summary>
    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        while (stream.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0) break;

                var requestJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var request = JsonSerializer.Deserialize<TrayPipeRequest>(requestJson);

                if (request == null) continue;

                var response = ProcessRequest(request);

                var responseJson = JsonSerializer.Serialize(response);
                var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch
            {
                break;
            }
        }
    }

    /// <summary>
    /// Process a request from the service.
    /// </summary>
    private TrayPipeResponse ProcessRequest(TrayPipeRequest request)
    {
        try
        {
            switch (request.Command.ToLowerInvariant())
            {
                case "showmessagebox":
                    if (request.Parameters?.TryGetValue("text", out var text) == true)
                    {
                        var caption = request.Parameters.TryGetValue("caption", out var cap) ? cap : "EventSystem";
                        var buttons = request.Parameters.TryGetValue("buttons", out var btn) ? btn : "OK";
                        var icon = request.Parameters.TryGetValue("icon", out var icn) ? icn : "Information";

                        // Marshal to UI thread using SynchronizationContext
                        DialogResult result = DialogResult.OK;
                        var syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
                        syncContext.Send(_ =>
                        {
                            result = MessageBox.Show(
                                text,
                                caption,
                                ParseMessageBoxButtons(buttons),
                                ParseMessageBoxIcon(icon));
                        }, null);

                        return new TrayPipeResponse
                        {
                            Success = true,
                            Data = new { Result = result.ToString() }
                        };
                    }
                    return new TrayPipeResponse { Success = false, Error = "Missing 'text' parameter" };

                case "sendnotification":
                    if (request.Parameters?.TryGetValue("title", out var title) == true &&
                        request.Parameters.TryGetValue("message", out var message) == true)
                    {
                        SendToastNotification(title, message);
                        return new TrayPipeResponse { Success = true };
                    }
                    return new TrayPipeResponse { Success = false, Error = "Missing 'title' or 'message' parameter" };

                case "run":
                    if (request.Parameters?.TryGetValue("app", out var appPath) == true)
                    {
                        var args = request.Parameters.TryGetValue("args", out var argsValue) ? argsValue : "";
                        var workingDirectory = request.Parameters.TryGetValue("workingdirectory", out var wd) ? wd : null;
                        var waitForExit = request.Parameters.TryGetValue("waitforexit", out var waitStr) && 
                                         bool.TryParse(waitStr, out var wait) && wait;
                        
                        var useShellExecute = true; // Default
                        if (request.Parameters.TryGetValue("useshellexecute", out var useShellStr))
                        {
                            bool.TryParse(useShellStr, out useShellExecute);
                        }
                        
                        var createNoWindow = false; // Default
                        if (request.Parameters.TryGetValue("createnowindow", out var createNoWindowStr))
                        {
                            bool.TryParse(createNoWindowStr, out createNoWindow);
                        }

                        var result = RunApplication(appPath, args, workingDirectory, waitForExit, useShellExecute, createNoWindow);
                        return new TrayPipeResponse
                        {
                            Success = result.Success,
                            Error = result.Error,
                            Data = result.ExitCode.HasValue ? new { ExitCode = result.ExitCode.Value } : null
                        };
                    }
                    return new TrayPipeResponse { Success = false, Error = "Missing 'app' parameter" };

                default:
                    return new TrayPipeResponse { Success = false, Error = $"Unknown command: {request.Command}" };
            }
        }
        catch (Exception ex)
        {
            return new TrayPipeResponse { Success = false, Error = ex.Message };
        }
    }

    private MessageBoxButtons ParseMessageBoxButtons(string buttons)
    {
        return buttons.ToUpperInvariant() switch
        {
            "OK" => MessageBoxButtons.OK,
            "OKCANCEL" => MessageBoxButtons.OKCancel,
            "YESNO" => MessageBoxButtons.YesNo,
            "YESNOCANCEL" => MessageBoxButtons.YesNoCancel,
            "RETRYCANCEL" => MessageBoxButtons.RetryCancel,
            "ABORTRETRYIGNORE" => MessageBoxButtons.AbortRetryIgnore,
            _ => MessageBoxButtons.OK
        };
    }

    private MessageBoxIcon ParseMessageBoxIcon(string icon)
    {
        return icon.ToUpperInvariant() switch
        {
            "ERROR" => MessageBoxIcon.Error,
            "WARNING" => MessageBoxIcon.Warning,
            "INFORMATION" => MessageBoxIcon.Information,
            "QUESTION" => MessageBoxIcon.Question,
            "NONE" => MessageBoxIcon.None,
            _ => MessageBoxIcon.Information
        };
    }

    private void SendToastNotification(string title, string message)
    {
        try
        {
            var notification = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                BalloonTipTitle = title,
                BalloonTipText = message,
                BalloonTipIcon = ToolTipIcon.Info,
                Visible = true
            };

            notification.ShowBalloonTip(5000);
            
            // Clean up after showing
            Task.Delay(6000).ContinueWith(_ =>
            {
                notification.Visible = false;
                notification.Dispose();
            });
        }
        catch { }
    }

    private (bool Success, string? Error, int? ExitCode) RunApplication(string appPath, string arguments, string? workingDirectory, bool waitForExit, bool useShellExecute = true, bool createNoWindow = false)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                Arguments = arguments,
                UseShellExecute = useShellExecute,
                CreateNoWindow = createNoWindow
            };

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, "Failed to start process", null);
            }

            if (waitForExit)
            {
                process.WaitForExit();
                return (true, null, process.ExitCode);
            }
            else
            {
                // Don't wait, just start it and return
                return (true, null, null);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cancellationTokenSource?.Dispose();
    }
}

public class TrayPipeRequest
{
    public string Command { get; set; } = string.Empty;
    public Dictionary<string, string>? Parameters { get; set; }
}

public class TrayPipeResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public object? Data { get; set; }
}

using System.Drawing;
using System.Diagnostics;
using EventSystem.Tray.IPC;

namespace EventSystem.Tray;

/// <summary>
/// Manages the system tray icon and context menu.
/// </summary>
public class TrayIcon : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly NamedPipeClient _pipeClient;
    private readonly EventSystem.Tray.IPC.TrayNamedPipeServer _trayPipeServer;
    private readonly string _configDirectory;
    private bool _disposed;

    public TrayIcon()
    {
        _pipeClient = new NamedPipeClient();
        _trayPipeServer = new EventSystem.Tray.IPC.TrayNamedPipeServer();
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EventSystem", "config");
    }

    /// <summary>
    /// Initialize and show the tray icon.
    /// </summary>
    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "EventSystem",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _notifyIcon.DoubleClick += (sender, e) => RefreshStatus();

        // Start the reverse IPC server so the service can request UI operations
        _trayPipeServer.Start();
    }

    /// <summary>
    /// Create the context menu for the tray icon.
    /// </summary>
    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Status section
        var statusItem = new ToolStripMenuItem("Status") { Enabled = false };
        menu.Items.Add(statusItem);

        menu.Items.Add(new ToolStripSeparator());

        // Providers section
        var providersItem = new ToolStripMenuItem("Providers");
        menu.Items.Add(providersItem);

        // Listeners section
        var listenersItem = new ToolStripMenuItem("Listeners");
        menu.Items.Add(listenersItem);

        menu.Items.Add(new ToolStripSeparator());

        // Config section
        var configItem = new ToolStripMenuItem("Configuration");
        var openMainConfigItem = new ToolStripMenuItem("Open Main Config", null, (s, e) => OpenConfigFile("EventSystem.json"));
        var openProvidersConfigItem = new ToolStripMenuItem("Open Providers Config Folder", null, (s, e) => OpenFolder(Path.Combine(_configDirectory, "providers")));
        var openListenersConfigItem = new ToolStripMenuItem("Open Listeners Config Folder", null, (s, e) => OpenFolder(Path.Combine(_configDirectory, "listeners")));
        var reloadConfigItem = new ToolStripMenuItem("Reload Config", null, async (s, e) => await ReloadConfig());
        
        configItem.DropDownItems.Add(openMainConfigItem);
        configItem.DropDownItems.Add(openProvidersConfigItem);
        configItem.DropDownItems.Add(openListenersConfigItem);
        configItem.DropDownItems.Add(new ToolStripSeparator());
        configItem.DropDownItems.Add(reloadConfigItem);
        menu.Items.Add(configItem);

        menu.Items.Add(new ToolStripSeparator());

        // Refresh item
        var refreshItem = new ToolStripMenuItem("Refresh Status", null, (s, e) => RefreshStatus());
        menu.Items.Add(refreshItem);

        // Debug item
        var debugItem = new ToolStripMenuItem("Get Debug Info", null, async (s, e) => await GetDebugInfo());
        menu.Items.Add(debugItem);

        menu.Items.Add(new ToolStripSeparator());

        // Exit item
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Application.Exit());
        menu.Items.Add(exitItem);

        // Load initial status
        RefreshStatus();

        return menu;
    }

    /// <summary>
    /// Refresh the status from the service and update the menu.
    /// </summary>
    private async void RefreshStatus()
    {
        if (_notifyIcon?.ContextMenuStrip == null) return;

        try
        {
            var status = await _pipeClient.GetStatusAsync();
            if (status == null)
            {
                UpdateStatusMenuItem("Service not available");
                return;
            }

            UpdateMenuWithStatus(status);
            var activeProviders = status.Providers.Count(p => p.IsEnabled);
            var totalProviders = status.Providers.Count;
            var activeListeners = status.Listeners.Count(l => l.IsEnabled);
            var totalListeners = status.Listeners.Count;
            UpdateStatusMenuItem($"{activeProviders}/{totalProviders} providers, {activeListeners}/{totalListeners} listeners");
        }
        catch (Exception ex)
        {
            UpdateStatusMenuItem($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Update the menu with current status.
    /// </summary>
    private void UpdateMenuWithStatus(ServiceStatus status)
    {
        if (_notifyIcon?.ContextMenuStrip == null) return;

        var menu = _notifyIcon.ContextMenuStrip;

        // Find providers and listeners menu items
        ToolStripMenuItem? providersItem = null;
        ToolStripMenuItem? listenersItem = null;

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                if (menuItem.Text == "Providers")
                    providersItem = menuItem;
                else if (menuItem.Text == "Listeners")
                    listenersItem = menuItem;
            }
        }

        // Update providers menu
        if (providersItem != null)
        {
            providersItem.DropDownItems.Clear();
            foreach (var provider in status.Providers)
            {
                var item = new ToolStripMenuItem(
                    provider.Name,
                    null,
                    async (s, e) => await ToggleProvider(provider.Name, !provider.IsEnabled));
                item.Checked = provider.IsEnabled;
                providersItem.DropDownItems.Add(item);
            }

            if (status.Providers.Count == 0)
            {
                providersItem.DropDownItems.Add(new ToolStripMenuItem("No providers loaded") { Enabled = false });
            }
        }

        // Update listeners menu
        if (listenersItem != null)
        {
            listenersItem.DropDownItems.Clear();
            foreach (var listener in status.Listeners)
            {
                var item = new ToolStripMenuItem(
                    listener.Name,
                    null,
                    async (s, e) => await ToggleListener(listener.Name, !listener.IsEnabled));
                item.Checked = listener.IsEnabled;
                listenersItem.DropDownItems.Add(item);
            }

            if (status.Listeners.Count == 0)
            {
                listenersItem.DropDownItems.Add(new ToolStripMenuItem("No listeners loaded") { Enabled = false });
            }
        }
    }

    /// <summary>
    /// Update the status menu item.
    /// </summary>
    private void UpdateStatusMenuItem(string text)
    {
        if (_notifyIcon?.ContextMenuStrip == null) return;

        var menu = _notifyIcon.ContextMenuStrip;
        var statusItem = menu.Items.Cast<ToolStripItem>()
            .FirstOrDefault(i => i.Text == "Status");

        if (statusItem != null)
        {
            statusItem.Text = text;
        }
    }

    /// <summary>
    /// Toggle a provider.
    /// </summary>
    private async Task ToggleProvider(string name, bool enabled)
    {
        var success = await _pipeClient.ToggleProviderAsync(name, enabled);
        if (success)
        {
            RefreshStatus();
        }
        else
        {
            MessageBox.Show($"Failed to toggle provider: {name}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Toggle a listener.
    /// </summary>
    private async Task ToggleListener(string name, bool enabled)
    {
        var success = await _pipeClient.ToggleListenerAsync(name, enabled);
        if (success)
        {
            RefreshStatus();
        }
        else
        {
            MessageBox.Show($"Failed to toggle listener: {name}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Open a config file in the default text editor.
    /// </summary>
    private void OpenConfigFile(string fileName)
    {
        var filePath = Path.Combine(_configDirectory, fileName);
        if (!File.Exists(filePath))
        {
            MessageBox.Show($"Config file not found: {filePath}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open config file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Open a folder in the default file explorer.
    /// </summary>
    private void OpenFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Reload configuration.
    /// </summary>
    private async Task ReloadConfig()
    {
        var success = await _pipeClient.ReloadConfigAsync();
        if (success)
        {
            MessageBox.Show("Configuration reloaded successfully", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshStatus();
        }
        else
        {
            MessageBox.Show("Failed to reload configuration", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Get debug information and open it in notepad.
    /// </summary>
    private async Task GetDebugInfo()
    {
        try
        {
            var filePath = await _pipeClient.GetDebugAsync();
            if (!string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show($"Debug information saved to:\n{filePath}\n\nThe file will open in Notepad.", 
                    "Debug Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Failed to generate debug information", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error getting debug information: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayPipeServer?.Stop();
        _trayPipeServer?.Dispose();
        _notifyIcon?.Dispose();
        _pipeClient.Dispose();
    }
}

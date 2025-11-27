using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace EventSystem.Tray.Installation;

/// <summary>
/// Manages startup entries for the EventSystem tray application.
/// </summary>
public class StartupEntryManager
{
    private readonly ILogger<StartupEntryManager>? _logger;
    private const string STARTUP_KEY_NAME = "EventSystem.Tray";
    private const string REGISTRY_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public StartupEntryManager(ILogger<StartupEntryManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Add the tray app to startup for all users.
    /// </summary>
    public bool AddStartupEntry(string trayExecutablePath)
    {
        try
        {
            // Validate executable path
            if (!File.Exists(trayExecutablePath))
            {
                _logger?.LogError("Tray executable not found: {ExecutablePath}", trayExecutablePath);
                Console.Error.WriteLine($"Error: Tray executable not found: {trayExecutablePath}");
                return false;
            }

            // Use full path
            var fullPath = Path.GetFullPath(trayExecutablePath);

            // Check if entry already exists
            if (StartupEntryExists())
            {
                _logger?.LogWarning("Startup entry '{KeyName}' already exists", STARTUP_KEY_NAME);
                Console.WriteLine($"Startup entry '{STARTUP_KEY_NAME}' already exists.");
                
                // Update it anyway to ensure it points to the correct path
                _logger?.LogInformation("Updating startup entry...");
            }

            // Open registry key for all users (requires admin)
            using var key = Registry.LocalMachine.OpenSubKey(REGISTRY_PATH, true);
            if (key == null)
            {
                _logger?.LogError("Failed to open registry key. Administrator privileges required.");
                Console.Error.WriteLine("Error: Failed to open registry. Administrator privileges required.");
                return false;
            }

            // Set the value
            key.SetValue(STARTUP_KEY_NAME, $"\"{fullPath}\"", RegistryValueKind.String);

            _logger?.LogInformation("Startup entry '{KeyName}' added successfully", STARTUP_KEY_NAME);
            Console.WriteLine($"Startup entry '{STARTUP_KEY_NAME}' added successfully for all users.");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger?.LogError("Access denied. Administrator privileges required.");
            Console.Error.WriteLine("Error: Access denied. Administrator privileges required.");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error adding startup entry");
            Console.Error.WriteLine($"Error adding startup entry: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove the tray app from startup for all users.
    /// </summary>
    public bool RemoveStartupEntry()
    {
        try
        {
            // Check if entry exists
            if (!StartupEntryExists())
            {
                _logger?.LogWarning("Startup entry '{KeyName}' does not exist", STARTUP_KEY_NAME);
                Console.WriteLine($"Startup entry '{STARTUP_KEY_NAME}' does not exist.");
                return false;
            }

            // Open registry key for all users (requires admin)
            using var key = Registry.LocalMachine.OpenSubKey(REGISTRY_PATH, true);
            if (key == null)
            {
                _logger?.LogError("Failed to open registry key. Administrator privileges required.");
                Console.Error.WriteLine("Error: Failed to open registry. Administrator privileges required.");
                return false;
            }

            // Delete the value
            key.DeleteValue(STARTUP_KEY_NAME, false);

            _logger?.LogInformation("Startup entry '{KeyName}' removed successfully", STARTUP_KEY_NAME);
            Console.WriteLine($"Startup entry '{STARTUP_KEY_NAME}' removed successfully.");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger?.LogError("Access denied. Administrator privileges required.");
            Console.Error.WriteLine("Error: Access denied. Administrator privileges required.");
            return false;
        }
        catch (ArgumentException)
        {
            // Value doesn't exist (shouldn't happen due to check, but handle gracefully)
            _logger?.LogWarning("Startup entry '{KeyName}' does not exist", STARTUP_KEY_NAME);
            Console.WriteLine($"Startup entry '{STARTUP_KEY_NAME}' does not exist.");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error removing startup entry");
            Console.Error.WriteLine($"Error removing startup entry: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if the startup entry exists.
    /// </summary>
    private bool StartupEntryExists()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(REGISTRY_PATH, false);
            if (key == null)
            {
                return false;
            }

            var value = key.GetValue(STARTUP_KEY_NAME);
            return value != null;
        }
        catch
        {
            return false;
        }
    }
}

using EventSystem.Tray;
using EventSystem.Tray.Installation;
using EventSystem.Tray.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventSystem.Tray;

static class Program
{
    private const int ERROR_INSTALLATION_FAILED = 2;

    [STAThread]
    static int Main(string[] args)
    {
        var cmdArgs = new CommandLineArgs(args);

        // Handle installation/uninstallation flags first
        if (cmdArgs.HasFlag("install"))
        {
            return HandleInstall();
        }

        if (cmdArgs.HasFlag("uninstall"))
        {
            return HandleUninstall();
        }

        // Normal tray app execution
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var trayIcon = new TrayIcon();
        trayIcon.Initialize();

        Application.Run();
        return 0;
    }

    /// <summary>
    /// Handle startup entry installation.
    /// </summary>
    private static int HandleInstall()
    {
        Console.WriteLine("EventSystem Tray Startup Entry Installation");
        Console.WriteLine("===========================================");
        Console.WriteLine();

        // Get current executable path (the launched instance)
        var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName 
            ?? Environment.ProcessPath 
            ?? AppDomain.CurrentDomain.BaseDirectory + "EventSystem.Tray.exe";

        if (!File.Exists(currentExe))
        {
            Console.Error.WriteLine($"Error: Cannot find executable: {currentExe}");
            return ERROR_INSTALLATION_FAILED;
        }

        var currentExePath = Path.GetFullPath(currentExe);
        Console.WriteLine($"Tray executable: {currentExePath}");
        Console.WriteLine();

        // Create logger for installer
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        // Add startup entry
        var startupManager = new StartupEntryManager(loggerFactory.CreateLogger<StartupEntryManager>());
        Console.WriteLine("Adding tray app to startup...");
        var startupAdded = startupManager.AddStartupEntry(currentExePath);
        Console.WriteLine();

        // Summary
        Console.WriteLine("Installation Summary:");
        Console.WriteLine($"  Startup Entry: {(startupAdded ? "✓ Added" : "✗ Failed")}");
        Console.WriteLine();

        if (startupAdded)
        {
            Console.WriteLine("Installation completed successfully!");
            Console.WriteLine("The tray app will now start automatically for all users.");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("Installation failed.");
            Console.Error.WriteLine("Please check the messages above and ensure you are running as administrator.");
            return ERROR_INSTALLATION_FAILED;
        }
    }

    /// <summary>
    /// Handle startup entry uninstallation.
    /// </summary>
    private static int HandleUninstall()
    {
        Console.WriteLine("EventSystem Tray Startup Entry Uninstallation");
        Console.WriteLine("================================================");
        Console.WriteLine();

        // Create logger for installer
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        // Remove startup entry
        var startupManager = new StartupEntryManager(loggerFactory.CreateLogger<StartupEntryManager>());
        Console.WriteLine("Removing tray app from startup...");
        var startupRemoved = startupManager.RemoveStartupEntry();
        Console.WriteLine();

        // Summary
        Console.WriteLine("Uninstallation Summary:");
        Console.WriteLine($"  Startup Entry: {(startupRemoved ? "✓ Removed" : "✗ Failed or not installed")}");
        Console.WriteLine();

        if (startupRemoved)
        {
            Console.WriteLine("Uninstallation completed successfully!");
            return 0;
        }
        else
        {
            Console.WriteLine("Uninstallation completed. Startup entry may not have been installed.");
            return 0; // Don't treat as error if entry wasn't installed
        }
    }
}

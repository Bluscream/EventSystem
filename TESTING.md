# EventSystem Testing Guide

## Build Status

✅ **Build Successful** - All projects compiled without errors.

**Version**: 1.0.0  
**Release Date**: November 27, 2025

## Setup Complete

- Plugin directories created in `%APPDATA%\EventSystem\providers` and `%APPDATA%\EventSystem\listeners`
- All provider and listener DLLs copied to plugin directories
- Configuration directories created automatically on first run

## Running for Testing

### 1. Start the Tray Application (Run first)

The tray app acts as the bridge between the service and user, and hosts the IPC server for UI operations.

**From build directory:**
```powershell
cd "P:\Visual Studio\source\repos\EventSystem\EventSystem.Tray\bin\Debug\net10.0-windows10.0.17763.0"
.\EventSystem.Tray.exe
```

**Or from deployment location:**
```powershell
cd D:\EventSystem
.\EventSystem.Tray.exe
```

You should see a system tray icon appear. Right-click it to access the menu.

### 2. Start the Core Service (Console Mode)

Run the core service in console mode for testing and debugging:

**From build directory:**
```powershell
cd "P:\Visual Studio\source\repos\EventSystem\EventSystem.Core\bin\Debug\net10.0-windows10.0.17763.0\win-x64"
.\EventSystem.Core.exe --console
```

**Or from deployment location:**
```powershell
cd D:\EventSystem
.\EventSystem.Core.exe --console
```

The service will:

- Load configuration from `%APPDATA%\EventSystem\config\EventSystem.json` (created automatically if missing)
- Load all plugins from the plugin directories
- Start all enabled providers and listeners
- Display logs in the console

Press `Ctrl+C` to stop the service.

## Testing Features

### Tray App Features

- Right-click the tray icon to see:
  - Status of all providers and listeners
  - Toggle providers/listeners on/off
  - Open config files
  - Reload configuration
  - Get debug info

### IPC Commands (Available to plugins)

- `ShowMessageBoxAsync()` - Show message boxes via tray app
- `SendNotificationAsync()` - Send toast notifications
- `RunApplicationAsync()` - Run applications under user account

### Providers Available

- ActionCenter - Windows Action Center notifications
- Screens - Screen configuration changes
- EventLog - Windows Event Log entries
- USB - USB device connections
- Disks - Disk space and mount monitoring
- Tail - Log file tailing with regex

### Listeners Available

- DirectoryRunner - Execute scripts on events
- LogFile - Write events to log files
- Webhook - Forward events to HTTP webhooks
- HomeAssistant - Send events to Home Assistant
- EventLog - Write events to Windows Event Log
- DiscordWebhook - Forward events to Discord
- Toast - Show Windows Toast notifications

## Configuration

Configuration files are located in: `%APPDATA%\EventSystem\config\`

- `EventSystem.json` - Main configuration
- `providers\{ProviderName}.json` - Provider-specific configs
- `listeners\{ListenerName}.json` - Listener-specific configs

All config files are created automatically with defaults on first run.

## Debug Information

Use the tray app's "Get Debug Info" menu item to:

- Collect debug information from all plugins
- Save it as JSON to `%TEMP%\EventSystem_debug_{timestamp}.json`
- Open it automatically in Notepad

## Notes

- The core service must be running for the tray app to control it
- Some plugins may require elevation (administrator privileges)
- Plugins requiring elevation will be skipped if the service is not elevated
- Configuration changes require a reload via the tray app menu

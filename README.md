# EventSystem

A modern, comprehensive, and cross-compatible Windows C# service that runs constantly in the background, providing a modular event system with providers (event sources) and listeners (event handlers).

## Features

- **Modular Architecture**: Load providers and listeners as plugins (DLL or raw .cs files)
- **Plugin System**: Supports both compiled DLLs and runtime-compiled .cs files using Roslyn
- **Service/Console Mode**: Can run as a Windows Service or console application
- **IPC Communication**: Named pipes for communication between service and tray app
- **Configuration**: JSON-based configuration with hot-reload support
- **Event Bus**: Central event routing system with type-safe and dynamic event support

## Architecture

### Core Components

- **EventSystem.Core**: Main service application with plugin loading, event bus, and service host
- **EventSystem.Tray**: Windows Forms tray application for user interaction

### Providers (Event Sources)

- ActionCenter: Monitors Windows Action Center notifications
- Screens: Monitors screen configuration changes
- VirtualDesktop: Monitors virtual desktop changes
- EventLog: Monitors Windows Event Log
- Tail: File tailing with regex pattern matching
- Network: Network adapter and port monitoring
- Windows: System events (shutdown, restart, updates)
- Winget: Package installation and update monitoring
- Hardware: CPU/GPU/Device monitoring
- USB: USB device connection monitoring
- Disks: Disk space and mount monitoring
- NamedPipes: Named pipe creation/deletion monitoring

### Listeners (Event Handlers)

- DiscordWebhook: Forward events to Discord webhooks
- DiscordBot: Forward events via Discord bot
- Webhook: Generic HTTP webhook POST
- DirectoryRunner: Execute files from event directories
- HomeAssistant: Send events to Home Assistant
- OpenRGB: Control RGB devices based on events
- EventLog: Write events to Windows Event Log
- LogFile: Write events to log files
- Database: Store events in databases
- Toast: Show Windows Toast notifications
- ToastNotify: Alternative toast implementation

## Installation

### Building from Source

```bash
dotnet build EventSystem.sln --configuration Release
```

The built binaries will be in:
- `EventSystem.Core/bin/Release/net10.0-windows10.0.17763.0/win-x64/`
- `EventSystem.Tray/bin/Release/net10.0-windows10.0.17763.0/`

### Installing as Windows Service

1. Build the project in Release mode
2. Copy the built files to your desired location (e.g., `D:\EventSystem\`)
3. Run the installer with administrator privileges:

```powershell
cd D:\EventSystem
.\EventSystem.Core.exe -install
```

The service will be installed as `EventSystemCore` (to avoid conflicts with Windows' built-in EventSystem service).

To uninstall:
```powershell
.\EventSystem.Core.exe -uninstall
```

### Installing Tray App Startup Entry

To automatically start the tray app on login:

```powershell
cd D:\EventSystem
.\EventSystem.Tray.exe -install
```

To remove the startup entry:
```powershell
.\EventSystem.Tray.exe -uninstall
```

### Configuration

Configuration files are stored in `%APPDATA%/EventSystem/config/`:

- `EventSystem.json`: Main configuration
- `providers/*.json`: Provider-specific configurations
- `listeners/*.json`: Listener-specific configurations

### Plugins

Place plugin DLLs or .cs files in:

- `%APPDATA%/EventSystem/providers/`: Provider plugins
- `%APPDATA%/EventSystem/listeners/`: Listener plugins

Plugins should embed their dependencies using Costura.Fody for single-DLL deployment.

## Usage

### Running as Console Application

```bash
EventSystem.Core.exe --console
```

### Running as Windows Service

After installation, start the service:

```powershell
sc start EventSystemCore
```

Check status:
```powershell
sc query EventSystemCore
```

Stop the service:
```powershell
sc stop EventSystemCore
```

### Tray Application

The tray app provides a user interface for:
- Viewing provider and listener status
- Toggling providers/listeners on/off
- Opening configuration files
- Reloading configuration
- Getting debug information

## Features

### Event Deduplication

All providers implement caching to prevent duplicate events:
- **DisksProvider**: Tracks disk full state changes
- **ScreensProvider**: Caches screen configuration
- **ActionCenterProvider**: Tracks processed notification IDs
- **UsbProvider**: Debounces device events
- **EventLogProvider**: Prevents duplicate log entries
- **TailProvider**: Caches processed log lines

### IPC Communication

The service communicates with the tray app via named pipes, allowing:
- Status queries
- Configuration reloads
- Plugin toggling
- Debug information collection
- Message boxes and notifications
- Application execution under user context

## Requirements

- Windows 10/11 (Windows 10.0.17763.0 or later)
- .NET 10.0 Runtime
- Administrator privileges (for service installation and some plugins)

## License

MIT License

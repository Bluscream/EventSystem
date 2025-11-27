i want ./EventSystem/EventSystem.Core/ to be a modern, comprehensive and cross-compatible windows c# service that runs constantly in the background, having modules like providers like

it should be able to run as a normally hidden (but optional console) app that can be also installed as a service somehow

- ./EventSystem/EventSystem.Provider.ActionCenter/ (based on ./ActionCenterEvents/)
- ./EventSystem/EventSystem.Provider.Screens/ (events for OnScreenConfigurationChanged, OnScreenConnected, OnScreenDisconnected)
- ./EventSystem/EventSystem.Provider.VirtualDesktop/ (providing events based on D:\OneDrive\AHK\Scripts\vr.ahk)
- ./EventSystem/EventSystem.Provider.EventLog/ (providing events from windows event log)
- ./EventSystem/EventSystem.Provider.Tail/ (providing events from configurable log files using regex patterns)
- ./EventSystem/EventSystem.Provider.Network/ (providing events when ports are opened/closed/connected/disconnected, network adapters are added/removed/got ip/etc.)
- ./EventSystem/EventSystem.Provider.Windows/ (providing events like OnShutdownRequested,OnRestartRequested,OnUpdateStarted,OnUpdateFinished,etc)
- ./EventSystem/EventSystem.Provider.Winget/ (providing events like OnWingetApplicationInstalled,OnWingetApplicationUpdated,OnWingetUpdateAvailable)
- ./EventSystem/EventSystem.Provider.Hardware/ (providing events like OnCpuUsageExceeded,OnDeviceFound,OnDeviceDisconnected)
- ./EventSystem/EventSystem.Provider.Usb/ (providing events like OnUsbDeviceConnected,OnUsbDeviceDisconnected)
- ./EventSystem/EventSystem.Provider.Disks/ (providing events like OnDiskFull,OnDiskConnected,OnDiskDisconnected)
- ./EventSystem/EventSystem.Provider.NamedPipes/ (providing events like OnNamedPipeCreated,OnNamedPipeRemoved)

and Listeners like

- ./EventSystem/EventSystem.Listener.DiscordWebhook/ (which can forward events to a discord webhook)
- ./EventSystem/EventSystem.Listener.DiscordBot/ (which can forward events via a discord bot)
- ./EventSystem/EventSystem.Listener.Webhook/ (forwards events to generic webhooks)
- ./EventSystem/EventSystem.Listener.DirectoryRunner/ (based on ./ActionCenterEvents/Services/EventDirectoryService.cs)
- ./EventSystem/EventSystem.Listener.HomeAssistant/ (sends POST to http://homeassistant.local:8123/api/events/<event_type> with token Bearer)
- ./EventSystem/EventSystem.Listener.OpenRGB/ (uses openrgb sdk)
- ./EventSystem/EventSystem.Listener.EventLog/ (writing events to windows event log)
- ./EventSystem/EventSystem.Listener.LogFile/ (writing events to log files)
- ./EventSystem/EventSystem.Listener.Database/ (writing events to local/remote databases like sql,mongo,etc [can be split up into multiple listeners if more useful])
- ./EventSystem/EventSystem.Listener.Toast/ (showing a toast for events)
- ./EventSystem/EventSystem.Listener.ToastNotify/ (showing a toast for events)

Have base interface or class for Providers and Listeners (depending on whats more modern)

where the modules are loaded as either compiled dlls or raw .cs files with a folder structure like
<eventsystemdir>/EventSystem.exe (service running in bg)
%APPDATA%/EventSystem/config/EventSystem.json (config file)
%APPDATA%/EventSystem/config/providers/_.json (provider config files)
%APPDATA%/EventSystem/config/listeners/_.json (listener config files)
%APPDATA%/EventSystem/providers/_.dll,_.cs
%APPDATA%/EventSystem/listeners/_.dll,_.cs

make listeners be able to either specifically handle certain events from certain providers in code with autocomplete support, and/or listen to all events with data being in a Dictionary<string,object> style (or if you know of a better way lemme know)

i want plugins (providers,listeners) to always embed their dependencies into a single dll if possible

we should also have a EventSystem.Tray app that acts as a bridge between the server (running with highest privileges) and the user so hes able to restart the server, toggle providers/listeners and open their config files from a tray icon without the need for UAC prompts

tray app shouldnt need GetConfig or UpdateConfig just open the file in a text editor and provide a "Reload Config" menu item then

Always fully implement everything, dont leave stub or legacy code anywhere.

all plugins should have a RequiresElevation bool which, when set and core isnt running elevated, will cause a warning and make that plugin not load

all plugins should have a Dictionary<string, object>GetDebug() method which the core can call to collect debug infos about the state of the plugin so the ipc client can request a debug dump of the whole app which is then saved as "%TEMP%\EventSystem*debug*<unixtime>.json" and opened in notepad

ActionCenter listener should poll the db like ActionCenterPoller in ./ActionCenterListener/ActionCenterListener.cs

ipc client should have a ShowMessageBox and SendNotification commands that the core can use if its running as service and has no way to do those itself

the ipc tray app should also have a Run command that can run a app+args under the user account the tray app is running under

the ShowMessageBox, SendNotification and Run IPC commands should be wrapped as functions so theyre available to plugins in code

can we rename ./EventSystem/EventSystem.Core/IPC/TrayPipeClient.cs to ./EventSystem/EventSystem.Core/IPC/NamedPipeClient.cs to have more consistent naming

can we have each provider and listener main .cs name be Main.cs to be more consistent?

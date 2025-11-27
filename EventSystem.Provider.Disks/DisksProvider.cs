using System.Management;
using System.IO;
using EventSystem.Core.Core;
using EventSystem.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EventSystem.Provider.Disks;

/// <summary>
/// Provider for disk events (connection, disconnection, disk full).
/// </summary>
public class DisksProvider : IProvider
{
    private readonly ILogger<DisksProvider>? _logger;
    private readonly ConfigManager? _configManager;
    private ManagementEventWatcher? _diskWatcher;
    private bool _isRunning;
    private DisksConfig? _config;
    private HashSet<string> _knownDisks = new();
    private Dictionary<string, long> _lastFreeSpace = new();
    private Dictionary<string, bool> _diskFullState = new(); // Track if disk was already reported as full

    public string Name => "Disks";
    public bool IsEnabled { get; set; } = true;
    public bool RequiresElevation => false; // WMI queries don't require elevation
    public event EventHandler<IEvent>? OnEvent;

    public DisksProvider(ILogger<DisksProvider>? logger = null, ConfigManager? configManager = null)
    {
        _logger = logger;
        _configManager = configManager;
    }

    public Task InitializeAsync()
    {
        try
        {
            _config = _configManager?.LoadProviderConfig<DisksConfig>(Name) ?? new DisksConfig();
            DetectInitialDisks();
            _logger?.LogInformation("Disks provider initialized");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Disks provider");
            throw;
        }
    }

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        try
        {
            var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent");
            _diskWatcher = new ManagementEventWatcher(query);
            _diskWatcher.EventArrived += OnDiskEvent;
            _diskWatcher.Start();

            // Poll disk space periodically
            _ = Task.Run(async () =>
            {
                while (_isRunning)
                {
                    await Task.Delay(_config!.DiskSpaceCheckIntervalMs);
                    if (_isRunning)
                    {
                        CheckDiskSpace();
                    }
                }
            });

            _isRunning = true;
            _logger?.LogInformation("Disks provider started");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Disks provider");
            throw;
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        _diskWatcher?.Stop();
        _diskWatcher?.Dispose();
        _diskWatcher = null;
        _isRunning = false;

        _logger?.LogInformation("Disks provider stopped");
        return Task.CompletedTask;
    }

    private void DetectInitialDisks()
    {
        var disks = GetAllDisks();
        _knownDisks = new HashSet<string>(disks.Select(d => d.DeviceId));
        foreach (var disk in disks)
        {
            _lastFreeSpace[disk.DeviceId] = disk.FreeSpace;
            // Initialize disk full state based on current free space
            var freeSpacePercent = disk.TotalSize > 0 
                ? (double)disk.FreeSpace / disk.TotalSize * 100 
                : 0;
            _diskFullState[disk.DeviceId] = freeSpacePercent <= _config!.DiskFullThresholdPercent;
        }
        _logger?.LogInformation("Detected {Count} initial disk(s)", disks.Count);
    }

    private List<DiskInfo> GetAllDisks()
    {
        var disks = new List<DiskInfo>();
        try
        {
            // Query logical disks
            var logicalDiskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk");
            foreach (ManagementObject logicalDisk in logicalDiskSearcher.Get())
            {
                var deviceId = logicalDisk["DeviceID"]?.ToString() ?? "";
                var freeSpace = Convert.ToInt64(logicalDisk["FreeSpace"] ?? 0);
                var totalSize = Convert.ToInt64(logicalDisk["Size"] ?? 0);
                var driveType = Convert.ToInt32(logicalDisk["DriveType"] ?? 0);
                var mediaType = logicalDisk["MediaType"]?.ToString() ?? "";
                var volumeName = logicalDisk["VolumeName"]?.ToString() ?? "";
                var fileSystem = logicalDisk["FileSystem"]?.ToString() ?? "";

                var diskInfo = new DiskInfo
                {
                    DeviceId = deviceId,
                    FreeSpace = freeSpace,
                    TotalSize = totalSize,
                    Removable = driveType == 2, // DriveType 2 = Removable
                    Inserted = true, // Disk is present if we can query it
                    Type = (DiskType)driveType,
                    PartitionLayout = GetPartitionLayout(deviceId),
                    Volumes = GetVolumesForDisk(deviceId)
                };

                disks.Add(diskInfo);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to query disks");
        }
        return disks;
    }

    private string GetPartitionLayout(string deviceId)
    {
        try
        {
            // Get the physical disk associated with this logical disk via partition
            var query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID=\"{deviceId}\"}} WHERE AssocClass=Win32_LogicalDiskToPartition";
            var searcher = new ManagementObjectSearcher(query);
            
            foreach (ManagementObject partition in searcher.Get())
            {
                // Get the disk drive from the partition
                var diskQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID=\"{partition["DeviceID"]}\"}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                var diskSearcher = new ManagementObjectSearcher(diskQuery);
                
                foreach (ManagementObject disk in diskSearcher.Get())
                {
                    // Try to get partition style from Win32_DiskDrive
                    var partitionStyle = disk["PartitionStyle"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(partitionStyle))
                    {
                        return partitionStyle; // MBR, GPT, RAW
                    }
                }
            }

            // Fallback: Query Win32_DiskDrive directly
            var allDisksSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject disk in allDisksSearcher.Get())
            {
                var partitionStyle = disk["PartitionStyle"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(partitionStyle))
                {
                    return partitionStyle;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to get partition layout for {DeviceId}", deviceId);
        }
        
        return "Unknown";
    }

    private List<string> GetVolumesForDisk(string deviceId)
    {
        var volumes = new List<string>();
        try
        {
            // Query Win32_Volume to find volumes associated with this logical disk
            var query = $"SELECT * FROM Win32_Volume WHERE DriveLetter='{deviceId}'";
            var searcher = new ManagementObjectSearcher(query);
            
            foreach (ManagementObject volume in searcher.Get())
            {
                var volumePath = volume["DeviceID"]?.ToString() ?? "";
                var volumeName = volume["Name"]?.ToString() ?? "";
                
                if (!string.IsNullOrEmpty(volumePath))
                {
                    volumes.Add(volumePath);
                }
                else if (!string.IsNullOrEmpty(volumeName) && !volumes.Contains(volumeName))
                {
                    volumes.Add(volumeName);
                }
            }

            // If no volumes found via Win32_Volume, use the device ID itself as the volume
            if (volumes.Count == 0)
            {
                volumes.Add(deviceId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to get volumes for {DeviceId}", deviceId);
            // Fallback: use device ID as volume
            volumes.Add(deviceId);
        }
        
        return volumes;
    }

    private void OnDiskEvent(object sender, EventArrivedEventArgs e)
    {
        var eventType = e.NewEvent["EventType"]?.ToString() ?? "";
        var driveName = e.NewEvent["DriveName"]?.ToString() ?? "";

        if (eventType == "2") // Inserted
        {
            Task.Delay(500).Wait(); // Wait for disk to be ready
            var disks = GetAllDisks();
            foreach (var disk in disks)
            {
            if (!_knownDisks.Contains(disk.DeviceId))
            {
                _knownDisks.Add(disk.DeviceId);
                _lastFreeSpace[disk.DeviceId] = disk.FreeSpace;
                // Initialize disk full state
                var freeSpacePercent = disk.TotalSize > 0 
                    ? (double)disk.FreeSpace / disk.TotalSize * 100 
                    : 0;
                _diskFullState[disk.DeviceId] = freeSpacePercent <= _config!.DiskFullThresholdPercent;
                OnDiskConnected(disk);
            }
            }
        }
        else if (eventType == "3") // Removed
        {
            if (_knownDisks.Remove(driveName))
            {
                _lastFreeSpace.Remove(driveName);
                _diskFullState.Remove(driveName);
                OnDiskDisconnected(driveName);
            }
        }
    }

    private void CheckDiskSpace()
    {
        var disks = GetAllDisks();
        foreach (var disk in disks)
        {
            if (!_knownDisks.Contains(disk.DeviceId))
            {
                _knownDisks.Add(disk.DeviceId);
                _lastFreeSpace[disk.DeviceId] = disk.FreeSpace;
                // Initialize disk full state
                var freeSpacePercent = disk.TotalSize > 0 
                    ? (double)disk.FreeSpace / disk.TotalSize * 100 
                    : 0;
                _diskFullState[disk.DeviceId] = freeSpacePercent <= _config!.DiskFullThresholdPercent;
                OnDiskConnected(disk);
                continue;
            }

            // Only check if free space has changed
            if (!_lastFreeSpace.TryGetValue(disk.DeviceId, out var lastFree) || lastFree != disk.FreeSpace)
            {
                var freeSpacePercent = disk.TotalSize > 0 
                    ? (double)disk.FreeSpace / disk.TotalSize * 100 
                    : 0;

                var isCurrentlyFull = freeSpacePercent <= _config!.DiskFullThresholdPercent;
                var wasPreviouslyFull = _diskFullState.GetValueOrDefault(disk.DeviceId, false);

                // Only trigger event if state changed (entered full state or exited full state)
                if (isCurrentlyFull != wasPreviouslyFull)
                {
                    if (isCurrentlyFull)
                    {
                        OnDiskFull(disk, freeSpacePercent);
                    }
                    _diskFullState[disk.DeviceId] = isCurrentlyFull;
                }

                _lastFreeSpace[disk.DeviceId] = disk.FreeSpace;
            }
        }
    }

    private void OnDiskConnected(DiskInfo disk)
    {
        var evt = new DiskEvent("OnDiskConnected", Name, disk);
        OnEvent?.Invoke(this, evt);
        _logger?.LogInformation("Disk connected: {DeviceId}", disk.DeviceId);
    }

    private void OnDiskDisconnected(string deviceId)
    {
        var evt = new DiskDisconnectedEvent(Name, deviceId);
        OnEvent?.Invoke(this, evt);
        _logger?.LogInformation("Disk disconnected: {DeviceId}", deviceId);
    }

    private void OnDiskFull(DiskInfo disk, double freeSpacePercent)
    {
        var evt = new DiskFullEvent(Name, disk, freeSpacePercent);
        OnEvent?.Invoke(this, evt);
        _logger?.LogWarning("Disk full: {DeviceId} ({FreePercent:F2}% free)", disk.DeviceId, freeSpacePercent);
    }

    public Dictionary<string, object> GetDebug()
    {
        return new Dictionary<string, object>
        {
            ["Name"] = Name,
            ["IsEnabled"] = IsEnabled,
            ["IsRunning"] = _isRunning,
            ["RequiresElevation"] = RequiresElevation,
            ["DiskSpaceCheckIntervalMs"] = _config?.DiskSpaceCheckIntervalMs ?? 60000,
            ["DiskFullThresholdPercent"] = _config?.DiskFullThresholdPercent ?? 10.0,
            ["KnownDisks"] = _knownDisks.ToList(),
            ["KnownDiskCount"] = _knownDisks.Count,
            ["LastFreeSpaceCount"] = _lastFreeSpace.Count,
            ["Watcher"] = new Dictionary<string, object>
            {
                ["IsNull"] = _diskWatcher == null
            }
        };
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _diskWatcher?.Dispose();
    }
}

public enum DiskType
{
    Unknown = 0,
    NoRootDirectory = 1,
    Removable = 2,
    Fixed = 3,
    Network = 4,
    CDRom = 5,
    RamDisk = 6
}

public class DiskInfo
{
    public string DeviceId { get; set; } = "";
    public long FreeSpace { get; set; }
    public long TotalSize { get; set; }
    public bool Removable { get; set; }
    public bool Inserted { get; set; } = true; // True if disk is currently present
    public DiskType Type { get; set; }
    public string PartitionLayout { get; set; } = "";
    public List<string> Volumes { get; set; } = new();
}

public class DiskEvent : BaseEvent
{
    public DiskEvent(string eventType, string providerName, DiskInfo disk)
        : base(eventType, providerName)
    {
        Data["DeviceId"] = disk.DeviceId;
        Data["FreeSpace"] = disk.FreeSpace;
        Data["TotalSize"] = disk.TotalSize;
        Data["FreeSpaceGB"] = disk.FreeSpace / (1024.0 * 1024.0 * 1024.0);
        Data["TotalSizeGB"] = disk.TotalSize / (1024.0 * 1024.0 * 1024.0);
        Data["Removable"] = disk.Removable;
        Data["Inserted"] = disk.Inserted;
        Data["Type"] = disk.Type.ToString();
        Data["TypeValue"] = (int)disk.Type;
        Data["PartitionLayout"] = disk.PartitionLayout;
        Data["Volumes"] = disk.Volumes;
        Data["VolumeCount"] = disk.Volumes.Count;
    }
}

public class DiskDisconnectedEvent : BaseEvent
{
    public DiskDisconnectedEvent(string providerName, string deviceId)
        : base("OnDiskDisconnected", providerName)
    {
        Data["DeviceId"] = deviceId;
    }
}

public class DiskFullEvent : BaseEvent
{
    public DiskFullEvent(string providerName, DiskInfo disk, double freeSpacePercent)
        : base("OnDiskFull", providerName)
    {
        Data["DeviceId"] = disk.DeviceId;
        Data["FreeSpace"] = disk.FreeSpace;
        Data["TotalSize"] = disk.TotalSize;
        Data["FreeSpacePercent"] = freeSpacePercent;
        Data["FreeSpaceGB"] = disk.FreeSpace / (1024.0 * 1024.0 * 1024.0);
        Data["TotalSizeGB"] = disk.TotalSize / (1024.0 * 1024.0 * 1024.0);
        Data["Removable"] = disk.Removable;
        Data["Inserted"] = disk.Inserted;
        Data["Type"] = disk.Type.ToString();
        Data["TypeValue"] = (int)disk.Type;
        Data["PartitionLayout"] = disk.PartitionLayout;
        Data["Volumes"] = disk.Volumes;
        Data["VolumeCount"] = disk.Volumes.Count;
    }
}

public class DisksConfig
{
    public int DiskSpaceCheckIntervalMs { get; set; } = 60000; // 1 minute
    public double DiskFullThresholdPercent { get; set; } = 10.0; // 10% free space
}

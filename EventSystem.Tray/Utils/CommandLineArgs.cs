namespace EventSystem.Tray.Utils;

/// <summary>
/// Simple command-line argument parser using only built-in .NET features.
/// </summary>
public class CommandLineArgs
{
    private readonly Dictionary<string, string?> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = new();

    /// <summary>
    /// Parse command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments array</param>
    public CommandLineArgs(string[] args)
    {
        Parse(args);
    }

    private void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Handle flags: --flag or -flag
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var flagName = arg.Substring(2);
                
                // Check if it's a key=value format: --loglevel=Debug
                if (flagName.Contains('='))
                {
                    var parts = flagName.Split('=', 2);
                    _flags[parts[0]] = parts.Length > 1 ? parts[1] : null;
                }
                // Check if next arg is a value: --loglevel Debug
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    _flags[flagName] = args[++i];
                }
                else
                {
                    // Boolean flag
                    _options.Add(flagName);
                }
            }
            // Handle short flags: -c or -c value
            else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
            {
                var flagName = arg.Substring(1);
                
                // Check if it's a key=value format: -loglevel=Debug
                if (flagName.Contains('='))
                {
                    var parts = flagName.Split('=', 2);
                    _flags[parts[0]] = parts.Length > 1 ? parts[1] : null;
                }
                // Check if next arg is a value: -loglevel Debug
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    _flags[flagName] = args[++i];
                }
                else
                {
                    // Boolean flag
                    _options.Add(flagName);
                }
            }
            else
            {
                // Positional argument
                _positional.Add(arg);
            }
        }
    }

    /// <summary>
    /// Check if a flag/option is present (e.g., --console, -install).
    /// </summary>
    public bool HasFlag(string flagName)
    {
        return _options.Contains(flagName) || _flags.ContainsKey(flagName);
    }

    /// <summary>
    /// Get the value of a flag (e.g., --loglevel=Debug returns "Debug").
    /// Returns null if flag is not present or has no value.
    /// </summary>
    public string? GetFlagValue(string flagName)
    {
        return _flags.TryGetValue(flagName, out var value) ? value : null;
    }

    /// <summary>
    /// Get the value of a flag with a default if not present.
    /// </summary>
    public string GetFlagValue(string flagName, string defaultValue)
    {
        return GetFlagValue(flagName) ?? defaultValue;
    }

    /// <summary>
    /// Try to get and parse a flag value as the specified type.
    /// </summary>
    public bool TryGetFlagValue<T>(string flagName, out T? value) where T : IParsable<T>
    {
        var stringValue = GetFlagValue(flagName);
        if (stringValue != null && T.TryParse(stringValue, null, out var parsed))
        {
            value = parsed;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try to get and parse a flag value as an enum.
    /// </summary>
    public bool TryGetFlagValueAsEnum<T>(string flagName, out T? value) where T : struct, Enum
    {
        var stringValue = GetFlagValue(flagName);
        if (stringValue != null && Enum.TryParse<T>(stringValue, true, out var parsed))
        {
            value = parsed;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Get positional arguments (non-flag arguments).
    /// </summary>
    public IReadOnlyList<string> Positional => _positional;

    /// <summary>
    /// Get all flags with values.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Flags => _flags;

    /// <summary>
    /// Get all boolean options (flags without values).
    /// </summary>
    public IReadOnlySet<string> Options => _options;
}

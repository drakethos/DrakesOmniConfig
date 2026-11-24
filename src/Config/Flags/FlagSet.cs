using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrakesOmniConfig.Config.Settings;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DrakesOmniConfig.Config.Flags;

/// <summary>
/// Server flags (Phase F2): declared with defaults in <c>Modlets/flags.yaml</c>, switched by admins at runtime
/// (<see cref="FlagState"/>). Effective value = the saved admin state if there is one, else the default.
/// Pure data: no Unity calls (safe on the start-up worker thread).
/// <code>
/// flags:
///   event_weekend: false
///   pvp: true
/// </code>
/// </summary>
internal sealed class FlagSet
{
    public const string FileName = "flags.yaml";

    /// <summary>Flag names: lower-case letters, digits and '_', 1 to 32 long (they also become world global keys).</summary>
    public static readonly Regex NamePattern = new("^[a-z0-9_]{1,32}$", RegexOptions.Compiled);

    private static readonly IDeserializer In = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    private sealed class Dto
    {
        public Dictionary<string, bool> Flags { get; set; } = new();
    }

    private readonly Dictionary<string, bool> _defaults;
    private readonly Dictionary<string, bool> _saved;

    private FlagSet(Dictionary<string, bool> defaults, Dictionary<string, bool> saved)
    {
        _defaults = defaults;
        _saved = saved;
    }

    public static FlagSet Empty { get; } = new(new Dictionary<string, bool>(), new Dictionary<string, bool>());

    /// <summary>Declared flags (name -> default) and the admin's saved state, read from disk now.</summary>
    public static FlagSet Load(string modletsFolder)
    {
        var defaults = new Dictionary<string, bool>(StringComparer.Ordinal);
        string path = Path.Combine(modletsFolder, FileName);
        if (File.Exists(path))
        {
            try
            {
                foreach (KeyValuePair<string, bool> flag in (In.Deserialize<Dto>(File.ReadAllText(path)) ?? new Dto()).Flags)
                {
                    if (NamePattern.IsMatch(flag.Key))
                    {
                        defaults[flag.Key] = flag.Value;
                    }
                    else
                    {
                        ModLog.Warning($"{FileName}: flag name '{flag.Key}' must be lower-case letters, digits or '_' (1-32); skipped.");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"Could not read '{path}': {ex.Message}. No flags are declared.");
            }
        }

        return new FlagSet(defaults, FlagState.Load(modletsFolder));
    }

    public bool IsDeclared(string name) => _defaults.ContainsKey(name);

    /// <summary>Effective value; an undeclared flag is off.</summary>
    public bool IsOn(string name)
    {
        if (!_defaults.TryGetValue(name, out bool value))
        {
            return false;
        }

        return _saved.TryGetValue(name, out bool saved) ? saved : value;
    }

    /// <summary>Every declared flag: name, effective value, and whether an admin switch overrides the default.</summary>
    public IEnumerable<(string Name, bool On, bool Switched)> All =>
        _defaults.Keys.OrderBy(n => n, StringComparer.Ordinal).Select(n => (n, IsOn(n), _saved.ContainsKey(n)));

    /// <summary>"a, b" all on (empty list = true).</summary>
    public bool AllOn(IEnumerable<string>? names) => names == null || names.All(IsOn);

    public bool AnyOn(IEnumerable<string>? names) => names != null && names.Any(IsOn);
}

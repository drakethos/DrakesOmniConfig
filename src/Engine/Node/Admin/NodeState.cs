using System;
using System.Collections.Generic;
using System.IO;
using DrakesOmniConfig.Config.Settings;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DrakesOmniConfig.Engine.Node.Admin;

/// <summary>
/// Admin on/off switches for modlets and rules, kept in their own file next to the Modlets folder so a
/// toggle from the panel never rewrites an author's modlet files. Authoritative side only.
/// </summary>
internal sealed class NodeState
{
    private static readonly ISerializer Out = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
    private static readonly IDeserializer In = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    private sealed class Dto
    {
        public List<string> DisabledModlets { get; set; } = new();
        public List<string> DisabledRules { get; set; } = new();
    }

    private string _path = "";
    public HashSet<string> DisabledModlets { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DisabledRules { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string PathFor(string modletsFolder) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modletsFolder).TrimEnd(Path.DirectorySeparatorChar)) ?? modletsFolder, "omniconfig-state.yaml");

    public void Load(string modletsFolder)
    {
        _path = PathFor(modletsFolder);
        DisabledModlets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DisabledRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            Dto dto = In.Deserialize<Dto>(File.ReadAllText(_path)) ?? new Dto();
            DisabledModlets.UnionWith(dto.DisabledModlets);
            DisabledRules.UnionWith(dto.DisabledRules);
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not read '{_path}': {ex.Message}. Every modlet and rule starts enabled.");
        }
    }

    public void Save()
    {
        if (_path.Length == 0)
        {
            return;
        }

        var dto = new Dto { DisabledModlets = new List<string>(DisabledModlets), DisabledRules = new List<string>(DisabledRules) };
        dto.DisabledModlets.Sort(StringComparer.OrdinalIgnoreCase);
        dto.DisabledRules.Sort(StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(_path, "# Admin on/off switches from the DrakesOmniConfig panel. Safe to edit or delete.\n" + Out.Serialize(dto));
    }

    public static bool Toggle(HashSet<string> set, string key)
    {
        if (set.Remove(key))
        {
            return true; // now enabled
        }

        set.Add(key);
        return false;
    }
}

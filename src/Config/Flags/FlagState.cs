using System;
using System.Collections.Generic;
using System.IO;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Node.Admin;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DrakesOmniConfig.Config.Flags;

/// <summary>
/// Admin flag switches, kept like <see cref="NodeState"/>: their own file next to the Modlets folder
/// (<c>omni-flags.yaml</c>), so a switch never rewrites an author's <c>flags.yaml</c>. Survives restarts.
/// </summary>
internal static class FlagState
{
    private static readonly ISerializer Out = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
    private static readonly IDeserializer In = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    private sealed class Dto
    {
        public Dictionary<string, bool> Flags { get; set; } = new();
    }

    public static string PathFor(string modletsFolder) =>
        Path.Combine(Path.GetDirectoryName(NodeState.PathFor(modletsFolder)) ?? modletsFolder, "omni-flags.yaml");

    public static Dictionary<string, bool> Load(string modletsFolder)
    {
        string path = PathFor(modletsFolder);
        if (!File.Exists(path))
        {
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        try
        {
            return new Dictionary<string, bool>((In.Deserialize<Dto>(File.ReadAllText(path)) ?? new Dto()).Flags, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            ModLog.Error($"Could not read '{path}': {ex.Message}. Flags use their defaults.");
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        }
    }

    /// <summary>Saves an admin switch (null = forget it, back to the flags.yaml default).</summary>
    public static void Set(string modletsFolder, string name, bool? on)
    {
        Dictionary<string, bool> saved = Load(modletsFolder);
        if (on == null)
        {
            saved.Remove(name);
        }
        else
        {
            saved[name] = on.Value;
        }

        File.WriteAllText(PathFor(modletsFolder), Out.Serialize(new Dto { Flags = saved }));
    }
}

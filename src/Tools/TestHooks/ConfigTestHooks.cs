using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using DrakesOmniConfig.Engine.ModConfigs;

namespace DrakesOmniConfig.Tools.TestHooks;

/// <summary>DrakesTestLab hooks for Phase F <c>configs:</c> bindings (suite DrakesOmniConfig-Configs).</summary>
internal static class ConfigTestHooks
{
    private static string ModletsRoot => Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "Modlets");

    /// <summary>{ mod, section, key, disk? } — the entry's value now (as text), or with disk=true what a save would write.</summary>
    [TestHook("omniconfig.cfg")]
    internal static object? Cfg(Dictionary<string, object?> args)
    {
        ConfigEntryBase entry = Entry(args);
        if (args.TryGetValue("disk", out object? disk) && disk is true)
        {
            return entry.GetSerializedValue();
        }

        return TomlTypeConverter.ConvertToString(entry.BoxedValue, entry.SettingType);
    }

    /// <summary>{ mod } — saves that mod's config file now, like a config manager would (disk protection must hold).</summary>
    [TestHook("omniconfig.cfgSave")]
    internal static object? CfgSave(Dictionary<string, object?> args)
    {
        Plugin(Arg(args, "mod")).Config.Save();
        return true;
    }

    /// <summary>{ mod, section, key } — the value written in the mod's .cfg file on disk right now.</summary>
    [TestHook("omniconfig.cfgFile")]
    internal static object? CfgFile(Dictionary<string, object?> args)
    {
        ConfigEntryBase entry = Entry(args);
        string section = "";
        foreach (string raw in File.ReadAllLines(entry.ConfigFile.ConfigFilePath))
        {
            string line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }

            int eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (section == entry.Definition.Section && eq > 0 && line.Substring(0, eq) == entry.Definition.Key)
            {
                return line.Substring(eq + 3);
            }
        }

        throw new InvalidOperationException("the key is not in the file");
    }

    /// <summary>{ mod, section, key } — what the last apply did with that binding ("applied ...", "rejected: ...", "mod ... not installed", ...).</summary>
    [TestHook("omniconfig.cfgStatus")]
    internal static object? CfgStatus(Dictionary<string, object?> args)
    {
        string id = DrakesOmniConfig.Config.Modlets.Schema.ResolvedConfigBinding.Identify(Arg(args, "mod"), Arg(args, "section"), Arg(args, "key"));
        return ConfigBindingApplier.Status.TryGetValue(id, out string? status) ? status : "";
    }

    /// <summary>{ mod, section, key } — the modlet whose binding won for that entry ("" = not bound).</summary>
    [TestHook("omniconfig.cfgWinner")]
    internal static object? CfgWinner(Dictionary<string, object?> args)
    {
        string id = DrakesOmniConfig.Config.Modlets.Schema.ResolvedConfigBinding.Identify(Arg(args, "mod"), Arg(args, "section"), Arg(args, "key"));
        return DrakesOmniConfig.Engine.Orchestration.ModletOrchestrator.CurrentConfigBindings.FirstOrDefault(b => b.Identity == id)?.WinningModletId ?? "";
    }

    /// <summary>
    /// { modlet, file, from, priority? } — writes a test modlet: manifest.yaml (with the priority) plus one YAML file copied
    /// from the suites folder. Reload afterwards (omniconfig.admin reload) to apply it.
    /// </summary>
    [TestHook("omniconfig.putModlet")]
    internal static object? PutModlet(Dictionary<string, object?> args)
    {
        string folder = Path.Combine(ModletsRoot, Arg(args, "modlet"));
        Directory.CreateDirectory(folder);
        int priority = args.TryGetValue("priority", out object? p) && p != null ? Convert.ToInt32(p, CultureInfo.InvariantCulture) : 0;
        string requires = args.TryGetValue("requires", out object? r) && r != null ? $"requiresFlags: [{r}]\n" : "";
        File.WriteAllText(Path.Combine(folder, "manifest.yaml"), $"schemaVersion: 1\ndisplayName: {Arg(args, "modlet")}\nenabled: true\npriority: {priority}\n{requires}");
        File.Copy(Path.Combine(Paths.ConfigPath, "DrakesTestLab", "suites", Arg(args, "from")), Path.Combine(folder, Arg(args, "file")), overwrite: true);
        return true;
    }

    /// <summary>{ modlet } — deletes a test modlet folder (reload afterwards).</summary>
    [TestHook("omniconfig.removeModlet")]
    internal static object? RemoveModlet(Dictionary<string, object?> args)
    {
        string folder = Path.Combine(ModletsRoot, Arg(args, "modlet"));
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return true;
    }

    /// <summary>{ file, from } — copies a suites file to the Modlets root (vars.yaml, groups.yaml, flags.yaml).</summary>
    [TestHook("omniconfig.putRootFile")]
    internal static object? PutRootFile(Dictionary<string, object?> args)
    {
        File.Copy(Path.Combine(Paths.ConfigPath, "DrakesTestLab", "suites", Arg(args, "from")), Path.Combine(ModletsRoot, Arg(args, "file")), overwrite: true);
        return true;
    }

    /// <summary>{ file } — deletes a Modlets root file; also clears the admin flag switches when file is flags.yaml.</summary>
    [TestHook("omniconfig.removeRootFile")]
    internal static object? RemoveRootFile(Dictionary<string, object?> args)
    {
        string path = Path.Combine(ModletsRoot, Arg(args, "file"));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (Arg(args, "file") == DrakesOmniConfig.Config.Flags.FlagSet.FileName && File.Exists(DrakesOmniConfig.Config.Flags.FlagState.PathFor(ModletsRoot)))
        {
            File.Delete(DrakesOmniConfig.Config.Flags.FlagState.PathFor(ModletsRoot));
        }

        return true;
    }

    /// <summary>{ key } — whether the world has that global key.</summary>
    [TestHook("omniconfig.globalKey")]
    internal static object? GlobalKey(Dictionary<string, object?> args) =>
        ZoneSystem.instance != null ? ZoneSystem.instance.GetGlobalKey(Arg(args, "key")) : throw new InvalidOperationException("no world");

    private static ConfigEntryBase Entry(Dictionary<string, object?> args) =>
        ConfigBindingApplier.Find(Arg(args, "mod"), Arg(args, "section"), Arg(args, "key"), out string? missing)
        ?? throw new InvalidOperationException(missing);

    private static BaseUnityPlugin Plugin(string guid) =>
        BepInEx.Bootstrap.Chainloader.PluginInfos.FirstOrDefault(p => string.Equals(p.Key, guid, StringComparison.OrdinalIgnoreCase)).Value?.Instance
        ?? throw new InvalidOperationException($"mod '{guid}' is not installed");

    private static string Arg(Dictionary<string, object?> args, string name) =>
        args.TryGetValue(name, out object? value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : throw new ArgumentException($"needs '{name}'");
}

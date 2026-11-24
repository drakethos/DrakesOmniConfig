using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using HarmonyLib;

namespace DrakesOmniConfig.Engine.ModConfigs;

/// <summary>
/// Applies <c>configs:</c> bindings to other mods' BepInEx config entries.
/// <list type="bullet">
/// <item><b>In memory only.</b> The mod's .cfg file is never changed: the value is set with the file's SaveOnConfigSet off,
/// and <see cref="ConfigEntryBase.GetSerializedValue"/> is patched to write the entry's own (baseline) value whenever
/// anything saves the file while the binding is on.</item>
/// <item><b>From the baseline.</b> The value the entry had before OmniConfig first touched it is kept; every apply computes
/// from it, so <c>*2</c> stays x2 across reloads, and removing a binding puts the baseline back.</item>
/// <item><b>Server:</b> applies everything; a mod that syncs the entry itself (<see cref="SyncDetection"/>) then pushes it to
/// players on its own. <b>Client:</b> applies what OmniConfig's sync delivered, skipping entries the mod syncs itself, and
/// puts every baseline back when the world closes.</item>
/// <item>A missing mod or key is reported once and tried again on every apply (some mods bind entries late).</item>
/// <item>If something else changes a bound entry (a config manager, the mod), the binding is set again and the change logged.</item>
/// </list>
/// Limitation: a mod that reads a setting only at start-up sees a new value only after a relog or restart.
/// </summary>
internal static class ConfigBindingApplier
{
    private sealed class Bound
    {
        public ConfigEntryBase Entry = null!;
        public object? Baseline;
        public object? Applied;
    }

    private static readonly Dictionary<ConfigEntryBase, Bound> BoundEntries = new();
    private static readonly Dictionary<string, string> StatusByIdentity = new();
    private static readonly HashSet<string> Warned = new();
    private static readonly HashSet<ConfigFile> Watched = new();
    private static int _writing;

    /// <summary>Identity ("guid|section|key") -> what happened on the last apply. For configs.list and tests.</summary>
    public static IReadOnlyDictionary<string, string> Status => StatusByIdentity;

    /// <summary>
    /// Applies the bindings and puts back every entry no longer bound. authority = server/host (apply all);
    /// otherwise a joined client (skip entries the mod syncs itself). Returns the bindings OmniConfig must deliver
    /// itself (not self-synced, or not found here: a client-only mod).
    /// </summary>
    public static List<ResolvedConfigBinding> Apply(IReadOnlyList<ResolvedConfigBinding> bindings, bool authority)
    {
        var deliver = new List<ResolvedConfigBinding>();
        var keep = new HashSet<ConfigEntryBase>();
        StatusByIdentity.Clear();

        foreach (ResolvedConfigBinding binding in bindings)
        {
            ConfigEntryBase? entry = Find(binding, out string? missing);
            if (entry == null)
            {
                StatusByIdentity[binding.Identity] = missing!;
                WarnOnce(binding.Identity, $"Config binding {binding} (modlet '{binding.WinningModletId}'): {missing}; will try again on the next reload or world start.");
                deliver.Add(binding);
                continue;
            }

            bool selfSynced = SyncDetection.IsSelfSynced(entry);
            if (!selfSynced)
            {
                deliver.Add(binding);
            }

            if (!authority && selfSynced)
            {
                StatusByIdentity[binding.Identity] = "skipped: the mod syncs this entry itself";
                continue;
            }

            if (!BoundEntries.TryGetValue(entry, out Bound? bound))
            {
                bound = new Bound { Entry = entry, Baseline = entry.BoxedValue };
            }

            if (!TryCompute(entry, bound.Baseline, binding.Expression, out object? value, out string? problem))
            {
                StatusByIdentity[binding.Identity] = "rejected: " + problem;
                WarnOnce(binding.Identity + "|" + problem, $"Config binding {binding} (modlet '{binding.WinningModletId}'): {problem}; entry left as it was.");
                continue;
            }

            BoundEntries[entry] = bound;
            bound.Applied = value;
            keep.Add(entry);
            Watch(entry.ConfigFile);
            Write(entry, value);
            StatusByIdentity[binding.Identity] = "applied" + (selfSynced ? " (the mod's own sync delivers it)" : authority ? " (OmniConfig sync delivers it)" : "");
        }

        foreach (Bound gone in BoundEntries.Values.Where(b => !keep.Contains(b.Entry)).ToList())
        {
            Restore(gone);
        }

        return deliver;
    }

    /// <summary>Puts every bound entry back to its own value (world closed, or the client disconnected).</summary>
    public static void RestoreAll()
    {
        foreach (Bound bound in BoundEntries.Values.ToList())
        {
            Restore(bound);
        }

        StatusByIdentity.Clear();
    }

    /// <summary>The value written to disk for this entry: its baseline while bound (null = not bound). For tests.</summary>
    public static bool TryGetBaseline(ConfigEntryBase entry, out object? baseline)
    {
        if (BoundEntries.TryGetValue(entry, out Bound? bound))
        {
            baseline = bound.Baseline;
            return true;
        }

        baseline = null;
        return false;
    }

    public static ConfigEntryBase? Find(string mod, string section, string key, out string? missing)
    {
        BepInEx.PluginInfo? plugin = null;
        foreach (KeyValuePair<string, BepInEx.PluginInfo> pair in Chainloader.PluginInfos)
        {
            if (string.Equals(pair.Key, mod.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                plugin = pair.Value;
                break;
            }
        }

        if (plugin?.Instance == null)
        {
            missing = $"mod '{mod}' is not installed";
            return null;
        }

        foreach (ConfigDefinition definition in plugin.Instance.Config.Keys)
        {
            if (string.Equals(definition.Section, section.Trim(), StringComparison.Ordinal) && string.Equals(definition.Key, key.Trim(), StringComparison.Ordinal))
            {
                missing = null;
                return plugin.Instance.Config[definition];
            }
        }

        missing = $"'{mod}' has no setting [{section}] {key}";
        return null;
    }

    private static ConfigEntryBase? Find(ResolvedConfigBinding binding, out string? missing) => Find(binding.Mod, binding.Section, binding.Key, out missing);

    // ---------------------------------------------------------------- values

    private static bool TryCompute(ConfigEntryBase entry, object? baseline, FieldValueExpression expression, out object? value, out string? problem)
    {
        value = null;
        problem = null;
        Type type = entry.SettingType;
        try
        {
            if (expression.Operator == ValueOperator.Set)
            {
                if (IsNumber(type) && expression.Operand is double number)
                {
                    value = ToNumber(number, type);
                }
                else
                {
                    string text = Convert.ToString(expression.Operand, CultureInfo.InvariantCulture) ?? "";
                    value = TomlTypeConverter.ConvertToValue(text, type);
                }
            }
            else
            {
                if (!IsNumber(type))
                {
                    problem = $"'{expression.Operator}' needs a number setting, this one is {type.Name}";
                    return false;
                }

                double from = Convert.ToDouble(baseline, CultureInfo.InvariantCulture);
                double by = Convert.ToDouble(expression.Operand, CultureInfo.InvariantCulture);
                double result = expression.Operator switch
                {
                    ValueOperator.Add => from + (expression.IsPercentage ? from * by / 100.0 : by),
                    ValueOperator.Subtract => from - (expression.IsPercentage ? from * by / 100.0 : by),
                    ValueOperator.Multiply => from * by,
                    ValueOperator.Divide when by != 0 => from / by,
                    _ => double.NaN,
                };
                if (double.IsNaN(result))
                {
                    problem = "division by zero";
                    return false;
                }

                value = ToNumber(result, type);
            }
        }
        catch (Exception ex)
        {
            problem = $"'{expression.Operand}' is not a valid {type.Name} ({ex.GetType().Name})";
            return false;
        }

        AcceptableValueBase? acceptable = entry.Description?.AcceptableValues;
        if (acceptable != null && !acceptable.IsValid(value))
        {
            if (acceptable.GetType().Name.StartsWith("AcceptableValueRange", StringComparison.Ordinal))
            {
                object clamped = acceptable.Clamp(value);
                ModLog.Warning($"Config binding {entry.Definition}: {value} is outside {acceptable.ToDescriptionString().Trim()}; using {clamped}.");
                value = clamped;
            }
            else
            {
                problem = $"'{value}' is not one of the allowed values ({acceptable.ToDescriptionString().Trim()})";
                return false;
            }
        }

        return true;
    }

    private static bool IsNumber(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) ||
        type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(float) || type == typeof(double) ||
        type == typeof(decimal);

    private static object ToNumber(double number, Type type)
    {
        bool integral = type != typeof(float) && type != typeof(double) && type != typeof(decimal);
        return Convert.ChangeType(integral ? Math.Round(number, MidpointRounding.AwayFromZero) : number, type, CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- writing (memory only)

    private static void Write(ConfigEntryBase entry, object? value)
    {
        ConfigFile file = entry.ConfigFile;
        bool save = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false;
        _writing++;
        try
        {
            entry.BoxedValue = value;
        }
        finally
        {
            _writing--;
            file.SaveOnConfigSet = save;
        }
    }

    private static void Restore(Bound bound)
    {
        BoundEntries.Remove(bound.Entry);
        try
        {
            Write(bound.Entry, bound.Baseline);
        }
        catch (Exception ex)
        {
            ModLog.Warning($"Could not put {bound.Entry.Definition} back: {ex.Message}");
        }
    }

    private static void Watch(ConfigFile file)
    {
        if (Watched.Add(file))
        {
            file.SettingChanged += OnSettingChanged;
        }
    }

    private static void OnSettingChanged(object sender, SettingChangedEventArgs args)
    {
        if (_writing > 0 || !BoundEntries.TryGetValue(args.ChangedSetting, out Bound? bound) || Equals(args.ChangedSetting.BoxedValue, bound.Applied))
        {
            return;
        }

        ModLog.Info($"{args.ChangedSetting.Definition} was changed outside OmniConfig; a modlet binds it, so it is set back to {bound.Applied}.");
        Write(args.ChangedSetting, bound.Applied);
    }

    private static void WarnOnce(string key, string message)
    {
        if (Warned.Add(key))
        {
            ModLog.Warning(message);
        }
    }

    /// <summary>Any save of a bound entry's file writes the entry's own value, never the binding's (ServerSync patches the same method on clients).</summary>
    [HarmonyPatch(typeof(ConfigEntryBase), nameof(ConfigEntryBase.GetSerializedValue))]
    private static class ConfigEntryBase_GetSerializedValue_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ConfigEntryBase __instance, ref string __result)
        {
            if (!BoundEntries.TryGetValue(__instance, out Bound? bound))
            {
                return true;
            }

            __result = TomlTypeConverter.ConvertToString(bound.Baseline, __instance.SettingType);
            return false;
        }
    }
}

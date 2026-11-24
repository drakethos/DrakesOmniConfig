using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.ModConfigs;
using HarmonyLib;

namespace DrakesOmniConfig.Tools.HtmlHelper;

/// <summary>
/// Phase F4: writes BepInEx/config/DrakesOmniConfig/ModConfigManifest.json, every installed mod's settings (GUID,
/// section, key, type, default, description, allowed values, enum names, and whether the mod syncs it itself), for the
/// Config Builder's "Mod settings" page that writes <c>configs:</c> entries. Written when a world loads (every plugin has
/// bound its settings by then); rewritten only when the content changed.
/// </summary>
internal static class ModConfigManifest
{
    public static string PathFor() => Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "ModConfigManifest.json");

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    private static class ZNetScene_Awake_Patch
    {
        private static void Postfix()
        {
            try
            {
                Write(PathFor());
            }
            catch (Exception ex)
            {
                ModLog.Warning($"ModConfigManifest: {ex.Message}");
            }
        }
    }

    public static void Write(string path)
    {
        var sb = new StringBuilder("{\n  \"mods\": [");
        bool firstMod = true;
        foreach (PluginInfo plugin in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.GUID, StringComparer.OrdinalIgnoreCase))
        {
            if (plugin.Instance == null)
            {
                continue;
            }

            ConfigFile config = plugin.Instance.Config;
            sb.Append(firstMod ? "\n" : ",\n").Append("    { \"guid\": ").Append(Json(plugin.Metadata.GUID))
              .Append(", \"name\": ").Append(Json(plugin.Metadata.Name))
              .Append(", \"version\": ").Append(Json(plugin.Metadata.Version.ToString()))
              .Append(", \"settings\": [");
            firstMod = false;
            bool firstSetting = true;
            foreach (ConfigDefinition definition in config.Keys.OrderBy(d => d.Section, StringComparer.Ordinal).ThenBy(d => d.Key, StringComparer.Ordinal))
            {
                ConfigEntryBase entry = config[definition];
                sb.Append(firstSetting ? "\n" : ",\n").Append("      { \"section\": ").Append(Json(definition.Section))
                  .Append(", \"key\": ").Append(Json(definition.Key))
                  .Append(", \"type\": ").Append(Json(TypeName(entry.SettingType)))
                  .Append(", \"default\": ").Append(Json(Text(entry.DefaultValue, entry.SettingType)))
                  .Append(", \"description\": ").Append(Json(entry.Description?.Description ?? ""))
                  .Append(", \"selfSynced\": ").Append(SyncDetection.IsSelfSynced(entry) ? "true" : "false");
                AppendAllowed(sb, entry);
                sb.Append(" }");
                firstSetting = false;
            }

            sb.Append(firstSetting ? "] }" : "\n    ] }");
        }

        sb.Append("\n  ]\n}\n");
        string json = sb.ToString();
        if (File.Exists(path) && File.ReadAllText(path) == json)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        ModLog.Info($"Wrote {Path.GetFileName(path)} (every installed mod's settings, for the Config Builder's Mod settings page).");
    }

    private static void AppendAllowed(StringBuilder sb, ConfigEntryBase entry)
    {
        Type type = entry.SettingType;
        if (type.IsEnum)
        {
            sb.Append(", \"values\": [").Append(string.Join(", ", Enum.GetNames(type).Select(Json))).Append(']');
            return;
        }

        if (type == typeof(bool))
        {
            sb.Append(", \"values\": [\"true\", \"false\"]");
            return;
        }

        AcceptableValueBase? acceptable = entry.Description?.AcceptableValues;
        if (acceptable == null)
        {
            return;
        }

        Type kind = acceptable.GetType();
        PropertyInfo? min = kind.GetProperty("MinValue"), max = kind.GetProperty("MaxValue");
        if (min != null && max != null)
        {
            sb.Append(", \"min\": ").Append(Json(Text(min.GetValue(acceptable), type)))
              .Append(", \"max\": ").Append(Json(Text(max.GetValue(acceptable), type)));
            return;
        }

        if (kind.GetProperty("AcceptableValues")?.GetValue(acceptable) is IEnumerable list)
        {
            sb.Append(", \"values\": [").Append(string.Join(", ", list.Cast<object>().Select(v => Json(Text(v, type))))).Append(']');
        }
    }

    private static string TypeName(Type type) =>
        type.IsEnum ? "enum" : type == typeof(bool) ? "bool" : type == typeof(string) ? "string" :
        type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? "number" :
        type.IsPrimitive ? "integer" : type.Name;

    private static string Text(object? value, Type type)
    {
        try
        {
            return TomlTypeConverter.CanConvert(type) ? TomlTypeConverter.ConvertToString(value, type) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
        catch
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }

    private static string Json(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}

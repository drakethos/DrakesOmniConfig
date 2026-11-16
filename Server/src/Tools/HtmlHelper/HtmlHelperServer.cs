using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using DrakesOmniConfig.Config.Settings;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Tools.HtmlHelper;

/// <summary>
/// Writes a plain JSON manifest of prefab names and field layouts so the static HTML config builder
/// (Server/Tools/ConfigBuilder/index.html) has something to browse — without that page ever reading
/// the DLL or running any code itself. This is the ONLY place that does the reflection work; the HTML
/// side is pure data display + YAML text generation from a file the browser's own file picker loads.
///
/// Deliberately bounded, not a "wacky DB" dump of everything: only component types actually attached to
/// a real prefab (plus Player/Humanoid/Character explicitly, since Scope.Global needs them even though
/// they're not always "a prefab's component" the same way), recursing into non-Unity nested field types
/// (structs like DamageTypes, data classes like SharedData) up to a fixed depth — never into
/// Transform/GameObject/engine internals, which would explode the output for no benefit.
///
/// One-time per (prefab count) — EnsureGameDataManifest skips the rescan if the manifest already exists
/// and its recorded prefab count still matches, so a normal launch is cheap; adding/removing prefabs
/// (a new mod, a mod removed) changes the count and triggers a fresh scan automatically. A vanilla game
/// update that only changes field layout without changing prefab count won't auto-detect — delete the
/// manifest + its .meta sidecar to force a refresh in that case.
/// </summary>
internal static class HtmlHelperServer
{
    private const int MaxDepth = 4;
    private const BindingFlags FieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    // Server-exclusive trigger — this file only exists in DrakesOmniConfigServer, so this patch is
    // never registered in the baseline build. Same string-literal reasoning as PrefabPassHook's patch.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    private static class ZNetScene_Awake_ManifestPatch
    {
        private static void Postfix()
        {
            string manifestPath = Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "GameDataManifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            EnsureGameDataManifest(manifestPath);
        }
    }

    public static void EnsureGameDataManifest(string outputPath)
    {
        if (ZNetScene.instance == null)
        {
            ModLog.Warning("EnsureGameDataManifest: ZNetScene.instance is null — call this after prefabs are loaded.");
            return;
        }

        int currentPrefabCount = ZNetScene.instance.m_prefabs.Count;
        string metaPath = outputPath + ".meta";

        if (File.Exists(outputPath) && File.Exists(metaPath)
            && int.TryParse(File.ReadAllText(metaPath).Trim(), out int recordedCount)
            && recordedCount == currentPrefabCount)
        {
            ModLog.Debug($"Game data manifest up to date ({currentPrefabCount} prefabs) — skipping rescan.");
            return;
        }

        WriteGameDataManifest(outputPath, currentPrefabCount);
        File.WriteAllText(metaPath, currentPrefabCount.ToString());
    }

    private static void WriteGameDataManifest(string outputPath, int prefabCount)
    {
        var prefabEntries = new List<(string Name, List<string> Components)>();
        var typeFields = new Dictionary<string, List<(string Name, string Type)>>();
        var enumValues = new Dictionary<string, List<string>>();
        var visited = new HashSet<Type>();
        var queue = new Queue<(Type Type, int Depth)>();

        foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            var componentNames = new List<string>();
            foreach (Component component in prefab.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                Type type = component.GetType();
                componentNames.Add(type.Name);
                if (visited.Add(type))
                {
                    queue.Enqueue((type, 0));
                }
            }

            prefabEntries.Add((prefab.name, componentNames));
        }

        foreach (string typeName in new[] { "Player", "Humanoid", "Character" })
        {
            Type? type = ResolveTypeByName(typeName);
            if (type != null && visited.Add(type))
            {
                queue.Enqueue((type, 0));
            }
        }

        while (queue.Count > 0)
        {
            (Type type, int depth) = queue.Dequeue();
            if (depth > MaxDepth)
            {
                continue;
            }

            var fields = new List<(string, string)>();
            foreach (FieldInfo field in type.GetFields(FieldFlags))
            {
                fields.Add((field.Name, field.FieldType.Name));

                // Enums are terminal for browsing purposes (their "fields" are named constants, not
                // instance data) — record the real member names once instead of queuing them to scan.
                if (field.FieldType.IsEnum)
                {
                    if (!enumValues.ContainsKey(field.FieldType.Name))
                    {
                        enumValues[field.FieldType.Name] = new List<string>(Enum.GetNames(field.FieldType));
                    }
                }
                else if (ShouldScanNested(field.FieldType) && visited.Add(field.FieldType))
                {
                    queue.Enqueue((field.FieldType, depth + 1));
                }
            }

            typeFields[type.Name] = fields;
        }

        string json = BuildJson(prefabEntries, typeFields, enumValues);
        File.WriteAllText(outputPath, json);
        ModLog.Info($"Wrote game data manifest: {prefabEntries.Count} prefabs, {typeFields.Count} types -> '{outputPath}'.");
    }

    // Skip anything Unity/engine-ish (Transform, GameObject, Component subclasses other than the roots
    // we already enqueued explicitly, anything in System.*) — these are huge, mostly irrelevant to
    // modlet overrides, and the reason a naive "reflect everything" dump turns into a wacky-DB-style mess.
    private static bool ShouldScanNested(Type type)
    {
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
        {
            return false;
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            return false;
        }

        string? ns = type.Namespace;
        if (ns != null && (ns.StartsWith("UnityEngine") || ns.StartsWith("System")))
        {
            return false;
        }

        return true;
    }

    private static Type? ResolveTypeByName(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? found = assembly.GetType(typeName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    // Hand-rolled rather than pulling in a JSON library for this one file — the shape is simple
    // (strings and lists of strings), so a tiny escaper is enough.
    private static string BuildJson(
        List<(string Name, List<string> Components)> prefabs,
        Dictionary<string, List<(string Name, string Type)>> types,
        Dictionary<string, List<string>> enums)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"generatedAt\": \"{DateTime.UtcNow:O}\",\n");

        sb.Append("  \"prefabs\": [\n");
        for (int i = 0; i < prefabs.Count; i++)
        {
            (string name, List<string> components) = prefabs[i];
            sb.Append("    { \"name\": ").Append(JsonString(name)).Append(", \"components\": [");
            sb.Append(string.Join(", ", components.ConvertAll(JsonString)));
            sb.Append(i < prefabs.Count - 1 ? "] },\n" : "] }\n");
        }

        sb.Append("  ],\n");

        sb.Append("  \"types\": {\n");
        int typeIndex = 0;
        foreach (KeyValuePair<string, List<(string Name, string Type)>> entry in types)
        {
            sb.Append("    ").Append(JsonString(entry.Key)).Append(": [\n");
            List<(string Name, string Type)> fields = entry.Value;
            for (int i = 0; i < fields.Count; i++)
            {
                sb.Append("      { \"name\": ").Append(JsonString(fields[i].Name))
                  .Append(", \"type\": ").Append(JsonString(fields[i].Type)).Append(" }");
                sb.Append(i < fields.Count - 1 ? ",\n" : "\n");
            }

            sb.Append(++typeIndex < types.Count ? "    ],\n" : "    ]\n");
        }

        sb.Append("  },\n");

        sb.Append("  \"enums\": {\n");
        int enumIndex = 0;
        foreach (KeyValuePair<string, List<string>> entry in enums)
        {
            sb.Append("    ").Append(JsonString(entry.Key)).Append(": [");
            sb.Append(string.Join(", ", entry.Value.ConvertAll(JsonString)));
            sb.Append(++enumIndex < enums.Count ? "],\n" : "]\n");
        }

        sb.Append("  }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string JsonString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

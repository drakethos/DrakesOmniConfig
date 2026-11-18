using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DrakesOmniConfig.Engine.Node.Runtime;

/// <summary>
/// NodeCatalog.json: every trigger, condition and action this engine knows (with args, types and help), plus
/// the pick-lists the Rules editor offers — item prefabs, status effects, skills, biomes. The editor builds its
/// palette from this, so it can never offer a node the installed engine doesn't have. Written by the Server
/// build at world load; shared here so a test can write it too.
/// </summary>
internal static class NodeCatalog
{
    public static void Write(string path)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"schemaVersion\": 1,\n");
        foreach (NodeKind kind in new[] { NodeKind.Trigger, NodeKind.Condition, NodeKind.Action })
        {
            sb.Append("  \"").Append(kind.ToString().ToLowerInvariant()).Append("s\": [\n");
            sb.Append(string.Join(",\n", NodeRegistry.All(kind).Select(Def)));
            sb.Append("\n  ],\n");
        }

        IEnumerable<string> items = ObjectDB.instance == null
            ? Enumerable.Empty<string>()
            : ObjectDB.instance.m_items.Where(i => i != null && i.GetComponent<ItemDrop>() != null).Select(i => i.name);
        IEnumerable<string> effects = ObjectDB.instance == null
            ? Enumerable.Empty<string>()
            : ObjectDB.instance.m_StatusEffects.Where(e => e != null).Select(e => e.name);
        IEnumerable<string> skills = Enum.GetNames(typeof(Skills.SkillType)).Where(n => n != "None" && n != "All");
        IEnumerable<string> biomes = Enum.GetNames(typeof(Heightmap.Biome)).Where(n => n != "None" && n != "All");

        sb.Append("  \"items\": ").Append(List(items)).Append(",\n");
        sb.Append("  \"statusEffects\": ").Append(List(effects)).Append(",\n");
        sb.Append("  \"skills\": ").Append(List(skills)).Append(",\n");
        sb.Append("  \"biomes\": ").Append(List(biomes)).Append("\n}\n");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }

    private static string Def(NodeDef d)
    {
        var args = d.Args.Select(a =>
            "{ \"name\": " + Str(a.Name) + ", \"type\": " + Str(a.Type) + ", \"required\": " + (a.Required ? "true" : "false") +
            ", \"default\": " + Str(a.Default) + ", \"help\": " + Str(a.Help) + ", \"choices\": " + List(a.Choices) + " }");
        return "    { \"name\": " + Str(d.Name) + ", \"help\": " + Str(d.Help) + ", \"shorthand\": " + Str(d.Shorthand) +
               ", \"branches\": " + Str(d.BranchKey) + ", \"args\": [" + string.Join(", ", args) + "] }";
    }

    private static string List(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase).Select(Str)) + "]";

    private static string Str(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
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

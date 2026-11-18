using System;
using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Engine.Node.Schema;

namespace DrakesOmniConfig.Engine.Node.Runtime;

internal enum NodeKind
{
    Trigger,
    Condition,
    Action
}

/// <summary>One argument a node accepts. <see cref="Type"/> drives the HTML editor's input control.</summary>
internal sealed class NodeArgDef
{
    public string Name { get; set; } = "";

    /// <summary>item, prefab, number, percent, text, statusEffect, skill, biome, bool, choice.</summary>
    public string Type { get; set; } = "text";

    public bool Required { get; set; }
    public string Default { get; set; } = "";
    public string Help { get; set; } = "";
    public List<string> Choices { get; set; } = new();
}

internal sealed class NodeDef
{
    public NodeKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Help { get; set; } = "";
    public List<NodeArgDef> Args { get; set; } = new();

    /// <summary>The arg a bare scalar fills: <c>- chance: 10</c> means <c>- chance: { percent: 10 }</c>.</summary>
    public string Shorthand { get; set; } = "";

    /// <summary>Branch support (popup buttons). The YAML key holding the list, e.g. "buttons".</summary>
    public string BranchKey { get; set; } = "";

    public Func<NodeContext, NodeStep, bool>? Check { get; set; }
    public Action<NodeContext, NodeStep>? Run { get; set; }
}

/// <summary>
/// Every trigger, condition and action the engine knows, by name. The loader validates rules against it,
/// RuleEngine runs from it, and the Server build exports it as NodeCatalog.json so the HTML editor's
/// palette always matches the engine actually installed.
/// </summary>
internal static class NodeRegistry
{
    private static readonly Dictionary<string, NodeDef> Triggers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, NodeDef> Conditions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, NodeDef> Actions = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(NodeDef def)
    {
        Table(def.Kind)[def.Name] = def;
    }

    public static bool TryGet(NodeKind kind, string name, out NodeDef def) => Table(kind).TryGetValue(name, out def!);

    public static IEnumerable<NodeDef> All(NodeKind kind) => Table(kind).Values.OrderBy(d => d.Name, StringComparer.Ordinal);

    private static Dictionary<string, NodeDef> Table(NodeKind kind) => kind switch
    {
        NodeKind.Trigger => Triggers,
        NodeKind.Condition => Conditions,
        _ => Actions
    };

    public static NodeArgDef Arg(string name, string type, string help, bool required = false, string def = "", params string[] choices) =>
        new() { Name = name, Type = type, Help = help, Required = required, Default = def, Choices = choices.ToList() };
}

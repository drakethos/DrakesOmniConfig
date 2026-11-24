using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace DrakesOmniConfig.Engine.Node.Schema;

/// <summary>
/// A rule chain: when <see cref="Event"/> happens (filtered by <see cref="Trigger"/> args such as
/// <c>item</c>/<c>target</c>/<c>every</c>), and every condition passes, run <see cref="Actions"/>;
/// otherwise run <see cref="Else"/>.
/// </summary>
internal sealed class NodeRule
{
    public string ModletId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Event { get; set; } = "";
    public Dictionary<string, string> Trigger { get; set; } = new();
    public List<NodeStep> Conditions { get; set; } = new();
    public List<NodeStep> Actions { get; set; } = new();
    public List<NodeStep> Else { get; set; } = new();
    public float Cooldown { get; set; }

    /// <summary>The file's own <c>enabled:</c>. Admin toggles live in <see cref="DisabledByAdmin"/> instead, so they never rewrite the author's file.</summary>
    public bool Enabled { get; set; } = true;

    public bool DisabledByAdmin { get; set; }

    [YamlIgnore]
    public string Key => ModletId + "/" + Id;
    [YamlIgnore]
    public bool Active => Enabled && !DisabledByAdmin;
}

/// <summary>What the admin panel shows for each modlet on the server (synced with the rules).</summary>
internal sealed class NodeModletInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool DisabledByAdmin { get; set; }
    public int OverrideCount { get; set; }
    public int RuleCount { get; set; }
}

/// <summary>Everything the server publishes about rules in one payload.</summary>
internal sealed class NodeFlagInfo
{
    public string Name { get; set; } = "";
    public bool On { get; set; }
    public bool Switched { get; set; }
}

internal sealed class NodeRuleSet
{
    public List<NodeModletInfo> Modlets { get; set; } = new();

    /// <summary>Phase F: the server's flags (for the admin panel; synced with the rules).</summary>
    public List<NodeFlagInfo> Flags { get; set; } = new();

    /// <summary>Phase F: one line per config binding with what happened to it on the server (admin panel).</summary>
    public List<string> ConfigBindings { get; set; } = new();
    public List<NodeRule> Rules { get; set; } = new();
}

using System.Collections.Generic;

namespace DrakesOmniConfig.Engine.Node.Schema;

/// <summary>
/// One node in a rule chain: a condition (<c>chance</c>, <c>hasItem</c>, ...) or an action (<c>give</c>,
/// <c>popup</c>, ...). Args stay plain strings so the same model loads from YAML, syncs as YAML and
/// round-trips through the HTML editor without per-node DTOs; NodeRegistry interprets them at run time.
/// </summary>
internal sealed class NodeStep
{
    public string Type { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();

    /// <summary>Labelled sub-chains, e.g. a popup's buttons. Empty for most nodes.</summary>
    public List<NodeBranch> Branches { get; set; } = new();
}

internal sealed class NodeBranch
{
    public string Label { get; set; } = "";
    public List<NodeStep> Steps { get; set; } = new();
}

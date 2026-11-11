using System.Collections.Generic;

namespace DrakesOmniConfig.Config.Modlets.Schema;

/// <summary>
/// One modlet FOLDER (not a single file): e.g. Modlets/com.author.NameBla/. Id comes from the folder
/// name, not a duplicated field inside a YAML, to avoid the two drifting apart. ModletLoader merges
/// every *.yaml in that folder (other than the reserved ones below) into one Overrides list.
/// </summary>
internal sealed class Modlet
{
    public ModletManifest Manifest { get; set; } = new();
    public List<ModletOverride> Overrides { get; set; } = new();

    /// <summary>
    /// Named values from this modlet's variables file (quick-edit, e.g. vars.yaml) — substituted for
    /// "{name}" placeholders in any of this modlet's other YAML files before FieldValueExpression parsing.
    /// </summary>
    public Dictionary<string, object?> Variables { get; set; } = new();

    /// <summary>
    /// Group name -> member ids, from this modlet's groups file (e.g. groups.yaml). Referenced by
    /// TargetSelector.GroupName. TODO(you): decide whether a shared/global groups file (outside any
    /// single modlet folder) should also exist for cross-modlet reuse — not needed to start.
    /// </summary>
    public Dictionary<string, List<string>> Groups { get; set; } = new();

    /// <summary>
    /// Reserved for Wave 2 node-based scripting (Engine.Node / Engine.DynamicPatcher) — NOT loaded or
    /// wired yet. ModletLoader should recognize a modlet's nodes/ subfolder and record paths here
    /// without parsing them, so an existing modlet folder doesn't need restructuring once Wave 2 lands.
    /// </summary>
    public List<string> NodeScriptPaths { get; set; } = new();
}

using System.Collections.Generic;

namespace DrakesOmniConfig.Config.Modlets.Schema;

/// <summary>One YAML "overrides" entry: a target plus the fields it sets on that target.</summary>
internal sealed class ModletOverride
{
    public TargetSelector Target { get; set; } = new();

    /// <summary>Field name -> expression, as parsed from YAML. Covers both reflected and virtual fields identically.</summary>
    public Dictionary<string, FieldValueExpression> Fields { get; set; } = new();

    /// <summary>
    /// Default false: if another modlet already claimed one of these fields, this entry backs off.
    /// True: this entry wins outright over a non-override claim, regardless of priority.
    /// </summary>
    public bool Override { get; set; }
}

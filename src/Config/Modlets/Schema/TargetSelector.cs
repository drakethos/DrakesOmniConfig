namespace DrakesOmniConfig.Config.Modlets.Schema;

/// <summary>When the engine applies this: baked into a prefab template once, or kept live on a known singleton.</summary>
internal enum ModletScope
{
    Prefab,
    Global,
}

/// <summary>
/// WHICH targets a selector matches, in specificity order (declared order == default tier order:
/// Item is most specific, Blanket least). ConflictResolver uses (int)Kind as the primary tie-break
/// before Priority — a narrower selector beats a broader one by default, same as the override escape
/// hatch lets a broader one beat a narrower one on request.
/// </summary>
internal enum SelectorKind
{
    Item,
    Group,
    NamePattern,
    Category,
    Blanket,
}

internal sealed class TargetSelector
{
    public string TypeName { get; set; } = "";
    public ModletScope Scope { get; set; }
    public SelectorKind Kind { get; set; } = SelectorKind.Item;

    /// <summary>Kind.Item — exact prefab/item/piece id.</summary>
    public string? ItemId { get; set; }

    /// <summary>Kind.Group — a name looked up in this modlet's Groups (Schema.Modlet.Groups).</summary>
    public string? GroupName { get; set; }

    /// <summary>Kind.NamePattern — wildcard or regex against the target's name, e.g. "bronze*".</summary>
    public string? NamePattern { get; set; }

    /// <summary>Kind.Category — a classification tag (weapon tier, piece category, etc.) — TODO(you): define the category taxonomy.</summary>
    public string? Category { get; set; }

    // Kind.Blanket needs no extra data — matches every TypeName instance.
}

using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Schema;

namespace DrakesOmniConfig.Config.Modlets.Management;

/// <summary>
/// Flattens every loaded modlet's overrides into one winner per (target, field). Agreed rule, two axes:
///
///   1. Selector specificity tier, (int)SelectorKind ascending — Item beats Group beats NamePattern
///      beats Category beats Blanket. A narrower selector wins over a broader one by default.
///   2. Within the same tier: Priority (higher wins), then load order as the final tiebreak.
///
///   Escape hatch: Override == true on an entry lets it win over a non-override claim regardless of
///   tier or Priority. Two Override == true entries still break by tier, then Priority.
///
/// Deferring to a genuinely separate (non-modlet) mod is NOT modeled here — that's handled structurally
/// by VirtualFieldAccessorBase's default low Harmony priority (see Engine.Fields.Accessors.Virtual).
/// This resolver only arbitrates between our own modlets' claims.
///
/// Runs once, server-side (or singleplayer/hosting) only — a joined client never calls this (see ModletOrchestrator).
/// </summary>
internal sealed class ConflictResolver
{
    // TODO(you): implement the rule above.
    public List<ResolvedOverride> Resolve(IReadOnlyList<Modlet> modlets)
    {
        return new List<ResolvedOverride>();
    }
}

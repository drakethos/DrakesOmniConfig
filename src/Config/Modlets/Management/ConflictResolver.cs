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
    public List<ResolvedOverride> Resolve(IReadOnlyList<Modlet> modlets)
    {
        var winners = new Dictionary<string, Claim>();

        foreach (Modlet modlet in modlets)
        {
            if (!modlet.Manifest.Enabled)
            {
                continue;
            }

            foreach (ModletOverride modletOverride in modlet.Overrides)
            {
                foreach (KeyValuePair<string, FieldValueExpression> field in modletOverride.Fields)
                {
                    string key = TargetKey(modletOverride.Target, field.Key);
                    var claim = new Claim(modletOverride.Target, field.Key, field.Value, modlet.Manifest, modletOverride.Override);

                    if (!winners.TryGetValue(key, out Claim? incumbent) || Beats(claim, incumbent))
                    {
                        winners[key] = claim;
                    }
                }
            }
        }

        var results = new List<ResolvedOverride>();
        foreach (Claim claim in winners.Values)
        {
            results.Add(new ResolvedOverride
            {
                Target = claim.Target,
                FieldName = claim.FieldName,
                Expression = claim.Expression,
                WinningModletId = claim.ModletId,
            });
        }

        return results;
    }

    // Identifies "the same thing being touched" across modlets so they can actually conflict.
    // TODO(you): Group/NamePattern/Category selectors that resolve to overlapping sets of real targets
    // (e.g. two different wildcard patterns that both happen to match "BronzeSword") aren't detected as
    // conflicting by this key — that needs real selector-matching against live targets, which is Phase C.
    private static string TargetKey(TargetSelector target, string fieldName)
    {
        string identity = target.Kind switch
        {
            SelectorKind.Item => target.ItemId ?? "",
            SelectorKind.Group => target.GroupName ?? "",
            SelectorKind.NamePattern => target.NamePattern ?? "",
            SelectorKind.Category => target.Category ?? "",
            _ => "*",
        };

        return $"{target.TypeName}|{identity}|{fieldName}";
    }

    private static bool Beats(Claim challenger, Claim incumbent)
    {
        if (challenger.Override != incumbent.Override)
        {
            return challenger.Override;
        }

        int tierCompare = ((int)challenger.Target.Kind).CompareTo((int)incumbent.Target.Kind);
        if (tierCompare != 0)
        {
            return tierCompare < 0;
        }

        if (challenger.Priority != incumbent.Priority)
        {
            return challenger.Priority > incumbent.Priority;
        }

        return false;
    }

    private sealed class Claim
    {
        public TargetSelector Target { get; }
        public string FieldName { get; }
        public FieldValueExpression Expression { get; }
        public string ModletId { get; }
        public int Priority { get; }
        public bool Override { get; }

        public Claim(TargetSelector target, string fieldName, FieldValueExpression expression, ModletManifest manifest, bool isOverride)
        {
            Target = target;
            FieldName = fieldName;
            Expression = expression;
            ModletId = manifest.Id;
            Priority = manifest.Priority;
            Override = isOverride;
        }
    }
}

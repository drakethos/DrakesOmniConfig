namespace DrakesOmniConfig.Engine.Fields.Accessors.Virtual;

/// <summary>
/// Fall damage has no backing field — it's computed inside Character.Damage(HitData) from
/// m_maxAirAltitude. TODO(you): Harmony Prefix on Character.Damage, same pattern as DrakesTestLab's
/// Character_Damage_Fall_Patch (TestRulePatches.cs) — rewrite hit.m_damage.m_damage for fall-type hits
/// using this accessor's configured multiplier, then let the original method proceed.
/// </summary>
internal sealed class FallDamageAccessor : VirtualFieldAccessorBase
{
    public override void Set(object target, object? value)
    {
    }

    public override object? Get(object target)
    {
        return null;
    }
}

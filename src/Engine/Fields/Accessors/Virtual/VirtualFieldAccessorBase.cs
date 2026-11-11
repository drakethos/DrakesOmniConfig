using DrakesOmniConfig.Engine.Fields.Accessors;

namespace DrakesOmniConfig.Engine.Fields.Accessors.Virtual;

/// <summary>
/// Base for hand-written, Harmony-backed accessors — used only for the handful of computed values that
/// have no plain backing field (e.g. fall damage). Hand-curated on purpose: this is NOT auto-generated
/// from YAML, that stays Wave 2's job (Engine.DynamicPatcher). Default Harmony patch priority is Low, so
/// a genuinely separate mod patching the same vanilla method still wins by default; a modlet's
/// override == true on this field should bump the registered priority instead.
/// </summary>
internal abstract class VirtualFieldAccessorBase : IFieldAccessor
{
    public abstract void Set(object target, object? value);
    public abstract object? Get(object target);
}

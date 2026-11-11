using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Fields.Registry;

namespace DrakesOmniConfig.Engine.Fields.Application;

/// <summary>
/// Applies one ResolvedOverride to a concrete target instance (a live singleton) or a prefab template
/// component, via whatever IFieldAccessor FieldRegistry resolves. Doesn't care whether that's reflected
/// or virtual — that distinction stops at FieldRegistry.
/// </summary>
internal sealed class FieldPatcher
{
    private readonly FieldRegistry _registry;

    public FieldPatcher(FieldRegistry registry)
    {
        _registry = registry;
    }

    // TODO(you): resolved.Target.TypeName + resolved.FieldName -> IFieldAccessor via _registry.
    // Relative operators (Add/Subtract/Multiply/Divide) need the CURRENT value first — read it with
    // accessor.Get(targetInstance), apply resolved.Expression against it (IsPercentage only matters for
    // Add/Subtract), then accessor.Set(targetInstance, result). Operator.Set just writes Expression.Operand directly.
    public void Apply(ResolvedOverride resolved, object targetInstance)
    {
    }
}

using System;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Fields.Accessors;
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

    public void Apply(ResolvedOverride resolved, object targetInstance)
    {
        IFieldAccessor accessor = _registry.Resolve(resolved.Target.TypeName, resolved.FieldName);
        FieldValueExpression expression = resolved.Expression;

        object? newValue = expression.Operator == ValueOperator.Set
            ? expression.Operand
            : ComputeRelative(accessor.Get(targetInstance), expression);

        accessor.Set(targetInstance, newValue);
    }

    // Relative operators need the field's CURRENT value first. Everything is done in double so int,
    // float, and double fields all work the same way — ReflectedFieldAccessor.Set already converts the
    // result back to whatever the real field type is.
    private static double ComputeRelative(object? current, FieldValueExpression expression)
    {
        double currentValue = ToDouble(current);
        double operand = ToDouble(expression.Operand);

        return expression.Operator switch
        {
            ValueOperator.Add => currentValue + (expression.IsPercentage ? currentValue * operand / 100.0 : operand),
            ValueOperator.Subtract => currentValue - (expression.IsPercentage ? currentValue * operand / 100.0 : operand),
            ValueOperator.Multiply => currentValue * operand,
            ValueOperator.Divide => currentValue / operand,
            _ => throw new NotSupportedException($"Unsupported relative operator '{expression.Operator}'."),
        };
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0d,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => Convert.ToDouble(value),
        };
    }
}

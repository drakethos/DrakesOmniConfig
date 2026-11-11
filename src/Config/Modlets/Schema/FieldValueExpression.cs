namespace DrakesOmniConfig.Config.Modlets.Schema;

internal enum ValueOperator
{
    Set,
    Add,
    Subtract,
    Multiply,
    Divide,
}

/// <summary>
/// A field's target value, as light math rather than a bare number — e.g. "durability: *1.5" or
/// "durability: +20%". Relative operators (Add/Subtract/Multiply/Divide) are only resolvable against
/// the field's CURRENT value, so FieldPatcher reads it via IFieldAccessor.Get() at apply time; this type
/// never holds the resolved result itself.
/// </summary>
internal sealed class FieldValueExpression
{
    public ValueOperator Operator { get; set; } = ValueOperator.Set;

    /// <summary>
    /// Numeric operand. TODO(you): ModletLoader substitutes any "{varName}" placeholder against the
    /// modlet's Variables at the raw-text level, before this is parsed — by the time it lands here it
    /// should already be a plain number.
    /// </summary>
    public object? Operand { get; set; }

    /// <summary>Only meaningful for Add/Subtract: treat Operand as a percentage of the current value (e.g. +20%) rather than a flat amount.</summary>
    public bool IsPercentage { get; set; }
}

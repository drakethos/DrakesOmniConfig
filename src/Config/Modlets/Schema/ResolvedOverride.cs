namespace DrakesOmniConfig.Config.Modlets.Schema;

/// <summary>
/// One (target, field) pair after ConflictResolver has picked a winner across every loaded modlet.
/// Expression stays unevaluated — relative operators (Add/Multiply/etc.) can only be resolved against
/// the field's live current value, which FieldPatcher reads at apply time, not here.
/// </summary>
internal sealed class ResolvedOverride
{
    public TargetSelector Target { get; set; } = new();
    public string FieldName { get; set; } = "";
    public FieldValueExpression Expression { get; set; } = new();

    /// <summary>Which modlet's Id won this field — for logging/debugging conflicts, not for re-resolving.</summary>
    public string WinningModletId { get; set; } = "";
}

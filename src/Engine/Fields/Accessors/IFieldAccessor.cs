namespace DrakesOmniConfig.Engine.Fields.Accessors;

/// <summary>
/// One field, written and read the same way regardless of whether it's a plain reflected field or a
/// hand-written Harmony-backed virtual one. A modlet author never knows which kind they're touching.
/// </summary>
internal interface IFieldAccessor
{
    void Set(object target, object? value);
    object? Get(object target);
}

using System;
using DrakesOmniConfig.Engine.Fields.Accessors;

namespace DrakesOmniConfig.Engine.Fields.Accessors.Reflected;

/// <summary>
/// The default/automatic accessor: a plain serialized field or property reached by reflection.
/// TODO(you): compile the getter/setter once in the constructor (Expression trees or
/// Delegate.CreateDelegate against the resolved FieldInfo/PropertyInfo) — never raw reflection
/// in Set/Get itself, that's the hot apply path.
/// </summary>
internal sealed class ReflectedFieldAccessor : IFieldAccessor
{
    public ReflectedFieldAccessor(Type targetType, string fieldName)
    {
    }

    public void Set(object target, object? value)
    {
    }

    public object? Get(object target)
    {
        return null;
    }
}

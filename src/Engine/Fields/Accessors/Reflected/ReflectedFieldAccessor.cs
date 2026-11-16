using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using DrakesOmniConfig.Engine.Fields.Accessors;

namespace DrakesOmniConfig.Engine.Fields.Accessors.Reflected;

/// <summary>
/// The default/automatic accessor: a plain serialized field reached by reflection. You only ever pass
/// a (Type, fieldName) pair — the field doesn't need to be known ahead of time or hardcoded anywhere.
///
/// fieldName may be a dotted path ("m_itemData.m_shared.m_name") to walk into nested fields — the
/// Hammer example needs this (ItemDrop -&gt; m_itemData -&gt; m_shared -&gt; m_name is three hops).
/// Resolution happens once, in the constructor: it walks the type chain and fails fast with a clear
/// exception (naming exactly which segment and type didn't resolve) if anything is wrong, rather than
/// failing silently later during Set/Get.
///
/// Fields only, not properties — Valheim's own data is field-based (m_-prefixed public fields), not
/// C# properties. Add PropertyInfo support later only if something genuinely needs it.
///
/// Not yet delegate-compiled (Expression trees / Delegate.CreateDelegate) for the hot path — this uses
/// plain FieldInfo.Get/SetValue each call, which is fine for proving Phase A end to end. Revisit if/when
/// this runs often enough to matter (many prefabs, every frame, etc.) — not a concern for one-shot
/// prefab-template or rare global-rebind applies.
/// </summary>
internal sealed class ReflectedFieldAccessor : IFieldAccessor
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly string _fieldPath;
    private readonly FieldInfo[] _chain;

    public ReflectedFieldAccessor(Type targetType, string fieldName)
    {
        if (targetType == null)
        {
            throw new ArgumentNullException(nameof(targetType));
        }

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        }

        _fieldPath = fieldName;
        string[] segments = fieldName.Split('.');
        _chain = new FieldInfo[segments.Length];

        Type currentType = targetType;
        for (int i = 0; i < segments.Length; i++)
        {
            FieldInfo? field = currentType.GetField(segments[i], Flags);
            if (field == null)
            {
                
                throw new MissingFieldException(
                    $"No field '{segments[i]}' on '{currentType.FullName}' while resolving '{fieldName}' on '{targetType.FullName}'.");
            }

            _chain[i] = field;
            currentType = field.FieldType;
        }
    }

    // Struct-safe: a value-type field in the middle of the chain (e.g. ItemDrop.SharedData.m_damages,
    // a DamageTypes struct) returns a BOXED COPY from GetValue — mutating that copy and stopping there
    // silently loses the write, since nothing connects the copy back to the real field it came from.
    // Walk forward recording every hop's value, set the target field, then walk backward writing each
    // boxed copy back into its own parent, stopping as soon as a parent turns out to be a reference
    // type (its mutation already persisted live, nothing above it needs rewriting).
    public void Set(object target, object? value)
    {
        var values = new object?[_chain.Length + 1];
        values[0] = target;
        for (int i = 0; i < _chain.Length; i++)
        {
            object? next = values[i];
            if (next == null)
            {
                throw new InvalidOperationException(
                    $"'{(i == 0 ? "target" : _chain[i - 1].Name)}' was null while resolving '{_fieldPath}' for Set.");
            }

            values[i + 1] = _chain[i].GetValue(next);
        }

        FieldInfo lastField = _chain[_chain.Length - 1];
        object? parent = values[_chain.Length - 1];
        lastField.SetValue(parent, ConvertValue(value, lastField.FieldType));
        values[_chain.Length] = lastField.GetValue(parent);

        for (int i = _chain.Length - 1; i >= 1; i--)
        {
            if (!_chain[i - 1].FieldType.IsValueType)
            {
                break;
            }

            _chain[i - 1].SetValue(values[i - 1], values[i]);
        }
    }

    public object? Get(object target)
    {
        object? current = target;
        foreach (FieldInfo field in _chain)
        {
            if (current == null)
            {
                return null;
            }

            current = field.GetValue(current);
        }

        return current;
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
        {
            return null;
        }

        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        if (underlying.IsEnum)
        {
            return Enum.Parse(underlying, value.ToString()!, ignoreCase: true);
        }

        return Convert.ChangeType(value, underlying);
    }
}

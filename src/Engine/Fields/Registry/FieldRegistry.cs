using System;
using System.Collections.Generic;
using System.Reflection;
using DrakesOmniConfig.Engine.Fields.Accessors;
using DrakesOmniConfig.Engine.Fields.Accessors.Reflected;

namespace DrakesOmniConfig.Engine.Fields.Registry;

/// <summary>
/// Resolves (TypeName, FieldName) -> IFieldAccessor. Reflected is the automatic fallback; Virtual
/// accessors are registered explicitly, one at a time, for the handful of fields that need one
/// (see Accessors.Virtual). A modlet's YAML never says which kind it's touching — this is the only
/// place that decides.
/// </summary>
internal sealed class FieldRegistry
{
    private readonly Dictionary<string, IFieldAccessor> _virtual = new();
    private readonly Dictionary<string, IFieldAccessor> _reflectedCache = new();

    public void RegisterVirtual(string typeName, string fieldName, IFieldAccessor accessor)
    {
        _virtual[Key(typeName, fieldName)] = accessor;
    }

    public IFieldAccessor Resolve(string typeName, string fieldName)
    {
        string key = Key(typeName, fieldName);

        if (_virtual.TryGetValue(key, out IFieldAccessor? virtualAccessor))
        {
            return virtualAccessor;
        }

        if (_reflectedCache.TryGetValue(key, out IFieldAccessor? cached))
        {
            return cached;
        }

        Type targetType = ResolveType(typeName)
            ?? throw new TypeLoadException(
                $"Could not find type '{typeName}' in any loaded assembly (resolving field '{fieldName}').");

        var accessor = new ReflectedFieldAccessor(targetType, fieldName);
        _reflectedCache[key] = accessor;
        return accessor;
    }

    private static string Key(string typeName, string fieldName) => $"{typeName}.{fieldName}";

    // Plain Type.GetType only looks in mscorlib + the calling assembly, which never finds Valheim's own
    // types (ItemDrop, Humanoid, etc. live in assembly_valheim). Search every already-loaded assembly
    // instead — by the time a BepInEx plugin's Awake() runs, the game's own assemblies are loaded.
    private static Type? ResolveType(string typeName)
    {
        Type? direct = Type.GetType(typeName);
        if (direct != null)
        {
            return direct;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? found = assembly.GetType(typeName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}

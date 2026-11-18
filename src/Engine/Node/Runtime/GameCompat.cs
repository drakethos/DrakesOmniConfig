using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DrakesOmniConfig.Engine.Node.Runtime;

/// <summary>
/// Calls a game method whose signature changed between the stub DLLs CI compiles against and the live game
/// (Inventory.AddItem, SEMan.AddStatusEffect, Character.Message all gained trailing optional parameters in
/// 1.0). A direct call binds to the stub's shorter signature, which doesn't exist live and throws
/// MissingMethodException in game; this finds whichever overload starts with the given argument types and
/// fills the rest from their declared defaults.
/// </summary>
internal static class GameCompat
{
    private static readonly Dictionary<string, MethodInfo> Cache = new();

    public static object? Call(object instance, string name, params object?[] leading)
    {
        Type type = instance.GetType();
        string key = type.FullName + "." + name + "/" + string.Join(",", leading.Select(a => a?.GetType().Name ?? "null"));
        if (!Cache.TryGetValue(key, out MethodInfo? method))
        {
            method = Find(type, name, leading) ?? throw new MissingMethodException(type.Name, name);
            Cache[key] = method;
        }

        ParameterInfo[] ps = method.GetParameters();
        var args = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            args[i] = i < leading.Length ? leading[i] : Default(ps[i]);
        }

        return method.Invoke(instance, args);
    }

    private static MethodInfo? Find(Type type, string name, object?[] leading)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == name)
            .Where(m =>
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < leading.Length || ps.Skip(leading.Length).Any(p => !p.IsOptional))
                {
                    return false;
                }

                for (int i = 0; i < leading.Length; i++)
                {
                    Type pt = ps[i].ParameterType;
                    if (leading[i] == null ? pt.IsValueType : !pt.IsInstanceOfType(leading[i]))
                    {
                        return false;
                    }
                }

                return true;
            })
            // the most-derived declaration first (Player.Message over Character.Message), then the fewest extra params
            .OrderByDescending(m => Depth(m.DeclaringType))
            .ThenBy(m => m.GetParameters().Length)
            .FirstOrDefault();
    }

    private static int Depth(Type? t)
    {
        int d = 0;
        for (; t != null; t = t.BaseType)
        {
            d++;
        }

        return d;
    }

    private static object? Default(ParameterInfo p)
    {
        if (p.HasDefaultValue && p.DefaultValue != DBNull.Value)
        {
            return p.DefaultValue;
        }

        return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
    }
}

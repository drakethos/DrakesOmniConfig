using System;
using System.Collections.Generic;
using System.Globalization;
using DrakesOmniConfig.Engine.Fields.Accessors.Reflected;
using UnityEngine;

namespace DrakesOmniConfig.Tools.TestHooks;

/// <summary>
/// DrakesTestLab finds this by the attribute's NAME alone (reflection, no assembly reference either way —
/// see TestHooks.cs's own doc comment in DrakesTestLab), so this is a separate copy, not a shared type.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestHookAttribute : Attribute
{
    public TestHookAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Mod-specific test hook: lets a DrakesTestLab test JSON read any field a modlet could target, straight off
/// the real prefab (the same GameObject PrefabPassHook edits), without TestLab needing to know anything about
/// Harmony, reflection chains, or this mod's schema. One hook covers every override this mod can ever write —
/// a generic reflected-field reader, not a per-field check.
/// Usage from a test's "check": { "check": "hook", "name": "omniconfig.field",
/// "args": { "prefab": "Cart", "component": "Vagon", "field": "m_breakForce" }, "expect": "4000" }
/// "live": true reads off the local Player singleton instead of a prefab template — needed for
/// Scope.Global overrides (GlobalRebindHook patches the live instance, not a prefab), and more direct
/// than TestLab's own "stat" check for a field like m_baseHP: GetMaxHealth() layers food bonuses AND,
/// for an existing saved test character, the save's own stored max/current HP on top, so it can read
/// back a value that has nothing to do with whether this mod's override actually landed on the field.
/// </summary>
internal static class OmniConfigTestHooks
{
    [TestHook("omniconfig.field")]
    internal static object? FieldValue(Dictionary<string, object?> args)
    {
        string componentName = RequireArg(args, "component");
        string fieldPath = RequireArg(args, "field");
        bool live = args.TryGetValue("live", out object? liveArg) && liveArg is bool b && b;

        GameObject prefab;
        if (live)
        {
            Player? player = Player.m_localPlayer;
            if (player == null)
            {
                throw new InvalidOperationException("No local player (use 'live': true only once a player exists).");
            }

            prefab = player.gameObject;
        }
        else
        {
            string prefabName = RequireArg(args, "prefab");
            if (ZNetScene.instance == null)
            {
                throw new InvalidOperationException("ZNetScene is not ready yet.");
            }

            prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Prefab '{prefabName}' not found.");
            }
        }

        Component component = prefab.GetComponent(componentName);
        if (component == null)
        {
            throw new InvalidOperationException($"'{prefab.name}' has no component '{componentName}'.");
        }

        var accessor = new ReflectedFieldAccessor(component.GetType(), fieldPath);
        return accessor.Get(component);
    }

    private static string RequireArg(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out object? value) || value == null)
        {
            throw new ArgumentException($"omniconfig.field needs '{key}'.");
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }
}

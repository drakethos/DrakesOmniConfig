using System.Collections.Generic;
using System.Text.RegularExpressions;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Fields.Application;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Hooks;

/// <summary>
/// One of the engine's fixed Harmony hook points — modlets never declare their own.
///
/// Prefab templates (GameObjects with components like ItemDrop) don't exist yet when the plugin's own
/// Awake() runs — they're only registered once ZNetScene.Awake() finishes building its prefab list.
/// So Scope.Prefab overrides can't be applied synchronously from ModletOrchestrator.Bootstrap(); they
/// have to wait for that moment. This class bridges that gap: Prepare() stashes what to apply (called
/// from Bootstrap, any time), and the Harmony Postfix below actually applies it, whenever ZNetScene.Awake
/// really fires. Every instance spawned afterward inherits the edited template for free — no per-instance patch.
/// </summary>
internal static class PrefabPassHook
{
    private static IEnumerable<ResolvedOverride>? _pendingOverrides;
    private static FieldPatcher? _pendingPatcher;

    /// <summary>Call once from ModletOrchestrator.ApplyAll() with the Scope.Prefab subset of ResolvedOverrides.</summary>
    public static void Prepare(IEnumerable<ResolvedOverride> overrides, FieldPatcher patcher)
    {
        _pendingOverrides = overrides;
        _pendingPatcher = patcher;
    }

    // String literal, not nameof(ZNetScene.Awake) — Unity lifecycle methods are typically private, and
    // nameof would fail to compile against an inaccessible member. Harmony resolves the string via
    // reflection at runtime instead, so visibility doesn't matter.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    private static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance)
        {
            Apply(__instance);
        }
    }

    private static void Apply(ZNetScene zNetScene)
    {
        if (_pendingOverrides == null || _pendingPatcher == null)
        {
            return;
        }

        foreach (ResolvedOverride resolved in _pendingOverrides)
        {
            if (resolved.Target.Scope != ModletScope.Prefab)
            {
                continue;
            }

            switch (resolved.Target.Kind)
            {
                case SelectorKind.Item:
                    ApplyToItem(zNetScene, resolved);
                    break;
                case SelectorKind.NamePattern:
                    ApplyToNamePattern(zNetScene, resolved);
                    break;
                default:
                    // Group/Category selector matching isn't wired yet — Phase C.
                    ModLog.Warning(
                        $"Skipping '{resolved.FieldName}': SelectorKind.{resolved.Target.Kind} prefab matching isn't implemented yet.");
                    break;
            }
        }
    }

    private static void ApplyToItem(ZNetScene zNetScene, ResolvedOverride resolved)
    {
        if (string.IsNullOrEmpty(resolved.Target.ItemId))
        {
            ModLog.Warning($"Skipping '{resolved.FieldName}': SelectorKind.Item needs an ItemId.");
            return;
        }

        GameObject prefab = zNetScene.GetPrefab(resolved.Target.ItemId);
        if (prefab == null)
        {
            ModLog.Warning($"Prefab '{resolved.Target.ItemId}' not found (override '{resolved.FieldName}').");
            return;
        }

        ApplyToPrefab(prefab, resolved);
    }

    // Glob syntax ("*" = any run of characters), not full regex — matches how the schema doc describes
    // it ("bronze*" etc). zNetScene.m_prefabs is every registered prefab GameObject; this walks all of
    // them once, per override, at the one point they're all guaranteed to exist.
    private static void ApplyToNamePattern(ZNetScene zNetScene, ResolvedOverride resolved)
    {
        if (string.IsNullOrEmpty(resolved.Target.NamePattern))
        {
            ModLog.Warning($"Skipping '{resolved.FieldName}': SelectorKind.NamePattern needs a NamePattern.");
            return;
        }

        Regex regex = GlobToRegex(resolved.Target.NamePattern!);
        int matchCount = 0;
        foreach (GameObject prefab in zNetScene.m_prefabs)
        {
            if (prefab != null && regex.IsMatch(prefab.name))
            {
                ApplyToPrefab(prefab, resolved);
                matchCount++;
            }
        }

        ModLog.Debug($"NamePattern '{resolved.Target.NamePattern}' matched {matchCount} prefab(s) for '{resolved.FieldName}'.");
    }

    private static void ApplyToPrefab(GameObject prefab, ResolvedOverride resolved)
    {
        Component component = prefab.GetComponent(resolved.Target.TypeName);
        if (component == null)
        {
            // Expected for a broad pattern matching prefabs that don't have this component — Debug, not
            // Warning, so a wide NamePattern doesn't spam the log.
            ModLog.Debug(
                $"Prefab '{prefab.name}' has no component '{resolved.Target.TypeName}' (override '{resolved.FieldName}').");
            return;
        }

        // One bad/missing field must not abort every override still queued in this pass — without this,
        // a single throw here silently kills the rest of ApplyAll's prefab batch with no log line at all.
        try
        {
            _pendingPatcher!.Apply(resolved, component);
        }
        catch (System.Exception ex)
        {
            ModLog.Error(
                $"Override '{resolved.FieldName}' on prefab '{prefab.name}' ({resolved.Target.TypeName}) threw: {ex}");
        }
    }

    private static Regex GlobToRegex(string pattern)
    {
        string escaped = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase);
    }
}

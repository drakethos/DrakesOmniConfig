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

    /// <summary>Runs the prefab pass against the current world now (a joined client that received the server's set late).</summary>
    public static void ApplyNow()
    {
        if (ZNetScene.instance != null)
        {
            // Relative edits (*1.5, +20%) compound if a template is edited twice, so a second pass in the same world
            // only rebuilds clones (fresh copies, so their edits start over) and looks.
            Apply(ZNetScene.instance, clonesOnly: _appliedTo == ZNetScene.instance);
        }
    }

    private static ZNetScene? _appliedTo;

    /// <summary>The last world pass: how many clones it handled and how many of those were the main-menu copies. For tests.</summary>
    internal static int LastClones { get; private set; }

    internal static int LastReused { get; private set; }

    private static void Apply(ZNetScene zNetScene) => Apply(zNetScene, clonesOnly: false);

    private static void Apply(ZNetScene zNetScene, bool clonesOnly)
    {
        _appliedTo = zNetScene;
        if (_pendingOverrides == null || _pendingPatcher == null)
        {
            return;
        }

        // a prebuild still running at the menu stops here; what it finished is reused below
        MenuPrebuild.Stop();
        PrefabCloner.BeginPass();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int reused = 0;
        // Clones first: every other override may target a prefab that only exists once its clone is made.
        // (One summary line: a line per clone was a measurable part of a 400-clone load.)
        int clones = 0;
        double afterClones = 0;
        PrefabCloner.Deferred++;
        try
        {
            // a copy made at the main menu (MenuPrebuild) from the same definition is only registered, not made again
            Dictionary<string, List<ResolvedOverride>> byItem = ByItem(_pendingOverrides);
            foreach (ResolvedOverride resolved in _pendingOverrides)
            {
                if (resolved.Target.Scope == ModletScope.Prefab && resolved.FieldName == Config.Modlets.Loading.ModletLoader.CloneOfField)
                {
                    clones++;
                    string id = resolved.Target.ItemId ?? "";
                    string from = System.Convert.ToString(resolved.Expression.Operand, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    if (PrefabCloner.TryReuse(zNetScene, id, CloneKey(id, from, byItem)))
                    {
                        reused++;
                    }
                    else
                    {
                        PrefabCloner.Create(zNetScene, id, from);
                    }
                }
            }

            LastClones = clones;
            LastReused = reused;
            if (clones > 0)
            {
                ModLog.Info($"Clones: {clones} ({reused} prebuilt at the main menu) in {clock.Elapsed.TotalMilliseconds:0} ms (details at Debug log level).");
            }

            afterClones = clock.Elapsed.TotalMilliseconds;

            foreach (ResolvedOverride resolved in _pendingOverrides)
            {
                if (resolved.Target.Scope == ModletScope.Prefab && resolved.FieldName == Config.Modlets.Loading.ModletLoader.RecipeField)
                {
                    PrefabCloner.SetRecipe(resolved.Target.ItemId ?? "", System.Convert.ToString(resolved.Expression.Operand, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                }
            }
        }
        finally
        {
            PrefabCloner.Deferred--;
        }

        PrefabCloner.RegisterAll();

        foreach (ResolvedOverride resolved in _pendingOverrides)
        {
            if (resolved.Target.Scope != ModletScope.Prefab || resolved.FieldName.StartsWith("@", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (clonesOnly && !PrefabCloner.IsClone(resolved.Target.ItemId))
            {
                continue;
            }

            // already applied to the menu copy of this clone (applying again would double a relative edit)
            if (resolved.Target.Kind == SelectorKind.Item && PrefabCloner.WasAppliedAtPrebuild(resolved.Target.ItemId, OverrideKey(resolved)))
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

        double afterFields = clock.Elapsed.TotalMilliseconds;
        // looks may reskin a clone made above, so they come last; unchanged looks built at the menu stay as they are,
        // unless a clone was made fresh here (its look must be built on the new copy)
        Looks.LookBridge.ApplyNow(allowSkip: !PrefabCloner.AnyRecreated);
        PrefabCloner.EndPass();
        double end = clock.Elapsed.TotalMilliseconds;
        ModLog.Info($"Prefab pass timing (ms): clones {afterClones:0}, recipes+fields {afterFields - afterClones:0}, looks {end - afterFields:0}; total {end:0}.");
    }

    // ---------------------------------------------------------------- fingerprints shared with MenuPrebuild

    /// <summary>One override as text: what it sets and how. Equal text = the same edit.</summary>
    internal static string OverrideKey(ResolvedOverride r) =>
        string.Join("|", r.Target.TypeName, r.FieldName, r.Expression.Operator.ToString(),
            System.Convert.ToString(r.Expression.Operand, System.Globalization.CultureInfo.InvariantCulture) ?? "", r.Expression.IsPercentage ? "%" : "");

    /// <summary>A clone's definition: its source and every by-id override aimed at it, in order.</summary>
    internal static string CloneKey(string id, string from, Dictionary<string, List<ResolvedOverride>> byItem)
    {
        var sb = new System.Text.StringBuilder(from);
        if (byItem.TryGetValue(id, out List<ResolvedOverride>? list))
        {
            foreach (ResolvedOverride r in list)
            {
                sb.Append('\n').Append(OverrideKey(r));
            }
        }

        return sb.ToString();
    }

    /// <summary>Prefab-scope, by-id overrides grouped by target id (no "@" pseudo-fields).</summary>
    internal static Dictionary<string, List<ResolvedOverride>> ByItem(IEnumerable<ResolvedOverride> overrides)
    {
        var byItem = new Dictionary<string, List<ResolvedOverride>>();
        foreach (ResolvedOverride r in overrides)
        {
            if (r.Target.Scope != ModletScope.Prefab || r.Target.Kind != SelectorKind.Item || string.IsNullOrEmpty(r.Target.ItemId) ||
                r.FieldName.StartsWith("@", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (!byItem.TryGetValue(r.Target.ItemId!, out List<ResolvedOverride>? list))
            {
                byItem[r.Target.ItemId!] = list = new List<ResolvedOverride>();
            }

            list.Add(r);
        }

        return byItem;
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

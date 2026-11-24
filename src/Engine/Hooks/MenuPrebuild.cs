using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Orchestration;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Hooks;

/// <summary>
/// Builds item clones and looks at the main menu, a few milliseconds per frame, so entering a world only registers them.
/// <list type="bullet">
/// <item>Source: the set this side expects (the modlets read in the background at start-up, or the last world's set).</item>
/// <item>Each copy carries a fingerprint of its definition (source + every override aimed at it). At world entry
/// <see cref="PrefabPassHook"/> reuses a copy only if the fingerprint still matches (a joined server with other modlets,
/// an edit) and makes the rest the normal way. Nothing is stored on disk: there is no cache to go stale or to clear.</item>
/// <item>Only number and true/false edits are applied here (they need nothing from the world); edits that name another
/// object are applied at world entry, as before.</item>
/// <item>Items only (the menu has the item list, not the world's other prefabs). A headless server has no menu and skips this.</item>
/// <item>A world that starts before it finishes stops it; what it finished is used.</item>
/// </list>
/// </summary>
internal static class MenuPrebuild
{
    internal static MonoBehaviour? Runner;
    private static Coroutine? _running;

    /// <summary>The last prebuild's outcome, for tests: "clones N, looks M, X ms over F frame(s)", "stopped", or "".</summary>
    internal static string LastSummary { get; private set; } = "";

    private const double FrameBudgetMs = 4;

    [HarmonyPatch(typeof(FejdStartup), "Start")]
    private static class FejdStartup_Start_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(FejdStartup), "Start") != null;

        private static void Postfix() => Begin();
    }

    private static void Begin()
    {
        if (Runner == null || Looks.LookBridge.Headless)
        {
            return;
        }

        Stop();
        _running = Runner.StartCoroutine(Run());
    }

    internal static void Stop()
    {
        if (_running != null && Runner != null)
        {
            Runner.StopCoroutine(_running);
            LastSummary = "stopped: " + LastSummary;
        }

        _running = null;
    }

    private static IEnumerator Run()
    {
        // wait (at most 30 s) for the modlets, the game's item list, and Forge if looks are on
        float deadline = Time.realtimeSinceStartup + 30f;
        (IReadOnlyList<ResolvedOverride> Resolved, Dictionary<string, Dictionary<string, string>> Looks, Fields.Application.FieldPatcher Patcher)? source = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            source = ModletOrchestrator.PrebuildSource();
            if (source != null && ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0 &&
                (!Looks.LookBridge.Available || Looks.LookBridge.Ready))
            {
                break;
            }

            source = null;
            yield return null;
        }

        if (source == null)
        {
            _running = null;
            yield break;
        }

        var total = Stopwatch.StartNew();
        var frame = Stopwatch.StartNew();
        int frames = 1, made = 0, edits = 0, fresh = 0;
        IReadOnlyList<ResolvedOverride> resolved = source.Value.Resolved;
        Dictionary<string, List<ResolvedOverride>> byItem = PrefabPassHook.ByItem(resolved);
        LastSummary = "running";
        foreach (ResolvedOverride clone in resolved)
        {
            if (clone.Target.Scope != ModletScope.Prefab || clone.FieldName != ModletLoader.CloneOfField || string.IsNullOrEmpty(clone.Target.ItemId))
            {
                continue;
            }

            string id = clone.Target.ItemId!;
            string from = System.Convert.ToString(clone.Expression.Operand, CultureInfo.InvariantCulture) ?? "";
            GameObject? item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(from) : null;
            if (item == null)
            {
                continue; // not an item (or not in this game version): built at world entry
            }

            GameObject? before = PrefabCloner.TryGet(id);
            GameObject copy = PrefabCloner.Prebuild(id, item, PrefabPassHook.CloneKey(id, from, byItem));
            if (copy != before)
            {
                fresh++;
            }

            if (byItem.TryGetValue(id, out List<ResolvedOverride>? overrides))
            {
                foreach (ResolvedOverride edit in overrides)
                {
                    if (!NeedsNothingFromWorld(edit) || copy.GetComponent(edit.Target.TypeName) is not { } component)
                    {
                        continue;
                    }

                    try
                    {
                        source.Value.Patcher.Apply(edit, component);
                        PrefabCloner.MarkApplied(id, PrefabPassHook.OverrideKey(edit));
                        edits++;
                    }
                    catch (System.Exception ex)
                    {
                        ModLog.Debug($"Menu prebuild: '{id}.{edit.FieldName}' left for world entry ({ex.GetType().Name}).");
                    }
                }
            }

            made++;
            if (frame.Elapsed.TotalMilliseconds > FrameBudgetMs)
            {
                LastSummary = $"running: {made} clone(s)";
                yield return null;
                frames++;
                frame.Restart();
            }
        }

        // looks last: they may reskin the copies above (Forge finds them through PrefabCloner.TryGet)
        int looks = 0;
        double clonesMs = total.Elapsed.TotalMilliseconds, looksMs = 0;
        if (Looks.LookBridge.Available && source.Value.Looks.Count > 0)
        {
            yield return null;
            frames++;
            var looksClock = Stopwatch.StartNew();
            // fresh copies replaced the ones earlier looks were built on (menu opened again): build those looks again
            Looks.LookBridge.Prebuild(source.Value.Looks, clonesReplaced: fresh > 0);
            looksMs = looksClock.Elapsed.TotalMilliseconds;
            foreach (KeyValuePair<string, Dictionary<string, string>> m in source.Value.Looks)
            {
                looks += m.Value.Count;
            }
        }

        LastSummary = $"clones {made}, edits {edits}, looks {looks}; {total.Elapsed.TotalMilliseconds:0} ms over {frames} frame(s), looks frame {looksMs:0} ms (clones phase {clonesMs:0} ms wall time)";
        ModLog.Info($"Menu prebuild: {LastSummary}.");
        _running = null;
    }

    /// <summary>A number or true/false: applies the same at the menu as in a world. A name may point at a world object.</summary>
    private static bool NeedsNothingFromWorld(ResolvedOverride edit)
    {
        object? operand = edit.Expression.Operand;
        if (operand is bool || operand is double || operand is float || operand is int || operand is long || operand is decimal)
        {
            return true;
        }

        string text = System.Convert.ToString(operand, CultureInfo.InvariantCulture) ?? "";
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || bool.TryParse(text, out _);
    }
}

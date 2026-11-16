using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Fields.Application;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Hooks;

/// <summary>
/// The engine's other fixed hook point — same two-halves shape as PrefabPassHook, for the same reason:
/// the singleton this targets (the local Player) doesn't exist yet when Prepare() is called from
/// ModletOrchestrator.ApplyAll(). Needed on top of PrefabPassHook because a live singleton can be
/// recreated (death/respawn, world rejoin) — a prefab-template edit alone doesn't cover that; this
/// re-applies every time the singleton comes back.
/// </summary>
internal static class GlobalRebindHook
{
    private static IEnumerable<ResolvedOverride>? _pendingOverrides;
    private static FieldPatcher? _pendingPatcher;

    /// <summary>Call once from ModletOrchestrator.ApplyAll() with the Scope.Global subset of ResolvedOverrides.</summary>
    public static void Prepare(IEnumerable<ResolvedOverride> overrides, FieldPatcher patcher)
    {
        _pendingOverrides = overrides;
        _pendingPatcher = patcher;
    }

    [HarmonyPatch(typeof(Player), "Awake")]
    private static class Player_Awake_Patch
    {
        private static void Postfix(Player __instance)
        {
            Apply(__instance);
        }
    }

    private static void Apply(Player player)
    {
        if (_pendingOverrides == null || _pendingPatcher == null)
        {
            return;
        }

        foreach (ResolvedOverride resolved in _pendingOverrides)
        {
            if (resolved.Target.Scope != ModletScope.Global)
            {
                continue;
            }

            // "Player" itself resolves here too — GetComponent(string) finds a component on its own
            // GameObject by simple type name, Player included, same as any other attached component.
            Component component = player.GetComponent(resolved.Target.TypeName);
            if (component == null)
            {
                ModLog.Warning(
                    $"Player has no component '{resolved.Target.TypeName}' (override '{resolved.FieldName}').");
                continue;
            }

            // Same reasoning as PrefabPassHook: don't let one bad field silently abort every override
            // still queued for this Player (re-)Awake pass.
            try
            {
                _pendingPatcher.Apply(resolved, component);
            }
            catch (System.Exception ex)
            {
                ModLog.Error(
                    $"Override '{resolved.FieldName}' on Player ({resolved.Target.TypeName}) threw: {ex}");
            }
        }
    }
}

using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Fields.Application;

namespace DrakesOmniConfig.Engine.Hooks;

/// <summary>
/// The engine's other fixed hook point. TODO(you): Harmony Postfix on Player.Awake (or the relevant
/// respawn point) — re-applies every Scope.Global override whenever the singleton (re)spawns. Needed
/// because a live singleton can be recreated (death/respawn, world rejoin); a prefab-template edit
/// doesn't cover that.
/// </summary>
internal static class GlobalRebindHook
{
    public static void Apply(IEnumerable<ResolvedOverride> overrides, FieldPatcher patcher)
    {
    }
}

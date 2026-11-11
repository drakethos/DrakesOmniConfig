using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Fields.Application;

namespace DrakesOmniConfig.Engine.Hooks;

/// <summary>
/// One of the engine's fixed Harmony hook points — modlets never declare their own. TODO(you): Harmony
/// Postfix on whichever vanilla method actually fires once prefab templates exist (e.g. ZNetScene.Awake
/// or ObjectDB.CopyOtherDB — confirm which). Bakes every Scope.Prefab override into its template's
/// component ONCE; every instance spawned afterward inherits it for free, no per-instance patch.
/// </summary>
internal static class PrefabPassHook
{
    public static void Apply(IEnumerable<ResolvedOverride> overrides, FieldPatcher patcher)
    {
    }
}

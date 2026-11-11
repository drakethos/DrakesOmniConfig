using DrakesOmniConfig.Config.Modlets.Management;
using DrakesOmniConfig.Engine.Fields.Application;
using DrakesOmniConfig.Sync.Authority;

namespace DrakesOmniConfig.Engine.Orchestration;

/// <summary>
/// Top-level coordinator, constructed and called from the plugin's Awake(). Owns the server-vs-client
/// branch so every other class stays agnostic of which side it's running on.
/// </summary>
internal sealed class ModletOrchestrator
{
    private readonly ModletManager _modletManager;
    private readonly FieldPatcher _fieldPatcher;
    private readonly ModletAuthority _authority;

    public ModletOrchestrator(ModletManager modletManager, FieldPatcher fieldPatcher, ModletAuthority authority)
    {
        _modletManager = modletManager;
        _fieldPatcher = fieldPatcher;
        _authority = authority;
    }

    // TODO(you): branch on ZNet.instance.IsServer():
    //   - true (dedicated server, or singleplayer/hosting): _modletManager.LoadAndResolve(folder), then
    //     _authority.PublishAll(_modletManager.ResolvedOverrides).
    //   - false (joined client): subscribe _authority.ResolvedReceived -> _modletManager.AdoptResolved;
    //     never call LoadAndResolve on this side.
    // Either path finishes by calling ApplyAll() once the resolved set is known.
    public void Bootstrap()
    {
    }

    // TODO(you): walk _modletManager.ResolvedOverrides, split by Target.Scope, hand Scope.Prefab entries
    // to Hooks.PrefabPassHook and Scope.Global entries to Hooks.GlobalRebindHook (both take _fieldPatcher).
    public void ApplyAll()
    {
    }
}

using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Management;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Fields.Application;
using DrakesOmniConfig.Engine.Hooks;
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

    // modletsFolder comes from MainConfig.ModletsFolder (BepInEx/config/DrakesOmniConfig/Modlets by
    // default) — no more hardcoded dev path. Skips the ZNet.instance.IsServer() branch entirely (Phase
    // D) — just loads, resolves, and applies unconditionally so the pipe proves out end to end.
    //
    // TODO(you): Phase D — branch on ZNet.instance.IsServer():
    //   - true (dedicated server, or singleplayer/hosting): _modletManager.LoadAndResolve(folder), then
    //     _authority.PublishAll(_modletManager.ResolvedOverrides).
    //   - false (joined client): subscribe _authority.ResolvedReceived -> _modletManager.AdoptResolved;
    //     never call LoadAndResolve on this side.
    public void Bootstrap(string modletsFolder)
    {
        ModLog.Info($"Bootstrap: loading modlets from '{modletsFolder}'.");
        _modletManager.LoadAndResolve(modletsFolder);
        ApplyAll();
    }

    public void ApplyAll()
    {
        var prefabOverrides = new List<ResolvedOverride>();
        var globalOverrides = new List<ResolvedOverride>();

        foreach (ResolvedOverride resolved in _modletManager.ResolvedOverrides)
        {
            if (resolved.Target.Scope == ModletScope.Prefab)
            {
                prefabOverrides.Add(resolved);
            }
            else
            {
                globalOverrides.Add(resolved);
            }
        }

        ModLog.Info($"ApplyAll: {prefabOverrides.Count} prefab-scope, {globalOverrides.Count} global-scope override(s) resolved.");
        foreach (ResolvedOverride resolved in _modletManager.ResolvedOverrides)
        {
            ModLog.Debug(
                $"  {resolved.Target.TypeName}/{resolved.Target.ItemId}.{resolved.FieldName} -> won by '{resolved.WinningModletId}'");
        }

        PrefabPassHook.Prepare(prefabOverrides, _fieldPatcher);
        GlobalRebindHook.Prepare(globalOverrides, _fieldPatcher);
    }
}

using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Management;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Fields.Application;
using DrakesOmniConfig.Engine.Hooks;
using DrakesOmniConfig.Sync.Authority;
using HarmonyLib;

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
    private string _modletsFolder = "";

    // Same two-halves shape as PrefabPassHook/GlobalRebindHook: Bootstrap() only runs at plugin Awake()
    // time, before ZNet.instance exists, so the real IsServer() branch has to wait for ZNet's own Awake.
    private static ModletOrchestrator? _current;

    public ModletOrchestrator(ModletManager modletManager, FieldPatcher fieldPatcher, ModletAuthority authority)
    {
        _modletManager = modletManager;
        _fieldPatcher = fieldPatcher;
        _authority = authority;
        _authority.ResolvedReceived += OnResolvedReceived;
    }

    // modletsFolder comes from MainConfig.ModletsFolder (BepInEx/config/DrakesOmniConfig/Modlets by
    // default). Only remembers it here — ZNet.instance doesn't exist yet at plugin Awake() time, so the
    // actual IsServer() branch happens in OnZNetAwake below.
    public void Bootstrap(string modletsFolder)
    {
        _modletsFolder = modletsFolder;
        _current = this;
    }

    [HarmonyPatch(typeof(ZNet), "Awake")]
    private static class ZNet_Awake_Patch
    {
        private static void Postfix()
        {
            _current?.OnZNetAwake();
        }
    }

    // Re-runs on every ZNet.Awake (world load/rejoin), same as PrefabPassHook/GlobalRebindHook re-apply
    // on every relevant lifecycle event rather than latching "done once" — cheap, and picks up modlet
    // file edits made between sessions.
    private void OnZNetAwake()
    {
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            ModLog.Info($"Bootstrap: authoritative side (server or hosting) — loading modlets from '{_modletsFolder}'.");
            _modletManager.LoadAndResolve(_modletsFolder);
            ApplyAll();
            _authority.PublishAll(_modletManager.ResolvedOverrides);
        }
        else
        {
            ModLog.Info("Bootstrap: joined-client side — waiting for the server's resolved overrides.");
        }
    }

    private void OnResolvedReceived(IReadOnlyList<ResolvedOverride> resolved)
    {
        ModLog.Info($"Received {resolved.Count} resolved override(s) from the server.");
        _modletManager.AdoptResolved(resolved);
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

using System;
using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Node.Schema;
using DrakesOmniConfig.Sync.Transport;
using ServerSync;

namespace DrakesOmniConfig.Sync.Authority;

/// <summary>
/// Branches on ZNet.instance.IsServer() (true for a dedicated server AND a singleplayer/hosting player) —
/// see ModletOrchestrator for where that branch actually lives. Authoritative side: publishes the
/// already-resolved override set through one shared ModletChannel. Joined-client side: listens for that
/// channel's received payload and raises ResolvedReceived so ModletOrchestrator can hand it straight to
/// ModletManager.AdoptResolved — this side never re-runs ConflictResolver itself.
///
/// The dedicated server runs DrakesOmniConfigServer and connecting clients run plain DrakesOmniConfig —
/// different BepInEx GUIDs by design. This only works because ServerSync's ConfigSync/CustomSyncedValue
/// handshake is keyed by the STRING you hand its constructor (confirmed by decompiling ServerSync.dll:
/// the RPC it registers is literally "{that string} ConfigSync"), not by the plugin's own BepInEx GUID —
/// so SyncName below has to be identical in both builds, and deliberately isn't either side's own GUID.
/// </summary>
internal sealed class ModletAuthority
{
    private const string SyncName = "com.drakemods.omniconfig.sync";
    private const string Identifier = "ResolvedOverrides";
    private const string RulesIdentifier = "NodeRules";
    private const string LooksIdentifier = "Looks";
    private const string ConfigsIdentifier = "ConfigBindings";

    // The sync WIRE FORMAT's own version — bump when any payload's shape changes. 2 = node rules added;
    // 3 = looks (Forge recipes) added. An older peer would silently ignore the new channel, so it is refused
    // rather than half-synced.
    // Deliberately NOT either build's own plugin Version: DrakesOmniConfig and DrakesOmniConfigServer
    // are released independently and will drift apart over time.
    // 4: ConfigBindings payload (Phase F). Identifiers must exist on both sides from start-up (see ModletChannel).
    private const string ProtocolVersion = "4";

    private readonly ConfigSync _sync;
    private readonly ModletChannel _channel;
    private readonly ModletChannel _rules;
    private readonly ModletChannel _looks;
    private readonly ModletChannel _configs;

    public event Action<IReadOnlyList<ResolvedOverride>>? ResolvedReceived;

    /// <summary>A joined client received the bindings OmniConfig delivers (not on the authoritative side: it applied them before publishing).</summary>
    public event Action<IReadOnlyList<ResolvedConfigBinding>>? ConfigsReceived;
    public event Action<NodeRuleSet>? RulesReceived;
    public event Action<Dictionary<string, Dictionary<string, string>>>? LooksReceived;

    public ModletAuthority()
    {
        _sync = new ConfigSync(SyncName)
        {
            DisplayName = "DrakesOmniConfig",
            CurrentVersion = ProtocolVersion,
            MinimumRequiredVersion = ProtocolVersion,
        };
        _channel = new ModletChannel(_sync, Identifier);
        _channel.Received += OnReceived;
        _rules = new ModletChannel(_sync, RulesIdentifier);
        _rules.Received += OnRulesReceived;
        _looks = new ModletChannel(_sync, LooksIdentifier);
        _looks.Received += OnLooksReceived;
        _configs = new ModletChannel(_sync, ConfigsIdentifier);
        _configs.Received += OnConfigsReceived;
    }

    /// <summary>Authoritative side only, alongside <see cref="PublishAll"/>.</summary>
    public void PublishLooks(Dictionary<string, Dictionary<string, string>> looks)
    {
        _looks.Publish(LooksSerializer.Serialize(looks));
    }

    // The authoritative side applied what it publishes before publishing it; ServerSync still echoes each value back
    // to it. Parsing that echo (every clone, field and look again) was a measurable part of a large modlet set's load.
    private static bool IsAuthority => ZNet.instance != null && ZNet.instance.IsServer();

    private void OnLooksReceived(string payload)
    {
        if (IsAuthority)
        {
            return;
        }

        LooksReceived?.Invoke(string.IsNullOrEmpty(payload) ? new Dictionary<string, Dictionary<string, string>>() : LooksSerializer.Deserialize(payload));
    }

    /// <summary>Authoritative side only: other mods' config bindings that players get through OmniConfig.</summary>
    public void PublishConfigs(IReadOnlyList<ResolvedConfigBinding> bindings)
    {
        _configs.Publish(Sync.Transport.ConfigBindingSerializer.Serialize(bindings));
    }

    private void OnConfigsReceived(string payload)
    {
        if (IsAuthority)
        {
            return;
        }

        ConfigsReceived?.Invoke(string.IsNullOrEmpty(payload) ? new List<ResolvedConfigBinding>() : Sync.Transport.ConfigBindingSerializer.Deserialize(payload));
    }

    /// <summary>Authoritative side only: publishes payloads already serialised off the main thread (the start-up preload).</summary>
    public void PublishPrepared(string overrides, string rules, string looks)
    {
        _channel.Publish(overrides);
        _rules.Publish(rules);
        _looks.Publish(looks);
    }

    /// <summary>Authoritative side only. Called once at bootstrap and again on every admin-triggered reload.</summary>
    public void PublishAll(IReadOnlyList<ResolvedOverride> resolved)
    {
        _channel.Publish(ResolvedOverrideSerializer.Serialize(resolved));
    }

    /// <summary>Authoritative side only, alongside <see cref="PublishAll"/>.</summary>
    public void PublishRules(NodeRuleSet set)
    {
        _rules.Publish(NodeRuleSetSerializer.Serialize(set));
    }

    private void OnRulesReceived(string payload)
    {
        if (!string.IsNullOrEmpty(payload))
        {
            RulesReceived?.Invoke(NodeRuleSetSerializer.Deserialize(payload));
        }
    }

    private void OnReceived(string payload)
    {
        if (string.IsNullOrEmpty(payload) || IsAuthority)
        {
            return;
        }

        ResolvedReceived?.Invoke(ResolvedOverrideSerializer.Deserialize(payload));
    }
}

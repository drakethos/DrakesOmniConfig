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

    // The sync WIRE FORMAT's own version — bump when either payload's shape changes. 2 = node rules added;
    // a v1 peer would silently ignore the rules channel, so it is refused rather than half-synced.
    // Deliberately NOT either build's own plugin Version: DrakesOmniConfig and DrakesOmniConfigServer
    // are released independently and will drift apart over time.
    private const string ProtocolVersion = "2";

    private readonly ConfigSync _sync;
    private readonly ModletChannel _channel;
    private readonly ModletChannel _rules;

    public event Action<IReadOnlyList<ResolvedOverride>>? ResolvedReceived;
    public event Action<NodeRuleSet>? RulesReceived;

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
        if (string.IsNullOrEmpty(payload))
        {
            return;
        }

        ResolvedReceived?.Invoke(ResolvedOverrideSerializer.Deserialize(payload));
    }
}

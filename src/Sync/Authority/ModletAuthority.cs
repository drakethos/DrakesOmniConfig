using System;
using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Schema;

namespace DrakesOmniConfig.Sync.Authority;

/// <summary>
/// Branches on ZNet.instance.IsServer() (true for a dedicated server AND a singleplayer/hosting player).
/// Authoritative side: serializes ModletManager's already-resolved overrides and publishes one
/// ModletChannel per modlet. Joined-client side: listens for each channel's received payload and raises
/// ResolvedReceived so ModletOrchestrator can hand it straight to ModletManager.AdoptResolved — this
/// side never re-runs ConflictResolver itself.
///
/// The dedicated server runs DrakesOmniConfigServer and connecting clients run plain DrakesOmniConfig —
/// different BepInEx GUIDs by design. This only works because ServerSync's CustomSyncedValue handshake
/// is keyed by RPC name, not by plugin GUID; it does not care that the two sides are different assemblies.
/// </summary>
internal sealed class ModletAuthority
{
    public event Action<IReadOnlyList<ResolvedOverride>>? ResolvedReceived;

    // TODO(you): authoritative-side entry point. One ModletChannel.Publish(...) per modlet, called after
    // ModletManager.LoadAndResolve and on every admin-triggered reload.
    public void PublishAll(IReadOnlyList<ResolvedOverride> resolved)
    {
    }
}

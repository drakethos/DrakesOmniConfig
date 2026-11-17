using System;
using ServerSync;

namespace DrakesOmniConfig.Sync.Transport;

/// <summary>
/// Wraps ONE ServerSync.dll CustomSyncedValue&lt;string&gt;. Originally designed as one channel PER
/// modlet id, but that ran into a real protocol constraint confirmed by decompiling ServerSync.dll:
/// CustomSyncedValue/ConfigEntry identifiers are a fixed, statically-known set on both sides — a client
/// that has never heard of a modlet id can't pre-register a channel for it, and a channel the client
/// doesn't recognize just gets logged as "unknown" and skipped (not fatal, but it also never arrives).
/// Simplest robust fix: ONE shared channel carrying the WHOLE resolved-override set as one payload, so
/// there's nothing to discover ahead of time — see ModletAuthority for how it's used.
/// </summary>
internal sealed class ModletChannel
{
    private readonly CustomSyncedValue<string> _value;

    public ModletChannel(ConfigSync sync, string identifier)
    {
        _value = new CustomSyncedValue<string>(sync, identifier, "");
        _value.ValueChanged += () => Received?.Invoke(_value.Value);
    }

    /// <summary>Authoritative side only. Setting .Value is what triggers ServerSync's own distribution to peers.</summary>
    public void Publish(string payload)
    {
        _value.Value = payload;
    }

    /// <summary>Fires whenever the value changes — on the authoritative side too (harmless; unused there).</summary>
    public event Action<string>? Received;
}

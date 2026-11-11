using System;

namespace DrakesOmniConfig.Sync.Transport;

/// <summary>
/// One ServerSync.dll CustomSyncedValue&lt;string&gt; per modlet id — deliberately NOT one channel for
/// the whole modlet set, so republishing one modlet never resends everyone else's.
/// TODO(you): wire the actual ServerSync.dll embedding (libs/ServerSync.dll + ILRepack.targets +
/// ILRepack.Lib.MSBuild.Task NuGet package — see BetterRepair for the exact pattern, including the
/// Patch-ServerSyncEverybody.ps1 Valheim-1.0 compat fix). Not referenced in this project yet.
/// </summary>
internal sealed class ModletChannel
{
    public string ModletId { get; }

    public ModletChannel(string modletId)
    {
        ModletId = modletId;
    }

    /// <summary>Server/authoritative side only. Payload is the already-resolved override set, serialized — never raw modlet files.</summary>
    public void Publish(string serializedResolvedOverrides)
    {
    }

    /// <summary>Client side only. Fires once when this modlet's value arrives/changes — never polled.</summary>
    public event Action<string>? Received;
}

using System.Collections;
using System.Linq;
using DrakesOmniConfig.Config.Flags;
using DrakesOmniConfig.Config.Settings;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Flags;

/// <summary>
/// Server side: every flag that is on is also the world global key <c>omni_flag_&lt;name&gt;</c>, so any mod can read it
/// with <c>ZoneSystem.instance.GetGlobalKey("omni_flag_pvp")</c> and no reference to OmniConfig. Keys of flags that are
/// off or no longer declared are removed (global keys are saved with the world). Off with MainConfig
/// <c>MirrorFlagsToGlobalKeys</c>. Global keys reach players through the game's own sync.
/// </summary>
internal static class GlobalKeyMirror
{
    public const string Prefix = "omni_flag_";

    public static bool Enabled { get; set; } = true;

    private static FlagSet _flags = FlagSet.Empty;
    private static bool _pending;

    /// <summary>Authoritative side, after every load: mirror now if the world's key store exists, else when it does.</summary>
    public static void Apply(FlagSet flags)
    {
        _flags = flags;
        _pending = true;
        if (ZoneSystem.instance != null && ZNet.instance != null && ZNet.instance.IsServer())
        {
            Sync();
        }
    }

    private static void Sync()
    {
        if (!Enabled || ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        ZoneSystem zones = ZoneSystem.instance;
        foreach ((string name, bool on, bool _) in _flags.All)
        {
            string key = Prefix + name;
            bool has = zones.GetGlobalKey(key);
            if (on && !has)
            {
                zones.SetGlobalKey(key);
            }
            else if (!on && has)
            {
                zones.RemoveGlobalKey(key);
            }
        }

        // keys from the world save whose flag is now off or gone
        foreach (string stored in zones.GetGlobalKeys().ToList())
        {
            string key = stored.Split(' ')[0];
            if (key.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase) && !_flags.IsOn(key.Substring(Prefix.Length)))
            {
                zones.RemoveGlobalKey(key);
            }
        }

        _pending = false;
    }

    // The world's global keys are read from its save around ZoneSystem start; mirror a moment after, so a stale saved key
    // cannot come back over a fresh switch.
    [HarmonyPatch(typeof(ZoneSystem), "Start")]
    private static class ZoneSystem_Start_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(ZoneSystem), "Start") != null;

        private static void Postfix(ZoneSystem __instance) => __instance.StartCoroutine(Later());
    }

    private static IEnumerator Later()
    {
        yield return new WaitForSeconds(2f);
        if (_pending)
        {
            Sync();
        }
    }
}

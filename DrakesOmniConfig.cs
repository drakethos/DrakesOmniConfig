using BepInEx;
using HarmonyLib;

namespace DrakesOmniConfig
{
    [BepInPlugin(GUID, ModName, Version)]
    public partial class DrakesOmniConfigPlugin : BaseUnityPlugin
    {
        private readonly Harmony _harmony = new Harmony(GUID);

        private void Awake()
        {
            _harmony.PatchAll();
            Logger.LogInfo($"{ModName} {Version} Awake.");
        }
    }
}

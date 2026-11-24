using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Management;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Fields.Application;
using DrakesOmniConfig.Engine.Fields.Registry;
using DrakesOmniConfig.Engine.Node.Library;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Orchestration;
using DrakesOmniConfig.Sync.Authority;

namespace DrakesOmniConfig
{
    /// <summary>
    /// Baseline/client build. Deployment rule: every connecting client installs THIS build;
    /// a dedicated server installs DrakesOmniConfigServer instead — never both on the same machine.
    /// A self-hosting/singleplayer player is still fully authoritative with this build alone (the
    /// engine's IsServer() branch doesn't care which assembly it's running in); they just don't get
    /// Server's Tools.* authoring conveniences.
    /// Do NOT add [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, ...)] here — client and
    /// server intentionally run DIFFERENT GUIDs (com.drakemods.omniconfig vs .omniconfigserver), so
    /// Valheim's own GUID-matching compat check would wrongly reject every connection. Sync relies on
    /// ServerSync's own RPC-name handshake (Sync.Authority.ModletAuthority), which never checks GUID equality.
    /// </summary>
    
    // Soft: popups use DrakeModsLibs' window when it is installed, a built-in dialog otherwise. Never required.
    [BepInDependency("com.drakemods.libs", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(GUID, ModName, Version)]
    public partial class DrakesOmniConfigPlugin : BaseUnityPlugin
    {
        
        private readonly Harmony _harmony = new Harmony(GUID);
        private ModletOrchestrator? _orchestrator;

        private void Awake()
        {
            _harmony.PatchAll();

            var mainConfig = new MainConfig(Config);
            ModLog.Init(Logger, mainConfig.MinimumLogLevel.Value);
            var loader = new ModletLoader();
            var resolver = new ConflictResolver();
            var modletManager = new ModletManager(loader, resolver);
            var fieldRegistry = new FieldRegistry();
            var fieldPatcher = new FieldPatcher(fieldRegistry);
            var authority = new ModletAuthority();
            _orchestrator = new ModletOrchestrator(modletManager, fieldPatcher, authority);

            _orchestrator.Bootstrap(mainConfig.ModletsFolder.Value);
            NodeLibrary.RegisterAll();
            Engine.Flags.GlobalKeyMirror.Enabled = mainConfig.MirrorFlagsToGlobalKeys.Value;
            _orchestrator.StartPreload(); // after the node library: rules are parsed against it
            Engine.Hooks.MenuPrebuild.Runner = this; // builds clones and looks at the main menu (players; not on a headless server)
            Engine.Looks.LookBridge.Start(this, Logger);
            gameObject.AddComponent<NodeTicker>();
            Logger.LogInfo($"{ModName} {Version} Awake.");
        }
    }
}

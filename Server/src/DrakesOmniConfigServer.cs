using BepInEx;
using HarmonyLib;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Management;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Fields.Application;
using DrakesOmniConfig.Engine.Fields.Registry;
using DrakesOmniConfig.Engine.Orchestration;
using DrakesOmniConfig.Sync.Authority;

namespace DrakesOmniConfig
{
    /// <summary>
    /// Server/admin build. Runs the SAME engine as DrakesOmniConfigPlugin — the Config/Engine/Sync source
    /// is shared (compiled a second time into this assembly, not referenced), not reimplemented — plus
    /// Tools.* for building and testing modlets. Deployment rule: a dedicated server installs THIS
    /// build instead of the baseline, never both — same reasoning as DrakesOmniConfig.cs.
    /// Do NOT add [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, ...)] here either — see
    /// the matching note on DrakesOmniConfigPlugin for why the client/server GUID mismatch is intentional.
    /// </summary>
    [BepInPlugin(GUID, ModName, Version)]
    public partial class DrakesOmniConfigServerPlugin : BaseUnityPlugin
    {
        private readonly Harmony _harmony = new Harmony(GUID);
        private ModletOrchestrator? _orchestrator;

        private void Awake()
        {
            _harmony.PatchAll();

            // TODO(you): identical soft-wired graph to the baseline plugin (see ../../src/DrakesOmniConfig.cs) —
            // keep the two Awake() methods in sync when either changes, same as the Compile lists.
            var mainConfig = new MainConfig(Config);
            var loader = new ModletLoader();
            var resolver = new ConflictResolver();
            var modletManager = new ModletManager(loader, resolver);
            var fieldRegistry = new FieldRegistry();
            var fieldPatcher = new FieldPatcher(fieldRegistry);
            var authority = new ModletAuthority();
            _orchestrator = new ModletOrchestrator(modletManager, fieldPatcher, authority);

            // TODO(you): Tools-only init goes here once Tools.ConsoleCommands / Tools.HtmlHelper exist.

            Logger.LogInfo($"{ModName} {Version} Awake.");
        }
    }
}

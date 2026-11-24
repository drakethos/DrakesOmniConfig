using BepInEx;
using BepInEx.Configuration;

namespace OmniConfigTestTarget;

/// <summary>
/// A test-only mod with one setting of each kind, for the DrakesOmniConfig-Configs suite (modlet <c>configs:</c> bindings).
/// "[Synced] Server value" carries a tag named like ServerSync's (OwnConfigEntryBase, SynchronizedConfig = true), so
/// OmniConfig treats it as an entry the mod syncs itself. Deployed only into the test profile by the test script.
/// </summary>
[BepInPlugin("com.drakemods.omniconfig.testtarget", "OmniConfig Test Target", "1.0.0")]
public sealed class TestTargetPlugin : BaseUnityPlugin
{
    public enum Mode { Alpha, Beta, Gamma }

    private void Awake()
    {
        Config.Bind("Numbers", "Plain int", 10, "No range.");
        Config.Bind("Numbers", "Second int", 7, "For the bad-value test.");
        Config.Bind("Numbers", "Multiplier", 2.0, "Relative edits.");
        Config.Bind("Numbers", "Ranged float", 1.5f, new ConfigDescription("0 to 10.", new AcceptableValueRange<float>(0f, 10f)));
        Config.Bind("Toggles", "Enabled", false, "A bool.");
        Config.Bind("Text", "Greeting", "hello", "Text, and the conflict test.");
        Config.Bind("Text", "Mode", Mode.Alpha, "An enum.");
        Config.Bind("1 - General: extra", "Stack: multiplier", 3, "Section and key names with ':' in them.");
        Config.Bind("Synced", "Server value", 5, new ConfigDescription("Looks like a ServerSync-synced entry.", null, new OwnConfigEntryBase { SynchronizedConfig = true }));
    }
}

/// <summary>Named like ServerSync's tag type: OmniConfig detects self-synced entries by type name only.</summary>
public class OwnConfigEntryBase
{
    public bool SynchronizedConfig { get; set; }
}

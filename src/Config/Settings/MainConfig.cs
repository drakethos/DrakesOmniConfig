using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace DrakesOmniConfig.Config.Settings;

internal sealed class MainConfig
{
    public ConfigEntry<LogLevel> MinimumLogLevel { get; }
    public ConfigEntry<string> ModletsFolder { get; }
    public ConfigEntry<bool> MirrorFlagsToGlobalKeys { get; }

    public MainConfig(ConfigFile config)
    {
        MinimumLogLevel = config.Bind(
            "General",
            "MinimumLogLevel",
            LogLevel.Info,
            "Minimum level of messages to display."
        );

        ModletsFolder = config.Bind(
            "General",
            "ModletsFolder",
            Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "Modlets"),
            "Folder containing modlet subfolders (each with its own manifest.yaml). Defaults to BepInEx/config/DrakesOmniConfig/Modlets."
        );

        MirrorFlagsToGlobalKeys = config.Bind(
            "Flags",
            "MirrorFlagsToGlobalKeys",
            true,
            "Server: every flag that is on (Modlets/flags.yaml, switched with flag.set) is also the world global key omni_flag_<name>, so other mods can read it without depending on OmniConfig."
        );
    }
}

internal enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4
}

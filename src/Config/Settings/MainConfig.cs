using BepInEx.Configuration;

namespace DrakesOmniConfig.Config.Settings;

/// <summary>
/// The mod's own BepInEx settings — where to find modlets, hot-reload behaviour. NOT modlet content
/// itself; that's Config.Modlets.
/// </summary>
internal sealed class MainConfig
{
    // TODO(you): ConfigEntry<string> ModletsFolder, ConfigEntry<bool> HotReload, etc. Bind(ConfigFile) in the constructor.
    public MainConfig(ConfigFile config)
    {
    }
}

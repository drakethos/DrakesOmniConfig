using BepInEx.Logging;

namespace DrakesOmniConfig.Config.Settings;

/// <summary>
/// Shared logger for everything outside the plugin class itself — BaseUnityPlugin's own `Logger`
/// property only exists on that one instance, so plain classes (ModletLoader, FieldPatcher, etc.) have
/// nowhere else to log from. Init() is called once from the plugin's Awake() with its own ManualLogSource
/// and MainConfig.MinimumLogLevel; everything below that threshold is silently dropped.
/// </summary>
internal static class ModLog
{
    private static ManualLogSource? _source;
    private static LogLevel _minimum = LogLevel.Info;

    public static void Init(ManualLogSource source, LogLevel minimum)
    {
        _source = source;
        _minimum = minimum;
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    private static void Write(LogLevel level, string message)
    {
        if (_source == null || level < _minimum)
        {
            return;
        }

        switch (level)
        {
            case LogLevel.Debug:
                _source.LogDebug(message);
                break;
            case LogLevel.Info:
                _source.LogInfo(message);
                break;
            case LogLevel.Warning:
                _source.LogWarning(message);
                break;
            case LogLevel.Error:
                _source.LogError(message);
                break;
            case LogLevel.Fatal:
                _source.LogFatal(message);
                break;
        }
    }
}

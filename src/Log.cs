using BepInEx.Logging;

namespace ExtraInventorySlotUpgrade;

internal static class Log
{
    private static ManualLogSource _source;

    public static void Init(ManualLogSource source) => _source = source;

    public static void Info(string msg) => _source?.LogInfo(msg);
    public static void Warn(string msg) => _source?.LogWarning(msg);
    public static void Error(string msg) => _source?.LogError(msg);

    /// <summary>Gated behind the VerboseLogging config entry (house rule).</summary>
    public static void Verbose(string msg)
    {
        if (PluginConfig.VerboseLogging != null && PluginConfig.VerboseLogging.Value)
        {
            _source?.LogInfo(msg);
        }
    }
}

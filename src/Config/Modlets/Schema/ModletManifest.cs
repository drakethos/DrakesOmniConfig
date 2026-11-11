namespace DrakesOmniConfig.Config.Modlets.Schema;

internal sealed class ModletManifest
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Stable id — used for conflict bookkeeping and as the sync channel name. Never rename after sharing a modlet.</summary>
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Higher wins a conflict between two non-override claims. Ties break by load order.</summary>
    public int Priority { get; set; }
}

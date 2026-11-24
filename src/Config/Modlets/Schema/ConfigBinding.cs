namespace DrakesOmniConfig.Config.Modlets.Schema;

/// <summary>
/// One <c>configs:</c> entry in a modlet: set another mod's BepInEx config entry (plugin GUID, section, key) with the
/// same light math as <c>overrides:</c> fields. Never written to the mod's .cfg file (see ConfigBindingApplier).
/// </summary>
internal sealed class ConfigBinding
{
    public string Mod { get; set; } = "";
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public FieldValueExpression Expression { get; set; } = new();
    public bool Override { get; set; }
}

/// <summary>The winning binding for one entry after conflict resolution (server side), or as received by a client.</summary>
internal sealed class ResolvedConfigBinding
{
    public string Mod { get; set; } = "";
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public FieldValueExpression Expression { get; set; } = new();
    public string WinningModletId { get; set; } = "";

    /// <summary>"guid|section|key" with the GUID lower-cased: the identity two bindings conflict on.</summary>
    public string Identity => Identify(Mod, Section, Key);

    public static string Identify(string mod, string section, string key) => $"{mod.Trim().ToLowerInvariant()}|{section.Trim()}|{key.Trim()}";

    public override string ToString() => $"{Mod} [{Section}] {Key}";
}

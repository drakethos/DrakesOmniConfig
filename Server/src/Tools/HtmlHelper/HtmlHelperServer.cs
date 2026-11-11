namespace DrakesOmniConfig.Tools.HtmlHelper;

/// <summary>
/// Reserved for a later wave: a local HTML helper for visually building modlets (fields, nodes, items,
/// overrides) instead of hand-writing YAML. Server-exclusive — this file only exists in
/// DrakesOmniConfigServer, never the baseline build. Not wired yet — undecided whether this serves a
/// page via a small embedded HTTP listener or ships as a standalone static tool; either way it should
/// only ever read/write modlet folders, never bypass ConflictResolver or FieldPatcher.
/// </summary>
internal static class HtmlHelperServer
{
}

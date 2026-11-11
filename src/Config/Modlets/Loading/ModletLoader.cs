using System.Collections.Generic;
using DrakesOmniConfig.Config.Modlets.Schema;

namespace DrakesOmniConfig.Config.Modlets.Loading;

/// <summary>
/// Reads modlet FOLDERS off disk into parsed Schema.Modlet instances. Disk IO + parsing only — no
/// conflict logic here. Layout, per modlet, under the Modlets root folderPath points at:
///
///   Modlets/
///     com.author.NameBla/
///       manifest.yaml      - ModletManifest (Id comes from the folder name, not this file) — only fixed, required file
///       vars.yaml          - quick-edit Variables ("{name}" placeholders used elsewhere in this modlet)
///       groups.yaml        - optional Groups (name -> member ids), referenced by TargetSelector.GroupName
///       nodes/**           - reserved, Wave 2 node scripts, any structure — record paths into NodeScriptPaths, don't parse
///       (anything else)    - author's own structure, not crawled by fixed name: weapons/bronze.yaml,
///                            biomes/meadows/durability.yaml, one flat folder, whatever they want.
///
/// Everything under the modlet folder other than the four reserved root names above is RECURSIVELY
/// crawled for *.yaml/*.yml (any depth, any subfolder naming) and merged into one Overrides list —
/// the directory structure carries no meaning to the loader, it's purely the author's own organization.
/// </summary>
internal sealed class ModletLoader
{
    // TODO(you): pick + wire a YAML library (YamlDotNet is the obvious choice; vendor it, don't add a
    // Thunderstore dependency). Enumerate subfolders of folderPath — each is one modlet, id = folder name.
    // Recursively glob *.yaml/*.yml under each modlet folder, skipping manifest.yaml/vars.yaml/groups.yaml
    // (parsed separately, not as overrides) and nodes/ (recorded into NodeScriptPaths, not parsed).
    // Substitute "{var}" placeholders (from vars.yaml) at the raw-text level before parsing a scalar into
    // a FieldValueExpression. A malformed file must be skipped + logged individually — one bad modlet
    // (or one bad file within it) must never prevent the rest from loading.
    public List<Modlet> LoadAll(string folderPath)
    {
        return new List<Modlet>();
    }
}

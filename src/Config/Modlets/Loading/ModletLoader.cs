using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DrakesOmniConfig.Config.Flags;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
    private const string ManifestFileName = "manifest.yaml";
    private const string VariablesFileName = "vars.yaml";
    private const string GroupsFileName = "groups.yaml";
    private const string NodesFolderName = "nodes";

    private static readonly Regex VariablePattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    // CamelCaseNamingConvention maps our PascalCase schema properties (DisplayName, SchemaVersion, ...)
    // to the camelCase keys the fixtures already use (displayName, schemaVersion, ...).
    private readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>The flags read by the last <see cref="LoadAll"/> (declared defaults + admin switches).</summary>
    public FlagSet Flags { get; private set; } = FlagSet.Empty;

    public List<Modlet> LoadAll(string folderPath)
    {
        var modlets = new List<Modlet>();

        if (!Directory.Exists(folderPath))
        {
            ModLog.Warning($"Modlets root '{folderPath}' does not exist.");
            return modlets;
        }

        // Phase F2: flags first (variables and gating depend on them), then the root vars/groups every modlet shares
        Flags = FlagSet.Load(folderPath);
        Dictionary<string, object?> rootVariables = ConditionalVars.Resolve(ParseVariables(FileOrNull(Path.Combine(folderPath, VariablesFileName))), Flags.IsOn);
        Dictionary<string, List<string>> rootGroups = ParseGroups(FileOrNull(Path.Combine(folderPath, GroupsFileName)));

        foreach (string modletFolder in Directory.GetDirectories(folderPath))
        {
            string id = Path.GetFileName(modletFolder);
            try
            {
                Modlet? modlet = LoadOne(modletFolder, id, rootVariables, rootGroups);
                if (modlet != null)
                {
                    modlets.Add(modlet);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to load modlet '{id}': {ex.Message}");
            }
        }

        return modlets;
    }

    private Modlet? LoadOne(string modletFolder, string id, IReadOnlyDictionary<string, object?> rootVariables, IReadOnlyDictionary<string, List<string>> rootGroups)
    {
        string manifestPath = Path.Combine(modletFolder, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            ModLog.Warning($"Modlet '{id}' has no {ManifestFileName} — skipping.");
            return null;
        }

        ModletManifest manifest = ParseManifest(manifestPath);
        manifest.Id = id;

        if (!manifest.Enabled)
        {
            ModLog.Debug($"Modlet '{id}' is disabled — skipping.");
            return null;
        }

        if (!Flags.AllOn(manifest.RequiresFlags) || Flags.AnyOn(manifest.UnlessFlags))
        {
            ModLog.Info($"Modlet '{id}' is off because of its flags (requires: {string.Join(", ", manifest.RequiresFlags)}; unless: {string.Join(", ", manifest.UnlessFlags)}).");
            return null;
        }

        // {name} lookup: this modlet's vars.yaml first, then Modlets/vars.yaml; both may depend on flags
        var variables = rootVariables.ToDictionary(v => v.Key, v => v.Value);
        foreach (KeyValuePair<string, object?> own in ConditionalVars.Resolve(ParseVariables(FileOrNull(Path.Combine(modletFolder, VariablesFileName))), Flags.IsOn))
        {
            variables[own.Key] = own.Value;
        }

        var groups = rootGroups.ToDictionary(g => g.Key, g => g.Value);
        foreach (KeyValuePair<string, List<string>> own in ParseGroups(FileOrNull(Path.Combine(modletFolder, GroupsFileName))))
        {
            groups[own.Key] = own.Value;
        }

        string nodesFolder = Path.Combine(modletFolder, NodesFolderName);
        var nodeScriptPaths = Directory.Exists(nodesFolder)
            ? Directory.GetFiles(nodesFolder, "*", SearchOption.AllDirectories).ToList()
            : new List<string>();

        var overrides = new List<ModletOverride>();
        var configBindings = new List<ConfigBinding>();
        IEnumerable<string> yamlFiles = Directory.GetFiles(modletFolder, "*.yaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(modletFolder, "*.yml", SearchOption.AllDirectories));

        foreach (string yamlFile in yamlFiles)
        {
            if (IsReservedRootFile(modletFolder, yamlFile) || IsUnderFolder(nodesFolder, yamlFile))
            {
                continue;
            }

            try
            {
                overrides.AddRange(ParseOverridesFile(yamlFile, variables, configBindings));
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to parse '{yamlFile}' in modlet '{id}': {ex.Message}");
            }
        }

        return new Modlet
        {
            Manifest = manifest,
            Overrides = overrides,
            ConfigBindings = configBindings,
            Variables = variables,
            Groups = groups,
            NodeScriptPaths = nodeScriptPaths,
            Looks = ReadLooks(Path.Combine(modletFolder, LooksFolderName)),
        };
    }

    internal const string LooksFolderName = "looks";

    // Text only: a look never ships files over the network (textures/models come from installed mod packs).
    private static Dictionary<string, string> ReadLooks(string folder)
    {
        var looks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder))
        {
            return looks;
        }

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            looks[file.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/')] = File.ReadAllText(file);
        }

        return looks;
    }

    private ModletManifest ParseManifest(string manifestFilePath)
    {
        string text = File.ReadAllText(manifestFilePath);
        return _yaml.Deserialize<ModletManifest>(text) ?? new ModletManifest();
    }

    private Dictionary<string, object?> ParseVariables(string? variablesFilePath)
    {
        if (variablesFilePath == null)
        {
            return new Dictionary<string, object?>();
        }

        string text = File.ReadAllText(variablesFilePath);
        return _yaml.Deserialize<Dictionary<string, object?>>(text) ?? new Dictionary<string, object?>();
    }

    private Dictionary<string, List<string>> ParseGroups(string? groupsFilePath)
    {
        if (groupsFilePath == null)
        {
            return new Dictionary<string, List<string>>();
        }

        string text = File.ReadAllText(groupsFilePath);
        return _yaml.Deserialize<Dictionary<string, List<string>>>(text) ?? new Dictionary<string, List<string>>();
    }

    // Unknown {name} is left as literal text — downstream parsing (ParseExpression / Enum.Parse) then
    // fails loudly with a clear message rather than this method guessing or throwing blind.
    internal static string SubstituteVariables(string rawText, IReadOnlyDictionary<string, object?> variables)
    {
        if (variables.Count == 0)
        {
            return rawText;
        }

        return VariablePattern.Replace(rawText, match =>
        {
            string name = match.Groups[1].Value;
            if (variables.TryGetValue(name, out object? value) && value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? match.Value;
            }

            return match.Value;
        });
    }

    private List<ModletOverride> ParseOverridesFile(string filePath, IReadOnlyDictionary<string, object?> variables, List<ConfigBinding> configBindings)
    {
        string rawText = File.ReadAllText(filePath);
        string substituted = SubstituteVariables(rawText, variables);

        RawOverridesFile? raw = _yaml.Deserialize<RawOverridesFile>(substituted);
        if (raw == null)
        {
            return new List<ModletOverride>();
        }

        foreach (RawConfigEntry entry in raw.Configs ?? new List<RawConfigEntry>())
        {
            if (string.IsNullOrWhiteSpace(entry.Mod) || string.IsNullOrWhiteSpace(entry.Section) || string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
            {
                ModLog.Warning($"'{filePath}': a configs entry needs mod, section, key and value; skipped ({entry.Mod} [{entry.Section}] {entry.Key}).");
                continue;
            }

            configBindings.Add(new ConfigBinding
            {
                Mod = entry.Mod.Trim(),
                Section = entry.Section.Trim(),
                Key = entry.Key.Trim(),
                Expression = ParseExpression(entry.Value),
                Override = entry.Override,
            });
        }

        var result = new List<ModletOverride>();
        foreach (RawClone clone in raw.Clones)
        {
            result.Add(ParseClone(clone));
        }

        foreach (RawOverrideEntry entry in raw.Overrides ?? new List<RawOverrideEntry>())
        {
            var target = new TargetSelector
            {
                TypeName = entry.Target.TypeName,
                Scope = (ModletScope)Enum.Parse(typeof(ModletScope), entry.Target.Scope, ignoreCase: true),
                Kind = (SelectorKind)Enum.Parse(typeof(SelectorKind), entry.Target.Kind, ignoreCase: true),
                ItemId = entry.Target.ItemId,
                GroupName = entry.Target.GroupName,
                NamePattern = entry.Target.NamePattern,
                Category = entry.Target.Category,
            };

            var fields = new Dictionary<string, FieldValueExpression>();
            foreach (KeyValuePair<string, string> field in entry.Fields)
            {
                fields[field.Key] = ParseExpression(field.Value);
            }

            result.Add(new ModletOverride
            {
                Target = target,
                Fields = fields,
                Override = entry.Override,
            });
        }

        return result;
    }

    /// <summary>The override field that marks a clone: its operand is the prefab to copy. PrefabPassHook creates every clone before it applies any other override.</summary>
    internal const string CloneOfField = "@cloneOf";

    /// <summary>The override field that carries a clone's recipe, encoded as "station|level|amount|Item:n,Item:n" so it syncs as one string.</summary>
    internal const string RecipeField = "@recipe";

    // A clone is an ordinary override on the NEW id (so conflict resolution and server sync carry it unchanged):
    // one "@cloneOf" field naming the source, then name/description and the clone's own fields.
    private static ModletOverride ParseClone(RawClone clone)
    {
        if (string.IsNullOrWhiteSpace(clone.Id) || string.IsNullOrWhiteSpace(clone.From))
        {
            throw new FormatException("a clone needs both 'id' (the new prefab name) and 'from' (the prefab to copy).");
        }

        var fields = new Dictionary<string, FieldValueExpression>
        {
            [CloneOfField] = new FieldValueExpression { Operator = ValueOperator.Set, Operand = clone.From.Trim() }
        };
        if (!string.IsNullOrEmpty(clone.Name))
        {
            fields["m_itemData.m_shared.m_name"] = new FieldValueExpression { Operator = ValueOperator.Set, Operand = clone.Name };
        }

        if (!string.IsNullOrEmpty(clone.Description))
        {
            fields["m_itemData.m_shared.m_description"] = new FieldValueExpression { Operator = ValueOperator.Set, Operand = clone.Description };
        }

        foreach (KeyValuePair<string, string> field in clone.Fields)
        {
            fields[field.Key] = ParseExpression(field.Value);
        }

        if (clone.Recipe != null)
        {
            if (clone.Recipe.Requires.Count == 0)
            {
                throw new FormatException($"clone '{clone.Id}': a recipe needs at least one entry under 'requires'.");
            }

            string requires = string.Join(",", clone.Recipe.Requires.Select(r => r.Key.Trim() + ":" + r.Value));
            fields[RecipeField] = new FieldValueExpression
            {
                Operator = ValueOperator.Set,
                Operand = $"{clone.Recipe.Station.Trim()}|{clone.Recipe.Level}|{clone.Recipe.Amount}|{requires}"
            };
        }

        return new ModletOverride
        {
            Target = new TargetSelector { TypeName = clone.Type, Scope = ModletScope.Prefab, Kind = SelectorKind.Item, ItemId = clone.Id.Trim() },
            Fields = fields,
            Override = true
        };
    }

    // The light math: "HelloWorld" / "50" -> Set; "=5" -> Set; "+20%" -> Add(20, percentage); "-5" ->
    // Subtract(5); "*1.5" -> Multiply(1.5); "/2" -> Divide(2). Only Add/Subtract honour a trailing "%".
    internal static FieldValueExpression ParseExpression(string raw)
    {
        string text = raw.Trim();
        if (text.Length == 0)
        {
            return new FieldValueExpression { Operator = ValueOperator.Set, Operand = "" };
        }

        char firstChar = text[0];
        bool hasOperatorPrefix = firstChar is '+' or '-' or '*' or '/' or '=';

        if (!hasOperatorPrefix)
        {
            return new FieldValueExpression { Operator = ValueOperator.Set, Operand = ParseOperand(text) };
        }

        string rest = text.Substring(1).Trim();
        bool isPercentage = false;
        if (rest.EndsWith("%", StringComparison.Ordinal))
        {
            isPercentage = true;
            rest = rest.Substring(0, rest.Length - 1).Trim();
        }

        ValueOperator op = firstChar switch
        {
            '+' => ValueOperator.Add,
            '-' => ValueOperator.Subtract,
            '*' => ValueOperator.Multiply,
            '/' => ValueOperator.Divide,
            _ => ValueOperator.Set,
        };

        return new FieldValueExpression
        {
            Operator = op,
            Operand = ParseOperand(rest),
            IsPercentage = isPercentage && op is ValueOperator.Add or ValueOperator.Subtract,
        };
    }

    private static object ParseOperand(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number
            : text;
    }

    private static string? FileOrNull(string path) => File.Exists(path) ? path : null;

    private static bool IsReservedRootFile(string modletFolder, string filePath)
    {
        string? parent = Path.GetDirectoryName(filePath);
        bool atRoot = parent != null && string.Equals(
            Path.GetFullPath(parent), Path.GetFullPath(modletFolder), StringComparison.OrdinalIgnoreCase);

        if (!atRoot)
        {
            return false;
        }

        string fileName = Path.GetFileName(filePath);
        return fileName.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(VariablesFileName, StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(GroupsFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderFolder(string folder, string filePath)
    {
        string normalizedFolder = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        string normalizedFile = Path.GetFullPath(filePath);
        return normalizedFile.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase);
    }

    // Raw YAML-facing shapes — deliberately separate from Schema.* because Fields' values need the
    // light-math parse above, not a direct deserialize, and Scope/Kind arrive as plain strings.
    private sealed class RawOverridesFile
    {
        public List<RawOverrideEntry>? Overrides { get; set; }
        public List<RawClone> Clones { get; set; } = new();
        public List<RawConfigEntry>? Configs { get; set; }
    }

    // A list, not a map: section and key names may contain ':'.
    private sealed class RawConfigEntry
    {
        public string Mod { get; set; } = "";
        public string Section { get; set; } = "";
        public string Key { get; set; } = "";
        public string? Value { get; set; }
        public bool Override { get; set; }
    }

    private sealed class RawClone
    {
        public string Id { get; set; } = "";
        public string From { get; set; } = "";
        public string Type { get; set; } = "ItemDrop";
        public string? Name { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new();
        public RawRecipe? Recipe { get; set; }
    }

    private sealed class RawRecipe
    {
        /// <summary>Crafting station prefab (piece_cauldron, piece_workbench, forge ...); empty = craftable by hand.</summary>
        public string Station { get; set; } = "";
        public int Level { get; set; } = 1;
        public int Amount { get; set; } = 1;
        public Dictionary<string, int> Requires { get; set; } = new();
    }

    private sealed class RawOverrideEntry
    {
        public RawTargetSelector Target { get; set; } = new();
        public Dictionary<string, string> Fields { get; set; } = new();
        public bool Override { get; set; }
    }

    private sealed class RawTargetSelector
    {
        public string TypeName { get; set; } = "";
        public string Scope { get; set; } = nameof(ModletScope.Prefab);
        public string Kind { get; set; } = nameof(SelectorKind.Item);
        public string? ItemId { get; set; }
        public string? GroupName { get; set; }
        public string? NamePattern { get; set; }
        public string? Category { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using YamlDotNet.Serialization;

namespace DrakesOmniConfig.Engine.Node.Loading;

/// <summary>
/// Reads a modlet's <c>nodes/**/*.yaml</c> into rules:
/// <code>
/// rules:
///   - id: lucky-pickaxe
///     when: { event: hit, item: "Pickaxe*", target: "Rock*|MineRock*" }
///     if:   [ { chance: 10 } ]
///     do:   [ { give: { item: Coins, amount: 5 } }, { message: "Lucky strike!" } ]
///     else: [ ... ]
/// </code>
/// Each node is a one-key map (the node's name); a bare scalar fills the node's shorthand arg. A bad rule is
/// logged and skipped; the rest of the file still loads. Top-level <c>modlet:</c>/<c>file:</c> keys are
/// ignored here — the HTML editor writes them so the in-game panel knows where a pasted file belongs.
/// </summary>
internal sealed class NodeLoader
{
    private readonly IDeserializer _yaml = new DeserializerBuilder().Build();

    public List<NodeRule> LoadModlet(Modlet modlet, string modletId)
    {
        var rules = new List<NodeRule>();
        foreach (string path in modlet.NodeScriptPaths.Where(IsYaml).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string text = ModletLoader.SubstituteVariables(File.ReadAllText(path), modlet.Variables);
                rules.AddRange(Parse(text, modletId, Path.GetFileName(path), out _));
            }
            catch (Exception ex)
            {
                ModLog.Error($"Node file '{modletId}/{Path.GetFileName(path)}' could not be read: {ex.Message}");
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NodeRule rule in rules.ToList())
        {
            if (!seen.Add(rule.Id))
            {
                ModLog.Error($"Rule id '{rule.Id}' appears twice in modlet '{modletId}'; the second one is skipped.");
                rules.Remove(rule);
            }
        }

        return rules;
    }

    /// <summary>Parses one file's text. <paramref name="errors"/> lists every problem (also logged) so the admin panel can report them back.</summary>
    public List<NodeRule> Parse(string text, string modletId, string fileName, out List<string> errors, bool logErrors = true)
    {
        errors = new List<string>();
        Action<string> log = logErrors ? ModLog.Error : ModLog.Warning;
        var rules = new List<NodeRule>();
        object? root = _yaml.Deserialize<object>(text);
        if (root is not Dictionary<object, object> map || !map.TryGetValue("rules", out object? list))
        {
            errors.Add($"{fileName}: no 'rules:' list at the top.");
            log($"Node file '{modletId}/{fileName}' has no 'rules:' list at the top.");
            return rules;
        }

        if (list is not List<object> entries)
        {
            errors.Add($"{fileName}: 'rules:' must be a list.");
            log($"Node file '{modletId}/{fileName}': 'rules:' must be a list.");
            return rules;
        }

        int index = 0;
        foreach (object entry in entries)
        {
            index++;
            string where = $"{modletId}/{fileName} rule #{index}";
            try
            {
                NodeRule rule = ParseRule(entry, modletId, ref where);
                rules.Add(rule);
            }
            catch (FormatException ex)
            {
                errors.Add($"{where}: {ex.Message}");
                log($"Rule '{where}' skipped: {ex.Message}");
            }
        }

        return rules;
    }

    private static NodeRule ParseRule(object entry, string modletId, ref string where)
    {
        if (entry is not Dictionary<object, object> map)
        {
            throw new FormatException("each rule must be a map with id/when/do.");
        }

        var rule = new NodeRule { ModletId = modletId };
        rule.Id = Scalar(map, "id");
        if (rule.Id.Length == 0)
        {
            throw new FormatException("missing 'id'.");
        }

        where = $"{modletId}/{rule.Id}";
        rule.Enabled = !map.TryGetValue("enabled", out object? enabled) || !string.Equals(enabled?.ToString(), "false", StringComparison.OrdinalIgnoreCase);
        if (map.TryGetValue("cooldown", out object? cooldown) && cooldown != null)
        {
            if (!NodeValues.TryNumber(cooldown.ToString(), out float seconds))
            {
                throw new FormatException($"cooldown '{cooldown}' is not a number of seconds.");
            }

            rule.Cooldown = seconds;
        }

        if (!map.TryGetValue("when", out object? when) || when == null)
        {
            throw new FormatException("missing 'when' (the trigger).");
        }

        if (when is Dictionary<object, object> whenMap)
        {
            foreach (var kv in whenMap)
            {
                string key = kv.Key.ToString();
                if (key.Equals("event", StringComparison.OrdinalIgnoreCase))
                {
                    rule.Event = kv.Value?.ToString() ?? "";
                }
                else
                {
                    rule.Trigger[key] = ScalarText(kv.Value, key);
                }
            }
        }
        else
        {
            rule.Event = when.ToString();
        }

        if (!NodeRegistry.TryGet(NodeKind.Trigger, rule.Event, out NodeDef trigger))
        {
            throw new FormatException($"unknown trigger event '{rule.Event}'. Known: {string.Join(", ", NodeRegistry.All(NodeKind.Trigger).Select(d => d.Name))}.");
        }

        rule.Event = trigger.Name;
        CheckArgs(trigger, rule.Trigger, "trigger");

        rule.Conditions = Steps(map, "if", NodeKind.Condition);
        rule.Actions = Steps(map, "do", NodeKind.Action);
        rule.Else = Steps(map, "else", NodeKind.Action);
        if (rule.Actions.Count == 0 && rule.Else.Count == 0)
        {
            throw new FormatException("has no actions ('do:' list).");
        }

        return rule;
    }

    private static List<NodeStep> Steps(Dictionary<object, object> map, string key, NodeKind kind)
    {
        if (!map.TryGetValue(key, out object? value) || value == null)
        {
            return new List<NodeStep>();
        }

        if (value is not List<object> list)
        {
            throw new FormatException($"'{key}:' must be a list of nodes.");
        }

        return list.Select(item => Step(item, kind)).ToList();
    }

    private static NodeStep Step(object item, NodeKind kind)
    {
        string kindName = kind == NodeKind.Condition ? "condition" : "action";
        if (item is not Dictionary<object, object> map || map.Count != 1)
        {
            throw new FormatException($"each {kindName} must be a one-key map like '- give: {{ item: Coins }}'.");
        }

        var pair = map.First();
        string type = pair.Key.ToString();
        if (!NodeRegistry.TryGet(kind, type, out NodeDef def))
        {
            throw new FormatException($"unknown {kindName} '{type}'. Known: {string.Join(", ", NodeRegistry.All(kind).Select(d => d.Name))}.");
        }

        var step = new NodeStep { Type = def.Name };
        if (pair.Value is Dictionary<object, object> args)
        {
            foreach (var kv in args)
            {
                string argName = kv.Key.ToString();
                if (def.BranchKey.Length > 0 && argName.Equals(def.BranchKey, StringComparison.OrdinalIgnoreCase))
                {
                    step.Branches = Branches(kv.Value, def);
                }
                else
                {
                    step.Args[argName] = ScalarText(kv.Value, argName);
                }
            }
        }
        else if (pair.Value != null)
        {
            if (def.Shorthand.Length == 0)
            {
                throw new FormatException($"'{type}' needs named args, e.g. '- {type}: {{ ... }}'.");
            }

            step.Args[def.Shorthand] = ScalarText(pair.Value, def.Shorthand);
        }

        CheckArgs(def, step.Args, $"{kindName} '{type}'");
        return step;
    }

    private static List<NodeBranch> Branches(object? value, NodeDef def)
    {
        if (value is not List<object> list)
        {
            throw new FormatException($"'{def.BranchKey}:' must be a list.");
        }

        var branches = new List<NodeBranch>();
        foreach (object entry in list)
        {
            if (entry is not Dictionary<object, object> map)
            {
                throw new FormatException($"each entry in '{def.BranchKey}:' needs 'label:' and optionally 'do:'.");
            }

            branches.Add(new NodeBranch { Label = Scalar(map, "label"), Steps = Steps(map, "do", NodeKind.Action) });
        }

        return branches;
    }

    private static void CheckArgs(NodeDef def, Dictionary<string, string> args, string what)
    {
        foreach (NodeArgDef arg in def.Args.Where(a => a.Required))
        {
            if (!args.TryGetValue(arg.Name, out string? v) || string.IsNullOrWhiteSpace(v))
            {
                throw new FormatException($"{what} is missing '{arg.Name}' ({arg.Help}).");
            }
        }

        foreach (string name in args.Keys)
        {
            if (!name.Equals("not", StringComparison.OrdinalIgnoreCase) && def.Args.All(a => !a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new FormatException($"{what} has no arg '{name}'. Known: {string.Join(", ", def.Args.Select(a => a.Name))}.");
            }
        }
    }

    private static string Scalar(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out object? v) && v != null ? ScalarText(v, key) : "";

    private static string ScalarText(object? value, string key)
    {
        if (value is Dictionary<object, object> || value is List<object>)
        {
            throw new FormatException($"'{key}' must be a single value, not a list or map.");
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private static bool IsYaml(string path) =>
        path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
}

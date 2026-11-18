using System;
using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Node.Schema;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Runtime;

/// <summary>
/// Holds the active rule set (local on the authoritative side, the server's copy on a joined client) and
/// runs a rule chain whenever its trigger event fires for the local player.
/// </summary>
internal static class RuleEngine
{
    private static NodeRuleSet _set = new();
    private static Dictionary<string, List<NodeRule>> _byEvent = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, float> LastFired = new();
    private static readonly HashSet<string> ReportedFailures = new();

    public static NodeRuleSet Current => _set;
    public static int ActiveCount { get; private set; }

    /// <summary>Raised after a new set is adopted (the admin panel refreshes from it).</summary>
    public static event Action? Changed;

    public static void SetRules(NodeRuleSet set)
    {
        _set = set;
        _byEvent = set.Rules
            .Where(r => r.Active)
            .GroupBy(r => r.Event, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        ActiveCount = _byEvent.Values.Sum(l => l.Count);
        ReportedFailures.Clear();
        Changed?.Invoke();
    }

    public static IReadOnlyList<NodeRule> RulesFor(string evt) =>
        _byEvent.TryGetValue(evt, out List<NodeRule>? rules) ? rules : (IReadOnlyList<NodeRule>)Array.Empty<NodeRule>();

    public static bool HasRules(string evt) => _byEvent.ContainsKey(evt);

    /// <summary>Fire <paramref name="evt"/> for the local player. <paramref name="filter"/> lets a trigger veto per rule (e.g. "every" timing).</summary>
    public static void Fire(string evt, ItemDrop.ItemData? item, string itemId, GameObject? target = null, Func<NodeRule, bool>? filter = null)
    {
        if (!_byEvent.TryGetValue(evt, out List<NodeRule>? rules))
        {
            return;
        }

        Player? player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }

        string targetName = NodeValues.PrefabName(target);
        foreach (NodeRule rule in rules)
        {
            if (!NodeValues.Matches(Trig(rule, "item"), itemId) || !NodeValues.Matches(Trig(rule, "target"), targetName))
            {
                continue;
            }

            if (filter != null && !filter(rule))
            {
                continue;
            }

            if (rule.Cooldown > 0f && LastFired.TryGetValue(rule.Key, out float last) && Time.time - last < rule.Cooldown)
            {
                continue;
            }

            var ctx = new NodeContext
            {
                Event = evt,
                Player = player,
                Item = item,
                ItemId = itemId,
                Target = target,
                TargetName = targetName,
                Rule = rule
            };
            Run(rule, ctx);
        }
    }

    public static string Trig(NodeRule rule, string arg) => rule.Trigger.TryGetValue(arg, out string? v) ? v : "";

    private static void Run(NodeRule rule, NodeContext ctx)
    {
        try
        {
            bool pass = rule.Conditions.All(c => Check(c, ctx));
            List<NodeStep> chain = pass ? rule.Actions : rule.Else;
            if (chain.Count == 0)
            {
                return;
            }

            if (rule.Cooldown > 0f)
            {
                LastFired[rule.Key] = Time.time;
            }

            ModLog.Debug($"Rule '{rule.Key}' fired on '{ctx.Event}' ({(pass ? "do" : "else")}, {chain.Count} action(s)).");
            RunSteps(chain, ctx);
        }
        catch (Exception ex)
        {
            // Once per rule per rule set: a broken rule on an interval would otherwise flood the log.
            if (ReportedFailures.Add(rule.Key))
            {
                ModLog.Error($"Rule '{rule.Key}' failed on '{ctx.Event}': {ex.Message}");
            }
        }
    }

    public static void RunSteps(IEnumerable<NodeStep> steps, NodeContext ctx)
    {
        foreach (NodeStep step in steps)
        {
            if (!NodeRegistry.TryGet(NodeKind.Action, step.Type, out NodeDef def) || def.Run == null)
            {
                throw new InvalidOperationException($"unknown action '{step.Type}'.");
            }

            def.Run(ctx, step);
        }
    }

    private static bool Check(NodeStep step, NodeContext ctx)
    {
        if (!NodeRegistry.TryGet(NodeKind.Condition, step.Type, out NodeDef def) || def.Check == null)
        {
            throw new InvalidOperationException($"unknown condition '{step.Type}'.");
        }

        bool result = def.Check(ctx, step);
        return NodeValues.Bool(step, "not") ? !result : result;
    }
}

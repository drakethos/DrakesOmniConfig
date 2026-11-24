using System;
using System.Linq;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>Built-in conditions. Any condition also takes <c>not: true</c> to invert it.</summary>
internal static class Conditions
{
    public static void Register()
    {
        Add("item", "The event's item matches a pattern.", "pattern", (c, s) => NodeValues.Matches(NodeValues.Raw(s, "pattern"), c.ItemId),
            NodeRegistry.Arg("pattern", "item", "Item prefab name or pattern.", required: true));

        Add("target", "What was hit matches a pattern (hit trigger).", "pattern", (c, s) => NodeValues.Matches(NodeValues.Raw(s, "pattern"), c.TargetName),
            NodeRegistry.Arg("pattern", "prefab", "Prefab name or pattern.", required: true));

        Add("chance", "Passes this percent of the time.", "percent", (c, s) => UnityEngine.Random.value * 100f < NodeValues.Number(s, "percent"),
            NodeRegistry.Arg("percent", "percent", "0-100.", required: true));

        Add("quality", "The event's item quality (upgrade level) is within a range.", "min", Quality,
            NodeRegistry.Arg("min", "number", "Lowest quality that passes.", def: "1"),
            NodeRegistry.Arg("max", "number", "Highest quality that passes (empty = no limit)."));

        Add("hasItem", "The player carries at least this many of an item.", "item", HasItem,
            NodeRegistry.Arg("item", "item", "Item prefab name.", required: true),
            NodeRegistry.Arg("amount", "number", "How many.", def: "1"));

        Add("equipped", "Something the player has equipped matches a pattern.", "item",
            (c, s) => c.Player.GetInventory().GetEquippedItems().Any(i => NodeValues.Matches(NodeValues.Raw(s, "item"), NodeValues.ItemId(i))),
            NodeRegistry.Arg("item", "item", "Item prefab name or pattern.", required: true));

        Add("health", "The player's health is below and/or above a value (number, or % of max).", "below", Health,
            NodeRegistry.Arg("below", "percent", "Passes when health is under this (e.g. 30%)."),
            NodeRegistry.Arg("above", "percent", "Passes when health is over this."));

        Add("biome", "The player is in one of these biomes.", "is",
            (c, s) => NodeValues.Matches(NodeValues.Raw(s, "is"), c.Player.GetCurrentBiome().ToString()),
            NodeRegistry.Arg("is", "biome", "Biome name or pattern: Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, AshLands, DeepNorth, Ocean.", required: true));

        Add("hasStatus", "The player has this status effect active.", "effect",
            (c, s) => c.Player.GetSEMan().GetStatusEffects().Any(e => e != null && NodeValues.Matches(NodeValues.Raw(s, "effect"), e.name)),
            NodeRegistry.Arg("effect", "statusEffect", "Status effect name or pattern (Wet, Rested, Potion_*).", required: true));

        Add("skillLevel", "One of the player's skills is within a range.", "skill", SkillLevel,
            NodeRegistry.Arg("skill", "skill", "Skill name (Swords, Run, ...).", required: true),
            NodeRegistry.Arg("min", "number", "Lowest level that passes.", def: "0"),
            NodeRegistry.Arg("max", "number", "Highest level that passes (empty = no limit)."));

        Add("time", "It is day or night.", "is", (c, s) =>
            {
                string want = NodeValues.Raw(s, "is").Trim();
                bool night = EnvMan.IsNight();
                return want.Equals("night", StringComparison.OrdinalIgnoreCase) ? night : !night;
            },
            NodeRegistry.Arg("is", "choice", "day or night.", true, "day", "day", "night"));
    }

    private static void Add(string name, string help, string shorthand, Func<NodeContext, NodeStep, bool> check, params NodeArgDef[] args) =>
        NodeRegistry.Register(new NodeDef
        {
            Kind = NodeKind.Condition,
            Name = name,
            Help = help,
            Shorthand = shorthand,
            Args = args.ToList(),
            Check = check
        });

    private static bool Quality(NodeContext c, NodeStep s)
    {
        if (c.Item == null)
        {
            return false;
        }

        int q = c.Item.m_quality;
        return q >= NodeValues.Int(s, "min", 1) && (!NodeValues.Has(s, "max") || q <= NodeValues.Int(s, "max"));
    }

    private static bool SkillLevel(NodeContext c, NodeStep s)
    {
        string name = NodeValues.Raw(s, "skill");
        if (!Enum.TryParse(name, true, out Skills.SkillType skill) || skill == Skills.SkillType.None)
        {
            throw new InvalidOperationException($"no skill named '{name}'.");
        }

        float level = c.Player.GetSkills().GetSkillLevel(skill);
        return level >= NodeValues.Number(s, "min", 0f) && (!NodeValues.Has(s, "max") || level <= NodeValues.Number(s, "max"));
    }

    private static bool HasItem(NodeContext c, NodeStep s)
    {
        string id = NodeValues.Raw(s, "item");
        int need = Mathf.Max(1, NodeValues.Int(s, "amount", 1));
        int have = c.Player.GetInventory().GetAllItems().Where(i => NodeValues.ItemId(i).Equals(id, StringComparison.OrdinalIgnoreCase)).Sum(i => i.m_stack);
        return have >= need;
    }

    private static bool Health(NodeContext c, NodeStep s)
    {
        float hp = c.Player.GetHealth();
        float max = c.Player.GetMaxHealth();
        bool ok = true;
        if (NodeValues.Has(s, "below"))
        {
            ok &= hp < Limit(NodeValues.Raw(s, "below"), max);
        }

        if (NodeValues.Has(s, "above"))
        {
            ok &= hp > Limit(NodeValues.Raw(s, "above"), max);
        }

        return ok;
    }

    private static float Limit(string raw, float max)
    {
        NodeValues.TryNumber(raw, out float n);
        return NodeValues.IsPercent(raw) ? max * n / 100f : n;
    }
}

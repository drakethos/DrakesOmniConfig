using System;
using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>
/// Actions that reach past the local player: players and creatures in a radius, and the thing the event hit.
/// Damage and healing go through the game's own Damage/Heal, which route to the owner's client themselves.
/// Status effects only exist on the local client, so there is no area status action.
/// </summary>
internal static class AreaActions
{
    private const string WhoHelp = "enemies (wild creatures), players (other players), or all.";

    public static void Register()
    {
        Add("aoeDamage", "Damage creatures and/or other players within a radius of the player.", "amount", AoeDamage,
            NodeRegistry.Arg("amount", "percent", "Damage per target (number).", required: true),
            NodeRegistry.Arg("radius", "number", "Metres.", def: "8"),
            NodeRegistry.Arg("who", "choice", WhoHelp, false, "enemies", "enemies", "players", "all"));

        Add("aoeHeal", "Heal the player and other players within a radius (number, or % of each one's max health).", "amount", AoeHeal,
            NodeRegistry.Arg("amount", "percent", "e.g. 20 or 20%.", required: true),
            NodeRegistry.Arg("radius", "number", "Metres.", def: "10"),
            NodeRegistry.Arg("self", "bool", "Include the triggering player.", def: "true"));

        Add("damageTarget", "Damage the creature the event is about (hit, kill is gone by then). Number, or % of its max health.", "amount", DamageTarget,
            NodeRegistry.Arg("amount", "percent", "e.g. 20 or 50%.", required: true));

        Add("healTarget", "Heal the creature the event is about (tamed or not).", "amount", HealTarget,
            NodeRegistry.Arg("amount", "percent", "e.g. 20 or 50%.", required: true));
    }

    private static void Add(string name, string help, string shorthand, Action<NodeContext, NodeStep> run, params NodeArgDef[] args) =>
        NodeRegistry.Register(new NodeDef { Kind = NodeKind.Action, Name = name, Help = help, Shorthand = shorthand, Args = args.ToList(), Run = run });

    private static float Amount(string raw, float max)
    {
        NodeValues.TryNumber(raw, out float n);
        return NodeValues.IsPercent(raw) ? max * n / 100f : n;
    }

    private static IEnumerable<Character> Within(NodeContext c, float radius)
    {
        Vector3 origin = c.Player.transform.position;
        return Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && (ch.transform.position - origin).sqrMagnitude <= radius * radius).ToList();
    }

    private static void AoeDamage(NodeContext c, NodeStep s)
    {
        string who = NodeValues.Raw(s, "who", "enemies").Trim().ToLowerInvariant();
        float radius = Mathf.Clamp(NodeValues.Number(s, "radius", 8f), 0.5f, 100f);
        foreach (Character ch in Within(c, radius))
        {
            bool isPlayer = ch.IsPlayer();
            if (ch == c.Player || (who == "enemies" && (isPlayer || ch.IsTamed())) || (who == "players" && !isPlayer))
            {
                continue;
            }

            Hurt(c, ch, Amount(NodeValues.Raw(s, "amount"), ch.GetMaxHealth()));
        }
    }

    private static void AoeHeal(NodeContext c, NodeStep s)
    {
        float radius = Mathf.Clamp(NodeValues.Number(s, "radius", 10f), 0.5f, 100f);
        bool self = NodeValues.Bool(s, "self", true);
        foreach (Character ch in Within(c, radius).Where(ch => ch.IsPlayer()))
        {
            if (ch != c.Player || self)
            {
                ch.Heal(Amount(NodeValues.Raw(s, "amount"), ch.GetMaxHealth()), true);
            }
        }
    }

    private static Character TargetCharacter(NodeContext c) =>
        (c.Target != null ? c.Target.GetComponent<Character>() : null)
        ?? throw new InvalidOperationException("the event's target is not a creature (use hit with a creature target).");

    private static void DamageTarget(NodeContext c, NodeStep s)
    {
        Character ch = TargetCharacter(c);
        Hurt(c, ch, Amount(NodeValues.Raw(s, "amount"), ch.GetMaxHealth()));
    }

    private static void HealTarget(NodeContext c, NodeStep s)
    {
        Character ch = TargetCharacter(c);
        ch.Heal(Amount(NodeValues.Raw(s, "amount"), ch.GetMaxHealth()), true);
    }

    private static void Hurt(NodeContext c, Character victim, float amount)
    {
        var hit = new HitData { m_point = victim.transform.position };
        hit.m_damage.m_damage = amount;
        hit.SetAttacker(c.Player);
        RuleEngine.Quiet++;
        try
        {
            victim.Damage(hit);
        }
        finally
        {
            RuleEngine.Quiet--;
        }
    }
}

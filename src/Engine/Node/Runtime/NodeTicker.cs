using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Engine.Node.Library;
using DrakesOmniConfig.Engine.Node.Schema;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Runtime;

/// <summary>
/// Polls the triggers that have no single game method to patch: spawn (local player changed), interval
/// (per-rule timers) and broke for items the game doesn't break through DrainEquipedItemDurability (armor,
/// durability set by another mod). Idle when no rule uses them.
/// </summary>
internal sealed class NodeTicker : MonoBehaviour
{
    private const float Step = 0.25f;
    private const float SpawnDelay = 1f;

    private static readonly Dictionary<ItemDrop.ItemData, float> Durability = new();
    private static readonly HashSet<ItemDrop.ItemData> ReportedBroken = new();

    private Player? _player;
    private float _spawnAt = -1f;
    private float _next;
    private readonly Dictionary<string, float> _intervalNext = new();

    /// <summary>Fires "broke" once per break, whether the patch or the poll sees it first.</summary>
    public static void ReportBroke(ItemDrop.ItemData item)
    {
        if (!ReportedBroken.Add(item))
        {
            return;
        }

        Durability[item] = item.m_durability;
        RuleEngine.Fire("broke", item, NodeValues.ItemId(item));
    }

    private void Update()
    {
        Player? current = Player.m_localPlayer;
        if (current != _player)
        {
            _player = current;
            Durability.Clear();
            ReportedBroken.Clear();
            _intervalNext.Clear();
            WorldWatch.Reset();
            // a moment after spawning, so the saved inventory and equipment are in place first
            _spawnAt = current != null ? Time.time + SpawnDelay : -1f;
        }

        if (current == null || Time.time < _next)
        {
            return;
        }

        _next = Time.time + Step;
        if (_spawnAt >= 0f && Time.time >= _spawnAt)
        {
            _spawnAt = -1f;
            RuleEngine.Fire("spawn", null, "");
        }

        if (RuleEngine.HasRules("interval"))
        {
            Intervals(current);
        }

        if (RuleEngine.HasRules("broke"))
        {
            Broke(current);
        }

        WorldWatch.Poll(current);
    }

    private void Intervals(Player player)
    {
        foreach (NodeRule rule in RuleEngine.RulesFor("interval"))
        {
            NodeValues.TryNumber(RuleEngine.Trig(rule, "every"), out float every);
            every = Mathf.Max(0.5f, every);
            if (!_intervalNext.TryGetValue(rule.Key, out float due))
            {
                _intervalNext[rule.Key] = Time.time + every;
                continue;
            }

            if (Time.time < due)
            {
                continue;
            }

            _intervalNext[rule.Key] = Time.time + every;
            string pattern = RuleEngine.Trig(rule, "item");
            ItemDrop.ItemData? item = null;
            if (pattern.Length > 0)
            {
                item = player.GetInventory().GetEquippedItems().FirstOrDefault(i => NodeValues.Matches(pattern, NodeValues.ItemId(i)));
                if (item == null)
                {
                    continue;
                }
            }

            NodeRule only = rule;
            RuleEngine.Fire("interval", item, NodeValues.ItemId(item), null, r => r == only);
        }
    }

    private static void Broke(Player player)
    {
        List<ItemDrop.ItemData> equipped = player.GetInventory().GetEquippedItems();
        // tracked items too: one that broke and got unequipped between two polls still counts
        foreach (ItemDrop.ItemData item in equipped.Concat(Durability.Keys).Distinct().ToList())
        {
            if (!item.m_shared.m_useDurability)
            {
                continue;
            }

            float now = item.m_durability;
            if (now > 0f)
            {
                ReportedBroken.Remove(item);
            }
            else if (Durability.TryGetValue(item, out float before) && before > 0f)
            {
                ReportBroke(item);
            }

            if (equipped.Contains(item))
            {
                Durability[item] = now;
            }
            else
            {
                Durability.Remove(item);
            }
        }
    }
}

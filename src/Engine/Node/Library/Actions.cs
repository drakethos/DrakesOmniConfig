using System;
using System.Linq;
using DrakesOmniConfig.Config.Settings;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>Built-in item, player and world actions. All run on the local player's own client.</summary>
internal static class Actions
{
    public static void Register()
    {
        Add("give", "Give the player an item (dropped at their feet if the inventory is full).", "item", Give,
            NodeRegistry.Arg("item", "item", "Item prefab name.", required: true),
            NodeRegistry.Arg("amount", "number", "How many.", def: "1"),
            NodeRegistry.Arg("quality", "number", "Quality (upgrade level).", def: "1"));

        Add("remove", "Take an item from the player's inventory.", "item", Remove,
            NodeRegistry.Arg("item", "item", "Item prefab name. Empty = the event's own item."),
            NodeRegistry.Arg("amount", "number", "How many.", def: "1"));

        Add("durability", "Change the event's item durability: +25, -10%, =100%.", "change", Durability,
            NodeRegistry.Arg("change", "text", "+N / -N / =N, with % meaning percent of max durability.", required: true));

        Add("status", "Give the player a status effect (Rested, Wet, Burning, Potion_health_minor, ...).", "effect", Status,
            NodeRegistry.Arg("effect", "statusEffect", "Status effect name.", required: true),
            NodeRegistry.Arg("duration", "number", "Seconds (empty = the effect's own length)."));

        Add("removeStatus", "Remove a status effect from the player.", "effect", RemoveStatus,
            NodeRegistry.Arg("effect", "statusEffect", "Status effect name.", required: true));

        Add("heal", "Heal the player (number, or % of max health).", "amount",
            (c, s) => c.Player.Heal(Amount(NodeValues.Raw(s, "amount"), c.Player.GetMaxHealth()), true),
            NodeRegistry.Arg("amount", "percent", "e.g. 25 or 25%.", required: true));

        Add("damage", "Damage the player (number, or % of max health). Armor does not reduce it.", "amount", Damage,
            NodeRegistry.Arg("amount", "percent", "e.g. 10 or 10%.", required: true));

        Add("stamina", "Change stamina: +20, -15, =100%.", "change", Stamina,
            NodeRegistry.Arg("change", "text", "+N / -N / =N, with % meaning percent of max stamina.", required: true));

        Add("skill", "Raise one of the player's skills.", "skill", Skill,
            NodeRegistry.Arg("skill", "skill", "Skill name: Swords, Axes, Pickaxes, WoodCutting, Run, Jump, Sneak, Blocking, Bows, ...", required: true),
            NodeRegistry.Arg("amount", "number", "Experience to add.", def: "1"));

        Add("effect", "Play a visual/sound effect prefab at the player (vfx_..., sfx_..., fx_...).", "prefab",
            (c, s) => SpawnAt(NodeValues.Raw(s, "prefab"), c.Player.transform.position, c.Player.transform.rotation),
            NodeRegistry.Arg("prefab", "prefab", "Effect prefab name.", required: true));

        Add("spawn", "Spawn a prefab (creature, item, object) in front of the player.", "prefab", Spawn,
            NodeRegistry.Arg("prefab", "prefab", "Prefab name.", required: true),
            NodeRegistry.Arg("amount", "number", "How many.", def: "1"),
            NodeRegistry.Arg("distance", "number", "Metres in front of the player.", def: "2"));
    }

    private static void Add(string name, string help, string shorthand, Action<NodeContext, NodeStep> run, params NodeArgDef[] args) =>
        NodeRegistry.Register(new NodeDef
        {
            Kind = NodeKind.Action,
            Name = name,
            Help = help,
            Shorthand = shorthand,
            Args = args.ToList(),
            Run = run
        });

    private static float Amount(string raw, float max)
    {
        NodeValues.TryNumber(raw, out float n);
        return NodeValues.IsPercent(raw) ? max * n / 100f : n;
    }

    private static GameObject ItemPrefab(string id)
    {
        GameObject? prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(id) : null;
        if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
        {
            throw new InvalidOperationException($"'{id}' is not an item prefab.");
        }

        return prefab;
    }

    private static void Give(NodeContext c, NodeStep s)
    {
        string id = NodeValues.Raw(s, "item");
        GameObject prefab = ItemPrefab(id);
        int remaining = Mathf.Max(1, NodeValues.Int(s, "amount", 1));
        int quality = Mathf.Max(1, NodeValues.Int(s, "quality", 1));
        int maxStack = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
        Inventory inventory = c.Player.GetInventory();

        while (remaining > 0)
        {
            int chunk = Mathf.Min(remaining, maxStack);
            remaining -= chunk;
            if (GameCompat.Call(inventory, "AddItem", prefab.name, chunk, quality, 0, 0L, "", false) != null)
            {
                continue;
            }

            Transform t = c.Player.transform;
            GameObject drop = Object.Instantiate(prefab, t.position + t.forward + Vector3.up, Quaternion.identity);
            ItemDrop itemDrop = drop.GetComponent<ItemDrop>();
            itemDrop.SetStack(chunk);
            itemDrop.m_itemData.m_quality = quality;
        }
    }

    private static void Remove(NodeContext c, NodeStep s)
    {
        Inventory inventory = c.Player.GetInventory();
        int amount = Mathf.Max(1, NodeValues.Int(s, "amount", 1));
        string id = NodeValues.Raw(s, "item");
        if (id.Length == 0)
        {
            if (c.Item != null)
            {
                inventory.RemoveItem(c.Item, Mathf.Min(amount, c.Item.m_stack));
            }

            return;
        }

        foreach (ItemDrop.ItemData item in inventory.GetAllItems().Where(i => NodeValues.ItemId(i).Equals(id, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (amount <= 0)
            {
                break;
            }

            int take = Mathf.Min(amount, item.m_stack);
            inventory.RemoveItem(item, take);
            amount -= take;
        }
    }

    private static void Durability(NodeContext c, NodeStep s)
    {
        ItemDrop.ItemData? item = c.Item;
        if (item == null || !item.m_shared.m_useDurability)
        {
            ModLog.Debug($"Rule '{c.Rule.Key}': durability skipped, the event's item has no durability.");
            return;
        }

        float max = item.GetMaxDurability();
        item.m_durability = Mathf.Clamp(NodeValues.ApplyChange(NodeValues.Raw(s, "change"), item.m_durability, max), 0f, max);
    }

    private static StatusEffect FindEffect(string name)
    {
        StatusEffect? se = ObjectDB.instance?.m_StatusEffects.FirstOrDefault(e => e != null && e.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return se ?? throw new InvalidOperationException($"no status effect named '{name}'.");
    }

    private static void Status(NodeContext c, NodeStep s)
    {
        var added = GameCompat.Call(c.Player.GetSEMan(), "AddStatusEffect", FindEffect(NodeValues.Raw(s, "effect")), true) as StatusEffect;
        if (added != null && NodeValues.Has(s, "duration"))
        {
            added.m_ttl = NodeValues.Number(s, "duration");
        }
    }

    private static void RemoveStatus(NodeContext c, NodeStep s)
    {
        c.Player.GetSEMan().RemoveStatusEffect(FindEffect(NodeValues.Raw(s, "effect")).NameHash(), false);
    }

    private static void Damage(NodeContext c, NodeStep s)
    {
        var hit = new HitData();
        hit.m_damage.m_damage = Amount(NodeValues.Raw(s, "amount"), c.Player.GetMaxHealth());
        hit.m_point = c.Player.transform.position;
        c.Player.Damage(hit);
    }

    private static void Stamina(NodeContext c, NodeStep s)
    {
        float current = c.Player.GetStamina();
        float target = NodeValues.ApplyChange(NodeValues.Raw(s, "change"), current, c.Player.GetMaxStamina());
        float delta = target - current;
        if (delta > 0f)
        {
            c.Player.AddStamina(delta);
        }
        else if (delta < 0f)
        {
            c.Player.UseStamina(-delta);
        }
    }

    private static void Skill(NodeContext c, NodeStep s)
    {
        string name = NodeValues.Raw(s, "skill");
        if (!Enum.TryParse(name, true, out Skills.SkillType skill) || skill == Skills.SkillType.None)
        {
            throw new InvalidOperationException($"no skill named '{name}'.");
        }

        c.Player.RaiseSkill(skill, Mathf.Max(0f, NodeValues.Number(s, "amount", 1f)));
    }

    private static void Spawn(NodeContext c, NodeStep s)
    {
        Transform t = c.Player.transform;
        int amount = Mathf.Clamp(NodeValues.Int(s, "amount", 1), 1, 50);
        float distance = NodeValues.Number(s, "distance", 2f);
        for (int i = 0; i < amount; i++)
        {
            Vector3 spread = amount > 1 ? Quaternion.Euler(0f, 360f * i / amount, 0f) * Vector3.forward * 0.75f : Vector3.zero;
            SpawnAt(NodeValues.Raw(s, "prefab"), t.position + t.forward * distance + spread + Vector3.up * 0.5f, t.rotation);
        }
    }

    private static void SpawnAt(string name, Vector3 position, Quaternion rotation)
    {
        GameObject? prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
        if (prefab == null)
        {
            throw new InvalidOperationException($"no prefab named '{name}'.");
        }

        Object.Instantiate(prefab, position, rotation);
    }
}

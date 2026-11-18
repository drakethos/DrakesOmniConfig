using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Engine.Node.Runtime;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>
/// Trigger definitions plus the Harmony patches that fire them. Every patch bails out first thing when no
/// rule uses its event, so an install with no node rules pays only a dictionary lookup per call.
/// spawn / interval / broke are polled by <see cref="NodeTicker"/> instead of patched.
/// </summary>
internal static class ItemTriggers
{
    public static void Register()
    {
        var item = NodeRegistry.Arg("item", "item", "Item prefab name or pattern (Sword*, Club|Torch). Empty = any item.");
        Trigger("equip", "The player equips an item.", item);
        Trigger("unequip", "The player unequips an item.", item);
        Trigger("pickup", "The player picks an item up from the ground.", item);
        Trigger("craft", "The player crafts or upgrades an item at a station.", item);
        Trigger("consume", "The player eats, drinks or uses up a consumable.", item);
        Trigger("hit", "The player hits something with a weapon or tool.",
            NodeRegistry.Arg("item", "item", "Weapon/tool doing the hitting (pattern). Empty = anything, including bare hands."),
            NodeRegistry.Arg("target", "prefab", "What got hit: creature, rock, tree or piece prefab name or pattern (Boar, Rock*)."));
        Trigger("broke", "An equipped item's durability reaches zero.", item);
        Trigger("interval", "Every few seconds (optionally only while a matching item is equipped).",
            NodeRegistry.Arg("every", "number", "Seconds between runs (0.5 or more).", required: true),
            NodeRegistry.Arg("item", "item", "Only while an equipped item matches this pattern. Empty = always."));
        Trigger("spawn", "The local player spawns: joining the world or respawning after death.");
    }

    private static void Trigger(string name, string help, params NodeArgDef[] args) =>
        NodeRegistry.Register(new NodeDef { Kind = NodeKind.Trigger, Name = name, Help = help, Args = args.ToList() });

    private static bool IsLocal(Humanoid h) => h != null && h == Player.m_localPlayer;

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    private static class EquipPatch
    {
        // triggerEquipEffects is false when the game re-equips a saved loadout on load, which must not fire rules.
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects, bool __result)
        {
            if (__result && triggerEquipEffects && item != null && RuleEngine.HasRules("equip") && IsLocal(__instance))
            {
                RuleEngine.Fire("equip", item, NodeValues.ItemId(item));
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    private static class UnequipPatch
    {
        private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, out bool __state)
        {
            __state = item != null && RuleEngine.HasRules("unequip") && IsLocal(__instance) && __instance.IsItemEquiped(item);
        }

        private static void Postfix(ItemDrop.ItemData item, bool triggerEquipEffects, bool __state)
        {
            if (__state && triggerEquipEffects)
            {
                RuleEngine.Fire("unequip", item, NodeValues.ItemId(item));
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    private static class PickupPatch
    {
        private static void Prefix(Humanoid __instance, GameObject go, out ItemDrop.ItemData? __state)
        {
            __state = null;
            if (go != null && RuleEngine.HasRules("pickup") && IsLocal(__instance))
            {
                __state = go.GetComponent<ItemDrop>()?.m_itemData;
            }
        }

        private static void Postfix(bool __result, ItemDrop.ItemData? __state)
        {
            if (__result && __state != null)
            {
                RuleEngine.Fire("pickup", __state, NodeValues.ItemId(__state));
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    private static class ConsumePatch
    {
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__result && item != null && RuleEngine.HasRules("consume") && IsLocal(__instance))
            {
                RuleEngine.Fire("consume", item, NodeValues.ItemId(item));
            }
        }
    }

    internal sealed class CraftState
    {
        public string ItemId = "";
        public ItemDrop.ItemData? Upgrade;
        public int UpgradeQuality;
        public HashSet<ItemDrop.ItemData> Before = new();
        public int CountBefore;
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    private static class CraftPatch
    {
        private static void Prefix(Player player, Recipe ___m_craftRecipe, ItemDrop.ItemData ___m_craftUpgradeItem, out CraftState? __state)
        {
            __state = null;
            if (player == null || ___m_craftRecipe == null || ___m_craftRecipe.m_item == null || !RuleEngine.HasRules("craft") || !IsLocal(player))
            {
                return;
            }

            string id = ___m_craftRecipe.m_item.gameObject.name;
            List<ItemDrop.ItemData> items = player.GetInventory().GetAllItems();
            __state = new CraftState
            {
                ItemId = id,
                Upgrade = ___m_craftUpgradeItem,
                UpgradeQuality = ___m_craftUpgradeItem?.m_quality ?? 0,
                Before = new HashSet<ItemDrop.ItemData>(items),
                CountBefore = items.Where(i => NodeValues.ItemId(i) == id).Sum(i => i.m_stack)
            };
        }

        private static void Postfix(Player player, CraftState? __state)
        {
            if (__state == null)
            {
                return;
            }

            if (__state.Upgrade != null)
            {
                if (__state.Upgrade.m_quality > __state.UpgradeQuality)
                {
                    RuleEngine.Fire("craft", __state.Upgrade, __state.ItemId);
                }

                return;
            }

            List<ItemDrop.ItemData> items = player.GetInventory().GetAllItems();
            var matching = items.Where(i => NodeValues.ItemId(i) == __state.ItemId).ToList();
            if (matching.Sum(i => i.m_stack) <= __state.CountBefore)
            {
                return; // nothing was made (missing materials, full inventory)
            }

            ItemDrop.ItemData crafted = matching.FirstOrDefault(i => !__state.Before.Contains(i)) ?? matching.Last();
            RuleEngine.Fire("craft", crafted, __state.ItemId);
        }
    }

    // Where tools and weapons really break: the game shows its "broke" message and unequips the item here.
    [HarmonyPatch(typeof(Humanoid), "DrainEquipedItemDurability")]
    private static class BrokePatch
    {
        private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, out bool __state)
        {
            __state = item != null && item.m_durability > 0f && RuleEngine.HasRules("broke") && IsLocal(__instance);
        }

        private static void Postfix(ItemDrop.ItemData item, bool __state)
        {
            if (__state && item.m_durability <= 0f)
            {
                NodeTicker.ReportBroke(item);
            }
        }
    }

    // One prefix per damageable type: each Damage(HitData) runs on the attacker's own client, so filtering on
    // attacker == local player fires once per hit, on the client whose player swung.
    private static void OnHit(Component victim, HitData hit)
    {
        if (hit == null || victim == null || !RuleEngine.HasRules("hit") || Player.m_localPlayer == null)
        {
            return;
        }

        Player player = Player.m_localPlayer;
        if (hit.GetAttacker() != player)
        {
            return;
        }

        ItemDrop.ItemData? weapon = player.GetCurrentWeapon();
        RuleEngine.Fire("hit", weapon, NodeValues.ItemId(weapon), victim.gameObject);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    private static class HitCharacterPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance != Player.m_localPlayer)
            {
                OnHit(__instance, hit);
            }
        }
    }

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
    private static class HitDestructiblePatch
    {
        private static void Prefix(Destructible __instance, HitData hit) => OnHit(__instance, hit);
    }

    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
    private static class HitMineRock5Patch
    {
        private static void Prefix(MineRock5 __instance, HitData hit) => OnHit(__instance, hit);
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
    private static class HitMineRockPatch
    {
        private static void Prefix(MineRock __instance, HitData hit) => OnHit(__instance, hit);
    }

    [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
    private static class HitTreePatch
    {
        private static void Prefix(TreeBase __instance, HitData hit) => OnHit(__instance, hit);
    }

    [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]
    private static class HitLogPatch
    {
        private static void Prefix(TreeLog __instance, HitData hit) => OnHit(__instance, hit);
    }
}

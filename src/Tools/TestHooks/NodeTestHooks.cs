using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Node.Admin;
using DrakesOmniConfig.Engine.Node.Loading;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using DrakesOmniConfig.Engine.Node.Ui;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrakesOmniConfig.Tools.TestHooks;

/// <summary>
/// DrakesTestLab hooks for the node rules. The "do" hooks call the real game methods (EquipItem, ConsumeItem,
/// Pickup, Character.Damage) so the actual trigger patches fire, not a shortcut into RuleEngine.
/// </summary>
internal static class NodeTestHooks
{
    /// <summary>{ command, arg?, payload? | file? } — file is relative to config/DrakesTestLab/suites. Returns whether the server said ok.</summary>
    [TestHook("omniconfig.admin")]
    internal static object? Admin(Dictionary<string, object?> args)
    {
        string payload = Arg(args, "payload");
        string file = Arg(args, "file");
        if (file.Length > 0)
        {
            payload = File.ReadAllText(Path.Combine(Paths.ConfigPath, "DrakesTestLab", "suites", file));
        }

        AdminChannel.Send(Require(args, "command"), Arg(args, "arg"), payload);
        Debug.Log("[omniconfig.admin] " + AdminChannel.LastReply);
        return AdminChannel.LastOk;
    }

    /// <summary>Writes config/DrakesOmniConfig/NodeCatalog.json (the Rules editor's palette) and returns how many nodes it lists.</summary>
    [TestHook("omniconfig.catalog")]
    internal static object? Catalog(Dictionary<string, object?> args)
    {
        NodeCatalog.Write(Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "NodeCatalog.json"));
        return NodeRegistry.All(NodeKind.Trigger).Count() + NodeRegistry.All(NodeKind.Condition).Count() + NodeRegistry.All(NodeKind.Action).Count();
    }

    [TestHook("omniconfig.lastReply")]
    internal static object? LastReply(Dictionary<string, object?> args) => AdminChannel.LastReply;

    /// <summary>{ modlet? } — active rules, all or in one modlet.</summary>
    [TestHook("omniconfig.rules")]
    internal static object? Rules(Dictionary<string, object?> args)
    {
        string modlet = Arg(args, "modlet");
        return RuleEngine.Current.Rules.Count(r => r.Active && (modlet.Length == 0 || r.ModletId == modlet));
    }

    /// <summary>{ modlet } — parses that modlet's nodes/ files even when the modlet is disabled. Returns the rule count, throws with the errors.</summary>
    [TestHook("omniconfig.validate")]
    internal static object? Validate(Dictionary<string, object?> args)
    {
        string id = Require(args, "modlet");
        string folder = Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "Modlets", id, "nodes");
        if (!Directory.Exists(folder))
        {
            throw new InvalidOperationException($"No nodes folder for modlet '{id}'.");
        }

        var vars = new Dictionary<string, object?>();
        string varsPath = Path.Combine(Path.GetDirectoryName(folder)!, "vars.yaml");
        if (File.Exists(varsPath))
        {
            vars = new YamlDotNet.Serialization.DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(File.ReadAllText(varsPath)) ?? vars;
        }

        var loader = new NodeLoader();
        var errors = new List<string>();
        int count = 0;
        foreach (string path in Directory.GetFiles(folder, "*.yaml", SearchOption.AllDirectories))
        {
            count += loader.Parse(ModletLoader.SubstituteVariables(File.ReadAllText(path), vars), id, Path.GetFileName(path), out List<string> e).Count;
            errors.AddRange(e);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" | ", errors));
        }

        return count;
    }

    [TestHook("omniconfig.equip")]
    internal static object? Equip(Dictionary<string, object?> args) => LocalPlayer().EquipItem(Item(args), true);

    [TestHook("omniconfig.equipFresh")]
    internal static object? EquipFresh(Dictionary<string, object?> args) => LocalPlayer().EquipItem(Item(args, fresh: true), true);

    [TestHook("omniconfig.unequip")]
    internal static object? Unequip(Dictionary<string, object?> args)
    {
        LocalPlayer().UnequipItem(Item(args), true);
        return true;
    }

    [TestHook("omniconfig.consume")]
    internal static object? Consume(Dictionary<string, object?> args)
    {
        Player player = LocalPlayer();
        return player.ConsumeItem(player.GetInventory(), Item(args), false);
    }

    /// <summary>{ item } — drops one at the player's feet and picks it up through Humanoid.Pickup.</summary>
    [TestHook("omniconfig.pickup")]
    internal static object? Pickup(Dictionary<string, object?> args)
    {
        Player player = LocalPlayer();
        GameObject prefab = ObjectDB.instance.GetItemPrefab(Require(args, "item")) ?? throw new InvalidOperationException("No such item.");
        GameObject drop = Object.Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity);
        return player.Pickup(drop, false, false);
    }

    /// <summary>{ target } — the nearest creature with that prefab name within 30 m takes a 1-damage hit from the player.</summary>
    [TestHook("omniconfig.hit")]
    internal static object? Hit(Dictionary<string, object?> args)
    {
        Player player = LocalPlayer();
        string target = Require(args, "target");
        Character victim = Character.GetAllCharacters()
            .Where(c => c != player && NodeValues.PrefabName(c.gameObject).Equals(target, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => Vector3.Distance(c.transform.position, player.transform.position))
            .FirstOrDefault(c => Vector3.Distance(c.transform.position, player.transform.position) < 30f)
            ?? throw new InvalidOperationException($"No '{target}' within 30 m.");

        var hit = new HitData();
        hit.m_damage.m_blunt = 1f;
        hit.m_point = victim.transform.position;
        hit.SetAttacker(player);
        victim.Damage(hit);
        return true;
    }

    /// <summary>{ item } — how many the player carries.</summary>
    [TestHook("omniconfig.count")]
    internal static object? Count(Dictionary<string, object?> args)
    {
        string id = Require(args, "item");
        return LocalPlayer().GetInventory().GetAllItems().Where(i => NodeValues.ItemId(i).Equals(id, StringComparison.OrdinalIgnoreCase)).Sum(i => i.m_stack);
    }

    /// <summary>{ item, set? } — durability as a percent of max (rounded); "set" changes it first.</summary>
    [TestHook("omniconfig.durability")]
    internal static object? Durability(Dictionary<string, object?> args)
    {
        ItemDrop.ItemData item = Item(args);
        if (args.TryGetValue("set", out object? set) && set != null)
        {
            item.m_durability = item.GetMaxDurability() * Convert.ToSingle(set, CultureInfo.InvariantCulture) / 100f;
        }

        return Mathf.RoundToInt(100f * item.m_durability / Mathf.Max(0.001f, item.GetMaxDurability()));
    }

    /// <summary>{ item } — wears the equipped item out through the game's own Humanoid.DrainEquipedItemDurability, the way use breaks a tool.</summary>
    [TestHook("omniconfig.wearOut")]
    internal static object? WearOut(Dictionary<string, object?> args)
    {
        Player player = LocalPlayer();
        ItemDrop.ItemData item = Item(args);
        item.m_durability = 0.0001f;
        HarmonyLib.AccessTools.Method(typeof(Humanoid), "DrainEquipedItemDurability").Invoke(player, new object[] { item, 10f });
        return item.m_durability <= 0f;
    }

    [TestHook("omniconfig.hasStatus")]
    internal static object? HasStatus(Dictionary<string, object?> args)
    {
        string name = Require(args, "effect");
        StatusEffect? se = ObjectDB.instance.m_StatusEffects.FirstOrDefault(e => e != null && e.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return se != null && LocalPlayer().GetSEMan().HaveStatusEffect(se.NameHash());
    }

    /// <summary>Opens the admin panel (the same content the Server build's F10 shows) for the current rule set.</summary>
    [TestHook("omniconfig.panel")]
    internal static object? Panel(Dictionary<string, object?> args)
    {
        DialogService.Show(AdminPanelModel.Build(RuleEngine.Current, ""));
        return DialogService.Current?.Title ?? "";
    }

    /// <summary>No args: the open dialog's title ("" when none). { click: "label" }: presses that button.</summary>
    [TestHook("omniconfig.popup")]
    internal static object? Popup(Dictionary<string, object?> args)
    {
        DialogModel? open = DialogService.Current;
        string click = Arg(args, "click");
        if (click.Length == 0)
        {
            return DialogService.IsOpen && open != null ? open.Title : "";
        }

        // "row": only that row's buttons (a panel has a "Turn off" on every row)
        string row = Arg(args, "row");
        var candidates = row.Length > 0
            ? open?.Rows.Where(r => r.Label.Trim().Equals(row, StringComparison.OrdinalIgnoreCase)).SelectMany(r => r.Buttons)
            : open?.Buttons.Concat(open.Rows.SelectMany(r => r.Buttons));
        DialogButton button = candidates?.FirstOrDefault(b => b.Label.Equals(click, StringComparison.OrdinalIgnoreCase))
                              ?? throw new InvalidOperationException($"No open dialog button '{click}'{(row.Length > 0 ? $" on row '{row}'" : "")}.");
        DialogService.Click(button);
        return true;
    }

    /// <summary>{ builtin: true|false } sets the dialog renderer for following popups. No args: the open dialog's renderer.</summary>
    [TestHook("omniconfig.dialogMode")]
    internal static object? DialogMode(Dictionary<string, object?> args)
    {
        if (args.TryGetValue("builtin", out object? b) && b != null)
        {
            DialogService.ForceBuiltIn = Convert.ToBoolean(b, CultureInfo.InvariantCulture);
            return true;
        }

        return DialogService.Renderer;
    }

    private static Player LocalPlayer() => Player.m_localPlayer ?? throw new InvalidOperationException("No local player.");

    /// <summary>The player's first item with that prefab name; "equipped": true or "fresh": true (durability left) narrow it.</summary>
    private static ItemDrop.ItemData Item(Dictionary<string, object?> args, bool fresh = false)
    {
        string id = Require(args, "item");
        bool equipped = Arg(args, "equipped").Equals("true", StringComparison.OrdinalIgnoreCase);
        fresh |= Arg(args, "fresh").Equals("true", StringComparison.OrdinalIgnoreCase);
        return LocalPlayer().GetInventory().GetAllItems().FirstOrDefault(i =>
                   NodeValues.ItemId(i).Equals(id, StringComparison.OrdinalIgnoreCase) &&
                   (!equipped || i.m_equipped) &&
                   (!fresh || !i.m_shared.m_useDurability || i.m_durability > 0f))
               ?? throw new InvalidOperationException($"The player has no matching '{id}'.");
    }

    private static string Arg(Dictionary<string, object?> args, string key) =>
        args.TryGetValue(key, out object? v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

    private static string Require(Dictionary<string, object?> args, string key)
    {
        string v = Arg(args, key);
        return v.Length > 0 ? v : throw new ArgumentException($"needs '{key}'.");
    }
}

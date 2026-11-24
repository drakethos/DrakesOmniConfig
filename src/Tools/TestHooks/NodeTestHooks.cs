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

    /// <summary>
    /// Serialises the live resolved set with the sync writer (hand-made JSON) and with the old YamlDotNet writer, reads
    /// both back and returns how many overrides differ (expect 0), so the faster writer cannot change what clients get.
    /// </summary>
    [TestHook("omniconfig.syncRoundTrip")]
    internal static object? SyncRoundTrip(Dictionary<string, object?> args)
    {
        var live = DrakesOmniConfig.Engine.Orchestration.ModletOrchestrator.CurrentResolved;
        if (live.Count == 0)
        {
            throw new InvalidOperationException("no resolved overrides loaded");
        }

        var json = DrakesOmniConfig.Sync.Transport.ResolvedOverrideSerializer.Deserialize(DrakesOmniConfig.Sync.Transport.ResolvedOverrideSerializer.Serialize(live));
        var yaml = DrakesOmniConfig.Sync.Transport.ResolvedOverrideSerializer.Deserialize(DrakesOmniConfig.Sync.Transport.ResolvedOverrideSerializer.SerializeYaml(live));
        if (json.Count != yaml.Count)
        {
            return Math.Abs(json.Count - yaml.Count) + 1000000;
        }

        int diff = 0;
        for (int i = 0; i < json.Count; i++)
        {
            string a = Key(json[i]), b = Key(yaml[i]);
            if (a != b)
            {
                diff++;
                if (diff <= 5) Debug.LogWarning("[omniconfig.syncRoundTrip] json " + a + " | yaml " + b);
            }
        }

        return diff;

        static string Key(DrakesOmniConfig.Config.Modlets.Schema.ResolvedOverride r) => string.Join("|", r.Target.TypeName, r.Target.Scope, r.Target.Kind,
            r.Target.ItemId, r.Target.GroupName, r.Target.NamePattern, r.Target.Category, r.FieldName, r.Expression.Operator,
            Convert.ToString(r.Expression.Operand, CultureInfo.InvariantCulture), r.Expression.IsPercentage, r.WinningModletId);
    }

    /// <summary>"preload" when the world start used the background start-up load, "load" when it read the modlets itself.</summary>
    [TestHook("omniconfig.loadPath")]
    internal static object? LoadPath(Dictionary<string, object?> args) => DrakesOmniConfig.Engine.Orchestration.ModletOrchestrator.WorldStartLoadPath;

    /// <summary>Whether this world's clones came from the main-menu prebuild: "reused N of M" (and the prebuild's own summary).</summary>
    [TestHook("omniconfig.prebuild")]
    internal static object? Prebuild(Dictionary<string, object?> args)
    {
        Debug.Log($"[omniconfig.prebuild] reused {DrakesOmniConfig.Engine.Hooks.PrefabPassHook.LastReused} of {DrakesOmniConfig.Engine.Hooks.PrefabPassHook.LastClones}; menu: {DrakesOmniConfig.Engine.Hooks.MenuPrebuild.LastSummary}");
        return DrakesOmniConfig.Engine.Hooks.PrefabPassHook.LastReused > 0;
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

    /// <summary>{ on: item|player|target, item?|target?, type?, field } — reads a field off a live object the way the "set" action writes it (item: a carried item; target: a component of the nearest creature).</summary>
    [TestHook("omniconfig.instanceField")]
    internal static object? InstanceField(Dictionary<string, object?> args)
    {
        string on = Require(args, "on").ToLowerInvariant();
        string field = Require(args, "field");
        object instance;
        string typeName;
        switch (on)
        {
            case "item":
                instance = Item(args);
                typeName = "ItemDrop+ItemData";
                break;
            case "player":
                instance = LocalPlayer();
                typeName = "Player";
                break;
            default:
                typeName = Arg(args, "type").Length > 0 ? Arg(args, "type") : "Character";
                instance = Nearest(Require(args, "target")).GetComponent(typeName) ?? throw new InvalidOperationException($"no component '{typeName}'.");
                break;
        }

        Type type = HarmonyLib.AccessTools.TypeByName(typeName) ?? throw new InvalidOperationException($"no type {typeName}.");
        return Convert.ToString(new DrakesOmniConfig.Engine.Fields.Accessors.Reflected.ReflectedFieldAccessor(type, field).Get(instance), CultureInfo.InvariantCulture);
    }

    /// <summary>{ prefab } — the first model renderer's material colour (_Color) on that prefab template, as #RRGGBB.</summary>
    [TestHook("omniconfig.tint")]
    internal static object? Tint(Dictionary<string, object?> args)
    {
        GameObject prefab = ZNetScene.instance.GetPrefab(Require(args, "prefab")) ?? throw new InvalidOperationException("No such prefab.");
        // the model's own renderers, as Forge counts them: a weapon's swing trail or a particle is not the look
        Renderer renderer = prefab.GetComponentsInChildren<Renderer>(true)
                                .FirstOrDefault(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
                            ?? throw new InvalidOperationException("No renderer with a _Color material.");
        Color32 c = renderer.sharedMaterial.color;
        return $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }

    /// <summary>{ prefab, color } — whether that prefab's first material colour is exactly #RRGGBB.</summary>
    [TestHook("omniconfig.tintIs")]
    internal static object? TintIs(Dictionary<string, object?> args) =>
        string.Equals((string)Tint(args)!, Require(args, "color"), StringComparison.OrdinalIgnoreCase);

    /// <summary>{ item } — the recipe that makes this item, as "station level amount: Item xN, ..." ("" when there is none).</summary>
    [TestHook("omniconfig.recipe")]
    internal static object? RecipeFor(Dictionary<string, object?> args)
    {
        string id = Require(args, "item");
        Recipe? r = ObjectDB.instance.m_recipes.FirstOrDefault(x => x != null && x.m_item != null && x.m_item.name == id);
        if (r == null)
        {
            return "";
        }

        string station = r.m_craftingStation != null ? NodeValues.PrefabName(r.m_craftingStation.gameObject) : "hand";
        return $"{station} {r.m_minStationLevel} {r.m_amount}: " + string.Join(", ", r.m_resources.Select(q => $"{q.m_resItem.name} x{q.m_amount}"));
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


    /// <summary>{ target } — kills the nearest creature with that prefab within 30 m through Character.Damage (a real hit by the player).</summary>
    [TestHook("omniconfig.kill")]
    internal static object? Kill(Dictionary<string, object?> args)
    {
        Character victim = Nearest(Require(args, "target"));
        var hit = new HitData();
        hit.m_damage.m_slash = victim.GetMaxHealth() * 10f;
        hit.m_point = victim.transform.position;
        hit.SetAttacker(LocalPlayer());
        victim.Damage(hit);
        return true;
    }

    /// <summary>{ target } — health of the nearest living creature with that prefab (within 30 m) as a percent of max; -1 when none is alive.</summary>
    [TestHook("omniconfig.healthPct")]
    internal static object? HealthPct(Dictionary<string, object?> args)
    {
        string target = Require(args, "target");
        Player player = LocalPlayer();
        Character? c = Character.GetAllCharacters()
            .Where(x => x != null && x != player && !x.IsDead() && NodeValues.PrefabName(x.gameObject).Equals(target, StringComparison.OrdinalIgnoreCase)
                        && Vector3.Distance(x.transform.position, player.transform.position) < 30f)
            .OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault();
        return c == null ? -1 : Mathf.RoundToInt(100f * c.GetHealth() / Mathf.Max(0.001f, c.GetMaxHealth()));
    }

    /// <summary>{ target, percent } — takes that percent of max health off the nearest creature, with no attacker (so no rule sees it).</summary>
    [TestHook("omniconfig.hurt")]
    internal static object? Hurt(Dictionary<string, object?> args)
    {
        Character victim = Nearest(Require(args, "target"));
        victim.SetHealth(victim.GetMaxHealth() * (1f - Convert.ToSingle(Require(args, "percent"), CultureInfo.InvariantCulture) / 100f));
        return true;
    }

    /// <summary>{ set? } — the player's health as a percent of max; "set" changes it first.</summary>
    [TestHook("omniconfig.playerHealth")]
    internal static object? PlayerHealth(Dictionary<string, object?> args)
    {
        Player player = LocalPlayer();
        if (args.TryGetValue("set", out object? set) && set != null)
        {
            player.SetHealth(player.GetMaxHealth() * Convert.ToSingle(set, CultureInfo.InvariantCulture) / 100f);
        }

        return Mathf.RoundToInt(100f * player.GetHealth() / Mathf.Max(0.001f, player.GetMaxHealth()));
    }

    /// <summary>{ piece, float? } — places that piece 3 m in front of the camera through Player.PlacePiece (so the place trigger fires). "float": true keeps it from collapsing in mid-air. Returns the piece's prefab name.</summary>
    [TestHook("omniconfig.place")]
    internal static object? Place(Dictionary<string, object?> args)
    {
        GameObject go = PiecePrefab(Require(args, "piece"));
        Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : LocalPlayer().transform;
        System.Reflection.MethodInfo m = HarmonyLib.AccessTools.Method(typeof(Player), "PlacePiece");
        System.Reflection.ParameterInfo[] ps = m.GetParameters();
        var call = new object?[ps.Length];
        call[0] = go.GetComponent<Piece>();
        call[1] = cam.position + cam.forward * 3f;
        call[2] = Quaternion.identity;
        for (int i = 3; i < ps.Length; i++)
        {
            call[i] = false;
        }

        m.Invoke(LocalPlayer(), call);
        if (Arg(args, "float").Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            // a piece hanging in mid-air would be collapsed by the structural-support check before a test can use it
            var spot = (Vector3)call[1]!;
            Piece? placed = Object.FindObjectsOfType<Piece>().Where(p => p.name.StartsWith(go.name, StringComparison.Ordinal))
                .OrderBy(p => Vector3.Distance(p.transform.position, spot)).FirstOrDefault();
            WearNTear? support = placed != null ? placed.GetComponent<WearNTear>() : null;
            if (support != null)
            {
                Object.Destroy(support);
            }
        }

        return go.name;
    }

    /// <summary>{ piece } — places a piece 3 m ahead of the camera and removes it with the hammer's remove in the same frame (the test camera drifts between frames).</summary>
    [TestHook("omniconfig.placeRemove")]
    internal static object? PlaceRemove(Dictionary<string, object?> args)
    {
        Place(new Dictionary<string, object?> { ["piece"] = Require(args, "piece"), ["float"] = true });
        HarmonyLib.AccessTools.Method(AccessTools_Type("UnityEngine.Physics"), "SyncTransforms").Invoke(null, null);
        return RemoveAhead(args);
    }

    private static Type AccessTools_Type(string name) => HarmonyLib.AccessTools.TypeByName(name) ?? throw new InvalidOperationException($"no type {name}.");

    /// <summary>Takes the piece in front of the camera away through Player.RemovePiece, the hammer's remove. Returns whether the game removed it.</summary>
    [TestHook("omniconfig.removeAhead")]
    internal static object? RemoveAhead(Dictionary<string, object?> args)
    {
        Player player = LocalPlayer();
        Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : player.transform;
        var mask = (int)HarmonyLib.AccessTools.Field(typeof(Player), "m_removeRayMask").GetValue(player);
        Piece? ahead = DrakesOmniConfig.Engine.Node.Library.WorldTriggers.RayPiece(cam.position, cam.forward, mask, player.transform.position + Vector3.up, 50f, out string diag);
        object? removed = HarmonyLib.AccessTools.Method(typeof(Player), "RemovePiece").Invoke(player, Array.Empty<object>());
        if (removed is not true)
        {
            string near = string.Join("; ", Object.FindObjectsOfType<Piece>()
                .Where(p => Vector3.Distance(p.transform.position, cam.position) < 8f)
                .Select(p => $"{p.name} layer {p.gameObject.layer} at {Vector3.Distance(p.transform.position, cam.position):0.0} m, {p.GetComponentsInChildren<Component>().Count(c => c != null && c.GetType().Name.EndsWith("Collider", StringComparison.Ordinal))} collider(s)"));
            throw new InvalidOperationException($"the game did not remove it (pieces within 8 m of the camera: [{near}]; mask {mask}; ray: {diag}; piece {(ahead != null ? ahead.name : "none")}; camera at {cam.position}, looking {cam.forward}, player at {player.transform.position}).");
        }

        return true;
    }

    /// <summary>{ piece } — places a copy 3 m in front of the player and presses use on it through Player.Interact.</summary>
    [TestHook("omniconfig.interact")]
    internal static object? Interact(Dictionary<string, object?> args)
    {
        Player player = LocalPlayer();
        GameObject go = Object.Instantiate(PiecePrefab(Require(args, "piece")), player.transform.position + player.transform.forward * 3f, Quaternion.identity);
        HarmonyLib.AccessTools.Method(typeof(Player), "Interact").Invoke(player, new object[] { go, false, false });
        return true;
    }

    /// <summary>Makes the next poll see a biome change, as if the player had just walked in from somewhere else.</summary>
    [TestHook("omniconfig.biomeChange")]
    internal static object? BiomeChange(Dictionary<string, object?> args)
    {
        Heightmap.Biome now = LocalPlayer().GetCurrentBiome();
        DrakesOmniConfig.Engine.Node.Library.WorldWatch.ForgetBiome(now == Heightmap.Biome.Ocean ? Heightmap.Biome.Meadows : Heightmap.Biome.Ocean);
        return now.ToString();
    }

    private static GameObject PiecePrefab(string name) =>
        ZNetScene.instance.GetPrefab(name) is { } go && go.GetComponent<Piece>() != null ? go : throw new InvalidOperationException($"'{name}' is not a piece prefab.");

    private static Character Nearest(string target)
    {
        Player player = LocalPlayer();
        return Character.GetAllCharacters()
                   .Where(c => c != player && !c.IsDead() && NodeValues.PrefabName(c.gameObject).Equals(target, StringComparison.OrdinalIgnoreCase)
                                && Vector3.Distance(c.transform.position, player.transform.position) < 30f)
                   .OrderBy(c => Vector3.Distance(c.transform.position, player.transform.position)).FirstOrDefault()
               ?? throw new InvalidOperationException($"No '{target}' within 30 m.");
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

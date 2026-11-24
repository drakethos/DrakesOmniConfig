using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Config.Settings;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Hooks;

/// <summary>
/// Makes a new prefab by copying an existing one (a modlet's "clones:" entry), then registers it where the game
/// looks prefabs up: ZNetScene (spawn, console, network) and, for items, ObjectDB (inventory, recipes, give).
/// The copy keeps the source's model, icon and components; the modlet's overrides then change what differs.
/// Re-made on every world load, so a rejoin never sees a stale copy.
/// </summary>
internal static class PrefabCloner
{
    private static GameObject? _holder;
    private static readonly Dictionary<string, GameObject> Made = new();

    /// <summary>Clone id -> its recipe spec ("station|level|amount|Item:n,Item:n") and the Recipe built from it once the game is ready.</summary>
    private static readonly Dictionary<string, string> RecipeSpecs = new();
    private static readonly Dictionary<string, Recipe> Recipes = new();

    // Copies live under an inactive object: they are templates, so none of their Awake/Start code may run.
    private static Transform Holder()
    {
        if (_holder == null)
        {
            _holder = new GameObject("DrakesOmniConfig.Clones");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
        }

        return _holder.transform;
    }

    // Private in the game's real assembly (the publicized reference only helps at compile time), so by reflection.
    private static readonly System.Reflection.FieldInfo? NamedPrefabsField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

    private static Dictionary<int, GameObject>? NamedPrefabs(ZNetScene scene) =>
        NamedPrefabsField?.GetValue(scene) as Dictionary<int, GameObject>;

    private static readonly System.Reflection.FieldInfo? ItemByHashField = AccessTools.Field(typeof(ObjectDB), "m_itemByHash");

    private static Dictionary<int, GameObject>? ItemsByHash() =>
        ObjectDB.instance == null ? null : ItemByHashField?.GetValue(ObjectDB.instance) as Dictionary<int, GameObject>;

    // ---------------------------------------------------------------- main-menu prebuild (MenuPrebuild)

    /// <summary>Clone id -> fingerprint (source + every override aimed at it) of a copy made at the main menu, not yet used by a world.</summary>
    private static readonly Dictionary<string, string> PrebuiltKey = new();

    /// <summary>Clone id -> the overrides (by <see cref="PrefabPassHook.OverrideKey"/>) already applied to its menu copy.</summary>
    private static readonly Dictionary<string, HashSet<string>> PrebuiltApplied = new();

    /// <summary>True when this world pass made a fresh copy of any clone (looks on clones must then be built again).</summary>
    internal static bool AnyRecreated { get; private set; }

    internal static int PrebuiltCount => PrebuiltKey.Count;

    internal static GameObject? TryGet(string id) => Made.TryGetValue(id, out GameObject? clone) && clone != null ? clone : null;

    /// <summary>Main menu: copies an item prefab into the clone holder without registering it anywhere yet.</summary>
    internal static GameObject Prebuild(string id, GameObject source, string key)
    {
        if (Made.TryGetValue(id, out GameObject? old) && old != null)
        {
            if (PrebuiltKey.TryGetValue(id, out string? had) && had == key)
            {
                return old; // already prebuilt from the same definition (menu opened again)
            }

            ObjectDB.instance?.m_items.Remove(old);
            Object.Destroy(old);
        }

        GameObject clone = Object.Instantiate(source, Holder());
        clone.name = id;
        Made[id] = clone;
        if (clone.GetComponent<ItemDrop>() is { } item)
        {
            item.m_itemData.m_dropPrefab = clone;
        }

        PrebuiltKey[id] = key;
        PrebuiltApplied[id] = new HashSet<string>();
        return clone;
    }

    internal static void MarkApplied(string id, string overrideKey)
    {
        if (PrebuiltApplied.TryGetValue(id, out HashSet<string>? applied))
        {
            applied.Add(overrideKey);
        }
    }

    internal static bool WasAppliedAtPrebuild(string? id, string overrideKey) =>
        id != null && PrebuiltApplied.TryGetValue(id, out HashSet<string>? applied) && applied.Contains(overrideKey);

    /// <summary>World pass: registers the menu copy if it was made from the same definition. False = build it the normal way.</summary>
    internal static bool TryReuse(ZNetScene scene, string id, string key)
    {
        if (!PrebuiltKey.TryGetValue(id, out string? had) || had != key || !Made.TryGetValue(id, out GameObject? clone) || clone == null)
        {
            return false;
        }

        GameObject? existing = scene.GetPrefab(id);
        if (existing != null && existing != clone)
        {
            return false; // a real prefab of that name appeared (another mod); the normal path reports it
        }

        Register(scene, id, clone);
        return true;
    }

    /// <summary>
    /// End of a world pass. A menu copy is used by one world only: relative edits (*1.5, +20%) from patterns applied in the
    /// world would compound on a second use, so the next world builds fresh copies (or reuses a new menu prebuild).
    /// </summary>
    internal static void EndPass()
    {
        PrebuiltKey.Clear();
        PrebuiltApplied.Clear();
    }

    internal static void BeginPass() => AnyRecreated = false;

    private static void Register(ZNetScene scene, string id, GameObject clone)
    {
        int hash = id.GetStableHashCode();
        Dictionary<int, GameObject>? named = NamedPrefabs(scene);
        if (named == null || !named.TryGetValue(hash, out GameObject? had) || had != clone)
        {
            scene.m_prefabs.Add(clone);
        }

        if (named != null)
        {
            named[hash] = clone;
        }

        ItemDrop? item = clone.GetComponent<ItemDrop>();
        if (item != null && ObjectDB.instance != null)
        {
            item.m_itemData.m_dropPrefab = clone;
            if (ItemsByHash() is { } byHash)
            {
                if (byHash.TryGetValue(hash, out GameObject? previous) && previous != null && previous != clone)
                {
                    ObjectDB.instance.m_items.Remove(previous);
                }

                if (!byHash.TryGetValue(hash, out GameObject? present) || present != clone)
                {
                    ObjectDB.instance.m_items.Add(clone);
                }

                byHash[hash] = clone;
            }

            RegisterByData(clone);
        }
    }

    /// <summary>Never throws: this runs inside ZNetScene.Awake, and an exception there would leave the world half-built.</summary>
    public static void Create(ZNetScene scene, string id, string from)
    {
        try
        {
            CreateUnchecked(scene, id, from);
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"Clone '{id}' (from '{from}') failed: {ex}");
        }
    }

    private static void CreateUnchecked(ZNetScene scene, string id, string from)
    {
        if (id.Length == 0 || from.Length == 0)
        {
            ModLog.Warning("A clone needs both an id and a source prefab.");
            return;
        }

        GameObject? source = scene.GetPrefab(from);
        if (source == null)
        {
            ModLog.Warning($"Clone '{id}': source prefab '{from}' not found, skipped.");
            return;
        }

        GameObject? existing = scene.GetPrefab(id);
        if (existing != null && !Made.ContainsKey(id))
        {
            ModLog.Warning($"Clone '{id}': a prefab with that name already exists in the game, skipped.");
            return;
        }

        if (Made.TryGetValue(id, out GameObject? old) && old != null)
        {
            Remove(scene, id, old);
        }

        GameObject clone = Object.Instantiate(source, Holder());
        clone.name = id;
        Made[id] = clone;
        AnyRecreated = true;
        PrebuiltKey.Remove(id);
        PrebuiltApplied.Remove(id);

        int hash = id.GetStableHashCode();
        scene.m_prefabs.Add(clone);
        NamedPrefabs(scene)![hash] = clone;

        ItemDrop? item = clone.GetComponent<ItemDrop>();
        if (item != null && ObjectDB.instance != null)
        {
            item.m_itemData.m_dropPrefab = clone;
            // drop a previous copy by its hash, not by scanning every item's name (a native call per item, per clone)
            if (ItemsByHash() is { } byHashNow && byHashNow.TryGetValue(hash, out GameObject? previous) && previous != null && previous != clone)
            {
                ObjectDB.instance.m_items.Remove(previous);
            }

            ObjectDB.instance.m_items.Add(clone);
            ItemsByHash()![hash] = clone;
            RegisterByData(clone);
        }

        ModLog.Debug($"Clone '{id}' made from '{from}'{(item != null ? " (item)" : "")}.");
        if (Recipes.TryGetValue(id, out Recipe? stale) && stale != null)
        {
            // the old recipe points at the previous copy of this clone
            ObjectDB.instance?.m_recipes.Remove(stale);
            Recipes.Remove(id);
        }

        if (Deferred == 0)
        {
            EnsureItemsRegistered();
        }
    }

    /// <summary>
    /// While above 0 (the prefab pass making many clones), Create/SetRecipe skip registering; the pass registers once at
    /// the end. Registering after each clone walked every clone so far: clones² work for a large set.
    /// </summary>
    internal static int Deferred;

    /// <summary>Registers every clone and recipe now (after a deferred batch).</summary>
    internal static void RegisterAll() => EnsureItemsRegistered();

    /// <summary>Remembers a clone's recipe; it is (re)added to ObjectDB whenever the game rebuilds its tables.</summary>
    public static bool IsClone(string? id) => id != null && Made.ContainsKey(id);

    public static void SetRecipe(string id, string spec)
    {
        RecipeSpecs[id] = spec;
        if (Recipes.TryGetValue(id, out Recipe? old) && old != null)
        {
            ObjectDB.instance?.m_recipes.Remove(old);
            Object.Destroy(old);
        }

        Recipes.Remove(id);
        if (Deferred == 0)
        {
            EnsureItemsRegistered();
        }
    }

    private static Recipe? BuildRecipe(string id, string spec, ItemDrop item)
    {
        string[] parts = spec.Split('|');
        if (parts.Length != 4)
        {
            ModLog.Warning($"Clone '{id}': unreadable recipe '{spec}'.");
            return null;
        }

        CraftingStation? station = null;
        if (parts[0].Length > 0)
        {
            station = ZNetScene.instance?.GetPrefab(parts[0])?.GetComponent<CraftingStation>();
            if (station == null)
            {
                return null; // the scene isn't up yet (try again on the next refresh) or the name is wrong (logged then)
            }
        }

        var requirements = new List<Piece.Requirement>();
        foreach (string entry in parts[3].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = entry.Split(':');
            GameObject? res = ObjectDB.instance.GetItemPrefab(kv[0].Trim());
            if (res == null || res.GetComponent<ItemDrop>() == null)
            {
                ModLog.Warning($"Clone '{id}': recipe ingredient '{kv[0]}' is not an item, recipe skipped.");
                return null;
            }

            requirements.Add(new Piece.Requirement
            {
                m_resItem = res.GetComponent<ItemDrop>(),
                m_amount = kv.Length > 1 && int.TryParse(kv[1], out int n) ? n : 1,
                m_recover = true
            });
        }

        Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
        recipe.name = "Recipe_" + id;
        recipe.m_item = item;
        recipe.m_amount = int.TryParse(parts[2], out int amount) ? Mathf.Max(1, amount) : 1;
        recipe.m_minStationLevel = int.TryParse(parts[1], out int level) ? Mathf.Max(1, level) : 1;
        recipe.m_craftingStation = station;
        recipe.m_enabled = true;
        recipe.m_resources = requirements.ToArray();
        ModLog.Debug($"Clone '{id}': recipe at {(station != null ? parts[0] : "hand crafting")} ({parts[3]}).");
        return recipe;
    }

    // ObjectDB.GetItemPrefab(ItemData) looks an item up by its SharedData; the dictionary is absent in some game versions.
    private static readonly System.Reflection.FieldInfo? ItemByDataField = AccessTools.Field(typeof(ObjectDB), "m_itemByData");

    private static void RegisterByData(GameObject clone)
    {
        ItemDrop? item = clone.GetComponent<ItemDrop>();
        if (item != null && ObjectDB.instance != null && ItemByDataField?.GetValue(ObjectDB.instance) is System.Collections.IDictionary byData)
        {
            byData[item.m_itemData.m_shared] = clone;
        }
    }

    /// <summary>
    /// ObjectDB rebuilds its item list and hash table when a scene loads (after ZNetScene.Awake made the clones), which would
    /// drop them; this puts every live clone back whenever the table is refreshed.
    /// </summary>
    private static void EnsureItemsRegistered()
    {
        if (Made.Count == 0 || ObjectDB.instance == null)
        {
            return;
        }

        Dictionary<int, GameObject>? byHash = ItemsByHash();
        // sets, not List.Contains per clone: that was items x clones Unity object comparisons on every UpdateRegisters
        var items = new HashSet<GameObject>(ObjectDB.instance.m_items);
        var recipes = new HashSet<Recipe>(ObjectDB.instance.m_recipes);
        foreach (KeyValuePair<string, GameObject> pair in Made.ToList())
        {
            GameObject clone = pair.Value;
            if (clone == null || clone.GetComponent<ItemDrop>() == null)
            {
                continue;
            }

            if (items.Add(clone))
            {
                ObjectDB.instance.m_items.Add(clone);
            }

            if (byHash != null)
            {
                byHash[pair.Key.GetStableHashCode()] = clone;
            }

            RegisterByData(clone);

            if (RecipeSpecs.TryGetValue(pair.Key, out string? spec))
            {
                if (!Recipes.TryGetValue(pair.Key, out Recipe? recipe) || recipe == null)
                {
                    recipe = BuildRecipe(pair.Key, spec, clone.GetComponent<ItemDrop>());
                    if (recipe != null)
                    {
                        Recipes[pair.Key] = recipe;
                    }
                }

                if (recipe != null && recipes.Add(recipe))
                {
                    ObjectDB.instance.m_recipes.Add(recipe);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
    private static class ObjectDB_UpdateRegisters_Patch
    {
        // a game update that renames the method must not abort the patches after this one
        private static bool Prepare() => AccessTools.Method(typeof(ObjectDB), "UpdateRegisters") != null;

        private static void Postfix() => EnsureItemsRegistered();
    }

    private static void Remove(ZNetScene scene, string id, GameObject old)
    {
        scene.m_prefabs.Remove(old);
        NamedPrefabs(scene)?.Remove(id.GetStableHashCode());
        ObjectDB.instance?.m_items.Remove(old);
        Object.Destroy(old);
    }
}

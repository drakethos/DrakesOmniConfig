using System.Collections.Generic;
using System.Linq;
using DrakesOmniConfig.Engine.Node.Runtime;
using HarmonyLib;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>
/// World-facing triggers: kill, place, destroy, interact, death and biome. Like <see cref="ItemTriggers"/>,
/// every patch bails out first when no rule uses its event. kill and biome are polled by
/// <see cref="WorldWatch"/> because the kill can land on another client and a biome change has no game method.
/// </summary>
internal static class WorldTriggers
{
    public static void Register()
    {
        var target = NodeRegistry.Arg("target", "prefab", "Prefab name or pattern.");
        Trigger("kill", "The player kills a creature (the kill may be finished by someone else's client; it still counts).",
            NodeRegistry.Arg("item", "item", "Weapon used for the last hit (pattern). Empty = anything."),
            NodeRegistry.Arg("target", "prefab", "Creature prefab name or pattern (Boar, Greydwarf*)."));
        Trigger("place", "The player places a building piece.",
            NodeRegistry.Arg("target", "prefab", "Piece prefab name or pattern (wood_floor, piece_workbench)."));
        Trigger("destroy", "The player removes a piece with the hammer.", target);
        Trigger("interact", "The player presses use on a piece or object (doors, chests, stations, signs).",
            NodeRegistry.Arg("target", "prefab", "Prefab name or pattern (wood_door, piece_chest*)."));
        Trigger("death", "The local player dies.");
        Trigger("biome", "The player walks into a different biome. {target} in messages is the biome name.",
            NodeRegistry.Arg("biome", "biome", "Only when entering this biome (pattern). Empty = any change."));
    }

    private static void Trigger(string name, string help, params NodeArgDef[] args) =>
        NodeRegistry.Register(new NodeDef { Kind = NodeKind.Trigger, Name = name, Help = help, Args = args.ToList() });

    private static bool IsLocal(Player p) => p != null && p == Player.m_localPlayer;

    // PlacePiece returns void live, so success is read from Piece.Awake: a piece is created inside the call only when it worked.
    private static bool _placing;
    private static Piece? _placed;

    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    private static class PlacePatch
    {
        private static void Prefix(Player __instance)
        {
            _placed = null;
            _placing = RuleEngine.HasRules("place") && IsLocal(__instance);
        }

        private static void Postfix()
        {
            _placing = false;
            Piece? piece = _placed;
            _placed = null;
            if (piece != null)
            {
                RuleEngine.Fire("place", null, "", piece.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(Piece), "Awake")]
    private static class PlaceAwakePatch
    {
        private static void Postfix(Piece __instance)
        {
            if (_placing)
            {
                _placed = __instance;
            }
        }
    }

    // The hammer's remove raycasts from the camera itself (not the hover), so repeat the game's own ray to learn
    // which piece it is about to take; report it once the game says the removal went through.
    [HarmonyPatch(typeof(Player), "RemovePiece")]
    private static class DestroyPatch
    {
        private static void Prefix(Player __instance, int ___m_removeRayMask, Transform ___m_eye, float ___m_maxPlaceDistance, out string? __state)
        {
            __state = null;
            if (!RuleEngine.HasRules("destroy") || !IsLocal(__instance) || GameCamera.instance == null)
            {
                return;
            }

            // Physics.Raycast by reflection: UnityEngine.PhysicsModule is not a reference this mod otherwise needs.
            Transform cam = GameCamera.instance.transform;
            Piece? piece = WorldTriggers.RayPiece(cam.position, cam.forward, ___m_removeRayMask, ___m_eye.position, ___m_maxPlaceDistance);
            __state = piece != null ? NodeValues.PrefabName(piece.gameObject) : null;
        }

        private static void Postfix(bool __result, string? __state)
        {
            if (__result && __state != null)
            {
                RuleEngine.Fire("destroy", null, "", null, null, __state);
            }
        }
    }

    /// <summary>The piece the game's own hammer-remove ray would hit (null when none). Physics by reflection: PhysicsModule is not a reference this mod otherwise needs.</summary>
    internal static Piece? RayPiece(Vector3 origin, Vector3 direction, int mask, Vector3 eye, float maxPlaceDistance) =>
        RayPiece(origin, direction, mask, eye, maxPlaceDistance, out _);

    internal static Piece? RayPiece(Vector3 origin, Vector3 direction, int mask, Vector3 eye, float maxPlaceDistance, out string diag)
    {
        diag = "no reflection";
        System.Type? physics = AccessTools.TypeByName("UnityEngine.Physics");
        System.Type? hitType = AccessTools.TypeByName("UnityEngine.RaycastHit");
        System.Reflection.MethodInfo? raycast = physics == null || hitType == null
            ? null
            : AccessTools.Method(physics, "Raycast", new[] { typeof(Vector3), typeof(Vector3), hitType.MakeByRefType(), typeof(float), typeof(int) });
        if (raycast == null || hitType == null)
        {
            return null;
        }

        var call = new object?[] { origin, direction, null, 50f, mask };
        if (!(bool)raycast.Invoke(null, call)!)
        {
            diag = "ray hit nothing";
            return null;
        }

        object hit = call[2]!;
        var point = (Vector3)AccessTools.Property(hitType, "point").GetValue(hit, null);
        var collider = (Component?)AccessTools.Property(hitType, "collider").GetValue(hit, null);
        diag = $"ray hit '{(collider != null ? collider.name : "?")}' (layer {(collider != null ? collider.gameObject.layer : -1)}) at {Vector3.Distance(point, eye):0.0} m";
        return Vector3.Distance(point, eye) < maxPlaceDistance && collider != null ? collider.GetComponentInParent<Piece>() : null;
    }

    [HarmonyPatch(typeof(Player), "Interact")]
    private static class InteractPatch
    {
        private static void Prefix(Player __instance, GameObject go, bool hold)
        {
            if (hold || go == null || !RuleEngine.HasRules("interact") || !IsLocal(__instance))
            {
                return;
            }

            Component? interactable = go.GetComponentInParent<Interactable>() as Component;
            if (interactable == null)
            {
                return;
            }

            Piece? piece = interactable.GetComponentInParent<Piece>();
            RuleEngine.Fire("interact", null, "", piece != null ? piece.gameObject : interactable.gameObject);
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    private static class DeathPatch
    {
        private static void Postfix(Player __instance)
        {
            if (RuleEngine.HasRules("death") && IsLocal(__instance))
            {
                RuleEngine.Fire("death", null, "");
            }
        }
    }

    // Remember creatures the local player hits; WorldWatch reports the ones that die.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    private static class KillTrackPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit != null && __instance != null && __instance != Player.m_localPlayer && !__instance.IsPlayer() && RuleEngine.HasRules("kill")
                && hit.GetAttacker() == Player.m_localPlayer)
            {
                ItemDrop.ItemData? weapon = Player.m_localPlayer.GetCurrentWeapon();
                WorldWatch.TrackHit(__instance, NodeValues.ItemId(weapon), weapon);
            }
        }
    }
}

/// <summary>Per-tick state for the world triggers, driven from <see cref="NodeTicker"/>.</summary>
internal static class WorldWatch
{
    private const float ForgetAfter = 15f;

    private sealed class Tracked
    {
        public string Name = "";
        public string ItemId = "";
        public ItemDrop.ItemData? Item;
        public float Last;
    }

    private static readonly Dictionary<Character, Tracked> Hit = new();
    private static Heightmap.Biome _biome = Heightmap.Biome.None;

    public static void Reset()
    {
        Hit.Clear();
        _biome = Heightmap.Biome.None;
    }

    /// <summary>Test hook: pretend the player was last seen in <paramref name="fake"/>, so the next poll reports a biome change.</summary>
    public static void ForgetBiome(Heightmap.Biome fake) => _biome = fake;

    public static void TrackHit(Character victim, string itemId, ItemDrop.ItemData? item)
    {
        Hit[victim] = new Tracked { Name = NodeValues.PrefabName(victim.gameObject), ItemId = itemId, Item = item, Last = Time.time };
    }

    public static void Poll(Player player)
    {
        if (RuleEngine.HasRules("kill") && Hit.Count > 0)
        {
            foreach (KeyValuePair<Character, Tracked> pair in Hit.ToList())
            {
                Character c = pair.Key;
                Tracked t = pair.Value;
                bool gone = c == null; // destroyed object: the name was captured when it was hit
                if (gone || c!.IsDead())
                {
                    Hit.Remove(c!);
                    RuleEngine.Fire("kill", t.Item, t.ItemId, null, null, t.Name);
                }
                else if (Time.time - t.Last > ForgetAfter)
                {
                    Hit.Remove(c);
                }
            }
        }
        else if (Hit.Count > 0)
        {
            Hit.Clear();
        }

        if (RuleEngine.HasRules("biome"))
        {
            Heightmap.Biome now = player.GetCurrentBiome();
            if (_biome != Heightmap.Biome.None && now != _biome)
            {
                string name = now.ToString();
                RuleEngine.Fire("biome", null, "", null, r => NodeValues.Matches(RuleEngine.Trig(r, "biome"), name), name);
            }

            _biome = now;
        }
    }
}

using System;
using System.Linq;
using DrakesOmniConfig.Config.Modlets.Loading;
using DrakesOmniConfig.Config.Modlets.Schema;
using DrakesOmniConfig.Engine.Fields.Application;
using DrakesOmniConfig.Engine.Fields.Registry;
using DrakesOmniConfig.Engine.Node.Runtime;
using DrakesOmniConfig.Engine.Node.Schema;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Library;

/// <summary>
/// The "set" action: change any exposed value when something happens, with the same field paths and value math a
/// modlet override uses. Per-instance values (an item's durability, quality) change that one item; "shared" values
/// (m_shared.*) and prefab values change every copy of that item or piece, for this client, until the world reloads.
/// </summary>
internal static class FieldActions
{
    private static readonly FieldPatcher Patcher = new(new FieldRegistry());

    public static void Register()
    {
        NodeRegistry.Register(new NodeDef
        {
            Kind = NodeKind.Action,
            Name = "set",
            Help = "Change a value. on=item: the event's item (m_durability, m_quality, m_shared.m_weight ...). on=player: the player (m_walkSpeed, m_jumpForce ...). " +
                   "on=target: a component of what the event hit/placed/used (type=Character: m_health). on=prefab: every copy of a prefab (prefab=SwordBronze, type=ItemDrop, m_itemData.m_shared.m_value). " +
                   "Value: 5, +5, -10%, *1.5, /2, or text.",
            Args =
            {
                NodeRegistry.Arg("on", "choice", "What to change.", true, "item", "item", "player", "target", "prefab"),
                NodeRegistry.Arg("field", "text", "Field path, e.g. m_durability or m_shared.m_damages.m_fire.", required: true),
                NodeRegistry.Arg("value", "text", "New value or change: 5, +5, -10%, *1.5, /2, or text.", required: true),
                NodeRegistry.Arg("type", "text", "Component type for on=target / on=prefab (Character, Piece, ItemDrop, WearNTear ...)."),
                NodeRegistry.Arg("prefab", "prefab", "For on=prefab: the prefab name.")
            },
            Run = Run
        });
    }

    private static void Run(NodeContext c, NodeStep s)
    {
        string on = NodeValues.Raw(s, "on", "item").Trim().ToLowerInvariant();
        string field = NodeValues.Raw(s, "field").Trim();
        string type = NodeValues.Raw(s, "type").Trim();
        object instance;
        string typeName;
        switch (on)
        {
            case "item":
                instance = c.Item ?? throw new InvalidOperationException("this event has no item (on: item needs equip, pickup, craft, consume, hit, ...).");
                typeName = "ItemDrop+ItemData";
                break;
            case "player":
                instance = c.Player;
                typeName = "Player";
                break;
            case "target":
                typeName = type.Length > 0 ? type : "Character";
                instance = Component(c.Target, typeName, "this event has no target object");
                break;
            case "prefab":
                string prefabName = NodeValues.Raw(s, "prefab");
                GameObject? prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
                typeName = type.Length > 0 ? type : "ItemDrop";
                instance = Component(prefab, typeName, $"no prefab named '{prefabName}'");
                break;
            default:
                throw new InvalidOperationException($"unknown 'on: {on}' (item, player, target, prefab).");
        }

        var resolved = new ResolvedOverride
        {
            Target = new TargetSelector { TypeName = typeName },
            FieldName = field,
            Expression = ModletLoader.ParseExpression(NodeValues.Text(c, s, "value"))
        };
        Patcher.Apply(resolved, instance);
    }

    private static object Component(GameObject? go, string typeName, string missing)
    {
        if (go == null)
        {
            throw new InvalidOperationException(missing + ".");
        }

        return go.GetComponent(typeName) ?? go.GetComponentInParent(Type.GetType(typeName) ?? typeof(Component)) as object
               ?? throw new InvalidOperationException($"'{go.name}' has no component '{typeName}'.");
    }
}

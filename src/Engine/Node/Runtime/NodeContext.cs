using DrakesOmniConfig.Engine.Node.Schema;
using UnityEngine;

namespace DrakesOmniConfig.Engine.Node.Runtime;

/// <summary>What a trigger hands to its rule's conditions and actions.</summary>
internal sealed class NodeContext
{
    public string Event { get; set; } = "";
    public Player Player { get; set; } = null!;

    /// <summary>The live inventory item the event is about, when there is one. Actions that change an item (durability, remove "this") act on it.</summary>
    public ItemDrop.ItemData? Item { get; set; }

    /// <summary>Prefab name of that item (e.g. "Club"), also set when only the name is known.</summary>
    public string ItemId { get; set; } = "";

    public GameObject? Target { get; set; }
    public string TargetName { get; set; } = "";
    public NodeRule Rule { get; set; } = null!;
}

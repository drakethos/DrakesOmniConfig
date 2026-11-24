# DrakesOmniConfig

A declarative config/modding engine for Valheim. Instead of installing a separate tiny mod for every tweak
("more carry weight," "tougher bronze swords," "bigger chests"), a server admin writes plain YAML files
("modlets") describing what to change, and this mod applies them — no C#, no recompiling, no new DLL per tweak.

**This is the client/player build.** It only applies modlets; it doesn't help you write them. If you're the
one setting up a server or authoring the YAML, install **[DrakesOmniConfigServer](Server/README.md)**
instead — same engine, plus the authoring tools and the full schema wiki. **Never install both at once** on
the same machine (see "Client vs. Server" below).

## What it does

- Reads every modlet under `BepInEx/config/DrakesOmniConfig/Modlets/` and applies their field overrides to
  the running game — items, pieces, the player, anything with a Unity component the game itself exposes.
- Supports exact values, percentage/relative math (`*1.5`, `+20%`, `-5`), wildcard name matching
  (`"Sword*"` hits every tier of every sword at once), and conflict resolution when two modlets touch the
  same field.
- One override can target either a **prefab template** (baked in once, so every future spawn/craft of that
  item inherits it for free) or the **live player** (re-applied every time, so it survives death/respawn).
- **Node rules** make things happen with items: when a player equips, eats, crafts, picks up, hits with or
  breaks an item, run actions — give items, heal, add a status effect, change durability, show a message or
  a popup with buttons. Built in a visual editor, no code.

For the full schema, every option, and worked examples, see **[DrakesOmniConfigServer's README](Server/README.md)** — it's written as the wiki for both builds, since the YAML format is identical either way.

## Install

Drop it in your `BepInEx/plugins` folder (or install via your mod manager) on **every client** that joins a
server using DrakesOmniConfig modlets. It has no settings you need to touch — if the server/host is running
`DrakesOmniConfigServer`, it publishes its resolved modlets and this build just adopts them; otherwise this
build reads its own local `Modlets/` folder.

## Client vs. Server

| | `DrakesOmniConfig` (this) | `DrakesOmniConfigServer` |
|---|---|---|
| Who installs it | Everyone who plays | The dedicated server, and admins who tweak it |
| Reads local `Modlets/` folder | Yes | Yes |
| Authoring tools (Fields + Rules editors, manifest generation) | No | Yes |
| In-game admin panel (F10: rules on/off, paste rules, reload) | No | Yes |
| Popups use DrakeModsLibs' window if installed (optional) | Yes | Yes |
| Install together on one machine | **Never** | **Never** |

They intentionally use **different BepInEx GUIDs** (`com.drakemods.omniconfig` vs.
`com.drakemods.omniconfigserver`) — Valheim's own mod-compatibility check matches on GUID, and these two are
meant to never both be present in the same game process. They talk to each other over their own ServerSync
handshake, not GUID matching, which is exactly why the GUIDs are allowed to differ.

## New items and live value changes

Two things beyond plain overrides, both plain YAML in a modlet:

**Clone an item or prefab** (`clones:` in any non-`nodes` YAML file). The copy keeps the original's model and icon; you change what differs:

```yaml
clones:
  - id: PoisonSoup
    from: SerpentStew
    name: Poison Soup
    fields:
      m_itemData.m_shared.m_food: "0"      # same paths and value math as overrides
```

It is a real prefab: `spawn PoisonSoup` in the console, in inventories, in the Rules editor's item list. A clone syncs from the server like any override. See `Examples/Modlets/com.drakemods.poisonsoup` (clone plus a `consume` rule that poisons the eater).

**Change any value when something happens** (the node action `set`): `on: item | player | target | prefab`, a `field` path and a `value` (`5`, `+5`, `-10%`, `*1.5`, `/2`, or text).

```yaml
- id: lucky-pick
  when: { event: equip, item: PickaxeIron }
  do:
    - set: { on: item, field: m_quality, value: "=3" }
```

**New looks** (`looks/*.json` in a modlet): recolour or restyle any item, or a clone, with the
[Drakes Asset Forge](https://github.com/drakethos/DrakesAssetForge) recipe format: tint, gloss/metal, glow, icon
colour, borrowed materials and meshes, parts. Forge's look code is compiled into this mod, so nothing else to
install except Jotunn (soft dependency: without it looks are skipped and the rest works). The server syncs looks as
text; textures and models are never sent, they come from the player's own game or an installed mod pack. Looks change
live: an admin's `looks.put` reaches every client without a restart. The Config Builder's **Items** page
(`items.html`) writes both the clone and its look.

```json
{ "id": "PoisonSoup", "kind": "reskin", "base": "PoisonSoup",
  "look": { "materials": [ { "tint": "#5BBF3A" } ], "iconTint": "#8BE05A" } }
```

**Other mods' settings** (`configs:` in a modlet): set any installed mod's BepInEx setting by GUID, section and key,
with the same value math. The mod's `.cfg` file is never changed (the value lives in memory; remove the binding and
the mod's own value comes back), and on a server every player gets the value, through the mod's own sync or
OmniConfig's. **Shared variables and server flags:** `Modlets/vars.yaml` is shared by every modlet, and
`Modlets/flags.yaml` declares switches an admin flips at runtime (`omni flag event_weekend on`, or the F10 panel) to
change variables, config bindings and whole modlets; other mods can read a flag as the world global key
`omni_flag_<name>`. Details in the [Server README](Server/README.md#other-mods-settings-configs).

Per-instance values (an item's `m_durability`, `m_quality`) change that one item. `m_shared.*` and `on: prefab` values change every copy for this client until the world reloads.

## Status

Local modlets (YAML on disk, applied to your own single-player/hosted game) are implemented and covered by
an automated test suite. Server→client sync is implemented: the server/host resolves its modlets once and
publishes them, and a client with *no* local modlets of its own adopts whatever the server sends. Node rules
(item and world triggers, conditions, actions, area actions, popups) sync the same way and have their own automated suite. Other mods' settings (`configs:`), shared variables and server flags are implemented with their own suite. See
[ROADMAP.md](ROADMAP.md) for what's still ahead (group/category selectors, more node events, per-player flags). Requirements: BepInEx only. DrakeModsLibs is optional; Jotunn is optional and turns on looks (`looks/*.json`).

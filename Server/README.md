# DrakesOmniConfigServer

The admin/authoring build of [DrakesOmniConfig](../README.md). Same engine as the client build, plus the
tools you need to actually *write* modlets instead of hand-guessing field names. This file is the wiki for
both builds — the YAML format, folder layout, and every option below apply identically whether you're
running the client or server build.

**Standalone — never install alongside `DrakesOmniConfig`.** See [the client README](../README.md#client-vs-server)
for why. Install this one on the machine where you're authoring/testing modlets; everyone who just plays
installs the client build.

---

## Contents

- [What a modlet is](#what-a-modlet-is)
- [Quick start](#quick-start)
- [Folder layout](#folder-layout)
- [manifest.yaml](#manifestyaml)
- [Overrides: target + fields](#overrides-target--fields)
  - [Scope: Prefab vs. Global](#scope-prefab-vs-global)
  - [Kind: how a target is selected](#kind-how-a-target-is-selected)
  - [Field paths](#field-paths)
  - [Value expressions (the math grammar)](#value-expressions-the-math-grammar)
- [vars.yaml — reusable numbers](#varsyaml--reusable-numbers)
- [groups.yaml — named sets of ids](#groupsyaml--named-sets-of-ids)
- [Conflicts: when two modlets touch the same field](#conflicts-when-two-modlets-touch-the-same-field)
- [Other mods' settings: configs:](#other-mods-settings-configs)
- [Server flags](#server-flags)
- [Finding real field names: the Config Builder tool](#finding-real-field-names-the-config-builder-tool)
- [Node rules: make things happen](#node-rules-make-things-happen)
  - [Triggers](#triggers-when) · [Conditions](#conditions-if) · [Actions](#actions-do--else)
  - [The Rules editor](#the-rules-editor) · [The admin panel](#the-admin-panel-in-game) · [Who installs what](#who-installs-what)
- [Console commands (F5)](#console-commands-f5)
- [Worked examples](#worked-examples)
- [Troubleshooting](#troubleshooting)
- [Current limits](#current-limits)

---

## What a modlet is

A **modlet** is a folder of plain-text YAML files describing a set of changes to apply to the game. No
code, no compiling, no DLL. Multiple modlets can be installed at once (think of them like small
config packs); the engine resolves conflicts between them automatically (see
[Conflicts](#conflicts-when-two-modlets-touch-the-same-field)).

## Quick start

1. Install `DrakesOmniConfigServer`.
2. Create a folder under `BepInEx/config/DrakesOmniConfig/Modlets/your-modlet-name/`.
3. Add a `manifest.yaml` (required — see below) and at least one other `.yaml` file with an `overrides:` list.
4. Launch the game (or a dedicated server). Your overrides apply automatically — no reload button, no
   console command needed.
5. If something doesn't look right, check `BepInEx/LogOutput.log` — a bad field name throws a clear
   "no field 'x' on 'y'" error naming exactly what didn't resolve, and (as of this build) one bad field
   no longer breaks every *other* override queued in the same batch.

The folder name itself doesn't matter to the engine (it's just a filesystem path); the modlet's real
identity — used for conflict bookkeeping and, later, sync — is the `id` field... actually, there isn't one:
**the folder name IS the modlet's id.** Don't rename a shared modlet's folder once people are using it.

## Folder layout

```
BepInEx/config/DrakesOmniConfig/Modlets/
  your-modlet-name/
    manifest.yaml      <- required, see below
    vars.yaml           <- optional: {placeholder} values reused across this modlet's files
    groups.yaml          <- optional: named sets of ids (parsed; selector matching not wired yet, see Current limits)
    anything-else.yaml  <- your own organization — flat, one file per category, nested folders, whatever
    nested/folder/also-works.yaml
```

Only `manifest.yaml`, `vars.yaml`, and `groups.yaml` are special names, and only at the modlet's root —
everything else (any depth, any name, `.yaml` or `.yml`) is scanned recursively for an `overrides:` list and
merged into one set. There's no required naming convention beyond that; organize however makes sense to you
(by category, by biome, one giant file — all equivalent to the engine).

## manifest.yaml

The one required file. Example:

```yaml
schemaVersion: 1
displayName: My Server's Tweaks
enabled: true
priority: 0
```

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | int | Reserved for future format changes. Always `1` today. |
| `displayName` | string | Shown in logs/tools. Doesn't need to match the folder name. |
| `enabled` | bool | `false` disables the whole modlet without deleting it. |
| `priority` | int | Higher wins a conflict against another modlet that *also* has `priority` set and neither uses `override: true`. See [Conflicts](#conflicts-when-two-modlets-touch-the-same-field). |
| `requiresFlags` | list | The modlet is on only while **all** of these [flags](#server-flags) are on. |
| `unlessFlags` | list | The modlet is off while **any** of these flags is on. |

## Overrides: target + fields

Everywhere else in the modlet, you write a list under `overrides:`. Each entry has a `target` (what to
change) and `fields` (what to change on it):

```yaml
overrides:
  - target:
      typeName: ItemDrop
      scope: Prefab
      kind: Item
      itemId: SwordBronze
    fields:
      m_itemData.m_shared.m_damages.m_blunt: "*1.3"
      m_itemData.m_durability: "=500"
```

### Scope: Prefab vs. Global

| Scope | When it applies | Use it for |
|---|---|---|
| `Prefab` | Once, baked into the item/piece's **template** the moment the game registers its prefabs. Every future spawn, craft, or placement of that item inherits the change for free — it's not a per-instance patch. | Items, pieces, anything that isn't a singleton (weapons, armor, carts, chests, campfires, ...). |
| `Global` | Re-applied **every time** the local Player object is (re)created — first spawn, death/respawn, rejoining a world. | The player themself (`typeName: Player`) — health, stamina, carry weight, jump force, pickup range, etc. A prefab-template edit alone wouldn't survive a respawn, which is why this exists as a separate scope. |

If you're not sure which to use: if the target is something you'd spawn/craft/place more than once, use
`Prefab`. If it's literally the player, use `Global`.

### Kind: how a target is selected

| Kind | Extra field | Matches |
|---|---|---|
| `Item` (default) | `itemId` | One exact prefab by name (`SwordBronze`, `Player`, `Cart`, ...). |
| `NamePattern` | `namePattern` | A glob against every registered prefab's name. `*` means "any run of characters" — `"Sword*"` matches `SwordBronze`/`SwordIron`/etc., `"*Leather*"` matches anywhere in the name. Case-insensitive. **`Prefab` scope only** — there's only ever one Player, so a name pattern against `Global` scope wouldn't mean anything. |
| `Group` | `groupName` | A named set defined in this modlet's `groups.yaml`. **Parsed today, but matching against real targets isn't wired up yet** — see [Current limits](#current-limits). |
| `Category` | `category` | A classification tag (weapon tier, piece category, etc.). **Not implemented yet** — the taxonomy itself hasn't been designed. Avoid for now. |
| `Blanket` | *(none)* | Matches every instance of `typeName`. **Not implemented yet.** |

`typeName` is always required — it's the Unity component type the field lives on (`ItemDrop`, `Player`,
`Container`, `Vagon`, `Fireplace`, `Ship`, `WearNTear`, ...), not the prefab's own name. For most items it's
`ItemDrop`; for the player it's `Player`; for a piece, it depends on what that piece actually is (a chest is
`Container`, a cart is `Vagon`, a campfire is `Fireplace`) — see the [Config Builder tool](#finding-real-field-names-the-config-builder-tool)
to find the right one without guessing.

### Field paths

A field name can be a **dotted path** to reach into nested data: `m_itemData.m_shared.m_damages.m_blunt`
walks `ItemDrop` → its `m_itemData` field → that struct's `m_shared` field → that's `m_damages` field →
finally `m_blunt`. You can reach any depth this way. If any segment is wrong, the error names exactly which
one didn't resolve and on what type — it won't fail silently.

### Value expressions (the math grammar)

Every field's value is a short string, not a bare number, because relative changes need an operator:

| Write | Means |
|---|---|
| `"500"` or `"=500"` | Set to exactly 500 (quotes with no leading operator and a leading `=` do the same thing). |
| `"HelloWorld"` | Set to a literal string (for text/name fields). |
| `"true"` / `"false"` | Set a boolean field. |
| `"*1.3"` | Multiply the current value by 1.3. |
| `"/2"` | Divide the current value by 2. |
| `"+20"` | Add 20 to the current value. |
| `"+20%"` | Add 20% of the current value (relative, not a flat +20). |
| `"-5"` / `"-10%"` | Subtract, flat or percentage. |

`%` only means something after `+`/`-`; it has no effect after `*`/`/` (multiplying by a percentage doesn't
make sense the same way). Relative operators (`+`/`-`/`*`/`/`) read the field's *current* value first, so
they stack correctly with whatever the vanilla default (or another mod) already set — you don't need to
know the exact vanilla number to write `"*1.3"`.

## vars.yaml — reusable numbers

Define once, use everywhere in that modlet via `{name}`:

```yaml
# vars.yaml
weightMultiplier: 0.5
```

```yaml
# anywhere-else.yaml
fields:
  m_itemData.m_shared.m_weight: "*{weightMultiplier}"
```

**Shared by every modlet:** a `vars.yaml` directly in the `Modlets` folder (next to the modlet folders) works the
same way for all of them. A modlet's own `vars.yaml` wins when both define the same name.

**Depending on a [flag](#server-flags):** a variable can be a map instead of a plain value. The first `when:` entry
whose flag matches wins (`!name` = the flag is off), else `default:`.

```yaml
# Modlets/vars.yaml
stackMult:
  default: 2
  when:
    event_weekend: 4
carryBonus: 150
```

An unknown `{name}` (typo, or you forgot to define it) is left as literal text rather than guessed — you'll
see the literal `{name}` in the error when it fails to parse as a number, which tells you immediately what's
wrong.

## groups.yaml — named sets of ids

```yaml
# groups.yaml
BronzeWeapons:
  - SwordBronze
  - AxeBronze
  - MaceBronze
```

A `groups.yaml` directly in the `Modlets` folder is shared by every modlet (a modlet's own group of the same
name wins). Referenced via `kind: Group` + `groupName: BronzeWeapons`. **Parsing works; the engine doesn't yet match
targets against a group** (see [Current limits](#current-limits)) — for now, use `NamePattern` wildcards or
list separate `Item` entries for the same effect.

## Conflicts: when two modlets touch the same field

If two modlets both have an override for the same `(typeName, itemId/pattern/etc., fieldName)`, exactly one
wins — the losing one is simply skipped for that field, no error. The rule, in order:

1. **Selector specificity.** `Item` beats `Group` beats `NamePattern` beats `Category` beats `Blanket` — a
   narrower, more specific claim always wins over a broader one, regardless of priority.
2. **Priority** (from `manifest.yaml`), if the specificity tier is the same. Higher wins.
3. **Load order**, as the final tiebreak if everything else is equal.

**Escape hatch:** add `override: true` on a specific override entry (sibling to `target:`, not inside
`fields:`) to make it win outright over a non-`override` claim, *regardless* of tier or priority:

```yaml
overrides:
  - target:
      typeName: Player
      scope: Global
      kind: Item
      itemId: Player
    override: true
    fields:
      m_baseHP: "=400"
```

Two `override: true` entries still break by tier, then priority, same as normal — `override` just means "I
beat anyone who *isn't* also claiming an override," not "I always win no matter what."

If your override doesn't seem to be taking effect and there's no error in the log, **this is the first thing
to check** — another modlet (yours or someone else's) may be claiming the same field at equal or higher
specificity. This exact scenario is why the automated test suite bundled with this mod (see
`Tests/suites/DrakesOmniConfig/`) checks the *actual live value*, not just "did it throw" — a silently-lost
conflict looks identical to success in the log.

## Other mods' settings: configs:

Any modlet YAML file may set **another mod's BepInEx setting** with a `configs:` list (a list, because section and
key names can contain `:`). The value uses the same [math grammar](#value-expressions-the-math-grammar) as fields;
`true`/`false` and enum names work for those settings.

```yaml
configs:
  - mod: Azumatt.AzuCraftyBoxes       # the mod's BepInEx plugin GUID (any letter case)
    section: "1 - General"
    key: "Stack multiplier"
    value: "{stackMult}"              # "5", "=5", "*1.5", "+20%", "-5", text, true/false, an enum name
    override: false                   # same escape hatch as overrides
```

- **The mod's `.cfg` file is never changed.** The value lives in memory while the binding is on; anything that saves
  the file writes the mod's own value. Remove the binding (and reload) and the mod's own value comes back.
- **Relative edits start from the mod's own value** every time, so `*2` stays x2 however often you reload.
- **Conflicts** between modlets binding the same setting: `override: true` first, then the higher `priority`.
- **Delivery to players.** The server sets every binding. A setting the mod syncs itself (ServerSync, or Jotunn
  admin-only) then reaches players through that mod; for the rest, OmniConfig sends it and each player's game applies
  it the same way (in memory, file untouched). Leaving the world puts every bound setting back.
- **A mod or setting that isn't there** is logged once and tried again on every reload and world start (some mods
  create settings late). A value of the wrong type is refused with a warning; a value outside a setting's allowed
  range is clamped.
- **Settings read only at start-up:** some mods read a setting once when the game starts. A new value reaches them
  after a relog or restart; there is no reliable way to detect this.
- `configs.list` (admin command, console `omni configs`, or the F10 panel) shows every binding, which modlet won it,
  and what happened to it. The Config Builder's **Mod settings** page (`mods.html`) lists every installed mod's
  settings (from `ModConfigManifest.json`, written when a world loads) and writes the `configs:` entries for you.

Example: `Examples/Modlets/com.drakemods.modconfigs` (off by default).

## Server flags

Switches an admin can flip at runtime (`event_weekend`, `pvp`, ...), declared with defaults in
`Modlets/flags.yaml`:

```yaml
flags:
  event_weekend: false
  pvp: true
```

Names: lower-case letters, digits and `_`, up to 32 characters. A flag can:

- **change variables** ([conditional variables](#varsyaml--reusable-numbers)), and through them overrides, config
  bindings and rules;
- **switch whole modlets** (`requiresFlags` / `unlessFlags` in [manifest.yaml](#manifestyaml)).

Switch one with the admin command `flag.set` (F10 panel **Flags** section, or console `omni flag event_weekend on`).
Switches are kept in `BepInEx/config/DrakesOmniConfig/omni-flags.yaml` and survive restarts; `default` forgets a
switch. A switch takes effect at once for config bindings, rules and flag-gated modlets; prefab **field overrides**
change on the next world load.

**Other mods can read flags without depending on OmniConfig:** on a server, every flag that is on is also the world
global key `omni_flag_<name>` (removed when the flag goes off; config `[Flags] MirrorFlagsToGlobalKeys`):

```csharp
bool weekend = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey("omni_flag_event_weekend");
```

Flags are server-wide: there are no per-player or per-group flags yet.

## Finding real field names: the Config Builder tool

Guessing field names (`m_amor` vs `m_armor`, `m_maxHealth` vs `m_baseHP`) is the single most common way to
waste time authoring a modlet. Don't — use the bundled tool instead:

1. Launch the game once with `DrakesOmniConfigServer` installed. It generates
   `BepInEx/config/DrakesOmniConfig/GameDataManifest.json` — every prefab, every component type actually
   attached to a real prefab, every field on those types (and on `Player`/`Humanoid`/`Character`), and every
   enum's real member names. (It's cheap after the first run — it only rescans when the number of prefabs
   changes, e.g. you add/remove a mod.)
2. Open `BepInEx/config/DrakesOmniConfig/ConfigBuilder/index.html` in a browser (this build writes it there
   at startup, right next to the manifest; `rules.html` beside it is the [Rules editor](#the-rules-editor)). It tries to load the manifest automatically; if your browser blocks that for local
   files, use the page's own file picker and point it at the JSON.
3. Browse: pick a prefab → pick one of its real components → drill into its real fields (including nested
   structs) → pick a value control appropriate to that field's type (checkbox, number, dropdown for an enum,
   text otherwise) → it writes out the exact `manifest.yaml`/overrides YAML for you, ready to save into a
   modlet folder.

This is the same manifest you could read by hand if you wanted (it's just JSON), but the tool saves you the
trip through nested JSON and gets the exact field *type* right, which the hand-written examples below also
rely on for anything non-obvious.

## Node rules: make things happen

Overrides change *values*. **Rules** make things *happen*: when a player equips, eats, crafts, picks up,
hits with or breaks an item, run a chain of actions — give items, heal, add a status effect, change
durability, show a message or a popup with buttons. Still no code: a rule is a few lines of YAML, and the
[Rules editor](#the-rules-editor) builds them for you visually.

Each rule is a chain: **when** (one trigger) → **if** (conditions, all must pass) → **do** (actions).
Optionally **else** runs when a condition fails.

```yaml
# Modlets/my-server/nodes/lucky-pickaxe.yaml
rules:
  - id: lucky-pickaxe
    when: { event: hit, item: "Pickaxe*", target: "Rock*|MineRock*" }
    if:
      - chance: 10
    do:
      - give: { item: Coins, amount: 3 }
      - message: { text: "Lucky strike! +3 coins", where: corner }
```

### Where rules live

Any `.yaml` under a modlet's `nodes/` folder (any depth). The modlet still needs its `manifest.yaml`, and
its `vars.yaml` placeholders work in rules too (`chance: "{luckyChance}"` — quote them). `id` must be unique
within the modlet. A rule with a mistake is skipped with a clear message in the log naming the rule and the
problem; the rest of the file still loads.

### Rule options

| Key | Meaning |
|---|---|
| `id` | Required. Unique name inside the modlet; the admin panel and logs use `modlet/id`. |
| `when` | Required. The trigger: `when: spawn`, or `when: { event: equip, item: "Sword*" }`. |
| `if` | Optional list of conditions. All must pass. |
| `do` | Actions when the conditions pass. |
| `else` | Optional actions when a condition fails. |
| `cooldown` | Seconds before this rule may run again for this player (`cooldown: 60`). |
| `enabled` | `false` keeps the rule in the file but off. |

**Nodes** are one-key maps: `- give: { item: Coins, amount: 3 }`. Nodes with one main value also take a
short form: `- chance: 10`, `- message: "Hello"`, `- heal: 25%`.

**Patterns** (`item`, `target`, `is`, ...): case-insensitive, `*` is a wildcard, `|` separates
alternatives — `"Sword*|Axe*"`. An empty pattern matches anything.

**Text placeholders** in `message`/`popup` text: `{item}` (the item's display name), `{itemId}` (its prefab
name), `{player}`, `{target}`. Game tokens like `$item_club` are translated too.

### Triggers (`when`)

| Event | Fires when | Filters |
|---|---|---|
| `equip` | the player equips an item | `item` |
| `unequip` | the player unequips an item | `item` |
| `pickup` | the player picks an item up from the ground | `item` |
| `craft` | the player crafts or upgrades an item at a station | `item` |
| `consume` | the player eats, drinks or uses up a consumable | `item` |
| `hit` | the player hits a creature, rock, tree or log | `item` (weapon/tool), `target` (what was hit) |
| `broke` | an equipped item's durability reaches zero | `item` |
| `interval` | every `every` seconds (0.5 minimum) | `every` (required), `item` (only while an equipped item matches) |
| `spawn` | the player joins the world or respawns | — |

Re-equipping a saved loadout on login does **not** fire `equip`.

### Conditions (`if`)

Every condition also takes `not: true` to invert it.

| Condition | Passes when | Args |
|---|---|---|
| `item` | the event's item matches | `pattern` |
| `target` | what was hit matches (`hit` trigger) | `pattern` |
| `chance` | a random roll succeeds | `percent` (0-100) |
| `quality` | the event's item quality is in range | `min` (default 1), `max` |
| `hasItem` | the player carries at least `amount` | `item`, `amount` (default 1) |
| `equipped` | something equipped matches | `item` |
| `health` | health is below/above a number or % of max | `below`, `above` |
| `biome` | the player is in a biome | `is` (Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, AshLands, DeepNorth, Ocean) |
| `time` | it's day or night | `is`: `day` / `night` |

### Actions (`do` / `else`)

| Action | Does | Args |
|---|---|---|
| `give` | adds an item (drops it at the player's feet if the inventory is full) | `item`, `amount`, `quality` |
| `remove` | takes items from the inventory | `item` (empty = the event's own item), `amount` |
| `durability` | changes the event's item durability | `change`: `+25`, `-10%`, `=100%` (% of max) |
| `status` | adds a status effect | `effect` (Rested, Warm, Wet, ...), `duration` (seconds) |
| `removeStatus` | removes a status effect | `effect` |
| `heal` | heals the player | `amount` (number or % of max) |
| `damage` | damages the player (armor doesn't reduce it) | `amount` (number or % of max) |
| `stamina` | changes stamina | `change`: `+20`, `-15`, `=100%` |
| `skill` | raises a skill | `skill` (Swords, Axes, Pickaxes, WoodCutting, Run, ...), `amount` |
| `effect` | plays an effect prefab at the player | `prefab` (vfx_..., sfx_...) |
| `spawn` | spawns a prefab in front of the player | `prefab`, `amount`, `distance` |
| `message` | shows short on-screen text | `text`, `where`: `center` / `corner` |
| `popup` | opens a window with buttons | `title`, `text`, `buttons` (below) |

A **popup**'s buttons each run their own actions:

```yaml
- popup:
    title: "Welcome, {player}!"
    text: Take a starter kit?
    buttons:
      - label: Take the kit
        do:
          - give: { item: Club }
          - give: { item: Torch }
      - label: No thanks
```

Popups use DrakeModsLibs' window when DrakeModsLibs is installed (same look as the other Drakes mods), and
a small built-in window otherwise. **DrakeModsLibs is optional** — OmniConfig never requires it.

### Where rules run, and sync

The server (or the host) loads the rule files and sends them to every client with the field overrides, over
the same ServerSync channel. A player with **no local modlets** still gets the server's rules. Each rule runs
on the client of the player it's about, which is where Valheim itself keeps that player's inventory,
health and status effects.

### The Rules editor

`BepInEx/config/DrakesOmniConfig/ConfigBuilder/rules.html` (written there by this build at startup, next to
the Fields page `index.html`). Open it in a browser — no server needed.

1. The palette on the left lists every trigger, condition and action **this engine** supports, with help
   text. It loads `NodeCatalog.json` (written by this build at world load) when the browser allows; otherwise
   pick that file with the Catalog button. Without it the page still works from a built-in copy, minus the
   item suggestions.
2. **+ New rule**, then click a trigger. Click a lane (if / do / else, or a popup button's lane), then click
   palette nodes to add them there — or drag nodes onto lanes.
3. Click a node to edit it on the right: typed inputs, suggestions for items, status effects, skills and
   biomes, required args marked. Move or remove nodes there; popups get a button list.
4. **Check** lists problems (missing args, duplicate ids, rules with no trigger).
5. **Export**: set the modlet id and file name, then **Copy YAML** (to paste in game) or **Download .yaml**
   (to save into `Modlets/<modlet>/nodes/` yourself). **Import** loads an existing rules file back in for
   editing. Your work is kept in the browser between visits.

### The admin panel (in game)

Press **F10** (config `[Admin] PanelKey`) in a game where you are the host, or on a server where you are on
the admin list. It shows the server's modlets and every rule with **Turn off / Turn on** buttons, plus:

- **Paste rules** — sends the YAML on your clipboard (from the Rules editor's Copy) to the server, which
  checks it, saves it as `Modlets/<modlet>/nodes/<file>.yaml` (the `modlet:`/`file:` lines the editor
  writes; `admin-rules/pasted` otherwise) and reloads. A file with mistakes is refused with the reason, and
  nothing is saved.
- **Reload** — re-reads every modlet from disk and re-sends to all players.
- **Flags** — every [server flag](#server-flags) with its state and a **Turn on / Turn off** button.
- **Other mods' settings** — every `configs:` binding, the modlet that won it, and whether it applied.

On/off switches are kept in `BepInEx/config/DrakesOmniConfig/omniconfig-state.yaml`, never in the modlet
files themselves. Rules change immediately for everyone; a modlet's **field overrides** change on the next
world load.

### Console commands (F5)

Server build. For the host, or an admin on a dedicated server (checked against the server's admin list):

| Command | Does |
|---|---|
| `omni flag <name> on\|off\|default` | Switch a [flag](#server-flags) (`default` = back to `flags.yaml`) |
| `omni flags` | List the flags and their state |
| `omni configs` | List every `configs:` binding and what happened to it |
| `omni reload` | Re-read every modlet and re-send to all players |

### Who installs what

| Machine | Install |
|---|---|
| Dedicated server | `DrakesOmniConfigServer` + its modlets |
| Admins who tweak the server from their own game | `DrakesOmniConfigServer` (for the F10 panel and the editor) |
| Everyone else | `DrakesOmniConfig` |

Both builds speak the same sync protocol, so admins on the Server build and players on the client build
share one server. Neither build uses Jotunn, so there is no Jotunn mod-list check between them. A client
and server must both be on the **same sync protocol** (builds with node rules use protocol 2; 0.1.0-beta.1/beta.2 used 1),
or the server turns the client away with a version message.

## Worked examples

The `Examples/Modlets/` folder in this repo (auto-deployed into your `Modlets/` folder on every build — dev
convenience, never ships) has several complete, working modlets:

- `com.drakemods.helloworld` — the simplest possible override (renames the Hammer).
- `com.drakemods.toolkit` — `{var}` substitution, `Multiply`, and `Add%` in one file.
- `com.drakemods.playertweaks` / `com.drakemods.testlab` — `Scope.Global` overrides on the Player
  (health, stamina, carry weight, jump force).
- `com.drakemods.bronzeweapons` / `com.drakemods.leatherarmor` — `NamePattern` wildcards hitting every
  tier of a weapon/armor type at once.
- `com.drakemods.conflict-low` / `com.drakemods.conflict-high` — two modlets deliberately claiming the
  same field, demonstrating `priority`.
- `com.drakemods.testlab` (full) — the most complete reference: player stats, 11 weapon-family wildcards,
  armor, and four different piece types (cart/ship/chest/fire), each a different Unity component shape.
  This is also the modlet the automated test suite (`Tests/suites/DrakesOmniConfig/`) checks against —
  every value it sets has a corresponding exact assertion, so it's a good "does my install actually work"
  sanity check too.
- `com.drakemods.itemrules` — **node rules**, off by default (`enabled: false`): lucky pickaxe hits, a
  welcome-kit popup with buttons, potion bonuses, a warm torch at night, a broken-tool message, an upgrade
  bonus, hunting rewards. The automated suite `Tests/suites/DrakesOmniConfig-Nodes/` checks every trigger,
  condition branch, popup, admin command and the editor's own export.

## Troubleshooting

- **"No field 'x' on 'y' while resolving 'a.b.c'"** — you (or an example) typo'd a field name, or guessed
  one that doesn't exist. Use the [Config Builder](#finding-real-field-names-the-config-builder-tool)
  instead of guessing. The error names the *exact* segment that failed, so for a dotted path you know
  precisely where to look.
- **No error, but the value isn't changing** — almost always a [conflict](#conflicts-when-two-modlets-touch-the-same-field)
  with another modlet. Check for another modlet touching the same `typeName`/`itemId`/field.
- **A `Prefab`-scope override to the Player doesn't stick** — you probably want `Global` scope instead.
  `Prefab` scope bakes into the template once; a *second*, different cause of "didn't stick" for the Player
  specifically is the conflict case above, not a Scope mistake — check both.
- **A rule never fires** — check the log at world load for "Rule '...' skipped: ..." (a mistake in the
  file), and the F10 panel for whether it's on. `item`/`target` match prefab names (`Club`, `Deer`), not the
  display names shown in game.
- **Changed a field but an already-placed/already-crafted instance in your world doesn't reflect it** —
  expected for `Prefab` scope: it affects future spawns, not retroactively existing ones (existing world
  objects already baked in whatever value was live when they were created/saved).

## Current limits

Honest status, not aspirational — see [ROADMAP.md](../ROADMAP.md) for the full phase-by-phase breakdown:

- **`Group`/`Category`/`Blanket` selector *matching* isn't implemented** — `groups.yaml` parses, but nothing
  in the engine matches a target against a group yet, and `Category`'s taxonomy hasn't even been designed.
  `Item` and `NamePattern` (Prefab scope only) are the only two that actually work end to end today.
- **Server→client sync is implemented.** The server/host resolves its modlets once and publishes the result
  over ServerSync; a joined `DrakesOmniConfig` client with no local modlets adopts whatever the server sent.
- **Console commands:** `omni flag/flags/configs/reload` only (Server build); creating or editing modlets from the
  console is not built.
- **`configs:` and mods that read a setting only at start-up:** the new value reaches them after a relog or restart.
- **Flags are server-wide:** no per-player or per-group flags, and no scheduled flags yet.
- **Node rules are rule chains**, not a free-form dataflow graph: one trigger, conditions, actions, else and
  popup-button branches. Item and player events only so far; a way to hook arbitrary game methods is not built.
- **Rules run for the local player only.** There is no "everyone in range" or server-side world action yet.

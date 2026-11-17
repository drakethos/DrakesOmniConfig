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
- [Finding real field names: the Config Builder tool](#finding-real-field-names-the-config-builder-tool)
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

Referenced via `kind: Group` + `groupName: BronzeWeapons`. **Parsing works; the engine doesn't yet match
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

## Finding real field names: the Config Builder tool

Guessing field names (`m_amor` vs `m_armor`, `m_maxHealth` vs `m_baseHP`) is the single most common way to
waste time authoring a modlet. Don't — use the bundled tool instead:

1. Launch the game once with `DrakesOmniConfigServer` installed. It generates
   `BepInEx/config/DrakesOmniConfig/GameDataManifest.json` — every prefab, every component type actually
   attached to a real prefab, every field on those types (and on `Player`/`Humanoid`/`Character`), and every
   enum's real member names. (It's cheap after the first run — it only rescans when the number of prefabs
   changes, e.g. you add/remove a mod.)
2. Open `BepInEx/config/DrakesOmniConfig/ConfigBuilder/index.html` in a browser (it auto-deploys right next
   to the manifest). It tries to load the manifest automatically; if your browser blocks that for local
   files, use the page's own file picker and point it at the JSON.
3. Browse: pick a prefab → pick one of its real components → drill into its real fields (including nested
   structs) → pick a value control appropriate to that field's type (checkbox, number, dropdown for an enum,
   text otherwise) → it writes out the exact `manifest.yaml`/overrides YAML for you, ready to save into a
   modlet folder.

This is the same manifest you could read by hand if you wanted (it's just JSON), but the tool saves you the
trip through nested JSON and gets the exact field *type* right, which the hand-written examples below also
rely on for anything non-obvious.

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
- **Console commands are a reserved placeholder**, not yet built.
- The node-based scripting system (hooking arbitrary Harmony patch points visually, mentioned in the
  client README as "later") hasn't been designed yet — it's a deliberately later phase.

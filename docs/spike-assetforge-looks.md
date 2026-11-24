# Spike: new items with new colours, synced from the server

Date: 2026-11-23. Question: should Asset Forge's runtime be merged into DrakesOmniConfig, so a server admin can make
recoloured/reskinned items from the server menu or the HTML helper and have them sync, with real assets
(meshes, PNGs) still shipped only through mod packs?

## What exists today

| | DrakesOmniConfig | Forge Runtime (Asset Forge) | DrakesReskinIt |
|---|---|---|---|
| Makes new items | `clones:` (stats, name, recipe; same look as the source) | yes, `kind: item` + `base` | no |
| Looks | none | tint, gloss/metal, borrow materials by name, emission, fire/light colour, kitbash parts, sprites, PNG textures | per-item icon/model swap + tint |
| Server sync | yes (ServerSync, overrides + rules) | **no**: packs are local folders | per-item (cosmetic data on the item) |
| Dependencies | BepInEx only (Libs optional) | **Jotunn** in every core file (Registry, LookApplier, Host, Parts) | DrakeModsLibs |
| Input | YAML modlets | JSON recipes (`items/*.json`) + optional PNG/GLB | in-game menu |

Two facts decide it:

1. Forge's recipe **parser is already text-in**: `RecipeSerializer.ReadRecipe(string json, problems)` in
   `Forge.Format` (no dependencies). Only `PackReader.Load(folder)` touches disk. So a recipe can travel as a string.
2. Forge's **look engine is Jotunn-bound** (`LookApplier`, `ForgeRegistry`, `ForgeHost` all use Jotunn managers) and
   is ~3k lines that already handle the hard cases (armour body textures, atlases, hot reload, baselines).

## Options

**A. Merge Forge Runtime into OmniConfig.** OmniConfig gains a Jotunn dependency (its README promises "BepInEx
only"), ships two prefab registries (Forge's via Jotunn, OmniConfig's `PrefabCloner`) that can collide on ids, and
Forge's own releases (data packs, embedded C# exports) would need the same code kept in sync in two mods.
Not recommended.

**B. Re-implement looks in OmniConfig** (tint + borrowed materials only). Small at first, but it re-solves what
`LookApplier` already solved (shared materials, atlases, armour, icons) and drifts from Forge. Not recommended.

**C. Bridge (recommended).** Each mod keeps its job; OmniConfig carries Forge recipes over the wire:

- A modlet may hold `forge/*.json`: ordinary Forge recipes. Text only. A recipe may use everything Forge does
  *without files* (tint, gloss, emission, borrowed materials/meshes, kitbash parts, fire colours, fields, craft).
  A recipe that names a PNG/GLB must come from a pack already installed through the mod pack; the bridge never
  sends files.
- The server publishes them as a third ServerSync value beside `ResolvedOverrides` and `NodeRules`.
- On every side, OmniConfig hands them to Forge Runtime through one new public Forge call, e.g.
  `ForgeApi.LoadPackFromText(packId, IDictionary<string fileName, string json>)` (builds a `LoadedPack` in memory,
  then the existing `RegisterAll`/`Reload`). Soft dependency: without Forge Runtime the items are skipped with one
  clear warning, and plain `clones:` still work.
- Admin authoring: the HTML helper gets a "New item" page (pick a base from `GameDataManifest.json`, id/name,
  colour picker, gloss, borrow-a-material, stats, recipe) that writes a Forge recipe; the F10 panel gets a paste/
  upload like `rules.put` (`forge.put`, checked against the admin list). Hot reload already exists in Forge, so a
  pasted recipe can rebuild live.
- Node rules and overrides can target Forge items like any prefab, because they are real registered prefabs.

What C needs:

| Where | Work |
|---|---|
| Forge Runtime | `ForgeApi.LoadPackFromText` (+ unload) and a test; a Forge Runtime minor release |
| OmniConfig | load `forge/*.json` per modlet, third sync value + protocol bump, soft-call into Forge, admin `forge.put/delete` |
| HTML helper | "New item" page that writes Forge recipe JSON (reuses the Rules editor's palette data) |
| Tests | item built from a synced recipe exists, is tinted (material colour read back), survives rejoin |

`clones:` stays for the no-Forge case (stat-only variants like Poison Soup). Where both exist, a modlet should use one
or the other per id; a duplicate id is refused with a warning.

## Decision (2026-11-23)

Owner chose **one mod**: Forge's runtime source is compiled into OmniConfig (`src/Forge`, namespaced, same transform as Forge's
own C# export) instead of a soft dependency on a separately installed Forge Runtime. Jotunn stays a soft dependency.
The rest of option C stands (text-only sync, looks as Forge reskins, clones for new ids).

## Open questions for the owner (answered above)

1. OK for OmniConfig to soft-depend on Forge Runtime (no hard dependency, items skipped without it)?
2. OK to add `ForgeApi.LoadPackFromText` to Forge Runtime and cut a Forge Runtime minor release first?
3. ReskinIt stays separate (per-player, per-item cosmetics); the bridge is for server-made items only. Agreed?

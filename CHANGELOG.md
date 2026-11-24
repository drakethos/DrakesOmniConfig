# Changelog

## Unreleased
- **Other mods' settings** (Phase F1, `docs/plans/PHASE-F-mod-configs-and-flags.md`): a modlet's `configs:` list sets any
  installed mod's BepInEx setting (plugin GUID, section, key) with the same value math as overrides. Values live in
  memory only (the mod's `.cfg` file is never changed), relative edits start from the mod's own value, removing a
  binding puts it back, and two modlets on the same setting resolve like overrides (`override`, then priority). On a
  server, settings the mod syncs itself reach players through that mod; the rest through OmniConfig (new sync payload).
  Example modlet `com.drakemods.modconfigs` (off by default). Settings a mod reads only at start-up need a relog.
- **Shared variables, server flags** (Phase F2): `Modlets/vars.yaml` and `Modlets/groups.yaml` are shared by every
  modlet (a modlet's own file wins on the same name). `Modlets/flags.yaml` declares flags with defaults; admins switch
  them with the `flag.set` command (kept in `omni-flags.yaml`, survives restarts; `flag.list` shows them). A variable
  can depend on flags (`default:` + `when: { event_weekend: 4, "!pvp": 1 }`), and a modlet can require or exclude flags
  (`requiresFlags`, `unlessFlags` in its manifest). Switching a flag updates config bindings, rules and gated modlets at
  once; prefab field overrides on the next world load. On a server, every flag that is on is also the world global key
  `omni_flag_<name>`, readable by any mod (`MirrorFlagsToGlobalKeys`, on by default). `configs.list` shows every binding
  and what happened to it.
- **Admin control** (Phase F3): the F10 panel has a **Flags** section (switch each flag) and an **Other mods'
  settings** list (every binding, its winner and whether it applied). Server build: F5 console commands
  `omni flag <name> on|off|default`, `omni flags`, `omni configs`, `omni reload` (admins and the host only, through the
  same checked admin channel).
- **Config Builder: Mod settings page** (Phase F4, `mods.html`): pick any installed mod's setting and how to change
  it, and it writes the `configs:` YAML. Reads `ModConfigManifest.json`, which the Server build writes when a world
  loads (every mod's settings with type, default, allowed values, and whether the mod syncs it itself).
- Sync protocol is now 4 (config bindings payload): client and server must both have this build.
- **Faster loading with many items** (Phase G, `docs/phase-g-performance.md`). With 426 cloned, recoloured items,
  start-up cost over no mod went from +3.0 s to +0.7 s and the world-entry burst from ~1.4 s to ~0.2 s (before the
  main-menu prebuild):
  - modlets are read, resolved and prepared for sync on a background thread while the game starts, and used at world
    start when no modlet file changed in between;
  - item clones, their number edits and their looks are built at the main menu, a few milliseconds per frame, and
    only registered when the world loads (each copy is checked against its definition first; nothing is stored on disk);
  - a dedicated (headless) server no longer builds purely visual looks; they are still sent to players;
  - the override list is sent as JSON (readable by older clients), and the server no longer re-reads its own
    published values.
- Fix: looks on clones were lost from the second world load in a session on.
- **Looks:** `looks/*.json` in a modlet recolour/restyle items and clones, using Drakes Asset Forge's recipe format
  (Forge's look code is compiled in from `src/Forge`, refreshed by `build/scripts/sync_forge.py`). Synced from the
  server as text (no files), applied live. Admin commands `looks.put` / `looks.delete`. Needs Jotunn (soft
  dependency); without it looks are skipped.
- Clone **recipes**: `recipe:` on a clone (station, level, amount, requires).
- A joined client now builds the server's clones and overrides as soon as they arrive, not on its next world load.
- Config Builder **Items** page (`items.html`): pick a base, set id/name, colour, gloss, glow, fields and recipe; it
  writes the clone YAML and the look JSON.
- Sync protocol is now 3 (looks channel): client and server must both have this build.
- **Clones:** a modlet's `clones:` section makes a new item/prefab by copying an existing one (`id`, `from`, `name`,
  `description`, `fields`). Registered with the game's prefab and item tables, re-made on every world load, synced
  from the server like any override.
- **Node action `set`:** change any value when something happens, on the event's item, the player, the target, or a
  prefab, using the same field paths and value math as overrides.
- Example modlet `com.drakemods.poisonsoup`: Poison Soup, cloned from Serpent Stew, poisons instead of healing.
- **Node rules, world events:** new triggers `kill`, `place`, `destroy` (hammer), `interact`, `death` and `biome`
  (entering a biome; `{target}` is the biome name); new conditions `hasStatus` and `skillLevel`; new actions
  `aoeDamage`, `aoeHeal` (other players and creatures within a radius), `damageTarget` and `healTarget`.
- **Node rules** (Phase 2): YAML rule chains in a modlet's `nodes/` folder — a trigger (equip, unequip,
  pickup, craft, consume, hit, broke, interval, spawn), conditions (item, target, chance, quality, hasItem,
  equipped, health, biome, time; any can be inverted) and actions (give, remove, durability, status,
  removeStatus, heal, damage, stamina, skill, effect, spawn, message, popup with buttons), plus `else` and
  per-rule cooldowns. Rules sync from the server like overrides.
- Popups use DrakeModsLibs' window when it is installed (soft dependency) and a built-in window otherwise.
- Admin requests (reload, toggle a modlet or rule, upload or delete a rules file) from a server admin's
  client, checked against the server's admin list.
- Sync protocol is now 2: a client and server must both have node-rule builds to connect.

## 0.1.0-beta.2
- Fix: the release zip didn't include `YamlDotNet.dll` (not ILRepack-merged, unlike `ServerSync.dll`),
  so a clean install of 0.1.0-beta.1 threw a `TypeLoadException` on startup and loaded no modlets.
  The zip now carries every non-BepInEx dependency DLL alongside the plugin DLL.

## 0.1.0-beta.1
- Server/host now resolves modlets once and publishes the result to clients over a ServerSync
  channel; clients adopt what the server sends instead of resolving their own modlets locally.
  Pairs with `DrakesOmniConfigServer`.
- Not yet implemented: `Group`/`Category`/`Blanket` selector matching, console commands.
  See [ROADMAP.md](ROADMAP.md).

## 0.1.0
- Load and apply YAML modlets from `BepInEx/config/DrakesOmniConfig/Modlets/` — declarative field overrides
  on items, pieces, and the player, no custom code per tweak.
- `Scope.Prefab` (baked into a prefab template once) and `Scope.Global` (re-applied on every Player
  (re)spawn) override application.
- `Kind.Item` (exact id) and `Kind.NamePattern` (wildcard, e.g. `"Sword*"`) target selection.
- Value expressions: `Set`, `Add`/`Subtract` (flat or `%`), `Multiply`, `Divide`.
- `{var}` substitution via each modlet's `vars.yaml`.
- Conflict resolution between modlets touching the same field: selector specificity, then
  manifest `priority`, then load order, with an `override: true` escape hatch.
- Covered by an automated DrakesTestLab suite (player stats, weapons, armor, pieces).
- Not yet implemented: `Group`/`Category`/`Blanket` selector matching, server→client sync, console
  commands. See [ROADMAP.md](ROADMAP.md).

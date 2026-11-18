# Changelog

## Unreleased
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

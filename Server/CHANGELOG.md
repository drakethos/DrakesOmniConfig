# Changelog

## Unreleased
- Everything in the baseline's node rules (see its changelog).
- **Rules editor** (`ConfigBuilder/rules.html`): visual node palette built from `NodeCatalog.json`, lanes for
  when / if / do / else and popup buttons, typed arg inputs with item / status effect / skill / biome
  suggestions, live problem check, YAML export (copy or download) and import.
- **In-game admin panel** (F10, `[Admin] PanelKey`): the server's modlets and rules with on/off switches,
  Paste rules (from the editor's Copy) and Reload. Works for the host and for admins on a dedicated server.
- `NodeCatalog.json` written at world load next to `GameDataManifest.json`.
- Fix: the Config Builder pages now ship inside the DLL and are written to `ConfigBuilder/` at startup —
  the released zip never contained them before.
- The Server build no longer deploys itself into the `drakeTest` dev profile (it must never load next to
  the baseline); `-p:ServerDeployProfile=<profile>` deploys it to a profile you name.

## 0.1.0-beta.2
- Fix: the release zip didn't include `YamlDotNet.dll`, so a clean install of 0.1.0-beta.1 threw a
  `TypeLoadException` on startup and loaded no modlets. See the baseline changelog for detail.

## 0.1.0-beta.1
- Same sync change as `DrakesOmniConfig` 0.1.0-beta.1 (see its changelog): this build resolves modlets
  and publishes them to connected clients over ServerSync.

## 0.1.0
- Same modlet engine as `DrakesOmniConfig` 0.1.0 (see its changelog) — this build is for authoring/testing,
  not for regular players.
- `GameDataManifest.json` generation: every real prefab, every component type actually attached to one
  (plus `Player`/`Humanoid`/`Character`), every field (including nested structs) and enum member name —
  cached, only rescans when the prefab count changes.
- Config Builder HTML tool (`Server/Tools/ConfigBuilder/index.html`, auto-deployed next to the manifest):
  browse prefab → component → field, type-aware value controls, generates ready-to-save modlet YAML.
  Works from a plain `file://` page with a manual file picker, and auto-loads via `fetch()` where the
  browser allows it.
- Tools.ConsoleCommands: still a reserved placeholder, not yet implemented.

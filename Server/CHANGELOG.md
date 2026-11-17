# Changelog

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

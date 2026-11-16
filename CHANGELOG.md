# Changelog

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

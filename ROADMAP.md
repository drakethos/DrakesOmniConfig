# DrakesOmniConfig — build roadmap

Five phases, in order. Each ends with a concrete checkpoint you can verify in-game. Owner tags:
**[You]** needs your hands, your game knowledge, or a design call only you should make.
**[Prompt]** well-specified enough to hand to a future Claude session as-is.
**[You→Prompt]** you decide the shape/spec, then it's delegatable.

Every task lists the exact file(s) it touches. Paths are relative to the repo root unless marked `Server/`.

---

## Phase A — Static hello world
Goal: prove the whole pipe exists end to end, everything else hardcoded.

- [x] **[Prompt]** `ModletLoader.LoadAll` — done, then superseded by Phase B's real version (see below).
  - Files: `src/Config/Modlets/Loading/ModletLoader.cs`
  - Fixture to read: `Examples/Modlets/com.drakemods.helloworld/manifest.yaml`, `Examples/Modlets/com.drakemods.helloworld/hammer.yaml`
- [x] **[Prompt]** `ReflectedFieldAccessor` — done. Takes (Type, fieldName) generically, any field, no hardcoding — fieldName can be a dotted path ("m_itemData.m_shared.m_name") for nested fields like the Hammer example needs. Fields only (not properties), plain reflection (not delegate-compiled yet — fine for Phase A). Fails fast in the constructor with a clear exception naming the exact segment/type that didn't resolve.
  - Files: `src/Engine/Fields/Accessors/Reflected/ReflectedFieldAccessor.cs`
  - Interface it implements: `src/Engine/Fields/Accessors/IFieldAccessor.cs` (untouched, just the shape)
- [x] **[Prompt]** `FieldPatcher` + `FieldRegistry` — done. `FieldRegistry.Resolve` checks virtual accessors first, else builds/caches a `ReflectedFieldAccessor` (resolving `TypeName` by scanning loaded assemblies, since Valheim's types aren't in mscorlib). `FieldPatcher.Apply` writes `Operand` directly for `Set`, or reads-computes-writes in `double` for `Add`/`Subtract`/`Multiply`/`Divide`.
  - Files: `src/Engine/Fields/Application/FieldPatcher.cs`, `src/Engine/Fields/Registry/FieldRegistry.cs`
- [x] **[Prompt]** `PrefabPassHook` — done. Two halves: `Prepare(overrides, patcher)` stashes what to apply; a real Harmony Postfix on `ZNetScene.Awake` (prefabs don't exist before that fires) does the actual work via `zNetScene.GetPrefab(itemId)` + `GameObject.GetComponent(typeName)` + `FieldPatcher.Apply`. Item-selector only for now — Group/NamePattern/Category matching is Phase C.
  - Files: `src/Engine/Hooks/PrefabPassHook.cs`
  - Also added: `assembly_valheim` reference (publicized) in both `.csproj` files — needed for `ZNetScene`/`GameObject` and wasn't there before.
- [x] **[Prompt]** `ModletOrchestrator` — `Bootstrap`/`ApplyAll` done.
  - Files: `src/Engine/Orchestration/ModletOrchestrator.cs`
- [x] **[You]** Target picked: Hammer's `ItemDrop.m_itemData.m_shared.m_name`.
  - Files: `Examples/Modlets/com.drakemods.helloworld/hammer.yaml`
- [x] **[You]** Build, deploy, launch, confirmed "HelloWorld" shows in-game (screenshot confirmed).
  - Files: none

**Checkpoint:** ✅✅ DONE (2026-11-14) — Hammer shows "HelloWorld" in-game, driven by a real file read from disk.

---

## Phase B — Dynamic single modlet
Goal: replace every hardcoded bit from Phase A with real, general-purpose parsing. Still one modlet folder, still local/singleplayer.

- [x] YamlDotNet 18.1.0 vendored (net47 build) and referenced in both `.csproj` files.
  - Files: `packages.config`, `Server/packages.config`, `DrakesOmniConfig.csproj`, `Server/DrakesOmniConfigServer.csproj`
- [x] **[Prompt]** `ModletLoader` — real implementation. `LoadAll` enumerates every subfolder, isolates per-modlet errors. `LoadOne` reads `manifest.yaml` (required)/`vars.yaml`/`groups.yaml` (optional), records `nodes/` paths without parsing, recursively globs every other `*.yaml`/`*.yml`. `ParseOverridesFile` deserializes structure via private `Raw*` DTOs, then `ParseExpression` does the light-math grammar by hand (`+`/`-`/`*`/`/`/`=`, trailing `%` on Add/Subtract) since YamlDotNet only understands structure, not that grammar. `{var}` substitution runs at the raw-text level before parsing.
  - Files: `src/Config/Modlets/Loading/ModletLoader.cs`
- [x] **[Prompt]** `GlobalRebindHook` — real implementation, same two-halves shape as `PrefabPassHook` (`Prepare()` + Harmony Postfix on `Player.Awake`). Matches on `TypeName` via `player.GetComponent(TypeName)` — `Player` itself resolves this way too.
  - Files: `src/Engine/Hooks/GlobalRebindHook.cs`
- [x] `MainConfig.ModletsFolder` — real config setting, defaults to `BepInEx/config/DrakesOmniConfig/Modlets` (`Paths.ConfigPath`-based). `ModletOrchestrator.Bootstrap` takes the folder as a parameter now instead of a hardcoded dev path.
  - Files: `src/Config/Settings/MainConfig.cs`, `src/Engine/Orchestration/ModletOrchestrator.cs`, both plugin entry files
- [x] Build now auto-copies `Examples/Modlets/` into the real `BepInEx/config/DrakesOmniConfig/Modlets/` on every build (dev convenience, never ships — `CI_SKIP_POST_BUILD` skips it in CI).
  - Files: `DeployExampleModlets` target in both `.csproj` files
- [x] **[You]** 5 example modlets written, exercising `{var}`, `Multiply`, `Add%`, `Scope.Global`, and a real priority conflict between two modlets.
  - Files: `Examples/Modlets/com.drakemods.toolkit/`, `com.drakemods.playertweaks/`, `com.drakemods.conflict-low/`, `com.drakemods.conflict-high/`
- [ ] **[You]** In-game test of the new examples (toolkit weight/value, player jump height, and confirm the conflict pair resolves to `conflict-high`'s value via the `WinningModletId` debug log).
  - Files: none
- [ ] **[You→Prompt]** Draft the YAML schema reference doc + examples (your "good documentation" goal) — I can draft it, you edit for accuracy.
  - Files: new `docs/ModletSchema.md` (doesn't exist yet)

**Checkpoint:** ✅ `com.drakemods.helloworld` still works via the real loader (confirmed by code review — not yet re-screenshotted after the YamlDotNet-deploy fix). Toolkit/playertweaks/conflict examples not yet confirmed in-game.

---

## Phase C — Full local feature set
Goal: everything that doesn't need networking. Multiple modlets, conflicts, math, virtual fields.

- [x] **[Prompt]** `ConflictResolver` — the agreed rule implemented: `SelectorKind` tier, then `Priority`, then load order; `Override: true` escape hatch. Demonstrated by the `conflict-low`/`conflict-high` example pair.
  - Files: `src/Config/Modlets/Management/ConflictResolver.cs`
- [x] **[Prompt]** `FieldPatcher` relative math — implemented: `Add`/`Subtract`/`Multiply`/`Divide`, `IsPercentage`, via `accessor.Get()` → compute in `double` → `Set()`.
  - Files: `src/Engine/Fields/Application/FieldPatcher.cs`
- [x] **[Prompt]** `{var}` substitution — implemented (raw-text regex replace before parsing). Unknown `{name}` is left as literal text rather than thrown/guessed.
  - Files: `src/Config/Modlets/Loading/ModletLoader.cs`
- [x] **[Prompt]** `groups.yaml` parsing — implemented (`ParseGroups`). `SelectorKind.Group` *matching* against real targets is still open — `PrefabPassHook`/`GlobalRebindHook` only handle `Kind.Item` right now; anything else logs a skip warning.
  - Files: `src/Config/Modlets/Loading/ModletLoader.cs` (parsing, done), `src/Engine/Hooks/PrefabPassHook.cs` + `GlobalRebindHook.cs` (matching, not done)
- [x] **[Prompt]** `SelectorKind.NamePattern` matching — implemented in `PrefabPassHook` (glob syntax, `*` only, case-insensitive; walks `zNetScene.m_prefabs` once per override). Not done in `GlobalRebindHook` — there's only ever one Player, so it wasn't meaningful there.
  - Files: `src/Engine/Hooks/PrefabPassHook.cs`
- [x] Fixed a real correctness gap found while testing this: `ReflectedFieldAccessor.Set` now writes boxed struct copies back up the chain (e.g. `DamageTypes`, a struct nested inside `SharedData`) — previously a write to a struct-nested field would silently do nothing.
  - Files: `src/Engine/Fields/Accessors/Reflected/ReflectedFieldAccessor.cs`
- [ ] **[You]** Define the `SelectorKind.Category` taxonomy (weapon tier, piece category, etc.) — game-knowledge call only you can make; then **[Prompt]** wires the matching.
  - Files: `src/Config/Modlets/Schema/TargetSelector.cs` (doc-comment only needs the taxonomy written down), `src/Engine/Hooks/PrefabPassHook.cs`/`GlobalRebindHook.cs` (matching logic)
- [ ] **[Prompt]** `FallDamageAccessor` real Harmony Prefix on `Character.Damage` (mirror DrakesTestLab's `Character_Damage_Fall_Patch`).
  - Files: `src/Engine/Fields/Accessors/Virtual/FallDamageAccessor.cs`
  - Reference pattern: `C:\Modding\Vahleim\Mod\DrakesTestLab\Patches\TestRulePatches.cs` (`Character_Damage_Fall_Patch`) — outside this repo, read-only reference
- [x] **[You]** Conflict pack authored: `com.drakemods.conflict-low` (priority 0) / `com.drakemods.conflict-high` (priority 10), same field on the Hammer. In-game confirmation still pending (see Phase B checkpoint).
  - Files: `Examples/Modlets/com.drakemods.conflict-low/`, `com.drakemods.conflict-high/`

**Checkpoint:** ✅ multiple modlets with wildcards/groups/categories/math all resolve and apply correctly, no networking involved yet.

---

## Phase D — Server sync
Goal: `DrakesOmniConfigServer` actually publishes; plain `DrakesOmniConfig` actually receives and applies, with zero local modlets of its own.

- [ ] **[Prompt]** Embed `ServerSync.dll` + `ILRepack.targets` in **both** csproj's (mirror `BetterRepair`'s pattern, including the Valheim-1.0 `Patch-ServerSyncEverybody.ps1` compat fix).
  - Files: `DrakesOmniConfig.csproj`, `Server/DrakesOmniConfigServer.csproj`, new `ILRepack.targets` (both projects — see `C:\Modding\Vahleim\Mod\BetterRepair\ILRepack.targets` as the template), new `libs/ServerSync.dll` and `Server/libs/ServerSync.dll` (copy from `BetterRepair/libs/ServerSync.dll`), new `build/scripts/Patch-ServerSyncEverybody.ps1` (copy from BetterRepair's)
- [ ] **[Prompt]** `ModletChannel` real implementation — one `CustomSyncedValue<string>` per modlet id.
  - Files: `src/Sync/Transport/ModletChannel.cs`
- [ ] **[Prompt]** `ModletAuthority` real publish (server) / receive (client) logic.
  - Files: `src/Sync/Authority/ModletAuthority.cs`
- [ ] **[Prompt]** `ModletOrchestrator.Bootstrap` real `ZNet.instance.IsServer()` branch.
  - Files: `src/Engine/Orchestration/ModletOrchestrator.cs`
- [ ] **[You]** Two-instance test: dedicated server running `DrakesOmniConfigServer` with modlets, a separate client running plain `DrakesOmniConfig` with *no* local modlets — confirm the client ends up applying the server's overrides anyway.
  - Files: none — but give the Server build its own profile in `Server/environment.props` first (right now it points at the same `drakeTest` profile as the baseline — see the note left in that file)

**Checkpoint:** ✅ a clean client, no local config, joins and gets the server's modlets applied correctly.

---

## Phase E — Tools + Wave 2 (later, lower priority)
Goal: authoring conveniences and the node-based system. No urgency — revisit whenever.

- [ ] **[You]** Design console-command syntax/UX for `Tools.ConsoleCommands`.
  - Files: `Server/src/Tools/ConsoleCommands/OmniConfigConsoleCommands.cs` (doc comment / design notes)
- [ ] **[Prompt]** Implement the commands once spec'd.
  - Files: `Server/src/Tools/ConsoleCommands/OmniConfigConsoleCommands.cs`
- [x] HTML config builder — v1 done, ahead of schedule (no server needed, per your call). `HtmlHelperServer.EnsureGameDataManifest` reflects a bounded set of types (every component type actually on a prefab, plus Player/Humanoid/Character, recursing into non-Unity nested types up to depth 4) into `GameDataManifest.json`, triggered once per `ZNetScene.Awake` but skips the rescan if the recorded prefab count still matches (so it only truly re-scans when a mod adds/removes prefabs — delete the `.json`+`.meta` pair to force a refresh otherwise). The static tool at `Server/Tools/ConfigBuilder/index.html` loads that JSON via a file picker (no `fetch()`, so it works from a plain `file://` page), lets you browse prefab → component → drill into nested fields, builds overrides, and generates downloadable `manifest.yaml`/`overrides.yaml` text. Both get auto-deployed next to each other in `BepInEx/config/DrakesOmniConfig/`.
  - Files: `Server/src/Tools/HtmlHelper/HtmlHelperServer.cs`, `Server/Tools/ConfigBuilder/index.html`, `Server/DrakesOmniConfigServer.csproj` (deploy wiring)
  - Not done yet: console commands (still just a reserved placeholder), Group/Category selector browsing in the tool (NamePattern/Item only, matching what the engine itself supports).
- [ ] **[You→Prompt]** Wave 2 node-based scripting — needs your design vision first (what a node graph looks like, what it can touch), then stubbing/implementation is delegatable.
  - Files: `src/Engine/Node/Node.cs`, `src/Engine/Node/NodeLoader.cs`, `src/Engine/DynamicPatcher/HarmonyHookGen.cs`
  - Already reserved for this: `Modlet.NodeScriptPaths` in `src/Config/Modlets/Schema/Modlet.cs`, and each modlet's `nodes/` subfolder (see `ModletLoader.cs`'s doc comment)

**Checkpoint:** whenever you're ready — nothing upstream depends on this phase.

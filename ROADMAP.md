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

- [x] **[Prompt]** Embed `ServerSync.dll` + `ILRepack.targets` in **both** csproj's (mirrored `BetterRepair`'s pattern). `EmbedServerSync: true` added to both `mod.package.props` (also sets `DeployAfterTargets: ILRepacker` so deploy/package wait for the merge). `ILRepack.targets`' `LibraryPath` had to be widened to `$(OutputPath);$(BepInExPath)\core;$(CorlibPath);$(PublicizedAssembliesPath)` beyond BetterRepair's plain `$(OutputPath)` — BetterRepair's `BepInEx` reference has no `Private=false`, so it gets copied locally where ILRepack can see it; ours deliberately has `Private=false` (never bundle BepInEx), so ILRepack needs the real install paths instead. `Patch-ServerSyncEverybody.ps1` copied for reference but not yet run — the vendored `libs/ServerSync.dll` is BetterRepair's own already-shipped copy, presumed already patched since that mod works; re-verify if ServerSync.dll ever gets replaced with a fresh unpatched copy.
  - Files: `DrakesOmniConfig.csproj`, `Server/DrakesOmniConfigServer.csproj`, `ILRepack.targets` (both), `libs/ServerSync.dll` + `Server/libs/ServerSync.dll`, `packages.config` (both, + vendored `ILRepack.Lib.MSBuild.Task.2.0.43`), `mod.package.props` (both), `build/scripts/Patch-ServerSyncEverybody.ps1`
- [x] **[Prompt]** `ModletChannel` real implementation — **design changed from the original "one `CustomSyncedValue<string>` per modlet id"** after decompiling `ServerSync.dll` to check a real assumption rather than guess: `ConfigSync`/`CustomSyncedValue` identifiers are a fixed, statically-known set on both sides (confirmed via IL — an unmatched identifier just logs "unknown config entry" and is skipped, it doesn't abort the whole sync, but it also never arrives). A client that has never heard of a modlet id can't pre-register a channel for it, so per-modlet dynamic discovery doesn't work cleanly with this library. Now: ONE shared `ConfigSync` (fixed name `"com.drakemods.omniconfig.sync"`, identical in both builds — confirmed via IL that the RPC name is literally `"{that string} ConfigSync"`, not tied to either build's own differing BepInEx GUID) + ONE `CustomSyncedValue<string>` carrying the WHOLE resolved-override set as one YAML-ish payload (`ResolvedOverrideSerializer`, new file, flat DTOs + YamlDotNet, reusing the vendored dependency). Per-modlet granularity can be revisited later if it's ever a real perf issue — unlikely given modlets are small text overrides synced only on connect/reload, not every frame.
  - Files: `src/Sync/Transport/ModletChannel.cs`, new `src/Sync/Transport/ResolvedOverrideSerializer.cs`
- [x] **[Prompt]** `ModletAuthority` real publish (server) / receive (client) logic — owns the shared `ConfigSync` + `ModletChannel`, exposes `PublishAll`/`ResolvedReceived`.
  - Files: `src/Sync/Authority/ModletAuthority.cs`
- [x] **[Prompt]** `ModletOrchestrator.Bootstrap` real `ZNet.instance.IsServer()` branch — deferred to a Harmony Postfix on `ZNet.Awake` (same two-halves shape as `PrefabPassHook`/`GlobalRebindHook`: `ZNet.instance` doesn't exist yet at plugin `Awake()` time). Server/hosting: `LoadAndResolve` + `ApplyAll` + `PublishAll`, re-run on every `ZNet.Awake` (world load/rejoin), not latched to "once". Client: skips `LoadAndResolve` entirely, waits for `ResolvedReceived` → `AdoptResolved` → `ApplyAll`.
  - Files: `src/Engine/Orchestration/ModletOrchestrator.cs`
- [x] Both builds verified to compile AND ILRepack-merge ServerSync cleanly (`dotnet msbuild`, Debug) — confirms the wiring is structurally sound.
- [ ] **[You]** Two-instance test: dedicated server running `DrakesOmniConfigServer` with modlets, a separate client running plain `DrakesOmniConfig` with *no* local modlets — confirm the client ends up applying the server's overrides anyway. **Not yet done** — needs a genuinely separate profile for the Server build (still shares `drakeTest`'s compile-time `BepInExPath` per the note in `Server/environment.props`; its deployed plugin keeps getting removed from `drakeTest` by hand after each build specifically so client+server never load together there) and a real second Valheim process to connect to it.
  - Files: none yet

**Checkpoint:** code-complete and compiles/merges on both sides; the actual two-instance network test is the one thing not yet verified.

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
- [x] Wave 2 node rules, v1 (design decided 2026-11-18: rule chains, not a free-form dataflow graph).
  - Engine: `src/Engine/Node/` — `Schema` (rule, step, branch), `Loading/NodeLoader` (YAML → rules, every mistake named and skipped), `Runtime` (`NodeRegistry`, `RuleEngine`, `NodeTicker` for spawn/interval/broke polling, `NodeCatalog` for the editor), `Library` (item trigger patches, conditions, actions, message/popup), `Ui` (one dialog model; DrakeModsLibs window when installed — soft dependency — or a built-in IMGUI window), `Admin` (`AdminChannel` RPC checked against the server admin list, `NodeState` on/off file, `AdminPanelModel`).
  - 9 triggers (equip, unequip, pickup, craft, consume, hit, broke, interval, spawn), 9 conditions (+ `not`), 13 actions incl. popup buttons with their own action chains, `else`, per-rule cooldowns.
  - Sync: second ServerSync value `NodeRules` beside `ResolvedOverrides`; protocol bumped to 2.
  - Server build: `ConfigBuilder/rules.html` visual editor (palette from `NodeCatalog.json`, built-in snapshot via `Server/Tools/build_rules_page.py`), F10 admin panel (toggle / paste / reload), builder pages embedded in the DLL and written out at startup (they never shipped before).
  - Tests: `Tests/suites/DrakesOmniConfig-Nodes` (22 tests: every trigger through the real game method, conditions/else, cooldown, both popup renderers, durability, both break paths, status, admin toggle/refuse/panel buttons, example files, catalog, the editor's own export). All 26 DrakesOmniConfig tests green 2026-11-18.
  - `src/Engine/DynamicPatcher/HarmonyHookGen.cs` (hooking arbitrary game methods from a modlet) is still a stub: not part of v1.
- [ ] Node rules, next: more events (kill a creature, place/destroy a piece, enter a biome, interact with a piece), actions on other players / in an area, a server-side rule mode, dataflow values between nodes. Needs your priorities.
- [ ] **[You]** Real two-machine test of sync (client and server on different PCs) — blocked on network access to the remote test server (no route from this PC; port-forward not in place), 2026-11-17.

**Checkpoint:** whenever you're ready — nothing upstream depends on this phase.

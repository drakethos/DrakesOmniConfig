# Phase F: other mods' configs, shared variables and server flags

**Status:** planned, not started. Written 2026-11-24 as a hand-off for an implementing agent.
**Owner tags:** same as [ROADMAP.md](../../ROADMAP.md): **[Prompt]** delegatable as written, **[You]** needs Drakethos, **[You→Prompt]** decide first, then delegate.

## 1. Goal

Make OmniConfig the **one place a server admin tunes a whole modded server**, beyond prefab fields:

1. **`configs:`** in a modlet sets **any other mod's BepInEx config entry** (by plugin GUID, section, key). Same light-math grammar, same conflict rules as `overrides:`.
2. **Shared variables** across modlets (`Modlets/vars.yaml`), not only per modlet.
3. **Server flags** (`event_weekend`, `pvp`, ...) that admins switch at runtime. Flags drive **conditional variables** and can switch **whole modlets** on or off.
4. **Admin control:** flag commands (admin channel, F10 panel, F5 console).
5. **Zero-dependency integration for other mods:** flags are mirrored into vanilla world **global keys**, readable by any mod without referencing OmniConfig.

### Decisions already made (do not reopen)

- **No new sync system.** OmniConfig keeps its embedded ServerSync for its own payloads. Other mods keep their own sync (embedded ServerSync, Jotunn). OmniConfig never replaces, patches or intercepts another mod's ServerSync.
- **Mods' `.cfg` files on disk are never edited.** Bound values live in memory only; removing a binding restores the mod's own value.
- **No `$var` syntax inside other mods' `.cfg` files.** BepInEx would parse and overwrite it at bind time; it would need a preloader patcher. Bindings live in OmniConfig's YAML.
- **No per-player or per-group flags in this phase.** Leave a hook for later (section 7).
- The separate "ServerSyncPlus" shared-dependency idea was evaluated and dropped: the community, including ServerSync's author, rejects a shared ServerSync, and its benefits were small. Background is in the owner's notes; it doesn't affect this phase.

## 2. Why this works: delivery to players

The server is authoritative (`ModletOrchestrator.OnZNetAwake` → `LoadPublish`). For each bound entry:

| The target mod... | How the value reaches players |
|---|---|
| syncs that entry itself (embedded ServerSync, or Jotunn admin-only) | **Nothing extra.** OmniConfig sets the value on the server, and the mod's own sync pushes it on login and on change. |
| does not sync it | OmniConfig's sync carries it: a **new third payload** on the existing `ConfigSync`, applied by the client build. |

Detection (both sides, same function): the entry's `ConfigDescription.Tags` contains an object whose type (or base type) is named `SyncedConfigEntry`1` / `OwnConfigEntryBase` (any ServerSync copy; check `SynchronizedConfig == true` via reflection if present), or a `ConfigurationManagerAttributes`-like tag with `IsAdminOnly == true` (Jotunn). Match by **type name**, never by type identity: every mod's ServerSync copy is a different type.

## 3. Current architecture: what to read first

| Concern | File |
|---|---|
| Entry points (client, server builds) | `src/DrakesOmniConfig.cs`, `Server/src/DrakesOmniConfigServer.cs` |
| Loading modlets, `{var}` substitution, light-math parse | `src/Config/Modlets/Loading/ModletLoader.cs` |
| Schema | `src/Config/Modlets/Schema/*.cs` |
| Conflicts (tier → priority → load order, `override` hatch) | `src/Config/Modlets/Management/ConflictResolver.cs` |
| Resolved state | `src/Config/Modlets/Management/ModletManager.cs` |
| Server/client branch, reload, admin commands | `src/Engine/Orchestration/ModletOrchestrator.cs` |
| Sync (one `ConfigSync` `"com.drakemods.omniconfig.sync"`, payloads `ResolvedOverrides` + `NodeRules`, `ProtocolVersion = "2"`) | `src/Sync/Authority/ModletAuthority.cs`, `src/Sync/Transport/*` |
| Admin RPC (admin-list checked) + persisted toggles | `src/Engine/Node/Admin/AdminChannel.cs`, `NodeState.cs`, `AdminPanelModel.cs`, `Server/src/Tools/AdminPanel/AdminPanel.cs` |
| Console commands (empty placeholder) | `Server/src/Tools/ConsoleCommands/OmniConfigConsoleCommands.cs` |
| In-game tests (DrakesTestLab JSON suites + `[TestHook]`) | `Tests/suites/*`, `src/Tools/TestHooks/*` |

## 4. Specification

### 4.1 `configs:` entries

Any non-reserved YAML file in a modlet may have a top-level `configs:` list next to (or instead of) `overrides:`. List form only, because section and key names can contain `:`.

```yaml
configs:
  - mod: Azumatt.AzuCraftyBoxes        # BepInEx plugin GUID (exact, case-insensitive)
    section: "1 - General"
    key: "Stack multiplier"
    value: "{stackMult}"               # same grammar as fields: "5", "=5", "*1.5", "+20%", "-5", text, true/false, enum name
    override: false                    # same escape hatch as overrides
```

- **Parsing:** reuse `ParseExpression`. `true`/`false` and enum names are `Set` with a string operand.
- **Conflicts:** extend `ConflictResolver` with a second key space `config|{guid}|{section}|{key}` (lowercased guid). Tier is always Item (exact target). Priority, load order and `override` work as today. Output: new `ResolvedConfigBinding { Mod, Section, Key, Expression, WinningModletId }`.
- **Baseline, so values don't compound:** relative operators apply to the entry's **baseline**: the value it had before OmniConfig first touched it. Record it once per entry, re-apply from the baseline on every resolve, and restore it when a binding disappears. Without this, every reload or world load multiplies again.
- **Applying (new `src/Engine/Config/ConfigBindingApplier.cs`):**
  1. Find the plugin: `BepInEx.Bootstrap.Chainloader.PluginInfos[guid].Instance.Config` (case-insensitive lookup).
  2. Find the entry by `ConfigDefinition(section, key)` without throwing (iterate `Keys`).
  3. Compute the value: numbers in `double`, then `Convert.ChangeType` to `SettingType`. Strings, bools and enums go through `TomlTypeConverter.ConvertToValue`. Respect `AcceptableValues` (clamp, or reject with a warning).
  4. Write in memory: set `ConfigFile.SaveOnConfigSet = false`, then `entry.BoxedValue = v`, then restore the flag. Setting `BoxedValue` raises the mod's `SettingChanged`; on the server that is what makes the mod's own ServerSync push the new value.
  5. **Disk protection:** Harmony prefix on `ConfigEntryBase.GetSerializedValue`. For entries OmniConfig currently binds, write the baseline, so any later `Save()` by the mod or a config manager never persists OmniConfig's value. ServerSync copies patch the same method on clients; match by entry, return `false`, and make sure the result for bound entries is the baseline. Test this together with a ServerSync mod (section 6).
  6. **Missing targets:** a GUID that isn't installed or a key that doesn't exist (yet) is logged **once** as a warning, then retried on the next resolve and on `ZNet.Awake`. Some mods bind entries late.
- **Delivery:**
  - On the server, apply everything.
  - Publish bindings whose entries are not self-synced (section 2) in a new payload `ConfigBindings` (serializer like `ResolvedOverrideSerializer`) and bump `ProtocolVersion` to `"3"`.
  - The client applies received bindings with the same applier: baseline, in memory, disk-protected. It **skips** entries the target mod syncs itself, and restores baselines on disconnect (postfix on `ZNet.Shutdown`, like ServerSync's own reset).
  - While connected, if the player changes a bound entry, re-apply the server value. Guard against recursion.
- **Known limitation, document it:** mods that read a setting only once at startup won't see a new value until relog or restart. It can't be detected reliably; say so in the README.

### 4.2 Shared variables

- New optional file **`Modlets/vars.yaml`** at the modlets root, not inside a modlet. `LoadAll` currently only enumerates subfolders, so this is backward compatible.
- Lookup order for `{name}`: modlet `vars.yaml`, then root `vars.yaml`. Unknown names stay literal (current behavior).
- Do the same for **`Modlets/groups.yaml`** while there. It's the open TODO in `Modlet.Groups`.

### 4.3 Flags and conditional variables

- New optional file **`Modlets/flags.yaml`**: declared flags with defaults.

  ```yaml
  flags:
    event_weekend: false
    pvp: true
  ```

- **Runtime state:** admin changes persist across restarts in the same place and style as `NodeState` (a sibling file, e.g. `omni-flags.yaml`, next to wherever `NodeState` saves). Effective value = saved state if present, else the default.
- **Conditional variables** (root or modlet `vars.yaml`), resolved **before** `{var}` substitution, so overrides, configs and node rules all get them for free:

  ```yaml
  stackMult:
    default: 2
    when:              # first matching flag in listed order wins
      event_weekend: 4
  carryBonus: 150      # plain scalars keep working
  ```

  `when` keys may be negated with `!flag`.
- **Modlet gating:** new manifest fields `requiresFlags: [a, b]` (all must be on) and `unlessFlags: [c]` (none may be on). A gated-off modlet behaves like `enabled: false` for overrides, configs and rules.
- **Changing a flag** re-runs `LoadPublish()`. Configs and node rules update immediately. **Prefab field overrides currently only re-apply on the next world load** (see the `modlet.toggle` message in `ModletOrchestrator`). Keep that behavior and state it in the reply message. Making prefab overrides re-apply live is out of scope.
- **Vanilla global key mirror** (server only, `MainConfig` toggle `MirrorFlagsToGlobalKeys`, default `true`):
  - For each flag that is on, set world global key `omni_flag_<name>`; remove it when off.
  - Verify the exact `ZoneSystem` set/remove API against the real `assembly_valheim.dll`, and how keys sync to clients, before relying on it.
  - Global keys persist in the world save. On startup, remove `omni_flag_*` keys whose flag is now off or no longer declared.
  - This is **the** integration path for other mods in this phase: `ZoneSystem.instance.GetGlobalKey("omni_flag_pvp")`, no reference to OmniConfig needed.
- **No public C# API in this phase.** The client and server builds are different assemblies (`DrakesOmniConfig.dll` vs `DrakesOmniConfigServer.dll`), so a hard reference to one breaks with the other. If a typed API is wanted later, see section 7.

### 4.4 Admin control

- **`AdminChannel` commands:**
  - `flag.set` (arg = name, payload = `true`/`false`)
  - `flag.list`
  - `configs.list`: every resolved binding with winning modlet, delivery path (mod's own sync / OmniConfig), applied or missing
  - Same admin-list check as existing commands.
- **F10 panel (`AdminPanelModel` / `AdminPanel`):** a Flags section with toggles, and a read-only Bindings list.
- **F5 console (server build, fill the placeholder):**
  - `omni flag <name> on|off`
  - `omni flags`
  - `omni configs`
  - `omni reload`

  Register via a postfix on `Terminal.InitTerminal` with `new Terminal.ConsoleCommand(name, description, args => ...)`. Commands route through `AdminChannel.Send` so they work from an admin's client against a dedicated server.

### 4.5 Authoring support (later in this phase)

- `HtmlHelperServer` also writes **`ModConfigManifest.json`** with every loaded plugin's entries: guid, section, key, type, default, description, acceptable values, and whether the mod syncs it itself.
- The ConfigBuilder page gains a "Mod settings" tab that generates `configs:` entries.

## 5. Tasks

Each task should build both projects (section 8) and update `CHANGELOG.md` → *Unreleased*.

### F1: configs core
- [ ] **[Prompt]** Schema + loader: `configs:` parsing, `ResolvedConfigBinding`.
  - Files: new `src/Config/Modlets/Schema/ConfigBinding.cs` (+ resolved type), `ModletLoader.cs` (Raw DTOs + parse), `Modlet.cs` (`ConfigBindings` list)
- [ ] **[Prompt]** Conflict resolution for bindings (second key space, same rules) + `ModletManager` exposes `ResolvedConfigBindings`.
  - Files: `ConflictResolver.cs`, `ModletManager.cs`
- [ ] **[Prompt]** `ConfigBindingApplier`: lookup, baseline, conversion and clamping, in-memory write, `GetSerializedValue` disk protection, missing-target retry, restore on unbind.
  - Files: new `src/Engine/Config/ConfigBindingApplier.cs`, new `src/Engine/Config/SyncDetection.cs` (self-synced detection, type-name based)
- [ ] **[Prompt]** Orchestrator wiring: apply on server in `LoadPublish`; client adoption path; reset on `ZNet.Shutdown`.
  - Files: `ModletOrchestrator.cs`
- [ ] **[Prompt]** Sync: `ConfigBindings` payload + serializer, `ProtocolVersion` → `"3"`.
  - Files: `ModletAuthority.cs`, new `src/Sync/Transport/ConfigBindingSerializer.cs`
- [ ] **[Prompt]** Add every new file to **both** `DrakesOmniConfig.csproj` and `Server/DrakesOmniConfigServer.csproj`. Both use explicit `<Compile Include>` lists.
- [ ] **[Prompt]** Example modlet `Examples/Modlets/com.drakemods.modconfigs/`: one binding to a test target (see F1 tests), plus one deliberate conflict pair.

**F1 checkpoint:** on a dedicated server, a modlet binding changes another mod's setting; a joined client sees the value. That covers both a mod with its own ServerSync and a mod without sync. Disconnecting restores the client's value, and no `.cfg` file on either side changes.

### F2: shared variables, flags, conditions
- [ ] **[Prompt]** Root `vars.yaml` + `groups.yaml`, lookup order modlet → root.
  - Files: `ModletLoader.cs`, `Modlet.cs`
- [ ] **[Prompt]** `flags.yaml` + persisted flag state + effective-value logic.
  - Files: new `src/Config/Flags/FlagSet.cs`, new `src/Config/Flags/FlagState.cs` (mirror `NodeState`'s load/save style)
- [ ] **[Prompt]** Conditional variables (`default` / `when` / `!flag`), resolved before substitution.
  - Files: `ModletLoader.cs` (+ a small pure resolver class so it is unit-testable)
- [ ] **[Prompt]** Manifest gating `requiresFlags` / `unlessFlags`.
  - Files: `ModletManifest.cs`, `ModletLoader.cs` or `ModletManager.cs` (where `Enabled` is evaluated)
- [ ] **[Prompt]** Global key mirror + startup cleanup + `MainConfig.MirrorFlagsToGlobalKeys`.
  - Files: new `src/Engine/Flags/GlobalKeyMirror.cs`, `MainConfig.cs`
- [ ] **[You]** Pick the global key prefix (proposed `omni_flag_`) and confirm the flag naming rules (proposed `^[a-z0-9_]{1,32}$`).

**F2 checkpoint:** toggling `event_weekend` on the server changes a conditional variable used in a `configs:` binding, and players get the new value without a relog. `listkeys` shows `omni_flag_event_weekend` while it's on.

### F3: admin control
- [ ] **[Prompt]** `AdminChannel` commands `flag.set`, `flag.list`, `configs.list` (handler in `ModletOrchestrator.HandleAdmin`).
- [ ] **[Prompt]** F10 panel: Flags toggles + Bindings list.
  - Files: `AdminPanelModel.cs`, `Server/src/Tools/AdminPanel/AdminPanel.cs`
- [ ] **[You→Prompt]** Console syntax. Section 4.4 is a proposal; confirm or change it, then implement in `Server/src/Tools/ConsoleCommands/OmniConfigConsoleCommands.cs`.

### F4: authoring tools
- [ ] **[Prompt]** `ModConfigManifest.json` writer.
  - Files: `Server/src/Tools/HtmlHelper/HtmlHelperServer.cs` (or a new sibling writer)
- [ ] **[Prompt]** ConfigBuilder "Mod settings" tab generating `configs:` YAML.
  - Files: `Server/Tools/ConfigBuilder/index.html`

### F5: docs
- [ ] **[Prompt]** Document `configs:`, root vars and groups, flags, conditions, gating, commands, the global-key integration snippet, and the startup-only limitation.
  - Files: `Server/README.md` (the wiki), `README.md` (short), `ROADMAP.md` (tick boxes)

## 6. Tests

Follow the existing **DrakesTestLab** pattern: a new suite `Tests/suites/DrakesOmniConfig-Configs/` plus `[TestHook]` methods in `src/Tools/TestHooks/`.

- **Test hook `omniconfig.cfg`:** args `{ mod, section, key }` returns the entry's current `BoxedValue` (string). A second variant returns the value as it would be written to disk (via `GetSerializedValue`), to prove disk protection.
- **Test targets:**
  - OmniConfig's own `MainConfig` entries are a convenient always-present target without ServerSync.
  - For the self-synced path, add a tiny test-only plugin, or use any installed ServerSync mod in the test profile. **[You]** pick one.

| Test | Expect |
|---|---|
| CF-01 Set | Bound entry has the modlet's value; disk value unchanged |
| CF-02 Relative | `*2` applied twice (two reloads) still equals baseline × 2 |
| CF-03 Conflict | Higher-priority modlet wins; `configs.list` names the winner |
| CF-04 Unbind | Removing the binding + reload restores the baseline |
| CF-05 Missing target | Unknown GUID/key: one warning, no error, nothing else affected |
| CF-06 Type safety | Bad value for an int entry: warning, entry unchanged; range clamp respected |
| CF-07 Root vars + condition | `when: { event_weekend: ... }` value switches when the flag toggles |
| CF-08 Gating | `requiresFlags` modlet on/off follows the flag |
| CF-09 Global key | `omni_flag_x` present iff the flag is on; stale keys removed at startup |
| CF-10 Admin refuse | Non-admin `flag.set` refused (mirror NR-15) |
| CF-11 Two-instance | Dedicated server + client: both delivery paths, restore on disconnect. **[You]**: same blocker as Phase D's two-machine test |

Where logic is pure (conditional-variable resolution, binding key normalization, value conversion), keep it in small classes with no Unity calls. If the repo gains a unit-test project later, they drop straight in.

## 7. Later (not this phase)

- **Per-player / per-group flags:** needs a client-side override channel that targets one player, and re-applying after the mod's own sync updates. The design hook: keep "effective flags" behind one function `FlagSet.IsOn(name, ZNetPeer? peer = null)` so a peer-aware version can slot in.
- **Typed API for other mods:** a tiny contract (e.g. `OmniFlags.IsOn(string)`) that resolves either build's assembly by name at runtime, shipped as a copy-in snippet.
- **Live re-apply of prefab overrides** on flag changes.
- **Scheduled flags** ("event_weekend on Fri 18:00 – Sun 23:59").

## 8. Build, verification and gotchas

- **Builds:** both projects must compile and ILRepack-merge ServerSync, the same way CI does (`.github/workflows/release.yml`: Pfhoenix reference package staged as a fake install, `dotnet msbuild`). Do not tag or publish. The release workflow publishes to Hexium on tags.
- **Explicit compile lists:** both `.csproj` files list every `.cs` file by hand. A new file missing from either list compiles in one build and is silently absent from the other.
- **`Version` name clash:** `assembly_valheim` has a global type named `Version` that shadows `System.Version`. Write `System.Version` explicitly if you need it.
- **Embedded ServerSync is patched:** `libs/ServerSync.dll` was patched by `build/scripts/Patch-ServerSyncEverybody.ps1` (Valheim 1.0 made `ZRoutedRpc.Everybody` a const). Re-run that script if the DLL is ever replaced.
- **Never** add `[NetworkCompatibility]`, and keep the two builds' differing GUIDs. See the comments in both entry points.
- **Detect other mods' sync by type name only.** Every mod's ServerSync is a different internal type.
- Keep OmniConfig's own sync on the single existing `ConfigSync`. Add payloads, not new `ConfigSync` instances. Identifiers must be known on both sides at startup (see the `ModletChannel` doc comment).

## 9. Open questions for the owner

1. **[You]** Should a binding on a mod that *does* sync the entry itself also be sent in OmniConfig's payload, as a belt-and-braces fallback? The plan says no, to avoid two systems fighting on the client.
2. **[You]** When an admin edits a bound entry from their own config manager, should OmniConfig re-apply its value on the server (OmniConfig wins), or update the binding? The plan says re-apply and log.
3. **[You]** Should flags be declared only in `Modlets/flags.yaml`, or may modlets declare their own flags too (namespaced by modlet id)?

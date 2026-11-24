# Phase G: performance (load time at scale, dedicated servers)

Started 2026-11-24. Goal: a modlet set with hundreds of new items (clones with stats, recipes and looks) must load
faster and lighter than Wacky's Database (WackyDB) doing the same job, on players' games and on dedicated servers,
without WackyDB's cache pitfalls. (Roadmap: `ROADMAP.md`, Phase G.)

## How it is measured

Benchmark tools in `~/Modding/Vahleim/Tools/bench/` (plan: `docs/benchmark-wackydb-plan.md`):

- One loadout, generated into both formats by `make_loadout.py`: each item is a clone of a vanilla item with its own
  name, 3 stat changes, a workbench recipe and a recolour. **Small** = 10 items, **massive** = 426 (every vanilla item
  with a recipe, an icon and a coloured model, minus 2 TestLab items and 2 that WackyDB 2.6.14 does not recolour as
  specified: Demister, StaffRedTroll; OmniConfig recolours both).
- Profiles rebuilt from scratch per launch: no mod, Jotunn only, OmniConfig (+Jotunn), WackyDB cold, WackyDB warm cache.
- `BenchProbe` (same in every profile) checks every loadout item really exists with its name, stats, recipe and colour
  (a run that built fewer items fails), times the game methods content mods hook, and reads managed memory after a
  full GC. Optional per-patch profiling for diagnostic runs only.
- 1 warm-up + 5 timed launches per setup, each its own Valheim queue turn, always Vulkan; medians reported.
- WackyDB and no-mod numbers are stored; each OmniConfig change is timed alone (`bench_all.py run --modes omni --tag X`).

## Results (massive, 426 items; cost over no mod, median of 5)

| | Start to playable | World-entry burst | Total memory |
|---|---|---|---|
| OmniConfig before Phase G | +3.0 s | +1,420 ms | +20 MB |
| opt1 (Forge batched scene searches) | +2.2 s | +575 ms | +19 MB |
| opt2 (no self-echo re-parse) | +1.5 s | +575 ms | +20 MB |
| opt3 (JSON sync writer, one registration pass) | +1.2 s | +662 ms | +20 MB |
| opt4 (Forge recipe-scan fix) | +0.7 s | +220 ms | +19 MB |
| opt5 (background load, headless looks) | +0.7 s | +225 ms | +19 MB |
| **opt6 (main-menu prebuild)** | **+0.4 s** | **+90 ms** (world entry +36, main menu +56) | +20 MB |
| **WackyDB 2.6.14** (cold / warm cache) | +3.4 s / +4.3 s | +49 ms (world entry +49) / +62 | +36 MB |

"World-entry burst" here is ObjectDB.Awake + ZNetScene.Awake + the main-menu CopyOtherDB. Split out for opt6: world
entry +36 ms (WackyDB +49), plus a fixed ~56 ms once at the main menu (Forge/Jotunn setup, the same at 10 or 426
items). Small loadout, opt6: world entry +15 ms vs WackyDB +58 ms; start +0.4 s vs +1.7 s; memory +0.8 MB vs +8 MB.
ZNet.Awake varies by about ±70 ms between runs for every setup (WackyDB included), so it is left out.

Frame times (idle, inventory open, workbench open) are the same for both mods within run-to-run noise. Small loadout:
OmniConfig +0.4 to +1.1 s vs WackyDB +1.7 s to start; +0.6 MB vs +8 MB total memory.

## What changed

| Round | Change | Where |
|---|---|---|
| opt1 | Forge scanned the whole scene 3x per reskin to update live copies (~1,300 scans at 426 items): one scan per batch. Per-item log lines → one summary (details at Debug). | Forge `InstanceSync`, `ForgeRegistry.Reload`; `PrefabPassHook`, `PrefabCloner` |
| opt2 | ServerSync echoes each published value back to the authoritative side, which re-parsed every clone, field and look: overrides/looks echo skipped there (rules echo kept, NR-00 tests that path). O(n²) `List.Contains` in clone registration → sets. Phase timing lines in the log. | `ModletAuthority`, `PrefabCloner`, `ModletOrchestrator` |
| opt3 | The override list for sync is written as JSON by hand (JSON is YAML, so clients and older clients read it unchanged): 415 → 35 ms. Clone registration once per pass instead of once per clone. | `ResolvedOverrideSerializer`, `PrefabCloner.Deferred`, `PrefabPassHook` |
| opt4 | Forge's `SanitizeRequirements` (on Jotunn's items-registered event, inside ObjectDB.Awake) read every recipe's item name per reskin (426 × 850 native calls): grouped once. 420 → 17 ms. | Forge `ForgeRegistry` |
| opt5 | Modlets are read, resolved and serialised on a worker thread during plugin load; the first world start only publishes (12 ms). Used only if no modlet file (or the admin state file) changed since, else the normal load. A headless server skips purely visual looks (still synced to players; a look that also touches stats, crafting, components, collision, scale or name is still built). Clone replace by hash, not a name scan. | `ModletOrchestrator` (preload, fingerprint), `LookBridge`/`ForgeLooks.WithGameplay`, `PrefabCloner` |
| opt6 | Clones and their number/true-false edits, then looks, are built at the main menu (≤ 4 ms of work per frame for clones, one 58 ms frame for 426 looks). Each copy carries a fingerprint (source + every edit aimed at it); world entry only registers copies whose fingerprint still matches and builds the rest as before. Unchanged looks are not rebuilt. Fix: Forge kept the destroyed copy of a clone, so looks on clones were lost from the second world load in a session on. | `MenuPrebuild` (new), `PrefabCloner` (Prebuild/TryReuse/Register), `PrefabPassHook` (fingerprints), `LookBridge` (skip unchanged), Forge `ForgeApi.ExtraPrefab`, `ForgeHost.IsReady`, stale-prefab reload |

Forge changes are made in the Forge repo first (`DrakesAssetForge/Forge/Runtime`) and synced with
`build/scripts/sync_forge.py`; they need a Forge Runtime release note too.

## Design: one-time work without WackyDB's pitfalls

| WackyDB | OmniConfig |
|---|---|
| Disk cache; clear it by hand after big changes or get hash errors | No disk cache. In-memory results checked against a fingerprint of the modlet files (preload) or of each clone's definition (prebuild); anything that changed is rebuilt automatically |
| First launch without a cache can show errors | First launch takes the same path as every launch |
| Modpacks should ship the cache folder for fast joins | Nothing extra to ship |
| Cost lands at game start (+3.4 s at 426 items) | Background thread at plugin load + spread over main-menu frames; world entry only registers |
| Dedicated servers do all of it, visuals included | Headless servers skip visual-only looks; no menu prebuild needed there |

Rules that keep it correct:
- A menu copy is used by one world only (pattern edits applied in the world would compound on reuse); the next world
  uses a new menu prebuild or fresh copies.
- Only number and true/false edits are applied at the menu; edits that name another object wait for the world.
- Only items are prebuilt (the menu has the item list, not the world's other prefabs).
- A world that starts before the prebuild finishes stops it and uses what is done.
- A joined client compares the server's set with its local prebuild per clone: same modpack = reused.

## Tests added (suite `DrakesOmniConfig-Nodes`)

- **NR-00a** the first world start used the background load (`omniconfig.loadPath` = `preload`).
- **NR-43** the JSON sync writer reads back field-for-field like the old YAML writer (`omniconfig.syncRoundTrip` = 0).
- **NR-96** clones came from the menu prebuild (`omniconfig.prebuild`), and after a relog (back to the menu, world
  loaded again) Poison Soup still has its colour, stats, name and recipe.

opt6 run: 48 of 49 green (NR-23, the hammer `destroy` test, fails as before Phase G).

## Still to do

- [x] opt6 timing medians (2026-11-24, above).
- [ ] A few fresh no-mod runs to rule out machine drift: idle frame time in the later rounds came out 1.5–2.8 ms
      *below* the morning's no-mod runs, which is the machine, not the mod.
- [ ] Optional: find the fixed ~56 ms at the main menu (Jotunn's vanilla-prefabs event with OmniConfig's Forge subscribed).
- [ ] **Dedicated server benchmark**: needs Valheim Dedicated Server (Steam app 896660) installed. Time from launch to
      "ready for players", WackyDB vs OmniConfig, small and massive. Verifies the headless look skip for real.
- [ ] Joined-client path in game: prebuild reuse when the server's set matches, partial rebuild when it does not
      (two instances; see Phase D's two-machine test).
- [ ] Forge Runtime release with the three fixes (batched scene searches, recipe-scan fix, destroyed-copy reload) and
      the two additions (`ForgeApi.ExtraPrefab`, `ForgeHost.IsReady`).
- [ ] Optional: spread the 58 ms menu looks frame over several frames (needs per-chunk Forge packs).
- [ ] Optional: non-item clones (pieces, creatures) at the menu via Jotunn's prefab cache.
- [ ] Remaining world-entry cost is mostly Jotunn and the game itself; OmniConfig's own patches total ~40 ms at 426
      items after opt6 (prefab pass 16, start-up 18, registration 6).

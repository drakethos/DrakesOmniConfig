# DrakesOmniConfig

A declarative config/modding engine for Valheim. Instead of installing a separate tiny mod for every tweak
("more carry weight," "tougher bronze swords," "bigger chests"), a server admin writes plain YAML files
("modlets") describing what to change, and this mod applies them — no C#, no recompiling, no new DLL per tweak.

**This is the client/player build.** It only applies modlets; it doesn't help you write them. If you're the
one setting up a server or authoring the YAML, install **[DrakesOmniConfigServer](Server/README.md)**
instead — same engine, plus the authoring tools and the full schema wiki. **Never install both at once** on
the same machine (see "Client vs. Server" below).

## What it does

- Reads every modlet under `BepInEx/config/DrakesOmniConfig/Modlets/` and applies their field overrides to
  the running game — items, pieces, the player, anything with a Unity component the game itself exposes.
- Supports exact values, percentage/relative math (`*1.5`, `+20%`, `-5`), wildcard name matching
  (`"Sword*"` hits every tier of every sword at once), and conflict resolution when two modlets touch the
  same field.
- One override can target either a **prefab template** (baked in once, so every future spawn/craft of that
  item inherits it for free) or the **live player** (re-applied every time, so it survives death/respawn).

For the full schema, every option, and worked examples, see **[DrakesOmniConfigServer's README](Server/README.md)** — it's written as the wiki for both builds, since the YAML format is identical either way.

## Install

Drop it in your `BepInEx/plugins` folder (or install via your mod manager) on **every client** that joins a
server using DrakesOmniConfig modlets. It has no settings you need to touch — the modlets themselves (or,
once server sync ships, the server) drive everything.

## Client vs. Server

| | `DrakesOmniConfig` (this) | `DrakesOmniConfigServer` |
|---|---|---|
| Who installs it | Everyone who plays | Whoever authors/tests modlets |
| Reads local `Modlets/` folder | Yes | Yes |
| Authoring tools (HTML config builder, manifest generation) | No | Yes |
| Install together on one machine | **Never** | **Never** |

They intentionally use **different BepInEx GUIDs** (`com.drakemods.omniconfig` vs.
`com.drakemods.omniconfigserver`) — Valheim's own mod-compatibility check matches on GUID, and these two are
meant to never both be present in the same game process. Once server sync ships (see the roadmap in the
Server build), the two will talk to each other over their own handshake, not GUID matching, which is exactly
why the GUIDs are allowed to differ.

## Status

Local modlets (YAML on disk, applied to your own single-player/hosted game) are implemented and covered by
an automated test suite. Server→client sync (so a client with *no* local modlets still gets the server's
overrides automatically) is not implemented yet — see [ROADMAP.md](ROADMAP.md).

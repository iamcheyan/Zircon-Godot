# Zircon-Godot — Legend of Mir 3

[简体中文](README.zh-CN.md) · [日本語](README.ja-JP.md)

Zircon-Godot is a cross-platform Legend of Mir 3 client and server project. It preserves the original C# server rules and protocol stack while rebuilding the client with Godot/C#.

This repository is a fork of [Suprcode/Zircon](https://github.com/Suprcode/Zircon). The current direction is a Godot client connected to the original-compatible server, with local development first and remote/Web support following.

## Current status

`ServerCore` runs headlessly on Linux and listens on TCP port `7000`. `GodotClient` can connect, log in, select a character, enter the world, load original `.Zl` libraries and `.map` files, and render maps, objects, movement, combat, NPCs, companions, inventory, skills, lighting, weather, and the main UI. Client parity work is ongoing.

| Area | Status |
|---|---|
| Linux server (`ServerLibrary`, `LibraryCore`, `ServerCore`) | Working and maintained |
| Login, character selection, and entering the game | Working |
| Godot map and `.Zl` rendering | Working; parity work continues |
| Movement, combat, NPCs, companions, inventory, and skills | Integrated and being refined |
| Remote server operation | Supported; validate the endpoint first |
| Web client | Prototype and research work in Mir3-Research |

## Architecture

```text
                    TCP 7000
┌─────────────────┐  packets  ┌────────────────────────┐
│ ServerCore      │◄──────────►│ GodotClient            │
│ rules, accounts,│            │ rendering, input, UI,  │
│ maps, combat    │            │ resource loading       │
└─────────────────┘            └────────────────────────┘
```

`ServerLibrary/` owns game rules and world state. `LibraryCore/` provides shared models, packet serialization, and TCP connection code. `GodotClient/` owns rendering, input, UI, and client-side resource loading. `Client/` and `RenderingCore/` remain legacy reference implementations.

## Requirements and runtime data

- .NET 10 SDK
- Godot 4.x .NET (`godot-mono`)
- Original `.Zl` libraries, `.map` files, `System.db`, sounds, and related client data
- Development runtime directory: `/home/tetsuya/mir2ei`

Large runtime assets stay outside Git. On the development machine, `Debug/Client` and `development/Debug/Client` point to `/home/tetsuya/mir2ei`.

## Build

Run builds from the repository root:

```bash
dotnet restore ServerCore/ServerCore.csproj
dotnet build GodotClient/ZirconClient.csproj
```

For a server-only build:

```bash
dotnet build ServerCore/ServerCore.csproj
```

A successful build must report zero errors. Existing warnings in a few Godot scripts are tracked separately.

## Run locally

```bash
cd /home/tetsuya/mir2ei
./login_game.sh
```

Use `all` to clean stale processes, rebuild, restart the server, and launch the client:

```bash
./login_game.sh all
```

The normal local endpoint is `127.0.0.1:7000`. `ServerCore` expects `Server.ini`, `Database/`, and `Map/` in its runtime directory.

When the launcher starts the server itself, closing the Godot client also shuts down that server. A server that was already running before the launcher starts is left untouched.

To launch the client directly:

```bash
godot-mono --path /home/tetsuya/development/Zircon/GodotClient -- \
  --server 127.0.0.1 --port 7000 \
  --user test@test.com --pass test123 --char TestHero --window
```

The test account is for development validation only. Do not place production credentials in the repository, logs, or screenshots.

## Repository layout

```text
ServerLibrary/   Server rules, world state, entities, and gameplay
ServerCore/      Linux headless server host
LibraryCore/     Shared models, MirDB, packets, and TCP connection code
GodotClient/     Cross-platform Godot/C# client
Client/          Legacy Windows client; reference only
RenderingCore/   Legacy rendering components; reference only
LibraryEditor/   Library and resource tooling
BotRunner/       Automated gameplay and testing support
docs/            Audits, handoffs, and codebase documentation
screenshots/     Committed evidence from client runs
```

## Screenshots

![Gameplay: companion and combat](screenshots/gameplay_companion_combat.jpg)

![Gameplay: network debug overlay](screenshots/gameplay_network_debug.jpg)

![Gameplay: windowed combat](screenshots/gameplay_windowed_combat.png)

## Documentation and research

- [`docs/handoffs/`](docs/handoffs/) — client/server handoffs and operating notes
- [`docs/codebase/`](docs/codebase/) — protocol, map, combat, monster, item, and infrastructure notes
- [`docs/notes/`](docs/notes/) — architecture decisions and validation records
- [`docs/REMOTE_SERVER_AND_CLIENT_SETUP.md`](docs/REMOTE_SERVER_AND_CLIENT_SETUP.md) — remote deployment notes
- [`docs/MAGIC_FULL_AUDIT.md`](docs/MAGIC_FULL_AUDIT.md) — magic and effect audit
- [`docs/UPSTREAM_SYNC_POLICY.md`](docs/UPSTREAM_SYNC_POLICY.md) — upstream review and selective-porting policy
- [Mir3-Research](../Mir3-Research) — original-client reverse engineering, resource decoding, map audits, and Web research tools

Changes involving login, game entry, maps, rendering, conversion, or index conventions require behavioral validation, not compilation alone. Keep generated runtime assets outside Git where possible. Commit messages follow the repository's Chinese convention.

## Provenance

Zircon-Godot is a fork and ongoing reimplementation of the upstream project. Original game data and client resources are external runtime dependencies and are not redistributed by this source repository.

---

*The English README is the default entry point. Translations are maintained alongside it.*

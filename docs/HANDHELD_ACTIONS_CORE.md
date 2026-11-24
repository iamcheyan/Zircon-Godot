# Handheld action core

This document describes the Godot independent input/configuration core in `GodotClient/Input/HandheldActions` and its handheld integration. `GameScene.HandheldActions.cs` loads per-character configuration, routes physical semantic keys into the engine, validates each intent against live game state, and uses existing game action paths. `HandheldActionBindings` edits pages and fixed triggers from Journal → System → Operations → Handheld bindings; changes are saved atomically per character. The fbdev backend supplies physical evdev keys and axes. Bindings produce intents only; they are never packets or permission to bypass existing game checks.

## Inputs and behavior

- Two pages each expose exactly six configurable direct buttons: `X`, `Y`, `Up`, `Down`, `Left`, `Right`.
- Six fixed triggers are independent of the current page: `L2tap`, `L2hold`, `R2tap`, `R2hold`, `L1+R1`, and `L2+R2`.
- Direct `X`, `Y`, and D-pad bindings emit once on the physical press. They have no delayed hold action; holding or OS repeat cannot emit another intent.
- A and B always emit semantic `Interact` and `Cancel` intents on release. They do not send packets.
- L1/R1 singles switch between the two pages immediately on release. A recognized L1+R1 chord emits the fixed-slot intent and suppresses both page switches. L2+R2 emits its fixed-slot intent and suppresses both trigger intents.
- Chords require both buttons to be physically down at once. The second press must occur within the inclusive chord window from the first press. If the inclusive window and hold threshold end at the same timestamp, the arriving partner press wins before the hold tick and the chord suppresses both. A key released before its partner is pressed has already committed its single action and cannot be paired later.
- L2/R2 taps emit on release. A hold emits once at threshold; release does not add a tap. A later press cannot retroactively suppress an already emitted hold or tap.
- Taking input ownership for a menu or losing focus cancels pending input. Buttons held at takeover, and buttons pressed while the menu owns input, remain blocked until their release is observed.

All event methods take caller supplied monotonic milliseconds. `Tick` is the only clock advancement and no Godot/global clock is used. The default timing is a 600 ms long threshold and an 80 ms chord window. The supported long threshold range is 100–5000 ms; the chord window is 0 through the long threshold.

## Bindings and persistence

Bindings are `Empty`, `Skill`, `Item`, or a bounded `Menu` action. Skills and items refer to stable string identifiers, never inventory slot indices. Skill modes are limited to `Active` and the existing `Toggle` behavior; passive skills cannot be represented as executable bindings. The menu enum is a fixed allowlist. There is no macro, command-string, GM, or arbitrary action payload.

Schema version 1 stores the two pages, fixed triggers, `LongPressThresholdMs`, and `ChordWindowMs`. `HandheldActionConfiguration.Validate()` checks schema, exact page/button/slot sets, enums, binding payloads, stable IDs, and timing limits. `HandheldActionConfigurationStore.Load()` throws for malformed or invalid data. `Save()` validates before writing, writes a temporary sibling file, then replaces the destination; failures return `SaveResult` with an error and invalid data does not replace the existing file.

The UI-facing snapshot reports page, immutable pressed/pending sets, normalized long-press progress, and last emitted intent. The intent is descriptive only; it is not a network packet or a guarantee that the game action is currently legal.

## Checks

Run the standalone checks without Godot, a server, or game data:

```sh
dotnet run --project Tests/HandheldActionsChecks/HandheldActionsChecks.csproj
```

These checks cover tap/hold boundaries, semantic A/B intents, both fixed chords and suppression, immediate page selection, menu release gating, overlapping versus sequential chord presses, late and exact-window chord edges, configuration validation/persistence, snapshot progress, repeat suppression, and monotonic-time enforcement.

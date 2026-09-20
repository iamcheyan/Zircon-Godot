# Upstream Sync Policy

This document records how Zircon-Godot follows the upstream [Suprcode/Zircon](https://github.com/Suprcode/Zircon) repository.

## Current baseline

The working fork is `iamcheyan/Zircon-Godot`; the upstream repository is `Suprcode/Zircon`.

At the review on 2026-09-20:

- Upstream `master`: `89802f34`, `documentation updates`, 2026-09-13
- Local `master`: `77a0d2eb`
- The branches have diverged substantially from their common ancestor.
- The local branch contains 611 commits beyond the reviewed comparison point, while upstream contains 32 commits not present locally.
- Upstream has no direct changes to `GodotClient/` in the reviewed range.

Because of this divergence, a full `git merge upstream/master` is not the default synchronization strategy.

## Project boundary

Upstream continues to develop the original Windows client, WinForms UI, SharpDX/Silk rendering, editors, and shared server code. Zircon-Godot also has a separate Godot/C# client with different rendering, UI, resource-loading, and world-scaling code.

Changes under the original `Client/`, `RenderingCore/`, and Windows editor projects must not be assumed to apply to `GodotClient/`. A successful legacy-client merge does not mean that the corresponding feature exists in Godot.

## Recent upstream areas worth monitoring

| Upstream commits | Area | Default treatment |
|---|---|---|
| `f8dfba19` | Crafting system | Selective port; requires Godot UI and packet verification |
| `4e11416c`, `04de15b1` | New monsters, AI, and `CLEARMAP` | Review server-side pieces independently |
| `d9a8d66b` | `DragonCharge` and `RisingStrike` horse skills | Selective port if the Godot skill pipeline needs them |
| `db932b01`, `10fa3cd1` | Dynamic lighting and shadows | Do not merge legacy renderer code; compare behavior and port the idea |
| `2c556665` | Loot, floor, and rendering-cache performance | Review server/data-model value; ignore legacy renderer-only code |

Upstream scaling, WinForms, SharpDX, and editor changes are not direct Godot changes and should normally remain upstream-only.

## Evidence from the review

The crafting commit was tested in a temporary worktree and produced conflicts in `Client/Scenes/GameScene.cs`, `LibraryCore/Stat.cs`, and `ServerLibrary/Models/PlayerObject.cs`. It also touched the original client UI extensively. Whole-commit cherry-picking is therefore unsafe even when a feature is useful.

The correct approach is to inspect server, shared-model, packet, and client-UI portions separately.

## Synchronization procedure

For each upstream review:

1. Fetch upstream without merging it into the working branch.
2. Record the upstream head and common ancestor.
3. List commits and changed paths since the common ancestor.
4. Classify changes as **safe to port**, **selective port**, **reference only**, or **do not port**.
5. Use a temporary worktree or disposable branch for cherry-pick tests.
6. Never resolve conflicts by blindly choosing the upstream or local side; reconcile protocol and data-model changes explicitly.
7. Build affected projects and perform behavioral validation. Login, game-entry, map, rendering, packet, and index changes require runtime or independent cross-checks, not compilation alone.
8. Record the selected upstream commit and porting decision in the feature commit or audit note.

Useful read-only commands:

```bash
git fetch https://github.com/Suprcode/Zircon.git master:refs/remotes/upstream/master
git merge-base master upstream/master
git log --oneline master..upstream/master
git diff --stat master...upstream/master
```

## Decision

For the current divergence, **do not merge upstream `master` wholesale**. Follow upstream by selectively porting server-side functionality that has a clear Godot client plan, while using legacy-client changes as behavioral reference material only.

Revisit this policy when upstream introduces changes to `GodotClient/`, or when a complete protocol/data-model migration plan exists.

# TG5040 full-screen journal

Date: 2026-11-23

Device: TinaLinux TG5040, framebuffer 1280x720, logical journal 640x360. Client started through `pocketjs-native-launch zircon`; screenshots used `/tmp/zircon-grabfb` and removed the device-side raw file after pull.

The original deployment used `client-fbdev.xqz24s`.

- `Mir2eiClient` SHA-256: `7f6062dc0d010604cfa3074bc81a376df90db8dbd6972f972a63482a8f745feb`
- `ZirconClient.dll` SHA-256: `dc0f9d5a64c708a75633486d240afe119d612fd7f3f1582fe16e47d60b4af646`

Original deployed framebuffer [capture](tg5040-journal-final-2026-11-23.png).

The first export used for the six-page screenshot set was `client-fbdev.z8180b`; it showed:

- 背包: populated real inventory, item detail, character preview.
- 人物: equipment and character paper doll.
- 技能: live skill icons, level/status detail.
- 任务: explicit empty-state message on this save.
- 社交: explicit no-friends state on this save.
- 系统: live settings page.

The follow-up deployment used `client-fbdev.7yUrIP`, with installed `ZirconClient.dll` SHA-256 `e054d6eedac0413328d496e6af07e2ac34523cc3a55f86da295476b49c092a23`. The three gameplay fixes are in `ae2cb147`. The client built with zero errors.

| Area | Verified on TG5040 | Remaining coverage |
|---|---|---|
| Movement | Separate save at 11/51 weight. Held-stick positions progressed from `(458,397)` to `(458,396)` to `(458,394)`. The built-in movement check separately logged server-confirmed one-tile Walking and two-tile Running. Production save showed 4385/2516 weight. [Fixture endpoint](tg5040-journal-run-qa-2026-11-23.png) | Physical-stick feel |
| Backpack and character | All six backpack and five character categories. Short sword swapped to wooden sword, persisted after relogin and swapped back. Full-bag unequip notice. [Swap](tg5040-journal-equip-worn-2026-11-23.png) · [Restored](tg5040-journal-short-worn-2026-11-23.png) · [Full bag](tg5040-journal-unequip-menu-2026-11-23.png) | Empty-slot unequip, other item actions, titles and mounts |
| Skills | Five categories. Fireball cast received `MagicCooldown` and `ObjectMagic cast=True`. F1 binding and clearing, favorite and removal verified in the UI. [Cast](tg5040-journal-skill-cast-2026-11-23.png) · [Binding](tg5040-journal-fireball-bound-2026-11-23.png) · [Favorite](tg5040-journal-fireball-favorited-2026-11-23.png) | Binding persistence after relogin, additional spell behavior |
| Quest | All, in progress and completed filters rendered their empty states. [All](tg5040-journal-quests-entry-2026-11-23.png) | Tracking and map navigation require a character with a quest |
| Social | Five categories, Friends opened Communication and B restored the journal. [Window](tg5040-journal-social-window-2026-11-23.png) · [Return](tg5040-journal-social-window-back-2026-11-23.png) | Live multiplayer interactions |
| System | Six categories. Display effects toggled off and back on. Return to character selection defaulted to Cancel. [Effects restored](tg5040-journal-effects-restored-2026-11-23.png) · [Confirmation](tg5040-journal-exit-confirmation-2026-11-23.png) | Additional settings actions |
| Controls and services | Start switched HUD in both directions. B returned from the communication window. [Default HUD](tg5040-journal-hud-after-toggle-2026-11-23.png) · [Compact HUD](tg5040-journal-hud-original-2026-11-23.png) | Full input isolation and merchant, storage and trade round trips |

Controller events were replayed through evdev on the device. Long-run native stability remains unverified.

Additional device checks

- [Bracelet slot choices](tg5040-journal-bracelet-slots-2026-11-23.png), [belt binding](tg5040-journal-belt3-verified-2026-11-23.png), and [restored empty belt slot](tg5040-journal-belt3-restored-2026-11-23.png).
- [Discard cancellation](tg5040-journal-discard-cancelled-2026-11-23.png) and [automatic potion settings return](tg5040-journal-autopotion-return-2026-11-23.png).
- [Map return](tg5040-journal-journal-map-back-2026-11-23.png) and [stick focus inside the journal](tg5040-journal-journal-stick-held-2026-11-23.png).
- [Mount request](tg5040-journal-qa-mount-result-2026-11-23.png) left the character unmounted. The [titles category](tg5040-journal-qa-title-action-2026-11-23.png) was empty. The isolated character died before the empty-slot unequip scenario could be exercised.

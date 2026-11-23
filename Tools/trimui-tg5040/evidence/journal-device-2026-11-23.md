# TG5040 full-screen journal

Date: 2026-11-23

Device: TinaLinux TG5040, framebuffer 1280x720, logical journal 640x360. Client started through `pocketjs-native-launch zircon`; screenshots used `/tmp/zircon-grabfb` and removed the device-side raw file after pull.

The final deployed export was `client-fbdev.xqz24s`:

- `Mir2eiClient` SHA-256: `7f6062dc0d010604cfa3074bc81a376df90db8dbd6972f972a63482a8f745feb`
- `ZirconClient.dll` SHA-256: `dc0f9d5a64c708a75633486d240afe119d612fd7f3f1582fe16e47d60b4af646`

The final deployed framebuffer capture is `tg5040-journal-final-2026-11-23.png`.

The first export used for the six-page screenshot set was `client-fbdev.z8180b`; it showed:

- 背包: populated real inventory, item detail, character preview.
- 人物: equipment and character paper doll.
- 技能: live skill icons, level/status detail.
- 任务: explicit empty-state message on this save.
- 社交: explicit no-friends state on this save.
- 系统: live settings page.

The source was subsequently compiled successfully with 0 errors and the existing 12 handheld regression assertions passed. A final export is being deployed after the contextual-service ledger and multi-cell inventory fixes; re-capture is required before treating this evidence as the final package hash.

Limits: this proves rendering and page navigation on the target device. It does not prove every server-backed action, merchant/storage/trade round trip, or long-run native stability. The design prototype's sample records are not used as fake game data.

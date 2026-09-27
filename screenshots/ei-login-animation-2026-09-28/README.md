# EI login animation runtime evidence — 2026-09-28

Tested the `ca6b01ab` latest fetched `ui/legacy-layout-lab` source in an isolated worktree; no changes were made to the user's existing checkout or untracked artifacts.

- Built `GodotClient/ZirconClient.csproj`: succeeded, 0 errors (3 existing warnings).
- Ran GodotClient on the local Xvfb display with `--legacy-ui`; loaded EI assets from `/home/tetsuya/mir2ei/LegacyEI/Data`.
- Runtime log confirmed `ei_Login.ogv` playback, `wemade.ogv` intro playback (4.97s), legacy 640×480 login layout, and successful connection/version handshake to local `127.0.0.1:7000`.
- Captured screenshots at 1024×768. Two login-video captures taken several seconds apart differ in 230,400 pixels, consistent with changing video frames.
- Visual review confirmed the animated video and login controls render; account password is masked.
- Auto-login with the test account was initiated, but this run did not reach a `[Game]` entered-world confirmation; treat game-entry/login completion as unverified.

Files `01`–`05` are raw client screenshots, not edited composites.

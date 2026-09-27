# EI 复古 UI 默认行为变更记录

日期：2026-09-28

代码提交：`a3bb3156`（`默认启用 EI 复古界面`）

## 变更目的

避免直接启动 Godot 客户端时因遗漏 `--legacy-ui` 而落入现代 Zircon UI。EI 复古界面现为默认；现代 Zircon UI 必须显式选择。

## 行为约定

- 不传 UI 模式参数：启用 EI 复古 UI 与旧版 HUD。
- 传入 `--zircon-ui`：切换现代 Zircon UI。
- 传入 `--legacy-ui`：显式启用 EI 复古 UI，并优先于 `--zircon-ui`。
- `LegacyEI/login_game.sh` 的 Legacy 模式继续传递 `--legacy-ui --legacy-hud`；`zircon` 模式现在显式传递 `--zircon-ui`。
- 复古 UI 资源从 `ZIRCON_LEGACY_UI_DATA_PATH` 或 EI 安装根的 `Data` 子目录读取；不会借用 `ZIRCON_UI_DATA_PATH` 或现代 UI 目录作为静默回退。找不到 EI 目录时记录错误并保留预期 EI 路径，避免误载现代素材。

## 涉及文件

- `GodotClient/Scripts/AutoLoginArgs.cs`：复古 UI 默认值及 `--zircon-ui` 退出开关。
- `GodotClient/Controls/MirSkin.cs`：复古 UI 资源根判定和 EI 路径解析。
- `LegacyEI/login_game.sh`：现代 Zircon 启动分支显式追加 `--zircon-ui`。
- `Docs/LEGACY_LOGIN_BOOT_FLOW.md`：入口及默认模式说明。

## 验证

- `dotnet build GodotClient/ZirconClient.csproj --no-incremental`：通过，0 错误、3 个既有警告。
- Godot headless 启动（无 UI 模式参数、连接目标指定为本机未监听端口）日志：`legacyUi=True`、`legacyHud=True`；`MirSkin` 实际从 EI `LegacyEI/Data` 加载 `Interface1c.wil` 和 `GameInter.wil`，登录背景纹理 `tex=True`。
- 同一 smoke test 传入 `--zircon-ui`：日志为 `legacyUi=False`、`legacyHud=False`。
- `bash -n LegacyEI/login_game.sh` 与 `git diff --check`：通过。

Headless smoke test 只验证启动 UI 模式和资源根选择，不代表登录、选角、创建角色或进入游戏流程通过。启动脚本会结束本工作树的 Godot 客户端，并可能在本地启动服务端；进行实机流程验收前须确认进程和目标服务器。

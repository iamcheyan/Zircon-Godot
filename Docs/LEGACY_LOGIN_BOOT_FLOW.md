# Legacy 登录启动与视频资源说明

更新日期：2026-09-27

## 唯一启动入口

仓库现在只保留一个启动脚本：

```text
LegacyEI/login_game.sh
```

这个脚本是仓库推荐的一键入口。**Godot 客户端代码也默认启用 EI 复古 UI**；现代 Zircon UI 需要显式传 `--zircon-ui`。脚本会按选择组装对应参数，并将 EI 素材指向 `LegacyEI/Data`；启动时会结束同一工作树的 Godot 客户端，若目标端口没有服务端还会启动本地服务端，因此不要在用户客户端运行时贸然启动。

```bash
./LegacyEI/login_game.sh              # Legacy 界面，手动登录
./LegacyEI/login_game.sh test         # Legacy 界面，自动登录测试账号
./LegacyEI/login_game.sh zircon       # 现代 Zircon 界面，手动登录
./LegacyEI/login_game.sh test zircon  # 现代界面，自动登录
./LegacyEI/login_game.sh test 2x      # Legacy + 自动登录 + 2 倍缩放
```

## 路径和环境变量

默认 EI 工作目录为 `$HOME/mir2ei/LegacyEI`，也可显式指定：

```bash
export ZIRCON_EI_ROOT=/path/to/mir2ei/LegacyEI
```

兼容 `MIR3_EI_ROOT`；资源路径由启动器设置为：

```text
ZIRCON_UI_DATA_PATH=$ZIRCON_REPO_ROOT/Debug/Client/Data
ZIRCON_LEGACY_UI_DATA_PATH=$ZIRCON_EI_ROOT/Data
```

测试账号不写死在启动命令中，可用以下变量覆盖：

```bash
export ZIRCON_TEST_USER=test@test.com
export ZIRCON_TEST_PASS=test123
export ZIRCON_TEST_CHAR=TestHero
```

## 动画来源与播放链路

动画逻辑在客户端 `GodotClient/Scripts/LoginScene.cs` 和 `SelectScene.cs` 中，但视频文件是运行时资源，不在 Zircon Git 仓库内。

Legacy 登录启动时，`LegacyEI/login_game.sh` 添加 `--legacy-ui --legacy-hud`；`LoginScene._Ready()` 检测到 `AutoLoginArgs.LegacyUi` 后：

1. `PlayLegacyBootLogo()` 从 `MirSkin.UiDataPath/wemade.ogv` 播放约 4.97 秒的 WeMade 开场 Logo，播放结束后移除播放器。
2. `ApplyLegacyEiLoginLayout()` 从同一资源目录播放循环背景 `ei_Login.ogv`。
3. 选角和进入游戏过渡由 `SelectScene.PlayLegacyTransition()` 播放 `CreateChr.ogv` 和 `StartGame.ogv`。

原始 EI 文件是 Intel Indeo 5.0 AVI，Godot 不能直接播放；`Tools/convert_legacy_login_video.sh` 将它们转换为同名 `.ogv`。运行时实际需要的是：

```text
$ZIRCON_EI_ROOT/Data/wemade.ogv
$ZIRCON_EI_ROOT/Data/ei_Login.ogv
$ZIRCON_EI_ROOT/Data/CreateChr.ogv
$ZIRCON_EI_ROOT/Data/StartGame.ogv
```

## 82 机器同步验证

2026-09-27 已通过 SSH 在 82 机器确认四个 `.ogv` 均存在于：

```text
/home/tetsuya/mir2ei/LegacyEI/Data/
```

并与本机文件 SHA-256 一致：

```text
wemade.ogv    fadf30fbe704d5b80c6b35721e1bb7dbc381e6a771b97fcf161dc47fa6f25670
ei_Login.ogv  1865c8226ba56ef5fc3237c908dde5fb1f5b52613f46ad6097fe58e79c78acc1
CreateChr.ogv 480ebcc7ea0b7f26d25531ea9b5dc800b9a95a4c242c82bb1a5fbf84962311ef
StartGame.ogv 503c1184f1879804d36c57b50b224295b685b5439ca31adfef6f3eb6c63d8cdd
```

在 82 上运行前应确认：

```bash
export ZIRCON_EI_ROOT=/home/tetsuya/mir2ei/LegacyEI
./LegacyEI/login_game.sh
```

如果日志出现 `缺少开场 logo 视频` 或 `缺少背景视频`，优先检查 `ZIRCON_EI_ROOT` 和上述四个文件，而不是重新修改动画代码。

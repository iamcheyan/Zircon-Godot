# Zircon-Godot — Legend of Mir 3

[English](README.md) · [日本語](README.ja-JP.md)

Zircon-Godot 是《传奇 3》的跨平台客户端与服务端重实现项目。项目保留原版 C# 服务端规则和协议栈，并使用 Godot/C# 重写客户端。

本仓库是 [Suprcode/Zircon](https://github.com/Suprcode/Zircon) 的 fork。当前重点是让 Godot 客户端连接兼容原版的服务端，先完善本地运行，再推进远程服务端和 Web 方向。

## 当前状态

`ServerCore` 已可在 Linux 上以无头模式运行，默认监听 TCP `7000`。`GodotClient` 已支持连接、登录、选角、进入游戏、读取原版 `.Zl` 图库和 `.map` 地图，并渲染地图、对象、移动、战斗、NPC、伙伴、背包、技能、光照、天气和主要 UI。当前仍在持续进行原版一致性修复。

| 模块 | 状态 |
|---|---|
| `ServerLibrary`、`LibraryCore`、`ServerCore` Linux 运行 | 可用并持续维护 |
| 登录、选角、进入游戏 | 可用 |
| Godot 地图与 `.Zl` 渲染 | 可用，持续对齐 |
| 移动、战斗、NPC、伙伴、背包、技能 | 已接入，持续完善 |
| 远程服务端 | 支持，需先验证服务端 |
| Web 客户端 | Mir3-Research 中进行原型研究 |

## 环境与启动

需要 .NET 10 SDK、Godot 4.x .NET（`godot-mono`）以及原版 `.Zl`、`.map`、`System.db` 和声音资源。开发机运行资源位于 `/home/tetsuya/mir3ei`，大型运行资源不复制进 Git。

在仓库根目录构建：

```bash
dotnet restore ServerCore/ServerCore.csproj
dotnet build GodotClient/ZirconClient.csproj
```

启动本地整套环境：

```bash
cd /home/tetsuya/mir3ei
./login_game.sh
```

使用 `all` 清理旧进程、重建并重启：

```bash
./login_game.sh all
```

默认连接地址是 `127.0.0.1:7000`。开发测试账号只用于验证，不要把生产凭据写入仓库、日志或截图。

## 目录结构

```text
ServerLibrary/   服务端规则、世界状态和玩法
ServerCore/      Linux 无头服务端
LibraryCore/     共享模型、MirDB、协议和 TCP 连接
GodotClient/     Godot/C# 跨平台客户端
Client/          原版 Windows 客户端，仅作参考
RenderingCore/   原版渲染组件，仅作参考
LibraryEditor/   图库和资源工具
BotRunner/       自动化玩法与测试支持
docs/            审计、交接和代码文档
screenshots/     客户端运行截图
```

## 游戏截图

![伙伴与战斗](screenshots/gameplay_companion_combat.jpg)

![网络调试与游戏画面](screenshots/gameplay_network_debug.jpg)

![窗口化战斗](screenshots/gameplay_windowed_combat.png)

## 文档与开发约定

- [`docs/handoffs/`](docs/handoffs/)：客户端和服务端交接资料
- [`docs/codebase/`](docs/codebase/)：协议、地图、战斗、怪物、物品和基础设施文档
- [`docs/notes/`](docs/notes/)：架构决策和验证记录
- [`docs/REMOTE_SERVER_AND_CLIENT_SETUP.md`](docs/REMOTE_SERVER_AND_CLIENT_SETUP.md)：远程部署说明
- [Mir3-Research](../Mir3-Research)：原版客户端逆向、资源解码、地图审计和 Web 研究工具

登录、进游戏、地图、渲染、资源转换或索引约定相关改动，必须做行为验证，不能只看编译结果。提交信息遵循仓库现有中文风格。

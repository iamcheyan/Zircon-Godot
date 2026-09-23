# Zircon 本地游戏启动指南

本文档说明如何在本地（macOS / Linux）启动 Zircon 服务端与客户端。

---

## 方式一：客户端一键自动拉起单机模式（推荐）

Zircon 客户端内置了 **单机模式启动器**（`SinglePlayerLauncher`）：
- 当目标端口无服务端监听时，客户端启动会自动拉起本地 `ServerCore.dll --singleplayer-dev` 服务端；
- 测试账号 `test@test.com` 会自动注入 **满级（255 级）+ 全技能 + 全装备 + 1亿金币**；
- **客户端关闭时，会自动退出它拉起的服务端进程**（生命周期完全绑定，不留残留后台）。

### 启动命令

在仓库根目录（`/Users/tetsuya/Development/Zircon`）执行：

```bash
godot-mono --path GodotClient -- --port 7001 --user test@test.com --pass test123 --char TestHero --window
```

> 💡 **macOS 说明**：macOS 系统控制中心（ControlCenter / AirPlay）默认占用 TCP `7000` 端口，因此本地请使用 `--port 7001`。

---

## 方式二：一键登录脚本（`login_game.sh`）

仓库根目录提供了一键脚本，包含进程清理、自动编译、启动服务与登录：

```bash
# 默认模式：如果服务端已在运行则直接连接；如果未运行则自动拉起
bash login_game.sh

# 全部重启模式：强制重启服务端和客户端（修改了服务端代码后使用）
bash login_game.sh all

# 使用旧版 EI HUD 登录（服务端不重启）
bash login_game.sh legacy

# 重启服务端并使用旧版 EI HUD
bash login_game.sh all legacy
```

---

## 方式三：手动分步启动（双终端独立调试）

如果你需要分别查看服务端和客户端的控制台输出：

### 第一步：编译（若刚修改代码）

```bash
# 编译无头服务端
dotnet build ServerCore/ServerCore.csproj -c Debug

# 编译 Godot 客户端
dotnet build GodotClient/ZirconClient.csproj -c Debug
```

### 第二步：终端 1 启动服务端

```bash
# 进入服务端运行目录（包含 Server.ini、Database、Map 等资源）
cd /Users/tetsuya/Development/Debug/ServerCore

# 启动单机开发服务端（自动满级测试角色）
dotnet ServerCore.dll --singleplayer-dev

# 或启动标准服务端
dotnet ServerCore.dll
```

启动成功后终端会显示：
```text
Map loaded: Lost Paradise [1]
Network Started. Listen: 127.0.0.1:7001
Loading Time: 3 Seconds
```

### 第三步：终端 2 启动客户端

```bash
cd /Users/tetsuya/Development/Zircon

# 自动登录测试账号进游戏（窗口模式）
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7001 --user test@test.com --pass test123 --char TestHero --window

# 或手动输入账号密码登录
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7001 --window
```

---

## 常用命令行启动参数

参数放在 `--` 之后传递给游戏逻辑：

| 参数 | 说明 | 示例 |
|---|---|---|
| `--server <ip>` | 目标游戏服务器地址 | `--server 127.0.0.1` |
| `--port <port>` | 目标服务器端口（macOS 建议 7001） | `--port 7001` |
| `--user <email>` | 自动登录邮箱（提供即触发自动登录） | `--user test@test.com` |
| `--pass <password>` | 自动登录密码 | `--pass test123` |
| `--char <name>` | 进入的角色名（缺省进首个角色） | `--char TestHero` |
| `--window [=WxH]` | 强制窗口模式（可指定分辨率） | `--window` 或 `--window=1600x900` |
| `--lang <lang>` | 强制指定 UI 语言 | `--lang CHINESE` 或 `--lang ENGLISH` |

---

## 本地测试账号

```text
邮箱：test@test.com
密码：test123
角色：TestHero（管理员/永久 GM）
```

---

## 数据还原

单机开发模式（`--singleplayer-dev`）会将满级角色数据保存到 `Debug/ServerCore/Database/Users.db`。如果需要清空或还原初始状态：

```bash
# 停止服务端后，从备份恢复干净数据库
cp /Users/tetsuya/Development/Debug/ServerCore/Database/Users.db.empty-backup-0809-1036 \
   /Users/tetsuya/Development/Debug/ServerCore/Database/Users.db
```

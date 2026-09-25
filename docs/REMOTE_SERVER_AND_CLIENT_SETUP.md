# 82 远程服务端与本机客户端使用手册

本文说明如何在 82 服务器上运行 Zircon 的 ServerCore，以及如何让本机的
Godot 客户端或原 Windows 客户端连接它。

## 1. 当前部署结构

82 的正式源码仓库：

    /home/tetsuya/development/zircon

82 的编译输出和运行目录：

    /home/tetsuya/development/Debug/ServerCore

运行目录中的重要内容：

    ServerCore.dll       无头游戏服务端
    Server.ini           服务端配置
    Database/System.db   游戏系统数据库
    Database/Users.db    账号、角色等用户数据
    Map/                 地图文件
    server.log           服务端标准输出
    server-error.log     服务端错误输出

服务由 systemd 管理，服务名为 zircon-server.service。

当前网络配置：

    服务器地址：192.168.3.82
    游戏端口：7000
    用户统计端口：3000

客户端只需要连接 7000。3000 是服务端内部的用户数量监听端口。

## 2. 查看服务器状态

登录 82：

    ssh 82

查看服务状态：

    sudo systemctl status zircon-server
    sudo systemctl is-active zircon-server
    sudo systemctl is-enabled zircon-server

正常结果应该是 active 和 enabled。

查看端口：

    ss -ltnp | grep -E ':(7000|3000)\b'

查看最近日志：

    tail -50 /home/tetsuya/development/Debug/ServerCore/server.log

实时查看日志：

    tail -f /home/tetsuya/development/Debug/ServerCore/server.log

也可以通过 systemd 查看：

    sudo journalctl -u zircon-server -f

正常启动时，日志末尾应包含：

    Network Started. Listen: 0.0.0.0:7000
    Loading Time: 3 Seconds

## 3. 启动、停止和重启服务端

    sudo systemctl start zircon-server
    sudo systemctl stop zircon-server
    sudo systemctl restart zircon-server

修改 systemd 配置后：

    sudo systemctl daemon-reload
    sudo systemctl restart zircon-server

服务配置文件：

    /etc/systemd/system/zircon-server.service

当前服务具有以下特性：

- 82 开机自动启动；
- 服务异常退出后 5 秒自动重启；
- 工作目录固定为 /home/tetsuya/development/Debug/ServerCore；
- 运行用户为 tetsuya；
- 日志写入 server.log 和 server-error.log。

自动重启只能处理程序退出，不能解决服务器断电、网络中断、磁盘损坏或数据库损坏。

## 4. Server.ini 配置

配置文件：

    /home/tetsuya/development/Debug/ServerCore/Server.ini

当前有效内容：

    [Network]
    IPAddress=0.0.0.0
    Port=7000

    [System]
    CheckVersion=False
    MapPath=Map/

    [Control]
    AllowStartGame=True
    RelogDelay=00:00:02

配置说明：

| 配置 | 作用 |
|---|---|
| IPAddress=0.0.0.0 | 允许 82 的局域网地址接收客户端连接 |
| Port=7000 | 游戏 TCP 端口 |
| CheckVersion=False | 开发阶段不强制客户端 DLL 哈希一致 |
| MapPath=Map/ | 相对于运行目录的地图目录 |
| AllowStartGame=True | 允许角色进入游戏 |
| RelogDelay | 重新登录保护时间 |

不要把 MapPath 写成 Debug/ServerCore/Map/。因为服务端工作目录已经是
Debug/ServerCore，正确写法是 Map/。

修改后执行：

    sudo systemctl restart zircon-server

## 5. 服务端资源要求

服务端至少需要：

    Database/System.db
    Database/Users.db
    Map/*.map

Users.db 是运行中的用户数据，包含账号和角色。不要在服务端运行时用本机旧版
Users.db 覆盖它。

如果地图或数据库不完整，服务端可能仍然监听端口，但角色进入地图、加载 NPC
或读取系统数据时会出现错误。应检查日志中的 Map loaded 和 Network Started。

## 6. 82 上更新正式程序

正式源码更新后，在 82 执行：

    cd /home/tetsuya/development/zircon
    git pull --ff-only
    dotnet restore ServerCore/ServerCore.csproj
    dotnet build ServerCore/ServerCore.csproj -c Debug
    sudo systemctl restart zircon-server

当前 ServerCore 项目的 Debug 输出目录是：

    /home/tetsuya/development/Debug/ServerCore

更新程序时通常不需要重新复制 Database/ 和 Map/。特别是不要随意覆盖
Database/Users.db。

## 7. 本机 Godot 客户端配置

### 7.1 使用图形界面配置

启动 Godot .NET 客户端后，打开客户端设置中的网络设置，填写：

    使用网络配置：开启
    服务器地址：192.168.3.82
    服务器端口：7000

Godot 客户端只有在“使用网络配置”开启时，才会使用填写的地址；关闭时会强制
连接 127.0.0.1:7000。

设置保存后重新连接或重启客户端。

### 7.2 Godot 配置文件

Godot 客户端使用 Godot 的 user://Zircon.ini，而不是项目目录里的普通文件。
配置界面保存后会写入：

    [Network]
    UseNetworkConfig=true
    IPAddress=192.168.3.82
    Port=7000

如果客户端一直连接 127.0.0.1，优先检查 UseNetworkConfig 是否为 true。

### 7.3 启动命令

构建客户端（必须在仓库根目录执行）：

    cd /home/tetsuya/development/Zircon && dotnet build GodotClient/ZirconClient.csproj

以测试账号启动（自动登录 test@test.com / test123，角色 TestHero）：

    godot-mono --path /home/tetsuya/development/Zircon/GodotClient -- --user test@test.com --pass test123 --char TestHero --window

或者（不带测试账号，手动登录）：

    godot-mono --path GodotClient/

或者：

    ~/.local/bin/godot-mono --path GodotClient/

## 8. 原 Windows 客户端配置

原 Windows 客户端使用运行目录中的 Zircon.ini。在 [Network] 中设置：

    [Network]
    UseNetworkConfig=True
    IPAddress=192.168.3.82
    Port=7000

关闭 UseNetworkConfig 时，原客户端会回退到默认地址 127.0.0.1:7000。
也可以从原客户端的网络设置界面修改 IP 和端口。

## 9. 连接故障排查

本机测试游戏端口：

    ncat -vz 192.168.3.82 7000

如果显示 Connected，说明网络和端口正常，继续检查客户端配置。

如果连接失败，在 82 执行：

    sudo systemctl is-active zircon-server
    ss -ltnp | grep ':7000'

能登录但无法进入游戏时，检查：

- Server.ini 中 AllowStartGame=True；
- Map/ 中是否存在对应地图；
- Database/System.db 是否存在且未被截断；
- Database/Users.db 是否可读写；
- 客户端和服务端协议版本是否匹配。

修改代码后没有变化时，确认重新编译并重启：

    cd /home/tetsuya/development/zircon
    dotnet build ServerCore/ServerCore.csproj -c Debug
    sudo systemctl restart zircon-server

不要只修改源码后直接重启，因为 systemd 运行的是
/home/tetsuya/development/Debug/ServerCore/ServerCore.dll。

## 10. 研究工具与正式服务端的区别

82 上还运行着地图查看器、WIL/UI 查看器和逆向分析任务。这些属于
Mir3-Research，不是游戏服务端：

    8765  WIL/UI 研究工具
    8899  地图查看器
    7000  正式游戏服务端

重启 zircon-server 不会重启研究工具；停止研究工具也不会停止正式游戏服务端。

## 11. 推荐的日常操作顺序

    ssh 82 'sudo systemctl is-active zircon-server'
    ncat -vz 192.168.3.82 7000
    godot-mono --path GodotClient/

启动客户端前，在网络设置中确认：

    使用网络配置：开启
    地址：192.168.3.82
    端口：7000

如果服务端代码刚更新，则先在 82 编译和重启，再启动本机客户端。

## 12. AI Bot 的归属和运行位置

当前 AI Bot 位于正式源码仓库的：

    BotRunner/

它是独立的 .NET 控制台程序，不属于 Godot 的图形客户端，也不是
ServerCore 内部自动启动的模块。

BotRunner 的工作方式是：

    BotRunner
        ├─ 建立多个 TCP 客户端连接
        ├─ 使用游戏协议登录服务器
        ├─ 被服务器视为普通玩家
        └─ 自己执行移动、攻击、练级、聊天、交易等行为

因此它的准确归类是：

- 部署形态：独立后台进程；
- 网络身份：多个模拟客户端；
- 游戏规则：仍由 ServerCore 决定；
- AI 行为：由 BotRunner 自己决定；
- 推荐运行位置：82 服务器；
- 启动顺序：先 ServerCore，后 BotRunner。

不要把 BotRunner 的代码合并到 ServerCore 的启动流程中，也不要在 Godot
客户端启动时顺便启动 BotRunner。这样拆开后，服务端、真人客户端和机器人
可以分别重启，互不影响。

## 13. 为什么 BotRunner 推荐放在 82

BotRunner 需要持续在线，但不需要图形界面。因此放在 82 比放在本机更合适：

- 本机关机后，机器人仍然在线；
- BotRunner 和 ServerCore 在同一台机器，网络延迟低；
- 不需要启动 Godot 或 Windows 客户端；
- 可以用 systemd 自动重启；
- 本机客户端可以随时上线观察机器人。

本机也可以运行 BotRunner 进行调试，但本机关闭后所有机器人会断线。

## 14. 在 82 上编译和手动启动 BotRunner

首先更新并编译：

    ssh 82
    cd /home/tetsuya/development/zircon
    dotnet restore BotRunner/BotRunner.csproj
    dotnet build BotRunner/BotRunner.csproj -c Debug

BotRunner 需要读取 System.db 和地图文件。82 上运行时建议使用绝对路径，
不要直接使用 BotRunner.json 里的本机相对路径。

可以在 82 的正式源码仓库中创建一个本地配置文件，例如：

    BotRunner.82.json

配置示例：

    {
      "Host": "127.0.0.1",
      "Port": 7000,
      "TickMilliseconds": 250,
      "MaxBots": 20,
      "AccountPrefix": "bot",
      "Password": "bot123456",
      "AutoCreateAccount": false,
      "EnableBotPvP": true,
      "DatabasePath": "/home/tetsuya/development/Debug/ServerCore/Database",
      "MapPath": "/home/tetsuya/development/Debug/ServerCore/Map",
      "ClientHashPath": ""
    }

因为 BotRunner 和 ServerCore 都在 82，Host 使用 127.0.0.1 即可。若从本机
运行 BotRunner，Host 才应改为 192.168.3.82。

启动 20 个机器人：

    cd /home/tetsuya/development/zircon
    dotnet run --project BotRunner/BotRunner.csproj -- \
      BotRunner.82.json 20

最后的数字会覆盖配置中的 MaxBots。例如只启动 3 个：

    dotnet run --project BotRunner/BotRunner.csproj -- \
      BotRunner.82.json 3

也可以直接运行编译输出，但具体输出目录要以构建结果为准：

    dotnet BotRunner/bin/Debug/net10.0/BotRunner.dll \
      BotRunner.82.json 20

正常启动时会看到类似输出：

    Zircon BotRunner: 20 bots -> 127.0.0.1:7000
    [Bot01] online ...

生产/长期运行时必须使用已经由 `BotProvisioner` 配置过的账号，并关闭自动建号：

    "AccountPrefix": "bot",
    "AutoCreateAccount": false

这样 BotRunner 使用的是 `bot01@bot.local`～`bot20@bot.local`，对应角色
`Bot01`～`Bot20`。这些角色已经按职业、性别、等级配置了装备、技能、金币、药品，
不会因为服务重启而生成新的 1 级空白战士。

如果数据库中尚未有这批角色，先停止 BotRunner 和 ServerCore，备份
`Debug/ServerCore/Database/Users.db`，再运行：

    dotnet run --project Tools/BotProvisioner -- \
      /home/tetsuya/development/Debug/ServerCore/Database \
      --prefix bot --count 20 --reference TestHero

工具是幂等的：已有角色会保留，并补齐缺失的职业装备、技能、货币和消耗品。
不要在服务端运行时直接改 `Users.db`。

首次测试/临时环境才允许自动建号，例如：

    "AccountPrefix": "bot82",
    "AutoCreateAccount": true

这种模式只会创建新手角色，不会自动附加装备和技能；它不适合作为正式机器人配置。

按 Ctrl+C 会停止所有机器人。

## 15. BotRunner 配置重点

| 配置 | 作用 |
|---|---|
| Host | 服务端地址；82 上运行时用 127.0.0.1 |
| Port | 服务端游戏端口，当前为 7000 |
| MaxBots | 启动的机器人数量，程序限制为 1 到 20 |
| AccountPrefix | 账号名前缀；正式配置为 `bot`，使用 `bot01`～`bot20` |
| Password | 机器人账号统一使用的密码 |
| DatabasePath | BotRunner 读取 System.db 的目录 |
| MapPath | BotRunner 读取地图文件的目录 |
| EnableBotPvP | 是否允许机器人之间进行 PvP |
| ClientHashPath | 开启服务端版本校验时才需要填写 |
| AutoCreateAccount | 正式配置必须为 `false`；开启时新账号只会得到空白新手角色 |

BotRunner 读取 DatabasePath 主要是为了得到地图、怪物、物品和技能等系统
数据；它不直接操作 Users.db。账号和角色仍由 ServerCore 负责创建和保存。

首次运行时，服务端需要允许注册和创建角色：

    AllowNewAccount=True
    AllowNewCharacter=True

当前版本的 BotRunner 已经内置自动注册账号和自动创建角色逻辑，不需要手工
逐个在客户端注册。机器人使用的账号和角色会写入 82 的 Users.db。

当前服务端默认允许注册。机器人账号注册成功后会保存到服务端的 Users.db，
后续启动会继续使用这些账号。

## 16. 用 systemd 长期运行 BotRunner

当前 82 已经安装并启用 zircon-bots.service。它通过
/etc/systemd/system/zircon-server.service.wants/ 下的 systemd Wants 关系，
绑定到 zircon-server.service。

当前实际行为：

- 启动 zircon-server 时自动启动 zircon-bots；
- 停止 zircon-server 时先停止 zircon-bots，再停止 zircon-server；
- ServerCore 重启时 BotRunner 会重新启动；
- BotRunner 启动前会等待 7000 端口真正进入 LISTEN，避免启动竞态；
- BotRunner 运行异常时会自动重启。

因此日常不需要单独启动 BotRunner：

    sudo systemctl start zircon-server
    sudo systemctl stop zircon-server
    sudo systemctl restart zircon-server

查看联动状态：

    sudo systemctl is-active zircon-server
    sudo systemctl is-active zircon-bots

两者都显示 active 才表示正式服务端和机器人都已经运行。

如果只想临时停止机器人而不停止服务端，可以执行：

    sudo systemctl stop zircon-bots

但下一次服务端重启或启动时，BotRunner 会按联动关系再次启动。

确认手动启动正常后，才建议把 BotRunner 托管到 systemd。服务文件示例：

    [Unit]
    Description=Zircon AI BotRunner
    After=zircon-server.service
    Requires=zircon-server.service

    [Service]
    Type=simple
    User=tetsuya
    WorkingDirectory=/home/tetsuya/development/zircon
    ExecStart=/usr/bin/dotnet /home/tetsuya/development/zircon/BotRunner/bin/Debug/net10.0/BotRunner.dll /home/tetsuya/development/zircon/BotRunner.82.json 20
    Restart=always
    RestartSec=10

    [Install]
    WantedBy=multi-user.target

将它保存为：

    /etc/systemd/system/zircon-bots.service

然后执行：

    sudo systemctl daemon-reload
    sudo systemctl enable --now zircon-bots
    sudo systemctl status zircon-bots

查看机器人日志：

    sudo journalctl -u zircon-bots -f

停止机器人但保留服务端：

    sudo systemctl stop zircon-bots

重启机器人：

    sudo systemctl restart zircon-bots

如果只是临时测试，优先使用 tmux 或前台运行；确认账号、地图和行为都正常
后再启用 systemd，避免机器人不断重连并制造大量日志。

## 17. 三类进程的关系

    82:
      zircon-server.service   正式游戏服务端，监听 7000
      zircon-bots.service     可选 AI 机器人，连接 127.0.0.1:7000

    本机:
      GodotClient             你操作的图形客户端，连接 192.168.3.82:7000

服务端必须先启动。BotRunner 和本机客户端都可以随后启动；它们都是服务端
的 TCP 客户端，但 BotRunner 没有图形界面。

## 18. 本机客户端调试 82 上当前工作树

如果代码直接在 82 的 `/home/tetsuya/development/zircon` 工作树开发，而希望用本机的
Godot 窗口和本机 EI 素材登录这份服务端，可在本机 Zircon 仓库根目录运行：

```bash
cd /home/tetsuya/development/Zircon
bash login_game.sh remote 192.168.3.82 legacy
```

此命令按以下顺序工作：

1. 通过 SSH（默认别名 `debian`）读取 82 工作树构建目录中的 `Server.ini` 端口；
   若 SSH 别名不同，可设置 `ZIRCON_REMOTE_SSH_TARGET=82` 等已配置的别名。
2. 本机只构建 Godot 客户端；服务端使用 82 当前 checkout 的源码，在
   `/home/tetsuya/development/zircon/Debug/ServerCore` 输出目录编译。
3. 仅停止工作目录正好是上述 `Debug/ServerCore` 的 `dotnet ServerCore.dll` 测试进程，
   然后从同一目录启动新服务端。启动日志位于 82 的 `/tmp/servercore_login_remote.log`。
4. 建立本机 loopback SSH 转发，让 Godot 客户端连接 `127.0.0.1:<本地转发端口>`，
   流量通过 SSH 到达 82 的 `127.0.0.1:<Server.ini Port>`。因此无需把远程游戏端口
   改绑到外部网卡或开放防火墙端口。
5. Godot 客户端退出后自动关闭 SSH 转发；远程测试服务端继续运行，下一次运行此命令时会重建并重启。

`remote` 模式不执行 Git pull、复制源码、清理或覆盖 82 的工作树；远程未提交源码也会参与构建。
它不操作 `/home/tetsuya/development/Debug/ServerCore` 下由 `zircon-server.service` 管理的正式服务端。
如果 82 的手动测试服务端是从其它工作目录启动的，脚本不会结束它；新进程可能因端口占用而无法启动，
需先确认并停止目标测试实例。运行账号需要 SSH 免交互登录、远程 .NET SDK、`nc`，并对服务端
构建输出目录有写权限；本机也需要 `ssh` 和 `nc`。

不需要 legacy HUD 时去掉末尾的 `legacy`：

```bash
bash login_game.sh remote 192.168.3.82
```

### 18.1 macOS 本机客户端（2026-09-26 实测通过）

macOS 的目录结构与 82 不同，且不能直接照抄 82 的环境变量（原因见下），需要用包装器
显式设置。已验证可用的入口：

```bash
/Users/tetsuya/mir2ei/LegacyEI/login_game.sh remote 192.168.3.82 legacy
```

#### 平台路径对照

| 用途 | 82（Debian） | macOS |
|---|---|---|
| 仓库工作树 | `/home/tetsuya/development/zircon` | `/Users/tetsuya/Development/Zircon` |
| 现代客户端资源（`.Zl`） | `<repo>/Debug/Client/Data` | `<repo>/Debug/Client/Data`（软链到 `/Users/tetsuya/mir2ei`） |
| 旧版 EI 素材（`.wil`/`.wix`） | `/home/tetsuya/mir2ei/Data` | `/Users/tetsuya/mir2ei/LegacyEI/Data` |
| EI 原版客户端（含 exe/dll） | `/home/tetsuya/mir2ei` | 未复制，只取 `Data` |
| 启动包装器 | `/home/tetsuya/mir2ei/login_game.sh` | `/Users/tetsuya/mir2ei/LegacyEI/login_game.sh` |

#### 环境变量契约

| 变量 | 作用 | 是否必须 |
|---|---|---|
| `ZIRCON_UI_DATA_PATH` | 世界/角色/物品/地图图库根（现代 `.Zl`）。**变量名含 UI，实际决定世界数据路径。** | 必须显式设置 |
| `ZIRCON_LEGACY_UI_DATA_PATH` | 旧版界面图库根（EI `.wil`/`.wix`），仅 `--legacy-hud` 时生效 | 用 legacy HUD 时必须 |
| `MIR3_EI_ROOT` | 只作为上面两者的后备默认（`$MIR3_EI_ROOT/Data`） | 可选 |

`MirSkin.ResolveDataPath()` 的解析顺序：

1. `$ZIRCON_UI_DATA_PATH` —— 存在即用
2. `$MIR3_EI_ROOT/Data` —— 存在即用
3. 硬编码 `/home/tetsuya/mir2ei/Data` —— **存在即用（陷阱）**
4. `res://../Debug/Client/Data`

**第 3 条是坑。** 82 上 `/home/tetsuya/mir2ei/Data` 存在，但里面只有 EI 的 `.wil`/`.wix`，
没有任何 `.Zl`；而地形图库只走 `.Zl`（`MapView` → `LibraryCache`，没有 WIL 回退）。
一旦解析落到第 3 条，地形会整片消失。所以 `ZIRCON_UI_DATA_PATH` 必须显式指向现代 `.Zl`
目录，**绝不能指向 EI 的 WIL 目录**。

实测对照（同一张地图 Sabuk Keep、同一坐标 `(203,112)`）：

| 机器 | 环境变量 | `[MapView] 首帧绘制` | `[MapView] 贴图诊断` |
|---|---|---|---|
| macOS | 显式设 `ZIRCON_UI_DATA_PATH` | 480 格 | `missingLibraries=0, missingTextures=0` |
| 82 | 仅 `MIR3_EI_ROOT`+`ZIRCON_UI_DATA_PATH` → EI 目录 | **0 格** | `missingLibraries=2076` |

#### 自检方法

客户端日志中这两行即可判断配置是否正确：

```
[DB] 加载 System.db 从: <repo>/Debug/Client/Data/
[MapView] 贴图诊断: missingLibraries=0, missingTextures=0
```

`missingLibraries` 不为 0，说明 `ZIRCON_UI_DATA_PATH` 落到了没有 `.Zl` 的目录。
另外 `[MirSkin] legacy UI WIL source: GameInter -> <EI 根>/Data/GameInter.wil (1103 frames)`
出现即表示旧版 WIL 素材路由正确。

#### 前置条件：本机客户端构建需要 82 的未提交改动

`ui/legacy-layout-lab` 的**已提交状态编译不过**：`GameScene.cs` 调用的
`NPCTextControl.ButtonAreas` 只存在于 82 工作树**未提交**的 `NPCTextControl.cs` 中，
构建会报 `CS1061: 'NPCTextControl' に 'ButtonAreas' の定義が含まれておらず`。
因此本机客户端必须先同步 82 的未提交改动：

```bash
cd <repo>
git checkout -- . && ssh debian 'cd /home/tetsuya/development/zircon && git diff' | git apply -
```

> ⚠️ `git checkout -- .` 会丢弃本机该 worktree 的未提交改动，执行前确认没有自己的内容。
> 82 改动后重新同步时重复这条命令即可。

#### bash 陷阱：`$VAR` 紧跟全角标点

`login_game.sh` 原先有三处把变量紧贴全角标点：`$PORT，`、`$REMAIN，`、`$PORT）`。
在 UTF-8 locale 下 bash 会把全角标点的首字节（`0xEF`）算进变量名，`set -u` 于是报
`PORT<0xEF>: 未割り当ての変数です` / `unbound variable`，脚本在打印模式行后立即中断。
已改为 `${PORT}` / `${REMAIN}`。

复现范围：bash 5.3 在默认 locale 即复现；bash 3.2 仅在 UTF-8 locale 复现。
**写含中文的 shell 脚本时，全角标点不要紧贴变量名。**


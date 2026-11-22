# TrimUI Smart Pro（TG5040）运行包

把 Zircon 服务端和客户端打成一个自带资源的包，放在 SD 卡上，并在掌机启动器里
加一张封面卡；点开即启动本地 ServerCore + Godot 客户端并自动登录。

桌面端不受影响：所有掌机逻辑都在本目录、`System/compat/bin/pocketjs-native-launch`
和 `trimui/app/MainUI` 里；客户端改动全部走显式开关（见下文“共用代码改动”）。

## 目录与文件

```text
Tools/trimui-tg5040/
├── launch.sh                 掌机入口（服务端 + 客户端，自动登录）
├── deploy.sh                 部署到掌机（备份、推送、重启）
├── cover/zircon.png          启动器封面（256×256，运行时会缩到 128×128）
├── device/
│   ├── pocketjs-native-launch 运行时 LAUNCH 分支的执行脚本（新增 zircon 路由）
│   └── MainUI                 掌机 supervisor（新增 @zircon 交接分支）
└── launcher/repack-launcher.ts 给现有启动器包加一张 Zircon 卡（Bun）
```

包内布局（卡上 `/mnt/SDCARD/Apps/Zircon`）：

```text
Debug/Client/{Data,Map,Sound,LegacyEI}   ← ~/mir2ei 运行资源
Debug/ServerCore/                        ← linux-arm64 自包含 ServerCore + Server.ini + Database/System.db
ClientData/                              ← 仓库 ClientData（帧表/魔法/声音 JSON）
GodotClient/                             ← Godot 4.6.3 Linux ARM64 导出
launch.sh
```

## 启动链路（与内置 GBA 卡同一套机制）

```text
PocketShell 卡片（Zircon，coverPath=/storage/Apps/zircon.png）
  └─ 运行时 LAUNCH zircon
       └─ System/compat/bin/pocketjs-native-launch zircon
            ├─ 写 System/pocketjs/active = @zircon
            └─ kill pocket-linux-kms（交出屏幕）
  └─ trimui/app/MainUI 读到 @zircon
       └─ Apps/Zircon/launch.sh
            ├─ ServerCore（127.0.0.1:7000，--singleplayer-dev）
            └─ Mir2eiClient --server 127.0.0.1 --auto-login --user test@test.com …
退出客户端 → launch.sh 停服务端 → MainUI 重新拉起启动器
```

## 掌机操作（TrimUI Smart Pro / TG5040）

客户端启动时由 `launch.sh` 设置 `ZIRCON_HANDHELD=1`。掌机 HUD 中，A/B 不再同时产生
按键和鼠标事件；默认 HUD 与传统对话框保留右摇杆移指针、A 单独点击、B 返回。
Start 在两套 HUD 之间切换：默认 HUD 保留原有布局，掌机 HUD 隐藏底部大面板、
技能条、腰带和聊天栏，保留小地图与紧凑 HP/MP/Focus/目标状态栏。

下表是掌机 HUD 中的操作；传统对话框打开时，战斗和快选按键不会透传。

| 实体按键 | 游戏中动作 |
|---|---|
| 左摇杆 | 八方向移动；短推走，持续推动后跑 |
| 右摇杆 | 选择附近 NPC/怪物/掉落物目标；打开选择器时移动高亮 |
| 十字键上 | 打开技能选择器 |
| 十字键下 | 打开腰带选择器 |
| 十字键左/右 | 循环选择附近目标 |
| A | 确认/交互；确认当前目标操作 |
| B | 返回/取消并清除目标焦点 |
| X | 拾取当前附近掉落物 |
| Y | 对当前目标普通攻击 |
| L1/R1 | 选择器中上一个/下一个；游戏中切换技能栏组 |
| L2/R2 | 直接使用腰带第 1/第 2 格 |
| Start | 默认 HUD / 掌机 HUD 切换 |
| Select | 短按切换大地图，长按切换地面掉落物名称 |
| Mode | 打开设置 |
| L3 | 自动跑步开关 |
| R3 | 锁定/解除当前目标 |

fbdev 内部将 Select/L3/R3/Y 分别转成 `F15/F13/F14/F16`，避免与桌面
`Alt/Shift/Ctrl/Tab` 语义冲突；这些内部键只在 `AutoLoginArgs.Handheld` 下消费。

技能选择器和腰带选择器均为中央面板：右摇杆、方向键或 L1/R1 逐项选择，A 确认，B 关闭。
技能选择器直接使用已学技能，不要求绑定桌面 F1–F12；被动技能仅供查看。腰带面板显示
服务端的全部十个链接，不受默认 HUD 六格布局限制，并复用现有物品使用及冷却校验。
全屏 Menu 是另一个功能，见 [设计与网页原型](design/zircon-handheld-menu-spec.md)，尚未接入游戏。

桌面端默认行为不变；上述按键和 HUD 分支由 `AutoLoginArgs.Handheld` /
`ZIRCON_HANDHELD` 门控。实现位于 `GodotClient/Scripts/GameScene.Handheld.cs`
和 `GodotClient/Controls/HandheldQuickSelect.cs`。

账号与角色：首次运行会在卡上的 `Debug/ServerCore/Database/Users.db` 里自动创建
`test@test.com`，并按 `--char TestHero` 自动建角（战士男），之后直接进游戏。

## 构建

```bash
# 1. 服务端（linux-arm64 自包含）
MIR3_EI_ROOT=~/mir2ei nix shell nixpkgs#dotnet-sdk_10 -c \
  dotnet publish ServerCore/ServerCore.csproj -c Release -r linux-arm64 --self-contained true \
  -o .artifacts/trimui-tg5040/server -p:InvariantGlobalization=true

# 2. 客户端（Godot arm64 导出；需要 linux_release.arm64 模板）
#    导出模板放在 .artifacts/godot-data/godot/export_templates/4.6.3.stable.mono/
DOTNET_ROOT=<dotnet10> XDG_DATA_HOME=$PWD/.artifacts/godot-data MIR3_EI_ROOT=~/mir2ei \
  godot-mono --headless --path GodotClient --export-release Linux \
  .artifacts/trimui-tg5040/client/Mir2eiClient

# 3. 启动器卡（给卡上现有 boot.pocket 加一张卡，其它字节保持不变）
bun Tools/trimui-tg5040/launcher/repack-launcher.ts \
  --source .artifacts/trimui-tg5040/device-boot-current.pocket \
  --out .artifacts/trimui-tg5040/boot-zircon.pocket

# 4. 组装 bundle（Debug/ ClientData/ GodotClient/ launch.sh）后部署
Tools/trimui-tg5040/deploy.sh
```

`repack-launcher.ts` 只替换包内 JS 段：identity / plan / pak / profile hash 全部
逐字节校验不变，因此设备运行时仍按 `trimui-tsp-fbdev`（640×360 逻辑、1280×720
物理）准入；产物可用
`bun ~/development/pjs-app-build/tools/pocket-pack.ts verify <pkg>` 复核。

## 部署与回滚

`deploy.sh` 接收已组装的包，不负责构建。它会备份 `boot.pocket` 与 `trimui/app/MainUI`，
但不会自动保全 `Users.db`。覆盖程序前应退出游戏并单独备份 `Users.db` 和 `System.db`，
不要把干净包中的数据库推到已有存档上。使用 `ADB_SERIAL` 明确指定设备，并在同一设备锁内操作。

`godot-fbdev/rebuild-client.sh` 只构建和导出，不连接或修改掌机。每次使用新目录，
检查嵌入的 PCK 和配套 .NET 程序集后才报告成功。已有经过验证的 ARM64 fbdev 模板时，
可用 `GODOT_FBDEV_TEMPLATE=/path/to/template` 跳过引擎重建。部署前核对客户端、服务端
和存档备份；supervisor 脚本改动需要重启掌机才会生效。

## 验证记录

- 本机 Godot Mono/X11 接隔离服务端和存档副本：完成 TestHero 登录、地图绘制；测试账号补齐到 61 个技能、431 件物品。
- 显式开启掌机模式：键盘模拟 F10/方向键验证 HUD 双向切换、技能/腰带面板；确认释放未绑定的火球术并收到服务端 `ObjectMagic`。
- fbdev 输入源文件已用 Zig 编译为 AArch64 目标文件；未重新导出或部署整包，实体摇杆、扳机、对话框指针与新包上屏仍待实机验证。

### TG5040 启动恢复（2d7ff081）

卡上原 `Mir2eiClient` 连无画面启动都在文本服务初始化期间段错误；其末尾没有嵌入 PCK 的 `GDPC` 标记。用卡上另一个已能启动的客户端所对应的 fbdev 模板，重新导出 `2d7ff081` 的 ARM64 客户端与配套 `data_ZirconClient_linuxbsd_arm64`，并同步更新服务端程序。通过原生启动入口两次进入地图；原有数据库未被覆盖，部署前备份位于卡上 `apps/Zircon/backups/tg5040-pre-2d7ff081/`。

[实机进图截图（1280×720，读取 fbdev 当前显示页）](evidence/tg5040-world.png)。此图只证明默认 HUD 和地图已上屏，不证明掌机快选或新版 fbdev 输入实现已经验收；该轮导出复用了已有模板。

### 旧版设备实测（2026-11-22）

通过 supervisor 的真实入口 `pocketjs-native-launch zircon` 触发，日志见
`.artifacts/trimui-tg5040/evidence/`：

```text
Supervisor: native app requested: zircon
Starting Zircon at …
[zircon] server ready on 127.0.0.1:7000
[zircon] starting client /mnt/SDCARD/Apps/Zircon/GodotClient/Mir2eiClient
[Login] 账号不存在，自动创建单机账号: test@test.com
[Login] 登录成功, 角色数 0
[Select] 建角色成功: TestHero
[Game] 进入游戏! 玩家: TestHero, 位置: (457,400), 方向: Up, 地图: 1
```

退出客户端后 MainUI 会重新拉起启动器（日志 `Zircon exited … / Starting pocket-linux-kms`）。

## 未完成 / 已知限制

1. **性能**：fbdev 后端已能在掌机屏幕上显示完整游戏画面；当前内部渲染为
   640×360、2×上采样到 1280×720，实测约 8–11 FPS。瓶颈主要是 EGL pbuffer
   的 `glReadPixels` 回读，后续可评估 PBO 双缓冲或 DMA-BUF 路径；默认音频仍为
   `Dummy`，因为设备 ALSA 回报的 period/buffer 参数会阻塞渲染循环。
2. **启动器封面的人工点击**：卡片、封面、`LAUNCH zircon` 路由都已就位，并已用
   运行时 `LAUNCH` 分支实际执行的同一条命令（`pocketjs-native-launch zircon`）
   验证了完整交接；本机自动化按键注入没能驱动该 PocketJS 构建（自建 uinput 手柄
   被运行时打开但不产生 UI 动作），因此“用手按键点封面”这一步需要在机器上人工确认。
   另外这台设备的 `/dev/fb0` 抓帧只返回一页旧帧，不能作为视觉判据。
3. 服务端目前用 `--singleplayer-dev`（测试账号注入满级数据），正式化前需要改成
   普通模式。

## 共用代码改动（桌面端默认行为不变）

| 文件 | 改动 |
|---|---|
| `GodotClient/Network/SinglePlayerLauncher.cs` | 新增 `ZIRCON_SERVER_EXE`：设置时直接运行随包的自包含 apphost；未设置时仍然是原来的 `dotnet ServerCore.dll` |
| `GodotClient/Scripts/AutoLoginArgs.cs` | 暴露已缓存的命令行参数快照 `AllArgs`（设备上重复调用 `OS.GetCmdlineUserArgs()` 会卡住） |
| `GodotClient/Scripts/LoginScene.cs` | `ZIRCON_TRACE=1` 时打印 `_Ready` 进度；headless 下跳过登录视频/开场 logo（`VideoStreamPlayer.Play()` 在 headless 会阻塞）；自动登录遇到 `AccountNotExists` 时创建单机账号后重试 |
| `GodotClient/Scripts/SelectScene.cs` | headless 下跳过选角/进游戏过场；`--char` 指定的角色不存在且账号无角色时自动建角 |

以上开关都基于环境变量或 headless 检测，桌面端（有窗口、无 `ZIRCON_TRACE`）行为不变。

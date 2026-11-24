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

客户端启动时由 `launch.sh` 设置 `ZIRCON_HANDHELD=1`。掌机模式始终使用实体按键语义输入，A/B 不会再同时产生按键和鼠标事件，右摇杆只用于目标选择，不移动鼠标。Start 在两套 HUD 之间切换：默认 HUD 保留原有布局，掌机 HUD 隐藏底部大面板、技能条、腰带和聊天栏，保留小地图与紧凑 HP/MP/Focus/目标状态栏及配置动作栏。

下表是掌机模式的操作；游戏窗口失焦、手札/绑定界面或模态窗口拥有焦点时，战斗动作会取消并等待按键释放后再恢复。

| 实体按键 | 游戏中动作 |
|---|---|
| 左摇杆 | 八方向移动；短推走，持续推动后跑 |
| 右摇杆 | 按方向选择附近有效目标；方向夹角优先，其次距离。选择规则固定 |
| 十字键上/下/左/右 | 执行当前页对应的绑定；空槽不执行动作 |
| X / Y | 执行当前页对应的绑定 |
| A | 对当前目标交互：NPC 对话、拾取掉落物、攻击怪物 |
| B | 取消/清除当前目标焦点 |
| L1 / R1 | 松开时切换两页；两键实际重叠按下触发固定组合槽，不翻页 |
| L2 / R2 | 短按松开执行tap槽；达到配置阈值执行hold槽一次；L2+R2重叠触发组合槽 |
| Start | 默认 HUD / 掌机 HUD 切换 |
| Select | 短按切换大地图，长按切换地面掉落物名称 |
| Menu / Mode | 打开或关闭全屏手札；绑定入口在「系统 → 操作」 |
| L3 | 自动跑步开关；绑定界面中打开操作时间设置 |
| R3 | 锁定/解除当前目标 |

F2 掌机 HUD 右下显示当前页六个真实绑定（十字键方向、X、Y），左下显示六个固定槽；空绑定保持空白，不以未绑定技能填充。长按肩键时显示tap、hold、组合动作和进度。「系统 → 操作」中的「操作绑定」界面可编辑两页、固定槽、长按阈值和组合窗口；绑定按角色和服务器保存，确认替换后立即生效。

fbdev 内部将 Select/L3/R3/Y 分别转成 `F15/F13/F14/F16`，避免与桌面
`Alt/Shift/Ctrl/Tab` 语义冲突；这些内部键只在 `AutoLoginArgs.Handheld` 下消费。
进入地图并关闭当前对话框后，按 **Menu** 打开全屏手札。L1/R1 切背包、人物、技能、任务、社交、系统，L2/R2 切分类；十字键或左摇杆选中项目，A 操作，B 逐层返回，右摇杆上下滚动详情。与 NPC 交谈或交易时，服务端开放的服务优先，不可用手札绕过。见[设计与网页原型](design/zircon-handheld-menu-spec.md)。

桌面端默认行为不变；上述按键和 HUD 分支由 `AutoLoginArgs.Handheld` /
`ZIRCON_HANDHELD` 门控。实现位于 `GodotClient/Input/HandheldActions/`、
`GodotClient/Scripts/GameScene.Handheld*.cs` 和 `GodotClient/Controls/HandheldHud.cs`。

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

### 本地动作绑定集成（2026-11-24）

- `dotnet build GodotClient/ZirconClient.csproj --no-restore` 通过；只有仓库原有 nullable/unused-variable 警告。
- `Tests/HandheldActionsChecks` 16 项通过，覆盖单键按下只触发一次、长按、释放、组合边界、组合成员重按门控和配置校验/往返。
- Godot headless `HandheldActionIntegrationChecks`、`HandheldBindingsChecks`、`HandheldHudChecks`、`HandheldInputChecks`、`HandheldQuickLayoutChecks`、`HandheldMiniMapChecks` 与 `RunningContinuityChecks --offline-movement-test` 通过。
- Xvfb/OpenGL 在 640×360 实际渲染手札绑定页、第二页、固定槽、功能候选、清空候选、保存失败和阈值页；F2 动作页、固定槽、L2 长按及组合提示截图见 `evidence/handheld-actions-ui-2026-11-24-verified/` 与 `evidence/handheld-actions-hud-2026-11-24/`。HUD 截图使用无地图角色夹具，未验证实际地图纹理或真实角色绑定数据。
- `godot-fbdev/rebuild-client.sh` 使用已有 AArch64 fbdev 模板完成 ARM64 导出，产物 201 个文件，启动器可执行文件含 `GDPC`，配套 `ZirconClient.dll` 已生成并列入 SHA-256 manifest。此为当前工作树构建，不代表已部署。
- TG5040（PJOS 1.5.3 / A133）已备份并仅更新客户端；197 个部署文件校验通过。经现有 MainUI 原生入口启动，核对运行进程映射的 DLL 哈希并取得[真机进图截图](evidence/tg5040-native-actions-world-2026-11-24.png)。实体输入、绑定保存重登、施法/用药未验收，见[整合报告](../../docs/HANDHELD_ACTIONS_INTEGRATION_REPORT.md)。

- 本机 Godot Mono/X11 接隔离服务端和存档副本：完成 TestHero 登录、地图绘制；测试账号补齐到 61 个技能、431 件物品。
- 显式开启掌机模式：键盘模拟 F10/方向键验证 HUD 双向切换、技能/腰带面板；确认释放未绑定的火球术并收到服务端 `ObjectMagic`。
- fbdev 输入源文件已用 Zig 编译为 AArch64 目标文件；未重新导出或部署整包，实体摇杆、扳机、对话框指针与新包上屏仍待实机验证。

### TG5040 启动恢复（2d7ff081）

卡上原 `Mir2eiClient` 连无画面启动都在文本服务初始化期间段错误；其末尾没有嵌入 PCK 的 `GDPC` 标记。用卡上另一个已能启动的客户端所对应的 fbdev 模板，重新导出 `2d7ff081` 的 ARM64 客户端与配套 `data_ZirconClient_linuxbsd_arm64`，并同步更新服务端程序。通过原生启动入口两次进入地图；原有数据库未被覆盖，部署前备份位于卡上 `apps/Zircon/backups/tg5040-pre-2d7ff081/`。

[实机进图截图（1280×720，读取 fbdev 当前显示页）](evidence/tg5040-world.png)。此图只证明默认 HUD 和地图已上屏，不证明掌机快选或新版 fbdev 输入实现已经验收；该轮导出复用了已有模板。

[掌机 HUD 实机截图](evidence/tg5040-compact-hud.png)：用重新从 `2d7ff081` 构建的 fbdev 后端在 TG5040 进图，以 evdev 测试手柄触发 Start；日志记录掌机 HUD 和默认 HUD 双向切换。截图可见紧凑状态栏、右上小地图及底部聊天栏隐藏。这个测试不等同于实体按键验收。

[技能快选实机截图](evidence/tg5040-skills.png)：在掌机 HUD 中用 evdev 测试手柄触发十字键上，显示九个技能、选中框和详情。面板完整上屏；640×360 内部渲染下的小字仍偏难读。本次未验证释放技能。

[腰带快选实机截图](evidence/tg5040-belt.png)：按 B 退出技能快选，再用 evdev 测试手柄触发十字键下，显示十格腰带及选中框。面板完整上屏，小字辨识度仍有限；本次未验证道具消耗。

### 掌机输入回归与稳定性（2026-11-23）

在 TG5040 上通过 `PJOS Test Injector` 虚拟 evdev 重放验证移动、目标锁定、普通攻击、火球与反伤技能、背包装备、腰带绑定与消耗、第二页腰带选择，以及模态设置窗口关闭后恢复移动。服务端收到对应 `ObjectAttack`、`ObjectMagic`、`ItemMove`、`StatsUpdate`、`ItemChanged` 消息；随机传送卷轴使用后位置变化。硬件实体按键没有参与这些输入重放。

新增无服务端、无存档的界面回归脚本：

```bash
ZIRCON_HANDHELD=1 MIR3_EI_ROOT=~/mir2ei godot-mono --headless \
  --path GodotClient --script res://Tests/HandheldInputChecks.cs
```

当前脚本覆盖永久聊天栏不拦截游戏、可见对话框拦截输入、关闭后恢复、两套 HUD 使用统一世界瞄准格等行为。历史 `HandheldQuickSelect` 仅由隔离布局测试实例化，运行时十字键不再打开旧快选面板；现用 `HandheldQuickLayoutChecks` 验证六格绑定动作区。脚本不连接服务端，不验证 NPC 对话或技能命中。

**实机 NPC 对话：**角色在 `(379,442)`，用紧凑 HUD 选中 `(379,444)` 的六面神石（对象 54），按 A 打开[传送对话框](evidence/tg5040-npc-dialog.png)。对话中保持左摇杆向右，再按 B 关闭：两个阶段角色坐标都未改变。摇杆回中后重新向上推，角色才移动到 `(379,440)`。未选择收费传送选项；从远处按 A 自动接近未验证成功。

当前 TG5040 运行画面：[默认 HUD](evidence/tg5040-default-hud-2026-11-23.png) · [紧凑掌机 HUD](evidence/tg5040-compact-hud-2026-11-23.png) · [腰带选择器](evidence/tg5040-belt-selector-2026-11-23.png)。画面来自 framebuffer mmap 的当前显示页；两种 HUD 均通过 Start 在运行中来回切换。

修复包括把 compact/default HUD 的技能落点选择与选择器临时指针状态解耦，并移除指针/准星绘制循环中的逐帧小数组分配。完整 fbdev 客户端已重新导出并配套使用其 .NET 程序集。

长时间稳定性尚未通过：此前一轮 TG5040 会话约运行 7 小时后以 SIGSEGV 退出；没有 OOM 记录，旧启动禁用了 core，因此该次崩溃没有 native backtrace。新会话早期约 15–20 FPS、RSS 400 MB。一次严重掉帧期间，进程处于内核 `D`（disk sleep）状态，`MemAvailable` 约 19 MB、`/tmp` tmpfs 占用 153 MB；其中已完成的 framebuffer `.raw` 诊断帧共占约 137 MiB。清理这些临时帧后，tmpfs 占用降到 16 MB、`MemAvailable` 升至 161 MB，游戏立即从 `D` 状态恢复，FPS 从 0.8–1.3 回到 16–20；fbdev read/convert/blit 合计约 9–13 ms。随后的 12 分钟内，13 个样本持续在 17–20 FPS、RSS 399–414 MB，read 3.3–5.4 ms、convert 2.0 ms、blit 5.8–11.0 ms。该复现实验确认诊断帧堆积导致了当前的掉帧和内存回收停顿；它很可能也加重了前一轮的多小时问题，但没有旧 core，不能断定它造成了那次 SIGSEGV。清理后继续监控并为当前进程保留 core 捕获与 fbdev 每秒耗时记录。以后 framebuffer 原始帧 pull 到主机后应立即删除设备端 `.raw` 文件，避免再次占满 `/tmp` tmpfs。

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

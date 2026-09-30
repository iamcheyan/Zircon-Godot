# EI Godot 登录—选角—创建—进入游戏 全流程独立复核（2026-09-30）

执行：本次独立复核 goal（依据 `docs/GOAL_OPENCODE_EI_LOGIN_CHARACTER_FLOW_REVIEW.md`）
仓库：`/home/tetsuya/development/zircon`（`master`，本轮基线 `4cbf5360`）· 客户端 `GodotClient/`
逆向资料：`/home/tetsuya/development/Mir3-Research`（**全程只读，未修改/暂存/提交**）
上一轮报告：[`EI_LOGIN_CHARACTER_FLOW_REVIEW_2026-09-29.md`](EI_LOGIN_CHARACTER_FLOW_REVIEW_2026-09-29.md)（本文对其 §9 的一处事实性错误做了更正）

---

## 0. 本轮定位与工作区边界

- 本轮是**新的、独立的复核**：不照抄上一轮结论，全部关键行为在本机隔离环境重新回放取证。
- 开工时工作区**含他人未提交资产**（并发 goal 的 EI 对话框改动集），本轮**全程未覆盖 / 未回滚 /
  未暂存 / 未提交**它们；本轮只提交自己新建的文档与截图（见 §9）。
- 隔离测试：自建 `ServerCore` 副本监听 **127.0.0.1:7003**，数据库为该副本内 `Database/Users.db`
  （源 = 仓库 `Debug/ServerCore/Database/Users.db`，md5 `138ac3549426fae0682a93af0afbed2d`）。
  **仓库库 md5 本轮前后一致**；未触碰 7000（共享）、7001、7002（他人隔离实例）。

| 他人未提交资产（本轮基线 md5） | md5 |
| --- | --- |
| `GodotClient/Scripts/SelectScene.cs` | `83311442f05f21ba85ba6ff763e33e9c` |
| `GodotClient/Scripts/LegacyEiText.cs` | `05aa4fc57d3382343aca741cfb1822f3` |
| `GodotClient/Controls/LegacyEiDialog.cs` | `0d0539e073bb6dea3b8dc829092b9112` |
| `GodotClient/Controls/LegacyEiDialogText.cs` | `ba34b646881e7e773a62207eaa002189` |
| `GodotClient/Controls/LegacyEiNoticeDialog.cs` | `8bfbb251614bc9633b82690953da65f1` |

> 本轮复核**验证了这批 WIP 的运行行为**（它们已被运行时采纳），并在其中发现两处缺陷（§5 F1/F2）。
> 第一阶段严格未改动这些文件（见 §9）；owner 随后将它们提交（`bcb4adce`/`032e74a7`）并授权继续，
> 第二阶段只在 `LegacyEiNoticeDialog.cs` 上做最小增量修复。

---

## 1. 结论摘要

| 分类 | 项 |
| --- | --- |
| **verified**（本轮真机回放） | 登录屏（logo 视频 / ID·PASSWORD / 连接游戏）；**登录成功**；**登录失败 `WrongPassword` → 状态行 → 重试成功**；F50 洞窟选角屏与槽位选中详情框；创建入口 phase0→1→2；**创建成功 → phase3 → CreateChr 过场 → 回列表**；**创建失败弹框：`BadCharacterName` / `AlreadyExists`**（且留在 phase 2）；**每账号 2 角色上限弹框**；删除确认 Yes/No 框 + **NO 取消**；**YES → 服务端删除成功 → 列表刷新**；开始游戏 → phase4 → StartGame 过场 → **GameInter F0 公告框** → 确认进入游戏（800×600）；**StartGame 被拒 `Disabled` → 「无法开始游戏。」弹框**；**StartGame `Delayed` → phase3「冷却中, 3秒后重试」循环**；**断线弹框 + 按钮禁用** |
| **fixed & verified**（本轮修复） | **F1** 公告框 ✕ 关闭后永久黑屏 → 去掉 Zircon 关闭钮（`7b876c33`）；**F2** 公告框 800×600 未居中（偏 -80,-60）→ 按 800×600 逻辑画布居中（`a787ac85`）。两处均实机复验（截图 19/20） |
| **confirmed defect（已修）** | 见上；修复前证据保留在截图 13/14 |
| **not tested** | 名字 >14 的弹框（UI 不可达，见 F5）；非 Legacy(`--zircon-ui`) 全量回归 |
| **blocked** | 原版运行画面同视口 A/B（本机无 Windows/Wine，沿用既有结论） |
| **pending-evidence** | 公告框资源身份：本轮按 WIP 既有的 **GameInter F0** 修复 F1/F2；原版登录公告 UI 究竟用 F0 / F950 族 / `DMessageDlg` 帧 1240/1248/1250 尚未由逆向闭合（见 §5 F2 与 `EI_LOGIN_CHARACTER_FLOW_BLOCKERS.md`） |
| **evidence-conflict** | 无 |
| **对上一轮报告的更正** | 上一轮 §9「`--stay-select` 是无引用死标志」**错误**：该标志在报告自身基线 `db85cf8c` 即有 3 处引用（见 §5 F3） |

**未宣称「1:1 完成」**：§7 列出全部未闭合项。

---

## 2. 测试环境与可复现命令

```bash
# 构建（仓库根目录）。基线 = 0 错误 / 3 个既有警告（CS8632 + CS0219 ×2）
dotnet build GodotClient/ZirconClient.csproj --no-incremental

# 隔离服务端（绝不动 7000 共享服务端 / 7001 / 7002）
#   /tmp/ei-flow-review  ← 拷贝 Debug/ServerCore 可执行+Config+Translations
#   Database/{Users,System}.db = 仓库库副本；Map/ 软链只读
#   Server.ini: Port=7003, UserCountPort=3003（UTF-16 改写）
cd /tmp/ei-flow-review && dotnet ServerCore.dll > server.log 2>&1
# 客户端（始终显式 --server/--port，避免 SinglePlayerLauncher 自动拉起共享库服务端）
ZIRCON_LEGACY_UI_DATA_PATH=/home/tetsuya/mir2ei/LegacyEI/Data \
  godot-mono --path GodotClient -- --server 127.0.0.1 --port 7003 \
  --user test@test.com --pass test123 [--stay-select|--char TestHero] --window
```

- 无头显示：`Xvfb :100 -screen 0 1024x768x24` + openbox；`scrot` 截图。
- **窗口客户区原点实测 = 屏幕 (1,24)**（`xwininfo -id` 判定）；点击坐标 = 客户区坐标 + (1,24)。
- 校验工具 `ffprobe` 与资源解码器 `wilsdk.py` 与 Godot 侧实现**互不共用**（沿用既有审计）。

### 2.1 本轮踩到并已规避的陷阱（记录）

- **`SinglePlayerLauncher` 自动拉起共享库服务端**：客户端只给 `--window`（未给 `--server`）时，
  端口 7000 无监听 → 启动器用**仓库 `Debug/ServerCore/`（含仓库 Users.db）**拉起
  `ServerCore.dll --singleplayer-dev`（`SinglePlayerLauncher.cs:48-83`）。本轮一次误操作触发过它
  （已停止，仓库库 md5 未变）。**此后所有回放都显式 `--server 127.0.0.1 --port 7003`**，
  该分支在 `AutoLoginArgs.ServerAddress != null` 时直接 return（`SinglePlayerLauncher.cs:48`）。

---

## 3. 全流程状态图（本轮实测）

```
[启动 640×480] wemade.ogv logo(4.97s) → 登录屏
   ├ 背景 ei_Login.ogv (0,60)640×360 循环 + Interface1c F1 底条 + **F2(96,439)「ID[框] PASSWORD[框]」**
   ├ 按钮 F11 连接游戏(459,436)/F12 创建账号(139,379)/F14 修改密码(279,379)/F16 结束(439,379)
   └ 状态行 (8,460)（移植保留行；原版此屏无）
        │ 连接游戏 → CM_LOGIN
        ├ 失败：状态行「登录失败: WrongPassword」+ 按钮恢复（实测）→ 可重试
        └ 成功：phase3 淡出 2000ms → 选角屏
[选角 640×480, F50]
   ├ 槽0(250,210)/槽1(300,210)，variant 1→2 循环；刷新后**不默认选中**
   ├ 点击槽 → 选中详情框(80,110)「角色名/等级/职业」+ 开始/删除可用（实测）
   ├ 创建角色(440,93) → phase1（CreateChr.ogv 1.3s，无输入）→ phase2
   ├ 删除角色(79,243) → GameInter F950 Yes/No 确认框（WIP）→ YES 才发 DeleteCharacter
   └ 开始游戏(259,49) → StartGame
[建角 phase2, F80]
   ├ 两预览槽(variant4) + F82(201,434) + F81(247,384) + 名字框(288,405,75×13,max14) + 说明框
   ├ ✔(450,444) 提交；失败 → 弹框且**留在 phase2**（BadCharacterName/AlreadyExists，实测）
   └ 成功 → phase3 → SelChr 音效 + CreateChr.ogv → phase0 回列表（新角色可见，实测）
[进游戏]
   StartGame Success → phase4 → StartGame.ogv(1.37s, attachToRoot) → 黑屏 + **GameInter F0 公告框**
      → 底部对勾 → GameScene（800×600，实测）
[异常]
   断线 → 弹框「与服务器的连接被断开。」+ 创建/删除/开始禁用，结束保留（实测）
```

---

## 4. 逐阶段证据矩阵（本轮实机）

证据级别：`primary`=原版 EXE/反汇编/资源；`runtime`=本轮真机；`derived`；`impl`=仅实现。

| # | 阶段 | 原版证据（引用） | Godot 代码 | 级别 | 本轮验证 | 结论 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 启动/媒体 | `Docs/LEGACY_LOGIN_BOOT_FLOW.md`；wemade.dat 640×360 | `LoginScene.PlayLegacyBootLogo` | primary | 截图 01 | verified |
| 2 | 登录表单 | `login-flow-evidence.json::screens.char_select` | `ApplyLegacyEiLoginLayout` | primary | 截图 01 | verified |
| 3 | 登录成功/失败/重试 | 同上；服务端 `[Wrong Password]` | `ShowLoginResult`（`LoginScene.cs:271-278`） | runtime | 截图 02/03；服务端日志 15:33:29 `[Wrong Password]` → 15:33:42 `[Account Logon]` | verified |
| 4 | 角色列表/选中 | `[0x1168]=-1` 初值；0x4598BF 选中 | `RefreshList`/`SelectSkinCharacter`/`ApplyLegacySlotSelection` | primary | 截图 04/05；日志 `槽位选中: index=-1→0` | verified |
| 5 | 创建入口/过场 | F51(440,93) → phase1 → 0x45763D → phase2；F80/F81/F82 | `OnCreatePressed`/`SetSelectPhase`/`BuildLegacyCreateLayer` | primary | 截图 06；日志 `phase=1→2 背景F=50→80` | verified |
| 6 | 创建成功回列表 | 0x209 → phase3 → CreateChr → phase0 | `ShowNewCharacterResult`（`SelectScene.cs:1952-1966`） | primary | 服务端 `[Character Created]`；日志 `phase=3→0 角色数=1→2` | verified |
| 7 | 创建失败 | 0x20A 错误弹框（LoadString 800/802/9000） | `LegacyCreateFailureText` + `ShowLegacyEiDialog` | primary | 截图 07/08；日志 `建角色失败: BadCharacterName` / `AlreadyExists`，phase 停在 2 | verified |
| 8 | 每账号 2 角色上限 | F51 handler 两槽占用 → LoadString 802 | `SelectScene.cs:1202-1212` | primary | 截图 09；日志 `F51 建角被拒` | verified |
| 9 | 删除确认/取消/成功 | F53 → CMsg 228 Yes/No → mrYes 才发 | `ShowLegacyDeleteConfirm`（`SelectScene.cs:2125-2137`） | primary+derived | 截图 10/11/12；NO 时**无** `DeleteCharacter` 包；YES 时服务端 `[Character Deleted]` | verified |
| 10 | StartGame/过场/公告 | 0x20D → phase4 → StartGame.dat | `ShowStartGameResult`（`SelectScene.cs:2203-2232`）+ `LegacyEiNoticeDialog` | primary | 截图 13；日志 `phase=4 → ogv → GameInter F0 len=19` | verified（公告资源等价见 §7） |
| 11 | 进游戏身份/视口 | mode3 800×600 | `EnterGameScene`/`GameScene` | primary | 截图 15；日志 `[Game] 玩家: TestHero, 位置:(199,338), 地图:8`；窗口 800×600 | verified |
| 12 | 断线 | 0x210 弹框 | `ShowDisconnected`（`SelectScene.cs:2293-2308`） | primary | 截图 16；日志 `与服务器断开连接，禁用选角操作` | verified |
| 13 | 取消/返回/重试 | phase2 ✘ 回列表；StartGame Delayed 重试 | `ExitLegacyCreate`/`OnStartGameRetry` | impl | ✘ 退出建角实测回列表；`Delayed` → `phase=3` + 3s 重试循环（截图 18） | verified |
| 14 | StartGame 被拒 | 0x20E 弹框 | `ShowStartGameResult` else 分支（`SelectScene.cs:2256-2274`） | runtime | 隔离库置 `AllowStartGame=False` → `Result=Disabled` → 弹框「无法开始游戏。」（截图 17） | verified |
| 15 | StartGame 冷却 | 0x209 等待语义 | `ShowStartGameResult` Delayed 分支（`SelectScene.cs:2238-2255`） | runtime | 隔离库 `RelogDelay=5min` → 连续 `Result=Delayed`，`phase=3` 保留上一相位画面 + 3s 单一定时器重试（截图 18） | verified |

---

## 5. 本轮发现（含证据与文件行号）

### F1【confirmed defect，高 → **已修复** `7b876c33`】进游戏公告框右上 ✕ → 永久黑屏，无法进入游戏

- **现象**：StartGame 过场结束后出现 GameInter F0 公告框；点击其**右上角 ✕**（Zircon `WindowManager`
  给 `DXWindow` 加的关闭钮），对话框消失、`_uiLayer` 仍隐藏，客户端**永久停在 800×600 黑屏**，
  没有任何“进入游戏”路径可走。
- **复现（修复前）**：截图 `14-notice-close-x-stuck-black.png`；日志 `notice-x.log` 在
  `显示 GameInter F0 公告框` 之后**再无任何**相位/进入日志；等待 12s 仍黑屏。
- **根因**：`DXWindow` 为 `DrawChrome` 窗口生成关闭钮（`GodotClient/Controls/DXWindow.cs:27,90-113`，
  用现代 `Interface[15]` 帧），点它调用 `WindowManager.Close(this)` 只隐藏窗口；
  而 `LegacyEiNoticeDialog` 无 `Cancelled`、`SelectScene.ShowLegacyStartNotice`
  （`SelectScene.cs:2040-2082`）亦无关闭处理 → 无人接管，`_uiLayer` 保持隐藏。
- **原版语义核实**：该 ✕ 是**移植版外壳控件，原版没有**：
  • 本框素材 `GameInter` **F0** 只有底部中央一个「对勾」（本文件头注释 / 用户素材定位）；
  • 同族预游戏确认框 `0x418030`（`GameInter` F950）的绘制 `0x4182A0` 只画背景 + 标题 +
  自身 3 个按钮，**无标题栏关闭控件**（`RESEARCH_LOG` Finding 82 / Round 5756）；
  • 原版登录公告是**模态阻塞框**（`source-vs-reverse/client.md` §6.2 `DMessageDlg` 主线程阻塞循环），
  只由自身按钮退出 —— 不产生「外壳 ✕ 隐藏窗口而调用方仍等待」的路径。
  → 结论：这不是「✕ 应继续/取消/禁止关闭」的选择，而是**该 ✕ 本就不属于此框**，应移除。
- **修复**：`LegacyEiNoticeDialog` 构造设 `ShowCloseButton = false`（`GodotClient/Controls/LegacyEiNoticeDialog.cs`）。
  仅去掉移植版外壳控件，不新增调用方状态、不改 `SelectScene` 确认路径。
- **实机验证（修复后）**：公告框**无 ✕**；点击旧 ✕ 位置（客户区 461,22）后画面**逐字节不变**
  （两次 `scrot` md5 均 `e13b25b5…`）；点底部对勾仍正常 `进入游戏世界`（截图 19/20）。

### F2【suspected deviation，中 → **已修复** `a787ac85`】公告框在 800×600 下未居中（偏 -80,-60）

- **现象**：公告框 F0（324×462）实测绘制在客户区 **(158,9)**（截图 `13-startgame-notice-f0.png`），
  而 800×600 下的居中位置应为 **((800-324)/2,(600-462)/2)=(238,69)**，差 **(80,60)**。
- **根因**：`LegacyEiNoticeDialog.DefaultLocation` 用 **640×480** 计算
  `((640-324)/2,(480-462)/2)=(158,9)`；但该框在 `ClientSettings.ApplyLegacyPregameWindow(800,600)`
  （`SelectScene.cs:2226`）**之后**显示，画布已是 800×600，且未得到任何居中补偿。
- **证据**：原版模态提示框构造时**居中**（`source-vs-reverse/client.md` §6.2
  「DMessageDlg 按 DialogSize 选背景帧……并居中」）；`UiScaler.BaseWidth/Height = 800×600`
  （`GodotClient/Scripts/UiScaler.cs:40-41`，即原版 mode 3 屏幕区），层 Transform 会把该逻辑画布
  整体缩放+居中到视口 → 按逻辑画布居中即屏幕居中（任意缩放）。
- **修复**：`DefaultLocation` 改为 `((UiScaler.BaseWidth-324)/2,(UiScaler.BaseHeight-462)/2)=(238,69)`。
- **实机验证（修复后）**：截图 19 中公告板左上 = 客户区 **(238,69)**（800×600 中心）。
- **遗留 pending-evidence**：F0 是否为原版登录公告的正确资源仍未由逆向闭合（另见
  `DMessageDlg` 帧 1240/1248/1250 候选）——已记入 `EI_LOGIN_CHARACTER_FLOW_BLOCKERS.md`。

### F3【docs 更正】`--stay-select` 并非无引用死标志

- 上一轮报告 §9（第 192 行）称 `AutoLoginArgs.StayInSelect` 是“无引用死标志”。
- 实测：该标志在**报告自身基线 `db85cf8c` 就有 3 处引用**
  （`git show db85cf8c:GodotClient/Scripts/SelectScene.cs` → L207/L227/L1827；当前
  `SelectScene.cs:217/237/2006`），且本轮 `--stay-select` 回放**实际生效**（登录后停在选角屏）。
- **处置**：本轮已对上一轮报告 §9 该行加注更正。

### F4【服务器语义，非客户端缺陷】同账号允许重名

- 客户端 `AlreadyExists` 弹框仅在**跨账号**重名时可触发：`ServerLibrary/Envir/SEnvir.cs:3946-3953`
  在 `CharacterInfoList[i].Account == con.Account` 时 `continue`（同账号不判重）。
- 本轮以 `Bot01`（他账号角色）成功触发 `AlreadyExists`（截图 08），以同账号 `TestHero` 两次创建
  **均成功**——这是服务端既有设计，非 bug；但意味着同账号重名不会走该弹框。

### F5【impl，防御性分支】名字 >14 的弹框在 UI 上不可达

- `SelectScene.cs:1039-1046` 有 `name.Length > 14` 的弹框分支；但名字输入框
  `_legacyCreateName` 设 `MaxLength = 14`（`SelectScene.cs:1708`），UI 无法输入 >14。
- 原版同为 `EM_SETLIMITTEXT 0xE`（`0x4511D0`），即原版该分支同样是**防御性**的（WM_CHAR 也受限）。
- **判定**：两端一致，非差异；仅记录其运行期不可达。

### F6【candidate】选角 phase 0 左上角提示文字

- 实测选角屏左上角显示 `选择角色后点进入游戏`（截图 04/05），来自 `SelectScene.cs:406` +
  `RelocateLegacyStatusLabel()`（`SelectScene.cs:1332-1344`，定位 (8,8)）。
- 原版 phase 0 tick（`0x457790`）只画 F50 → 2 槽 → 4 按钮 → 选中详情（`0x458150`），
  **未见左上角常驻文字**。判定为移植版附加文本（candidate，缺否定性 primary 证据）。

---

## 6. 真实执行结果（逐项）

| 检查 | 结果 |
| --- | --- |
| 构建 `dotnet build GodotClient/ZirconClient.csproj --no-incremental` | **0 错误 / 3 警告**（CS8632 TableSnapshotTool.cs:128；CS0219 GameScene.cs:6628/6714，均既有） |
| 登录失败+重试 | 服务端 `15:33:29 [Wrong Password]` → 客户端状态行「登录失败: WrongPassword」、按钮恢复 → 重输 `test123` 点连接 → `15:33:42 [Account Logon]` → 进入选角屏 |
| 选角/选中 | `槽位选中: index=-1`（刷新后不选中，符合 `[0x1168]`）→ 点槽 0 → `index=0 开始=True 删除=True`，详情框「角色名/等级 255/职业 道士」 |
| 创建成功 | 输入名 → ✔ → `phase=3` + `CreateChr.ogv` → `phase=0`，服务端 `[Character Created]` |
| 创建失败 | `BadCharacterName`（名字含空格）→「此角色名不正确。」；`AlreadyExists`（Bot01）→「此角色名已存在。」；两次均留在 phase 2 |
| 2 角色上限 | 点创建 → 「您可以为每个单独的帐号建立两个角色。」（`F51 建角被拒`） |
| 删除取消 | Yes/No 框点 **NO** → 框关闭、选中保持、**未发** `DeleteCharacter` 包（`grep -c DeleteCharacter = 0`） |
| 删除成功 | 点 **YES** → `入队: DeleteCharacter` → 服务端 `[Character Deleted]` → `角色数=2→1`，状态「删除成功」 |
| StartGame→进游戏 | `StartGame Success` → `phase=4` → `StartGame.ogv 播放完毕` → `GameInter F0 公告框 len=19` → 点对勾 → `[Game] 进入游戏! 玩家: TestHero, 位置:(199,338), 地图:8`，窗口 800×600 |
| StartGame 被拒 | 隔离库 `AllowStartGame=False` → `S.StartGame Result=Disabled` → 弹框「无法开始游戏。」，留在选角屏 |
| StartGame 冷却 | 隔离库 `RelogDelay=5min` → `Result=Delayed` → `phase=3 保留上一相位画面` + 状态「冷却中, 3秒后重试...」→ 3s 后重发 `StartGame`，仍 Delayed（单一 `_startRetryTimer`，无定时器堆积） |
| 断线 | kill 隔离服务端 → `与服务器断开连接，禁用选角操作` + 弹框「与服务器的连接被断开。」；创建/删除/开始禁用，结束保留 |
| 重复点击 | 登录屏连点「连接游戏」两次 → `入队: Login` 计数 **= 1**（首次点击即禁用按钮），无双发 |
| **F1/F2 修复后复验** | 公告框无 ✕；点旧 ✕ 位置（461,22）前后截图 **md5 相同** `e13b25b5…`（无副作用）；公告板左上 = 客户区 **(238,69)**；点对勾 → `进入游戏世界`、`[Game] 玩家: TestHero, 地图: 8`（截图 19/20） |
| 数据安全 | 仓库 `Debug/ServerCore/Database/Users.db` md5 前后均 `138ac3549426fae0682a93af0afbed2d`；全部建/删只写 `/tmp/ei-flow-review/Database/Users.db` |
| 工作区 | 开工时保留他人 5 文件未动（md5 逐一相同）；其由 owner 以 `bcb4adce`/`032e74a7` 提交后，本轮在其上修复；`git diff --check` 无输出；Mir3-Research 未改动 |

### 截图索引（仓库内 `docs/screenshots/ei-login-flow-review-2026-09-30/`，均为客户端窗口完整 viewport，共 20 张）

| 文件 | 内容 |
| --- | --- |
| `01-login-video.png` | 登录屏（logo 视频 + ID/PASSWORD + 四按钮 + 状态行） |
| `02-login-fail-wrongpassword.png` | 密码错误：状态行「登录失败: WrongPassword」 |
| `03-login-retry-success.png` | 重试成功进入 F50 选角屏 |
| `04-select-list.png` | 选角屏（1 角色，未选中） |
| `05-select-slot-selected.png` | 点击槽 0 后选中详情框「角色名/等级/职业」 |
| `06-create-phase2.png` | 建角 phase2（F80 + 两预览 + F82/F81 + 名字框 + 说明框） |
| `07-create-badname.png` | 非法名弹框「此角色名不正确。」 |
| `08-create-name-exists.png` | 跨账号重名弹框「此角色名已存在。」 |
| `09-create-2char-limit.png` | 2 角色上限弹框 |
| `10-delete-confirm-yesno.png` | 删除确认 Yes/No 框（CMsg 228 文案） |
| `11-delete-cancelled-no.png` | 点 NO 后框关闭、状态不变 |
| `12-delete-success.png` | 删除成功回列表、状态「删除成功」 |
| `13-startgame-notice-f0.png` | StartGame 过场后 GameInter F0 公告框（800×600） |
| `14-notice-close-x-stuck-black.png` | **点公告框 ✕ 后永久黑屏（F1 证据）** |
| `15-ingame-800x600.png` | 进入游戏世界（地图 8，800×600 HUD） |
| `16-select-disconnected.png` | 断线弹框 + 按钮禁用 |
| `17-startgame-disabled-cannotstart.png` | StartGame 被拒「无法开始游戏。」弹框 |
| `18-startgame-delayed-phase3.png` | StartGame 冷却：phase 3 保留选角画面 + 「冷却中, 3秒后重试...」 |
| `19-notice-fixed-centered-noclose.png` | **修复后**：公告框无 ✕ 且左上 = 客户区 (238,69)（F1/F2 修复证据） |
| `20-notice-fixed-confirm-ingame.png` | 修复后点对勾正常进入游戏（800×600） |

原始日志在 `/tmp/ei-flow-review/*.log`（不入库；含账号等运行字段）。

---

## 7. 未验证 / 阻塞 / 冲突

- `not tested`：名字 >14 的弹框（UI 不可达，见 F5）；非 Legacy(`--zircon-ui`) 全量回归未做。
- 另记（minor，未修）：`ShowStartGameResult` 成功分支日志仍写「显示 F602 公告确认」（`SelectScene.cs:2230`），
  与实际使用的 GameInter F0 不一致（同一文件 2068 行已改为 F0 文案）。
- `pending-evidence`：**GameInter F0 公告框**的资源身份与屏幕坐标仍缺 primary —— F1/F2 已按
  「原版模态框居中 + 预游戏对话框族无标题栏关闭钮」的证据修复（`a787ac85`/`7b876c33`），
  但 F0 是否为原版登录公告的正确资源（对比 `DMessageDlg` 帧 1240/1248/1250）未闭合；
  需用户提供 F0 归属的原始依据或授权继续逆向。已记入 `EI_LOGIN_CHARACTER_FLOW_BLOCKERS.md`。
- `blocked`：原版运行画面同视口对照（本机无 Windows/Wine，沿用既有 blocked）。
- `evidence-conflict`：无（F3 是对上一轮报告的文字更正，非原版证据冲突）。
- F6（phase0 左上提示文字）为 `candidate`，需否定性 primary 证据方能定论。

---

## 8. commit / push / 远端 SHA

远端：`origin = git@github.com:iamcheyan/Zircon.git`（当前目标主分支 `master`）。
**每完成一项即提交推送，并用 `git ls-remote origin refs/heads/master` 核对。**

| # | SHA | 说明 | 远端核对 |
| --- | --- | --- | --- |
| 1 | `8191bf9c` | `docs(ei复核): 独立复核报告 + 公告框 ✕ 死锁取证截图` | ✅ |
| 2 | `199a7725` | `docs(ei复核): 更正上一轮报告 --stay-select 死标志结论` | ✅ |
| 3 | `7c64f7a1` | `docs(ei复核): 补 StartGame 被拒/冷却两条失败路径取证与截图` | ✅ |
| 4 | `9a58a80b` | `docs(ei复核): 补重复点击防重入实测记录` | ✅ |
| — | `bcb4adce` / `032e74a7` | **owner 提交的既有 WIP**（新对话框控件 + SelectScene 接入，非本 Goal 提交） | ✅ |
| 5 | `a787ac85` | `fix(ei复核): 公告框按 800×600 逻辑画布居中，修正 (80,60) 偏位`（F2） | ✅ |
| 6 | `7b876c33` | `fix(ei复核): 公告框去掉 Zircon 关闭 ✕，消除关闭后永久黑屏`（F1） | ✅ |
| 7 | 见下 | `docs(ei复核): 记录 F1/F2 修复与复验` + 截图 19/20 | ✅ |
| 8 | 见下 | `docs: EI 登录流程阻塞事项`（`EI_LOGIN_CHARACTER_FLOW_BLOCKERS.md`，仅该文件） | ✅ |

代码改动仅限 `GodotClient/Controls/LegacyEiNoticeDialog.cs`（F1/F2 两处）。报告自身的提交 SHA 无法内嵌，
以推送后 `git ls-remote origin refs/heads/master` 的远端 HEAD 为准。全程未 force push、未切分支、
未提交数据库/日志/凭据。

---

## 9. 文件边界与修复落地

- **第一阶段（复核）**：`GodotClient/Scripts/SelectScene.cs`、`LegacyEiText.cs`、
  `LegacyEiDialog.cs`、`LegacyEiDialogText.cs`、`LegacyEiNoticeDialog.cs` 这 5 个文件当时属他人未提交
  资产，本轮**未覆盖/未回滚/未暂存/未提交**（开工与收工 md5 一致）。
- **第二阶段（修复）**：owner 已把上述文件分两批提交（`bcb4adce` / `032e74a7`）并明确授权继续修改；
  此后本轮在其上做最小增量修复——仅 `GodotClient/Controls/LegacyEiNoticeDialog.cs` 两处
  （F2 居中常量 `a787ac85`、F1 关闭钮 `7b876c33`），未改动 `SelectScene.cs` 与其它对话框控件。
- **本轮 docs 提交只含**：本报告、`docs/screenshots/ei-login-flow-review-2026-09-30/` 20 张截图、
  上一轮报告 §9 一行更正、以及 `docs/EI_LOGIN_CHARACTER_FLOW_BLOCKERS.md`。

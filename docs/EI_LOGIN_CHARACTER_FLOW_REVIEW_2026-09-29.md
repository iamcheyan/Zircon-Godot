# EI Godot 登录—选角—创建—进入游戏 全流程复核报告（2026-09-29）

执行者：OpenCode（`docs/GOAL_OPENCODE_EI_LOGIN_CHARACTER_FLOW_REVIEW.md`）
仓库：`/home/tetsuya/development/zircon`（`master`）· 客户端 `GodotClient/`
逆向资料：`/home/tetsuya/development/Mir3-Research`（**全程只读，未修改/暂存/提交**）

---

## 1. 结论摘要

| 分类 | 项 |
| --- | --- |
| **verified** | 登录成功路径、登录失败+重试路径（服务端 `[Wrong Password]` → 状态行 → 二次 Login 成功）、选角屏停留与槽位选中、创建角色全流程（phase 0→1→2→3→0，服务端 `[Character Created]`，列表 1→2 角色）、StartGame 成功过场、F602 公告确认、进入游戏世界身份校验、预游戏 640×480 / 进游戏 800×600 视口、登录表单 8 处修复、创建屏 640×480 无裁切 |
| **not tested** | 删除角色确认流（避免误删 `TestHero`）、断线恢复（`8b54a809` 的 `DisconnectionEvent` 接线未做行为测试）、名字 >14 字拒绝、重名/非法名拒绝、服务器拒绝创建、多角色（≥3）列表布局、取消/返回/重复点击的完整矩阵 |
| **blocked** | 无（隔离服务端 7001 + 独立 `Users.db` 副本可用，写库类路径可测） |
| **unresolved / pending-evidence** | Interface1c **F2 绘制原点没有静态 draw 记录**（本轮用截图拟合反解，见 §6.3）；EI 原版 phase 2→3 淡出 2000ms 的逐帧时序未逐帧比对；`Client/` 旧版与 EI 证据的差异未再系统比对 |

**未宣称「1:1 完成」**：§9 列出全部未验证项。

---

## 2. 测试环境与可复现命令

```bash
# 构建（仓库根目录；基线 = 0 错误 / 3 个既有警告 CS8632 + CS0219 ×2）
dotnet build GodotClient/ZirconClient.csproj            # 本轮 ~25s，0 错误 3 警告
dotnet build GodotClient/ZirconClient.csproj --no-incremental

# 隔离服务端（绝不动 7000 共享服务端）
cd /tmp/ei-flow-srv && dotnet ServerCore.dll > server.log 2>&1   # 监听 127.0.0.1:7001
#   数据库：/tmp/ei-flow-srv/Database/{System,Users}.db（**独立副本**）
#   仓库库：/home/tetsuya/development/zircon/Debug/ServerCore/Database/Users.db
#           md5=138ac3549426fae0682a93af0afbed2d，本轮前后完全一致

# 无头显示
Xvfb :100 -screen 0 1024x768 & DISPLAY=:100 openbox &

# 本轮验证脚本（坐标换算：截图客户区原点 = (1,24)）
/tmp/ei-flow/run.sh <tag> <sleep秒> [客户端参数]              # 启动+截图
/tmp/ei-flow/e2e-click.sh e2e-f2        # 登录→选角→StartGame→F602→点击✔→进游戏
/tmp/ei-flow/create_check.sh cc2        # 选角屏点「创建」（--char NoSuchHero 停在选角）
/tmp/ei-flow/create_flow.sh cf1         # 创建角色提交→回列表（写隔离库）
```

`--char NoSuchHero` 是「停在选角屏」的开关：`SelectScene.cs:207` 指定角色不存在时不自动进游戏。
（`--stay-select` 标志在代码中**无引用**，是死标志，不要用。）

---

## 3. 全流程状态图（当前实现）

```
[启动] Godot 启动 → 显示设置 → ApplyLegacyPregameWindow(640,480)
   │   wemade.ogv 开场 logo（覆盖层，播完自毁，原版是独立 boot 阶段）
   ▼
[登录 LoginScene, 640×480]
   背景 ei_Login.ogv (0,60) 640×360 ＋ Interface1c F1 底条 (0,360)
   ＋ **Interface1c F2 (96,439)「ID [框] PASSWORD [框]」** ＋ 4 按钮 F11/F12-13/F14-15/F16-17
   ├─ 输入：账号 (128,440)-(227,454)、密码 (326,440)-(425,454)（无边框，框线来自 F2）
   ├─ 点「连接游戏」→ CM_LOGIN(0x65/0x201 线)
   │     ├─ 失败：状态行提示、按钮重新 Enabled（实测服务端 `[Wrong Password]`）→ 可重试
   │     └─ 成功：phase 3 淡出 2000ms → 选角屏
   └─ 断线：LoginScene 有断线提示路径（选角/游戏另有 DisconnectionEvent）
   ▼
[选角 SelectScene, 640×480]
   背景 F50 (0,0) 640×480；按钮 创建(440,93) 删除(79,243) 开始(259,49)
        结束(28,438) ✔(450,444) ✘(491,444) 武士(266,419) 法师(308,419) 道士(352,419)
   洞窟槽位 slot0=(250,210) slot1=(300,210)，点击只改选中态（原版 [0x1168] 语义）
   ├─ 点「创建」→ phase 1（CreateChr.dat 过场 1.3s）→ phase 2 建角面板
   ├─ 点 ✔ → CM_NEWCHR(0x65) → 等待服务端
   └─ 点「开始」/ auto-login → StartGame 请求
   ▼
[建角 phase 2]
   背景 F80 (0,0) 640×480；F81(247,384) 164×88；F82(201,434) 256×32
   预览槽 (110,110)/(400,160)；名字框底 (287,404,77×15)；名字框 (288,405,75×13,max14)
   ├─ 提交：✔(450,444) 或 名字框回车（原版 F86 处理器 0x459F56-0x45A02A：清框→>14 拒→0x4589B0 校验→0x65）
   ├─ 成功：SM_NEWCHR_SUCCESS → phase 3（SelChr.wav + CreateChr.dat）→ phase 0 回列表，新角色可见
   └─ 原版提交后不改阶段，阶段 3 由回包驱动（代码注释与实现一致）
   ▼
[StartGame]
   SendStartGame → S.StartGame Success → 窗口切 800×600 → StartGame.ogv 过场
   → F602 公告确认框（✔=496,27 40×20 → 客户区 603..643/137..157，点击屏幕 (624,171)）
   → 进入 GameScene（800×600 HUD 铺满视口）
```

---

## 4. 逐阶段证据矩阵

证据级别：`primary`=原版 EXE/反汇编/资源；`runtime`=原版运行截图；`derived`=由 1+2 推导；`impl`=仅当前实现。

| # | 阶段 | 原版证据 | Godot 对应 | 级别 | 本轮验证 | 结论 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 启动/媒体 | `Docs/LEGACY_LOGIN_BOOT_FLOW.md`；`wemade.dat` AVI 640×360 149帧；`ei_Login.dat` AVI | `LoginScene.PlayLegacyBootLogo` / `ApplyLegacyEiLoginLayout`（ogv 转码） | primary | 截图 `f2-1.png`：logo 播放、视频满 640 列 | verified（**编码不同**：Indeo→Theora，见 §8） |
| 2 | 登录表单/请求/失败 | `login-flow-evidence.json::screens.char_select`：账号/密码 `SetRect`、按钮帧与坐标、`CM_IDPASSWORD 0x2001` | `ApplyLegacyEiLoginLayout`、`OnLoginPressed` | primary | 服务端 `[Wrong Password]` + 二次 Login 成功；`click_t1_fail.png`/`click1.log` | verified |
| 3 | 角色列表 | 同上 phase 状态机（1 登录→2 服务器列表→3 淡出）；选角屏 `parent` 证据 | `SelectScene._Ready/RefreshList` | primary | `cc2-1`/`cf1`：角色数=1→2 | verified |
| 4 | 选角/人物显示 | 选角帧段与锚点（`EI_CHARACTER_SELECT_ANIMATION_PARITY_2026-09.md`） | `BuildLegacySelectUi`/洞窟槽位 | primary | 截图 + 日志 `槽位选中 index=-1`、槽动画 variant 1→2 | verified（本轮未改，沿用既有审计） |
| 5 | 创建入口/页面 | F80/F81/F82、名字框 `(287,404)`、`0x459F56` F86 处理器、上限 14 | `BuildLegacyCreateLayer`/`SubmitSkinCharacter` | primary | `cf1`：phase 0→1→2；元素坐标全部在 640×480 内 | verified |
| 6 | 创建提交/回列表 | `CM_NEWCHR 0x65`、`SM_NEWCHR_SUCCESS → phase 3` | `SubmitSkinCharacter`→`SendNewCharacter` | primary | 服务端 `[Character Created] Tmpflow`；客户端 `phase=3 → phase=0 角色数=2`；**仓库库 md5 不变** | verified（写在隔离库） |
| 7 | StartGame/过场 | `StartGame.dat` 过场、F602 公告 | `AutoStartGame`/`PlayLegacyTransition`/`ShowLegacyStartNotice` | primary | `e2e-f2.log`：Success → ogv → F602 len=19 → ✔ → 进入 | verified |
| 8 | 进游戏身份/状态 | 800×600 模式 3 写点 `0x419377` | `GameScene` L1036 `ApplyLegacyPregameWindow(800,600)` | primary | `[Game] 进入游戏! 玩家: TestHero, 位置:(199,338), 地图:8`；HUD bbox 0..799/0..599 | verified |
| 9 | 取消/返回/重试/异常 | 断线、重复点击、非法值 | `ShowLoginResult`、`DisconnectionEvent`、名字 >14 分支 | impl | 仅失败重试实测；其余未测 | **not tested**（见 §9） |

---

## 5. 本轮确认的问题与修复（11 项代码修复，逐项推送核对见 §10）

| # | SHA | 问题 → 根因 | 验证 |
| --- | --- | --- | --- |
| 1 | `ed1f241b` | Legacy 登录表单整体右下偏移 (80,100) → `_Ready` 里「居中偏下」在 `ApplyLegacyEiLoginLayout` 之后又平移一次 | 截图坐标回归 |
| 2 | `70f2e81f` | 视频盖住「创建账号/修改密码/结束」三个按钮 → `AddChild` 次序使视频画在表单之上 | 截图：三按钮可见 |
| 3 | `ac544429` | 顶/底黑带与底图错位 → 清屏色不是黑 + backdrop 偏移 | `f2-1`：y0..59 最大亮度 0，视频占满 640 列 |
| 4 | `1b979ecb` | 原版没有的标题文字 → `_loginTitle` 未隐藏 | 截图无标题 |
| 5 | `6ba6aa9a` | 预游戏窗口尺寸 → 原版 mode2=640×480 / mode3=800×600（`0x45D270` 写点） | 登录 642×509 外框 / 进游戏 802×629 |
| 6 | `309c8d4f` | 选角屏 StartGame 回包处理三处缺陷 | `e2e-click.sh` |
| 7 | `8b54a809` | 选角屏缺断线处理 + Legacy 状态文字被裁 | 编译+接线（**行为未测**） |
| 8 | `83502a82` | 登录/注册按钮 `Enabled` 状态与原生按钮不一致 | 失败→按钮重新可用 |
| 9 | `cf35b08b` | **公告正文不显示**：`DXTextArea` 构造期用默认 `Size(14,12)` 算出 `_edit.Size=(8,8)`，对象初始化器赋 `Size=500×112` 时**树外节点不派发 `Resized`** | 调试打印 `edit=(8,8)` → `_Ready` 调 `ResizeEditor()`；公告 19 字可见 |
| 10 | `7418e3c8` | **面板在 x=640 被切断**：`DisplayServer.WindowSetSize` 只改 X11 窗口，Godot `Window`/视口停在 640×480 | `[Diag] ds/win/vp`：修前 `vp=(640,480)`、修后 `vp=(800,600)`；公告 bbox x107..690 |
| 11 | `db85cf8c` | **登录屏缺 ID/PASSWORD 标签与输入框线**（只放了无边框 `DXTextInput`）→ 补 Interface1c F2 | 见 §6.3 |

> **#9/#10 是可复用的 Godot 陷阱**，已写进代码注释：
> ① 树外 `Size=` 赋值不派发 `Resized`；② `WindowSetSize` 不更新 Godot 视口。
> `DXTextArea` 修复同时惠及 `CommunicationDialog._detail/_message`、`GuildDialog._noticeArea`。

---

## 6. 本轮三个「独立根因」的取证方法

### 6.1 DXTextArea 正文不可见
日志断点：`[DXTextArea] _Ready … edit=(8,8)`（对象初始化器 `Size=500×112` 之后仍 8×8）。
修复 = `_Ready()` 内补一次 `ResizeEditor()`；调试打印已移除。

### 6.2 预游戏视口停在 640×480
临时 `[Diag] vp/win/ds` 打印：StartGame 后 `ds=(800,600) win=(640,480) vp=(640,480)`。
修复 = `ClientSettings.ApplyLegacyPregameWindow` 先设 `SceneTree.Root.Size`，再用 `DisplayServer.WindowSetSize` 兜底；诊断代码已删除。

### 6.3 Interface1c F2 绘制原点（**pending-evidence → derived**）
- 资源：`wilsdk` 解码 `/home/tetsuya/mir2ei/LegacyEI/Data/Interface1c.wil` **F2 = 328×20**，
  逐列扫描得 `ID x0..12/y5..14`、`框1 x27..128/y0..19`、`PASSWORD x138..209`、`框2 x225..326/y0..19`（1012/6560 像素不透明，框内透明）。
- 静态证据缺失：`layout.json::secondary_control_constructors`（scope `interface1c-cluster-0x4027`）只枚举 F11/F13/F15/F17 四个按钮，**无 F2 draw 记录**。
- 反解：把 F2 贴进 640×480 黑底 → 降采样到 205×154 → 与 `SCREEN0001.jpg` 底条（rows139..152, cols26..141）做 SSD，
  BOX/LANCZOS/BILINEAR/HAMMING **四种重采样一致最优 `(96,439)`**（次优 `(97,439)`；全黑基线 SSD 473.7 → 最优 87.1 / 30.3）。
- 手算交叉：框1 = x123..225 对应缩略图列 39..71、框2 = x321..422 对应列 102..135，文字行 443..452 落在缩略图 142..145 行，全部吻合。
- 交付：`LoginScene.ApplyLegacyEiLoginLayout` 新增 F2 贴图并 `MoveChild(0)` 压在输入框之下（右框线 x=225 落在账号框 128..227 内）。
- 实机比对：我们的截图带 `96..423 × 439..458` 与 F2 参考 **1:1 逐像素 p95 差 = 0**，差异仅来自预填账号文字与密码点号。

---

## 7. 真实执行结果

| 检查 | 结果 |
| --- | --- |
| `dotnet build` | **0 错误 / 3 警告**（`CS8632` TableSnapshotTool.cs:128、`CS0219` GameScene.cs:6602/6688，均为既有） |
| 全流程 E2E（`e2e-f2`） | 登录 640×480 → 选角 → `StartGame 成功` → `StartGame.ogv` → `F602 公告 len=19` → 点击 ✔ `F602 勾选 -> 进入游戏` → `[Game] 进入游戏! 玩家: TestHero … 地图:8`；进游戏视口 bbox `x0..799 / y0..599` |
| 登录失败+重试（`click1.log`） | 首次错误密码：服务端 `[Wrong Password] IP 127.0.0.1 Account test@test.com`，客户端状态行 + 按钮恢复；第二次 `入队: Login`(136B) → `登录成功, 角色数 1` |
| 创建角色（`cf1.log`） | `phase 0→1(CreateChr.ogv)→2` → 输入 `Tmpflow` 回车 → **服务端 `[Character Created] Character: Tmpflow`** → `phase 3` → 过场 → `phase 0`，`洞窟槽位: 角色数=2`，槽 0/1 动画均启动 |
| 创建屏裁切（`cc2`） | 声明元素 F81/F82/名字框/双预览槽全在 640×480 内；**y473..479 与 F80 参考逐像素差 0.00**（底部亮带是背景美术，不是被切控件）；y460..472 差异 = F81/F82/名字框叠加区 |
| 选角屏裁切 | 所有按钮底边 ≤479（✔/✘ y444..472、结束 y438..464），右边界 x≤639 无越界内容 |
| 数据安全 | 仓库 `Debug/ServerCore/Database/Users.db` md5 **前后均为 `138ac3549426fae0682a93af0afbed2d`**；本次建角只写 `/tmp/ei-flow-srv/Database/Users.db`；未删除任何角色；7000 未被触碰 |
| 工作区 | `git status --short` 空、`git diff --check` 无输出；Mir3-Research 未改动 |

### 截图/日志索引（`/tmp/ei-flow/`）

| 文件 | 内容 |
| --- | --- |
| `f2-1.png` / `f2-1.log` | 登录屏 + F2 标签条实装后 |
| `e2e-f2-1-notice.png` / `e2e-f2-2-ingame.png` / `e2e-f2.log` | F602 公告全宽 + 进游戏 800×600 |
| `cc2-1-select.png` / `cc2-2-create.png` / `cc2.log` | 选角屏 / 创建屏（裁切检查） |
| `cf1-1-typed.png` / `cf1-2-after.png` / `cf1-3-list.png` / `cf1.log` | 建角输入 / 提交后 / 回列表（角色数 2） |
| `click_t1_fail.png` / `click1.log` | 密码错误 → 状态行 + 重试成功 |
| `login-final.png` | 窗口尺寸修复后的登录屏 |
| 原版参照 | `Mir3-Research/docs/research/ei2-research/images/iamcheyan/SCREEN0001.jpg`（205×154，640×480 缩略） |

---

## 8. 与原版的已知差异（如实记录，未掩盖）

1. **视频编码**：原版 Indeo 5.0 AVI（`ei_Login.dat`/`wemade.dat`/`StartGame.dat`/`CreateChr.dat`），Godot 只支持 Ogg Theora，改用 `Tools/convert_legacy_login_video.sh` 转出的 `.ogv`（画面相同、编码不同）。
2. **状态提示行**：原版此屏无常驻状态行；实现保留一条 `DXLabel (8,460) 620×16` 用于登录失败/连接状态（`_skinStatus`），属**有意偏差**。
3. **密码点号与记住账号**：原版由 Windows EDIT 自绘，当前用 `DXTextInput.Secret` + `RememberDetails` 实现，形态接近但非同一套绘制。
4. **开场 logo**：原版是独立 boot 阶段，实现是叠在登录 UI 之上的覆盖层（播完自毁），观感相同、时序不同。
5. **登录屏 phase 1→2→3 状态机**（服务器列表、2000ms 淡出）只做了淡出段，服务器列表子态未还原。
6. **输入法/IME**：原版走 Windows EDIT + `WM_CHAR`，Godot 走 `LineEdit`，中文 IME 行为未验证。

---

## 9. 未验证 / 阻塞 / 冲突（不得据此宣称 1:1）

- `not tested`：删除确认流、断线恢复、名字 >14 拒绝、重名/非法名、服务器拒绝创建、≥3 角色列表、重复点击/取消返回矩阵、非 Legacy（`--zircon-ui`）回归**未做全量回归**（本轮改动全部在 Legacy 分支，但仍属未测）。
- `pending-evidence`：Interface1c F2 的**绘制调用点**（VA/参数）仍缺静态记录，(96,439) 属截图拟合的 `derived` 级；若后续在 `0x404700/0x4182A0` 绘制路径找到该帧的 blit，应以静态值覆盖。
- `unresolved`：`AutoLoginArgs.StayInSelect`（`--stay-select`）是**无引用死标志**，建议删除或接线。
  > **⚠️ 更正（2026-09-30，`EI_LOGIN_CHARACTER_FLOW_REVIEW_2026-09-30.md` §5 F3）**：本条**不成立**。
  > 该标志在**本条报告自身基线 `db85cf8c` 即有 3 处引用**（`SelectScene.cs` L207/L227/L1827；
  > 当前 L217/L237/L2006），且 `--stay-select` 回放实际生效。原文系事实性错误，作废。
- `evidence-conflict`：无（本轮未出现互相矛盾的原版证据；差异均已归入 §8 或 §9）。

---

## 10. commit / push / 远端 SHA 核对

远端：`origin = git@github.com:iamcheyan/Zircon.git`（服务器提示已迁移到 `Zircon-Godot.git`，推送实际有效）。
**每完成一项即提交推送，并用 `git ls-remote origin refs/heads/master` 核对。**

| # | SHA | 说明 | 远端核对 |
| --- | --- | --- | --- |
| 1 | `ed1f241b` | 修复 Legacy 登录表单被居中逻辑二次平移 (80,100) | ✅ |
| 2 | `70f2e81f` | 修复 Legacy 登录视频盖住表单按钮的绘制次序 | ✅ |
| 3 | `ac544429` | 修复 Legacy 登录顶部/底部黑带与底图对齐 | ✅ |
| 4 | `1b979ecb` | Legacy 登录隐藏原版没有的标题文字 | ✅ |
| 5 | `6ba6aa9a` | 修复 Legacy 预游戏窗口尺寸：登录/选角 640×480，进游戏 800×600 | ✅ |
| 6 | `309c8d4f` | 修复选角屏 StartGame 回包处理的三处缺陷 | ✅ |
| 7 | `8b54a809` | 选角屏补断线处理与 Legacy 状态文字可见 | ✅ |
| 8 | `83502a82` | Legacy 登录/注册按钮状态与原生按钮对齐 | ✅ |
| — | `9409f386` | **其他会话/agent 的 docs 提交**（非本 Goal，未触碰） | ✅ |
| 9 | `cf35b08b` | 修复 DXTextArea 正文不显示：入树时同步内部 TextEdit 尺寸 | ✅（与 #10 同批推送，见下） |
| 10 | `7418e3c8` | 修复 Legacy 预游戏窗口只改到 OS 窗口、视口停在旧尺寸 | ✅（与 #9 同批推送：远端被 `9409f386` 更新需先 rebase） |
| 11 | `db85cf8c` | Legacy 登录补回 Interface1c F2 标签条（ID/PASSWORD 输入框贴纸） | ✅ |
| 12 | 见下 | docs: 本报告（`docs/EI_LOGIN_CHARACTER_FLOW_REVIEW_2026-09-29.md`） | ✅ |

**最终状态**：11 项代码修复 + 本报告，代码基线 = `db85cf8ce1960df291603294c227d574c7c0be00`。
本报告自身的提交 SHA 无法内嵌自身，以推送后
`git ls-remote origin refs/heads/master` 的远端 HEAD 为准（推送即核对，报告写作时远端 HEAD = `db85cf8c`，随后更新为报告提交）。
全程未 force push、未切换分支、未提交数据库/日志/截图/凭据。

---

## 11. 后续建议（按性价比排序）

1. 补 §9 的 `not tested` 项，尤其**断线恢复**与**创建失败分支**（需服务端可注入失败）。
2. 在反汇编里定位 F2 的 blit 调用点，把 `(96,439)` 从 `derived` 升级为 `primary`。
3. 删除或接线 `--stay-select` 死标志；补一条自动化「选角停驻」脚本。
4. 用 `ZIRCON_UI_SCALE=2` 复测 640×480 预游戏窗口在 HiDPI 下的表现（本轮仅测 scale=1）。

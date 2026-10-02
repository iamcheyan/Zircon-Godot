# GodotClient HUD 全面复核与修复报告（2026-10-02）

- 工作目录 `/home/tetsuya/development/zircon`，分支 `master`，远端 `origin`。
- 任务目录：`.artifacts/godot-hud-parity-2026-10-02/`（本报告 + `shots/` 真实运行截图 + `EI_HUD_PRIMARY_STATIC.md` 独立反汇编证据）。
- 客户端模式开关（`GodotClient/Scripts/AutoLoginArgs.cs:94,96`）：
  - `LegacyUi = Has("--legacy-ui") || !Has("--zircon-ui")` → **默认即 EI legacy**。
  - `LegacyHud = Has("--legacy-hud") || LegacyUi`。
  - 即：不带参数 = legacy EI HUD；`--zircon-ui` = 现代 Zircon UI。

## 0. 并发会话接管记录

开工时发现**另一个会话 `godot-ui-audit-20261002`（tmux pane PID 4015930，omp --auto-approve）
正在同一 `master` 分支执行同类 HUD 修复**，并在本会话工作期间推送了 4 个提交：

| SHA | 主题 |
|---|---|
| `9b0683fe` | AC/DC 数值落回 F50 黑色值框并去掉文本前缀 |
| `5a12603b` | 主 HUD 聊天输入条去掉不属于 EI 的窗口边框/底色 |
| `1ff758eb` | 去掉 Zircon 扩展 HUD 控件上的窗口金框（buff/任务跟踪/怪物悬停） |
| `0a3094ff` | HUD 视觉/布局/内容来源全面复核报告 |

经用户裁决「接管：让它停，我基于其成果继续」后：
- 已 `tmux kill-session -t godot-ui-audit-20261002` + `kill -TERM/KILL 4015930`；其子 Godot 客户端与 Xvfb :100 随之退出。
- 停止前已归档其未提交改动与屏幕记录：`peer-uncommitted.diff`、`peer-commits.txt`、`peer-tmux-transcript.txt`。
- 其未提交改动含 1 处合法修复（现代 HUD 血/蓝数值字号 12→8）与 1 处调试脚手架（`DumpVisibleChildren` + F12 调用）；脚手架已移除，GameScene 已回到 HEAD 状态。

## 1. 原版基线（primary-static，独立于既有研究 JSON 的取证）

见 `EI_HUD_PRIMARY_STATIC.md`。要点：反汇编 `/tmp/eiwork/Mir3.exe`
（MD5 `264d848da377c2172ffe1444bf31e7d0`）的 `0x00429740-0x0042A838`，
得到主 HUD 全部绘制元素、rect 立即数、格式串与颜色。

## 2. AC/DC（用户点名缺陷）

### 2.1 原版（primary-static）

```
AC 值 rect : SetRect(636, 586, 694, 597)   @0x0042A752-0x0042A76B
DC 值 rect : SetRect(736, 586, 794, 598)   @0x0042A7AA-0x0042A7C3
格式串     : 0x0047BD28 = "%d-%d"   —— 无 "AC"/"DC" 前缀
颜色       : 0x0032C8FF (COLORREF) = RGB(255,200,50) 琥珀金  @0x0042A77D / 0x0042A801
对齐       : 0x45DE50 内 DrawTextA flags=0x25 = DT_SINGLELINE|DT_VCENTER|DT_CENTER
```
"AC"/"DC" 字样是 **F50 底图烘焙美术**（像素实测金色字形 AC x607..620、DC x705..717），
程序只绘制数值。F50 内另有黑色值框实测 AC x635..696、DC x733..795，与 SetRect 立即数一致。

### 2.2 缺陷与根因（实测坐标）

修复前：`ACLabel@(580,108) 88x16`、`DCLabel@(680,108) 88x16`，文本 `"AC 2-25"` / `"DC 25-25"`。
真实 800×600 legacy 截图实测：白色文本 ink 中心 AC `(627,572)`、DC `(715,572)`；
原版目标中心 AC `(665,591.5)`、DC `(765,592)` → **偏左上 41×10.5 / 41×11 px**，
字形压过 F50 金框并盖住上方圆盘装饰。根因：布局常量写错 + 文本多拼了前缀。

### 2.3 修复（本会话批次 1，基于并发会话成果继续）

- `ACLabel` → `(636,121)` 58×11；`DCLabel` → `(736,121)` 58×12（原版 SetRect 转面板相对）。
- 文本仅数值，去掉 `"AC "` / `"DC "` 前缀。
- **新增**：`TextColour = LegacyEiAcDcColour`（RGB 255,200,50）——并发会话未做颜色。
- 审计断言同步到新矩形。

### 2.4 验收（真实运行，非合成）

命令：`godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 --window=800x600 --legacy-ui --user test@test.com --pass test123 --char TestHero`
（DISPLAY=:102，本地 ServerCore 127.0.0.1:7000）

- 日志：`[LegacyHud] PASS viewport=(800, 600) location=(0, 464) ... ac=0-0@(636, 121) dc=0-0@(736, 121)`。
- 截图：`shots/02-ingame-legacy-800x600.png`、`shots/03-ingame-legacy-800x600-viewport.png`。
- 像素实测（viewport 截图，F50 位于 y=464，与日志一致）：

| 元素 | 实测 ink bbox | 中心 x | 原版目标中心 x | 颜色实测 |
|---|---|---|---|---|
| AC 数值 | x653..675, y586..594 | 664.0 | 665.0（Δ1px） | **RGB(255,200,50)** |
| DC 数值 | x750..778, y587..595 | 764.0 | 765.0（Δ1px） | **RGB(255,200,50)** |
| F50 烘焙 AC 字样 | x608..619, y587..592 | — | — | (232,176,112) 美术 |
| F50 烘焙 DC 字样 | x706..717, y586..592 | — | — | (232,176,112) 美术 |

结论：数值落回 F50 黑色值框、位于烘焙字样右侧，位置误差 ≤1px，颜色与原版 `0x32C8FF` 完全一致，
无重复前缀。

## 3. 现代 Zircon UI（`--zircon-ui`）模式

### 3.1 严重缺陷：EI 玩家球在现代模式被拉伸成巨型乱码块（本会话批次 2）

- **现象**：`--zircon-ui` 1024×768 真机截图中，HP/MP 区域出现约 110px 高的巨型
  金色字形块（形似 "DC"），压在血蓝条与数值上（`shots/20-zircon-ui-1024x768.png`）。
- **根因（已证）**：`MainPanel._playerOrb` 是**旧版 EI 专有**控件，位于面板相对
  `(49,13)`、尺寸 `112×110`，构造期即 `Visible`，而现代模式**没有任何代码把它关掉**。
  其 `DrawPlayerOrb` 取 `MirSkin.GetTexture(GameInter, 62)`：legacy 模式解析的是
  EI `GameInter.wil`（F62 = 112×110 完整红球），现代模式解析的是 Zircon 自己的
  `Debug/Client/Data/GameInter.Zl`，其中 **F62 只有 24×12**（属性小图标）——
  于是被拉伸到 112×110，放大成巨型乱码。
  证据：`zlsdk` 读 `GameInter.Zl` → F50(1024,68)、F60(20,12)、F61(32,12)、
  **F62(24,12)**、F63(36,12)；而 `wilsdk` 读 EI `GameInter.wil` → F50(800,136)、
  F60/61(56,110)、**F62(112,110)**。真机诊断该控件 `global=(161,645) size=112x110`。
- **修复**：`_playerOrb` / `_playerOrbHoverArea` 构造期 `Visible=false`，
  由 `ApplyLegacyEiStatsLayout()`（仅 legacy 调用）重新置 true。
  这样现代模式不再绘制任何 EI 专有球体，legacy 行为不变。
- **同批**：HP/MP 数值字号/对齐改为构造期固定（`FontSize=8`、`AutoSize`、
  `Align=Left`、`VAlign=Top`）；此前 `UpdatePlayerOrbNumbers` 的现代分支在运行期
  反复改写 `Align=Center`，与构造期约定冲突，且 `Size.X==0` 时按 0 宽居中会把文字
  推到 `Location` 左侧。
- **验收**：
  - 现代：`shots/23-zircon-ui-1024x768-final.png` —— 巨型乱码块消失，HUD 可读
    （HP/MP/IP 条、CL 道士、LV 255、AC 2-25、DC 25-25、MAC 7-49、SC 45-101）。
  - legacy 回归：`shots/04-legacy-regression-800x600.png` —— 球体正常渲染，
    日志 `[LegacyHud] PASS ... orb=(49,13)/(112,110) visible=True`。
- 修复前证据图：`shots/20-zircon-ui-1024x768.png`；viewport 原图 `shots/21-...`。

### 3.2 现代模式血/蓝数值标签水平重叠（残余，未改）

`HealthLabel`/`ManaLabel` 按 `HealthBar(43×70)`/`ManaBar(42×70)` 居中，
但 "8950/8950"（54px）宽于 43px 的条，导致两个标签互相覆盖
（`shots/23-zircon-ui-1024x768-final.png` 中 `8950/8950` 与 `3680/3680` 叠在一起）。

- 这是**现代 Zircon UI 自有的布局问题**：EI legacy HUD 不用这两个标签（改用球体悬停
  黄色提示），因此**没有原版对照坐标**，不属于本目标的 EI 保真范围。
- 按目标要求「产品差异不能擅自定案」，本项**记录为残余**，未改；
  若需修复应另行决定现代 HUD 的血蓝条宽度/文字方案。

## 4. 窗口尺寸结论

- **legacy EI 模式固定 800×600**：`GameScene.cs:1075` 在 `AutoLoginArgs.LegacyUi` 时
  调 `ClientSettings.ApplyLegacyPregameWindow(800, 600)`，实测 `--window=1024x768`
  仍以 800×600 开窗（日志 `[LegacyHud] PASS viewport=(800, 600)`），
  AC/DC 与 800×600 结果完全一致（F50 y=464、AC cx=664、DC cx=764）。
  这与原版 EI 3.0 固定 800×600 一致，不是缺陷。
- **现代模式**在 1024×768 实测（本报告 §3）。
- 截图：`shots/11-ingame-legacy-1024x768.png`（legacy 请求 1024 仍为 800×600）、
  `shots/23-zircon-ui-1024x768-final.png`（现代）。

## 5. 残余项

- 现代模式血/蓝数值标签重叠（§3.2）：无 EI 对照，记录未改。
- 经验条未达成比例的视觉：受存档等级 255 限制，未闭环。
- 现代模式其它 Zircon 专有元素（技能条、buff 条、任务跟踪）的逐项视觉复核：待续。

## 6. 与并发会话报告的交叉引用

并发会话（`godot-ui-audit-20261002`，会话 id `01a0f9a5`）在 `.artifacts/hud-audit-2026-10-02/HUD_REPORT.md`
§4 列出「现代 HUD 血/蓝数值条文本字号」为**已定位、未修复**（`CreateBarLabel` 未设
`FontSize`，走 `DXLabel` 默认 12；原版继承 `CEnvir.FontSize(8F)`）。

本会话批次 2（commit `f7d8481a`）已修复该项：

- 构造期对 `HealthLabel`/`ManaLabel` 设 `FontSize = 8`（不再依赖运行期分支）；
- 同时修正 `Align`（`Left` 而非默认 `Center`，避免 `AutoSize` 下 `Size.X==0` 时文字被推到左侧）；
- 并修复同批发现的更严重缺陷：EI 玩家球在现代模式被拉伸成巨型乱码块（§3.1）。

该行在本会话报告中标记为**已被 `f7d8481a` 取代**；为避免与另一 goal 的产物互相覆盖，
未回改对方报告文件。

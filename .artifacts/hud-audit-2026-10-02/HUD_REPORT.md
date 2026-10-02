# Godot 客户端 HUD 视觉/布局/内容来源复核（2026-10-02）

> 前置：本文件是「HUD 交互审计」之后的**视觉/内容一致性**复核，不能拿
> `.artifacts/godot-ui-audit-2026-10-02/REPORT.md` 的「6 个交互缺陷已修复」当作
> HUD 视觉验收。
>
> 运行环境：`godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000
> --window=800x600 --user <测试账号> --char <测试角色>`，legacy EI 界面
> （`--legacy-hud` 默认开），逻辑画布 800×600 = 窗口像素（scale=1）。
> 所有坐标都是**逻辑画布坐标**。

## 0. 判定基线（先说清「原版是什么」）

| 依据 | 内容 |
|---|---|
| EI 主 HUD 帧 | `GameInter.wil` **F50**，800×136，屏幕 `(0,465)-(800,600)`（`primary-main-hud-setrect.md`：`SetRect(0, 601-height, width, 600)`） |
| 主 HUD 初始化 SetRect 全表 | `setrect_calls.json` 中 `0x427600-0x427A00` 共 7 条：主背板、聊天区(224,492,578,566)、16 行文本数组、血球区(61,496,104,566)、魔球区(105,496,147,566)、地图名区(235,586,400,597)、小状态区(206,499,215,574) |
| HUD 文本格式串全表 | `Mir3.exe` `.data` 内与 HUD 相关的**只有**：`(血量)%d/%d`、`(魔法)%d/%d`、`(经验条)%.2f%s`、`(负重)%d/%d`、`%s : [%d,%d]`、`:%d / `、`: %d`、`%d %d/%d`；**不存在 AC/DC 格式串** |
| 16 个 caption 控件 | `hud-label-evidence.json::caption_ctor_table`：帧号 + hud 相对 x/y + 文案 + 文本 VA |
| HUD 条绘制序 | `hud-bars-render-evidence.json`：动态帧 0x82-0x85 → F62 → F60 → F61 → F63 → 经验%文本；血/魔球 SetRect 与 HP/MP 数值注入链 `-` |
| 等级文本 | `hud-status-bars-level-paint-evidence.json`：`0x42A26A` 等级文本 + 格式串 `0x47BD40`，居中锚 `0x216/0xCD` |

**关键结论（本文档全部判断的基础）**：EI 主 HUD 的**静态装饰 + AC/DC 字样**
都是 F50 的**烘焙像素**；`Mir3.exe` 只在其上绘制**动态数值**（HP/MP/XP 文本、
等级数字、AC/DC 数字、地图名）与 16 个 caption 按钮。

## 1. HUD 元素全量清单（来源 / 所属模式 / 原版对应 / 位置）

「来源」列区分 **EI 原生**（原版就有）与 **Zircon 扩展**（原版没有）。

| # | 元素 | 来源 | EI 原版对应 | legacy 位置 | 现代 Zircon 位置 | 备注 |
|---|---|---|---|---|---|---|
| 1 | `MainPanel` F50 底板 | EI 原生 | F50 800×136 | `(0,465)` 800×136 | 同（`LegacyHudLayout.MainPanelY`） | 逻辑根 |
| 2 | 血球/魔球（F60/61/62） | EI 原生 | SetRect 血球 `(61,496,104,566)`、魔球 `(105,496,147,566)` | `_playerOrb (49,13)/(112,110)`（面板相对） | 同 | `--legacy-audit orb=` 全绿 |
| 3 | HP/MP 数值文本 | EI 原生 | 悬停时在球下方 | `(49,123)/(105,123)`（hover 才显示） | 现代条内居中 | 见 `orbDetails valuePositions` |
| 4 | 经验条（F63） | EI 原生 | 164×10 凹槽 | `(235,122)` 164×10 | 同 | `DrawExperienceFill` 按比例裁切 |
| 5 | 背包负重条（F67） | EI 原生 | 4×70 竖条 | 仅 legacy 显示 | 隐藏 | `WeightBar.Visible=true`（legacy） |
| 6 | **AC 数值** | EI 原生（数字） | F50 烘焙的 AC 金框 + 黑值框 `x636..696`，行带 `y118..130` | **`(636,118)` 61×13**（本次修复） | 现代九格属性栏 `(470,22)` | 见 §2 ISSUE-A |
| 7 | **DC 数值** | EI 原生（数字） | F50 DC 金框 + 黑值框 `x734..795`，行带 `y118..130` | **`(734,118)` 62×13**（本次修复） | 现代 `(470,42)` | 同上 |
| 8 | 等级数字 | EI 原生 | `0x42A26A`，右侧圆盘中心 | `(665,60)` 70×16 | 现代 `(300,42)` | 位置正确（真机目视吻合） |
| 9 | 职业/FP/CP/MR/MC/SC 文本 | Zircon 现代属性栏 | EI legacy HUD **无** | 全部 `Visible=false` | 显示 | `ApplyLegacyEiStatsLayout` 已关 |
| 10 | 属性图标列（F62-73） | Zircon 现代属性栏 | EI legacy HUD **无** | 全部 `Visible=false` | 显示 | 同上 |
| 11 | 小地图（128×128） | EI 原生 | minimap rect `{672,0,800,128}` | `(672,0)` 128×128 | 同 | `DrawChrome=false`（已正确） |
| 12 | 腰带（6 格） | EI 原生 | `(393,13)` 入口 + 6 槽 | `(552,418)` 248×46 | 同 | `DrawChrome=false`（已正确） |
| 13 | 技能条（12 槽） | EI 原生 | `B`/cap2 翻转 flag（`0x42C241`） | `B` 键切换（默认随 legacy 隐藏） | 常驻 | 裸 `Control`，无 chrome |
| 14 | 聊天历史区 | EI 原生 | `(224,492,578,566)` → 面板相对 `(224,27)` 354×74 | `(224,27)` 354×74 | 同 | `LegacyHudLayout.ChatLog*` |
| 15 | 聊天输入条 | EI 原生 | F50 内暗凹槽，面板相对 `(223,105)` 354×16 | `(223,105)` 354×16 | 同 | 见 §2 ISSUE-B |
| 16 | 主菜单按钮列（cap0..15） | EI 原生 | 16 caption 帧/坐标/文案全表 | 16 个 `DXButton`，坐标取自 `caption_ctor_table` | 现代九键 | `ApplyLegacyEiHudCaptions` |
| 17 | buff 图标条 | **Zircon 扩展** | EI 原版 HUD **无** buff 条 | 小地图左侧，30×30 起 | 同 | 见 §2 ISSUE-C |
| 18 | 任务跟踪条 | **Zircon 扩展** | EI 原版 **无** 常驻任务条 | 可开关 | 同 | 见 §2 ISSUE-C |
| 19 | 怪物悬停信息框 | **Zircon 扩展** | EI 原版只画 HP 条 + 名字 | 跟随鼠标 | 同 | 见 §2 ISSUE-C |
| 20 | 血/蓝数值条文本（现代） | Zircon 现代 | EI 用球体 | 隐藏（走球体） | 条内居中 | `CenterBarLabel` |
| 21 | 攻击模式/宠物模式标签 | Zircon 现代 | EI legacy HUD **无** | `Visible=false` | 显示 | — |

## 2. 已确认并修复的问题

### ISSUE-A 右下角 AC/DC 文本错位（用户举例）— 已修复，提交 `9b0683fe`

- **现象**：AC/DC 数字整体偏左上约 40px，字形压过 F50 的金框并盖到圆盘装饰上；
  文本还带 `"AC "` / `"DC "` 前缀，与 EI 的烘焙字样重复。
- **修复前**：`ACLabel@(580,108)` 88×16、`DCLabel@(680,108)` 88×16，文本 `"AC 2-25"`。
- **证据**：
  1. F50 像素实测：`y 119..130` 有两个 12/12 行纯黑矩形 —— AC `x 636..696`、
     DC `x 734..795`；同带金色标签字形 AC `x 607..620`、DC `x 705..717`。
  2. `Mir3.exe` 全二进制**无** `"AC"`/`"DC"` 格式串（独立 ASCII 命中全在 `.text`
     指令字节里，属误报）→ 字样是烘焙美术，原版只填数字。
  3. 旧 `Client/Scenes/GameScene.cs:4136/4139` 赋的也只是
     `Stats.GetFormat(Stat.MaxAC/DC)`，不含前缀。
  4. 旧 `Client/Scenes/Views/MainPanel.cs` 的 `(470,20)/(470,40)` 是**现代九格
     属性栏**版本，legacy HUD 有自己的布局覆盖，**不能照搬**。
- **修复**：→ `(636,118)` 61×13 / `(734,118)` 62×13，只填数值。
- **验证**：真机截图 `evidence/40-hud-acdc-before.png` → `41-hud-acdc-after.png`；
  `--legacy-audit` 新增断言「位置 == (636,118)/(734,118) 且文本不以 AC/DC 开头」。

### ISSUE-B 主 HUD 聊天输入条带 Zircon 窗口金框 — 已修复，提交 `5a12603b`

- **现象**：legacy HUD 输入条上下各多一条亮线 `RGB(47,40,24)` 与内圈暗线
  `(14,11,6)`，呈「描金框」；F50 同位置是纯色深凹槽（0/8/16 级暗色）。
- **根因**：`ChatTextBox` 是 `DXWindow`，`DrawChrome` 默认 true →
  `DXWindow._Draw` 调 `DrawWindowChrome`（`DXWindow.cs:160-161/170-224`）画
  Zircon 窗口底色 + `Interface[0/2/1/11/12/25/26]` 金框。legacy 布局此前只设了
  `Border=false`（那是 `DXTextInput` 自己的描边，`DXControl.cs:195`）与透明底色。
- **修复**：`ApplyLegacyHudLayout` 内 `DrawChrome=false`，并补
  `_input.Border=false`、`_input.BackColour=Transparent`
  （避免 `FocusChanged` 聚焦时切成黑块，`ChatTextBox.cs:75`）。
- **验证**：真机逐行实测输入条带 `y 569..584` 的 `max` 从 47 降到 ≤56（金线消失）；
  `evidence/43-hud-chat-input-before.png` → `44-hud-chat-input-after.png`。

### ISSUE-C Zircon 扩展 HUD 控件带窗口金框 — 已修复，提交 `1ff758eb`

- **现象**：buff 图标条外围一圈 Zircon 金框，在 EI HUD 上非常显眼。
- **根因**：`BuffDialog` / `QuestTrackerDialog` / `MonsterDialog` 都是 **Zircon 扩展**
  （EI 原版 HUD 无这些控件），却都保留了 `DXWindow.DrawChrome` 默认 true。
- **修复**：三者构造期显式 `DrawChrome = false`。
- **验证**：真机 buff 条金框消失，只剩图标自身边框；
  `evidence/45-hud-buff-frame-before.png` → `46-hud-buff-frame-after.png`。
- **对照（本来就正确的）**：`MiniMapDialog.cs:83`、`BeltDialog.cs:132` 早已
  `DrawChrome=false`；`MagicBar` 是裸 `Control` 无 chrome；`MainPanel` 用自绘 F50。

## 3. 已核查、判定**不是缺陷**的项目

| 项目 | 核查 | 判定 |
|---|---|---|
| 等级数字 `(665,60)` | 真机目视落在右侧圆盘中心，与 F50 装饰吻合 | 正确 |
| 职业/FP/CP/MR/MC/SC 文本 + 属性图标列 | legacy 下全部 `Visible=false`（`ApplyLegacyEiStatsLayout`） | 正确（EI 无此栏） |
| 血/蓝球 `(49,13)/(112,110)` | 与 `hud-bars-render-evidence.json` 的 F60/61/62 语义一致 | 正确 |
| 经验条 `(235,122)` 164×10 | 按比例裁切绘制，非整帧拉伸 | 正确 |
| 16 个 caption 按钮 | 坐标逐项取自 `caption_ctor_table`（`--legacy-audit buttons=True`） | 正确 |
| 小地图/腰带 | 已有 `DrawChrome=false` | 正确 |
| 技能条默认隐藏 | `_magicBar.Visible = !AutoLoginArgs.LegacyHud` → legacy 默认隐藏，`B` 可切 | 正确 |

## 4. 未确认 / 需用户裁决

| 项目 | 状态 | 说明 |
|---|---|---|
| 经验条未达成比例的视觉 | 未闭环 | 需要角色经验从 0 变化才能量到；当前存档等级 255，增益极慢 |
| buff / 任务跟踪 / 怪物框的**有内容**状态 | 部分 | buff 有内容已截图；任务跟踪需接任务、怪物框需悬停怪物 |
| 现代 Zircon UI（`--zircon-ui`）HUD | 未覆盖 | 本轮只审 legacy EI HUD；现代 HUD 是另一套控件，需单独一轮 |
| `MainPanel.DrawChrome` 保持 true | 记录 | F50 本身即整块底图、且它是 `DXImageControl` 非 `DXWindow`，实测无金框；未改动 |

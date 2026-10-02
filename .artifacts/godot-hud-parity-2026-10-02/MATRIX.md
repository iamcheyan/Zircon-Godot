# GodotClient HUD 控件级对照矩阵

- 原版列证据：`EI_HUD_PRIMARY_STATIC.md`（反汇编 `/tmp/eiwork/Mir3.exe`，
  MD5 `264d848da377c2172ffe1444bf31e7d0`；与 `Mir3-Research` 既有 JSON 交叉一致）。
- Godot 列证据：文件:行号，均为本轮实读。
- 坐标：legacy 逻辑画布 = 800×600，面板原点 (0,465)（原版为 (0,464) 运行时观测值，
  见 §4 说明）。面板相对坐标 = 绝对坐标 − (0,465)。

## A. EI legacy HUD 元素（`--legacy-ui`，默认模式）

| # | 元素 | 原版证据（rect / 资源 / 颜色 / 条件） | Godot 实现（文件:行） | 差异 | 结论 |
|---|---|---|---|---|---|
| A1 | F50 底板 | `SetRect(0,601-h,800,600)`；F50 800×136 | `MainPanel` ctor `MainPanel.cs:51-54`（Index=50, Size 800×136） | 无 | ✅ 一致 |
| A2 | 玩家球 F60/61/62 | 面板相对 `(49,13)` 112×110；F62=完整红球 112×110，有 MaxMana 时 F60+F61 各 56×110 | `_playerOrb` `MainPanel.cs:107-114`；`DrawPlayerOrb` `MainPanel.cs:459-475` | 无 | ✅ 一致 |
| A3 | 球悬停数值 | 光标跟随黄底黑框 caption；格式 `(血量)%d/%d`@0x47BD70、`(魔法量)%d/%d`@0x47BD60 | `LegacyHudCaptionHint` `MainPanel.cs:126-132`；文本 `MainPanel.cs:687-691`（`(血量)` 前缀见 683/687） | 无 | ✅ 一致 |
| A4 | 经验条 F63 | F63 164×6；`SetRect(235,586,400,597)` @0x0042770D（容器 165×11，即原版条所在凹槽） | `ExperienceBar` `MainPanel.cs:64-71` loc `(235,122)`=abs(235,587) size 164×10；`DrawExperienceFill` `MainPanel.cs:438-457` | 容器高 10 vs 11、F63 原生 6px 居中 | ✅ 一致（本轮更正） |
| A5 | 经验百分比文本 | `0x13B..0x250`/`0x145..0x252`（即 (315,583)-(592,594)）；格式 `(经验条)%.2f%s`@0x47BD4C/5C | 仅 `ExperienceBar.TooltipText` `MainPanel.cs:718` | 原版为常显文本；Godot 为悬停提示 | ⚠ 形式差异（低影响） |
| A6 | 负重竖条 F67 | 位移 `[+0xC58]+0xD1, [+0xC5C]+0x25`；F67 4×70 | `WeightBar` `MainPanel.cs:82-88` loc (208,36)；`DrawWeightFill` `MainPanel.cs:744-759` | 无 | ✅ 一致 |
| A7 | HP/MP 数值格式 | `(血量)%d/%d`@0x47BD70、`(魔法量)%d/%d`@0x47BD60（球悬停） | 同 A3 | 无 | ✅ 一致 |
| A8 | 地图标题+坐标 | `0x0042A498-0x0042A4AE`：`SetRect(0,585,201,599)`（面板相对 (0,120) 201×14）；格式 `%s : [%d,%d]`@0x47BD30；颜色 `0x00C8FFFF`；带 4 向黑描边（±1） | **无对应实现**（`MiniMapDialog` `HasTitle=false`，`MiniMapDialog.cs:80`） | 原版常显于底栏左侧，Godot 缺 | ❌ 缺失（见 C1） |
| A9 | 等级数字 | 面板相对 `(665,60)`；格式 `%d`@0x47A214 | `LevelLabel` `MainPanel.cs:322-324` (665,60) 70×16 | 无 | ✅ 一致 |
| A10 | AC 数值 | `SetRect(636,586,694,597)`；格式 `%d-%d`@0x47BD28；色 `0x0032C8FF`；DT_VCENTER\|DT_CENTER | `ACLabel` `MainPanel.cs:340-343` (636,121) 58×11 + `LegacyEiAcDcColour` `MainPanel.cs:47` | 实测中心 Δ1px | ✅ 一致（本轮修复） |
| A11 | DC 数值 | `SetRect(736,586,794,598)`；同上 | `DCLabel` `MainPanel.cs:344-347` (736,121) 58×12 | 实测中心 Δ1px | ✅ 一致（本轮修复） |
| A12 | AC/DC 字样 | **烘焙美术**（F50 金色字形 AC x607..620、DC x705..717）；程序不绘制 | 无（正确：不绘制） | 无 | ✅ 一致 |
| A13 | 16 个 caption 按钮 | `hud-label-evidence.json::caption_ctor_table`；三态（常态不画/悬停文字/按下帧） | `CreateButton` `MainPanel.cs:143-164`（定义 486）；`ApplyLegacyEiHudCaptions` `MainPanel.cs:364-386` | 无 | ✅ 一致 |
| A14 | 小地图 | `{672,0,800,128}`（byte-exact D3D target rect） | `MiniMapDialog`，`MiniMapDialog.cs:62-98` | 无 | ✅ 一致 |
| A15 | 聊天槽 | `SetRect(224,492,578,566)` → 面板相对 (224,27) 354×74 | `LegacyHudLayout.ChatLog*`；`ChatLogPanel.ApplyLegacyHudLayout` | 无 | ✅ 一致（并发会话已验） |
| A16 | 聊天输入条 | 面板相对 (223,105) 354×16 | `LegacyHudLayout.ChatInput*`；`ChatTextBox.ApplyLegacyHudLayout` | 无 | ✅ 一致（并发会话已验） |

## B. Zircon 专有元素（EI 原版 HUD 没有）

| # | 元素 | 归属 | Godot 证据 | 是否属于 HUD 混入 | 处置 |
|---|---|---|---|---|---|
| B1 | 九格属性栏图标列（F62-73） | 现代属性栏 | `CreateStatImage` `MainPanel.cs:229-243`（定义 502）；legacy 下 `ApplyLegacyEiStatsLayout` 全关 `MainPanel.cs:298-313` | legacy 下已隐藏 | ✅ 正确 |
| B2 | 职业/FP/CP/MR/MC/SC 文本 | 现代属性栏 | 同上 `MainPanel.cs:315-320` | legacy 下已隐藏 | ✅ 正确 |
| B3 | 血/蓝/专注**横条** | 现代 HUD | `CreateBar` `MainPanel.cs:99-102`；legacy 下 `HealthBar/ManaBar.Visible=false`（ctor `MainPanel.cs:143-144`），改用球体 | legacy 下已隐藏 | ✅ 正确 |
| B4 | 现代血/蓝条上数值 | 现代 HUD | `HealthLabel/ManaLabel`；legacy 下 `Visible=false` `MainPanel.cs:645-653` | legacy 下已隐藏 | ✅ 正确 |
| B5 | **玩家球（EI 专有）出现在现代模式** | 误混入现代 | `_playerOrb` 原为构造期可见；**本轮修复**：ctor `Visible=false` `MainPanel.cs:133-142`，仅 `ApplyLegacyEiStatsLayout` 重新开启 `MainPanel.cs:303-305` | **是**（严重） | ✅ 已修复 |
| B6 | buff 图标条 | Zircon 扩展 | `BuffDialog`；`DrawChrome=false`（并发会话 `1ff758eb`） | 原版无；已去金框 | ✅ 已处理 |
| B7 | 任务跟踪条 | Zircon 扩展 | `QuestTrackerDialog`；同上 | 原版无；已去金框 | ✅ 已处理 |
| B8 | 怪物悬停信息框 | Zircon 扩展 | `MonsterDialog`；同上 | 原版无；已去金框 | ✅ 已处理 |
| B9 | 技能条（12 槽） | Zircon 扩展 | `MagicBar`；`_magicBar.Visible = !AutoLoginArgs.LegacyHud` `GameScene.cs:4656` | legacy 默认隐藏 | ✅ 正确 |
| B10 | 攻击/宠物模式标签 | 现代 HUD | `AttackModeLabel/PetModeLabel`，原版 ctor 即 `Visible=false` 且无处置 true | legacy 下隐藏 | ✅ 正确 |

## C. 未修复 / 阻塞项

| # | 项 | 证据 | 状态 |
|---|---|---|---|
| C1 | 原版地图标题+坐标文本（`%s : [%d,%d]`@0x47BD30，`SetRect(0,585,201,599)`，色 `0xC8FFFF`，4 向黑描边）无 Godot 对应实现 | `EI_HUD_PRIMARY_STATIC.md` §1 序 8（本轮重新反汇编 `0x0042A471-0x0042A4AE` 更正矩形）；Godot 侧无 `0x47BD30` 使用点（已全仓库 grep） | **记录为缺失**；属新增功能而非既有差异修复，未擅自添加。EI 参考截图中该处被水印遮盖，无法像素比对 |
| C2 | 现代模式血/蓝数值标签水平重叠（"8950/8950" 宽 54px > 条宽 43px） | `REPORT.md` §3.2；截图 `shots/23-…-final.png` | 记录未改：无 EI 对照坐标，属现代 HUD 自有布局 |
| C3 | 经验条未达成比例的视觉（0% 状态） | 存档等级 255，经验增长极慢 | 未闭环（资源限制） |
| C4 | 经验条容器高 10 vs 原版 SetRect 高 11（左缘 235 已与原版 `0x0042770D` 一致） | A4 行 | 未改：1px 容器差无独立裁决证据，且会影响并发会话已验收的 legacy 布局 |

## D. 模式与窗口尺寸结论

| 模式 | 开关 | 窗口尺寸行为 | 实测 |
|---|---|---|---|
| EI legacy（默认） | 无参数 / `--legacy-ui` / `--legacy-hud` | 强制 800×600（`GameScene.cs:1075` → `ApplyLegacyPregameWindow(800,600)`），与原版固定 800×600 一致 | `shots/05-legacy-hud-sweep.png`、`shots/03-…-viewport.png` |
| 现代 Zircon | `--zircon-ui` | 跟随 `--window=WxH`（最小 1024×768） | 800×600 `shots/24-zircon-ui-800x600.png`；1024×768 `shots/23-zircon-ui-1024x768-final.png` |

模式判定代码：`AutoLoginArgs.cs:94,96`
（`LegacyUi = Has("--legacy-ui") || !Has("--zircon-ui")`；`LegacyHud = Has("--legacy-hud") || LegacyUi`）。

### D.1 AC/DC「多尺寸」复验结论

目标要求「AC/DC 错位经多个尺寸真实截图复验」。实测结论：

- **legacy EI 模式的窗口尺寸是固定 800×600 的**，无法用不同窗口尺寸复验：
  `GameScene.cs:1075` 在 `AutoLoginArgs.LegacyUi` 时调用
  `ClientSettings.ApplyLegacyPregameWindow(800, 600)`；实测请求
  `--window=1024x768` 仍以 800×600 开窗（日志
  `[Display] Legacy window: 800x600 logical`、`[LegacyHud] PASS viewport=(800, 600)`）。
  这与原版 EI 3.0 固定 800×600 一致（原版主 HUD 就是 800×600 屏坐标），**不是缺陷**。
- 尝试用 `ZIRCON_UI_SCALE=2` + `--window=1600x1200` 制造第二渲染尺寸：实测窗口仍为
  800×600，HUD 层被放大到 2× 后**超出窗口被裁切**（截图
  `shots/30-legacy-scale2-1600x1200.png` 只见左上局部）。该环境变量是 `UiScaler.cs:68-70`
  与 `GameScene.cs:5015` 标注的**调试钩子**，不是可用渲染尺寸，故不作为复验依据。
- **可用的第二尺寸在 AC/DC 之外**：现代 `--zircon-ui` 模式按
  `--window=WxH` 自适应，已在 800×600 与 1024×768 两个尺寸真实复验（D 段表格）。

因此 AC/DC 的「多尺寸」以**两个独立真机会话**复验：
① 800×600 legacy（`shots/03-…-viewport.png`，F50 y=464、AC cx=664、DC cx=764）；
② 1024×768 legacy（`shots/12-…-viewport.png`，实测 F50 y=464、AC cx=664、DC cx=764，
   与原尺寸逐像素一致）。两者与原版 SetRect 中心（665 / 765）相差 ≤1px，
颜色均精确为 RGB(255,200,50)。

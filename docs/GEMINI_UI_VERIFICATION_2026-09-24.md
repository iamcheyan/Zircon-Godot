# Gemini UI 运行时验证报告
**日期**：2026-09-24  
**分支**：ui/legacy-layout-lab  
**验证人**：Antigravity (Claude Sonnet 4.6 Thinking) — 会话 55379e17  
**前序会话**：feea0c10-0c6f-447f-9910-925eeea1841f（生成了 00/01/02 截图，但未完成记录）

---

## 任务前状态

### git status（任务开始时）
```
On branch ui/legacy-layout-lab
Your branch is up to date with 'origin/ui/legacy-layout-lab'.

Untracked files:
    docs/GEMINI_UI_VERIFICATION_2026-09-24.md
    docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md
    screenshots/gemini-ui-verify-00-baseline.png
    screenshots/gemini-ui-verify-01-hud-baseline.png
    screenshots/gemini-ui-verify-02-inventory.png
```
HEAD commit：`48aa688d` — 更新旧版UI审计阻塞清单

### 运行环境
- 操作系统：NixOS，Wayland（labwc compositor）
- 显示器：1280×1024（但游戏窗口 1024×768）
- 已运行游戏客户端：PID 1103974，参数 `--legacy-ui --legacy-hud`，在 Wayland
- 测试账号：test@test.com，角色：TestHero，已登录地图：沙巴克城（Map 7）
- 截图工具：`grim`（Wayland），`WAYLAND_DISPLAY=/run/user/1000/wayland-0 grim <output>`
- 鼠标输入：`ydotool`（通过 `/tmp/ydaemon.sock`，daemon PID 1137308）
- 游戏窗口尺寸：1024×768（从截图文件头确认：PNG 1024×768）
- MainPanel（底部 HUD）位置推算：
  - 面板逻辑尺寸 800×136（LegacyHudLayout.LogicalWidth=800, 高度=136）
  - 在 1024px 视口中：panelLeft = (1024-800)/2 = **112**，panelTop = 768-136 = **632**
  - 按钮屏幕坐标 = (112 + 按钮本地X, 632 + 按钮本地Y)

### 关键 HUD 按钮屏幕坐标（1024×768 像素）
| 按钮 | 本地位置 | 屏幕坐标 | 对应 EI cap |
|------|----------|----------|------------|
| InventoryButton (包袱栏) | (648, 32) | **(760, 664)** | cap14 |
| CharacterButton (坐骑/状态) | (648, 70) | **(760, 702)** | cap13 |
| SpellButton (技能书) | (703, 16) | **(815, 648)** | cap8 |
| MenuButton (设置) | (703, 85) | **(815, 717)** | cap11 |
| PartyButton (组队) | (616, 47) | **(728, 679)** | cap5 |
| GuildButton (行会) | (616, 82) | **(728, 714)** | cap6 |
| QuestButton (任务) | (718, 70) | **(830, 702)** | cap10 |
| BeltButton (腰带) | (393, 2) | **(505, 634)** | cap7 |

---

## 验证队列

根据 `docs/LEGACY_EI_UI_AUDIT_2026-09-23.md` 和 `docs/LEGACY_HUD_LAYOUT_LAB.md`，提取具有运行时路径且未被明确标记为"阻断"的条目：

| 编号 | 窗口/功能 | 入口 | 审计状态 | 优先级 | 结果 |
|------|----------|------|---------|--------|------|
| Q-01 | 基础 HUD 主界面（游戏内初始状态） | 进入游戏即可见 | 运行截图存在但未系统验收 | 高 | → V-00 |
| Q-01b | HUD 特写/细节验证 | 同上 | 同上 | 高 | → V-01 |
| Q-02 | 背包/物品栏（InventoryDialog） | HUD InventoryButton (760,664) | 入口已实现，未验收 | 高 | → V-02, V-03 |
| Q-03 | 人物/状态窗口（CharacterDialog） | HUD CharacterButton (760,702) | CHAR-01..04 部分修复 | 高 | → V-04 |
| Q-04 | 技能书（MagicDialog） | HUD SpellButton (815,648) | SKL 系列多项差异 | 高 | → V-05 |
| Q-05 | 设置窗口（ConfigDialog） | HUD MenuButton (815,717) | SET 系列有修复记录 | 中 | → V-06 |
| Q-06 | 组队窗口（GroupDialog） | HUD PartyButton (728,679) | GROUP-01..05 静态已修复部分 | 高 | → V-07 |
| Q-07 | 坐骑窗口（HorseDialog） | HUD CharacterButton (760,702) | HRS-01..03 锚点修复 | 高 | → V-08 |
| Q-08 | 行会窗口（GuildDialog） | HUD GuildButton (728,714) | GUILD-01..03 | 高 | → V-09 |
| Q-09 | 任务窗口（QuestDialog） | HUD QuestButton (830,702) | QUEST 系列 | 中 | → V-10 |
| Q-10 | 腰带窗口（BeltDialog） | HUD BeltButton (505,634) | Belt 系列 | 中 | → V-11 |

> **阻断项（不在队列中）**：EXIT-01（退出），MODAL-01（模态），NOTICE-01，CHAT-02，SHOP-01，WH-01/02，SKL-01/02（阻断），INV-01（阻断），GUILD-02（阻断），NPC-02（阻断），TRADE-01（需双人），小地图（MAP-01 明确阻断）

---

## 参考：EI 原版证据图
主要来源：`docs/evidence/legacy-ei-ui/`  
已有截图：`screenshots/01_status_character.png`、`02_inventory.png`、`13_character_panel.png`、`14_magic_panel_chinese.png`、`17_config_chinese.png` 等

---

## 验证记录

---

### V-00 — Q-01 基础 HUD 主界面（baseline）

**截图**：`screenshots/gemini-ui-verify-00-baseline.png`  
**截取时间**：2026-09-24 ~15:11（前序会话）  
**入口**：游戏启动后的默认状态，角色 TestHero 在沙巴克城  
**参数**：`--legacy-ui --legacy-hud`

**期望行为**（根据审计文档）：
- 底部 HUD：旧版 GameInter[50] 底图（石像守卫两侧），红/蓝球显示 HP/MP
- 右侧圆形功能按钮组（EI 原版 cap8..15 配置）
- 右上角小地图
- 上方技能快捷栏（MagicBar）
- 聊天区域在 HUD 上方
- 无多余的现代 UI 元素（除非明确保留）

**实际结果（视觉检查）**：
- ✅ 底部 HUD 可见：旧版石像守卫底图，红/蓝球显示 HP 3606/3606，MP 741/1741
- ✅ 右侧圆形功能按钮组可见，约 8 个图标
- ✅ 右上角小地图显示"沙巴克城"
- ✅ 上方技能快捷栏（1-12 槽 + 11,12 两个可见的技能）
- ⚠️ 技能快捷栏使用线框（无 GameInter2 旧版资源，fallback 到矩形框）
- ⚠️ 等级徽章图标（70级圆形）使用了现代/环形显示，非旧版数字图标风格
- ℹ️ 聊天区（左下）黑色背景条可见但内容区为空
- ℹ️ 左侧石像底图正确，显示 HP/MP 数字
- 游戏地图：沙巴克城（草地，城墙）正常渲染

**判定**：`ISSUE`（次要）  
**问题**：
1. MagicBar 快捷栏资源回退为线框（已知问题 SKL-09）
2. 等级徽章显示方式待与 EI 原版对比

**代码问题疑虑**：MagicBar 使用 GameInter2 资源，EI 目录下无该文件（已知 SKL-09）

---

### V-01 — Q-01b HUD 基线复现确认

**截图**：`screenshots/gemini-ui-verify-01-hud-baseline.png`  
**截取时间**：2026-09-24 ~15:13（前序会话）  
**入口**：同 V-00，无操作

**实际结果（视觉检查）**：
- 截图与 V-00 完全相同（像素级相同内容）
- 游戏状态未变化，这是同一帧或极近帧的重复截图
- 无额外信息可提取

**判定**：`PASS`（与 V-00 相同，作为冗余基线存档）  
**备注**：前序会话生成了两张相同截图，此截图独立存档。无代码问题。

---

### V-02 — Q-02 背包窗口打开尝试（前序会话）

**截图**：`screenshots/gemini-ui-verify-02-inventory.png`  
**截取时间**：2026-09-24 ~15:14（前序会话）  
**入口**：前序会话声称点击 HUD InventoryButton，但使用 ydotool（套接字断线）

**期望行为**：
- InventoryDialog 在右上角（_inventoryDialog.Location = (vp.X - inventoryDialog.Width, miniMap.Height)）打开
- 旧版 GameInter[250] 背包底图，46 格物品栏，装备栏（头/身/腿/手/腰带）
- 角色 TestHero 的当前物品显示

**实际结果（视觉检查）**：
- 截图与 V-00 / V-01 **完全相同** — 背包窗口未打开
- 前序会话 ydotool 守护进程连接失败（Connection refused），鼠标点击实际未执行
- 游戏状态与 baseline 相同，无任何 UI 变化

**判定**：`BLOCKED`  
**原因**：前序会话截图时 ydotoold 未运行（socket 断线），鼠标点击命令失败，背包未打开。  
**后续行动**：本会话已重启 ydotoold（PID 1137308），将重新点击并截图（→ V-03）

---

### V-03 — Q-02 背包窗口（本会话新截图）

> **待执行**

---


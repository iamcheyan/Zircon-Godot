# Legacy(EI) 界面窗口总览与打开方式

用途：把客户端**全部窗口**列出来，并写清每种窗口**怎么打开**，便于挨个真机测试外观。
两条腿：① 游戏内可触发的键位/按钮/NPC；② 只在巡检里能开的窗口（程序化打开，附巡检命令）。

> 本轮（2026-10-16）新增 `--ui-mask-audit`：它会把下表**所有**窗口逐个打开、各截一张图，
> 并统计"灰色遮罩"（DXButton 兜底灰块）。截图落在 `--ui-mask-audit-out`（缺省
> `/tmp/ui_mask_audit/`），文件名就是窗口名 —— 测试外观时**先看这批图**最快。

```bash
bash /tmp/zircon_uimask_audit.sh          # 需要本地服务端 7001 在跑
# 或手动：
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7001 --window \
  --user test@test.com --pass test123 --char TestHero --legacy-ui --legacy-hud \
  --ui-mask-audit --ui-mask-audit-out /tmp/ui_mask_audit
```

## 一、Legacy EI 字母键（旧版 UI 模式下生效，见 GameScene._Input）

| 键 | 打开/作用 | 对应窗口 |
|---|---|---|
| **Q** | 包袱（背包） | `InventoryDialog` |
| **W** | 状态（人物属性） | `CharacterDialog` |
| **E** | 技能书 | `MagicDialog` |
| **R** | 聊天输入条开关 | `LegacyChatDialog`（F350） |
| **N** | 设置 | `ConfigDialog` |
| **G** | 组队 | `GroupDialog` |
| **F** | 行会 | `GuildDialog` |
| **D** | 任务 | `QuestDialog` |
| **C** | 选中实体 + 交易请求（需目标） | `TradeDialog` |
| **S**（或 Ctrl+S） | 坐骑 | `HorseDialog` |
| **T** | 小地图 128↔256 切换 | `MiniMapDialog`（放大态） |
| **B** | 大地图（legacy 下走小地图放大） | `MiniMapDialog` |
| **Tab** | 独立大地图窗（本移植补充的键） | `BigMapDialog` |
| **PageUp / PageDown** | 聊天记录翻页 | — |
| **Enter / Space / `/` / `@` / `!`** | 打开聊天输入条 | — |

现代 UI 模式的键位不同（N 菜单 / H 帮助 / O 设置 / Q 状态 / W 包袱 / E 技能 /
S 仓库 / Z 腰带 / A 自动喝药 / Ctrl+C 货币 / Ctrl+F 过滤 / Ctrl+W 运势 /
L 任务追踪 / V 小地图 / B 大地图 / R 排行 / P 组队 / G 行会 /
`,` 邮件 / `.` 发邮件 / F 黑名单 / J 任务日志 / Alt+Q 退出 / Alt+X 注销 /
T 交易请求 / M 坐骑 / D 自动跑 / K 聊天模式 / 1..6 腰带格）。
完整表见 `GodotClient/Controls/KeyBindManager.cs` 的 `KeyBinds`。

## 二、HUD 上的按钮入口（主面板 GameInter[50]）

| 按钮 | 位置(面板内坐标) | 打开 |
|---|---|---|
| 交易栏 | (204, 2) | `TradeDialog`（同 C） |
| 小地图 | (228, 2) | `MiniMapDialog` |
| 技能书 | (252, 2) | `MagicDialog` |
| 退出 | (161, 46) | 退出确认 `ExitDialog` |
| 包袱栏 | (648, 32) | `InventoryDialog` |
| 状态栏 | (648, 70) | `CharacterDialog` |
| 技能栏 | (703, 16) | `MagicDialog`（栏位页） |
| 菜单 | (703, 85) | `MenuDialog` |

## 三、由 NPC 带出的窗口（找对应 NPC 说话）

客户端按服务端页类型开窗（`NPCDialogType` 见 `LibraryCore/Enum.cs`）。代表 NPC 取自
`tools/npc_dialog_ir.json`（地图号/坐标即该 NPC 所在位置）：

| 窗口 | 触发 | 代表 NPC（地图 x,y） |
|---|---|---|
| 商店 / 买卖 | `func:buy` | 店员 #14 (0, 470,424)、药店老板 #15 (0, 397,363) |
| 修理 | `func:repair` | 王铁匠 #21 (1, 382,198)、恩实 #17 (0, 414,349) |
| 仓库 | `func:storage` | 客栈保管员 #34 (2, 346,234)、满春 #140 (8, 231,260)、绿洲仓库管家 #208 |
| 宠物（伙伴） | `func:companion` | 王大人 #100 (0, 389,396)、努玛驯兽师 #103 (4, 430,77) |
| 宠物仓库 | NPC 子页 | 同上（宠物窗内的仓库页） |
| 强化 / 精炼 / 打孔 / 合成 / 转职 等 | `NPCDialogType.Refine` 等 | 走对应功能 NPC（技能书页里点选项） |
| 任务领取 / 任务列表 | 任务 NPC | 各城任务 NPC |
| 公告 | 服务端广播 | 自动弹出 `NoticeDialog` |

进图命令（GM）：`@move <地图号> <x> <y>`，例如 `@move 0 470 424` 到店员面前。

## 四、目前**只能程序化打开**（巡检里开、游戏内暂无入口）

这些窗口在客户端里已实现，但还没有键位/NPC 入口，只能靠巡检脚本或调试命令打开 ——
它们同样在 `--ui-mask-audit` 的截图里：

`ConsignmentDialog`（寄售）、`MarketHistoryDialog`（成交记录）、`GameStoreDialog`（游戏商店）、
`MilestoneDialog`（里程碑）、`MonsterDialog`（图鉴）、`FishingDialog`（钓鱼）、
`CommunicationDialog`（好友/邮件）、`NPCSocketDialog` / `NPCSocketCombineDialog`（打孔/合成）、
`NPCQuestDialog` / `NPCQuestListDialog`、`ChatOptionsDialog`、`BuffDialog`、`QuestTrackerDialog`、
`MagicBarDialog`、`MenuDialog`、`HelpDialog`、`NoticeDialog`。

> 要真机逐个调它们的外观，最省事的是跑一次 `--ui-mask-audit`，然后看
> `/tmp/ui_mask_audit/*.png`（每窗一张，含 0 灰块的验收结论）。

## 五、贴底与接缝（2026-10-16 修正，供对照）

- **HUD 贴底**：底板素材 `GameInter[50]` 底部 3 行是纯黑（实测 y=133/134/135 全黑），
  顶部聊天区前 11 行同样纯黑。布局改用**内容高度 133**（`LegacyHudLayout.MainPanelContentHeight`）
  当窗口高度，美术自然越过屏幕底边 → 屏幕最底下不再露出素材黑行。
- **腰带贴 HUD**：腰带素材 `GameInter[51]` 底部 2 行纯黑，底板顶部 11 行纯黑，两者叠加
  就是那道 ~13px 缝。故腰带锚点额外下移 13px（`BeltDialog.LegacyEiBeltDropIntoPanel`），
  让腰带内容底边去接底板内容顶边。
- 核对办法：实机截图量中轴列的逐行像素，木纹/黄球之间不应再有连续纯黑段。

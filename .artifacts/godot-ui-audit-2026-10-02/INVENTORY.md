# GodotClient 可达 UI 全量清单（2026-10-02）

> 数据来源：`GodotClient/` 源码枚举（62 个 `DXWindow` 子类，逐个给出声明位置），
> 交叉核对 `GODOT_WINDOW_PARITY_MATRIX_2026-09-29.md`（13 个 EI 主窗口 + HUD + 确认框 + 小地图）。
> 「经 WindowManager」= 该窗口在 `GameScene` 中至少有一个字段由
> `WindowManager.Open/Toggle/Close` 驱动（决定它是否进入 Z 序列表、能否被 Esc 关掉）。

## 1. 全量窗口清单（62 个 `DXWindow` 子类）

| # | 类别 | 界面 | 声明位置 | GameScene 字段 | 经 WindowManager |
|---|---|---|---|---|---|
| 1 | HUD | `MiniMapDialog` | `Controls/MiniMapDialog.cs:14` | `_miniMap` | ✗（常驻，显隐直控） |
| 2 | HUD | `BuffDialog` | `Controls/BuffDialog.cs:12` | `_buffDialog` | ✗（常驻，显隐直控） |
| 3 | HUD | `BeltDialog` | `Controls/BeltDialog.cs:14` | `_beltDialog` | ✓ |
| 4 | HUD | `ChatTextBox` | `Controls/ChatTextBox.cs:10` | `_chatTextBox` | ✗（常驻输入条） |
| 5 | HUD | `LegacyChatDialog` | `Controls/LegacyChatDialog.cs:10` | `_legacyChatDialog` | ✓（`OpenChat/CloseChat` 内部走 WindowManager） |
| 6 | HUD | `QuestTrackerDialog` | `Controls/QuestTrackerDialog.cs:12` | `_questTracker` | ✗（常驻跟踪条） |
| 7 | HUD | `MonsterDialog` | `Controls/MonsterDialog.cs:12` | `_monsterDialog` | ✗（悬停怪物信息，跟随鼠标） |
| 8 | HUD | `StatusWindow` | `Scripts/StatusWindow.cs` | `_statusWindow` | ✗（F2 调试窗） |
| 9 | 主菜单 | `MenuDialog` | `Controls/MenuDialog.cs:9` | `_menuDialog` | ✓ |
| 10 | 主菜单 | `HelpDialog` | `Controls/HelpDialog.cs` | `_helpDialog` | ✓ |
| 11 | 主菜单 | `ConfigDialog` | `Controls/ConfigDialog.cs` | `_configDialog` | ✓ |
| 12 | 主菜单 | `ExitDialog` | `Controls/ExitDialog.cs` | `_exitDialog` | ✓ |
| 13 | 主菜单 | `ExitGameDialog` | `Controls/ExitGameDialog.cs` | `_exitGameDialog` | ✓ |
| 14 | 主菜单 | `LogoutConfirmDialog` | `Controls/LogoutConfirmDialog.cs` | `_logoutDialog` | ✓ |
| 15 | 主菜单 | `NoticeDialog` | `Controls/NoticeDialog.cs` | `_noticeDialog` | ✓ |
| 16 | 主菜单 | `KeyBindDialog` | `Controls/KeyBindDialog.cs` | —（`ConfigDialog` 内按需 new） | ✗（子对话框，`WindowManager.Open` 于 ConfigDialog.cs:506） |
| 17 | 主菜单 | `ChatOptionsDialog` | `Controls/ChatOptionsDialog.cs` | `_chatOptionsDialog` | ✓ |
| 18 | 对话/弹窗 | `ConfirmDialog` | `Controls/ConfirmDialog.cs` | —（按需 new） | ✗（一次性，`WindowManager.Open` 于调用点） |
| 19 | 对话/弹窗 | `ItemAmountDialog` | `Controls/ItemAmountDialog.cs` | —（按需 new） | ✗（一次性） |
| 20 | 对话/弹窗 | `QuestRewardChoiceDialog` | `Controls/QuestRewardChoiceDialog.cs` | —（按需 new） | ✗（一次性） |
| 21 | 对话/弹窗 | `GroupLfgInputDialog` | `Controls/GroupLfgInputDialog.cs` | —（按需 new） | ✗（一次性） |
| 22 | 对话/弹窗 | `LegacyEiNoticeDialog` | `Controls/LegacyEiNoticeDialog.cs:16` | `_legacyStartNoticeDialog`（`SelectScene`） | ✓（`SelectScene.cs:2072/2084`） |
| 23 | 对话/弹窗 | `LegacyPanelDialog` | `Controls/LegacyPanelDialog.cs` | —（基类/容器） | ✗ |
| 24 | 对话/弹窗 | `LegacyLoginDialog` | `Controls/LegacyLoginDialogs.cs` | —（`LoginScene`） | ✓（`LoginScene` 内 `WindowManager.*`） |
| 25 | 对话/弹窗 | `DXColourPicker` | `Controls/DXColourControl.cs` | —（按需 new） | ✗（一次性） |
| 26 | 对话/弹窗 | `FilterDropDialog` | `Controls/FilterDropDialog.cs` | `_filterDropDialog` | ✓ |
| 27 | 对话/弹窗 | `CaptionDialog` | `Controls/CaptionDialog.cs` | `_captionDialog` | ✓ |
| 28 | 对话/弹窗 | `MilestoneDialog` | `Controls/MilestoneDialog.cs` | `_milestoneDialog` | ✓（自身 `:38` Open / `:26` Close） |
| 29 | 背包/角色/技能 | `InventoryDialog` | `Controls/InventoryDialog.cs` | `_inventoryDialog` | ✓ |
| 30 | 背包/角色/技能 | `CharacterDialog` | `Controls/CharacterDialog.cs` | `_characterDialog`、`_statusPreviewDialog` | ✓ |
| 31 | 背包/角色/技能 | `EditCharacterDialog` | `Controls/EditCharacterDialog.cs` | `_editCharacterDialog` | ✓ |
| 32 | 背包/角色/技能 | `MagicDialog` | `Controls/MagicDialog.cs` | `_magicDialog` | ✓ |
| 33 | 背包/角色/技能 | `AutoPotionDialog` | `Controls/AutoPotionDialog.cs` | `AutoPotionBox` | ✓ |
| 34 | 背包/角色/技能 | `CurrencyDialog` | `Controls/CurrencyDialog.cs` | `_currencyDialog` | ✓ |
| 35 | 背包/角色/技能 | `StorageDialog` | `Controls/StorageDialog.cs` | `_storageDialog` | ✓ |
| 36 | 背包/角色/技能 | `BundleDialog` | `Controls/BundleDialog.cs` | `_bundleDialog` | ✓ |
| 37 | 背包/角色/技能 | `LootBoxDialog` | `Controls/LootBoxDialog.cs` | `_lootBoxDialog` | ✓ |
| 38 | 社交 | `GroupDialog` | `Controls/GroupDialog.cs` | `_groupDialog` | ✓ |
| 39 | 社交 | `GuildDialog` | `Controls/GuildDialog.cs` | `_guildDialog` | ✓ |
| 40 | 社交 | `GuildMemberDialog` | `Controls/GuildMemberDialog.cs` | `_guildMemberDialog` | ✓ |
| 41 | 社交 | `CommunicationDialog` | `Controls/CommunicationDialog.cs` | `_communicationDialog` | ✓ |
| 42 | 社交 | `RankingDialog` | `Controls/RankingDialog.cs` | `_rankingDialog` | ✓ |
| 43 | 社交 | `TradeDialog` | `Controls/TradeDialog.cs` | `_tradeDialog` | ✓（`OpenTrade`/`ClearTrade` 内部） |
| 44 | 系统 | `DungeonFinderDialog` | `Controls/DungeonFinderDialog.cs` | `_dungeonFinderDialog` | ✓ |
| 45 | 系统 | `MarketHistoryDialog` | `Controls/MarketHistoryDialog.cs` | `_marketHistoryDialog` | ✓（自身 Open/Close） |
| 46 | 系统 | `GameStoreDialog` | `Controls/GameStoreDialog.cs` | `_gameStoreDialog` | ✓ |
| 47 | 系统 | `GameStoreGiftDialog` | `Controls/GameStoreGiftDialog.cs` | —（按需 new） | ✗（一次性） |
| 48 | 系统 | `ConsignmentDialog` | `Controls/ConsignmentDialog.cs` | `_consignmentDialog` | ✓ |
| 49 | 系统 | `FishingDialog` | `Controls/FishingDialog.cs` | `_fishingDialog` | ✓ |
| 50 | 系统 | `FishingCatchDialog` | `Controls/FishingDialog.cs`（同文件第二个类） | `_fishingCatchDialog` | ✗（钓鱼小游戏浮层） |
| 51 | 系统 | `FortuneCheckerDialog` | `Controls/FortuneCheckerDialog.cs` | `_fortuneDialog` | ✓ |
| 52 | 系统 | `HorseDialog` | `Controls/HorseDialog.cs` | `_horseDialog` | ✓ |
| 53 | 系统 | `HorseTameDialog` | `Controls/HorseTameDialog.cs` | `_horseTameDialog` | ✗（驯马小游戏浮层） |
| 54 | 系统 | `CompanionDialog` | `Controls/CompanionDialog.cs` | `_companionDialog` | ✓ |
| 55 | 系统 | `BigMapDialog` | `Controls/BigMapDialog.cs` | `_bigMap` | ✗（常驻，显隐直控 + 自身关闭钮） |
| 56 | 系统 | `QuestDialog` | `Controls/QuestDialog.cs` | `_questDialog` | ✓ |
| 57 | 系统 | `TimerDialog` | `Controls/TimerDialog.cs` | `_timerDialog` | ✗（倒计时浮层） |
| 58 | NPC | `NPCDialog` | `Controls/NPCDialog.cs` | `_npcDialog` | ✓ |
| 59 | NPC | `NPCQuestDialog` | `Controls/NPCQuestDialogs.cs` | `_npcQuestDialog` | ✓ |
| 60 | NPC | `NPCQuestListDialog` | `Controls/NPCQuestDialogs.cs` | `_npcQuestListDialog` | ✓ |
| 61 | NPC | `NPCSocketDialog` | `Controls/NPCSocketDialogs.cs` | `_npcSocketDialog` | ✓ |
| 62 | NPC | `NPCSocketCombineDialog` | `Controls/NPCSocketDialogs.cs` | `_npcSocketCombineDialog` | ✓ |
| 63 | NPC | `NPCCompanionStorageDialog` | `Controls/NPCCompanionStorageDialog.cs` | `_npcCompanionStorageDialog` | ✓ |
| 64 | NPC | `ConsignItemDialog` | `Controls/ConsignmentDialog.cs`（同文件第二个类） | —（按需 new） | ✗（一次性） |
| 65 | 商店/交易 | `NPCGoodsPanel` | `Controls/NPCGoodsPanel.cs` | 由 `_npcDialog` 承载 | ✗（NPC 商店面板，非独立窗口） |
| 66 | 商店/交易 | `NPCRepairPanel` | `Controls/NPCRepairPanel.cs` | 由 `_npcDialog` 承载 | ✗（同上） |

> 注：表中 #65/#66 不是 `DXWindow` 子类（NPC 对话窗内的面板），列在此处是为了商店/交易
> 类别不漏项；`NPCAdvancedPanels.cs` / `NPCSocketPanels.cs` 同为面板族。
> 类别计数（按 `DXWindow` 子类去重）：HUD 8、主菜单 9、对话/弹窗 11、
> 背包/角色/技能 9、社交 6、系统 12、NPC 7 = **62**。

## 2. 非窗口 HUD 元素与覆盖层

| 元素 | 位置 | 说明 |
|---|---|---|
| `MainPanel` | `Controls/MainPanel.cs:16` | F50 底图 + 16 个 caption 按钮 + 球体 + 属性标签 |
| `MagicBar` | `Controls/MagicBar.cs` | 12 槽技能条（`B` 键 / HUD cap2 翻转 `Visible`） |
| `ChatLogPanel` | `Controls/ChatLogPanel.cs` | 常驻聊天历史（F50 内槽位） |
| `GroupHealthPanel` | `Controls/GroupHealthPanel.cs` | 组队血条浮层 |
| `LegacyHudCaptionHint` | `Controls/LegacyHudCaptionHint.cs` | caption 悬停提示 |
| `UiOverlay` | `Controls/UiOverlay.cs` | Web 编辑器覆盖层（`ui_overlay.json`） |
| `LegacyHudLayout` | `Scripts/LegacyHudLayout.cs` | 逻辑画布 800×600 常量与槽位 |

## 3. 只可能由服务端包触发的界面（单人测试不可达）

| 界面 | 触发包 | 处理器 |
|---|---|---|
| `NPCDialog` | `S.NPCResponse` | `GameScene.cs:7324` |
| `TradeDialog` | `S.TradeOpen` / `S.TradeRequest` | `GameScene.cs:1343/1346` |
| `GuildDialog` 邀请框 | `S.GuildInvite` | `GameScene.cs:2879` |
| `GuildDialog` 求婚框 | `S.MarriageInvite` | `GameScene.cs:3105` |
| `GroupDialog` 邀请框 | `S.GroupInvite` | `GameScene.cs:2801` |
| `StorageDialog`（仓库） | `S.StorageSize` 等 | `GameScene.cs:280` 区域 |
| `GameStoreDialog` / `ConsignmentDialog` | 商城/拍卖开关包 | `GameScene.cs:717/719` |
| `QuestDialog` 内容 | `S.QuestUpdate` 等 | 任务链 |
| `CommunicationDialog` 邮件 | `S.MailList` / `S.MailSend` | `GameScene.cs:1245+` |
| `NoticeDialog` | `S.Notice` | `GameScene.cs:381` |
| `MilestoneDialog` | 里程碑包 | `GameScene.cs` 里程碑回调 |

## 4. 不经 `WindowManager` 的窗口（Esc 关不掉，属设计内）

以下 12 个窗口在 `GameScene` 中有字段但**从未**经 `WindowManager` 驱动，因此
`Esc`（`WindowManager.CloseTop`）**不会**关闭它们。逐个核对后判定为**设计内**，
不是缺陷（各有自己的关闭路径）：

| 窗口 | 为什么不经 WindowManager | 关闭路径 |
|---|---|---|
| `MiniMapDialog` | 原版小地图是常驻 HUD（D3D rect {672,0,800,128}） | `V` 键 / HUD cap（`_miniMap.Visible` 直控） |
| `BuffDialog` | 常驻 buff 条（583,0,84,30） | 无 buff 时自动隐藏 |
| `ChatTextBox` | 现代版常驻输入条 | legacy 下由 `LegacyChatDialog` 取代 |
| `QuestTrackerDialog` | 常驻任务跟踪条 | 设置项 `QuestTrackerVisible` / 任务窗勾选 |
| `MonsterDialog` | 跟随鼠标的怪物信息浮层 | 鼠标移开即隐藏 |
| `StatusWindow` | F2 调试窗（原版无此窗） | F2 |
| `BigMapDialog` | 由小地图放大态承载（EI 无独立大地图窗） | 自身关闭钮（`BigMapDialog.cs:50`） |
| `FishingCatchDialog` / `HorseTameDialog` | 小游戏浮层，随小游戏状态出现/消失 | 小游戏结束 |
| `TimerDialog` | 倒计时浮层 | 计时结束 |
| `LegacyChatDialog` | 经 `OpenChat`/`CloseChat` 间接走 `WindowManager`（已计入 ✓） | `R` / `Esc`（本轮修复）/ ✕ |
| `TradeDialog` | 经 `OpenTrade`/`ClearTrade` 间接走 `WindowManager` | `Esc` / 交易结束 |

> 本轮真机矩阵已验证：`Q/W/E/D/G/F/N/V/Z/S/R` 十个热键入口 + HUD cap11/cap6
> 按钮的「开 → 同键关 → Esc 关」生命周期全部 PASS（见 `REPORT.md` §2）。

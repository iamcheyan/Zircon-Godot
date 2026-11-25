# 掌机全客户端 UI 审计阶段报告

日期：2026-11-25（暂停任务续跑）。本报告只覆盖掌机适配和本地隔离检查；不代表 TG5040 真机签收，不代表所有网络服务已验证。

## 入口与状态

源码实际注册的主要窗口入口包括：`InventoryDialog`、`CharacterDialog`、`EditCharacterDialog`、`StorageDialog`、`BeltDialog`、`AutoPotionDialog`、`CurrencyDialog`、`FilterDropDialog`、`BundleDialog`、`FortuneCheckerDialog`、`LootBoxDialog`、`DungeonFinderDialog`、`MagicDialog`、`QuestDialog`、`GroupDialog`、`CommunicationDialog`、`GuildDialog`、`GuildMemberDialog`、`RankingDialog`、`CompanionDialog`、`NPCCompanionStorageDialog`、`GameStoreDialog`、`ConsignmentDialog`、`MarketHistoryDialog`、`FishingDialog`、`FishingCatchDialog`、`HorseTameDialog`、`HorseDialog`、`TradeDialog`、`NPCDialog`、`NPCSocketDialog`、`NPCSocketCombineDialog`、`NPCQuestListDialog`、`NPCQuestDialog`、`HelpDialog`、`ConfigDialog`、`ChatOptionsDialog`、`BigMapDialog`、`MiniMapDialog`、`ConfirmDialog`、`ItemAmountDialog`。掌机手札提供背包、人物、技能、任务、社交、系统六个主标签及实际分类。

本机机器可读入口清单 `.artifacts/handheld-ui-full-20261125/coverage.json` 记录 51 个从 GameScene 提取的原生窗口入口及 30 个手札分类。源码关闭的商城、寄售、地牢与同伴功能记录了对应开关。

## 已验证

- **PASS**：世界地图 Select 打开/关闭和输入所有权；小地图纹理、玩家居中、地图切换；背包多格占用/分页/详情；动作绑定两页、六个直达槽、组合键和持久化；退出确认；数量/确认 prompt 无 native text focus、方向键/A/B、默认取消、失效回调和单次调用；NPC 独立掌机对话、焦点、分页、长文滚动、颜色和含冒号选项。
- **PASS（截图）**：Xvfb/OpenGL 640×360 的 NPC 对话、仅出售服务簿、背包占格与底栏、大地图、启动预览。证据在 `.artifacts/handheld-ui-full-20261125/screens/`。离线 NPC 目录另有 2031 张页截图。
- **PASS（PC 回归范围）**：`HandheldServiceFocusChecks` 不带 `--handheld` 通过，确认 sale bag/native NPC pane 不改变桌面原生链路。没有启动或部署正式 PC 客户端全流程，因此不把它写成完整 PC 回归。
- **FIXED**：仅出售 NPC 的 native sale bag 不再抢掌机 service root 焦点；普通确认框仍拥有最高输入优先级。提交 `d28ddaa8`。
- **FIXED**：掌机 NPC 正文显示 FCOLOR 颜色，提交 `d28ddaa8`。
- **FIXED**：背包拆叠等非 NPC 数量/确认框现在通过 GameScene 接收方向键和 A/B。重复键不重复改单，关闭后的 A 松开不会触发世界动作。提交 `18fe40f2`。
- **FIXED**：手札底栏按实际字宽缩短操作提示，避免遮住相邻按键。提交 `d4271aae`，前后截图见 `screens/bag-footprint.png` 和 `screens/bag-footprint-footer-fixed.png`。
- **FIXED**：640×360 建角说明越过蓝色面板右边缘。掌机改用实际绘制字宽折行，窗口缩放后重新布局。三职业在三个倍率的检查均通过，PC 保持原折行。提交 `cad9911c`，前后截图见 `screens/checkpoint-create-640.png` 和 `screens/create-640-fixed.png`。

## 未完成 / 阻塞

- **PARTIAL**：本机连接拒绝显示错误提示，角色选择和建角离线预览已截取实际 640×360 画面。X11 legacy 启动采用保存的窗口尺寸，验收用 `xdotool windowsize` 调整到 640×360。登录、注册、改密、角色选择与建角提交、掉线重连及退出恢复链仍待联机。
- **BLOCKED_LIVE**：好友、私聊、组队、行会、邮件附件、排行、查看玩家与交易需要隔离的角色与服务端状态。
- **BLOCKED_LIVE**：仓库、购买/出售、修理、锻造/精炼、打孔/镶嵌、评估、任务领取/传送等专用服务只做了投影/焦点或本地 prompt 检查，尚未逐项核对服务器包、费用、物品变化和错误状态。
- **PARTIAL**：HUD、动作页、腰带/自动喝药、人物/装备/抗性/称号/坐骑、技能/任务、设置分页已枚举并有若干本地 checks，但没有将每个动态数据/网络回应/关闭重开路径都现场签收。
- **ISSUE**：之前 live NPC sweep 仅 11/294 NPC，65 页，12 条 `link_failed`；修复后的 service focus 尚未重跑这些实服项。不能宣称 NPC 全量完成。
- **NOT_DEPLOYED**：未部署 TG5040，未改生产服务或数据库。

## 验证记录

- `dotnet build GodotClient/ZirconClient.csproj`：PASS，0 errors。
- PASS checks：`HandheldInputChecks`、`HandheldBindingsChecks`、`HandheldActionIntegrationChecks`、`HandheldJournalExitChecks`、`HandheldPromptChecks`、`HandheldNpcDialogueChecks`、`HandheldNpcInteractionChecks`（最新单独运行）、`HandheldServiceFocusChecks`（handheld/desktop）、`HandheldBagFootprintChecks`、`HandheldQuickLayoutChecks`、`HandheldHudChecks`、`HandheldMiniMapChecks`、`HandheldWorldMapChecks`。
- Xvfb/OpenGL 截图：`HandheldNpcDialogueChecks`、`HandheldBagFootprintChecks`、`HandheldWorldMapChecks`，均 640×360。
- 离线 catalog：`HandheldNpcCatalogChecks`，294 NPC / 4295 pages / 4284 structurally reachable / 2031 screenshots；不是 live service acceptance。

## 交付边界

代码检查点为 `d28ddaa8`、`18fe40f2`、`d4271aae`、`cad9911c`、`55b161cd`。针对提交源码归档的验证见 `.artifacts/handheld-ui-full-20261125/logs/`。PC 核对仅覆盖原生出售窗口焦点、背包占格和建角折行。先前未提交的其他修改仍保留在工作区。
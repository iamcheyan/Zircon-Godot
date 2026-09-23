# 旧版 EI 对话框迁移阶段总结

> 更新时间：2026-09-23
> 分支：`ui/legacy-layout-lab`
> 当前提交：`03a45ba9`
> 状态：核心布局和测试基础设施已建立，业务完整迁移仍在进行中。

## 1. 目标与总体判断

本阶段的目标不是给新版窗口更换一张旧背景，而是让 Zircon 的窗口逐个复用旧版 EI 的真实根矩形、GameInter 资源、按钮帧、格子坐标和业务交互。

目前已经确认：旧版 UI 的贴图通常包含透明画布，不能把整张 WIL 帧强制缩放进 Godot 窗口。正确方式是保持原始比例，按有效像素边界平移到根窗口内，并由根窗口裁剪透明区域。当前测试场和 `--legacy-ui` 验证入口都遵循这一原则。

当前不能宣布“全部迁移完成”。已经通过的是若干窗口的几何/贴图基础验收；属性、物品、技能、NPC 等业务数据和后续窗口仍需要逐项验证。

## 2. 事实来源与关键理解

### 最近一轮：交易与行会旧版根框（2026-09-23）

- `TradeDialog` 已增加旧版 EI F1050 布局：根矩形 `484×330`，GameInter F1050，双方物品格改为证据中的 5×6，位置分别为 `(14,91)` 和 `(246,91)`；原有物品链接、金币、确认和关闭网络逻辑保留。
- `GuildDialog` 已增加旧版 EI F600 布局：根矩形 `446×596`，保留成员、仓库、公告和管理操作控件，不再使用新版 Interface 260 作为旧版迁移背景。
- 两个窗口均纳入 `LegacyHudLayoutLab --legacy-audit`，本轮结果为 `trade=True guild=True`；独立测试场完整审计仍为 `PASS`。
- 本轮只确认了静态根框、资源、格子几何和现有业务控件未被移除；双客户端交易、真实行会成员/仓库/公告数据的线上验收仍未宣称完成。
- 随后补齐仓库旧版紧凑分支：`StorageDialog` 使用 GameInter F1001、`205×205` 根矩形和证据中的 `4×3/12` 格子（`(22,43)`，步长 38）；商店 F1000 没有与仓库错误复用，继续按独立状态机处理。
- 本轮再补齐设置窗口：`ConfigDialog` 使用 GameInter F750、`248×264` 根矩形和 F161/F162 关闭按钮；五个设置页签仍由原有控件切换，保存逻辑未替换。`LegacyHudLayoutLab --legacy-audit` 已对设置窗口一并通过。
- 测试场 HUD 的行会、交易按钮已从“仅打印提示”改为实际打开已迁移的 F600/F1050 窗口；完整审计仍通过。小地图、退出/登出仍保留各自的现有入口，公告/提示窗口尚未完成旧版 F602 迁移。
- F602 公告窗口已完成：`NoticeDialog` 使用旧版 GameInter F602、`584×252` 根矩形、关闭按钮 `(548,16)`、操作按钮 `(496,27)` 和正文区 `(23,94,500,112)`；`GameScene.ReceiveChat` 在 `--legacy-ui` 下收到 `Announcement` 时打开该窗口，同时保留聊天记录。
- F602 接入后的完整 `login_game.sh -R --legacy-ui` 烟测再次通过，服务端重建、登录、StartGame 和进入 `TestHero` 地图均成功，日志无新的运行时错误。
- 正式 `GameScene` 新增 `--legacy-open=<window>` 直达入口；实测 `--legacy-open=character` 的完整登录链成功进入地图。该次 Xvfb 根截图受全屏窗口捕获方式影响为空白，因此只计入启动/路由验证，不把它算作正式视觉截图证据。
- 修复 NPC 旧版动态路径：`NPCDialog.ShowPage()` 在真实协议回包后不再把 F1100 退回 380×204；旧版模式会重新保持 `552×176`、正文 `(20,28,500,112)` 和旧版关闭按钮。测试场审计通过，真实 NPC 商店/修理回包仍需实际 NPC 场景验收。
- 修复仓库动态路径：`StorageDialog.RefreshStorage()` 在容量/物品刷新后重新保持 F1001 的 `205×205`、4×3 网格和旧版控件可见性，避免服务器回包把布局恢复成现代 10 列窗口。
- NPC/仓库动态刷新修复后的 `login_game.sh -R --legacy-ui` 回归通过：服务端重建、登录、StartGame 和进入 `TestHero` 地图均成功，未出现新的运行时错误。
- 修复角色窗口动态路径：旧版模式下 `ShowOwn()` 不再恢复现代 F110 和扩展装备格，而是重新保持 F200/F201、已确认的 38×38 装备格和旧版属性标签；`ApplyInspect()` 仍保留查看他人角色逻辑。编译及完整旧版审计通过。
- NPC 背包模式接线补齐：出售入口继续使用 `[变卖]`，修理 NPC 回包现在调用 `SetInventoryLegacyMode(Repair)` 显示 `[修补]`，关闭 NPC 统一恢复 `[包袱]`；拿取/放下/拆分/丢弃仍复用现有 `DXItemCell` 业务逻辑。
- 角色动态与 NPC 背包模式修改后的 `login_game.sh -R --legacy-ui` 回归通过：服务端、登录、StartGame 和进入 `TestHero` 地图均成功，未出现新的运行时错误。
- 范围确认：商城、寄售、地下城和宠物功能当前均由 `GameScene` 的功能开关禁用，符合开发阶段先简化玩法的决定；这些窗口不再作为旧版核心 UI 迁移的未完成项。
- 测试审计强化：`LegacyHudLayoutLab --legacy-audit` 现在会实际调用角色 `ShowOwn()` 和仓库 `RefreshStorage()` 后再检查旧版根框/格子，动态恢复路径也纳入 PASS 条件。
- 完整 `login_game.sh -R --legacy-ui` 烟测再次通过：服务端重建并监听 7000，登录成功、StartGame 成功并进入 `TestHero` 地图；同时修复了 Login/Select/Game 场景重复绑定 `size_changed` 导致的 Godot 错误，复测日志已不再出现该警告。
- 视觉验收补充完成：测试场分别在 800×600 和 1600×900 下运行并截图，`/tmp/legacy-ui-800x600.png`、`/tmp/legacy-ui-1600x900.png` 均目视确认旧版人物窗口保持原始比例，HUD 等比缩放且无强制拉伸。
- 修复任务/聊天动态换页覆盖旧版资源的路径：旧版任务 F700 在切换当前/可接/已完成页时保持 F700，聊天 F350 在好友/收件/写信/屏蔽页切换时不再改写隐藏的现代页背景；聊天页签使用绝对旧版坐标，重复应用皮肤不会累加偏移。无头 `LegacyAudit` 仍为全 PASS。
- HUD 测试场补齐真实入口：小地图按钮改为打开 `MiniMapDialog`，退出/登出按钮改为打开 `ExitDialog`，不再只打印提示；审计新增小地图几何检查并通过。
- 技能窗口旧版模式清理完成：F400 书页下不再保留现代纵向技能列表和滚动条，刷新时只显示旧版 12 格；技能图标/等级、分类和 F1-F12/Shift+F1-F12 绑定逻辑继续复用真实 `UserMagics`。构建、`LegacyAudit` 和完整登录回归均通过。
- 依据旧版窗口注册表修正菜单语义：F750 只属于旧版选项/设置窗口；`--legacy-ui` 下 HUD 菜单按钮和 `--legacy-open=menu` 均路由到 `ConfigDialog`，通用 `MenuDialog` 仅保留给新版模式。构建与 `LegacyAudit` 通过。

事实来源按以下优先级使用：

1. `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/` 中的反编译 primary-static 证据。
2. WIL/WIX 资源和 `Magic.exp`。
3. `Tools/mir3_client_simulator/data/` 的布局和交互记录。
4. 网页模拟器的浏览器复现。
5. 当前 Godot 控件只用于保留业务逻辑，不能反推旧版尺寸。

已经确认的通用规则：

- 旧版背景和按钮优先使用 `LibraryFile.GameInter`，不能用当前 `Interface` 黑底近似。
- `FixedSize` 只固定控件边界，不改变有效贴图尺寸；`StretchImage=false` 是默认值。
- 旧版关闭按钮通常是 GameInter F161/F162，不是新版 Interface[15]。
- 测试场必须使用正式窗口类，不能另造只用于截图的假窗口。
- 测试场默认隐藏所有窗口，避免多个窗口叠加造成错误截图。
- 坐骑 F850 与快捷物品腰带是两个不同窗口，不能再混用名称或代码。

## 3. 已完成的代码工作

### 3.1 测试场与审计入口

文件：`GodotClient/Scripts/LegacyHudLayoutLab.cs`

- 支持 `--legacy-open=<window>`。
- 支持 `--legacy-audit`，对人物、背包、技能、坐骑、NPC 及聊天/任务/设置/组队根尺寸进行自动审计。
- 支持 F2～F9 和 Esc 进行窗口切换/关闭。
- 测试场统一使用 800×600 旧版逻辑坐标并等比缩放。

当前已通过的无头审计输出：

```text
[LegacyAudit] PASS character=True inventory=True magic=True horse=True npc=True roots=True
character size=(244, 328) background=F200 toggle=(176, 264)/(36, 36) visibleSlots=8 switch=True
inventory size=(284, 324) background=F250 grid=(6, 6)@(25, 41) close=(249, 288) action=(176, 262)
magic size=(452, 380) background=F400 categories=8 skillSlots=12
horse size=(296, 332) F850 buttons=(28, 244),(74, 244),(133, 244),(192, 244) state=0
npc size=(552, 176) background=F1100 text=(20, 28)/(500, 112)
```

### 3.2 人物窗口

文件：`GodotClient/Controls/CharacterDialog.cs`

- 根尺寸：244×328。
- 属性背景：F200；装备背景：F201。
- 关闭按钮：F161/F162，`(212,298)`，28×26。
- 属性/装备切换：F171/F172，`(176,264)`，36×36；审计会实际切换到 F201 再恢复 F200。
- 已按证据放置 8 个已确认装备格，全部 38×38：头盔、火把、毒药、左右手镯、左右戒指、鞋。
- 未确认的新版扩展槽没有继续猜坐标。

尚未完成：旧版属性文本、纸娃娃和装备数据在两个旧版页面中的完整绑定，以及真实换装/拖动/快捷键验收。

本轮增量：F200/F201 已实际切换审计；F200 属性区新增旧版坐标文本（等级、HP、MP、攻击、魔法、防御、魔御），数值从当前 `PlayerStats`/玩家等级刷新。测试场截图确认文字位于旧版窗口内部；仍需真实登录场景确认实际数值、纸娃娃和换装刷新。

### 3.3 背包窗口

文件：`GodotClient/Controls/InventoryDialog.cs`

- 根尺寸：284×324，背景 F250。
- 6×6 格子，起点 `(25,41)`，步距 36。
- 关闭按钮：F161/F162，`(249,288)`。
- 动作按钮：F264/F265，`(176,262)`，64×20。
- 负重文字改为旧版格式：`负重:%d / 总量:%d`。
- 旧版包袱/变卖标签和金币区域已经放入旧版坐标。

尚未完成：修补/储存模式的真实服务器场景验收，以及旧版动作按钮与现有拿取/放下/拆分/丢弃/NPC 交易业务的完整点击回归。

本轮增量：`InventoryMode` 已扩展为 Normal/Repair/Sell/Storage 四态；旧版模式标签和负重文字显示规则已接入 `SetLegacyMode`，但修补/储存仍需由服务器/NPC 回包驱动，尚未宣称业务闭环完成。

### 3.4 技能窗口

文件：`GodotClient/Controls/MagicDialog.cs`

- 根尺寸：452×380，背景 F400；保留原始比例，不使用旧的 296×332 候选值。
- 已建立八个旧版分类按钮：火、冰、电、风、神圣、黑暗、幻影、剑。
- 已建立 12 个 36×36 技能格，四列三行，起点 `(30,60)`，步距 40。
- 运行时有技能数据时，格子会从 `MagicInfo.Icon` 刷新实际 `MagicIcon`，并显示技能名称/等级状态提示，而不是永远显示静态 F410～F421。

尚未完成：完整 Magic.exp 分类过滤、熟练度显示，以及正式登录场景中逐项验证。

本轮增量：旧版技能格选中后，MagicDialog 已接管 F1-F12/Shift+F1-F12，并将当前技能栏组键位写回 `UserMagic`、发送网络更新和刷新快捷栏；仍需真实登录场景点击技能格后验证服务端回写。

### 3.5 坐骑窗口

新增文件：`GodotClient/Controls/HorseDialog.cs`

- 根尺寸：296×332，背景 F850。
- 四个动作按钮：
  - F860/F861：`(28,244)`，上马。
  - F862/F863：`(74,244)`，遛马/下马分支。
  - F864/F865：`(133,244)`，收马。
  - F866/F867：`(192,244)`，遛马/取马。
- 关闭按钮：F161/F162，`(252,293)`。
- 状态 0 只启用上马按钮，非 0 启用遛马分支；收马和取马按钮保持旧版的无条件入口。
- 正式 `GameScene` 已创建该窗口，接收坐骑状态更新。
- 正式游戏加入 Ctrl+S 入口；现代 M 键坐骑动作保持不变。

### 3.6 NPC 窗口

文件：`GodotClient/Controls/NPCDialog.cs`

- 根尺寸：552×176，背景 F1100。
- 已把旧版根框、正文区域、滚动区和 F161/F162 关闭按钮接入。
- 现有 NPC 文本、选项、商店和修理控件仍由同一个 NPCDialog 负责。

尚未完成：真实 NPC 协议正文、商店、修理页面在 F1100 根框内的完整视觉和交互验收。

### 3.7 其它已经接入测试场的窗口

聊天 F350、任务 F700、设置 F750、组队 F900 已有旧版根框和透明交互热区。聊天和任务已保留动态正文，并修复切页时覆盖旧版外框的状态路径；`LegacyAudit` 检查内容 alpha 和根尺寸，设置页和组队网络操作仍需真实场景验收。

## 4. 正式游戏接入方式

为避免未完成的旧版数据绑定直接影响默认游戏，正式场景增加了显式开关：

```bash
godot-mono --path GodotClient -- \
  --server 127.0.0.1 --port 7000 \
  --user test@test.com --pass test123 --char TestHero \
  --window --legacy-ui
```

`--legacy-ui` 会让正式 `GameScene` 复用旧版核心窗口控件树；不带该参数时仍使用当前正式布局，便于逐窗迁移和回归测试。坐骑窗口和 Ctrl+S 接入不依赖该开关。

已经完成真实登录 smoke test：登录成功、StartGame 成功并进入 `TestHero` 地图；本轮交易/行会布局后再次复测，仍成功进入地图。日志中出现的 `size_changed already connected` 以及测试环境缺失怪物图库提示是现有环境问题，不是本轮窗口布局新增错误。该结果只证明旧版控件接入没有阻断登录流程，不能代替窗口业务验收。

## 5. 已执行的验证

### 编译

```bash
dotnet build GodotClient/ZirconClient.csproj --no-incremental
```

结果：成功，只有仓库原有的 nullable/未使用变量警告，无错误。

### 旧版测试场

```bash
ZIRCON_UI_DATA_PATH=/home/tetsuya/mir3ei/LegacyEI/Data \
godot-mono --path GodotClient res://Scenes/LegacyHudLayoutLab.tscn -- \
  --window --legacy-audit
```

结果：`LegacyAudit PASS`，人物、背包、技能、坐骑、NPC 和核心根尺寸均通过。

### 现有新版 UI 回归审计

已运行 `UITestScene` 的通信、技能、任务、人物、组队、设置、快捷键和窗口 chrome 审计。现有新版业务审计大部分通过；`UIHudAudit` 和 `UIItemGridAudit` 仍有仓库已存在的失败项，不能误记为旧版迁移通过：

- `UIHudAudit`：当前基线 panel/character 尺寸与预期不一致。
- `UIItemGridAudit`：当前基线 badges/lootLock 标志失败。
- `UIHorseAudit`：检查的是驯马提示控件，不是新增的旧版 F850 坐骑窗口。

### 真实登录

使用测试账号完成：

```text
Login 成功
StartGame 成功
进入游戏! 玩家: TestHero
```

## 6. 当前提交和推送记录

本阶段相关提交已推送到 `origin/ui/legacy-layout-lab`：

| 提交 | 内容 |
|---|---|
| `443cc0e8` | 人物、背包、技能窗口基础验收 |
| `bda31496` | 坐骑窗口资源与技能数据刷新 |
| `bef6b97f` | 坐骑正式热键与状态接入 |
| `b2782e02` | 正式登录场景 `--legacy-ui` 开关 |
| `405f03d3` | 真实登录验证入口记录 |
| `b3d65601` | 窗口索引和资料映射校正 |
| `9a76d412` | NPC F1100 根框和审计 |
| `2c49a4ab` | 人物 F200/F201 切换验收 |

## 7. 未完成任务与推荐顺序

1. 完成人物 F200 属性文本、纸娃娃和 F201 装备数据绑定，验证换装、拖动、关闭和快捷键。
2. 完成背包四模式与所有物品操作，重点验证旧版 6×6 格子和 NPC 模式。
3. 完成技能 Magic.exp/UserMagics/熟练度/快捷键闭环。
4. 在真实登录场景逐项打开聊天、任务、设置、组队，确认动态数据不被透明热区遮挡。
5. 完成 NPC 商店/修理、GameInter F1000 商店、仓库、交易 F1050、行会 F600、公告 F602。
6. 对 HUD 每个按钮建立“按钮 → 窗口 → 关闭/置顶/快捷键”的映射表并逐项点击。
7. 在 800×600 和当前缩放分别截图，与网页模拟器和旧版证据逐像素检查。
8. 所有检查项完成后，才移除或默认开启 `--legacy-ui` 的保护开关，并进行最终真实登录验收。

## 8. 交接注意事项

- 不要把 `HorseDialog` 和 `BeltDialog` 合并。
- 不要把 `legacy_ui.json` 中的候选尺寸当作已确认事实；尤其是商店、交易、行会和部分 NPC 子页面。
- 不要为了让图片“填满”窗口而开启整张 WIL 画布拉伸。
- 不要把测试场的根尺寸 PASS 当成业务迁移完成；动态数据、点击行为和真实登录都必须单独验收。
- 当前工作树应保持干净；修改后必须编译、跑 `--legacy-audit`、提交并推送。

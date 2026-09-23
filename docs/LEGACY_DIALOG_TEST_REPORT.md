# 旧版 EI 测试界面阶段验收报告

> 验收日期：2026-09-23  
> 分支：`ui/legacy-layout-lab`  
> 最新提交：`122f4c07`  
> 验收范围：旧版 EI 测试场，不把正式游戏内替换当作本阶段的视觉验收依据。

## 0. HUD 球体修正（2026-09-23）

复核截图发现右侧红球并不是玩家状态球，而是 `MCImage` 错误复用了
`GameInter[62]` 完整红球帧。现已隐藏该错误属性图标；玩家红球和红蓝球只由
`MainPanel._playerOrb` 在左侧 `(49,13)` 的同一个 `112×110` 控件绘制。测试场
审计新增 `orb=True`，同时检查重复整球帧不可见、左右细球控件不可见、玩家球位置固定。

本次回归结果：

- `dotnet build ... --no-incremental`：通过，0 错误（仅保留 3 个既有警告）。
- `--legacy-audit`：通过；13 个旧版窗口、生命周期、资源根节点和 HUD 球体均为 `True`。
- 球体审计：`orb=(49, 13)/(112, 110) visible=True duplicateIcon=True states=True/True/True single=True`。
- 状态语义：最大魔法值为 0 时同一控件显示完整红球；最大魔法值大于 0 时同一控件显示红蓝球，当前魔法值为 0 不会错误退回完整红球。
- 多分辨率启动检查：800×600 与 1600×900 均能启动测试场；1600×900 截图确认 HUD 保持旧版比例、左侧只有一个完整红球，右侧没有重复红球。

真实登录回归：使用 `bash login_game.sh` 自动构建并启动本地 `ServerCore`，客户端完成版本校验、账号登录、自动选角和 `StartGame`，日志确认进入 `TestHero` 地图。该回归证明单球修正没有阻断正式旧版 UI 接入；Xvfb 下的 root 截图抓取不作为像素证据，像素/位置以测试场贴图截图和 `LegacyAudit` 坐标审计为准。

新增直达窗口日志后，`--legacy-ui --legacy-open=inventory` 的真实登录回归输出为：
`[LegacyOpen] requested=inventory type=InventoryDialog visible=True size=(284, 324)`，随后正常进入 `TestHero` 地图。该结果确认真实场景打开的是旧版 `InventoryDialog`，不是仅在测试场创建的占位窗口。

## 1. 本轮结论

旧版 EI 测试场已经可以独立启动，并复用正式窗口类、旧版 GameInter 贴图和旧版根矩形。自动审计通过，人物窗口本轮补齐了装备 enum 到旧版视觉格子的映射校验。

本轮确认完成的是“测试界面布局、资源和基础窗口路由”。这不等同于所有服务器业务已经完成迁移；真实 NPC 商店/修理、双客户端交易、行会数据、仓库存取、技能服务端回写和物品拖放仍须在有真实数据的登录场景逐项验证。

## 2. 启动与验收命令

在仓库根目录执行：

```bash
dotnet build GodotClient/ZirconClient.csproj --no-incremental

ZIRCON_UI_DATA_PATH=/home/tetsuya/mir3ei/LegacyEI/Data \
godot-mono --path GodotClient res://Scenes/LegacyHudLayoutLab.tscn -- \
  --window --legacy-audit
```

单独打开窗口时，把 `--legacy-audit` 换成：

```text
--legacy-open=character
--legacy-open=inventory
--legacy-open=magic
--legacy-open=horse
--legacy-open=npc
--legacy-open=chat
--legacy-open=quest
--legacy-open=trade
--legacy-open=guild
--legacy-open=storage
--legacy-open=config
--legacy-open=notice
--legacy-open=minimap
```

## 3. 自动审计结果

本轮实际输出：

```text
[LegacyAudit] PASS character=True inventory=True magic=True horse=True npc=True chat=True quest=True trade=True guild=True storage=True config=True notice=True minimap=True lifecycle=True roots=True
[LegacyAudit] character size=(244, 328) background=F200 toggle=(176, 264)/(36, 36) visibleSlots=8 slots=True switch=True
[LegacyAudit] inventory size=(284, 324) background=F250 grid=(6, 6)@(25, 41) close=(249, 288) action=(176, 262)
[LegacyAudit] magic size=(452, 380) background=F400 categories=8 skillSlots=12
[LegacyAudit] horse size=(296, 332) F850 buttons=(28, 244),(74, 244),(133, 244),(192, 244) state=0
[LegacyAudit] npc size=(552, 176) background=F1100 text=(20, 28)/(500, 112)
[LegacyAudit] chat size=(572, 388) background=F350 bodyAlpha=1 close=(536, 354)
[LegacyAudit] quest size=(340, 440) background=F700 contentAlpha=1 close=(304, 404)
[LegacyAudit] trade size=(484, 330) frame=1050 userGrid=(5, 6)@(14, 91) playerGrid=(5, 6)@(246, 91)
[LegacyAudit] guild size=(446, 596) frame=600 content=(410, 415)@(18, 80)
[LegacyAudit] storage size=(205, 205) frame=1001 grid=(4, 3)@(22, 43) compact=True
[LegacyAudit] config size=(248, 264) frame=750 legacyButtons=8 pageVisible=False
[LegacyAudit] notice size=(584, 252) frame=602 close=(548, 16) action=(496, 27) text=(500, 112)@(23, 94)
[LegacyAudit] minimap size=(200, 200) area=(-6, 18), (212, 188) panel=(212, 188) resize=True buttons=(36, 18)@(161, 24)/(36, 18)@(161, 42)/(0, 0)@(197, 60)
[LegacyAudit] lifecycle windows=15 open-close=True stack=0
```

## 4. 本轮重点修正

人物窗口的旧版静态证据确认：

- `EquipmentSlot.Helmet (2)` 在左下 `(27,264)`。
- `EquipmentSlot.Torch (3)` 在顶部 `(177,70)`。
- 左右手镯为 `(27,186)`、`(175,186)`。
- 左右戒指为 `(27,227)`、`(175,227)`。
- 毒药为 `(64,264)`，鞋为 `(103,264)`。

此前 Helmet/Torch 的两个坐标曾反向。本轮已修正，并将槽位、可见性、38×38 尺寸及坐标加入 `CharacterDialog.AuditLegacyEiLayout()`，避免只检查背景而漏掉物品格错误。

设置窗口也已确认使用 F750 的原始旧版画布，隐藏现代页签和表单，仅保留旧版 F760/F762 状态按钮及现有设置保存逻辑；不再把通用新版 `MenuDialog` 冒充旧版设置窗口。

## 5. 已验证与未宣称完成的边界

已验证：

- 项目无增量构建成功；仅保留仓库已有的 nullable/未使用变量警告。
- 测试场能从 `/home/tetsuya/mir3ei/LegacyEI/Data` 读取旧版资源。
- 13 个窗口的旧版根尺寸、背景帧、主要按钮/格子、正文区域和窗口根路由通过自动审计。
- 人物 F200/F201 可以切换并恢复，8 个已确认装备槽通过独立坐标校验。
- 测试场截图中已看到旧版人物、背包、技能、设置、公告等真实贴图，不是黑色占位窗口。
- 测试场生命周期审计逐个打开 15 个窗口，确认重复打开不会重复入栈，关闭最上层窗口后窗口栈恢复为 0。

尚未宣称完成：

- 空数据测试场不会凭空生成角色装备、背包物品、技能等级或 NPC 商品，因此截图不能替代真实数据交互验收。
- 角色换装/拖放、背包拿取/放下/拆分/丢弃、修理/储存模式，需要真实登录和对应服务端回包。
- 技能的完整 `Magic.exp` 分类、熟练度和服务端快捷栏回写需要真实角色数据点击验证。
- NPC 商店/修理、仓库存取、双客户端交易、行会成员/仓库/公告数据需要真实流程验证。
- 正式游戏仍通过 `--legacy-ui` 显式开关启用迁移布局；本阶段没有擅自改成默认替换。

这些项目是业务验收缺口，不是测试场贴图或根框缺失。下一轮应使用测试账号按窗口逐项打开、操作、关闭、再次打开，并记录服务器回包和截图。

## 6. 复现和交接

当前分支已经推送到：

```text
origin/ui/legacy-layout-lab
```

最近提交：

```text
a932092d 补充旧版测试界面验收报告
```

工作树在提交后应保持干净；任何后续窗口修改都必须重新执行构建和 `--legacy-audit`，不能仅凭 Godot 启动成功作为验收。

# 待处理：Godot EI NPC 对话界面 Review

- 日期：2026-10-01
- Review 执行者：OpenCode（`opencode/mimo-v2.6-flash-free`）
- 仓库：Zircon，基线 `cbf88fe2`（执行时 `master` 与 `origin/master` 一致）
- 审查范围：Godot 客户端 NPC 对话窗口/正文选项条、EI F1100 布局；只读 review，未修改源码。
- 状态：**发现一个源码结构可确认的裁剪/层级缺陷；F1100 最终位置和当前联机窗口画面仍待验证。**

## 结论摘要

EI 风格 NPC 对话界面并未完成可据以宣称像素/运行一致的验收。当前实现把用于绘制 EI 菜单条的控件挂在正文裁剪区内，和源码注释所说的“裁剪区之外”相矛盾。另有 F1100 背景定位假设尚未由原版目标矩形证据闭合。此前的联机截图被误认为 NPC 对话窗口；研究记录后续模板匹配已更正为聊天窗 F350，因此不能作为 NPC 窗口实机验收证据。

## 发现

### F1 — 菜单条绘制层被正文裁剪区裁切

**级别：高；源码结构已确认，具体画面影响需当前运行时截图复核。**

- `GodotClient/Controls/NPCDialog.cs:150-152`：EI 正文容器 `_textArea` 被设为 `(LegacyTextX, LegacyTextY)`，大小 `LegacyTextWidth × LegacyTextHeight`，并启用 `Clip`；相关常量为正文宽 149px。
- `NPCDialog.cs:344-346` 注释要求 383px 宽的 `_legacyStripLayer` 必须放在 `_textArea` 外，否则会裁切。
- 但 `NPCDialog.cs:357` 实际执行 `_textArea.AddControl(_legacyStripLayer)`；`NPCDialog.cs:359` 的 `MoveChild` 是对 NPCDialog 根控件调用，并不能改变该控件仍属于 `_textArea` 的事实。
- `NPCDialog.cs:352` 给条带绘制宽度设为 383px。由父控件裁剪边界与绘制宽度可确认二者不匹配；实现注释还记录曾观察到只剩约 200 逻辑像素可见，但本次 review 未重新运行截图验证该具体可见宽度。
- 同文件 `NPCDialog.cs:230-235` 的 `SyncStripLayerPosition()` 按“层是 `_textArea` 子控件”的坐标约定更新位置；若改挂根节点，必须同步调整坐标为窗口相对位置，不能只改 Parent。
- **最小建议**：把 `_legacyStripLayer` 挂到 NPC 对话窗根控件，定位到正文原点并应用滚动偏移；用根控件的 `MoveChild`/明确绘制顺序保证条带在正文文字下方。同步更新 `SyncStripLayerPosition()` 注释和坐标计算。然后用带多个菜单项的 EI 页面截图核实无裁切、滚动后条带与文字对齐。

### F2 — F1100 背景最终绘制位置尚无闭合证据

**级别：待证据，不作为已确认错位。**

- `NPCDialog.cs:139-148` 设置 NPC 根窗口为 552×176，使用 `GameInter` F1100，将图像控件放在 `(-64,-59)`；代码注释以 F1100 资源 alpha bbox 左上 `(64,59)` 为依据，将可见像素锚到窗口原点。
- Mir3-Research 的 `docs/research/ei-ui-layout/npc-window-render-evidence.json` 记录了 EI 原版窗口构造尺寸 552×176、F1100 背景，以及构造阶段写入 `this+0x520/0x524`、绘制阶段读取这些坐标字段的关系；但当前证据未闭合这些字段的最终运行值/屏幕位置。
- 独立资源审计记录 F1100 画布 512×256、alpha bbox `(64,59)-(448,197)`，可见尺寸 384×138。可见美术小于点击窗口矩形本身并不自动构成缺陷。
- **建议**：取得 `[0x520/0x524]` 的构造表达式/运行值，或同版本 EI 客户端截图后再决定是否改 `(-64,-59)`；不拉伸 F1100，也不改 552×176 命中区来“填满”背景。

### F3 — NPC 窗口真实联机画面目前未验收

**级别：未验证。**

- Mir3-Research `docs/research/ei-ui-layout/GODOT_UI_RUNTIME_ACCEPTANCE_2026-09-30.md` §10.17 更正：此前被称为 NPC 对话窗的联机截图，经 F350 模板匹配（平均差 2.9/255）实际是聊天窗 `LegacyChatDialog`。因此此前关于 NPC 窗口已打开、文本、选项按钮的结论均作废。
- 同文 §10.18.1 记录：两张截图均未找到 EI F1100 的有效匹配，NPC 窗口未被证实打开；原记录提出从相邻格点击 NPC 并确认 `S.NPCResponse` 到达后再验收。
- **建议**：在隔离/已授权的测试环境，从相邻格触发 NPC 对话，确认 `TrySendNpcCall → C.NPCCall → S.NPCResponse → OnNPCResponse`，再用 F1100 及正文/条带截图验收；区分窗口视觉证据与按钮/服务端行为证据。

## 非缺陷/不要误修

- 研究证据给出的 NPC 对话窗口尺寸、正文位置、关闭与滚动控件坐标，与 `docs/research/ei-ui-layout/GODOT_WINDOW_PARITY_MATRIX_2026-09-29.md` 的 NPC 条目一致；代码有对应设置。图标帧只能证明视觉状态，不能单独证明按钮业务语义。
- F1100 alpha bbox 较小不等同于资源被错误缩放；当前实现显式禁止拉伸。
- 商店商品面板是独立窗口语义，当前代码按屏幕坐标 `(0,184)` 放置，不应仅因其不是普通 NPC 对话文本窗而一概判为错位。

## 本次执行与限制

- Mimo 模型完成只读源码/研究资料 review，OpenCode 退出码为 0。
- 本次未启动游戏、未做新截图、未重新构建，也未改代码；F1 的真实画面宽度、F2 的 EI 最终位置、F3 的 NPC 联机打开链仍需针对性实测。
- Review 过程中原有工作区未提交修改被观察到；收尾时 `git status` 为干净，`HEAD` 与 `origin/master` 同为 `cbf88fe2`。本次文档提交只应包含本文件。

## 下一步

1. 修复 F1 父控件/裁剪/坐标问题，并用 EI 菜单选项与滚动截图复验。
2. 闭合 F2 的 EI 构造坐标证据；证据未闭合前不改背景定位。
3. 按 F3 重新做一次可复现的 NPC 联机打开与视觉/交互验收。

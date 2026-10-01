# 待处理：Godot EI NPC 对话界面 Review

- 日期：2026-10-01
- Review 执行者：OpenCode（`opencode/mimo-v2.6-flash-free`）；F1 修复与验证执行者：Agy（`gemini-3.8-flash-high`）
- 仓库：Zircon
- 审查范围：Godot 客户端 NPC 对话窗口/正文选项条、EI F1100 布局。
- 状态：**F1 菜单条裁剪/层级缺陷已修复并通过离线自检验证；F2 保持未决保留；F3 纳入 §10.19 联机自检证据并收窄未验证范围。**

## 结论摘要

EI 风格 NPC 对话界面的菜单背景条裁剪与层级缺陷已修复：通过引入专用的 384×136 垂直裁剪容器 `_legacyStripArea`，彻底解决了正文 149px 宽度硬裁剪与父节点 `MoveChild` 运行报错问题，实现了 384px 完整条带宽度、正确的图层层级（背景条在文字下方）以及精确的上下边界垂直裁剪和同步滚动。

F1100 背景定位假设尚未由原版目标矩形证据闭合，保持未决保留，严禁擅改。联机验证方面，此前遗漏的 `GODOT_UI_RUNTIME_ACCEPTANCE_2026-09-30.md` §10.19 已由端口自带 `--legacy-npc-response-selftest` 证实了 F1100 窗口定位、素材匹配、选项字形命中区及 `C.NPCButton` 发包链路；残余未验证项已明确收窄为真实 NPC 精灵点击触发链和服务端后续业务结算分支。

## 发现与处理状态

### F1 — 菜单条绘制层被正文裁剪区裁切

**状态：已修复并验证（2026-10-01）。**

- **问题回溯**：
  - `GodotClient/Controls/NPCDialog.cs:150-152` 将 EI 正文容器 `_textArea` 设为 `(LegacyTextX, LegacyTextY) = (150, 40)`，大小 `149 × 136` 并启用 `Clip = true`。
  - `NPCDialog.cs:357` 原代码错误地执行了 `_textArea.AddControl(_legacyStripLayer)`，导致宽 383/384px 的菜单背景条被 149px 的正文容器水平截断；
  - `NPCDialog.cs:359` 原代码对根窗口直接调用 `MoveChild(_legacyStripLayer, 0)`，由于父级不匹配在 Godot 运行时抛出 `ERROR: Child is not a child of this node`；
  - `_textColumn2.LegacyMenuStrips` 曾被置为 `true`，导致第二列容器内部重复绘制残缺的 149px 条带截断块。
- **修复方案**：
  - 在根窗口添加独立的 384×136 专用条带容器 `_legacyStripArea`（挂载于根节点，排在 `_textArea` 与 `_textColumn2Area` 之前），位置严格对齐正文原点 `(150, 40)`，并开启 `Clip = true`；
  - `_legacyStripLayer` 挂载在 `_legacyStripArea` 内部，绘制宽度设为 `LegacyStripWidth = 384`，移除错误的 `MoveChild` 调用，自然依靠树节点顺序保证条带位于文字下方；
  - `SyncStripLayerPosition()` 与 `ScrollLegacy()` 将条带层和第二列正文层的 Y 轴偏移与主正文严格保持一致，垂直方向由 `_legacyStripArea.Clip = true` 精确裁剪在 `[40, 176]` 视口内，杜绝向上或向下溢出；
  - 关闭第二列内部的重复条带绘制（`_textColumn2.LegacyMenuStrips = false`），由 `_legacyStripLayer` 统一承载完整跨列条带。
- **验证证据**：
  - 构建：`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 编译通过（0 错误，仅既有无关 CS8632/CS0219 警告）；
  - 几何断言：`LegacyHudLayoutLab --legacy-hud --legacy-audit` 全项 PASS，包括 `AuditLegacyEiLayout` 中新增的 `_legacyStripArea` 位置 `(150,40)`、尺寸 `(384,136)` 及 `Clip=True` 校验；
  - 运行时交互自检：`LegacyHudLayoutLab --legacy-hud --legacy-npc-selftest` 运行通过（`PASS failures=0`，`move_child` 报错彻底清除）；
  - 视觉取证：截图（`npc-f1100-self-01-open-top.png`、`npc-f1100-self-03-scrolled-max.png` 等）像素采样显示菜单条完整横跨 384px（从 x=150 到 x=534），右端金属边框完整可见；滚动到底部与回滚时，顶部和底部裁剪边缘干净，文字与背景条无错位。

### F2 — F1100 背景最终绘制位置尚无闭合证据

**级别：待证据，保持未决保留（Pending）。**

- `NPCDialog.cs:139-148` 设置 NPC 根窗口为 552×176，使用 `GameInter` F1100，将图像控件放在 `(-64,-59)`；代码注释以 F1100 资源 alpha bbox 左上 `(64,59)` 为依据，将可见像素锚到窗口原点。
- Mir3-Research 的 `docs/research/ei-ui-layout/npc-window-render-evidence.json` 记录了 EI 原版窗口构造尺寸 552×176、F1100 背景，以及构造阶段写入 `this+0x520/0x524`、绘制阶段读取这些坐标字段的关系；但当前证据未闭合这些字段的最终运行值/屏幕位置。
- 独立资源审计记录 F1100 画布 512×256、alpha bbox `(64,59)-(448,197)`，可见尺寸 384×138。可见美术小于点击窗口矩形本身并不自动构成缺陷。
- **结论与约束**：在取得 `[0x520/0x524]` 的构造表达式/运行值或原版实机同版本客户端截图之前，保持 `(-64,-59)` 不动，不拉伸 F1100，不改 552×176 命中区，严禁无证据修改。

### F3 — NPC 窗口真实联机画面验收状态

**级别：部分已验证（收窄未验证范围）。**

- 此前报告指出“NPC 窗口没有任何联机验证”，该表述遗漏了新近的 `GODOT_UI_RUNTIME_ACCEPTANCE_2026-09-30.md` §10.19 证据，需予纠正收窄。
- **已由 §10.19 联机自检证实的部分**：
  - 运行环境：联机 TestHero，地图 1，触发 `--legacy-npc-response-selftest`；
  - 走通链路：复用真实绑定的 `NPCPage` 派发 `S.NPCResponse` → `OnNPCResponse` → `ShowPage`；
  - 窗口定位：F1100 在预测点 `(128,96)`，模板搜索匹配最优差 15.4（差额源于上方叠加的文本与按钮）；
  - 布局与发包：4 个选项、16 个字形命中区全部就绪，点击选项行正确发出 `C.NPCButton`（id=1 与 id=2 已获捕获）。
- **残余未验证事项（明确边界）**：
  1. 真实 NPC 精灵的点击打开链：在无头环境下通过鼠标点击场景中的 NPC 精灵驱动 `TrySendNpcCall`（此前无头 xdotool 尝试未能命中 `MouseObject`）；
  2. 选项点击后服务端的后续业务分支：如买卖面板呼出、金币扣减、修理成功等业务结果，需真实服务端业务配合观察。

## 非缺陷/不要误修

- 研究证据给出的 NPC 对话窗口尺寸、正文位置、关闭与滚动控件坐标，与 `docs/research/ei-ui-layout/GODOT_WINDOW_PARITY_MATRIX_2026-09-29.md` 的 NPC 条目一致；代码有对应设置。图标帧只能证明视觉状态，不能单独证明按钮业务语义。
- F1100 alpha bbox 较小不等同于资源被错误缩放；当前实现显式禁止拉伸。
- 商店商品面板是独立窗口语义，当前代码按屏幕坐标 `(0,184)` 放置，不应仅因其不是普通 NPC 对话文本窗而一概判为错位。

## 验收结论与工作区状态

- F1 菜单条裁剪/层级缺陷已由代码结构修复并经独立测试场及自检证实闭合；
- 本次改动仅限白名单文件：`GodotClient/Controls/NPCDialog.cs` 与本报告文件；
- 编译通过、自检通过、无无关改动残留。

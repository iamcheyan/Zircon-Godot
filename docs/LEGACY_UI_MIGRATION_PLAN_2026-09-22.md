# 旧版 UI 迁移计划

## 目标

以 `/home/tetsuya/development/Mir3-Research/` 中最早版客户端的反编译证据为视觉基准，将当前 Godot 客户端逐步改造成旧版风格，同时保留当前已经可用的网络、交互和玩法逻辑。

原则：旧版资料决定“长什么样、摆在哪里”，Godot 代码决定“怎么交互、怎么工作”。不直接移植旧版 WinForms/DX 控件的事件实现。

> **状态复核（2026-09-24）：**下方各阶段的“已完成”是本计划早期阶段性记录，不代表旧版 EI UI 已达到逐窗、逐控件的行为与视觉一致，也不构成本次审计的验收结论。后续 primary-static 与当前源码交叉审计发现技能书、状态/装备、任务、HUD入口、背包/提示等仍存在已证差异或外部证据阻塞，详见 [旧版 EI UI 全量审计](LEGACY_EI_UI_AUDIT_2026-09-23.md)。本计划保留为历史实施记录；新的完成状态以该审计矩阵逐项复核及关联验收证据为准。

## 五个阶段

### 第一阶段：旧版 UI 基准数据（已完成）

- 读取旧版 `window_layout.json` 和 `layout.json` 的反编译结果。
- 统一记录窗口名称、800×600 坐标、尺寸、`GameInter.wil` 帧号、可移动属性和证据等级。
- 在 Zircon 内生成可读取的 `GodotClient/UI/legacy_ui.json`。
- 明确已确认窗口与 candidate 窗口，禁止把推测布局当作确定事实。

验收：基准数据包含旧版 13 个窗口，坐标和尺寸与原始证据一致，800×600 → 1024×768 的换算规则明确。

### 第二阶段：Legacy UI 控件皮肤（已完成）

- 在 Godot 中抽取 `LegacyWindow`、`LegacyButton`、`LegacyLabel`、`LegacyImage`、`LegacyItemCell` 等公共控件。
- 复用旧版 `GameInter`、`Interface1c`、`Inventory`、`Equip` 等资源。
- 当前窗口逻辑、网络和事件绑定保持不变，只替换视觉控件。

### 第三阶段：双数据源 UI 编辑器（已完成）

- 扩展现有 `Mir3-Research/Tools/uieditor`。
- 同时加载当前 `ui_tree.json` 和旧版 `legacy_ui.json`。
- 支持旧版参考层、旧版截图 underlay、控件框叠加、坐标差异显示和坐标复制。
- 继续使用 `ui_overlay.json` 与 F12 热加载，不改变现有编辑闭环。

### 第四阶段：核心窗口迁移（已完成）

按主面板、背包、角色/装备、技能、NPC 对话、聊天、菜单、地图窗口的顺序迁移。每个窗口都要完成视觉对位、交互回归和截图记录。

### 第五阶段：清理与扩展（已完成）

- 隐藏或移除已经决定暂时禁用的商城、宠物、修炼、隐视、副本等 UI。
- 对保留的声望、称号、宝石、钓鱼等玩法继续使用统一 Legacy UI 控件。
- 建立旧版/当前版 UI 差异审计和回归截图。

## 坐标约定

旧版反编译 UI 使用 800×600 逻辑坐标，Godot 客户端使用 1024×768 逻辑画布。迁移时使用：

```text
GodotX = LegacyX × 1024 / 800
GodotY = LegacyY × 768 / 600
```

资源帧号不能只凭名称猜测；必须同时记录来源文件、帧号和证据等级。`candidate` 只作为参考预览，不能直接用于最终布局。

## 当前阶段来源

- 旧版窗口基准：`Mir3-Research/docs/research/ei-ui-layout/window_layout.json`
- 旧版完整布局/绘制证据：`Mir3-Research/docs/research/ei-ui-layout/layout.json`
- 旧版布局结构：`Mir3-Research/docs/research/ei-ui-layout/layout.schema.json`
- 当前 Godot 编辑器：`Mir3-Research/Tools/uieditor/`

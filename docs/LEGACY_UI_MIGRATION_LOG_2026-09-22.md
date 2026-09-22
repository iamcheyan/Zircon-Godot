# 旧版 UI 迁移实施记录

目标：以 Mir3-Research 中最早版客户端的反编译证据为视觉基线，保留 Godot 当前的网络、交互和玩法实现。旧版资料只决定坐标、尺寸、资源帧和视觉规则；业务逻辑仍由 Godot 控件负责。

## 阶段提交与验收

| 阶段 | 内容 | 提交 | 状态 |
|---|---|---|---|
| 1 | 13 个旧版窗口基准、证据等级、800×600 换算 | `49438d6b` | 已完成并推送 |
| 2 | 公共 Legacy UI 皮肤读取器、窗口视觉默认值、可复用 profile 应用接口 | `cee7ab1b` + 本记录提交 | 已完成 |
| 3 | 仓库内双数据源编辑器：旧版参考层、当前控件树、换算和 overlay 下载 | 本记录提交 | 已完成 |
| 4 | 核心窗口使用统一 Legacy profile 适配入口，保留原有动态布局与交互 | 待提交 | 已完成 |
| 5 | 禁用功能 UI/快捷键/服务端边界审计，保留三职业和体验扩展玩法 | 待提交 | 已完成 |

## 阶段 1 证据

来源为 `Mir3-Research/docs/research/ei-ui-layout/window_layout.json`、`layout.json` 和对应提取脚本。`GodotClient/UI/legacy_ui.json` 当前记录 8 个 confirmed、5 个 candidate；candidate 不得直接用于最终布局。

## 阶段 2 规则

`LegacyUiSkin` 延迟读取 `legacy_ui.json`，提供资源库/帧号、旧版坐标转换、标题色、窗口底色、阴影以及 `ApplyWindowProfile`。默认只应用公共视觉，不强制覆盖窗口动态尺寸；显式传入 `geometry: true` 且 profile 已 confirmed 时才应用位置和尺寸。

## 阶段 3 使用记录

`Tools/LegacyUiEditor/` 是仓库内可复现编辑器。它从 HTTP 相对路径同时载入两份 JSON，在两个 800×600/1024×768 画布显示窗口框，展示换算结果；candidate 会被拒绝复制。下载的条目可人工合并到 `ui_overlay.json`，由现有 F12 热加载闭环验证。

## 阶段 4 实施记录

`GameScene` 初始化核心窗口后统一调用 `ApplyLegacyCoreWindowProfiles`，当前接入背包、角色、任务、设置、组队和 NPC 对话窗口。公共视觉立即生效；几何覆盖保留为 confirmed profile 的显式开关，避免破坏当前窗口内部动态布局。后续需要逐窗截图对位时，可在单个窗口调用 `ApplyWindowProfile(window, geometry: true)`，不影响其它窗口。

## 阶段 5 实施记录

功能范围清理已完成：三职业、基础循环、自动巡路、自动战斗、套装、宝石、强化、钓鱼、声望和称号保留；刺客新建、隐士、修炼、宠物、商城、寄售和副本入口关闭。客户端入口、快捷键/回调和服务端配置拒绝分别核对，具体矩阵见 `LEGACY_UI_PARITY_AUDIT_2026-09-22.md`。

最终运行验收已完成：`login_game.sh -R` 强制重建 ServerCore 后，测试账号成功进入游戏，客户端加载地图、玩家和怪物；服务端记录 `StartGame player.StartGame() 完成`。

## 核心窗口真实替换进度

2026-09-22 开始实际底图迁移：新增 `LegacyUiFrame`，直接读取旧版图库帧并拉伸到当前窗口，不再只依赖通用皮肤颜色。当前已接入：

- 背包：`GameInter[250]`；
- 角色/装备：`GameInter[200]` 属性视图、`[201]` 查看装备视图；
- 设置：`GameInter[750]`；
- 任务：`GameInter[700]`；
- 组队：`GameInter[900]`。
- 技能窗口内容区：`GameInter[400]`。
- 聊天记录区：`GameInter[350]` 半透明旧版底图。
- 仓库/储存窗口：复用旧版背包底图 `GameInter[250]`，保留当前仓库网格逻辑。
- 聊天选项窗口：使用旧版设置底图 `GameInter[750]`。

业务控件和事件暂时保留，下一轮继续按旧版证据替换窗口内部按钮、标签和坐标。

## 验证约定

- 每阶段运行 `git diff --check`。
- Godot 端运行 `dotnet build GodotClient/ZirconClient.csproj --no-incremental`。
- JSON 基准用独立脚本与旧版 `window_layout.json` 比对，不复用生产解析器。
- 编辑器用本地 HTTP 服务和 `curl` 检查页面及两份 JSON 可达。
- 涉及进游戏的窗口迁移，最终必须用测试账号完整登录并截图回归；不能仅以编译通过代替行为验证。

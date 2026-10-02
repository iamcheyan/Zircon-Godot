# GodotClient HUD 全面复核与修复

## 仓库与约束
- 工作目录：`/home/tetsuya/development/zircon`，当前分支 `master`，远端 `origin`（iamcheyan/Zircon），推送 `origin/master`。
- 现有 Godot UI 交互审计 goal 已 completed，不要复用/追加旧会话；新任务独立运行。
- 开工先读仓库 `AGENTS.md`、`docs/CLIENT_UI.md`、`docs/CLIENT_WORLD_UI_SCALING.md`，以及 Mir3-Research `docs/handoffs/MIR3_UI_REVERSE_ENGINEERING_DOC.md` 与能找到的原版 HUD 审计资料。检查 git 状态、近期提交、并发 goal/改动，保护任何非本任务文件。仅针对可确认的 HUD 缺陷改代码。
- WorkBuddy 模型按用户要求使用 OMP；启动时显式 `--model workbuddy/global:deepseek-v4.1-flash`（若不可用先验证/报告，不擅自换付费模型）。无人值守用 `--auto-approve`。
- 每完成一个独立逻辑修复批次，立刻只 stage 本任务文件，中文 commit，push `origin master`，并用 `git ls-remote origin refs/heads/master` 核实远端 SHA。不得积攒到最后，不得 force push。

## 目标
对 GodotClient HUD 做完整、细致的视觉与原版对应审计并修复，用户已明确指出：右下角 AC/DC 文本错位；HUD 混入许多 Zircon UI 元素；部分布局/元素和原版对不上。示例不是完整范围。

逐项盘点游戏内 HUD 全部元素：底板/资源、生命/魔法/经验等条、数字与标签（包括 AC/DC 等属性）、职业/等级/地图信息、快捷栏、按钮/图标、状态/目标提示以及各模式下显示条件。分别审计 EI legacy HUD 与 Zircon UI 模式；确认命令行开关和资源选择，避免把模式混淆。对照原版 `Client/` 源码、布局/资源引用与 Mir3-Research 证据，建立控件级矩阵：原版位置/尺寸/字号/对齐/资源/条件 → Godot 实际实现 → 差异 → 证据/结论。

优先追查右下角 AC/DC：定位标签与值的布局计算、字体基线/对齐、逻辑坐标与 UiScale，给出实测坐标和根因；不要仅凭截图目测随意挪像素。对所谓 Zircon 元素先查来源与模式，不得未经原版证据直接删除。产品差异/证据冲突不能擅自定案，记录阻塞并回报。

## 执行与验收
1. 建立任务专属报告/截图目录 `.artifacts/godot-hud-parity-2026-10-02/`，报告持续记录清单、逐项结果、文件/行号证据、commit SHA、残余阻塞。截图为真实 Godot 运行画面，禁止合成。
2. 复用现有合法测试设施，运行客户端进入游戏；检查至少两种常见窗口宽高比/尺寸，并覆盖 legacy 与 Zircon UI 模式、相关 HUD 显示开关。截图逐项检查文字错位、裁切、重叠、元素误混、资源不符。参考游戏仓库和 Mir3-Research 中现成无头验证工具，勿另造重复工具。
3. 每批修复后 dotnet build GodotClient/ZirconClient.csproj；然后真实重启客户端复测，保留截图证据。最终复测完整 HUD 和变更影响面，`git diff --check`，确保测试客户端及本任务启动的进程退出。
4. 高频小批提交+push，每次验证远端 SHA；绝不提交其他 goal 的改动/截图。最终检查 git 状态、HEAD、远端 SHA、构建结果、真实运行截图和遗留项。
5. 不得因先前 UI 交互审计完成而声称 HUD 已验收；前一轮未覆盖现代 Zircon UI，本 goal 必须分别核验模式。

## 完成标准
所有可复现且有证据支持的 HUD 差异已修复；AC/DC 错位经多个尺寸真实截图复验；HUD 元素逐项映射并明确哪些是 EI legacy、哪些属于 Zircon 模式；报告有可复核的证据/截图；每个修复批次均已 push 且远端 SHA 对齐。无法验证的差异明确标记阻塞，不谎报完成。
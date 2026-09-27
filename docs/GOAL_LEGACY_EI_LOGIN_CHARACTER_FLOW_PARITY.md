# Goal：EI 登录—选角—创建/删除—进游戏整流程还原

## 目标

以 EI 原版反编译证据、EI 原版客户端代码/资源与当前 GodotClient 代码逐阶段核对，把 Legacy EI 模式的启动、登录、洞窟选角、角色动画、创建、删除/确认、返回、进入游戏过场与错误/取消流程做成连贯且可实测的原版行为；修复已经确认的问题，逐项留存截图和运行证据。不能只做一个静态菜单或只修某张截图。

用户当前观察：原版进选人页是洞穴背景，人物在洞穴场景中显得较小并有动作；进入游戏时人物可能还会动；创建/删除角色会切换到另一画面/人物比例发生变化。以上是待核查的观察线索，**不得未经证据就把它们当成原版事实**。请确定每个阶段实际使用的背景、人物模型/帧、尺寸/锚点、视频或动作、切换时机，再实现。

## 仓库、分支与边界

- 工作目录：`/home/tetsuya/.hermes/cache/scratch/zircon-ei-login-test`（已注册的 Zircon worktree，基于刚抓取的最新 `origin/ui/legacy-layout-lab` 提交 `ca6b01abd07bb20db7c1826d233240e519aa68f3`，另含已推送的动画截图证据提交 `68b2f970`）。本目标分支：`goal/legacy-ei-login-select-flow`。
- 原用户 checkout `/home/tetsuya/development/zircon` 当前停在 `9555c0c4`、落后远端 215 个提交且有未跟踪 `.artifacts/`；**不要在那里切分支、合并、覆盖、清理或暂存这些文件**。当前 worktree 是为了保全它们而使用的隔离工作区。
- EI 本机运行资源：`/home/tetsuya/mir2ei/LegacyEI/Data`。测试时显式设置 `ZIRCON_LEGACY_UI_DATA_PATH`，不要静默回退至 `/home/tetsuya/mir2ei/Data` 的现代素材。
- 本目标只触及登录/选择人物/创建/删除/过场流程及直接依赖，不重构游戏内 HUD、其他窗口或网络协议之外的子系统。避免为“看起来一样”随意更改通用 DX 控件。
- 绝不触碰用户原版 `Client/` 源码；它只作为只读参考。目标代码通常在 `GodotClient/Scripts/LoginScene.cs`、`SelectScene.cs` 及最少量直接依赖。
- 禁止 `git reset/clean`、覆盖既有用户资产、把数据库/日志/密钥/账号密码截图提交。不能提交 EI 素材二进制或运行数据库。

## 必读权威资料（先读再改）

1. Zircon 仓库指引：`AGENTS.md`，特别是验证深度/登录流程必须真机行为验证条款。
2. EI 综合审计：`docs/LEGACY_EI_UI_AUDIT_2026-09-23.md`。先读总则、首轮差异及 **PRE-01..PRE-18**（约行 486–516），再读后续与登录/选角有关的具体复核/更新章节；该文档持续追加，早期摘要可能被后文纠正，须沿时间顺序核对最新结论。
3. 启动/媒体资源说明：`Docs/LEGACY_LOGIN_BOOT_FLOW.md`。
4. 逆向研究仓库：`/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/`：
   - `RESEARCH_LOG.md` Finding 267（登录状态机/协议 primary-static，约行 5085 起）及其后续针对登录选角的更正；
   - `UI_COMPLETION_AUDIT.md` 中对应登录/选角研究回合；
   - `login-flow-evidence.json`、`login-charselect-flow-evidence.json`、`login-flow-final-verification-evidence.json`、`intro-splash-state-machine-evidence.json`；
   - 只读查看 JSON 原始 trace、证据级别、候选/冲突/待证字段，不要只依据 RESEARCH_LOG 摘要。
5. 原版 C# 参考：`Client/Scenes/LoginScene.cs`、`Client/Scenes/SelectScene.cs`、`Client/Controls/NewCharacterDialog.cs`、动画/窗口控件实现。它们是仓库保留的旧客户端代码，**不能自动视为与逆向目标 EI 3.0 完全同版**；仅在版本/行为得到交叉证实时作为实现参考，不得用旧代码推翻 EI primary evidence。
6. 当前 Godot 具体入口：`GodotClient/Scripts/LoginScene.cs`（`BuildLegacyLoginUi`、`ApplyLegacyEiLoginLayout`、`PlayLegacyBootLogo`）、`GodotClient/Scripts/SelectScene.cs`（`RefreshList`、`SelectSkinCharacter`、`UpdateCharacterDisplay`、`UpdateCaveSlots`、`SetSelectPhase`、`PlayLegacyTransition`、创建/删除/进入处理），`GodotClient/Controls/DXAnimatedControl.cs`、`DXImageControl.cs`、`MirSkin.cs`、`AutoLoginArgs.cs`、`ServerConnection.cs` 与对应 client/server packet 定义。

## 已知事实、风险与必须重新核实处

- Finding 267 给出了 EI 模式字节、启动栈、登录对象、parent 选角对象、按钮帧、协议消息与 phase 状态机。重点链：启动/登录→`0x208` 角色列表→F51 创建/`0x64` 等旧消息与 phase 1/2/3，或 F55 开始/`0x67`→只在 `0x20D` 成功响应后 phase 4/`StartGame.dat`→游戏模式。按实际 trace 逐项追读，不要把相同数字消息号映射为 Zircon 包。
- 目标 EI `Interface1c.wil` 为 WIL/WIX，F50 是 640×480 选角洞穴背景；F51/F53/F55/F57 有可见文字图，分别是创建/删除/开始/结束的资源候选。F92/F95/F98/F86/F89 控件的业务语义有 candidate，不能仅凭图形猜成职业选择/翻页/确认动作。
- 研究版 EXE 身份与本机 EI 安装包尚未完全闭合；某些“两个槽”等逆向字段不代表可见上限/槽位业务必然已确定。明确写出来源、构建身份与证据级别，冲突留 unresolved，不把旧摘要说成铁律。
- `CreateChr.dat`、`StartGame.dat`、`ei_Login.dat`、`wemade.dat` 是运行媒体；当前最新文档记载的 `.ogv` 是转码播放产物。视频出现 ≠ 阶段/顺序/循环/输入锁定与 EI 一致。逐段确定是否由实际 EI 构造/分发链触发，检查裁切尺寸、时长、遮罩、播放完回到哪个 phase。
- 已知当前实现并非空白起点：`SelectScene` 已有洞窟 F50、两个槽位动画、创建/开始 `.ogv` 过场、阶段按钮门控等代码。其注释中的坐标、脚底线、帧组、2 槽、角色帧分组与“原版常驻动画”等多处是推导/假设，须逐项验证，**不能直接信代码注释或自测 PASS**。已审计发现的高风险点包括：旧过场状态映射不完整、`RefreshList()` 选择首项、角色对象的字段/角色动画与页面人物大小及锚点不等价、F50 根坐标/800×600 vs 1024×768、旧 EI WIL 素材与 Zircon 动画控制语义；创建成功后的页面可见性、删除确认/成功/失败路径、StartGame 成功响应后的动画也需真实回放。
- 补充的源码逐路径检查重点：旧 `Client/Scenes/SelectScene.cs` 删除请求有 5 秒确认延迟并携 checksum（约 L577–610、1251–1286）；Godot `SelectScene.cs` 删除走通用确认框后立即发包（约 L1303–1337），不能因操作结果相同就认为 EI 时序相同。Godot start path 的确认/等待/Delays/Success 分别约 L1240–1392；必须逐条对 EI 原始 handler 回包核。
- **过场生命周期高风险**：Godot `PlayLegacyTransition()` 在 `_uiLayer` 下创建 `VideoStreamPlayer`，但选角成功后随即切换/释放 `SelectScene`。必须跟踪 `_uiLayer` 所属节点树及 `QueueFree` 时序，实测 StartGame 视频是否能完整播放到尾帧后再入世界；不能仅凭“开始播放”日志判定通过。创建过场也要核它和创建请求/回包的准确先后、失败时清理/返回。
- 必须对照原始证据工件 `char-select-stage-machine-evidence.json`、`char-select-enter-layout-evidence.json`、`char-select-visual-verification-evidence.json`、`interface1c-parent-context.json`，并从 `login-flow-evidence.json`/`login-charselect-flow-evidence.json` 交叉核：EI phase 0–4（不要误用 Godot phase 编号）、服务器消息阶段、视频、音效、5 阶段字段。`UI_COMPLETION_AUDIT.md` 的 Finding 267/F349/F541/F603 索引和审计日志只用于导航，逐字段 trace/原始指令优先。
- 视频 `.ogv` 运行日志能证明播放器启动，但尚不证明 EI `.dat` 内容转换逐帧相同、30fps/总时长/黑场尾帧/音效/裁剪与阶段门一致；逐文件跑 `ffprobe` 并抽关键帧和运行捕获对照。EI 文件与原版 EXE 的构建身份不能凭文件名推成同版。

## 执行顺序

### A. 先产出逐阶段差异矩阵，再动实现

对每个 stage 单列：登录表单/服务器选择（仅限当前协议支持范围）、角色列表/选中/空槽、创建过渡、创建编辑阶段、创建确认/响应、删除确认/响应/取消、返回选择、开始确认/等待、StartGame 过场/结束、服务器错误/超时/断线。每行记录：

- 原版对象/phase、消息或输入触发、EI 媒体及图集帧、真实画布与 RECT、角色大小/动画/锚点、显示层级/裁剪/时长/是否可交互；
- 对应 EI 反编 trace 和证据级别；本机 EI 资源文件/帧头的独立核验；
- 当前 Godot 控件/状态/协议/实屏表现；
- 已确认差异、未知项、实现决策、可复现验收步骤。

当文档/逆向/源码/资源相互矛盾时保留冲突，继续追踪原始指令/调用者/资源；不要根据用户记忆、Godot 现有常量、资源帧“看起来像”或旧 C# 代码来臆断 EI 语义。

### B. 最小范围实现

按矩阵修复当前 Legacy EI 模式，使每个 phase 有明确 enter/update/exit/lifecycle，视频和人物动画按正确事件切换，不残留旧场景控件/多个重叠视频/隐藏窗口抢焦点；鼠标 hit rect 与显示像素一致；进入/取消/失败后焦点、状态和按钮恢复正确。保持非 Legacy Zircon 流程不回归。旧 EI 网络协议与 Zircon 网络协议不兼容时，只实现能明确映射的 UI/时序；若需用户拍板或协议扩展才能忠实还原，把阻塞项写入报告，不伪造包、不擅自改服务端协议。

人物大小、洞窟里的角色循环/入场动作、创建/删除时“角色变大/另一个画面”、StartGame 中人物是否动作，必须有可引用的 EI trace/原版代码/同态画面至少两类证据支持，再实现。若证据只支持场景视频，不要把角色模型做成未经证实的跑步动画；在结果中明确无法确认之处。

### C. 运行验收（必须真实交互）

- 编译：`dotnet build GodotClient/ZirconClient.csproj --no-incremental`（或记录等价成功命令）。
- 在 Xvfb/openbox 独立测试显示器上用最新目标 build、本机资源根运行。优先 EI 800×600 基准；另验 1024×768，并可选一个放大倍率。截图必须是客户端窗口完整 viewport，不用桌面缩略图替代。
- 至少真实逐项回放并留图/日志：启动 WeMade → 登录视频/表单 → 进入 F50 洞穴选角（0/1/多个角色的可见状态、角色大小/动作、选中和切换）→ 打开创建 → 创建媒体结束到编辑/确认 → 成功回列表 → 删除确认取消 → 删除成功后列表更新 → 进入游戏按钮/等待响应/StartGame 视频结束到世界 → 错误/失败路径至少一项。记录发送/接收包类型、phase 和实际截图；启动日志不能替代每项输入回放。
- 测试数据安全：7000 上正在运行的服务端不得被停止、重启或写入/删除角色。严禁删除 `TestHero` 或真实 Users.db 角色。创建/删除验收必须使用独立临时 Users.db/独立临时 ServerCore 运行副本及专用临时账号；先证明隔离成功。若无法安全准备隔离环境，跳过会写数据库的回放，将它标 `blocked` 并给出原因，不得对真实数据库试探。
- 每个关键状态截屏至少一张，保存于 `screenshots/legacy-ei-character-flow-2026-09/`，并写简短索引说明文件；用视觉检查逐张检查裁切、大小、遮挡和素材，日志/构建结果一起记录。不得提交原始完整 Godot 日志或敏感账号字段。

## Git 与交付

- 在本分支逐阶段 commit，仅 stage 本 goal 文件/目标源码/本次截图与短证据索引；不要回收或提交其他 goal artifacts。用户偏好有改动就提交并 push；每次 push 后 `git ls-remote` 验 SHA。
- 与 UI/协议/动画实现直接无关的文件不得修改。保留已有未跟踪/历史截图。至少在最终提交前运行 `git diff --check`、build、逐阶段 runtime tests 并检查最终 diff。
- 最终报告区分 `completed / blocked / not tested`，列出阶段矩阵与截图路径、commit/push SHA、环境/分辨率、实际操作、build/日志结果、剩余证据差异；不可把自检断言当运行验收。

## 终态

整体 UI 流程与 EI 证据逐阶段一致并完成真实交互回放，或将无法闭合部分明确记为 blocked 并列出所需证据/决策。实现完成不代表任务完成；不能在只编译、只启动登录页或只验证自动登录的状态下报告 complete。

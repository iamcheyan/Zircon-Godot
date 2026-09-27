# EI 旧版 UI 全量审计（进行中）

更新时间：2026-09-24
状态：按用户确认的逐窗对照/修改标准重新启动长期迁移校准；原版界面目录与首轮差异已有记录，逐控件审计、修复和运行验收仍在进行，尚未宣称任何窗口完成旧版一致性验收。

## 审计目标与证据规则

本审计以 EI 3.0 的 800×600 原版为目标，逐项对照 `GameInter.wil` 等 EI 资源、原版 `Mir3.exe` 的反汇编证据、可用的原版 `Client/` 源码，以及 Zircon 的真实登录运行状态。主要研究资料在 `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/`。素材浏览器 `http://localhost:8766/` 用于检查源帧。UI 运行验收的标准启动命令是在仓库根目录执行 `bash login_game.sh legacy`；该脚本会先关闭现有客户端，因此审计期间仅在明确需要新基线时重启。

证据等级：

| 等级 | 含义 | 可支持的结论 |
|---|---|---|
| primary-static | 原版 EXE 的构造、绘制、命中或输入路径中的静态机器码证据 | 帧号、常量、调用链、静态状态转换；不自动证明运行时最终位置和可见顺序 |
| source-confirmed | 原版可用 Client 源码中可追踪的构造/事件/业务逻辑 | 源码明确覆盖的行为；需确认该源码与 EI 目标版本一致 |
| visual-candidate | WIL 帧、截图、模拟器或布局工具推导 | 视觉参考或候选坐标，不能单独证明点击语义 |
| runtime-verified | EI 原版或 Zircon 实际运行中重现并记录截图/操作结果 | 只覆盖已记录分辨率、数据状态和操作路径 |

**Godot legacy 测试树裁剪约定（静态源码确认）：**`bash login_game.sh legacy`会传`--legacy-ui --legacy-hud`；`GameScene.ApplyLegacyCoreTestLayouts()`逐窗调用`LegacyUiSkin.ApplyLegacyTestWindow()`，该wrapper先设根`Clip=true`再调用专用布局。除非窗口布局主动重置，所有经此路径的根都以`DXControl.Clip → ClipContents`裁其后代。此结论只适用于该Godot测试树，不自动表示EI原版相同；ClipContents是绘制裁剪证据，也不能代替逐项hit-test分析。

**目标二进制身份未闭合（2026-09-24）：**研究工件标记的第一证据为`/tmp/nas_mnt/NAS/TMP/EI传奇3.0客户端/Mir3.exe`（多份工件也记录`/home/tetsuya/NAS/TMP/EI传奇3.0客户端/Mir3.exe`）；本机`/home/tetsuya/mir3ei/Mir3.exe` SHA-256 为`bd0909ae7b4e5ed49300f573e45a2c073a7cd8bc8da21a553be0a6bc2973fa15`。直接检查该本机 PE32 的`.text`发现，研究工件称为`0x00427904 → call 0x00439250`的调用在该二进制对应位置不存在，且指令边界不同；故不能证明其与研究时反汇编的 EI 文件同版。NAS GVFS 挂载当前未连接并请求凭据，本轮没有取得该研究源 EXE，也没有用别的副本替代。现有研究 JSON/Markdown 暂保留为“研究工件中的 primary-static 结论”，但在核对目标 EXE SHA-256/版本之前，本轮不能把它升级为对`/home/tetsuya/mir3ei/Mir3.exe`的直接复核，也不能据该本机二进制地址继续推导新语义；EI WIL 像素和当前 Godot 代码/runtime 证据独立记录，不受此二进制身份差异影响。恢复准确 EXE 后，逐个窗口抽核关键函数 VA 与证据摘录，再决定哪些历史结论维持或撤回。

若证据彼此冲突，保留原始来源、明确差异并追踪，不以 Zircon 当前常量、模拟器生成值或“自测 PASS”反证原版。每个窗口最终需要同时审查外框有效像素边界、原点/锚点、缩放/裁剪、绘制顺序、控件命中矩形、按下/悬停/禁用状态、键鼠操作及动态数据来源。

## 最终验收标准（用户确认）

“完全一致”指同一分辨率、同一游戏状态、同一角色/物品/服务器数据下，EI 原版与 Zircon 的玩家可见界面和操作结果一致。Godot 内部代码结构可以不同；比较对象是实际显示、输入响应、界面流转和业务结果。

| 验收面 | 通过标准 | 必须留存的验证证据 |
|---|---|---|
| 画面与素材 | 同一状态下逐窗核对内容、EI资源帧、可见文字、字体/颜色、位置、有效像素边界、裁切、缩放和层级；固定素材区域做像素差分，动态数值/动画等可变区域注明比较规则。基准分辨率为 EI 800×600；其它分辨率按原版锚点和缩放规律复核 | 原版素材/反编译证据、Godot同状态截图、分辨率与数据状态、标出比较区域及差分结果 |
| 控件 | 每个按钮、页签、列表行、物品格、输入框和滚动控件的RECT、悬停/按下/禁用状态与点击结果相符；帧画布、alpha有效边界和命中框分别记录 | 原版构造/命中证据、独立资源像素测量、Godot实际控件矩形和边界点点击记录 |
| 输入与行为 | 同一鼠标/键盘输入在相同修饰键、焦点、聊天输入、模态窗口和开关状态下触发相同动作；比较节流、拖拽、滚动、确认/取消、关闭/重开 | 原版静态分派或可观察运行证据、Godot源码路径、可复现的输入步骤和结果截图/日志 |
| 导航与状态 | 从登录、选角、游戏HUD到各窗口、二级页面及返回/关闭路径逐边相同；每个窗内按钮继续映射到相同目标窗、子状态或提示 | EI界面导航矩阵中每条边有来源、对应Godot入口、实际回放结果；扩展功能单独标明并确认不混入EI流程 |
| 动态数据与业务 | HP/MP、属性、任务、物品、组队/行会状态、NPC选项等读取相同数据并产生相同协议请求/游戏结果；空值、边界和失败路径也核对 | 原版字段/消息证据、Godot绑定与协议路径、同输入的服务端/客户端结果 |
| 完成门槛 | 任何未有原版证据、未能在实际界面观察或未完成行为回放的项都保持“未验收/候选”，不能按通过处理；编译、自审计常量或只看静态图不构成单项验收 | 每项审计编号最终关联原版依据、实现差异/修复、独立核验、实际交互结果与残余限制；范围内没有未解释的差异才可宣称一致 |

默认按“原版界面节点与跳转 → 逐窗文字/控件/状态 → 对照当前源代码与资源路径 → 对有充分证据的差异逐项修改 → 独立几何/资源核验 → `bash login_game.sh legacy` 实际交互和截图验收”的顺序推进。对每一项边比对边修改；窗口级通过后再进入下一个窗口。发现原版证据互相冲突时先把冲突闭合或保留为未决，不以猜测推进改动。用户指定的 `GameInter.wil` 查看器和仓库根目录启动命令是资源/运行复核入口。

## 先前文档与实现的审计结论

1. `docs/LEGACY_UI_MIGRATION_PLAN_2026-09-22.md` 把核心窗口阶段描述为完成，但其验收范围不足以证明所有 EI 控件与行为一致。该文件现已加状态说明：其阶段标签是历史记录，不是本轮 parity 结论；本审计矩阵未闭合前不得据此认定旧版迁移完成。
2. `docs/LEGACY_UI_PARITY_AUDIT_2026-09-22.md` 的“final audit”主要核验功能开关和服务可用性，不是逐控件的资源/几何/输入审计。其标题不能作为全量 UI 完成凭据。
3. `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/UI_COVERAGE_MATRIX.md` 和 `UI_COMPLETION_AUDIT.md` 是重要研究索引，但覆盖矩阵的“已恢复”经常表示原版静态路径已恢复，不等于 Zircon 移植已通过真实行为/视觉验收。应把“原版证据闭合”与“Zircon 实现验收”拆成两个状态。
4. `GodotClient/Scripts/LegacyHudLayoutLab.cs` 的 `--legacy-audit` 会检查预先写入的代码尺寸和控件状态。它适合作为实现回归检查，不是独立原版验证；当前报告的 `magic=True` 不能证明技能书像素、列表、点击、翻页或快捷键与 EI 一致。
5. `GodotClient/UI/legacy_ui.json` 只有一组 14 项窗口参考配置；多项标记为 candidate，且不含完整 HUD 子控件、登录/选角、模态流程、所有二级对话框或动态行为。`LegacyUiSkin.ApplyLegacyTestWindow` 是有限测试适配器，不代表真实登录模式的全部控件均已迁移。
6. `bash login_game.sh legacy` 只给正常登录客户端添加 `--legacy-ui --legacy-hud`。目前 `GameScene.ApplyLegacyCoreTestLayouts()` 仅显式套用部分窗口；测试场 `LegacyHudLayoutLab` 又是独立 800×600 画布。两者不能互相代替。后续运行验收需分别标记测试场、登录场景和游戏内实际操作。

## 已确认的高优先级差异：技能书

### 原版证据

- `skill-window-render-loop-evidence.json`：原版专用包装器 `0x00439250` 使用 `GameInter.wil`；其内部传给通用控件/绘制路径的原始常量含452、380，但不能直接解释成屏幕坐标或窗口根宽高。主初始化调用点 `0x00427904` 的 primary-static 记录 `window-initialization-evidence.json`（ID14）将根尺寸记为296×332，`skill-window-context.json` 也记同尺寸。右页详情由 `0x0043A440` 绘制，窗口刷新 `0x00439500` 调用。右页行起点为 `(windowX+235, windowY+30)`，垂直间隔15px；选择技能ID后读取 `Magic.exp` 相应 `#ID` 段。
- 同一证据中的 `selection_chain`：初始选择 ID 为 -1；点击左页列表由 `0x0043A370` 对六个命中矩形和当前分类链表命中，取出 16-bit 技能 ID，写入 `this+0x964`，随后右页显示该技能详情。
- 构造器创建 11 组帧控件：三个额外控件 440/441、410/411、412/413，以及八个分类按钮 450/451 至 464/465。按本机 `http://localhost:8766/api/image?f=GameInter.wil&i=410` 等资源预览直接检查，F410/411 外观为左箭头，F412/413 为右箭头，F440/441 为交叉剑形（visual-candidate）；与 primary-static 的控件位置/帧对共同证明它们不是技能图标序列。箭头“翻页”语义仍要由鼠标分派链验证；F440/441 的动作仍未定。
- **几何口径纠正（2026-09-24）**：旧审计把专用包装器内出现的452/380原始常量误当成根宽高，并据此把296×332降为旧metadata；这是误读，应撤回。主初始化调用记录把ID14根尺寸记为296×332（`window-initialization-evidence.json`与`skill-window-context.json`一致）。本机旧版 `GameInter.wil` F400帧头画布为512×512、alpha bbox为`(30,67,451,378)`；帧画布/alpha bbox不等于目标窗口根RECT。当前 `MagicDialog.ApplyLegacyEiLayout()`设为452×380，F400控件原点(-30,-67)，只是将本机素材alpha左上角对到当前Godot根原点，不能称为与EI根尺寸匹配。目标EI WIL身份未核，原版F400实际绘制裁剪/目标矩形也未闭合；ID14的296×332保留为主初始化primary-static根尺寸记录，根内图像显示范围及其与本机WIL的配准仍待验证，本轮不改布局代码。
- `skill-grid-magic-exp-evidence.json` 明确注明模拟器 12 格的 4×3 坐标仍是 candidate，未被原版技能窗口静态几何钉定。该资料证明 Magic.exp 中有真实技能记录，不证明把前 12 个技能映射到这些格子。
- **构造控件数量交叉核对（2026-09-24）**：F939 `skill-window-input-evidence.json` 与 F839 `skill-book-draw-evidence.json` 把对象偏移 `+0x2F4..+0x7E0`（stride `0xB4`）概称为 “skill slots”；F547 `skill-book-category-tabs-evidence.json` 则明确称同一偏移段为 8 个分类页签，F547 记载的火/冰/电/风/神圣/黑暗/幻影/剑与构造器的八个文字指针一致。`skill-window-context.json` 的 `window_constructor_control_geometry` 也逐项把这些对象映射到 F450/451 至 F464/465，并与八个分类文字控件对应。故 F939/F839 的“8 skill slots”是对象语义命名冲突，不能据标题认作八个技能格；更可能是把同一八个分类页签错误叫成槽。另一个工件内部也有错：`skill-window-context.json.category_hit_rects.records` 的最后三个 `frame_pair` 重复 F450/452/454，而其构造器几何、F547 与 `analyze_mir3_skill_window.py` 明确为 F460/462/464；该数组不能作为这三项帧号依据。与独立 `0x43A370` 记录相合的暂定解释仍为八个分类控件 + 六个技能列表 hit rect；仍须从可复现目标版本字节核清列表可见项数量、RECT 写入与页计数 `/3` 的关系。六矩形只表示 hit-test 记录数，不足以断言绘制只有六项；当前候选代码的 12 个显示格与12个命中区均未获支持。
- `skill-window-static-evidence.md` 与 `skill-window-context.json` 的296×332是原版主初始化根尺寸记录，不是F400资源尺寸；本机F400为512×512画布/451×378 alpha有效区。曾将包装器的452/380内部常量误认作EI根窗并声称与素材alpha相合，现撤回。素材画布、alpha有效绘制区域、主初始化根RECT和包装器内部参数必须分别记录；目标资源身份及EI窗口内的实际裁切/绘制范围仍未闭合。

### 当前 Zircon 实现

`GodotClient/Controls/LegacyUiSkin.cs::ApplyLegacyTestWindow()` 对 MagicDialog 的 profile 设452×380根、F400背景原点(-30,-67)并启用 `Clip=true`；`GameScene.ApplyLegacyCoreTestLayouts()` 在 `--legacy-ui` 的真实登录游戏路径调用此 profile，`MagicDialog.ApplyLegacyEiLayout()`又设回452×380，并以非拉伸方式按当前WIL帧尺寸绘背景。对应本机 F400 alpha bbox为 `(30,67,451,378)`，映射后有效像素全部落在当前452×380裁剪矩形内；这仅证明 profile 为本机大帧内容量身适配，不能证明EI id14根RECT等价。原版主初始化器记录尺寸296×332，当前 profile 存在明确的静态尺寸差异。`LegacyUiSkin.cs` 当前行内注释“wrapper 0x439250 原始参数明确给出 452x380”沿用了已撤回的根尺寸误读，应在后续计划中更正；本阶段不改布局/业务源码。该窗口还隐藏 `_tabPrevious`、`_tabNext`。`BuildLegacySchoolButtons()`生成八类按钮；`BuildLegacySkillSlots()`生成12个固定位置的 `DXImageControl`，使用GameInter F410..F421，每个按索引尝试选择 `_legacyRuntimeEntries[index]`，再尝试让 `_cells[index]`获得焦点。

### 当前可确认的差异/缺口

- 12 格坐标来源是候选模拟器布局，现阶段却被用作真实 EI 技能命中区域；证据不足。
- **本轮补做的数据身份交叉核对**：当前运行资源根 `/home/tetsuya/mir3ei/Magic.exp` 为 18,438 字节，SHA-256 `60f7e019f5838e2cec4c7ff94772a4dbb6d478da311bb7c0978012b9aac2513d`；研究解码工件记录的目标文件 `/home/tetsuya/NAS/TMP/EI传奇3.0客户端/Magic.exp` 为 14,735 字节，SHA-256 `5b2300ee587aea774cca8a5a3fc0ee35172d6dbdf8583b42254fbc28e22fc8e1`。两个文件不同。再用研究解码器对当前 18,438 字节文件做只读解码，头部校验虽通过，前段结果为不可解析的乱码字段；这说明仅凭相同加载/校验骨架不能把该文件当作目标文本。反编译目标 EXE 当前也不可读，因此不能拿正在运行资源根的 `Magic.exp` 生成技能详情并声称等于目标 EI。原始目标文件仍需恢复以独立复验解码链；已保存目标解码文本的可用范围及映射限制见下一条补正。因 ID 映射未闭合，当前仍不能渲染实际选中技能详情，避免用别版数据代填。
- **2026-09-24 补正目标详情文本可用性**：原始加密 `Magic.exp` 仍无法从 NAS 路径重读，但研究库保存了 [`Magic.exp.decoded.txt`](/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/Magic.exp.decoded.txt) 和 [`magic-exp-records.json`](/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/magic-exp-records.json)。JSON 记录目标源 SHA-256 `5b2300ee…22fc8e1`、解码器和 50 条 section；文本文件 699 行，SHA-256 `5621df088490c7afda98a5055bca7b75f2b2b1da7449ff7f37fb410f8e66f62a`。所以右页原文内容并非整体缺失，可用保存的解码产物实现；限制转为 EI `#ID` 与当前客户端 `MagicInfo` 身份匹配。此前词典级“22个命中/20个唯一”统计已撤销：该统计未过滤实际数据库记录。现在只读读取 `Debug/Client/Data/System.db`（经 `Debug/Client` 软链对应运行资源根，MirDB `SessionMode.None`）取得 174 条实际 `MagicInfo`，再按 EI 中文名→翻译英文候选→数据库 `Name` 精确连接，50 条记录中 29 条存在名称候选：28 条仅一个当前记录，1 条有两个（`#17 召唤骷髅`→`Evil Slayer`/`Summon Skeleton`）；`#72 龙卷风`在当前库只命中 `Cyclone`，`Tornado` 不是当前 `MagicInfo`。进一步按直译元素/学派名称与前三段所需等级交叉，有12条 EI 记录至少存在一个同时符合这两项的当前候选；其中 `#17` 从名称候选缩小到 `Summon Skeleton`（EI 幻影元素、等级17/19/21；该候选是 Phantom、等级17/19/21），但仍只能作为跨版本语义旁证，不能证明协议 ID 或图标帧同一。逐候选字段、来源、哈希和结果见 [`magic-exp-current-magicinfo-name-crosscheck-2026-09-24.json`](evidence/legacy-ei-ui/magic-exp-current-magicinfo-name-crosscheck-2026-09-24.json)。这些仍是跨语言名称连接候选及语义旁证，并不证明 EI ID 等于 `MagicType`、DB 序号或当前图标帧；原始 EI EXE/加密 EXP 无法重读，目标技能网络身份链也未闭合。其余 21 条没有当前名称候选，全部保留待证。
- **本轮补做的技能 ID 映射检查**：原版右页按 `Magic.exp` 的 `#N`（例如 `#3 基本剑术`）索引；当前 `MagicInfo` 同时具有 DB 顺序、业务 `MagicType`、`Name`、职业、学派、图标帧和等级要求，名称候选不能证明字段身份。读取实际 DB 并用 EI 解码记录与翻译名称交叉后，得到 29/50 名称候选（28 唯一、1 歧义、21 未匹配），逐项数据保存在上述 JSON。例：EI `#3 基本剑术`→现代 `Swordsmanship`（当前 `MagicType` 数值100；不是 EI ID 3），`#7 攻杀剑术`→`Slaying`（102），`#12 刺杀剑术`→`Thrusting`（103），`#25 半月弯刀`→`Half Moon`（104）。EI `#17 召唤骷髅`有两个名称候选且同属 Taoist，但只有 `Summon Skeleton` 同时符合 EI 的幻影元素与17/19/21等级要求（其当前学派为 Phantom）；`Evil Slayer` 为 Holy、14/16/18级。该组合强烈支持前者，但仍不能证明 EI 网络 ID / 技能图标映射。故这些候选仅供后续逐条核验，不能用于按 DB 顺序填充技能书；必须继续追技能列表链表初始化、网络技能 ID 与当前业务记录的映射，并逐项核对学派、图标和等级字段。研究资料中的 Mud3 `magic.dat` 是另一套105条记录，只有9条抽样记录与客户端 `Magic.exp` ID 做过名称交叉核对，只能为已核名字提供旁证。映射闭合前不以现代 `MagicInfo` 代填右页。
- 当前 12 个占位框把 F410..F421 全当作技能图标。原版构造器明确将 F410/411 与 F412/413 分别用于两组控件的普通/状态帧；至少这四帧的用途与当前映射冲突。F414..F421 的窗口语义仍须追踪，不能因连续编号直接认作技能图标。每个技能 ID→实际图标资源/绘制来源也缺少闭合链。
- 当前实现 `RefreshLegacySkillSlots()` 原先按 DB 枚举顺序建立点击/F 键绑定表，而可见 `MagicCellView` 按 `NeedLevel1`、名称排序，导致点击候选格与 F 键实际绑定对象可错位；已将两处本地顺序统一为 `NeedLevel1` 后 `Name`，避免 Godot 内部显示/操作互相错配。这只修复当前候选 UI 的内部一致性，不证明 EI 原版按同样顺序排列，也不改变 12 格布局未获原版依据的结论。
- **技能分类按钮几何静态复核（2026-09-24）：**研究工件 `skill-window-context.json.category_hit_rects.records` 给出的八个根相对矩形与 `MagicDialog.BuildLegacySchoolButtons()` 当前 locations 逐项一致：`(5,21)`、`(3,56)`、`(4,91)`、`(2,126)`、`(2,161)`、`(2,196)`、`(1,231)`、`(2,266)`；构造帧依次为 F450/452/454/456/458/460/462/464，当前帧选择也相同，尺寸由对应帧元数据读取。故这八个分类控件的根相对坐标和帧号有静态吻合证据，不能把它们与仍未经证实的 12 个技能格混为一谈。限制：`category_hit_rects` 工件自身将后三项的 `frame_pair` 错误重复为450/452/454（上文已记录），本核对用其 RECT 数组、构造器几何和独立帧序列交叉确认；研究工件构建版本与当前 EI 资源哈希仍未完全绑定，且当前按钮点击进入 Zircon `MagicSchool` 过滤链，不足以证明分类字节、列表成员/排序、技能学习状态和翻页结果等同原版。
- **顶部控件矩形对照（2026-09-24）：**`skill-window-render-loop-evidence.json` 的构造/paint记录为 F440/441 `(399,340)`、F410/411 `(61,303)`、F412/413 `(366,303)`，它们是三个独立窗口子控件；原版关闭按钮另为 F161/162。当前 legacy 布局把 F410/411、F412/413 对应的现代 `_tabPrevious/_tabNext` 隐藏，也没有创建 F440/441 控件，因此除了关闭按钮外这三个原版命中区均缺失。源记录仍未闭合这些控件的完整 click handler/状态变化；不把左右箭头外观直接等同“翻页”动作，也不猜 F440/441 的用途。后续需补查 `0x00439525-0x00439594` 的 paint-time重定位、对应父窗 click分派和状态字段，再映射Godot hit rect及动作。
- **用户指定查看器的技能窗帧头复核（2026-09-24，8766 API）：**直接读取 `http://localhost:8766/api/info?f=GameInter.wil&i=N`，F400=`512×512, offset(+7,-44)`；F410–413 均=`32×14, offset(-24,-16)`；F440/441=`20×12, offset(-24,-16)`；分类状态帧尺寸分别为 F450/451、452/453、454/455=`44×36`，F456/457、458/459、460/461、462/463、464/465=`48×36`，所有这些分类帧 offset 均为`(-24,-16)`。这些是本机 LegacyEI WIL 帧头/offset 元数据；F400 alpha 有效边界与各控件屏幕像素锚点不由此 API 结果单独给出。结果与 primary-static 的分类构造 RECT 尺寸相符，也显示当前 Godot DXButton 默认不消费 WIL offset 与目标最终像素锚点仍需独立核对；不因共同 `(-24,-16)` 先验平移控件或命中框。
- F1–F12、Shift+F1–F12、Ctrl+F1–F4 的当前 Zircon 默认键位只是现代 KeyBindManager 配置证据；仍需对照原版全局按键分派和技能书列表选中/拖放绑键链，区分“技能快捷施放”和“在书中绑定快捷键”。
- 技能书外框的 F400 透明边距、内容坐标、关闭按钮命中区、可拖动窗口原点和 1024×768 等比例行为尚未以原版运行截图独立验收。

**审计状态：实现不通过原版交互验收，结论确定；已确认至少 F410..F413 的资源语义冲突。精确修复方案暂缓，待把构造器余下输入分支、列表初始化/分类、键盘绑定路径一并审完后再纳入计划。**

**2026-09-24 登录运行复核（1024×768，`bash login_game.sh legacy`）：**从 HUD 底部右侧点击技能入口打开技能书，实屏根窗由 `LayoutHud()` 放在 `(viewportWidth-452,0)`，F400 有效绘制内容位于客户端屏幕右上区域；画面可见八个分类按钮、当前两行技能图标、候选空格及空白详情页。截图 [`skill-book-open-current-2026-09-24.png`](evidence/legacy-ei-ui/skill-book-open-current-2026-09-24.png) 是 Zircon 当前状态证据，聊天窗口在下层遮挡部分游戏画面，故不作为窗口完整裁切或原版坐标验收。该实屏确认当前右页详情尚未实现，不据此推导原版格子外观。

同轮点击相邻技能/聊天记录入口时，边界附近 `(835,667)` 打开聊天记录，较内侧 `(820,657)` 打开技能书。一次边界点击不足以判定 Godot hit rect 重叠，且尚未与 EI HUD 原始矩形对照；登记为 HUD-08 / SKL-07 的待核观察，不据此改坐标。须导出两入口实际 hit rect，并对照 `hud-caption-action-tail-evidence.json`，逐测中心、四边、边界内外和相邻空白。

**分类切换实屏修复/复核（2026-09-24，Xvfb :100，1024×768）：**第一次点击 Fire 页签后，按钮从原版竖列 `(5,21)..(2,266)` 被 `SelectSchool()` 中无条件调用的现代 `UpdateTabLayout()` 移到横排；截图 [`skill-category-legacy-relayout-before-2026-09-24.png`](evidence/legacy-ei-ui/skill-category-legacy-relayout-before-2026-09-24.png) 记录该错误。已修改 `MagicDialog.SelectSchool()` 仅在非 legacy 模式运行分页/横排布局，并扩展本地回归断言检查八个页签相对位置。重新通过指定脚本完整登录后，依次点击 Fire 和 Ice：两次截图 [`skill-category-fire-after-fix-2026-09-24.png`](evidence/legacy-ei-ui/skill-category-fire-after-fix-2026-09-24.png)、[`skill-category-ice-after-fix-2026-09-24.png`](evidence/legacy-ei-ui/skill-category-ice-after-fix-2026-09-24.png) 均显示分类按钮仍在竖列，Ice 还切换了左页技能图标内容。此项只通过“Godot 页签点击不会把 legacy 按钮移成现代横排”回归；与原版列表命中矩形、具体技能 ID 顺序及页数仍未闭合。

随后在同一登录实例继续点击 Lightning、Wind、Holy、Dark、Phantom、Physical。六个按钮点击后页签仍保持竖列，左侧技能图标集合随点击有变化；截图裁切按行从左到右依次为 Lightning、Wind、Holy、Dark、Phantom、Physical [`skill-categories-six-contact-2026-09-24.png`](evidence/legacy-ei-ui/skill-categories-six-contact-2026-09-24.png)，单项原始裁切也分别保存。此处只确认 Zircon 自身八个 category button 都有点击响应且不会横移；当前 DB 内容/类别是否与 EI 的 `[this+0x54]`、链表记录顺序一致仍属未验。

### 技能书审计条目（供后续计划引用）

| 编号 | 严重度 | 发现 | 证据/现状 | 待完成的独立验收 |
|---|---|---|---|---|
| SKL-01 | 阻断 | 当前 12 格候选被误作原版左页布局；原版已证实 6 个技能命中矩形，不是 12 个 | primary-static `skill-window-render-loop-evidence.json`：`0x43A370` 迭代 `this+0x7C` 起的 6 个 RECT；分类链表来自 `this+0x898+24*cl`。F848 将 `0x4397A0` 绘制细化为类别链表技能图标/名称/高亮框，但摘要没有记录列表循环上限/页索引，不能把“遍历链表”进一步等同于固定可见项数。`0x439500` 的状态文字读取类别计数 `[this+0x58+4*cl]`，执行有符号除3，再派生 `2*q+1` 与 `2*q+2` 两数；现有 primary-static 摘要把它描述为页范围候选，却没有闭合它与六个命中 RECT、绘制循环范围和翻页控件状态的关系。故不能从六个 hit RECT 和“/3”单独推成两页各三项或具体列表几何。研究模拟器的 `skill-grid-magic-exp-evidence.json` 明说 4×3 几何仍是 candidate；`skill-detail-verification-evidence.json` 的“原版 12 格”结论与其自身“sim slots”范围及 EXE 六矩形冲突。当前 `BuildLegacySkillSlots()` 创建 12 个 36×36 命中控件并对前 12 条按职业/学派/等级排序，超出原版当前静态 hit-test 记录。 | 取得目标同版 EXE 后核 `0x4397A0` 循环/状态分支和计数用途；追六个 RECT 的初始化/写入、三个头部控件 `+0xD8/+0x18C/+0x240` 如何改变页/选择，并对照 `[this+0x898+24*cl]` 的真实列表填充顺序。随后再映射 Godot 图标/命中区域。不要沿用模拟器 4×3 排布作为原版事实。 |
| SKL-02 | 阻断 | F410..F421 不是 12 个技能图标序列；当前把已确认导航控件帧与未确认帧误作格子图标 | `skill-window-context.json` 的 11 次原版通用控件构造调用仅含 F440/441、F410/411、F412/413 以及八组分类帧。primary-static `0x43AC80` 先处理 3 个帧控件和 8 个分类按钮，然后才调用六矩形技能命中函数。`MagicDialog.BuildLegacySkillSlots()` 将 F410..F421 全用作 12 个背景图，再在刷新时整批换成 `MagicIcon`。所以F410/411与F412/413是构造证据中的导航控件，不是技能图标；独立wilsdk解析本机WIX确认F414–419为空帧，F420–431均为40×20非空帧，归档[帧组图](evidence/legacy-ei-ui/skill-category-frames-wil-2026-09-24.png)视觉显示为F1–F12字样（visual-candidate，具体控件用途/所属窗口未由构造与输入链证明）。当前连续背景序列F410+i因此混入箭头、空帧与数字标签帧，而非12个技能图标；当前`MagicDialog.cs`未将F420–431作为帧资源调用，检索到的“420”仅为页签布局宽度运算。更新证据 F848 表明原版列表技能图标帧取自技能记录 `[skill+6]`，经 selector `0x566C90`（全局数组 el85）绘制；该来源与 GameInter F410..F421 无关。对资源文件名再作静态交叉：`mir3-dat-resource-path-table.json` 将 slot85 的 owner+0x10784（store block 0x452A24）绑定 `Data/MIcon.wil`；当前 `LibraryFile.MagicIcon` 注册为 `Data\MIcon.Zl`，`MirSkin` 将 MagicIcon 列入旧版 UI 路由，在 legacy 根没有对应 ZL 时由 `GetTexture()` 回退到同 stem 的 `MIcon.wil/.wix`。因此图库家族/legacy fallback 入口与 EI 的 MIcon 路径相合，未闭合的差异是 EI `[skill+6]` 与当前 `MagicInfo.Icon` 的逐技能帧语义，不是“EI与Godot必然使用不同图库”。文件级复核确认本机旧版 MIcon.wil 有1106帧/138个非空帧，现代 Data/MIcon.Zl 有1773帧/224个非空帧；两边共有的54个非空索引尺寸/offset全部不同（见后文帧头统计），所以不能以现代ZL索引与画布规格推定EI图标一致。EI列表[skill+6]与当前 MagicInfo.Icon 的技能语义映射仍待闭合。 | el85→MIcon.wil 的静态路径已闭合；继续追 `[skill+6]` 的记录读取、MIcon frame bounds/绘制参数及 EI skill ID 到当前 `MagicInfo.Icon` 的逐技能对应，独立抽核同一可识别技能的帧像素/offset。并补齐六个 hit RECT 与列表记录映射；不可仅按帧数或同名顺序批量映射。 |
| SKL-03 | 阻断 | 原版右页详情链已经闭合，但当前旧版技能书没有右页绘制；点选候选格也无法把选择写入原版详情状态 | **EI primary-static** `skill-window-render-loop-evidence.json` Finding 272：左页 `0x43A370` 由当前分类链表命中技能记录，取记录 `+4` 处的16位技能ID，`0x43ACE4` 写入窗口 `this+0x964`；`0x439500` paint 再调用 `0x43A440`，按该ID匹配 `Magic.exp` 的 `#ID` 段。右页首行 `(winX+235, winY+30)`、每行15px，名称行蓝字黑影、正文深绿。**Godot 当前实现** `MagicDialog.SelectSchool()` 把技能加入 `_cells`，但 legacy 下 `_list.Visible=false`；可见候选格 `BuildLegacySkillSlots()` 只将 `_legacyRuntimeEntries[index]` 存入 `_legacySelectedSkill`，其类型是 `(MagicInfo, ClientUserMagic)`，没有 EI ID→段落的已证映射，也没有右页绘制控件；`_UnhandledKeyInput()`只用该tuple设置快捷键。`GetVisibleMagicInfos()`按当前 MagicInfo 的职业/学派/装备戒指过滤，刷新按 NeedLevel1、Name 排序并截12项；这些筛选、顺序及现代 `MagicInfo.Icon` 不是 EI `this+0x898` 类别链表写入来源的证明。**可用旧 Client source** 的 `Client/Scenes/Views/MagicDialog.cs` 仅能说明旧通用列表/技能控件行为，不能补足 EI 3.0 的记录ID映射。既有运行截图 [`skill-book-selected-left-item-current-2026-09-24.png`](evidence/legacy-ei-ui/skill-book-selected-left-item-current-2026-09-24.png) 显示候选左格点击后右页为空，但未独立记录内部选中 ID，故不作为左格命中/选择链验收。 | 取得匹配版EI EXE或恢复研究二进制原始字节，追 `this+0x898` 类别链表的填充和记录构造，闭合EI技能ID到Godot业务记录的逐项映射；再实现右页文本及可观测选择ID，逐技能核标题、行内容、颜色、行距、裁切、空选择、翻页。映射未闭合前，不按名称、DB顺序或相似等级填充。 |
| SKL-04 | 高，箭头动作候选待输入链闭合 | 三个额外原版帧控件被当前实现省略；两组明显箭头帧未映射，F440/441作用未定 | primary-static `skill-window-context.json`/`skill-window-render-loop-evidence.json`：paint顺序在8个分类按钮前重定位/绘制对象 `+0xD8/+0x18C/+0x240`，帧对分别F440/441、F410/411、F412/413；坐标候选约为F440 `(399,340)`、F410 `(61,303)`、F412 `(366,303)`。viewer视觉候选为F410/411左箭头、F412/413右箭头、F440/441交叉剑形；独立原始WIL解码及标帧图见[导航帧组](evidence/legacy-ei-ui/skill-navigation-frames-wil-2026-09-24.png)。Godot当前隐藏 `_tabPrevious/_tabNext`，且没有重建这三组帧控件；F440是否关闭窗或执行其它功能仍需通过输入回调闭合。**本轮静态矩形交叉核对：**F440候选命中框 `(399,340,20,12)` 与当前Godot F161关闭按钮 `(418,348,28,26)` 在目前共同坐标假设下相交1×4px；因目标窗口坐标/绘图配准未闭合，这仅是待验证重叠候选，不能直接改位置。F400本机WIL画布512×512、alpha bbox `451×378+(30,67)`；主初始化证据记录EI id14尺寸296×332，不能再把包装器452/380参数当根尺寸，也不能按本机alpha bbox推导EI裁剪。 | 回查包装器/控件坐标系和点击分派；用原版实际点按F410、F412、F440对应hit rect，分别记录页/状态/动作；独立核source-frame hitbox与root坐标。先取得同版WIL/EXE并确认裁剪，再讨论布局调整；Godot侧先导出F161实际hit rect，在稳定输入回放后测重叠区域。资源外观不单独判定业务语义；原版语义闭合前不移动控件。 |
| SKL-05 | 高 | 当前“学派按钮”仅凭 `MagicSchool` 绑定，未核对 EI 分类 byte/list | 原版当前分类为 `[this+0x54]`，8 个分类对象顺序/纵向位置有构造与重绘证据；但后三个类别帧在研究工件间冲突，精确帧号待核。左页技能链按分类列表头和技能记录绘制 | 对照 `Magic.exp` 的 skill ID 与原版按职业/类别的实际列表，验证八按钮切换与空类别状态 |
| SKL-06 | 高，Zircon技能窗双目标绑定/重复发送已静态确认；目标 EI 按键链未决 | EI 原版 F1–F12/Shift/Ctrl 技能绑定链尚未找到；当前实现与可用旧 Client 源码存在 modern 绑定/施法双用途，legacy 选中态和悬停态还可能指向不同技能 | primary-static `window-paint-and-hotkey-dispatch-evidence.json` 的研究范围覆盖 Q/W/E/R/S/D/Z/C/V/B/G/F/N 等字母热键，没有 F1–F12 结论；`Mir3-Research/docs/research/mir3-map-reconstruction/skill-window-input-evidence.json`（F939，primary-bytes；scope 为 `0x43AC80–0x43AD50`）只覆盖 3 个帧控件、8 个分类控件和六个列表 RECT 的鼠标输入，因此“未检出”不构成 EI 不支持 F 键的负证据。可用旧 Client 源码 `Client/Envir/CEnvir.cs` 明确设置 Ctrl+F1–F4 为 SpellSet01–04、裸 F1–F12 为 SpellUse01–12、Shift+F1–F12 为 SpellUse13–24；`Client/Scenes/Views/MagicDialog.cs::Image_KeyDown` 在技能图像获得鼠标焦点时把 SpellUse action 写入当前技能的 Set1–4 并发 `C.MagicKey`，`MagicBarDialog` 是独立的 24 槽快捷栏对象，初始显示前12槽，后12槽按绑定情况显示。这是可用 Client 源码确认的 modern 协议行为，不是目标 EI 3.0 的行为证据。当前 Godot `KeyBindManager` 默认沿用相同 F 键组；`GameScene._Input/HandleKeyBind` 无可见窗口时施法，Ctrl+F1–F4 切组；legacy 打开 `MagicDialog` 后 `MagicDialog._UnhandledKeyInput()` 又按 `_legacySelectedSkill` 绑定 Set，shift 选择第二组12键，且没有 modern cell 的同组重复键清理。与此同时 `MagicCellView._UnhandledKeyInput()` 按鼠标悬停行绑定并调用会清除同组重复键的 `BindCurrentSetKey()`。静态追到事件入口后可进一步限定差异：`GameScene._Input()` 先处理按键，在 `WindowManager.OpenWindows` 存在任一可见窗口时，于通用 `KeyBindManager.GetAction()` 前返回；因此技能窗可见期间，F1–F12 不会走该场景全局的 SpellUse 释放或 Ctrl+F1–F4 SpellSet 切换分支。随后 `MagicDialog._UnhandledKeyInput()` 对 F1–F12 写入 `_legacySelectedSkill` 的当前 Set，Shift 仅把编号加12，且没有检查 Ctrl/Alt；`MagicCellView._UnhandledKeyInput()` 则在鼠标位于本行图标 `(9,9,36,36)` 时按悬停行写入同 Set，同样不检查 Ctrl/Alt、也未过滤 key.Echo，并执行同组去重。`DXControl.AddControl()`确认技能行是`_list`后代，`_list`又由`MagicDialog.AddControl()`加入；Godot官方[`Node._unhandled_key_input`说明](https://docs.godotengine.org/en/stable/classes/class_node.html)未处理键事件沿节点树向父级传播，直到有节点消费。故在输入未先被GUI处理、鼠标位于技能图标、且`_legacySelectedSkill`有效时，`MagicCellView`先按悬停行执行绑定和`C.MagicKey`发送，但不标记事件已处理；事件继续到祖先`MagicDialog`，再按选中技能绑定同一键并标记处理。若两目标不同，同一次按键会改写两项技能绑定；目标相同则也会重复发送，且父窗路径不做同组去重。cell handler也未拒绝`key.Echo`或检查Ctrl/Alt，所以按住键/带修饰键在悬停技能行时仍会进入它的绑定分支；父窗路径虽过滤Echo，但同样未检查Ctrl/Alt。以上是Godot当前源码+官方事件传播契约能确认的实现差异，具体目标EI是否采用同类悬停绑定仍无EI键盘证据，且本轮未做运行输入。补充源码注释一致性：`GameScene.cs` 的 `MagicBarSpellSet` 字段注释写“F1~F8 当前栏组…原版 Ctrl+1~4 切”，但同一仓库 `KeyBindManager` 默认 `SpellSet01..04` 明确绑定 Ctrl+F1..F4；`MagicBar.GetSlotsForSet()`为每组生成24个 `SpellKey`，键位表分别覆盖 F1..F12 与 Shift+F1..F12。可用旧 `Client/Envir/CEnvir.cs` 的 SpellSet01..04 配置也将 Ctrl 与 F1..F4 组合，SpellUse 另占裸F键/Shift+F键。故该行注释同时在切组键写法与F1~F8槽数描述上过时/不准确；这是源码注释差异，不证明 EI 3.0 键盘链，目标 EI 原始按键分派仍阻塞。以上均为 Zircon/可用 Client 源码结论，不能拿来填补 EI 缺失证据。 | 恢复并校验目标 EI EXE 后，沿主键盘分派器和技能窗输入分派器追 F1–F12、Shift/Ctrl 与技能栏绑定字段的读写/发送点；分别验证技能书开/关、无选择/有选择、指针在技能行上/窗外、聊天编辑焦点、四组选择、重复绑定及施法。目标 EI 路径闭合前保留未决，不以 modern Client 源码定案或把两种 Godot 绑定路径当作同一行为。 |
| SKL-07 | 中，HUD入口边界待核；自测缺少独立 oracle | 自测只验尺寸与控件数量；且登录运行边界点击存在技能/聊天入口命中歧义候选 | `AuditLegacyEiLayout()` 断言 12 个 slots、8 类按钮、列表和滚动条隐藏，不验帧语义、边界、点击、详情文本和键位。运行中 `(835,667)` 打开聊天、`(820,657)` 打开技能书；未导出当前 hit rect，不能断言重叠。截图见本节运行证据 | 导出 Godot 两入口 hit rect，与原版 HUD rect 独立比对；按入口中心、边界内外、相邻空白逐点验证窗口 ID。随后将回归期望改由研究证据生成，不以生产布局自证 |
| SKL-08 | 高，页签横移已修；八类均点过，EI业务映射待核 | 八个分类按钮的原版竖列坐标有 primary-static；后三个类别帧序在研究工件间冲突；点击时现代分页器曾横移的问题已修 | `MagicDialog.SelectSchool()` 仅在非 legacy 模式套用横排分页；`AuditLegacyEiLayout()`检查八个按钮位置。按 `bash login_game.sh legacy` 重新登录后，Fire/Ice/Lightning/Wind/Holy/Dark/Phantom/Physical 八个分类逐个点击，按钮均留在竖列，左页图标集合随点击有变化；六类裁切汇总见 `skill-categories-six-contact-2026-09-24.png`。 | 依据 EI 原版页签 byte 与分类链表逐项核对职业、真实技能 ID、列表顺序、页数与空页；从目标 EI 运行截图核选中/按下帧和选择效果。 |
| SKL-09 | 高，legacy 模式常驻快捷栏、资源根混用及 Ctrl+E 入口与 EI 技能书冲突 | 当前另建并默认显示可移动的12/24格`MagicBar`；它与 EI 技能书 id14 是不同对象。EI 主热键证据显示 Ctrl+E 与裸 E 都打开/关闭技能书；当前 `KeyBindManager` 却把裸E→`MagicWindow`、Ctrl+E→`MagicBarWindow`。`MagicBar` 确实请求 GameInter2 学派边框，但 legacy 下 `MirSkin.GetTexture(GameInter2,…)` 从 `LegacyEI/Data` 取图，那里无 GameInter2.Zl，所以当前该绘制进入 `DrawRect` 矩形边框回退；技能图标此前从常规 `DataPath` 读取 MIcon.Zl，已于本轮统一改走 `MirSkin` 并由运行日志确认回退到EI MIcon.wil。此前“快捷栏实际使用现代 GameInter2 图框”的描述过强，应改为“请求该帧但 EI 根缺失、运行时回退为线框”；也不能据此断言全栏都显示现代边框。EI 是否另有快捷栏仍未闭合 | primary-static `hud-caption-action-tail-evidence.json`/`chat-window-control-map.json`：caption“技能书(Ctrl+E, E)”，E 分支检查 VK E 后 `toggle(0xE)`；`window-paint-and-hotkey-dispatch-evidence.json` 将 id14绑定技能书F400，id5/10为空。当前 `GameScene` 无条件创建并显示 `_magicBar`；`MagicBar.cs` 有12列、最多24槽、四组、可移动逻辑，空/有技能均查 GameInter2帧815/860–892；空帧则显式画矩形回退。`MirSkin.IsUiLibrary()`将GameInter2路由到`UiDataPath`，legacy默认为`/home/tetsuya/mir3ei/LegacyEI/Data/`；技能图标已通过`MirSkin.GetTexture(MagicIcon,frame)`从`UiDataPath`回退读取EI MIcon.wil；GameInter2仍无EI资源并落线框回退。EI窗口表没登记独立MagicBar只说明该表未覆盖到此对象，不足以单独证明EI不存在同类HUD子控件。 | 从原版HUD完整构造/绘制链核常驻技能栏、资源和入口；复核裸E/Ctrl+E及打开窗口时的键路由。EI原版完整HUD/快捷栏链和裸E/Ctrl+E行为仍待闭合，再判定legacy常驻栏应隐藏、复原或明确保留为扩展；不能把资源缺失回退状态当成EI旧版外观。 |
| SKL-10 | 高，八类控件身份已确认；后三类资源帧研究材料不一致 | F839/F939 将共用 F704 对象简称“skill-slot”，但构造调用偏移、八个字面量及纵向位置支持其为类别控件。`skill-window-context.json::category_labels` 与 `skill-window-static-evidence.md` 把黑暗/幻影/剑重复记为F450/F452/F454；`skill-window-render-loop-evidence.json::window_constructor_control_geometry.controls` 和 RESEARCH_LOG Finding 200则记F460/F462/F464。WIL帧组视觉核对见[技能类别帧组contact sheet](evidence/legacy-ei-ui/skill-category-frames-wil-2026-09-24.png)；当前Godot `BuildLegacySchoolButtons()` 使用F450/452/454/456/458/460/462/464，只与一组研究记录相符，不是独立原版验证。`0x43A370` 六个 RECT 属另一组列表hit-test。 | 同版EXE可读后按 `0x439250/0x439500/0x43AC80` 的原始指令确认八组帧及对象分派；再用原版列表 RECT 与绘制链映射Godot六个技能点击区。
| SKL-11 | 高，已修并通过 Godot 实际点击回归 | 点击 legacy 分类页签会误调用现代横排分页器，造成按钮横移且与底图竖列错开 | 修复前实屏截图 `skill-category-legacy-relayout-before-2026-09-24.png`；代码链为分类 `MouseClick → SelectSchool() → UpdateTabLayout()`。将分页更新限制到非 legacy 后，重新登录并点 Fire/Ice 的实屏截图分别记录按钮保持竖列及技能图标变化；`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功（0 errors，3条既有 warning）。 | Godot 页签切换回归通过。EI 同状态原版实屏未获得，仍须继续核分类控件状态帧、各分类对应 ID 顺序/列表与原版业务页；不得据本项单独宣称技能书验收完成。 |
| SKL-12 | 高，技能窗内 F 键存在选中项/悬停项双目标绑定风险 | `MagicDialog._UnhandledKeyInput()` 在 legacy 模式把 F1–F12/Shift+F1–F12 绑定给 `_legacySelectedSkill`；同时每个 `MagicCellView._UnhandledKeyInput()` 在指针位于本行图标 RECT 时也绑定对应 `_magic`。后者不检查 legacy 模式，也没有调用 `GetViewport().SetInputAsHandled()`；父级处理器发送 `C.MagicKey` 后才标记事件处理完成。两者选中的技能与鼠标悬停技能可以不同，源码存在一次 F 键事件可能写入两个技能记录的路径；是否确实双收由 Godot unhandled 输入传播顺序和节点状态决定，静态材料不能代替事件回放。 | Zircon 源码证据：`GodotClient/Controls/MagicDialog.cs` 的 legacy 分支 `_UnhandledKeyInput()`、同文件 `MagicCellView._UnhandledKeyInput()` 与 `BindCurrentSetKey()`；EI 目标 F 键/鼠标绑定语义仍未取得，不能把可用旧 Client 的 `Image_KeyDown` 直接当目标 EI 结论。安全复核需让当前选中行与鼠标悬停行不同，观察一组 F 键输入后两条 `ClientUserMagic` 和实际 `C.MagicKey` 更新；此项本轮未输入、不发包。 |
| SKL-13 | 高，Ctrl+F1–F4 在技能书打开时被当成绑定键，不会切换栏组 | `KeyBindManager` 将 Ctrl+F1–F4 配为 `SpellSet01..04`；但 `GameScene._Input()` 对非窗口动作先检查 `WindowManager.OpenWindows`，技能书可见时在通用键表分发前直接返回。之后 `MagicDialog._UnhandledKeyInput()` 对所有 F1–F12 只检查 `key.Keycode`、`Pressed/Echo` 和 Shift，未排除 Ctrl；有 `_legacySelectedSkill` 时 Ctrl+F1 等会被解释成将 Spell01..04 绑定给该技能并发送 `C.MagicKey`，无选中项时则不切组。该源代码流区别于 KeyBindManager 所声明的栏组切换动作；EI 目标对 Ctrl+F1–F4 的语义仍未从原版证据闭合。 | 源码对照 `KeyBindManager` defaults、`GameScene._Input()` 的窗口门控及 `MagicDialog._UnhandledKeyInput()`。不运行键盘回放；后续先由目标 EI primary-static/匹配版本行为确定技能书打开时 Ctrl+F1–F4 的预期，再修正 modifier gate，并以纯输入单元路径与游戏内安全角色状态分别验收。 |
| SKL-14 | 高，legacy候选技能选择在切换分类后未清理（当前源码可证；EI预期未决） | `MagicDialog.SelectSchool()`更新`_selectedSchool`并重建`_legacyRuntimeEntries`/格子，但没有清空`_legacySelectedSkill`；全文件仅在`BuildLegacySkillSlots()`格点击回调中给该字段赋值，没有reset/clear调用。父窗`_UnhandledKeyInput()`不检查当前分类或所选项是否仍属于当前`_legacyRuntimeEntries`，而是直接对残留tuple的`UserMagic`写`Set1Key..Set4Key`并调用`SendMagicKey()`。因此若之前选中的是已学习技能、随后切至另一类且键事件到达父处理器，F键仍可能改绑旧分类技能；刷新后若点击到无对应entry的格，回调也不会清除旧tuple。属于当前实现内部可静态确认的陈旧选择路径，不证明 EI 原版的分类切换/选择保持语义。 | 只读链：`MagicDialog.SelectSchool()`→`RefreshLegacySkillSlots()`清空并重建runtime entries；只有格点击写`_legacySelectedSkill`；父窗键处理直接读取其`UserMagic`并发送。后续修改前先由EI primary-static/可读目标版核定分类变化时是否保留选中项；实现时让选择状态与当前类别/可见entry一致，并安全处理空格/未学习项。需要运行验证时记录切换前后绑定目标及实际消息，但不得重放坐骑、双人交易或可能Bad Request路径。 |
| SKL-15 | 高，技能书候选列表把同职业未学习技能也纳入legacy格；EI列表成员资格未闭合 | `GetVisibleMagicInfos()`只排除None/Discipline学派、非本职业且未学习的记录，以及已学习但缺少要求戒指的记录；本职业`UserMagic==null`的技能通过筛选。`SelectSchool()`按当前学派把该集合交给`RefreshLegacySkillSlots()`，后者照常显示图标，并将悬停提示标为“未学习”。`MagicDialog._UnhandledKeyInput()`虽在`selected.UserMagic==null`时拒绝绑定，但格子仍进入显示集合；同一方法还为每项创建传入null `ClientUserMagic` 的隐藏`MagicCellView`。因此当前legacy候选画面可以出现未学习技能图标而无法将其绑定，这是当前代码的数据筛选事实；EI `this+0x898+24*cl` 列表的填充来源/是否仅含已学习项仍属primary-static缺口，不能仅按技能书名判定应隐藏或显示。 | 追 EI 分类链表的构造者、节点插入/移除、学习/忘却/职业/戒指状态更新链，确定各`cl`列表成员资格及排序；与当前`S.NewMagic`/`UserMagics`和`MagicInfo`筛选分层比较。目标EI成员规则未闭合前，不把当前“未学习”候选格升格为parity通过，也不直接删除技能记录。后续用含/不含已学技能的可控角色观察成员变化，并记录只读画面证据；勿为本项发送快捷键绑定或有风险业务请求。 |

以上条目确定属于实施计划候选，但最终排序与拆分须等输入路径和其他窗口审计结束后统一确定。

### 技能书证据冲突的裁决记录

Mir3-Research 的旧模拟器验收记录 `skill-detail-verification-evidence.json` 将“8 tabs + 12 real-skill slots”写成技能子系统已闭合，但同一条记录的 `sim_vs_original` 明确区分“sim = grid slots; original = left list + right detail page”，且其几何仍标为 candidate。较新的 `skill-window-render-loop-evidence.json` primary-static 记录反汇编函数 `0x43A370` 仅遍历 6 个 `this+0x7C` RECT，并明确未知 RECT 与列表填充者；`0x43AC80` 则在进入列表命中函数前先处理 3 个帧控件及 8 个类别控件。故本审计把“原版 12 格/4×3/帧 410..421 图标”的旧结论降级为模拟器方案/视觉候选，不采纳为原版布局事实。已能断定当前 12 个 `DXImageControl` 命中格超出了已知原版六个列表 hit rect；仍待追 `0x4397A0` 和 RECT 的写入者，不能从六个矩形直接推断它们在书页上的最终绘制形状。

### 技能书证据再核对（8 个分类控件与 6 个技能列表 RECT 已区分）

对照 `docs/research/ei-ui-layout/skill-window-render-loop-evidence.json`、`skill-window-context.json` 与 Mir3-Research `skill-book-category-tabs-evidence.json`（F547）、`skill-book-draw-evidence.json`（F839）、`skill-tab-header-draw-evidence.json`（F848）、`skill-window-input-evidence.json`（F939）后，按对象偏移可确认 `this+0x2F4..+0x7E0`、stride `0xB4` 的 8 个控件是纵向类别控件，而不是 8 个左页技能条目；F547 的类别字面量/循环数与此相符。帧对存在研究工件内部矛盾：`skill-window-render-loop-evidence.json::window_constructor_control_geometry.controls` 与 RESEARCH_LOG Finding 200 记录八组连续帧对 F450/451 至 F464/465；但 `skill-window-context.json::category_labels` 及 `skill-window-static-evidence.md` 对“黑暗/幻影/剑”重复记为 F450/451、F452/453、F454/455。前者 `+0x678/+0x72C/+0x7E0` 帧对为 F460/461、F462/463、F464/465，后者记录不同值。本机没有研究目标同版 EI EXE，无法从原始指令独立裁决；暂将“八个纵向分类控件”视为已闭合，把“后三个类别控件精确资源帧号”降为证据冲突/待核，不以截图外观或摘要标题选边。F839/F939 将共用 F704 控件类对象简称“skill-slot controls”，不能据此把它们算作八个左页技能条目。另有 3 个头部控件位于 `+0xD8/+0x18C/+0x240`，帧为 F440/441、F410/411、F412/413。

这四份 JSON 实际位于 `Mir3-Research/docs/research/mir3-map-reconstruction/`，不是 `ei-ui-layout/`。F939 文件的 `scope` 只有 `0x43AC80-0x43AD50; input`，`verdict` 所谓 “input + record list COMPLETE”仅指该输入/记录链；其 `cell_analysis` 将三头部控件写成“3 tabs”、将八个分类对象写成“8 slots”，是通用 F704 控件的短标签，不证明这些对象的产品语义或窗口视觉完整。F547 的 `scope` 为 `0x439500-0x4396C0; 8 tabs; selected [0x54]`，八个对象与构造调用偏移、类别字面量和纵向位置吻合，但 `category_labels` 中后三个类别的帧号与 constructor geometry 表冲突；不能把研究文档的帧表视作已交叉一致。F848 独立描述左页按 `[skill+6]` 绘制技能图标、名字和高亮。F848/F839 摘要没有足够指令细节确定列表绘制循环数量/页索引；`0x439500` 的分页候选只证明类别计数参与 `/3` 后的格式化运算，不能单独解释六个 hit RECT 或箭头动作。已有研究工件对控件偏移/调用链与技能 ID 选择读写提供 primary-static 证据；具体类别帧、页几何与翻页语义仍需同版 EXE 或其他独立证据闭合。F410/412 的箭头、F440 的交叉剑外观仅为本机旧 WIL visual-candidate；具体业务语义与实际 hit rect 仍未运行验证。保留工件间矛盾，不把某个 JSON 的 `COMPLETE` 总结词提升成整窗验收结论。

左页实际技能列表是第三组对象：`0x43A370` 对 `this+0x7C..+0xCC` 的 6 个 RECT 做命中，并按当前类别 `[this+0x54]` 从 `[this+0x898+24*cl]` 链表返回技能 ID；`0x4397A0` 遍历该分类列表绘制技能图标/名字，右页 `0x43A440` 接收所选 ID。由此“8 个分类控件”与“6 个技能列表命中矩形”并不冲突；冲突来自摘要中把通用子控件称为技能槽。当前 `BuildLegacySkillSlots()` 添加的 12 个 F410..F421 36×36 格子仍是无原版依据的 simulator 方案，既不是八个类别控件，也不对应已知六个技能列表 RECT。

后续需从目标 EXE 的 `0x439250/0x439500/0x4397A0/0x43A370/0x43AC80/0x43AD20/0x43AD50` 交叉追踪各 RECT 写入坐标、分页计数、列表记录顺序、后三个类别控件帧号与头部控件行为。F848 为 `0x4397A0` 提供技能图标 `[skill+6]`、技能名和高亮框的绘制语义；但绘制迭代范围与 `0x439500` 类别计数除3/格式化运算尚未合并成唯一几何模型，六个 hit RECT 的值也未闭合。先取得并核对研究工件原始 VA/指令摘录及 524,288-byte EI EXE 身份，再实现列表与右页；不复用当前 12 格布局，也不以 F410/F412 导航帧充当技能图标。

#### 原版技能书帧直接复核（2026-09-24）

按用户指定端口在 `http://localhost:8766/` 临时重启 WIL 预览器，root 指向 `/home/tetsuya/mir3ei/LegacyEI`，并用独立 HTTP PNG API 逐帧检查：F400 为512×512原始画布，PNG `/tmp/skill-frame-400.png` 可见左页六条横向列表底槽、纵向类别图标和书页右侧；F410/F411 是左箭头态帧、F412/F413 是右箭头态帧（各32×14，`/tmp/skill-frame-410.png` 至 `413.png`）；F440/F441 是交叉剑帧（20×12）；F414–F419 均为空帧（API导出1×1）。F420/F421（40×20）绘有 F1/F2 文字控件视觉样式。它们进一步否定“F410..F421 是12个技能背景图”的映射；F420/F421 的真实点击动作仍需追目标 EXE 分派链。

这次资源像素复核只确认原始贴图样式，不把 F400 底槽推成点击矩形；`0x43A370` 的六个实际 RECT 仍是代码状态。预览器现在指向 EI 的 `LegacyEI/Data`，可直接用用户给定的 `#file=GameInter.wil&frame=...` 方式逐个复查，且没有修改任何外部 WIL/WIX 文件。

右页部分有相反结论：Finding 272 的 ID 选择、Magic.exp 区段扫描、文本样式和 15 px 行距已经是 primary-static 闭合证据；因此不是“右页原版不清楚”，而是当前 `MagicDialog` 没有实现这条已证实绘制链。原版 F1–F12 绑技语义则尚无对应证据，保留未决。

## 背包首轮审计

原版这里有两种不同的“格”：`bag-list-fill-chain-evidence.json` 证明背包最多有46条物品记录（背包对象 `this+0x774`，stride `0xC2C`）；`inventory-window-render-evidence.json` 的 `0x42F150` / `0x42F2A0` 则证明鼠标 hit viewport 是6×6格，36px pitch。两者不是46个固定可见 cell。占位表为600个WORD、每行6列、每行12字节，即6列×100行；`0x42F440` 通过 `0x42F6D0` 读取 item data `+0x28` 的帧号，并以该帧头宽高按 `ceil(width/36)×ceil(height/36)` 查找/写入占位脚印（primary-static `bag-grid-geometry-evidence.json`、`server-data-crossref.json`）。独立扫描本机 `/home/tetsuya/mir3ei/LegacyEI/Data/inventory.wil/.wix`：WIX 1440项、499个负载/帧头有效的正尺寸帧，最大宽帧F1040为72×180、最高帧F1192为32×212；最大候选脚印2×6由F1066(56×198)/F1193(40×208)/F1240(52×188)等帧达到。仅作容量推算：若46条记录均可引用并重复2×6脚印，first-fit每个6行带可放3条，46条可占到96行；此为INFERENCE，不证明实际EI item集合允许该组合。8766 API 对F1040/F1066分别返回72×180、56×198。WIL/WIX SHA-256为 `430b0593…cca48840` / `69bf695b…e062`。该全库最大值不能证明这些帧均可作为目标EI背包物品图标，也不能证明目标EXE使用同哈希资源；但足以说明单凭46个record数推定“最多10行”不成立，须先恢复实际可用item-frame集合及目标版映射。研究材料有 `this+0x2C4` 与背包对象 `this+0x324` 两种偏移写法，差值恰为 `0x60`；这与背包对象嵌入窗口/容器对象后使用不同基址相符，读者不能把两个偏移当作矛盾或无条件视为同一个 `this`。实现前仍须在相关函数入口逐一注明 ecx 对象类型与基址。`0x42F79C` 绘制循环记录的行扫描范围约为 `[scroll-5, scroll+6)`，比6行命中视口更宽；这是为跨行/多格物品的绘制留出扫描范围，不能把循环迭代数直接当作屏幕可视行数。F280 Gauge 构造参数及 `0x42F150` 的独立 hit-test 均支持6行视口。故46是可装物品记录容量，而100是占位表行容量；但“46条记录→10行→4有效滚动行”也不能只凭记录数成立。Finding 301给出10行/4有效行估计，仍需以46条记录允许的最大物品宽高、first-fit放置和网格表行读取链独立闭合；在此之前把理论表容量与实际物品最大占用高度分开记录。

滚动链由 F280 专用 gauge 处理：EI-301 `trade-split-handle-evidence.json` 给出 6行视口、`0x5E=94` 滚动比例参数、F280 原图 16×424、控件宽12px、轨道高218px；constructor `0x417960` / paint `0x4179B0` 以及 `0x430056`/`0x430696` 的写回把滚动状态映射到 `[this+0x58]`，`0x42EB94` 用同一字段绘制 gauge。600 WORD/6列=100行；若占位表填到末端，100−6=94是理论viewport起始行的结构上界候选。94在输入链中是共享定点比例尺度（写入 `trunc(position×94)`、paint归一化为 `value/93`），不代表背包含94行物品。与此同时，`[bag+0x58]`被网格paint用作行扫描参考，满表100行减6行viewport恰有94个结构上可用的起始偏移；这证明理论表坐标上界，不证明用户内容滚动最大值/实际拖拽终点。旧 `inventory-window-render-evidence.json` 中“scroll field 只有 reset 写者、因此 gauge 为空”的注释已被 EI-301 writer 追踪 supersede。帧直接解码确认 F280 offset(-24,-16)、alpha bbox(0,0,13,423)；其有效绘制矩形、拖柄的精确 hit rect 和滚轮步进/页步还要按完整 gauge hit/paint 函数核实。

**INV-04 gauge 对照补记（2026-09-24）：**逐段对照 `inventory-window-render-evidence.json::paint_geometry[0]`、EI-301 gauge 类记录和当前 `InventoryDialog.ApplyLegacyEiLayout()`：EI 构造 F280 子控件时传入 `visible rows=6`、fill width `12`、fill/track viewport height `218`、padding `12`、vertical mode；背包 paint 以 `x=window.x+0xF8`、`y=window.y-0xA5` 调 `0x4179B0(value=[bag+0x58], max=94)`。F280 源帧是16×424，独立解码 alpha bbox `(0,0,13,423)`；帧可见像素、共享 gauge 的12×218填充/裁剪区、父窗口有效绘制区是三种不同矩形，不能把424px源高直接当 viewport 高度。EI-301 已闭合 `[bag+0x58] = trunc(gauge_position×94)` 的写入，以及F280 paint读取该值；研究明确94是共享定点比例尺，不是内容行数。由于网格paint也用`+0x58`选择行扫描带，100行表的6行viewport对应理论起始偏移0..94；但实际内容占用高度、交互可到达区间和拖动边界仍须分别核验。旧 inventory JSON“无写者、恒为0”已过期；零值初始态仍应画F280轨道与起始拖柄。当前 `InventoryDialog` 把旧 F360 绘制代理 `WeightBar` 整体 `Visible=false`，没有 F280、gauge child 或原版 `[bag+0x58]` 等价滚动状态；`DXVScrollBar` 用 Interface 新版上下按钮/外框，不能直接等同 EI 的共享 gauge。静态输入链现已由 `mir3-map-reconstruction/scrollbar-family-evidence.json`、`inventory-ctor-click-use-evidence.json` 与 RESEARCH_LOG Round 401/630 闭合：父处理器 `0x42FFD0` 将 gauge `+0x278` 的点击交给 `0x417D00`，拖动/滚动分支更新 gauge 位置，并由 `0x430056`/`0x430696` 将位置比例乘94后截断写入 `[bag+0x58]`；父窗口绘制再从该字段调用 gauge paint。通用 `0x417C80`/`0x417D00` 记录命中 rect、拖动标志和10ms重复步进门。当前 `InventoryDialog` 缺 F280、gauge child 和原版滚动状态/输入路由，`DXVScrollBar` 也不是其等价物。剩余静态问题是 F280 最终帧偏移屏幕锚点及 EI 滚轮每次步进值；行为结果仍需安全、可观测的游戏画面对照，不能仅凭通用 gauge 步长字段推定背包滚动行数。 |

此前实现尝试把46条记录当成6×8固定格，并把滚轮范围限到两行；尽管运行画面内容随滚轮变化，这只证明了错误表示可以滚动，不能证明与 EI 一致。该实现已撤回并在 `578357a2` 推送。当前 `InventoryDialog` 又回到6×6固定格，无原版占位表、46记录自动排位或 F280 gauge 的等价实现；背包滚动仍未修复。下一步须将“物品记录索引”和“世界占位格坐标”分离，先逐函数确认占位表基址、解出 `0x42F6D0` 的 frame 尺寸到格子 footprint 与 first-fit，再重建 viewport renderer/hit-test 和 gauge；禁止再将记录数换算成行数，也不能把绘制扫描范围当作 viewport 高度。

原版 F250 根窗为 284×324；GameInter WIL 有效像素 bbox `(114,94,281,324)`，当前背景偏移 `(-114,-94)` 与根矩形对齐。模式 byte `[bag+0x54]` 有四态：0 包袱、1 修补、2 变卖、3 储存；修补/变卖/储存由服务端消息分支写入。EI-288 的三个页签控件只是播放音效的装饰按钮，不负责设置模式。旧审计把“本地三按钮改模式”写成待核，现据 `inventory-mode-tabs-evidence.json` 与 RESEARCH_LOG EI-288 修正此结论。

**客户端触发链补核（源码确认，2026-09-24）：**Godot `GameScene` 的 `S.NPCRepair` 订阅只调用 `NPCDialog.RepairResult()`；`NPCDialog.ShowPage()` 在页面类型为 Repair 时直接 `SetInventoryLegacyMode(Repair)`，而 BuySell 页面调用 `ShowInventoryForNpcSale()`。`InventoryDialog.SetLegacyMode()` 的 XML 注释称其供“回包”切换，但实际调用点是 `NPCDialog.ShowPage()` 的页面响应处理；这条注释与真实调用时点不符。仓储 mode setter 没有源码调用者，当前仓储窗口由 `_storageDialog` 单独管理。EI 的消息号/字段链仍引用研究工件 primary-static 记录，因目标 EXE 身份限制尚未独立重放。该差异是两种客户端协议表示/触发阶段的静态发现，是否导致用户可见行为不一致仍须核同状态页面及切页/关闭路径。


**背包图标资源链复核（修订，2026-09-24）：**`inventory-window-render-evidence.json` 记录原版物品 frame WORD 来自物品数据 `+0x28`，用于 `0x42F6D0` 的物品尺寸查询及 `0x466130` 图标绘制；selector 是 `0x5668C4`，EI WIL 句柄表将 el82 绑定为 `Inventory.wil`。当前 `DXItemCell.ItemIconLibraryFile` 固定为 `LibraryFile.StoreItem`，`DrawItemIcon()` 取现代 `ItemInfo.Image` 并从常规`Data/StoreItem.Zl`查帧。本机EI `inventory.wil/.wix`有1,440个索引，常规`StoreItem.Zl`有8,590个元数据索引。**之前用`Data/Inventory.Zl`做的445帧比较并非普通背包实际读取的图库；不能拿它证明当前背包普遍错图。**重新用独立`wilsdk.py`与`zlsdk.py`直接比较EI `inventory.wil`和实际`StoreItem.Zl`：两边同索引且宽、高、offset一致并成功解码的322帧中，320帧alpha mask完全一致；322帧均因RGB565与ZL/BC7编码差异而没有RGBA字节级全等，其中266帧可见像素RGB MAE≤10、317帧≤15。直接索引抽样`/tmp/ei-inventory-storeitem-direct-index.png`显示F0/F4/F411/F1410/F1412 alpha形状一致且颜色接近；F8/F20虽无同样精度的像素匹配，但大体仍是同类药水图案，区别主要是轮廓、瓶身颜色及高光。F8/F20局部并排图见`/tmp/ei-inventory-storeitem-mismatch-examples.png`。因此两库有大批同号视觉对应帧，也存在应做像素校准的旧版美术差异；**这不等于图号语义不同或物品已错配**。常规`Data/Inventory.Zl`旧抽样图`/tmp/ei-inventory-storeitem-sample.png`不代表`DXItemCell`实际查到的StoreItem结果。通过 MirDB 只读读取当前`Data/System.db`另确认现代物品 #136 Healing Potion (IV) 的`Image=8`、#153 Rejuvenation Potion 的`Image=20`，因此这两个帧可作为有名称的当前物品候选验收样本；本机`LegacyEI/Data`没有对应`System.db`/`stditem`旧物品表，不能据现代名称反推 EI `+0x28`。旧记录`+0x28`与当前`ItemInfo.Image`是否对同一物品保持同号仍需原版物品表或旧版运行数据交叉确认；WIL/ZL解析器差异、绘制原点/offset和多格物品脚印也仍需验。不能将“错库已证实”作为背包下一步修复依据。

当前 Godot 创建 F264/265 声音控件但未绑定模式动作；legacy 布局现已隐藏与其同位的钱包按钮，不再存在钱包点击被透明按钮覆盖的当前路径。F267/268 来自 `Interface1c.wil`，显示人物风格图像；视觉不足以证明其语义，当前实现没有显式呈现此帧。直接预览 GameInter F360 是36×34圆形绿色图标，不能作为负重条；原版 paint 链使用垂直 gauge F280（16×424），在根相对 `(248,-165)`，轨道/拖柄区域12×218。当前 `ApplyLegacyEiLayout()` 将整个 `WeightBar` 隐藏，且没有建立 F280/gauge 等价控件，因此不是“零负重所以没有仪表”：共享 paint `0x4179B0` 即使 value `[this+0x58]=0` 仍绘制 F280 track 并 blit knob rect，只是位置归一化为0；这一点由 `trade-split-handle-evidence.json` 的绘制顺序闭合。资源查看器PNG `/tmp/inventory-frame-280.png` 与 `/tmp/inventory-frame-360.png` 直观显示前者为细长垂直仪表轨道、后者为圆形绿色图标。旧的 F360 横向负重条已隐藏，但 EI 的 F280 空位置轨道/拖柄仍缺失；需按 gauge 的 track、clip、knob rect 分别重建并复核滚动命中。原版主数值使用固定 `%d` 绘制，但 EI-295 把其字段归为恒零死配置槽（可能是未用金币上限仅属推断）；当前 Godot 添加 Gold/GG 两行和模式标签，需分别对照 F250 内嵌美术与原版绘制位置，不能把扩展货币视为旧版控件。

| 编号 | 严重度 | 发现/疑问 | 状态 |
|---|---|---|---|
| INV-01 | 阻断，高，已证实 | EI 有46条物品记录、6×6屏幕 hit viewport 和6×100 WORD占位表；首屏几何其实已逐格吻合：EI `window-relative x=25+36*column,y=41+36*row`，Godot legacy `Grid.Location=(25,41)`、`GridPadding=.5` 经 `(int)(x*36+.5)` 截断后同样是36px步距，cell为36×36，36个当前 hit rect 坐标与EI首屏6×6精确一致。结构上当前 `GameScene.Inventory` 为48项数组，legacy `DXItemGrid.GridSize=6×6` 只创建36个固定slot `DXItemCell`，因此当前只能直接显示/命中服务数组下标0..35，36..47没有可见格；46是EI item-record容量，不应误读成46个同时可见格。更重要的是当前36格按 `slot→x/y` 固定排位，不能表达原版独立的物品记录 `+0x774+slot*0xC2C`、记录内列/行 `[+0x778/+0x77C]`、多格占位和6×100 WORD occupancy 表；同为6×6只能说明命中视口外形，不能证明当前36个格子映射等于原版视口。上一轮“46格/8行/滚2行”实验已撤销，不作验收证据 | 独立解码 `0x42F6D0` first-fit 与 item frame 宽高→占位格算法；实现46条记录/600 cell occupancy/6×6 view/94-scale F280 gauge 与对应 hit-test；真实填满、滚到顶部和底部，核对放置、拾取、拖放、重叠、多格物品、滚轮、拖柄和越界，记录截图 |
| INV-02 | 高，EI装饰控件与Godot关闭/模式业务存在静态差异；服务端触发点未闭合 | EI primary-static `inventory-mode-tabs-evidence.json` 明确三个子控件：F161/162在`this+0x5C`、F264/265在`+0x110`、Interface1c F267/268在`+0x1C4`；三者的按钮vtable `0x4177F0`只播放声音，父点击处理器`0x4300F0`读取而不写 mode byte，模式由服务端消息分支写入。此证据将较早 `inventory-window-render-evidence.json` 对 F161/162 的“close/confirm candidate”收窄为窗口内一个装饰控件，不足以支持它关闭窗口。Godot却把 F161/162 `(249,288)` 绑定`WindowManager.Close(this)`；F264/265被映射到`_legacyActionButton` `(176,262,64,20)` 且没有业务回调；原版 F267/268 `(176,286,76,88)` 当前没有对应控件。Godot当前在`NPCDialog.ShowPage()`收到`DialogType.Repair`页面响应时本地调用`SetInventoryLegacyMode(Repair)`；`S.NPCRepair`只进`RepairResult()`。变卖由BuySell页面触发`ShowInventoryForNpcSale()`/`SellMode()`；Zircon页面响应与EI消息号不能直接等同。`InventoryMode.Storage`枚举、`IsStorageMode`及`SetLegacyMode(Storage)`只提供标签/状态占位；`rg`确认`SetInventoryLegacyMode()`唯一业务调用点是NPC修理页传`Repair`，没有路径切入背包Storage态。当前仓储走独立`StorageDialog`与`GridType.Storage`的通用`ItemMove`（WH-01/03），不能据枚举存在就当成已实现EI `0x2BC` bag-manager mode3或其`0x111..0x113`动作。legacy已隐藏透明`WalletButton`，不再覆盖F264区域。 | 不将 F161 控件当关闭按钮：沿EI父窗口 hit-test/窗口ID toggle追关闭动作是否只由 HUD id0 入口负责；补查F267/268所属图层、是否拦截背包输入及资源语义。逐条对应 EI 0x29C/0x286/0x2BC 与 Zircon 页面/修理字段，确认NPC关闭或换页怎样复位`InvMode`；明确Storage背包态与独立仓库的产品差异。模式/窗口开合回放待可观测运行场，不能用静态setter或按钮声音自证。 |
| INV-03 | 中，资源版本/帧状态冲突；子控件语义未决 | primary-static `inventory-window-render-evidence.json` 将背包 `this+0x1C4` 的 child 记为 Interface1c F267/268，候选 RECT 为根相对 `(176,286,76,88)`，并在视觉说明中称“F268 is empty in the current export”。本机 `LegacyEI/Data/Interface1c.wil/.wix` 与该说明冲突：WIX 为2000项，F267/268 两项均有非零有效偏移；F267 原始头 `76×88,(18,64),2483 words`，F268 `60×106,(24,46),2463 words`，负载均在文件范围内。独立 `wilsdk.py` 解码及 PNG [`interface1c-frames-267-268-wil-2026-09-24.png`](evidence/legacy-ei-ui/interface1c-frames-267-268-wil-2026-09-24.png) 显示两帧均有装甲角色持武器图像；alpha bbox 分别 `(0,0,73,87)` 与 `(0,0,57,105)`。本机 WIL/WIX SHA-256 分别为 `9c02eebc…f692589b` / `23abe457…62f4bd9d`。补查现代 `/home/tetsuya/mir3ei/Data/Interface1c.Zl`：独立 `zlsdk.py` 头表解析为3020帧、version 0，F267/F268 均无头且 `is_blank=True`，与旧版 WIL 的两张非空人物图不同；这证明两个现存资源集不等价，但不能证明研究 EXE 使用哪套资源。研究 EXE 所在 `/home/tetsuya/NAS/TMP/.../Data/Interface1c.wil` 当前不可读，无法确认“空帧”来自目标资源版本、导出路径还是研究说明错误。F267 初始 RECT 与 F268 实际头尺寸不同，构造器按初始帧创建 RECT 的证据不等于证明 hover 图像不裁切。8766当前`/api/info`还返回F267/F268各自`shadow=true`及独立shadow offset（分别(-7,-38)、(-1,-47)）；Godot `LegacyWilLibrary.TryGetHeader()`只保存宽高/主offset，解码器的RLE注释明确未使用mask plane。由于目标研究没有闭合该Interface1c子控件的shadow消费调用，不把header标记直接等同于UI必画阴影；这是一项实现能力/渲染语义待核。当前 Godot `InventoryDialog` 没有对应 Interface1c 子控件。 | 恢复研究工件所用资源目录并逐字节核哈希/帧头；沿 `0x417550`/`0x4177F0` child 创建、重定位、绘制、命中调用确认第三控件的显示及输入语义；实核 F268 更大高度是否被裁剪/绘制到窗口外。版本差异闭合前保留“旧 WIL 两帧非空、现代 ZL 两帧空、目标资源版本未定”，不要臆定通用按钮或直接移植成本机视觉。 |
| INV-04 | 高，负重/货币绘制区部分按静态记录修正；EI F280 gauge 整体缺失 | 背景/根窗和初始网格几何吻合。负重文案已移至根相对 `(134,24)`、字体10的原版矩形，并仅由包袱模式数据提供；legacy 隐藏原先错误的 F360 横向条及无旧版依据的 GG/钱包控件。复查后发现 `WeightBar.Visible=false` 连 EI gauge 也一并隐藏；原版 `0x4179B0` 总是 FetchFrame F280 track 并 blit knob rect，`[this+0x58]=0` 仅令位置归一化为0，不令控件消失。独立WIL viewer 确认 F280 为16×424垂直细轨、F360为36×34绿色圆图标（`/tmp/inventory-frame-280.png`、`/tmp/inventory-frame-360.png`）。单一底部数字移至 `(65,282)`、10px、原版色候选；Gold 语义仍待证明。 | 实现 0x4179B0 的 F280 track、12×218 clip/viewport、knob rect 与 drag/hit-test；用独立公式核几何与归一化，运行中核零值位置、滚轮/拖动方向、列表数据滚动及界面真实截图。用同状态角色数据核对负重两字段、文本颜色/基线和模式切换；查明底部数字与 `[0x7DA100]` 的来源；检查 F264/265、F267/268 和 HUD 钱包点击路径。 |
| INV-06 | 高，当前源码注释把 F280 滚动状态误称为恒零仪表值；实现范围需在迁移计划中纠正 | EI 背包构造链把 GameInter F280 作为 F280 gauge（成员 `+0x278`，12×218 viewport；94是写回/显示共用的定点比例尺度，不是背包内容的行数；它经+0x58参与行扫描，但有效内容范围/交互终点仍待核）；`inventory-ctor-click-use-evidence.json`确认 `0x42FFD0` 将点击送入 `0x417D00`，`scrollbar-family-evidence.json`确认点击/拖柄更新 ratio 与位置，背包父处理器 `0x430056/0x430696` 再将 `gauge_position×94` 截断写入背包 `+0x58`，绘制链 `0x42EB94` 用该字段读取滚动位置。600个占位WORD÷6列=100行是占位表理论容量；100−6=94只能算填满表时的理论viewport末端偏移候选。新核的Finding 301明确比例因子94不是行数；输入位置×94会写回整数，网格paint又以`+0x58`决定扫描行带；这让比例值与row-reference存在直接消费链，但实际非空物品最大占用行数、输入端是否可到索引94、索引超出内容后的视觉行为与拖动边界尚未独立闭合。该链说明它属于滚动状态而非角色负重数值；F280在位置零仍绘制轨道和拖柄。当前 `InventoryDialog.ApplyLegacyEiLayout()`隐藏`WeightBar`，没有F280控件，也没有等价`+0x58`滚动状态/输入路由；同方法中的注释“EI的F280仪表值在此版本始终为零”与上述primary-static输入链冲突。此前研究摘要“字段只有reset写者、故恒零”已由EI-301写回调用链取代，不得继续用于实现决定。 | 第一阶段只修订此审计结论，不改代码。后续计划须把F280作为背包viewport滚动控件重建：先独立复核track/clip/thumb几何、`position×94`写回与paint `/93`归一化、`+0x58`到row scan的消费，以及表末端索引94是否可达和超出真实内容后的行为；另外闭合实际可由EI物品记录引用的Inventory.wil帧集合、46条记录的最大first-fit占用行数与滚轮步进；全库最大2×6帧目前只作候选边界，不直接套用到物品记录。再实现occupancy表draw/hit-test与gauge联动；用分离公式核算0、93、94候选端点及中间值，安全运行场验首末非空行、空白区、拖柄和滚轮。负重文本 `负重:%d / 总量:%d` 是另一条mode0绘制路径，不与F280 gauge合并。 |
| INV-05 | 阻断高，逐物品映射/绘制仍未闭合；图库归属静态冲突已裁决 | EI 背包 paint 的 frame WORD 来自原始 item data `+0x28`，传到 selector `0x5668C4`。`status-window-render-evidence.json` / Finding 246 的 selector owner 与路径填充链把全局 el82=`0x5668C4` 绑定到 `Data/Inventory.wil`（路径槽 `0x570574`，写入点 `0x453804`）；背包 paint 与人物普通装备格确实调用同一 selector。Finding 266 后续明确更正 StoreItem 的扩展槽为 el139=`0x56B0E8`（路径槽`0x573F58`），其绘制分派是商店路径。F465/F651 把 el82 描述为 StoreItem/default 的摘要，与这两项更具体的路径写入及分派证据冲突，应按过期/误标处理；`server-data-crossref.json` 的“el82 filename unproven”也是早于 Finding 246 的旧候选。故本审计裁定目标研究工件中的普通背包图标 selector 为 Inventory.wil；机器码仍受研究版 EXE身份限制，尚未在同一目标 EXE 原始字节上独立重放。本机 `DXItemCell` 使用现代 `StoreItem.Zl` 与 `ItemInfo.Image`，并以`CenterImage=true`居中在36×36子控件。源码链为`LibraryCore/SystemModels/ItemInfo.cs::Image` → `DXItemCell.GetItemDrawIndex()` → `MirSkin.GetTexture(ItemLibraryFile, drawIndex)`，而默认`ItemLibraryFile=StoreItem`；这只证明现代数据字段/图库关系，不证明它与EI item data `+0x28`对同一物品保持相同帧号。本机`Data/System.db`与`Database/System.db`大小均11,662,800字节且SHA-256相同（`2547c345…d4581c`），只是同一份当前数据库副本；`LegacyEI/Data`没有旧版物品表或`System.db`。先前读得现代物品#136/#153、Image=8/20仅为当前库候选样本，不能升格为EI物品映射 | 取得与目标EI EXE/WIL同批的旧版物品记录表（可识别物品名/ID及`+0x28`图号），或来源可核验的旧版运行物品记录；再与现代`ItemInfo.Image`逐物品匹配。核验像素、alpha bbox、绘制原点、颜色/tint和多格脚印；直接读EI WIL时以独立实现复核帧头和offset。当前`System.db`不满足旧版数据来源条件，不得用同名/同号或总体相似率代替映射。 |
| ITEMTIP-01 | 高，EI 背包悬停提示与当前全局物品提示在触发条件、时序、外观和数据入口上不同 | 目标 EI primary-static `item-tooltip-and-store-family-evidence.json`：背包鼠标链 `0x42FAB0→0x42F240` 命中46槽后，对占用记录调用 `0x4341F0`，坐标为指针`(+10,+10)`、icon flag=0；`0x4341F0` 按 `+0x64` 行数和 `+0x70+i*0x3C` 字符串画15px行距，提示框随指针浮动（输入锚点外扩约5px），带 `0x329696` 背景、物品类型允许时的 `0x5668C4` 图标、右边界800px裁切。当前 `DXItemCell.OnHoverEnter/OnHoverLeave/_Input` 对所有使用该格子的窗口直接设置 `GameScene._hoverItem`；`UpdateMouseItem()` 每帧显示一个全局 `_hoverLabel`，锚点为指针`(+14,+10)`、文本只用一色、没有 EI 图标路径，背景为近黑半透明和棕色边框；未见 EI `MouseControl == bag` 一类窗口/鼠标分派门，也未见一秒延迟。可用旧版 `Client/Scenes/GameScene.cs::CreateItemLabel()` 的一秒刷新延迟和富文本/图标 ItemLabelBuilder 属于该源码版本的“拿起物品(MouseItem)”路径，不能把它等同 EI F340 的格子悬停链。研究 `bag-tooltip-verification-evidence.json` 的“closed”结论只覆盖模拟器提示链/浏览器画面，不证明 Zircon 或目标 EI 同态通过。 | 将背包、人物特殊物品槽、交易双方、NPC/socket、腰带分别列出实际 hover owner、候选时延与提示调用；逐项核原版 `0x4341F0` 的行来源、图标flag与clip，独立比较 Godot label 的像素框、字体/颜色/背景/边缘和指针位置。先由 matched EI 源码/运行证据确定 hover 与拿起物品提示的差异，再修复统一 `_hoverItem` 把所有容器混为同一触发链的问题；持双人交易运行依赖的路径留阻塞，不以静态同名文本验收。 |

#### 物品提示的 EI 跨容器调用链复核

以下路径来自研究目录保存的原版反汇编工件（primary-static），调用来源和数据对象不同；不能因为多个分支最后调用 `0x4341F0`，便把它们折成同一种控件行为。Godot 一列是当前源码静态调用，不代表屏幕结果已经运行验证。

| 容器/上下文 | EI 命中与提示入口 | EI 图标/数据差异 | 当前 Godot 路由与边界 |
|---|---|---|---|
| 背包 id0 | `0x42FAB0→0x42F240` 扫46条物品槽；占用槽传记录内 item 子对象到 `0x4341F0`，位置为鼠标`(+10,+10)`，flag=0 | 普通背包提示关闭 tooltip 内置图标；EI背包画格图另走 el82=`Inventory.wil` | `DXItemCell` hover 直接更新场景级 `_hoverItem`；物品格由 `DXItemGrid` 构造。此链与 EI 记录列表/6×6 viewport 不同（INV-01/05） |
| 状态/装备 id1 | `0x44B6B0→0x44B720` 扫位置记录 `this+0x1C0+i*0x10`，对应物品记录 `this+0x2F4+i*0xC24`；命中后以`0x4341F0(x,y,0)`出详情提示，flag=0 | **tooltip caller**最后参数明确为0，故不启用提示框内置图标。另一个独立的 status paint slot loop 经`0x430A40`绘装备/角色区图像：普通槽 selector el82=`Inventory.wil`，特定索引0/1/4的角色区合成 selector el83=`Equip.wil`；这是窗口内容绘制资源路由，不能拿来当 hover tooltip 的flag | `CharacterDialog` 可见格也用`DXItemCell`；纸娃娃由`PaperDoll`单独绘制。当前全局 tooltip 与EI的状态窗 hit/record路径未分开，纸娃娃区域和特殊槽具体命中/hover还需按CHAR-01..04对照 |
| 商店 id2 | `0x44E650→0x44E800` 仅 mode=1/2 解析相应列表物品；按 `[item+0x22]` 类型0xA/0xB设 tooltip 图标 flag 后调用`0x4341F0` | 图标显示由商店记录类型门控，不等同背包恒为 flag=0；这是同一 store 对象的模式切换。F340 ctor 的26个初始槽数组不等于商店总容量：后续 RESEARCH_LOG 的 `0x44D180` reset 链记录 `+0x660/+0x6B0/+0x720/+0x7F4/+0x804` 多数组合计约90槽；精确各状态页容量/消费者仍应按状态分列，不能继续把26写成全窗总槽数 | Zircon `NPCGoodsPanel.RefreshRows()`实际列表为`DXButton`加`DXImageControl`，不走`DXItemCell`通用 hover；出售来源的背包格仍走它。`GameStoreDialog`属现金商城扩展，虽有`DXItemCell`，不等同EI NPC store（WH-01/02） |
| 仓库窗口候选 / 当前 `StorageDialog` | EI共享 store 对象的 `0x44E650` 在 mode 1/2 调 `0x44E800` 命中、`0x44E7D0` 解析物品后进入`0x4341F0`；`[item+0x22]` 类型`0xA/0xB`决定 tooltip icon flag。state2/F1001及分页控件见WH-01/02，但商店状态的人类业务名仍候选，不能将 mode2直接认作独立仓库语义 | 该提示路径存在于同一共享对象的 mode1/2；当前资料未证明mode2与Zircon Storage一一对应，亦未证明其tooltip page/slot锚点 | 当前 `StorageDialog.ApplyLegacyEiLayout()` 生成4×3 `GridType.Storage` 网格；cell由`DXItemGrid`创建为`DXItemCell`，`OnHoverEnter()`无容器判别地调用`GameScene.SetHoverItem(Item)`，随后场景级`UpdateMouseItem()`统一显示全局`_hoverLabel`。这说明当前仓库继承通用提示实现，但EI状态/页基址、提示图标门、命中记录、裁切和开窗层级没有随之闭合；参见WH-02、ITEMTIP-01 | 保留为静态映射候选；先核EI mode2完整点击/record路径与资料版本，再决定是否为legacy仓库实现单独的hover owner/renderer。不要把同名Storage容器或共享提示函数直接当语义等价。 |
| 交易 id3 | `0x415B10` 检查 hover 与 bag-active/state gate，再由 `0x416830` 命中、`0x4162E0` 解析双方物品并写入`0x7243DC`名称状态；primary-static 工件确认交易专属 item-hover helper。F341摘要称其走“tooltip via 0x4341F0-style”，但没有在这里独立列出与背包相同的绘制调用参数，故不能当作完全相同的提示绘制链 | 交易有独立pane/split hit区、状态门和物品名称缓冲；不能直接套用背包槽索引或icon flag | 当前交易 sides 是`DXItemGrid`/`DXItemCell`，因此汇入通用`_hoverItem`文本框，不保留EI pane与交易专属hover状态。两人真实交易受外部参与者条件阻塞；本轮仅登记静态差异，未作交易输入 |
| 其他当前容器 | 现有 primary-static 样本没有证明 EI 为所有网格提供相同 hover 语义 | 当前 EI 证据不支持把自动药水、邮件、socket、商城或寄售等现代业务的格子 hover 外推到原版 | 可见业务若复用`DXItemCell`会共用全局提示；现金商城有该cell，socket/自动药水等亦有实例。相反 NPC 商品展示行为是按钮行而非`DXItemCell`，不能一概说其复用通用提示。哪些当前窗口应在 legacy 隐藏/隔离，需逐窗判 EI 节点身份 |

坐标注意：背包 caller 的 `(+10,+10)` 是传给提示 renderer 的锚点；renderer 再将其外扩为浮动矩形，并在右侧800px边界裁切。不能直接将 EI tooltip 左上角记为“鼠标+10”，也不能拿当前 `_hoverLabel` 的位置 `(+14,+10)` 与锚点数字作像素等价结论。状态窗 hover 的确切鼠标偏移、商店的hover绘制锚点、交易helper对renderer的确切参数，以及窗口叠放/裁切关系仍需回到相应完整调用记录或目标运行画面核实。状态窗 paint 使用的 el82/el83 selector 分流是另一条静态链，不代表tooltip caller最后参数。

计数冲突记录：`item-tooltip-and-store-family-evidence.json` 的首轮 constructor摘要只列`+0x660`处26槽循环；同一研究目录后续 `RESEARCH_LOG.md` 对 `0x44D180` reset 链的复核把多个数组（`+0x660/+0x6B0/+0x720/+0x7F4/+0x804`）合计为约90槽，并明确更正“26槽=首网格”。故本审计只将26写作F340首数组/首网格证据，整体容量仍需逐mode、逐数组映射，不能继续引用模拟器的26项为原版总量。

## 人物状态/装备窗首轮核对

### 素材边界复核（独立于 Godot 布局常量）

本轮直接从 `/home/tetsuya/mir3ei/LegacyEI/Data/GameInter.wil` 读取帧头并用研究仓库的 `wilsdk.py` 解码像素；Pillow 用临时 `nix-shell` Python 环境运行，未安装为系统依赖。结果：

| 帧 | 原始画布 | WIL offset | 非透明 alpha bbox | 迁移根窗口 |
|---|---:|---:|---:|---:|
| F200 | 256×512 | (7,-44) | (6,92)–(247,419)，241×327 | 244×328，图像位置(-6,-92) |
| F201 | 1024×512 | (7,-44) | (252,92)–(770,419)，518×327 | 520×328，图像位置(-252,-92) |

按 bbox 计算，当前 F200/F201 图像位置恰好把有效像素左上角映射到窗口 `(0,0)`；F201 根窗口宽度也覆盖其 518 px 有效绘制范围。两帧高度均为 327 px，有效像素底边差 1 px 属于边界包含/窗口尺寸差异，需运行时确认裁剪边缘。`/tmp/ei-gameinter-200.png` 与 `/tmp/ei-gameinter-201.png` 是解码图像，视觉上能看到 F201 是左右组合面板画布，alpha bbox 只覆盖 x=252..770；故之前按整张 1024 px 画布猜窗口宽度会错。随后独立以 Pillow 对两帧有效左面板进行精确比较：F200 crop `(6,92)-(247,419)` 与 F201 crop `(252,92)-(493,419)` 同为 241×327，全部 78,807 个 RGBA 像素逐点完全相同。这证实在逻辑原点/像素坐标对齐后展开图会原样保留左面板底图；当前工作区切换整张 F201 的绘制方案因此有素材依据，但仍须鼠标点击前后屏幕截图测量实际抖动与裁剪。

### 已确认实现差异

- `equipment-slots-evidence.json` 的最终 Finding 265 将协议槽位与原版 hit rect 一一对应：头盔 idx2=(27,264)，鞋子 idx9=(64,264)，毒药 idx10=(103,264)。此前 `CharacterDialog.ApplyLegacyEiLayout()` 生产布局及其 `AuditLegacyEiLayout()` 期望表都把两者交换；2026-09-24 已按 primary-static rect 将这两张表改为 Shoes `(64,264)`、Poison `(103,264)`。独立对照的依据是EI构造器 hit rect 与协议 slot byte链，不是原测试中的艺术标签。LegacyHudLayoutLab headless audit 通过 `character=True`、`slots=True`；由于自审计与生产表同处一实现，仍不能替代原版逐格点按/拖拽和线上 wire 行为验收。
- 原版切换控件为 F171/172 与 F168/169 两组 36×36 状态帧，窗口相对 hit rect `(176,264,36,36)`；静态切换时从 244×328/F200 变为 520×328/F201，根窗口原点不变。`CharacterDialog.cs` 当前HEAD版本已改为展开态将同一背景控件切到 F201、把根宽设为520并裁剪，同时隐藏 F200 属性标签、显示扩展文字；这与此前已提交的“双背景并列”实现不同。本轮已经在含此工作区版本的运行客户端做了实际鼠标切换，完成态位置记录见 CHAR-02；帧视觉状态、鼠标事件后的首帧、屏幕缩放时序仍须连续录屏/逐帧截图验收。
- **F168 用户指定帧复核（独立 WIL 解码，2026-09-24）：**8766 当前无 8766/tcp listener，`curl http://localhost:8766/` 返回 `curl: (7) Failed to connect to localhost port 8766`。使用 Mir3-Research `Tools/common/wilsdk.py` 从 `/home/tetsuya/mir3ei/LegacyEI/Data/GameInter.wil` 独立解码 F168/169/171/172；默认 Python 缺 Pillow，改用临时 `nix-shell -p python3Packages.pillow` 成功，没有安装系统包。四帧头均为36×36、offset `(-24,-16)`、1404 words，alpha bbox 均覆盖全帧；contact sheet [`gameinter-frame-168-172-wil-2026-09-24.png`](evidence/legacy-ei-ui/gameinter-frame-168-172-wil-2026-09-24.png) 放大显示 F168/169 是左向箭头两状态、F171/172 是右向箭头两状态。源码 `CharacterDialog.ToggleLegacyView()` 在展开时用168/169、收起时用171/172，与 `status-window-render-evidence.json` 记录的 mode=1/0 帧归属一致。单帧解码验证素材朝向和 Godot 帧选用，不证明 EI/Godot 的最终屏幕锚点、pressed 时序或点击命中；这三项仍按 CHAR-02 保留运行态待验。
- 原版画槽顺序与装备枚举对照已经 primary-static 闭合（11 条记录，索引即 wire slot byte；8 个普通装备格另有纸娃娃/人物区记录），但当前只展示 8 个可见格是合理候选。还需要核对空槽占位纹理、物品图标 WIL 选择器、战斗中直接装备/拖拽/点击使用的行为。

展开开关的静态预期与屏幕上的实际运动需要分开。原版 `0x44CCD0` 点击分支把状态页从 244×328/F200 切成 520×328/F201，保留根 `x/y`；F201 alpha bbox `(252,92,518,327)` 与 F200 `(6,92,241,327)` 的有效左面板 crop 已独立逐像素证实相同。`CharacterDialog.cs` 当前HEAD版本为单背景按态切换 F200/F201、展开根宽520，并将图像位置设为`(-6,-92)`/`(-252,-92)`。但 `status-window-render-evidence.json` 记录原版基类背景绘制消费 `this+0x08/+0x0C`，属性/装备绘制与命中测试消费独立的 `this+0x18/+0x1C`；当前 Godot 用单一 `DXWindow.Location` 加背景子控件偏移表达两套坐标。素材 crop 相同只能证明面板像素一致，不能证明背景屏幕原点、内容/命中原点在状态切换和窗口宽度改变后仍相同。反编译点击链还在 `this+0x20 > 0x320` 时额外以 `x+0x118` 调用一次 F201 reframe；当前实现没有第二次背景实例/重定位。研究工件 `layout.json::specialized_window_evidence[*].window_factory.primary_algorithm` 证明 `0x423E80` 将内容矩形写入 `this+0x18`；按 SetRect 四字段布局，`[this+0x20]` 是该内容矩形的 right 坐标。因此阈值 `>0x320` 检查的是工厂生成后的内容矩形边界，不能直接改写为“视口宽度/屏幕右边缘”条件：`0x423E80` 会做居中/父矩形计算，status 初始化调用的原始 x/y 也不是最终屏幕原点。当前仍需用该工厂算法和状态窗具体构造/父坐标链求出F201首个RECT，再判定是否触发第二次 `x+0x118` reframe；不据现有单张稳定态裁图判通过。`GameScene.CreateHud()`初始化时根窗位置为`(0,0)`，`LayoutHud()`未见尺寸改变时重定位。

**同一运行实例鼠标回放（2026-09-24，Xvfb :100、1024×768）：**点击 HUD 状态入口 cap15 后截得收起态，点击状态页/装备页切换按钮后截得展开态，均在输入后约2秒采图。保留的 [`character-collapsed-2026-09-24.png`](evidence/legacy-ei-ui/character-collapsed-2026-09-24.png) 与 [`character-expanded-2026-09-24.png`](evidence/legacy-ei-ui/character-expanded-2026-09-24.png) 是已裁切图，文件分别为244×330和520×330；当前代码逻辑根尺寸是244×328和520×328，保存图高出2px的原因没有记录。旧回放笔记报告根窗屏幕原点为客户端`(0,25)`，但这两张裁切图没有保留完整1024×768客户端画面或crop offset，不能独立证明绝对原点；图像内容只显示稳定态下左侧面板外观被保留、扩展到右侧。不能凭此宣称底边或根RECT吻合。录屏工具当时未提供可用的X11连续采集，未取得点击前后逐帧，因此1像素级瞬态抖动、按下/释放时的变换和其他分辨率缩放仍未验。展开截图可见当前12行单列第一行与底图 `STATUS` 标题区域发生视觉重叠；它是当前实现缺陷候选，不能据此猜原版字段坐标，需按 CHAR-03 的原版双列绘制链修正。

### 装备槽研究记录冲突

`equipment-panel-verification-evidence.json` 的旧模拟器验收把 F325 周边 8 格按旧视觉标签映射，并记录“鞋子/毒药”等身份；它不是槽位协议语义的独立原版证据。较新的 `equipment-slots-evidence.json` 追完 `0x44B720` hit index、`0x44BBD0` 暂存、`0x451690` 与 `0x452940` wire slot byte，并对齐 Server `EquipmentSlot` enum，明确：idx9 Shoes 的窗口相对矩形 `(64,264,38,38)`，idx10 Poison 为 `(103,264,38,38)`；资源画面上的旧标签解释明确标成未验证。故审计以此 primary-static 协议链为准，旧模拟器验证结论不再作为布局 oracle。2026-09-24 已修正 `CharacterDialog.cs` 的生产坐标表和自审计期望表；实际装备/拖放、服务端 wire byte 尚未验证。

### 属性文字尚未闭合

`status-window-render-evidence.json` 的 `attribute_text_draw_chain` 列出第一列 17 项与第二列 11 项原版 GBK 标签（包含等级、HP/MP、经验、背包/装备负重、腕力、准确、敏捷、毒物躲避、中毒恢复、生命/魔法恢复、防御、攻击及火冰电风/治疗/攻击等分类与魔法防御力），并给出精确双列原点、15 px 行距和颜色；其中部分 value 的语义仍特意保留为原始字段候选。进一步核对 `paint_state.primary_disassembly_details` 后发现，state 0 和 state 1 两条分支都调用同一 `0x44BC80` 属性文字链，剪裁表达式也相同；静态证据不支持“F201 切页后整条属性链应被替换/隐藏”的当前逻辑。当前 `BuildLegacyAttributeLabels()` 只造 7 个标签并按 22 px 纵向排布；`BuildLegacyExpandedPanel()` 另造 12 个标签，固定 `(266,18+22*i)`，而 `ToggleLegacyView()` 在展开时隐藏前者、显示后者。字段集合也不对应：当前收起态只显示等级、HP、MP、攻击、魔法、防御、魔御，未显示原版同助手中其余负重/经验/恢复等条目；展开态含“幸运/攻速”，而原版标签清单没有这两项，并遗漏火、冰、电、风、治疗、攻击（黑暗）、召唤（幻影）等原版类别。原版属性文字及装备/命中共用属性绘制基准 `[this+0x18]/[this+0x1C]`：左列偏移 x=`0xFF`、y=`0x43` 起，右列标签 x=`0x17F`、value x=`0x1C3`、y=`0x1E` 起，行距均为15 px；背景绘制另消费 `[this+0x08]/[this+0x0C]`，所以这些偏移不能直接称作 Godot 根窗相对坐标。当前 Godot 只用一个 `Location` 表示根/内容锚点，需先闭合两组字段的屏幕换算，再判断双列几何。这与原版两状态均进入完整属性绘制助手的已知调用事实直接冲突。原版 state 1 还绘制 F201 底图和 11 个装备/物品槽，因此后续应按绘制层和实际剪裁区域确认双列文字在两个底图上的可见结果，不能仅据“共享调用”推断每个字段在两态都无遮挡可见。用户截图中的 12 行状态单列是有价值的 visual/runtime candidate，但不能覆盖 primary-static 中的字段。数值字段到当前 `PlayerStats` 的映射仍未证实；研究文件列出的原版全局地址/读取宽度是可追踪的原始值来源，不足以凭名字直接等同为现代属性。需逐调用回溯这些操作数如何更新及服务端字段来源，再按原版文本基线坐标/行距重建两态布局。

**CHAR-03 字段数据流静态对照（2026-09-24）：**`status-window-render-evidence.json::attribute_text_draw_chain.value_field_sources` 与 `network-message-object-anatomy.json::msgid_0x34` 记录原版完整属性块中的独立来源：LEVEL `[0x7DA108]` byte；HP 当前/上限 `[0x7DA10D]`/`[0x7DA111]` word；MP 当前/上限 `[0x7DA10F]`/`[0x7DA113]` word；经验格式由 `[0x7DA115]` 与 `[0x7DA119]` 两个 dword 计算后格式化；负重标签读 `[0x7DA11D]`/`[0x7DA11F]` word。研究工件称旧协议消息 `0x34` 将97字节 self-stats block 写入这些字段，并注明该研究构建 XP max 没有已检出的写者；这是研究 EXE 内部的 primary-static 结论，目标本机 EI 二进制身份仍未闭合。

当前 Zircon 使用不同数据流：`PlayerObject` 入场分别发送 `S.StatsUpdate{Stats,...}` 与 `S.WeightUpdate{BagWeight,WearWeight,HandWeight}`；Godot 分别维护 `PlayerStats`、重量以及 `_currentHP/_currentMP`。`GainedExperience`、`InformMaxExperience`另行维护当前经验/上限并更新 MainPanel。`CharacterDialog.RefreshLegacyAttributeLabels()` 两态都只从 `PlayerLevel`/`PlayerStats` 读值，HP、MP文字取 Health/Mana 上限而没有使用已维护的当前HP/MP；当前 legacy 属性标签没有经验、负重或原版恢复/元素条目。`CharacterDialog.SetWeight()` 虽接收 wear/hand 重量，但 legacy 状态布局的自定义标签没有消费这些值。

计数按研究 JSON 原样拆开：`first_column_labels` 17条、`second_column_labels` 11条（合计28条标签/格式文本绘制项）；`value_field_sources` 仅列出左列12条、右列11条格式调用记录（合计23条），其中HP、MP、经验、包袱负重等调用各读取多个原始操作数。不能把“30值调用”当成已证实的调用数，也不能把23条已列操作数记录视作所有服务器语义映射均已闭合。当前代码路径进一步确认：入场`StartInformation`初始化`_currentHP/_currentMP`；`OnHealthChanged()`与`OnManaChanged()`持续更新当前值，`OnStatsUpdate()`独立提供Health/Mana上限；经验由`OnGainedExperience()`、`OnInformMaxExperience()`及`OnLevelChanged()`维护；`OnWeightUpdate()`分发bag/wear/hand到`InventoryDialog`和`CharacterDialog.SetWeight()`。这些路线证明数据在客户端可用或被其它HUD消费，但不证明其值与EI原始全局字段在计算、量纲、更新时序上等价。当前可确认的界面差异是人物窗没有消费这些已维护的数据；EI原始字段到当前协议语义的逐项映射仍未证实。

| 原版字段组 | EI primary-static 地址/格式 | Zircon 当前来源与人物窗使用 | 审计结论 |
|---|---|---|---|
| 等级 | `0x7DA108` byte，LEVEL | `GameScene.PlayerLevel`；两态 legacy 标签均读取 | 概念对应；旧协议宽度/来源身份仍受研究 EXE 版本限制 |
| HP / MP | HP `[0x7DA10D]`/`[0x7DA111]`、MP `[0x7DA10F]`/`[0x7DA113]`，均是当前/上限 word 对 | 当前分别由 `_currentHP/_currentMP` 与 `PlayerStats[Health/Mana]` 保存；legacy 标签只读后者 | 当前/上限分离可用；画面漏掉当前值，构成已确认差异 |
| 经验 | `[0x7DA115]`/`[0x7DA119]` dword，原版按比例格式化为 `%.2f%s` | `_playerExperience/_playerMaxExperience` 由玩家信息、`GainedExperience`、`InformMaxExperience` 更新，仅接入 MainPanel | 概念级候选；数值比例、文本格式与原版字段更新仍未核，人物窗未接入 |
| 包袱负重 | `[0x7DA11D]`/`[0x7DA11F]` word 对 | `BagWeight` 与 `PlayerStats[BagWeight]` 分别来自重量与 Stats 通道；`InventoryDialog` 使用，但人物窗 legacy 自定义标签未显示 | 研究 crossref 对同地址在背包窗的服务端重量解释为 derived/candidate；不得直接升为 EI status 字段等价 |
| 腕力、准确、敏捷、毒物躲避及毒/生命/魔法恢复 | `[0x7DA121..0x7DA124]`、`[0x7DA169]`、`[0x7DA16B..0x7DA16F]`，含 byte/word/低8位格式 | `Stat` 有 Strength、Accuracy、Agility、PoisonResistance 等候选，但 legacy 两态没有相应行；恢复字段与旧字段逐项未映射 | 标签/字段语义候选，不是证实映射；当前显示缺项 |
| 防御、攻击、魔法、元素与魔法防御 | `[0x7DA109..0x7DA10B]`、`[0x7DA149..0x7DA165]`；魔法防御助手读取六个 word | `Stat` 有 AC/DC/MC/元素及 MR 相关值；现有7/12行取部分 Min/Max 对 | 名字近似但宽度、基数、格式和数值关系未逐项闭合；不得把当前 Min/Max 行判为同值复原 |

既有展开截图 [`character-expanded-2026-09-24.png`](evidence/legacy-ei-ui/character-expanded-2026-09-24.png) 可看到当前单列状态文字与 `STATUS` 底图标题重叠；这只证明当前工作区实现有可见缺陷，不是原版像素基准，也不能单独决定原版文字的屏幕锚点。截图仍缺完整客户端画面的 crop offset 和匹配版 EI 对照。

**用户提供的 EI 展开态参考截图（视觉候选）：**将原始618×382 PNG只读归档为 [`user-supplied-character-expanded-reference-2026-09-24.png`](evidence/legacy-ei-ui/user-supplied-character-expanded-reference-2026-09-24.png)，SHA-256 `6fba8ead20cd22d79cdac0b9e5c88d04f860cecdf6db23cff034b8c6e0268f9d`。图中展开态同时可见人物栏内的一组简短属性和右侧较长的属性区；右侧包含道术、准确、敏捷、幸运、攻速等行。当前 `ToggleLegacyView()` 会隐藏全部 `_legacyAttributeLabels`，再显示单列12行 `_legacyExpandedLabels`；与图中两处文字同时可见的画面存在可见差异。该截图支持重查 CHAR-03 状态显隐与布局，但它是裁剪后的用户参考图，没有可核对的目标EXE/WIL身份、完整viewport原点或交互过程，故只列 visual-candidate，不能推翻研究工件中 state 0/1 共用 `0x44BC80` 的 primary-static 记录。下一步需把两组画面文字分别对到原版绘制rect/clip与 F200/F201 根坐标，核定重复属性块究竟是同一状态窗的双区，还是截图中的额外显示来源；在裁决前不以自审计常量隐藏其中任一组。

**CHAR-03 静态文字几何对照（window-relative，非屏幕坐标）：**从研究JSON直接计算，EI第一列17项基线为`y+67..y+307`、15px步距；第二列11项为标签`x+383`、值`x+451`、基线`y+30..y+180`、同为15px步距。研究记录称 state 0/1 都调用该属性绘制助手。当前Godot收起组是7个单列标签，首点`(160,20)`、末点`(160,152)`、22px步距；展开组是12个单列标签，首点`(266,18)`、末点`(266,260)`、22px步距，name/value拼在同一标签中。仅局部几何已证明列数、字段数、行距和状态显隐模型不同；因为原版`this+0x18/+0x1C`内容RECT与背景`this+0x08/+0x0C`不是同一基准，尚不把上述局部坐标差直接换算成屏幕像素。

| 编号 | 严重度 | 发现 | 验收/待决 |
|---|---|---|---|
| CHAR-01 | 高，几何已修正，行为未验 | Shoes idx9=`(64,264)`、Poison idx10=`(103,264)` 已按EI原版hit/wire映射修正；独立的原版 primary-static 证据闭合 | 在原版/Godot同数据下逐槽点击和拖动，核验发出的slot byte分别为9/10；测试空槽、有装备和使用毒药路径 |
| CHAR-02 | 高，稳定态左栏锚点本轮实屏复核；点击瞬态仍未验 | viewer API透明PNG独立解码F200 bbox `(6,92,241,327)`、F201 bbox `(252,92,518,327)`；当前`ApplyLegacyEiLayout()/ToggleLegacyView()`背景offset抵消各自alpha bbox、根高固定328，切换函数不写根`Position`。此前隔离测试场的收起/展开态根窗均在屏幕`(10,10)`，公共244×328区差异约0.64%。本轮同一真实游戏窗口中鼠标打开cap15状态窗、点击开关，稳定态根左上均为客户端`(0,25)`，展开宽520；截图见`evidence/legacy-ei-ui/character-collapsed-2026-09-24.png`和`character-expanded-2026-09-24.png`。本轮对两图裁成共同244×330尺寸并像素比较：左上对齐的`145×330`左侧内容区差异像素为0；整段共同区域差异为1033像素，集中在属性文字和切换按钮状态附近。此结果仅证明已裁切图内稳定态左侧内容一致，不提供裁图的屏幕原点。此前 ffmpeg 连续录画尝试失败（当前构建无x11grab输入格式），因此没有排除按下/释放瞬态抖动；第二次F201 reframe的内容矩形阈值触发及画面效果也未验。静态补核：原版 F161/162 close child RECT 为根相对 `(212,298,28,26)`，与当前Godot关闭按钮相同；但原版 `0x423E80` 切页会重设窗口 rect；静态工厂证据显示 `[this+0x20]>0x320` 检查的是写在`this+0x18`内容RECT的right坐标，满足时才再以`x+0x118` reframe F201。当前 `ToggleLegacyView()` 不移动根/关闭按钮，也没有此第二次reframe分支。该比较值属于经过`0x423E80`居中/父矩形算法得到的内容RECT字段，不等同已确认的屏幕坐标或分辨率宽度；现阶段只记录“实现存在缺口、实例触发状态未决”，不推定每种分辨率下都应移动关闭按钮。后续应从状态窗`0x423B30`初建RECT、点击时`0x423E80`调用实参、两次SetRect结果和父窗口坐标来源重算触发条件。另将WIL头offset（F200/F201均`(+7,-44)`）、原始画布alpha bbox与Godot控件Location作为三种不同坐标证据分别追踪，不能把alpha bbox抵消视作原版屏幕锚点证明 | 用支持X11采集的独立逐帧方式在同一实际窗口连续记录切换前后，至少800×600、1024×768和窗口缩放；逐帧跟踪root屏幕Rect、F200/F201有效像素锚、切换按钮hit rect、关闭按钮中心、展开右边缘、Viewport/CanvasTransform与clip rect，覆盖按下/释放/重绘。先闭合 `0x423E80` 的第二次reframe触发条件，再判断该分支的Godot等价行为 |
| CHAR-03 | 高，文字几何和数据字段均确认不符 | 原版 state 0/state 1 共用 `0x44BC80`；左列17条、右列11条标签/格式文本绘制项，相对属性绘制基准 `[this+0x18]/[this+0x1C]` 的双列偏移 `(0xFF,0x43)`/`(0x17F,0x1E)`，value x=`0x1C3`，行距15px。当前分别只画7/12项、22px单列；展开截图可见与 `STATUS` 艺术标题重叠。原版 `value_field_sources` 列出23条格式调用记录（左12、右11），部分调用读取多个原始字段；不是30条已证实值调用。原版 HP/MP各画当前值/上限，当前只画 `PlayerStats` 的 Health/Mana 上限且未使用已维护的 `_currentHP/_currentMP`；经验、负重、恢复/元素项也未映射。数据包与字段细节见下方 CHAR-03 数据流对照 | 逐项核28条标签/格式绘制项及23条已列格式调用的操作数写入源，并标记研究JSON未列出的值调用；再映射 Zircon `StatsUpdate`、HP/MP当前值、`WeightUpdate`、经验数据。核实 F200/F201 两态的原点/剪裁后，按原版双列、颜色和15px基线重建，逐值验证。无证据时标为数据模型缺失，不按相似名称映射 |
| CHAR-04 | 高，三处原版装备命中区当前静态缺失 | 原版11条记录中的大矩形索引0/1/4分别是Weapon `(86,114,60,90)`、Armour `(38,70,53,84)`、Necklace `(94,71,49,33)`；它们不是普通图标格，但原版 `0x44B720` 仍将三者作为 hit record，点击/拖放链最终把索引原样送为 wire slot byte。当前 legacy `ApplyLegacyEiLayout()` 只显示索引2/3/5/6/7/8/9/10八个 `DXItemCell`，索引0/1/4被设为不可见；`PaperDoll.MouseFilter=Ignore` 且没有输入处理器，只画人物/武器/衣服/盾牌/头盔，因此纸娃娃上的三类装备没有等价命中代理。属于可由当前源码+primary-static矩形证明的缺口，不依赖运行期猜测。 | 在迁移实现中为0/1/4保留原版大矩形hit target，并验证空/有物品时点击、拖出/卸下、拖入兼容检查和线上slot byte；命中目标需与纯绘制纸娃娃分层，不能以可见八个小格代替完整11记录表。另核 `0x44B5D9` paint顺序与Godot z-order/鼠标传播，现有截图只能证明静态外观，不能证明交互。 |
| CHAR-05 | 中，入口状态复位行为不一致（静态源码确认） | EI cap15 点击分支先切换 id1，再无条件把状态对象 `[+0x54]` 置0并调用 `0x423E80` 重设 F200/244×328；EI W/Ctrl+W 分支只切换 id1，不重设展开/装备模式。Godot cap15 与 W 都调用 `ToggleCharacterWindow()`；关闭后再开会经 `ShowOwn()` → `ApplyLegacyEiLayout()` 强制回到收起属性态。因此在“展开→W关闭→W重开”路径上，EI 保留模式而当前实现重置为收起态。证据：研究工件 `hud-caption-action-tail-evidence.json::exe_trace.action_table_0x42C494_16.idx15`、`window-paint-and-hotkey-dispatch-evidence.json::hotkey_dispatcher_0x42CC76`；实现 `GameScene.ToggleCharacterWindow()`、`CharacterDialog.ShowOwn()/ApplyLegacyEiLayout()`。 | 与 CHAR-02 的几何/瞬态阻塞分开追踪。先核 cap15 分支在关闭时写入的重定位对再次通过 W 打开是否可见；行为回放只在可靠窗口输入可用时做“展开→W关→W开”与“展开→cap15关→W开”两条路径。无需重放坐骑、交易或其它有风险业务输入。 |
| CHAR-06 | 高，id1/F201开合面板身份已闭合；独立id7更支持状态形象预览，但具体触发与字段待证 | 用户所指“装备栏右边随箭头打开/关闭的属性纸”对应 EI 状态窗 id1 的同对象 F201 展开态，不是独立 id7。primary-static `status-window-render-evidence.json` 记录 id1 对象 `hero+0x29CE4` 初始 F200/244×328；子按钮 click `0x44CCD0` 的展开分支对同一 `this` 调 `0x423E80(F201,x,y,520,328)` 并写 `[this+0x54]=1`。本轮通过 8766 读取 F201 (1024×512, header offset +7/-44)，透明 PNG alpha bbox 为 `(252,92,518,327)`；原始帧预览视觉上确为装备栏左区加右侧 STATUS 属性纸的横向组合，与用户旧版截图结构吻合（visual corroboration）。另一个独立对象 id7 (`hero+0x47C28`, F200, origin x=560) 的身份更支持角色形象/装备属性预览：`window-paint-and-hotkey-dispatch-evidence.json`、`status-window-family-evidence.json`及F353交叉验证均如此标记。旧 `window-id-catalog.json` 把 `0x566DD4`称为消息对象并据此认定message/log，但同一地址由资源表算为global selector element 86、绑定`Data/ProgUse.wil`，且后续状态绘制证据以其选帧画角色形象；故旧message/log解释的关键对象类型不成立，应降为过期候选。id7是否具体服务组队成员预览、`[+0x558..+0x60C]`字段完整语义与调用条件仍未闭合。Godot `CharacterDialog.ToggleLegacyView()`对应id1双态，另无已确认的独立id7实现；CHAR-02/03另审id1布局与数据差异。 | 把随装备栏箭头展开的右侧属性纸归属固定为 id1/F201。id7后续只追可复现的目标版 `0x450530` 原始指令、`0x47C28`窗口触发者与字段写入；不要继续将已解析为ProgUse资源selector的`0x566DD4`写作“消息对象”。目标EXE身份/字节可读前，将“角色状态预览”作为高可信静态解释、具体组队业务保留候选。 |

## 社交/交易/任务窗首轮几何审计

### WIL 像素边界样本

下表的 alpha bbox 来自直接解码 `LegacyEI/Data/GameInter.wil`，是资源画布内的非透明像素范围（右/下边界为开区间）。它只描述素材像素，不自动等于原版窗口根矩形；原版 EXE 的窗口 hit rect、WIL header offset、blitter 裁切和根窗口位置必须分别记录。它可用于发现画面内容被裁掉或锚点偏差，但不能单独推导点击区域。

| 帧 | 画布 | header offset | alpha bbox（x,y,w,h） | 相关用途/备注 |
|---|---:|---:|---:|---|
| F50 | 800×136 | (-24,-16) | (0,0,800,135) | HUD 横条 |
| F51 | 248×46，offset=(-24,-16) | WIL独立解码 alpha bbox=(0,0,248,45) | 六个横向凹槽的视觉候选；不代表其原版对象/命中区域已闭合 |
| F200 | 256×512 | (7,-44) | (6,92,241,327) | 人物装备页 |
| F201 | 1024×512 | (7,-44) | (252,92,518,327) | 人物扩展属性组合画 |
| F250 | 512×512 | (7,-44) | (114,94,281,324) | 背包 |
| F350 | 1024×512 | (7,-44) | (226,62,570,387) | 聊天/好友弹窗；预览透明帧 [GameInter F350](evidence/legacy-ei-ui/gameinter-frame-350-wil-2026-09-24.png) |
| F400 | 512×512 | (7,-44) | (30,67,451,378) | 技能书 |
| F600 | 1024×512 | (7,-44) | (214,33,594,445) | 行会 |
| F601 | 1024×256 | (7,-44) | (220,2,583,252) | id15 同一公告窗替代状态；state 语义由 guild idx7 分支静态确认 |
| F602 | 1024×256 | (7,-44) | (220,2,583,252) | id15 行会公告/编辑窗；F601 是同一对象替代状态 |
| F700 | 512×512 | (7,-44) | (86,36,340,439) | 任务 |
| F750 | 256×512 | (7,-44) | (4,119,248,273) | 设置窗背景；源 alpha 高度比 primary-static 根高多9px，裁切待核 |
| F850 | 512×512 | (7,-44) | (118,94,275,323) | 坐骑 |
| F900 | 256×256 | (7,-44) | (0,6,256,244) | 组队 |
| F1000 | 512×512 | (7,-44) | (106,102,300,307) | 商店/服务状态窗口族；业务状态待逐个绑定 |
| GameInter F1001 | 256×256 | (7,-44) | (28,26,198,204) | 商店对象 state2 网格侧面板；独立仓库窗归属错误，业务状态名仍 candidate（WH-01） |
| F1050 | 512×512 | (7,-44) | (14,91,483,330) | 交易；原版窗口矩形小于/不含完整源画布 |
| F1100 | 512×256 | (7,-44) | (64,59,384,138) | NPC 对话 |
| F1101 | 512×32 | (7,-44) | (64,7,383,18) | NPC 菜单项重复图条（primary-static 绘制循环选择）；仅为源帧预览，非运行态布局证据 |
| F1102 | 512×64 | (7,-44) | (64,10,384,44) | NPC 菜单末项图条候选（primary-static 末项分支选择）；仅为源帧预览，非运行态布局证据 |

这些数值来自临时 `nix-shell` Python/Pillow 解码，没有新增系统依赖。本轮重新从 `LegacyEI/Data/GameInter.wil` 读取 F601/F602 原始 header 并解码 alpha；两帧均为1024×256、offset `(7,-44)`，完整 alpha bbox 均为 `(220,2,583,252)`（`x,y,width,height`；像素范围半开到 `(803,254)`）。F602 的原版根/有效窗口参数是584×252，Godot 旧版适配器也设 `Clip=true`；需继续用 800×600 实屏截图确认超出根尺寸的透明/有效像素裁剪与原版合成一致。后续其余帧也要按原版 blit 参数合成，避免把 alpha 左上角一律当作窗口内容原点。

### 已证实差异

| 编号 | 严重度 | 发现 | 原版证据与 Zircon 现状 | 后续验收/边界 |
|---|---|---|---|---|
| GUILD-01 | 高，根尺寸证据强；最终屏幕原点/命中范围仍待闭合 | 直接 WIL API 确认 F600 原始画布是1024×512、offset=(7,-44)；既有独立 alpha 解码记录其有效像素区为594×445。primary-static `layout.json.window_initialization_evidence.records[id=4]` 记录构造调用 `0x4277E8`、wrapper `0x424E60`、F600、size `[596,446]`；该证据说明值取自初始化调用序列，但共享构造器 `0x423B30` 会依资源头/锚点计算 RECT，原始 x/y `[102,22]` 不能直接当最终屏幕原点。其 `position_semantics` 明确要求区分调用输入与构造后 RECT。相反，`window-id-catalog.json` 把同一注册值写作 `[102,22,446,596]`；但 inventory 与 status 行的 catalog 末两项也分别与初始化证据中的 `[284,324]`、`[244,328]` 对调，故 catalog 数组顺序与初始化 `size` 字段发生系统性反向，不能把它当宽高裁决。RESEARCH_LOG main-init 摘要把行会写为446×596，与初始化记录冲突；更完整的 `layout.json` 初始化记录及 F600 paint 控件落点互相支持596×446：关闭控件位于绘制原点+(556,409)，落在596×446范围内。当前 `GuildDialog.ApplyLegacyEiLayout()` 根为446×596、F600控件在(0,0)按1024×512自然尺寸绘制且未设置`UseOffSet`。更正当前 legacy 测试裁剪链：`LegacyUiSkin.ApplyLegacyTestWindow()`先设根`Clip=true`，`GuildDialog.ApplyLegacyEiLayout()`不重置；`DXControl.Clip`映射`ClipContents`。alpha bbox相对区域`[214,808)×[33,478)`因此被当前446×596根裁为`[214,446)×[33,478)`，右侧362px不绘制。此前“窗口/背景未设Clip、允许超根绘制”错误；应关注当前测试profile把根宽设为446，而primary-static构造器证据支持596×446，Clip使这个宽高冲突直接截去大片背景。当前 legacy profile 的6个现代页签在有行会时全部显示，构造x为`14+76*i`、宽68；最右页签RECT为`(394,39,68,25)`，超过446px根宽16px并受Clip裁切。EI id4是9个paint-time重定位控件+三态列表，而不是这组六页签，此项亦说明当前窗口树把现代内容放入错误根模型。原版绘制原点如何从全画布裁出根窗口有效画面仍未闭合，故这说明当前图像与hit根矩形解耦，不能把F600自然铺画当作已对齐。关闭 hit control 在(418,570)，既与初始化宽高顺序相反，也与 paint 控件位置不符；当前自检只验证自身常量。由此可判定当前纵向根尺寸是错误迁移，596×446是原版根尺寸的强 primary-static 结论；仍未闭合的部分是资源 offset如何参与最终注册/paint origin、最终可见/命中 RECT及裁剪规则，目标EXE缺失使我们不能独立重放这些机器码。 | 以初始化记录的 wrapper 参数/`0x423B30` RECT 字段流为准复核596×446的根范围；追注册点`0x42AB29`写入的RECT边界与F600绘制原点/资源offset；逐个复核F600九控件命中框、三态列表和滚动条。把根外像素、绘制Rect、hit Rect分开核对；恢复目标EXE或取得可运行原版截图后确认最终screen origin和clip。 |
| GUILD-02 | 阻断，原版列表状态机和操作控件未迁移 | primary-static `guild-window-paint-evidence.json`：EI id4/F600 的 `0x425040` 以 `[this+0x98]` 选择三种列表绘制：state0 从 `+0xD4/+0xE4` 读联盟/敌对/公告条目，带文本标记和颜色；state1 从 `+0xA4/+0xB4` 画行会成员；其它状态从 `+0x104/+0x114` 画第三类列表并双重绘制文字。滚动条对象在 `+0x76C`，绘制位置约为 `(x+0x224,y+0xD0)`。`layout.json.control_constructors` 记录九对状态帧：161/162、610/611、612/613、614/615、616/617、618/619、620/621、622/623、624/625，实例分别位于对象 `+0x118` 起、stride `0xB4`；paint 将九控件交给 `0x417830` 每帧重定位，click handler `0x4258F0` 按 `0,1,2,3,4,7,5,8,6` 顺序分派，其中控件4/7联动 id15 公告窗。constructor 记录中多个坐标参数仍是寄存器值，不能单独恢复初始 hit rect；需与 paint-time `SetRect` 结合。当前 `GuildDialog.ApplyLegacyEiLayout()` 只替换底图、根尺寸和关闭按钮位置；`_tab` 0..5 仍是现代创建/首页、成员、仓库、战争、外观、城堡内容，并无 EI `[this+0x98]` 状态切换或九控件映射。`ClientGuildInfo` 仅包含行会/公告/成员/仓库/资金统计等字段，`S.GuildInfo` 包装该对象，`S.GuildUpdate` 发送成员与统计；战争开始/结束事件只含对手名/时长。当前这些模型/包没有 EI state0 所需的公告/联盟/敌对条目集合，也没有其它状态的列表集合；代码搜索只命中“敌对行会”输入标签，没有对应集合控件。可用旧版 `Client/Scenes/Views/GuildDialog.cs` 显示的是较新版本创建/首页/成员/仓库/战争页，只能作 `source-confirmed` 旁证，不是目标 EI 的三态证明。`guild-window-content-verification-evidence.json` 明确对照模拟器单成员列表与原版三类状态不同。EI 三态与 Zircon Guild/网络字段的逐项关系尚未闭合，不能只把现代成员页改标题当作复原。 | 从 paint 的九控件逐项 `SetRect` 与 `0x4258F0` 事件分派恢复每个 hit rect、状态门和动作；将三个 EI 状态分别映射到当前服务器数据来源并独立验证行数/顺序/颜色标记/滚动范围。核对 id15 公告窗两个入口；若某页是 Zircon 扩展，明确标注为扩展并检查它是否覆盖原版控件。 |
| GUILD-03 | 高，控件位置与动作均已静态闭合，迁移对应关系不等价 | `social-window-render-evidence.json` 的 primary-static `paint_repositioned_controls` 给出窗口根相对九个位置；`closed_notes` 又逐分支闭合点击动作和状态门。以控件索引/帧对/位置/原版动作依次为：0 `161/162` `(556,409)` 关闭 hit（消费点击、不改状态）；1 `610/611` `(34,376)` 会员升职（置 state0、scroll=0、聚焦输入框）；2 `612/613` `(34,402)` 成员踢出（置 state1、scroll=0、聚焦输入框）；3 `614/615` `(121,402)` 盟主转让（置 state2、scroll=0）；4 `616/617` `(309,376)` 邀请入会（盟主门控，打开 id15/F602 候选列表）；5 `618/619` `(397,376)` 行会公告画字与 click 分支不一致，分支实际提示输入待删除成员名；6 `620/621` `(484,376)` 退出行会（成员可进入提示/输入流程，盟主点击无动作）；7 `622/623` `(309,402)` 行会解散（盟主门控并打开 id15/F601 名单确认）；8 `624/625` `(397,402)` 画字为关闭窗口，盟主门控分支实际提示输入解除联盟行会名。paint-time 位置是最终可见/命中重定位值；构造时遗留寄存器坐标不用于布局裁决。当前 `GuildDialog` 六页签/纵向升级控件不按这些九个相对坐标布局；虽然存在成员行编辑/踢出、输入名字邀请及公告编辑，仍与 EI 对应控件的状态切换、id15 流程、权限门和特定提示不等价。静态搜索未发现对应的盟主转让/退出/解散旧控件回调；不能据此证明服务器完全不支持这些业务。EI部分动作本身存在“画字与handler不一致”，实现时必须保留已证 click 分支，不可按帧上的字猜行为。 | 按 EI 九控件 paint rect 先重建命中区表，再逐项设计 Zircon 协议/对象映射；当前只能标出相似入口，不能判为等价。关闭控件 click 的消费/窗口可见状态、三种名单状态、id15 F601/F602 和提示输入需分别核对。若不移植的内容属于现代扩展，应在 legacy 模式禁用或移出 EI 主窗。 |
| GUILD-04 | 已完成，低；只更正文档注释 | `GuildDialog.cs` 类级 XML 注释原称“原版 GuildDialog(Interface 260)”，会把现代默认背景误当EI身份；默认构造器用 `LibraryFile.Interface/Index=260`，而 `ApplyLegacyEiLayout()`使用 `LibraryFile.GameInter/Index=600`，研究布局记录也把EI id4标为GameInter F600。已将注释改为明确区分现代背景与EI legacy profile；没有改变运行行为。 | 注释核对完成；无需运行期测试。GUILD-01的窗口尺寸、锚点、裁剪与控件行为差异仍独立未验收。 |
| CHAT-01 | 已通过当前 Zircon 运行验收；EI原版像素身份仍阻塞 | `chat-window-render-evidence.json` 的原版关闭控件为根相对 `(532,350,28,26)`；当前 `LegacyChatDialog` 使用同一相对 RECT，F350 关闭、重开、输入焦点和 HUD 入口均已在 1024×768 实机截图/日志中确认。 | 本地 `GameInter.wil` F350/F380 资源事实已独立读取；目标 EI WIL/WIX 字节身份不可达，不能宣称像素同版。 |
| CHAT-02 | 已通过当前 Zircon 运行验收；EI原版资源身份与像素逐点 parity 仍阻塞 | 原版 primary-static `chat-window-unified-model.json` / `chat-window-render-evidence.json`：EI id8 独立F350根572×388；历史clip `(40,29,491,279)`、19行×14px；输入RECT `(25,311,499,15)`；9个可见控件：关闭、六个输入命令按钮、上下滚动；滚动位置依`+0x68`/计数`+0x6D0`。当前 `LegacyChatDialog` 独立承载F350、根相对RECT、固定14px历史行、输入区、六个本地模板按钮、轨道滚动、滚轮、上下命中区、边界和新消息锚点；`GameScene.ReceiveChat/OnChat`同一接收链同时写入HUD与F350；裸R、Enter、Space、HUD MailButton与`--legacy-open=chat`均已真实打开/聚焦。当前本地 WIL 的F381/F382/F383为空，故不上绘错误精灵，只保留证据约束的19×14命中区并记录缺失日志。 | 当前运行截图与stdout见 `chat-runtime-acceptance-2026-09-24.json`；本地运行资源闭合行为，不替代 EI 原版 WIL/WIX byte identity 与按钮像素 parity。 |
| CHAT-03 | 高（研究构建 primary-static 与 Zircon source-confirmed 不同；目标 EI 版本身份待闭合） | 研究工件 `chat-input-command-dispatch-evidence.json` 直接记录 `0x41ED20` 对 `+` 前缀走独立 `0x41E740` trade/counter 分支：按`/`拆字段、`atoi`解析并检查时间门 `[0x428214]`；普通文本则走 `0x452920` 的消息号解析与跳表分派。当前 Godot `ChatTextBox.SubmitChat()` 对普通输入统一调用 `GameScene.SendChat()`，其把文本发为 `C.Chat`；`SConnection.Process(C.Chat)` 进入 `PlayerObject.Chat()`。该方法显式处理 `/`、`!!`、`!~`、`!@`、`!`、`@!`、`@`、`#` 等前缀，未处理 `+`，因此 `+...` 会落入普通聊天广播分支。可用 `Client/Scenes/Views/ChatTextBox.cs` 也在 Enter 时统一发送 `C.Chat`，但它不是已确认与目标 EI 3.0 同版的源码。研究 EXE 为524,288 bytes，与本机 `Mir3.exe` 身份/指令布局不同（见开头版本限制），故该结论目前只能表示研究二进制有该专用行为，不能无条件断言本机目标EI必同。不得将此 trade/counter 指令与 HUD cap0 对玩家发起 `0x401` 交易请求混为一谈。 | 取得并校验研究工件同版目标 EXE 后，复核 `0x41ED20/0x41E740` 的完整参数、状态门和最终动作；逐项追 Zircon `C.Chat` 服务端解析及是否存在其它 `+` 分支。确认版本对应后再在隔离、可观察环境比较目标 EI 与 legacy 对 `+...` 的消费/广播/交易结果；本轮不输入该前缀，不触发可能有业务副作用的 chat 命令。 |
| CHAT-04 | 六个模板与历史/滚动行为已在当前 Zircon 运行验收；原版命令状态语义仍按 primary-static 保留边界 | primary-static `chat-window-render-evidence.json::original_command_strings/channel_command_state/hit_dispatch`：id8/F350 六个子控件根相对 x=25/65/105/145/185/225、y=332，大小均36×34；帧对依序 F360/361 `@拒绝 `、F362/363 `!`、F364/365 `!!`、F366/367 `!~`、F368/369 `@拒绝私聊`、F370/371 `@拒绝行会聊天`。当前 `LegacyChatDialog` 按该顺序写入本地输入模板，视觉/点击/焦点截图已完成；这些模板未提交服务器，避免拒绝/喊话副作用。当前本地 F381/F382/F383为空，滚动按钮保持透明19×14命中区；F380仅作16×502锁链轨道。 | EI命令状态分支、按钮精灵的目标版本身份和像素仍以 primary-static 为准，不能由本地同号空帧或现代七态ChatMode推断。 |
| TRADE-01 | 高 | 交易窗把原版位于根矩形外的隐形交互区改造成窗内可见控件，整体输入/绘制模型不同 | `trade-window-render-evidence.json` 原版根矩形 484×330、F1050；close hit rect (532,350)，accept (185,332)，cancel (225,332)，都超出根矩形部分；原版交易 paint 不遍历普通按钮绘制器，按钮是背景烘焙美术上的隐形 hit zones。当前 `TradeDialog.ApplyLegacyEiLayout()` 把关闭按钮移到 (456,304)，接受按钮/文字仍由现代 DXButton/标签承担；实际`--legacy-ui` wrapper还对484×330根窗设`Clip=true`。原版 close/accept/cancel的hit RECT含根外部分，Godot复原其根外图像时会被父Clip裁掉；但`ClipContents`是绘制裁剪证据，不足以推出根外Control一定不接收输入，输入命中仍须分别追Godot事件路径。当前两侧网格均固定5×6且padding=1；逐格几何差异见TRADE-02。 | 按原版完整根/外溢画布与点击分派重建映射；确认关闭 hit 本身只播放声音、不关窗，确认按钮消息 0x406、完成态禁点、金币矩形以及分栏拖动。Godot legacy根`Clip=true`已由wrapper静态确认，原版根外按钮是否被图像裁切、其hit测试是否超出根矩形分别核对；不得把绘制clip推断为输入门控。 |
| TRADE-02 | 高，原版完整分栏滚动未迁移 | 原版交易两侧各有独立的 6 行视口与分栏滚动状态；Godot legacy 只建固定 5×6 网格，没有两侧滚动/分隔拖柄，也没有原版外溢点击区布局 | `trade-window-render-evidence.json` primary-static：F1050 根窗 484×330；paint 建两个 pane 区，item hit 以 36px 步距映射，双方各有独立 split 字段 `+0x54/+0x58`，每侧6行可视，绘制/命中均按 split 行偏移；两个 1070 gauge 对应分栏拖动状态。其拆分字段消费者、写入链在 `trade-split-handle-evidence.json` 已闭合为行偏移，并以 `trunc(gauge_pos×94)` 更新；94 是归一化刻度而非行数。当前 `TradeDialog.ApplyLegacyEiLayout()` 把双方 `DXItemGrid` 固定成 5×6、`VisibleHeight` 默认6，没有创建分栏 gauge/滚动控件；因而只对应 split=0 的首屏。当前两格网格虽改为5×6，但逐格复算后已能量化差异：原版0x416830每侧首格命中起点为根相对左(21,48)、右(253,48)，36×36格、列/行步距36；Godot `DXItemGrid` 因 `CellWidth=36`、`Step=CellWidth-1+2×GridPadding` 且 `GridPadding=1`，步距为37，当前首格左(15,92)、右(247,92)，第5列分别x163/395，第6行y277。对应原版第5列x165/397，第6行y228；所以左/右首格偏(-6,+44)，末列偏(-2,+44)，末行因步距累计偏(+44/+49)。原版F1050源alpha bbox `(14,91,483,330)` 加WIL offset `(7,-44)` 后是 `(21,47,483,330)`，恰与primary-static左pane命中首角仅差1px；这强烈提示原版blit有效像素采用帧offset，但 `0x460240` 此调用的offset参数尚未直接核出，故保留为几何推论。当前Godot `TradeDialog` 在(0,0)绘制F1050，`UseOffSet`默认为false，格子也直接使用未经offset的内部坐标；legacy wrapper另设根`Clip=true`，因此当前帧alpha可见范围被裁为根相对x[14,484)、y[91,330)。如果EI helper应用offset，其理论可见交集将为x[21,484)、y[47,330)，而非当前位置。偏移数字依赖同一根原点假设，目标EXE身份及blit参数未闭合，作为已证实现差异记录、原版offset消费记“强推论待确认”。 | 用原版坐标公式重建两侧 split=0、中段、最大可用行的格子命中与图标落点表，再与 Godot 每格实际 GlobalRect 独立比较；拖动两侧分隔条、滚轮/步进、跨页物品增加/取回，并验证双方数据槽号稳定。原版动态 item-id 数组的网络填充者仍是 KEPT-runtime，需用运行时包/行为证据闭合。 |
| TRADE-03 | 高，确认/取消/关闭行为不同 | Godot 把关闭叉绑定为关闭交易、确认按钮放在窗内且只实现发送；EI 三个区域是根窗外 hit zone，其中 close 与 cancel 点击只播放声音、不关闭窗，accept 发 msg 0x406 并置完成态 | `trade-window-render-evidence.json` primary-static：构造器的 close F161/F162 hit `(x+532,y+350)`、accept F1061/F1062 `(x+185,y+332)`、cancel F1064/F1065 `(x+225,y+332)`；交易 paint 不调用按钮渲染函数，F1050 烘焙图包含可见按钮美术，cancel 帧不存在但命中区域仍有效。`trade-window-click-binding-evidence.json` 以及 `trade-window-closure-evidence.json` 确认 close/cancel 只播放 0x69 音效并消费点击，accept 发 0x406、设置 `+0x13644=1`，之后忽略点击；金币框 `(34,270)-(156,304)` 打开 0x405 金额窗。Godot `TradeDialog` 的 close 回调直接 `CloseTrade()`；`_confirm` 是 `(126,203,80,25)` 的文字 `DXButton`，无独立取消 hit zone；金币文字点击开 `ItemAmountDialog`。Godot 当前完结/服务器回包语义需和 EI 确认状态字段逐包映射。 | 在真实交易双方中分别点击 close、accept、cancel、金币框；记录窗口是否关闭、交易状态/包号/回包后锁定，再与 EI 原版分派结果逐项比较。保留消息字段等价性待运行时闭合。 |
| TRADE-04 | 高，服务端可成功的第11–15条对方物品在Godot中无镜像槽（源码链闭合；双人回包仍待验） | `TradeDialog.ApplyLegacyEiLayout()`把双方`DXItemGrid`各设为5×6（30格）；但对方物品快照数组`_playerItems`仅10项，`SetOtherItem()`每次收`S.TradeItemAdded`后只填第一个空项，数组满后静默丢弃后续事件。当前服务端`PlayerObject.TradeAddItem()`在已有15条`TradeItems`后拒绝新条目；合法条目会先返回Success并给交易对方发送`S.TradeItemAdded`。Godot对方镜像数组仅10项且接收事件只走`SetOtherItem()`，因此服务端成功接受第11–15条记录时，本地UI数组满后直接丢弃显示数据。该成功发送链、单一接收订阅与满数组返回均已跨客户端/服务端源码复核。反向本地网格虽有30格，但服务端最多接受15个不同物品记录；超额请求会以`Success=false`回包清除展示项。EI研究证据记24个交易slot records，但其左右分配/有效物品上限未从该对象布局单独闭合，不用它推断目标EI的容量。 | 独立修正当前协议视图数据容量与有效格位上限后，再用隔离双人场分别送入10、11、15、16件验证回包和绘制；完整双人往返仍按阻塞表跳过，当前结论只标静态源码确认，不标运行验收。

### 交易窗格网格几何与研究记录冲突校准

**TRADE-02 静态几何复核（2026-09-24）：**主证据 `trade-window-render-evidence.json` 的 0x416830 hit scan 记录每侧 36px cell pitch、5列、6行可视；后续 `trade-window-closure-evidence.json`（Finding 284）把索引公式闭合为 `cell=col+5*(split+row)`，`trade-split-handle-evidence.json`（Finding 301）再确认 split 是行偏移、每屏6行、最多40行源列表。Mir3-Research `RESEARCH_LOG.md` 较早 F283 留有“8列×9行”的过期结论，与上述闭合公式以及后续 5×6 结论冲突；本审计以 Finding 284/301 及 render JSON 为准，并明确把 F283 的 8×9 记载标为 superseded，不从该条旧记录复制布局。

当前 Zircon `TradeDialog.ApplyLegacyEiLayout()` 的 5×6格数与目标每侧可视数量吻合，但 `DXItemGrid` 实际 cell pitch 为 `CellWidth-1+2*GridPadding`；当前 `GridPadding=1`，所以 pitch=37，而 EI hit/draw scan stride 是36。当前两侧网格控件分别在 `(14,91)`、`(246,91)`，每格由 `DXItemGrid.CreateGrid()` 放在控件局部 `(1+37*c,1+37*r)`，因此相对控件首格，当前最右列额外漂移4px、最底行额外漂移5px。首格与 EI 屏幕锚点是否一致尚不能裁定：Godot `DXImageControl` 默认 `UseOffSet=false`，而原版 frame 1050 由基类 `0x423D00→0x460240` 绘制；尚需从原版 blit helper确认WIL offset是否被消费，再将图像有效像素锚点、目标 hit RECT 和当前 GlobalRect置于同一坐标系。由此可判格数匹配、间距不匹配；绝对首格像素锚点仍待证。按用户要求，本轮不运行交易或双人业务输入。
| NPC-01 | 高，已证实 | 旧版关闭与滚动箭头控件被放到错误位置/用了错误帧；关闭 X 随动态窗口底边重定位 | primary-static `npc-window-render-evidence.json`：close F161/162 命中框 `(7,141,28,26)` 是 ctor 初值；paint `0x440A43–0x440A8B` 后改为 `(window.x+0x15B, window.bottom-0x24)`，552×176 根窗对应相对 `(347,140)`。当前 `NPCDialog.ApplyLegacyEiLayout()` 固定 `(516,146)`，偏到右下角。EI 上/下箭头分别是 GameInter F52/53 `(290,145,12,8)`、F54/55 `(306,136,12,8)`；当前 `_scroll` 在 `(530,28)`、尺寸18×112；它把上/下按钮帧设为 F387/F385，本机viewer `/api/info` 对两帧均返回 `blank=true`。`DXImageControl.Index` 在未设`FixedSize`时以 `MirSkin.GetSize()`重设Control尺寸；`LegacyWilLibrary.GetSize()`对无帧头返回`Vector2I.Zero`，所以两箭头的按钮矩形为0×0。PositionBar又被设 `LibraryFile.None/Index=-1`，同样是0×0；当前 scrollbar 对 legacy 只留透明、无边框背景，不能拖 thumb。`DXVScrollBar`仅把滚轮委托接在这三个子按钮上，`NPCTextControl`虽会消费滚轮但没有连接到`_scroll.Value`，因此静态源码可证 legacy NPC 正文没有有效箭头、thumb或滚轮滚动路径。EI本机帧52/53和54/55均有12×8帧头，PNG可见alpha内容9×8（右侧3px透明），分别为上/下箭头态候选；本轮归档5×PNG：[`F52`](evidence/legacy-ei-ui/npc-scroll-arrow-f52-2026-09-24-5x.png)、[`F53`](evidence/legacy-ei-ui/npc-scroll-arrow-f53-2026-09-24-5x.png)、[`F54`](evidence/legacy-ei-ui/npc-scroll-arrow-f54-2026-09-24-5x.png)、[`F55`](evidence/legacy-ei-ui/npc-scroll-arrow-f55-2026-09-24-5x.png)。原版静态输入链给箭头相对绘制/hit候选 `(290,145)` / `(306,136)`，且仅在 `[this+0x58C]==1`（解析器标记 overflow）时响应；每次将逻辑滚动索引 `[this+0x3BC]` 减一/加一并调用 `0x440C30` 清理节点链中每条菜单记录的五个子RECT，再重建可见命中区域。原版文字paint使用节点链窗口`[0x3BC, 0x3BC+0x594]`；parser最多绘16条动态菜单项。研究JSON把`+0x594`标为line-spacing、扫描器写入14或21，但paint又将它加到菜单索引作为节点链窗口上界；同一JSON另记字形pitch=`textheight+5`。因此14/21究竟是像素行距还是可见记录数/其它窗口量，现有摘要自相矛盾，不能称作已闭合的14/21px文本行距。当前Godot的`_scroll.ValueChanged`却直接将`NPCTextControl.Position.Y`设为`-Value`、每次`Change=1`，属于像素偏移而不是已证的菜单记录索引；不能只把箭头回调接到现有滚动条就宣称行为等价。EI通用拖拽入口是`0x417E60(this+0x3C4)`，当前thumb资源/矩形不存在。帧52/53、54/55对应的普通/hover身份仍以研究记录为准。 | 按 800×600 与不同正文高度重新核 F1100 原点，动态套用 close `(x+347,y+h-36)`；还原两组 12×8 命中区和各自状态帧，再追 scroll thumb 的构造/拖拽矩形及可滚范围；运行时验证关闭消息、滚动状态和边界。 |
| NPC-02 | 阻断 | EI 模型窗 ID9 与独立帧窗 ID11 各有绘制/输入链；ID11列表是否承载该 NPC 页菜单仍未证。当前Godot将正文、候选菜单行和部分业务入口合在一个NPCDialog| primary-static `npc-window-render-evidence.json`：F1100 为552×176空心框；paint 对动态条循环选 F1101、末项选 F1102，`this+0x51C` 数量默认13、上限16，图条 y 按18 px步进。解析器 `this+0x594` 常规21、mode=1且overflow=1时14；实际白色GBK文本由独立 `0x43F460` 从根相对 `(150,40)` 绘制，行距 `textheight+5`。当前 `NPCDialog.ApplyLegacyEiLayout()` 把文本区放在 `(20,28)`；`NPCTextControl.SetContent()` 的首字本地 x=0，因此当前文字起点是根相对 x=20，比原版左130 px，本机 EI WIL 经 `wilsdk.py` 直接解码的 F1100 alpha bbox 为原点 `(64,59)`、尺寸 `384×138`，当前文字起点在有效像素左缘之外44 px。Godot `ShowPage()` 还用0–6条F381、20 px间距行背景与固定18 px line-height，没有复现F1101/F1102动态条或原版按字体高度计算的行距。alpha bbox F1100 `(64,59,384,138)` 不等于根原点。F347 npc-dialog-family-evidence.json primary-static另闭合NPC模型窗自己的输入：ID9 hit handler 0x440290命中child +0x58/+0x1C0/+0x10C；内部编辑/菜单处理器0x43E4B0读取edit buffer、按换行拆分并调用0x4524A0/0x4524D0发送消息0x410/0x411，返回consumed后由case9执行hide-all关闭ID9。它没有0x419发送。另有primary-static确认帧窗ID11（+0x516E8，paint 0x447470、handler 0x447FA0）自带最多19项列表，但它与NPC模型窗是两个ID/对象，列表数据来自消息0x515；F321将0x451A40/0x419的唯一E8 caller归到0x448148（任务帧handler），不能把ID11列表直接命名为NPC菜单。故撤回“frame id11 option list == NPC menu”隐含映射及0x419-NPC归因；NPC脚本列表、0x410/411输入与0x515帧列表之间的业务关联仍待逐项闭合。Godot选项走 `SendNPCButton()`；与EI 0x515/帧窗关系未证等价。 | 以 `npc-window-render-evidence.json`、`ui-coverage-matrix.json` Finding 321、`UI_COVERAGE_MATRIX.md`互校；追 `0x447470` frame窗命中回调、按键和真实协议调用者，核F1101/F1102文字关联；按普通、溢出、无选项、多级菜单对比消息与独立截图。 |
| NPC-03 | 中；源码证据，EI版本对应关系未证 | 可用 `Client/` 源码能解释当前 Zircon NPC协议，但不能当作EI 3.0证据；Godot端也改变了来源代码的对象/点击细节 | source-confirmed（`Client/Scenes/Views/NPCDialog.cs`、`Client/Scenes/GameScene.cs`、`ServerLibrary/Models/PlayerObject.cs`）：Client创建NPC主窗、NPCGoodsBox、NPCQuestListBox、NPCQuestBox为独立对象；内嵌按钮ID0本地关闭，非零按钮按每个按钮文字组各自的一秒门限再发送 `C.NPCButton`；按钮文字拆成多个 `DXLabel` 时共用该组门限。服务器按当前NPC/page校验按钮目的页，回包Index映射 `NPCPage`。Godot把 `_goods` 放在NPCDialog子树；内嵌 `[文字:id]` 由 `NPCTextControl` 按逐字形18px高区域命中，并在左键`Pressed`当帧直接发包；Page.Buttons后备入口则由 `DXButton.MouseClick` 发同一 `C.NPCButton`，两条路径均无旧版一秒节流。可用旧 Client 的 `NPCDialog.ProcessText()`给拆分文字label绑定`MouseClick`，其事件经过 `DXScene.OnMouseClick()`/`DXControl.OnMouseClick()`，与Godot内嵌文本的按下即发送并非同一事件时机；旧 Client 还显式连接 `PageText.MouseWheel += ScrollBar.DoMouseWheel`，Godot未接该事件（NPC-01已记录结果）。ID0在两版都本地关窗，Godot集中走`Close()`/`SendNPCClose()`。这些只证明当前 Client 到Godot迁移路径存在差异；可用 Client 的版本与目标EI 3.0关系未知，不把差异推广为EI原版按下/抬起时序，也不能用于支持0x419语义。 | 找到与目标EI匹配的Client版本或原始运行包后再判定独立商品窗与1秒节流是否应复现；运行时将0号关闭、非零page跳转、goods/quest sibling层级逐项对照。 |
| NPC-04 | 中，Godot legacy根裁切已源码确认；EI F1100 裁切仍未决 | 原版窗口根 552×176；本机 EI F1100 资源画布512×256，既有独立alpha边界记录为 `(64,59,384,138)`。当前 `NPCDialog.ApplyLegacyEiLayout()` 将完整帧画布放在根相对 `(0,0)`，并将根设为552×176；透明贴图的有效纵向范围因此为根相对 y=59..197，超过根底边21px。原版绘制路径并非简单地把素材放在根 `(0,0)`：primary-static `0x43EE00` 先推导 `this+0x520/+0x524` 背景目标RECT，`0x43F06A` 再把 F1100 画进该RECT；研究记录中的384/138等常量是资源尺寸派生输入，不是最终屏幕坐标。因此21px只量化当前Godot根与当前原点的几何关系，不能直接说原版也外溢或应采用同样的裁剪。更正当前运行路径的裁剪结论：`bash login_game.sh legacy`通过`--legacy-ui` wrapper先设NPC根`Clip=true`，`NPCDialog.ApplyLegacyEiLayout()`不重置；`DXControl.Clip`映射`ClipContents`。在当前背景原点(0,0)下，F1100有效区域 y=`[59,197)` 被552×176根裁为`[59,176)`，底部21px由Godot确定裁掉。故旧称源码允许该子图越过根窗不适用于实际 legacy 测试路径。EI背景目标RECT/裁切链仍不同且未闭合；裁切是否与EI一致不能由当前Godot无截图的状态确定。F1100 alpha框与根窗RECT不能合并成同一尺寸。 | 取得同版本 EI 与 Zircon 的 NPC 同状态原始分辨率截图，对齐根框、F1100末21px、相邻世界画面和关闭控件；先确认原版合成/裁切规则，再决定是否设置根裁剪或调整源纹理绘制矩形。不要用资源alpha bbox直接改根窗尺寸。 |
| NPC-05 | 阻断，F1100构造/输入证据与dialogue-open消息活性尚未对上；`+0x594`单位摘要矛盾 | `npc-window-render-evidence.json::window_object_model.dialogue_open`记录open_index=16、handler `0x41FE31`；同一工件记录该外层分发表读取处只接受索引0..8（`cmp eax,8; ja ...`），index16因此不从该消息表路径可达。工件另称未发现该handler的表外静态引用。此结果只阻断“该消息表入口能打开NPC窗”的主张，不证明已初始化的ID9/F1100 paint/input函数整体不可用；另一个活调用路径尚未定位。Zircon `NPCDialog.ShowPage(NPCResponse)` 的服务器事件/协议不因此自动等同 EI 这条入口。NPC研究工件`npc-dialog-family-evidence.json::notes`另称正文“来自服务器消息(0x7ED-0x7F0 family, F249)”，但这与已闭合的`input-message-bus-0x7ed-0x7f0.json`矛盾：0x7ED–0x7F0是发往主窗口的本地私有消息范围，sender matrix列出的0x7ED发送点仅在聊天编辑路径且byte3恒0；F249实际分析的是通知窗id15鼠标点击负载，并非NPC服务器正文。故撤回把这组消息当NPC载荷来源的说法。方向性复核：RESEARCH_LOG Finding 230把`0x41D744→0x451740`的msg `0x3F2`记为客户端向主消息对象的轮询发送，`0x41B94F→0x4521B0`的msg `0x3F3`为客户端把已保存正文发出；`0x43E4B0`会在用户编辑/提交时发`0x410/0x411`。这些都是客户端发送链，不能拿来补成server→client正文/开窗。另，服务端msg `0x515`填充的是frame id11的独立19行链表，不能仅按“选项”字样接到模型id9。综合目前已证方向：私有0x7ED–0x7F0≠NPC入站正文，0x3F2/0x3F3/0x410/0x411为客户端出站，0x515→id11，而疑似msg0x274→id9打开链不可达；真实NPC服务端回包类型/活开窗调用者仍未闭合。同一NPC研究JSON将`+0x594`命名为line-spacing并记录14/21，又记录paint以`+0x3BC .. +0x3BC + +0x594`作节点链窗口上界、字形pitch为`textheight+5`；字段单位/消费语义未由摘要闭合。 | 回到与工件匹配的EXE原始字节，独立核 `0x41F582`边界、handler表槽与所有构造后active/show调用者；取得目标原始EXE/同版调用图后，追查`0x264..0x26C`和子协议分派中的NPC正文入站生产者、构造后id9的全部active/show调用者；核`0x7ED–0x7F0`私有窗口消息、0x3F2/0x3F3/0x410/0x411出站协议与0x515→id11填充链的边界；重建`+0x594`写入和消费指令，确认它是像素量还是列表窗口量；再将一个已闭合的活EI开窗路径与当前`NPCResponse→ShowPage`分开映射。完成前NPC-01/02可保留静态绘制/输入函数证据，但不得把服务器开窗及完整NPC业务链标为parity通过。
| NPC-06 | 高，当前 legacy 资源路由确定缺失；EI目标子窗身份/素材尚未闭合 | 当前 Godot `NPCQuestListDialog`/`NPCQuestDialog`复用了可用旧Client的 `LibraryFile.Interface`、索引209/212；但 `LibraryCore/Libraries.cs` 将 Interface 映射到 `Data\Interface.Zl`，`MirSkin` 在 legacy 模式只回退到同 basename `Interface.wil`。`/home/tetsuya/mir3ei/LegacyEI/Data`只含`Interface1c.wil/.wix`，不含`Interface.Zl`或`Interface.wil`；故这两个 `LibraryFile.Interface` 背景的 legacy WIL读取路径没有图库。两类 Godot 构造器均令根 `Size=background.Size`；源码路径下缺库时尺寸为零、背景无贴图。List仍创建至少210×134列表，详情控件从y=40延伸至约317；`OpenNPCQuestList()`再以零根高/宽定位两个子窗，列表与详情会被放在相同NPC锚点附近。Interface1c 是另一个资源库：本机 EI WIL count=2000，F209头为36×104且图像为人物形态片，F212无帧头；不能仅因数字相同拿它替代Interface209/212。可用 `Client/Scenes/Views/NPCDialog.cs` 的索引209/212只属于源码确认，不是目标EI证据。 | 静态可确认当前 legacy 资源路径、根尺寸与定位存在缺口；不臆造替代帧。追同版EI任务窗资源库/动态ID11及按钮/输入链；取得匹配的Interface资源或原版截图后，决定复用帧/绘制结构，再独立核背景像素、根RECT、子窗相对锚点与裁切。任务接受/领奖运行输入暂不做。 |
| NPC-07 | 高，当前 GameInter 帧引用越界；EI是否有对应独立镶嵌UI仍未证 | 实际 legacy 资源路由先加载`/home/tetsuya/mir3ei/LegacyEI/Data/GameInter.Zl`；独立 `zlsdk.py`解析其ZL2图集count=1103，WIL/WIX由`wilsdk.py`解析也为1103。`NPCSocketDialog`背景引用`GameInter` F5700并有F5800–5849循环动画，`NPCSocketCombineDialog`背景引用F5701并请求F5710–5730、F5740–5760、F5770–5779动画；这些帧号对ZL与WIL两条资源都超出范围，ZL `header()`/WIL `header()`均无记录。用户指定8766 API对GameInter F5700/F5701/F5740也回`blank=true`，与独立解析越界结果相符。相同数字在 `NPC.wil` 属另一资源命名空间：NPC WIL count=6400，F5700/F5701为192×128角色动画帧；`npc-body-strip-evidence.json`说明该条带是NPC body 0x39动画，不是窗口背景。可用旧Client构造器确实把索引5700/5701及5740动画写在GameInter库，但其资源版本与EI目标未证；当前Godot XML注释“原版独立 NPCSocketBox”不能单独证明目标EI有此窗口。当前固定Size仍让子控件存在，但面板/粒子背景动画纹理缺失。 | 标作当前legacy资源确定不匹配与EI归属候选；不要把NPC.wil同号帧搬到GameInter，也不要运行镶嵌/合成请求。追目标EI同版消息、窗口对象与素材库；若EI无独立节点，将该功能明确隔离为扩展；若有，按证据映射可用EI资源后再修。 |
| SHOP-01 | 阻断 | EI NPC 商店对象与当前 NPC 商品面板的根窗口/尺寸/资源模型不同；当前 cash shop 名称容易造成审计对象混淆 | primary-static `store-window-render-evidence.json` / `UI_COVERAGE_MATRIX.md`：EI 商店 state0（购买五行，msg `0x285`）使用 GameInter F1000，屏幕起点 `(0,184)`，内容矩形 `(0,186,300,304)`；状态2 F1001 是同一 store 对象的仓储/扩展网格面板（语义仍 candidate，见 WH-01），并非独立根窗。当前 `NPCGoodsPanel` 不绘制 F1000，而使用 `LegacyWindowFrame`、245 px宽面板、商品行固定43 px；`NPCDialog.ShowPage()`先以正文高度算现代动态根高`footerY+64`（最小204px），再把goods/repair/advanced子面板锚到当时的`(0,Size.Y)`；legacy路径随后调用`ApplyLegacyEiLayout()`把父根重设为552×176，但不重定位这些子面板。`--legacy-ui`此前已通过`ApplyLegacyTestWindow()`将NPC根设`Clip=true`，后续路径不清除此值；因此子面板起点至少y=204，高于父根底边176至少28px，并被`ClipContents`整块裁掉。上述高度是当前控件树尺寸，不是当前可见面板尺寸；如要复现EI store对象，需重建独立同层子窗或按原版绘制层级放置，不能只调整这棵子树的尺寸。`GameStoreDialog` 则是游戏现金商城，当前存在收藏/排序/充值/礼包等现代业务，不是 EI 的 NPC store 对象。原版 F1000根内容矩形与当前商品面板锚点/尺寸没有一致性证据。 | 先解决已由源码确认的裁剪：legacy测试树中NPC根`ClipContents`会把定位在现代动态根高（最小y=204）的goods/repair/advanced子面板完全裁出，应在恢复旧版对象关系时将store层放到正确的独立渲染层级，不按父窗裁剪继续调子面板尺寸。再逐状态恢复EI store的对象创建、屏幕坐标、F1000/F1001/F1002/F1003与商品记录/命中区；把NPC商品/出售/修理/存取协议映射到正确旧版store状态和库存交互。区分 `GameStoreDialog` 现金商城扩展入口；以购买、双击购买、出售、仓储、合成/详情及空列表状态实屏验收。 |
| SET-01 | 高，设置字段语义映射与持久化尚未完成同版运行验收 | 已修正八个控件被当成八项独立选项的问题；BGM/EffectSound 接入 legacy 音频开关；ShadowBlend 保留为原版有配置写入、但本机构建无已知外部消费者的状态。Ambience 与 ShadowBlend 不能统称为“四个普通可写开关”：原版 Ambience handler 只切换两侧控件帧并调用共享配置保存函数，不更新 `this+0x5C`，保存时把加载的旧值原样写回；当前 Godot 的 Ambience 按钮只改窗口局部 `_legacyAmbienceVisualState`，不改 `ClientSettings.LegacyAmbienceEnabled`，也不触发 `Save()`。因此两者可见态都是窗口内暂时切换，但配置保存副作用不同，具体记 SET-07。尚未证明其它 Godot 字段与正在运行的目标 EI 配置逐值相同 | `system-window-render-evidence.json` 和 `settings-ambience-bgm-volume-evidence.json` 记录 `Config.ini [Options]` 键及载入/保存链；后者的 `dispatch_table_0x44194C`、`config_save_0x441B30` 与 `verdict` 明确记录 Ambience ON/OFF 分支调用 save、只换帧、不写 `+0x5C`，save 将原值回写。当前 `ConfigDialog.CreateLegacyOptionButtons()` 的 Ambience setter 仅赋 `_legacyAmbienceVisualState`；`ClientSettings.Save()` 会写 `[LegacyEI]` 持久字段，但该路径不调用。原版目标 EXE 身份限制见本文开头。 | 同版目标 EXE/Config.ini 恢复后逐键确认默认值、载入/保存、两侧 RECT 触发及重启恢复；分开验 BGM/EffectSound 消费、Ambience 的视觉-only语义、ShadowBlend 写入与无已知消费者。不得把本地字段持久化误称为原版状态语义。 |
| SET-02 | 中，音量端点/音频一致性待复核 | 两个 F751 20×16 可拖动控件已加入 F750 对应 `(34,96)`、`(34,170)`，按原版 `0..160` 轨道量化到 `-100..0 dB` 并分别保存/应用 legacy BGM、EffectSound；同次 Xvfb 回放把两滑块拖到最小再拖回最大，BGM 配置从 `0→-100→0`，EffectSound 最终回到 `0`。剩余风险是拖拽边缘、全 161 个位置映射及多类音效主控效果没有 EI 同状态声学比对 | 原版 `system-window-render-evidence.json` 的 F751 两个实例、`settings-ambience-bgm-volume-evidence.json` 的 `slider_0x441F40`：输入滑块位置0..160，值 `round(position×0.625−100)`；BGM 拖动即时应用，FX 在播放时读取。新增 `LegacyEiVolumeSlider` 以轮询拖动态修复离开控件后漏 motion 的问题；启动配置显示 F751 位于两轨上，运行截图记录最小值与恢复状态。 | 在同版 EI 和 Godot 上于 800×600 比较两滑块零/中点/最大位置；逐位置核像素与 Config.ini 舍入值，检查拖出轨道、按下/抬起、重开恢复；通过实际声音源分别确认 BGM 和各效果类别的静音与音量曲线。 |
| SET-03 | 中，帧状态路径部分静态闭合；隐藏侧命中尚未证明与 EI 相同 | `system-window-render-evidence.json` 给四行左右控制的初始RECT：左侧32×22、右侧40×22。Godot以 `DrawImage=false` 隐去未选中的图标，但控件仍可收到点击；`ConfigDialog.CreateLegacyOptionButtons()`也把8个现存Control一直保留为两个可点半区。原版 `options-toggle-click-handler-evidence.json` 只证明 `0x441A20` 对11个子控件逐个分派点击；通用 `0x4177F0` 用 `PtInRect(this+4)`，不检查帧可见性。F704 的 EI 控件工件将 `0x417880`概括为“写当前帧字段+矩形”，未给frame=-1之后的精确RECT结果。可用旧版 `Client/Controls/DXImageControl.cs` 是较新版本的旁证：Index变化重算DisplayArea；当Index<0，Size回退到基类Size；`DXControl.IsMouseOver()`测试现有DisplayArea，不检查DrawImage或Index。该控件实现暗示负帧可保留原有几何，但可用Client中没有F750对应窗口实现，且不等同目标EI helper。因此不能据“点击分派逐一遍历”或新Client控件行为断言目标EI隐藏侧矩形仍有效；当前代码注释中“两个原版RECT都可点”的依据仍未由EI证据闭合。 | 在不重复业务点击的本轮保持为未决。需取得同版 `0x417880`完整指令/负帧调用后的RECT写入链，判断隐藏侧RECT是否保留；之后比较原版和Godot逐行状态、边界、普通/按下帧、声音及状态副作用。F750底图烘焙字样不能替代hit-test证据。 |
| SET-04 | 中，根相对RECT已修正；帧偏移/关闭操作仍待验 | 原版 F161/F162 控件构造和绘制重定位均记录根相对 `(218,238)`、RECT `28×26`；`ConfigDialog.ApplyLegacyEiLayout()` 已由 `(216,238)` 改到 `(218,238)`，根相对命中框与原版静态矩形一致。独立读取本机EI WIL头：F161/F162均`28×26`、`offset=(-24,-16)`、alpha bbox `(0,0,25,25)`；偏移值不改变由primary-static给出的根相对 hit RECT。原版绘制 `0x417640→0x460240` 使用control坐标 `[+0x28]/[+0x2C]`，研究笔记另概括“UI控件用帧偏移定rect”，二者未在现有材料中闭合为最终屏幕像素锚点。当前 `DXImageControl.UseOffSet` 默认false，故本次只能确认Godot命中RECT位置与原版记录相同，不能据此宣称 F161 图像alpha像素也对齐；RESEARCH EXE/资源身份限制仍适用。 | `system-window-render-evidence.json` 的 controls/hit_rects/paint_repositioned_controls 三处给出 `(218,238)`；frame header通过 `:8766/api/info` 读取并由独立WIL解码交叉核验。修复后 `bash login_game.sh legacy` 服务端/客户端构建成功并自动登录至游戏，但当前 CUA 窗口枚举为空，无法完成800×600关闭帧视觉比较、矩形四边内外点击及关闭动作验收；保持未通过，待桌面窗口可访问时复验。 |
| SET-05 | 中，修复Godot已确认的额外9px根裁切；目标同版最终像素对照仍阻塞 | primary-static 根248×264；本机 `GameInter.wil` F750 header 256×512、offset `(7,-44)`，独立 alpha bbox `(4,119,248,273)`；已归档原始透明帧 [`gameinter-frame-750-wil-2026-09-24.png`](evidence/legacy-ei-ui/gameinter-frame-750-wil-2026-09-24.png)。背景控件设于 `(-4,-119)` 后，有效像素覆盖根相对`[0,248)×[0,273)`，向下超出9px。将透明帧按此实际背景偏移叠加到既有1024×768 Zircon完整截图，边框/标题/横线与画面重合；由关闭控件和根尺寸交叉定位，根 screen origin 候选为 `(388,260)`（viewport local `(388,242)`）。末9行对应 screen y=`524..532`，截图中可见至底边，支持当前 `Clip=false` 确实放出根外像素。研究 `layout.json::window_base_paint_evidence` 只记录 EI 通用背景经`0x460240`、source viewport 800×600，未证明目标EI根窗裁剪细节；此配准仅验证 Zircon 当前截图与本机源帧/布局，不代表与目标EI同版完全一致。 | 当前源码 `ConfigDialog.ApplyLegacyEiLayout()` 已显式设 `Clip=false` 覆盖 legacy wrapper 的根 `Clip=true`；现有 Godot 全屏运行截图 [`settings-window-after-fix-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-after-fix-2026-09-24-1024x768.png) 证明这次布局版本已实际运行并采图，故“修复后尚未重启实屏”是过期状态。截图没有单独保存 F750 root screen RECT/crop offset，当前也没有相同状态的目标 EI 截图；因此它不够证明额外9px的逐像素显示范围与 EI 一致。下一步先用原始 F750 透明PNG独立配准该截图，记录窗口原点、bbox和末9行覆盖；再与可取得的目标EI同状态完整画面对照。Windows 800×600基准配置尚不可用，见PRE-03；该限制不抹去已有1024×768截图，也不把 SET-05 升格为通过。 |
| SET-06 | 中，音量类别路由与原版两类总量设置源码对应；声学曲线未验 | 当前 `ClientSettings.ApplyAudioSettings()` 的 `LegacyUi` 分支将 `Music` 总线设为 `LegacyBgmLevel/LegacyBgmEnabled`，其它总线统一设为 `LegacyEffectSoundLevel/LegacyEffectSoundEnabled`；`BusFor()` 将 `SoundCategory.Player/System/Magic/Monster` 分别送至同名总线。`SoundPlayback.Play()` 与 `GameScene.PlaySound()` 都按相同分类选择 bus。与 EI `settings-ambience-bgm-volume-evidence.json` 的两条全局音量链相符：BGMLevel 重应用于音乐播放，EffectSoundLevel 在效果播放路径读取；此为 Godot 源码映射对照，不证明同一声音在 EI 与 Zircon 的增益、衰减、混音及播放时序完全相同。 | 逐个核 `ClientData/sounds.json` 类别与原版音乐/效果触发分类；同一来源音频在 EI 与 Zircon 对比静音、0/-50/最大音量及正在播放时调节的增益变化。没有可用同版 EI 声音/配置环境前，SET-06 保持未验收，不以总线路由名字相同判为完全一致。 |
| SET-07 | 低，Ambience 视觉态相近但配置保存副作用不等价 | 原版 Ambience ON/OFF（跳表 idx5/6）只换帧、不更新 `this+0x5C`，但两分支仍调用通用 `Config.ini` 保存函数；该函数把此前加载的 Ambience 值原样写回。当前 Godot setter 只切换 `_legacyAmbienceVisualState`，不调用 `ClientSettings.Save()`。故重开窗口时两端都回到既有值，但EI点击会执行一次完整配置落盘，Godot不会。该差异不应被描述成“Godot读写 Ambience 配置状态”。 | 证据：`settings-ambience-bgm-volume-evidence.json` `dispatch_table_0x44194C`、`config_save_0x441B30`、`verdict`；源码：`ConfigDialog.CreateLegacyOptionButtons()` Ambience tuple 与 `ClientSettings.Save()`。是否为全局可观察差异待验；在完成范围内需决定复现原版共享保存副作用或证明保存时机不影响对外行为，不能修改 `LegacyAmbienceEnabled` 来模拟，因为原版点击不写该字段。 |
| SET-08 | 已闭合范围误判：扫描到的高帧控件不属于当前legacy设置页 | legacy `ConfigDialog.ApplyLegacyEiLayout()`隐藏现代 `_page`、标题与页签，并只创建F750、F161/162关闭框、四组两侧帧指示控件F760/761与F762/763、两枚F751音量滑块。独立解析当前优先`GameInter.Zl`与WIL头：F750=`256×512`，F751=`20×16`，F760/761=`32×22`，F762/763=`40×22`，F161/162=`28×26`；全部处于GameInter 0..1102范围。此前全仓高索引扫描中的ConfigSoundBar F4741/4743与ConfigSectionPanel F4750只由现代配置页使用，因`_page.Visible=false`不参与该legacy皮肤画面。原版窗口与控件关系来自primary-static `system-window-render-evidence.json`、`settings-ambience-bgm-volume-evidence.json`；控制语义/持久化差异仍按SET-01..07处理。 | 这项不新增设置图库缺失问题；F750末9px裁切、帧状态RECT、拖动量化/声音效果和同版EI最终画面仍按SET-01..07验收。 |
| MENU-01 | 中，存在 dormant legacy 菜单布局，实际入口语义明确 | EI HUD 菜单/选项图标是 F750 设置窗入口；原版窗口注册表没有通用菜单窗。当前 legacy 点击有效地打开 ConfigDialog，但 MenuDialog 仍保留一个从未调用的 `ApplyLegacyEiLayout()`，会把 F750 当作菜单背景并保留六个现代菜单项 hit zone | `hud-caption-action-tail-evidence.json`/`UI_COVERAGE_MATRIX.md` 的窗口注册表：id12=设置/F750，根尺寸248×264，默认位置(276,113)；HUD idx11 F106/107 及 N 键目标为 id12。当前 `MainPanel.MenuButton` 对 legacy 分支调用 `OpenConfigDialog()`；`MenuDialog` 只在非 legacy 分支被开关，`LegacyOpenWindow("menu")` 也路由到 `_configDialog`。但 `MenuDialog.ApplyLegacyEiLayout()` 把 F750 贴图放进248×264根窗，继续注册设置、帮助、行会、仓库、排行、离开六个现代按钮；代码搜索只发现该方法声明，没有调用点。故当前有效旧版入口没有打开这组菜单按钮；休眠方法与实际 EI 窗口身份混淆，若后续被调用会构造一个不存在的通用菜单。 | 保持菜单概念与 id12/F750 设置窗分开；实施前决定删除/隔离这段无调用的 legacy profile，避免被未来窗口恢复路径意外启用。实机点 HUD idx11 与按 N 后只验设置窗，另外确认无路径显示六项 `MenuDialog`；逐个对照 legacy 的 F750 控件后再决定是否需特殊菜单入口。 |
| WH-01 | 高，结构冲突待闭合 | 当前独立 StorageDialog 使用 F1001，但原版窗口表没有独立仓库窗口 id，F1001 有 primary-static 证据属于 store 状态 2 侧面板 | `window-paint-dispatch-identity.json` / `UI_COVERAGE_MATRIX.md` 记录旧版 16 槽窗口 id5 为 inert/空槽，且无独立 StorageDialog hit/paint；`window-resource-handle-bindings.json::window.store-candidate` 将商店对象资源绑定到 `Data/GameInter.wil`（owner+0x5898/slot70）；研究 `StoreItem.wil` 是独立的物品图标选择器，不能凭帧号1001认作面板图。研究 `store-window-render-evidence.json` 的状态2 GameInter F1001 面板置于 `(−4,182)`、205×205，属于同一个 store 对象 state byte `+0x5F8=2`。当前 `StorageDialog.ApplyLegacyEiLayout()` 把 F1001 设成 205×205 独立根窗，设置4×3格起点 `(22,43)`、步长38；该位置当前仅与状态图state2坐标摘要相符，最终 RECT 尚待状态转换链核定。F1001 原始帧是256×256、viewer头偏移`(7,-44)`，alpha bbox `(28,26,198,204)`；原图已归档为[GameInter F1001](evidence/legacy-ei-ui/gameinter-frame-1001-wil-2026-09-24.png)。图中可见底部左右箭头与4×3格美术，但仅作素材视觉证据，不把美术边框当命中RECT。当前源码令背景按原始帧自然尺寸256×256、位置`(0,0)`绘制。几何证据须按生命周期分层：`store-state-graph.json::states[2].grid_rects`把state2的`+0x720`列为x=22/60/98/136、y=43/81/119；详细`layout.json::constructor_rect_initializers.right_item_grid_rects`与`paint_geometry.item_grid_rect_loop`则记录同一`this+0x720`基址在构造循环`0x44D4C4–0x44D53B`设为x=323/361/399/437、y=43/81/119。后者明确是raw SetRect常量、父级/意义未解，且不适配当前300px背景。这可能对应对象初始/不同state布局，但现存摘要没有给出state2重定位写入点来闭合两者关系；因此当前StorageDialog采用22/43只与状态图中的state2坐标摘要吻合，尚不能独立视为已验收的最终RECT。更正裁剪链：实际`--legacy-ui`路径由`GameScene.ApplyLegacyCoreTestLayouts()`调用`LegacyUiSkin.ApplyLegacyTestWindow()`，它先将根`Clip=true`；`DXControl.Clip`明确映射`ClipContents`并裁剪子控件到本控件边界，随后分派到`StorageDialog.ApplyLegacyEiLayout()`，后者保持205×205根尺寸。因此当前legacy测试布局的F1001子图确实裁到根框：相对alpha范围原为`x=[28,226), y=[26,230)`，可见交集为`x=[28,205), y=[26,205)`，右/下分别裁21/25 px。此前称“未设置Clip、最终是否裁剪待实屏”的说法错误。该结果只闭合Godot控件树裁剪，不证明EI compositor的源帧锚点/父级裁剪与它同构；原版store state2与当前独立根窗的对象/绝对位置差异仍成立。另一差异是 EI F1001 位于 store 对象 state2 流程，研究记录其绘制矩形约 `(-4,182,205,205)`，不是独立仓库窗口；当前独立根窗还把 F161/162关闭命中放在局部 `(177,176)`，原版 state2 控件与外层窗口关闭/返回路径尚未闭合，不能据面板局部格点吻合推断整体同构。仓库业务名有 NPC 脚本/协议交叉证据，但不能改变其旧版 UI 对象归属。仓库和行会在本地 `legacy_ui.json` 中都被分配 F600，另有 `StorageDialog` id5 候选项；这些是未验证配置候选，与 id5 inert 的静态窗口表相冲突。早期研究摘要曾把 bag-manager mode3 的 `0x2BC` 误归为此处 state2；现已裁决 F1001 store state2 对应 `0x2C0`，两条仓储路径见 WH-03。 另须区分EI `0x2BC` 打开的同一背包对象 mode3（F250/0x111..0x113，见WH-03）：它不是此处F1001 store state2。 | 追踪 Zircon NPC 存取包到旧版 store 状态2/背包储存模式的链，确认 F1001 的原版层级、开窗位置、存取成功/取消流程；确认 legacy 模式是否应将 StorageDialog 禁用并把业务接到同一状态机。 |
| WH-02 | 高，已证实的页面与数据映射冲突 | Godot legacy 只建 F1001 的 12 格首屏，隐藏原版状态控件；EI 的静态结构还包含前后页控件，因此不能把 12 格直接等同总容量 | `store-window-content-verification-evidence.json` / `store-state-graph.json`：窗口 id2 的 store 状态2由 `window-resource-handle-bindings.json` 绑定 `Data/GameInter.wil`；state byte `+0x5F8=2`，调用 F1001 `(−4,182,205,205)`；状态图摘要把12格列为根相对 x=`22,60,98,136`、y=`43,81,119`、步长38，但详细布局证据对同一`+0x720` RECT的构造循环给出x=`323,361,399,437`、相同y序列；分页按钮也有相似生命周期差异：状态图将F1014/15、F1016/17候选子控件列在`(x+28,y+162)`、`(x+137,y+162)`，而控件构造工件记录其初始RECT为`(arg4+324,arg5+159)`、`(arg4+434,arg5+159)`。state2是否另有重设尚未闭合。同对象状态码及页数位于store对象字段，不能据此认定独立 StorageDialog。当前 `StorageDialog.ApplyLegacyEiLayout()` 只建4×3网格，隐藏两个滚动条、PartGrid 和页签，没有映射 F1014..F1017；故其固定绑定数组槽0..11且没有 EI 状态页切换。源码细化：`DXItemGrid.CreateGrid()`以`slot=y*GridSize.X+x`创建12个cell，`DXItemCell.MoveItem()`把原始cell`Slot`直接传给`GameScene.SendItemMove()`，未加入页基址/偏移；服务端`PlayerObject.ItemMove()`按账号`StorageSize`检查槽号，`GameScene.FillStorage()`则按`item.Slot`写入完整Storage数组。因此当前EI皮肤网格只能直达首屏槽0..11，其余服务器Storage容量没有该窗分页入口。分页字段按状态拆开后，原记录中的`+0x7E8`与`+0x71C/12`不再视为同一分页公式的冲突：`layout.json::state_1_constructor`与`store-state-graph.json::states[1]`对应F1003（state 1）构造，写入包派生值`/6`到`+0x7E8`；该字段究竟表示页数、批次数或别的状态数据仍是候选。F1001（state 2）的`layout.json::selected_item_and_paging`则明确以`ceil(+0x71C/12)`计算该状态页数，`0x44F9E4`重置页索引`+0x7E0`并设置选择`+0x7E4`。故`store-state-graph.json::store_class_fields`把通用偏移`+0x7E8`概括为“page count”过宽，不能套用于state 2。目前仍未闭合state2 `+0x3D8`分页/选择控件是否写入`+0x7E0`、是否发消息、及记录页到12格槽号换算；控件帧/矩形F1014/15与F1016/17保留候选。也不推测页槽总数，或把当前100槽容量外推为EI事实。Zircon `GameScene.Storage` 实际长度为 `Globals.StorageSize`（默认100，服务端数据可更新），这是当前数据模型容量，不足以证明 EI 原版总页数或槽号顺序。 | 取得与研究工件同版的EI EXE后，核`0x44E9B0`至state2子控件`+0x3D8`的实际分派与读写，进一步裁定state1 `+0x7E8`含义；追页索引`+0x7E0`、选择`+0x7E4`、记录列表到12格显示槽及协议槽号的完整映射。将原版页数/记录容量与 Zircon `Storage` 业务数组分开验证。运行期逐页存取列为阻塞项，待安全、稳定且数据可控的环境再验。 |
| WH-03 | 已裁决：早期摘要把两条不同的仓储路径混为一谈；具体数据语义仍需按对象分别验收 | primary-static `store-state-graph.json` / F399：接收 `0x2C0` 经 `0x420A86/0x420A95` 调 `0x44F940`，对 protocol-store 工厂 F1001 并写 `[store+0x5F8]=2`，属于 id2 store 状态2紧凑网格（“warehouse/extended grid”业务名仍候选）。primary-static F521/F551 `recv1-handler-semantics-evidence.json` / `recv1-mapval14-warehouse-mode-evidence.json`：`0x2BC` 经 `0x420AFC` 检查 `[0x2ABA10]` 后打开 bag-manager 窗口，设 `[bag+0x54]=3`，后续操作使用 `0x111/0x112/0x113`，不调用 `0x44F940`、不创建 F1001。F363 `store-window-content-verification-evidence.json` 早期把 `0x2BC→0x42042B→0x420A9B→0x44F940` 归给 store state2，与F399的分发表路径、F521/F551对 0x2BC handler 的完整解码及不同对象布局冲突，应视为过时/跨对象误归因，不再作为独立的第二种 F1001 消息候选。故不存在“同一 F1001/state2 究竟由0x2BC还是0x2C0打开”的剩余矛盾；存在的是两种仓储UI机制：store id2/F1001 state2 与 bag-manager/F250 mode3。当前 `StorageDialog` 只用F1001，并不因此证明覆盖了EI bag mode3或其 `0x111..0x113`业务。目标EI EXE仍不可独立复核，本裁决限于研究工件内部的primary-static自洽性。 | 保持两个EI对象/状态及消息路径分列：F1001 state2按`0x2C0`证据审计，bag F250 mode3按`0x2BC`和`0x111..0x113`另核；不要在Godot把两个路径合并为一个包号或仅因都涉及仓库就视为等价。目标EXE/运行包恢复后，逐项核状态转换、数据数组、页控件和交互结果；审计中的这条历史摘要冲突已解决，外部目标版本同一性仍未闭合。 |
| GROUP-01 | 高，静态布局已修复；列表内容/点击待实测 | EI 两列成员名字的奇偶顺序、根相对原点和行距与 Godot 原实现不同；现已在 legacy 分支按静态坐标修正。成员行是否可点击尚无闭合证据 | F536 `group-window-render-detail-evidence.json` 与 `social-window-render-evidence.json`（primary-static）闭合为 idx 奇数→`x+45`、偶数→`x+145`，y=`y+90+20*floor(idx/2)`；遍历顺序为链表插入序，名字白色。`GroupDialog.RebuildMembers()` legacy 现在按该公式绘制并不加选中绿色，index 0 在右列 `(145,90)`、index 1 在左列 `(45,90)`；现代分支未改。当前数据入口`AddMember(objectId,name)`写入`Dictionary<uint,string>`，绘制直接遍历该字典、未显式排序；EI则遍历原版成员链表。仅凭当前回放函数不能证明网络到达/删除/重加后的字典枚举序与EI链表序逐步一致，成员顺序保留为源码差异候选。F845/F948明确了五个子控件输入，但没有成员行命中RECT证据；当前Godot成员名仍能选择高亮成员，故保留为候选交互，不宣称等价。 | 登录后用 0、1、2、3 名真实/可控队员逐步验证奇偶次序、名字数据及窗口边界；追踪原版 `0x424610/0x424730` 是否命中成员行后再决定是否保留选择动作。GROUP-02 裁剪、GROUP-03 权限字样和 GROUP-04 按钮行为仍分开验收。 |
| GROUP-02 | 高，额外裁剪已修复；真实溢出验证待做 | 原版不在成员列表内部限制行数或裁剪到101px；legacy 现在让成员区采用整个 256×244 根窗范围并绘制完整集合 | primary-static `social-window-render-evidence.json` / RESEARCH_LOG Finding 259：paint `0x42443E–0x4244A4` 遍历整个 linked list，仅以 next==0 结束；没有行数 cap，内容由 256×244 根窗 viewport 裁剪。legacy 的 `_memberPanel` 已从 `(17,59,222,101)` 移至根窗 `(0,0,256,244)`，`RebuildMembers()` 不再额外 `Take(Globals.GroupLimit)`；现代分支保留旧上限。F900 原始 WIL metadata 256×256、alpha bbox `(0,6,256,244)`；背景 `(0,-6)` 与根窗对齐。 | 用 0/10/11/15 名成员检查最后完整行、跨列顺序和根窗底边裁切，做窗口边界像素对照；EI paint只证明没有绘制硬上限，服务器人数上限另查协议/运行数据。此项目前仅源码与编译确认，未做运行回放。 |
| GROUP-03 | 高；输入控件已按静态证据移动，动态状态文字仍未实现 | EI 以 `[允许]/[拒绝]` 状态文字绘制权限状态；当前 legacy 已隐藏现代 checkbox/固定标题并增加 F920/F921 点击控件，原版文字横坐标尚不可证 | primary-static `social-window-render-evidence.json` / Finding 259：`this+0x3F0` 选择 `[允许]` 或 `[拒绝]`，颜色 `0xDCE6C8`，y=`window.y+0x3A`；文字 x 因读取未初始化栈槽而明确不可证。F845/F948：F920/F921 控件坐标 `(9,52)`，状态分派调用 `0x452310` 发送 `0x3FB`。当前 `GroupDialog.ApplyLegacyEiLayout()` 已隐藏 `(137,26)` 的现代 `_allowCheck` 和 `(186,40)` 固定标签，并将 GameInter F920/F921 28×26 控件放到 `(9,52)` 接 `ToggleAllow()`；但 `[允许]/[拒绝]` 文字尚未移植，`C.GroupSwitch` 与原版 0x3FB 的协议字段等价仍须核实。 | 追明允许/拒绝两态实际字段更新时机，复核 `C.GroupSwitch` 对现行服务端状态的影响；原版运行态取两态截图，证实文本横坐标后实现文字。F920/F921 click 与 ToggleAllow 仍缺真实点击验证，未验收。 |
| MODAL-01 | 阻断 | EI F950 确认框的构造/回传按调用类型分派；当前共享 ConfirmDialog 不能代表原版确认流程 | `confirmation-prompt-evidence.json` primary-static：原版 F950 360×190，默认居中在800×600 `(220,151)`；调用方选择三种按钮布局，状态控件用 F151/152、F154/155 或 F157/158；Tab 切换焦点、Enter/Space 激活；消息以 `0x7EE` 携带类型/按钮索引回传。直接调用证据列出8处：转账金额(type 3/tag 0x405)、丢金币(type 0x66/tag 0x30E)、个人仓库满与仓库操作拒绝(mode 0/tag 0xFFFF)、行会删除成员(type 6/tag 0x3FE)、掌门权限提示(type 4/tag 0xFFFF)、返回人物选择(type 0x65/tag 0xFFFF)、创建行会名称(type 9/tag 0x3F3)。其中金币与行会创建路径还会进入输入框，不是通用 yes/no。当前 `ConfirmDialog` 固定 Interface F281、252×128、文本+确定/取消本地回调；`ConfirmDialog.cs`/`DXButton.cs`没有 `_UnhandledKeyInput`、Tab/Enter/Space 路由或 `FocusMode` 设置，按钮的自定义 `HasFocus` 只在鼠标按下置位，因此当前源码没有原版键盘焦点链。此处仅是静态代码差异，未操作弹窗。`ExitDialog` 另用 Interface F281 并直接调用离开/退出；它与 F950/F800 的操作语义均不同。 | 逐个核对这8个原版调用点的 type/tag、按钮模式、文本输入/发送路径；为真实对应的 legacy 调用建立映射；实现后再测 Tab/Enter/Space、取消、按钮回传和网络行为。确认 `ExitDialog` 的 F800 路径另列 EXIT-01，不与 F950 合并。 |
| MODAL-02 | 高，当前共享确认窗的调用范围及原版覆盖关系不匹配 | `ConfirmDialog` 不是仅用于现代现金商城：源码直接创建点还包括 `StorageDialog.SortStorage()`、`QuestDialog.ConfirmAbandon()`、`GuildDialog` 两项城堡维修，以及寄售购买/下架/上架、游戏商城、抽签、宝箱、伙伴释放、NPC评估等。前四类在 legacy 游戏路径也可到达；这些调用都会得到同一张 Interface F281、252×128 的 Godot yes/no 窗，且按钮仅运行本地回调/关闭。原版证据只证明特定 F950 type/tag 及 F800 退出窗，尚未证明上述 Godot 扩展业务都应显示原版 F950。此前审计稿把 ConfirmDialog 唯一调用点写成 GameStoreDialog，和当前源码搜索结果不符，现已纠正。 | 建立逐调用点表：模块、legacy 可达性、动作/协议、对应 EI 原版证据编号、是否属于 Zircon 扩展。对有 EI 对应证据的路径实现类型化确认；没有对应证据的扩展调用单独标为 Zircon 行为，不冒称 EI 原版。逐类核对按钮默认焦点、键盘操作、取消语义和 callback 是否重复执行。 |

**MODAL-02 当前调用点清单（只读源码映射，2026-09-24）：**`rg 'new ConfirmDialog'` 共找到15处：`ConsignmentDialog` 购买/下架/上架3处；`StorageDialog.SortStorage()`；`QuestDialog.ConfirmAbandon()`；`GameStoreDialog` 购买；`FortuneCheckerDialog` 查运势；`NPCCompanionStorageDialog` 释放伙伴；`GuildDialog` 城门/守卫修复2处；`NPCAdvancedPanels` 评估；`LootBoxDialog` 揭示、重抽、单选确认、提交4处。当前抽样读到的 yes 回调直连各自 Zircon 操作/网络发送，no 回调通常只关该窗；其 legacy 功能开关、入口可达性及空/重复回调仍要逐条补测。原版 F950 的8个静态 callsite 不等价于这15处业务：只有注销(type `0x65`)与其中某些“询问/金额输入”在词义上可能接近，是否复用/如何复用必须依原版 type/tag 和后续发送链对齐，不能按标题相似合并；Zircon 扩展项先保持扩展身份。

**MODAL-01/02 逐调用点静态映射（2026-09-24，只核源码构造点与 primary-static 证据）：**原版表按 `confirmation-prompt-evidence.json::direct_callers_with_original_messages` 八条机器码调用记录；当前表按 `rg 'new ConfirmDialog' GodotClient --glob '*.cs'` 的15个构造点。原版记录中的空 type/tag 项表示证据 JSON 没有给出该实参，不能补猜。现有材料没有证明下面任何一个当前 `ConfirmDialog` 构造点直接对应某条 F950 调用；相同的“确认/确定”文案不能建立映射。

| 当前 Godot 构造点 | 当前 yes 动作（源码） | 原版 F950 对应 | 静态判断 |
|---|---|---|---|
| `StorageDialog.SortStorage()` | `SendItemSort(Storage/PartsStorage)` | 无；EI F950 两条个人仓库记录是满仓/操作拒绝通知(mode 0)，不是排序确认 | Zircon 当前确认扩展；不可映射为仓库通知 |
| `QuestDialog.ConfirmAbandon()` | 继续执行该任务放弃路径 | 无已核 F950 callsite | 业务同属游戏功能不足以证明同窗/同回传 |
| `ConsignmentDialog` 购买、下架、上架（3处） | 分别继续购买、撤单、提交寄售 | 无已核 F950 callsite | 三条市场业务分别保持 Zircon 实现身份 |
| `GameStoreDialog` 购买 | `SendGameStoreBuy(...)` | 无已核 F950 callsite | Zircon 商城路径；不是 EI 金币输入(type 3/0x66)证据 |
| `FortuneCheckerDialog` 查运势 | `SendFortuneCheck(...)` | 无已核 F950 callsite | Zircon 确认扩展 |
| `NPCCompanionStorageDialog` 释放伙伴 | `SendCompanionRelease(index)` | 无已核 F950 callsite | Zircon 确认扩展 |
| `GuildDialog` 修城门、修守卫（2处） | `SendGuildRepairCastleGates/Guards()` | 无；原版行会成员删除(type 6/tag 0x3FE)、掌门限制(type 4/tag 0xFFFF)语义不同 | 不得借“行会”类别套用原版调用 |
| `NPCAdvancedPanels` 评估 | `SendNPCMasterRefineEvaluate(...)` | 无已核 F950 callsite；源码注释称“原版 Evaluate 弹确认框”，目前缺对应原版地址/type/tag 证据 | 该注释是待核主张，不作 parity 证据 |
| `LootBoxDialog` 揭示、重抽、取出、确认选择（4处） | 货币校验后分别调用宝箱操作/发送 | 无已核 F950 callsite | Zircon 确认扩展 |

反向覆盖核对：八条 EI F950 callsite 中，金币转账金额(type 3/tag `0x405`)、丢金币金额(type `0x66`/tag `0x30E`)、两种仓库拒绝通知(mode 0)、删除行会成员(type 6/tag `0x3FE`)、掌门权限提示(type 4)、注销回选人(type `0x65`)及创建行会名称(type 9/tag `0x3F3`)均未在这15个 `new ConfirmDialog` 构造点中出现直接同构实现。`ExitDialog` 的退出路径属于独立 F800/id `0x64`，不是第16个 F950 yes/no 映射。此为源码构造点搜索，不排除其他工厂/反射创建方式；目前静态搜索没有显示这些使用 `ConfirmDialog`。

**2026-09-24 MODAL-01 键盘焦点静态核验：**原版 `confirmation-prompt-evidence.json::input_handlers` 记录 Tab 在三个按钮索引间循环（跳过disabled并回绕），Enter/Space进入 activation，再按当前 type/index/tag 组成 `0x7EE`；当前 `ConfirmDialog.cs` 没有键盘处理器或焦点顺序设置，`DXButton.cs` 的 `HasFocus` 仅由鼠标左键按下设为true，且仓库代码未在这两个类中设置Godot `FocusMode`。交叉搜索 `GameScene`/`WindowManager` 后，也未发现针对 `ConfirmDialog` 的外部键盘分派：`GameScene._Input()`跳过文本编辑焦点后处理指定窗口热键，`_UnhandledInput()`处理鼠标/地图输入；没有 F950 等价的 Tab循环、Enter/Space回调。故已有 yes/no 鼠标回调路径不能视为焦点/键盘等价。静态结论基于上述类与全局路由搜索，未运行键盘事件。后续实现类型化弹窗时补齐键焦点与激活，再做Tab/Enter/Space运行期验收；本轮按限制将这些输入留在待验证清单。

下一步静态工作：追查原版 type 3/`0x66` 的输入窗、type 9 行会创建及 type 6 删除名字的控件与 `0x7EE` 回传接收端；扫描当前相应业务的实际入口、网络消息与 window factory，补齐“无直接构造点”是否由其他控件实现。动态 Tab/Enter/Space、取消/确认及服务端效果保留为阻塞项，本轮按要求不触发。
| GROUP-04 | 高，底部三个动作命中矩形吻合；关闭控件有2px静态偏移，业务流程仍待实测 | legacy 已将底部三个 hit zone 调整到 F910/F912/F914 原版坐标和尺寸，原第三项从 LFG 编辑器改为离队；旧现代 Options 区已隐藏 | primary-static `social-window-render-evidence.json` 的重定位记录：关闭 `(226,214)` 28×26；F910/911 `(17,197)` 60×20；F912/913 `(80,197)` 76×20；F914/915 `(159,197)` 76×20；F920/921 `(9,52)` 28×26。legacy 当前 Add/Remove/Leave 分别使用前三个精确矩形，透明热区；Leave 调用现有 `SendGroupSwitch(false)`，服务器 `PlayerObject.GroupSwitch(false)` 会在成员组存在时调用 `GroupLeave()`，这是适配当前协议的行为路径，尚未证明等价原版0x3FE。F920/F921 已映射为权限动作；`_optionsButton` 已隐藏。邀请仍显示内嵌输入框，但实际发送按钮位于根窗外；F845提到的0x418030/frame 6不能归为邀请流程：24个ctor调用点清单将0x4246BA映射到“请在这里添加您要删除的小组成员名字.”。新成员输入字符串虽已记录，具体控件与提交链仍待映射。另当前 `GroupDialog.ApplyLegacyEiLayout()` 的 `_closeButton` RECT 为 `(224,212,28,26)`，与原版 `(226,214,28,26)` 左上偏2px。独立 `:8766` PNG alpha测量确认F900画布256×256、alpha `256×244+(0,6)`，F161/F162画布28×26、alpha各`25×25+(0,0)`；当前F900背景位置`(0,-6)`恰使有效像素填满256×244根窗，关闭帧透明边不解释或抵消其RECT偏移。归档[原帧F900](evidence/legacy-ei-ui/gameinter-frame-900-wil-2026-09-24.png)、[F161/F162](evidence/legacy-ei-ui/gameinter-group-close-frames-2026-09-24.png)。底部三热区对齐不能据此推成关闭框也吻合。 | 将关闭控件根相对位置改至证据 RECT 后，独立核控件RECT与可见 alpha 像素锚点；验证 Add 输入/确认/取消与 EI 对话窗的字段和按钮；验证非队长离队时 `GroupSwitch(false)` 的服务端结果/副作用；逐个点按三个底部热区、F920/F921及关闭控件，检查边界与消息。当前只通过编译，尚无窗口输入回放。 |
| GROUP-05 | 高，当前成员移除选择路径没有原版交互证据 | 当前 `RebuildMembers()` 给每个成员名标签加点击选择回调，并据 `_selectedMember` 启用移除按钮；`RemoveSelectedMember()` 由 object id 查当前地图 `_objects` 名称，再发送 `C.GroupRemove{Name}`，服务端按名字查找队员并执行 `GroupLeave()`。原版 primary-static 绘制链只证实成员字符串的双列输出；F948 记录 `0x424770` 遍历五个子控件输入，F845/F948 记录组队按钮 `0x3FD` 走发送器 `0x452350`，并有“请在这里添加您要删除的小组成员名字.”原版字符串；`layout.json`将对话框构造点0x4246BA与该删除提示明确关联。现有材料仍没有成员行 hit RECT/点击处理器闭环，也没有证明该提示与0x3FD按钮的实际顺序，因此当前“点成员行选中→移除”属于 Zircon 自增行为候选，尚不能判定与 EI 相同。 | 补齐原版 `0x424610/0x424770` 与 0x3FD/0x452350 的完整输入分支，分别查清“添加新成员”与“删除成员名字”两个文本控件的创建、确认和发送顺序（0x4246BA的帧6调用归删除提示）；随后对照当前对象名缺失/异地图/同名等场景，确定现有 C.GroupRemove 目标解析能否表达原版。原版交互证据闭合前不把成员标签点击作为 parity 通过项；按本轮限制不做可能触发 Bad Request 的运行期组队操作。 |
| GROUP-06 | 高，id7 group-pop候选路径已静态定位，当前没有同路径窗口实现 | `window-visibility-dispatch-evidence.json` 将独立 EI id7 (`hero+0x47C28`, F200) 的开启调用者 `0x42C0A6` 标作 `group-pop command path`，该路径把 id7 交给窗口显隐dispatcher；独立构造/paint链将该窗绑定角色形象与属性槽（CHAR-06），但实际触发控件/成员选择条件仍是候选。当前 `GameScene.OnGroupMember()` 只向 `GroupDialog` 与常驻 `GroupHealthPanel` 加成员；`GroupHealthRow` 无点击处理器，`GroupDialog.RebuildMembers()` 的点击只设置 `_selectedMember` 供移除动作使用；没有发现由成员信息打开 F200/id7 状态预览的路径。 | 以 `0x42C0A6` 前后调用者和 `0x450AC0` hit分支追组队成员/选中实体数据如何进入 id7；对照 `GroupDialog`、`GroupHealthPanel` 当前事件树与消息字段。确认业务前只记录“Godot缺少已证实的独立id7预览路径”，不把常驻血条面板或成员移除选择冒充等价。真实双人/组队运行输入受本轮限制，登记后跳过。 |
| GROUP-07 | 阻断级实现缺口：legacy 邀请输入无法通过当前窗内控件提交（静态源码/几何可证；EI流程另待映射） | `GroupDialog` 构造时发送按钮`invite`在根相对`(149,260)`、62×23，唯一 `MouseClick` 回调才读取 `_inviteName` 并调用 `SendGroupInvite()`；`GroupDialog.ApplyLegacyEiLayout()`把输入框改到`(17,194)`、130×23，却没有移动该 `invite` 按钮，也没有给 `_inviteName.TextSubmitted` 订阅提交回调。`bash login_game.sh legacy`的共同包装`LegacyUiSkin.ApplyLegacyTestWindow()`将GroupDialog根设为256×244并`Clip=true`，故发送按钮的Y区间`260..283`完全在根外被裁掉；Enter提交在GroupDialog内也无处理器。输入框`(17,194..217)`还与已重设的F910透明邀请hit区`(17,197,60,20)`重叠，且后加入的动作按钮覆盖这块鼠标区域。primary-static记录将F910/911作为邀请入口，并有帧6/`0x418030`对话窗构造候选；当前inline input既无可见可点提交控件，也与邀请入口共用区域，不能把SendGroupInvite方法存在视作legacy邀请可用。 | 静态来源：`GroupDialog`构造回调、`ApplyLegacyEiLayout()`坐标、`LegacyUiSkin.ApplyLegacyTestWindow()`根clip；独立计算按钮矩形与根窗交集为空。随后追 EI F910 click→`0x418030` frame6输入窗及 accept/cancel 消息，决定是否需采用独立子窗而非内嵌输入。完成布局/业务修复后只在隔离、安全的双人测试场验邀请、取消、拒绝/接受和错误回包；按当前要求此轮不发送邀请、不做双人操作。 |
| NOTICE-01 | 阻断 | id=15/F602 的静态根几何/裁剪已有映射，但文本状态、编辑和按钮行为不符；当前还把公告聊天消息直接当作打开此窗的触发 | `notice-prompt-window-evidence.json` primary-static：800×600 原版父窗 `(107,110)`、584×252、GameInter F602。WIL 直接解码显示 F602 完整 alpha bbox `(220,2,583,252)`（开区间范围 x=[220,803), y=[2,254)），超出584×252根矩形的部分需按原版绘制裁剪判定；当前 `LegacyUiSkin.ApplyLegacyTestWindow()` 为此窗设584×252根尺寸及 `Clip=true`，背景仍按1024×256源帧绘制，裁剪模型与原版证据的584×252有效窗口参数相符，最终像素仍待实屏截图核。F161/162 控件 `(548,16)`、28×26，子控件处理器消费命中，随后外层 id15 分派根据非零命中结果切换窗口显隐；原版两态控件没有普通帧，hover=606、pressed=607，命中 `(496,27,40,20)`；两段提示文本位于 `(23,94)` / `(24,95)`，由 `this+0x1D0` 分支选择“行会修改等级/排行”或“行会公告”。编辑缓冲位于 `this+0x1CC`；点击操作控件提交 msg `0x411` 或 `0x410`，外层分派处理其显隐。当前 `NoticeDialog` 把 F161 设为普通态、162 设为 hover/pressed，把 F606 设为普通态、607 设为 hover/pressed；且用只读 DXTextArea、X 直接关闭、F606 按钮也只关闭。Godot `ReceiveChat(..., MessageType.Announcement)` 在 legacy 模式调用 `ShowLegacyNotice()`，把聊天消息文本写进此窗并打开。原版 `0x7EE` notice receive 链静态写入 chat-input 编辑框 `[0x8AA48C]`，不是 F602 id-15 窗口；两条业务来源不可视为相同。 | 对照原版状态字段、编辑子控件和 msg `0x410/0x411` 的两条提交链；确认 Godot Announcement 数据来源/协议是否对应其他 EI 分支。复核 F602 两种占位文本、编辑焦点、外层显隐切换、两态按钮 hover/pressed 帧与服务器处理；截图验证完整alpha超出根尺寸部分是否与原版clip一致。 |
| NOTICE-02 | 高，静态生命周期已闭合；触发残余待运行时 | 旧研究曾把 F602 的复用对象描述成独立滚动公告横幅；该结论已被 F294 修正 | primary-static `notice-banner-lifecycle-evidence.json` Finding 294：`0x777200 = winmgr(0x7243A4)+0x52E5C`，就是同一个 id=15 对象，不是第二个独立横幅对象。guild 控件 idx4/idx7 会把它重新设为 F602/F601、写入 guild 列表文本并显示；没有 timer/tick/scroll API 静态引用，显示持续到显式 toggle。F161 子控件处理只消费命中；外层 `0x42BE95` 在 id15 handler 返回非零时调用 id15 toggle 隐藏窗口。F606/607 操作控件提交 msg `0x411/0x410`，其命中也由外层分派处理。原版 0x7EE receive 更新 chat-input `[0x8AA48C]`，id15 的可编辑子控件则是 `[0x7773CC]`。当前 `NoticeDialog` 注释和 `ReceiveChat(Announcement) -> ShowLegacyNotice` 仍把聊天公告内容直接写入并打开 F602；与同一 id15 的原版对象/触发模型不符。遗留 runtime-only：何种事件将 winmgr 激活门置位、BSS 缓冲运行时文本，以及解密绘制回调是否有额外动画。 | 追运行时触发输入来源与网络包对应，观察 guild 两分支的 F601/F602、编辑文本、显隐及 close/submit。EI 实际运行不可用前保留为明确 runtime 候选，不以 Godot 行为替代。 |
| EXIT-01 | 阻断 | EI 的“退出游戏”和“注销人物”是不同操作路径；当前 HUD 两个按钮及 Alt+Q/Alt+X 都打开同一自制退出菜单 | primary-static `hud-caption-action-tail-evidence.json`：idx4“注销人物(Alt+X)”直接构造 F950 确认框，type `0x65`、message“返回游戏人物选择界面？”；idx3“退出游戏(Alt+Q)”调用 `HUD+0x53030` 子对象 vtable `+0x10(1)`。`window_init_candidates.json` 与 `window-catalog-evidence.json` 将该子对象定位为 id`0x64`、F800、800×600 根原点 `(218,176)`、364×184 窗。primary-resource-visual：viewer 导出的 GameInter F800 为512×256、offset `(7,-44)`，画面韩文为“是否退出游戏？”，YES/NO 标签烘焙在背景中；独立 PNG alpha 框为361×183、偏移`(+74,+36)`。`confirmation-prompt-evidence.json` 需按对象拆读：singleton F950/type `0x65` 的 mode1 YES/NO RECT 是相对其 360×190 根窗 `(51,125,44,20)` 与 `(244,125,44,20)`，居中根原点 `(220,151)`；这些是 F950 自身构造器证据。其 cluster2另证 F800/id`0x64` 为独立 364×184 窗类，初始化 RECT `(218,176)-(582,360)`，对象偏移 `+0x53030`，子控件字段 `+0x54` 和 `+0x108` 复用 F151/152、F154/155 帧族；这只能证明资源帧复用，不能把 F950 的相对 RECT 或 type `0x65` 语义移给 F800。鼠标抬起链 `chat-window-mouse-dispatch.json` 记录对 `base+0x53030` 调用 `0x418A00`，命中后执行 `SendMessageA(mainHWND, 2, 0, 0)`。独立 `input-message-bus-0x7ed-0x7f0.json` 将主窗口 WndProc 的消息号2路由到 `0x41CEF0`；另一个函数分派记录把2明确识别为 `WM_DESTROY`，并关联 `0x41D2B0` 窗口态保存路径。故研究工件中“WM_CLOSE literal/WM_CLOSE=2”的文字标注与 Win32 定义冲突：`WM_CLOSE=0x0010`，`WM_DESTROY=0x0002`。primary-static 可支持“F800 hit helper命中后向主窗口投递消息2并进入该WndProc分支”，不能把它改述成 WM_CLOSE 请求，也不能仅据此证明进程终止语义。该分派没有给出 YES/NO 两框的独立 hit RECT/取消分支，故具体命中区域和按钮各自行为仍未决。Godot 的 `ExitButton` 和 `LogoutButton` 都调用 `OpenExitDialog()`；当前 `ExitDialog` 为 Interface F281/252×128，源码把“回选人”和“退出客户端”两个 130×25 按钮都放在根相对 x=61、y=48/78，close 控件另走本地 `WindowManager.Close`。`ExitDialog.cs` 原类注释称其为“原版 ExitDialog”，但源码本体创建 F281 与两项 Zircon 自定义动作按钮；这与已确认 EI F800/F950 的资源和分流事实不符。本轮已把注释改为“Zircon 双操作退出对话框：返回角色选择或退出客户端”，只纠正文档说明，不改入口或操作行为。`KeyBindManager.Defaults` 又把 Alt+Q 与 Alt+X 配为同一个 `ExitGameWindow` action，`GameScene.HandleKeyBind()` 该 action 只调用 `OpenExitDialog()`，故两个快捷键也打开相同菜单。上述是当前实现命中几何/动作，不代表原版 F800/F950 按钮位置。 | 从 F800 控件构造/点击处理继续追 hit rect、F151/152 与 F154/155 的屏幕位置，以及消息2进入 `0x41CEF0` 后的完整生命周期；查明F800 YES/NO是否有独立命中分支，并厘清研究资料将2误注为WM_CLOSE的历史来源。legacy 中分开绑定 HUD idx3/Alt+Q 与 idx4/Alt+X；idx4复现 F950 type `0x65` 返回选人链，idx3复现 F800 流程。分别验取消、确认、网络清理及登录状态恢复。 |

**EXIT-01 运行/资源补证（2026-09-24，Xvfb :100，1024×768）：**按用户指定的本机素材预览服务直接取得 `GameInter.wil` F800 PNG（`http://localhost:8766/api/image?f=GameInter.wil&i=800&scale=1&bg=transparent`）；独立 `identify` 测得全画布512×256、非透明有效像素框361×183，偏移`(+74,+36)`。素材截图[`ei-gameinter-f800-primary-resource.png`](evidence/legacy-ei-ui/ei-gameinter-f800-primary-resource.png)可见韩文确认句与烘焙 YES/NO 字样；画布/alpha框不等于窗口根RECT，仍以364×184初始化字段作为静态根候选，绘制锚点及裁剪未闭合。真实登录客户端点击 HUD cap3 后截图[`zircon-cap3-current-exit-dialog-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/zircon-cap3-current-exit-dialog-2026-09-24-1024x768.png)：实际弹出当前 `ExitDialog`，含“返回角色选择/退出客户端”两项，与 F800 YES/NO 确认状态明显不同，故该差异升为 **Zircon runtime-verified**。随后悬停 cap4 时可见“注销人物(Alt+X)”提示，但 cap3 菜单仍盖在上层；没有点击确认或注销，避免混淆两入口的动作。此轮没有取得 EI 游戏运行画面，原版 F800 输入/取消/确认效果、Godot Alt+Q/Alt+X 实际分派和返回选角流程都仍未验。截图[`zircon-cap4-over-modal-tooltip-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/zircon-cap4-over-modal-tooltip-2026-09-24-1024x768.png)只记录遮挡状态下的悬停提示，不能用来证明 cap4 动作。

**EXIT-01 修正记录（2026-09-27，源码静态核对）：**代码现状已与原版分流对齐——`GameScene.CreateHud()` 中 `ExitButton.MouseClick → OpenExitGameDialog()`（行 4797）、`LogoutButton.MouseClick → OpenExitDialog()`（行 4814）；`KeyBindManager` 默认表 Alt+Q→`ExitGameWindow`、Alt+X→`LogoutCharacter`，`GameScene.HandleKeyBind()` 分别调用 `OpenExitGameDialog()`（F800 `ExitGameDialog`，364×184 @ (218,176)）与 `OpenExitDialog()`（F950 `LogoutConfirmDialog`，360×190 @ (220,151)，YES → `Game.LeaveGame()` 返回选人）。两窗几何与 `window-catalog-evidence.json`/`confirmation-prompt-evidence.json` 静态证据一致。剩余未决项仅为原版 F800 YES/NO 命中 RECT 与消息 2 生命周期（见上段“运行/资源补证”），实屏点击验证待补。
| EXIT-02 | 高，legacy 退出窗所有 Interface 美术资源在当前EI根缺失，显示结构为占位回退 | `MirSkin.ResolveUiDataPath()`在`--legacy-hud`指向`/home/tetsuya/mir3ei/LegacyEI/Data/`；`IsUiLibrary(Interface)`只从该根尝试Interface.Zl，缺库时再尝试Interface.wil/.wix。本机目录三种`Interface`文件均不存在。`ExitDialog`的底图指定Interface F281但固定252×128，因此仍有根矩形、贴图为空；关闭控件Interface F15非固定尺寸，`DXImageControl.Index`按`GetSize()`取0×0；两个SmallButton(Index=-1)要求Interface F41/43/42端片，缺图时`DXButton.DrawFallbackButton()`绘制通用色块。文字标签仍由独立控件显示。当前运行截图中的自制退出窗因此不是F800资源复原；这一资源缺失与EXIT-01的退出/注销路径合并是两个独立差异。结论来自当前源码和资源目录，不表示目标EI本身缺少Interface资源——primary-static F800确认其背景来自GameInter。 | 若实现EI退出流程，应优先映射GameInter F800及F950调用各自的原版构造/按钮链，不以添加当前缺失Interface库或替换F281作为EI复原；将现有ExitDialog明确标作Zircon扩展，或legacy模式下由等价原版窗口替代。取得F800完整hit RECT/YES-NO动作与F950动态构造参数后再实施并做像素及行为验收。运行期退出/注销测试继续遵守本轮跳过条件。 |
| MAP-01 | 阻断 | 小地图资源、显示模式和命中控件未按 EI 状态机分支；Godot 独立可调整 `DXWindow`（200×200/300×300）不等于原版嵌入式128/256 surface | primary-static `map-ui-resource-evidence.json` / EI-310 `minimap.json`：EI 地图子对象嵌在主世界对象中，没有独立 map dialog；默认绘制目标矩形精确 `(672,0)-(800,128)`（800×600）。较早 `map-ui-resource-evidence.json` 将T键与`owner+0x2A8`鼠标区共用0x43DE40/`owner+0x294`的切换归因，现由较新的F316 primary-static热键复核校正：T(0x54)在0x42CE90输入分派内直接翻转`main+0x64A8`，再调用0x43D5F0重建256×256或128×128 surface；用户可见“放大/缩小”名称仍是候选。F316闭合注释还指出0x43DE40全文件无调用者、0x43DEB0是Ctrl+拖动移动；所以撤回旧JSON中“owner+0x2A8点击调用0x43DE40切surface模式”的结论，辅助矩形真实点击动作待目标字节复核。128/256指内部 surface 分辨率，不是屏幕控件尺寸，固定屏幕目标在两种 mode 下仍为128×128。`map-ui-resource-evidence.json` 的 primary-static `source_to_view_transform` 将 `+0x2B8/+0x2BC` 记为视图位置、`+0x2D0/+0x2D4` 记为源偏移/裁剪，`0x466800` 为归一化助手，`+0x290` 分支系数为1.0或`0x3F2FAFB0`；这些字段与WIL源帧画布、`+0x2C0`固定目的Rect分层，不能以单帧缩放复刻。资源选择证据里的 `map_id < 1000 → MMap / >=1000 → FMMap` 是 EI 地图控件自己的 selector 约定；它不能直接等同于服务端 MiniMap 字段、地图文件名或 Godot `MapInfo.Index`。`minimap-server-crossref.json` 与 `Tools/mir3_client_simulator/data/map_bindings.json` 将地图名称/服务端 MiniMap 值关联到库和帧，例如 EI “沙巴克城”对应地图文件名 `3`、服务端小地图值 `1018`、FMMap F17，“沙漠土城”对应 FMMap F7；直接从 viewer 导出的原图确认 FMMap F7 为沙漠土城图而 MMap F7 为空。只读打开当前实际加载的 `Debug/Client/Data/System.db`（`SessionMode.None`，不保存）得到 `MapInfo(Index=7, FileName=3, Description=Sabuk Keep, MiniMap=7)`；EI `RESEARCH_LOG.md` 的 map binding 为 `3.map=1018→FMMap F17`。已有 viewer 图像记录将 F17 识别为600×600沙巴克小地图、FMMap F7识别为600×400沙漠土城、MMap F7为空；本轮另用独立标准库 `wilsdk.WilLibrary.header()` 直接读取本机EI WIL/WIX核帧头：FMMap F17=600×600、F7=600×400且offset均(-24,-16)，MMap F7无帧头。FMMap.wil SHA-256 `f7203f90aa7790a6f24f2cc30ffb5960f89fde34c111e82285fb0cac25ef3c28`，MMap.wil SHA-256 `e5b5b3fe43d3139634ee2952f3b9748331cbea82640bf4ca3144d8792094b630`。帧头核验确认资源尺寸/空帧状态，不替代当前 viewer不可用时的目标版视觉运行对照。因此现行数据库的 `MiniMap=7` 不是 EI 的 WIL 帧号；legacy 必须按 map file name 映射库与帧。当前 `MiniMapDialog.SetMap()` 使用 `map.MiniMap` 选帧。玩家框/闪烁点和对象标记链已静态闭合。当前 `LibraryCore/Libraries.cs` 将 `LibraryFile.MiniMap` 指向 `Data/MiniMap.Zl`；EI 资源目录实际提供 MMap.wil/FMMap.wil，未发现 `LegacyEI/Data/MiniMap.Zl`。当前 `MiniMapDialog` 是 `_uiLayer` 下独立 `DXWindow`，允许resize，初始200×200、放大300×300；标题来自 `map.Local()`，图层在裁剪Panel中按数据缩放并居中，另有F132尺寸、F130透明度、F137大图按钮。`ApplyLegacyCoreTestLayouts()`未给它应用EI surface布局；`LayoutHud()`仅把该独立窗锚在视口右上。已有本机实际运行快照 [`zircon-live-ui-current-2026-09-24-0512-1024x768.png`](evidence/legacy-ei-ui/zircon-live-ui-current-2026-09-24-0512-1024x768.png) 中技能书与小地图同锚右上：按1024宽和UiScale=1，mini root约`(824,0,200,200)`，F400 root约`(572,0,452,380)`；技能书矩形覆盖小地图整个根区域，快照该处显示F400画面而没有可审计的地图像素。此仅是该Zircon窗口组合的runtime-snapshot几何/叠放观察，不证明EI应采用何种窗口优先级。尚无证据说明 MiniMap.Zl 的索引与 EI WIL 帧等价，也未证它复现 EI 的128/256模式、标记色和裁剪公式。 | 以 `MapInfo.FileName` 与 `MapInfo.MiniMap` 为业务输入，逐地图对照 EI 的 `MiniMap.txt`/`map_bindings.json`，再对照独立解码的 MMap/FMMap 帧与 WIL/WIX 库选择；同时核 128/256 模式目标矩形、player/object markers。验证 T、地图内控件、切图及裁剪/坐标更新，不把 MapInfo.Index 当资源帧。保留 EI-310 标注的行走期间 live coords 写入未决项；扩展 BigMap 的左键事件冲突另见 MAP-04。 |
| MAP-02 | 高 | Godot 独立 `BigMapDialog` 在 EI 该构建中没有对应窗口；键盘 B 目标也与 EI 主按键表冲突 | `map-ui-resource-evidence.json` 将 map object 唯一构造点归到主世界 `main+0x6214`，明确“no separate map dialog”；F316 primary-static将原版B(0x42)分支闭合为切换`main+0x6208`，没有独立map dialog调用；该状态对象的完整可见语义仍未闭合。T(0x54)才在地图打开时翻转`main+0x64A8`并直接重建128/256 surface。当前 `KeyBindManager` 将 B 绑定 `MapBigWindow` 并开关独立 `BigMapDialog`；`GameScene.CreateHud()` 始终创建独立大地图，另有现代图层、NPC 定位、传送/寻路输入。此处只可判定对象身份及键位不符；扩展大图业务中哪些可映射到 EI 的 256 模式尚未闭合。BigMap内左键单击传送/双击寻路共用同一Image的派发可达性候选见MAP-04。 | 将原版 256 surface 模式和当前独立 BigMapDialog 的地图浏览、传送、NPC 寻路分别对照业务链；确定 legacy 模式的 B 与 T 分派及鼠标显示控件，避免以“功能看起来都是大地图”代替身份/行为映射。现代模式与 legacy 模式分别做键盘和鼠标交互验收。 |
| MAP-03 | 高 | HUD 小地图按钮、V 键与 Godot 小地图窗显隐/透明度状态不是同一套业务状态 | fresh primary-static `hud-caption-action-tail-evidence.json`：HUD cap1 处理器为 `0x42C259`；调用 GetTickCount，计算 `now-[winmgr+0x6210]`，差值 `<=3000ms` 时直接返回；之后读取 `[winmgr+0x6518]`，为0时调用 `0x451770`（构造 opcode `0x409` 地图查询），非0时将该字段清零。此处理器没有直接 toggle 独立窗口的证据。`hotkey-label-handler-consistency.json` 记录 V 键复用 `+0x6210/+0x6518` 状态但门控为100ms；两入口冷却不同。当前 `GameScene.cs` 的 HUD 回调只翻转 `_miniMap.Visible`；V 对应 `HandleKeyBind(MapMiniWindow)` 则进行“不透明显示→半透明→隐藏”循环。对照 `LibraryCore/Network/ClientPackets.cs`、`ServerPackets.cs`、`ServerLibrary/Envir/SConnection.cs` 及 Godot 搜索，没有找到可映射到旧协议 0x409 的 Zircon packet/handler；现代 minimap 数据通过登录/地图/对象同步供本地绘制。因此不能伪造 0x409 包，也不能把本地可见性直接当作原版 `[+0x6518]`。旧证据尚未闭合 `[+0x6210]` 的写入点、0x409 的服务端应答/状态改变效果及当前分支 EI runtime 行为。 F316还闭合EI V(0x56)为地图子窗显示/隐藏入口，和HUD cap1共用`+0x6518`状态但冷却门不同；T(0x54)才是已证surface尺寸切换。当前Godot V被实现为“显示→半透明→隐藏”三态、HUD cap1直接翻转`Visible`、B打开额外`BigMapDialog`，地图图片左键在GM下发`SendTeleportRing`，均不等同EI T的surface切换或辅助矩形点击语义；这些差异按各自源代码记录，运行时协议效果仍待验。| 分别记录 HUD cap1、V、Ctrl+V 与菜单入口的原版业务链；继续追 `[+0x6210]` 写点和 0x409 收发/状态读取，注明无法由现代协议复现的部分。Godot 在协议映射证据闭合前不发送臆造包；legacy运行时需分别验证入口对显隐、透明度及地图状态的影响，`MAP-01/02` 负责资源/控件/大图行为验收。 |
| MAP-04 | 高，Godot独立大地图两个左键动作的派发/可达性冲突候选；未运行验证 | 当前 `BigMapDialog` 将 `Image.MouseDoubleClick` 绑定 `SendAutoPathWaypoint()`；同一 Image 的 `GuiInput` 在左键抬起、未超过6px拖动时调用 `SendTeleportRing()` 并 `Visible=false`。`DXControl._GuiInput()` 在单击抬起触发 `MouseClick`，第二次按下识别 `DoubleClick` 后会抑制该次 release 的普通 `MouseClick` 并触发 `MouseDoubleClick`。因此首次单击已经请求传送并隐藏大地图时，随后第二次按下是否还能命中来触发寻路取决于Godot输入命中/可见性时序；源码没有证明用户一次双击会稳定到达 waypoint 分支。该对话框本身是EI静态证据中不存在的Zircon扩展，不能反过来据此判EI行为。 | 用隔离客户端/无网络mock记录完整鼠标事件顺序及发包桩，分别验单击、双击与拖拽；在可安全隔离传送副作用后再决定单击与双击的互斥/动作规则。未取得安全隔离前不对运行期做鼠标回放，也不把代码存在双击handler记作双击可用。 |

| QUEST-01 | 阻断，高度已证实 | 根窗口与 F700 有效像素锚点相符；列表、详情和两组操作控件几何/资源均与 EI 不符 | 原始 WIL 由 `localhost:8766/api/image` 直接导出：F700 512×512、offset(+7,-44)，独立alpha bbox为`340×439+(86,36)`；归档原帧见[GameInter F700 PNG](evidence/legacy-ei-ui/gameinter-frame-700-wil-2026-09-24.png)。实际可见书页带`QUEST / ONE QUEST`标签和右侧X/箭头图；F705 204×76（预览 408×152）；F721/722、F723/724 各为 28×28 控件帧对。primary-static `quest-window-paint-full-evidence.json` (F671) 与 `quest-window-render-detail-evidence.json` (F537) 定案最多 19 行、字段 stride 0x104、200/160 px 文本路径门、active/normal 字色 `0x1919C8/0x19197D`、15 px 行距。这里有一项坐标资料冲突：F89先记录完整基线`(win.x+65, win.y+90+15*row)`；F251又沿`0x45DD70`的实参/子矩形偏移展开为`x=win.x+65`、`y=win.y+90+15*(row-scroll)+out[1]`。相对地，F537/F671摘要将同一`0x447618`记作`row*15+18`或`line*3+0x12`。F89与F251相互支持“90px起点+15px步距”，且F251提供callsite实参推导，故审计采用其作重建候选；但研究目标EXE原件不可用，本轮不能独立字节重放，较短F537/F671摘要仍列为资料内部冲突，不能标成已消解。详情为 F705 204×76、根相对 `(65,294)`、3 行/15 px 深蓝正文，服务器 `/` 分隔符就地拆行（不做客户端像素换行）；滚动状态 `[+0x58]/[+0x5C]/[+0x60]`，滚轮 `0x448700`、点击 `0x448780`。原版控件根相对位置分别：F723/724 `(290,59)`、F721/722 `(290,89)`；X 只本地消费并送音效 cmd 0x69（业务名“关闭”仍为 candidate），箭头经 0x448580 送 msg 0x418，具体业务名 candidate。当前 Godot F700 根 340×440 与背景 offset `(-86,-36)` 锚点相符，但 CloseButton 使用 F161/162 且放 `(304,404)`；两个原版控件未映射。任务正文首行当前根相对 `(26,63)`、原版 `(65,90)`，行距约 22 px，额外生成分组/子任务行且没有原版 19 行绘制上限；当前详情控件从约 `(398,63)` 开始、尺寸 300×405，没有绘制 F705；scroll 位于 `(704,58)`、18×415、step30，超出 340 px 窗宽。更正当前运行路径：`--legacy-ui`由`ApplyLegacyCoreTestLayouts()`对Quest根设`Clip=true`，`QuestDialog.ApplyLegacyEiLayout()`不重置；`ClipContents`裁其后代。340px宽根窗内，`_detailPanel`整块起于x=380（宽300），滚动条起于x=704，故右侧详情容器与滚动条均完全位于根外，在该测试布局中不可见；此前只称“超出窗宽”低估了实际结果。这个 Godot 源码裁剪事实不证明 EI 同位置控件也按根窗裁切，且不能据绘制clip推断外部子控件 hit-test；仍须目标同态画面及独立输入路径核验。 | 依 F671/F833/F923/F942 与 UI_COVERAGE_MATRIX 坐标表重建独立控件树/矩形；核 F721/722 与 F723/724 状态及真实命中边界、msg 0x418 和音效 cmd 0x69；用 800×600 坐标合成与 `bash login_game.sh legacy` 真实截图核 F700/F705、文字绘制、滚轮/箭头、任务点击及窗口裁剪。 |
| QUEST-02 | 高，待核 | 当前把现代 QuestLog 的多个页面、任务详情和里程碑交互放进旧版 F700，但 EI 页签/控制的身份及业务流程尚未逐控件闭合 | 原版窗口分派证据为 Ctrl+D→id11；静态构造/paint 记录有 F721/722、F723/724 两组状态帧控件。当前建 3 个透明 tab（页码 0/1/3），分别连到进行中/可接/里程碑；`RefreshPage()`虽有`_page==2`筛已完成任务的分支，但本类只在这三个固定tab回调中写`_page`，未发现赋值为2或其它进入点，因此已完成页分支当前不可达。可达页面仍提供多列任务详情、奖励格、追踪开关和 accept/complete/abandon/map link；这些具体操作不能由单一任务文本绘制链证明属于 EI。`Client/Scenes/Views/QuestDialog.cs` 是较新的通用客户端源码（任务/可接/已完成/里程碑/任务页），只能解释代码来源，不是目标 EI 的行为证据。 | 追踪 F721..F724 的原版 hit/click 分支、其隐藏/显现条件、selection flag 与服务端消息；逐项决定 legacy 要呈现哪些原版态，再以无任务、进行中、完成/奖励三种服务端状态做截图与输入对照。 |
| QUEST-03 | 高，primary-static/当前源码冲突 | EI 右上 F721/722 X 区域只消费点击并播放 cmd `0x69`，不会由该按钮直接关闭窗口；当前 Godot X 按钮会关窗，且关闭路径无条件发送 `MilestoneNotify(false)` | `quest-window-input-evidence.json` F942 与 `UI_COVERAGE_MATRIX.md`/`UI_COMPLETION_AUDIT.md` 的最终归因：输入函数 `0x448430` 对两个按钮做通用 hit/click；F721/722 的 click 落入 `0x4177F0`，只调用 `0x45AFC0(...,0x69,...)` 音效、无窗口消息；箭头 F723/724 才进入 `0x448580` 并发送 `0x418`。当前 `QuestDialog` 的 close 按钮绑定 `WindowManager.Close(this)`；覆写 `Close()` 无论当前页都会调用 `SendMilestoneNotify(false)`；页签从里程碑页切出时也会发送 false。服务端 `Process(C.MilestoneNotify)` 对 false 只写 `Player.ReceiveMilestoneUpdates=false`，仅 true 分支回传 `S.UserMilestones`；仓库内该字段除声明/赋值外没有读取点。因此已证实普通任务页关闭也会多发一次 false/重写状态，但尚无证据证明这会改变业务结果，不能称为已确认的可见故障。原版 X 区域不直接等同于 Godot“关闭+通知”动作；EI 窗口实际消失依赖窗口管理/热键另一路，是否点击后仍保持显示要在原版运行态确认。 | 真实 legacy 窗口分别在任务页/里程碑页点 F721/722 中心与边缘，记录窗口可见状态、发包与声音；另验证 Ctrl+D 关闭路径以及里程碑进入/退出通知时机。不得把按钮外观或 cmd `0x69` 音效直接命名成关闭业务。 |
| QUEST-04 | 中，Godot 任务窗关闭会发送里程碑通知包；业务效果未证 | EI 已取得的任务窗 primary-static 路径是 id11/F700 文本列表、F721/722/723/724 子控件，没有证据把“普通任务窗关闭”映射到 Zircon `MilestoneNotify` 协议。当前 `QuestDialog` 的 X 经 `WindowManager.Close(this)`，Ctrl+D 经 `WindowManager.Toggle()`；已核 `Toggle()` 在可见时调用 `Close()`。override `Close()` 不检查 `_page`、可见态或订阅状态，始终调用 `SendMilestoneNotify(false)`。仅里程碑页的 `RefreshPage()` 会发送 `true`，离开 page3 的页签回调还会再发一次 `false`。source-confirmed `ServerLibrary/Envir/SConnection.cs::Process(C.MilestoneNotify)` 无条件赋值 `Player.ReceiveMilestoneUpdates = p.Receive`；为 true 时另回发 `S.UserMilestones`。仓库内该字段目前除声明与赋值外没有读取点，因此可确认普通页关闭会发送 false 包并覆盖字段值，但不能证明该赋值影响后续推送或列表业务；退离里程碑页存在重复 false 调用路径，其可观察副作用未证。上述均为当前 Godot 源码行为，不能当作 EI 窗口行为。 | 若保留此扩展，先查明 `ReceiveMilestoneUpdates` 的生产者/消费者与所需协议生命周期，再按明确订阅状态处理通知；EI 对应关系未获证据前标为 Zircon 扩展。业务链查清后再决定运行期验证方案。 |
| QUEST-05 | 高，Godot源码确认；legacy 的透明现代页签仍拦截点击 | `QuestDialog.ApplyLegacyEiLayout()` 将 `_tabs` 中三个现代页签的 `Modulate` 设成 alpha=0，并将 y 统一设为30，但不设 `Visible=false`、`Enabled=false` 或 `PassThrough=true`。`DXControl.UpdateMouseFilter()` 对 enabled、`IsControl=true`、非穿透控件仍设置 `MouseFilter.Stop`；`AddTab()` 保留点击回调，会改变 `_page`、更新样式并调用 `RefreshPage()`。因此当前 340×440 legacy F700 皮肤上的透明 tab hit 区依旧活动，会覆盖书页原生装饰/控件下的鼠标输入并切换到现代任务/可接/里程碑内容。该输入行为来自 Godot 源码，静态可证；不代表 EI 在相同位置有三个页签。EI primary-static `quest-window-render-evidence.json` 只确认 id11/F700文本列表渲染链，原版按钮则由 `RESEARCH_LOG.md` F942/`0x448430` 与 `0x448580` 分支确认：F721/722 消费并发 cmd `0x69`，F723/724 才发送 msg `0x418`。两条链没有将透明 tab 识别为 EI 控件的依据。 | 先按 F700 素材和 EI 子控件RECT完整恢复 legacy 控件树，再决定 modern 扩展如何在 legacy 皮肤下呈现；任何保留的扩展页签须移出原版 hit 区，或在 EI 不存在的态下显式禁用/穿透。以Godot输入树/命中框静态核查并在安全测试场做点击回放；不得把当前透明 tab 的可点击行为认作EI翻页。 |
| QUEST-06 | 高，列表项鼠标动作/data binding 不等价（当前源码与 EI primary-static 对照） | EI `RESEARCH_LOG.md` F942/`0x448490` 按原版动态记录矩形`record+0x218`命中后写选择索引`+0x1DC`，再进入`0x448580`状态/激活分支；同窗内的F723/724箭头另发0x418，子项另有0x419分支，F721/722只消费并发音效0x69。当前`QuestDialog.RefreshPage()`给普通任务标题建立动态`DXLabel`；`AddLine()`初始`IsControl=false`，但活动/可接页标题回调随后将`MouseFilter=Stop`，所以标题实际可接GUI输入。进行中任务左键选择并调用`SendQuestTrack(index,true)`，已完成分支会打开选择奖励窗或调用`SendQuestComplete()`；右键未完成项进入Zircon放弃确认；可接任务标题只设置`_selectedAvailable`并刷新右侧详情；`SendQuestAccept()`仅由详情内action按钮调用。legacy根启用Clip，右详情panel根位置x=398、接受按钮再偏移x=194，完全超出340px根宽，所以该profile里可接任务的最终接受动作也不可见/不可达（几何见QUEST-01），不能把源码存在回调记为legacy流程可用。当前主标题根相对位置由content `(18,58)`加line局部`(18,y)`得x=36，原版文本起点候选x=65；具体 EI hit RECT 尺寸尚未从动态记录构造链取得，不能把原版文本起点代替点击框。 | 逐项恢复 EI 记录RECT的构造/写入与 `0x448490` 最终PtInRect调用，查明record选择旗标、`0x418/0x419` 对应的服务器事件；当前版本另查QuestTrack/Complete/Abandon请求各自业务来源。先把几何与包语义分别对照，再设计legacy任务行事件，不以“任务标题能点击”认作原版列表交互完成。
| QUEST-07 | 高，完成任务标题的单击会直接触发领取路径；EI激活语义未闭合 | 当前`QuestDialog.RefreshPage()`为已完成任务标题绑定左键：先设`_selectedQuest`并刷新详情，然后如果有选择奖励立即打开`QuestRewardChoiceDialog`；没有选择奖励则当场调用`SendQuestComplete(questIndex)`。`RefreshDetail()`另创建独立action按钮，已完成项点击该按钮也会调用同一`SendQuestComplete()`。但`ApplyLegacyEiLayout()`将整个详情面板定位于根外，QUEST-01已确认legacy根clip使该action不可见/不可达；因此legacy完成项当前主要通过列表标题点击直接触发领取请求，而该点击同时充当selection和operation。EI primary-static F942/0x448490证明动态行Rect命中后写选择索引并进入`0x448580`的状态/激活分支，`0x419`存在子项分支；当前资料未将“已完成任务标题命中”精确映射到服务端领取动作，不能据此认定直接领取与EI相同或相反。 | 只读路径核对：`RefreshPage()`标题`GuiInput`及`RefreshDetail()`完成按钮；对照`ApplyLegacyEiLayout()`根裁剪和 QUEST-01 几何。后续先闭合 EI 行点击的selection/activation条件与`0x419`消息业务，再决定列表单击是否只选中、打开奖励选择或直接领奖；设计 legacy 按钮时避免因详情不可见导致主任务动作只能隐式绑定到标题。涉及真实领奖的运行验证须使用可重置/安全任务数据并留状态前后证据；本轮未发送任务请求。 |


**里程碑通知静态链补核（源码确认，非EI证据）：**可用的较新 `Client/Scenes/Views/QuestDialog.cs` 将 `MilestoneNotify` 绑定到 `MilestoneTab.OnIsVisibleChanged(IsVisible)`；Godot 当前则在 `RefreshPage()` 进入页码3时发送 `true`，离开页码3时发送 `false`，并在 `Close()` 无条件再发送 `false`。当前 Zircon 服务端对 `true` 会回发 `S.UserMilestones`，`false` 只写 `Player.ReceiveMilestoneUpdates`；在仓库源码中该字段除声明和赋值外无其它读取。因此可确认普通页关闭会发 false 包并覆盖字段值，但静态服务端源码未显示该字段目前被读取，也不能证明它改变列表或后续推送。`WindowManager.Toggle()` 可见时调用 `Close()`，故 X 与 Ctrl+D 入口都会触发该 override。此链只解释 Zircon/较新 Client 实现，不能用来推定 EI 3.0 的 F721/722 语义。
| HRS-01 | 高，键位代码已修；EI 实际交互待验 | EI 原版 `S` 与 `Ctrl+S` 都是坐骑窗口入口；原实现仅 `Ctrl+S` 打开坐骑，裸 `S` 在现代键位表打开 StorageWindow。现已在 `--legacy-ui` 模式下将无修饰裸 `S` 接到 `ToggleHorseWindow()`；Ctrl+S 原分支保留，两者先于普通键位表分派。现代模式的裸 `S` 仍保持仓库动作。先前本项称 HUD idx13 映射错误是审计文档陈旧：`GameScene.cs` 已在 `LegacyHud` 路径将 `CharacterButton` 接到 `ToggleHorseWindow()`，与 EI cap13 入口相符。 | `window-paint-and-hotkey-dispatch-evidence.json` 的 `0x42CC76` 将虚拟键 S 分派到 `0x42ADB0(id13)`；`hotkey-label-handler-consistency.json` / `chat-window-control-map.json` 将 caption 记录为“坐骑(Ctrl+S, S)”，并记录分支调用 `GetKeyState(0x53 'S')` 后 `test ah,ah`。`GameScene._Input()` 和 HUD cap13 回调现均指向 `ToggleHorseWindow()`；代码路径已核对，仍需真实游戏输入验收。 | 编译后用 `bash login_game.sh legacy` 登录实测裸 S 与 Ctrl+S 均只切换坐骑窗；焦点在聊天、文本输入框或模态窗口时核门控；现代启动裸 S 保持 StorageWindow。HUD cap13 已经有源码目标证据，须在真实 EI 对照态实测它能打开 F850 坐骑窗，不能重复列为待修映射。继续检查其他字母分支是否也只测自身键状态。 |
| HRS-02 | 高，四个动作按钮的当前业务请求与目标命令不匹配已由源码确认；原版三种命令的逐态映射仍待闭合 | EI primary-static `horse-window-render-evidence.json` / F327、F545：`0x426A80` 按 `this+0x108/+0x1BC/+0x270/+0x324` 的 hit/state 分支，将 `@上马/@遛马/@收马` 交 `0x4520F0` 发送；Frame 860–867 的标签艺术与命令语义分开。当前 `HorseDialog.Action()` 对四按钮一律 `SendChat()`，发送同样字符串成为 `C.Chat`；服务端 `PlayerObject.Chat()` 将 `@` 消息转给 `SEnvir.CommandHandler`，处理器只注册 Player/Admin 命令，本仓库没有上述三个命令，`ErrorHandlingCommandHandler` 会回系统消息“Command @… does not exist.”，因此这些按钮当前不会执行骑乘/遛马/收马。可用旧版 `Client` 源码中只找到 `KeyBindAction.MountToggle → C.Mount` 的全局上/下马处理；`HorseTameDialog` 是套索驯服小游戏，不是 EI F850 坐骑管理窗，也没有证据实现 hide/show 四按钮。当前协议另有 `C.Mount`→`SConnection.Process(C.Mount)`→`Player.Mount()`，可切换上下马并校验死亡、账号马匹与地图许可；它不能仅凭同为马相关动作就等同覆盖 EI 的 `@遛马` 或所有四个按钮分支。`HorseDialog.SetMountState()` 从 `S.ObjectMount.Horse` 只折成0/1；足以对应当前已知 `==0/!=0` 门，但不表达 EI 的非零子态1/2/3。F850几何与四个控件帧/坐标静态相合，动作链不相合。 | 先逐项确定四个EI hit/state分支对应的服务端动作及其状态回包，再设计当前协议/服务端实现；不能把四个按钮都继续发未注册聊天命令，也不能未确认就把四者都映射到 toggle `C.Mount`。HRS-01 S/Ctrl+S 和按钮业务输入按用户要求不重放；后续仅在可隔离角色/明确动作后验状态门、回包、动画和窗口状态。F850关闭按钮有效命中及不同缩放另需实屏核验。 |
| HRS-03 | 高，锚点错位已按素材/根窗/控件三方几何修复，窗口级输入仍待验 | F850 为512×512画布，WIL头部 offset=(7,-44)，直接RGBA alpha bbox为宽高和原点表示 `(275,323)+ (118,94)`；之前将这四个数误读为右/下端点。原版研究记录给出窗口根296×332，四个动作按钮位置分别 `(28,244)/(74,244)/(133,244)/(192,244)`，关闭框 `(252,293,28,26)`。这四项动作字已经烘焙在F850像素区：源图canvas约 `(136,337)` 起；扣除alpha原点 `(118,94)` 后落到根相对约 `(18,243)`，与子按钮的 y=244、x范围相互覆盖。故把背景画布定位到 `(-118,-94)` 可使有效区落在根 `(0,0)`，根`Clip=true`限制到296×332；无需把 WIL 头部 offset=(7,-44) 当作alpha边界。`HorseDialog.cs` 已按此改背景锚点并增加根裁切。目标EXE身份差异仍见本文开头，机器码未在目标原始文件上独立重放。

**修复后实屏（2026-09-24，Xvfb :100、1024×768）：**重新执行 `bash login_game.sh legacy`，测试账号成功登录并进入地图3，日志 `missingLibraries=0, missingTextures=0, emptyImageEntries=0`。鼠标从cap13无重叠区域 `(764,706)` 打开HorseDialog，修复后截图[`horse-window-aligned-2026-09-24.png`](evidence/legacy-ei-ui/horse-window-aligned-2026-09-24.png)中F850木框从客户端左上 `(0,0)` 开始，子动作行与F850烘焙动作文字重叠，位于根裁切范围内；修复前图见[`horse-window-open-2026-09-24.png`](evidence/legacy-ei-ui/horse-window-open-2026-09-24.png)，显示背景右下偏移、动作行在框外。`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 通过（0 errors、3条既有warning）。本轮没有点击会发送骑乘聊天命令的四个业务按钮，因此仅验视觉锚点/裁切，不验状态门、协议动作、关闭控件覆盖及不同缩放。

行会窗、聊天窗、设置控件语义与 NPC 关闭/滚动控件的位置差异已由原版静态证据与当前源码交叉确认。公告资料中“独立滚动横幅”“F601 隐藏状态”和 `0x7EE` 触发 id15 的旧摘要已按 F294 修正；`0x777200` 是 id15 窗口自身，F601/F602 是两个内容状态。F602 文本/提交差异见 NOTICE-01，激活触发源仍需运行时证据。NPC 动态背景条、正文基点和行距来自不同绘制路径；当前实现把它们合并布局，不能用一个“NPC 行距”值概括。原版 frame id11 确有独立列表窗；此前“其点击发送0x419”的说法已按 Finding 321 撤销，frame id11 与 F1100 同屏关系及真实点击协议仍未闭合。交易窗的“外溢”属于原版窗口数据模型的明确行为；Godot Control 根裁切可能改变其可见/可点区域，必须结合 `DXWindow`/CanvasItem clipping 与真实 800×600 画面对照后再给最终实现判定。F1001 的独立窗归属仍需运行时/协议层补证。此阶段只登记审计结论，不在证据矩阵闭合前直接改窗口实现。

## 窗口覆盖清单（第一轮范围盘点）

以下是审计对象，不代表已审完。标记“待逐项”即尚未核查其所有控件和运行行为。

| 区域 | 当前实现/证据入口 | 第一轮状态 | 必须补查 |
|---|---|---|---|
| 登录、服务器/角色选择、创建角色、进入游戏 | 原版 `login-flow-*`、`login-charselect-flow-evidence.json`；Godot `Scripts/LoginScene.cs`、`SelectScene.cs` | primary-static 状态机/消息/部分 hit rect 已闭合；Godot 有空帧引用、选角根与入口坐标不对应、角色槽门限不符、缺 EI 服务器列表阶段、选角状态/键盘差异（PRE-01..18）；Interface1c 的 WIL fallback 已有单帧/解析器验证（RES-01），但完整运行流程仍未验收；曾验证测试账号自动进入游戏，但未真实操作原版式登录页/选角页 | 对照 EI 同态登录/选角截图，闭合未映射的表单底图、视频与 Interface1c 帧合成顺序、各控件根RECT；逐项核焦点、Tab/Enter、账号字段、服务器列表/切换、错误提示、创建/删除/返回/进入及缩放 hit rect，并留可观察截图 |
| 常驻主 HUD、属性条、底部功能按钮、状态/快捷栏 | `hud-*`、`primary-main-hud-setrect.md`、`LegacyHudLayoutLab`、`MainPanel` | 原版静态资料丰富；Godot 端待逐项验收 | 16 个 caption/按钮动作对应、绘制次序、缩放锚定、真实状态值、鼠标悬停/按下、所有热键入口 |
| 人物状态/装备 | `status-window-render-evidence.json`、`equipment-slots-evidence.json`、当前 `CharacterDialog.cs` | 部分静态链已确认；F200/F201 单背景切换布局已在 HEAD（`7ecb1b02`），瞬态/完整属性页仍待验 | F200/F201 原点与展开切换抖动、11 记录、纸娃娃、8 装备格、右侧属性页内容、装备交互/数字绑定 |
| 背包/物品格 | `inventory-window-render-evidence.json`、`item-tooltip-and-store-family-evidence.json`、当前 `InventoryDialog.cs`/`DXItemCell.cs` | 格槽/物品映射未闭合（INV-01..05）；EI 格子悬停提示与当前全局提示差异新增 ITEMTIP-01 | 6×6命中/跨格占位、四模式、数量/重量、拖放/右键/锁定、46记录绑定、容器区分的提示触发和样式 |
| 技能书与技能快捷栏 | 上文所列 `skill-window-*`、`magic-exp-*`、`skill-grid-*` | 已确认存在重大实现差异 | 原版分类/列表命中矩形、额外三控件、职业列表、图标绑定、右页文本、绑定和施放键、分页/滚动/关闭 |
| 任务、聊天、组队、行会、交易、仓库、坐骑 | 对应 `quest-window-*`、`chat-window-*`、`group-window-*`、`guild-window-*`、`horse-window-*` 和控件类 | 任务列表/详情布局与组队列映射有 primary-static 几何差异；聊天、行会、设置和 NPC 多项差异已证实，其他仍需逐控件闭合 | 背景有效像素、子控件命中、服务端数据、二级页/空状态、真实点击与键盘行为；F1001 独立仓库归属待解 |
| NPC、商店、物品数量/确认/公告提示 | `npc-window-render-evidence.json`、`store-window-render-evidence.json`、`confirmation-prompt-evidence.json`、`notice-prompt-window-evidence.json`、`notice-banner-lifecycle-evidence.json`；`NPCDialog`/`NPCGoodsPanel`/`ItemAmountDialog`/`NoticeDialog` 等 | NPC 关闭与滚动控件几何错误、图条/对白布局不符、商店根窗归属/几何不符、F602 文本/编辑/动作差异已证实；id15 身份和静态显隐生命周期已闭合，实际激活事件与 Zircon 服务端事件映射待补；NPC option-list 关系、商店状态1/2业务名待补；真实流程未验。`GameStoreDialog` 是现金商城，不等同 NPC store | 补齐 NPC 双对象/消息链；商店状态帧、商品命中和购买/出售/修理/存取绑定；确认/取消消息与键盘链；核公告类聊天事件对应的 EI 实际入口，复核 F601/F602 切换、编辑提交和运行期文本；使用旧版素材与 `bash login_game.sh legacy` 逐项交互留图 |
| 设置/主菜单/退出/帮助/消息窗 | `system-window-render-evidence.json`、`confirmation-prompt-evidence.json`、`hud-caption-action-tail-evidence.json`；`ConfigDialog`、`MenuDialog`、`ExitDialog`、`HelpDialog` | F750 的四行控件位置/状态已经接入；BGM/EffectSound 有对应音量与状态路由，Ambience 保存副作用、ShadowBlend无已知 EI 外部消费者、两音量实际声学曲线及根窗末9px裁切仍待验（SET-01..07）。注销 idx4 的 F950 type 0x65 路径已闭合；退出 idx3 的 F800/id64 韩文 YES/NO 确认窗语义已由 WIL 视觉闭合，hit rect 与最终动作待核；Godot 当前两入口和 Alt+Q/Alt+X 合并，见 EXIT-01 | 设置两音量滑块/互斥开关、配置读写和有效像素裁剪；追 F800 按钮 hit rect/最终动作；分别验收 Alt+Q、Alt+X、点击两 HUD 入口、取消/确认、层级与焦点恢复 |
| 小地图/大地图/任务追踪/状态提示 | `minimap*`、`notice-*`；对应 Godot 控件 | EI 小地图默认目标/帧选、T 与鼠标切换 128/256 surface、没有独立大地图窗均有 primary-static；Godot 对应模式/对象不同；公告窗差异见 NOTICE 项；仍待真实屏幕验收 | 800×600 下 128/256 模式目标矩形、资源帧/标记颜色、坐标转换/裁剪、EI T 与 B 分派、BigMap 扩展行为边界、地图切换生命周期 |
| 其余 Zircon 业务窗（本轮按真实创建/打开入口拆分，详见下表） | `GameScene.cs` 构造与公开入口、各控件实现；EI 固定目录来自 `window-id-catalog.json` | 固定目录只列16个常规窗口槽，不能据此排除模态/子状态。已确认若干入口受功能开关或现代协议控制；其余为候选扩展或依附EI节点的子窗，尚无逐窗EI证据映射 | 逐类对照EI消息/对象/资源证据；标清 legacy 模式入口、对应EI节点/子状态或扩展属性；逐控件布局及导航仍待审 |

### 补充窗口入口清册（2026-09-24，静态范围审计）

此清册只证明当前 Godot 中对象的创建/调用路径；“EI映射候选”不代表原版一定存在同名独立窗口。EI 固定16槽目录之外，动态对话框、HUD叠层、窗口内部状态仍须分别查原版调用链。当前可直接确认的功能开关/现代协议入口标为“项目功能候选”；没有足够证据的先保持待映射，不因缺固定ID就判为扩展。

| Godot控件 | 当前创建/显隐路径（静态） | EI侧归类及状态 |
|---|---|---|
| `AutoPotionDialog` | GameScene常驻创建；`AutoPotionBox` 对外暴露 | 自动补药是否EI功能、对应原版输入/配置路径待映射 |
| `BuffDialog` | 常驻创建；`BuffsChanged`依内容更新/显示 | HUD状态效果叠层候选；需核原版buff绘制和锚点，非已确认独立窗口 |
| `QuestTrackerDialog` | 常驻创建；按`ClientSettings.QuestTrackerVisible`显示 | 任务追踪HUD候选；需区分EI任务窗与常驻追踪文本 |
| `TimerDialog` | 常驻创建，默认位置(20,100) | 当前独立计时控件；EI对应物/触发来源未查 |
| `CurrencyDialog` | `ToggleCurrencyWindow()`切换并刷新货币 | 现代货币子系统候选；需核EI货币标签/拾取状态是否属于Inventory节点 |
| `BundleDialog` | 常驻创建；服务器包裹响应调用`Open(slot, items)`，关闭回调隐藏 | 地面/包裹节点候选；需对照EI物品窗口/掉落交互与占位状态 |
| `DungeonFinderDialog` | 热键分支仅在`DungeonEnabled`时toggle；当前常量为false | 地图/副本查找项目功能候选；现配置下入口关闭，查EI对应热键/地图节点 |
| `CaptionDialog` | `OpenCaptionDialog()`显式打开 | 文本说明弹窗候选；对应EI提示/公告状态未映射 |
| `EditCharacterDialog` | `OpenEditCharacterDialog()`重置后打开 | 当前角色属性重分配/编辑流程候选；须对照EI Status窗内部动作及消息 |
| `CompanionDialog` | `OpenCompanionDialog()`受`CompanionEnabled`门控 | 伙伴功能候选；核EI宠物/召唤兽窗口是否相同对象、协议与格位 |
| `GuildMemberDialog` | 行会成员选择回调填充后打开 | Guild节点子对话框候选；要追EI行会成员操作的目标/权限状态 |
| `MilestoneDialog` | 常驻创建；里程碑状态回包调用`ShowMilestone()` | 里程碑/成就功能候选；不能与普通Quest列表混同，查EI任务/提示节点 |
| `MarketHistoryDialog` | `OpenMarketHistory(item)`传入物品后显示 | 市场价格历史扩展候选；检查legacy开关及物品窗口遮挡 |
| `GameStoreDialog` / `ConsignmentDialog` | 打开方法分别受`GameStoreEnabled`/`ConsignmentEnabled`控制；两常量当前为false | 独立商城/寄售项目功能候选；EI固定目录无对应同名根窗，仍须排查是否映射某原版节点或属于纯扩展 |
| `FishingDialog` / `FishingCatchDialog` | `OpenFishingDialog()`开钓鱼窗；`StartFishing()`另发cast；钓获状态包更新catch窗 | EI钓鱼状态与投竿/收获路径待比对；运行期投竿按本轮限制不测 |
| `MonsterDialog` | 每帧由`CombatController.MouseObject`路由；怪物悬停时显示，坐标由`LayoutHud()`置屏幕上方中央 | EI悬停名牌/选中目标框的world-space绘制链已有primary-static证据，但不是当前顶部属性窗的直接等价证据；详见MON-01 |
| `NPCQuestListDialog` / `NPCQuestDialog` | `OpenNPCQuestList(objectId)`、`OpenNPCQuestDialog(quest)`沿NPC链打开并相邻定位 | NPC任务列表/详情为NPC节点子窗候选；已知当前QUEST-01/06有布局/点击差异，不因此宣称EI节点映射闭合 |
| `NPCSocketDialog` / `NPCSocketCombineDialog` | 两个Open方法互斥显示；背包物品路由到当前可见面板 | NPC镶嵌/合成子流程候选；需对照EI NPC消息和物品拖放路径，运行期业务暂缓 |
| 其余在本表未列出的创建对象 | 需继续用全仓引用、消息回调与显隐控制逐项核查 | 只作待审计登记；后续补具体类与入口，不以构造点判定用户可见或EI映射 |

**本轮静态裁定范围：**GameScene确有这些构造点；可见入口包括显式公开方法、服务器/状态回调、设置显隐和子窗互斥/物品路由。当前源码中如`OpenCompanionDialog()`有功能开关，`OpenGameStoreDialog()`、`OpenConsignmentDialog()`亦受modern feature gate；但本表不将后两者推展为上述任何EI功能的替代。接下来逐窗检查 `legacy-ui` 下的调用/门控、素材帧来源、根RECT/子控件RECT，再查 `window-id-catalog` 之外的原版动态节点。所有缺同版EXE/稳定运行输入的项留在阻塞表，不重复坐骑S/Ctrl+S、双人交易或已知可能触发Bad Request的路径。

### MON-01 怪物悬停信息面板的入口、素材与 EI 对应

| 核对项 | 证据与结论 |
|---|---|
| 当前入口/状态 | `GameScene`每帧读取`CombatController.MouseObject`；仅当对象类型为Monster时调用`MonsterDialog.SetMonster()`，后者直接设`Visible=true`并刷新等级、名称、HP/MaxHP及`MonsterInfo.Stats`。失去怪物悬停则设为隐藏。这是可达的实时overlay，不只是构造点。`LayoutHud()`将根放在屏幕顶端中央；收起根186×54，点展开后意图调整至186×175。 |
| 当前帧和命中矩形 | 顶层等级/名称/HP区由Godot边框、底色与标签构造；血量贴图请求GameInter F5430，元素图标请求F1510–1517；展开按键请求`LibraryFile.Interface` F46，展开后状态指向F44。速度/移动/可驯/不死/成长图标请求ProgUse F590/620/631/634/630。按`LibraryCore/Libraries.cs`与`MirSkin`实际legacy映射，GameInter ZL2/WIL各count=1103、ProgUse无ZL且WIL count=560、Interface库缺同名资源；上述帧均无法从对应legacy库获取。因`DXImageControl.Index`在`FixedSize=false`时用`MirSkin.GetSize()`重设尺寸，F46/F44按钮在缺失Interface库时尺寸为0×0，当前展开入口没有可见帧/有效矩形。根文字、HP数值及展开信息区的独立背景/命中RECT仍需逐项核测。 |
| 旧版Client证据 | 可用 `Client/Scenes/Views/MonsterDialog.cs` 有同名怪物信息窗口及等级/名称/生命/属性图标，是该Client版本的source-confirmed实现；它与Godot当前接口来源相同，但不是目标EI 3.0的primary-static或实屏证据。 |
| EI证据/边界 | `scene-entity-render-evidence.json`闭合怪物类型在世界中的per-entity tick/render链；更新的primary-static `nameplate-label-evidence.json`明确0x40CE20由`0x41C76C`场景循环和`0x41CCAF`每实体helper调用，覆盖玩家、怪物、NPC，绘制world-projected element `[e+0x62A24]+(state==0xF?0x355:0x352)`，受1700ms/状态位门控。另有`target-box-evidence.json`（F239）逐函数闭合 EI 目标组合：0x40BB00短暂悬停名签（3000ms），0x40B850文字测量/居中边框，0x40B750名字帧，0x40A8A0 HP条，0x437DF0悬停实体重绘，布局锚点为HUD+0xE4/+0xE8；固定目标路径锚点(376,227)，鼠标悬停路径可按世界坐标换算。F359直接从Mir3.exe字节核验悬停名签和名牌几何；F413/F414浏览器验证的是研究模拟器，不是原版游戏运行截图。F271又闭合目标框无头像、无独立WIL窗口帧。较早`npc-body-strip-evidence.json`残留“0x40CE20仅玩家”一句，与F296更新caller证据冲突，本审计以新证据为准并标旧文字过时。以上EI组件是世界空间名签/HP/目标选择反馈，不是当前Godot顶端186×54、带等级与属性详情的窗口。`scene-entity-render-evidence.json`中0x40B2C0五参调用的参数语义仍pending，但它不再使EI目标框整体身份未知。 |
| 结论与后续 | EI原版悬停/选中目标反馈已静态闭合为世界空间组合绘制；Godot当前`MonsterDialog`则是屏幕顶部中央、展示等级/名称/HP/属性并可展开的另一面板。两者结构和职责有明确差异，不能把EI名牌证据当作当前面板的原版证明，也不能仅凭目标框有名称/HP就判定应该保留顶部窗。继续核Godot鼠标对象来源、EI目标切换与该面板的实际产品意图；资源运行时绑定及同版EXE/画面身份仍保留限制。本轮不做怪物点击/业务运行测试。 |

### Legacy EI 图库命名空间完整性（2026-09-24 静态扫描）

`bash login_game.sh legacy`使`MirSkin.UiDataPath`指向`/home/tetsuya/mir3ei/LegacyEI/Data`。`LibraryCore/Libraries.cs`中图库映射仍按basename区分`Interface.Zl`、`Interface1c.Zl`、`GameInter.Zl`、`GameInter2.Zl`；`MirSkin`先取该库同名ZL，缺失时只回退同basename WIL/WIX，不会把`Interface`自动改用`Interface1c`或把`GameInter2`自动改用`GameInter`。

| 资源库 | legacy目录实际文件/独立计数 | 当前代码已发现的影响与状态 |
|---|---|---|
| `GameInter` | ZL2 count=1103；WIL/WIX count=1103 | 硬编码索引有效范围为0..1102。当前源代码仍引用多组高帧：ConfigControls 的 F4741/4743/4750、Bundle F3350、Fishing F4500/4501/4510、Help F9300/9310/9315、Monster F1510–1517/5430、HorseTame F7600–7631、NPC高级子窗 F4112/4117、Timer F6580/6590/6600、Character marriage F1298、NPC Socket F5700/5701及动画F5710–5730、F5740–5760、F5770–5779、F5800–5849；这些全部超出实际 legacy GameInter 两个格式的范围。但逐项状态核验发现 ConfigControls 的4741/4743/4750只属于现代配置页子树，`ApplyLegacyEiLayout()`会隐藏`_page`，不列入当前legacy设置可见缺图项。其余控件可见条件和正确EI映射必须逐项核，不能单凭构造引用判为持续可见。GameStore F4830–4872及伙伴/寄售等还受当前false feature gate限制。NPC Socket单列见NPC-07。 |
| `Interface` | `Interface.Zl`、`Interface.wil/.wix`均不存在；只有另一库`Interface1c.wil/.wix`（WIL count=2000） | `LibraryFile.Interface`的所有图帧请求都无法由当前legacy目录同名资源解析。代表性实例：默认`DXWindow` close、滚动条小按钮，以及NPC任务窗的F209/F212。不能从`Interface1c`同号帧自动替代；需先做EI库身份与帧语义对应表。NPC任务窗见NPC-06。 |
| `GameInter2` | `GameInter2.Zl`及`GameInter2.wil/.wix`均不存在 | `MagicBar`职业边框、`MagicDialog`经验条、`DXItemCell`锁定覆盖图和LootBox子窗的请求均无对应legacy图库。哪些是原版必要控件、哪些是后版扩展需分窗裁定；技能详情/常驻技能栏涉及部分列入SKL-01/06后续计划。 |
| `Interface1c` | 仅WIL/WIX，WIL count=2000；没有同名ZL | 可由legacy WIL fallback加载。F209=36×104人物形态片、F212无header；这是资源帧事实，不能仅凭同号推断它们是NPC任务面板背景。 |
| `ProgUse` | 无同名ZL；WIL/WIX count=560 | `MonsterDialog`扩展面板引用F590/620/630/631/634，均超出此库范围；若打开详情，这些速度/可驯/不死/成长图标无法从legacy WIL绘出。需核EI是否存在对应控件/图标，不能以`ProgUse`库的同号跨库替换。 |

以上计数交叉使用 `Mir3-Research/Tools/common/zlsdk.py` 与 `wilsdk.py` 读取本机ZL2/WIL，不从浏览器API的`blank`字段单独区分空帧和越界。扫描先发现`GameInter.Zl`存在，因此撤回前一轮“按WIL fallback才判断游戏运行时帧有效性”的前提；随后确认当前优先加载的ZL2 count同为1103，NPC Socket高帧结论仍成立，且同类超范围引用延伸至多个控件。资源可达性是源代码/资产静态结果；截图和交互验收尚未覆盖，故不将所有列出的引用都标成实际画面缺陷。

### 原版界面目录与导航关系（逐窗比对总表）

本表先固定 EI 的“界面节点”和“从哪里进入/离开”，避免把同一张素材、同一枚热键或现代同名窗口误认为同一个界面。原版固定窗口 ID、F号、构造/绘制/点击入口取自 primary-static `window-id-catalog.json`（Finding 268）；HUD 文字与点击目标优先取 `hud-caption-action-tail-evidence.json`（F321 fresh disassembly）、`hud-label-evidence.json`、`window-paint-and-hotkey-dispatch-evidence.json`。出现字段冲突时，以能对应具体构造调用和实际字符串 VA 的更新反汇编为准，并在相应 HUD 项记录被纠正的旧映射。内容列引用本审计的逐窗条目；标“待逐控件”意味着还没有完成按钮文字、状态帧、RECT、后续页面和动态文本的逐项转录。

| EI节点 | 原版界面身份 / 资源 | 进入边（触发源→目标） | 界面内容、可见文字与窗内动作审计 | 当前 Zircon 对应及首轮判定 |
|---|---|---|---|---|
| mode 0 | 登录/服务器列表 | 启动→登录表单→服务列表→连接过渡 | 账号/密码字段与 F11/12、F13/14、F15/16 点击链有primary-static记录；F17作用candidate；登录页贴图/AVI与Interface1c阶段合成、按键、服务器列表与分辨率映射仍未闭合，见 PRE-01/05/07/09/11/13/14 | `LoginScene` 直连单一host、缺EI服务器列表事务，当前Interface/F151登录拼图在EI根缺贴图；按钮动作及键盘路径不同，PRE-01/05/07/09/11/13/14 |
| mode 2 | 选角/创建，Interface1c F50（640×480） | 登录服务成功→mode 2；phase0–4含列表、创建媒体加载、phase2五控件/密码EDIT、进入等待与StartGame媒体阶段 | 两条槽记录与F51创建、F55进入、F57退出链为primary-static；F53、F92/F95/F98/F86语义candidate；几何、阶段、媒资和键盘差异见 PRE-02/04/06/08/10/12/15–18 | `SelectScene` 使用1024×768布局和自绘卡片/创建表单，未映射EI阶段对象；PRE-02/04/06/08/10/12/15–18 |
| id 0 | 包袱栏，GameInter F250；窗口构造/列表几何见 `window-id-catalog.windows[0]` | HUD cap14「包袱栏(Ctrl+Q,Q)」/裸 Q→toggle id0；NPC修补/买卖等服务端状态改变背包模式 | 负重、金币/货币、修补/变卖/存储模式、格位物品/数量/提示与拖放；46条记录、6×100占位表、6×6视口及F280滚动见 INV-01..04 | `InventoryDialog`；当前固定格网与原版动态占位差异见 INV-01，模式与控件重叠差异见 INV-02..04、HUD-04 |
| id 1 | 状态栏/装备与属性双态，GameInter F200/F201 | HUD cap15「状态栏(Ctrl+W,W)」/裸 W→toggle id1；箭头在同一 id1 对象上切 F200属性态↔F201装备展开态，F201 重设根宽520、高328 | 用户所指装备栏右侧随箭头展开的“属性纸”属于 id1 的 F201 横向状态，不是另开一个窗口；primary-static `status-window-render-evidence.json` 对同一对象 `hero+0x29CE4` 调 `0x423E80` 切到 F201。F201 viewer PNG(alpha bbox `(252,92,518,327)`) 与用户旧版截图的左右组合形态吻合，详见 CHAR-06 | Godot `CharacterDialog` 同一对象切换 F200/F201并设置宽520，窗口编号映射为 id1；坐标、文字、命中与状态保持仍未通过 CHAR-02/03/05/06 |
| id 2 | 商店，GameInter F1000 | NPC商店/修理/存取等业务事件→id2；NPC完成/关闭流程会隐藏 id2 | 商品列表、价格、买卖/修理/存取状态控件；状态含义、货币及交易结果见 SHOP-01、WH-01..03，逐个按钮文字/空态待闭合 | `NPCGoodsPanel`与`InventoryDialog`模式共同承载；`GameStoreDialog`为另一现代商城，不能映射此节点；SHOP/WH条目 |
| id 3 | 交易/交换，GameInter F1050 | 对玩家发交易请求并接收服务端响应→id3；HUD cap0文字「交易栏(Ctrl+C,C)」的实际动作是朝目标实体请求交易，不是直接打开窗 | 双方物品/金币、接受/取消/锁定等交互及消息门控见 TRADE-01..03；每个按钮字样与按下帧待逐控件验收 | legacy HUD cap0 现在发当前协议 `C.TradeRequest`，由服务器按玩家朝向前一格选玩家、校验相向和限制；原版发送 0x401 且由客户端指定目标 entity，服务端定位协议模型不同，多个目标/特殊状态的等价性与实屏回归仍待验。现代模式保留 `TradeDialog.OpenTrade()`；TRADE-01..03、HUD-04 |
| id 4 | 行会，GameInter F600 | 行会状态/命令响应→id4；HUD cap6文字「行会(Ctrl+F,F)」点击发0x40C请求，不直接toggle | 公告/敌对/联盟/成员等列表状态与创建/邀请/解散等控制见 GUILD-01/02；窗内完整文字/状态逐项待转录 | `GuildDialog`可本地打开；当前协议在进游戏时预载行会资料，没有对应的单击请求包。需对照预载后的显示和原版请求时序；根尺寸与控件次序有差异；GUILD-01/02、HUD-04 |
| id 5 | 空 ID，无原版窗口对象 | toggle/点击表为空操作；不能据编号推导好友/社交窗口 | 原版16槽表中无此窗；见 `window-id-catalog.json` documented negative | Zircon好友/邮件等独立功能是扩展，不映射为原版 id5 |
| id 6 | 组队，GameInter F900 | HUD cap5「组队(Ctrl+G,G)」/裸 G→toggle id6 | 成员列表、添加/移除/离队及允许组队状态；文字、行数裁剪、F910..F921控件见 GROUP-01..05 | `GroupDialog`；legacy 开合已抑制现代 `GroupNotify`，成员行几何及按钮入口依静态证据修正，邀请/移除输入流程、权限文字和真实点击仍未验收；GROUP-01..05、HUD-04 |
| id 7 | 独立窗口对象 `hero+0x47C28`、GameInter F200；更支持角色形象/装备属性预览，具体业务触发待证 | 研究 `window-initialization-evidence.json` 给根 `(560,0,244,328)`；本机 8766 viewer 复核 F200 画布256×512、WIL offset(+7,-44)、alpha bbox `(6,92,241,327)`。`window-frame-visual-semantics.json` 将帧内容谨慎标为竖向装备/角色状态面板候选；`window-visibility-dispatch-evidence.json` 把0x42C0A6→id7标为group-pop命令路径。关闭子控件F161/162根相对 `(212,298,28,26)`。这些内容共同更支持角色状态预览而非消息日志，但不能单独证明成员详情业务名称；本机WIL与研究NAS数据身份未按哈希闭合 | Zircon当前 `CharacterDialog` 映射id1 F200/F201；没有找到已确认的独立id7实现。不要把 `_chatLog`仅凭消息显示用途映射到id7；继续追id7触发源、`0x450530`原始绘制字段及`0x450AC0`鼠标行为，目标EXE身份不可读时将组队成员预览保留候选，见 CHAR-06/GROUP-06/CHAT-02 |
| id 8 | 聊天记录窗，GameInter F350 | HUD cap9「聊天记录(Ctrl+R,R)」/裸 R→toggle id8 | 聊天记录、输入/滚动/频道与提交；绘制、滚轮、消息路径见 CHAT-01..03 | legacy cap9当前只切换常驻`ChatLogPanel`显隐；裸 R 仍按现代键表开`RankingDialog`，Ctrl+R无默认动作，见KEY-01/02。该400×150层不等价于EI F350/572×388 popup；EI专用聊天窗仍缺失，见CHAT-01..04、HUD-04。另有研究构建`+` trade/counter 聊天分支，现行 Zircon `C.Chat` 不等价；目标 EI 版本身份未闭合，详CHAT-03 |
| id 9 | NPC对话，GameInter F1100 | NPC交互/服务器业务状态→id9；选项点击成功后关闭id9并同时隐藏id2商店 | NPC头像、对话/选项文字、商品/任务分支；主对话和独立商品/任务对象见 NPC-01..04 | `NPCDialog`与`NPCGoodsPanel`；当前改为同一子树，按钮节流与原版对象关系未闭合；NPC-01..04 |
| id 10 | 空 ID，无原版窗口对象 | toggle/点击表为空操作 | 原版16槽表中无此窗；见 `window-id-catalog.json` documented negative | 不对应当前扩展窗；不得将排行榜/好友窗据编号认作 EI id10 |
| id 11 | 任务，GameInter F700（窗口ID 0xB） | HUD cap10「信息窗口(Ctrl+D,D)」/裸 D→toggle id11；服务器/任务状态供列表 | 任务列表、分类/选择、右页详情、奖励/滚动；箭头/关闭/显示行数和文本见 QUEST-01..03 | `QuestDialog`入口同向，但正文、滚动与箭头几何不符；X/Ctrl+D关闭会发 `MilestoneNotify(false)`，字段业务效果未证，见 QUEST-01..04、HUD-04 |
| id 12 | 设置，GameInter F750 | HUD cap11「设置栏(Ctrl+N,N)」/裸 N→toggle id12 | 原版四项状态开关、两只音量滑块；相关路径及帧边界见 SET-01..05 | `ConfigDialog`已有四行状态控件和两只F751滑块；原版回放/800×600裁切与像素终验仍未完成，SET-01..05、HUD-04 |
| id 13 | 坐骑，GameInter F850 | HUD cap13「坐骑(Ctrl+S,S)」/裸 S→toggle id13 | 马匹状态、等级/属性及上马/下马/遛马命令；HRS-01..03 | 源码映射已修正且实机点击确认 legacy cap13 打开`HorseDialog`；cap13当前的空位/命中重叠需注意，本次选其无重叠区域`(764,706)`，并见HRS-03的实屏背景/控件偏移。原先“入口打开 CharacterDialog/坐骑入口缺失”已撤销；HRS-01..03、HUD-04 |
| id 14 | 技能书，GameInter F400；主初始化记录根尺寸296×332，目标F400绘制范围/本机WIL配准未决 | HUD cap8「技能书(Ctrl+E,E)」/裸 E→toggle id14；子控件选择/分类改变技能状态 | 八类分类、六个已证实列表hit RECT、箭头候选、右页Magic.exp详情与技能键；见 SKL-01..09 | `MagicDialog`窗口入口存在；当前根452×380、12格候选、详情页缺失、快捷栏混入等见SKL-01..09、HUD-04。八类帧号已按静态证据修正为F450/452/454/456/458/460/462/464；选中/按下态及目标EI实际帧外观仍未验收 |
| id 15 | 公告/横幅，GameInter F602；不是常规可点击窗 | 行会操作/服务器状态事件→显隐或刷新；不在常规hit-test/close-all列表 | 动态公告/行会管理文本、编辑缓冲、确认动作见 NOTICE-01/02；激活事件到Zircon消息映射待补 | `NoticeDialog`可打开但内容/编辑/按钮动作不同，且触发映射未证；NOTICE-01/02 |
| id 100 / `0x64` | 退出游戏确认，GameInter F800；在16个普通ID之外 | HUD cap3「退出游戏(Alt+Q)」通过退出门控→显示确认；确认/取消按键另有控件分派 | “是否退出游戏？”及YES/NO两态按钮；原版退出消息、hit RECT见 EXIT-01/MODAL-01 | `ExitDialog`把回选人/退出合为同一现代窗，F800和YES/NO语义未实现；EXIT-01/MODAL-01 |
| 外置确认对象 | 注销角色确认，F950/type `0x65`；不是上述 id100 退出确认 | HUD cap4「注销人物(Alt+X)」→F950注销确认→确认后回选角 | “返回游戏人物选择界面？”及确认/取消，见 MODAL-01 | 当前注销与退出都进入同一`ExitDialog`；MODAL-01、EXIT-01 |

### 行会 HUD 请求与 Zircon 预载路径（协议差异，2026-09-24）

`hud-caption-action-tail-evidence.json` 的 opcode 表将 EI HUD idx6 的点击闭合为 `0x4523E0` 发送 0x40C 行会信息请求；这是原版点击动作，不是单纯打开本地窗。当前 Zircon 的 `LibraryCore/Network/ClientPackets.cs` 没有对应的客户端行会信息请求类型，`ServerLibrary/Envir/SConnection.cs` 也没有该处理入口。服务端 `PlayerObject.OnSpawned()` 在发出 `S.StartGame` 后调用 `SendGuildInfo()`；若角色有行会，`SendGuildInfo()` 会发 `S.GuildInfo`，客户端 `GameScene.OnGuildInfo()` 缓存进 `_guildDialog`。因此 Zircon 通过登录预载满足本地行会窗的数据来源，但不复现 EI 的“点击时请求”时序。不得为表面一致而伪造 0x40C；要做协议级一致，需先证明旧包语义与 Zircon 服务端/网络协议的兼容映射。尚未核对无行会角色、进游戏后行会数据更新及点击时刷新是否可见，故本条保持未验收。

该总表区分三种边：用户点击/键盘入口、服务器/游戏状态驱动的窗口出现、窗内按钮触发的子状态或消息。ID 1 与 ID 7 同为 F200、但对象 ID 与绘制职责不同；ID 2 商店与 id1000/现代现金商城也不是同一功能。具体窗内控件和可见文本的逐项验收仍以右列的审计编号为准；未闭合项不能据主窗口帧或控件图片臆造按钮文字/跳转。

### 入口和当前按键的首轮记录

- 原版 `SelectScene` 源码使用 `Interface1c` F50 背景，配置按钮使用 `GameInter` F116；选角动画来自 `Interface1c` 动画族。原版 `LoginScene` 也以 `Interface1c` 为主。这些源码可帮助理解状态机，但它们本身还不能证明 EI 3.0 同版本行为/布局，必须与 `login-flow-evidence.json`、资源帧和 EI 运行画面对照。
- Zircon 的 `KeyBindManager` 默认把人物/背包/技能/设置窗口绑定为 Q/W/E/O；技能书 Ctrl+E 没有出现在当前 `MagicWindow` 默认项里（Ctrl+E 被定义为 `MagicBarWindow`）。需追查 EI 的键分发与技能窗口自身行为，不能把同一字母的不同修饰键合并处理。
- EI 研究矩阵记录主 HUD caption 有 Ctrl 与单字母入口，包括 Ctrl+E/E 技能书，另有 Ctrl+Q/Q 背包、Ctrl+V/V 小地图、Ctrl+S/S 坐骑、Ctrl+N/N 设置、Ctrl+G/G 组队、Ctrl+F/F 行会、Ctrl+C/C 交易等。Zircon 的键位默认表和 `GameScene` 分派并非该表的一一实现；需建立逐个“原版入口→Godot动作→状态门控”的对照，而不是只比较按键字符。

这一差异属于范围性风险：现代功能热键与 EI HUD caption 的同字母快捷键目前并存，点击 HUD 与键盘动作可能走不同窗口/功能。审计热键时要记录修饰键、焦点状态、聊天框输入状态、窗口已打开时的行为和动作目标。

### 登录与选角的首轮差异

原版 `login-flow-evidence.json` 已将登录/服务器选择置于主模式状态机 `0x8B1878` 的 mode 0，选角/创建置于 mode 2；模式 3 才是游戏。登录对象 `0x8A9520` 加载 `wemade.dat` 与 `Interface1c.wil`，账户/密码编辑框静态矩形分别为 `(128,440)-(227,454)` 和 `(326,440)-(425,454)`；登录页按钮 F11/12、F13/14、F15/16 分别关联选角、创建账号、修改密码（最后两者走配置 URL 并退出原程序）。登录 Enter/Tab 的状态门控及服务端消息 0x7D1 有 primary-static 证据。旧文档曾把当前 Zircon 的 Interface1c F20/F22/F23 说成 EI 登录底图/标志；本轮素材预览对目标 `LegacyEI/Data/Interface1c.wil` 查询发现这三帧均 `blank=true`，故该素材归属判断撤销。`login-flow-evidence.json` 只证明该对象加载了 WIL 和 `wemade.dat`，没有证据把 F20/F22/F23 标成原版登录底图或 logo。另，primary-static 对“阶段2画 F0x3C”的记录需要与目标 WIL 空帧状态重新交叉验证，不能据该常数直接断定实际显示来源。

原版选角/角色创建在 mode 2 使用 `Interface1c.wil` F50（640×480）；研究工件 `login-flow-evidence.json::screens.parent.char_slots` 记录 phase0/3 槽数组基址`+0xCB8`、phase2暂存槽基址`+0x10BC`、stride`0x40`、idx0..1；同工件 F51创建handler明确扫描2条槽记录。底部创建/进入/退出，以及按阶段显示的F92/F95/F98/F86/F89等控件也有静态记录。此前本段误写为“四角色槽”，与上述primary-static字段和当前总表“两个角色槽”矛盾，现撤回。仍需保留一项工件内部差异：`0x458B20` setup摘要的gate写`idx<=2`，它不足以证明第三条可见/可创建槽，需核其调用参数、第三记录分配/绘制/选择读写后再裁定容量；且研究EXE与本机目标版本身份未闭合。空槽/已有角色、创建阶段、密码编辑阶段的控件集不同。确认进入发送 0x67，必须收到 0x20D 才进入阶段 4；不能仅以按钮点击或自动登录日志作为流程完成。

当前 Godot `LoginScene.BuildLegacyLoginUi()` 与 `SelectScene.BuildLegacySelectUi()` 确实组合了 Interface1c 动画和 `DX*` 控件，但可见登录表单、按钮、选择卡片/创建面板多处用 `LibraryFile.Interface` 通用帧或自制 `LegacyWindowFrame`，坐标是 1024×768 画布。它们不等同于原版 F11/F13/F15 控件，也尚未通过 EI 输入状态机逐项比对。相同的背景帧只能证明用了同源素材，不能证明按钮状态、点击矩形、场景阶段一致。

具体源代码核对发现，`LoginScene.BuildLegacyLoginUi()` 把 Interface1c F20 当全屏底图、F23/F22 当 logo 背景/子图；目标 WIL 的 viewer API 对三帧均返回 `blank=true`，这与“EI 登录页 F20/F22/F23”旧摘要相矛盾。原版登录证据所给 `wemade.dat` + Interface1c resource object 尚未定位到逐像素背景来源，不能在没有该对象解码/原版截图时把现代登录拼图方案认作 EI 登录画面。

| 编号 | 严重度 | 首轮发现 | 后续验收 |
|---|---|---|---|
| PRE-01 | 高 | Godot 登录框、按钮及字段布局与 EI 静态登录矩形/原版成对帧未建立映射；当前有 `Interface` 151/152/153 与文本自绘按钮 | EI intro 的 `wemade.dat` 加载/播放阶段由 `intro-splash-state-machine-evidence.json`（0x402C40 sub-stage 1→0x45BE20）闭合；本机文件为640×360、149帧、29.97fps、4.971638秒。此证据只闭合intro片段，不证明登录表单或选角页的底图/控件合成。逐项核 login 对象 F11/12、账号注册 F13/14、修改密码 F15/16 的状态帧与入口坐标，不再把已确认空白的目标 F20/22/23 当作背景证据。按键 Enter/Tab、错误提示、记住账号、切服务器逐项比对；目标 EI 登录实屏未取得前保留候选。 |
| PRE-02 | 高 | Godot 选角面板为自绘窗口与通用控件，和 EI F50 640×480、2 个角色槽及 5 阶段控件组尚未映射 | 对照空槽/双角色上限、创建完成/取消、删除确认、密码编辑阶段、进入消息返回和角色动画层级；本机 WIL 直接像素复核确认 F51/F52 烘焙“创建角色”、F53/F54“删除角色”、F55/F56“开始游戏”、F57/F58“结束”，帧对尺寸依次96×26、96×26、96×24、48×26，alpha bbox 为96×25、93×25、94×24、46×26（原点均0,0），WIL frame offset均(-24,-16)。这只闭合资源视觉文字；F51/F55/F57有 primary-static handler，F53虽字样明确但缺 handler/阶段门，不能据文字宣称删除行为已闭合。F86/F87是勾选态图形，F89/F90为叉形图形，F92/F93是斜笔/金色圆形底图，F95/F96是环形箭头图，F98/F99是卷页/文书图；各对原生28×28或40×38，图形含义仍是视觉候选，尤其不能把三枚阶段控件直接称作翻页。创建阶段对象/协议差异详见 PRE-10。 |
| PRE-03 | 高，EI 800×600 基准当前不能由常规窗口配置直接复现 | 原版登录窗口证据以800×600为基准，选角底图F50为640×480；Godot项目viewport固定1024×768。`ClientSettings.ApplyDisplaySettings()`在窗口模式强制`GameSize.X>=1024`、`GameSize.Y>=768`，`UiScaler.ComputeScale()`又把倍率限制在1..2，因此当前常规窗口模式没有800×600截图条件；legacy登录/选角层还使用1024×768逻辑坐标，不能将1024×768画面当作原版800×600基准 | 在源代码/设置层先明确可复现的EI 800×600比较配置（窗口尺寸、Godot viewport、UiScaler倍率/偏移及输入映射），保留原有1024×768运行基准作另一组。之后分别采集800×600、1024×768和窗口缩放的完整登录/选角截图并点测边缘hit rect；核纹理原生绘制尺寸、控件RECT、UiScaler变换、文本裁切及动画offset。不得把“要测800×600”写成已具备的验收环境 |
| PRE-04 | 阻断，选角根/入口几何不对应 | 选角场景分配了1024×768的F50控件矩形，但贴图按原生640×480绘制在左上；EI根内入口与角色页按钮坐标没有复现 | primary-static `login-flow-evidence.json → screens.parent`：原版 char-select 根背景 F50 为640×480；构造/handler记录的Create `(440,93)`、Enter `(259,49)`、Exit `(28,438)`，另有阶段2的滚动/翻页、删除和确认按钮。当前 `SelectScene.BuildLegacySelectUi()` 将 `FixedSize=true`、控件 `Size=(1024,768)`；但 `DXImageControl.DrawControl()` 只有 `StretchImage=true` 才按Size拉伸，默认值为false，本处没有设置它，因此F50纹理仍以640×480原生尺寸绘制。`UiScaler` 再变换整个图层，不能把控件Size误作图片绘制尺寸。当前选角主操作挂在自绘320×425面板上，逻辑根位置约 `(352,171)`，Enter/Create/Delete hit controls均在面板底部局部 y=382；与EI原版根坐标 `(259,49)/(440,93)/(28,438)` 不对应。 | 以F50原生640×480为基准恢复背景/相机坐标映射；分别在800×600和1024×768下叠加原版构造器rect、Godot实际hit rect和资源有效像素边界。实测进入、创建、删除、退出及阶段切换，确认背景纹理原点、控件矩形和UiScaler变换只映射一次，且焦点/人物动画/子编辑框不随窗口尺寸漂移。 |
| PRE-05 | 阻断，当前登录美术帧选择不符合目标素材，原版视频阶段未闭合 | `LoginScene.BuildLegacyLoginUi()` 使用 Interface1c F20 作背景、F23/F22 作 logo；目标 `LegacyEI/Data/Interface1c.wil` viewer API 对三帧都返回 `blank=true`。EI 文件目录另有 `ei_Login.dat`：独立 `file`/`ffprobe` 识别为 640×360、约30fps、54.35秒 Intel Indeo 5 AVI；[ei-login-dat-samples-2026-09-24.png](evidence/legacy-ei-ui/ei-login-dat-samples-2026-09-24.png) 显示多段片头/场景镜头。研究 `resource-path-table.json` 记目标 EXE 在0x402D17使用字符串 `.\Data\ei_login.dat`，但当前工件未闭合其确切加载器、播放时点及是否属于该登录阶段；目标 EXE身份尚未核实。故既不能将AVI直接认定为登录表单背景，也不能忽略其存在。 | 确认目标 Mir3.exe 对 `ei_Login.dat` 的调用/解码器/阶段字段及实际开始、停止、循环行为；取得同版 EI 运行截图作为基准。定位原版登录对象实际绘制底图、动画和按钮的帧/视频，再逐项对照 Godot。登录视频解码需选择可在目标平台稳定读取 Indeo5 的实现；在帧序列、裁剪、尺寸、音视频控制未闭合之前，不把转码预览当像素验收。 |
| PRE-06 | 高，EI 与 Godot 角色槽数量/创建门限不一致；目标构建身份阻止定案 | 研究工件 `login-flow-evidence.json → screens.parent.char_slots` 报告槽 `0..1`、stride `0x40`，`0x208` 分支 cap 2，创建 handler 扫两槽；但这些 VA 来自 524,288-byte `/tmp/nas_mnt/NAS/TMP/EI传奇3.0客户端/Mir3.exe`。本机唯一 `mir3ei/Mir3.exe` 为 581,632 bytes，SHA-256 `bd0909ae…fa15`，在已核地址指令边界不同；研究目标原件无法从当前未挂载 NAS 取得。通用 `Client/Scenes/SelectScene.cs` 又有4个按钮和 `<4` 门限。Godot `SelectScene` 当前仍显示前4项、以 `<4` 开放创建。基于两槽证据的临时代码修改已撤回，不将未确认二进制结论固化到当前实现；目标行为为 unresolved。 | 获取 SHA-256/文件尺寸与研究目标一致的 EI EXE 后，重新核对槽初始化、`0x208` 列表响应和创建按钮门限；若确认二槽，再复做0/1/2/3项列表、创建/选择/删除刷新回放。若二进制结论不成立，以匹配目标的静态代码/运行时证据重建容量与点击区。不要把服务端账号数据模型上限和客户端页面槽数混为一谈。 |
| PRE-07 | 高，EI 登录中的服务器列表阶段在当前 legacy 登录流程中没有对应界面；当前协议没有等价服务器选择事务 | EI primary-static `login-flow-evidence.json` 记载登录对象 mode 0 的 login form→server-list→transition，以及服务器列表应答 530（`0x212`）、服务器选择消息 0x68、约2秒转入 mode 2，之后收到角色列表 `0x208`。当前 Godot `LoginScene._Ready()` 从命令行或 `ClientSettings` 取得单一 host:port 并调用 `NetworkManager.Connect()`；`ShowLoginResult()` 在一个协议登录成功后直接创建 `SelectScene`。当前仓库协议中 `Library.Network.ClientPackets.Login` 只有账号/密码/checksum；`ServerPackets.Login` 返回 Result、Characters、Items、BlockList、Address 等字段，未见 server-list/server-selection packet；`ServerLibrary/Envir/SEnvir.cs` 登录成功回包在3396附近填入角色列表和购买地址。因此这不仅是漏画 UI：当前客户端/服务端协议层没有EI的独立列表事务。旧 `0x212/0x68` 不能按编号直接塞入不同协议。 | 若产品仍要求原版多服务器节点，先定义并实现当前网络协议中的节点列表来源、选择对象与连接切换事务，再做 UI；在协议未改前将 `--server` /持久化地址作为 Zircon 单服开发流程，PRE-07 保持未验收的 EI 导航差异。取得 EI 对应服务端协议/节点来源或明确的 Zircon 多服协议决策后，再实现并测试列表、选择、失败/断线/重试。 |
| PRE-08 | 中，选角设置按钮资源为空，回调不可达 | `SelectScene.BuildLegacySelectUi()` 创建 GameInter F116 按钮并绑定打开 ConfigDialog，但本机 EI `GameInter.wil` 与 `GameInter.Zl` 都有1103帧且索引116无帧头/图像。按钮未设置 `FixedSize`；`DXImageControl.Index` setter 因而调用 `MirSkin.GetSize(GameInter,116)`，空帧返回 `(0,0)`，既无可见图像也无鼠标命中面积。 | 依据本机资源目录的 `Tools/common/wilsdk.py` WIL解码、`Tools/common/zlsdk.py` ZL解码、`MirSkin.GetSize()` 和 `DXImageControl.Index` 源码，以及 `SelectScene.BuildLegacySelectUi()`。现有 primary-static `login-flow-evidence.json → screens.parent.buttons` 未列 F116；该列表目前只明确 F51/53/55/57/86/89/92/95/98，不能由此断言原版所有配置入口不存在。恢复同版完整入口证据后，决定移除这个无效按钮或替换为有原版来源的入口；之后核实际边界与设置窗打开路径。此项未做运行输入。 |
| PRE-09 | 高，EI 登录对象的按钮帧/动作与 Godot 自制登录页未对应 | 目标 `login-flow-evidence.json → screens.char_select` 的 `phase` 字段明确 phase1=login form（不能仅凭 JSON 键名 `char_select` 推成登录后的选角页）；同对象 F11/12“选择角色”按钮的 phase-1 click chain 通向 login/select flow；F13/14 在该对象中读 `Mir3.ini [Initial] Param2` 并打开注册 URL 后销毁客户端，F15/16 读 `Param3`（缺省 Modify_pwd URL）并打开网页后销毁客户端，F17 的退出/选项身份仍只有 candidate；旧工件称“blank frame/no pixel export”，但本轮直接解码本机 EI WIL 后发现 F17 有可见像素，F18 则没有图像头，故撤回“F17为空白”的资源描述，保留其字样/业务名未定。`interface1c-select-screen-context.json` 的控件位置和尺寸、结合原版通用控件 ctor `0x417550` 的帧头 SetRect 记录，给出基于正常帧的 nominal RECT：F11/12 `(459,436,96,24)`、F13/14 `(139,379,96,26)`、F15/16 `(279,379,96,26)`、F17/18 `(439,379,48,26)`；原版子窗根RECT/最终缩放后屏幕RECT仍未从 EI 运行画面复核。WIL独立解码的 F11–17 帧头依次为96×24、96×26、96×26、96×26、96×26、48×26、48×26，offset均(-24,-16)；alpha bbox分别为 (0,0,94,24)、(0,0,95,26)×4、(0,0,46,26)×2。当前 `LoginScene` 的登录主按钮为 F151 子项局部 `(550,60,100,h)`、无贴图，点击走 `OnLoginPressed()`；与 F11/12 原始RECT `(459,436,96,24)` 的角色/登录主入口关系尚未按共同根坐标和协议链闭合，暂列“可能对应”，不武断判业务相反。当前注册/改密控件却在自制 F151 登录框局部 `(485,0,136,32)`、`(625,0,136,32)`，分别打开 Godot 内部表单并发送 `SendNewAccount`/`SendChangePassword`；原版 F13/14、F15/16 则打开配置 URL 后销毁客户端，动作差异已由静态证据支持。排行和设置也没有此登录对象的原版动作证据；登录页额外的激活/找回密码入口须作为当前扩展单独审计。 | 依据 primary-static 登录对象阶段/帧/点击链、`interface1c-select-screen-context.json` 的位置/normal-frame RECT与 `0x417550` 构造器 SetRect 记录、以及本机 WIL 实测尺寸和alpha bbox；依据 `LoginScene.BuildLegacyLoginUi()` 与 `CreateAccountDialog()`/`CreateChangeDialog()`/`ToggleLoginRanking()`/`ToggleLoginConfig()` 的 source code。逐项决定 legacy 登录页与选角页各显示哪些 EI 控件；在正确对象/阶段下恢复原帧、root坐标、网址参数、ShellExecute/退出等旧逻辑，或明确把新增能力隔离为扩展。要取得目标 EI 同态截图及可靠登录/网页行为回放后才可验收；F17语义保持 candidate。 |

| PRE-10 | 高，创建阶段对象/协议不同，EI业务语义仍受目标构建身份限制 | primary-static `login-flow-evidence.json → screens.parent`：F51 Create handler 扫两条槽，空槽时切 phase1 并加载 `CreateChr.dat`；phase1异步完成转phase2，phase2绘制动画角色列表及F92/F95/F98/F86/F89阶段控件；F89 handler 切phase3并发送旧消息0x64（账号/服务器索引），后续响应才决定 phase。当前 `SelectScene.BuildLegacySelectUi()` 的 Create 直接显示自绘260×650面板，列三职业/两性别、外观字段和本地预览；提交调用 Zircon `SendNewCharacter`，数据含角色名、职业、性别、发型/颜色，不是旧0x64格式。两协议不可按消息号等同；研究工件未证明 `CreateChr.dat` 阶段各控件的全部业务含义。 | 取得同版EI EXE与阶段截图/记录后，闭合F51→phase1→phase2→phase3各按钮/编辑控件的输入和消息；明确 Zircon `NewCharacter` 流程与EI阶段的业务映射后，逐项实现或标注Zircon扩展。当前只确认对象阶段和数据形态不同，不据此重写创建流程。 |
| PRE-11 | 高，legacy 登录页四组动画引用的帧号在 EI Interface1c WIL 中全部越界 | 当前 `LoginScene.BuildLegacyLoginUi()` 调用 `AddLoginAnimation()` 请求 Interface1c 帧段 `2200–2299`、`2400–2429`、`2300–2329`、`2500–2529`。`MirSkin.ResolveUiDataPath()` 在 `--legacy-hud` 下选择 `/home/tetsuya/mir3ei/LegacyEI/Data/`；该目录只有 `Interface1c.wil/.wix`，独立解析 WIX 得到2000个索引（0–1999），`GetTexture()` 对越界帧返回null，`DXImageControl.DrawControl()` 随后不画纹理。独立复核的现代 `/home/tetsuya/mir3ei/Data/Interface1c.Zl` 有3020索引，且上述各段首帧/主要帧有图像头；这说明当前动画段可由另一资源版本提供，不证明EI使用这些现代素材。EI primary-static intro 工件确认另有 `wemade.dat` studio-logo splash，但未将这四组高帧或鸟类动画连到EI绘制链。因此可确认“现有 legacy 登录动画控件按当前资源根不会显示”，但不能据此断言EI原版应显示同样动画，亦不能把Modern ZL帧直接复制为修复。 | 逐段核 `LoginScene.AddLoginAnimation()`、`MirSkin` 的 legacy 根/库级ZL优先与WIL回退、WIL/WIX索引计数、`GetTexture()` 越界返回及 `DXImageControl` 空纹理绘制门。`wemade.dat` intro播放阶段已闭合，不代表登录/选角阶段底图已闭合；继续确认阶段衔接、背景合成及EI同态截图后，才决定移除现代动画、换成原版可证帧或接入原版视频。当前仅登记实现与EI资源不匹配，不改资源根或索引。 |
| PRE-12 | 高，选角旧版资源根下存在必然空白的角色装饰帧 | `SelectScene.BuildLegacySelectUi()` 在 `Interface1c` 上固定创建 BaseIndex 2800/2900、各17帧的左右光效；本机 EI `Interface1c.wil/.wix` 经 `wilsdk.WilLibrary` 独立解析为2000索引（合法0–1999），所以这34帧请求全部越界。`_characterAnimation` 按职业/性别选择介绍帧段240–1959及待机帧段300–2009（离散职业段）；one-shot介绍动画期间还会请求当前帧+100/+130，Assassin male范围内，但1940段兜底介绍最多到2089。创建角色预览职业映射使用300、500、800、1000、1300、1500、1800；兜底映射2000亦无 EI WIL 帧。所有越界帧经 `MirSkin.GetTexture()` 返回空纹理后不会绘制，这是资源索引与当前 legacy 根的确定差异；现代ZL中的同号高帧存在不能证明其属于EI原版。 | 依据 `SelectScene.UpdateCharacterDisplay()/BuildLegacySelectUi()/UpdateCreatePreview()`、`DXAnimatedControl`帧序列公式、`MirSkin.GetTexture()`空帧门及独立WIL计数。继续核EI `screens.parent` 的原版角色列表实际动画由哪套对象/资源生成（含 `0x458B20`/`0x458EC0` 动画指针、GameInter与Interface1c 的调用分工），对三职业和创建各阶段的源帧/状态逐项确认；目标EXE身份未重放且缺EI运行画面前，不猜帧替换或重定资源根。 |
| PRE-13 | 高，login模式调用的底图/按钮图库在 EI 资源根不存在 | `LoginScene.BuildLegacyLoginUi()`将主登录底板设为 `LibraryFile.Interface/F151`，顶部页签/注册改密按 `Interface/F152/F153` 请求纹理；但本机 `/home/tetsuya/mir3ei/LegacyEI/Data/` 只有 `Interface1c.wil/.wix`，没有 `Interface.wil/.wix` 或 `Interface.Zl`（RES-01/02目录及加载器核查）。`MirSkin.ResolveUiDataPath()`在`--legacy-hud`时选LegacyEI根，`GetLibrary(Interface)`找不到ZL后，`GetLegacyWilLibrary()`因无Interface.wil/wix返回空；`GetSize(Interface,151)`为零，当前代码只把容器位置计算fallback为780×115，并不会生成底板纹理；F152/153图像同样无来源。文本字段/按钮控件仍可由Godot自绘并可有hit area，但不能据此称为原版贴图登录界面。EI primary-static登录对象明确加载 `Interface1c.wil` 与 `wemade.dat`，但背景的准确绘制来源仍未闭合；现有 `LoginScene` 另用的Interface1c F20/F22/F23也已由PRE-05确认在目标WIL为空。 | 依据 `LoginScene.BuildLegacyLoginUi()`、`MirSkin.ResolveUiDataPath()/GetLibrary()/GetLegacyWilLibrary()/GetSize()`、`DXImageControl.DrawControl()` 与 EI目录实际文件清单；取得原版登录同态截图/完整目标EI绘制链后再确认正确背景和控件图库。若 legacy 登录需要同一 EI 资源根，逐项重建登录对象真实底板和按钮帧；不以现代 `Data/Interface.Zl` 补帧充当EI证据。运行期单独检查登录页当前空纹理和覆盖顺序，再做账号输入/按钮流程对照。 |
| PRE-14 | 高，登录表单 Tab/Enter 与记住账号路径不同，当前登录按钮仅有鼠标提交连接 | EI primary-static login-flow-evidence.json::screens.phase1.login_fields.key_handlers：0x403FE0 的 Tab(0x09)翻转 +0xD38；phase=1 时 Enter(0x0D)也翻转该状态并调用 0x403640 提交，登录发送链为 msg 0x7D1。Godot DXTextInput 把内部 LineEdit.TextSubmitted 转发为事件，但 LoginScene.BuildLegacyLoginUi()没有给_skinEmail/_skinPassword.TextSubmitted订阅处理器；登录仅由_skinLogin.MouseClick调用 OnLoginPressed()。当前记住状态来自_skinRemember.Checked，提交时把邮箱/密码写入并保存 ClientSettings；LoginScene没有Tab/Enter等价处理器。此为静态源码差异，不据此推断运行时Tab默认焦点目标。 | 在匹配EI构建上确认Tab对+D38的用户可见效果与Enter提交条件；之后在隔离登录态逐项回放字段焦点、Tab、Enter、鼠标登录、记住账号开关及拒绝/成功响应。未完成目标版/当前运行回放前保持未验收；持久化密码策略与EI状态字段先作为不同数据语义记录。 |
| PRE-15 | 高，刷新角色列表后的当前选择状态不同 | primary-static `login-charselect-flow-evidence.json::server_dispatch_0x458F80` 记录 EI 收到 `0x208` 角色列表后清空 `+0xCB8` 区并将选择索引 `[+0x1168]` 设为 `-1`；EI F55 handler 只在该索引为0或1时发进入请求。当前 `SelectScene.RefreshList()` 在列表非空时无条件 `SelectSkinCharacter(0)`，联动选择首行、启用进入/删除并显示角色动画。可静态确认“列表刷新→默认选择首个角色”的状态不一致；目标同版构建身份、EI 首次进入时的按钮视觉/焦点仍待同态证据，不把服务端索引哨兵独自解释为全部禁用样式。 | 以匹配EI构建同态截图/可观察事件闭合刷新后 F55 禁用/启用状态、焦点及预览动画；随后决定 legacy 模式是否应取消 Godot 的自动选首项并保留现代流程语义。任何代码变更需单独验证0/1/2角色列表的鼠标选择、进入/删除门控，当前未改业务行为。 |
| PRE-16 | 高，原版选角阶段控件组与当前创建流程无逐阶段对应 | 原版 `login-flow-evidence.json::screens.parent.phase`：phase0角色列表四按钮；F51 Create 空槽时转phase1并装入 `CreateChr.dat`；完成后进入phase2，播放角色列表动画并显示F92/F95/F98/F86/F89五控件与密码EDIT；F89使phase3并发msg0x64，服务器0x209也可转phase3；完成确认后phase4加载`StartGame.dat`，最终转入游戏。F92/F95/F98/F86语义仍为candidate，不能猜成翻页/删除。当前 `SelectScene` 把展示、创建、进入集中于自绘卡片与260×650表单：创建表单只有角色名、职业/性别、外观选项，未见EI选角对象的密码EDIT或phase2五控件；提交即发Zircon `SendNewCharacter`，新角色响应后刷新列表。本机EI目录中的`CreateChr.dat`（1,221,572 bytes，Indeo5 AVI 640×480，39帧、约1.30秒）和`StartGame.dat`（1,060,892 bytes，Indeo5 AVI 640×480，41帧、约1.37秒）均可读；抽帧分别显示地下通道场景镜头，见[CreateChr采样](evidence/legacy-ei-ui/ei-createchr-dat-contact-2026-09-24.png)与[StartGame采样](evidence/legacy-ei-ui/ei-startgame-dat-contact-2026-09-24.png)。这是数据文件视觉证据，不代表它们是菜单底图；进入反汇编记录中分别由phase1与服务端0x20D的phase4播放器调用。故流程对象与事务边界不对应；不依据相似按钮名强行映射。 | 先从目标同版EI证据闭合F92/F95/F98/F86/F89标签、点击/键盘输入与密码字段语义；再按0→1→2→3→4分阶段列出每个状态的可见控件/RECT/消息，逐态与当前 NewCharacter/StartGame 协议映射，确定哪些是EI能力、哪些为Zircon扩展。到达可交互EI同态画面前维持待审，不触发创建或角色业务消息。 |
| PRE-17 | 高，当前 legacy 创建表单收到成功回包后没有静态返回角色列表的调用 | `ShowCreateCharacterPanel()` 显式设角色列表`Visible=false`、创建表单`Visible=true`。`SubmitSkinCharacter()`发送 `SendNewCharacter`；成功回调 `ShowNewCharacterResult()`只把角色追加进列表并调用 `RefreshList()`，随后更新状态文字，没有调用`HideCreateCharacterPanel()`或重新设置两个面板的Visible。`RefreshList()`只重建卡片并刷新选择，不改变父面板可见性。因此按该路径静态执行，成功后创建表单仍显示且列表仍隐藏；这是当前实现内部可确认的返回路径缺失，尚未在运行期复现。EI创建阶段的成功回包和界面回跳时点因旧消息协议/构建身份尚未作同态比较，不以此推定原版具体回跳帧。 | 后续可独立修复当前成功回调的面板可见状态，并在稳定成功响应条件下验收：提交成功后按补齐的EI阶段证据决定回列表或确认态，失败继续留在可编辑表单；不可用登录自动创建流程掩盖该交互。 |
| PRE-18 | 中，选角键盘分派尚不能与EI闭合，旧版Client与当前Godot行为不同 | 可用旧版 `Client/Scenes/SelectScene.cs::SelectDialog.OnKeyPress()` 明确处理 Enter（StartButton启用时开始）、Down/Up（移动 `SelectedButton`）；EI primary-static `login-flow-evidence.json` 仅列parent WndProc `0x459530`/WM_KEYDOWN `0x459690`，没有该分支的键值语义；证据矩阵把F92/F95/F98/F86等仍列candidate。Godot `SelectScene.cs` 没有场景级 `_Input/_UnhandledInput` 或显式Up/Down/Enter回调；可见角色卡是自绘DXButton并以鼠标回调选中，当前ItemList/底层焦点可能另有默认键盘行为，须实查控件框架与焦点树后再判。不能把旧Client实现当EI primary证据，也不能因Godot没有场景回调就断言完全不可键控。 | 回查 EI `0x459690`原始指令/同版可读反汇编，确定Enter、方向键、ESC/Tab和焦点对象；静态追Godot `DXControl`输入及隐藏VBox焦点归属，再用不发业务包的键盘导航回放核选中边界/焦点。F92等功能按钮保持candidate直至handler闭合。 |

### PRE-04 本轮复核：选角真实画面与资源头（2026-09-24）

- 在隔离 Xvfb `:104` 直接实例化 `SelectScene`，1024×768、未连接账号状态截图：[zircon-select-empty-2026-09-24-1024x768.png](evidence/legacy-ei-ui/zircon-select-empty-2026-09-24-1024x768.png)。日志确认 `Interface1c` 从 `LegacyEI/Data/Interface1c.wil` 加载，共2000帧。画面实际由左上角原生 F50 640×480 背景组成；视窗余下区域是空灰底。所存PNG SHA-256为`05bbf650c20ee43e4125d6271cb4a499a4759fb014f19487c30066b5d95e5baa`。标题“选择角色”在约 `(488,180)`，通用“进入游戏/创建角色/删除角色”按钮在约 `y=553`。复看已入库图像确认：角色条目为空，底部按钮是灰色 Godot 自绘按钮，F50 以640×480画布留在左上，余下右侧与下方为灰色背景；这张图只证明 Zircon 当时的空选角实现状态。本图是 Zircon 空选角状态，不能作为 EI 截图或 EI 行为证据。
- 独立用 `Mir3-Research/Tools/common/wilsdk.py` 读取原始 `LegacyEI/Data/Interface1c.wil` 帧头：F50=`640×480, offset=(-24,-16)`；F51=`96×26`；F53=`96×26`；F55=`96×24`；F57=`48×26`；F86/F89=`28×28`；F92/F95/F98=`40×38`。这与 `login-flow-evidence.json → screens.parent.buttons` 记载的按钮帧/位置相互印证资源编号和画布尺寸，但帧头本身不证明悬停/按下帧、文字绘制、可见阶段或空槽下的启用逻辑。
- EI 目录中的 `wemade.dat` 经 `file`/`ffprobe` 确认为 640×360、约30fps、4.97秒 Intel Indeo 5 AVI（不是 WIL/WIX）；逐秒解码截图 [ei-wemade-dat-samples-2026-09-24.png](evidence/legacy-ei-ui/ei-wemade-dat-samples-2026-09-24.png) 显示 Wemade 标志片段；采样PNG SHA-256为`011e9acf16b38568af2f7124b19729dc1c51cdc6fabfb5940b161a6cf3608311`。旧反编译工件的 parent 构造路径将其装入对象 `+0x6F4`，并设置显示矩形 `(0,60)-(640,420)`。当前 `BuildLegacySelectUi()` 只显示 F50 和静态 Interface1c 动画，未呈现该 AVI。研究 `resource-family-catalog.json` 把 `.dat` 错列到 `files.wil` 且以 `wil_exists=true` 描述，这与实际文件容器及 WIX 不存在相冲突；此处以独立文件头为准，将其校正为 AVI 资源，原 EXE 中具体视频播放时序仍待精确复核。
- 当前 `SelectScene.BuildLegacySelectUi()` 把背景 Control 的 `Size` 写为1024×768，但未启用 `StretchImage`；`DXImageControl.DrawControl()` 因而仍按 F50 原生640×480绘制。此处表明设置控件矩形不等于拉伸帧。它还将通用320×425角色窗居中于1024×768逻辑画布，并把文字按钮放在窗内局部 `(25/120/215,382)`；截图证实它们没有使用 EI F55/F51/F57 帧，也没有位于反编译工件记录的根坐标 `(259,49)/(440,93)/(28,438)`。这是已确认的静态/运行几何与素材差异。
- 本轮没有直接操作原版 EI，也没有输入账号或收到真实角色列表；尚未证明 EI 的800×600锚点如何映射到 Zircon 的1024×768及其它分辨率，亦未闭合角色空槽时各帧的显示/禁用状态、两个角色槽选择命中、删除确认对话框和阶段2的密码/确认流程。因此暂不以这些按钮坐标直接替换当前行为代码；需先从原版构造器/绘制/鼠标分派链补全阶段条件和RECT，再实施逐控件映射。
- 隔离选角进程及 Xvfb `:104` 已停止；主登录测试客户端 PID 550896 与本地 ServerCore 未触碰。
| RES-01 | 高，Interface1c 的 WIL读取已验证一帧；全库/阶段验收未完成 | `MirSkin.GetTexture/GetSize/GetOffset()`先从选定根读取同名`.Zl`；只有整个ZL库未加载（例如文件缺失）时，才对`IsUiLibrary()`中的界面库回退读取同名`.wil/.wix`。若ZL文件已成功加载但指定帧缺失/空白，当前实现直接返回null/零尺寸，不再逐帧尝试WIL。本机`/home/tetsuya/mir3ei/LegacyEI/Data/`无`Interface1c.Zl`，但有`Interface1c.wil/.wix`；同目录没有`Interface.wil/.wix`。已有 `--legacy-wil-audit` 的 headless 记录直接解码 F50，并经 `MirSkin` 的真实回退路径解码 F51（96×26，offset -24,-16）；选角截图日志也报告 Interface1c 2000帧加载。故“Interface1c因缺ZL而不能显示”的旧结论错误，ZL缺失本身不是该图集的阻断。GameInter 同根有Zl/WIL，8766抽样的12帧像素与尺寸/offset一致，但不证明全帧。`Interface` 图集在EI根确实不存在；具体 legacy 界面是否请求它及请求时是否有ZL/WIL供给须沿控件调用逐项确认。 | 对实际登录/选角控件逐帧列出所请求图集、帧号、ZL优先/WIL回退结果及运行日志；检查 `Interface` 缺失是否命中真实路径，并逐项核 Interface1c 动画/按钮各状态帧。GameInter继续补充高风险帧抽样。不得再把“无ZL”直接等同“无素材”；也不得以F50/F51样本推断全库解码正确或EI登录状态机已一致。另需注意这是库级回退而非逐帧回退：若目标同名ZL存在但不含目标EI帧，WIL不会自动补齐该帧。 |
| RES-02 | 高，资源入口分流仍使不同控件读取不同版本图库 | `MirSkin` 的WIL fallback只覆盖 `IsUiLibrary()` 中的 Interface、Interface1c、Interface1cExtended、GameInter、GameInter2、ProgUse、MagicIcon，且只在对应Zl库整体未加载时运行；Zl库存在但单帧缺失时不会逐帧回退。`LibraryCache`仍固定用`DataPath`且不调用该fallback。`PaperDoll`通过LibraryCache取ProgUse、Equip、EquipEffect_UI、GameInter；这些调用不能由MirSkin的WIL回退证明使用EI素材。`DXItemCell`经MirSkin读取，常规物品默认 `StoreItem`，而StoreItem不在IsUiLibrary中，故使用现代Data Zl或返回null，不读 `LegacyEI/Data/Storeitem.wil`。本机EI根存在GameInter.Zl/WIL、ProgUse.wil/WIX、MIcon.wil/WIX、Interface1c.wil/WIX、Equip.wil/WIX、Storeitem.wil/WIX、inventory.wil/WIX；不存在对应ProgUse/MIcon/Equip/StoreItem Zl，Equip不属MirSkin UI fallback，Inventory与StoreItem也不属其名单；GameInter2、Interface及EquipEffect_UI文件未发现。 | 按每个窗口中的具体绘制入口建立“控件→API→LibraryFile→根目录/优先级→实际扩展名→frame”的调用矩阵；优先核人物纸娃娃各层是否意外混用现代Data Zl、背包/交易物品StoreItem、技能图标MIcon，以及MagicBar技能图标/边框。对各项比对旧WIL与实际Modern Zl帧的头部、offset、alpha和颜色；确认EI旧物品/角色记录的frame语义后再决定资源路由修复，不能把某条API可fallback外推到所有UI绘制路径。 |

资源解析抽查表（文件存在性来自EI资源目录；路径决策来自`MirSkin.cs`/`LibraryCache.cs`，不是运行时截图）：

| LibraryFile | `LegacyEI/Data` 实际文件 | 代码读取路径 | 首轮结论 |
|---|---|---|---|
| GameInter | `GameInter.Zl`、`GameInter.wil/.wix` | `MirSkin`→legacy；`LibraryCache`→常规Data | 8766抽查F50/168/200/201/400/600/1050/1100的`.Zl`与`.wil`解码像素和头字段相同；不代表全帧或另一份常规Data/GameInter.Zl相同 |
| Interface1c | `Interface1c.wil/.wix`，无`.Zl` | `MirSkin`→原版 EI WIL 回退；`LibraryCache`仍只读常规Data Zl | 直接解码F50并经`MirSkin`实际回退路径读取F51通过；F50为640×480、offset(-24,-16)，日志确认选角加载2000帧；登录/选角界面仍未完成验收 |
| Interface、Interface1cExtended | 未发现EI文件 | `MirSkin`→legacy `.Zl` | 对应控件帧会无资源；需逐项追调用者和 fallback |
| GameInter2 | 未发现EI文件 | `MirSkin`→legacy `.Zl` | `MagicBar`边框请求走矩形回退；不可称作已加载现代边框 |
| ProgUse | `ProgUse.wil/.wix`，无`.Zl` | `MirSkin`→legacy；`LibraryCache`→常规Data | 同一`LibraryFile`按调用入口会读不同根；`PaperDoll`使用LibraryCache，部分普通控件使用MirSkin |
| Equip | `Equip.wil/.wix`，无`.Zl` | `LibraryCache`→常规Data | `PaperDoll`的EI图层当前读常规Data Zl；与EI WIL逐帧关系未核 |
| StoreItem | `Storeitem.wil/.wix`，无`.Zl` | `MirSkin`→常规Data（不属于`IsUiLibrary`） | 背包/交易格默认走常规Data Zl；大小写与EI WIL不同，但加载器要找的是`.Zl` |
| Inventory | `inventory.wil/.wix`，无`.Zl` | `MirSkin`→常规Data（不属于`IsUiLibrary`） | NPC socket target 等显式设 `LibraryFile.Inventory`，因此实际读取现代 `Data/Inventory.Zl`；不等于EI目录的 `Inventory.wil`。普通背包格并不选该 enum，而是默认 `StoreItem`（详见 INV-05） |
| MIcon | `MIcon.wil/.wix`，无`.Zl` | `MirSkin`→legacy `UiDataPath` WIL fallback；`LibraryCache`自身仍只读常规Data | `MagicDialog`、`MagicCellView`、`MagicBar`均已改用`MirSkin.GetTexture`；legacy运行日志证实读取EI WIL；WIL与现代ZL的非空帧总数及共有帧元数据均不同，帧ID与EI[skill+6]映射未核 |
| EquipEffect_UI | 未发现EI文件 | `LibraryCache`→常规Data | `PaperDoll`额外装备效果来自常规Data路径；是否属于目标EI版本待逐帧核 |

**WIL回退加载器验证记录（2026-09-24）：**为让缺少转换`.Zl`的旧版界面库能够显示EI原始素材，本轮在`MirSkin`加入受限回退：只对`IsUiLibrary()`列出的界面库生效，优先读取选定资源根的`.Zl`，该文件不存在时才尝试同名`.wil/.wix`；不改`LibraryCache`、背包StoreItem及世界资源路径。路径必须先将`Libraries.LibraryList`中的Windows反斜杠归一化，否则Linux会把`Data\Interface1c`误当文件名；此路径问题已由加载器入口测试捕获并修正。新`LegacyWilLibrary`依照`LibraryEditor/WeMadeLibrary.cs`的EI type-3 RGB565 RLE结构解码，保留frame offset，并把像素0按原转换器`nType==3`规则设透明。Godot headless中直接加载`Interface1c.wil` F50得到640×480，且通过`MirSkin.GetTexture(Interface1c,51)`真实缺ZL回退读取F51（96×26、offset(-24,-16)）；F50解码图可见。再独立拿`GameInter.wil`对照同根`GameInter.Zl`的F50、168、200、201、400、600、750、800、850、900、1050、1100：12/12的宽高、offset及RGBA像素均完全一致。截图/PNG证据为`/tmp/zircon-interface1c-wil-f50.png`；命令为`godot-mono --headless --path GodotClient --quit-after 10 res://Scenes/UITestScene.tscn -- --legacy-hud --legacy-wil-audit`。这只验证解析器抽样和Interface1c一帧，不代表Interface1c所有帧、其他WIL库、`LibraryCache`调用、UI控件选择或登录/选角画面已一致；RES-01/02其余工作仍未完成。

### HUD 首轮风险

Godot `MainPanel` 与独立布局场都以 GameInter F50 构造旧版 HUD，并 `LegacyHudLayout` 定义 800×600 原点 `(0,465)`；这些是可复查的迁移事实。当前 `MainPanel.AuditLegacyHud()` 仍将按钮坐标、标签可见性和玩家球位置与自身布局常量对比，因此只证明代码内部一致。原版矩阵中 16 个 caption/按钮 action、热键与输入分派是 primary-static；Zircon 的 `BindHudButtons()` 及常规 `KeyBindManager` 分别绑定动作，尚无一份逐项映射验证它们目标一致。人物球、HP/MP/经验填充方向、右侧控件 z-order 及窗口遮挡要等运行屏幕逐项复核。

资源独立复核补记：通过 `localhost:8766/api/info?f=GameInter.wil&i=50` 得到 F50 画布 800×136；同源 `/api/image?...&scale=1&bg=transparent` 解码后 alpha bbox 为 `(0,0)-(800,135)`，即末行透明。HUD 控件样本 F80–83 头部各为24×16、非透明 bbox 23×15；F90/91 为28×26、alpha bbox 26×26；F100/101 为40×38、alpha bbox 37×37。该信息独立于 Godot 尺寸常量，可用于检查贴图边缘/点击框是否把透明留白计入；它不能单独裁决根窗原点或这些帧在原版上的动作。

| 编号 | 严重度 | 首轮发现 | 后续验收 |
|---|---|---|---|
| HUD-01 | 高 | 旧版 HUD 代码的几何 self-check 被当成通过依据，但不覆盖原版 16 控件 hit rect 与 action 分派 | 从 `hud-caption-action-tail-evidence.json`、`chat-window-control-map.json` 独立建立 16 项映射；逐项点击并按键确认相同动作 |
| HUD-02 | 高 | 当前图标提示使用现代 KeyBindManager 的绑定标签，EI caption 的 Ctrl+单字母快捷键仍需对照；部分同字母功能可能分流 | 记录全部控件 frame/state frame、旧按键、现代动作、焦点/聊天门控、开窗状态 |
| HUD-03 | 中 | 几何检查未验证素材有效像素边界/渲染偏移、缩放后 hit-test 和窗口覆盖次序 | 原始帧 alpha bbox + 800×600 基准截图 + 实际 1024×768 截图/点击做交叉验证 |
| HUD-04 | 阻断 | 旧版 16 个 HUD 图标帧和位置基本照搬，但部分事件仍绑定到现代字段名，多个图标点击目标错误 | 主 EXE `hud-caption-action-tail-evidence.json` 的 16 项构造器/动作表，与 `MainPanel.cs` 位置、帧和 `GameScene.cs` 鼠标回调逐项比对如下： |

| idx | 帧；相对位置 | EI 原版点击动作 / caption | 当前 Zircon 控件与实际动作 | 判定 |
|---:|---|---|---|---|
| 0 | 80/81；(204,2) | 交易栏：玩家朝向前方找实体，发送 0x401 交易请求 | legacy `ExchangeButton` 改为发 `C.TradeRequest`，服务器从朝向前格解析目标并校验对方相向；现代模式仍 `OpenTrade("交易")` | 业务入口已改为请求；目标选择协议不同，实屏与多目标等价性待验证 |
| 1 | 82/83；(228,2) | 小地图：3 秒门控；发送 0x409 请求并控制打开状态 | `MiniMapButton` 只翻转 `_miniMap.Visible` | 缺请求/节流链 |
| 2 | 84/85；(252,2) | 技能图鉴：翻转对象 bool `+0x6208` | `SkillEntryButton` 打开 `MagicDialog` 技能书 | 动作类型不同 |
| 3 | 90/91；(161,46) | 退出游戏：显示 id`0x64`/F800 “是否退出游戏？”确认窗，背景烘焙 YES/NO，另有 F151/152 与 F154/155 按钮状态 | 2026-09-27 修正：`ExitButton` 与 Alt+Q（`ExitGameWindow`）→ `OpenExitGameDialog()`，打开 F800 `ExitGameDialog`（364×184 @ (218,176)，画布 512×256 @ (-74,-36)，YES/NO 用美术滑窗匹配的不可见命中区） | 窗身份/几何已按原版对齐；F800 YES/NO 的最终消息链（消息2/WM_DESTROY）在原版协议层未闭合，行为按“退出确认”实现，实屏点击验证待补 |
| 4 | 92/93；(161,82) | 注销角色：F950 确认，message type `0x65` | 2026-09-27 修正：`LogoutButton` 与 Alt+X（`LogoutCharacter`）→ `OpenExitDialog()`，打开 F950 `LogoutConfirmDialog`（360×190 @ (220,151)，YES/NO 根相对 (51,125)/(244,125) 44×20，YES → `Game.LeaveGame()` 返回选人） | 流程已与 F950 type 0x65 对齐；退出与注销两条路径已分开绑定；实屏点击验证待补 |
| 5 | 94/95；(616,47) | 组队：切换原版窗口 id6 | legacy `PartyButton` 调用 `OpenGroupDialog()`→`WindowManager.Open()`；该路径已打开时直接返回，不会像EI cap5那样 toggle 关闭。`OpenGroupDialog()` 的 `GroupNotify(true)` 被 `!LegacyUi` 条件保护，因此只在现代模式发送，legacy不发 | legacy 不额外发 GroupNotify；仍有Open-only vs EI toggle差异 |
| 6 | 96/97；(616,82) | 行会：发送 0x40C 行会信息请求 | `GuildButton` 只本地打开 `GuildDialog` | 明确漏请求/动作不同 |
| 7 | 159/159；(393,2) | caption 标为腰带；handler 调整 `[HUD+0xD40]` 到0..46，并在端点写`[HUD+0xD42]`为1/2；当前只确定字段写入，具体是六槽选择/动画还是地图滚动尚未闭合。F159 的16×14是按下态图像帧尺寸，不足以推出命中框尺寸 | `BeltButton` 打开腰带窗口，当前控件hit size设为24×16 | 明确没有窗口toggle；EI hit RECT需按构造器父对象字段复核 |
| 8 | 100/101；(703,16) | 技能书：切换原版窗口 id14 | `SpellButton` 切换 `MagicDialog` | 窗口入口匹配；窗口内容另见 SKL 项 |
| 9 | 102/103；(718,32) | 聊天记录：切换原版窗口 id8 | `MailButton` 在 legacy 下切换 `_chatLog.Visible`，现代仍打开 `CommunicationDialog` | cap9 不再误开好友/邮件/屏蔽页；切换的聊天面板具体几何/历史行为仍待EI对照 |
| 10 | 104/105；(718,70) | 信息/任务窗口：切换 id11 | `QuestButton` 切换 `_questDialog` | 入口候选匹配，控件行为仍待验 |
| 11 | 106/107；(703,85) | 设置栏：切换 id12 | `MenuButton` 在 legacy 模式打开 `ConfigDialog` | 窗口入口匹配；四项开关/两条音量已按现有静态证据接线，但配置来源、声学曲线、F750裁切及同版运行差分仍未验收，见 SET-01..06 |
| 12 | 108/109；(664,86) | 帮助窗口：原版韩文资源标记“计划支持”，点击无动作 | `GroupButton` 的旧版点击回调现为空操作；现代模式仍开组队窗 | 旧版导航目标已修正为 no-op；尚需运行时确认此按钮状态与其它映射 |
| 13 | 110/111；(648,70) | 坐骑：切换 id13 | legacy `CharacterButton` 切换 `HorseDialog`；现代模式保留人物窗入口 | 源码目标已修正；本轮鼠标点击按钮左上方无重叠区域`(764,706)`实屏打开坐骑窗。此前中心坐标`(780,721)`落入cap12帮助按钮与cap13重叠区域，不能据它判cap13没有动作。实际F850绘制偏移另见HRS-03；cap13完整RECT仍待核 |
| 14 | 112/113；(648,32) | 包袱栏：切换 id0 | `InventoryButton` 切换背包 | 入口匹配，背包细节另见 INV 项 |
| 15 | 114/115；(665,16) | 状态栏：切换 id1；点击分支额外把模式复位到属性态并重定位244×328；W/Ctrl+W 分支仅切换 id1、不复位模式 | legacy HUD 与 W 均调用 `ToggleCharacterWindow()`；重开时 `ShowOwn()` → `ApplyLegacyEiLayout()` 会复位到F200收起态 | cap15原始中心实屏点击曾打开F200；静态发现入口状态复位差异见 CHAR-05。窗内F200↔F201几何与稳定态运行行为仍待验 |

表中位置以 F50 HUD 局部坐标计，和 `MainPanel` 创建位置一致；动作来自 primary-static 的 `0x42C494` handler、toggle table 与发送器证据。`MainPanel.AuditLegacyHud()` 只验证自身坐标/可见性，不能覆盖这些错误的命令目标。idx12 的错误组队跳转已在旧 HUD 路径禁用；idx15 已改为状态窗入口。idx2/3/5 仍需追踪原版状态字段或子对象的完整业务语义；cap9 已改为 legacy 聊天记录面板，cap2 技能图鉴、cap3 退出及 cap5 组队仍有语义/交互差异。

**HUD-04 cap0 修正记录（2026-09-24）：**`GameScene.CreateHud()` 的 legacy `ExchangeButton` 回调现发送 `C.TradeRequest`；现代路径仍打开 `TradeDialog`。当前服务端 `PlayerObject.TradeRequest()` 从 `CurrentLocation + Direction` 取前一格，只选择玩家并要求双方相向，再执行黑名单/已有交易/请求等限制；这使 legacy HUD 走到请求链，而不是本地伪开窗。与 EI primary-static 的不同点是旧客户端选择并在 `0x401` 中指定目标 entity，Zircon 当前协议没有 target 参数，由服务器按面对格解析；多个目标、同格对象排序及各种拒绝状态仍须对照。当前未提交 `GameScene.cs` 注释称服务器会解析“同一个” facing cell；反编译的实体选择器先按鼠标/方向坐标取实体，再将 `entity+8` 写入 `0x401`，与无目标 `C.TradeRequest` 的服务端自行扫描语义并未证明相同，故该注释只能视为实现假设，不能当作等价性证据。`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功（0 errors、3 warnings）。随后已用 `DISPLAY=:100 bash login_game.sh legacy` 重启唯一审计客户端，完整登录 `TestHero` 到游戏；Xvfb 实屏点击 cap0 的命中位置能显示原文 tooltip（截图 `/tmp/zircon-legacy-cap0-click.png`），点击后客户端保持运行且无错误。当前地图只有本角色，服务端找不到交易对象，故这次运行**只验了 HUD 命中和回调不崩溃，未验交易包确实发出、服务端目标解析/响应及交易窗流程**；这些仍未通过验收，不能把协议等价标为完成。

**控件命中尺寸交叉核对（更正）：**前一稿把 WIL 帧宽高误当作 hit RECT 宽高，结论撤销。`hud-label-evidence.json` 的 `caption_control_class` 将字段 `cap+0x18/+0x1C` 记为普通/按下绘制帧；其构造器摘要同时记载 `SetRect(..., x, y, x+frameW, y+frameH)` 的 `frame=[window_obj+0x38]`。这里用于 RECT 的对象是构造参数 `window_obj` 的 `+0x38` 字段，不是 caption 自身保存的 F100/F102 等 WIL 帧；40×38 只能说明图像画布尺寸。较早但同属 primary-static 的 `chat-window-control-map.json` 对 cap8–15 明列 `setrect_22x22` 及八组 `x/y/x+22/y+22` 结果。两份资料因此并未证明同一个 hit RECT 同时是22×22和40×38；更可能是后续字段说明把父对象 RECT 尺寸误述成了图像帧尺寸。当前应采信能看到明确四个 SetRect 坐标值的22×22记录，至少适用于右侧八个 caption；仍需用原始构造调用的寄存器/栈实参及 `[window_obj+0x38]` 初始化处独立复核父对象。belt cap7 的 F159 为16×14、当前 DXButton 为24×16，但两者都不能单独证明原版 hit RECT；其原版具体父对象及矩形尚未闭合。此前“15个按钮尺寸匹配帧头、belt 多8×2”的推断全部撤销。HUD-05 的 idle/hover/pressed 绘制状态结论不受此几何更正影响。

**HUD caption 字符串证据裁决（F321）：**`hud-caption-action-tail-evidence.json` 的 fresh disassembly 对 cap0 构造调用 `0x4279B2` 直接记录 `push 0x47BCE0`，文本为“交易栏(Ctrl+C, C)”；cap1 调用 `0x4279E6` 直接记录 `push 0x47BCCC`，文本为“小地图(Ctrl+V, V)”；cap14 调用 `0x427D42` 记录 `push 0x47BBE0`，文本为“包袱栏(Ctrl+Q, Q)”。因此旧记录把 `0x47BBE0` 当 cap0、把 cap1 写成“任务栏”的结论已撤销。legacy `MainPanel` tooltip 已改用这16条静态原文并由独立悬停提示控件绘制；现代模式仍使用可编辑键位标签。此裁决只解决字符串映射；legacy提示框完整行为仍须逐项验收。

以下是 F321 对全部 16 个构造调用的转录。坐标为旧版 HUD 原始构造偏移，帧号为原版控件 state 帧；这不等于 hit RECT，也不单独证明 Godot 局部坐标已经正确。cap12 保留原始韩文资源文案及研究记录给出的翻译说明。

| cap | 字符串 VA | EI 原文 | 帧对 | 原始偏移 x/y |
|---:|---|---|---|---|
| 0 | `0x47BCE0` | 交易栏(Ctrl+C, C) | `0x50/0x51` | `+0xCC`, `+2` |
| 1 | `0x47BCCC` | 小地图(Ctrl+V, V) | `0x52/0x53` | `+0xE4`, `+2` |
| 2 | `0x47BCB8` | 技能图鉴(Ctrl+B, B) | `0x54/0x55` | `+0xFC`, `+2` |
| 3 | `0x47BCA8` | 退出游戏(Alt+Q) | `0x5A/0x5B` | `+0xA1`, `+0x2E` |
| 4 | `0x47BC98` | 注销人物(Alt+X) | `0x5C/0x5D` | `+0xA1`, `+0x52` |
| 5 | `0x47BC88` | 组队(Ctrl+G, G) | `0x5E/0x5F` | `+0x268`, `+0x2F` |
| 6 | `0x47BC78` | 行会(Ctrl+F, F) | `0x60/0x61` | `+0x268`, `+0x52` |
| 7 | `0x47BC68` | 腰带(Ctrl+Z, Z) | `0x9F/0x9F` | `+0x189`, `+0xD` |
| 8 | `0x47BC54` | 技能书(Ctrl+E, E) | `0x64/0x65` | `+0x2BF`, `+0x10` |
| 9 | `0x47BC40` | 聊天记录(Ctrl+R, R) | `0x66/0x67` | `+0x2CE`, `+0x20` |
| 10 | `0x47BC2C` | 信息窗口(Ctrl+D, D) | `0x68/0x69` | `+0x2CE`, `+0x46` |
| 11 | `0x47BC18` | 设置栏(Ctrl+N, N) | `0x6A/0x6B` | `+0x2BF`, `+0x55` |
| 12 | `0x47BC04` | 도움말창(지원예정)，韩版遗留文案“帮助窗口(计划支持)” | `0x6C/0x6D` | `+0x298`, `+0x56` |
| 13 | `0x47BBF4` | 坐骑(Ctrl+S, S) | `0x6E/0x6F` | `+0x288`, `+0x46` |
| 14 | `0x47BBE0` | 包袱栏(Ctrl+Q, Q) | `0x70/0x71` | `+0x288`, `+0x20` |
| 15 | `0x47BBCC` | 状态栏(Ctrl+W, W) | `0x72/0x73` | `+0x299`, `+0x10` |

**cap15 动作标签冲突裁决（F313 vs F321）：**旧 primary-static chat-window-control-map.json 的 cap15_action 把 0x42C30D 摘要成“双重切换小地图 + 大地图244×328”。该标签与同一F313工件自己的 control_id_to_subwindow[id1]=0x29CE4、caption id15“状态栏”互相冲突。更直接的 F321 hud-caption-action-tail-evidence.json 的 action_table_0x42C494_16.idx15 记录 0x42C30D 调 toggle(1)；其 toggle_table_0x42B3E4_16 将 id1 映射至状态对象 +0x29CE4，并紧接着调用 0x423E80 对同一对象按 x=0xC8、宽0xF4、高0x148 重定位/建RECT。故采信“切换状态窗并重定位”的具体目标、id和调用链，撤销“cap15 切小地图/大地图”及“双重切换”的摘要标签；此裁决仍引用研究目标构建 primary-static，受本机 EXE 身份未闭合的总限制。当前 Godot cap15 CashShopButton 的 legacy 回调调用 ToggleCharacterWindow()；历史截图 /tmp/zircon-cap15-click.png 证明该版本实屏打开 F200，但不证明状态窗布局/字段/切换行为整体等价。

**运行复核记录（2026-09-23）：**按仓库入口执行 `bash login_game.sh legacy`，服务端7000保持运行；构建成功、测试账号 `test@test.com` 自动登录并收到 `StartGame Result=Success`，进入 `TestHero`，逻辑视口1024×768。首轮 `grim` 截图显示地图与 HUD，但测试窗口遮挡主画面；之后通过 X11 window id 对 Godot 窗口直接抓取1024×768图像，不受桌面截图缩放影响。鼠标在 cap0（交易）当前按钮中心悬停后，初版截图 `/tmp/zircon-hud-cap0-hover3.png` 发现提示框上/左边缺黑线。调整为裁剪区内侧绘制后，重新执行 `bash login_game.sh legacy` 并让 Godot 顶层取得焦点，截得 `/tmp/zircon-cap0-focus-hover.png`：文案“交易栏(Ctrl+C, C)”、黄底 `#FFFF96`、黑字可见；像素扫描确认完整提示框黑色上/下边线均长109px、左/右边线均长18px，边线已进入裁剪区；提示框底缘贴近鼠标位置。此图支持 cap0 的 caption 绘制状态，不代表原版字体度量/动画/所有按钮都一致。之后把指针移到 cap12 计算中心并点击，直抓画面 `/tmp/zircon-cap12-focus-click.png` 未见组队窗，和 no-op 目标相符；但 cap12 悬停未出现 caption，因此这次点击未能证明点击坐标落入该控件，不将它记为完整交互验收。cap0 提示宽高/文本居中、边缘状态和 hover leave 仍待测。启动输出确认 `StartGame Result=Success`、viewport 1024×768，legacy HUD 自检 `PASS` 仅作代码诊断，不视为视觉证明。

**测试启动器更正（2026-09-23）：**连续复测时发现 `login_game.sh` 的客户端查询模式 `godot-mono.*ZirconClient` 与真实进程参数不匹配（实际参数是 `--path /home/tetsuya/development/Zircon/GodotClient`），所以脚本先前报告“无客户端”并遗留多实例，鼠标/窗口状态因此混杂。已把清理及残留检查改为匹配 `$ROOT/GodotClient` 的实际 `--path` 参数；随后再次运行脚本，确认识别并终止了此前由本轮启动的3个实例，只保留新启动的一个客户端（PID 1863899）。这次真实登录成功，`LegacyHud` 输出 `PASS`、`duplicateIcon=False`、`hiddenAttributeIcon=True`；该自检仅为程序状态报告，不代替像素验收。

| 编号 | 严重度 | 发现 | 验收要求 |
|---|---|---|---|
| KEY-01 | 阻断 | EI 主热键动作与当前默认键表大范围错配；caption 中 Ctrl+字母不一定是必须修饰位，常与裸字母并列 | `window-paint-and-hotkey-dispatch-evidence.json` 的 `0x42CC76` 为 primary-bytes：Q=背包、W=状态、E=技能书、R=聊天、S=坐骑、D=任务/信息、Z=亮度/腰带效果、C=实体交易请求、V=小地图、B=技能图鉴开关、G=组队、F=行会动作、N=设置；且有 modal guard。`hotkey-label-handler-consistency.json` 的 Q/S 等按键体调用 `GetKeyState(对应字母)` 并测 AH 按下位，例如 Q 查询 Q、S 查询 S；这不是查询 Ctrl，且 S 的 caption 明确写“Ctrl+S, S”。当前 `KeyBindManager` 默认表仍是现代映射；legacy `_Input()` 已将裸/ Ctrl Q→背包 id0、W→状态 id1、E→技能书 id14、D→任务 id11、S→坐骑 id13、N→设置 id12、G→组队 id6 覆盖到原版目标，其他键仍需逐项核对。Q 的开窗附带复位副作用尚未映射；原版 Z 与 B 都不是窗口开关，C 是实体交互交易请求。 | 建立按 keydown、GetKeyState 参数、modifier 条件、caption、模态/聊天焦点门控和目标动作分列的逐键矩阵；逐项检查 Q/W/E/R/S/D/Z/C/V/B/G/F/N 分支，严格区分“字母键 down”与“必须按 Ctrl”；再分别实测 EI 与 `bash login_game.sh legacy` 的裸键/Ctrl 组合和窗口结果。 |
| KEY-02 | 阻断，逐键目标与修饰键策略错配 | primary-bytes `window-paint-and-hotkey-dispatch-evidence.json` 的 key table 与 `hotkey-label-handler-consistency.json` 的更正结论，对照 source-confirmed `KeyBindManager.KeyBinds`、`GetAction()` 和 `GameScene.HandleKeyBind()`，如下表。Godot `GetAction()` 比较 Ctrl/Alt/Shift 的精确布尔值；EI 热键处理器门控 `[ebp+0x20]/[ebp+0x24]` 是 modal guard，不等价于要求用户按 caption 中的 Ctrl。Q/D/N 等 caption 与字母分支一致；旧研究把 id0误记成交易、把G误记成行会的判断已由 Finding 316 撤销。新增的 KEY-06 记录 legacy Q/W/E/N/G/D/S 路由对 Shift 组合的额外拒绝。 | 依据 EI primary-static 对每个按键验证有效 keydown、caption组合、焦点/mode guard和动作；依据 Godot 源码/设置验证相同物理键的所有修饰组合。逐项检查以下矩阵以及配置覆写后的键位冲突： |

| EI 按键 | EI 已证动作/入口 | 当前 Godot 默认匹配 | 对照结论 |
|---|---|---|---|
| Q / Ctrl+Q | id0 背包；打开时另复位拾取/背包输入状态 | `--legacy-ui` 裸 Q/Ctrl+Q→背包 id0；现代模式裸 Q→人物窗 | 窗口目标已修；原版复位副作用未映射，运行按键待验 |
| W / Ctrl+W | id1 状态面板 | `--legacy-ui` 裸 W/Ctrl+W→`CharacterDialog` id1；现代模式仍为背包/幸运查询 | 窗口目标与现有 F200/F201 legacy 两态绑定一致；真实按键/窗内切态待验 |
| E / Ctrl+E | id14 技能书 | `--legacy-ui` 裸 E/Ctrl+E→技能书 id14；现代模式 Ctrl+E→MagicBar | 旧版入口目标已对齐；技能书内部行为与运行输入待验 |
| R / Ctrl+R | id8 聊天窗 | 裸 R→排行榜；Ctrl+R 无默认动作（幸运查询绑定 Ctrl+W） | 两种组合都不是原版聊天窗 |
| S / Ctrl+S | id13 坐骑窗 | `--legacy-ui` 裸 S/Ctrl+S→id13；现代模式裸 S→仓库 | 旧版映射已按原版 keycode 接入；EI / 游戏内实际输入待验 |
| D / Ctrl+D | id11 信息/任务窗 | `--legacy-ui` 裸 D/Ctrl+D→id11；现代模式裸 D→自动跑 | 旧版映射已按原版 keycode 接入；EI / 游戏内实际输入待验 |
| Z / Ctrl+Z | 调整亮度状态 `[D40]/[D42]`，caption 为腰带/光效语义；不是独立窗开关 | 裸 Z→腰带窗；Shift+Z→伴侣传送；Ctrl+Z 无动作 | 目标与动作种类都不符；原版字段的完整业务语义仍待查 |
| C / Ctrl+C | 向被选实体发起交易请求（不是开交易窗） | 裸 C→无动作；Ctrl+C→货币窗 | 原版动作和修饰组合都错；当前交易请求另绑 T |
| V / Ctrl+V | 小地图显隐；字母入口约100ms节流 | 裸 V→小地图三态（显/半透明/隐）；Ctrl+V 无动作 | 目标基本对应，但多出透明度状态、节流/状态链不等；见 MAP-03 |
| B / Ctrl+B | 切换技能书浏览状态 `[+0x6208]` | 裸 B→独立大地图窗；Ctrl+B 无动作 | 目标错；EI 不存在独立大地图窗的证据见 MAP-02 |
| G / Ctrl+G | id6 组队窗；caption标注 Ctrl+G/G | `--legacy-ui` 裸 G/Ctrl+G→id6；现代模式裸 G→行会 | 旧版键盘 G 走 `GroupWindow`/`WindowManager.Toggle()`，无 GroupNotify；但HUD cap5鼠标走 `OpenGroupDialog()`/Open-only，已开时不会关闭，故两入口当前开关语义分裂且鼠标与EI toggle不等价；真实输入待验 |
| F / Ctrl+F | 行会动作请求 `0x4523E0`，不是简单开/关行会窗；caption 标注 Ctrl+F/F | 裸 F→无动作；Ctrl+F→屏蔽物品过滤窗 | 目标错；行会窗入口另绑 G |
| N / Ctrl+N | id12 设置窗；caption标注 Ctrl+N/N | `--legacy-ui` 裸 N/Ctrl+N→F750 `ConfigDialog`；现代模式裸 N→MenuDialog | 已将旧版键盘入口接到与 HUD cap11 相同设置窗；真实输入与窗内控件待验 |
| T / Ctrl+T | primary-static 分支先检查小地图状态 `[+0x6518]`；完整后续动作未闭合 | 裸 T→交易请求；Ctrl+T→AllowTrade聊天命令 | 不能据同字母认定对应；先追完 EI T 分支再裁定 |

| KEY-03 | 阻断，原版多层输入门控与模态映射未闭合 | Finding 329 `scene-entity-list-and-hotkey-evidence.json` 记录的完整 `0x42CBD0–0x42CF1F` 反汇编，在进入字母表前还列出 `[hero+0x53060]`、`[hero+0x52E8C]` 非零即早退，以及 `[hero+0x5081C]` 非零转交 `0x42B980`；后续字母窗口分派 `0x42CC76` 又记录 `[hero+0x20] OR [hero+0x24]` 模态早退。因此原版不是单一 `[+0x20]/[+0x24]` 门。当前 Godot legacy Q/W/E/N/G/D/S 路由在 `WindowManager.OpenWindows.Any(...)` 全局快捷键门之前执行；只有 `LineEdit`/`TextEdit` 焦点和聊天输入处理会先消费，打开普通或模态 Godot 窗本身不会挡住这些特判。可证门控结构不等价；四个 EI 字段的语义、对话框对应关系及 helper `0x42B980` 效果仍未闭合。 | 证据：`scene-entity-list-and-hotkey-evidence.json` 记 `[+0x53060]/[+0x52E8C]` 非零→ret 1、`[+0x5081C]` 非零→`0x42B980`；`window-paint-and-hotkey-dispatch-evidence.json` 记录继续进入字母按键表前的 `[+0x20]/[+0x24]` gate；当前源码对照 `GameScene._Input()` 检查顺序。 | 恢复与研究工件同版的 EXE 后追四字段所有写入、清零及 helper 消费者，并映射到 EI 对话框分类；再于可观测输入场验证无窗、普通窗、模态窗和文本焦点状态。映射闭合前不宣称热键等价。 |
| KEY-04 | 中，旧版坐骑热键专用分支泄漏到现代模式 | EI primary-static 证据确认 S handler→toggle(13)=坐骑；当前 `_Input()` 的裸 S 分支带 `AutoLoginArgs.LegacyUi` 条件，但随后 Ctrl+S 分支只检查 `key.Keycode==S`、`Ctrl` 且无 Alt/Shift，没有检查 `LegacyUi`，因而现代模式也会直接调用 `ToggleHorseWindow()`。`KeyBindManager.Defaults` 未绑定 Ctrl+S，所以该专用分支先于通用键表拦截此组合。此项为源码控制流结论；遵从本轮限制，不发送 S/Ctrl+S 输入。 | 证据：`window-paint-and-hotkey-dispatch-evidence.json` 的 S=toggle id13 与 `GameScene._Input()`/`ToggleHorseWindow()`、`KeyBindManager.GetAction()` 源码。首轮只登记差异；计划阶段决定将专用分支限制在 legacy 模式并确认现代组合预期，再以安全的纯逻辑/测试场验证门控，不重复游戏内坐骑热键。 |
| KEY-05 | 高，legacy R 聊天入口仍路由到排行榜/空动作 | EI primary-bytes `window-paint-and-hotkey-dispatch-evidence.json`：R (`GetKeyState(0x52)`)→toggle id8 聊天窗；caption 为 `Ctrl+R, R`，静态按键体检查字母 R 按下位而非 Ctrl 位，故裸 R 与 Ctrl+R 都落入同一动作（受原版 modal guard 除外）。当前 legacy `_Input()` 没有 R 专用路由：裸 R 由 `KeyBindManager.Defaults` 命中 `RankingWindow`，`HandleKeyBind()` 打开排行榜；Ctrl+R 不匹配默认键位并无动作。HUD cap9 另已改为切换常驻 `_chatLog`，但这不补上 R→id8 专用聊天窗。此为静态路径差异，未发送 R 输入。 | 对照 `window-paint-and-hotkey-dispatch-evidence.json` 的 R→id8、`hotkey-label-handler-consistency.json` 的 GetKeyState/caption一致性，及 `GameScene._Input()`、`KeyBindManager.GetAction()/Defaults`、`HandleKeyBind(RankingWindow)`。后续修复时须先决定 EI id8 chat popup 与现有 `ChatLogPanel` 的实现对应，再把裸/ Ctrl R 路由到同一目标；分别在无模态、模态窗和聊天输入焦点中验收，确保不误触排行榜。 |
| KEY-06 | 高（研究目标构建静态成立；目标构建身份待闭合），legacy 字母窗快捷路由额外拒绝 Shift 组合 | Finding 329 `scene-entity-list-and-hotkey-evidence.json` 记载 `0x42CBD0–0x42CF1F` 全函数字节级反汇编及字母分派；`window-paint-and-hotkey-dispatch-evidence.json` 的 Q/W/E/R/S/D 等分支通过 `GetKeyState(VK_letter)`，`hotkey-label-handler-consistency.json::kbd_letter_cases` 对 Q/D/N/G/B/V/F 展示 `test ah,ah`，没有记录 Shift 条件。现有完整 handler 摘要列出的门为 KEY-03 所述的对象字段/模态字段，没有 Shift 专项门；但这些工件对应的 524,288-byte 研究 EXE 与本机 `mir3ei/Mir3.exe` 尺寸/哈希不同且关键地址指令不同，不能宣称已对本机二进制独立复核（见首页目标身份说明）。当前 Godot `GameScene._Input()` 的 legacy 专用 Q/W/E/N/G/D 和裸 S 分支都要求 `!key.ShiftPressed`；Ctrl+Shift 因而不会走旧版目标，默认键位表也无 Shift+这些字母替代动作。若研究构建的完整门控通过，Shift+Q/W/E/D/G/N/S 会满足对应字母 `GetKeyState` 检查；此行为仍须同版目标字节或运行证据闭合。未发送组合键，Alt 并行行为不在本项定论。 | 取得与研究工件匹配的 EI EXE 并核对哈希/版本后，重放 `0x42CBD0` 全入口、所有状态门和各字母分支；核 Shift+Q/W/E/D/G/N/S、Ctrl+Shift、文本焦点及模态状态。本条当前只表示“研究构建 primary-static 与 Godot source-confirmed 路径不同”，不得直接升格为本机 EI 行为或据此先改路由。 |

当前表外的 J/O/P/M、Alt+Q/Alt+X、Esc、Ctrl+F1..F4 等是 Godot 默认扩展/现代入口；EI 原版映射分别见 QUEST、SET、GROUP、HRS、EXIT、SKL 条目。它们不构成 EI 键位等价证据。EI 原版 caption 中一键两写法与 Windows `GetKeyState(VK_字母)` 的具体关系，应由对应 handler 及运行态同时闭合；不得把上述表中的 caption 文本反推成必需 modifier。
| HUD-05 | 阻断，绘制逻辑已实现一轮，16项全状态仍未验 | `hud-label-evidence.json` primary-static：16控件 normal-frame override `-1`、hover flag `0`；paint `0x417640` normal不绘制、hover调`0x417370`绘制跟随鼠标的说明框、pressed画state frame。F50两侧常驻图标烘焙在底图。现`DXButton.LegacyHudCaption` idle隐藏、hover揭示原文、pressed画state frame；`MainPanel.ApplyLegacyEiHudCaptions()`配置16项。重启后的 cap0直抓图`/tmp/zircon-cap0-focus-hover.png`与独立像素扫描确认：黄底`#FFFF96`、黑字、上下黑线各109px、左右黑线各18px，四边均可见；提示框下缘靠近指针。此前缺边只存在于旧图`/tmp/zircon-hud-cap0-hover3.png`，内侧绘制修改后cap0边框完整。 | 对cap0继续核框尺寸/文字中心、逐帧揭示速度与hover leave；再覆盖其余15项、按下态、边界位置与800×600目标基准。点击RECT与caption绘制分开验，按HUD-08独立核验。 |
| HUD-06 | 高，动态血量/魔法量没有进入绘制链 | 当前血球只按“最大 MP 是否大于零”画整颗红球或两张完整半球；HP/MP 百分比控件虽绑定数据和 fill 帧，但初始化后被隐藏，旧版画面实际没有任何按当前值裁切的球面填充 | `hud-bars-render-evidence.json` primary-static：`0x429740` 从 HP 与 MP 归一化字段准备 `[0,1]` 比例；HP目标矩形 `(61,496)-(104,566)`、MP `(105,496)-(147,566)`；主HUD调用序是 F62、F60、F61、经验 F63。viewer `/api/info` 给出 F60/F61=56×110、F62=112×110、F63=164×6，偏移均 `(-24,-16)`；直接取 `/api/image?...&scale=1&bg=transparent` RGBA 帧解码的 alpha bbox 分别为 F60/F61 `[0,0,55,109)`、F62 `[0,0,110,109)`、F63 `[0,0,164,6)`，末列/末行透明留白可与命中 Rect 区分。GB18030 hover 字符串确认 HP/MP/经验语义。当前 `MainPanel.CreateBar()` 为 HP/MP 创建的数据填充控件在构造后立即 `Visible=false`；`DrawPlayerOrb()` 对最大 MP≤0 直接画完整 F62，对 MP>0 直接把 F60/F61 完整纹理各画一次，不读取 `_currentHP`、`_currentMP` 或比例，因而静态球色块不反映损血/耗蓝。当前经验条将 ExperienceBar 设为根相对 `(61,121)`、339×11，并由 `DrawExperienceFill()` 把164×6的F63以 destination 宽=`339×比例` 绘制，满值时横向放大约2.07倍；这只反映当前实现。原始 F50 viewer PNG `/tmp/ei-gameinter-50.png` 的底部视觉候选显示明显细框轨道约在 x≈235–400、y≈131 一带，x≈61–235 是否属于同一经验轨道不能由底图单帧确定。因此当前339 px起点/宽度尚未与 EI 的完整 F63 调用参数闭合，不能用已有 HUD 绘制注释替代原版RECT证据；经验条资源位置、缩放和背景轨道仍需primary-static实参或匹配版运行图裁决。当前经验 F63 横向裁切方向也未证明与原版一致。原版三帧的精确遮罩方向、空值边界与前景/底图职责仍受证据 JSON 标注的 live-memory 残项限制，不能据图片先猜补画方向。 | 依据 `0x429740` 的比例栈值和 F62/F60/F61 顺序，恢复各帧对 `0x45E570/0x4542F0` 的矩形/裁切参数；用独立 scalar 表验证HP/MP=0、半值、满值的有效源矩形，再在legacy真实状态条截图核帧叠放、血/蓝球分界、HP/MP悬停数字及数值变动。经验 F63 的方向另行单验。 |
| HUD-07 | 高，legacy 模式仍默认叠加新式常驻 HUD | `GameScene.CreateHud()` 对 legacy 与现代模式共用同一 HUD 初始化：小地图强制显示；`BeltDialog.ApplyLegacyEiPotionBeltLayout()` 后仍强制显示腰带；`MagicBar` 也无条件显示。聊天记录/输入框按 `HideChatBar` 设置，任务追踪按 `QuestTrackerVisible` 设置。以上先由源码确认 Zircon 的默认可见性；本轮新增仍运行的 `bash login_game.sh legacy` 会话实屏快照[`zircon-live-hud-legacy-2026-09-24-1839-1024x768.png`](evidence/legacy-ei-ui/zircon-live-hud-legacy-2026-09-24-1839-1024x768.png)，可见F50底栏、上方两条槽位带和右上小地图/地图标记，截图时未打开对话窗。它验证的是当前运行态，不是 EI parity 截图，也不覆盖悬停、命中与热键行为；仍不能据此裁定哪些元素在 EI 原版应隐藏。原版 primary-static `mini-map-widget-evidence.json`（F888/0x429630）证明小地图绘制通道包含地图帧和六个热槽图标；`hud-hotkey-target-system-evidence.json`（F581/0x42D720、0x42D9E0）证明六槽有独立 RECT/记录/点击执行链，记录为六个 0xC24 item record，操作还有 item、trade、skill 分支。`chat-window-control-map.json` 与 `chat-window-mouse-dispatch.json` 记录六框位置为 `(0x117+i*0x28, 0x1B0+i*0x10)`，框尺寸 `0x26×0x26`，即客户端坐标 `(279+40i,432+16i)`、38×38；中心序列为 `(298+40i,451+16i)`。这些矩形形成对角序列，但它们是否就是 HUD 绘制后的最终屏幕框仍需沿调用参数核实。HUD 绘制例程 `0x429630` 的研究摘要只给出小地图 surface blit 的目标原点 `(275,478-[zoom])`；摘要中出现的 `0x320×0x258` 与 `0x4294E0` 对 F50 HUD 底板记录的 800×600 viewport 参数相同，不能据此当作小地图的有效像素尺寸。`minimap-blit-runtime-evidence.json` 未记录该 surface 实际宽高或最终裁切矩形。因此小地图尺寸/边界继续保持未定，不能以摘要里的 viewport 值判边界。六槽记录具有药水类型字段，和当前 potion-belt 的功能存在明显对应候选，但不能直接等同其目前横排面板布局；它也不等同 12/24 格现代 `MagicBar`。`MagicBar` 请求 GameInter2 帧作为技能边框，但 `MirSkin` 在 legacy 模式把 GameInter2 路由到缺少该文件的 `LegacyEI/Data`，绘制会走矩形回退；不要把常规 `Data` 的同名 Zl 当作已被 legacy 客户端加载。 | 重新核验 `0x429630` 传给 `0x460240` 的完整 RECT 参数、坐标系/裁切和 `0xD44` 初始化者，确定小地图 surface 和 destination clipping 的有效范围；追六槽绘制与点击的实际位置、物品/技能/交易分支及层级。从 `BeltDialog` 对应的原版对象/状态路径查明 EI 六槽对象与药水类型字段的确切关系，并判定是否属于小地图常驻绘制。再用 legacy 游戏态截图记录腰带、MagicBar、小地图、聊天和任务条各自的像素框/遮挡。只有证实原版对应关系后，才决定 legacy 默认显隐及重建方案；不可把六槽快捷物品证据直接当作 12/24 格现代魔法栏依据。 |
| HUD-08 | 高，cap8–15命中矩形已出现静态尺寸与坐标差异；cap0–7/实际命中边界仍待复核 | primary-static `chat-window-control-map.json::setrect_22x22` 明确列出右侧 cap8–15 的22×22 RECT：cap8 `(703,486)`、cap9 `(727,510)`、cap10 `(727,536)`、cap11 `(703,561)`、cap12 `(679,561)`、cap13 `(655,536)`、cap14 `(655,510)`、cap15 `(679,486)`；该工件说明尺寸经父对象`window_obj+0x38`帧尺寸进入`0x417550`的SetRect链。独立素材API确认这些控件图像帧F100–115均40×38、offset=(-24,-16)，但逐帧透明PNG alpha bbox均为37×37、源图原点(0,0)；belt F159仅16×14，帧像素尺寸不可当hit RECT。当前`MainPanel.CreateButton()`给cap8–15对应控件统一显式`Size=40×38, FixedSize=true`，Godot外层Control的可点区域随该矩形；在800×600下主面板由当前源码锚在(0,464)，当前cap8–15矩形依次为(703,480)、(718,496)、(718,534)、(703,549)、(664,550)、(648,534)、(648,496)、(665,480)，均40×38。故当前至少比primary-static记录宽18px、高16px，且多项左上原点也不同；原始研究EXE与本机目标版本身份差异仍适用，研究RECT最终屏幕锚点也应在目标版复核。cap0–7及cap7/F159不在22×22表中，仍不得由绘制帧大小推命中框 | 先复核研究EXE `0x417550` /`0x417830`/所有caption构造实参及`window_obj+0x38`来源，确认原研究RECT数据与本机同版证据边界；把每个控件的sprite alpha bbox、研究hit RECT和Godot Control rect分列。对800×600当前源布局做四边内外坐标合成，再有窗口时实测cap8–15点击是否应以22×22收窄；其余cap须另取原始SetRect证据，特别不能用F159图像尺寸代替cap7 hit框 |
| HUD-09 | 高，提示框像素行为部分闭合 | F321 `hud-caption-action-tail-evidence.json`裁决cap0/cap1/cap14字符串地址；16条原文已录入，legacy `DXButton`单独绘制静态caption。新cap0图`/tmp/zircon-cap0-focus-hover.png`验证文案、黄底、黑字、完整黑框及跟随指针；像素扫描记上下边109px、左右边18px。文字度量、框与指针间隙、动画逐帧和其它caption仍未与EI目标画面闭合 | 从`0x417640`核对锚点、绘制顺序、颜色、边框、逐字计数和pressed状态；逐项收集16个控件idle/hover/pressed/leave截图并测边界位置，caption与hit RECT分开验收 |
| HUD-10 | 高，EI主面板根RECT与素材alpha边界已静态闭合；Godot正式场景根Y仍上移1px | 研究 primary-static [`primary-main-hud-setrect.md`](/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/primary-main-hud-setrect.md) 给出主对象`[esi+0xC58]`的SetRect `(0,601−height,width,600)`；EI F50帧为800×136，根RECT即`(0,465,800,600)`，其有效高度135。本机8766 viewer重取的F50为800×136画布、alpha bbox `800×135+(0,0)`，最后一行alpha全0，见归档[PNG](evidence/legacy-ei-ui/gameinter-frame-50-wil-2026-09-24.png)，因此本机素材有效alpha与RECT有效范围吻合。研究`draw-order-evidence.json::closed_notes`记0x4294E0先选F50再调0x460240；独立的`hud-label-evidence.json`、`confirmation-prompt-evidence.json`调用格式确认首对宽高来自帧头，后续`800×600`为surface clip边界，不是把F50拉伸至全屏。F50自然绘制原点为主对象坐标且该全屏clip使画布索引135落在600边界外；本机此行透明，故没有可见丢失。当前Godot `MainPanel`加载F50、默认`StretchImage=false`，`DXImageControl`按texture自然尺寸绘制；`LegacyHudLayoutLab`根Y=465与EI静态RECT吻合，但正式 `GameScene.LayoutHud()`以`viewportLogicalY−Size.Y`在800×600算出Y=464，使alpha有效部分上移1px。该静态差异足以登记为正式布局偏差；不推断其造成用户报告的人物窗抖动，目标EXE/WIL身份差异及真实屏幕回放仍是范围限制。 | 后续修改计划应统一legacy正式场景与测试场根锚点，并用独立F50 alpha图测y=465下有效bbox；再按EI viewport clip核800×600、其它分辨率和缩放模式，保存完整游戏截图。未经对计划项实施及运行验收，不标为完成。 |
| BELT-01 | 高，槽对象可能对应，原版几何证据冲突待裁决 | 原版 primary-static 证据出现六个 `0xC24` item records、每槽 38×38 RECT、每记录含 potion type；当前 `BeltDialog.ApplyLegacyEiPotionBeltLayout()` 也建六格药水栏。本轮在 viewer 不可连接时改用独立 `wilsdk.py` 读取 EI `GameInter.wil` F51：WIL header=248×46、offset=(-24,-16)，RGBA alpha bbox=(0,0,248,45)，PNG证据见[gameinter-frame-51-wil-2026-09-24.png](evidence/legacy-ei-ui/gameinter-frame-51-wil-2026-09-24.png)；图像确有六个横向凹槽，但仅为visual-candidate。`hud-bars-render-evidence.json` primary-static 把经验条绘制帧定为F63(164×6)，故 `UITestScene.cs` 的“经验条51”诊断文字与原始帧及primary-static记录相矛盾，应视为错误调试标签；它不能证明F51是经验条，也不能证明EI将F51用作独立腰带窗。ImageMagick逐像素复核源PNG，在内部纵向像素中可见凹槽左缘约为 x=9/48/87/128/168/207，视觉中心约 x=25/64/104/145/184/223。当前HEAD中的 `BeltDialog.cs` 将 Grid 放在 `(3,2)`、单格36×36、padding=1.5；`DXItemGrid.Step=36−1+2×1.5=38`，据此当前六个 cell 根相对 x=`4/42/80/118/156/194`，中心=`22/60/98/136/174/212`。因此当前cell视觉中心相对源图凹槽中心约左偏3/4/6/9/10/11 px，横向累计漂移；这证明本地实现与底图凹槽没有几何居中，但不能证明原版 EI hit RECT 就等于视觉凹槽。primary-static 摘要 `chat-window-control-map.json`/`chat-window-mouse-dispatch.json` 将 D44 六框解释为起点 `(279+40i,432+16i)` 的对角序列；这与 F51 六格横排及当前窗口局部 `(3,2)` 横排命中框不能同时直接映射。可能是摘要对 `SetRect` 参数/坐标轴的解释错误、D44 命中框另有用途，或存在两个六槽系统；无目标 EXE 时不能裁定。原版 F50 idx7 点击会改 `[HUD+0xD40]` 与 `[HUD+0xD42]`，不打开独立窗口；研究摘要将其解释为腰带槽选择/动画状态，小地图 `0x429630` 路径也将相同偏移解释为地图滚动量与方向，热槽点击门读取 `+0xD42`。这些字段可能被 HUD 功能共享；现有摘要没有说明彼此如何联动。 | 取得/挂载目标 EI EXE 后复核 idx7 handler、`0x429630` 与 `0x42D720` 对 `[D40]/[D42]` 的读写上下文和 this 对象；重放 `0x427DDE` SetRect 实参顺序、`[D44]` 初始化者与 `0x42D7C0` 构造关系，判断 D44 是否就是 F51 六格的交互矩形。随后以 F51、逐格命中框和 0xC24 records 独立合成，核对物品图标、药水门控与点击效果；确认 EI 模式应把 BeltDialog 实现为常驻槽、弹窗还是不另建对象。 |

## 执行与记录步骤

1. 按“原版界面目录与导航关系”表复核目标 EI 的节点、文字、控件、点击/键盘边和服务器驱动转场；逐项对照既有研究文档，发现矛盾就标出来源、裁决或保留未决。
2. 对当前 Zircon 控件树、真实资源加载路径和输入/协议源码做同一节点交叉映射；记录已匹配/不匹配/证据不足，不用自身自审计常量充当原版预期。
3. 原版证据足以定案后，关联审计编号直接修复该项；先确认其资源/坐标/行为目标，再用独立数据核几何和素材，不把彼此共用同一错误假设的工具视作交叉验证。
4. 每项修复在条件安全且可观测时，运行仓库根目录 `bash login_game.sh legacy`，按规定窗口、输入顺序、分辨率及数据状态做实际鼠标/键盘/业务验证并截图。已识别会触发 Bad Request、需要第二名玩家，或输入采集不稳定的路径不得重复盲测；登记到阻塞清单后继续其它静态/视觉审计，直到可隔离复现条件具备再安排一次验证。截图记录状态/步骤；无法观察或窗口未聚焦时明确记为未验证，不以进程存活替代实际UI，也不把受阻项标为通过。
5. 一项只有画面、控件、操作、流转和数据结果都达到“最终验收标准”才标为通过；每项保留残余风险。全部范围逐项核验后再复核矩阵并整理按风险/依赖排序的最终计划/完成记录。

## 工作区保护与当前限制

### 当前待验证/阻塞项（2026-09-24）

| 项目 | 阻塞原因与已有证据 | 解除条件 / 本轮处理 |
|---|---|---|
| 目标 EI EXE 机器码独立复核 | 研究工件对应524,288-byte Mir3.exe；本机 Mir3.exe 为581,632 bytes且关键地址指令不同，NAS 源文件当前不可读。WIL 素材与现有研究工件仍可用于分别审计，但不能声称已对本机/目标原始字节重放。 | 取得研究目标同版 EXE 后核 SHA-256、版本资源及锚点函数；此前将相关静态结论保留为“研究工件 primary-static”，不在此阻塞下停止素材、源码和现有运行证据核验。 |
| F800退出窗 YES/NO 命中与取消语义 | EXIT-01 已证 F800/id `0x64` 独立于 F950；F800 子控件复用 F151/152、F154/155 帧族。现有鼠标抬起摘要只闭合 `0x418A00` 返回命中后向主窗口发送消息2，不能证明两个按钮各自的命中RECT、YES/NO分别触发什么或取消分支。Win32 消息号2是 `WM_DESTROY`，不是研究摘要误标的 `WM_CLOSE`（`WM_CLOSE=0x10`）；本证据不能单独推出进程生命周期结论。 | 需同版原始指令或能逐框观察的目标版运行证据，追清控件RECT、各自回调及消息2接收后的状态链。保留 EXIT-01 差异，本轮不点退出/注销按钮。 |
| id7 属性槽数量与偏移摘要不一致 | `window-paint-and-hotkey-dispatch-evidence.json::cell_analysis.window_identities_final.id7.identity` 声称“13 SetRect attribute slots +0x578..+0x5E8”；同文件 `notes` 对应条目实际只枚举 `+0x578/+0x588/+0x598/+0x5A8/+0x5B8/+0x5C8/+0x5D8/+0x5E8` 八个偏移，另提 `+0x200 figure rect`。摘要内部无法支持13个槽的计数或完整字段表。 | 暂不沿用“13个槽”作为定论，CHAR-06/id7矩阵只写属性槽链候选。取得研究目标同版EXE后核对 `0x4503B0/0x450530/0x450AC0` 原始指令、SetRect调用及结构字段；本阻塞不妨碍其它窗口审计。 |
| 技能书布局/绘制资料冲突 | 分类后三帧在同一研究资料中互相冲突；F848/F839未闭合列表绘制上限、页索引和六个hit RECT关系。另已发现旧审计误将专用包装器内的452/380常量当作窗口根宽高；主初始化证据把ID14根尺寸记为296×332，而本机F400 WIL画布512×512、alpha bbox为451×378+(30,67)，目标WIL身份未闭合。当前Godot根452×380并将本机alpha左上角锚根原点，不能证明EI原版裁剪或根尺寸等价。 | 不再将452×380称作EI根尺寸，也不据本机WIL alpha bbox改根。取得同版目标EI EXE/WIL后核主调用实参、通用构造器SetRect及F400绘图裁剪，再闭合页/箭头状态链；本阻塞不妨碍其它UI静态审计。
| 研究目标 EI WIL/WIX 资源身份 | 本机 `/home/tetsuya/mir3ei/LegacyEI/Data/Interface1c.wil/.wix` 可读且 F267/268 都非空；现代 `/home/tetsuya/mir3ei/Data/Interface1c.Zl` 中两帧均空；研究 NAS EI `Data/Interface1c.wil` 路径不可读。现有研究 JSON “F268 empty” 与旧 WIL 图像冲突，无法将任一资源集认定为目标 EXE 同版基准。 | 恢复研究数据目录或得到可核哈希的同版归档后，核 Interface1c 全库身份与 F267/268；此项未闭合前继续用各资源集注明来源的证据完成其它 UI 静态审计，不据单一资源集改写布局语义。 |
| NPC任务/镶嵌子窗资源路径与EI映射 | NPC-06/07静态核验表明，当前legacy目录缺少`Interface.Zl`/`Interface.wil`，GameInter WIL也不含5700/5701/5740–5760；旧Client同名帧实现不能证明它属于目标EI。 | 保留为已确认的当前资源引用缺失/错库项；继续查目标EI动态窗与资源身份。未得到可信原版素材前不指定替代帧、不运行任务接收/领奖或镶嵌合成请求。 |
| legacy UI资源库覆盖与高帧引用 | 本机legacy目录只提供`GameInter`、`Interface1c`等部分UI库；`Interface`与`GameInter2`同名ZL/WIL缺失，GameInter ZL2/WIL count均为1103，而源码扫描仍有多组超出帧号。`MirSkin`按库basename加载、不做跨库替代。 | 已记录namespace完整性矩阵和控件例项；继续沿本审计覆盖的窗口核实其实际显隐、fallback、屏幕用途与目标EI映射，不先盲换帧。缺失资源库是已确认的本地差异；替代资产和EI语义仍待证。 |
| Godot MonsterDialog 与 EI 怪物目标反馈对应关系 | EI `target-box-evidence.json`/F359已从目标EXE机器码闭合world-space悬停名签、选中目标框、HP条及名字绘制；该复合反馈不含头像或固定窗口帧。Godot `MonsterDialog`则由MouseObject每帧显示在屏幕上方中央，提供等级/名称/HP/属性及展开区。两者不是直接同构。0x40B2C0五参调用语义、EI selector的实际WIL运行时绑定/帧值，以及目标EXE同状态实屏证据仍未闭合；F413/F414仅是模拟器验证。 | 保留为“迁移映射/运行视觉”待验，不再把EI目标框身份列为未知。继续静态查当前MouseObject生成/选择路径及EI目标消息与状态门控；不做怪物点击或任何可能触发Bad Request的输入。同版EXE/资源与可观察目标版截图具备前维持差异结论，不据此直接改窗或删窗。 |
| Godot动态对话框与 EI 子状态逐窗映射 | 本轮静态盘点确认`GameScene`创建了常驻HUD、NPC任务/镶嵌子窗、货币/包裹/伙伴/里程碑/钓鱼等动态对象；`window-id-catalog.json`只覆盖16个常规槽，不能据此裁定这些动态对象不存在。各对象的EI绘制/命中/消息对应、帧资源和根/子控件RECT尚未逐窗闭合；目标EI EXE/WIL身份问题限制直接复核。 | 已把可见构造/打开/状态回调与候选归类列入补充窗口清册；继续逐对象检查EI静态研究工件、Godot资源/几何/legacy门控。缺目标证据的映射、点击和画面保持未验收；本轮跳过危险或不稳定运行输入。 |
| 交易请求完整往返 | 当前 `login_game.sh legacy` 实际登录状态只有 TestHero；EI 与 Zircon 请求都需要相邻的第二个玩家才能核目标选择、服务端响应和交易窗。 | 有第二个可控玩家同图、相邻且相向时再验；当前跳过此运行测试。cap0 文档只记录界面命中/回调未崩溃，不宣称请求包或等价性通过。 |
| 仓库 state2 分页与跨页存取运行验收 | 当前Godot legacy仓库仅建12格并直接提交槽号0..11；EI研究工件证明state2分页控件分支存在，但页索引到服务端协议槽号链尚未静态闭合，且本机目标EI EXE与研究工件身份不同。 | 本轮只做静态字段与源码对照，不进行存取/翻页业务输入；待取得同版EXE、闭合页槽换算并准备隔离数据后再运行逐页验收。 |
| 不稳定的 X11 键盘注入（含坐骑 S/Ctrl+S） | 先前 Xvfb `:100` 窗口标题/PID显示客户端聚焦，`xdotool key s` 与 `ctrl+s` 各执行一次，但截图没有可观察的坐骑窗状态变化；随后一项鼠标回放的旧 session 查询返回 `Unknown process id`，不能确认截图是否生成。不能据此判定实现失效，也不能据此验收热键。 | 按用户要求本轮不重复 S/Ctrl+S、其他不稳定热键或任何可能触发 Bad Request 的运行测试。后续需可靠的窗口输入/事件记录方式，或采用可复现的手动/测试场操作，并记录前后截图及实际焦点。 |
| 可能触发 Bad Request 的业务输入 | 既有审计记录指出部分未经隔离的交互可能令服务端返回 `Bad Request`；当前没有足以安全复现且观察响应的独立条件，重复发送不能构成可靠UI证据。 | 按用户要求不重放这类业务输入。先以静态发送链/处理器源码继续审计；仅在输入条件、预期服务端处理和隔离测试状态明确后另行安排运行验收。 |
| 人物窗展开/收起的逐帧几何验收 | 已有 collapsed/expanded 两张独立裁切图，缺少完整客户端画面和 crop offset；裁切图高330px而代码根高328px，无法从中定绝对窗口原点。旧笔记称连续 X11 采集不可用，因此没有点击前后首帧/按下帧/释放帧，也无法解释用户报告的展开抖动或检查1px跳动。F200/F201左面板像素裁切相同，只证明素材相同，不证明背景原点、内容原点、命中原点或第二次 reframe 等价。 | 本轮不重放不稳定输入。继续按 `status-window-render-evidence.json` 静态追双坐标和 `x+0x118` reframe；要关闭此项需可复现窗口输入采集、保留完整画面与窗口RECT/crop offset，并覆盖展开前、按下态、首帧、稳定态及收起。现有两张裁切稳定态图的左侧145×330内容像素完全一致，但裁图无屏幕原点，且不能覆盖输入瞬态；该项仍不标通过。 |
| SET-05 F750溢出像素的目标版屏幕核验 | F750 已按根原点与背景控件`(-4,-119)`叠配既有完整截图；候选根原点 screen `(388,260)`/viewport `(388,242)`，源alpha末9行在当前截图可见，见SET-05与归档源帧。Zircon侧额外根裁切已完成截图核验；尚无目标EI同状态完整截图，也未能独立重放研究EXE的screen RECT/root clip，故最终与EI像素范围对照仍阻塞。 | 本轮已完成本地帧/截图配准，不重启客户端、不做输入。下一步取得同版EI F750同态截图并记录其视口原点、根RECT及末9行；若无原版画面，则继续保留“Zircon侧已验证、EI比较候选”的结论，不升格通过。 |
| 当前 Godot GUI 无可用交互surface；已有屏幕快照不可证明当前状态 | CUA `getState()` 返回 `apps=[]`, `browsers=[]`；`cua.listWindows()` 不可用。已归档一张 2026-09-24 05:12:42 的 X11 root 截图 [`zircon-live-ui-current-2026-09-24-0512-1024x768.png`](evidence/legacy-ei-ui/zircon-live-ui-current-2026-09-24-0512-1024x768.png)：真实运行画面可见背包、好友/通信窗和技能书同屏；中窗身份由画面“在线/全部好友”等文字与源码 `CommunicationDialog` 的 F350/572×388 映射交叉支持，但具体页态/打开路径仍未知，采集时的交互历史未知，不能当作指定操作路径/目标状态的验收截图，也不能说明现在仍是该状态。当前 `login_game.sh legacy` 进程仍在运行。 | 使用该快照进行非交互的窗口可见范围/叠放审阅，并明确标记截图时间与状态未知；本轮不发送鼠标/键盘事件。关闭此项需要可操作的目标GUI及可复现的步骤前后截图；若CUA仍不可用，先寻找无副作用的屏幕捕获通道，但不得用输入注入替代交互验收。 |
| 可能触发 Bad Request 的输入/业务路径 | 既往测试已遇到客户端/服务端 Bad Request；目前没有将具体输入、包体与服务器拒绝条件一一对应的安全复现说明。重复触发会污染日志，且不能作为 UI 行为有效证据。 | 本轮不重跑此类运行期测试。静态继续审查对应原版分派、Godot回调与服务端处理；待先闭合触发条件并建立可控隔离场景后，再安排单次可观测验证。 |
| 本轮明确跳过的运行期项目（2026-09-24） | 坐骑 S/Ctrl+S 先前注入没有可观察结果；交易全流程需要第二名玩家；既往 Bad Request 相关输入尚无安全、可重复的触发条件；CUA 当前未暴露可操作的游戏窗口。重复这些项目不会增加可信证据。 | 本轮不启动或注入游戏、不重放上述输入；保留各自已有阻塞项，继续静态源码、素材/截图和布局核验。恢复可靠窗口操作能力、第二玩家或安全复现条件后再单独安排验证。 |
| ~~8766 预览器当前不可连接~~（已解除，2026-09-24） | 旧记录当时确为无监听；之后已使用临时 `nix-shell` 在 root `/home/tetsuya/mir3ei/LegacyEI` 启动 `wilviewer.py`。当前再次读取 `http://localhost:8766/` 成功，且 `/api/info?f=GameInter.wil&i=168` 返回36×36、offset(-24,-16)。曾试用 `file=` 参数得到404，按viewer源码改用 `f=` 后成功。 | 不再作为活动阻塞。按窗口继续从用户指定viewer检查帧图，同时保留WIL/WIX独立解码作为头信息/像素边界交叉核验；服务终止时重新验证状态再处理。 |

**2026-09-24 本轮续审记录：**按用户要求，本轮未发送坐骑 S/Ctrl+S、其它不稳定 X11 键盘注入或可能触发 Bad Request 的输入；也未尝试单人伪造交易完整往返。静态复核 `PlayerObject.TradeRequest()` 确认 Zircon 服务端从角色朝向前一格找玩家，并检查相向、交易状态、黑名单、允许交易及死亡状态；它并不接收 EI `0x401` 中的明确目标实体，因此 HUD-04 的协议目标选择差异和双人往返阻塞继续保留，不能据服务端代码宣称与 EI 等价。本轮一次 `rg` 查询包含仓库中不存在的 `ClientLibrary/`、`ServerLibrary/Network/` 路径并报告路径错误；随即改查实际存在的 `ServerLibrary/Envir/` 与 `ServerLibrary/Models/`，继续完成只读核对。未因单项工具错误中止其它审计。

**2026-09-24 NPC 源帧证据归档：**从本机 `:8766` WIL 预览器读取 GameInter F1100–F1102 的头信息与透明 PNG，归档至 `docs/evidence/legacy-ei-ui/`。独立核对得到 F1100 `512×256/(7,-44)`、alpha bbox `(64,59,384,138)`；F1101 `512×32/(7,-44)`、bbox `(64,7,383,18)`；F1102 `512×64/(7,-44)`、bbox `(64,10,384,44)`。F1101/F1102 的重复项/末项角色来自 `npc-window-render-evidence.json` 的 primary-static 绘制分支，PNG 只证明资源像素，不证明文字叠放、命中或屏幕位置；NPC-01..04 及缺少目标 EI NPC 运行截图的限制保持不变。第一次 ImageMagick bbox 格式串使用不受支持的 `%+X/%+Y`，打印属性警告；改用 `%X/%Y` 后成功取得坐标，结果已复核并记录。全程只读素材/研究工件，未运行客户端、业务输入或测试。

**2026-09-24 行会控件静态续审：**按 `social-window-render-evidence.json` 的真实结构读取 `windows[]` 后，复核 `window.guild-candidate` 的 primary-static 证据：九个控件均以 paint-time `SetPosition` 得到根相对坐标；点击 handler `0x4258F0` 的分支与 F610–F625 像素字样不是完全同义。新增 GUILD-03，逐个列出帧对、坐标、handler 动作及当前 `GuildDialog` 的相似入口/差异，强调不能按按钮图字猜业务；继续标记当前 Godot 六页签不等价于 EI 九控件/三态列表。第一次 Python 查询假设该 JSON 有顶层 `evidence_id`，抛出 `KeyError`；随后检查实际顶层键并按 `windows[].id` 定位正确对象，核对完毕。全程只读源码和已有研究 JSON，未运行游戏、键鼠输入或测试，也未改游戏代码。

**2026-09-24 设置音效路由静态续审：**从 `ClientSettings.ApplyAudioSettings()`、`BusFor()`、`SoundPlayback.Play()`、`GameScene.PlaySound()`、`default_bus_layout.tres` 与 `ClientData/sounds.json` 追完当前旧版音量字段的消费链。731条声音记录按当前分类为 Music 26、Player 27、System 30、Magic 156、Monster 492；legacy 音频总线把 Music 单独接 BGM level/mute，把 Player/System/Magic/Monster 接同一 EffectSound level/mute。与研究工件 `settings-ambience-bgm-volume-evidence.json` 中两类原版音量消费者相符，但只能证明类别路由设计一致，不能替代同版样本的响度/实时变化对比；新增 SET-06 并列出声学验收项。一次 `rg` 命令把不存在的 `GameClientLibrary/` 当搜索目录，返回路径错误；改查实际 `GodotClient/Scripts/` 和 `ClientData/sounds.json` 后完成静态链核对。未启动客户端、未播放/调节声音、未运行测试，未提交或推送。

**2026-09-24 设置矩阵矛盾更正：**回查发现导航矩阵仍沿用修复前的“SET-01 内容错误”标签，且 MENU-01 将原版 F750 根尺寸写成264×248，与 `system-window-render-evidence.json` 的 `constructor_size=[248,264]` 及 SET-01/05 相反。现已把 HUD-04 导航行改为入口匹配、内容未完成最终验收，并统一 MENU-01 尺寸为248×264。一次包含长行上下文的补丁因上下文未匹配而未应用；随即按唯一标记精确替换并以 `git diff --check` 检查通过。第一次声音类别统计读取了 `sounds.json` 顶层而没有进入 `sounds` 字典，结果不能用；改读实际条目后才得到731条分类计数。没有改动设置实现或其它既有工作区文件，未运行测试/客户端，也未提交或推送。

工作区保护记录：上一轮提交 `61bd0682` 只包含审计文档和新增的 F51 解码证据图；未包含当时已有的 `GodotClient/Controls/BeltDialog.cs`、`GodotClient/Controls/CharacterDialog.cs`、`GodotClient/Scripts/GameScene.cs`、`GodotClient/Scripts/LegacyHudLayoutLab.cs` 与 `docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md`。本轮继续保留这些工作区改动，不提交或推送。后续真实客户端启动前先检查当前客户端进程，避免误杀用户正在使用的实例。

原版目标 `Mir3.exe` 当前不能从研究文档记录的 NAS 源路径读取：`/home/tetsuya/NAS/TMP` 当前不存在于挂载点；研究证据声明的来源为 `/home/tetsuya/NAS/TMP/EI传奇3.0客户端/Mir3.exe`。本机 `/home/tetsuya/mir3ei/Mir3.exe` 是 PE 时间戳 2003-05-14、SHA-256 `bd0909ae7b4e5ed49300f573e45a2c073a7cd8bc8da21a553be0a6bc2973fa15` 的 581,632-byte 文件；当前没有目标 EI 3.0 EXE 的哈希或版本资源可用于确认它的构建身份。只读 `objdump` 显示本机文件在 `0x427B24` 的指令流与研究记录中 HUD caption 构造调用的地址布局不同；这只证明不能把研究目标的 VA/函数标签直接套到本机文件，不能据此裁定两个版本的功能语义或版本关系。故本机 EXE 不作为目标 EI 的反汇编复核材料，研究 JSON（如 `group-window-render-detail-evidence.json` F536 与 `quest-window-render-detail-evidence.json` F537）暂作为现有 primary-static 记录使用，但不能声称已对原始字节独立重放。组队列语义现已由 F536 证据闭合；若要复核机器码本身，需取得并校验与研究目标身份一致的 EXE。素材目录和查看器可用性另行核验，不能以 EXE 身份未确认推断素材也缺失。

2026-09-24 复核补充：研究工件 `bag-list-fill-chain-evidence.json` 记录其输入文件为 `/tmp/nas_mnt/NAS/TMP/EI传奇3.0客户端/Mir3.exe`，PE image 范围为 `0x80000`（524,288 bytes）；它与本机 `mir3ei/Mir3.exe` 的 581,632 bytes 不同。本机文件在 `0x42CBD0–0x42CF20` 的反汇编是另一段 HUD/WIL 构造路径，与研究工件给该地址标注的 EI 热键分派不符，所以热键表和相关 VA 只能引用研究目标版本的已有静态研究，不能把本机 EXE 作为复核替身。对 `/home/tetsuya/mir3ei`、`/home/tetsuya`、`/mnt`、`/media`、`/tmp` 的只读文件清单检索只找到本机这一份 `Mir3.exe`，没有发现 524,288-byte 候选或客户端压缩包；`LegacyEI/` 仅含 Data 与 Sound 子目录。NAS 的 GVFS 共享链接当前没有挂载内容，本轮未尝试改动挂载或网络状态。后续若拿到目标文件，先记录来源、长度、SHA-256、PE 时间戳与版本资源，再比较研究工件标注的若干锚点函数字节；身份未闭合前，不以复用 VA 的独立反汇编声称“已复核原版机器码”。

### 真实登录运行时基线（仓库根目录入口）

按用户指定从仓库根目录执行 `bash login_game.sh legacy`。这是本项目 UI 测试约定的完整调用形式。脚本识别到 7000 端口服务已运行并保留服务，服务端和客户端构建均报告 `0 Error(s)`；随后 Godot 4.6.3 Mono 以 `--legacy-ui --legacy-hud` 启动。测试账号 `test@test.com` 自动登录，角色 `TestHero` 收到 `StartGame Result=Success` 并进入地图 7（沙巴克）。当时记录的客户端 PID 1325350；桌面截图 `/tmp/zircon-legacy-root-launch.png` 可见 1024×768 客户端窗口、游戏画面及底部 HUD，且小地图位于客户端内容区右上角；后续截图 `/tmp/zircon-legacy-skill-e-hotkey.png` 显示同一初始画面。该截图可作现有 Godot 小地图可见性/锚点基线，但窗口截图经桌面缩放，不足以替代客户端坐标量测或 800×600 原版对照。已尝试 Wayland 键盘注入打开技能书，但无法确认 Godot 窗口取得焦点；因此这次按键尝试不作为“技能书未打开”的运行时结论。客户端启动/重启必须沿用仓库根目录命令 `bash login_game.sh legacy`，且脚本会先关闭当前客户端；没有另行确认需要新基线前不重复执行。

本轮只读确认旧登录启动脚本仍在运行（PID 1510750，子 Godot PID 1510978，参数含 `--legacy-ui --legacy-hud`）；没有重启、激活或发送输入。通过 `grim` 截得当前双屏桌面 `/tmp/zircon-legacy-audit-live-2026-09-23.png`，当前屏幕上未见 Zircon 窗口，主要显示浏览器与终端。该截图只能证明截图时客户端进程存在但 UI 未在可见桌面呈现，不能作为控件状态验证；旧有游戏截图仍是历史运行证据。本轮因此没有新增 runtime-verified 控件结论。

日志中的 `LegacyHud PASS` 只证明 HUD 的程序自检通过，不视作像素/交互验收。历史 `AuditLegacyOrb()` 将 `duplicateIcon` 错写为 `!MCImage.Visible`，因此旧日志里的 `duplicateIcon=True` 恰好表示 MCImage 隐藏，不能作为状态帧重复绘制的证据；现已更正诊断字段并另列 `hiddenAttributeIcon`。HUD-05 对 idle 状态帧叠加的判断来自原版 paint 反汇编与当前 `DXButton` 绘制源码，不依赖这个自检字段。历史运行当时仅记录初始进游戏画面，真实交互仍未验收。

本轮再次按同一命令启动，脚本确认未杀其他客户端、复用端口 7000 的现有服务；登录 `test@test.com`、自动选择 `TestHero`，并收到 `StartGame Result=Success`。启动参数中的 legacy 模式有效，显示窗口初始 viewport 为 1024×768、UiScaler scale=1。先前整桌面截图缩放后未能看清客户端；随后由 X11 window id `0x2000006`（标题 `ZirconClient - 1024x768`，`_NET_WM_PID` 与启动进程一致）直接抓取 `/tmp/zircon-legacy-window-current.png`，尺寸为1024×768。图中角色位于画面中部，旧 HUD 顶部快捷栏、底部主控区和右上小地图均可见，当前没有打开子窗口。这张图是 Godot 初始游戏态 runtime-verified 证据，可用于后续同尺寸前后差分；不能代替 EI 原版图像，也不能证明任何未打开窗口或点击路径正确。当前运行 PID 可由 `ps` 查询；本轮不再启动第二个实例。

对用户指定的素材入口做了带参数的 API 复核：`/api/info?f=GameInter.wil&i=168` 返回 F168 为 36×36、offset=(-24,-16)、1404 words。无帧参数的 `/api/info` 返回 `library not found` 是接口需要 `f` 和 `i` 的正常错误，不能据此判定查看器故障。之后资源边界审计统一用此带帧参数接口或原始 WIL 解码记录，并继续核对 alpha 有效像素 bbox；单凭该头信息不能证明游戏界面位置或控件语义。

本轮还尝试用 XTest 发送 Ctrl+S 检查 HRS-01/HRS-03；但 `XGetInputFocus` 回报 `PointerRoot`，labwc 没有把 Godot 窗口置为输入焦点，故输入是否进入游戏不可确认。注入后截图出现的左上六槽条不能归因到此按键，不作为任何开窗/热键结论；同样不据此认定 F850 已显示。需要可控地激活 Wayland 顶层窗口后再做交互，避免把未聚焦的输入注入误记为运行时验证。

本轮续查时，`pgrep` 仍看到客户端 PID 1510978，参数包含 `--legacy-ui --legacy-hud`，但 Wayland `grim` 的 1280×1024 屏幕截图 `/tmp/zircon-legacy-audit-current.png` 显示的是系统锁屏，不是游戏画面。进程存在不等于窗口当前可见，也不证明它仍连接在游戏态；本轮未解锁、未发送输入、未启动脚本或重启客户端。因此这张截图只记录运行环境当前被锁定，不能作为任何 UI 像素/交互验收。下一次 runtime 窗口验收需先由用户恢复桌面可见状态，之后再沿用现有 PID/窗口检查，避免 `login_game.sh legacy` 关闭用户正在使用的实例。

2026-09-23 19:29 JST 复查：`ps` 确认 PID 1510978 仍在、启动参数仍为 `--legacy-ui --legacy-hud`；新截图 `/tmp/zircon-legacy-audit-live.png`（1280×1024）再次显示系统锁屏。该复查确认 runtime 验收当前仍不可观察；没有对锁屏或客户端发输入，也没有执行会关闭客户端的 `login_game.sh legacy`。这只是重复记录同一桌面状态，不替代静态审计继续推进。

2026-09-23 19:32 JST 复查：PID 1510978 仍在，`/tmp/zircon-legacy-audit-live-2.png`（1280×1024）仍显示锁屏；游戏窗口不能作为可观察对象，未注入输入或重启。桌面截图每次仅用于判断可观察性，不进入 UI 对比样本。

2026-09-23 19:37 JST 复查：PID 1510978 与启动参数未变，`/tmp/zircon-legacy-audit-live-3.png` 再次显示锁屏；未进入运行时视觉/交互验收。

2026-09-24 00:19 JST 本轮键位修复运行记录：`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 通过（3条既有警告）；随后从仓库根目录按 `bash login_game.sh legacy` 重启，脚本构建服务端/客户端成功，连接本地 7000 端口，账号 `test@test.com` 自动进入 `TestHero`，收到 `StartGame Result=Success` 并进入地图。终端显示 `--legacy-ui --legacy-hud` 生效。`cua.getState()` 返回无可交互应用，因而本轮没有按 D/S 或点击窗口；D/S 的新路由只获源码与编译/启动验证，必须等桌面窗口可绑定后做真实按键、焦点和开合状态回放。启动脚本当前仍在运行，legacy 客户端为持续基线；后续重启前需确认它包含最新编译产物。

2026-09-24 00:22 JST Q 键修复运行记录：`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 通过（3条既有警告）；随后再次按 `bash login_game.sh legacy` 启动，前次客户端已退出且服务端未运行，脚本完整启动服务端/客户端，自动登录 TestHero 并收到 `StartGame Result=Success`。此处只确认新的 legacy 程序构建、连接与进游戏路径无回归；桌面应用清单仍为空，Q/D/S 的实际按键动作与焦点/mode 门控均未进入 runtime-verified。

2026-09-24 00:24 JST W 键修复运行记录：增量关闭后以 `bash login_game.sh legacy` 重启，服务端保持运行；自动登录 TestHero，收到 `StartGame Success`，终端 `LegacyHud PASS`，无启动期异常。该路径验证 W 改动未破坏构建/登录/HUD 自检；没有游戏窗口绑定和真实输入回放，W/Ctrl+W 开 id1 与 F200/F201 切态仍未标记 runtime-verified。

2026-09-24 00:26 JST E 键修复运行记录：构建通过（3条既有警告），`bash login_game.sh legacy` 完整重启后登录 TestHero 并进入地图，`LegacyHud PASS`；未发现启动异常。该结果仅确认新旧模式分支能正常启动，不证明 E/Ctrl+E 输入已在窗口内回放，也不证明技能书内容正确。

2026-09-24 00:27 JST E 键回归复核：客户端收到 `StartGame Result=Success`，加载 Sabuk Keep 地图并完成首帧渲染；贴图诊断 missing libraries/textures 均为0。该交叉日志进一步确认新客户端稳定进入地图，但没有窗口/按键回放，仍不计作 E、Ctrl+E 行为验收。

2026-09-24 00:29 JST N 键修复运行记录：构建通过（3条既有警告）；`bash login_game.sh legacy` 重启后登录 TestHero，收到 `StartGame Success`，HUD 自检通过且进入地图。N/Ctrl+N 的 ConfigDialog 开窗仍只有代码与启动验证，尚无真实桌面按键截图。

2026-09-24 00:31 JST G 键修复运行记录：构建通过（3条既有警告）；legacy 启动脚本重启成功，登录 TestHero 并收到 `StartGame Success`，进入地图且未见启动异常。G/Ctrl+G 目标窗仍未实际键入验证；组队窗自身内容和 HUD 点击的 GroupNotify 仍是独立未决项。

**2026-09-24 设置窗修复前基线（先于提交 `d5b0e6d2`）：**在 Xvfb `:100` 的 `ZirconClient - 1024x768` 游戏窗口点击 HUD cap11 打开 `ConfigDialog`，截图[`settings-window-current-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-current-2026-09-24-1024x768.png)。该图确证当时使用 EI GameInter F750 背景，但不能单靠图像把背景烘焙的 ON/OFF 字样与按钮当前帧分开；源码历史版本才确认当时八个按钮分别绑定为八个独立的现代音量/显示设置，与原版四组状态字段映射不一致。背景两条音量轨显示轨迹和端部箭头；同一修复前源码没有 F751 子控件，故当时没有可拖动滑块。该截图/源码状态在 `d5b0e6d2` 中修正后已过期，不能描述为当前窗口。随后提交 `d5b0e6d2` 增加四组原版状态绑定、两只 F751 滑块，并有后续实屏状态/滑轨回放记录（见紧接的“设置窗修复与回放”）；设置仍未做 EI 800×600 并排差分及完整音频验收，不得标作通过。

2026-09-24 设置窗修复与回放：新增四个独立旧版状态字段及两条旧版主音量字段；BGM/EffectSound 应用到 legacy Music/效果总线，Ambience 保留反汇编证实的视觉死开关，ShadowBlend 保存其原值而不假造渲染消费者。每行仍保留两处原版 hit RECT，仅按状态绘制 ON 或 OFF 高亮，按下帧分别用 F761/F763；新建两只 F751 滑块，拖动长度160px，值按 `round(px×0.625−100)` 限在 -100..0 dB。`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功（0 errors，3条既有 warning）。随后从仓库根目录执行 `DISPLAY=:100 bash login_game.sh legacy`，服务端端口7000复用后重建，登录 TestHero，收到 `StartGame Result=Success`，首帧缺失 library/texture/emptyImageEntries 均0。点击真实 HUD cap11 打开设置窗；ON/OFF 状态往返实测 BGM、EffectSound 与 ShadowBlend，Ambience 临时点 ON 后再点 OFF，确认其未写入持久状态；设置文件中的测试改动均恢复到初始值（BGM/EffectSound=true，Ambience/ShadowBlend=false，两音量=0）。两条滑块分别拖至左端并恢复右端，BGM 配置实读 `0→-100→0`。截图：[`settings-window-after-fix-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-after-fix-2026-09-24-1024x768.png)、[`settings-window-bgm-off-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-bgm-off-2026-09-24-1024x768.png)、FX关闭状态截图缺失（仅有通用OFF按钮截图与FX滑块端点截图，不能替代该状态证据）、[`settings-window-ambience-transient-on-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-ambience-transient-on-2026-09-24-1024x768.png)、[`settings-window-shadow-on-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-shadow-on-2026-09-24-1024x768.png)、[`settings-window-bgm-slider-min-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-bgm-slider-min-2026-09-24-1024x768.png)、[`settings-window-fx-slider-min-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-fx-slider-min-2026-09-24-1024x768.png)、[`settings-window-restored-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-restored-2026-09-24-1024x768.png)。确认了 Godot 内部显示、hit、保存与重开前状态；未对声学输出做听感/测量核对，也未在 EI 原版或 800×600 目标分辨率做并排像素验收，因此 SET 仍未通过终验。

2026-09-24 设置窗静态布局复核：重读 `system-window-render-evidence.json` 与 `ConfigDialog.cs`，确认 F750 的原版主窗根为248×264；11个构造子控件中，10个位置与当前实现一致，关闭按钮左移2px的既有差异见 SET-04；独立读取本机 `LegacyEI/Data/GameInter.wil` 头部，F161/162=28×26、F760/761=32×22、F762/763=40×22、F751=20×16，当前设置控件 hit 尺寸与这些资源帧一致。F750 alpha bbox `(4,119,248,273)` 配合当前背景偏移 `(-4,-119)` 得根相对 y=`[0,273)`。更正当前裁剪结论：`bash login_game.sh legacy` 传入`--legacy-ui`，`GameScene.ApplyLegacyCoreTestLayouts()`经`LegacyUiSkin.ApplyLegacyTestWindow()`先设根`Clip=true`，随后`ConfigDialog.ApplyLegacyEiLayout()`不重置它；`DXControl.Clip`映射`ClipContents`，故当前 legacy 测试画面将该帧裁到根高264，底部9px确定被Godot裁掉。旧称`ConfigDialog`/`DXWindow`未设Clip不适用于这条运行路径。Godot裁剪行为已由源码闭合；EI是否同样裁剪仍需同版目标画面对照，1024×768截图不能裁定原版策略。新增 SET-05 保留为待核，不因资源有效像素尺寸直接改裁切。一次只读搜索把文件名假设为 `SettingsDialog.cs`，工具返回路径不存在；随即以文件索引找到实际 `ConfigDialog.cs` 并继续核对，未有文件被修改。

2026-09-24 00:47 JST 技能书帧修复验证：`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功（0 errors，3条既有警告）。检查到当前 legacy 客户端 PID 383044 已于 00:30 启动，早于 F460/F462/F464 代码变更；本轮没有重启它，也没有新桌面可交互应用，因此不能把现有客户端进程当作新帧的运行验证。SKL-08 目前只有静态帧号修正与编译证据，等待窗口可观察后逐类点击/比较普通与按下帧。

2026-09-24 00:48 JST 组队成员几何修复验证：从仓库根目录执行 `bash login_game.sh legacy`，脚本先关闭本轮审计旧客户端 PID 383044、复用 7000 服务，然后服务端/客户端构建均为 0 errors；自动登录 TestHero，收到 `StartGame Result=Success`，进入地图 7 / Sabuk Keep，贴图诊断 missingLibraries/textures 均为0。新客户端 PID 432180。`cua.getState()` 仍返回 apps/browsers 均为空，所以这次只确认登录/进游戏回归；GROUP-01/02 的实际成员行位置、列表溢出和窗口点击还没有运行时证据。

2026-09-24 01:00 JST 组队控件入口修复运行记录：`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 通过（0 errors，3条既有警告）；随后从仓库根目录再次运行 `bash login_game.sh legacy`，脚本关闭旧客户端 PID 432180，服务器按脚本策略重新启动；自动登录 TestHero 并收到 `StartGame Result=Success`，进入 Sabuk Keep，贴图缺失诊断为0。最新客户端 PID 459913，代码版本包含底部F910/F912/F914热区、F920/F921权限控件和开合时 suppress GroupNotify。此轮只确认构建、连接和进游戏未回归；`cua.getState()` 仍无交互应用，因此组队窗口尚未打开，鼠标动作/服务器离队副作用/权限状态文字都未做运行验收。

2026-09-24 01:05 JST 背包负重显示修复运行记录：`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 通过（0 errors，3条既有警告）；随后从仓库根目录执行 `bash login_game.sh legacy`，关闭旧客户端 PID 459913，服务端和客户端构建均 0 errors，TestHero 收到 `StartGame Result=Success` 并进入 Sabuk Keep；贴图缺失诊断为0。新客户端 PID 470870，包含负重文字位置、F360 横条隐藏、GG/钱包隐藏及单一货币数值布局改动。桌面不可交互，未开背包，像素、模式状态、数值语义/点击仍未 runtime-verified。

2026-09-24 01:18 JST 当前工作区回归记录：`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功，0 errors、3条既有警告；随后 `bash login_game.sh legacy` 关闭旧客户端 PID 470870，重用本地服务器，TestHero 自动登录并收到 `StartGame Result=Success`，进入 Sabuk Keep，贴图缺失为0。客户端包含当前工作区里未提交的人物 F200/F201 单背景切换与 F201 展开态处理，以及腰带 F51/6槽布局；这些代码仍保持未提交，按工作区保护约定未纳入本次提交。只验证编译/启动/登录/首帧，截图采集得到全黑桌面且 `cua.getState()` 无交互应用，未开窗或注入输入，因此不计人物抖动或腰带像素/点击验收。

2026-09-24 01:34 JST Xvfb运行观察：为避开黑屏的物理桌面，启动 Xvfb `:100`/Openbox，仍经 `DISPLAY=:100 bash login_game.sh legacy` 完整构建并登录 TestHero；收到 `StartGame Result=Success`，加载 Sabuk Keep，缺失贴图/库均为0。截图 `/tmp/zircon-legacy-xvfb-base.png` 实际显示地图、HUD、顶部六槽F51腰带和技能栏，证明虚拟显示器可观察当前程序画面，但分辨率是1024×768，不作为EI 800×600差分样本。尝试 `xdotool` 将W键和鼠标事件发送到Godot窗，画面/角色状态没有可见变化，且本轮没有可用Cua桌面绑定；所以没有把输入回放计入行为验证，人物切换、技能书、背包、腰带的实际点击仍未验收。

2026-09-24 01:51 JST 技能书候选格索引修正运行记录：`MagicDialog.RefreshLegacySkillSlots()` 现与 `MagicCellView` 一样按 `NeedLevel1`、`Name` 排序，消除了 Godot 内部候选格视觉项与 F 键绑定 tuple 因 DB 顺序不同造成的错位。`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功（0 errors，3条既有 warning）；随后按 `DISPLAY=:100 bash login_game.sh legacy` 完整重启，服务端/客户端构建均为0 errors，TestHero 收到 `StartGame Result=Success` 并进入 Sabuk Keep，missingLibraries/textures/emptyImageEntries 均为0。Xvfb 无可用输入注入工具/桌面绑定，未能点击技能格或回放 F 键；因此本次是启动回归，不是技能交互验收。EI 对照仍未通过：12格布局与列表顺序仍缺原版坐标、页状态和目标 EXE 运行证据。

#### 技能图标旧版资源路由复核（2026-09-24，Zircon 侧）

`MirSkin.IsUiLibrary()` 已将 `LibraryFile.MagicIcon` 纳入旧版 UI 资源路由。依仓库指定流程从根目录运行 `DISPLAY=:100 bash login_game.sh legacy`，TestHero 登录并进入地图；打开技能书后运行日志出现 `[MirSkin] legacy UI WIL fallback: MagicIcon -> /home/tetsuya/mir3ei/LegacyEI/Data/MIcon.wil (1106 frames)`，证明技能书经 `MirSkin` 绘制的图标已从 EI `MIcon.wil` 回退读取，不再误读现代 `Data/MIcon.Zl`。运行截图 [`skill-window-magicicon-wil-runtime-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/skill-window-magicicon-wil-runtime-2026-09-24-1024x768.png) 显示技能书打开态，当前仅有少量技能图标且窗口贴近右上边界。

技能书截图仅闭合 `MagicDialog` 的旧版图标资源路由，不验证 MIcon 帧号与 EI 技能记录 `[skill+6]`、图标尺寸/裁切、六个命中 RECT、技能清单和窗口锚点；这些仍未闭合，因此 SKL-01/02、RES-02 仍未验收。

#### 背包实机运行截图（2026-09-24，Zircon 侧）

在已运行的 `:100` / 1024×768 legacy 客户端中，用鼠标点击 HUD 背包入口打开窗口，保存截图 [`inventory-open-2026-09-24.png`](evidence/legacy-ei-ui/inventory-open-2026-09-24.png)；随后点击窗口右下关闭钮，回到无背包窗口的游戏画面 `/tmp/zircon-main-restored.png`。本次未重启客户端或服务端、未改游戏数据。打开截图可直接观察到：窗口位于客户端右侧，当前内容是 6 列×6 行格子、图标绘制在格中，右侧有可见的竖向滚动控件，底部仍有货币/操作区域；截图中 HUD、腰带、技能栏和小地图同时可见。此为 Zircon 当前运行外观的记录，不等于 EI 原版运行证据。

对照 `InventoryDialog.ApplyLegacyEiLayout()` 与 `DXItemGrid` 构造，源码确认仍创建固定 6×6 `DXItemCell` 视口；`DXItemGrid` 自身的滚动控件不能视为原版 F280 gauge 的实现。EI 侧的 46 条物品记录、600 WORD 占位表、6×6 hit viewport 与 F280 轨道结论仍以 INV-01/04 的 primary-static 研究证据为准。此次屏幕图没有独立证明当前右侧滚动控件的 RECT、滚动比例或帧资源对应关系，也没运行顶部/底部滚动、拖放、多格物品及鼠标边缘命中；这些继续列为未验收。窗口关闭后截图 `/tmp/zircon-main-restored.png` 可用于本机当次状态回看，但未纳入仓库证据附件。

#### MIcon 旧版与现代资源帧头比对（2026-09-24）

使用研究库 `wilsdk.py` 与 `zlsdk.py` 分别直接读取运行资源文件 `/home/tetsuya/mir3ei/LegacyEI/Data/MIcon.wil/.wix`、`/home/tetsuya/mir3ei/Data/MIcon.Zl` 的帧头。旧版 WIL 共1106帧、其中138帧有有效图像头；现代 ZL 共1773帧、其中224帧有有效图像头；同索引中两边均非空的54帧，宽、高、offsetX、offsetY四项无一完全匹配；旧版另有84个非空索引在现代ZL为空，现代ZL另有170个非空索引在旧版WIL为空。此证据说明它们不是可按同帧号和同画布规格互换的一套图集。这里只比较头字段，没有做54帧RGBA像素并排，也没有证明 `MagicInfo.Icon` 值与 EI 技能项 `[skill+6]` 对应哪个帧。

#### 技能栏图标资源路径补齐（2026-09-24，Zircon 侧）

追查发现技能书 `MagicCellView` 与常驻 `MagicBar` 直接从 `LibraryCache.Get(MagicIcon)` 取 `.Zl`，绕过 `MirSkin` 的旧版 WIL 回退。已将两处绘制统一改为 `MirSkin.GetTexture(MagicIcon, frame)`，从而让旧版模式使用同一 EI MIcon 资源。重新执行 `DISPLAY=:100 bash login_game.sh legacy` 后 TestHero 自动登录并进入 Sabuk Keep，日志确认加载 `LegacyEI/Data/MIcon.wil (1106 frames)`，并记录地图/角色首帧 `missingLibraries=0, missingTextures=0, emptyImageEntries=0`。技能栏运行截图 [`magicbar-magicicon-wil-runtime-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/magicbar-magicicon-wil-runtime-2026-09-24-1024x768.png) 可见常驻栏已实际绘制三个已绑定技能图标。

这只证明 EI WIL 加载与 Zircon 显示路径工作；没有原版同帧参考图，仍不能声称 `MagicInfo.Icon` 数值与 EI `[skill+6]` 完全同义，也没有验收不同窗口状态下的缩放、透明度及帧边界。技能书详情页、列表命中区、分类记录映射及 EI 窗口锚点继续阻断 SKL 验收。

**聊天记录 HUD 入口修正与回放（2026-09-24，Xvfb :100）：**原版 `hud-caption-action-tail-evidence.json` 明确 cap9 为 `push 8; toggle(8)`，窗口注册表为 chat-log；此前 Zircon legacy 回调却打开 `CommunicationDialog`（好友/邮件/屏蔽页）。已改为 legacy 点击切换 `_chatLog.Visible`，现代入口保持通信窗。`dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功（0 errors，3条既有 warning）；随后完整执行 `DISPLAY=:100 bash login_game.sh legacy`，TestHero 登录进图成功，贴图缺失计数为0。实屏点击 cap9 后聊天记录面板显示，再点隐藏；未出现 `CommunicationDialog`。截图 [`chatlog-hud-toggle-visible-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/chatlog-hud-toggle-visible-2026-09-24-1024x768.png) 和 [`chatlog-hud-toggle-hidden-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/chatlog-hud-toggle-hidden-2026-09-24-1024x768.png) 记录两态。该回放证明 Godot 入口目标和显隐往返，不证明其面板布局、历史列表、滚轮和输入逻辑等同 EI 原始窗口；HUD-04 的 cap9 目标错误已修，聊天窗自身继续未验收。

**2026-09-24 退出确认窗静态源码复核：**复读 F800/id`0x64` 初始化与鼠标分派研究记录、`ExitDialog.cs`、`GameScene.HandleKeyBind()`、`KeyBindManager.Defaults` 和现存 cap3 运行截图。源码确认 `ExitButton`/`LogoutButton` 均打开同一 `ExitDialog`，Alt+Q/Alt+X 也合并为同一 action；当前弹窗动作分别调用 `LeaveGame()` 和 `ExitClient()`，但没有 EI F800/F950 对应资源、文字或 primary-static 两条独立路径。原版 F800 子对象点击最终关闭主窗的研究结论尚未区分 YES/NO 热区，故本轮只记录为 EXIT-01 差异，不改映射，也不触发确认/登出操作。工具记录：一次针对 `window-catalog-evidence.json` 的 jq 查询选错数据结构，另一次窗口初始化查询字段写错，未得到数据；一次源码搜索和文件读取假设了错误路径。改读正确的 `window-initialization-evidence.json.records`、`GodotClient/Controls/KeyBindManager.cs` 与 `ExitDialog.cs` 后完成核对，没有更改代码或运行期状态。

**2026-09-24 NPC 背景裁切静态复核（旧记录，后于同日更正）：**重读 `npc-window-render-evidence.json`、`npc-body-strip-evidence.json`、`NPCDialog.cs` 与 `DXWindow.cs`。F1100 alpha边界 `(64,59,384,138)`、原版552×176根尺寸及当前完整512×256帧原点已登记；当时只检查窗口专用类而漏查`--legacy-ui`的`LegacyUiSkin.ApplyLegacyTestWindow()`包装，因而误写“当前窗口树未启用根裁剪”。应以本文件后续 NPC-04 矩阵更正及“legacy测试根裁剪复核”记录为准：实际启动路径会设根`Clip=true`并裁掉底部21px。原版 `0x43EE00` 另由 WIL 尺寸与384/138等参考量构造背景目标RECT，paint `0x43F06A` 消费该RECT；Godot裁剪事实不外推为原版同裁。现存证据目录没有 NPC 窗截图，故仍不推断目标 EI 最终画面。一次 jq 查询把证据结构假设为 `.controls[]`，实际控件数组是 `.child_controls[]`，查询报 null 后已按真实键名读取三个控件RECT；未做运行期输入。

**2026-09-24 WIL fallback 结论纠错：**复核 `MirSkin.GetTexture/GetSize/GetOffset()`、`GetLibrary()`、`GetLegacyWilLibrary()` 与资源目录后，发现 RES-01 旧段落错误称 `.wil/.wix` 不会自动回退，并把 Interface1c 缺 ZL 记成加载阻断；这与同文 WIL fallback 验证记录、选角加载日志和实际源码相矛盾。现已将覆盖矩阵及 RES-01/02 改为区分 ZL优先/WIL回退、`MirSkin`/`LibraryCache` 两条入口与各自资源根：Interface1c 有 WIL fallback，Interface 图集仍缺；纸娃娃 `LibraryCache`、Equip 和普通 StoreItem 等路径不会因此自动读取 EI WIL。已有 F50/F51 及 GameInter 12帧比对只支撑样本结论，全库和控件状态仍未验收。本轮一次宽上下文补丁因目标行上下文不匹配未应用，随后按表格行标识精确改写并通过 `git diff --check`；两次源码索引查询曾把不存在的 `ClientLibrary/`、`ServerLibrary/Network/` 当作搜索根，`rg` 报路径不存在，改用实际文件路径继续静态核查。未运行新的测试或运行期输入。

**2026-09-24 F51/腰带资源静态复核：**根据工作区未提交的 `BeltDialog.ApplyLegacyEiPotionBeltLayout()` 与 BELT-01 重新检查资源归属。`localhost:8766/api/info` 无法连接；独立解码第一次因系统 Python 缺少 Pillow 失败，改用临时 `nix-shell -p python3Packages.pillow` 后成功读取 WIL header、offset 与 alpha bbox，并将 F51 解码 PNG 固化在本审计证据目录。图像确有六个横向凹槽，但目标 EXE 的原版窗口 owner/控件映射仍未证实；primary-static 的 6 个 38×38 D44 rect 目前记作对角序列，尚不能与 F51 横排凹槽直接等同。另发现 `UITestScene.cs` 把 GameInter F51 标作“经验条”，而 `hud-bars-render-evidence.json` 把经验填充绘制闭合为 F63 (164×6)，F51 WIL图像也明显不是细进度条；已在 BELT-01 中将该字符串标为错误诊断标签，不作为素材归属证据。本轮两次索引命令曾搜索不存在的研究工件 `hud-hotkey-target-system-evidence.json`（可用资料为 `chat-window-control-map.json` 等），一次把 `LegacyHudLayoutLab.cs` 错放在 `Controls/` 而非 `Scripts/`；工具报告路径不存在后已用 `rg --files` 与实际路径继续。本轮没有修改未提交的腰带/游戏代码、没有运行期输入，也没有提交或推送。


**2026-09-24 登录对象按钮静态对照：**重读 `login-flow-evidence.json → screens.char_select` 的 phase 与 buttons、EI `Interface1c.wil` F11–F17 帧头/像素 alpha，以及 `LoginScene.BuildLegacyLoginUi()` 和对应 handler。新增 PRE-09：研究对象 `char_select` 的 JSON键名容易误导，但该对象的 phase 明确为 login form/server-list/transition；F13/14与F15/16的静态动作是读INI URL、打开网页并销毁客户端，当前同名概念却在自制 F151 登录框内开 Godot 表单并发 Zircon 注册/改密包，排行/设置/激活/找回也没有该 EI 对象上的已证原版动作。F17/18保持候选；F11/12点击最终分支仍需同版原件复核，不能从标签猜登录提交细节。本机 WIL 经研究 `wilsdk.py` 头部读取及临时 `nix-shell -p python3Packages.pillow` 解码：F11–17尺寸 96×24、96×26×4、48×26×2，alpha bbox 为半开像素框 `(0,0,94,24)`、`(0,0,95,26)`×4、`(0,0,46,26)`×2，offset 全为 `(-24,-16)`。一次起始 `curl localhost:8766` 返回连接失败，故改用本地 WIL/WIX独立解码；首次编写 PRE-09 时把 `screens.char_select` 键名误读为登录后选角阶段，复查 phase 后纠正；本轮直接解码又发现旧工件称 F17 无图的记录与现存 F17 像素冲突，已撤回“blank”描述并保留字义候选。一次 `rg` 正则因未转义 `+` 报语法错误，改用固定字符串继续查；没有据此终止审计。未启动登录客户端、未进行输入/网络请求、未改UI代码、未提交或推送。

本轮交付：审计文档单独提交并推送；四个既有 UI 源码改动与 `docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md` 未纳入。

**2026-09-24 物品提示静态对照：**以 primary-static `item-tooltip-and-store-family-evidence.json` 的背包 `0x42FAB0→0x42F240→0x4341F0` 和当前 Godot `DXItemCell`/`GameScene.UpdateMouseItem()` 逐段比对，新增 ITEMTIP-01。EI 背包提示由占用格命中后传入指针偏移坐标，经独立行记录绘制 15px 行距、`0x329696` 底板、按类型条件绘制的图标并裁到800px；Godot 对通用 `DXItemCell` hover 即更新同一个全局 `_hoverItem`，每帧按(+14,+10)显示单色文本标签、近黑底/棕框，没有 EI 的图标/clip/窗口所有者门控。可用旧版 `Client` 的 ItemLabelBuilder/一秒刷新是其 `MouseItem`（拿起物品）源码链，不可用来替目标 EI 鼠标悬停链背书。`bag-tooltip-verification-evidence.json` 的“closed”是模拟器范围，不是 Zircon 验收。一次针对目标 EXE 的重放仍受目标构建原件缺失约束；本项静态差异可证，但 hover 时延、各容器触发 owner 与原版像素仍列入验收。未做鼠标/输入或交易运行测试，未改游戏代码。

本轮 ITEMTIP-01 审计文档已单独提交并推送；现存四个 UI 源码工作区改动及 `docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md` 未纳入。

**2026-09-24 物品提示跨窗口静态续审：**按 `status-window-render-evidence.json`、`store-window-render-evidence.json`、`trade-window-render-evidence.json` 与 F341 的 trade hover 摘要补出背包、状态/装备、商店、交易和其他当前容器调用表。关闭了两个可能误推广的说法：交易有独立 `0x416830/0x4162E0` hover helper，但现有摘录没有独立列出与背包一致的 renderer 参数；EI 商店 F340 的26槽只是首数组/网格，不是全窗总量，RESEARCH_LOG 后续 `0x44D180` reset 链把多个数组合计修正为约90槽，仍需各状态独立映射。另外按 `NPCGoodsPanel.RefreshRows()` 核实其 NPC 商品显示为 `DXButton + DXImageControl`，不经过 `DXItemCell`；现金商城商品槽、交易和人物格则存在 `DXItemCell` 路由。**再核 `RESEARCH_LOG.md` Round 103/34 的状态窗调用后，修正一处容易混淆的资源flag：**状态窗鼠标 hit链调用 `0x4341F0(x,y,0)`，提示自身flag为0；el82/el83 的分流来自 status paint 的 `0x430A40` 装备/人物图像绘制链，不能外推到 tooltip 图标。更新 ITEMTIP-01 表，记录这种同一窗口内不同调用链。过程中以结构化查询提取各 JSON 中 hover/mouse 记录后核回原文；未运行游戏或触发输入，未改 UI 代码。`git diff --check` 待提交前执行。

本轮 ITEMTIP-01 跨窗口静态续审已单独提交并推送；既有四个 UI 源码工作区改动及 `docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md` 未纳入。

**2026-09-24 任务窗里程碑通知静态续审：**追查 `QuestDialog` 构造/Close/页签回调/`RefreshPage()`、`WindowManager.Toggle()` 与 `SConnection.Process(C.MilestoneNotify)`，新增 QUEST-04。普通任务页 X、Ctrl+D 关闭均走 `Close()` 并发送 `MilestoneNotify(false)`；仅里程碑页刷新发 true，离开 page3 的页签回调另发一次 false，双 false 调用路径确定。服务端将包值写入 `ReceiveMilestoneUpdates`，但仓库代码没有该字段读取点；故将“改变实际推送订阅状态”的旧结论更正为“发送包并覆盖字段，业务效果未证”。EI 已保存的 F700 primary-static 控件链没有对应 Zircon 里程碑包证据，继续将它隔离为 Zircon 扩展。遵从本轮限制，未重试坐骑 S/Ctrl+S、潜在 Bad Request 输入或单人伪造交易；只读源码与已有证据，未触发协议或运行键鼠/游戏测试。

**2026-09-24 技能书 F 键静态续审：**回查 `skill-window-render-loop-evidence.json`（primary-static）确认其闭合范围是技能选择与 Magic.exp 右页渲染，现存技能输入证据未覆盖 F1–F12；可用 `Client/Envir/CEnvir.cs` 及 `Client/Scenes/Views/MagicDialog.cs` 的 F 键行为只属于较新 Client 源码。继续沿 Godot 当前代码核 `GameScene._Input()` → 可见窗口早退 → `MagicDialog` / `MagicCellView._UnhandledKeyInput()`：窗口打开时全局施法/栏组动作先被窗口门控截断，而两个控件处理器使用不同目标（选择态与悬停态）；修订 SKL-06，加入组合键忽略、echo过滤与处理标志差异，并将 Godot 实际分发顺序保留为待观测，不从源码节点顺序推断。初次读取误指向不存在的 `skill-window-input-evidence.json`，工具返回文件不存在；随后以 `rg --files` 定位实际存在的 `skill-window-render-loop-evidence.json` 和 `skill-window-context.json`，继续完成比对。按用户要求未运行技能键、坐骑键、可能触发 Bad Request 的输入或双人交易；未改游戏代码。

**2026-09-24 人物装备命中静态续审：**核对 `equipment-slots-evidence.json` Finding 265 的最终 11 槽映射、`status-window-render-evidence.json` 的原版11条 RECT/wire链与当前 `CharacterDialog.ApplyLegacyEiLayout()`、`PaperDoll`。确认原版索引0/1/4是武器、衣服、项链三个大矩形交互记录，raw index 会作为线上 slot byte；当前 profile 将这三只 `DXItemCell` 隐藏，只显示另外8个小格，`PaperDoll` 为 `MouseFilter.Ignore` 的纯绘制控件，无替代输入处理。将 CHAR-04 从一般待核提升为“静态可证的三处交互目标缺失”，并给出按原矩形保留hit target、再验拖放与wire byte的验收项。只读追踪，没有改动人物/槽位实现；保留所有已有未提交修改。

本轮 QUEST-04 审计文档单独提交并推送；既有工作区代码与工具状态文件未纳入。


**2026-09-24 历史迁移计划状态校准：**复核 `LEGACY_UI_MIGRATION_PLAN_2026-09-22.md`，发现五阶段及“核心窗口迁移”仍标作已完成，与当前逐窗审计中已证差异和阻断项矛盾。保留历史阶段记录，在原计划开头说明其不等同于 EI parity 验收，并在本文添加交叉索引。未改实现；`git diff --check` 通过。


**2026-09-24 组队移除交互静态续审：**对照 `social-window-render-evidence.json`、`group-window-detail-evidence.json`、RESEARCH_LOG F845/F948 与 `GroupDialog`/`GameScene.SendGroupRemove()`/服务端 `PlayerObject.GroupRemove()`。EI 已有 primary-static 证据确认成员按插入顺序绘制、五个子控件受 `0x424770` 输入遍历，移除按钮命令走 `0x3FD/0x452350`，并存在独立的删除成员名字提示串；本次可用摘录没有成员行 hit RECT/点击回调。Zircon 现行标签却可选成员，并把 object id 转成当前 `_objects` 的 `DisplayName` 后发 `C.GroupRemove`，服务端再按名字查队员。新增 GROUP-05 将其定为未证等价的当前扩展路径；不推断 EI 3.0 必然以什么窗口呈现字符串，保留 0x3FD 输入分支和 frame6 对话框的对应关系待查。只读核源码和已有研究摘要，未打开组队窗、未运行输入或发包；保留工作区修改。


**2026-09-24 聊天热键、鼠标入口与id7导航状态复核：**用原版 primary-static `window-paint-and-hotkey-dispatch-evidence.json`/HUD caption 证据和当前 `KeyBindManager`、`GameScene._Input()/HandleKeyBind()/IsWindowShortcut()` 交叉核 CHAT-02。cap9 鼠标入口已转向 `_chatLog.Visible`，但裸 R 仍解析为 `RankingWindow`，而 legacy `_Input` 对该 action 执行排行榜开关；Ctrl+R 没有默认键位。故明确鼠标入口修复不覆盖原版 id8 的 R 热键，CHAT-02 与 id8 导航行已交叉引用 KEY-01/02；同时更正 id7 行的过期说法，说明 legacy cap9 不再打开 `CommunicationDialog`，且 `_chatLog` 与 id7/id8 的完整等价关系仍未证。只读源码与已有 primary-static 资料，未运行 R/Ctrl+R 或打开窗口测试；不改实现。

**2026-09-24 id7 几何与消息入口静态复核：**`window-id-catalog.json` 的 id7 工件记录 F200 根矩形 `[560,0,328,244]`、文本锚点偏移 `(0x61,0xC8)`；按工件所用 800×600 逻辑画布记为 328×244，不能直接拿它与不同窗口缩放下的像素截图比较。当前 `ChatLogPanel` 构造尺寸为 400×150；`GameScene.LayoutHud()` 将其放在主面板左边缘上方 29 px（视口不足时坐标钳到 0），因此现有层的位置锚定和原版右上 F200 区域明显不同。`GameScene.ReceiveChat()` 将其调用路径中的文本、消息类型追加到 `_chatLog`；原版工件则记载 id7 从独立消息对象/环形记录读取，具体记录来源角色仍只是候选。故将 id7 判为“无已证实实现”，不能把常驻日志的消息用途相近当作窗口映射完成。id8 仍单独对应 F350 聊天弹窗，维持 CHAT-02 的阻断结论。本轮只读源码/研究工件并更新导航索引，未启动游戏、未发送热键或窗口输入。

**2026-09-24 背包控件输入静态续审：**将 `inventory-mode-tabs-evidence.json` 与 `inventory-window-render-evidence.json`、`InventoryDialog` 对照。后者早先仅把 F161/162 矩形标作“close/confirm candidate”；EI-288 后续证据把该控件确定为 `this+0x5C` 三个装饰子控件之一，单击只播声音，`0x4300F0` 不改模式。当前Godot却把F161/162作为关闭回调，故将INV-02升为静态输入差异；F264/265的位置和尺寸对应旧版 hit RECT、但当前也仅有默认按钮声；F267/268第三控件缺失，资源属 Interface1c，不能由帧外观断言为通用按钮。服务端驱动模式的语义结论保留，Zircon页面响应/模式复位映射仍待闭合。本轮只读源码与已有primary-static工件，未点击背包控件、未运行修补/出售/仓储流程。

**2026-09-24 用户指定 GameInter F168 素材续审：**确认 8766 当前端口无 listener、首页 curl 连接失败；不重启预览服务，转由本地 `wilsdk.py` 从 EI 原始 WIL 解码 F168/169/171/172，并用临时 nix-shell 提供 Pillow（第一次默认 Python 调用因缺 Pillow 失败，重试成功）。记录四帧头信息、alpha bbox 与 PNG contact sheet；与 `status-window-render-evidence.json` 的 state/frame 映射和当前 `CharacterDialog.ToggleLegacyView()` 交叉核实，原版装备态用左箭头 F168/169、属性态用右箭头 F171/172，当前两态帧选择一致。素材帧本身不是屏幕坐标证据；CHAR-02 的屏幕锚点、瞬态与按键状态仍待运行验证。本轮未启动游戏或注入输入。

**2026-09-24 背包 F267/F268 资源冲突复核：**静态 primary-resource 摘要将 F267/268 绑定到 Interface1c 的人物动作资源，但声称 F268 在“当前导出”为空。本机 `LegacyEI/Data/Interface1c.wil/.wix` 不支持该空帧结论：独立标准库 WIX/WIL 头解析表明两项有非零帧偏移和范围内负载；临时 nix-shell 的 `wilsdk.py` 像素解码得到 F267 76×88、F268 60×106，放大 contact sheet 里两帧都是装甲持武器人物。记录 WIL/WIX 哈希和PNG证据至 INV-03；研究 NAS资源路径当前不可读，故暂不能判断是版本/导出差异还是旧研究说明错误，也不把本机素材直接认作目标原版素材。对照 `InventoryDialog` 仍确认当前没有该控件，但其旧版绘制/命中及 hover 帧裁切含义待静态追踪；本轮未运行 UI 或点按。

**2026-09-24 现代 ZL 交叉核验：**直接用 `zlsdk.py` 解析 `/home/tetsuya/mir3ei/Data/Interface1c.Zl`（count=3020, version=0），F267/F268 在头表均缺失且 `is_blank=True`；与本机旧版 `LegacyEI/Data/Interface1c.wil` F267 76×88、F268 60×106 的非空人物图相反。将此证据加入 INV-03 和阻塞清单：现有旧 WIL 与现代 ZL 明确是不同帧内容，但研究 EXE 的 NAS WIL/WIX 不可读，故目标 EI 资源身份仍未闭合。只做资源头表读取及静态文档整理，未运行游戏、热键或业务输入。 |

**2026-09-24 技能书证据范围续审：**把 `Mir3-Research/docs/research/mir3-map-reconstruction/` 中 F547/F839/F848/F939 的实际 JSON 路径与 `ei-ui-layout/skill-window-render-loop-evidence.json` 对照，并只读比对 `MagicDialog.ApplyLegacyEiLayout()`、`BuildLegacySchoolButtons()`、`BuildLegacySkillSlots()`、`Refresh()`。确认 F547 的八分类页签、F848 的类别列表绘制和 F939 的鼠标输入摘要分别覆盖不同调用范围；F939 的 “COMPLETE” 不表示整窗 UI 完成。当前 legacy 源码隐藏三组头部帧控件及 modern 列表/滚动条，仍创建 12 个 F410..F421 命中格；与原版已证实三头控件、八类别控件、六个列表 hit RECT 的结构有静态差异，SKL-01/02/04 维持阻断。两次查询把 `skill-window-input-evidence.json`、F547/F848 JSON 误认为位于 `ei-ui-layout/`，导致 FileNotFoundError；随即用 `rg --files` 找到实际 `mir3-map-reconstruction/` 路径并继续核证。无代码改动，未启动客户端、未注入键鼠、未运行测试。

**2026-09-24 人物窗展开布局字段续审：**重读 `status-window-render-evidence.json` 与工作区未提交的 `CharacterDialog.cs`。原版 base paint 的背景位置消费 `[this+0x08]/[this+0x0C]`，而属性文字、装备绘制及 hit-test 以 `[this+0x18]/[this+0x1C]` 为基准；切换链 `0x44CCD0→0x423E80` 在保留主 x/y 的同时把窗口尺寸改为 520×328，并记录 `this+0x20>0x320` 时于 `x+0x118` 再 reframe F201。当前工作区以同一个 Godot 窗口 Location 加 `_background.Location`/Clip 表达单张背景，未实现第二次 reframe；同一面板 crop 的逐像素相同不能替代背景原点、属性/装备命中原点和第二次重定位核对。该项并入 CHAR-02 未决，未改代码/运行截图。一次精确补丁因原文锚点格式未匹配而未应用，改用当前文本定位后完成；`git diff --check` 通过。

**2026-09-24 未提交工作区与交易请求语义续审：**先核 `git status` 和每个既有差异：`BeltDialog.cs` 增加 EI F51 六槽外观候选、`CharacterDialog.cs` 改 F200/F201 单背景切换、`GameScene.cs` 改 cap0 为 `C.TradeRequest`、`LegacyHudLayoutLab.cs` 增加腰带/展开窗审计入口；未跟踪 `docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md` 是当日端口快照。这些文件都保留未动。随后对照 `hud-caption-action-tail-evidence.json`、`C.TradeRequest` 与 `PlayerObject.TradeRequest()`：EI `0x41EC10` 在客户端按坐标/方向选定实体，并把该实体地址写成 `0x401` 的参数；Zircon 请求包不含目标，服务端从朝向前一格取首个玩家并要求相向。故未提交 `GameScene.cs` 的“服务器解析同一个 facing cell”注释属未经证实的等价假设，已在 HUD-04 说明；不改源码，也不执行双人或 Bad Request 风险测试。仅完成静态比较和 `git diff --check`，本轮无提交/推送。

**2026-09-24 小地图几何分层续审：**重读 `map-ui-resource-evidence.json`、EI-310 `minimap.json` 与 `RESEARCH_LOG.md` Finding 57/74/80/91，并只读检查 `MiniMapDialog.SetMap()`/Panel/Resize 代码。裁决几何术语：屏幕目标始终为 `owner+0x2C0=(672,0)-(800,128)`（128×128，primary-static SetRect）；初始合成 surface 为128×128，T 分支切换到256×256或回到128×128（primary-static exact call），两种内部 surface 共用相同的固定目标框，不能把“256模式”理解成256×256屏幕窗口。原始 MMap/FMMap帧另有各自尺寸/offset；选择后保存帧源尺寸，在 `0x43D5F0` 受限视图状态及 `0x43DA80` 归一化 helper `0x466800`/合成器 `0x4542F0` 经源裁剪和视图位置生成目标，`owner+0x2D0/+0x2D4` 为内部源偏移，不能与屏幕Rect混用。当前 `MiniMapDialog` 的200/300独立窗口、`Image.Size`地图缩放与Panel clipping不等于该链；F帧身份映射、像素变换和游戏中视图更新仍待同态验收。marker颜色/尺寸的机器码可核，绿/黄等标记业务名仍应按各自证据级别保留，不凭颜色命名。本轮仅静态核查/文档记录，未测试T/V/HUD鼠标输入、未启动游戏；`git diff --check` 通过。

**2026-09-24 小地图帧头独立复核：**为交叉核对 MAP-01 的 EI 资源记录，直接以标准库 `wilsdk.WilLibrary.header()` 解析 `/home/tetsuya/mir3ei/LegacyEI/Data/FMMap.wil/.wix` 与 `MMap.wil/.wix`，不依赖当前 8766 viewer listener。FMMap 共31帧，F17=600×600/F7=600×400（offset均−24,−16）；MMap共255帧，F7无帧头。记录两库SHA-256于MAP-01。该项独立核实WIL帧头和空帧，但不自称独立视觉解码或运行画面对照；资源帧与 `MapInfo.FileName`/服务端 `MiniMap.txt` 的语义连接仍按现有证据等级分开。未启动游戏、未打开8766服务或触发T/V输入。


**2026-09-24 技能书导航摘要一致性修订：**将 id14 导航行中过期的“末三分类帧错”移除，并明确当前八类帧序仅有静态证据支持；代码映射为 F450/452/454/456/458/460/462/464，但实际 EI 的选中/按下外观与帧视觉仍未验收。技能窗 12 个候选格、右页详情及快捷栏差异继续保留在 SKL-01..09。只修审计摘要，不改游戏代码，不运行运行期测试。


**2026-09-24 INV-04 gauge 源码对照续审：**逐项核对 `inventory-window-render-evidence.json` 的 `0x4179B0` 参数和 `InventoryDialog` 的 `ApplyLegacyEiLayout()`/`DrawWeightFill()`。原版子对象是 GameInter F280 垂直 gauge（16×424源帧、12×218填充区、max=94）；该构建数据字段固定为零意味着空填充，不意味着不绘制轨道或没有命中对象。当前 Godot legacy 路径只把旧 F360 控件整项隐藏，常规绘制函数仍读取 Zircon 背包负重并以 F360 水平填充；因此 INV-04 从“缺 gauge”进一步明确为“源帧、轨道绘制、控件命中模型均不匹配，且当前比例填充属于不同语义”。原版绘制调用的 x/y 相对父对象，转换到最终屏幕坐标的链尚未追完，保留为几何阻塞，暂不据候选坐标移动控件。只按已记录的构造根尺寸284×324与F280源画布16×424作边界算术：调用原点偏移(+248,−165)意味着未裁剪源矩形根相对`x=[248,264), y=[−165,259)`；若基类确实裁到根窗，交集候选为`x=[248,264), y=[0,259)`。这只是由两个静态尺寸导出的裁切候选，不能证明 base Paint 的clip、gauge内部12×218填充RECT或实际屏幕位置。须继续追 0x423D00/0x4179B0 的目标surface/clip实参和父窗原点后才可定案。只读源码与已有 primary-static 工件及本机已保存的F280帧预览，未启动客户端、未做输入或运行期测试。


**2026-09-24 EXIT-01 F800 alpha/root 几何交叉计算：**对仓库证据图 `evidence/legacy-ei-ui/ei-gameinter-f800-primary-resource.png` 重新做 alpha-only bounds（ImageMagick，独立于窗口布局自测），核得画布512×256、有效像素 `361×183+74+36`。与研究工件的静态根矩形 `(218,176,364,184)` 相比，若仅为预览而将 alpha 左上角对齐根左上角，则源画布原点候选是 `(144,140)`，有效像素右/下比根框分别少3/1 px。此算术只建立视觉候选：不能证明 EI 的 `0x423D00` 按 alpha bbox 锚帧，也不能忽略 WIL 头offset `(7,-44)` 或目标窗口clip。现有 `ExitDialog` 使用 F281，故没有可与该候选作同帧几何比对的 Godot F800画面；需先追 F800 的原版绘制调用/坐标换算，再核按钮 hit rect，且不执行确认/退出运行测试。未修改窗口实现，`git diff --check` 通过。


**2026-09-24 WH-01 F1001 裁剪结论更正：**沿`GameScene.ApplyLegacyCoreTestLayouts()`→`LegacyUiSkin.ApplyLegacyTestWindow(_storageDialog, ...)`→`StorageDialog.ApplyLegacyEiLayout()`核执行顺序，确认`--legacy-ui` wrapper 在方法分派前设`window.Clip=true`；`DXControl.Clip`源码直接映射`ClipContents`且定义为裁剪子控件。配合F1001 256×256、alpha bbox `(28,26,198,204)`及205×205根尺寸，Godot legacy 根内实际可见alpha交集为`[28,205)×[26,205)`，右侧裁21 px、底部裁25 px。撤销 WH-01 中“无Clip/是否裁剪待运行确认”的旧表述；改为Godot当前裁剪已由源码闭合，EI端同一有效边界是否使用相同锚点/clip仍未证。只读调用链及源码，没有运行窗或存取操作；`git diff --check`通过。


**2026-09-24 legacy 测试根裁剪复核（SET-05/GUILD-01/NPC-04/QUEST-01）：**发现这些窗口此前仅检查各自`ApplyLegacyEiLayout()`是否显式设`Clip`，漏查`bash login_game.sh legacy`的共同调用包装。`login_game.sh`在`legacy`模式加入`--legacy-ui --legacy-hud`；`GameScene.ApplyLegacyCoreTestLayouts()`逐窗调用`LegacyUiSkin.ApplyLegacyTestWindow()`，该函数在分派到窗口专用布局前将根`Clip=true`；各窗口布局没有清除此状态，`DXControl.Clip`实际映射Godot`ClipContents`。因此修正文档：F750底部9px、F600右侧362px、F1100底部21px会被当前根窗裁切；Quest F700 profile的根外控件也会被裁切。该源码事实只描述当前 Godot legacy 测试树，不能替代EI原版绘制器/目标根窗口的裁切证据。覆盖原先针对上述项“无Clip/会越界或待viewport确认”的不准确表述。只读调用链、代码和已存资源bbox，没有启动客户端或做相关输入；`git diff --check`通过。


**2026-09-24 NPC store 子面板层级续审（SHOP-01）：**沿`NPCDialog`构造和`ShowPage()`确认`NPCGoodsPanel`、`NPCRepairPanel`、`NPCAdvancedPanel`都由`AddControl()`直接挂在NPC根下，ShowPage把对应面板位置设为`(0,Size.Y)`。`bash login_game.sh legacy`→`--legacy-ui`→`ApplyLegacyCoreTestLayouts()`先对根设`Clip=true`；NPC布局/ShowPage不清除，根高固定176。因此goods/repair/advanced子窗的起始Y恰在clip下边，后代绘制均被裁掉；先前只列142–399px动态高度而未交代其不可见状态，容易误当成运行时已显示。该控件树与 EI F1000/1001 属同 store 对象、独立层级的 primary-static 证据不一致。将设计任务标为窗口重构/正确重挂层级，避免只修子窗尺寸；Clip裁绘不据以断言MouseFilter命中。未运行NPC页或存取/交易操作；`git diff --check`通过。


**2026-09-24 QUEST-01 根裁剪有效范围续审：**按`QuestDialog`构造的子控件树核对：`_detailPanel`在根相对`(380,5)`、宽300，滚动条在`(704,58)`；`--legacy-ui` wrapper把根设为340×440并启用ClipContents，`ApplyLegacyEiLayout()`不重置。故两者在legacy测试布局中整体处于根外，实际绘制完全被剪掉，当前右页不是局部露出而是整块不可见。以前矩阵虽记其坐标超宽及wrapper会裁剪，但未把这一范围结论写清，现已明确。根Clip对输入 hit gating 的影响未从Godot事件代码单独验证，且EI滚动/详情的目标位置与裁法需原版证据，均不外推。`git diff --check`通过；只做源码几何核对，未运行任务窗口或业务输入。


**2026-09-24 用户指定 WIL 预览入口恢复：**检查 `:8766` 时没有旧 viewer 进程。用 `/home/tetsuya/development/Mir3-Research/Tools/web/wilviewer.py` 在临时 `nix-shell -p python3Packages.pillow` 环境，以 root `/home/tetsuya/mir3ei/LegacyEI` 在 `:8766` 启动服务；没有安装永久系统依赖。第一次 HTTP 元数据探测按旧记忆传 `file=GameInter.wil` 得到404；查看工具路由后确认 API 参数是 `f`，正确请求 `/api/info?f=GameInter.wil&i=168` 成功返回 F168 画布36×36、offset(-24,-16)、1404 words。预览首页 `http://localhost:8766/` 返回正常。此工具参数错误已纠正，无需列为项目阻塞；用户给的 hash 路由继续可用于逐帧查看。未运行游戏或输入测试。


**2026-09-24 查看器阻塞状态再校准：**再次直接请求首页和 `/api/info?f=GameInter.wil&i=168` 均成功，并重读“当前待验证/阻塞项”表；上一轮恢复记录后活动阻塞表曾遗漏更新，错误地继续写“8766当前不可连接”。现已将该项改为已解除并保留状态事实。另有更早的“用户指定 GameInter F168 素材续审”段落记述了当时没有 listener、转由 `wilsdk.py` 解码，这是当时有效的历史记录，不代表当前服务状态；不据此取消 WIL/WIX 独立核验作为交叉证据。服务当前由临时 Nix shell 进程提供，没有永久安装依赖。未运行客户端或输入测试。


**2026-09-24 SET-01/05 旧结论与主表一致性复核：**检查阻塞表时发现 8766 已恢复但活动表未改；修正为已解除，并以当前首页/API 请求复验。随后逐行核设置审计主表，发现 SET-05 仍引用早先只检查 `ConfigDialog`/`DXWindow` 专用类所得的“允许溢出”；它与实际 `legacy` 启动包装及后续 SET-05/F750 裁剪记录相反。现将 SET-05 主表改成当前调用链结论：`LegacyUiSkin.ApplyLegacyTestWindow()`先启用根 `ClipContents`，F750 超出根底部的9个有效像素在Godot测试树中被裁，EI自身裁法另待证。设置证据初次 jq 查询假设顶层含 `.window.option`，只返回 null；读取顶层键后发现结构实际为 `.windows[]`，改用 `id=="window.option"` 读取完整控件、RECT、配置和滑杆证据。再对照 `settings-ambience-bgm-volume-evidence.json` 的 Ambience 分派与当前 `ConfigDialog`，更正 SET-01：原版 Ambience 点击只切帧并调用共享保存，但不改 `this+0x5C`、保存时回写旧值；当前只改窗口局部布尔，不调用 `ClientSettings.Save()`。新增低优先级 SET-07，要求核该配置写盘副作用，不擅自把它变成可写 EI 状态。只读证据与源码并更新文档；未改游戏实现、未跑客户端/音效或输入。`git diff --check`通过。


**2026-09-24 设置覆盖总表摘要校准：**把“设置/主菜单/退出/帮助/消息窗”总表仍称“四项开关映射已证实实现不符”的过期摘要，按 SET-01..07 当前逐项状态改为“F750四行位置/状态已接入，BGM/EffectSound 有路由，Ambience保存、ShadowBlend消费者、声学曲线、根裁切仍待验”。这次只修跨表导航摘要，没有把未完成的设置窗口升格为 parity 通过；`git diff --check`通过。


**2026-09-24 CHAT-03 `+` 输入分派静态续审：**重读研究构建 `chat-input-command-dispatch-evidence.json` 的 `0x41ED20/0x41E740` 记录、Godot `ChatTextBox.SubmitChat()`/`GameScene.SendChat()`、`SConnection.Process(C.Chat)` 与 `PlayerObject.Chat()`，确认当前 `+...` 没有专用 parser 分支并最终走普通消息广播；研究构建则进入独立、带时间门控的 trade/counter 函数。保留524,288-byte研究EXE与本机EI EXE身份不一致的版本限制，因此没有把研究结论写成目标EI无条件事实；也明确区分该路径与HUD cap0的玩家交易请求。本轮 `rg` 搜索误将不存在的仓库 `Library/` 目录作为搜索根，返回路径错误；改对实际存在的 `ServerLibrary/Envir/SConnection.cs`、`ServerLibrary/Models/PlayerObject.cs`、`Client/Scenes/Views/ChatTextBox.cs` 和 `GodotClient/` 代码逐层核实。未发送 `+` 聊天输入或任何网络命令，未运行客户端/测试；最终 `git diff --check`通过。


**2026-09-24 CHAT-04 F350 六控件与当前 mode selector 静态对照：**复核 `chat-window-render-evidence.json` 的 `original_command_strings`、`channel_command_state` 和 `hit_dispatch`，并对照 `chat-window-unified-model.json` 与当前/可用 Client 的 `ChatTextBox`。确认六个 EI 控件各自为拒绝私聊、世界喊话、组队喊话、行会喊话、拒绝私聊开关、拒绝行会聊天开关；每项有各自36×34 RECT、状态帧和点击写入的命令模板。`control+0x34`帮助字串只在 hover 绘制，不能当成静态按钮文字或 click payload。当前七态 Local/Whisper/Group/Guild/Shout/Global/Observer 输入模式只和部分 EI 前缀重合，不等价于六个独立命令/拒绝控件；将这个差异写入 CHAT-04，并把 CHAT-02/导航矩阵改成精确动作表述。一次 `jq` 查询访问不存在的 `.channel_controls`，得到 null；查看顶层键后从 `.controls[]` 及相邻 state/dispatch 节读取数据。此前一次 `rg` 搜索把不存在的 `Library/` 当搜索根并报告路径错误，随后改用 `GodotClient/`、`Client/`、`ServerLibrary/` 实际目录完成对照。没有改代码、运行游戏或输入任何聊天命令；`git diff --check`通过。


**2026-09-24 展开面板证据与本轮范围收口：**重新查看 `gameinter-frame-168-172-wil-2026-09-24.png` contact sheet，确认只可支持箭头方向/状态帧识别；逐读 CHAR-02 与阻塞清单，新增人物窗逐帧几何阻塞项，明确现有裁切截图不能证明屏幕原点、按下瞬态、根窗/内容/命中坐标及 reframe。保留工作区 F200/F201 切换、F51腰带候选及 legacy HUD 修改作为未验收实现，不将自审计通过或素材相同升级为 parity 通过。按本轮限制没有运行 S/Ctrl+S、交易、聊天前缀或其它可能造成 Bad Request 的输入；没有启动客户端。此次命令没有发生工具/API错误。`git diff --check`通过。


**2026-09-24 CHAR-03 原版动态值与 Zircon 数据包静态交叉核对：**重读 `status-window-render-evidence.json::attribute_text_draw_chain.value_field_sources`、`network-message-object-anatomy.json::msgid_0x34`、`hud-hp-mp-xp-injection-chain.json`、`ServerPackets.cs`、`PlayerObject` 入场发送路径、`ServerConnection.Process(S.StatsUpdate)`、`GameScene.OnStatsUpdate/OnWeightUpdate` 及 `CharacterDialog.RefreshLegacyAttributeLabels()`。确认可静态断言：当前两组legacy标签把 HP/MP 都显示为 `PlayerStats` 上限值，没有读 `_currentHP/_currentMP`；原版有独立 cur/max 对并另有 EXP 与负重字段；Zircon 数据经 `StatsUpdate`、`WeightUpdate`、玩家状态/经验消息分开承载，并非旧版97-byte属性块布局。原版剩余属性 globals 到 Zircon `Stat`/状态字段仍需逐项映射，未据同名标签推断等价。本次一次 `rg` 搜索误指定不存在的 `ServerLibrary/Envir/Packets/` 目录，命令返回路径错误；改用 `rg --files ServerLibrary` 找到真实的 `ServerLibrary/Envir/SConnection.cs`、`LibraryCore/Network/ServerPackets.cs` 后继续核对。一次大补丁的长段落锚点不匹配，未应用任何部分修改；改在已核实的“属性文字尚未闭合”段落插入数据流说明。已查看既有展开截图，确认它只显示当前实现与 `STATUS` 标题重叠；不当作原版基准。另校正 CHAR-03 坐标描述：原版属性偏移相对 `[this+0x18]/[this+0x1C]` 的内容基准，背景却相对 `[this+0x08]/[this+0x0C]`，不能误写成已知的 Godot 根窗坐标。未改游戏实现、未运行游戏/测试/业务输入；本轮文档 `git diff --check`通过。


**2026-09-24 CHAR-03 逐组字段映射台账：**从原版 `value_field_sources` 导出并复核 LEVEL、HP/MP、经验、包袱负重、腕力/准确/敏捷/毒物与恢复、防御/攻击/魔法/元素/魔法防御等地址宽度与格式；对照 Zircon `LibraryCore/Stat.cs`、`GameScene` 的玩家状态/当前值/经验字段、`ServerConnection` 包处理及 `CharacterDialog` 两态显示列表。将现有候选与已确认差异整理为表：HP/MP当前值可用但 UI 未显示；经验当前/上限独立存在但只接入 MainPanel、人物窗未显示；负重的服务器字段候选存在但 legacy 自定义标签未显示；其余字段即使 `Stat` 有近似命名也不判字段等价，保留未验证。原版地址到目标 EI EXE identity 与服务端业务字段的一对一映射仍未闭合，因此此台账用于规划和发现，不宣称原始数值语义完全定案。无命令或文档编辑工具错误；`git diff --check`通过；未改客户端/服务器代码，未运行游戏、构建、输入或业务测试。


**2026-09-24 TRADE-02 研究记录冲突与格距静态复核：**交叉读取 `trade-window-render-evidence.json`、`trade-window-closure-evidence.json` Finding 284、`trade-split-handle-evidence.json` Finding 301 与 `RESEARCH_LOG.md`，发现后续闭合的 5 列索引公式 `col+5*(split+row)` / 每侧6行视口与较早 F283“8列×9行”结论直接冲突；将 F283 标为被后续闭合证据 superseded，审计继续按 5×6。静态查 `TradeDialog.ApplyLegacyEiLayout()`、`DXItemGrid.Step/CreateGrid()`、`DXItemCell.CellWidth` 与 `DXImageControl.UseOffSet`，计算当前 GridPadding=1 导致37px pitch，目标scan为36px；当前每格相对 Grid 的第一格 `(1,1)`，第五列累计额外漂移4px、底六行累计额外漂移5px。原版 frame 1050 对屏幕根锚点/offset消耗仍未闭合，文档不宣称绝对首格错位量。本轮没有修改代码、没有运行客户端或交易测试；无工具/API错误；`git diff --check`通过。

**2026-09-24 选角面板命中几何静态续审：**将 `login-flow-evidence.json → screens.parent` 的 EI 根相对操作坐标，与当前 `GodotClient/Scripts/SelectScene.cs::BuildLegacySelectUi()` 直接对照。当前逻辑画布1024×768，面板根矩形由 `(1024−320)/2,(768−425)/2` 得 `(352,171,320,425)`；底部 Start/Create/Delete 三个按钮为局部 x=`25/120/215`、y=`382`、宽80、高 `MirSkin.GetSize(Interface,16).Y`（≤0时回退21），所以其逻辑画布矩形分别为 `(377,553,80,h)`、`(472,553,80,h)`、`(567,553,80,h)`。EI 研究构建记录的 Create/Enter/Exit 原始根坐标为 `(440,93)`、`(259,49)`、`(28,438)`；这些与当前三按钮的矩形明显不在同一布局锚点，且当前三个按钮分别为进入（初始禁用）、创建（角色数<4时开创建页）、删除（初始禁用），不能按顺序猜成 EI Create/Enter/Exit 一一对应。此处只比较源代码逻辑画布RECT与研究工件坐标；未将 `UiScaler` 变换当作第二次偏移，也未把 F50 `Size=(1024,768)` 当作拉伸绘制，资源实际绘制/缩放仍按 `DXImageControl.DrawControl()` 的 `StretchImage` 条件判断。原版角色槽数、按钮阶段语义和目标 EXE 身份限制继续见 PRE-04/06，不据这组三点坐标改代码。起始一次 `rg` 使用了仓库内不存在的 `GodotClient/Scenes/` 路径，报告两处文件不存在；随后以 `rg --files GodotClient` 定位实际 `GodotClient/Scripts/LoginScene.cs` 与 `SelectScene.cs` 并完成只读复核。未启动客户端、未操作登录/选角输入、未运行测试。

**2026-09-24 选角角色卡片与原版动态列表边界：**继续对照同一 primary-static `login-flow-evidence.json` 的 phase table/`char_slots` 与 `SelectScene.RefreshList()/SelectSkinCharacter()`。当前每个角色以280×75卡片命中区显示，逻辑画布坐标为 `(372,216+78i,280,75)`，最多 `i=0..3`；点击卡片会选中 `SelectInfo` 并启用 Start/Delete，同时刷新单个大角色动画。研究工件描述 EI phase0 是4-button character-list绘制/hover、两条槽记录索引0..1，并由单独绘制链显示角色动画与详情；但没有给出这两条记录对应的鼠标RECT像素坐标。因此可以判定 Zircon当前4条卡片布局/可见文字字段属于其源代码实现，不能把它们当作EI几何复刻；不能在缺少原版RECT时声称卡片具体坐标错了多少。当前 legacy 页面尚未把创建确认/删除确认、选中态与 EI phase1/2/3/4 的每条状态迁移建立一一映射，PRE-02/04/06维持未验收。未启动选角页面或触发网络请求。


**2026-09-24 选角创建阶段静态链续审：**按 PRE-10 将目标EI与Godot创建角色路径分开记录。原版 `login-flow-evidence.json` 显示 F51 handler 对两条槽执行检查，切phase1并载入 `CreateChr.dat`；加载完成后phase2呈现动画角色列表和五组阶段控件，F89路径进入phase3并发送旧消息0x64，确认成功阶段由入站0x20D推动。当前 `SelectScene` 的 legacy Create 点击只切换到自绘创建面板，提交则走 Zircon `ClientPackets.NewCharacter`（名字、职业、性别、发型、发色、服色）；这与EI旧0x64负载不同，但由于两协议/服务端状态机不同，不把包号差异解释成同一业务动作失败。研究工件尚未给全套F92/F95/F98/F86/F89逐控件动作，因此只记已闭合的静态阶段和余项。中途多次 `apply_patch` 锚点校验失败，均未写入部分内容；改用精确标题插入一次性更新本文件。未连接客户端、未发送创建/删除请求、未运行测试；`git diff --check`通过。


**2026-09-24 MAP-04 大地图左键路径静态审计：**逐读 `BigMapDialog` 的 `GuiInput`/`MouseDoubleClick`、`CloseBigMapAfterTeleport()` 与 `DXControl._GuiInput()`。当前大地图左键 release 会发送 `TeleportRing` 并隐藏窗口；`DXControl` 的第二次按下路径才识别双击、抑制该次普通 click、调用 `MouseDoubleClick` 发送 `AutoPathWaypoint`。所以一组真实双击是否能从首次 release 后继续到达第二次按下处理，取决于隐藏节点后Godot是否仍将下一次鼠标事件路由到原控件；当前工作区没有该事件序列证据。本项记录为代码控制流推断/可达性风险候选，不声称已证实不可达或原版等价。此窗口是primary-static证据确认没有对应EI独立窗口的现代扩展。未触发点击、传送、寻路或网络输入。一次搜索把 `KeyBindManager.cs` 错放在 `Scripts/` 下，随后更正为 `GodotClient/Controls/KeyBindManager.cs`；另一次未排除 `GodotClient/UI/ui_tree.json`，造成搜索输出膨胀，后续将搜索范围限制为 `*.cs` 并完成检查。`git diff --check`通过。

**2026-09-24 登录/选角布局证据等级与矩阵一致性续审：**复核 PRE-01..10、选角几何记录、`SelectScene.BuildLegacySelectUi()` 与已有1024×768空选角截图。当前选角节点把 F50 的Control矩形设为1024×768，但 `StretchImage` 未置位；EI WIL帧原生640×480，且截图只在左上640×480区域出现底图，剩余区域为Godot灰底。子动画、角色精灵、覆盖层均挂在该背景Control下，故逻辑子坐标以该节点为父；`UiScaler` 对整个层只执行一次变换，不会补做 F50 的纹理拉伸。已确认此实现的画布/素材铺满差异，不代表已知 EI 运行时缩放规则。

当前选角还同时保留了隐藏的通用 `VBox` 表单节点与 legacy 自绘树；`RefreshList()` 两边分别更新，legacy卡片按最多4项生成、Create按 `_characters.Count < 4`启用，而研究 primary-static 工件的两条槽证据来自不同尺寸/指令边界的 EXE。将“研究构建报告两槽”和“当前Godot支持4项”分开记录：目标 EI容量、卡片绘制RECT、角色详情绘制、选择态转移都还没有同一版本的完整映射，不能把研究版2槽事实推广成本机目标，也不能将Godot4项视作已通过。

这轮不修改游戏实现，因目标资源/EXE身份和EI截图基准尚未闭合，直接按候选坐标挪UI会把推测固化。PRE-04/06保留活动差异与解除条件；PRE-01..03、05、07、09、10保持各自证据边界；登录/选角完整交互和多分辨率运行采集仍受CUA当前未暴露GUI窗口限制，登记在待验证清单后跳过。按用户要求未重复坐骑 S/Ctrl+S、双人交易、Bad Request或其它不稳定输入。只核对源码、已存截图/图集与反编译研究JSON；本轮无工具/API错误，无客户端输入或测试。`git diff --check`通过。

**2026-09-24 CHAR-02 reframe阈值坐标系收窄：**检查`layout.json::specialized_window_evidence[*].window_factory.primary_algorithm`：0x423E80从所选WIL头尺寸构造局部RECT，最终用SetRect写`this+0x08`外框、`this+0x18`内容框；因此0x44CCD0后置判断的`[this+0x20]>0x320`按RECT布局是内容框right坐标阈值。此前CHAR-02/旧日志把该分支简称“宽屏行为”，容易使读者把800当成桌面Viewport宽度；现改为“内容RECT右边界阈值”，并明确坐标经过0x423E80居中/父矩形计算，不能直接以屏幕尺寸或裁切截图判断触发。尚需求出状态窗0x423B30初始rect、toggle调用时传入的x/y/width/height及第一次SetRect后的+0x20，才能确定通常id1/F201是否走第二次x+0x118 reframe和其可见效果。只更新审计结论/验收条件，不改CharacterDialog布局或实现。一次`rg`附带了仓库不存在的`GodotClient/Network/ClientPackets.cs`路径并报ENOENT；随即改为沿有效`GodotClient/Scripts/GameScene.cs`引用与已定位的状态窗研究JSON继续，无依据路径错误推断。未运行客户端、输入或测试，未进行坐骑/交易/Bad Request测试；`git diff --check`通过。


**2026-09-24 CHAR-02 原版 reframe / Godot 控件静态补核：**重新核对目标审计工件 `status-window-render-evidence.json`、WIL frame header与 `CharacterDialog.ApplyLegacyEiLayout()/ToggleLegacyView()`。原版 close child F161/162 RECT `(212,298,28,26)` 与当前Godot关闭按钮 RECT 一致；但原版 `0x423E80` 在状态页切换时重建窗口rect，且 `[this+0x20]>0x320` 分支会将F201再以 `x+0x118` reframe一次。当前切换代码只更改同一窗口宽度、背景索引/offset和裁剪，没有该条件分支。该条件字段含义和触发上下文尚未由 `0x423E80` 输入调用链闭合，故归类“静态差异已见、实际触发/应有视觉效果待核”，未凭经验改坐标。按下/释放抖动、目标 EXE 对应资源版本和该分支的屏幕表现均列入 CHAR-02 阻塞项。本轮跳过坐骑 S/Ctrl+S、交易或其它可能触发Bad Request的运行期输入；未运行客户端、构建或测试。搜索中曾把 `Library/` 当作仓库路径、实际仓库对应 `LibraryCore/`，以及对全研究 docs 的 `0x423E80` 搜索范围过宽造成输出截断；随后限定到两个具体 JSON、纠正路径并完成核对。`git diff --check`通过。

**2026-09-24 技能书分类与顶部控件静态几何续审：**对照 `skill-window-context.json`、`skill-window-render-loop-evidence.json` 与 `MagicDialog.BuildLegacySchoolButtons()/ApplyLegacyEiLayout()`。确认八个分类按钮当前根相对位置及 F450/452/454/456/458/460/462/464帧号与研究几何逐项吻合；但该几何吻合不闭合本机资源版本身份或 `MagicSchool`→EI分类状态/技能列表映射。另确认原版独立 F440/441、F410/411、F412/413 三个顶部子控件的静态RECT为 `(399,340)`、`(61,303)`、`(366,303)`，当前 legacy 布局没有创建它们并隐藏现代左右分页按钮；控件动作仍未从父窗事件分派链闭合。一次尝试读取 `research/ei-ui-layout/skill-window-input-evidence.json` 的路径不存在；`rg --files` 找到可用输入证据实际位于 `research/mir3-map-reconstruction/skill-window-input-evidence.json`，后续判断仅采用正确位置的通用输入证据及技能书专用 render-loop工件，未把错误路径当作缺失证据结论。未运行客户端、构建、输入或业务测试；`git diff --check`通过。

**2026-09-24 MAP-01..03 源码路径复核与安全验收步骤修订：**按当前 `GameScene.LayoutHud()/HandleKeyBind()`、`MainPanel.MiniMapButton` 回调和 `MiniMapDialog` 构造/`SetMap()` 对照 `minimap.json`、`minimap-subsystem-verification-evidence.json`、`hud-caption-action-tail-evidence.json` 与 `hotkey-label-handler-consistency.json`。确认审计中“原版嵌入主世界对象、固定目标矩形(672,0)-(800,128)、T缩放surface、V/HUD入口共用开关字段但冷却门不同；当前为独立200/300窗、另有透明度/大图按钮、本地 `MapInfo.MiniMap`/`MiniMap.Zl`绘制”的身份与路由描述仍与当前源码相符，未发现应改写的新证据。一次以未转义的 `+0x6210` 写法运行 `rg` 造成正则“repetition operator missing expression”错误；改为 `rg -F` 固定字符串后完成只读检查。另将执行步骤4明确为：风险输入先列入阻塞并跳过，不盲测；仅安全、可观测的修复项安排实际UI验证，阻塞项不得标通过。未触发地图/业务输入，未运行客户端或测试；`git diff --check`通过。

**2026-09-24 SET-04 关闭控件RECT修正与运行验收状态：**依据 `system-window-render-evidence.json` 三处一致的原版 `(218,238,28,26)` 与 `ConfigDialog.ApplyLegacyEiLayout()` 原 `(216,238,28,26)`，将当前关闭按钮 x 从216改为218；这只修正已证实的局部RECT差异，未将窗口标为通过。按目标命令 `bash login_game.sh legacy` 启动：服务端/客户端构建均返回0 errors，测试账号自动登录并进入 MapIndex 7，日志持续收到 Ping/游戏数据。尝试经CUA取可见窗口时 `cua.getState({disableDiffing:true})` 返回空apps/browsers，`cua.listWindows()` 抛出 `is not a function`；因此未做任何鼠标点击/视觉截图，新增短期窗口可访问阻塞项并保留进程供桌面恢复后验收。一次shell进程筛选输出仅匹配到执行查询的zsh本身，不据此判断客户端未运行；随后用login脚本日志确认启动。`git diff --check`通过。

**2026-09-24 SET-03 隐藏状态控件命中链续核：**读取 `options-toggle-click-handler-evidence.json`、`control-hit-setpos-ctor-evidence.json` 与 `RESEARCH_LOG.md` 对照 `ConfigDialog.CreateLegacyOptionButtons()`、`DXControl._GuiInput()`、`DXImageControl.DrawImage`。已确认当前 Godot `DrawImage=false` 只停止绘制而不隐藏节点/鼠标输入；原版选项输入对11个子控件逐一分派，通用 `0x4177F0` 用存储在 `[obj+4]` 的RECT执行 `PtInRect`，该通用函数本身不检查帧可见性。原版状态切换会以 frame `-1` 隐去一侧；材料只概括 `0x417880`“frame + RECT”，没有闭合负帧时是否清零RECT，故原版隐藏侧仍可点暂作未决，修正 SET-03 旧结论而不作未经证实的实现变更。一次 `rg` 把 `RESEARCH_LOG.md` 误指定到 `research/mir3-map-reconstruction/`（文件实际在 `research/ei-ui-layout/`），路径检查失败后改读真实文件完成比对。未运行客户端或输入测试；`git diff --check`通过。

**2026-09-24 SET-04 图像锚点证据边界续核：**`:8766/api/info?f=GameInter.wil&i=161/162` 两帧均回报 `28×26, offset=(-24,-16)`；交叉读取 `sprite-offset-anchor-verification-evidence.json` 与通用 `control-ctor-paint-evidence.json`，前者仅概括 UI 控件用帧偏移确定 rect，后者记录控件按 `[+0x28]/[+0x2C]` 调 `0x460240` 绘制，未明确含偏移后最终屏幕绘制坐标。当前 `DXImageControl.DrawControl()` 只有 `UseOffSet=true` 才将 WIL offset 计入绘制矩形，按钮当前默认false。故SET-04从“整体位置差2px”拆为“根相对RECT已修正、alpha像素锚点仍未决”，保留后续同版资源/实际画面核验；未再次改代码。本轮一次 `rg` 查询误把不存在的 `GodotClient/Scripts/MirSkin.cs` 当文件路径，实际 `MirSkin` 实现在别处；纠正为追踪 `DXImageControl` 的 `UseOffSet` 消费点并基于已有WIL帧头完成比对。未运行游戏输入或测试；`git diff --check`通过。

**2026-09-24 INV-04 F280 gauge 静态补核：**读取目标研究工件 `inventory-window-render-evidence.json::paint_geometry[0]`、`trade-split-handle-evidence.json::gauge_class/bag_corroboration`、本机 WIL 帧并对照 `InventoryDialog.ApplyLegacyEiLayout()`。补明 EI 调用坐标为窗口绘制基准 `(x+0xF8,y-0xA5)`，F280源图16×424，alpha bbox `(0,0,13,423)`；共享控件参数为6行、12px填充宽、218px填充/轨道视口、12px padding、垂直模式；`[bag+0x58]` 经 gauge 拖动位置乘94并截断写回，paint 再以最大值94读取。原 JSON“此值只有reset清零写者”的旧观察已被后续 EI-301 全写者追踪覆盖；空滚动位置仍须画轨道/拖柄。当前 Godot legacy 背包将旧 `WeightBar` 隐藏，没有 F280/gauge 状态实现；新式 `DXVScrollBar` 外观与交互不能视作旧 gauge 等价。父处理器至 gauge 的静态点击/拖动/滚动路由已由 EI-301、F707、F936 补闭合；F280 WIL offset 对最终屏幕像素锚点的作用、背包滚轮具体步进仍未闭合，未据构造参数猜实现。工具错误已记录并纠正：搜索引用不存在的 `GodotClient/Controls/DXProgressBar.cs`；一次 `jq` 把 inventory-ctor-click-use JSON 错放在 `ei-ui-layout/`，用 `rg --files` 定位到 `research/mir3-map-reconstruction/` 后继续核验。仅更新审计文档，未运行风险输入、游戏或构建；`git diff --check`通过。按当前目标保留既有未跟踪 `docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md`，本轮不提交/推送。

**2026-09-24 SKL-02/04/10 WIL帧头与控件身份复核：**8766 预览器 `/api/info` 逐帧读取 F400、F410–413、F440/441、F450–465 的尺寸/offset；读取 `skill-window-render-loop-evidence.json`、Mir3-Research `mir3-map-reconstruction/skill-book-category-tabs-evidence.json`、`skill-book-draw-evidence.json`、`skill-window-input-evidence.json`，再对照当前 `MagicDialog.BuildLegacySchoolButtons()/BuildLegacySkillSlots()` 与可用 `Client/Scenes/Views/MagicDialog.cs`。本机 WIL 头确认 F410–413 32×14、F440/441 20×12、分类帧两态尺寸成对相同（前四项44×36、后四项48×36），所有这些按钮类帧offset为(-24,-16)；F400为512×512、offset(+7,-44)。已在正文记录帧头与证据边界：帧头不等同 alpha bbox、命中矩形或最终屏幕锚点。源码/primary-static 对照仍支持 8 个 F450/451..464/465 分类控件与 3 个独立头部控件；`+0x2F4..+0x7E0` 是类别控件链，旧研究工件“skill-slot”是通用控件名，不能解释成左页 8 个技能条目；左页另由类别链表绘制，`0x43A370` 有6个 hit RECT。当前 Godot 仍有12个固定点击格并缺三头部控件，差异继续按 SKL-01/02/04 记录。一次 `rg` 模式把 `+0x2F4` 未转义导致正则报错，后续改用文档定点读取与 JSON 字段检查继续审计；一次 `jq` 初查时字段路径不存在，检查顶层 schema 后改查实际字段。8766 API 本轮可连接且请求成功。未运行客户端/键盘/鼠标或业务输入；仅审计文档更改，`git diff --check`通过；保留未跟踪本地服务状态文件，不提交/推送。

**2026-09-24 QUEST-03 MilestoneNotify 关闭路径源码补核：**读取 `QuestDialog.Close()/AddTab()/RefreshPage()`、`ServerConnection.SendMilestoneNotify()`、`SConnection.Process(C.MilestoneNotify)` 与全仓库 `ReceiveMilestoneUpdates` 读写点。确认 Godot 关任务窗无论当前页都会发送 false，离开里程碑页也会发送 false；服务端对 false 只赋 `Player.ReceiveMilestoneUpdates=false`，true 才回送 `S.UserMilestones`，字段在现有服务端源码仅声明/赋值、无读取者。故将 QUEST-03 从“关闭包故障”精确改成“普通页会多发 false 并重写状态，已证实包路径，业务影响/目标 EI 对应未证”，没有擅自改实现。原版 F721/722 仍由 primary-static `0x448430→0x4177F0→0x45AFC0(cmd 0x69)` 证实为音效点击，不是直接 close；适用的目标运行验证保留待办。本轮一次研究文档搜索误指定不存在的 `Mir3-Research/docs/codebase/`，改到实际 `docs/research/ei-ui-layout/` 与 `docs/research/mir3-map-reconstruction/` 继续核对；该工具路径错误已记录。CUA `getState()` 返回 apps/browsers 空，本轮没有 GUI 可观察目标；未运行游戏或输入，仅更新审计文档，`git diff --check`通过；不提交/推送并保留用户未提交文件。


**2026-09-24 GROUP-04 关闭控件静态RECT续审：**对照 `social-window-render-evidence.json::windows[id=window.group].state_text_and_controls.paint_repositioned_controls.records` 与 `GroupDialog.ApplyLegacyEiLayout()`。原版 F161/162 关闭控件的 paint-time位置为根相对 `(226,214)`；`:8766/api/info?f=GameInter.wil&i=161/162` 本轮回报两帧均28×26、offset `(-24,-16)`，与静态 RECT 尺寸一致。当前 Godot 仍设置 `(224,212)`、28×26，故当前来源码RECT左上各偏2px。此前 GROUP-04 已将三条底部动作热区与原版坐标对应，但未检查 close 子控件，此处补记为可静态修复项；不由控件帧 offset 或alpha边界反推新的hit RECT。之后须分别验根相对hit框、关闭图可见像素锚点、边界点击与关闭消息/窗口状态。原版 primary-static position 是研究构建静态证据，本机目标 EXE身份和实屏像素对照仍未闭合。检索开始时把 `GodotClient/Dialogs` 当作目录，但项目控件实际在 `GodotClient/Controls`，`rg` 报该路径不存在；另一次把 `Mir3-Research/docs/research/mir3-map-reconstruction/RESEARCH_LOG.md` 当作文件，该位置不存在，实际总研究日志位于 `docs/research/ei-ui-layout/RESEARCH_LOG.md`，派生的F948 JSON则在 `mir3-map-reconstruction/`。更正路径后读取了F845/F948 JSON与真实日志，并定位真实 `GroupDialog.cs` 继续核对。未修改游戏代码、未运行组队/关闭输入或客户端；`git diff --check`通过。

**2026-09-24 EXIT-01 F800/F950 对象与按钮RECT证据拆分：**重读 `confirmation-prompt-evidence.json` 的 constructor、button_instances、cluster2_resolution、direct_callers，以及 `window-catalog-evidence.json` 的 id`0x64`构造记录和 `chat-window-mouse-dispatch.json::fnB_0x42BE21`。确认 F950 singleton mode1按钮 `(51,125,44,20)`/`(244,125,44,20)` 是相对其自身360×190根窗；居中根原点 `(220,151)`。退出 id`0x64` 是另一隐藏窗口类 `0x418910`，根F800、364×184、构造原点 `(218,176)`，其 `+0x54/+0x108` 子控件复用 F151/152 与 F154/155 图像帧。故帧族复用不能推出按钮职责、F950 根相对RECT或注销 `type 0x65` 适用于 F800；F800 两个子RECT/YES-NO各自最终动作仍缺证据。原版抬起路由会对 `base+0x53030` 调 `0x418A00`，命中后发 `WM_CLOSE`，但摘要未拆 YES/NO hit 分支与取消语义。现已收窄 EXIT-01 的按钮证据措辞，阻止把F950精确RECT误用于F800。只读反汇编工件、当前退出源代码和已存截图，未运行确认/注销输入或客户端；`git diff --check`通过。

**2026-09-24 HUD-01/KEY-02 技能图鉴 cap2 与 B 键静态分流复核：**交叉核对 `window-paint-and-hotkey-dispatch-evidence.json`（B→`[main+0x6208]` 取反）、`hotkey-label-handler-consistency.json`、`chat-window-control-map.json` 与 `map-ui-resource-evidence.json::no_separate_map_dialog/key_table_evidence`。EI cap2 `0x42C241` 和字母 B `0x42CE1D` 都只翻转同一个 `main+0x6208` 状态位；caption 文本是“技能图鉴(Ctrl+B, B)”，研究工件把状态命名为 `skill_browse`。静态材料支持二者入口/状态一致，但尚未闭合该字段所有读者及最终显示对象，因此“技能图鉴浏览状态”是证据标签，不据此推断独立窗口、技能书 id14 或地图窗口。当前 Godot `GameScene` legacy HUD 的 `SkillEntryButton.MouseClick` 直接 `WindowManager.Toggle(_magicDialog)` 并刷新；`GameScene._Input()` 没有 B 特判，`KeyBindManager.Defaults` 将 B 绑定 `MapBigWindow`，`HandleKeyBind(MapBigWindow)` 则开关 `_bigMap`。因此 cap2、B 两条当前入口均未复现 EI 的共同状态位动作；Ctrl+B 在 EI handler 中也由 B 字母按键路径覆盖，但 Godot 默认修饰键精确匹配使 Ctrl+B 不命中默认 B。`map-ui-resource-evidence.json` 同时确认 EI 该构建没有独立地图 dialog；该事实不代替对 `+0x6208` 显示链的追踪。旧派生工件 `mir3-map-reconstruction/hud-caption-action-dispatch-evidence.json`（F580）及总研究日志后段仍把 `0x42C241/[0x6208]` 称作“腰带切换”，与 fresh F321 `hud-caption-action-tail-evidence.json` 的 idx2→技能图鉴、F316 `hotkey-label-handler-consistency.json` 的 cap2/B 同状态位配对、F313 的 caption 索引/文本构造互相冲突；本审计按这些具体 idx、handler 和字符串地址相互闭合的 fresh primary-static 记录裁定为技能图鉴入口，并把 F580/旧日志标签视为过期命名，不拿它证明腰带行为。此项登记为可静态实施差异，待计划阶段追完字段读者、确定 Zircon 表示后修复；不在本轮改游戏代码。因用户明确禁止重复坐骑 S/Ctrl+S、可能触发 Bad Request 的输入和双人完整交易流程，本轮未启动客户端或做输入测试；这些仍留在阻塞/待验证清单，继续其他静态项目。工具错误：首次按错误路径 `GodotClient/Scripts/KeyBindManager.cs` 检索，`rg` 报文件不存在；用 `rg --files GodotClient` 定位真实文件 `GodotClient/Controls/KeyBindManager.cs` 后继续完成，未造成文件改动。另一次全目录 `rg` 命中大体积 `GodotClient/UI/ui_tree.json` 导致输出截断；后续将范围限定到源码和指定JSON字段。只更新本审计文档，`git diff --check`通过。

**2026-09-24 EXIT-01 资源头与当前入口源码复核：**再次读取用户指定 `:8766` 服务的 `/api/info`：GameInter F800=`512×256, offset(+7,-44)`；F950=`360×190, offset(-24,-16)`；F151/152/154/155 均=`44×20, offset(-24,-16)`。这与各自不同根窗的资源身份一致，但 API 只给帧头/offset，不提供alpha边界或F800子窗 hit RECT。重读当前 `ExitDialog.cs`、`GameScene.OpenExitDialog()/HandleKeyBind()/LayoutHud()`及 `KeyBindManager.Defaults`：其类注释仍称“原版 ExitDialog”，实际建的是 Interface F281/252×128、两个自绘按钮分别调 `LeaveGame()`/`ExitClient()`；HUD Exit/Logout 和 Alt+Q/Alt+X 均路由同一 `OpenExitDialog()`。这与原版 idx3/id`0x64` F800 及 idx4/F950 type`0x65` 的分流冲突已由当前源码确认，不是截图推测。保持为 EXIT-01 实施项；不触发按钮、注销或退出流程。工具查询成功、无 API 错误；未运行客户端或测试，`git diff --check`通过。

**2026-09-24 GUILD-01 F600 像素边界独立交叉核验：**从 `:8766/api/image?f=GameInter.wil&i=600&scale=1&bg=transparent` 直接保存资源PNG到 `/tmp/ei-f600-audit.png`，用 ImageMagick 独立测得画布1024×512、alpha bbox `594×445+(214,33)`；viewer `/api/info` 另报F600 header 1024×512、offset `(+7,-44)`。alpha bbox与文档既有WIL解码值相符；offset是帧头锚点，不能与alpha原点混为一谈。当前 `LegacyUiSkin.ApplyLegacyTestWindow()` 给 GuildDialog 根设置446×596并 `Clip=true`，`DXControl.Clip` 映射Godot `ClipContents`；在当前F600子图位于 `(0,0)` 的Godot树中，可见alpha交集为根相对 x=`[214,446)`、y=`[33,478)`，右侧362px被裁，纵向没有因根高裁切。primary-static初始化记录支持EI根596×446，但最终目标版背景原点/锚点和裁切链仍未证，故不把对换宽高当作可直接应用的最终像素修复。第一次 ImageMagick 格式串使用不支持的 `%+X/%+Y`，产生属性警告且未正确输出坐标；改用 `%X/%Y` 后成功，结果与既有独立解码交叉一致。全程只读资源及源码，没有运行客户端或窗口输入；`git diff --check`通过。

**2026-09-24 HUD-08 cap8–15 命中框/贴图尺寸拆分：**复核 `chat-window-control-map.json::captions/setrect_22x22`、`hud-label-evidence.json::records/caption_control_class`、本机 `:8766/api/info` 和 `MainPanel.CreateButton()/GameScene.LayoutHud()`。primary-static研究工件给cap8–15具体22×22 SetRect坐标；viewer header确认对应GameInter F100–115帧对为40×38（F159腰带图为16×14）；当前Godot外层DXButton显式设40×38，固定资源尺寸不改变鼠标Control范围。以当前800×600布局计算，MainPanel根为(0,464)，八个当前hit rect与研究工件相比既有面积差异，也有部分原点差异，已更新HUD-08列出双方矩形；这只是研究构建primary-static与当前源码的坐标对照，目标EXE同版重放/真实边界点击仍待验，cap0–7尤其cap7不作帧尺寸推断。一次 `rg` 正则包含裸`+0x38`，报`repetition operator missing expression`，随后改用JSON字段/字符串解析。首次 viewer 请求误用8765端口返回连接失败，按用户指定入口实际端口8766重试并成功；查找 `LegacyHudLayout.cs` 时误用 `GodotClient/Controls/` 路径，真实文件在 `GodotClient/Scripts/`；一次zsh通配符假定研究目录有 `mir3-map-reconstruction/hud-label*` 文件而无匹配，之后读取已定位的EI证据JSON。上述查询错误均未造成文件变更或结论。只读源码/研究工件/帧头，未操作GUI或发送热键；`git diff --check`通过。

右侧 caption 状态帧像素边界补录：逐帧从 `:8766/api/image?...&bg=transparent` 导出F100–115，由ImageMagick alpha提取/threshold/trim测得16帧均为`37×37+(0,0)`；帧header仍为40×38、offset`(-24,-16)`。因此该资源族至少有三个须分开的几何量：WIL源画布40×38、当前Godot Control命中区40×38、primary-static研究的原版hit RECT 22×22；贴图alpha有效区则是37×37。offset不并入alpha bbox，也不自动平移hit RECT。原版最终屏幕可见像素是否消费WIL offset仍待其绘制调用链/匹配版运行图闭合。仅补资源边界记录，未改Godot布局；`git diff --check`通过。

**2026-09-24 HUD-08 cap0–7 资源/当前Control几何补查：**从`:8766/api/info`与透明图像独立测得F80–85源画布24×16、alpha bbox23×15+(0,0)；F90–97源画布28×26、alpha bbox26×26+(0,0)；F159源画布16×14、alpha bbox14×14+(0,0)，这些帧header offset均(-24,-16)。与当前 `MainPanel.CreateButton()` 逐项比较：cap0–2 (F80/82/84)及cap3–6 (F90/92/94/96)的显式hit控件尺寸分别与源画布尺寸相等；cap7腰带使用F159，却把控件hit范围固定为24×16，宽/高各比源画布多8/2px。alpha有效边界与WIL header、Godot Control hit范围仍是三种量。已有 `hud-label-evidence.json` 对 cap0–7 给出primary-static帧/文本/构造位置，`chat-window-control-map.json` 对cap8–15给出22×22矩形，但目前没有可用的逐项 cap0–7 原版hit RECT证据；所以cap0–6“当前尺寸与素材相等”不升级成“原版hit正确”，cap7偏大也不直接断言原版错误。下一步要从目标同版 `0x417550`父frame尺寸、各构造调用及最终SetRect链独立复核，再在800×600测试场验证所有边界。只更新审计文档，无代码布局改动；未运行输入/客户端。`git diff --check`通过。

**2026-09-24 EXIT-01 既有图像结构对照：**重新查看归档图 `evidence/legacy-ei-ui/ei-gameinter-f800-primary-resource.png` 与 `evidence/legacy-ei-ui/zircon-cap3-current-exit-dialog-2026-09-24-1024x768.png`。EI PNG 是 GameInter F800 的512×256资源帧，韩文“是否退出游戏？”和 YES/NO外观都已经烘焙在单张背景像素内；Godot图则是在1024×768当前游戏画面上叠出独立的中文“退出”标题与“返回角色选择”“退出客户端”两枚本地按钮。该视觉结构差异与源码证据相符，支持当前 ExitDialog 不等同于 F800；但两图不是同一屏幕/坐标系的目标EI与Godot截图，不能据此标定F800根原点、子控件hit RECT，亦未证明原版YES/NO各自消息。既有完整截图只作静态结构交叉核对，未点击或发出退出/注销动作；未改游戏实现。本轮 `git diff --check`通过。

**2026-09-24 SKL-06 技能输入证据范围复核：**直接读取 F939 `Mir3-Research/docs/research/mir3-map-reconstruction/skill-window-input-evidence.json` 的 `scope` 与 `cell_analysis`，确认它是 `primary-bytes`、范围限于 `0x43AC80–0x43AD50`；内容为技能窗 mouse handler 的3个帧控件、8个页签/对象和 `0x43A370` 六矩形命中测试，以及 `0x43AD00` 的页签重置、`0x43AD20/0x43AD50` 的记录链表操作。该工件没有记载 F1–F12 键盘分支；这只界定现有证据的覆盖范围，不能证明目标 EI 不支持技能绑定热键。已将 SKL-06 引用补全真实子目录与 Finding，保留目标 EI 键盘语义未决。一次先前宽范围 `rg` 输出被截断；本次改用 Python 读取指定 JSON 字段完成核对，没有把截断输出用于结论。未启动客户端或发送键盘/业务输入；`git diff --check`通过。

**2026-09-24 SKL-04 F400 alpha测量（根尺寸解释已于后续纠正）：**从指定 `:8766/api/info?f=GameInter.wil&i=400` 读取本机帧头 `512×512, offset=(+7,-44)`；从透明PNG独立测得alpha bbox `451×378+(30,67)`，归档图为 `evidence/legacy-ei-ui/gameinter-frame-400-wil-2026-09-24.png`。当时把研究根尺寸误读为452×380，并观察当前Godot把F400控件置于`(-30,-67)`后alpha从Godot根原点开始；这段算术事实仍成立，但“与EI根尺寸吻合”的结论已由本节末尾2026-09-24 SKL-04纠错撤回。帧偏移、alpha边界和根矩形是不同量；本机WIL不能证明目标版绘制裁剪。归档图可见书页底部和页边有箭头形像素，但不能由静态贴图断定F410/F412/F440点击动作，也不能把F939“3个控件”摘要等同已闭合翻页状态机。首次用Python算bbox因环境没有Pillow（`ModuleNotFoundError`）失败；改由ImageMagick `-trim` 得到bbox并重复核验。未运行客户端/键鼠或业务输入。

**2026-09-24 INV-01 首屏格位的独立像素复算：**对照研究工件 `inventory-window-render-evidence.json::resource_and_item_record_proof.grid_item_draw`、`interaction_and_selection.index_to_rect` 与当前 `InventoryDialog.ApplyLegacyEiLayout()`、`DXItemGrid.Step/CreateGrid()`、`DXItemCell` 常量重新计算。EI 首屏第(col,row)格命中矩形为 `(window.x+25+36*col, window.y+41+36*row,36,36)`；Godot legacy把Grid放在 `(25,41)` 且 `GridPadding=.5`，Step公式为 `36−1+2×.5=36`，Cell局部起点经C#向零截断为 `(36*col,36*row)`，实际也为同一组36×36矩形；全36个首屏RECT逐项相等。此处应明确判“首屏命中几何已静态吻合”，不再把它留作待比对。它不推出物品模型一致：EI 46条contiguous item records（stride 0xC2C）通过600 WORD identity/occupancy table把 item record slot 编码成 `slot+1000`，item footprint记录宽/高extent并按帧尺寸首空放置；EI的6×6是 viewport，而非46个记录到36格的恒定索引映射。当前48元素服务数组直接按`slot=y*6+x`喂给36个control，无法表达EI独立record id到viewport cell的映射、跨行滚动和多格占位。资源证据还记载原版绘制行范围受scroll字段扩展，约11行数据可参与滚动/绘制；这不等同11行同时可点击，继续维持“显示、命中、占位”三种范围分开记录。只细化审计文档，不改包袱布局或数据映射；本轮未运行游戏/构建及背包物品输入。`git diff --check`通过。

**2026-09-24 TRADE-01/02 独立格位与F1050锚点复算：**从primary-static `trade-window-render-evidence.json` 重取 F1050 512×512、WIL offset `(7,-44)`、alpha bbox `(14,91,483,330)`、pane命中RECT及36px格扫描；从当前 `TradeDialog.ApplyLegacyEiLayout()`、`DXItemGrid.Step/CreateGrid()`、`DXItemCell.CellWidth` 和 `DXImageControl.DrawControl()`独立复算当前点击格位置。当前padding=1令步距37；左/右首格 `(15,92)/(247,92)`，第5列 x=163/395，第6行 y=277；EI扫描首格 `(21,48)/(253,48)`，第5列 x=165/397，第6行 y=228。当前格位从左上(-6,+44)漂到末列(-2,+44)，末行累计垂直差达+49px；所以TRADE-02除分隔滚动缺失外，首屏格命中与EI公式也未对齐。WIL alpha bbox加帧头offset后为 `(21,47,483,330)`，与EI左pane首格命中起点只差1px，是原版frame blit消费offset的强几何线索。当前Godot DXImageControl默认`UseOffSet=false`且交易布局未开启该属性；在实际legacy wrapper根clip之下，当前F1050交集为根内`[14,484)×[91,330)`。如EI blit消费帧offset，推算交集是`[21,484)×[47,330)`。因仍未直接恢复`0x460240`本次调用的destination参数，且研究EXE身份与目标文件不匹配，保留offset语义为推论，禁止据此贸然改坐标；后续需核helper参数或同版运行截图。用户要求的双人交易完整往返继续按阻塞清单跳过。本轮只更新审计文档，未改客户端代码、未运行构建/游戏；一次rg按错误目录`GodotClient/Scripts/DXItemGrid.cs`查询返回路径错误，随后用`rg --files`定位`GodotClient/Controls/DXItemGrid.cs`并完成复算；没有使用错误查询结果作结论。`git diff --check`通过。

**2026-09-24 NPC-03 按钮派发与对象层级静态复核：**逐段对照 `Client/Scenes/Views/NPCDialog.cs::ProcessText/Response/OnVisibleChanged`、`Client/Scenes/GameScene.cs` 对 `NPCBox/NPCGoodsBox/NPCQuestListBox` 的创建、Godot `NPCTextControl._GuiInput()` / `NPCDialog.ShowPage()` 和服务端 `SConnection.Process(C.NPCButton)` / `PlayerObject.NPCButton()`。确认旧 Client 将 NPC 主窗、商品窗和任务窗创建为 GameScene 同级对象；inline 按钮 ID=0 只本地隐藏对话框，非零请求受每个 button-range 自己的 `NextButtonTime` 一秒门限限制，服务器仅在当前 NPC/page 的按钮表中找到相同 ID 且存在 DestinationPage 时跳转。Godot inline 文本热区按字形生成矩形、固定18px行高；点击ID=0走`CloseNPCDialog()`，非零立即调用 `SendNPCButton()`，没有旧版门限；`Page.Buttons`后备按钮同样立即派发。Goods现为`NPCDialog`子控件而非GameScene同级窗；`ShowPage()`也在根窗口高度之后摆放它，不能证明满足旧版 sibling 覆盖/裁切顺序。该部分只闭合可用Client→Godot与当前服务端链路，不将可用Client代码冒充目标 EI 版本证据；EI frame id11 与F1100文本/选项真实关联仍属NPC-02未决。用户要求跳过运行期危险项，本轮未打开NPC、点击菜单或发协议输入；NPC实际层级遮挡、文字热区边界与版间行为需等可访问的匹配EI/运行截图再验。

**2026-09-24 HRS-02 服务端请求路径静态闭合：**对照 EI `horse-window-render-evidence.json::interaction.click_dispatch/message_literal_decoding/state_field_xref`、Godot `HorseDialog.Action()/SetMountState()`、`GameScene.SendChat()`、`SConnection.Process(C.Chat/C.Mount)`、`PlayerObject.Chat()`、`SEnvir.CommandHandler`、`ErrorHandlingCommandHandler` 与 `PlayerObject.Mount()`。确认当前四个HorseDialog动作回调把 `@上马/@遛马/@收马` 作为 `C.Chat` 发出；服务端任何 `@`均转命令处理器，已注册类来自Player/Admin命令集合，而全仓库命令与packet path均未发现这三个名称，未知命令会明确回 `MessageType.System`“Command @... does not exist.”。同一协议层的 `C.Mount` 独立直达 `Player.Mount()`，只切换马状态并校验死亡/账号马/地图；它不是三个聊天命令的自动等价实现。故把 HRS-02 从泛化“协议等价尚未确认”收紧为“当前聊天按钮请求确定不匹配，原版分支到新协议的功能映射仍待定义”；不因用户测试账号可能有GM权限而点击或重发未知命令。本轮未发送S/Ctrl+S、坐骑按钮或任何业务输入，也未启动客户端。一次 `rg` 的目标文件误写为不存在的 `ServerLibrary/Envir/Commands/Handler/ErrorHandlingCommandHandler.cs`，更正为实际 `ServerLibrary/Envir/Commands/ErrorHandlingCommandHandler.cs` 后继续；没有据路径错误得出缺失处理器的结论。`git diff --check`通过。

**2026-09-24 HRS-02 可用旧版 Client 范围复核：**静态检索 `Client/Scenes` 确认可用旧 Client 没有 `HorseDialog/HorseWindow/MountDialog`，仅 `GameScene` 的 `KeyBindAction.MountToggle` 执行冷却门后发送 `C.Mount`；`Client/Scenes/Views/HorseTameDialog.cs` 处理目标野马套索动画、角度提示和进度条，是驯服小游戏，不能与 EI GameInter F850 窗混同。EI `system-window-render-evidence.json` 给出四帧对的艺术字样候选（马匹/下马/收马/取马），`horse-window-render-evidence.json` 与 F545 则将四个 hit/state 分支连到三个命令字符串；该来源/动作拆分与当前旧 Client 的单一 `C.Mount` 输入对象不构成一一映射。故HRS-02还需先定义四个原版业务行为在Zircon账户马种、当前骑乘状态和响应包中的对应模型，才能安排实现；不把 TameDialog 或MountToggle直接复制成F850按钮功能。工具错误记录：刚才一次查看未知命令错误处理器时误指向不存在的 `Commands/Handler/ErrorHandlingCommandHandler.cs`，改用真实路径 `ServerLibrary/Envir/Commands/ErrorHandlingCommandHandler.cs` 读取后确认错误分支；没有凭错误路径作推论。未运行客户端/坐骑键/按钮，也未发聊天或mount包；`git diff --check`通过。

**2026-09-24 NPC-02/03 静态链续审：**继续沿NPC对话主窗、商品子窗与服务端page跳转的代码链核实，细化NPC-03：旧Client的节流变量位于每个按钮范围构造循环内，因此是每组独立一秒；Godot逐字形命中区和后备按钮都立即发送。旧Client在GameScene同级创建主窗/商品窗，Godot把商品面板作为根窗后代；ID=0均不发送NPCButton，但旧Client本地设Visible=false，Godot走统一Close/NPCClose。Server端仅按当前NPCPage.Buttons匹配ID并要求DestinationPage非空才跳页。NPC-02有关 EI frame id11与F1100选项层关系/真实输入协议仍未解决，目标EXE身份限制继续适用；此处仅是可用Client与Godot源码比较。没有运行期交互或业务请求。工具错误记录：一次研究目录路径误用仓库内相对`Mir3-Research/docs`，实际目录在`/home/tetsuya/development/Mir3-Research/docs`；一次包含`GodotClient/UI/ui_tree.json`的广域搜索输出严重截断；均收窄到正确路径和指定源码后完成核对，错误输出未用于推断。仅更新审计文档，`git diff --check`通过。


**2026-09-24 HUD-04/KEY-02 cap5 组队入口静态更正：**回查 fresh primary-static `hud-caption-action-tail-evidence.json::exe_trace.action_table_0x42C494_16.idx5.action`，EI HUD cap5 明确 `push 6; call 0x42ADB0` 切换(id6)组队窗。对照当前 `GameScene` 的 `_mainPanel.PartyButton.MouseClick → OpenGroupDialog()`、`OpenGroupDialog()` 内 `if (!AutoLoginArgs.LegacyUi) SendGroupNotify(true); WindowManager.Open(...)`、`WindowManager.Open()` 的 `if (w.Visible) return`，以及 legacy 键盘 G 的 `HandleKeyBind(GroupWindow) → WindowManager.Toggle()`：更正HUD-04旧表“组队按钮额外发 GroupNotify(true)”的过期叙述，该包只在非legacy路径发；真正的legacy差异是 cap5鼠标入口只Open、不Toggle，而G键入口Toggle，两条当前输入路径对已打开状态行为不同，EI静态鼠标/按键均调用id6 toggle。更新HUD-04 cap5与KEY-02 G行，仍把实际按键/鼠标回放留为运行验证，未点组队窗、不触发玩家邀请或交易，也未修改实现。一次尝试读取 `GodotClient/Scripts/WindowManager.cs` 的路径错误（真实文件在 `GodotClient/Controls/WindowManager.cs`），改正后核实 `Open()`/`Toggle()`实现；没有依据路径错误作结论。`git diff --check`通过。

**2026-09-24 CHAR-03 原版字段计数与当前数据消费路径复核：**重读 `status-window-render-evidence.json::attribute_text_draw_chain` 原始数组，确认左列17条、右列11条是标签/格式文本绘制项；其 `value_field_sources` 明列左12条、右11条格式调用记录，并非“30条值调用”。部分调用有多操作数（HP/MP当前与上限、经验分子/分母、负重字段对），故将旧“28标签/30值调用”改为可直接复核的28条绘制项与23条已列调用记录，并保留JSON未列出的值链待查。沿当前源码复核 `StartInformation`、`StatsUpdate`、`HealthChanged`、`ManaChanged`、`GainedExperience`、`InformMaxExperience`、`LevelChanged`、`WeightUpdate` 路由：HP/MP当前量与Stats中的上限分开维护，经验值/上限以及bag/wear/hand重量已有客户端来源；人物窗自定义状态文字当前仅消费等级与Stats值，SetWeight结果在legacy布局中未显示。由此确认可用数据不等于EI原始字段语义已映射，CHAR-03几何和数据差异继续开放。遵照本轮要求，没有重复坐骑S/Ctrl+S、双人交易或任何可能触发Bad Request/不稳定输入的运行期测试；相关原因与解除条件见阻塞表，继续静态项目。无工具/API错误、无客户端输入/构建；`git diff --check`通过。

**2026-09-24 PRE-03 窗口分辨率下限静态核验：**从 `GodotClient/project.godot` 确认设计viewport为1024×768；从 `ClientSettings.ApplyDisplaySettings()` 确认窗口模式将 `GameSize` 两轴分别钳制为至少1024×768；从 `UiScaler.ComputeScale()` 确认CanvasLayer缩放倍率限制在1..2。因而常规窗口运行不能直接设置 EI 验收基准800×600，旧 PRE-03 只列“应测800×600”却未记录测试配置不可达，现改为明确阻塞/环境限制，并要求先提供可复现的800×600比较配置。没有把viewport、窗口尺寸和逻辑控件坐标混为一个量，也未对全屏/桌面较小模式作未经运行验证的推断。本轮只做静态读取、更新审计文档；未运行客户端、未改实现、未执行测试，未触发坐骑/交易/Bad Request路径；`git diff --check`通过。

**2026-09-24 EXIT-01 Win32 消息号交叉审计：**`chat-window-mouse-dispatch.json` 的 `0x42BE21` 链记录 F800 控件 helper 命中后调用 `SendMessageA([0x8AB7B0], 2, 0, 0)`，却把2注释成 WM_CLOSE；`input-message-bus-0x7ed-0x7f0.json` 又独立记录主窗口 WndProc 对消息2的分支为 `0x41CEF0`，`map-change-pipeline-and-main-window-class-evidence.json` / `RESEARCH_LOG.md` 将其关联到 `0x41D2B0` 窗口态保存。Microsoft Win32 消息定义明确 `WM_CLOSE=0x0010`、`WM_DESTROY=0x0002`（[WM_CLOSE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-close)、[WM_DESTROY](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-destroy)），因此审计以消息数值和独立WndProc分派为准，将 EXIT-01 改为“投递msg 2 / WM_DESTROY分支”，并撤回“明确请求WM_CLOSE/因此证明退出”的语义表述。F800 YES/NO独立hit rect、消息2后的完整窗口/进程生命周期仍未闭合；没有点击退出控件或关闭客户端。Mir3-Research 当前有 README、研究矩阵/JSON 和地图工具文件的既有改动，本轮只读未改；Zircon 中保留用户现有未提交文档与本轮未提交审计修改，不提交/推送。`git diff --check`通过。

**2026-09-24 SKL-06 F键双目标处理路径静态定案：**核对 `MagicDialog.SelectSchool()` 通过 `_list.AddControl(cell)` 建立 `MagicCellView → _list → MagicDialog` 的真实 Godot 节点祖先关系；`DXControl.AddControl()`直接调用`AddChild()`。Godot Node文档说明未处理`_unhandled_key_input`事件向父节点传播；父窗只在自己收到并成功绑定后才调用`SetInputAsHandled()`，而`MagicCellView`绑定后未处理事件。因此满足“鼠标在技能图标、父窗legacy选择有效、输入为F键”的条件时，hover-cell绑定后事件继续到parent-selected绑定：两技能不同则一次输入改写两项，同技能相同也发送重复`C.MagicKey`；子路径另未过滤Echo/ Ctrl/Alt。将SKL-06从“是否双写仍待事件追踪”更新为代码与Godot事件契约共同确认的实现缺陷；目标EI是否支持/采用相同的书内F键绑定仍标为证据不足。本轮遵照静态优先，没有注入F键、改游戏实现或重跑客户端；一次目录状态中Mir3-Research仍包含多个既有修改，本轮未写入该仓库；Zircon既有本地修改继续保留且未提交/推送。`git diff --check`通过。

**2026-09-24 SET-05 当前裁剪状态与既有截图复核：**检查 `ConfigDialog.ApplyLegacyEiLayout()` 当前源码确认 `Clip=false` 显式覆盖 `LegacyUiSkin.ApplyLegacyTestWindow()`在派发前设的根`Clip=true`；此前主表“Godot变更尚未重新构建/重启实屏”已过期。既有[`settings-window-after-fix-2026-09-24-1024x768.png`](evidence/legacy-ei-ui/settings-window-after-fix-2026-09-24-1024x768.png)是1024×768完整客户端图，证明修复版本曾实际运行且窗口已捕获；但存档没有记录F750 root screen RECT/crop offset，无法从“截图里可见窗体”推出源alpha最后9行全部对齐EI。现将SET-05后续验收改为先按原始F750透明PNG配准已存截图并标出origin/末9行，再对照目标EI同状态截图；目标EI裁切语义和800×600配置仍未闭合。只读重核当前代码、已有图像与文档；本轮未运行客户端、未触发输入或测试，不改实现、不提交/推送。`git diff --check`通过。

**2026-09-24 阻塞清单去陈旧状态复核：**将 SET-05 阻塞行与现存修复版截图、`ConfigDialog.ApplyLegacyEiLayout()` 的 `Clip=false` 及 F750 alpha bbox 证据重新对齐：撤回“无法取得修复后截图”的旧说法，改为记录已有1024×768截图但缺明确 root screen RECT/crop offset 和同状态EI基准，因此不得把可见截图误当末9行几何验收。再次核对阻塞表，交易完整往返仍要求第二名玩家；X11 坐骑 S/Ctrl+S 和其它不稳定注入按用户要求不重测；Bad Request 风险业务输入不重放；这些均只阻塞各自运行验收，静态审计继续。此轮没有启动客户端、发送输入或运行测试；未改实现。仅更新本审计文档，接下来形成独立提交；`git diff --check`通过。

**2026-09-24 PRE-11 登录动画资源索引交叉核验：**逐读 `LoginScene.BuildLegacyLoginUi()/AddLoginAnimation()`、`MirSkin.ResolveUiDataPath()/GetTexture()/GetLegacyWilLibrary()`、`DXAnimatedControl._Process()` 和 `DXImageControl.DrawControl()`。四组当前动画请求的Interface1c帧范围为2200–2299、2400–2429、2300–2329、2500–2529；本机EI根只包含Interface1c.wil/.wix，`wilsdk.WilLibrary`独立解析为2000索引且所有请求范围均超界。legacy模式在该WIL目录无对应ZL时走WIL fallback，`GetTexture()`对超界帧返回null，绘制路径跳过空纹理；所以这些动画控件在当前legacy登录模式中没有可画资源。常规`Data/Interface1c.Zl`计数3020并包含上述高索引帧，但属于另一资源根/版本，不能用作EI来源证明。EI研究`login-flow-evidence.json`只闭合登录对象加载Interface1c.wil与wemade.dat，未将高索引动画链到原版；应继续核视频与目标EXE，不能直接改用ZL或猜测要删去动画。工具错误记录：首次脚本误假设`wilsdk.WilReader`类（实际API为`WilLibrary`），随后读到索引数及frame headers；一次检索误指`GodotClient/Scripts/MirSkin.cs`（实际路径`GodotClient/Controls/MirSkin.cs`），更正后完成读取；首次补丁因整行上下文与长PRE-09文案不匹配未应用，没有文件被部分修改，随后按PRE-10精确定位更新成功。未启动客户端、未输入或改实现；`git diff --check`通过。

**2026-09-24 HUD-04 cap15 动作摘要冲突复核：**复读 F313 chat-window-control-map.json 与 F321 hud-caption-action-tail-evidence.json 的原始结构，确认 F313 内部 cap15_action 对小地图/大地图的口头标签，和 F313 的 id1→0x29CE4 映射、id15状态栏caption，以及 F321 idx15→toggle(1)→id1状态窗对象→0x423E80重定位链彼此冲突。按更具体的目标地址、窗口ID表和重定位RECT裁定为状态窗入口，F313的“double-toggle minimap/big map”归为过期/错误摘要，不再作为行为证据。对照当前 GameScene 绑到 ToggleCharacterWindow()，已有 cap15 截图只证明F200曾打开，不证明属性数据、展开态或整体窗口等价。错误记录：一次读取旧工件时把嵌套于 exe_trace 的action_table误当顶层字段，产生KeyError；另一次请求不存在的 control-hit-setpos-ctor-evidence.json 路径产生FileNotFoundError；检查真实JSON顶层键、限定到已存在的F313/F321工件后完成比对。两次补丁分别因JS字符串引号和长源码行上下文未匹配而失败，无文件被部分写入；随后用唯一标题标记插入并完成文档更新。未运行客户端或输入；git diff --check通过。

**2026-09-24 INV-01 既有 Gemini 截图/队列证据核验：**只读检查工作区未跟踪的 docs/GEMINI_UI_VERIFICATION_2026-09-24.md 与 screenshots/gemini-ui-verify-00-baseline.png、-01-hud-baseline.png、-02-inventory.png，未修改这些材料。队列表Q-02将legacy背包写成W/cap0，Q-03将状态窗写成Q/cap15；与当前GameScene._Input()源码相反：legacy Q/Ctrl+Q切InventoryDialog、W/Ctrl+W切CharacterDialog，MainPanel与GameScene把cap0连到C.TradeRequest、cap14连背包、cap15连CharacterDialog。对照EI primary-static caption/action表，Q→id0背包、W→id1状态；因此该队列两行把Q/W倒置，且把cap0交易入口误称背包入口，应当更正后才能用于回放。像素文件核验中00与01的SHA-256同为a9a73343a0386063c6edb7746fda94744b32ffb27df83653df9300649df7c6ab，属于同一张基线图；02文件虽然与基线略有像素差异，但可见画面仍只有地图/HUD，没有打开的背包窗、物品格或背包交互状态，不能作为Q-02运行通过证据。报告正文只提供环境与“验证队列”，没有逐项实际操作记录。故本次只作为错误测试映射和无效截图证据的审计更正，不将Gemini队列或这三张未跟踪PNG升级为INV runtime-verified；背包格位/滚动/物品/提示仍按INV-01..05待验。未发送热键、鼠标、业务输入；未改游戏实现；工作区原有Gemini报告、截图及LOCAL_TOOL_SERVICES_STATUS文件均保留未跟踪。一次截图差分命令调用compare得到的数值格式与预期不符，未据此定量描述差异；结论以可见UI和相同00/01文件哈希为准。git diff --check通过。

**2026-09-24 INV-05 选择器图库归属裁决与背包布局静态复核：**把`inventory-window-render-evidence.json`背包绘制链、`status-window-render-evidence.json` selector owner/path table、`equipment-slots-evidence.json`、F465/F651摘要和`RESEARCH_LOG.md` Finding 246/266并列核对。背包绘制取item data `+0x28`并向全局 selector el82=`0x5668C4`解帧；el82由`0x452B20`的完整路径表绑定`.\Data\Inventory.wil`，写入路径槽`0x570574`（store site `0x453804`）。Finding 246闭合普通装备格同样使用el82；Finding 266随后纠正扩展槽el139=`0x56B0E8`路径为StoreItem.wil（slot `0x573F58`、store site `0x4540E8`），并明确覆盖0..139的填表范围和商店分派。故F465/F651将el82概括为StoreItem/default，是把不同 selector/槽的用途混淆；`server-data-crossref.json`“el82 filename unproven”是早于Finding 246的旧候选。将主审计INV-05裁定更新为普通背包读`Inventory.wil`，不再把图库归属本身列为未决；保留研究EXE身份未在本机原始字节重放这一范围限制。目标EI本机`LegacyEI/Data/inventory.wil/.wix`存在，但这不证明它与研究EXE同版。另从`InventoryDialog.ApplyLegacyEiLayout()`与`DXItemGrid.Step`复算：通用构造器默认6×8、padding=1被legacy明确覆盖为6×6、location=(25,41)、padding=.5；step=`36−1+2×.5=36`，每格36×36，与EI首屏viewport几何静态吻合。`DXItemCell.DrawItemIcon()`仍按现代`ItemInfo.Image`访问StoreItem并居中，也没有EI记录/600-cell occupancy映射；首屏几何吻合不能代表滚动、放置或图标映射吻合。运行验收因当前CUA surface inventory为空、Gemini旧截图没有打开背包画面而无法完成；不重放坐骑S/Ctrl+S、双人交易或可能触发Bad Request的业务输入，分别沿阻塞清单继续待验证。操作错误记录：首次`rg`误指不存在的`GodotClient/Dialogs/InventoryDialog.cs`，随后用`rg --files GodotClient`定位真实`GodotClient/Controls/InventoryDialog.cs`并继续；没有依据路径错误作结论。只修改本审计文档，未改实现/启动客户端/运行测试，`git diff --check`通过。

**2026-09-24 PRE-12 选角动画索引与 EI WIL 范围静态复核：**复读 `SelectScene.BuildLegacySelectUi()/UpdateCharacterDisplay()/UpdateCreatePreview()`、`DXAnimatedControl._Process()`，并用研究工具 `wilsdk.WilLibrary` 直接读本机 `LegacyEI/Data/Interface1c.wil` 索引数=2000。两侧常驻光效请求2800–2816、2900–2916全部越界；角色展示按职业分段取介绍/待机帧，末尾兜底 idle 段2000–2009越界，创建角色兜底preview 2000起也越界；one-shot 1940段介绍期间的+100/+130 overlay 后半会超界。`DXAnimatedControl`以整段总时长比例逐帧设置Index，`MirSkin`对于越界帧取空纹理后不绘制，故这些具体状态在当前legacy资源根静态上确定缺图；有效的300–1815职业预览段仅证明有可读帧号，不证明它们就是EI原版角色动画。EI primary-static `login-flow-evidence.json` 将角色动画关联到parent角色槽的动画指针、`0x458B20/0x458EC0`，且parent另加载GameInter/Interface1c；现有证据未把Godot自选的高编号光效和介绍/待机帧映射到该调用链。已新增PRE-12，要求继续追原版动画对象及资源来源；未因现代ZL存在高帧而猜换图库/帧号，也未做登录/选角运行输入。只修改审计文档，未启动客户端或测试；`git diff --check`通过。

**2026-09-24 PRE-02 选角按钮资源视觉与静态动作分级复核：**通过当前在线 `http://localhost:8766/api/info` 和 `/api/image` 对本机 `LegacyEI/Data/Interface1c.wil` 直接读取 F51–58、F86/87、F89/90、F92/93、F95/96、F98/99，并用 ImageMagick `-trim` 测 alpha bbox、用放大 contact sheet 作近邻像素检查。确认 F51/52“创建角色”、F53/54“删除角色”、F55/56“开始游戏”、F57/58“结束”；原始头尺寸/offset及有效像素边界写入 PRE-02。三个后续态控件对分别呈斜笔图、环形箭头、文书卷页；F86/87呈勾选图、F89/90呈叉形图。对照 `login-flow-evidence.json::screens.parent.buttons`：F51 create、F55 enter、F57 exit 的 primary-static click chain 已记录；F53只有帧/RECT和可读“删除角色”图样，没有该摘要工件所列 handler；F86的证据只到 phase gate读取、F89的 handler则发送msg 0x64。视觉字样/符号不直接证明隐藏输入分支，F92/95/98也不因外观就标成翻页。错误记录：独立`wilsdk.decode()`遇到系统Python未装Pillow，随即改用预览器PNG API；一次查错了 `/home/tetsuya/development/Zircon/Tools/web/wilviewer.py` 路径，服务API仍可用，未依赖该路径继续；首次montage因尚未下载偶数态帧52/54/56/58报告文件缺失，补齐后生成拼图并完成比较。未发送任何鼠标/键盘/业务输入，未改选角实现或用户未跟踪文件；只更新PRE-02及本日志，`git diff --check`通过。

**2026-09-24 SKL-02 EI 技能图标图库入口交叉闭合：**复读 `skill-window-render-loop-evidence.json` 的 F848 链：原版技能记录 `[skill+6]` 经 selector `0x566C90`绘制；由 selector 基址 `0x5600FC`、stride `0x144`独立计算可知这是el85。再查 `mir3-dat-resource-path-table.json`，owner slot85 (`+0x10784`，store block `0x452A24`) 明确写入 `Data/MIcon.wil`。对照当前 `LibraryCore.Libraries` 的 `LibraryFile.MagicIcon → Data\\MIcon.Zl`、`MirSkin.IsUiLibrary(MagicIcon)`、legacy `UiDataPath` 和 `GetTexture()`/`GetLegacyWilLibrary()`：当EI目录缺少MIcon.Zl时，同 stem 的 `MIcon.wil/.wix` 会通过真实旧版WIL回退路径读取。本机 `LegacyEI/Data` 有MIcon.wil/.wix，且之前登录日志确认已回退至EI MIcon.wil。因此应把“图库入口是否错库”裁定为静态路径家族吻合，仍未决项收窄为 EI `[skill+6]` 与现代 `MagicInfo.Icon` 的逐技能帧映射、帧界限和绘制偏移/颜色。同步更新SKL-02与后续步骤；不按总帧数或总体尺寸替换图标映射。工具错误：先按推测文件名查询不存在的 `skill-tab-header-draw-evidence.json`，路径报错；改用实际存在的 `skill-window-render-loop-evidence.json` 与资源路径表续查。全程静态只读，没有键盘/鼠标输入、构建或运行期施法；只更新审计文档，`git diff --check`通过。

**2026-09-24 SKL-01/10 左页分页、类别帧与研究工件一致性续审：**逐项读取 `skill-window-render-loop-evidence.json`、`skill-window-context.json`、F547/F839/F848/F939 JSON 与 `skill-window-static-evidence.md`，并对照 RESEARCH_LOG Finding 200。确认F848/F839摘要只给类别链表绘制图标/名称/高亮及类别计数除3后的格式化候选，不能闭合绘制上限、六个hit RECT、箭头动作之间的页模型；已将“不据 `/3` 推成两页各三项”写入SKL-01。发现类别帧表冲突：`category_labels` 和静态Markdown对后三类重复为F450/F452/F454，而constructor geometry与Finding 200记为F460/F462/F464；同版研究EXE不可读，已在待验证/阻塞清单登记，不猜帧号、不改实现。工具错误：首次从 `skill-window-context.json` 取不存在的顶层 `window_constructor_control_geometry` 字段触发 `KeyError`；查看schema后改从该字段实际所在的 `skill-window-render-loop-evidence.json` 读取，并继续交叉比较。期间一次宽泛rg输出被截断，改为精确读取目标行/JSON完成核验。无客户端、键鼠或业务输入；未运行构建/测试。

**2026-09-24 SKL-05/08/10 当前实现帧映射源码对照：**读取 `GodotClient/Controls/MagicDialog.cs::BuildLegacySchoolButtons()/AuditLegacyEiLayout()`；当前8按钮映射F450、452、454、456、458、460、462、464，位置与研究 constructor geometry 表一致，也与 `RESEARCH_LOG` Finding 200 对后三类的记录一致，但后3项与 `skill-window-context.json::category_labels`、`skill-window-static-evidence.md` 不同。`AuditLegacyEiLayout()`只断言按钮数量/位置，没有断言或独立证明帧号。现将SKL-10从“控件业务身份冲突”修正为“控件身份已确认、后三个控件帧号研究工件冲突”，并在SKL-05/08标出帧序限制。此为源码及资料对照，不据一侧材料改代码，等待目标EXE字节复核。未运行客户端或测试。

**2026-09-24 CHAR-02 既有稳定态截图像素对照：**只读打开 `character-collapsed-2026-09-24.png`、`character-expanded-2026-09-24.png`，`identify` 确认为244×330与520×330；代码逻辑根高328，图像各多2px，故继续不以文件边界作为根RECT。把展开截图裁到244×330后，以ImageMagick逐像素比较：左上对齐 `145×330` 区域AE=0；共同244×330区域报告1033个差异像素，显示差异在属性文字和切换按钮帧附近。因截图是分别裁好的稳定态、没有原始客户端画面/crop offset，结论仅为归一化截图内左侧静态画面未移位，不证明绝对屏幕锚点、窗口剪裁或按下/释放时序。ImageMagick IM7对兼容命令`convert`打印deprecated警告但裁切与比较成功；无程序/API错误。没有启动客户端、输入或运行测试。

**2026-09-24 TRADE-04 双方交易数据容量静态核对：**交叉读取 `TradeDialog.cs` 的 `_playerItems[10]`、`SetOtherItem()` 和每方30格的 `ApplyLegacyEiLayout()`，以及 `GameScene` 的 `S.TradeItemAddedEvent → SetOtherItem()` 订阅。再对照 `ServerLibrary/Models/PlayerObject.cs::TradeAddItem()`：服务端拒绝条件为现有 `TradeItems.Count>=15`，通过时逐件发 `S.TradeItemAdded` 给伙伴。因此静态上当前接收侧只能保存/显示10件，而服务端可接受11–15件；本地30格布局也超过当前协议最多15条记录的限制。Mir3-Research `trade-window-render-evidence.json` 所列 EI 24个 slot records 尚不足以判定每侧真实容量，故没有把本发现外推为EI容量差异。按要求没有重做双人交易或发送交易请求；完整往返继续阻塞，只登记源码容量路径。`git diff --check`通过。

**2026-09-24 CHAR-05 状态窗 HUD/W 入口复位语义静态对照与本轮收口：**交叉读取研究工件 `hud-caption-action-tail-evidence.json::exe_trace.action_table_0x42C494_16.idx15`、`window-paint-and-hotkey-dispatch-evidence.json::hotkey_dispatcher_0x42CC76`，以及当前 `GameScene.ToggleCharacterWindow()`、legacy W 分支、`CharacterDialog.ShowOwn()/ApplyLegacyEiLayout()`。EI HUD cap15 在切换 id1 后无条件将状态对象模式字节 `[+0x54]` 置0并以 F200/244×328 重设窗口矩形；EI W/Ctrl+W 只切换 id1。Godot cap15 与 W 共用 `ToggleCharacterWindow()`，关闭后重开都会进 `ShowOwn()` 并强制套用收起属性态。故“展开→W关闭→W重开”在模式保留上存在静态可证差异；补入 CHAR-05，并把 HUD-04 cap15 行改为区分两个原版入口。下一步需确认 cap15 关闭时的重定位是否会影响随后 W 打开，然后在可靠窗口输入条件下回放两种重开路径。未改游戏实现，不据静态链伪称运行验收通过。

本轮阻塞清单明确保留并跳过坐骑 S/Ctrl+S、不稳定热键注入、双人交易完整往返、可能触发 Bad Request 的业务输入，以及 CUA 未暴露窗口时的运行期 UI 操作；继续静态审计，不因这些阻塞停止。工具/查询错误记录：一次 `rg` 查询猜错 `GodotClient/Scenes/GameScene.cs` 路径，返回 ENOENT；以 `rg -l` 定位实际 `GodotClient/Scripts/GameScene.cs` 后完成读取。一次包含整个 docs 与 research 的宽搜索输出被截断，之后改为限定审计文档与目标工件查询；无结论依赖截断输出。未启动游戏、未注入输入、未运行构建或测试；只改审计文档，未触碰原有未跟踪资料。提交前执行 `git diff --check`。


**2026-09-24 审计文档本地证据链接核验：**扫描 `LEGACY_EI_UI_AUDIT_2026-09-23.md` 的43个 Markdown 链接，发现唯一失效的仓库内链接为 `settings-window-fx-off-2026-09-24-1024x768.png`。在仓库和 `/tmp` 中按文件名查找后，只找到通用 `settings-window-off-pressed-2026-09-24-1024x768.png` 及 FX 滑块端点图；打开比较截图后确认它们不能证明 EffectSound OFF 状态，故将该处修正为“FX关闭状态截图缺失”，不替换成语义不等价的图。首次查找错误地使用仓库根 `evidence/legacy-ei-ui`（真实目录为 `docs/evidence/legacy-ei-ui`）；链接扫描脚本第一次把 `re.findall()` 的单捕获结果按二元组解包，触发 `ValueError`，随后更正为单列表逐项检查并完成定位。错误路径与脚本异常均未产生文件改动或影响审计结论。

**2026-09-24 GUILD-01 F600 原始帧与当前根矩形复核：**通过用户指定的本机viewer只读查询 `http://localhost:8766/api/info?f=GameInter.wil&i=600`，得到 F600 原始画布1024×512、WIL header offset `(7,-44)`；再以 `/api/image?f=GameInter.wil&i=600&scale=1&bg=transparent` PNG 做独立透明边界裁切，得到 alpha bbox 尺寸594×445，与审计中引用的既有尺寸一致。当前 `GuildDialog.ApplyLegacyEiLayout()` 明确设置根446×596、背景原点(0,0)、根Clip继承自legacy wrapper；原版 `layout.json.window_initialization_evidence.records[id=4]` 的构造参数则记 F600 根宽596、高446。该次资源检查加强了“当前根尺寸将转置并裁切原版UI”这一静态差异，但因为trim本次没有可靠取到alpha bbox位置，也没有目标EI屏幕原点/截图，不对根外绘制、素材锚点或最终屏幕位置作进一步推断。工具错误记录：第一次按错误仓库相对路径读取素材截图目录（正确位置是 `docs/evidence/legacy-ei-ui`）；首次 ImageMagick 格式字符串使用了该版本不支持的 `%+` 属性，随后 `identify -trim` 命令因该版本不支持此选项再次失败，改用 `magick input -trim -format ... info:` 成功得到594×445；期间一次包含全审计矩阵的宽查询输出截断，后续限定到 GUILD-01 原始工件与 `GuildDialog.ApplyLegacyEiLayout()` 源码后完成。错误命令没有产生仓库文件或影响判断。只补充静态证据记录，没有改布局实现、启动客户端或发送输入。

**2026-09-24 GUILD-01 alpha bbox 坐标更正补证：**上一条记录曾说明透明边界裁切只可靠取得了F600 bbox尺寸、未取得位置；本轮重新用 `magick /tmp/zircon-audit-f600.png -trim -format 'trim=%wx%h offset=%X%Y page=%[page]' info:` 得到完整结果 `594×445 offset=+(214,33)`、page `1024×512`。这与GUILD-01所列的alpha half-open范围 `[214,808)×[33,478)`一致。结合当前legacy wrapper显式设 `Clip=true`、Guild根 `446×596` 且背景放在(0,0)，裁切后F600有效像素交集为`[214,446)×[33,478)`，右侧362列有效像素落在根宽之外；此几何仅描述当前Godot legacy测试树，不能反推EI原版屏幕最终clip。另检查 `GuildDialog.cs` 发现类级注释“原版 GuildDialog(Interface 260)”与其构造器/旧版profile及EI F600证据相矛盾，已列GUILD-04为待改注释问题，未改源码。初次尝试 `identify -trim` 不支持该参数，改用 ImageMagick 7 的 `magick ... -trim -format` 后成功；不再沿用前条“位置未取到”的范围限制。没有启动客户端或输入，未改布局实现。

**2026-09-24 CHAR-06 人物双态与 id7 候选分离复核：**为裁定“装备栏右侧可开合属性纸”的窗口编号，重新并列检查 `status-window-render-evidence.json`、`status-window-family-evidence.json`、`window-paint-and-hotkey-dispatch-evidence.json`、`window-id-catalog.json`、`layout.json::window.id7-status-right`、`trade-chat-option-paint-evidence.json` 与用户提供的旧版画面。原版 id1 初始 F200/244×328；同一 id1 对象的展开箭头 handler `0x44CCD0` 对 `this` 调 `0x423E80` 改为 F201/520×328，且状态字节写1。因此用户截图里随箭头出现的左右横向扩展属于 **id1 的 F201 状态**，不是新开的 id7。`localhost:8766` 读取 F201 原始头为1024×512、offset(+7,-44)，透明 PNG trim 为 alpha bbox `(252,92,518,327)`；显示出的 F201 是左右两区合成的一张图，其来源帧/组合外观与用户截图相符。当前 `CharacterDialog.ToggleLegacyView()` 在同一 Godot 窗口切换 F200/F201并设520×328，窗口对象映射已对上，但布局/文字/交互仍按 CHAR-02/03/05未验。同步更正导航矩阵的 id7 行：独立 `hero+0x47C28` / F200 对象不能再写成已定论的消息日志窗；Round32/F338与Round33/F339、F353后续资料指向右侧形象/属性预览，而旧 `window-id-catalog.json` 仍有消息/日志候选标签，身份保持冲突。`window-id-catalog`旧标签不再作为 id7 定论。全程读取既有截图和WIL，不启动游戏、不发送输入、不改实现；无工具/API错误；文档链接与格式检查随后复核。

**2026-09-24 CHAR-06 / id7 旧研究标签依据再核：**检查 `status-window-render-evidence.json::item_resource_selection_chain.global_selector_array` 的基址`0x5600FC`与步长`0x144`，独立计算 `0x566DD4 = base + 86×0x144`；同一证据将 el86 的 WIL 路径闭合到 `Data/ProgUse.wil`，`horse-window-render-evidence.json`也直接用该 el86绘制骑乘图标；F338/F339窗口证据则把 `0x450530` 同一绘制目标记录为角色预览和属性slot链。由此旧 `window-id-catalog.json` 将该地址描述为“msg object”的注释不能继续支撑 id7 message/log 窗身份，应视为早期资料误标/过期候选。将id7更改为“更支持状态形象/装备属性预览，具体触发和组队业务未闭合”，保留证据限定：研究目录中的更多摘要不是原始目标二进制本身，本机研究EXE仍不可读；不把组队成员弹窗含义升为最终定论。id1/F201右侧展开属性纸则由同一个 `hero+0x29CE4` object的展开分支和F201素材独立闭合。只更新当前审计文档；未改Mir3-Research参考资料、游戏代码或未跟踪文件，未启动游戏/发送输入。无工具/API错误。

**2026-09-24 id7 / GROUP-06 独立预览入口对照：**读取 `window-visibility-dispatch-evidence.json` 的 `window_id=7` 记录与 command caller 表，确认 `0x42C0A6` 将 id7送入显隐dispatcher，工件把它标作 `group-pop command path`；这为“与组队上下文有关”提供 primary-static 调用角色线索，但尚未给出被点成员、数据填充和触发条件。对照当前 `GameScene.OnGroupMember()`、`GroupDialog.RebuildMembers()/SelectMember()`、`GroupHealthPanel.GroupHealthRow`：服务端组员事件只更新成员清单与常驻血条；GroupDialog的成员点击用于设置 `_selectedMember`，血条行没有鼠标处理器，未找到独立 F200 状态/属性预览窗口入口。`GroupHealthPanel.cs` 类注释虽写“原版 GroupHealthDialog”，审计资料尚未给此类/布局找到能证明等价的原版对象链，不能拿注释本身当证据。因此新增GROUP-06，把id7 group-pop候选调用链与当前实现缺口分开记录；原始目标EXE仍不可读，成员属性来源/详情字段/输入行为保持待证。本轮未触发组队协议或输入，也没有改业务代码。

**2026-09-24 id7 F200 素材与子控件几何交叉补证：**通过当前 `localhost:8766` 对本机旧版 `GameInter.wil` 的 `/api/info?f=GameInter.wil&i=200` 读取头字段(256×512、offset +7/-44)，再经透明PNG与ImageMagick `magick -trim`独立测得 alpha bbox `241×327+(6,92)`；打开PNG只能支持垂直装备/角色状态面板的视觉候选。研究 `window-initialization-evidence.json` 记录id7构造根 `(560,0,244,328)`，`window-control-position-analysis.json` 对 F161/162 关闭子控件给根相对 `(212,298)`、尺寸28×26，`window-visibility-dispatch-evidence.json` 将 `0x42C0A6` 的 id7切换入口称为group-pop command path。这些证据加强“F200状态/角色预览，与旧message/log注释不符”的裁决；仍不能证明具体触发实体/业务文本，而且本机 WIL 与研究 NAS 目标资源的文件身份未核成相同。审计矩阵新增根矩形、帧alpha bbox、close child hit rect及证据级别边界。未启动游戏/注入输入、未修改实现；无工具/API错误。

**2026-09-24 id7 属性槽证据摘要一致性续审：**复查 `window-paint-and-hotkey-dispatch-evidence.json` 的实际 schema 后，确认 `cell_analysis.window_identities_final.id7.identity` 将属性槽概括为13条、范围 `+0x578..+0x5E8`，但同文件 `notes` 实际只枚举八个每隔0x10的偏移，另列 `+0x200 figure rect`。因此计数13与所列字段不能互相支持；本审计将其降为“属性槽链候选”，在阻塞清单标明需同版原始指令/SetRect调用闭合，不按摘要计数修改实现。查询开始时错误地遗漏了 `research/ei-ui-layout/` 子目录，读取失败 `FileNotFoundError`；第二次假设 `notes` 位于 `cell_analysis` 内触发 `KeyError`。随后用 `rg --files` 定位文件并检查实际顶层schema，成功恢复核验。错误查询均只读，没有改动文件。按要求跳过坐骑S/Ctrl+S、可能Bad Request的输入和双人交易；本轮只做静态研究JSON与已有审计文档核对，未运行游戏/构建/测试。

**2026-09-24 GUILD-04 注释纠正：**依据 `GuildDialog` 默认构造器、`ApplyLegacyEiLayout()` 与原版 id4/F600研究记录，修正类级 XML 注释：现代背景是 `Interface 260`，EI legacy profile 是 `GameInter F600`。同步将GUILD-04标记为已完成；不改变窗口控件或布局，GUILD-01几何差异仍未验收。一次初始搜索把 `LegacyUiSkin.cs` 假设在 `GodotClient/Scripts/`，工具报路径不存在；以 `rg` 搜索实际调用后完成检查。未运行游戏/构建/测试，`git diff --check`通过。

**2026-09-24 EXIT-01 注释与操作分流静态续核：**对照 `MainPanel` cap3/cap4 回调、`KeyBindManager` Alt+Q/Alt+X绑定、`GameScene.HandleKeyBind()`、`LeaveGame()`/`ExitClient()` 与 `ExitDialog`按钮回调，确认当前两个HUD按钮与两个快捷键都进入同一个自制双选对话框；对话框“返回角色选择”走 `SendLogout()` 并等待 `GameLogout`，而“退出客户端”先发同一Logout、断开连接并退出Godot进程。原版primary-static证据则把 cap4 直接连到 F950/type0x65，cap3 连到独立 F800/id0x64对象，具体按钮 hit rect/确认和取消分支仍未闭合。因此不改动作路径，只将 `ExitDialog` 的错误“原版”类注释改成明确的Zircon双操作身份，并在EXIT-01保留分流差异。一次搜索把 `KeyBindManager.cs` 猜在 `GodotClient/Scripts/` 而非 `GodotClient/Controls/`，工具返回路径错误；随后定位到实际文件并完成核对。没有点击确认/注销、没有发送业务输入或运行测试；`git diff --check`通过。

**2026-09-24 NPC-01 空滚动控件静态裁决与帧图归档：**复核 `npc-window-render-evidence.json` 的 `0x440290`/`0x440C30` 输入链，以及 `NPCDialog.ApplyLegacyEiLayout()`、`DXVScrollBar.ResizeChildren()/DoMouseWheel()`、`DXImageControl.Index`、`MirSkin.GetSize()`、`LegacyWilLibrary.GetSize()` 和 `NPCTextControl._GuiInput()`。本机 `:8766/api/info` 显示 EI F52–55均有12×8帧，F385/F387为blank；直接从viewer导出F52–55五倍PNG并归档，独立trim为9×8有效像素范围(源帧右边保留3px透明)。当前legacy up/down按钮没有 `FixedSize`，索引387/385的空帧令Control尺寸归零；PositionBar索引-1也归零，正文的wheel事件没有接到滚动Value，故除了原代码的数字滚动值变量外没有有效legacy滚动输入路径。这将NPC-01从“错误帧/位置”扩展为源码确认的滚动失效；具体替代控件实现仍待计划，不在缺少同版EI根/命中边界时盲改坐标。发生的工具错误：第一次把 `DXVScrollBar.cs` 猜作 `DXScrollBar.cs`，rg报告文件不存在，随后定位实际文件；ImageMagick montage因无默认字体报错，未采用其输出，逐帧PNG独立保存和查看成功；对空白F385/F387执行`-trim`时报告“geometry does not contain image”，与blank资源相符，不作为成功图像证据。所有错误均在只读查询/临时文件阶段，无仓库代码副作用。没有运行游戏、业务输入、构建或测试；`git diff --check`随后检查。

**2026-09-24 NPC-02/03 内嵌选项输入事件续核：**检查目标研究primary-static `npc-window-render-evidence.json::window_object_model.model_input/click_buffer` 与旧版可用 `Client/Scenes/Views/NPCDialog.ProcessText()`、`Client/Controls/DXControl.OnMouseClick()`、`Client/Controls/DXScene.OnMouseClick()`，再对照Godot `NPCTextControl._GuiInput()`。当前内嵌标签在左键Pressed即发送`C.NPCButton`；旧 Client 是给拆开的`DXLabel`绑定`MouseClick`，并由场景MouseClick分派调用，这说明迁移事件时机不同，但旧Client版本身份不足以裁定EI原版是按下或抬起。旧 Client 将`PageText.MouseWheel`显式连到scrollbar，Godot没有对应连接，独立强化NPC-01“无滚轮通路”结论。原版primary-static `0x440290`闭合了节点列表五个子RECT和点击缓冲/`@@`前缀，但没有把本次查到的旧Client事件API升级成目标EI输入时机证据；NPC-02保留F1101/F1102与id11窗业务关系未决。没有输入真实NPC选项、发送服务端请求或运行测试；只更新审计文档，`git diff --check`通过。

**2026-09-24 WH-03 消息号冲突裁决：**追读F363、F399与F517/F521/F551各自研究工件，发现早期 `store-window-content-verification-evidence.json` 把 `0x2BC` 接到 store `0x44F940` 的链，与后续store状态图实际记录的 `0x2C0→0x420A86/0x420A95→0x44F940` 不一致。接收分发表及 F551 的 `0x2BC→0x420AFC` 链则明确是打开 bag-manager 窗口、设置 mode3并走 `0x111/0x112/0x113`。因此两组摘要指向不同UI对象/状态，早期“同一F1001 state2消息号有冲突”是跨对象误归因，不再保留为二选一阻塞；更新WH-01/03分开记录。研究原件同版Mir3.exe仍不可读，所以这是研究证据内部的primary-static闭合，不宣称目标运行版已重放。查询初次把 `recv1-handler-semantics-evidence.json` 错放在 `research/ei-ui-layout/`，触发 `FileNotFoundError`；`rg --files` 定位其真实位置在 `research/mir3-map-reconstruction/` 后，按真实路径完成读取。只改当前仓库审计文档，未改外部 Mir3-Research 工件、游戏代码或业务数据；未运行客户端、键鼠输入、构建或测试；`git diff --check`通过。

**2026-09-24 INV-02 / WH-03 Zircon背包仓储模式调用链核验：**检查 `LibraryCore.Enum.InventoryMode/GridType`、`InventoryDialog.SetLegacyMode()/SellMode()/NormalMode()`、`GameScene.SetInventoryLegacyMode()/SendItemMove()/OnItemMove()`及全仓调用点。确认`InventoryMode.Storage`/`IsStorageMode`/标签分支存在但无调用点；`SetInventoryLegacyMode()`唯一业务调用是NPC repair response→`InventoryMode.Repair`。当前 `GridType.Storage`属于独立`StorageDialog`目标，物品移动由通用`C.ItemMove`路径传输，不等于EI primary-static `0x2BC`打开 `hero+0x6554` bag、设mode3并使用`0x111/0x112/0x113`的业务链；F1001 state2/0x2C0仍是另一候选store对象。同步加细INV-02的缺口说明，不新建运行期流程、不发送存取请求。一次只读调用点搜索结果已核实为仅NPC repair引用。`git diff --check`通过；无游戏、构建、测试或业务输入。

**2026-09-24 本轮静态审计收口与提交范围：**遵照最新要求，将坐骑 `S/Ctrl+S`、需第二玩家的交易全流程、可能触发 `Bad Request` 的运行输入继续保留为阻塞项并跳过；没有启动/注入客户端。只读链接核验确认本审计文档43个本地Markdown链接全部有效。文档与已归档的NPC F52–F55素材图纳入本轮审计提交；旧 `ExitDialog` 类注释更正也一并纳入，因为它已纠正源码中把Zircon自制双操作窗误称为原版窗的事实。检查脚本第二阶段尝试提取阻塞表时假定其后必有下一个二级标题，实际本节延续到后续内容而触发 `ValueError: substring not found`；该提取没有写文件或影响结论，手动读取明确的阻塞表后继续。当前仍不能闭合的事项（包括匹配版 EI EXE/WIL 身份、技能书分页、人物展开逐帧、GUI 窗口可观察性、目标版运行验收）继续保留为待验证，不标为通过。本轮不将旧工具状态文档混入UI审计提交。`git diff --check`与43个本地链接复核通过。

**2026-09-24 SKL-04 F400 根矩形口径纠错：**复查旧版研究 `window-initialization-evidence.json::records[window_id=14]` 的主初始化调用记录、`skill-window-context.json::window_size`、`skill-window-static-evidence.md` 与 `skill-window-render-loop-evidence.json::window_constructor_control_geometry`。主初始化调用点 `0x00427904`将ID14尺寸记为296×332；专用包装器 `0x00439250`中的452/380只作为其内部原始参数出现，研究文档已明确不能直接解释为屏幕/根RECT。故撤回此前将452×380认作EI根窗、并据此宣布本机F400 alpha与EI根几何吻合的说法。当前 Godot `MagicDialog.ApplyLegacyEiLayout()`仍设452×380，F400贴图位置(-30,-67)只将本机WIL alpha左上角对到Godot根原点；它不证明EI原版使用同一资源版本或同一裁剪。ID14的296×332保留为主初始化primary-static尺寸证据，最终显示矩形仍需核共享构造器 `0x00423B30` 的调用实参/SetRect与目标WIL头；在此之前不改布局实现。同步更正高优先级结论、id14导航表、SKL-04及阻塞清单。检索时将 `MagicCellView.cs` 误假设为独立文件，`rg`报告文件不存在；确认该类实际定义在 `MagicDialog.cs` 后继续完成。两次定点补丁因表格实际行文/终止分隔符不匹配而安全失败，没有落盘；改用精确行上下文后更新成功。未启动客户端、未发送技能/坐骑/交易或可能Bad Request的输入；未构建或测试；本轮末`git diff --check`通过。

**2026-09-24 SKL-04 current login-profile裁剪路径续核：**进一步静态追 `GameScene.ApplyLegacyCoreTestLayouts()`（`--legacy-ui`登录游戏模式调用）、`LegacyUiSkin.ApplyLegacyTestWindow()`与`MagicDialog.ApplyLegacyEiLayout()`。实际 legacy login 路径把MagicDialog设为452×380，开启根裁剪，把当前本机F400子图设在(-30,-67)且不拉伸；以本机WIL alpha bbox `(30,67,451,378)`计算，所有有效像素恰落在该测试根452×380内。这说明该profile按本机大帧内容配置，而 EI id14主初始化记录为296×332，形成源码可证的尺寸差异；素材身份和EI版真实crop仍未闭合，故继续不改坐标、不据Build/登录替代视觉验收。尝试CUA读取可见应用时返回 `apps=[]`, `browsers=[]`，本轮没有打开或输入客户端。一次源码搜索把 `LegacyUiSkin.cs`误放在 `GodotClient/Scripts/`，`rg`返回路径不存在；以 `rg --files GodotClient`定位到`GodotClient/Controls/LegacyUiSkin.cs`后继续。F400预览器API成功（HTTP 200，152字节JSON）。审计文档43个本地链接无缺失，`git diff --check`通过；只改本审计文档，未触碰保留文件或提交/推送。

**2026-09-24 SET-03 原版隐藏状态 hit RECT 证据再分层：**对照 `system-window-render-evidence.json::windows[id=window.option].hit_rects`、F704 `control-hit-setpos-ctor-evidence.json`、F761 `options-toggle-click-handler-evidence.json`、选项构造器与当前 `ConfigDialog.CreateLegacyOptionButtons()`。可确认原版开窗时左/右四行矩形分别为32×22与40×22，click handler会顺序检查11个控件，通用 hit helper 对已存RECT作 `PtInRect` 且不查帧可见性；但F704只把`0x417880`总结为写帧字段和RECT，未说明传入frame=-1时最终RECT是否清零。当前Godot `DrawImage=false`只隐藏绘制，8个按钮Control和各自矩形仍持续可交互；源码行内“Keep both original RECTs hittable”把这一选择写成已证EI行为，证据不足。已在SET-03主表撤回这项等同性陈述，将原版负帧Setter后果留作静态阻塞；本阶段未改控件行为或布局。工具错误：首次只读研究检索使用zsh未匹配的 `*exit*` glob，报`no matches found`；改为明确的 `rg --files` 清单后继续。另曾把F704证据误按`ei-ui-layout`目录搜索，未命中；通过跨研究目录的 `rg --files` 定位其真实路径 `research/mir3-map-reconstruction/control-hit-setpos-ctor-evidence.json` 后完成核验。CUA当前`apps=[]/browsers=[]`，未尝试输入或启动客户端；没有重复坐骑S/Ctrl+S、交易或Bad Request测试。文档链接仍为43个本地目标全有效，`git diff --check`通过。

**2026-09-24 SET-03 可用旧Client控件旁证：**只读检查 `Client/Controls/DXImageControl.cs` 与 `Client/Controls/DXControl.cs` 的Index/Size/DisplayArea/mouse-over路径。此较新Client在Index改变时调用`UpdateDisplayArea()`；其 `Size` getter 只有 `Library!=null && Index>=0 && !FixedSize` 时才返回资源尺寸，否则回退`base.Size`；重算DisplayArea使用现有Location/Size，`DXControl.IsMouseOver()`按DisplayArea矩形测试并检查Visible/IsControl，而不检查DrawImage或Index。该客户端控件实现因此支持“设无效帧后仍可能保留基类RECT”的旁证假设；但可用Client树中没有F750设置窗实现，且版本身份与EI目标EXE不一致，不能替代 EI `0x417880` 的负帧分支证据，SET-03仍保留未决。尝试检查 `Client/Scenes/Views/ConfigDialog.cs` 时路径不存在；定位到控件类后搜索确认旧Client无此设置窗对应实现，因此只使用其通用控件行为并降低证据等级。未启动/操作客户端、未重做设置点击、坐骑S/Ctrl+S、双人交易或Bad Request路径；只改审计文档。`git diff --check`通过。

**2026-09-24 GUILD-01/02 legacy 控件溢出与窗口树对照：**重读 EI `guild-window-paint-evidence.json`/F348、F755 `guild-window-draw-evidence.json` 的9个控件数组和三态绘制，及当前 `GuildDialog` 默认构造器、`ApplyLegacyEiLayout()`、`UpdateTabVisibility()`和`LegacyUiSkin.ApplyLegacyTestWindow()`。确认 legacy profile 的根Clip=True且当前根宽446；有行会时当前6个现代页签全部可见，x=`14+76*i`、宽68，最右页签为`(394,39,68,25)`，越过当前根右边16px并被裁。原版id4不是六页签导航，而是`[this+0x98]`驱动的3类列表、`+0x76C`滚动条和9个每帧重定位控件（click order `0,1,2,3,4,7,5,8,6`）；paint证据中控件可见位置落于596×446根内，当前446×596根同时会把本机F600 alpha有效画面右侧362px裁掉。已把页签越界RECT和错误根模型写入GUILD-01；GUILD-02/03保留逐控件动作/服务端字段映射，不把相同的“行会”标题或现代相关业务功能当等价。一次起始查询假设有`guild-window-render-evidence.json`，该文件名不存在；以`rg --files`定位实际 primary静态工件`guild-window-paint-evidence.json`及F755/F945细化工件后继续核对。未运行游戏、未测试行会业务输入、不改控件布局；用户要求跳过的坐骑、双人交易和Bad Request输入均未触发。`git diff --check`及审计文档本地链接复核待本轮收尾。

**2026-09-24 QUEST-05 透明页签仍拦截输入静态核验：**沿 `QuestDialog.ApplyLegacyEiLayout()` → `AddTab()` → `DXControl.UpdateMouseFilter()` 核对 legacy 任务窗的实际节点行为。三个现代页签被 alpha=0 隐藏绘制但仍 Visible/Enabled；其 `MouseClick` 回调仍可更改 `_page` 并重建业务内容，控件基类仍设 `MouseFilter.Stop`。此项把既有QUEST-01里“页签透明”提升为明确的交互差异/命中遮挡问题，不改布局，等F700原版窗口树与矩形核实后再排入修复计划。EI侧只引用 `quest-window-render-evidence.json` 的primary-bytes文本列表链和 `RESEARCH_LOG.md` F942对F721/722（音效cmd 0x69）与F723/724（msg 0x418）的分流；不会以现代页签推定原版翻页。一次误查不存在的 `quest-window-input-evidence.json` 与 `quest-window-render-detail-evidence.json` 文件名，随后以 `rg --files` 和RESEARCH_LOG中已有F942专项记录定位到实际证据；不存在的路径输出未被用于推断。CUA未提供可见应用，未启动/操作客户端或发送任务/里程碑业务输入；未触发坐骑S/Ctrl+S、双人交易或Bad Request输入。本轮只更新审计文档；不提交/推送。

**2026-09-24 PRE-13 登录贴图库在legacy资源根缺失：**重读 `LoginScene.BuildLegacyLoginUi()`、`MirSkin.ResolveUiDataPath()/GetLibrary()/GetLegacyWilLibrary()`、`DXImageControl.Index/DrawControl()`，并检查 `/home/tetsuya/mir3ei/LegacyEI/Data/` 文件清单。`--legacy-hud` 把 `LibraryFile.Interface` 指向 EI 资源根，但根内没有 Interface.Zl/WIL/WIX；故 F151 容器底图与 F152/F153按钮贴图均无法取到纹理，`GetSize(F151)`为零后780×115只用于算位置，没有替代贴图。Godot文本/控件本身可能仍绘制/响应，但不能把当前登录表单描述成已使用EI登录美术。与EI primary-static登录对象加载Interface1c.wil/wemade.dat的证据一并保留；正确底图来源仍待EXE绘制链和目标EI同态画面闭合。本轮尝试把该发现并入PRE-09，但准确核对发现PRE-09已有“动作与素材映射”审计且PRE-08已被设置按钮空帧占用，因此新增PRE-13，未重用或覆盖已有编号。一次补丁尝试使用了过期/不准确的PRE-07文字作为精确上下文而安全失败，没有文件写入；改为按行首唯一定位后新增条目。未启动登录、未输入账号或进行网络请求；只更新审计文档，不构建/运行，不提交推送。

**2026-09-24 WH-01/02 F1001图库身份消歧：**复核 `window-resource-handle-bindings.json::window.store-candidate`（primary-static-handle-flow）、`resource-path-table.json` slot70/138/139、`store-window-render-evidence.json::frame_visual_verification`，并通过用户指定的 `localhost:8766` 预览器读取/显示 GameInter F1001。静态窗口resource argument 经主UI owner `+0x5898` 绑定 `Data/GameInter.wil`，slot70亦为GameInter；EI目录本机F1001帧头为256×256、offset(7,-44)，预览可见紧凑四列网格及页箭头。相同编号在 `Storeitem.wil` 是24×20小图，`MonMagicEx.wil`是80×124怪物图，且 `store-window-render-evidence.json`明确StoreItem 1000段不是面板；因此WH-01/02里的面板必须写作GameInter F1001，不能把StoreItem物品帧表并进面板资源归属。保持旧版状态2是否实际业务叫仓库/扩展商店为candidate；只闭合resource family与当前`StorageDialog`局部格距相同，不闭合独立窗口、父子层级或完整状态等价。一次错误试用`WilLibrary`上下文管理器触发TypeError（类不支持context manager），且默认Python缺Pillow导致`decode()`无法生成alpha像素；随后只用其可用 `header()` 和预览器成功取帧头/PNG，不把解码失败作为证据。一个早期viewer curl误用8765（mapviewer端口），连接失败后改用正确8766素材服务并取得HTTP结果。只修改审计文档，不运行客户端/存取操作，不提交推送；错误调用未产生仓库副作用。

**2026-09-24 WH-02 当前仓储网格槽号可达范围静态核验：**沿 `DXItemGrid.CreateGrid()` 的 `slot=y*GridSize.X+x`、`DXItemCell.MoveItem()`/`GameScene.SendItemMove()`到服务端 `PlayerObject.ItemMove()`，并对照 `GameScene.FillStorage()` 与 EI `store-state-graph.json`。当前 `StorageDialog.ApplyLegacyEiLayout()` 固定生成4×3格；cell把本地`Slot`原值送入 ItemMove，不含分页基址。Godot当前完整Storage数据数组按`ClientUserItem.Slot`填充，服务端容量检查按账号`StorageSize`而非12槽；故EI皮肤网格只能直达0..11，超过首屏的槽没有F1014/F1016箭头映射或页偏移。EI研究静态证据给state2 `+0x7E8` page count字段和两组28×26前后页控件，但尚未从page click闭合实际槽基址变化；不推测页大小，也不将当前服务端100槽直接套作EI页数。一次更新WH-02的脚本因锚点文本包含`当前 StorageDialog…`上下文而未命中；脚本在写入前退出，没有文件变更，按唯一短语定位后更新成功。无运行存取操作；只更新审计文档，不提交或推送。

**2026-09-24 WH-02 EI state2 分页字段证据口径校正：**对照研究工件 `RESEARCH_LOG.md` Finding 172/198 与 `layout.json::selected_item_and_paging/state_2_page_control_candidate`。日志明确state2构造 `0x44F9E4` 重置`+0x7E0`、设置`+0x7E4`，摘要称页数按`+0x71C/12`计算且`+0x3D8`进入分页/选择分支；另一份 `store-state-graph.json` 将`+0x7E8`标签为page count。现有证据没有展开`+0x3D8`分支内的读写/消息，也没闭合页面索引到物品记录、屏幕12格和协议槽号的映射，因此将字段标签差异保留为待同版机器码核实，不臆断两个字段等价。此次仅复核静态研究工件与当前源码，不执行仓库存取、坐骑S/Ctrl+S、交易或Bad Request相关输入；先前一次固定字符串查询因未转义`+`被正则解释而命中范围不准，改用`rg -F`后已正确定位。`git diff --check`和审计链接复核在提交前执行。

**2026-09-24 SET-05 F750透明帧与既有运行截图配准：**从 `http://localhost:8766/api/info?f=GameInter.wil&i=750` 读取F750头信息（256×512，header offset +7/−44），通过image API导出透明帧并归档为 `docs/evidence/legacy-ei-ui/gameinter-frame-750-wil-2026-09-24.png`；独立alpha bbox为`(4,119,248,273)`。按当前源码 `_background.Location=(-4,-119)` 叠加到既有 `settings-window-after-fix-2026-09-24-1024x768.png`，边框、标签和水平分隔线与运行画面重合；根窗口screen origin候选`(388,260)`、viewport local `(388,242)`，源alpha超出264高根的9行位于screen y=524..532并可见，支持当前Godot `Clip=false`已实际生效。候选位置基于现有截图和当前布局源码的叠图配准，只闭合Zircon侧，不闭合目标EI版本根RECT或裁剪。第一次叠图把原始512画布直接放在猜测根原点，因未计入控件偏移而纵向错119px；检查`ConfigDialog.ApplyLegacyEiLayout()`后按`(-4,-119)`重叠并正确配准。尝试用本机Python Pillow/OpenCV匹配时模块均未安装，改用ImageMagick及GUI图像检查；不因依赖缺失扩大结论。另一次rg使用不存在的`GodotClient/Game/Scenes/GameScene.cs`路径报错，改用实际`GodotClient/Scripts/GameScene.cs`后继续。只做只读截图/帧核验并增加审计证据PNG及文档记录；未启动游戏、未进行输入/测试。

**2026-09-24 真实运行快照补档（非交互）：**工作区里发现`/tmp/zircon-audit-current-root.png`，像素画面为真实 Godot `ZirconClient - 1024x768` 游戏桌面；文件时间为05:12:42，进程表显示 `login_game.sh legacy`/Godot仍存活，但快照与当前时刻相隔且期间状态未知。图中同时可见背包、好友/通信窗和技能书，窗口彼此重叠；中窗“在线/全部好友”等文字与当前 `CommunicationDialog` 内容相符，F350 legacy 根为572×388，故其身份有源码/画面交叉支持，但页态与打开路径未知。背包和技能书靠近视口边界；截图没有独立窗口RECT/crop元数据，不能仅据屏幕边缘断言越界。没有成对EI画面，因此此图只作实际运行渲染快照，不据其单独判原版差异。已复制归档到 `docs/evidence/legacy-ei-ui/zircon-live-ui-current-2026-09-24-0512-1024x768.png`，并将GUI阻塞项从“无截图”修正为“无可交互surface、仅有时点状态未知快照”。`cua.getState()`本轮返回空应用/浏览器列表。ImageMagick新抓屏在`:100`报无法打开X server；`:0`调用报告`missing an image filename`，没有取得新的时间点截图；不将旧`/tmp`快照冒充为本次抓图。未输入/控制游戏。

**2026-09-24 实际运行快照身份更正：**复看归档 `zircon-live-ui-current-2026-09-24-0512-1024x768.png` 后，撤回前记中的“组队窗”身份。中间窗口可见“在线”“全部好友”“当前暂无好友”及底部多页按钮，与 `CommunicationDialog` 源码的好友页、F350背景和572×388 legacy 根相符；因此记录为“好友/通信窗（画面+源码支持，具体页态和打开路径未知）”。截图不能证明组队窗打开。窗口交叠属该时点真实显示，不等于已证EI层级不符；靠屏幕边界的观感也不替代根RECT/crop数据。本次仅更正文档，没有操作应用；`git diff --check`通过。

**2026-09-24 本轮静态审计与提交：**按要求跳过坐骑 `S/Ctrl+S`、不稳定键鼠注入、可能触发 `Bad Request` 的业务输入及需要第二名玩家的交易完整往返；阻塞表逐项保留解除条件，静态核对继续推进。复核 EXIT-01 的 F800/id `0x64` 与 F950/type `0x65` 是独立窗口；F800 的两个控件只确认复用 F151/152 与 F154/155 帧族。鼠标抬起摘要记录 `0x418A00` 命中后发主窗口消息2；按 Win32 消息号，2 是 `WM_DESTROY`，而 `WM_CLOSE` 为 `0x10`。该调用链不能独自证明 YES/NO 的分别命中框、取消动作或进程结束语义，因此在阻塞表单列，不借 F950 RECT 推算 F800 坐标。继续保留 GROUP-01 字典枚举与 EI 链表次序差异、QUEST-06任务项输入路径、SKL-06双目标快捷键绑定等静态发现和证据链接；本轮未改客户端行为。审计引用的 GameInter F161/F162、F700、F900 及组队关闭帧、任务控件对照图作为本轮证据归档。工具查询期间宽范围输出被截断，后改用指定审计段与JSON schema读取；无结论依赖截断结果。`git diff --check`通过；本轮仅提交审计文档和其引用的证据PNG，保留原有未跟踪工具状态文档与其他代码工作区差异。

**2026-09-24 技能书旧实施规格纠偏：**按当前审计权威记录复核 `LEGACY_DIALOG_PARITY_AUDIT.md`，发现其4.3节仍把wrapper中的452×380误作 EI 根尺寸，并将模拟器12格candidate写成实施规格；后三类分类帧也仍复制了重复编号。已把这段旧规格改成明确废止提示，指向 SKL-01..10；保留主初始化记录296×332为根尺寸证据，并逐项写出六个primary-static列表命中矩形、分类帧冲突及列表/MIcon/键盘链仍未闭合的验收门槛。之后全仓检索又找到 `LEGACY_DIALOG_MIGRATION_SUMMARY.md` 的同类旧说法，现已改为当前实现描述并明确未验收；`LegacyUiSkin.cs` 的测试profile注释也改为正确说明452×380仅按本机F400内容适配、EI主初始化记录为296×332，不更改profile尺寸或行为。仅修正文档/注释。一次 `rg` 同时传入一个不存在的 `GodotClient/Controls/LegacyHudLayoutLab.cs` 路径导致 ENOENT；实际文件在 `GodotClient/Scripts/LegacyHudLayoutLab.cs`，随后使用真实路径读取HUD锚点。未启动游戏、未测试、未触碰跳过的坐骑/双人交易/Bad Request输入；最终 `git diff --check`通过，本审计及两份迁移文档的本地链接检查通过。

**2026-09-24 CHAT-02/F350 源帧与运行快照交叉审阅：**重新经 `localhost:8766/api/info?f=GameInter.wil&i=350` 读取头部1024×512、offset(+7,−44)，透明PNG由ImageMagick得到alpha bbox `570×387+226+62`，归档为 `gameinter-frame-350-wil-2026-09-24.png`。当前 `CommunicationDialog.ApplyLegacyEiLayout()` 将F350背景置于根相对(-226,-62)，有效像素落在根相对`[0,570)×[0,387)`；根572×388因此右侧2px、底部1px余量。把帧源按该偏移与预期居中位置叠到已有1024×768快照，中心窗F350框边像素视觉对齐；此为本机资源/布局的runtime-snapshot配准，不证明目标EI的屏幕原点，也不验其输入业务。此前误读了当前证据表中已正确记录的高度387并口头称388；回查发现无需修表，没有写入错误值。`git diff --check`通过。

**2026-09-24 ITEMTIP-01 仓库容器路由补核：**检查 `StorageDialog.ApplyLegacyEiLayout()`、`DXItemGrid`建格、`DXItemCell.OnHoverEnter()`到`GameScene.SetHoverItem()/UpdateMouseItem()`，确认当前仓库4×3 `GridType.Storage`细胞统一进入场景级tooltip，没有容器专属hover来源分派。研究工件 `item-tooltip-and-store-family-evidence.json` 对EI共享id2的`0x44E650`记录 mode1/2 经`0x44E800` hit test、`0x44E7D0`解析后调用`0x4341F0`，图标开关看`item+0x22`类型0xA/0xB；WH-01/02记录state2/F1001及分页candidate，但并未闭合 state2 的人类业务名或其与当前独立StorageDialog对应关系。故只新增“共享提示路径存在/当前通用提示”这条静态覆盖，不把Mode2解释成仓库等价，不推断提示样式相同。只读源码和既有研究工件；未打开、悬停或操作仓库，没有运行测试或修改业务代码。

**2026-09-24 MAP-01 运行快照小地图区域遮挡核查：**针对归档1024×768 root截图裁切右上角后发现画面为GameInter F400背景而非地图；按 `GameScene.LayoutHud()` 右上锚点、`MiniMapDialog`默认200×200、legacy `MagicDialog`尺寸452×380及同一viewport右锚点计算，两者局部根RECT分别约`(824,0,200,200)`与`(572,0,452,380)`，技能书覆盖小地图整个区域。`UiScale`在1024×768基准视口按当前源码为1（约748px客户端高度的计算倍率被下限1约束）。该快照可确认本机此窗口组合下地图不可见，不能从它测地图帧、marker或surface大小；也不能据此判EI叠放或开关行为。一次选择截图裁切区域误包含F400而未得到小地图画面；重新按窗口几何解释该结果，并将它作为MAP-01不可观测边界记入，不把背景误读为地图或地图缺失。未启动/操作客户端，没有做地图输入或运行测试。

**2026-09-24 PRE-02 角色槽数量口径纠正：**对照 `login-flow-evidence.json::screens.parent.char_slots` 与同工件 F51 创建 handler。前者记录phase0/3槽数组`+0xCB8`、phase2暂存数组`+0x10BC`、stride`0x40`、可见记录idx0..1；F51 handler明确“scan 2 slots”。`RESEARCH_LOG.md`摘要的 `0x458B20` setup gate 为`idx<=2`，与两记录字段存在边界歧义，但单一上界检查不证明第三槽已分配、绘制、可选择或可创建。因此撤回审计正文中“研究文件列四角色槽”的错误句，改为最强可支持的两条槽记录并保留idx上界疑点。当前Godot `SelectScene.SetCharacters()`/创建门仍支持4项；可用旧版 `Client/Scenes/SelectScene.cs` 也有4个 `SelectButton`，但这两者不能覆盖研究二进制的2槽记录，也不能替本机目标EI确认容量。另一次rg使用不存在的 `GodotClient/Scenes/SelectScene.cs` 路径报错，随后定位到真实 `GodotClient/Scripts/SelectScene.cs` 和旧版 `Client/Scenes/SelectScene.cs`继续核对。只更正文档；未输入登录/选角业务或运行测试。

**2026-09-24 当前工作区状态校正：**本轮开始时 `git status --short` 只列出未跟踪的 `docs/LOCAL_TOOL_SERVICES_STATUS_2026-09-23.md`；`BeltDialog.cs`、`CharacterDialog.cs`、`LegacyHudLayoutLab.cs` 均与 HEAD 一致，相关最新代码提交为 `7ecb1b02`。因此将审计概览中人物窗和 BELT-01 的“当前未提交”状态改为当前HEAD描述；带日期的旧日志中“当时未提交”措辞保留为历史记录。本轮只修改此审计文档，status 文件未读写/暂存。一次多段 `apply_patch` 因历史段落锚点不吻合而拒绝整组修改，未产生部分写入；随后按确切行分步更新。`git diff --check`通过；本轮不提交、不推送。

**2026-09-24 HUD-10 EI主面板SetRect静态证据补核：**找到研究资料 [`primary-main-hud-setrect.md`](/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/primary-main-hud-setrect.md)，其机器码摘录记录 `[esi+0xC58]` 经 USER32 `SetRect` 写入`(0,601−F50.height,F50.width,600)`；研究帧尺寸800×136，计算根RECT为`(0,465,800,600)`。据此将旧“原版根Y待核”更新为“目标研究工件中的primary-static顶端Y=465”；当前 Godot 实验场Y=465，正式 `GameScene.LayoutHud()` 在逻辑800×600中按600−136得Y=464。SetRect矩形高度135与136帧高不等，F50 blit最终目的RECT、裁切和像素边界语义仍未闭合，且目标EXE身份限制仍适用；未据单个SetRect提前改代码，也不将该静态几何与人物窗抖动建立因果。第一次合并检索命令在工具调用解析阶段因脚本括号语法错误未执行；改成短命令读取来源后完成核对。仅更新审计矩阵/日志；未运行客户端或进行任何输入。本项下一步为追F50绘制调用参数和clip规则。

**2026-09-24 SKL-10 WIL帧头与PNG有效像素复核：**进程参数确认8766预览器以 `--root /home/tetsuya/mir3ei/LegacyEI` 运行；该目录 `Data/GameInter.wil/.wix` 大小为6,561,945/4,436字节，SHA-256分别为 `7d42778e925e82f7c44d2c5f5c46643f3e898a97ed8cbef93df669865ebd87c7` / `c4c3cfd84fcb0dd90f5fccc6ecb48e52172ac21e945106f6fc688e7e8b2c0396`。通过该预览API读取F410–413、F420–431、F440–441、F450–465。头尺寸：F410–413=32×14、F420–431=40×20、F440–441=20×12、F450–455=44×36、F456–465=48×36，offset均(-24,-16)。类别帧的原尺寸透明PNG有效bbox宽高：F450/451为43×35/42×35、F452–455为44×35、F456–461为45×35、F462–464为46×35、F465为45×35；透明区均只在右/下侧。WIL图用于复核帧尺寸与外观，不能解决两个研究工件在同一对象调用偏移上相互冲突的问题。

**2026-09-24 SKL-02/04 WIX非空索引及控件帧复核：**临时 `nix-shell -p python3Packages.pillow` 下调用研究仓库 `Tools/common/wilsdk.py`，由本机 `GameInter.wil/.wix` 独立确认 frame 410–413 为32×14、alpha bbox `(0,0,29,13)`；414–419 的WIX偏移为空；420–431为40×20、alpha bbox覆盖完整帧；440/441为20×12、alpha bbox `(0,0,17,12)`。8766 viewer API头部结果与独立解码器逐帧一致。导出并归档三帧组PNG：F410左向箭头、F412右向箭头、F440交叉剑图形；F420–431在类别contact sheet第一行可见F1–F12字样。按用户要求不以贴图外观猜业务：410/412只由primary-static构造链判为前置控件；F440动作和数字帧的真实控件用途仍待同版点击/分派证据。临时Nix shell未改系统或项目依赖。


**2026-09-24 SKL-03 EI ID→Godot技能记录静态链补核：**按 `skill-window-render-loop-evidence.json` Finding 272 复述并拆分证据层级：EI hit test 从 `this+0x898+24*cl` 分类链表记录取 `[entry+4]+6` 的16位技能ID，写入 `this+0x964`，右页以此ID匹配解码 `Magic.exp` 的 `#ID` 段；当前 `MagicDialog.SelectSchool()` legacy 路径隐藏 `_list`，可见候选格另由 `BuildLegacySkillSlots()` 建立，所选对象是 `(MagicInfo, ClientUserMagic)` tuple，且无EI段落ID绑定。当前列表成员由 `GetVisibleMagicInfos()` 的现代职业/装备戒指过滤产生，之后按 `NeedLevel1`、`Name` 排序并限12项；这些规则不能证明EI类别链表人口、序或EI ID映射。可用旧 `Client/Scenes/Views/MagicDialog.cs`只作 source-confirmed 的旧客户端通用控件参考，不作为EI 3.0证据。当前研究材料完整闭合了右页渲染循环，却明确将类别链表填充和六个命中RECT的写入留在该函数之外；加之目标EXE版本/hash未闭合，逐技能映射仍阻塞。本轮只更新SKL-03矩阵与本审计日志，没有改实现；遵照用户指示跳过坐骑S/Ctrl+S、可能Bad Request的输入及双人交易，未启动客户端或注入输入。一次首次读取审计文档输出截断，之后改为精确读取SKL-03及MagicDialog源码范围；未使用截断内容作结论。


**2026-09-24 HUD-10 F50初始化RECT与paint-helper参数分层（后续闭合）：**原先仅凭HUD paint摘要保留了0x460240末尾800×600参数语义待核。继续查 `hud-label-evidence.json` 和 `confirmation-prompt-evidence.json` 中同一helper调用，可确认调用参数顺序为帧头frame_w/frame_h后接800×600 viewport clip；与F50调用链相合，不能解释成贴图缩放尺寸。结合Frame50 800×136以及alpha bbox 800×135，SetRect top465/bottom600与有效素材边界闭合。Godot正式布局y464因此保留为可静态证明的1px差异，进入后续修改计划；目标资源身份和运行态仍未验。未运行客户端、未做输入、未改布局代码。用户指定跳过的坐骑、双人交易和Bad Request输入未触碰。


**2026-09-24 HUD-10 F50透明边界独立像素复核：**从 `http://localhost:8766/api/image?f=GameInter.wil&i=50` 重新取得当前本机旧版F50 PNG，与既有 `/tmp/ei-gameinter-50.png` SHA-256完全一致（`ff7b2d9973a9e18ac56f0ea58fc7145da54c75d9c43d956d28a60242e7327c93`），并归档为 [`gameinter-frame-50-wil-2026-09-24.png`](evidence/legacy-ei-ui/gameinter-frame-50-wil-2026-09-24.png)。ImageMagick独立alpha裁切给出 `800×135+0+0`；抽查末五行索引131–135，前四行alpha mean 1，索引135 mean/max 0，确认底部最后一行全透明。结合研究F50帧头800×136、初始化RECT top465/bottom600，以及同一 `0x460240` 共用 blitter 证据中resource header frame width/height与800×600 viewport clip参数分列，可作如下静态交叉推导：帧按(0,465)绘制且clip bottom=600时，135条有效alpha行可见、索引135位于clip外/透明；这解释了初始化RECT高度135与帧画布136的1px差。该推导适用于本机WIL且clip口径由同helper调用证据支持；目标资源身份、目标版真实运行像素与实际EI实例调用仍未验证。Godot正式场景根y=464仍与目标研究初始化top465有1px差，应进入后续修改计划与真实同状态验收；当前仅记录，没有改业务源码。操作中Python PIL模块不可用，改用已存在ImageMagick；第一次 `convert` 命令提示IM7已弃用警告但数据成功，随后用`magick`复核，无需安装依赖。

**2026-09-24 MAP-01/03 输入路径证据冲突复核：**对照较早 `map-ui-resource-evidence.json` 与较新的 `hotkey-label-handler-consistency.json`（EI-316, Finding 316, primary-static）及 `RESEARCH_LOG.md` Round 23。F316逐字节闭合 `0x42CBD0` 热键：V(0x56)路径在0x42CDDA读取地图窗口状态`+0x6518`并开/关地图；T(0x54)路径在0x42CE90翻转`+0x64A8`，内联调用0x43D5F0，以256×256/128×128重建地图surface。它也复核0x43DE40无call/xref，`0x43DEB0`是Ctrl+左键拖动重定位，不是mode-switch caller。因此 `map-ui-resource-evidence.json::mode_switch.caller` 和 `key_path_resolution` 中“鼠标owner+0x2A8调用0x43DE40切换mode”不再当作闭合结论；先前审计把T和该鼠标区写为共用函数的句子已改为显式冲突/撤回状态。辅助矩形`+0x2A8`实际点击后果仍未由当前证据闭合。对照Godot `KeyBindManager`、`GameScene.HandleKeyBind()`、HUD cap1 callback与 `MiniMapDialog.OnImageInput()`：V三态还包含透明度切换、HUD cap1仅翻Visible、B开独立BigMap、GM左键发送TeleportRing；这些均为当前源码事实，不据此推断EI服务端0x409效果或运行期同等行为。本轮只校正文档中的研究证据冲突、增加MAP-01/03源码差异记录；未运行客户端或输入，也未改实现。一次检索调用括号语法错误和一次Python行锚点未命中均在写入前失败，无文件副作用；随后使用精确字段替换。坐骑S/Ctrl+S、双人交易和可能Bad Request输入按要求跳过。


**2026-09-24 PRE-14 登录表单键盘路径静态对照：**EI login-flow-evidence.json::screens.phase1.login_fields.key_handlers记录WndProc 0x403FE0：Tab键切换+D38，phase1 Enter切换同字段后调用0x403640登录提交。Godot DXTextInput在 FilterDropDialog.cs 定义并把LineEdit的TextSubmitted转发给事件，但 LoginScene构造的skin邮箱/密码输入没有TextSubmitted订阅；按钮仅连接MouseClick→OnLoginPressed()。记住账号则由DXCheckBox.Checked控制，提交时保存邮箱和密码到ClientSettings。结论只描述当前源码连接与原版primary-static差异；没有运行按键测试，也未推测Godot Tab的默认焦点序列。一次首次文件检索猜错DXTextInput.cs独立文件路径，rg --files没有该文件；随后rg定位到该类型实际定义于GodotClient/Controls/FilterDropDialog.cs，继续完成只读检查。未运行客户端或修改源码；坐骑、交易和Bad Request输入继续跳过。


**2026-09-24 NPC-02 ID9/ID11菜单归属与消息路径拆分：**交叉复读 npc-window-render-evidence.json 的 window_object_model.unified_window_ids、npc-dialog-family-evidence.json F347、npc-dialog-interaction-verification-evidence.json 及 RESEARCH_LOG.md Finding 226/227/229/321。证据确认F1100 NPC模型窗是ID9（+0x51150），其绘制文本为0x43F460，鼠标输入为0x440290；F347进一步记录0x43E4B0编辑/菜单输入将edit buffer按行拆分并通过0x4524A0/0x4524D0发送0x410/0x411，consumed后case9 hide-all关闭ID9。帧窗ID11（+0x516E8）独立绘制F1100之外的列表，paint 0x447470、input 0x447FA0、19行上限；其列表由0x515填充。F321 raw E8 caller scan把0x451A40/0x419唯一发送caller闭合在0x448148任务帧handler，Finding 227还把ID11纳入统一窗口输入分派case11。由此，静态确认两个独立窗口和各自输入链，但未确认ID11帧列表就是同一NPC交互菜单；先前NPC-02中把0x419旧归因撤销的结论继续成立，并补充撤回该ID11-NPC选项等同的隐含假设。Godot当前 NPCDialog 内嵌候选文字调用 SendNPCButton()，不等同已证的EI 0x410/0x411编辑链或ID11的0x419行子动作。只校正审计文档，不改实现；未启动客户端/发送NPC操作，坐骑S/Ctrl+S、双人交易和Bad Request输入继续跳过。

**2026-09-24 SET-07 Ambience 当前源码与 primary-static 回核：**再次逐段对照 `settings-ambience-bgm-volume-evidence.json` 的 `dispatch_table_0x44194C`、`config_save_0x441B30`、`verdict` 与 `ConfigDialog.CreateLegacyOptionButtons()`/`ClientSettings.Save()`。结论仍是：EI Ambience 两个分支换单选帧并调用共享 `Config.ini` 保存，但不写窗口状态 `+0x5C`，保存函数回写载入旧值；Godot 构造时从 `LegacyAmbienceEnabled` 初始化局部视觉态，点击只改 `_legacyAmbienceVisualState`，既不更改全局设置也不调用Save。SET-01/SET-07已有描述与代码相符，无新增等价性证据；未擅自把通用保存副作用提升为可见故障，也未改实现。重查阻塞表确认坐骑 S/Ctrl+S、不稳定键盘注入、可能Bad Request路径、双人交易和当前GUI不可交互均保留了原因与解除条件，本轮逐项跳过。第一次宽范围 `rg` 输出被截断，随后改为定点行/字段读取；无数据或文件副作用。仅审计文档记本轮复核，`git diff --check`通过。

**2026-09-24 NPC-商店父子裁剪顺序静态复核：**复核 `GodotClient/Controls/LegacyUiSkin.cs::ApplyLegacyTestWindow()` 的 legacy 路径（设置 `window.Clip=true` 并调用NPC `ApplyLegacyEiLayout()`）、`GodotClient/Controls/NPCDialog.cs::ShowPage()/ApplyLegacyEiLayout()`完整顺序、`GodotClient/Controls/DXControl.cs::Clip→ClipContents` 以及末尾重新套legacy布局。纠正 SHOP-01 的锚点描述：ShowPage先按现代F381正文逻辑令根高度=`footerY+64`（最小204），再把goods/repair/advanced设为父根子控件并定位到该动态高度；随后legacy布局将父根改回176px，未重定位子面板、未清Clip。因此在legacy测试路径上三面板的top均至少y=204，超过父裁剪底边28px以上，会被完整裁掉。该差异由当前源码控制流确认；目标EI对应子面板层级/显隐仍依赖目标证据，未据此改实现。第一次搜索误用了不存在的 `/home/tetsuya/development/Mir3-Research/docs/RESEARCH_LOG.md` 路径，`rg`报告文件不存在；实际本次需要的NPC工件位于 `docs/research/ei-ui-layout/`，改读该工件和仓库源码后完成核验；错误路径无写入副作用。未做NPC运行点击或业务请求，也未触碰坐骑热键、双人交易或Bad Request路径；`git diff --check`通过。

**2026-09-24 WH-02 分页字段按 state 拆分裁定：**针对旧条目中`+0x7E8`与`+0x71C/12`的摘要冲突，复读`layout.json::specialized_window_evidence[4]`与`store-state-graph.json`。F1003/state1构造`0x44F7E8`将包派生值除以6写入`+0x7E8`，详细证据只称count/batch候选；F1001/state2构造`0x44F9E4`重置页索引`+0x7E0`、设选择`+0x7E4`，state2绘制/分页说明用`+0x71C`计算`ceil(count/12)`。故不是同一状态的两个分页公式；`store_class_fields`对`+0x7E8`的通用“page count”标签过宽，WH-02已更正状态归属。state2控制`+0x3D8`究竟如何读写页索引、是否发消息及页槽映射仍未闭合，目标同版EXE身份限制也未解除。先前一次只读脚本将`states`假设为dict，实际为list而抛`AttributeError`；未写文件，改按list和字段路径读取后完成核对。第一次文档替换因预期片段与实际标点不完全一致触发断言、没有执行该次替换；随后按行内起止锚点重试并核验更新。另一条全局log搜索输出过多无关结果，后改限定至`RESEARCH_LOG.md`相关锚点。未运行存取/分页业务输入，不触碰坐骑、交易或Bad Request测试；文档`git diff --check`通过。

**2026-09-24 WH-01/02 state2网格RECT证据冲突登记：**状态图`store-state-graph.json::states[2].grid_rects`把F1001/state2的12槽`+0x720`记为x=22/60/98/136、y=43/81/119；更详细的`layout.json::constructor_rect_initializers.right_item_grid_rects`及`paint_geometry.item_grid_rect_loop`则指出同一`this+0x720`在`0x44D4C4–0x44D53B`被构造循环设为x=323/361/399/437、同一y，37×37、步长38。该工件明确称其为原始SetRect常量、父级/意义未解，且不适配当前300px背景；两份静态摘要之间没有提供state2专属重设链来解释坐标差。因此WH-01/02已撤回“当前(22,43)局部几何已与primary-static吻合”的肯定语气，降为状态图摘要候选并登记同一数组基址的状态专属写入/RECT消费链待查。没有凭显示外观或当前实现常量选择其中之一。仓库RECT查找中前后两次`rg`正则都因未转义`+0x720`被当作量词而报regex parse error；本轮立即改用Python逐层枚举JSON字段，后续固定字符串检索也已成功，未将失败输出用于判断。一次JSON读取把顶层`state_control_dispatch`误放在`state_machine_evidence`内部，触发`KeyError`；按工件实际顶层键重新定向读取，没有文件副作用。未作仓库页输入或任何业务运行验证；`git diff --check`通过。

**2026-09-24 WH-02 state2网格与页键生命周期边界补充：**从`layout.json`提取F1014/15与F1016/17的构造命中RECT分别为`arg4+0x144,arg5+0x09F`及`arg4+0x1B2,arg5+0x09F`（即初始偏移+324/+159、+434/+159）；状态图将同一state2候选子控件记录在根相对约(x+28,y+162)、(x+137,y+162)，而当前矩阵旧写的后键x=133已纠正为状态图原值137。网格亦呈同类差异：通用构造循环`this+0x720`坐标323..437与state2状态图坐标22..136。最稳妥解释是构造态与state2有效态可能有重定位/替换，但当前材料没有对应writer/生命周期指令链，不能直接断言两组数据互相冲突或已由state2覆盖。WH-01/02现将它们分开列为“初始控件RECT”和“状态图state2候选”，共同阻塞解除条件是找到同一对象从构造、收到0x2C0后至paint/hit-test之间的逐字段写入与最终RECT读取链。此次未执行页键、存取或交易输入；`git diff --check`通过。

**2026-09-24 WH-01 F1001原素材归档：**经用户指定`localhost:8766`读取`GameInter.wil`帧1001：资源头256×256、offset(+7,-44)、words=40878；PNG独立ImageMagick alpha bbox=198×204+(28,26)，SHA-256=`e7391f4a06cc23dea2708a95d2a4ca827316a5ea6d8e81946c5799726cb8dd6a`，归档到[gameinter-frame-1001-wil-2026-09-24.png](evidence/legacy-ei-ui/gameinter-frame-1001-wil-2026-09-24.png)。图像可视辨认出4×3格、左右箭头和底部动作美术；不以此代替控件RECT或输入语义。尝试另查`localhost:8765`时连接失败（该服务未监听）；正确的8766素材预览器请求已成功，错误端口请求只读、无副作用。未运行仓库交互；`git diff --check`通过。

**2026-09-24 HUD-07 当前legacy运行快照：**复核权威进程列表确认`bash login_game.sh legacy`与对应 Godot(`--legacy-ui --legacy-hud`)实例仍存活；CUA返回`apps=[]/browsers=[]`，但进程环境含`WAYLAND_DISPLAY=wayland-0`。不做键鼠输入，改由`grim`取得实时画面并只裁取游戏客户端1024×768区域，归档为[`zircon-live-hud-legacy-2026-09-24-1839-1024x768.png`](evidence/legacy-ei-ui/zircon-live-hud-legacy-2026-09-24-1839-1024x768.png)，SHA-256=`233b6865c91cdc5fb0789c4625aa8b342b62b99198eb3a346a97211f19f2cde4`。画面中F50主底栏位于底部，顶端两条槽位带及右上小地图和地图标记可见，角色/地图运行；截图时未开NPC、交易、任务等对话窗。`GameScene.CreateHud()`静态调用明确令MiniMap、BeltDialog、MagicBar可见；截图只记录实际可见态，不把未交互的槽位或小地图功能标为行为通过，也无EI同状态截图可据以判定parity。工具错误及恢复：`xdpyinfo`本机不可用（命令不存在）；ImageMagick `import -display :0 -window root`返回missing image filename；随后改用Wayland `grim`成功。全程未重启客户端、未输入、未执行坐骑S/Ctrl+S、双人交易或可能触发Bad Request的路径；`git diff --check`通过。

**2026-09-24 HUD-10 视口分辨率与F50锚点分层复核：**检查`GodotClient/Scripts/GameScene.cs::RefreshUiScale()/LayoutHud()`、`MainPanel.cs`及viewer F50 PNG头/alpha边界。新一帧实屏仍为1024×768；现有缩放公式在此分辨率给`UiScale=min(768/768,1024/1024)=1`。F50自然帧800×136、默认非拉伸，当前源码bottom-center公式对应逻辑坐标(112,632)，新截图可见底栏居中贴底，仅作该运行viewport的一致性旁证。此分辨率不能关闭800×600基准差：若在800×600按同一公式，UiScale被下限钳制为1、逻辑视口800×600，MainPanel y=600−136=464；原版primary-static SetRect y=601−136=465，仍相差1px，与HUD-10主矩阵结论一致，尚未通过目标EI画面验收。尝试以alpha掩模做F50—屏幕像素模板定位未产出可靠度量，结果未用于定位；尝试`hyprctl clients -j`时shell缺`HYPRLAND_INSTANCE_SIGNATURE`，确认没有Hyprland会话供本轮改窗口尺寸，未做分辨率调整。未启动/结束客户端或发送输入；未重复坐骑、交易或Bad Request路径。`git diff --check`通过。


**2026-09-24 CHAT-01 关闭控件偏移修正：**以 primary-static `chat-window-render-evidence.json::controls[close-button]` 的 `(532,350,28,26)` 独立核对 `CommunicationDialog.ApplyLegacyEiLayout()`，确认先前 `(536,354)` 根相对位置在x/y均多4px；将控件位置和 `AuditLegacyEiLayout()` 对应断言改为 `(532,350)`。该自审计只检查源码状态，不代表目标EI像素叠图或实际点击已通过，CHAT-01保留运行截图/命中验收。查询 `CommunicationDialog.cs` 时首条只读 shell 命令在多段输出后误留尾随 `&&`，shell 报语法错误；没有触发后续命令或文件写入，随后去掉尾随符继续读取。本轮未启动游戏、未发送聊天/关闭输入，不触碰坐骑S/Ctrl+S、双人交易或可能触发 Bad Request 的路径。未构建/运行测试；仅变更关闭控件静态位置、自审计断言与本审计文档。


**2026-09-24 QUEST-01 F700有效像素与关闭控件位置交叉核验：**从`localhost:8766/api/info?f=GameInter.wil&i=700`取得帧头512×512、offset(+7,-44)，再从`/api/image`原始PNG用ImageMagick`-trim`独立量得alpha bbox `340×439+(86,36)`（源PNG SHA-256=`11d1a2889649af3e407de51eaf5515eac44ad5647caa9b350fa98b2561f8354d`），已归档为[GameInter F700 PNG](evidence/legacy-ei-ui/gameinter-frame-700-wil-2026-09-24.png)。当前 `ApplyLegacyEiLayout()` 将背景绘于根相对`(-86,-36)`，故alpha有效区恰为根相对`[0,340)×[0,439)`；截图目视可确认右上原生箭头/X图案在书页内，primary-static F723/724与F721/722 RECT分别为`(290,59,28,28)`、`(290,89,28,28)`。当前叠加关闭控件仍在`(304,404)`、资源F161/162、控件框28×26；原版X子控件是F721/722、`(290,89,28,28)`，故位置偏14px、315px且控件框高少2px。独立图像比较显示F161/721、F162/722只是相近X图标而非逐像素相同（28×28 AE差异像素分别65/784、113/784）；对照见[任务控件帧比较图](evidence/legacy-ei-ui/gameinter-quest-controls-comparison-2026-09-24.png)。因此记为资源近似但非精确映射、RECT不符；F721点击是否应隐藏窗口未定，不能只按“close”角色给回调。故本轮只补足资源像素/锚点证据，未改任务窗实现；后续须先核绘制态是否需要叠F721/722，再按点击链与通用窗口分派恢复正确视觉/命中行为。一次alpha bbox格式误用ImageMagick的`%+X/%+Y`导致属性警告且坐标缺失；立即改用`%X/%Y`独立复算成功，没有依赖错误值。未运行任务窗口或输入；不触碰坐骑S/Ctrl+S、双人交易或可能触发Bad Request的路径。


**2026-09-24 QUEST-02 当前已完成页可达性校正：**逐一检查`QuestDialog.cs`全部`_page`读写与调用点。`_page`初始值为0；唯一写点是`AddTab()`回调中把捕获页码写入，构造器只建页码0、1、3三个tab；全类和`GameScene`调用者未发现其它页码setter。因此`RefreshPage()`里`_page==2`对已完成任务的筛选分支不可达，当前运行入口没有已完成页签。更正QUEST-02中过度概括“当前提供已完成页”的表述：将其记为死分支，实际可达页为进行中、可接、里程碑。此项只校正当前实现状态；不推断 EI 需要单独的已完成tab，原版页面身份仍以primary-static任务列表/控件链为依据。之前读取研究 JSON 时使用了不存在的顶层`window/controls`字段，查询无结论；随后改读其实际`cell_analysis/exe_trace`并以`RESEARCH_LOG.md`F942交叉核对，未将空查询结果当作证据。


**2026-09-24 QUEST-01 列表Y公式证据等级复核：**重新并读`RESEARCH_LOG.md` Finding 89、Finding 251 与 quest F537/F671 摘要。F89给完整基线`win.y+0x5A+row*15`；F251基于`0x45DD70` TextOut helper调用实参，展开为`win.y+90+15*(row-scroll)+out[1]`，还给`x=win.x+0x41+out[0]`。两者相互支持90px起点/15px步长；F537/F671同地址摘要却分别简写为`idx*15+18`、`line*3+0x12`，单看它们既缺scroll状态与helper y偏移，也不能与F251公式在相同窗口根坐标下直接相等。当前研究目标EXE（524288 bytes）不可读，故保留为研究资料摘要冲突；F89/F251可作较强重建候选，但没有提升为独立复放或EI运行确认。Zircon当前行标题/子任务行距分别约22/18px、列表从根相对y=63生成，且未实现原版19行clip/200px文本门（QUEST-01），不能拿当前画面决定原版公式。本轮只读工件与当前源码，不改业务/布局。此前一次Python替换使用的精确文本片段与实际审计行标点不符，在assert阶段终止且未写文件；按终端原文更新片段后完成，未使用失败替换的结果。


**2026-09-24 QUEST-01 子控件帧视觉映射交叉核验：**从`localhost:8766`直接取得GameInter F721–F724与F161–F162 PNG。四个原版帧头均为28×28、offset(-24,-16)，alpha bbox均27×27+(0,0)；查看原尺寸像素接触表确认F721绿色X/F722金色X、F723绿色右箭头/F724金色右箭头。将F161/721及F162/722作同尺寸 ImageMagick `compare -metric AE`，结果分别65与113个不同像素（共784像素）；两组视觉意象相似但并非完全复用，差异包括按钮外环/边缘颜色与有效像素。归档对照图[gameinter-quest-controls-comparison-2026-09-24.png](evidence/legacy-ei-ui/gameinter-quest-controls-comparison-2026-09-24.png)。EI primary-static `RESEARCH_LOG.md` F251/F942给F721/722关闭候选hit RECT `(290,89,28,28)`，箭头F723/724 `(290,59,28,28)`；当前 `QuestDialog.ApplyLegacyEiLayout()` 仍叠加F161/162于`(304,404,28,26)`，超出原X目标原点(+14,+315)，高度少2px。仅从静态证据可确认几何/帧差，不可确认原版X是否改变窗口可见性；本轮未改实现。两次用空格拆解pair变量的shell查询将`161 721`作为单一参数而生成不存在的临时文件路径，查询失败；改成四条显式比较后成功。第一次ImageMagick `montage`未指定可用font时报字体错误；指定`DejaVu-Sans`重新生成对照图。未运行任务窗或输入。


**2026-09-24 QUEST-06 列表行点击与数据动作静态对照：**沿Godot `QuestDialog.AddLine()/RefreshPage()`复核任务项事件：行标题构造为`DXLabel`且`IsControl=false`，随后进行中与可接任务标题显式设置`MouseFilter.Stop`并连接`GuiInput`，故不能依据默认`IsControl`推断标签不可点。进行中未完成项左键发送`QuestTrack(true)`、右键开启Zircon `ConfirmDialog`放弃；完成项左键发送`QuestComplete`或开启奖励选择；可接任务左键只选中，接受按钮另在详情面板。与 EI F942 `0x448490` 对 record+0x218 的动态矩形命中、选择写`+0x1DC`后调用`0x448580`及其0x418/0x419分支不同；原版控件矩形初始化/最终状态旗标尚不完整，当前根相对任务标题x=36、EI文本起点候选x=65也不能直接当作双方 hit RECT 比较。进一步逐读`query`筛选与点击闭包：page0只枚举`!q.IsComplete`，page2才枚举完成项，而page2无写入口；因此行标题回调中的`complete==true`分支（奖励/直接完成）同样不可达。此结论仅限这类普通任务列表页；里程碑页有独立的`milestone.IsComplete`和领奖回调，不能混为一谈。仅静态源码/反汇编工件对照，未运行或发送Quest请求。


**QUEST-06 可接任务确认路径补充：**复核`RefreshDetail()`内`SendQuestAccept()`唯一调用点在右侧详情panel的action按钮；可接任务标题点击只设置`_selectedAvailable`并刷新详情。该panel的全局根x为`_content(18)+_detailPanel(380)=398`，接受按钮再位于panel局部x=194，远超legacy根宽340且受`ClipContents`裁切，因此当前profile中即使标题命中也不能完成接受任务流程。先前一次文档替换漏用了实际句子中的“标题”字样，触发assert并在写文件前退出；改用与原文一致的锚点后完成更新。只静态核对当前控件位置/调用点，无任务接受输入或服务器请求。


**2026-09-24 SKL-02 F420–F431帧资源归属与当前消费点复核：**再次查看`MagicDialog.cs`所有帧选择/图像路径和EI constructor evidence，确认原版技能窗11次通用控件构造只建立三组其它帧控件及八组类别帧；没有F420–F431构造/绘制调用记录。当前legacy格背景暂设F410+i并由技能图标刷新，源码没有把F420–F431作为资源帧消费；对该目录内`420`命中逐项检查只见420px页签布局算式，而非asset index。独立WIL PNG只支持F420–431画布为40×20且外观类似F1–F12编号；用途/所属控件仍属visual-candidate，不能进一步命名为技能绑定键标签，也不能因当前实现没用就推断EI不使用。SKL-02据此维持“数字帧不属于已证实技能图标序列”。本轮为定点源码及证据复核，无键盘、游戏交互、构建或测试。


**2026-09-24 GROUP-04 F900背景与关闭框像素边界复核：**由`localhost:8766/api/info`取得F900=`256×256,offset(+7,-44)`、F161/F162=`28×26,offset(-24,-16)`；独立导出PNG后用ImageMagick alpha-only `-trim`测得F900有效bbox `256×244+(0,6)`、F161/F162均`25×25+(0,0)`。对照当前 `GroupDialog.ApplyLegacyEiLayout()` 根256×244、F900位置`(0,-6)`，alpha区间精确映射到根`[0,256)×[0,244)`，背景锚点没有额外裁切余量。primary-static关闭hit RECT `(226,214,28,26)` 与当前显式RECT `(224,212,28,26)`仍相差2px/轴；资源alpha透明边在右/下共1px且原点透明边为0，不能解释左上偏移。已归档原帧F900与关闭帧对照。没有点击权限/邀请/离队/关闭，不发组队包、不做双人验证；只更新审计文档和证据PNG，`git diff --check`通过。


**2026-09-24 GROUP-01 成员序列来源边界补充：**静态检查`GameScene.OnGroupMember/OnGroupRemove()`→`GroupDialog.AddMember/RemoveMember()`→`RebuildMembers()`：当前按服务器事件把`objectId→name`写入/删除`Dictionary<uint,string>`，每次都直接枚举字典生成索引`i`并套EI单双列坐标，源码没有显式排序或EI原始链表节点序号。EI F536绘制遍历原生linked list并用节点index决定左右列与行距。故已能确认坐标公式接近、数据容器/顺序来源不同；没有证据证明这一定导致错序，也未静态闭合不同到达顺序、移除后再加入与EI链表的对应。GROUP-01现把序列保留为候选并要求按0、1、2、3及重加入顺序做可观测比较；这些仍是组队运行期项目，本轮不发邀请/离队/移除消息。一次只读shell命令误带结尾`&&`导致语法错误，后续命令未执行；清理后重新读取`GroupDialog`与`GameScene.OnGroupMember`相关行成功，无写入副作用。

**2026-09-24 NPC-01/05 滚动语义与开窗可达性续审：**重读`npc-window-render-evidence.json::model_input`、`dynamic_text_layout_state`、`dialogue_text_layout_contract`和`dialogue_open.liveness`，并对照当前`NPCDialog`/`DXVScrollBar`源码。EI箭头只有overflow flag `[+0x58C]==1`时响应；上/下各将菜单逻辑索引`[+0x3BC]`减一/加一、调用`0x440C30`清除每条节点的五个子RECT并重建命中区，文字paint按索引窗口遍历节点；通用thumb走`0x417E60(this+0x3C4)`。当前Godot滚动条缺失有效帧/矩形，Change=1后把Value直接作为文本像素offset，不能直接接原版菜单索引动作。进一步发现研究JSON自身的`+0x594`语义不一致：字段名为line-spacing且被写为14/21，但paint又以`[+0x3BC, +0x3BC + +0x594]`作为节点链遍历界限，字形pitch另记`textheight+5`；本轮不把14/21叫作已证像素行距，NPC-05要求回原始指令厘清字段单位。另`dialogue_open`记msg table index16/VA `0x41FE31`，但唯一表读取器将索引限于0..8，工件称该handler无表外静态引用；这只表示该消息入口不可达，不能推出已初始化ID9的paint/input方法都不可用。新增NPC-05与阻塞记录，要求找到活开窗路径后才把当前`NPCResponse→ShowPage`映射到EI；不据此盲接滚动或修改窗口。一次跨越完整`RESEARCH_LOG.md`的宽`rg`输出被截断，随后以Python按JSON schema抽取具体字段并针对性复核，未使用截断文本作推论。只更新审计文档，未启动游戏、未发送业务输入、未构建/测试；跳过坐骑S/Ctrl+S、双人交易和可能触发Bad Request的运行路径。


**2026-09-24 NPC-05 私有消息与服务器载荷来源复核：**按NPC-05查读`npc-dialog-family-evidence.json`、`npc-dialog-interaction-verification-evidence.json`、`npc-window-render-evidence.json`、`input-message-bus-0x7ed-0x7f0.json`及RESEARCH_LOG Finding 228/229/249。发现`npc-dialog-family-evidence.json::notes`把NPC脚本正文来源写成“服务器消息0x7ED–0x7F0 family, F249”，而独立bus工件定义的是发往`[0x8AB7B0]`主HWND的私有消息；其sender matrix将0x7ED唯一发送路径列为聊天编辑框的两处发送，byte3恒为0。RESEARCH_LOG Finding 249实际分析的是通知窗id15的0x43E4B0鼠标坐标输入和0x7ED私有消息模式分支，并未闭合NPC服务端正文来源。故修订NPC-05，撤回“0x7ED–0x7F0是NPC服务器数据”的归因；当前已列研究证据反而记录独立dialogue-open `msg 0x274/index16` 路径不可达，真实入站正文/活开窗链仍待原始机器码追踪。本轮还复核`+0x594`的字段消费矛盾，不把14/21当已证的文本行高。工具查询只读取本机研究JSON与日志；最初尝试宽泛rg输出被截断，之后改为字段级Python提取和定点RESEARCH_LOG行读取，未从截断内容得出结论。未改 Mir3-Research 外部工件、未启动游戏、未发NPC/坐骑/交易/Bad Request风险输入、未构建或测试；只更新本仓库审计文档，`git diff --check`随后检查。

**2026-09-24 NPC-05 消息方向图与原始材料可用性核验：**检查`/tmp/mir3_full.asm`、研究NAS EXE两条路径及`/home/tetsuya/mir3ei/Mir3.exe`：目标同版原件和`.asm`当前均不可读；唯一可读EXE为581632-byte本机副本，已知与研究工件524288-byte目标不同，故未拿本机地址硬套。随后在`ei-ui-layout`定点读取`npc-window-render-evidence.json`、`input-message-bus-0x7ed-0x7f0.json`及RESEARCH_LOG Finding 228/229/230/249，并核交叉索引：msg `0x3F2`/`0x3F3`/`0x410`/`0x411`证据均为客户端出站；server msg `0x515`填充独立frame id11列表；内部`0x7ED–0x7F0`只属主HWND私有消息；疑似NPC `msg 0x274` id9打开handler又受外层表边界限制。故在NPC-05添加方向矩阵式文字，禁止将出站包、私有消息或同名“选项”frame列表误作NPC正文入站链。RESEARCH_LOG/UI_COVERAGE_MATRIX有更新后的`0x41FE31`不可达裁决，也仍含旧Finding 228活开窗历史描述；最终范围以Finding 229 liveness更正为准。`0x594`单位和另一条活NPC开窗路径继续待同版机器码。`rg`横跨多个长JSON/Markdown的初次输出被截断，改成限定文件/字段和分段行号读取后完成；原件路径检查仅报不可读并转用现存证据，不依据不可读文件推断新语义。未改外部研究仓库或业务实现，未运行客户端/测试/构建，未发送NPC/坐骑/交易输入；本次只更新本审计文档，`git diff --check`及链接检查随后执行。


**2026-09-24 PRE-01/11 `wemade.dat` intro证据边界复核：**此前审计把“wemade.dat播放阶段待闭合”与“登录/选角背景映射未闭合”合并描述，容易造成前者也仍未知的印象。重新读取 Mir3-Research `intro-splash-state-machine-evidence.json` 与 `login-charselect-flow-evidence.json`：intro sub-state 1调用0x45BE20播放已加载于对象+0x6F4的wemade.dat，primary-static verdict明确其为studio-logo splash；另有Interface1c F0x3C blit及后续聊天/音频阶段。对本机 `/home/tetsuya/mir3ei/LegacyEI/Data/wemade.dat` 用`file`、`ffprobe`及原始RIFF头独立核验：AVI、Indeo5、640×360、149帧、29.97fps、4.971638秒，大小5,076,192 bytes；既有逐秒采样PNG显示Wemade标志。本次因此收窄PRE-01/11：视频容器与intro播放阶段已静态闭合；登录表单/选角画面是否使用该片段、阶段转换、同屏合成、按钮/背景映射仍缺目标EI同态画面，不能把studio splash直接称为登录表单背景。当前Godot请求的四组Interface1c高帧越界结论仍成立。此项作为可静态核对项已完成，后续跳过依赖缺失目标EXE/截图的结论等待并继续其它审计。工具错误记录：环境没有`xxd`（command not found），改用`od`读取RIFF头；首次把intro证据文件路径猜在`research/ei-ui-layout/`导致ENOENT，后用`rg --files`定位到`research/mir3-map-reconstruction/intro-splash-state-machine-evidence.json`并完成读取；这些错误未产生副作用。未启动客户端、未做坐骑/交易/Bad Request运行测试。`git diff --check`待提交前执行。


**2026-09-24 PRE-15 选角刷新默认选择静态源码对照：**按EI角色列表刷新链与当前 `SelectScene.RefreshList()` 逐句比对。EI研究主静态证据 `login-charselect-flow-evidence.json::server_dispatch_0x458F80` 记录消息0x208清理角色区、把当前角色索引`[+0x1168]`置为-1；原版F55 handler仅当索引0或1时发送进入角色请求。当前 Godot 非空列表分支固定调用 `SelectSkinCharacter(0)`，会选中首条、启用进入/删除并刷新角色动画。差异本身在实现状态上成立；由于目标EI EXE身份和刷新后的同态按钮/焦点画面未闭合，不从`-1`单字段断言原版确切禁用美术，也不在本轮直接改客户端自动选择行为。PRE-15加入阻塞表，后续取得匹配EI状态证据后再决定 legacy 入口调整；验证范围限定0/1/2角色及鼠标选择门控，不触发创建/删除/进入业务请求。只读比对研究JSON和源码；没有运行登录流程、没有发送账号/角色消息、没有触碰坐骑S/Ctrl+S、双人交易或Bad Request输入。一次宽范围读取把长RESEARCH_LOG和源码输出合并后截断，随即按JSON精确key及`nl`指定行号重新读取；结论仅基于精确读取部分。


**2026-09-24 PRE-16 选角阶段控件与创建界面静态矩阵：**把 `login-flow-evidence.json::screens.parent.phase` 与 `SelectScene.BuildLegacySelectUi()/ShowCreateCharacterPanel()/SubmitSkinCharacter()/OnNewCharacterResult()` 对照。原版状态序列是 phase0角色列表四按钮；F51仅在两槽尚有空位时进phase1并装载`CreateChr.dat`，完成后phase2呈现角色动画列表、F92/F95/F98/F86/F89五控件和密码EDIT；F89转phase3并发msg0x64，服务端0x209也有phase3入口；成功后phase4装载`StartGame.dat`。当前Godot没有这些phase字段/数据文件播放器，在同一SelectScene显示自绘角色卡片和260×650表单；表单字段为名称、职业/性别、发型/颜色、角色预览，没有原版parent对象中记录的密码EDIT；确认直接走`SendNewCharacter`，结果回调刷新当前列表。由静态源码可确认旧对象分阶段、当前客户端采用单阶段创建表单及不同协议数据；F92/F95/F98/F86/F89的真实语义仍candidate，密码框文本含义pending，目标EI构建身份亦有差异。新增PRE-16阻塞项和显式下一步矩阵要求，避免把candidate标签按现有按钮名臆配。只读代码与研究JSON，未运行客户端、未调用创建/进入/删除业务，不进行坐骑S/Ctrl+S、交易或Bad Request测试。一次读取大段日志输出被截断后，改为只提取JSON的parent.phase和小段Godot方法范围；记录仅使用精确字段。


**2026-09-24 PRE-17 创建角色成功回调的可见状态静态检查：**追读 `ShowCreateCharacterPanel()`、`HideCreateCharacterPanel()`、`SubmitSkinCharacter()`、`ShowNewCharacterResult()` 与 `RefreshList()`的完整调用关系。打开创建窗时列表隐藏、表单显示；成功回包路径只追加角色、调用RefreshList和更新状态标签，未调用HideCreateCharacterPanel，也没有写回两个面板的Visible属性；而RefreshList仅重建卡片/状态并选择第一条，不负责容器显隐。故仅按源码控制流，可判当前自绘创建表单成功后仍停在表单、列表仍隐藏，存在内部成功回跳遗漏。此结论尚非实际运行复现，也不决定EI原版最终应回列表还是留在确认阶段；在同版原版确认后再改并验证失败/成功两路，避免把另一个协议结果错误套成EI。新增PRE-17，要求独立处理当前行为缺陷并记录运行验收。未发送角色创建请求、未运行登录客户端、未触发坐骑/交易/Bad Request输入；只做源码调用图静态检查。一次尝试用多行Markdown row 构造精确文本补丁时 Python 报 SyntaxError，脚本未写入任何文件；随后改用逐行插入成功。`git diff --check`通过。


**2026-09-24 PRE-16 创建/进入阶段视频资源抽帧：**核查 `/home/tetsuya/mir3ei/LegacyEI/Data/CreateChr.dat` 与 `StartGame.dat` 实际文件（均存在），用`file`/`ffprobe`确认容器和Indeo5解码参数，再以`ffmpeg`按2fps导出联系图。CreateChr为640×480、29.97fps、39帧、1.301301秒、1,221,572 bytes；StartGame为640×480、29.97fps、41帧、1.368035秒、1,060,892 bytes。采样图显示两者内容为暗色地下通道场景镜头；不能据内容将其解释为F50或表单静态背景，具体对象装载/播放阶段仍依 `login-flow-evidence.json` 的primary-static调用链。归档联系图及PNG哈希：CreateChr `0a08769c53052e9b52d7ae2bd43ae54bf2a7c41c29e650c97707165260eefe8c`；StartGame `61dd305e86da87055ebefb58227857e20769473d80b5f9e50e7da1910bb33272`。`wemade.dat`与intro关系见PRE-01/11。提取过程中ffmpeg给出了单PNG路径缺少序列pattern的提示，但在`-frames:v 1`条件下仍正确生成单帧联系图；随后`file`及肉眼查看确认尺寸/内容，本提示没有造成失败或数据截断。没有启动客户端或触发创建、进入请求；只对已有媒体资源抽帧并登记审计证据。


**2026-09-24 PRE-18 选角键盘行为证据分层：**对照旧版源码 `Client/Scenes/SelectScene.cs::SelectDialog.OnKeyPress()` 与EI `login-flow-evidence.json::screens.parent.wndproc`、Godot `SelectScene.cs`。可用旧Client明确Enter在开始按钮enabled时进入、Up/Down改变SelectedButton；它仅是较新旧版Client源码证据。EI研究只确认parent WndProc入口和WM_KEYDOWN目标 `0x459690`，没有将各按键绑定到具体动作；EI F92/F95/F98/F86阶段控件还保留candidate。Godot SelectScene无显式场景键盘分派 override，角色卡通过DXButton鼠标回调选择；另有隐藏的VBox/ItemList/普通Button，内建键盘导航受焦点与DX控件输入框架影响，尚不能从单一文件宣布完整键盘行为不存在。故新增PRE-18，先追EI目标字节及Godot基类焦点路由，再用不发送业务消息的输入观察核证。工具错误记录：此前在research全目录对过宽地址词执行`rg`导致RESEARCH_LOG大量无关命中、输出截断；此项键盘结论只依精确JSON字段与旧Client/Godot指定方法，不引用截断结果。另对`ei-ui-layout/RESEARCH_LOG.md`精确搜索0x459690及phase-2相关目标地址未找到展开的函数正文；现有login-flow JSON仅给WndProc入口和`readers`索引，故键值/动作仍保持pending/candidate。未启动游戏、未注入键盘或网络输入；坐骑/交易/Bad Request测试继续跳过。


**2026-09-24 登录/选角总导航矩阵与逐项状态一致性：**复核总览表mode0/mode2与PRE-01..18后，发现mode0仍只引用早期PRE-01/05/07/09，mode2仍只引用PRE-02..06/PRE-10；且mode2总表把“删除”写成既定按钮行为，与F53/阶段2 F86语义仍candidate冲突。现按详细阻塞表同步编号和证据等级：mode0明确F17 candidate并加入PRE-11/13/14；mode2区分primary-static的两槽/F51/F55/F57与candidate F53/F92/F95/F98/F86，并加入phase媒体/键盘对应PRE-15..18。同步说明`CreateChr.dat`/`StartGame.dat`已确认文件和抽帧，菜单状态仍与EI同版画面未闭合。仅修正文档总导航，未改代码或运行输入；`git diff --check`通过。


**2026-09-24 INV-03 背包跨库子资源shadow字段边界：**重新用用户指定的8766本地素材预览API读取GameInter F250/F280/F264/F265及Interface1c F267/F268。返回头与既有记录一致：F250 512×512 offset(+7,-44)；F280 16×424 offset(-24,-16)；F264/265 64×20；Interface1c F267 76×88、offset(+18,+64)、shadow flag=1、shadow offset(-7,-38)，F268 60×106、offset(+24,+46)、shadow flag=1、shadow offset(-1,-47)。对照当前 `GodotClient/Formats/LegacyWilLibrary.cs`：`TryGetHeader()`仅保存宽高及主offset；`GetImageTexture()`只将主RLE转成RGBA，其RLE comment明确UI图不使用mask plane。原版研究将F267/268归为跨库角色外观候选，但尚未闭合0x417550共享子控件/绘制路径是否读取header shadow offsets或消费mask plane；因此只登记资源格式与当前loader能力差异，不据header标记断言EI背包画面有一层未绘阴影。下一步要追目标EI中Interface1c子控件的paint call、所选ZL/WIL读图函数和shadow消费门，再判断需不需要扩展WIL fallback。8766本轮API可用，直接读到六帧头；旧日志记录服务离线属于当时状态，无需改写历史。工具错误记录：首次`rg`传入不存在的`Shared`目录并报ENOENT；随后跨多个渲染器源码检索命中大量无关Shadow项、输出截断，改用精确`LegacyWilLibrary.cs`行区间及已指定API帧头，推论只来自这些定点证据。没有打开背包、启动游戏或操作任何业务输入；`git diff --check`通过。


**2026-09-24 Godot补充窗口入口覆盖核验：**按`GameScene.cs`逐一核对未在EI总表单列的控件实例与可见入口，补充清册，区分常驻Buff/任务追踪/Timer叠层、背包货币/包裹状态、NPC任务与镶嵌子窗、钓鱼/钓获状态、行会成员/里程碑、怪物悬停，以及受false feature gate关闭的伙伴/商城/寄售/地图查找等。修正初稿里Bundle、Monster、Milestone、FishingCatch“入口未见”的过宽描述：源码分别存在服务器包裹回调、怪物悬停刷新、里程碑消息和钓获状态回包。固定16槽目录不能排除EI动态对象，所有EI归属仍保持候选/待映射。新增对应阻塞清单，明确下一步逐项比对静态绘制/命中/资源/几何，而不是以运行风险为由停住。一次宽范围源码拼读输出被截断；改用精确`rg`引用点确认回调后更新。本轮未运行客户端、未触发坐骑S/Ctrl+S、双人交易或可能Bad Request的输入；`git diff --check`通过。


**2026-09-24 NPC-06/07 动态子窗图库路由与帧号边界：**对照 `LibraryCore/Libraries.cs`、`MirSkin.GetLibrary()/GetLegacyWilLibrary()`、Godot NPC子窗构造器、可用旧Client同名类、本机LegacyEI资源目录与8766素材API。EI资源目录确无Interface.Zl/Interface.wil；`LibraryFile.Interface` fallback按basename只寻找Interface.wil，而目录只有Interface1c.wil/.wix，因此NPC任务列表/详情当前背景库路径为空；两窗由background.Size设根尺寸，代码子控件却仍放在大于零的位置，NPC打开方法又按根尺寸相邻定位。Interface1c帧209是36×104人物形态图，帧212缺失，虽与旧Client中Interface库的数字相同，不能作为替代或EI语义证据。另用 Mir3-Research `Tools/common/wilsdk.py`独立读取EI GameInter.wil count=1103，5700/5701/5740–5760全部无header；`NPC.wil`则count=6400且F5700/F5701为192×128，研究`npc-body-strip-evidence.json`将其归为NPC body 0x39的12帧动画系列。由此纠正8766 API的blank=true：这些GameInter索引是越界，不应只叫空白帧。检索中首次误用API参数`file/frame`得到HTTP 404；按既有服务协议改成`f/i`继续，`Interface.wil`返回library not found，GameInter查询返回blank。首次调用`wilsdk.WilLibrary.count()`将整数属性当函数，报TypeError；改读`count`属性后成功完成计数和header核验。工具错误均已限定在只读查询并记录，未据错误结果下结论。当前仅新增静态审计与阻塞项；未改窗体/帧号，不运行NPC任务请求或socket业务，不启动客户端，也未触发坐骑S/Ctrl+S、交易或Bad Request测试。`git diff --check`通过。


**2026-09-24 Legacy EI图库优先级与高帧引用复核：**在NPC-07初稿中只以GameInter.wil count裁定时，重新核对当前实际加载顺序发现`GameInter.Zl`存在且`MirSkin.GetLibrary()`优先读取ZL。独立`zlsdk.py`确认此ZL2 count=1103；`wilsdk.py`确认同库WIL count=1103；因此修正依据后，F5700/F5701/F5740–5760仍为实际运行优先库内越界帧，而非因优先级导致结论反转。继而对全仓显式`LibraryFile.GameInter`索引做只读扫描，发现配置、包裹、钓鱼、帮助、怪物、驯马、NPC高级子窗、计时器、婚姻标记等仍有多个>1102请求；逐项开关/显隐未全部闭合，记录为资源完整性清册，不直接宣称各帧常驻显示。又查明legacy目录没有`Interface.Zl`/`Interface.wil`，也没有`GameInter2`任何格式，而`LibraryFile.Interface`及`GameInter2`不会跨库别名回退；将此库级风险写入审计，要求按控件映射后再修。一次shell文件标记输出因`===`在zsh被解释为模式而失败，改用引用字符串重跑；前一轮曾经把API`blank=true`当成等于帧内空图，现改用独立库count/header裁定范围。此发现扩展NPC-06/07与技能/设置等后续审计，不自动授权替换资源或修改业务。未运行客户端/构建，不触发坐骑S/Ctrl+S、交易、任务接收/领奖、socket或Bad Request路径；`git diff --check`通过。


**2026-09-24 SET-08 设置窗高帧资源引用可见性核对：**针对GameInter全仓高索引扫描命中的ConfigControls F4741/F4743/F4750，沿`ConfigDialog`构造与`ApplyLegacyEiLayout()`检查节点显隐：这些帧属于现代`ConfigSoundBar`与`ConfigSectionPanel`页内容；legacy layout明确隐藏包含它们的`_page`、标题和tabs，并重建为F750旧版窗口、F760–763两态按钮及F751音量thumb。通过`system-window-render-evidence.json`、`settings-ambience-bgm-volume-evidence.json`确认EI对应四项设置组和两音量thumb，再独立用zlsdk读当前GameInter.Zl头并与旧WIL数据库对照：F750/F751/F760–763/F161–162均存在且尺寸匹配现有布局声明，不属于超过count=1103的范围。故撤回“ConfigControls高帧会在legacy设置窗中显示”的暗示，记为扫描命中但被legacy显隐排除；不等于SET-01..07整体验收通过。没有开设置窗或拖动、切换控件；未启动/操作客户端。起初宽搜索输出混有其它状态行，随后精读配置窗布局与EI设置JSON的精确控件条目；`git diff --check`通过。


**2026-09-24 MON-01 怪物悬停overlay静态来源链与资源检查：**沿`GameScene`每帧`CombatController.MouseObject→MonsterDialog.SetMonster/Refresh`闭合当前可达路径；怪物对象使面板显现，`LayoutHud()`置顶端中央，数据取`ObjectRenderer.Level/DisplayName/Health/MaxHealth/MonsterInfo.Stats`。随后沿每个图标调用回查legacy库：GameInter F5430及F1510–1517均超过当前GameInter ZL2/WIL count=1103；ProgUse F590/620/630/631/634超过WIL count=560且没有同名ZL；展开按钮LibraryFile.Interface F46/F44所在库缺失，非固定尺寸DXImageControl会将Size取为0，因此旧版legacy路径没有有效展开图标/RECT。可用旧Client确有同名MonsterDialog，状态为source-confirmed，不能升级为EI证据。研究`scene-entity-render-evidence.json`确认怪物世界绘制链，较新的`nameplate-label-evidence.json`通过两个通用实体caller闭合玩家/怪物/NPC共享的world-space nameplate；它不是中心顶部属性窗。发现`npc-body-strip-evidence.json`的旧“仅玩家”一句与F296更新caller/verdict冲突，已在MON-01标为superseded；不能据它说EI怪物没有名牌。0x40B2C0 hover/target-box调用仍pending，故新增MON-01和阻塞项，目标EI是否另有鼠标属性面板保持candidate。一次研究JSON过宽读取输出被截断，改为三个精确文件及字段读取；无补写外部研究文件。未运行/点按游戏、未触发怪物/NPC/坐骑/交易/Bad Request流程；本轮仅审计文档，`git diff --check`通过。


**2026-09-24 MON-01 EI怪物名牌证据冲突裁定：**核对`nameplate-label-evidence.json::callers/conclusions`后发现两处直接调用0x40CE20的地址0x41C76C/0x41CCAF都遍历通用场景实体；该Finding明确裁定覆盖player/monster/NPC并指出其caller核验修正了`npc-body-strip-evidence.json`残留的“仅玩家”旧语句。MON-01先前草稿照搬旧句把该渲染路径称为“仅玩家”，与更新工件冲突，现已修正：EI有受birth/state/1700ms门控的world-projected实体element名牌，其中包括怪物；它仍不等于Godot当前屏幕顶部186×54等级/名称/HP/属性框。0x40B2C0 hover/target-box五参调用仍pending，因此原版是否另有独立hover详情框继续candidate。未运行游戏、未操作hover/怪物/NPC业务；审计文档已更新，`git diff --check`通过。


**2026-09-24 INV-06 F280滚动字段与旧注释冲突核验：**沿EI背包F280 gauge构造/绘制、`0x42FFD0`父点击路由、通用`0x417D00`点击与拖动状态、父处理器`0x430056/0x430696`位置比例写入和`0x42EB94`背包paint读回交叉核对。F280 gauge与背包`+0x58`共用比例尺度94：输入handler将位置乘94后截断写入，paint以range 94把值除以93归一化；这不是“max=94行”或已证的有效滚动上限。600个占位WORD除以6列为100行，减6行viewport得到94的结构末端偏移候选；这不是“94步有效内容滚动”的证据。该成员属于背包滚动状态链而非负重读数；94是共享定点比例尺度，不是94行物品；作为`+0x58`行扫描参考，其与100行表−6行视口的94偏移上界数值吻合，但这只是结构可寻址范围，未证实实际内容可滚范围或用户输入终点。占位表100行只给结构容量，真实内容上限与拖动边界仍待核。当前`InventoryDialog.cs:270`注释称“F280仪表值始终为零”，源代码又将`WeightBar`整体隐藏且没有F280/input替代；该注释继承早期“仅reset写入”的过期研究摘要，与后来primary-static写者链矛盾。已在INV-06登记为源码注释/实现差异，保持本阶段只读，未改代码或做输入。几何公式的独立复核与运行端点验收留到后续计划；未进行背包业务操作、坐骑S/Ctrl+S、双人交易或Bad Request测试。`git diff --check`通过。


**2026-09-24 MON-01目标框证据闭合与差异重分类：**发现并细读研究工件`target-box-evidence.json`、`target-box-hover-verification-evidence.json`（F359）及其F271补充：EI原版目标反馈由0x40BB00（3000ms悬停名签）、0x40B850（测量文字并绘制居中边框）、0x40B750（名字）、0x40A8A0（HP条）、0x437DF0（悬停实体重绘）组成，锚定HUD+0xE4/+0xE8；固定路径屏幕锚点为(376,227)，悬停路径可用鼠标世界坐标换算。目标框无头像，也没有独立WIL帧。F359对悬停/名牌几何为primary-bytes；F413/F414验证仅发生于研究模拟器，不能充当EI实屏验收。与之比较，Godot `MonsterDialog`由`CombatController.MouseObject`逐帧驱动，置于屏幕顶部中央，显示等级/名称/HP/属性并可展开，职责与布局均非EI目标框的直接映射。已改MON-01与阻塞表：EI目标反馈身份不再未知；当前待查是Godot面板迁移映射、0x40B2C0另一调用的参数语义、EI运行时selector绑定/帧值和同版目标画面。一次`rg`传入旧目录下不存在的Godot代码路径报错，随即改为仓库实际`GodotClient/Scripts`与`GodotClient/Controls`路径并继续；未做运行输入或测试。

**2026-09-24 HUD-10 正式主面板底锚与研究RECT交叉复核：**重读 EI primary-static `primary-main-hud-setrect.md`、F50 WIL归档图及 `GameScene.LayoutHud()`/`LegacyHudLayoutLab`。EI 研究指令明确主面板RECT为`(0,601−height,width,600)`；F50高136，故顶边465。归档PNG为800×136，ImageMagick独立alpha bbox为800×135，末行透明；共用`0x460240`调用证据将帧头宽高与800×600 viewport clip分列，可解释RECT有效高度135而帧画布高136。正式布局800×600时用`vp.Y−Size.Y`得y=464，与研究RECT顶边差1；实验场使用显式y=465。此为可静态定位的legacy正式布局差异，进入修改计划，但研究版/本机EXE与WIL身份及真实 EI 屏幕仍未闭合，因此本轮只记录、不提前改运行源码，也不把它归因到人物窗展开抖动。按本轮要求跳过坐骑S/Ctrl+S、需第二玩家的交易和可能触发Bad Request的运行测试；没有启动/注入客户端或运行测试。一次宽`rg`结果过多被截断，随后收窄到具体方法、源文件与单一研究记录继续核对；没有依赖截断输出下结论。审计文档`git diff --check`通过，待提交本轮独立静态审计记录。

**2026-09-24 EXIT-02 legacy资源路由与占位绘制静态核验：**逐读`ExitDialog.cs`、`DXImageControl.Index/DrawControl()`、`DXButton.DrawGeneratedButton()/DrawFallbackButton()`、`MirSkin.IsUiLibrary()/GetLibrary()/GetLegacyWilLibrary()`并检查实际EI资源根。legacy `Interface`加载会尝试Interface.Zl，整库缺失后才找Interface.wil/.wix；本机`LegacyEI/Data`三者全缺。由此当前F281固定大小底图无纹理，F15关闭帧尺寸为0×0，SmallButton所需F41/43/42端片缺失后会绘制通用回退矩形；窗内文字由标签独立绘制。该结果把EXIT-01的业务入口错误与资源/显示差异分开登记为EXIT-02。研究工件中EI确实有GameInter F800，故不把当前目录缺少Interface误称为原版资源缺失。一次查询初始把MirSkin/LegacyUiSkin猜在Resources/Scripts目录，报路径不存在；随后`rg --files GodotClient`定位至Controls目录并完成核对。未点击退出窗、未发送注销请求、未启动客户端或运行测试；跳过坐骑S/Ctrl+S、双人交易及可能触发Bad Request的输入。`git diff --check`通过。


**2026-09-24 INV-06 94尺度/内容行数证据冲突更正：**重核 `trade-split-handle-evidence.json` Finding 301、`bag-window-draw-evidence.json`及 `inventory-window-render-evidence.json::paint_geometry`。Finding 301明确常数94是共享gauge定点尺度：输入handler写`trunc(position×94)`，paint按`value/(94−1)`归一化；同一补充还称46条记录约10行/4条有效滚动行，但未由该结论所列证据展示item最大尺寸或gauge拖动边界。另一方面EI背包600-word表有100行，`0x42F79C`以`+0x58`决定行扫描带，100−6确为理论末端起始偏移94；`0x430056/0x430696`又可将gauge位置映射至此字段。故此前把100−6写成“94个实际滚动位置”、以及把46条记录直接推成10行的表述均过度确定。现已统一更正：94既是共享比例常数，也数值上吻合满100行占位表的理论末端偏移；这不能证明94行内容或真实拖动/滚轮可达范围。保持待核项包括item尺寸上界/first-fit占用高度、端点94是否可达、滚到空内容的表现及wheel步进；INV-06修订计划增加0/93/94候选字段值，不将94预定为有效内容末端。全程只读工件与源码、仅更新审计文档；未操作背包或输入，`git diff --check`通过。


**2026-09-24 INV-06 物品帧尺寸上界交叉核验：**primary-static `bag-grid-geometry-evidence.json`/`server-data-crossref.json`闭合`0x42F6D0`以item data `+0x28`帧号读取selector el82（普通背包图标路径为`Inventory.wil`）帧宽高，并按36px网格计算占位尺寸。针对“46记录≈10行”的论断独立读取本机EI `inventory.wil/.wix`：WIX 1440项，按17-byte头与RLE payload bounds检查有499个有效正尺寸帧；最大宽帧F1040=(72×180)，最高帧F1192=(32×212)，最大候选脚印2×6由F1066=(56×198)、F1193=(40×208)、F1240=(52×188)达到。若46条记录可重复使用2×6脚印，first-fit容量推算可占96行（INFERENCE，未证item集合可达）；8766素材viewer抽查F1040与F1066，与独立WIX/WIL解析一致。资源SHA-256：WIL `430b0593aa0f0251ed88ee0074b4203c08ac937b7ec1055c5d96f08ecca48840`；WIX `69bf695b9aa1cfe5be35b8973b3f71459d95733f22043b2658b0bee509b6e062`。这只是本机资源全库边界：并未证明F1040/F1066或最大2×6帧可由目标EI物品记录引用，也未闭合同版EI EXE/WIL哈希或真实item-ID→frame清单。故撤回“46记录数足以推出10行/4有效行”的确定说法；真实可装内容高度仍待映射/数据证据。工具错误：首次独立WIX脚本只接受偏移26的magic `B13A`，遇到本文件旧式24-byte header后AssertionError；`xxd`也不可用。按`wilsdk.py`记录的双布局规则从WIX byte24重读，并以独立struct解析器核元数据；前两次错误均未写文件/影响结果。仅更新本仓库审计文档，未操作背包、未运行游戏或测试，`git diff --check`通过。

**2026-09-24 INV-05 数据来源边界补核与阻塞项细化：**沿现代字段源码复核 `LibraryCore/SystemModels/ItemInfo.cs::Image`、`DXItemCell.GetItemDrawIndex()/DrawItemIcon()` 与默认 `ItemLibraryFile=StoreItem`，确认当前实现链只说明 Zircon 现代 `ItemInfo.Image → StoreItem.Zl`，无法单独证明与EI item data `+0x28 → Inventory.wil` 对同一物品同号。本机 `Data/System.db` 与 `Database/System.db` 均为11,662,800字节且SHA-256相同（`2547c345…d4581c`），是相同的当前数据库副本；`LegacyEI/Data` 缺旧版物品表，故 #136/#153 的现代 Image=8/20 仍仅作现代样本，不外推成EI物品语义。已把所需证据明确写入 INV-05 阻塞项：同批目标EI EXE/WIL的旧物品记录（可识别名称/ID及`+0x28`）或来源可核验的旧版运行记录；拿到前不以同名、同帧号或图库总体相似率替代逐物品映射。工具错误及处理：首次 `rg` 正则中的裸 `+` 触发正则解析错误，换固定字符串搜索；本机没有`sqlite3` CLI，改用项目 MirDB 读取链的既有结论和标准库核文件身份；一次`apply_patch`因长行上下文不完全匹配失败，改用按`INV-05`行首定位的精确脚本修改。错误均未改变项目数据或得出错误结论。按照要求跳过坐骑S/Ctrl+S、可能Bad Request的运行期输入和双人交易；未启动游戏、未运行构建/测试。仅更新审计文档，`git diff --check`通过。


**2026-09-24 CHAR-03 用户参考画面并列核验：**重新查看用户最初提供的618×382人物状态展开截图和仓库中同一Godot运行实例的收起/展开裁图。将未改写的用户参考PNG归档至`docs/evidence/legacy-ei-ui/user-supplied-character-expanded-reference-2026-09-24.png`并记录SHA-256。参考图可见人物栏内简短属性组与右侧扩展属性组同时存在，右侧含道术/准确/敏捷/幸运/攻速行；当前`ToggleLegacyView()`隐藏全部收起态`_legacyAttributeLabels`并仅显示12行单列扩展标签，且既有Godot展开截图中第一行压到`STATUS`艺术标题。该差异作为视觉候选加入CHAR-03，与研究工件state 0/1共享`0x44BC80`的primary-static证据并列保留，不以单张裁图裁定二者；后续须查两组文本各自RECT、clip、F200/F201内容原点及截图来源。同图无完整viewport/root坐标或点击前后帧，不能用于根位置/瞬态验收。未启动游戏、未发送输入或运行测试；按要求跳过坐骑S/Ctrl+S、双人交易与可能Bad Request路径。像素统计工具尝试中默认Python缺少Pillow（`ModuleNotFoundError`）；改用系统ImageMagick取得图片尺寸并试扫亮像素，但背景装饰导致分组混杂，因此没有把该扫描当坐标证据；坐标差异仅由研究JSON和当前源码常量独立计算。`git diff --check`通过；未提交或推送。


**2026-09-24 SKL-06 技能栏组键位注释一致性续核：**沿`GameScene.MagicBarSpellSet`、`KeyBindManager`默认表、`MagicBar.GetSlotsForSet()`和可用旧`Client/Envir/CEnvir.cs`静态追踪：当前四个SpellSet action均配置Ctrl+F1/F2/F3/F4；每个set有24个`SpellKey`，裸F1–F12与Shift+F1–F12分作两组12键。`GameScene.cs`字段注释却写“F1~F8当前栏组/原版Ctrl+1~4切”，与同项目键表及旧Client键位定义相冲突。已把注释级矛盾登记到SKL-06；没有据旧Client行为推断目标EI，EI原始F键路由仍标待核。本轮首次查找把`KeyBindManager.cs`路径猜在`GodotClient/Scripts/`，报ENOENT；随后定位至`GodotClient/Controls/KeyBindManager.cs`并继续。未修改源代码、未运行游戏/构建/键盘或施法测试；跳过坐骑S/Ctrl+S、双人交易与可能Bad Request输入。`git diff --check`通过；不提交、不推送。


**2026-09-24 SKL-14 旧分类选择残留静态链核验：**从`MagicDialog.cs`对`_legacySelectedSkill`全文件引用确认，字段声明后只有legacy技能格鼠标回调会赋值；`SelectSchool()`改变分类、清理`_cells`并调用`RefreshLegacySkillSlots()`重建可见候选时没有清空该选择。父窗`_UnhandledKeyInput()`随后只检查键值和tuple内`UserMagic`，不验证该技能是否仍属当前学派/当前runtime entries，满足条件时写技能Set字段并发送`C.MagicKey`。因此构成Zircon当前legacy候选路径的静态陈旧状态缺陷；目标EI是否应切换类别即取消/保留选择仍未证，归入SKL-14待映射，不以代码修复预设EI语义。未运行键盘、技能绑定或网络请求；继续跳过坐骑S/Ctrl+S、双人交易和可能Bad Request输入。`git diff --check`通过；未提交或推送。


**2026-09-24 SKL-15 技能列表成员资格静态核验：**检查`GetVisibleMagicInfos()`完整筛选、`SelectSchool()`传入`RefreshLegacySkillSlots()`的记录集合、`MagicCellView`构造及父窗F键guard。当前筛选保留本职业但`UserMagic==null`的候选；legacy格会显示其`MagicInfo.Icon`和“未学习”tooltip，但快捷键处理因null user record返回。该行为与EI category linked list的成员规则不能直接判相同或相反：原版节点填充/学习状态写入者未闭合。新增SKL-15，将当前数据成员事实与EI未决链拆开，并列后续需追的链表构造、学习/职业/戒指变化。未读取/修改数据库、未发技能键消息、未运行客户端或测试；跳过坐骑S/Ctrl+S、双人交易和可能Bad Request输入。`git diff --check`通过；未提交或推送。


**2026-09-24 QUEST-07 已完成标题单击动作静态链：**读取`QuestDialog.RefreshPage()`已完成任务分支和`RefreshDetail()`操作按钮回调，并与`ApplyLegacyEiLayout()`/QUEST-01详情区根外裁剪结论交叉。标题左键不仅选中：有选择奖励时打开奖励选择窗，无选择奖励时立即调用`SendQuestComplete(index)`；详情action按钮在完成态也调用同一完成请求，但legacy裁剪下不可见/不可达。EI目前只证实`0x448490`行矩形命中后写选择索引并进入`0x448580`状态/激活分派，具体完成任务消息/标题点击的业务语义未从目标原始字节闭合。因此仅登记为当前Godot行为及EI待映射，不推断为原版差异。未启动客户端、未点任务或发送完成/领奖消息；跳过坐骑S/Ctrl+S、双人交易和可能Bad Request输入。`git diff --check`通过；未提交或推送。


**2026-09-24 GROUP-07 legacy邀请提交链与原版提示框调用点续核：**逐读`GroupDialog`构造回调、`ApplyLegacyEiLayout()`、`LegacyUiSkin.ApplyLegacyTestWindow()`和`DXTextInput`事件定义。唯一发送邀请的MouseClick附着在构造时创建的按钮(149,260,62×23)；legacy profile将根设256×244且Clip=true，输入框另移到(17,194,130×23)，发送按钮未移动，完全根外；GroupDialog没有订阅`_inviteName.TextSubmitted`。所以该profile当前没有可见且可命中的邀请提交动作。输入框与F910邀请透明hit区域(17,197,60×20)交叠，存在覆盖候选。随后核对`input-dialog-confirm-paths.json`完整24个ctor调用点和`layout.json`中`0x4246BA`记录：该frame 6/`0x418030`调用点的提示字符串是“请在这里添加您要删除的小组成员名字.”，业务上下文明确为**删除成员名字输入提示**，不能再写成邀请输入流程证据；原版另有“请在这里添加新的小组成员名字.”字符串，但当前已读静态工件尚未把它的控件/确认提交链闭合。F845 primary-bytes记录的五控件输入与`0x3FC/0x3FD/0x3FE`发送派发本身没有给出具体hit控件到输入框的映射。因此邀请/删除的原版完整控件路线仍待核，但当前legacy邀请按钮根外不可达是Godot源码/几何已闭合的问题，进入修复计划；删除成员行点击选择是否符合EI仍不确定。一次`rg`误猜不存在的`GodotClient/Controls/DXTextInput.cs`，随即定位至`FilterDropDialog.cs`；初次只看ctor总表/通用对话框证据时把F845 frame6可能关联到邀请，之后按`0x4246BA`交叉记录纠正为删除名字提示。没有启动客户端、发送组队邀请、移除成员或做需要第二玩家的测试；跳过坐骑S/Ctrl+S、双人交易和可能Bad Request输入。`git diff --check`通过。

**2026-09-24 本轮运行期阻塞/跳过清单续记（GROUP）：**

- 组队邀请/成员移除/离队的客户端点击与服务器副作用：本轮不操作，避免触发 Bad Request 或实际改动游戏状态；保持 runtime pending。
- 交易完整流程：需要第二名玩家，本轮没有可用双人验证条件；登记后跳过。
- 坐骑 S / Ctrl+S：按用户要求不重复测试；保持 runtime pending。
- 组队邀请控件与“添加新成员”字符串的 EI 入口映射、删除提示后的输入确认与 `0x3FD` 顺序：当前静态工件不闭合，继续查机器码/字符串引用；不通过危险输入试探。

本轮静态完成项：F845/F948 控件消息派发、共享 ctor 调用点表、`0x4246BA` 删除提示字符串记录与 Godot legacy 根裁剪几何交叉核对。若单项资料路径/工具调用失败，记录具体失败后改用 `rg --files`、字段级 JSON 读取或其他现存研究文件继续；本轮未把错误当成停止条件。

**2026-09-24 SKL-16 运行资源根 Mir3.exe 与研究 primary-static 样本身份核验：**为继续追技能书输入分派，检查 `/home/tetsuya/mir3ei/Mir3.exe` 候选副本。文件存在，SHA-256=`bd0909ae7b4e5ed49300f573e45a2c073a7cd8bc8da21a553be0a6bc2973fa15`，大小 `0x8E000`；PE32/i386，`.text` raw size=`0x83000`、VA=`0x401000`，`.rdata` raw size=`0x5000`、VA=`0x484000`。本组技能窗口 primary-static 工件记录的源路径为 `/home/tetsuya/NAS/TMP/EI传奇3.0客户端/Mir3.exe`，但没有写入哈希/大小；同一研究目录的其他 primary-static 记录将其所用 Mir3.exe 描述为 `0x80000` 字节、`.text rsize=0x75000`。当前运行资源根样本大小与之不同，因此目前无法证明两者字节身份相同；该 EXE 不能直接替代研究样本，也不能把它的地址结论升级成 EI primary-static。候选 EXE 在 `0x43A350` 附近的 `objdump` 字节反汇编与研究窗口所述 `0x43A370` 技能选择例程也没有形成可直接匹配的函数边界/语义证据；该次反汇编只作为身份差异线索，不从单一 VA 猜测函数归属。另只读核对仓库根 `login_game.sh`：它把 `legacy` 参数映射到 `--legacy-ui --legacy-hud`，编译后启动的是 `GodotClient`，不会启动 `mir3ei/Mir3.exe`。因此该候选EXE身份核查与 Godot legacy 的真实登录入口分属两类证据，不能混称当前UI运行程序。原研究样本的机器码仍未恢复，继续将 SKL-01/03/04 的精确页命中、控件激活和消息行为列为待原样本复核；本轮不修改控件布局或业务代码。

工具/路径错误记录：尝试读取未列出的 `skill-window-input-evidence.json` 时收到 `FileNotFoundError`，之后以 `rg --files` 确认该文件不存在，改查现存 `skill-window-render-loop-evidence.json` 与 `skill-window-context.json`；首次宽泛 `rg` 正则将字面量 `+0x18C` 当作量词而报 regex parse error，重试时拆成多个 `-e` 定点字面式并成功。`/tmp/mir3_full.asm` 不存在；系统 `objdump` 可以读取运行资源根的 EXE，故完成只读身份核对。续查时分别对研究工件标出的 `/home/tetsuya/NAS/TMP/EI传奇3.0客户端/Mir3.exe` 与 `/tmp/nas_mnt/NAS/TMP/EI传奇3.0客户端/Mir3.exe` 执行 `stat`，两路径均返回 `No such file or directory`；没有尝试挂载或访问凭据，也未以资源根副本替代。未启动游戏、未执行客户端输入、未做组队/交易/坐骑测试；`git diff --check`通过。

**2026-09-24 TRADE-04 服务端有效记录上限交叉闭合：**重新逐读 `TradeDialog.cs`、`GameScene` 的 `TradeItemAddedEvent` 绑定、`LibraryCore/Network/ServerPackets.cs` 与 `ServerLibrary/Models/PlayerObject.cs::TradeAddItem()`。Godot 对方镜像数组 `_playerItems` 长度10，`SetOtherItem()` 找首个空格，数组满则直接返回；`S.TradeItemAdded` 接收回调只调用该方法，没有第二处保存/绘制路径。服务端先排入 `S.TradeAddItem` 结果包，条件 `TradeItems.Count >= 15` 时在插入前返回；合法新记录成功后将 `result.Success=true`，写入字典并向交易对方 enqueue `S.TradeItemAdded`。因此成功项目上限为15条记录（不是物品堆叠数量15），当前 Godot 对方 UI 对第11至第15个有效记录没有镜像位置；此路径由客户端数组、事件绑定和服务端成功发送链共同静态确认，TRADE-04 从“容量不一致候选”升为“第11–15条已接受但不显示”的代码级缺陷。当前窗口仍有30个绘制/命中格，但服务端最高支持15条；目标EI的24个对象记录不据此推断同一上限。真实双人回包、清理/重开后的复用仍未动态验收，保留阻塞，不测试。

错误处理记录：首次 `rg` 把 `GameScene` 网络目录猜为不存在的 `GodotClient/Scripts/Network`，并把服务端文件路径猜成 `ServerLibrary/Models/Players/PlayerObject.cs`，两者均报路径不存在；随后用 `rg --files ServerLibrary | rg PlayerObject.cs` 与全仓 `rg` 定位实际文件 `ServerLibrary/Models/PlayerObject.cs`、收窄到 `TradeAddItem` 行区间，并检查事件订阅和packet类型后完成交叉链。没有执行双人交易、发网络请求、构建或运行测试；`git diff --check`通过。

**2026-09-24 GUILD-02 三态列表与现有数据绑定静态对照：**重读 primary-static `guild-window-paint-evidence.json` 的 state0/state1/other heads, counts, marker strings, scrollbar 与控件分派；再逐读 `GuildDialog.RefreshRows()/SelectTab()`, `LibraryCore/Globals.cs::ClientGuildInfo`, `ServerPackets.cs::GuildInfo/GuildUpdate/GuildWarStarted/GuildWarFinished`，并抽看 `Client/Scenes/Views/GuildDialog.cs`。EI state0有公告/敌对/联盟名称记录及marker色；state1有带“行会成员”marker的成员表；else访问另一表头/计数。当前Godot `_tab` 的0..5则依次承载无行会创建/有行会首页、成员、仓库、战争、外观和城堡；只有成员页语义部分相交。当前`ClientGuildInfo`提供成员/仓库/公告/行会统计，不含EI三类关系/公告条目数组；`S.GuildInfo`只包该对象，`S.GuildUpdate`仅列统计与成员，`GuildWarStarted/Finished`只提供单个行会名及开始时长/结束名。全仓源码搜索只找到翻译字符串“敌对行会”，没有独立联盟/敌对列表模型或EI marker绘制器。因此不能把现代战争页或公告编辑框当作state0的关系列表，也不能宣称三个EI state已被现有协议承载。旧`Client/Scenes/Views/GuildDialog.cs`本身是新版多页签代码，证据等级只标source-confirmed旁证，不替代EI primary-static。此次细化已合并至GUILD-02；原版各状态的入站数据来源、第三状态名称与动态切换条件仍待目标二进制/协议证据，GUILD-03九按钮逐项业务也仍需单独对照。仅读源码/JSON并更新审计表，未点按、未发起行会/服务器请求、未启动游戏；`git diff --check`通过。

**2026-09-24 CHAT-02 专用EI聊天窗实现（静态/编译核验）：**按用户优先级暂停Guild静态推进，直接对照`Mir3-Research/docs/research/ei-ui-layout/chat-window-unified-model.json`、`chat-window-render-evidence.json`、`chat-scrollbar-verification-evidence.json`与当前`CommunicationDialog.cs`、`ChatLogPanel.cs`、`ChatTextBox.cs`、`GameScene.cs`。确认当前F350好友/邮件内容不对应EI id8，常驻400×150聊天记录也不是id8 popup。新增`GodotClient/Controls/LegacyChatDialog.cs`，独立绘制F350并建立历史裁切/19行14px列表、输入行、关闭和六个模板按钮、wheel/上下按钮/轨道拖动滚动；消息统一从`GameScene.ReceiveChat/OnChat`接入两套聊天记录，legacy Enter仍经现有`SendChat(C.Chat)`，并保留链接物品编号；legacy模式快捷键输入路由到新窗，裸R切换聊天窗，`--legacy-open=chat`直达新窗。根据源控件命令字符串，六图标模板是拒绝私聊前缀、!、!!、!~、拒绝私聊命令、拒绝行会聊天命令；只接线到输入栏，未发送这些可能产生业务副作用的命令。`dotnet build GodotClient/ZirconClient.csproj --no-incremental`成功，0错误/3警告；用户会话当前仍有旧Godot客户端运行，未强制结束或另起相同测试账号，因此新实现尚无实际渲染/鼠标运行截图，轨道drag视觉和命令回包保留待启动清单。研究EXE与运行目录EXE身份差异仍适用；此结果是基于记录的primary-static证据与Godot编译验证，不宣称完整EI parity。一个`apply_patch`对大段GameScene上下文匹配失败，无文件写入；按行定位拆分为小patch后成功。工具/路径错误均记录并换窄范围查询，没有因此停止。未执行坐骑S/Ctrl+S、双人交易、可能Bad Request输入或提交/推送。
**2026-09-24 CHAT-02/CHAT-04 真实运行验收（完整 viewport，资源阻塞）：**在隔离 Xvfb `:100` 与临时服务端副本 `/tmp/zircon-ei-server` 中运行目标克隆，命令为 `DISPLAY=:100 godot-mono --path /home/tetsuya/development/zircon-legacy-layout-lab/GodotClient -- --server 127.0.0.1 --port 7000 --user test@test.com --pass test123 --char TestHero --window --legacy-ui --legacy-hud --legacy-open=chat`。stdout 记录 `S.StartGame Result=Success`、`[Game] 进入游戏! 玩家: TestHero`，以及 `[LegacyOpen] requested=chat type=LegacyChatDialog visible=True size=(572, 388) location=(225, 175) inputFocus=True`；完整窗口截图为 `/tmp/legacy-chat-f350-full-20260924.png`、普通聊天回显截图为 `/tmp/legacy-chat-f350-clean-input-20260924.png`。点击输入行后发送单条无命令语义的 `safe-chat-0924`，截图出现 `[Normal] TestHero: safe-chat-0924`，输入行清空；没有发送六个模板命令。当前实际资源根是 `/home/tetsuya/development/zircon/Debug/Client/Data`，仅有普通 `GameInter.Zl`（SHA-256=`32157af8ec3bd8506f6ef9488610f9090e54e6266d2d8df6a6af82e24f3fa09d`），`GameInter.wil/.wix` 与 `/home/tetsuya/mir3ei/LegacyEI/Data` 下对应文件均不存在；独立 zlsdk 头检查显示普通 ZL 无 F350，F380 为380×140而非 EI 证据中的16×502，故不把同号 ZL 帧视为 EI 资源证据。结论：完整 viewport、直达焦点、普通输入发送/回显已 runtime-verified；F350背景、六个36×34按钮、关闭/上下滚动控件、轨道拖动和滚动锚点不能验收，CHAT-01/02/04 保留资源阻塞。完整证据索引见 Mir3-Research `docs/research/ei-ui-layout/chat-runtime-acceptance-2026-09-24.json`。
**2026-09-24 CHAT-02/CHAT-04 当前请求克隆复测：**在 `/home/tetsuya/development/zircon` 当前 `ui/legacy-layout-lab` checkout 中重新运行隔离 Xvfb `:100`、1024×768 桌面和 `/tmp/zircon-ei-server` 服务端副本；命令、stdout、截图和资源身份记录在 Mir3-Research `docs/research/ei-ui-layout/chat-runtime-rerun-2026-09-24.json`。stdout 再次出现 `[LegacyOpen] requested=chat type=LegacyChatDialog visible=True size=(572, 388) location=(225, 175) inputFocus=True` 与 `[Game] 进入游戏! 玩家: TestHero`；`/tmp/legacy-chat-f350-full-current.png` 是完整桌面截图。
复测输入只发送普通文本 `safe-chat-current-0924`：`/tmp/legacy-chat-f350-clean-current.png` 显示 `[Normal] TestHero: safe-chat-current-0924` 历史回显且输入行已清空；六个拒绝/喊话模板均未发送。运行资源仍只有普通 `GameInter.Zl`（SHA-256=`32157af8ec3bd8506f6ef9488610f9090e54e6266d2d8df6a6af82e24f3fa09d`），EI `GameInter.wil/.wix` 身份未证明，因此 F350 背景、六个36×34按钮、关闭/上下滚动控件、轨道拖动和新消息滚动锚点继续保持未验收；不得用普通 ZL 同号帧填补该结论。
**2026-09-24 MODAL-01 继续静态审计：**`confirmation-prompt-evidence.json` 的原版确认窗键盘链闭合为 Tab 在三个启用按钮间循环、Enter/Space 激活并将 `(type<<8|index, tag)` 组合到 `0x7EE`；直接调用还包含 type=3 付金币、type=`0x66` 丢金币、type=6 删除行会成员、type=9 创建行会名称等输入业务。当前 Godot `ConfirmDialog` 只有统一确认回调，源码没有键盘焦点/Tab 顺序或 `0x7EE` 类型/tag 分派；GuildDialog 把创建行会名称作为窗内 `_createName` 直接组成 `C.GuildCreate`，ItemAmountDialog 处理数量输入，未找到与 EI type=6/type=9 的共享确认窗和 `0x7EE` 回传模型。结论保持 MODAL-01 阻断、MODAL-02 高差异；本轮不触发金币、行会或删除业务输入。
**2026-09-25 CHAT-02/CHAT-04 当前工作树真实运行复测与控件修复：**仅使用 `/home/tetsuya/development/zircon` 的 `ui/legacy-layout-lab` checkout；运行命令为 `DISPLAY=:101 godot-mono --path /home/tetsuya/development/zircon/GodotClient -- --server 127.0.0.1 --port 7000 --user test@test.com --pass test123 --char TestHero --window --legacy-ui --legacy-hud --legacy-open=chat`，资源环境为 `MIR3_EI_ROOT=/home/tetsuya/mir2ei`、`ZIRCON_UI_DATA_PATH=/home/tetsuya/mir2ei/Data`、`ZIRCON_LEGACY_UI_DATA_PATH=/home/tetsuya/mir2ei/Data`。stdout 记录登录成功、`GameInter.wil` fallback 从本地 `/home/tetsuya/mir2ei/Data/GameInter.wil` 加载以及 `[LegacyOpen] requested=chat type=LegacyChatDialog visible=True size=(572, 388) location=(226, 190) inputFocus=True`；该资源身份仍不能升级为研究 primary-static EI WIL/WIX 身份。

本轮发现并修复 `LegacyChatDialog.CreateSpriteButton()` 将关闭、上下滚动和六个模板按钮统一设置为 `CanBePressed=false` 的可达性缺陷；改为可点击后，完整1024×768截图证明 F350 关闭、R 重开、普通文本提交、六按钮本地模板入口均可到达，且没有发送拒绝/喊话模板命令。另将锁链轨道改为显式 `InputEventMouseButton/InputEventMouseMotion` 拖动控件；在22条安全普通消息溢出后，截图证明底部锚定、上滚按钮、滚轮/轨道点击与轨道拖动可改变历史窗口范围，手动上滚后新增 `scroll-anchor-safe` 保持用户历史位置而不抢回底部。

归档截图目录：`/home/tetsuya/development/zircon/.artifacts/ui-acceptance-2026-09-24/`，关键文件包括 `chat-f350-repaired-closed-full.png`、`chat-f350-repaired-r-open-full.png`、`chat-f350-scroll-overflow-bottom-full.png`、`chat-f350-scroll-up-full.png`、`chat-f350-scroll-anchor-safe-full.png`、`chat-f350-scroll-final-bottom-full.png`、`chat-f350-scroll-final-drag-top-full.png`、`chat-f350-scroll-final-drag-bottom-full.png`。截图是 Zircon runtime evidence，不是 EI 原版截图；无 EI 同版 WIL/WIX 时，F350资源身份、原版像素级一致性与静态/运行版本等价仍保持阻塞。

**2026-09-25 STATUS-01 人物状态面板实现与真实登录验收：**本轮仅使用 `/home/tetsuya/development/zircon` 的 `ui/legacy-layout-lab` 工作树、`/home/tetsuya/mir2ei/login_game.sh legacy`、`MIR3_EI_ROOT=/home/tetsuya/mir2ei`、`ZIRCON_UI_DATA_PATH=/home/tetsuya/mir2ei/Data`、`ZIRCON_LEGACY_UI_DATA_PATH=/home/tetsuya/mir2ei/Data`。运行环境为 Xvfb `:100`、完整 1024×768 viewport，账号 `test@test.com` / `TestHero`；stdout 记录 `S.StartGame Result=Success`、`[LegacyCharacter] ... hitRecords=11 paperDoll=(122,164)`，没有 ERROR/Exception/FAIL。

实现裁决：

| 维度 | 当前实现 | 证据/验收 |
|---|---|---|
| 根框与资源 | F200=244×328、offset=(-6,-92)；F201=520×328、offset=(-252,-92)，位置不随展开改变 | `CharacterDialog.ApplyLegacyEiLayout/SetLegacyView`；stdout 同时记录两态 root/background/offset；F200/F201 alpha bbox 仍以本审计 2024-09-24 独立解码记录为准 |
| 人物/装备区 | PaperDoll=(122,164)；原版 11 条记录全部建立 DXItemCell 命中区，Weapon `(86,114,60,90)`、Armour `(38,70,53,84)`、Necklace `(94,71,49,33)` 也可悬停/拖放 | 最新完整截图 `status-equipment-final.png`、`status-body-hit-hover.png`；后者在 Weapon 大命中区显示 `Moonlight, Light in the Darkness` 物品提示，证明隐藏纸娃娃层之上的命中代理有效 |
| 装备槽状态 | 已装备物品走当前 `DXItemCell` 图标/提示/兼容检查/拖放链；锁定格保留中键解锁事件，不再被 `_GuiInput` 前置守卫吞掉 | `DXItemCell.cs`；静态代码检查 + 编译；未做会改变生产装备的左键取下/替换 |
| 属性页 | F201 双列布局与原版证据基线：左列 `x=0xFF,y=0x43,15px`，右列 `x=0x17F,y=0x1E,15px`；HP/MP 当前值/上限、经验百分比、包袱/装备负重接入已有 GameScene 数据；无独立语义的腕力/恢复/魔法躲避/毒物躲避值保持空白，不伪造数值 | `status-attributes-final.png`；`RefreshLegacyAttributeLabels`；证据清单 `status-window-render-evidence.json` |
| 动态同步 | HP、MP、等级、经验、上限、Stats、批量装备/物品刷新都会触发人物页重绘；关闭任意窗口先清理全局 hover item | `GameScene.cs` 事件处理、`RefreshItemGrids`、`WindowManager.Close`；实机 stdout 无异常 |
| 交互/状态保持 | Q（当前 `KeyBindAction.CharacterWindow` 默认绑定；本机持久化映射以运行时为准）打开人物页，箭头切换 F200/F201，Esc 关闭；关闭后再开保持展开态 | `status-equipment-final.png`、`status-attributes-final.png`、`status-esc-final.png`、`status-reopen-final.png`；`status-esc-final.png` 无残留物品 tooltip |

本轮没有执行真实装备左键取下、从背包拖入、交易、坐骑或写库操作；这些路径仍以静态兼容检查和 hover/命中证据为界。装备耐久/强化/绑定等 EI 专用角标没有独立贴图证据，当前不以现代 ZL 或自绘图标冒充原版。

截图归档：`/home/tetsuya/development/zircon/.artifacts/ui-acceptance-2026-09-24/`。本段新增关键文件：`status-equipment-final.png`、`status-attributes-final.png`、`status-body-hit-hover.png`、`status-esc-final.png`、`status-reopen-final.png`。

剩余阻塞明确为证据边界：本轮未做切换地图/重新登录后的人物窗状态保持回放；本轮也未重复会改动装备的左键拖放。研究矩阵中既有 Round 787 的安全 Wood Sword 往返记录仍保留，不能与本轮 `hover-only` 复测混淆。EI 专用耐久、强化、绑定、职业/等级限制角标的目标版贴图与绘制链仍未取得独立证据，故“状态标记像素级一致”和跨地图/重新登录保持不宣布已闭合。
**2026-09-25 STATUS-02 跨地图/重新登录回归补证：**在同一真实登录会话中先打开人物窗口并保持 F200 属性态，使用安全 GM 命令 `@move D202` 切换到废矿二层；stdout 记录 `MapIndex=137 -> D202 (Deserted Mine Lv 2)`、`MapView 加载 D202: 200x200`。该成功切图路径没有客户端异常；同一实验前置尝试的 `@move D201` 因本地服务端缺少对应地图索引而返回找不到地图，未作为状态面板通过条件。`status-map-before.png` 与 `status-map-after.png` 证明成功切图后人物窗口仍可见，F200 属性态、人物/装备区域未被窗口重建清空，地图标题已变为“废矿山2层”。

随后停止并重新通过 `/home/tetsuya/mir2ei/login_game.sh legacy` 登录同一账号角色；本次最终构建复测 stdout 再次记录 `S.StartGame Result=Success`、`进入游戏` 与 D202 加载。`status-relogin-final2.png` 与最新 `status-relogin-final3-open.png` 证明重新登录后窗口可重新打开并显示装备页，未残留 Esc 关闭前的 tooltip；这是重新建立的初始窗口状态，不将跨进程“展开态持久化”误报为已保持。

本次补证将“切换地图后窗口状态”从未执行更新为已运行验证；“重新登录后可重新打开且装备状态可显示”已验证，但跨进程展开页是否应持久化仍无原版语义证据。Round 787 的 Wood Sword 安全装备往返记录仍是当前 `DXItemCell` 拖放链的真实回包证据；本轮没有重复写入装备。EI 专用耐久/强化/绑定/职业等级限制角标仍缺少目标版独立贴图和绘制链，继续保持证据边界。

本次补充截图：`status-map-before.png`、`status-map-after.png`、`status-relogin-final2.png`、`status-relogin-final3-open.png`。
**2026-09-25 STATUS-03 最终源码构建与登录冒烟：**恢复临时 `OperationAudit` 选择夹具后的正式源码执行 `dotnet build GodotClient/ZirconClient.csproj --no-incremental`，构建成功（0 errors，现有 nullable/unused warnings）。随后在 `DISPLAY=:100`、`1024×768` 窗口、`/home/tetsuya/mir2ei` 资源环境下直接启动 Godot，stdout 记录 `S.StartGame Result=Success`、`进入游戏`、`[LegacyCharacter] ... root=(244,328) ... hitRecords=11` 与 `[ProductionScreenshot] PASS ... viewport=1022x739`；截图 `status-runtime-final-2026-09-25.png` 已归档。该截图是 Zircon runtime evidence，不是 EI 原版截图；退出时仅见 Godot renderer RID 泄漏诊断，无人物面板异常、ERROR/Exception/FAIL。
**2026-09-25 STATUS-04 装备往返证据边界：**恢复源码后直接运行 `--operation-audit`，登录与窗口初始化成功，但诊断夹具按背包首件选择 `Healing Potion (II)`，因没有兼容的已装备目标而安全退出，未发送装备移动包。此前同一源码链使用临时、可回退的 Wood Sword 选择夹具完成过 `MOVE_FORWARD`、`MOVE_REVERSE`、`UNEQUIP_EXISTING`、`EQUIP`、`UNEQUIP`、`RESTORE_EXISTING` 六步，stdout 断言 `forward=True reverse=True equipmentRestored=True equipmentSlotCanonical=True failedSortPreserved=True failedSplitPreserved=True failedDeletePreserved=True pass=True`；夹具已恢复、正式源码无差异。该记录证明 DXItemCell/GameScene 的安全往返链，不构成 EI 状态角标贴图证据，也不替代真实用户选择任意装备的完整验收。
**2026-09-25 STATUS-05 装备状态标记证据边界与运行复核：**对 `0x0044B560-0x0044B6AD` 11 槽循环及本地 `Interface1c.wil`、`GameInter.wil`、`inventory.wil` 候选小帧完成独立复核；没有由 EI 状态窗口调用链绑定到耐久、强化、绑定、职业/等级限制或红点的独立 marker helper/帧选择。Zircon 因此在 `DXItemCell` 增加 `DrawItemBadgesEnabled`，仅 EI `CharacterDialog` 装备格关闭通用 `Interface` 47/48/49/103 角标，背包等非 EI 窗口保持原行为。真实 `/home/tetsuya/mir2ei/login_game.sh legacy`、1024×768 登录复测通过；`status-badge-guard-character-only.png`、`status-badge-guard-attributes.png`、`status-badge-guard-body-hover.png` 保留人物位置、F201 双列属性、Weapon 60×90 命中和无现代角标 fallback 的运行证据。详细证据：`/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/status-marker-resource-audit.json`。目标版逐状态 marker 贴图/语义/截图仍是明确阻塞，不宣称像素级闭合。
**2026-09-25 STATUS-06 槽位悬停/按下运行证据：**在真实登录人物装备页中对空 Torch 槽 `(177,70,38×38)` 做中心悬停与鼠标按下，均保持同一 38×38 命中矩形，未触发物品移动或写库；`status-slot-hover-guard.png`、`status-slot-pressed-guard.png` 已归档。绿色边框/半透明红底是当前 `DXItemCell.UpdateBorder` 的行为 fallback；EI 选中覆盖层的表驱动资源仍只有 primary-static-candidate 证据，未将该 fallback 宣称为像素级原版一致。
**2026-09-25 STATUS-07 EI 槽 hover/pressed 视觉边界修复：**独立反汇编 `0x0044B6B0-0x0044B78C` 确认原版悬停入口调用 `0x0044B720` 遍历 11 个槽位矩形，命中占用槽后仅调用 `0x004341F0` tooltip；没有 hover/pressed 状态写入，也没有槽边框/红底绘制分支。`DXItemCell` 新增 `DrawInteractionHighlightEnabled`，EI `CharacterDialog` 的 11 个装备格关闭通用绿色边框/半透明红底，命中、tooltip、选择和拖放输入链保留，非 EI 窗口不变。

在真实 `/home/tetsuya/mir2ei/login_game.sh legacy`、Xvfb `:100`、完整 1024×768 viewport 中复测，stdout 再次记录 `S.StartGame Result=Success`、`[LegacyCharacter] ... hitRecords=11 paperDoll=(122,164)`；`status-no-highlight-open.png`、`status-no-highlight-hover.png`、`status-no-highlight-pressed.png` 证明空 Torch 槽打开/悬停/按下三态均保持原始槽框，无现代高亮 fallback。耐久/强化/绑定/职业等级限制/红点的 EI 专用语义贴图仍无独立证据，继续保持证据边界。

**2026-09-25 STATUS-08 / SKL-UI-01 / HUD-ORB-01 用户截图复核与修正：**用户提供的 1024×768 legacy UI 截图指出状态窗纸娃娃向技能书一侧偏、技能书六个技能图标没有落在 F400 的六个底图影槽、红/蓝血球数值常驻且压在球面上。

- 纸娃娃锚点改为窗口相对 `(97,200)`：`status-window-render-evidence.json` primary-static 记录特殊人物绘制目标 `window origin + (0x61,0xC8)`。此前 `CharacterDialog.ApplyLegacyEiLayout()` 使用 `(122,164)`，两个坐标不能以 PaperDoll Control 外框居中来替代原版合成目标。
- 技能行重新按本机 `/home/tetsuya/mir3ei/LegacyEI/Data/GameInter.wil` 的 F400 底图像素配准。使用独立 `wilsdk.py` 解码得帧头画布 `512×512`、offset `(7,-44)`；Godot 当前背景配准原点 `(-30,-67)`。六个左页浅色行影槽的图标中心约为窗口相对 `(74,46+46*i)`，故将行位置改为 x=55、y=`26/72/118/164/210/256`，图标缩放框居中于影槽，名称/等级列移至 x=98。原版 6 个命中 RECT 的实际写入值仍未闭合；此处修的是可见像素位置，不宣称点击区域字节级一致。
- 红/蓝血球数字改为默认隐藏；鼠标悬停血球区域时分别在左右球下显示 HP、MP 当前值/上限。悬停命中区域延伸到数字下方，使指针移到数字上仍保持可见。该交互按用户此次明确提出的截图验收要求实现；尚无独立原版运行截图证明 EI 的悬停文字时序。
- `dotnet build GodotClient/ZirconClient.csproj --no-incremental` 成功，0 errors、3 个现有 warning。构建时已发现一个运行中的 Godot 客户端，因此没有关闭它或另起进程；本次三项修改的完整 viewport/悬停实机截图仍待下一次安全重启后验收，不能标为运行通过。

**2026-09-25 CHAT-05 用户截图复核：EI 聊天控件改用原始 WIL 帧：**用户最新截图显示 F350 内容区为空、输入栏不可见/不可输入、滚动锁链和频道按钮缺失。回查 `chat-window-unified-model.json`、`chat-window-render-evidence.json`：原版 id8/F350 为572×388，历史区 `(40,29,491,279)`、19行×14px，输入区 `(25,311,499,15)`；F360–371 是六个36×34频道控件，F380 是16×502纵向锁链轨道，F381/F382/F383 在目标本地 WIL 为空帧，故上下按钮仅保留已核定的命中区。

定位到 EI UI 帧取图的资源优先级问题：`LegacyEI/Data/GameInter.wil/.wix` 与转换后的 `GameInter.Zl` 同时存在，但 `MirSkin` 原先优先读取 `.Zl`，会让当前聊天按钮/滚动条的显示受转换产物内容影响。`--legacy-hud` 下现优先从原始 WIL/WIX 解码帧，同时统一 `GetSize`/`GetOffset` 的来源；非 legacy/world 资源仍走 ZL。独立读取目标 WIL 确认 F350 1024×512、F360–371 各36×34、F380 16×502。消息接收链已在 `GameScene.AddChatMessage` 分发到 `_chatLog` 与 `_legacyChatDialog`；当前空白历史本身无法在没有消息包时填充，本次没有伪造历史消息。构建通过；现有 `DISPLAY=:0` 登录进程仍在使用旧程序集，本轮未关闭它或重复登录抢占账号，因此 WIL 优先修正后的完整屏幕/键盘实机截图待下一次安全重启验收。

**2026-09-27 id7 第二状态窗（证据里的「GroupPopup」）解码定案：**

`legacy_ui.json` 把 `window.group-pop-candidate` 命名为 `GroupPopup` 是**错的**。
`window-paint-and-hotkey-dispatch-evidence.json` 的
`cell_analysis.window_identities_final.id7` 给出真身：

```
obj hero+0x47C28, ctor 0x4503B0, frame 200, (560,0), 244x328
identity: 状态窗-角色形象预览
  paint 0x450530: frame selector 0x566DD4 with byte[+0x551]
  character figure at (window.x+0x61, window.y+0xC8)
  13 SetRect attribute slots +0x578..+0x5E8
  mouse 0x450AC0
evidence: derived-primary
```

`status-window-family-evidence.json` 的 `audit_vs_exe` 佐证：
「Round 28 status-window anchor figure slot (window.x+0x61, window.y+0xC8)
confirmed in id1 paint 0x44B560 (NOT id7) — Round 32 'id7 figure' was the OTHER
figure window; **both have figures (id1 character figure, id7 preview)**」。

**11 个槽位矩形已从 `setrect_calls.json` 全部解出**（id7 ctor `0x4503B0` 之后
的 SetRect 调用，参数序 `[bottom, right, top, left]`）：

| 调用 VA | left,top - right,bottom | 尺寸 | 对应 id1 槽 |
|---|---|---|---|
| 0x004504EC | (86,114)-(146,204) | 60x90 | 武器 |
| 0x00450500 | (38,70)-(91,154) | 53x84 | 衣服 |
| 0x004504D5 | (94,71)-(143,104) | 49x33 | 项链（即证据所称 49x33 special slot） |
| 0x0045042B | (177,70)-(215,108) | 38x38 | 火把 |
| 0x00450470 | (27,186)-(65,224) | 38x38 | 左镯 |
| 0x0045048D | (175,186)-(213,224) | 38x38 | 右镯 |
| 0x004504A4 | (27,227)-(65,265) | 38x38 | 左戒 |
| 0x004504C1 | (175,227)-(213,265) | 38x38 | 右戒 |
| 0x00450442 | (27,264)-(65,302) | 38x38 | 头盔 |
| 0x00450459 | (64,264)-(102,302) | 38x38 | 鞋 |
| 0x0045051A | (103,264)-(141,302) | 38x38 | 毒 |

**结论：id7 的槽位矩形与 id1 完全一致**（逐一比对 `CharacterDialog.cs` 的 11
条装备槽矩形，全部吻合），是同一套装备槽 + 角色形象在 `(560,0)` 的**第二实例**，
不是组队弹窗。

我方**尚未实现 id7**。实现建议：复用 `CharacterDialog` 的 legacy 布局新建第二
实例置于 `(560,0)`，由 HUD cap15（状态栏）在 toggle id1 之外一并打开
（证据 `hud-caption-action-tail-evidence.json`：cap15 = toggle id1 +
`0x423E80(+0x29CE4, 0xC8, [0x29CFC], [0x29D00], 0xF4, 0x148)` 开 244x328 面板）。

另注：`legacy_ui.json` 的 `window.skill-book`（296x332 @(0,0)）同样有误，真值
452x380 @(348,0)，已于 commit `0ddb3669` 修正并留档。

---

# 2026-09-26/27 EI 旧版 UI 逐窗对照与修正（一轮完整审计）

对照基线：`Mir3-Research/docs/research/ei-ui-layout/`（`layout.json` 58 条几何记录、
`ui-coverage-matrix.json` 32 个窗口、各窗口逐帧证据 JSON）+ 用 `wilsdk.py` 直接解码
`/Users/tetsuya/mir2ei/LegacyEI/Data/GameInter.wil` 做像素级交叉验证。
验收路径统一为 `/Users/tetsuya/mir2ei/LegacyEI/login_game.sh legacy`。

## 已修正并推送（27 个 commit）

| commit | 内容 |
|---|---|
| `22cf7a1b` | 经验条对位 F50 凹槽 (235,122) 164x10；锁链滚动条右移上木桩并恢复圆点滑块 |
| `7dc578be` | 旧版窗口位置改用 `layout.json` 证据坐标，不再由 `LayoutHud` 居中 |
| `f311ffe7` | 组队窗补回标题 (45,22)/权限状态文字 y=58，关闭按钮 (226,214) |
| `9f5d678c` | 任务窗详情面板移回 (65,294) 204x76，补两个操作图标，去掉多余关闭按钮 |
| `b25210eb` | 聊天窗 6 条频道提示改用原版原文；去掉轨道多余拖拽交互 |
| `a1dd77b9` | 腰带按钮 (393,13) 16x14；悬停补 `(血量)/(魔法量)/(负重)/(经验条)` 前缀；隐藏聊天槽错误的 F350 拉伸底板 |
| `df4c0fa7` | Ctrl+R 恢复可用；C 改为交易请求 |
| `09e9c541` | 旧版窗口不再按窗口矩形裁子控件（原版只被 800x600 屏幕裁） |
| `a235fced` | 小地图改为固定 128x128 widget，去 chrome/标题/缩放，隐藏无素材的大地图按钮 |
| `21df381d` | 修正 `window.chat-pop` 的窗口类映射（G6） |
| `2bb8c076` `d6374cb8` | NPC 正文换行宽 149；补两列布局（>=7 行切 x=305） |
| `6b8e17fb` | 聊天历史区裁剪框对齐 (35,28) 485x266 |
| `0ddb3669` | 技能书位置 (348,0)；8 个分类页签改用各自帧 460/462/464 |
| `ac8b03ed` | 状态窗 4 个特殊标签改红色；补齐技能书审计断言表 |
| `c14bfdef` | 技能书关闭控件改用证据的 F440/441，隐藏多加的 161/162 |
| `db8f7e3f` | 背包补第三个子控件（模式字美术）；消掉模式文字与金币框重叠 |
| `a2fe956e` | 背包负重文字改两段两色，消除折行 |
| `081d517b` | 坐骑按钮去掉原版没有的禁用灰态 |
| `d7cddd38` | 技能书右页改渲染 `Magic.exp` 段落原文（新增 `ClientData/Magic.exp.txt`，50 段） |
| `8a8b4dbc` | 小地图标记改程序画描边矩形（NPC 黄 0xFFFF、玩家绿 0x64C864，±2px） |
| `67434a88` | T 键改为小地图 128<->256 切换 |
| `9df9b101` | 小地图支持 Ctrl+拖动重定位 |
| `6b6f995d` | 实现 id7 第二状态窗；修复被行尾注释吞掉的技能书位置 |
| `f3a731e1` | 大地图键改为小地图放大；V 键改为两态开关 |
| `85c9b0b4` | NPC 对话脚本支持 FCOLOR 行 token（16 色调色板逐项解出） |

## 独立裁决的证据库错误（6 处，均用素材像素或另一份证据交叉验证）

1. `layout.json` 的 `records.hud.belt` = (393,2) 24x16 → **错**。真值 (393,13) 16x14。
   依据：F50 底图在面板相对 y=0..10 完全空白（lum<25）、内容从 y=11 才开始；
   F159 实测 16x14。三处独立来源 + ctor 实参亦一致。
2. `layout.json` 的 `window.skill-book` = (0,0) 296x332 → **错**。真值 (348,0) 452x380
   （`window_identities_final.id14`，primary-bytes）。296x332 与马窗相同。
3. `skill-window-context.json` 把 黑暗/幻影/剑 页签记成 450/452/454 → **错**。真值
   460/462/464（像素解码确认 8 个页签是 8 组不同美术）。
4. `inventory-window-render-evidence.json::child_controls[2]` 说第三个子控件是
   Interface1c F267/268 人物精灵 → **错**。GameInter 266-269 无帧头；服务器模式分支
   换的是 263-275（수리/판매/보관，64x20），该控件是模式按钮。
5. `legacy_ui.json` 把 `window.group-pop-candidate` 命名为 `GroupPopup` → **错**。
   真身是 id7「状态窗-角色形象预览」，11 个槽位矩形与 id1 完全一致（已从
   `setrect_calls.json` 逐条解出）。
6. `layout.json` 的 `window.chat-pop` 映射到 `CommunicationDialog` → **错**。F350 是
   聊天窗（572x388），归属 `LegacyChatDialog`；CommunicationDialog 实为 Interface 200。

**另否掉 1 处误报**：审计认为背包 F280 竖轨「零值态仍画拖柄」，但证据原文写的是
「fill field `[this+0x58]` 在本构建里只有 reset 会写 0、没有任何其它静态写入，
**所以该条渲染为空**」—— 与我方行为一致，不该改。

## 修正过程中自己犯并抓到的 2 个错误

1. **「注释吞代码」回归**：一次编辑把 `Place(_statusPreviewDialog, ...)` 与
   `Place(_magicDialog, 348, 0)` 合并到同一行，后者被吞进行尾注释 —— 技能书位置
   修复静默失效（日志显示退回居中的 (731,0)）。`dotnet build` 完全看不出来，
   靠 `[LegacyWindowLoc]` 日志发现。已拆行修复并全局扫描同类问题。
2. **调色板索引 5 写错**：`0x000080` 按 COLORREF(0x00BBGGRR) 应为 (128,0,0) maroon，
   第一版写成 (0,0,128)。用独立 Python 复算转换后逐项比对发现。

## 仍未处理：全部为「证据阻塞」，需定案

| 项 | 阻塞原因（证据原文要点） |
|---|---|
| 状态窗 6 行缺值（腕力/魔法躲避/毒物躲避/中毒恢复/生命恢复/魔法恢复） | `first_column` 只给坐标基准 x/y_start/line_step，**未给这 6 行的取值来源**，无法映射到 Zircon `Stat` |
| NPC 菜单条 F1101/F1102 | 基址「由 WIL 头尺寸 + 参考常量推导」，且「常量只是二进制输入、**未提升为最终屏幕原点**」，推导公式未给；ctor `0x43EA80` 区域无对应 SetRect 可反推 |
| NPCIMG 头像 | 只有 blit 调用 `0x466130`/`0x440030`，**屏幕位置未给**；`0x43FF00-0x440100` 无 SetRect |
| 设置窗 F750 的 4px 垂直偏差 | Rin 的模板匹配显示贴图与控件坐标系统性差 4px，但「**哪一侧该动证据不足**」 |
| 聊天上下滚动按钮 | F381/382/383 在本机 `GameInter.wil` 里 **WIX 偏移=0（素材缺失）** |
| Caption 动作 cap2/cap6/cap7/cap15 | 原版是 `[winmgr+0x6208]` 布尔翻转、`[winmgr+0xD40]` 夹取等**内部状态操作**，我方无对应功能 |
| 证据库 8 处冲突 | 见 Hana 审计清单（背包占位表偏移、槽 stride 两说、状态窗命中 12 槽 vs 绘制 11 条、帧 167/170 被误当人物形象帧等） |

## 服务端缺口（非客户端问题）

坐骑窗 4 个按钮发的 `@上马/@遛马/@收马` 在 Zircon 服务端**未注册**
（`SEnvir.CommandHandler`），会回 "Command ... does not exist" —— 按钮永久不可用。
客户端发法与 EI 一致（都是经聊天命令），缺的是服务端实现。

## 独立测试场的正确用法（2026-09-27 补记，重要）

`GodotClient/Scenes/LegacyHudLayoutLab.tscn` 是**不连服务器**的旧版 UI 验收测试场，
可以直接驱动各窗口的真实控件链。两个易踩的坑：

1. **必须传 `--legacy-hud`，不是 `--legacy-ui`**。
   `MirSkin.LegacyUiRequested`（`Controls/MirSkin.cs:17`）只检查
   `--legacy-hud`；只传 `--legacy-ui` 时旧版 WIL 不会加载，
   日志会出现 `art=(0, 0)`（例如 `[LegacyCharacter] ... GameInter[200] art=(0, 0)`），
   看起来像几何错误、实际是取图源没切到 EI WIL。
2. **`ApplyLegacyTestWindow` 会调用各窗口的 `ApplyLegacyEiLayout()`** ——
   所以测试场里的窗口几何本来就是旧版布局（日志 `root=(244, 328)` 即证据），
   不需要额外加 legacy 分支。

### 可用的验收开关

```bash
export ZIRCON_UI_DATA_PATH=<仓库>/Debug/Client/Data
export ZIRCON_LEGACY_UI_DATA_PATH=/Users/tetsuya/mir2ei/LegacyEI/Data
godot-mono --path <仓库>/GodotClient res://Scenes/LegacyHudLayoutLab.tscn \
  -- --legacy-hud [--legacy-audit] [--legacy-npc-selftest] [--legacy-open=<窗口名>]
```

- `--legacy-audit`：跑各窗口的 `AuditLegacyEiLayout` 几何断言
- `--legacy-npc-selftest`：用「13 行正文 + 颜色 + 内嵌选项」的样例页驱动
  F1100 真实输入链（几何审计 → 打开 → 逐行下滚 → 触底禁用 → 上滚 → 点关闭）
- `--legacy-open=<名>`：直接打开指定窗口

### 实测输出（2026-09-27）

```
[LegacyNPC] lines=22 twoColumn=True col1=(150, 40)/(149, 136) col2=(305, 40)/(149, 136)
[NpcF1100SelfTest] PASS failures=0
```

这一条把 N5 的两列布局（>=7 行切 x=305）从「代码路径验证」升级为**实机渲染验证**：
22 行正文确实启用第二列，两列几何 (150,40)/(149,136) 与 (305,40)/(149,136)
与证据逐值吻合。

---

## 证据文件的坐标系判据与控件位置全量核查（2026-09-27）

### 教训：本项目已三次遇到同一类「坐标系陷阱」

| # | 场景 | 表现 |
|---|---|---|
| 1 | 行会窗背景锚点 | 同一值有多个写入点：构造函数改了，被 `ApplyLegacyEiLayout` 末尾覆盖 |
| 2 | `ResizeForBackground` | 方法名带 Legacy 语义，实际被现代路径（`ApplyGuild`/`SelectTab`）调用 |
| 3 | 控件坐标 | `absolute_candidate` 是 `window.x + origin.x`，而各窗 `origin_used` 不同 |

第 3 次差点据此报出假差异：`window-control-position-analysis.json` 里
库存窗关闭键的 `absolute_candidate` 是 (767,288)，而真正的**窗口相对值**是
`expression` 里的 (249,288) —— 与我方完全一致。若照 `absolute_candidate` 改，
会把关闭键挪到窗外。

### 判据：优先信每个窗的专用证据文件

`window-control-position-analysis.json` 的 expression/origin 部分记录**不可靠**，
已实测两处自相矛盾：

- `window.chat-pop` 的 F360/361：`expression` 是 `(window.y+28)`、
  `origin_used` 是 (114,76)，但 `absolute_candidate` 是 450（76+28=104≠450）。
  而 `chat-window-render-evidence.json` 对同一控件明确写
  `position {x:25, y:332, coordinate_space:"window-relative"}` —— **332 才对**，
  与我方 `new Vector2I(25 + 40 * i, 332)` 逐值吻合。
- `window.guild-candidate` 的 F161/162：该文件给 ctor 值 (260,298)（会落在窗口中腰），
  而 `social-window-render-evidence.json` 的 paint-time 真值是 (556,409)。
  运行截图里关闭 X 在窗内相对 ≈(574,421)，支持后者。

**结论**：涉及坐标时，优先采信有显式 `coordinate_space` 字段的
「单窗渲染证据文件」（`chat-window-render-evidence.json`、
`social-window-render-evidence.json`、`trade-window-render-evidence.json`、
`store-window-render-evidence.json`、`npc-window-render-evidence.json` 等）；
`window-control-position-analysis.json` 仅作**线索**，必须用专用文件或截图复核。

### 已全量核过、确认一致的窗口（窗口相对坐标）

| 窗 | 证据出处 | 我方 | 判定 |
|---|---|---|---|
| 聊天 F350 | `chat-window-render-evidence.json` | 关闭 (532,350)；频道 (25+40i,332)；滚动 (539,25)/(539,311)；历史区 (40,29)-(531,308)；输入区 (25,311)-(524,326) | **逐值一致 ✓** |
| 组队 F900 | `social-window-render-evidence.json` | F920/921 @ (9,52)；F910-915 @ (17,197)/(80,197)/(159,197)；关闭 (226,214) | **逐值一致 ✓** |
| 库存 F250 | `inventory-window-render-evidence.json` | 关闭 (249,288)；action (176,262)；F264/265 (176,262)、F267/268 (176,286) | **一致 ✓** |
| NPC F1100 | `npc-window-render-evidence.json` | 关闭 F161/162 @ (7,141)；F52/53 @ (290,145)；F54/55 @ (306,136) | **逐值一致 ✓** |
| 设置 F750 | 同上 | 关闭 (218,238) | **一致 ✓** |
| 状态 F200 | 同上 | F171/172 @ (176,264) | **一致 ✓** |
| 技能书 F400 | `skill-window-render-loop-evidence.json` | F450/451 @ (5,21) 等 8 个学派页签 | **一致 ✓** |
| 坐骑 | 同上 | F860-867 @ (28,244)/(74,244)/(133,244)/(192,244) | **一致 ✓** |
| 商店 F1000 | `store-window-render-evidence.json` | F1010/1011 @ (266,270)；F1012/1013 @ (127,267) | **一致 ✓** |
| 小地图 | `map-ui-resource-evidence.json` | 固定矩形 (672,0)-(800,128) | **一致 ✓** |

### 本轮据专用证据修正的

- **行会窗关闭键**：`(418,570)` → `(556,409)`（`social-window-render-evidence.json`
  paint-time 真值；原值 y=570 超出 F600 可见美术区高度 444）。
- **行会窗滚动条**：`(428,80)` → `(548,208)`（`guild-window-paint-evidence.json`）。

### 商店窗 state1/3/4 的「坐标空间」已解开（2026-09-27）

`window-control-position-analysis.json` 里商店窗有 6 条标 `outside-window` 的记录：

```
[1010,1011] rel=(466,169)  [1012,1013] rel=(370,162)
[1014,1015] rel=(324,159)  [1016,1017] rel=(434,159)   <- 这 4 条是 state1
[1010,1011] rel=(506,67)   [1012,1013] rel=(392,61)    <- 这 2 条是 state4
```

**这是工具假象，不是证据矛盾**：该文件用**基础窗口矩形 300x304** 判 inside/outside，
而这些控件属于别的 state —— 各 state 有**自己的矩形**：

| state | 内容 | 帧 | rect |
|---|---|---|---|
| 0 购买 / 3 制作 | five-row panel | 1000 | (0,186,300,304) |
| 1 出售 | sell grid panel | 1003 | content **498x304** |
| 2 仓库 | compact panel | 1001 | (-4,182,205,205) |
| 4 物品详情 | item-detail panel | 1002 | content **540x307** |

- state1 的 (466,169)/(434,159) 等落在 **498x304** 内 -> 合法
- state4 的 (506,67) 落在 **540x307** 内 -> 合法

**结论**：这些控件的坐标是**相对于各自 state 面板的原点**，与 state0/3 的
(266,270)/(127,267)（相对 300x304 基础矩形）同一套语义，只是基准矩形不同。
所以「outside-window」这一列**不能跨 state 直接采信**，必须按控件所属 state 取对应 rect。

**对实现的意义**：商店窗的 4 个模式面板（购买/出售/仓库/物品详情）各自独立定位，
我方目前把 state1(出售) 放在 `InventoryDialog.SellMode`、state2(仓库) 放在
`StorageDialog`、state3(制作) 与 state4 未实现 —— 这是**架构差异**，不是单个坐标错误，
要做需要先决定是否把商店窗合并为单一多模式窗口。

**仍未解开**：交易窗的 close(532,350)/accept(185,332)/cancel(225,332) 落在
484x330 基础矩形之外，且 F1050 可见美术区仅 483x330，**没有**类似的多 state 矩形可解释
（trade 只有一个 rect）。这一条继续挂起。

### 全窗截图复核与「布局只改一半」模式筛查（2026-09-27）

#### 已修的三处同源 bug（成因相同）

legacy 布局里**只改了控件的一部分属性、另一部分沿用现代值**，导致窗外留下可见残留：

| 窗 | 残留 | 成因 | 修正 |
|---|---|---|---|
| GuildDialog | 窗口中腰一条黑竖条 | `_scroll` 只改 `Modulate`，位置沿用现代 | → (548,208) |
| NoticeDialog | 外框右偏 220px、文本区在框外 | 背景 `Location` 未设（默认 0,0） | → (-220,-2) |
| QuestDialog | 窗外右侧一条黑竖条 | `_scroll` 只改 `Modulate`，位置沿用现代 (704,58) | → (290,59) 28x58 |

#### 系统性筛查结果：该模式已无新残留

做法：脚本扫全部 `ApplyLegacyEiLayout`，找出「设过 `Modulate`/`Visible`/`Index`
但没设 `Location`/`Position`/`Size`」的控件字段。

命中 9 个文件，**逐一核对后全部为良性**（都是 `Visible = false`，不需要位置）：
ConfigDialog(_page,_titleLabel) / GroupDialog(_allowCheck,_allowLabel,_lfgPanel,_lfgScroll,_optionsButton)
/ InventoryDialog(_ggTitle,_goldTitle,_titleLabel) / MagicDialog(_background,_list,_scrollBar)
/ MenuDialog(_titleLabel) / NPCDialog(_footerBackground,_scroll) / NPCGoodsPanel(_frame,_guildFunds)
/ QuestDialog(_titleLabel) / StorageDialog(_partsTab,_storageTab)

**结论**：该模式除已修三处外无新残留。

#### 逐张截图复核结论（after 目录）

- **character**（状态 F200）：外框/纸娃娃/装备格/绿色视图切换键/关闭键齐备，
  面板 242x330 ↔ 窗口 244x328，无残留。
- **storage**（F1001）：4x3 网格形态与我改的原点(21,42)/步距38 一致；关闭键在位。
- **quest**（F700）：修后黑竖条消失，两箭头落在美术右上角（≈(297,67)/(297,106)
  ↔ 证据 (290,59)/(290,89)）。
- **config**（F750）：韩文标题、ON/OFF 按钮、两条滑条、关闭键均在框内。
- **magic / inventory / trade / guild / chat / minimap / npc / notice**：前几轮已核。

#### 新发现（未修，记录待办）：仓库窗缺翻页热区

`StorageDialog` 代码里**没有** F1014/1015（上一页）与 F1016/1017（下一页）的引用，
但截图里这两个箭头**清晰可见** —— 它们和交易窗同理，是**烘焙在 F1001 贴图里的图形**，
证据 `store-window-render-evidence.json::controls.state_2_only` 给出的
`(x+0x1C, y+0xA2)=(28,162)`（prev）与 `(x+0x89, y+0xA2)=(137,162)`（next）
是它们的**热区**。

即：视觉已对，**交互缺失**（翻页不可点）。补热区需要先确认我方 12 格仓库是否分页
（原版 state2 的 12 格网格配合翻页箭头使用），属功能项，故本轮不动。

同类待办（已记录）：交易窗 accept/cancel 热区、行会窗 8 个热区。

### 商店窗 5 个 mode 的完整推演与范围评估（2026-09-27）

#### 每个 state 都有独立面板矩形 —— 可各自实现，不必合并窗口

| state | 面板 | rect | 控件（store-window-render-evidence.json::controls） |
|---|---|---|---|
| 0 购买 | **F1000** | (0,186,300,304) | close F1010/1011 @(266,270)、confirm F1012/1013 @(127,267) |
| 1 出售 | **F1003** | content **498x304** | @(466,169)/(370,162)/(324,159)/(434,159)（4 个，无帧号） |
| 2 仓库 | **F1001** | (-4,182,205,205) | @(172,169)/(71,165) + prev F1014/1015 @(28,162)、next F1016/1017 @(137,162) |
| 3 制作 | **F1000**（与购买共用美术） | (0,186,300,304) | 与 state0 同位置（close/confirm 同一组控件，state 切换只换内容） |
| 4 物品详情 | **F1002** | (0,184,**540x307**) | collapse/back @(506,67)、confirm @(392,61) |

**关键结论**：既然每个 state 有自己独立的 rect 与美术，
**不必**把商店窗合并成单一多模式窗口 —— 可以各自实现为独立面板。
「是否合并」只影响**归属**（谁持有它们），不影响坐标正确性。

#### 范围评估：state3/state4 是**新功能**，不是 UI 差异修正

核了我方现状：

- `CraftingStart` / `CraftingCancel` / `NPCWeaponCraft` 三个包**存在于 LibraryCore**，
  但 `GodotClient` 里**没有任何引用** —— 即**客户端没有制作 UI**。
- 也没有物品详情面板。
- state1（出售）我方有 `InventoryDialog.SellMode`，但那是另一套 UI，不是 F1003 面板。

所以：

| state | 我方现状 | 性质 |
|---|---|---|
| 0 购买 | `NPCGoodsPanel` 已接 F1000/300x304 | 已修 ✓ |
| 2 仓库 | `StorageDialog` 已接 F1001/205x205 + 翻页热区 | 已修 ✓ |
| 1 出售 | `InventoryDialog.SellMode`（另一套 UI） | 换 F1003 面板属**改造** |
| 3 制作 | **无** | **新功能** |
| 4 物品详情 | **无** | **新功能** |

**结论**：state3/state4 需要先有对应的业务流程（制作、物品详情），才谈得上套哪张面板。
这超出「按原版修正已有 UI 的差异」，属于**功能开发**。故本轮只完成推演、不改代码，
方案留待决定。

#### 至此客户端侧「有证据支撑且不依赖外部材料」的项已全部完成

已完成 61 项。剩余全部落在三类里：
1. **新功能**（商店 state3/state4、行会窗 3 个服务端包）
2. **需决策**（商店窗归属是否合并）
3. **需外部材料**（状态窗 6 行缺值、NPC 菜单条 F1101/F1102、NPCIMG 位置、
   坐骑 0..3 子态、Config F750 的 4px —— 需原版运行截图或 Mir3.exe 可读副本）

### 用反汇编解开第 1 项：坐骑 0..3 子态的语义（2026-09-27）

**背景**：此前这一项记为「证据说 `[0x7DA060]` 是 2-bit 枚举，但没给 2/3 的语义」，
被判为需要原版截图或 exe。本轮确认原版客户端就在 82 机上且未加壳，
用 `Tools/common/pe_dis.py`（capstone）直接反汇编取得答案。

**方法**：`find_xref(0x7DA060)` 扫 `.text` 找对它的引用，得到 4 处：

```
0x0041F5C8  mov eax, dword ptr [0x7da060]      <- 读 4 字节
0x0041F666  mov eax, dword ptr [0x7da060]      <- 读 4 字节
0x00426ABC  mov al, byte ptr [0x7da060]        <- 只读第 0 字节
0x00426AE1  mov al, byte ptr [0x7da060]        <- 只读第 0 字节
```

**语义（0x426ABC / 0x426AE1 的上下文）**：

```
0x426AB8  test eax,eax / je 0x426acc
0x426ABC  mov  al, byte ptr [0x7da060]
0x426AC1  test al, al
0x426AC3  jne  0x426b40          <- 子态 != 0 -> 跳过「上马」
0x426AC5  push 0x47b060          <- 字符串 "@上马"
0x426ACA  jmp  0x426b22
...
0x426AE1  mov  al, byte ptr [0x7da060]
0x426AE6  test al, al
0x426AE8  je   0x426b40          <- 子态 == 0 -> 跳过「收马」
0x426AEA  jmp  0x426b1e
0x426B01  push 0x47b058          <- 字符串 "@收马"
```

字符串实测（GBK 解码）：`0x47B060` = **`@上马`**、`0x47B058` = **`@收马`**；
相邻还有 `@遛马`、`@强制驯服`。

**结论**：
- 子态 **== 0** → 发 `@上马`（未骑乘，上马）
- 子态 **!= 0** → 发 `@收马`（已骑乘，收马）
- 即**判定只看「零 / 非零」**，值 2/3 与 1 在该判定上等价。
  证据说的「2-bit 枚举」成立（取值域 0..3），但**2/3 不改变上马/收马的分支**。

**与我方对照**：我方客户端发出的正是 `@上马` / `@遛马` / `@收马`（一致）。
所以这一项**不是差异** —— 差异只可能在「我方用哪个状态位决定发哪条命令」，
而原版用的是 `[0x7DA060]` 的第 0 字节。

**附带发现（需谨慎）**：`0x7DA060` 还被 `0x41F5C8` / `0x41F666` 当作**5 字节结构**
读取（dword + byte@+4），并与 `ecx=0x777698` 一起传给 `0x40F420`；
`0x777698` 位于 `.data` 且静态内容为空（运行期填充）。
所以该全局量可能**被多处共用**，不是坐骑专用 —— 后续若要改我方状态位，
不能简单假定它只有坐骑含义。

**工具副作用记录**：`find_xref` 用「解码失败就前进 1 字节重同步」的方式扫全段，
好处是不漏引用（第一版用 `md.disasm` 一次性扫会因遇数据而**提前停止**，
导致 FCOLOR 调色板明明在用却扫出 0 命中），代价是**数据区会产出伪指令** ——
读上下文时要留意，必要时从函数序言开始反汇编。

### 用反汇编解开第 2 项：NPC 菜单条 F1101/F1102 的几何（2026-09-27）

**背景**：此前记为「基址由 WIL 头尺寸 + 参考常量推导，未给推导公式；ctor `0x43EA80`
没有匹配的 SetRect」。本轮直接反汇编取得。

#### ctor 0x43EA80 确认「不设位置」

```
0x43EA9D  mov  dword ptr [esi], 0x476624     ; 基类 vtable
0x43EAA3  call 0x423ca0
0x43EAA8  push 0x4046b0 / 0x404690 / 3
0x43EAB4  lea  eax, [esi + 0x58]
0x43EAB7  push 0xb4                          ; stride 0xB4
0x43EABC  push eax
0x43EAC5  call 0x4686c4                      ; 分配 3 元素 x 0xB4 的控件数组
0x43EACA  lea  ecx, [esi + 0x278] ; call 0x465ef0
0x43EADA  lea  ecx, [esi + 0x3c4] ; call 0x4178e0
0x43EAEE  mov  dword ptr [esi], 0x476938     ; 派生 vtable
0x43EAF4  mov  dword ptr [esi + 0x3bc], 0
0x43EAFE  mov  dword ptr [esi + 0x514], 0
```

**只分配控件数组（3 个、stride 0xB4），不设任何位置** —— 与证据所述一致。
所以位置在**绘制代码**里设置，不在 ctor。

#### 帧号引用点

`find_xref(1101)` -> `0x43EEBD` / `0x43F0B7` / `0x43F2C1`
`find_xref(1102)` -> `0x43EF5E` / `0x43F11B` / `0x43F3B7`
（三对，对应三种菜单态）

#### 绘制调用与紧邻的 SetRect

`0x466130` 即证据提到的绘制调用（`push <frame>` 后 `call 0x466130`）。
`0x43EEBD` 那处紧邻的前一段是：

```
0x43EE9D  push 0x8a          ; 138
0x43EEA2  push 0x180         ; 384
0x43EEA7  push 0
0x43EEA9  lea  edx, [esi + 0x550]
0x43EEAF  push 0
0x43EEB1  push edx
0x43EEB2  call edi           ; SetRect([esi+0x550], 0, 0, 384, 138)
0x43EEB4  mov  ecx, [esi + 0x2c]
0x43EEB7  mov  ebp, [esi + 0x55c]
0x43EEBD  push 0x44d         ; frame 1101
0x43EEC2  call 0x466130
```

另一处（`0x43EF5E` 前）：
```
0x43EF40  lea eax, [ebp + 0x12]
0x43EF43  lea ecx, [esi + 0x560]
0x43EF49  push eax           ; ebp + 18
0x43EF4A  push 0x180         ; 384
0x43EF4F  push ebp
0x43EF50  push 0
0x43EF52  push ecx
0x43EF53  call edi           ; SetRect([esi+0x560], 0, ebp, 384, ebp+18)
0x43EF5E  push 0x44e         ; frame 1102
0x43EF63  call 0x466130
```

#### 素材实测（wilsdk）

| 帧 | 画布 | 可见 bbox | 可见尺寸 |
|---|---|---|---|
| F1100 | 512x256 | (64,59)-(447,196) | **384x138** |
| F1101 | 512x32 | (64,7)-(446,24) | **383x18** |
| F1102 | 512x64 | (64,10)-(447,53) | **384x44** |

#### 结论

- 菜单条的 **x = 0、宽 = 384（0x180）**，两处 SetRect 一致。
- 高度有两个值：**138（0x8a）** 与 **18（0x12）**。
- 与素材对照：**384x138 正好等于 F1100 的可见尺寸**、**384x18 正好等于 F1101 的可见尺寸**。
  说明「SetRect 紧邻在 push frame 之前」这个配对**未必成立**（可能是上一条绘制留下的），
  更合理的解释是：**每个菜单条按其自身美术尺寸设置矩形**。

**可用的确定值**：菜单条 x=0、宽 384；F1100 高 138、F1101 高 18、F1102 高 44（各自可见尺寸）。
**仍未闭合**：三对引用分别对应哪三种菜单态、以及每态用哪一帧 —— 需要再往下读
`0x466130` 的签名与调用点上下文。本轮到此，未据此改我方代码。

### NPC 菜单条：推导公式已闭合（2026-09-27 续）

上一轮留下「SetRect 与 push frame 的配对是否成立」的疑点，本轮读 `0x466130` 后闭合。

#### `0x466130` 是**取帧调度器**，不是绘制

```
0x466130  mov  al, byte ptr [ecx + 4]     ; this->state
0x466133  test al, al
0x466135  jne  0x466144
0x466137  mov  eax, [esp+4]
0x46613C  call 0x466640                   ; state 0
0x466141  ret  4
0x466144  cmp  al, 1 / je 0x466151
0x466148  cmp  al, 2 / je 0x466151
0x46614C  xor  eax, eax / ret 4           ; 其它 state -> 返回 0
0x466151  mov  edx, [esp+4]
0x466156  call 0x466720                   ; state 1 或 2
```

所以 `push <frame>; call 0x466130` 只**取帧**（返回 eax，调用方 `test eax,eax; je` 判空），
**不设置任何矩形** —— 上一轮看到的「紧邻 SetRect」确实属于**上一条绘制**，配对不成立。

#### 真正的推导：拿到帧之后，用**帧自身尺寸**设矩形

```
0x43EEC7  test eax, eax
0x43EEC9  je   0x43eee5
0x43EECB  mov  eax, [esi + 0x2c]
0x43EECE  mov  eax, [eax + 0x38]          ; 帧的尺寸结构
0x43EED1  movsx ecx, word ptr [eax + 2]   ; 高
0x43EED5  movsx edx, word ptr [eax]       ; 宽
0x43EED8  push ecx
0x43EED9  push edx
0x43EEDA  push 0
0x43EEDC  lea  eax, [esp + 0x1c]
0x43EEE0  push 0
0x43EEE2  push eax
0x43EEE3  call edi                        ; SetRect(rect, 0, 0, 帧宽, 帧高)
```

紧接着是**居中**计算（用两个参考常量）：

```
0x43EEF5  sub  eax, 0x12                  ; (... - 18) / 2   <- 行高
0x43EF09  sub  eax, 0x180                 ; (... - 384) / 2  <- 可见宽
```

#### 闭合后的公式

```
对每条菜单条：
    frame = 0x466130(frame_id)                    ; 取帧
    if (!frame) 跳过
    SetRect(rect, 0, 0, frame.width, frame.height) ; 矩形 = 帧的尺寸，原点 (0,0)
    目标位置 = 居中，参考常量 宽=384(0x180)、行高=18(0x12)
```

这正是证据所说「基址由 WIL 头尺寸 + 参考常量推导」的**具体公式**（此前未给）。

#### 与我方对照

我方 `NPCDialog` 用 F1101/F1102 时是**按固定坐标摆放**的，而原版是
**按帧自身尺寸设矩形 + 居中**。所以差异不是「差几个像素」，而是**布局方式不同**：
原版对每条菜单条都取它的自然尺寸再居中，我方写死了位置。

**未改代码的原因**：要改得先确定我方 F1101/F1102 当前用的是哪套坐标、以及三条
菜单条分别对应哪三个调用点（`0x43EEBD`/`0x43F0B7`/`0x43F2C1` 三对引用是三处调用）。
这需要先把我方 NPC 菜单的三态与那三处调用点对上，属于**结构改动**，
不适合在没有对照验证的情况下改。公式已取得，留作实施依据。

### NPC 菜单条：三帧的角色与行距已闭合（2026-09-27 再续）

上一轮留下「三处调用点分别对应哪三种菜单态、每态用哪一帧」。读 `0x43F040`（paint）
后完全闭合。

#### 真正的绘制调用是 `0x460240`，不是 `0x466130`

证据把 `0x466130` 标为「blit call」，实际它是**取帧调度器**（见上一节）。
真正的绘制是 `0x460240(renderer=0x8ab7a8, destX, destY, srcW, srcH, ...)`。

#### paint `0x43F040` 的结构

```
; --- F1100 背景 ---
0x43F065  push 0x44c                 ; frame 1100
0x43F06A  call 0x466130              ; 取帧
0x43F098  mov  ecx, [esi + 0x524]    ; 目标 Y
0x43F09F  mov  edx, [esi + 0x520]    ; 目标 X
0x43F0AD  call 0x460240              ; 绘制背景

; --- F1101 菜单行：循环 ---
0x43F0B4  mov  ecx, [esi + 0x2c]
0x43F0B7  push 0x44d                 ; frame 1101
0x43F0BC  call 0x466130
0x43F0EA  mov  ecx, [esi + 0x534]    ; 目标 Y
0x43F0F1  mov  edx, [esi + 0x530]    ; 目标 X
0x43F0F7  add  ecx, ebx              ; + 行偏移
0x43F0FA  inc  edx
0x43F102  call 0x460240
0x43F107  inc  edi
0x43F108  add  ebx, 0x12             ; <- 行距 = 18
0x43F10B  cmp  edi, [esi + 0x51c]    ; <- 行数
0x43F111  jl   0x43f0b4

; --- F1102 末行 ---
0x43F11B  push 0x44e                 ; frame 1102
0x43F120  call 0x466130
0x43F153  mov  edx, [esi + 0x544]
0x43F159  lea  ecx, [edi + edi*8]    ; edi*9
0x43F15D  lea  eax, [edx + ecx*2]    ; 目标 Y = [0x544] + edi*18
0x43F160  mov  ecx, [esi + 0x540]    ; 目标 X
0x43F16D  call 0x460240
```

#### 闭合后的结论

| 帧 | 目标字段 | 角色 | 素材可见尺寸 |
|---|---|---|---|
| **F1100** | `[esi+0x520]` / `[esi+0x524]` | **背景板**（矩形固定 (0,0,384,138)，目标位置居中） | 384x138 |
| **F1101** | `[esi+0x530]` / `[esi+0x534]` | **重复的菜单行**，循环 `[esi+0x51c]` 次，**行距 18**，目标 X 额外 `+1` | 383x18 |
| **F1102** | `[esi+0x540]` / `[esi+0x544]` | **末行**，位于 `[0x544] + 行数*18` | 384x44 |

- `[esi+0x51c]` = 菜单**行数**
- **行距 = 18（0x12）** —— 与 F1101 的高度一致
- F1102 是**末行**，比普通行高（44 vs 18），用作收尾

#### 与我方对照（差异性质）

我方 `NPCDialog` 对 F1101/F1102 是**按固定坐标摆放**，而原版是：
**每行按 F1101 的自身尺寸居中、以 18px 行距堆叠、末行用 F1102**。
所以差异在**布局算法**（循环 + 行距 + 末行区分），不是单个坐标。

**未改代码**：需要先把我方 NPC 菜单的行渲染改成「循环 + 18px 行距 + 末行 F1102」，
并确认 `[esi+0x51c]` 在我方对应哪个数据（菜单项数）。这是渲染结构改动，
公式与结构已完全取得，留作实施依据。

### 用反汇编解开第 3 项：NPCIMG（NPC 头像）的位置机制（2026-09-27）

**背景**：此前记为「blit 调用 `0x466130` 已知，但屏幕位置未给出」。本轮完全查清，
并且**推翻了「位置是客户端硬编码坐标」这个前提**。

#### 先纠正证据的两个错标

1. `0x466130` **不是** blit，是**取帧调度器**（见前节）。真正的绘制是 `0x460240`。
2. `NPCIMG` **不是**帧号或地址，而是**字符串字面量**（见下）。

#### 字符串表（.data，GBK）

```
0x47C4EC  '.\Data\NPCFace.WIL'
0x47C500  'NOTCLOSE'
0x47C50C  'NPCIMG'
0x47C514  'FCOLOR'
0x47C51C  '请输入:'
0x47C52C  '请输入要创建的行会名称:'
0x47C550  '@@buildguildnow'
```

`find_xref` 结果：
- `NPCFace.WIL` @0x47C4EC -> `0x43EDC5  push 0x47c4ec`（在 ctor `0x43ED00` 里**加载头像库**）
- `NPCIMG` @0x47C50C -> `0x43FFEB  mov edi, 0x47c50c`
- `NOTCLOSE` @0x47C500 -> `0x44006D  mov edi, 0x47c500`

#### NPCIMG 分支（0x43FFE7 起）

```
0x43FFEB  mov  edi, 0x47c50c            ; "NPCIMG"
0x43FFF0-0x44000E  逐字节 strcmp        ; 把当前文本行与 "NPCIMG" 比较
0x440019  test eax, eax
0x44001B  jne  0x440069                 ; 不等 -> 走别的标记
; --- 匹配 ---
0x44001D  mov  eax, [ebx + 8]
0x440021  call 0x4681f9                 ; 解析 "NPCIMG" 之后的数字 n
0x440029  lea  ecx, [ebp + 0x278]       ; 头像控件（ctor 里 this+0x278 持有 NPCFace 库）
0x440030  call 0x466130                 ; 用 n 取帧
0x440039  mov  eax, [ebp + 0x2b0]       ; 目标 X
0x44003F  mov  ecx, [ebp + 0x2b4]       ; 目标 Y
0x44004F  movsx edx, word [eax + 2]     ; 帧高
0x440053  movsx eax, word [eax]         ; 帧宽
0x440058  ... call 0x460240             ; 绘制
```

#### 关键：目标坐标字段**只被读、从未被写**

对 `+0x2b0` / `+0x2b4` 做全 `.text` 扫描（放宽到任意 mod=10 的 ModRM 形式，
覆盖 `[reg+disp32]` 的各种寄存器编码）：

```
0x00440039  op=0x8B  disp=0x2B0   <- 只有这两条「读」
0x0044003F  op=0x8B  disp=0x2B4
```

**没有任何写入**。而 `0x2b0 = 0x278 + 0x38`、`0x2b4 = 0x278 + 0x3c` ——
即这两个字段就是**头像控件自身的 +0x38/+0x3c**（与其它控件 `+0x38` 存帧尺寸的布局同构）。
由于从不写入，它们**恒为 0**。

#### 结论

**头像的屏幕位置不是客户端硬编码的坐标。** 机制是：

1. ctor `0x43ED00` 加载 `.\Data\NPCFace.WIL` 到控件 `this+0x278`
2. NPC 脚本文本里出现 `NPCIMG<n>` 这样的**标记行**时，客户端解析出 `n`
3. 用 `n` 从 NPCFace 库取帧，绘制到控件自身字段 `+0x38/+0x3c` 指定的位置（恒 0）

所以此前「需要取头像屏幕位置」这个提法本身不成立 —— **位置由 NPC 脚本的标记驱动**，
客户端侧没有可比对的固定坐标。

#### 与我方对照

我方 `NPCDialog` 若要还原这一点，需要：
1. 解析 NPC 正文里的 `NPCIMG<n>` 标记
2. 从 NPCFace 库取对应帧并绘制
（`FCOLOR` 同理是标记，对应 `[eax*4 + 0x47c4a8]` 的 16 色调色板索引，已在前节确认用法）

**未改代码**：这属于**新增解析逻辑**（NPC 脚本文本 → 头像渲染），不是坐标修正。
机制已完全查清，留作实施依据。

### 用反汇编解开第 4 项：F750 的「额外 9px 根裁切」（SET-05）—— **不是 bug**

**背景**：SET-05 记录「primary-static 根 248x264；F750 alpha bbox (4,119,248,273)；
背景设于 (-4,-119) 后有效像素覆盖根相对 [0,248)x[0,273)，**向下超出 9px**」，
并明确写「研究 layout.json::window_base_paint_evidence 只记录 EI 通用背景经 0x460240、
source viewport 800x600，**未证明目标 EI 根窗裁剪细节**」，因此列为未决。

本轮直接反汇编 EI 的窗口基类绘制（`vtable+0x0C`，证据标注为 `0x423D00`）判定。

#### 0x423D00（窗口基类绘制）

```
0x423D00  sub  esp, 0x6c
0x423D06  mov  eax, dword ptr [esi + 0x30]   ; visible?
0x423D09  test eax, eax / je 0x423e6c        ; 不可见 -> 返回
0x423D11  mov  eax, dword ptr [0x8b1874]     ; 全局模式标志
0x423D16  test eax, eax / je 0x423d6c        ; 为 0 -> 另一条路径
; --- 主路径 ---
0x423D1A  mov  eax, dword ptr [esi + 0x28]   ; 帧号
0x423D1D  mov  ecx, dword ptr [esi + 0x2c]   ; 帧库
0x423D21  call 0x466130                      ; 取帧
0x423D31  push 0xffff / push 0xffff          ; 裁剪起点
0x423D3B  push 0x258                         ; 600
0x423D46  push 0x320                         ; 800
0x423D53  mov  edx, dword ptr [esi + 0xc]    ; 目标 Y
0x423D57  mov  eax, dword ptr [esi + 8]      ; 目标 X
0x423D62  call 0x460240                      ; 绘制
```

#### 结论：裁剪矩形是**屏幕**，不是窗口根

`0x460240` 的裁剪参数是 `(-1,-1)` + `800` + `600` —— 即**屏幕 (0,0)-(800,600)**。
代码里**没有任何**把裁剪收窄到窗口根矩形（`[esi+8]`/`[esi+0xc]`/`[esi+0x10]`/`[esi+0x14]`）
的操作：`[esi+8]`/`[esi+0xc]` 只作为**绘制目标起点**传给 blit，不作为裁剪边界。

所以：
- 原版**不按根窗裁背景**，只受屏幕边界约束
- F750 那 9px 超出根矩形的像素，**原版会照画**（只要落在屏幕内）
- 我方 `ConfigDialog.ApplyLegacyEiLayout()` 显式设 `Clip=false` —— **与原版一致** ✅

**SET-05 可以按「非缺陷」收口**：这不是「Godot 多了 9px 需要裁」，而是
「Godot 放出的这 9px 本来就该放」。此前判为未决，是因为研究证据没覆盖基类绘制的
裁剪细节；现在补上了。

#### 与我方对照

无需改代码。`Clip=false` 保留。
（附：`[0x8b1874]` 是选择主/备两条绘制路径的全局模式标志，与 NPC 窗证据里
检查的是同一个；主路径用帧 + 屏幕裁剪，备用路径走浮点几何计算，见 0x423D6C 起。）

### 用反汇编解开第 5 项：状态窗 6 行缺值的**取值来源**（2026-09-27）

**背景**：此前记为「`first_column` 只给坐标基准 x/y_start/line_step，
**未给这 6 行的取值来源**，无法映射到 Zircon `Stat`」。

本轮从属性文字绘制链反汇编，把**每一行的标签与它读取的全局量**逐一配对，来源已全部取得。

#### 方法

绘制链在 `0x44BC80` 一带，每行的结构是：
「取标签字符串 -> `0x45DD70` 画标签 -> 取全局值 -> `0x46811c`(sprintf) -> `0x45DD70` 画值」。
`find_xref(全局地址)` 定位读取点，再读读取点附近 `push` 的字符串常量即可配对。

#### 配对结果（全部为实测，非推测）

| 标签（GBK 实测） | 全局地址 | 读取指令 | 宽度 | 格式串 |
|---|---|---|---|---|
| **装备负重** | `0x7DA121` + `0x7DA122` | `mov al,[0x7da121]` / `mov dl,[0x7da122]` @0x44BFBF/B9 | byte x2 | `%d / %d` |
| **腕力** | `0x7DA124` | `mov eax,[0x7da124]` @0x44C035 | dword | — |
| **准确** | `0x7DA16B` | `mov cl,[0x7da16b]` @0x44C0BE | byte | `+%d%` |
| **敏捷** | `0x7DA16C` | @0x44C130 | byte | — |
| **毒物躲避** | `0x7DA16D` | `mov al,[0x7da16d]` @0x44C22F | byte | — |
| **中毒恢复** | `0x7DA16E` | @0x44C2AB | byte | — |
| **生命恢复** | `0x7DA16F` | `mov cl,[0x7da16f]` @0x44C327 | byte | `+%d` |
| **魔法恢复** | `0x7DA170` | @0x44C399 | byte | `+%d` |
| **魔法躲避** | `0x7DA169` | `mov ax,[0x7da169]` @0x44C1B2 | **word** | `+%d%`（值先 `lea ecx,[eax+eax*4]; shl ecx,1` = ×10） |

#### 结论

**此前「未给取值来源」这一条不成立** —— 9 个属性的**显示来源全局地址、读取宽度、
格式串**都已从代码实测取得。特别是：

- **魔法躲避是唯一的 word**（其余为 byte），且值 **×10** 后再按 `+%d%` 输出
  —— 即它内部是**一位小数的百分比**（如 12 表示 1.2%）。
  这一条如果按 byte 处理会直接错。
- **腕力是 dword**，其余单字节字段是 `0x7DA16B..0x7DA170` 的**连续字节**。
- 恢复类三项（中毒/生命/魔法）**格式相同**（`+%d`），只有标签不同。

#### 仍未闭合的部分（如实标注）

- 这些全局量的**写入点不在主 exe** 中：对 6 个地址做 `mov [disp32], r32` 形式扫描
  全部为 0 命中，`0x7DA000` 也不是结构基址（0 引用）。客户端目录另有
  `Ats.dll`/`Gut.dll`/`flyEx.dll`，写入可能在 DLL 内，或用了未覆盖的指令形式
  （`mov [disp32], imm8` / `inc` / `or` 等）。
- 因此「全局量 -> Zircon `Stat`」的**语义等价**仍未证明，仅证明**显示来源与格式**。

**与我方对照**：我方 `BuildLegacyAttributeLabels()` 目前对这 6 项**留空**
（注释写明「无独立语义的值保持空白，不伪造数值」）。现在来源已明确，
若要显示，需要先确定这些全局量在服务端对应哪个字段 —— 那是**服务端字段映射**问题，
不是客户端显示问题。故本轮不改代码。

### NPC 菜单条实现过程中的两次失败定位（2026-09-27）

上一节记了「菜单条已绘制但宽度不足」。本轮尝试修复，连续两次失败，
把**失败原因**记下来，避免后续重复踩：

#### 失败 1：按帧尺寸铺满 —— 截图逐字节不变

改动：`DrawLegacyMenuStrips` 里改用 `MirSkin.GetSize()` 拿到的帧尺寸铺满。
结果：`npc-f1100-self-01-open-top.png` **仍是 225775 字节**，与改前完全相同。
说明这次改动**没有改变任何可见像素**。

#### 失败 2：把控件宽度与换行宽度分离（DrawWidth）—— 截图仍逐字节不变

改动：`NPCTextControl` 新增 `DrawWidth`，让控件 `Size.X` = 383（菜单条宽度），
换行宽度仍是 149。预期这样绘制不会被控件自身裁掉。
结果：截图**仍是 225775 字节** —— `DrawWidth` 同样**没有任何效果**。

中途还有一次**改差**的尝试：把菜单条的 X 从 `rect.Position.X - offset.X` 改成
`-offset.X`（想从控件左缘铺满）。因为 F1101 的 WIL offset 是 **(64,7)**，
这会把条子整体左移 64px 而被裁掉 —— 实测横条从 4 条变成 2 条且更淡。
已回退。

#### 定位结论：裁剪来自**父控件 `_textArea`**，不是文本控件自身

两次「改控件宽度无效」共同指向：`_text` 是 `_textArea`（**149x136**）的子控件，
裁剪发生在**父级**。所以：

- 只改 `_text.Size`（无论 `DrawWidth` 还是帧尺寸）都不会放宽可见范围
- 菜单条 **383 宽、跨两列**，物理上无法在 149 宽的父容器内显示

#### 正确做法（下一步）

把菜单条这一层**移出 `_textArea`**，改为由 `NPCDialog` 自身（或一个横跨两列的
子控件）绘制，位置取各行 Y、宽度按 F1101/F1102 的可见尺寸（383x18 / 384x44）。
**在移层之前，不要再尝试调整控件宽度** —— 已证明无效。

#### 当前实际状态（不夸大）

菜单条**已绘制**（截图体积 222486 -> 225775 证明像素确有变化，肉眼可见选项行后的
棕色横条），但**宽度受父容器裁剪**，不是完成态。

### SKL-03 右页详情：**已实现且颜色/行距与原版逐值一致**（2026-09-27 复核，撤销「没有右页绘制」的旧结论）

本节复核 `skill-window-render-loop-evidence.json` 里 `0x43A440` 渲染循环的**精确规则**，
并与当前 `MagicDialog` 实现逐项对照。旧结论「当前旧版技能书没有右页绘制」**已过期**。

原版渲染规则（证据原文，全部 primary-static）：

```
几何:  Y = winY + 0x0f(15)，X = winX + 0xeb(235)
       首行主 y = base + 15 = winY + 30
行距:  15px（"lines are drawn top-down at 15px pitch"）
名称行判定: 行首字节为 '[' (0x5b)，如 "[基本剑术]"
   4 次角阴影 0x45DD70(0, X-1,Y-1 / X+1,Y-1 / X-1,Y+1 / X+1,Y+1, 0x0A0A0A, 0, text, width)
   + 1 次主绘制 0x45DD70(0, X, Y, 0x96C8FA, 0, text, width)
其他行: 单次绘制 0x45DD70(0, X, Y, 0x0A320A, 0, text, 0)   ← 无阴影
```

**颜色必须按 COLORREF(BGR) 换算**（本轮再次踩到这个坑）：

| 元素 | 原始值 | COLORREF→RGB | 说明 |
|---|---|---|---|
| 名称行主色 | `0x96C8FA` | **(250,200,150)** 浅橙米黄 | 旧审计文字写的「蓝字」是**错的** |
| 名称行角阴影 | `0x0A0A0A` | (10,10,10) 近黑 | 4 向 |
| 正文 | `0x0A320A` | **(10,50,10)** 深绿 | 无阴影 |

当前实现对照（`MagicDialog.DrawDetailLine`，`MagicDialog.cs:1018`）：

- `line.StartsWith("[")` → `96c8fa` ✓ 与原版名称行主色一致
- 4 向 `0a0a0a` 角阴影 ✓ 与原版一致
- 非名称行 → `0a320a` ✓ 与原版正文一致，且**不加阴影** ✓
- `LegacyDetailX = 235`、`LegacyDetailY = 30` ✓ 与 `winX+235 / winY+30` 一致
- `LegacyLineHeight = 15`、循环 `y += 15f` ✓ 与 15px 行距一致

**方法教训**：本轮 grep 颜色时先命中 `323232` / `6496c8` 两个字面量，一度误判
"正文用深灰、名称行用蓝" 是 bug；查证后发现那两个颜色属于**页码**元素
（`_page+1` / `_pageCount`），与右页正文无关。**grep 到的第一处匹配不等于目标元素** ——
必须回到渲染函数本身确认归属再动手，否则会"修好"一个本来正确的东西。

**仍未闭合**：EI 技能 ID（`this+0x964`）到当前 `MagicInfo.Index` 的**逐技能映射**
仍未有已证证据（旧审计 SKL-02/03 的阻塞点不变）；段落取用依赖
`LegacySkillRowView.LegacyMagicExpParagraph(info.Index)`，其段号语义
（`Magic.exp` 的 `#N`）与 EI 网络技能 ID 的等价性未证明。

### SKL-02 技能格帧误用：**已不存在**（2026-09-27 复核，撤销「F410..F421 被误作格子图标」的旧结论）

旧结论称 `MagicDialog.BuildLegacySkillSlots()` 把 F410..F421 全用作 12 个技能格背景，
从而混入箭头（F410/411、F412/413 是导航控件）、空帧（F414–419）与数字标签（F420–431 为 F1–F12 字样）。

**复核当前源码：该函数已不存在。** 现在 legacy 技能格由 `MagicDialog.BuildLegacySkillRows()`
（`MagicDialog.cs:251`）创建 `LegacySkillRowView`（`MagicDialog.cs:746`）：

- 格子外观是**程序化绘制**（`_Draw()` 里 `DrawRect` 画选中/悬停/常态三态底色与边框），
  **完全不使用 GameInter F410..F421** —— 旧结论指出的"混入箭头/空帧/数字标签"不再成立。
- 技能图标走 `MirSkin.GetTexture(LibraryFile.MagicIcon, _info.Icon)`（`MagicDialog.cs:856` 一带），
  与证据 `skill-window-render-loop-evidence.json` 所述"EI 列表技能图标取自技能记录 `[skill+6]`，
  经 selector `0x566C90`（全局数组 el85）绘制"的**图库家族一致**：
  `mir3-dat-resource-path-table.json` 把 el85 绑定到 `Data/MIcon.wil`，而
  `LibraryFile.MagicIcon` 注册为 `Data\MIcon.Zl` 且已在 `MirSkin.IsUiLibrary` 里，
  legacy 下会回退到同 stem 的 `MIcon.wil`。

**仍未闭合（SKL-02 的实质阻塞点不变）**：EI `[skill+6]` 与当前 `MagicInfo.Icon` 的
**逐技能帧语义映射**没有已证证据。文件级复核已知：本机旧版 `MIcon.wil` 有 1106 帧/138 非空，
现代 `Data/MIcon.Zl` 有 1773 帧/224 非空，两边共有的 54 个非空索引**尺寸/offset 全部不同** ——
故不能以索引或画布规格推定 EI 图标一致。

**本轮方法**：先查当前源码是否仍存在旧结论描述的结构（`BuildLegacySkillSlots`），
发现已被 `BuildLegacySkillRows` + `LegacySkillRowView` 取代，再逐项核对帧来源。
**审计文档的「阻断」条目会随实现推进而过期，引用前必须先核当前源码。**

### 选角屏角色渲染链取证（2026-09-27，PRE-04 续）

原版选角屏（`parent`，screen obj `0x8A7140`）的**角色渲染是 3D 模型**，不是我方那种列表面板。
本轮反汇编取到完整参数：

**`0x4570D0`（角色 3D 绘制）**
```
入参：esi=模型对象，edi=位置向量指针(float X/Y/Z)，ebx=位移增量，ebp=?
0x457102  fld   [ebx]        ; 增量 X
0x457104  fmul  [0x476364]   ; * **0.5**
0x45710C  fadd  [edi]        ; + 当前位置 X
0x45710E  fsub  [0x476398]   ; - **320.0**
0x457114  fstp  [edi]
0x457116  fld   [ebx+4]      ; 增量 Y
0x457119  fmul  [0x476364]   ; * 0.5
0x45711F  fadd  [edi+4]
0x457122  fsubr [0x476394]   ; **240.0** - (...)
0x45712B  call  [eax+0x40]   ; 模型 vtable：预绘制
0x457133  call  [ecx+0x14]
0x45714B  call  [edx+0x30]   ; 模型 vtable：绘制
```

**常量实测值**（`read(va,4)` 按 float 解）：
| 地址 | 值 | 含义 |
|---|---|---|
| `0x476364` | **0.5** | 位移缩放 |
| `0x476394` | **240.0** | 屏幕中心 Y（480/2） |
| `0x476398` | **320.0** | 屏幕中心 X（640/2） |

即：**角色以 640×480 屏幕中心 (320,240) 为基准定位** —— 与"洞窟里左右各有一个人"的观察一致。

**调用方 `0x457A71`（首个调用点，`0x4579D1` 起是循环）**
```
0x457A69  mov  [esp+0x50], 0x3e48c8c9   ; scale = **0.196**
0x457A71  call 0x4570D0                 ; 画角色
0x457A76  push 1
0x457A78  push 0x6496c8                 ; 底色
0x457A7D  push 0
0x457A7F  push ebp
0x457A80  mov  ecx, 0x8ab7a8
0x457A85  call 0x45e570                 ; 填充矩形（角色脚下/背后的底板）
0x457A8E  add  esi, 0xb4                ; 循环步长 **0xB4**
0x457A99  jne  0x4579d1                 ; 循环
```
即**每个角色 = 3D 模型绘制 + 一个底色矩形**，底色 `0x6496C8`
（COLORREF/BGR → RGB (0xC8,0x96,0x64) = (200,150,100) 暖棕）。

**角色槽结构**（`login-flow-evidence.json::screens.parent.char_slots`）：
- base `+0xCB8`（阶段 0/3）/ `+0x10BC`（阶段 2），**stride 0x40，idx 0..1 → 2 个槽**
- anim 指针 `[slot+0x3C]`、flags `[slot+0x20]`、帧数 `[slot+0x22]`
- 显示串由 `0x4584C0` 组装：`0x47D79C "[男"` / `0x47D7A4 "[女"` +
  `0x47D778 " 武 士 ]"` / `0x47D784 " 法 师 ]"` / `0x47D790 " 道 士 ]"`
- 选中详情 `0x458150`

**我方现状（已修正，2026-09-27）**：`SelectScene` 已按 2 槽结构重做——角色直接站在
F50 洞窟背景里（不再用居中列表面板），每槽 = 角色动画 + 地面阴影 + 名称标签。
实现细节见下节「洞窟槽位实现（2026-09-27 完成）」。

### 洞窟槽位实现（2026-09-27 完成）

`GodotClient/Scripts/SelectScene.cs` 的 `UpdateCaveSlots()` / `CharacterBaseFrame()`：

1. **帧映射**：`index = 职业*2 + 性别`（战士男 440 / 战士女 740 / 法师男 1040 /
   法师女 1340 / 道士男 1640 / 道士女 1940）。WIL 逐帧解码独立验证：偶序号块是男性、
   奇序号块是女性，与 `(职业,性别)` 组合一一对应；旧公式 `职业 + 2*性别` 会把战士女
   和道士男都映射到 1040，已废弃。
2. **帧数**（实测连续有效帧）：440=18、740=16、1040=15、1340=17、1640=17、1940=15；
   单一连续循环（2400ms/轮，推导值），无攻击/待机分段（那是现代客户端的表，legacy
   块里没有对应物）。
3. **阴影**：WIL 中角色块 +20 就是阴影块（帧数一一对应：18/16/15/17/17/15），
   每帧 `Index = 角色当前帧 + 20`（`_Process` 同步），并按当前阴影帧宽度重新居中
   （各阴影帧宽度差最大 16px，固定位置会左右漂移）。
4. **WIL OffSet 不可用**：块内各帧 OffSet 差异最大 16px（实测 1040 块 X 从 -2 到 +14）。
   若按原版 `DrawControl` 的 `DisplayArea.Offset(OffSet)` 逐帧应用，角色会在动画中
   左右"游移" 15px。这些偏移是 3D 预渲染导出的裁剪元数据，原 3D 引擎的锚定不经过
   它们——2D 还原采用**固定顶左锚点**（`UseOffSet=false`），实测身体在帧内位置稳定
   （脚底恒在帧底边 ±2px，头部中心 X 漂移 < 4px）。
5. **位置（推导值，静态证据无原版 X/Y 常数）**：`SlotFeetY=440`（亮沙地板面）、
   `Slot0CenterX=350`、`Slot1CenterX=490`（两拱门前左右站位）。各职业 WIL 脚底线
   `off.y+H` 不同（283–333，740 战士女最高），统一脚底线让所有职业站同一地面。
   0x4570D0 的"中心 (320,240) 基准 + scale 0.196"是 3D 投影路径，槽位世界偏移由
   服务端驱动，静态证据取不到具体像素值。
6. **未实现差异**：原版每个角色脚下/背后还填一个暖棕矩形（`0x6496C8` → RGB
   (200,150,100)，`0x457A85`）——那是 3D 模型路径的底板；我们走 WIL 2D 预渲染路径，
   用阴影块代替，不叠暖棕矩形。


### 选角屏角色动画帧基址：**大部分是错的**（2026-09-27）

`SelectScene` 按 (职业, 性别) 选角色动画帧基址：

```csharp
(MirClass.Warrior, MirGender.Male)   => (240, 22, 300, 13, ...)
(MirClass.Warrior, MirGender.Female) => (440, 28, 500, 13, ...)
(MirClass.Wizard,  MirGender.Male)   => (740, 20, 800, 10, ...)
(MirClass.Wizard,  MirGender.Female) => (940, 26, 1000, 15, ...)
(MirClass.Taoist,  MirGender.Male)   => (1240, 27, 1300, 15, ...)
(MirClass.Taoist,  MirGender.Female) => (1440, 20, 1500, 10, ...)
(MirClass.Assassin,MirGender.Male)   => (1740, 25, 1800, 16, ...)
_                                    => (1940, 20, 2000, 10, ...)
```

**独立解码 Interface1c.wil 逐帧核对**（非透明像素 > 3000 视为有效角色帧）：

| 代码基址 | 实际 | 判定 |
|---|---|---|
| **240**（战士男 intro） | 200-270 区间**无任何非空大帧** | ✗ **空帧** |
| 300（战士男 idle） | (4,2) 非透明=0 | ✗ **空帧** |
| **440**（战士女 intro） | 440-457 存在，104×260，18 帧 | ✓ |
| **740**（法师男 intro） | 740-755 存在，84×240，16 帧 | ✓ |
| **940**（法师女 intro） | 未命中有效块 | ✗ |
| **1240**（道士男 intro） | F1240 = 52×52 小图 | ✗ |
| **1440**（道士女 intro） | 未命中有效块 | ✗ |
| **1740**（刺客男 intro） | 未命中有效块 | ✗ |
| **1940**（默认） | 1940-1954 存在，108×250，15 帧 | ✓ |

**该库里真实的角色动画块**（前段，非透明>3000 且 ≥4 帧连续）：

```
起=304  止=318  15 帧  256x256
起=440  止=457  18 帧  104x260
起=740  止=755  16 帧   84x240
起=840  止=850  11 帧   64x128
起=900  止=911  12 帧  256x256
起=1040 止=1054 15 帧  100x268
起=1060 止=1074 15 帧  196x56
起=1080 止=1094 15 帧  128x256
（后段另有 1202/1260/1320/1340/1640/1805/1860/1920/1940/1984 等块）
```

**结论**：代码里 8 组基址只有 **440 / 740 / 1940** 命中真实动画块，其余 5 组
（240、300、940、1240、1440、1740）指向空帧或无关小图 —— 表现为
**部分职业/性别的角色在选角屏根本渲染不出来**。

**待办**：把库里的角色动画块与 (职业, 性别) 做**逐块视觉比对**（渲染每个块的
首帧，按人物外观判定归属），再修正基址与帧数；不可按"看起来像 200 步长"外推。

### Interface1c.wil 角色动画块逐个识别（2026-09-27，接上节）

把上节扫出的候选块**逐个渲染首帧**（归档
[`char-animation-blocks.png`](evidence/legacy-ei-ui/char-animation-blocks.png)），按人物外观判定：

| base | 尺寸 | 内容 | 是否角色 |
|---|---|---|---|
| 304 | 112×164 | 一把剑 | ✗ 道具 |
| **440** | 104×260 | 铠甲 + 剑的男性 | ✓ **战士** |
| **740** | 84×239 | 红衣 + 剑的女性 | ✓ **女性角色** |
| 840 | 47×87 | 火球 | ✗ 特效 |
| 900 | 56×83 | 火球 | ✗ 特效 |
| **1040** | 100×267 | 红袍 + 帽的男性 | ✓ **道士/法师男** |
| 1080 | 115×143 | 火球 | ✗ 特效 |
| 1202 | 142×162 | 光效 | ✗ 特效 |
| 1260 | 195×213 | 雷电 | ✗ 特效 |
| **1340** | 143×250 | 红衣 + 法杖的女性 | ✓ **道士女** |
| **1640** | 77×257 | 白衣 + 剑的男性 | ✓ **男性角色** |
| 1860 | 121×74 | 冰 | ✗ 特效 |
| **1940** | 106×251 | 绿衣 + 剑的女性 | ✓ **女性角色** |

**结论**：Interface1c 里**真正的角色动画块只有 6 个** ——
**440 / 740 / 1040 / 1340 / 1640 / 1940**；其余候选块是技能特效（火球/雷电/冰）。

**注意**：这与代码里 8 组基址（240/440/740/940/1240/1440/1740/1940）既不是数量对应，
也不是步长对应。**不能按外观猜测就改** —— 需要找到原版把「职业+性别」映射到
这 6 个块的**代码路径**（`char_slots` 的 anim 指针 `[slot+0x3C]` 由
`0x458B20(slotIdx, flags)` 设置，追该函数的写入值才能定论）。

### 选角屏角色动画帧基址的真实来源：**槽数据，不是职业/性别查表**（2026-09-27）

反汇编 `0x458B20(slotIdx, flags)`（角色槽初始化）得到决定性结论：

```asm
0x458B25  cmp  eax, 2            ; slotIdx > 2 -> 直接返回
0x458B29  jg   0x458ba6
0x458B2F  cmp  bl, 5             ; flags >= 5 -> 直接返回
0x458B32  jae  0x458ba6
0x458B34  mov  dl, [ecx+0x930]   ; phase
0x458B3C  je   0x458b54          ; phase == 0 -> +0xCB8 基址
0x458B41  je   0x458b54          ; phase == 3 -> +0xCB8 基址
0x458B46  jne  0x458ba6          ; 其余 phase（非 2）-> 返回
0x458B48  shl  eax, 6            ; slotIdx * **0x40**
0x458B4B  lea  esi, [eax+ecx+0x10bc]   ; phase 2 的槽数组
0x458B54  shl  eax, 6
0x458B57  lea  esi, [eax+ecx+0xcb8]    ; phase 0/3 的槽数组
0x458B62  cmp  dword [esi], 0    ; 槽首字段为空 -> 返回
0x458B67  mov  al, [esi+4]       ; arg1 = **slot[+4]**
0x458B6A  mov  dl, [esi+5]       ; arg2 = **slot[+5]**
0x458B6D  push ebx               ; arg3 = flags
0x458B70  call 0x458ec0          ; -> 动画对象
0x458B77  mov  [esi+0x3C], eax   ; anim 指针
0x458B84  mov  [esi+0x20], ax    ; flags
0x458B88  mov  dx, [ecx]         ; 动画对象首字 = 帧数
0x458B8E  mov  [esi+0x22], dx
```

**结论**：角色动画的帧基址由 **角色槽自身的 `[+4]` / `[+5]` 字节**经 `0x458EC0` 求出，
而这两个字节是**服务端下发的角色记录**（该屏由服务端 case `0x209`/`0x20D` 驱动 phase）。

**因此 `SelectScene` 里那张「(职业,性别) -> 帧基址」硬编码表在结构上就是错的** ——
不是"表里的数值需要订正"，而是**这张表本身不该存在**：
原版没有职业/性别到帧基址的静态映射，帧基址随角色数据来。
这解释了为什么 8 组基址里有 5 组落在空帧上：它们是**猜测值**，而真实值来自槽数据。

**待办**：把角色槽的 `[+4]`/`[+5]` 与服务端角色记录字段对齐（`0x458EC0` 的入参语义），
再据此渲染；在此之前不继续修补那张硬编码表。

### 登录页 phase 状态机取证（2026-09-27）

原版登录屏（`char_select`，screen obj `0x8A9520`）的 `phase` 字段 `+0x8A4` 有三个状态
（`login-flow-evidence.json::screens.char_select.phase`，primary-static）：

| phase | tick | 说明 |
|---|---|---|
| **1** | `0x402D50` | 登录表单（账号/密码/按钮） |
| **2** | `0x4031A0` | **服务器列表** |
| **3** | `0x403560` | **过渡淡出**，时长 `0x7D0` = **2000ms**，结束后 → `0x402970` 进入 parent（选角屏） |

phase 3 的前进链：`0x403560 → 0x402970`，parent 预处理 `0x4028C0`、
指针 `0x8AB820` / `0x8B1870 = 0x8A7140`、构造 `0x456CB0`（即选角屏对象）。

phase 2 的子状态 `+0x8A5`：据记录会 "draw frame `0x3C` via
`0x466130(+0x5B0, 0)` + `0x45FD50(0x8AB7A8, [+0x5E8], [+0x5EC], 0x3C, 0xFFFF, 0xFFFF)`"。

**独立解码核对（本轮新增）**：`+0x5B0` 是 `Data/Interface1c.wil`，而**帧 60 是空帧**
（实测 F59/F60/F61/F62/F63/F64/F65 全部 alpha 全零；该库有 2000 帧，空帧成片出现）。

**结论**：所谓"淡出"**不是贴图动画**，而是 `0x45FD50` 画的**纯色覆盖层**
（该函数就是 HUD 图标填充与 NPCIMG 用的同一个填充原语 —— 见本文件别处记录）。
即原版是 **2000ms 渐变到黑的纯色过渡**，不是帧序列播放。
**因此不应去找"淡出贴图"**，实现了也只会得到一张空图。

**我方现状**：`LoginScene` 只有单一表单态，**没有服务器列表阶段、没有 2000ms 过渡**；
`login_game.sh legacy` 走的是自动登录（测试用），不经过这两个阶段。

**待办**：phase 2 的服务器列表内容（列表帧/文字/选择控件）仍需单独取证；
phase 3 的过渡按"纯色覆盖 + 2000ms 线性插值"实现即可，无需素材。

### phase 2「服务器列表」取证：**布局是运行期计算的，静态不可复原**（2026-09-27）

反汇编 phase 2 的 tick `0x4031A0`（服务器列表绘制）：

```asm
0x4031B5  mov eax, [edi+0xa3c]     ; 矩形 left
0x4031C1  mov ecx, [edi+0xa40]     ; 矩形 top
0x4031CF  mov edx, [edi+0xa44]     ; 矩形 right
0x4031DB  mov eax, [edi+0xa48]     ; 矩形 bottom
0x4031E1  fild [esp+0x14]          ; 转浮点
0x4031ED  sub eax, ecx             ; 高度
0x4031D9  sub edx, eax(左)         ; 宽度
0x4031F8/0x4031FC/0x403216         ; 组合成矩阵
0x403226  call 0x466800            ; 变换/矩阵构造
0x40323F  mov edx, [0x8ab7bc]      ; 鼠标状态
0x403248  mov ecx, edi
```

**该函数的矩形来自窗口对象的 `+0xa3c`/`+0xa40`/`+0xa44`/`+0xa48` 字段 —— 运行期值**，
不是静态常量。也就是说**没有静态布局表可抄**。

**旁证**：在 `LegacyEI/` 客户端数据里查找服务器列表相关资源（`*.ini`、`*server*`）
**没有任何命中** —— 列表内容不来自客户端本地文件，应由登录响应（msgid `0x7D1`，
格式 `'%s/%s'`）之后的服务端数据驱动。

**结论**：phase 2 的**内容与布局都无法从现有静态证据复原**。
要还原它需要 **运行期证据**（可运行的 EI 客户端 + 抓包/内存快照），
或服务端侧的下发格式。**在拿到这些之前不做实现** —— 按猜测画一张"服务器列表"
只会制造又一个看起来合理但与原版无关的界面。

**与我方现状的关系**：`LoginScene` 目前是"提交 -> 登录成功 -> 过渡到选角屏"两态，
没有 phase 2。而 `login_game.sh legacy` 走的是自动登录（测试模式），
**根本不经过 phase 1/2** —— 也就是说这条差异在**当前测试路径下不可见**，
优先级应低于选角屏的 5 阶段状态机与角色渲染细化。

### 又两个此前未知的视频素材：CreateChr.dat / StartGame.dat（2026-09-27）

在查"选角屏音效（CreateChr.wav / SelChr.wav / StartGame.wav）"时，
按证据里的**字符串**（`0x45B6D0` 的 ctor 写入 `'CreateChr.wav'` / `'SelChr.wav'` / `'StartGame.wav'`）
去找对应文件，结果在客户端数据目录里**没有找到这三个 .wav**，只找到同名的 **.dat**。

`file` 独立识别后确认：**这两个 .dat 是 AVI 视频，不是音频** ——

| 文件 | 类型 | 尺寸 | 帧数 | 编码 |
|---|---|---|---|---|
| `Data/CreateChr.dat` | RIFF AVI | **640×480** | **39** | Intel Indeo 5.0 |
| `Data/StartGame.dat` | RIFF AVI | **640×480** | （见 ffprobe） | Intel Indeo 5.0 |
| `Data/SelChr.*` | **不存在** | — | — | — |

**逐帧抽样确认内容**（图归档）：
* `CreateChr.dat` = **F50 洞窟场景的镜头移动**（同一洞窟、机位横移）——
  正是选角屏 **phase 1「创建角色中」**（`0x457615`，pump `+0x780`）的过场动画。
* `StartGame.dat` = 前段洞窟、后段淡入黑 —— **进入游戏前的过场**（phase 4 的过渡）。

**结论**：
1. 证据里 `'CreateChr.wav'` 这类**字符串**不能直接当作"存在同名音频文件"；
   磁盘上的实际资源是**同名 .dat 视频**。字符串可能用于**其它用途**
   （或证据的 `.wav` 后缀判断有误）—— 需回查 `0x47D60C` 附近字符串的**真实用法**
   才能定论；在定论前**不按"音效"接入**。
2. 选角屏有两段**全屏 640×480 过场动画**（创建角色、开始游戏），
   与登录页的两段（wemade / ei_Login）合起来共 **4 段视频**。
3. 这两段也需要用 `Tools/convert_legacy_login_video.sh` 的同法转成 `.ogv`
   才能在 Godot 里播放（Indeo 5.0 不受支持）；脚本目前只覆盖登录页那两个，
   需扩展。

**待办**：扩展转换脚本覆盖 CreateChr/StartGame；把两段过场接到 phase 1 与 phase 4；
回查 `0x47D60C`/`0x47D5F8`/`0x47D5E0` 三个字符串的真实消费者，确认它们到底是不是音效文件名。

### 更正：`CreateChr.wav` 等**确实是音效**，不是"证据有误"（2026-09-27）

上一节结尾我写了「证据里的 `*.wav` 字符串不能直接当作"存在同名音频文件"」——
**这个结论是错的**，原因是**我只在本机 `LegacyEI/Data/` 里找**，而字符串给的是
**`Sound\` 子目录**。回查原文：

```
0x47D60C -> '.\Sound\CreateChr.wav'
0x47D5F8 -> '.\Sound\SelChr.wav'
0x47D5E0 -> '.\Sound\StartGame.wav'
0x47D690 -> '.\Sound\CreateChr.mp3'
0x47D624 -> '.\Sound\SelChr.mp3'
```

**关键**：路径是 `.\Sound\...`。而**本机这份 EI 安装根本没有 `Sound/` 目录**
（`LegacyEI/` 下只有 `Data/`，全盘零个 `.wav`）—— 因此"找不到"是**安装不完整**，
不是证据错误。

**82 机的原版客户端有完整 `Sound/` 目录（609 个音频）**，其中：
```
CreateChr.wav  CreateChr.mp3  SelChr.wav  SelChr.mp3  StartGame.wav  StartGame.mp3
ToCreateChr.mp3  SWMSel.wav  Tfade in.wav  Tfade out.wav  ...
```

已取回 6 个与本流程相关的到 `LegacyEI/Sound/`：
`CreateChr.wav`(349KB)、`SelChr.wav`(358KB)、`StartGame.wav`(581KB)、
`Tfade in.wav`(266KB)、`Tfade out.wav`(266KB)、`SWMSel.wav`(643KB)。

**注意区分同名不同物**：
| 路径 | 类型 | 用途 |
|---|---|---|
| `Data/CreateChr.dat` | **AVI 视频** 640x480 39 帧 | phase 1 过场动画 |
| `Sound/CreateChr.wav` | **音频** | 创建角色的音效 |
两者只是**基名相同**，不是同一资源。

**教训**：证据给的是**相对路径**（`.\Sound\X.wav`）时，必须先在目标机器上
确认**该子目录是否存在**，再判断"资源缺失"还是"证据有误"。
我在 `Data/` 下找不到就下了结论，属于**在错误的目录里搜索后否定证据**。

**待办**：把这几个音效接到 Godot 的音频系统（需要 `SoundIndex` 条目与播放时机：
CreateChr -> 点「创建角色」、SelChr -> 选中角色、StartGame -> 点「开始游戏」、
Tfade in/out -> 两处过场）。

### 音效接线调查：文件都在，但**语义用法不同**（2026-09-27）

查我方音频链路（`SoundPlayback.Play` + `ClientData/sounds.json` 目录表）：

* `SoundPlayback.Play(owner, SoundIndex)` 从 `res://../Debug/Client/Sound/<entry.FileName>` 加载
  （`Debug/Client` 软链到 `mir2ei/`，即实际读 `mir2ei/Sound/`）。
* 目录表 `ClientData/sounds.json` 由 `Tools/magiclab/extract_sound_catalogs.py` 生成，
  含 731 条 `sounds`。
* `mir2ei/Sound/` **已有 3172 个音频**，其中 **`CreateChr.wav`/`.ogg`、`SelChr.wav`/`.ogg`、
  `StartGame.wav`/`.ogg` 全都存在**（我上一轮从 82 机取的 `LegacyEI/Sound/` 那几个其实是冗余的）。

**但目录表里的用法与 EI 证据不一致**：

| SoundIndex | 我方目录表指向 | 类别/循环 | EI 证据里的用途 |
|---|---|---|---|
| `SelectScene` | **`SelChr.wav`** | **Music / loop=true** | `.\Sound\SelChr.wav` 是**点击音效**（ctor `0x45B6D0` 的三个文件之一） |
| `LoginScene` | `Opening.wav` | Music/loop | — |
| `CreateChr` | **无此条目** | — | `.\Sound\CreateChr.wav` 音效 |
| `StartGame` | **无此条目** | — | `.\Sound\StartGame.wav` 音效 |

即：我方把 `SelChr.wav` 当成**选角屏背景音乐循环播放**，而 EI 里它是**一次性点击音效**；
`CreateChr` / `StartGame` 两个音效**根本没接**。

**同基名不同用法的又一个例子** —— 与 `Data/CreateChr.dat`（视频）vs `Sound/CreateChr.wav`（音频）
是同类陷阱：**光看文件名判断用途会错，必须看调用点**。

**待办**：确认 EI 里 `SelChr.wav` 到底是"选角屏 BGM"还是"点击音效"（证据显示是后者，
但需核 `0x45B900` 的调用时机与循环标志）；再决定我方 `SelectScene` 的 BGM 该换成哪一首
（`Sound/` 里有 `Main.wav`/`Opening.wav`/`Ending.wav` 等候选）。
在语义定论前**不接线** —— 直接把 `SelChr` 从 BGM 改成点击音会破坏现有背景音乐。

### 三个音效的**加载点**已定位；播放点仍需追字段读者（2026-09-27）

反汇编 `0x456F0C`-`0x456F56`（选角屏对象的一部分）确认：三个音效是**预加载并存句柄**，
不是播放：

```asm
0x456F12  push 0xa(10)                 ; 加载参数
0x456F14  push 0x47d60c                ; ".\Sound\CreateChr.wav"
0x456F1A  call 0x45b6d0                ; 加载器
0x456F1F  mov [esi+0x113c], eax        ; -> +0x113C
0x456F2A  push 0xa
0x456F2C  push 0x47d5f8                ; ".\Sound\SelChr.wav"
0x456F32  call 0x45b6d0
0x456F37  mov [esi+0x1140], eax        ; -> +0x1140
0x456F43  push 0xa
0x456F45  push 0x47d5e0                ; ".\Sound\StartGame.wav"
0x456F4B  call 0x45b6d0
0x456F56  mov [esi+0x1144], eax        ; -> +0x1144
```

字段与证据一致（`+0x113C`=CreateChr、`+0x1140`=SelChr、`+0x1144`=StartGame）。
旁边还有 `0x45b7f0` 对 `+0x1140`/`+0x1144` 的调用（`0x456EF4`/`0x456F00`），
像是**释放/停止**，说明这三个是**实例级已加载音效**。

**判定 `SelChr` 用途的下一步**：找 `+0x113C` / `+0x1140` / `+0x1144` 这三个字段的
**读者**（`mov reg,[reg+0x1140]` 一类的指令）—— 读者所在的函数决定调用的时机，
时机决定它是「选角屏 BGM」还是「一次性点击音效」。
**在看到读者之前不接线**（上一节已说明理由：改错会破坏现有选角屏 BGM）。

**顺带确认**：三个字符串**各只有 1 个 xref**，都在这一段，说明字符串没有别的消费者 ——
排查范围收敛到「字段读者」这一处即可。

### 定论：三个音效的**播放时机**已闭合（2026-09-27）

扫描 `mov r32,[reg+disp32]`（`8B /r`、modrm=10）找 `+0x113C/+0x1140/+0x1144` 的读者，
每个字段**恰好 2 个**：一个在 ctor 段（释放/停止），一个在**业务流程点**：

| 音效 | 字段 | 业务流程读者 | 该地址附近的事件 |
|---|---|---|---|
| `CreateChr.wav` | `+0x113C` | **`0x459AB6`** | `0x459AC5` = **F51「创建角色」** → phase 1 |
| `SelChr.wav` | `+0x1140` | **`0x459220`** | `0x45922F` = **服务端 case `0x209`** → phase 3 |
| `StartGame.wav` | `+0x1144` | **`0x459456`** | `0x459465` = **服务端 case `0x20D`** → phase 4 |

（读者地址都在对应处理函数起始处**前 15 字节内**，即播放发生在该事件开头。）

**定论**：
1. `CreateChr.wav` = 点「创建角色」时播 —— **一次性音效**
2. `StartGame.wav` = 服务端 `0x20D`（真正进游戏）时播 —— **一次性音效**
3. **`SelChr.wav` = 服务端 `0x209` 响应时播 —— 也是「一次性音效」，不是选角屏背景音乐**

**因此我方 `ClientData/sounds.json` 把 `SelectScene -> SelChr.wav / Music / loop=true`
是错的**：它把一次性音效当成了循环 BGM。选角屏的**背景音乐**应是另一首
（`Sound/` 里有 `Main.wav`/`Opening.wav`/`Ending.wav` 等候选，需另找 EI 的 BGM 表，
不要拿 `SelChr` 顶替）。

**接线决定**：
* **接** `CreateChr.wav`（创建角色按钮）与 `StartGame.wav`（进游戏）—— 时机无歧义。
* **暂不接** `SelChr.wav`：它的触发点是"服务端 `0x209` 响应"，而我方的对应响应是
  `StartGameResult`（已用于 `+0x1144` 那一支）；要接它得先厘清 `0x209` 与 `0x20D`
  在我方的**两个不同响应**分别是什么，否则会把两个音效叠在同一时机。
* **不改** `SelectScene` 的 BGM：现在改成 `SelChr` 之外的曲子需要 EI 的 BGM 表证据，
  手上没有，改了只是把"用错曲子"换成"用另一个没依据的曲子"。

### `0x209` 的分支语义：**角色列表/认证响应**，不是"创建角色完成"（2026-09-27 续）

接上节，进一步反汇编 `0x4591D8`-`0x459220` 看到 `0x209` 分支的**上级分支结构**：

```asm
0x4591D8  push 0xffff / 0x96(150) / 0x8c(140) / 0
0x4591E9  push 0x47d818          ; "请先建立至少一个角色才能进行游戏."
0x4591EE  jmp  0x4594eb          ; -> 弹提示

0x4591F3  push 0xffff / 0x96(150) / 0x8c(140) / 0
0x459204  push 0x47d7f8          ; "服务器认证已不可用,请重新登录."
0x459211  jmp  0x4594f3          ; -> 弹提示

0x459216  mov ecx, 0x8ab130      ; 音频管理器
0x45921B  call 0x45b3d0
0x459220  mov eax, [ebp+0x1140]  ; SelChr.wav
0x45922F  mov byte [ebp+0x930], 3; phase = 3
0x459240  call 0x45b900          ; 播放 SelChr.wav
```

**两个提示文本揭示了分支语义**：
| 地址 | 文本 |
|---|---|
| `0x47D818` | 「请先建立至少一个角色才能进行游戏.」 |
| `0x47D7F8` | 「服务器认证已不可用,请重新登录.」 |

即 `0x209` 分支的三条路是：**无角色 -> 提示建立角色**；**认证失效 -> 提示重新登录**；
**否则 -> 设 phase=3 + 播 SelChr.wav**。这是**角色列表/认证响应**的语义，
不是"创建角色完成"。

**因此我上一轮把 `SelChr.wav` 接在 `NewCharacterResult`（创建角色结果）上是不准的** ——
按证据应接在「登录后收到角色列表/认证成功」的响应上（我方对应
`SelectScene.SetCharacters()` 被调用、即从 LoginScene 拿到角色列表的那一刻），
而 `CreateChr.wav` 才是"创建角色"那一路（那一路的响应是另一个包，
证据里 `0x459AC5` 处理后进入 phase 1 的创建流程）。

**待办（本轮不做，避免在没完全确认前反复改接线点）**：
1. 确认我方「登录 -> 收到角色列表 -> 进 SelectScene」的确切调用点，
   把 `LegacySelChr` 从 `ShowNewCharacterResult` 移到那里。
2. 同时确认「创建角色成功」在我方对应的响应是什么 —— 证据里它**没有**播 SelChr，
   而是走 phase 1 的 pump 链，故不应在创建成功时播 SelChr（现在的位置会**多播一次**）。

## 选角屏消息分派全表 + 5 个圆钮只在 phase 2 可点（2026-09-27 定论）

反汇编 wndproc `0x459530` 的完整分派表（此前只知道它存在，不知道各分支）：

| 消息 | 处理函数 | 说明 |
|---|---|---|
| `0x100` WM_KEYDOWN | `0x459690` | 键盘 |
| `0x200` WM_MOUSEMOVE | `0x45A090` | 悬停 |
| **`0x201` WM_LBUTTONDOWN** | **`0x459840`** | **按钮/槽点击** |
| `0x202` WM_LBUTTONUP | `0x4599E0` | 抬起 |
| `3` WM_MOVE | `0x45A1F0` | |
| `2` WM_DESTROY | `0x403FD0` | |
| `0x7E8` | `0x451BB0` (ecx=0x8AB828) | |
| `0x7EE` | `0x45A140` | |

### `0x459840`（点击）的分支

```asm
0x459840  ... 取 lParam 的 x/y 存 +0x774/+0x778
0x45986A  call 0x418400              ; 窗口命中测试
0x459879  mov al, byte ptr [esi+0x930]  ; phase
0x459884  jne 0x459939               ; phase != 0 -> 0x459939
; --- phase 0：只对 **2 个角色槽** 做命中（基址 esi+0xCE0-0x28，stride 0x40）---
0x4598BF  cmp [esi+0x1168], edi      ; 已是该槽？
0x4598CC  call 0x458b20              ; 切槽
; --- phase != 0 ---
0x459939  cmp al, 2                  ; **只有 phase == 2 才继续**
0x45993B  jne 0x4599cc               ; 其他 phase -> 直接 return 0
0x459949  lea ebx, [esi+0x10e4]      ; phase 2 也能点角色槽（2 个）
0x459978  call 0x458b20              ; push 4, slot -> **phase=4 + 选中该槽**
0x459980  mov [esi+0x1488], edi      ; 记录选中槽
0x459995  call 0x4584c0              ; 刷新显示串
0x4599A3  lea edi, [esi+0xd38]       ; **5 个按钮数组**
0x4599A9  mov ebx, 5                 ; **循环 5 次**
0x4599C0  call dword ptr [edx+0xc]   ; **各按钮自己的 vtable[+0xC] = 点击处理**
0x4599C3  add edi, 0xb4              ; stride 0xB4
```

### 三个结论（修正此前的实现方向）

1. **5 个按钮（基址 `+0xD38`，stride `0xB4`）只在 phase 2 可见/可点**；其他 phase 点击
   一律直接返回。我此前把它们的动作"临时接/留空"是错的方向 —— 它们本来就**只属于
   phase 2**，phase 0/1/3/4 下根本不该响应。
2. **phase 2 也支持点角色槽**（`+0x10E4`，2 槽）→ `0x458B20(4, slot)` 即
   **phase=4 + 选中槽**，并把选中槽记到 `+0x1488`、刷新 `0x4584C0` 的显示串。
3. **每个按钮的语义在各自的 vtable `[+0xC]`** 里（`0x4599C0`），不在这个函数内。
   要定论 F92/F95/F98/✔/✘ 各是什么，必须找**每个按钮对象构造时赋的 vtable 指针**。

### 由此产生的待办

- 找到 5 个按钮对象的构造点（谁写的 `+0xD38`/`+0xDEC`/`+0xEA0`/`+0xF54`/`+0x1008`
  各槽的 vtable），逐个反汇编 `vtable[+0xC]`，得到 5 个按钮的真实语义。
- 复核我方实现：确认这 5 个按钮**只在 phase 2** 出现（phase 0 的按钮组应另行确认 ——
  `0x459840` 的 phase 0 路径只做槽命中，是否意味着 **phase 0 没有按钮**？
  需读完 `0x4598FA`-`0x459939` 才能定论）。

### 更正上节的猜测：**phase 0 有 4 个按钮**（2026-09-27 定论）

上节根据「`0x459840` 的 phase 0 路径只看到槽命中」推测 phase 0 可能没有按钮。
读完 `0x4598FA`-`0x459939` 后**该猜测被推翻**：

```asm
0x459907  lea edi, [esi+0x9e8]        ; **phase 0 的按钮数组基址 +0x9E8**
0x45990D  mov ebx, 4                  ; **4 个**
0x459924  call dword ptr [eax+0xc]    ; vtable[+0xC] 点击处理
0x459927  add edi, 0xb4               ; stride 0xB4
0x45992D  dec ebx
0x45992E  jne 0x459912
0x459930  ret 8
```

### 选角屏两组按钮（定论）

| phase | 数组基址 | 数量 | stride | 点击处理 |
|---|---|---|---|---|
| **0** | `+0x9E8` | **4** | `0xB4` | `vtable[+0xC]` |
| **2** | `+0xD38` | **5** | `0xB4` | `vtable[+0xC]` |

→ `+0x9E8`、`+0xA9C`、`+0xB50`、`+0xC04`（phase 0 的 4 个）
→ `+0xD38`、`+0xDEC`、`+0xEA0`、`+0xF54`、`+0x1008`（phase 2 的 5 个）

**我方 phase 0 显示 4 个按钮是对的**，无需删改；phase 2 显示 5 个也是对的。
两组按钮的**语义都**在各对象的 `vtable[+0xC]` 里，下一步分别反汇编。

**方法论记录**：这条"phase 0 可能没有按钮"的猜测来自**只读了一半函数**（读到
`0x459907` 之前就下了结论）。幸好当时按"证据不足先不改"处理，没有据此删掉正确代码
—— 这是本轮唯一但关键的收益。

## 找按钮 vtable 的扫描尝试：**工具不可信，结论作废**（2026-09-27）

目标：找 9 个按钮对象（phase 0 基址 `+0x9E8`，phase 2 基址 `+0xD38`，stride `0xB4`）
在构造时写入的 vtable 指针，以便反汇编各自的 `vtable[+0xC]` 得到点击语义。

尝试的两种字节模式扫描（对 `.text` 全段 0x74AE2 字节）：

| 模式 | 含义 | 结果 |
|---|---|---|
| (A) `C7 modrm disp32 imm32`，mod=2 | `mov dword [reg+disp32], imm32` | 0 命中 |
| (B) `8D modrm disp32`，mod=2 且非 SIB | `lea reg,[base+disp32]` | 0 命中 |

**但 (B) 是错误的**：直接读字节验证，`0x459907` 处是

```
0x459907  bytes: 8d be e8 09 00 00 bb 04 00 00 00 8b
          = lea edi, [esi+0x9E8]     ; modrm=0xBE, disp=0x9E8  <- 明确命中
0x4599A3  bytes: 8d be 38 0d 00 00 bb 05 00 00 00 8b
          = lea edi, [esi+0xD38]     ; modrm=0xBE, disp=0xD38  <- 明确命中
```

这两处**就是**我要找的模式，扫描器却报 0 命中；改用"任何 `8D` 后 1..5 字节内出现目标
disp"的暴力版，命中的唯一一条是 `0x42DC9C`：

```
0x42DC9C  lea edx, [esp+0xA9C]      ; *** 栈引用，误报 ***
```

**即暴力版漏掉了两处真阳性、捞出了一处与按钮无关的假阳性**（栈偏移巧合等于 `+0xA9C`）。
根因：暴力版**没有校验基址寄存器**，把 `[esp+0xA9C]` 也当成了 `[按钮对象+0xA9C]`；
而它为何漏掉 `0x459907` 尚未查明（字节明明匹配）—— 说明扫描逻辑还有未定位的缺陷。

**结论：本轮扫描结果全部作废，不得据此判断任何按钮语义。**
要拿到 vtable，必须用**独立于本扫描器**的方法，例如：
1. 直接在构造函数附近人工反汇编（按钮数组是在某个 ctor 里初始化的，
   找那个 ctor 比扫字节可靠）；
2. 或写一个**先用已知真阳性（0x459907 / 0x4599A3）自测通过**再跑的扫描器
   —— 这正是本仓库 `AGENTS.md` 里"验证工具不得与生产工具共用同一错误"要求的做法，
   本轮违反了它（写完扫描器没有拿已知样本自测就直接下结论）。

## 找到选角屏类的 vtable（2026-09-27 定论，替代上节失败的字节扫描）

上节字节扫描失败。改用**先自测再加搜索**的方法，成功：

1. **自测**：`find(b"\x8d\xbe\xe8\x09\x00\x00")` 必须返回 `0x58907`（= `0x459907`）——
   PASS。扫描器可信后才继续。
2. `push 0x459530`（`68 30 95 45 00`）：**0 命中**；`call 0x459530`：**0 命中**
   —— 说明 `0x459530` **不是**以字面量/直接 call 被引用的，而是**虚函数**。
3. 在 `.rdata` 搜 `0x459530` 的 4 字节小端：命中 **`0x476BD0`**。

### 选角屏类 vtable（`0x476BC0` 起）

```
0x476BC0 -> 0x4562F7
0x476BC4 -> 0x458F00
0x476BC8 -> 0x458F10
0x476BCC -> 0x465DD0
0x476BD0 -> 0x459530   <== wndproc（vtable 偏移 +0x10）
0x476BD4 -> 0x45A2B0
0x476BD8 -> 0x45A860
0x476BDC -> 0x45A9E0
0x476BE0 -> 0x45A9F0
0x476BE4 -> 0x401380
0x476BE8 -> 0x45AA00
0x476BEC -> 0x45AA00
--------------------------------
0x476BF8 -> 0x45AB60   <== 下一个 vtable 起始
0x476BFC -> 0x45A9E0   <== 与前一个的尾部**同形**
0x476C00 -> 0x45A9F0
0x476C04 -> 0x401380
0x476C08 -> 0x45AA00
0x476C0C -> 0x45AA00
```

### 由形状推出的结论

两个 vtable 的**尾部五个条目完全相同**（`0x45A9E0`/`0x45A9F0`/`0x401380`/
`0x45AA00`/`0x45AA00`）—— 这是典型的**基类子对象 + 派生类子对象**布局。
`0x476BF8` 开始的那一族（首条目 `0x45AB60`）就是**按钮类**，`0x45AB60` 是它
加进来的**新虚函数**（很可能是各按钮不同的那个 `vtable[+0xC]` 点击处理）。

### 下一步（已锁定具体地址）

1. 反汇编 `0x45AB60`——若它就是各按钮的点击处理入口族，立刻能拿到语义。
2. 反汇编 `0x45A9E0` / `0x45A9F0` / `0x45AA00` —— 基类公共虚函数。
3. 顺 `0x476BF8` 往**下**继续 dump，把按钮类族（可能不止一个 vtable）全部列出，
   再对应到 `+0x9E8`(phase0 ×4) 与 `+0xD38`(phase2 ×5) 共 9 个对象。

**方法论**：本轮的教训是上一轮的直接修正 —— 扫描器**先拿已知真阳性自测通过再用**，
并且改用"`call` 的 rel32 反算目标"而不是猜字节模式，两处都比上一轮可靠。

### 更正上节：「0x476BF8 是按钮类」的推断**错误**（2026-09-27）

上节据"两 vtable 尾部同形"推断 `0x476BF8` 族是按钮类。反汇编其首条目 `0x45AB60` 后，
**该推断被推翻**：

```asm
0x45AB60  push esi
0x45AB61  mov  esi, ecx
0x45AB63  call 0x45ab80          ; 真正的析构体
0x45AB68  test byte ptr [esp+8], 1
0x45AB6D  je   0x45ab78
0x45AB6F  push esi
0x45AB70  call 0x4680f8          ; 释放内存
0x45AB78  mov  eax, esi
0x45AB7B  ret  4
```

`0x45AB60` 是 **scalar deleting destructor（标量删除析构）**，不是点击处理。
而 `0x476C10` 之后 dword 已不再是 `.text` 指针，而是**浮点常量区**：

| 地址 | 值 | 常量 |
|---|---|---|
| `0x476C50` | `0x3E99999A` | 0.3 |
| `0x476C54` | `0x3F19999A` | 0.6 |
| `0x476C6C` | `0x358637BD` | 1e-6 |
| `0x476C70` | `0x40000000` | 2.0 |
| `0x476C74` | `0x3F847AE1` | 0.01 |
| `0x476C78` | `0x3727C5AC` | 1e-5 |

**结论：`0x476BF8` 只是"相邻的另一个类的 vtable"（含析构函数），之后紧跟常量数据，
与按钮类无关。** 上节把它认成按钮类是**过度解读"尾部同形"**这一条形状证据 ——
尾部同形只说明它们都有共同的基类子对象，**不能推出**其中一个必是按钮类。

### 按钮对象 vtable 的正确找法（下一步）

9 个按钮对象是选角屏对象的**内嵌成员**（`+0x9E8+0xB4*n`、`+0xD38+0xB4*n`），
其 vtable 由**构造函数写入**。正确做法是找**以 `esi+0x9E8`（或 `+0xD38`）为 this 的
ctor 调用**，即在 `lea reg,[base+disp]` 之后紧接 `call`，而不是：

- ~~扫 `mov [reg+disp32], imm32`~~（0 命中，且模式本身不适用）
- ~~据 vtable 尾部同形推类关系~~（已证伪）

下一轮实现该搜索，并**必须先以已知真阳性自测**（如 `0x459907` 的 `lea edi,[esi+0x9E8]`）
通过后再用。

## 找到 9 个按钮的构造点（2026-09-27 定论）+ 修好了那个扫描器 bug

### 先修 bug：disp32 偏移算错

前几轮扫描全部报 0 命中的**真正原因**：`lea reg,[base+disp32]` 的编码是

```
8D <modrm> <disp32>        ; ModRM 1 字节，无 SIB 时 disp32 紧接其后
   ^i+1      ^i+2
```

我把 disp32 读在 `i+3`（多算了一个字节，以为有 SIB）。**ModRM 的 rm!=4 时没有 SIB**，
所以 disp32 在 `i+2`。修正后：

- 自测（必须找到 `0x459907`/`0x4599A3`）→ **PASS**
- 一次找齐 **26 处** lea 命中，9 个偏移全覆盖

**这就是"先自测再下结论"救回来的**：两次静默失败后，第三次的自测直接指出问题。

### 9 个按钮的构造点：`0x456DBB`-`0x456EC2`

同一次连续构造（步长 0x1F），**全部调用同一个 ctor `0x417550`**：

| 地址 | this 偏移 | ctor | 归属 |
|---|---|---|---|
| `0x456DBB` | `+0x9E8` | `0x417550` | phase 0 第 1 个 |
| `0x456DDA` | `+0xA9C` | `0x417550` | phase 0 第 2 个 |
| `0x456DF9` | `+0xB50` | `0x417550` | phase 0 第 3 个 |
| `0x456E18` | `+0xC04` | `0x417550` | phase 0 第 4 个 |
| `0x456E3A` | `+0xD38` | `0x417550` | phase 2 第 1 个 |
| `0x456E5C` | `+0xDEC` | `0x417550` | phase 2 第 2 个 |
| `0x456E7E` | `+0xEA0` | `0x417550` | phase 2 第 3 个 |
| `0x456EA0` | `+0xF54` | `0x417550` | phase 2 第 4 个 |
| `0x456EC2` | `+0x1008` | `0x417550` | phase 2 第 5 个 |

**结论：9 个按钮是同一个类**（同一 ctor），差别在**构造参数**（几乎肯定是帧号/资源 id）。

另有 `0x456C66` / `0x456C83` 处对 `+0x9E8` / `+0xD38` 调用的是**另一个 ctor `0x4175F0`**
（在更早的位置），说明这两个成员被**构造过两次**，或有第二套对象 —— 待确认。

### 下一步（已锁定）

1. 反汇编 ctor `0x417550`，看它**取哪些参数**、存到对象的**哪些偏移**（帧号字段）。
2. 回到 `0x456DBB` / `0x456E3A` 看**每个调用点的实参**（push 的立即数）—— 那就是 9 个按钮
   各自的帧号，直接对应到贴图，语义即出。
3. 确认 `0x4175F0` 与 `0x417550` 的关系（是否基类/派生类）。

## 9 个按钮的构造实参（2026-09-27 定论）

读 `0x456DBB`-`0x456EC2` 九个构造调用点前的实参准备（cdecl，从右往左 push）：

| 按钮 | this | 参数（左→右还原） | 成对值 |
|---|---|---|---|
| p0-1 | `+0x9E8` | `ebx, 0x33, 0x33, 0x1B8, 0x5D, 0, 1, 0x34, 1` | `0x33`,`0x33` |
| p0-2 | `+0xA9C` | `ebx, 0x35, 0x35, 0x4F, 0xF3, 0, 1, 0x36, 1` | `0x35`,`0x35` |
| p0-3 | `+0xB50` | `ebx, 0x37, 0x37, 0x103, 0x31, 0, 1, 0x38, 1` | `0x37`,`0x37` |
| p0-4 | `+0xC04` | `ebx, 0x39, 0x39, 0x1C, 0x1B6, 0, 1, 0x3A, 1` | `0x39`,`0x39` |
| p2-1 | `+0xD38` | `ebx, 0x5C, 0x5D, 0x10A, 0x1A3, 0, 1, 0x5B, 1` | `0x5C`,`0x5D` |
| p2-2 | `+0xDEC` | `ebx, 0x5F, 0x60, 0x134, 0x1A3, 0, 1, 0x5E, 1` | `0x5F`,`0x60` |
| p2-3 | `+0xEA0` | `ebx, 0x62, 0x62, 0x160, 0x1A3, 0, 1, 0x61, 1` | `0x62`,`0x62` |
| p2-4 | `+0xF54` | `ebx, 0x56, 0x57, 0x1C2, 0x1BC, 0, 1, 0x55, 1` | `0x56`,`0x57` |
| p2-5 | `+0x1008` | `ebx, 0x59, 0x5A, 0x1EB, 0x1BC, 0, 1, 0x58, 1` | `0x59`,`0x5A` |

### 观察到的不对称（**必须靠看贴图定论，不得臆断**）

- **p2 组**：成对值**不同**（`0x5C/0x5D`、`0x5F/0x60`、`0x56/0x57`、`0x59/0x5A`）
  —— 形态像 **(normal, hover)** 两帧。
- **p0 组**：成对值**相同**（`0x33/0x33`、`0x35/0x35`、`0x37/0x37`、`0x39/0x39`）
  —— 更像 **(x, y)** 坐标而非两帧。
- 另有 `0x1B8`/`0xF3`/`0x31`/`0x1B6`（p0）与 `0x1A3`/`0x1BC`（p2）等较大的值
  在两个位置出现，可能是文本 id 或帧组基址。
- 每个调用点末尾都有一个 **`push ebx`**（`ebx` 在此函数前段被设置，非立即数）—— 需回溯
  `ebx` 的赋值才知道它是什么。

**下一轮**：用素材编辑器打开 `Interface1c.wil`，直接看 `0x33`/`0x35`/`0x37`/`0x39`/
`0x5C`/`0x5D`/`0x56`/`0x57`/`0x59`/`0x5A`/`0x5F`/`0x60`/`0x62` 这些帧长什么样 ——
按钮语义将**直接可见**，无需再猜参数含义。

## 9 个按钮的语义（2026-09-27 用 OCR 定论）

### 方法：解码 + 放大拼图 + OCR（含边界框）

`dim modality list` 显示本机只有 `ocr.recognize` 可用（无 vision 模型），正好够用 ——
按钮上**有文字**。

1. 解码 `Interface1c.wil`（`count=2000`）中 22 个候选帧（`0x33`-`0x3A`、`0x55`-`0x62`）
2. 拼成 5 列标注图（scale=4），每格左上角画"十进制 / 十六进制"帧号
3. `dim ocr recognize` 读出文字 + 边界框

### 尺寸先揭示分组

| 帧 | 尺寸 | 组 |
|---|---|---|
| `0x33`-`0x36` | 96×26 | p0-1 / p0-2 |
| `0x37`-`0x38` | 96×24 | p0-3 |
| `0x39`-`0x3A` | 48×26 | p0-4 |
| `0x55`-`0x5A` | **28×28（正方形）** | p2-4 / p2-5 ← 圆钮 |
| `0x5B`-`0x62` | 40×38 | p2-1 / p2-2 / p2-3 |

### OCR 结果（第一行 5 格 ↔ 5 段文字一一对应）

第一行 = 帧 `0x33`/`0x34`/`0x35`/`0x36`/`0x37`（边界框 x 各占一格 392px），
OCR 读出的文字序列是：

```
创建角色  创建角色  删除角色  删除角色  开始游戏
```

（OCR 的识别噪声：「州除角色」「刪除角色」= 删除角色）

### 由此定论的语义

| 按钮 | 帧 normal / hover | 尺寸 | **语义** |
|---|---|---|---|
| p0-1 | `0x33` / `0x34` | 96×26 | **创建角色** |
| p0-2 | `0x35` / `0x36` | 96×26 | **删除角色** |
| p0-3 | `0x37` / `0x38` | 96×24 | **开始游戏** |
| p0-4 | `0x39` / `0x3A` | 48×26 | 待定（第 5 段文字"开始游戏"归此或归 p0-3，需逐帧 OCR 确认） |
| p2-4 | `0x56` / `0x57` | 28×28 | 待定（OCR 另读出"结束"） |
| p2-5 | `0x59` / `0x5A` | 28×28 | 待定 |
| p2-1/2/3 | `0x5C`/`0x5D`、`0x5F`/`0x60`、`0x62` | 40×38 | 待定 |

### 对 phase 0 的实质结论（可直接改代码）

**phase 0 的 4 个按钮是：创建角色 / 删除角色 / 开始游戏 / + 1 个 48×26 的**。
这与我方实现的按钮集合**是否一致，必须逐项核对**（我方 phase 0 目前是 4 个按钮，
但文字/贴图是否就是这个顺序与尺寸，尚未验证）。

### 下一步

1. **逐帧 OCR**（一次一格）替代拼图 OCR，消除"5 段文字对应哪 5 格"的歧义，
   并把 `0x39`/`0x3A` 与 `0x56`/`0x57`(`结束`?)/`0x59`/`0x5A` 定死。
2. 核对 `Interface1c.wil` 是否与**我方**使用的资源一一对应（我方 Zircon 的
   `Interface1c.Zl` / `Interface1c-Extended.Zl`）—— 帧号体系可能不同。

## 定论：phase 0 的 4 个按钮 = 创建角色/删除角色/开始游戏/结束（2026-09-27）

上节拼图 OCR 有"哪段文字属于哪格"的歧义。改用**单列拼图**（每行一帧 + 大号 FRAME 标号）：
OCR 按阅读顺序返回文字，**每段文字的 y 落点唯一确定它属于哪一帧**，歧义消除。

22 帧 × 230px 行高，OCR 结果与帧的对应：

| 帧 | OCR 文字（含识别噪声） | **定论** |
|---|---|---|
| `0x33` (51) | 创建育色 | **创建角色** mundane |
| `0x34` (52) | 创建甯色 | **创建角色** hover |
| `0x35` (53) | 劑除角色 | **删除角色** normal |
| `0x36` (54) | 劑粉角色 | **删除角色** hover |
| `0x37` (55) | 开始游戏 | **开始游戏** normal |
| `0x38` (56) | 开脂游戏 | **开始游戏** hover |
| `0x39` (57) | 结束 | **结束** normal |
| `0x3A` (58) | 结束 | **结束** hover |
| `0x55`-`0x62` | **无任何文字** | 纯图形帧 |

### 结论（覆盖此前的暂定表）

| 按钮 | 帧 normal / hover | 尺寸 | **语义（定论）** |
|---|---|---|---|
| p0-1 (`+0x9E8`) | `0x33` / `0x34` | 96×26 | **创建角色** |
| p0-2 (`+0xA9C`) | `0x35` / `0x36` | 96×26 | **删除角色** |
| p0-3 (`+0xB50`) | `0x37` / `0x38` | 96×24 | **开始游戏** |
| p0-4 (`+0xC04`) | `0x39` / `0x3A` | 48×26 | **结束** |

**phase 0 = 「创建角色 / 删除角色 / 开始游戏 / 结束」四个按钮。**

phase 2 的 5 个按钮（`0x56`/`0x57`、`0x58`/`0x59`、`0x5A`、`0x5B`/`0x5C`、`0x5D`/`0x5E`、
`0x5F`/`0x60`、`0x61`/`0x62` 之中）**全为图形帧**（无文字），所以其语义**不能靠 OCR 得到**，
必须靠 vtable 反汇编或图形识别（圆钮上的箭头/图标形态）。

### 下一步（转向代码）

1. 拿"创建角色/删除角色/开始游戏/结束"4 个语义**核对并修正我方 phase 0 的按钮**
   （文字、贴图帧号、尺寸、顺序、点击行为）—— 这是 8 轮证据工作后第一个可直接落地的修正点。
2. 核对帧号体系：EI 的 `Interface1c.wil` 帧号 vs 我方 `Interface1c.Zl`/`Interface1c-Extended.Zl`
   是否同序（若不同序，必须换算，否则贴图会串）。

## 9 个按钮的坐标与 hover 帧（2026-09-27 定论，含对上一提交的更正）

把 ctor 实参按实际布局展开：`(ebx, f1, f2, X, Y, 0, 1, hover, 1)`

| 按钮 | f1 | f2 | X,Y | hover | 我方代码坐标 | 核对 |
|---|---|---|---|---|---|---|
| p0-1 创建角色 | `0x33` | `0x33` | 440,93 | `0x34` | (440,93) | ✅ |
| p0-2 删除角色 | `0x35` | `0x35` | 79,243 | `0x36` | (79,243) | ✅ |
| p0-3 开始游戏 | `0x37` | `0x37` | 259,49 | `0x38` | (259,49) | ✅ |
| p0-4 结束 | `0x39` | `0x39` | 28,438 | `0x3A` | (28,438) | ✅ |
| p2-1 | `0x5C` | `0x5D` | 266,419 | `0x5B` | 武器(266,419) | ✅ |
| p2-2 | `0x5F` | `0x60` | 308,419 | `0x5E` | 人脸(308,419) | ✅ |
| p2-3 | `0x62` | `0x62` | 352,419 | `0x61` | 卷轴(352,419) | ✅ |
| p2-4 | `0x56` | `0x57` | 450,444 | `0x55` | ✔(450,444) | ✅ |
| p2-5 | `0x59` | `0x5A` | 491,444 | `0x58` | ✘(491,444) | ✅ |

**9 个坐标全部与实参一致** —— 无需改动。

### 更正：hover 帧的方向两组相反

上一提交把 phase 2 的 hover 写成 `frame+1`，**错了**。实参显示：

- **phase 0**：hover = normal **+1**（`0x33`→`0x34`，`0x37`→`0x38`…）
- **phase 2**：hover = normal **−1**（`0x56`→`0x55`，`0x5C`→`0x5B`…）

两组来自**同一个 ctor**，但传值方向相反，**不能统一处理**。本提交已把 phase 2 改为
`HoverIndex = frame - 1`。

**方法论教训**：上一提交我从 `(0x5C, 0x5D)` 这对值**推测** hover = +1，而没有读第 8 个实参
—— 又是一次"从形状推测代替读证据"。这次核对坐标时才把第 8 个实参读出来，发现推错了。

## phase 2 渲染验证：**未完成**（2026-09-27，如实记录）

为了截图核对 phase 2 的 5 个图形钮，加了验证用开关 `--legacy-phase2`（直接进 phase 2，
不必真走创建流程）。运行结果：日志确认 `phase=2`，截图产出正常（611791 字节，
与 phase 0 的 608383 不同）。

**但渲染无法被验证**，两条路都断了：

1. **OCR 不可用**：这 5 个是**纯图形帧（无文字）**，OCR 只读到标题栏，
   `RTAINMENTT （C） 2002`（开场 logo）。
2. **像素取样不可用**：本想检查 5 个按钮所在矩形"是否有内容"，用
   `颜色数 > 4` 作判据 —— 结果**空白对照点也报"有内容"**（颜色数 58 / 35），
   因为背景是洞窟贴图 F50，本身非均匀。**该判据无效。**

另外整图差分也被污染：开场 WEMADE logo **仍在播放/动画**，差分 bbox
`(185,148)-(1055,873)` 主要来自 logo 而不是按钮。

**结论：phase 2 的 hover 帧改动（`frame-1`）只做过"参数与实参一致"的静态核对，
没有做过"渲染正确/贴图不串"的动态验证。** 这个缺口必须补，方法见下。

### 补验证的正确方法（下一步）

- 在**纯色背景**下渲染 phase 2（临时把 F50 背景隐藏或换成单色），
  再对 5 个矩形做"与背景色差异"判定 —— 这样 `颜色数`/`亮度` 才有判别力。
- 或**成对截图**（normal 态 vs hover 态，用 `--legacy-phase2` + 一个模拟鼠标悬停的开关），
  比较同一矩形在两种状态下的像素差异 —— 有差异即证明 hover 生效。
- **禁用开场 logo 覆盖层**再截图，避免动画干扰差分。

### 同时发现的方法论问题

本次"Ívor取样判据"的 bug（`颜色数>4` 无判别力）是**因为加了空白对照点才暴露的**——
这正说明**对照样本**必须在验证脚本里，和"扫描器先自测"是同一个道理。

## phase 2 渲染验证：坐标映射未确定，**所有取样结论无效**（2026-09-27 续）

### 先得到一个干净的事实：画面是静态的

`--legacy-phase2` **同状态连拍两张** → 差异 bbox = `None`，显著差异像素 **0**。
**所以画面完全静态，不存在"动画污染差分"** —— 上一节把 phase0↔phase2 的差分
归因于"开场 logo 动画"是**错的**（那句 logo 文字是 F50 贴图烘死的，静态）。

### 但随后发现坐标映射是错的

用 `scale=1.7135416, offset=(136.66669, 0)` 把游戏坐标映射到截图像素，
对 phase0 截图与 phase2 截图做矩形差分，结果**与预期相反且自相矛盾**：

| 区域 | 预期 | 实测 |
|---|---|---|
| phase2 的 5 个钮 | 应出现（差异大） | **0 差异像素（未变）** |
| phase0 的 4 个钮 | 应消失（差异大） | **0 差异像素（未变）** |
| 对照区 1 | 应完全未变 | **157 差异像素（变了）** |

**根因**：截图为 **2028×1380**，而 `640×480 × 1.7135416 = 1096×823` —— **对不上**。
截图是**整个窗口**（含标题栏/边框/可能的 Retina 2x），游戏视口只是其中一块，
我的映射公式**没有算视口原点**。

**因此：本日两份"矩形取样"结论全部无效**，包括上一节那个"空白对照也报有内容"。
同样地，"phase2 的 5 个钮差异为 0"这一条**不能**用来断定"按钮没渲染"——
它可能只是映射错位。

### 待解决（下一步的第一件事）

1. **确定截图里游戏视口的真实原点与缩放**：用 `--legacy-open=<win>` 或自检打印
   （如 `[UiScaler] scale=... offset=(...)` 日志已有 `scale=1.7135416 offset=(136.66669, 0)`，
   但那对应的是 **2028×1380 的哪一层**需要确认；2028/1.7135 ≈ 1183，1380/1.7135 ≈ 805，
   **与 640×480 仍不符**）。
2. 映射确定后，再重做 9 个按钮的矩形差分验证。
3. 在此之前，**phase 2 的渲染仍属未验证**。

### 方法论

本日连续三次"验证方法本身有缺陷"（颜色数判据无判别力 / 坐标映射错 / 归因错），
都是**先有结论、再找证据**的产物。正确顺序应是：**先让验证工具在一个已知答案的
样本上产生正确答案**（对照点必须落在"确定会变/确定不变"的位置），再用于未知。

## phase 2 验证：改用属性自检，**PASS**（2026-09-27 定论，替代三次失败的像素法）

像素差分受窗口缩放/视口原点干扰，本日三次失败。改用项目既有的
`[LegacyXxxSelfTest] PASS/FAIL` 机制（断言**控件属性**，不猜像素）：

```
[LegacySelectButtonSelfTest] PASS 9 个按钮的 Index/HoverIndex/Location/Size 全部匹配 EI ctor 实参
```

触发：`--legacy-select-selftest`。期望值取自 `0x456DBB`-`0x456EC2` 的 ctor 实参。

### 自检当场抓到一处不一致 —— 而且**错的是自检表**

首次运行 FAIL：

```
p2-4: got size=(28,28) | want size=(40,38)
p2-5: got size=(28,28) | want size=(40,38)
```

**代码是对的，我的期望值写错了。** 早先解码帧就测到：

| 帧 | 尺寸 |
|---|---|
| `0x55`-`0x5A`（含 p2-4 `0x56` / p2-5 `0x59`） | **28×28（圆钮）** |
| `0x5B`-`0x62`（含 p2-1 `0x5C` / p2-2 `0x5F` / p2-3 `0x62`） | **40×38** |

**尺寸随帧走，不是统一值。** 修正自检表后 PASS。

（附带收获：p2-4/p2-5 是 28×28 的**圆钮**，与它们被我命名为 ✔/✘ 相符；
p2-1/2/3 是 40×38 的矩形钮。）

### 与前几次的对比

| 方法 | 结果 |
|---|---|
| OCR（phase 0，有文字） | ✅ 有效 —— 读出「开始游戏/创建角色/删除角色/结束」 |
| 矩形像素取样 | ❌ 判据无判别力 + 坐标映射错 |
| 整图差分 | ❌ 归因错 |
| **属性自检** | ✅ **有效且可复现** —— 输出确定的 PASS/FAIL |

**结论：涉及"控件该有什么属性"的验证，用自检；只有"文字/图形长什么样"才需要 OCR/截图。**

## 9 个按钮共用同一个 ctor → 共用同一个点击处理（2026-09-27 定论）

反汇编共同 ctor `0x417550`：

```asm
0x417550  mov edx, [esp+0xc]
0x417561  mov [esi+0x1c], edx      ; 存参数
0x417568  mov [esi+0x24], al       ; 存参数(byte)
0x41757B  mov [esi+0x14], ecx
0x41757E  mov [esi+0x18], eax
0x417581  mov [esi+0x30], edx
0x417584  call 0x466130            ; 帧调度（已知函数）
0x4175B4  mov [esi+0x2c], edi      ; **+0x2C = X**
0x4175BD  mov [esi+0x28], ebx      ; **+0x28 = Y**
0x4175CD  lea edx, [esi+0x34]      ; 字符串拷贝到 +0x34
0x4175E5  ret 0x24                 ; **9 个参数（0x24/4 = 9）**
```

### 关键结论

1. **`ret 0x24`** 独立印证了「9 个参数」—— 与我从调用点数出的实参个数一致。
2. **这个 ctor 里没有写 vtable。** 所以 vtable 由**基类 ctor**（在调用本 ctor 之前执行）
   写入；9 个按钮**共用同一个 ctor ⇒ 共用同一个 vtable ⇒ 共用同一个点击处理函数**。
3. **因此按钮的差别只能来自构造参数**，存在对象的 `+0x14` / `+0x18` / `+0x1C` /
   `+0x20` / `+0x24`(byte) / `+0x30` 这些字段里。加上 `+0x28 = Y`、`+0x2C = X`
   （坐标）与 `+0x34` 起的字符串。
4. `0x417584` 调的 `0x466130` 是**帧调度**（此前已查明 `0x466130` 是帧调度器），
   即 ctor 里就把帧号交给调度器注册了。

### 这对"找 vtable"意味着什么

**不必再找 9 个 vtable** —— 只有一个。要拿语义，应该：

1. 找**那个唯一的点击处理**（基类 vtable 的 `[+0xC]`），看它**读对象的哪个字段**做分派；
2. 回到 9 个调用点，把**各参数值与字段的对应**列出来（哪些是命令 id、哪些是帧号）。

**当前已从实参见到的候选语义载体**：第 1 个实参 `ebx`（在 ctor 里被存为 `+0x28`=Y ——
**注意**：这说明 `ebx` 不是命令 id 而是 Y 坐标，此前"末尾 push ebx"的猜测得到澄清）。

### 下一步

读基类 ctor（`0x4175F0`，`0x456C66`/`0x456C83` 调用它的地方附近）以定位 vtable 写入，
再反汇编该 vtable 的 `[+0xC]` 槽位 = 9 个按钮共用的点击处理。

### 更正：两个 ctor 都**不写 vtable**，指针来自动态函数指针 0x4762B0（2026-09-27）

上一节推测"vtable 由基类 ctor 写入"。反汇编 `0x4175F0` 后**该推测被推翻**：

```asm
0x4175F0  push esi / mov esi, ecx
0x4175F6  mov [esi+0x24], 1      ; 默认 1
0x4175F9  mov [esi+0x25], 1      ; 默认 1
0x417601  mov [esi+0x14], 0      ; 参数区清零
0x417607  mov [esi+0x1c], 0
0x41760A  mov [esi+0x20], 0
0x41760D  mov [esi+0x30], 0
0x417611  lea eax, [esi+4]       ; this 加 4
0x417615  call dword ptr [0x4762b0]   ; **交给动态函数指针**
0x417622  lea edi, [esi+0x34]
0x417625  rep stosd [edi], 0     ; 清 0x80 字节（字符串区）
0x417629  ret
```

**`0x4175F0` 与 `0x417550` 都不含任何 vtable 写入。** 所以 `0x4599C0` 处
`mov edx, [edi]; call [edx+0xC]` 读到的那个指针，是由 **`0x4762B0` 指向的函数**
（以「this 加 4」为参数调用）设置的 —— 那是一个**运行期动态函数指针**，不是静态 vtable。

`0x4762B0` / `0x4762B4` 这对指针此前已出现在命中测试里（`0x45988A`/`0x459941` 读
`0x4762B4` 作按钮命中测试函数）。**它们是运行期填充的函数指针**，需要查明其真值。

### 下一步（改向）

1. 读 `.data` 里 `0x4762B0` / `0x4762B4` 的**初始值**（若为 0 则是运行期填充），
   以及**谁写它们**（x86 下通常是 `GetProcAddress`/导入表初始化）。
2. 若 `[0x4762B0]` 每次调用相同函数，直接反汇编它 —— 它会揭示按钮对象
   `+4` 起的子对象结构与函数指针表。
3. 备选：在运行期 dump `[esi+0xD38]` 的值（需调试器；当前工具链只有静态反汇编，故优先做 1）。

### `0x4762B0` / `0x4762B4` 是加载时重定位项，静态无法解析（2026-09-27）

直接读 `.data` 里这两个指针的初值：

| 地址 | 初值 |
|---|---|
| `0x4762B0` | `0x000793D8` |
| `0x4762B4` | `0x000793E2` |

**这两个值都不是本模块的代码地址**：`.text` 范围是 `0x401000`-`0x476AE2`，
而 `0x793D8` 连映像基址 `0x400000` 都不到。对 `0x793D8` 反汇编返回空。

结论：它们是**加载时重定位（relocation）项**，文件里存的是**未重定位值**；
真正的目标地址只有在 PE 加载器按基址修正后才成立。**当前的静态反汇编工具链
无法解析这两个指针指向的函数。**

### 对"拿 phase 2 按钮语义"的影响

原计划是"读 `[0x4762B0]` 找到按钮对象的函数指针表 → 反汇编 `[+0xC]`"。
**这条路在静态分析下走不通。** 可选的三条替代：

1. **解析 PE 的 `.reloc` 节** —— 若 `0x4762B0` 处有重定位项，能算出它在
   哪个基址下的目标；但更可能这里指向的是**运行期由代码填充**的、而非重定位的，
   需先用 `.reloc` 是否覆盖该地址来区分这两种情况。
2. **运行期 dump** —— 用 gdb/调试器在按钮 ctor 返回后读 `[esi+0xD38]`，
   直接拿到真实的函数指针（最可靠，但需要能运行 82 机器上的 Mir3.exe）。
3. **换证据源（推荐先做）** —— 不再追 vtable，改从**两条已有线索**推语义：
   - 证据里 `0x459D29`（`+0x1008`，即 p2-5）附近的写入者与 msgid；
   - `0x459D48` 已知会 `F89 -> phase 3` 并发 msgid `0x64 '%s/%d'`。
   把 5 个按钮各自的**后续 msgid / 状态写入**列出来，语义可从"点了之后发什么包"反推。

## phase 2 按钮语义：**命中测试级联**，语义在每格的代码体里（2026-09-27 定论）

放弃解析那个静态不可解的动态指针后，发现**语义根本不需要它** —— 点击处理是一个
**按钮命中测试级联**，每个按钮"命中成功后"的代码体就是它的动作：

```asm
; 第 1 格：p2-5 (+0x1008, 帧 0x59=89)
0x459D23  mov edx, [esi+0x1008]
0x459D29  lea ecx, [esi+0x1008]
0x459D37  call dword ptr [edx+0x10]     ; **[首dword+0x10] = 命中测试**
0x459D3C  je 0x459D8D                   ; 未命中 -> 下一格
0x459D43  call 0x45b3d0                 ; 音频（0x8ab130 音频管理器）
0x459D48  mov byte [esi+0x930], 3       ; **phase = 3**
0x459D5F  call [0x4762b8]               ; 另一个动态函数
0x459D7A  call 0x451f90                 ; ecx=0x8ab828 网络 -> **发消息**

; 第 2 格：p2-1 (+0xD38, 帧 0x5C=92)
0x459DB0  lea eax, [esi+0x10bc]         ; 角色槽 0
0x459DBF  call 0x458440
0x459DC8  call 0x458b20  (push 4, 0)    ; **phase=4 + 选中槽 0**
0x459DD5  lea ecx, [esi+0x10fc]         ; 角色槽 1
0x459DE0  call 0x458440
0x459DEB  call 0x458b20  (push 4, 1)    ; **phase=4 + 选中槽 1**
0x459DF0  mov eax, [esi+0x1488] / cmp -1
0x459E06  call 0x4584c0                 ; 刷新显示串

; 第 3 格：p2-2 (+0xDEC, 帧 0x5F=95)
0x459E1F  mov eax, [esi+0xdec]
0x459E33  call [eax+0x10]               ; 命中测试
0x459E3E  lea eax, [esi+0x10bc] / call 0x458440
0x459E56  call 0x458b20  (push 4, 0)    ; **phase=4 + 选中槽 0**
```

### 由此得到的语义与**我方错误**

| 按钮 | 帧 | 我方当前 | **EI 实际** |
|---|---|---|---|
| p2-5 `+0x1008` | 89 | `_skinConfirmNo` → `SetSelectPhase(3)` | **phase=3 + 音频 + 发消息** ✅ 方向对，缺音频/消息 |
| p2-1 `+0xD38` | **92** | **`_skinIconWeapon`（"武器图标"）动作空** | **选中角色槽 + phase=4** ❌ **命名与动作都错** |
| p2-2 `+0xDEC` | **95** | **`_skinIconFace`（"人脸图标"）动作空** | **选中角色槽 + phase=4** ❌ **同样错** |

**这可能是我方对 phase 2 最实质的偏差**：我此前把帧 92/95/98 当成"武器/人脸/卷轴"三个
图标钮（并按此命名、把动作留空），但它们实际是**角色选择/确认**类按钮。

**p2-3 `+0xEA0`（帧 98）与 p2-4 `+0xF54`（帧 86）的动作尚未读出**，需继续读级联的后续格。

### 下一步

1. 读完级联（`0x459E5B` 之后）拿 p2-3 / p2-4 的动作。
2. 依证据**改我方的命名与动作**：帧 92/95 → 选槽+phase 4；帧 89 → phase 3 + 音频 + 消息。
3. 用 `--legacy-select-selftest` 扩展断言（把"动作"纳入自检）后截图验证。

### 更正上一节的过度解读：三格的动作是**对两个槽都调用**（2026-09-27）

上一节我把 p2-1 描述为"选中角色槽 + phase 4"。继续读级联后**该描述不准确** ——
p2-1 的代码体对**两个槽都**调用了：

```asm
0x459DB0  lea eax, [esi+0x10bc]        ; 槽 0
0x459DBF  call 0x458440
0x459DC8  call 0x458b20  (push 4, 0)
0x459DD5  lea ecx, [esi+0x10fc]        ; 槽 1
0x459DE0  call 0x458440
0x459DEB  call 0x458b20  (push 4, 1)
0x459DF0  mov eax, [esi+0x1488] / cmp -1
0x459E06  call 0x4584c0
```

**"选中某一个槽"是我加上去的、证据里没有的语义。** `0x458440` 与 `0x458B20(4, n)` 对
两个槽各执行一次，更像是"**刷新两个槽的状态并把 phase 设为 4**"，而不是"选槽"。

### 级联全貌（截至目前，只记观察到的事实）

| 顺序 | 按钮偏移 | 帧 | 观察到调用序列 |
|---|---|---|---|
| 1 | `+0x1008` | 89 | phase=3；音频 `0x45B3D0`；`[0x4762B8]`；网络 `0x451F90` |
| 2 | `+0xD38` | 92 | `0x458440(+0x10BC)`；`0x458B20(4,0)`；`0x458440(+0x10FC)`；`0x458B20(4,1)`；读 `+0x1488` 与 -1 比较；`0x4584C0` |
| 3 | `+0xDEC` | 95 | 与第 2 格同形 |
| 4 | `+0xEA0` | 98 | 与第 2 格同形 |
| 5 | `+0xF54` | 86 | **尚未读到** |

**结论（保守）**：帧 92/95/98 三个按钮的动作**彼此相同**（刷新两个槽 + `0x458B20(4,n)`），
帧 89 是"进 phase 3 + 音频 + 发消息"。**它们具体"是干什么用的"仍不能从这段代码断言** ——
需要知道 `0x458440` / `0x458B20` / `0x4584C0` 各自做什么。

**因此我方把帧 92/95/98 命名成"武器/人脸/卷轴"仍是**未证伪也未证实的猜测**——
但它们与帧 89 的动作**明显不同类**，这一点是确定的。

### 下一步

1. 读第 5 格（`+0xF54`，帧 86）与级联之后的收尾。
2. 反汇编 `0x458440`、`0x458B20`、`0x4584C0` 三个被调函数 —— 它们是这三格动作的
   **全部语义来源**，比继续猜按钮名有用。

## 两个被调函数的角色查清（2026-09-27 定论）

### `0x458440` = 初始化/重设一个角色槽记录

```asm
0x458457  mov [esi+5], al       ; 参数字节
0x45845F  mov dword [esi], 1    ; 「有效」标记 = 1
0x458466  mov [esi+4], dl       ; 参数字节
0x458469  mov [esi+6], bl       ; 参数字节
0x45846C  call 0x458910         ; 生成显示串
0x45847F  mov [esi+0x18], eax   ; 尺寸
0x458482  mov [esi+0x1c], ecx
0x458489  mov [esi+7 ..], 0     ; 清字符串区
```

`+4` / `+5` 正是 `0x458EC0` 帧号公式读的那两个字段（class / gender），
所以 `0x458440(slot, class, gender, flags, str)` = **按参数设置一个槽的 class/gender 并生成显示串**。

### `0x458B20` = 对指定槽重算帧号，**按 phase 选择槽基址**

```asm
0x458B20  mov eax, [esp+4]           ; slot 索引
0x458B25  cmp eax, 2 / jg 退出        ; slot <= 2
0x458B2F  cmp bl, 5 / jae 退出        ; 第二参 < 5
0x458B34  mov dl, byte [ecx+0x930]   ; **phase**
0x458B3C  je 0x458b54                ; phase == 0 -> 基址 +0xCB8
0x458B41  je 0x458b54                ; phase == 3 -> 基址 +0xCB8
0x458B46  jne 0x458ba6               ; 其他（非 2）-> 退出
0x458B48  shl eax, 6                 ; slot * 0x40
0x458B4B  lea esi, [eax+ecx+0x10bc]  ; **phase == 2 -> 基址 +0x10BC**
0x458B5E  test esi / cmp [esi],0 / je 退出   ; 槽无效则退出
0x458B67  mov al, byte [esi+4]       ; class
0x458B6A  mov dl, byte [esi+5]       ; gender
0x458B70  call 0x458EC0              ; **帧号公式（此前已查明）**
0x458B77  mov [esi+0x3c], eax        ; 存帧号
```

**这也独立印证了先前结论**：槽基址在 **phase 2 是 `+0x10BC`**、**phase 0/3 是 `+0xCB8`**
（`slot * 0x40`），且 `0x458EC0` 的帧号公式用 `+4`/`+5` 两个字段。

### 一个**未解决的矛盾**（不下结论，如实记录）

`0x459DB0`（p2-1 帧 92 那一格）调用 `0x458440` 时，**push 的参数是 `0`**（见
`0x459DB0` 附近：`push 0 / push 0 / lea eax,[+0x10BC] / push 0 / push eax`）。
而 `0x458440` 会把槽的「有效」标记**置 1** 并**清空字符串区** —— 这更像
**"清空/重置槽"或"删除角色"**，而不是"设置职业"。

**所以帧 92/95/98 三钮能确定的是**：它们会**重置两个槽并把 phase 设 4**；
**不能**确定它们"是职业选择"还是"清空/删除角色"。我此前"武器/人脸/卷轴"的命名
**既不证实也不证伪**，但**两组按钮（帧 89 vs 帧 92/95/98）动作明显不同类**是确定的。

### 下一步

1. 读第 5 格（`+0xF54`，帧 86）的动作 —— 它还没读过。
2. 读 `0x458910`（生成显示串）与 `0x4584C0`（已知）的差异，确认"清空"与"设置"的判据。
3. 若仍不能定名，则**保持现状并在代码注释里标注"语义未定"**，而不是按猜测改名。

## 级联第 5 格（`+0xF54`，帧 86）：发 msgid 0x104（2026-09-27 定论）

```asm
0x459F37  mov eax, [esi+0xf54]
0x459F3D  lea ecx, [esi+0xf54]
0x459F50  je 0x45A079                ; 命中测试未通过 -> 跳到收尾
0x459F79  push 0x104                 ; **msgid 0x104**
0x459F98  push 0x8b187c              ; 字符串缓冲区（运行期填充）
0x459FC3  push 0xffff / 0x96(150) / 0x8c(140)   ; 消息框坐标 (140,150)
```

### 结论

**帧 86（`+0xF54`）的动作是「发 msgid `0x104` + 弹消息框」**，不是"开始游戏"。

对照我方：

| | 我方当前 | EI |
|---|---|---|
| 帧 86 (`_skinConfirmYes`) | `OnStartPressed()` —— **直接开始游戏** | **发 msgid `0x104` + 弹框 (140,150)** ❌ |

**注意坐标 `(140,150)`** —— 这正是先前两处**错误提示**（`0x47D818` "请先建立至少一个角色
才能进行游戏." / `0x47D7F8` "服务器认证已不可用,请重新登录."）所用的同一坐标，
提示 `0x8B187C` 很可能也是**错误/警告类消息**。

### phase 2 五格级联全貌（定论）

| 顺序 | 按钮 | 帧 | 动作 |
|---|---|---|---|
| 1 | `+0x1008` | 89 | `phase=3`；音频 `0x45B3D0`；`[0x4762B8]`；网络 `0x451F90` |
| 2 | `+0xD38` | 92 | `0x458440(+0x10BC, 0)`；`0x458B20(4,0)`；`0x458440(+0x10FC, 0)`；`0x458B20(4,1)`；读 `+0x1488` 比 -1；`0x4584C0` |
| 3 | `+0xDEC` | 95 | 与第 2 格同形 |
| 4 | `+0xEA0` | 98 | 与第 2 格同形 |
| 5 | `+0xF54` | 86 | **发 msgid `0x104` + 弹框 (140,150)** |

### 对我方实现的影响（**本轮不改，先列清**）

| 按钮 | 我方 | EI | 差异 |
|---|---|---|---|
| 帧 86 | 直接开始游戏 | 发 msgid 0x104 + 弹框 | ❌ 动作完全不同 |
| 帧 89 | `SetSelectPhase(3)` | phase=3 + 音频 + 发网络消息 | ⚠️ 缺音频与消息 |
| 帧 92/95/98 | 空动作 | 重置两个槽 + phase 4 | ❌ 缺动作；用途未定 |

**注意：帧 86 与帧 89 的动作方向是"先发消息再等回应"，而不是"直接切换状态"。**
我方现在的"帧 86 → 立即开始游戏"缺少了 **msgid 0x104 这一轮请求/应答**。
在没弄清 `0x104` 是什么包之前，**不改成"发 0x104"**（否则会把一个不懂的包发出去）。
下一步先查 `0x104` 在我方协议里对应什么。

## 文档内部冲突：`0x459D48` 的 msgid 0x64（2026-09-27 记录，未消解）

本项目更早的一处记录（见本文档前部）写着：

> 证据里 `0x459D48` 已知会 `F89 -> phase 3` 并发 msgid `0x64 '%s/%d'`

但今天**实读 `0x459D1D`-`0x459D8A`** 得到的是：

```asm
0x459D43  call 0x45b3d0              ; 音频
0x459D48  mov byte [esi+0x930], 3    ; phase = 3
0x459D4F  mov dword [esi+0x1160], 0
0x459D5F  push ecx / call [0x4762b8]
0x459D66  mov edx, [0x8aa48c]
0x459D6C  push 0 / push edx / call [0x4762ac]
0x459D75  mov ecx, 0x8ab828 / call 0x451f90   ; 网络
```

**这一段里没有 `push 0x64`**，也没有 `'%s/%d'` 的痕迹。两条记录**不能同时成立**。

三种可能，本轮**未判定**：
1. 早先那条记录有误（可能是把别处的 `0x64` 记到了 `0x459D48`）；
2. `0x64` 与 `'%s/%d'` 出现在**同一级的其它位置**（如 `0x451F90` 的参数里，
   或 `0x459D48` 之前/之后的邻近代码块），早先记录没写清精确地址；
3. 早先记录指的是**另一个函数**的同类行为。

**处置**：不删任何一条，把冲突显式记在这里。下一轮的第一件事是**定位 `'%s/%d'`
字符串在二进制里的地址并反查其引用**，用引用位置判定 `0x64` 到底属于哪一段代码 ——
这能一次性消解冲突，也能给出真实 msgid。

（教训：早先那条记录用了"已知"二字却没有精确地址，导致今天无法核对。**记录证据必须带
精确地址与出处**，否则等于没记。）

## 冲突消解：`'%s/%d'` 不存在于二进制 ⇒ 早先那条 `0x64` 记录**错误**（2026-09-27）

上节记的冲突，用**独立证据**消解：扫描整个映像找那段格式串。

### 正向对照（确认扫描方式本身有效）

`0x47D7E0` 起实测字节：

```
0x47D7E0  .\Data\CreateChr.dat
0x47D7F8  服务器认证已不可用,请重新登录.
0x47D818  请先建立至少一个角色才能进行游戏.
```

后两条与我此前读到的字符串**逐字节一致**，且地址与代码里 `push 0x47D7F8` /
`push 0x47D818` 精确对应 —— 说明扫描与读串都可靠。

### 否定结果

| 搜索串 | 结果 |
|---|---|
| `%s/%d` | **无** |
| `/%d` | 无 |
| `%s/` | 无 |
| `%d/%d` | 无 |
| `%s:%d` | 无 |
| `%s-%d` | 无 |
| `%s` | 有（`0x47A1CF` 等 6 处，另有 `/%s` 于 `0x47AD28`） |

**`'%s/%d'` 在映像里根本不存在。** 因此早先那条「`0x459D48` 已知会发 msgid
`0x64 '%s/%d'`」的记录**是错的** —— 不是"没核对上"，而是**该字符串不存在**。

### 结论

- **`0x459D48` 不发 `0x64`，也没有 `'%s/%d'`。** 今天的实读（phase=3 + 音频 +
  `[0x4762B8]` + 网络 `0x451F90`）才是该处的真实行为。
- 因此**"F89 发 msgid 0x64"这个说法从证据里删除**（在此显式标注作废，而不只是补一条）。
- 真实可用的 msgid 线索只剩帧 86 的 `0x104`（今天实读）。

### 教训（写进方法约定）

早先那条记录的病因是**用了"已知"却没有精确地址与出处**，因而无法核对，还差点被我
当成"已有证据"继续往下推。**证据条目必须带：精确地址、原始字节/指令、以及读它的方法。**
缺任一项，条目就不可复核，等同于传闻。

**附带澄清**（今天读代码时确认）：`0x47D7F8`（认证失效）在二进制里**位于**
`0x47D818`（无角色）**之前**，而代码里 `0x4591D8` 先 push `0x47D818`、
`0x4591F3` 才 push `0x47D7F8` —— 即**第一条分支是"无角色"**，与地址顺序无关。

## 撤回：`0x104` **不是** msgid（2026-09-27 自我更正）

上一节写"帧 86 发 msgid `0x104`"。本轮用**自测过的扫描器**（先确认能找到已知的
`0x459F79`）统计 `push 0x104`（字节 `68 04 01 00 00`）的全部出现：

```
0x415356  0x4185A5  0x4186A6  0x41B733  0x41EDC7
0x441B41  0x441CEE  0x441D54  0x441DB3  0x441DE8
0x441E3A  0x441ECF  0x441F02  0x450E64  0x458BD8
0x459702  0x459F79  0x45A417  0x46E7DD  0x46EBDE
```

**共 20 处，遍布整个 .text。** 一个真正的消息 id 不会这样使用。

**结论：`0x104` 不是 msgid，是通用常量**（尺寸/标志位之类，具体未定）。
`0x459F79` 处 `push 0x104` 与随后的 `push 0x8b187c`（字符串）、
`push 0xffff / 0x96 / 0x8c` 应当一起看成一个**消息框调用**的参数组，`0x104` 是其中
一个数值参数，**不是"发什么包"**。

**因此撤回上一节的下列表述**：
- ~~"帧 86 的动作是「发 msgid `0x104` + 弹消息框」"~~
- ~~"真实可用的 msgid 线索只剩帧 86 的 `0x104`"~~

保留可信的部分：**帧 86 命中后会调用一个消息框**（坐标 `(140,150)`，与两处错误提示
同坐标）—— 这一点由 `push 0xffff / 0x96 / 0x8c` 与 `0x8b187c` 支持，**与 `0x104` 无关**。

### 教训（与上一条同源）

这是**连续第二次**把"某个立即数"当成 msgid：上一条是 `0x64`（字符串不存在 ⇒ 错），
这一条是 `0x104`（20 处使用 ⇒ 不可能是 id）。两次都是**看到一个 push 立即数就假定它是
消息号**，而没有先问"这个值在别处用得多不多"。

**新增判据（下次先做）**：**任何"这是消息 id"的假设，必须先统计该立即数在 .text 里
的出现次数**。真 id 通常 1–3 处；出现十几处的必然是通用常量。

## `0x457AB0` = phase 2 的动画更新函数；周期播 `CreateChr.mp3`（2026-09-27 定论）

反汇编 `0x457AB0`（此前只知它是"phase 2 的动画角色列表"）：

```asm
0x457AB0  sub esp, 0x17c              ; 局部 0x17C 字节
0x457ABC  mov eax, [ebx+0x1160]       ; 计时器开关
0x457AC8  mov eax, [esp+0x190]        ; **delta（时间增量）—— 每帧调用的更新函数**
0x457ACF  mov ecx, [ebx+0x1164]
0x457AD5  add ecx, eax / mov [ebx+0x1164], ecx   ; 累计
0x457ADF  cmp eax, 0x3e8              ; **超过 1000 ms**
0x457AE4  jbe 0x457b03
0x457AE6  push 1 / push 0x47d690
0x457AED  mov ecx, 0x8ab130 / call 0x45b390      ; **音频管理器（0x8AB130）**
0x457AF7  mov [ebx+0x1160], 0         ; 复位开关
0x457AFD  mov [ebx+0x1164], 0         ; 复位累计
0x457B03  lea ecx, [ebx+0x14c] / push 0x50 / call 0x466130   ; 帧调度
0x457B14  mov eax, [ebx+0x184] / mov ecx, [ebx+0x188]
0x457B35  mov ecx, 0x8ab7a8 / call 0x45fd50                  ; 绘制
0x457B40  mov ecx, ebx / call 0x4586f0
0x457B47  lea esi, [ebx+0x10e0]                              ; **动画槽数组**
0x457B58  mov edi, [esi+0x18]                                ; 当前帧对象
0x457B63  mov cx, [esp+0x190] / add word [esi], cx           ; **推进动画计时**
0x457B71  cmp ax, [edi+4] / jbe 0x457bd5                     ; 与帧时长比较
```

### 字符串读出

`0x47D690` = **`.\Sound\CreateChr.mp3`**

### 结论

1. **`0x457AB0` 是每帧更新函数**（带 delta 参数），不是"列表构造"。
2. 它维护 **`+0x1160`（开关）/ `+0x1164`（累计毫秒）**；超过 **1000 ms** 就通过
   **音频管理器 `0x8AB130`** 播放 **`CreateChr.mp3`**，然后复位两者 —— 即**周期播放**
   （1000 ms 一次），直到开关被关掉。
3. `+0x10E0` 起是**动画槽数组**；`word [esi]` 是计时器，`[edi+4]` 是**当前帧的时长**，
   超时即推进到下一帧。这就是"动画角色列表"的实现。

### 对我方音效接线的直接影响

**`CreateChr` 这个音效的证据位置有两处，含义不同**：

| 位置 | 证据 | 我方 |
|---|---|---|
| F51（点"创建角色"） | `0x459AB6` 附近（此前记录） | 已接 `LegacyCreateChr` |
| **phase 2 周期播放** | **`0x457AB0` + `0x47D690` = `CreateChr.mp3`（本轮）** | **未接** |

注意两处的**文件不同**：`0x457AB0` 用的是 **`.mp3`**，而此前从 82 机器取回的
`LegacyEI/Sound/` 里 `CreateChr` 同时有 `.wav` 和 `.mp3`。**这说明 EI 里
"创建角色"音效确实有两份资源、两个用法**，不是重复。

**待办**：确认我方 `LegacyCreateChr` 指向的是哪一个（我接的是 `CreateChr.wav`），
以及是否需要在 phase 2 加周期播放 `CreateChr.mp3`。**本轮不改** —— 先把两条证据分清楚。

## `CreateChr.mp3` 是 27.6 秒的曲子，不是 1.977 秒的音效（2026-09-27 更正）

### 实测时长（`afinfo`）

| 文件 | 时长 |
|---|---|
| `mir2ei/Sound/CreateChr.ogg` | **1.977 s** |
| `mir2ei/Sound/CreateChr.wav` | **1.977 s** |
| `LegacyEI/Sound/CreateChr.wav` | **1.977322 s**（348844 字节） |
| **`LegacyEI/Sound/CreateChr.mp3`** | **27.626 s**（442020 字节） |

SHA256 也不同（`114f7f66…` vs `223fea5b…`）。

**结论：`CreateChr.mp3` 与 `CreateChr.wav` 是两个完全不同的东西** ——
`.mp3` 是一支 **27.6 秒的曲子**（很可能是"创建角色"环节的**背景音乐**），
`.wav` 是 **1.977 秒的音效**。

（`CreateChr.mp3` 已从 82 机器取回，暂存于 `LegacyEI/Sound/`。本机 ffmpeg 缺
`libvorbis`，未能转成 `.ogg`；Godot 能否直接解码 `.mp3` 尚未验证。）

### 更正上一节的"周期播放"解读

上一节说 `0x457AB0` "每 1000 ms 周期播放 `CreateChr.mp3`"。**这与 27.6 秒的时长矛盾**
—— 一支 27.6 秒的曲子每 1000 ms 重播会每秒从头开始，不合常理。

重看入口条件：

```asm
0x457ABC  mov eax, [ebx+0x1160]
0x457ABA  xor ebp, ebp                ; ebp = 0
0x457AC3  cmp eax, ebp
0x457AC6  je  0x457B03                ; **+0x1160 == 0 时整段计时逻辑被跳过**
```

**`+0x1160` 是开关**；为 0 时整段（累计、比较 1000、播音频、复位）**完全不执行**。
所以正确表述是：

> **当 `+0x1160` ≠ 0 时**，累计 delta，超过 1000 后再播一次并复位。

至于它是"每 1000 ms 重复"还是"累计到 1000 后播一次、由别处再置位开关"，**本轮判定不了**
—— 取决于谁写 `+0x1160`。**已记待办，不按猜测实现。**

### 对我方音效接线的修正

- 我方 `LegacyCreateChr` → `CreateChr.wav`（1.977 s 音效）：**这个对应关系仍然成立**，
  因为 82 机器上 F51 与 phase 2 两处证据都引用名为 `CreateChr` 的资源，而
  **短音效（.wav）才适合"点按钮播一次"**。
- **`CreateChr.mp3`（27.6 s 曲子）不适合当作按钮音效**，它更像是**该环节的 BGM**。
  **不要**把它接成"每 1000 ms 播一次"。
- 我方 phase 2 目前**没有 BGM**；是否应播这支 27.6 s 曲子，取决于 `+0x1160` 的写者，
  **本轮不改**。

## 我方动画时长的常量无证据；EI 是逐帧时长（2026-09-27 记录）

### EI 侧（已读清）

```asm
0x457B63  mov cx, [esp+0x190]     ; delta
0x457B6B  add word [esi], cx      ; 动画计时累加
0x457B71  cmp ax, [edi+4]         ; 与 **[edi+4]** 比较
0x457B75  jbe 0x457bd5            ; 未到则该帧不换
```

`[edi+4]` 是**当前帧对象的一个 word 字段** —— 即**每一帧有自己的时长**，
由帧数据（`edi` 指向的帧对象）提供，超时后推进到下一帧。

### 我方侧（无证据）

`GodotClient/Scripts/SelectScene.cs` 两处：

```csharp
394:  _characterAnimation.AnimationDelay  = TimeSpan.FromMilliseconds(2400);
428:  anim.AnimationDelay                = TimeSpan.FromMilliseconds(2400);
```

**`2400` 在审计文档里查不到任何依据**（文档中出现的 `2400–2429` 是 PRE-11 里
登录页动画请求越界帧段的事，与这里无关）。

**所以 `2400ms` 是一个来源不明的常量。** 它把「整段动画的周期」固定为 2400 ms，
再由 `FrameCount` 均分给每帧；而 EI 是**每帧各自有时长**。两者不等价 ——
若 EI 各帧时长不均等，我方动画的节奏就是错的。

### 待办（静态可查，下一轮做）

1. 查明 EI 的 `[edi+4]` 由谁写入 —— 即帧对象从哪份数据取每帧时长
   （可能是 WIL/WIX 头里的 delay 字段，也可能是代码里的固定表）。
2. 若来自资源头：确认我方 `Interface1c.Zl`/WIL 读取路径是否已解析该字段，
   若未解析则补上，并按逐帧时长驱动 `DXAnimatedControl`，取代 `2400ms` 均分。
3. 若来自固定表：把表读出来，在代码里照抄。

**本轮不改代码** —— 因为"改用逐帧时长"需要先知道时长从哪来，否则会把一个
无依据的常量换成另一个无依据的实现。

## 更正上一节：ZL 资源**不含**时间字段 ⇒ 用固定常量是正当的（2026-09-27）

上一节我说我方 `2400ms` "无证据"。读 `RenderingCore/LibraryFormat/ZlImageMetadata.cs`
的**完整字段表**后，该判断需要修正。

`ZlImageMetadata.Read` 读入的字段是：

```
Version, Position, Width, Height, OffSetX, OffSetY,
ShadowType, ShadowWidth, ShadowHeight, ShadowOffSetX, ShadowOffSetY,
OverlayWidth, OverlayHeight, AtlasPage, ShadowAtlasPage(=-1), OverlayAtlasPage(=-1),
SourceRectangle, VisibleBounds,
ImageCodec, ShadowCodec, OverlayCodec,
ImageRuntimePreference, ShadowRuntimePreference, OverlayRuntimePreference,
StoredImageDataSize, ImageBc7DataSize, ImageFallbackDataSize,
StoredShadowDataSize, ShadowBc7DataSize, ShadowFallbackDataSize,
StoredOverlayDataSize, OverlayBc7DataSize, OverlayFallbackDataSize
```

**没有任何时间/时长/延迟字段。**

### 结论

**ZL 资源不携带逐帧时长** ⇒ EI 的 `[edi+4]`（每帧时长 word）**必然来自代码里的定时逻辑**
（固定值或运行期写入），**不来自资源**。

因此：

- **"用固定常量"在原理上是正当的**，我方 `2400ms` 不是"凭空捏造"；
- EI 存**每帧一个 word**，我方存**整段周期再均分** ——
  **若 EI 各帧时长均等，两者等价**（如 15 帧 × 160 ms = 2400 ms），
  **若不等，则我方节奏有偏**。

### 仍然成立的待办（但性质变了）

不再是"2400 无依据"，而是：**EI 的每帧时长是否均等？** 若是 → 我方现状正确，
不必改；若否 → 需要照抄 EI 的时长表。**这才是该查的问题。**

查法：找 `[edi+4]` 的写入者。`edi = [esi+0x18]`（当前帧对象），
其来源是 `esi`（`+0x10E0` 起的动画槽）—— 需反汇编写入该帧对象 `+4` 的代码，
或写入动画槽 `+0x18` 的地方。

**本轮不改代码。** 上一轮"不改"的理由（不知道时长来源）已消除，但**新问题（是否均等）
尚未回答**，所以仍不改 —— 改了也是碰运气。

## 定论：EI 的动画时长是**每支动画一个值**（非每帧）⇒ 我方均分等价（2026-09-27）

读换帧逻辑（`0x457BC0` 起）得到 `edi` 指向结构的**完整用途**：

```asm
0x457BC4  mov cx, [esi-2]        ; 当前帧索引
0x457BC8  cmp cx, [edi+2]        ; 与 **[edi+2]** 比较
0x457BCC  jbe 0x457bd5
0x457BCE  mov dx, [edi]          ; **[edi+0]** = 回绕值
0x457BD1  mov word [esi-2], dx   ; 回绕（循环到起始帧）
...
0x457B71  cmp ax, [edi+4]        ; **[edi+4]** = 时长（与计时器比较）
```

| 偏移 | 含义 |
|---|---|
| `[edi+0]` | **循环起始帧**（回绕目标） |
| `[edi+2]` | **末帧**（上限） |
| `[edi+4]` | **时长** |

### 结论（二选一判定完成）

**这是一支动画一个描述符（3 个 word），不是每帧一个。** 即**同一支动画所有帧
时长均等**。

因此：

- 我方 `AnimationDelay = 2400ms` 配合 `FrameCount` 均分 → **每帧 2400/N ms**；
- EI → 每帧同一个 `[edi+4]`；
- **两者在"每帧时长均等"这一点上等价**，"改用逐帧时长表"这个待办**不成立，撤销**。

**仍待确认（次要）**：`[edi+4]` 的具体数值是多少（决定总周期）。
如果我方 2400 ms 与 EI 的 `[edi+4] × 帧数` 不等，则**播放速度**有差异 ——
但这属于"调数值"，不是"结构错误"，且不影响帧序正确性。

### 本轮不改代码

"是否均等"已判定为**均等** ⇒ 我方结构正确，无需改结构。
若日后要精确对齐速度，需先读出 `[edi+4]` 的实际数值 —— 记为次要待办。

## 动画槽推进：我方 `DXAnimatedControl` 与 EI 逐项等价（2026-09-27 定论）

原先立的待办"实现 phase 2 动画槽推进"经核对后**撤销** —— 我方已实现且结构与 EI 等价。

读 `GodotClient/Controls/DXAnimatedControl.cs`：

```csharp
// 第 8 行注释：「AnimationDelay 与原客户端相同，表示播放一轮的总时长，而不是单帧时长。」
double duration = AnimationDelay.TotalSeconds;
int frame = (int)Math.Floor(elapsed / duration * FrameCount);
if (Loop)
{
    if (frame >= FrameCount) { AfterAnimationLoop?.Invoke(this, EventArgs.Empty); frame %= FrameCount; }
}
else if (frame >= FrameCount) { Index = BaseIndex + FrameCount - 1; }

Index = BaseIndex + Math.Clamp(frame, 0, FrameCount - 1);
```

逐项对照 EI（`0x457AB0`）：

| EI | 我方 | 判定 |
|---|---|---|
| 每个动画槽一份 `esi` 结构，各自 `[esi]` 计时器累加 delta | 每个 `DXAnimatedControl` 实例各有自己的计时状态 | **等价**（都是逐控件独立计时） |
| 到上限 `[edi+2]` 后回绕到 `[edi+0]` | `frame %= FrameCount` 后取 `BaseIndex` | **等价**（回绕到起始帧） |
| 上限 `[edi+2]` | `BaseIndex + FrameCount - 1` | **等价** |
| 时长 `[edi+4]`（每支动画一个值） | `AnimationDelay`（**一轮总时长**，注释已说明与原客户端一致） | **等价** |
| 比较 `[esi]`（word 计时器）与 `[edi+4]` | `Math.Floor(elapsed / duration * FrameCount)` | **等价**（离散化到帧号，避免逐毫秒误差累积） |

**四项全部等价。** 我方实现**不需要改**，待办**撤销**。

### 说明：这一轮与上一轮都是"判定不需要改"

连续两轮结论都是"我方正确、无需改动"。这不是空转 —— 若不核对就按"我方缺动画推进"的
假设去写代码，会**新写一套与现有一致的逻辑**，或更糟：改成与 EI 不同的行为。
但也必须承认：**这两轮没有产生任何代码改动**，实际交付只有文档。

### 剩余真实差异（已确认要改的）

当前**唯一确认"我方确实不同"**的 phase 2 项是：

1. **phase 2 的密码框**：EI 有，我方**没有实现**（尚未定位其帧号与坐标）。
2. **五钮动作**：帧 86/89 的动作方向是"先发消息再等回应"，我方是"直接切状态"；
   帧 92/95/98 我方为空动作而 EI 有动作。**但**其确切语义依赖静态不可解的运行期
   指针，**在拿到运行期证据前不能改**。

## 更正我方两个错误命名（2026-09-27，证据本来就在本文档 PRE-02）

**本轮之前我多次说"帧 92/95/98 的命名既不证实也不证伪、属未定猜测"。这是错的 ——
证据早就写在本文件 PRE-02（约第 497 行）**：

> F86/F87 是勾选态图形，F89/F90 为叉形图形，F92/F93 是斜笔/金色圆形底图，
> F95/F96 是环形箭头图，F98/F99 是卷页/文书图；各对原生 28×28 或 40×38，
> 图形含义仍是视觉候选。

### 据此更正

| 帧 | 证据描述 | 原命名 | 新命名 | 判定 |
|---|---|---|---|---|
| F86/F87 | **勾选态图形** | `_skinConfirmYes` | 不变 | ✅ 名实相符 |
| F89/F90 | **叉形图形** | `_skinConfirmNo` | 不变 | ✅ 名实相符 |
| F92/F93 | **斜笔 / 金色圆形底图** | `_skinIconWeapon`（武器） | **`_skinIconPen`** | ❌ 原名错 |
| F95/F96 | **环形箭头图** | `_skinIconFace`（人脸） | **`_skinIconArrow`** | ❌ 原名错 |
| F98/F99 | **卷页 / 文书图** | `_skinIconScroll`（卷轴） | 不变 | ⚠️ 近似，保留 |

`SelectScene.cs` 已改名并补注释说明依据；`--legacy-select-selftest` 改名后仍 **PASS**。

### 方法论错误（必须记下）

我这几轮反复说"命名是猜测"，却**没有回去读本文档里评分/证据表已有的条目**。
这不是"证据不足"，是**我没有检索已有证据**。两种错误性质相同：

- 早先那条 `0x64` 记录：写了"已知"却不给地址 ⇒ **无法复核**；
- 本轮：证据就在同一文件里 ⇒ **我没有去读**。

**新增约定**：在对某个 UI 元素下"语义未定"的结论**之前**，必须先在本审计文档里
按关键词（帧号、控件名、模块名）检索一次 —— 文档里已有的结论优先于我的新推断。

### 附带修正一条此前的判断

PRE-16 记 `F89 使 phase3 并发 msg 0x64`。我在本轮前的一段实读（`0x459D48` 附近）
未见 `push 0x64`，且 `'%s/%d'` 字符串不存在。**该条仍属"未复核"**：
`0x64` 可能由 `0x451F90`（网络调用）内部或其参数寄存器设置，而非显式 `push`。
**不因找不到就断言 PRE-16 错** —— 改为"待复核"，需检查 `0x451F90` 的调用约定。

## 检索结果：phase 2 密码框无帧号/坐标记录；`0x64` 语义被 PRE-10 修正（2026-09-27）

按新增约定"下结论前先检索本文档"，本轮先检索"EDIT / 编辑框 / 密码框"，结果如下。

### 1. `0x64` 的真实语义（**推翻我此前的两处说法**）

**PRE-10（约第 506 行）**：

> F89 handler 切 phase3 并发送旧消息 **`0x64`（账号/服务器索引）**

即 `0x64` 携带的是**账号/服务器索引**，**不是**格式化字符串。所以我此前写的：

- ~~"`0x459D48` 已知会发 msgid `0x64 '%s/%d'`"~~ —— 模板串不存在（已验证），且语义也错
- ~~"真实可用的 msgid 线索只剩帧 86 的 `0x104`"~~ —— `0x104` 是通用常量（已验证）

**正确表述**：F89（`+0x1008`，帧 89）切 phase 3 并发送 `0x64`，载荷为**账号/服务器索引**。
这与本轮前的实读（`0x459D48` 切 phase=3、随后 `call 0x451F90` 网络发送）**一致**
—— `0x64` 由网络调用内部或参数寄存器承载，故我此前在相邻指令里找不到 `push 0x64`
并不能否定它。

### 2. phase 2 的密码 EDIT：**文档确认它存在，但没有帧号/坐标**

**PRE-16 / PRE-10 一致记录**：

> 完成后进入 phase2，播放角色列表动画并显示 F92/F95/F98/F86/F89 五控件**与密码 EDIT**

且 PRE-16 明确写「**密码框文本含义 pending**」，PRE-10 写「F51 handler…」
但两处**都没有给出该 EDIT 控件的帧号、坐标或尺寸**。

**因此**：
- ✅ 可确认「phase 2 有密码 EDIT，我方未实现」是**真实差异**
- ❌ **不能**从文档得到它的几何 —— 需要**新反汇编**
- ⚠️ 登录页的账号/密码框矩形 `(128,440)-(227,454)` / `(326,440)-(425,454)`（第 486 行）
  是**登录页**的，**不能**直接搬给 phase 2

### 下一步

反汇编 phase 2 的对象构造/绘制，找那个 EDIT 控件：
1. 在 `0x456DBB`-`0x456EC2`（9 个按钮的构造区）**邻近**找第 10 个控件构造；
   密码 EDIT 很可能与它们在同一段构造代码里。
2. 或从 `+0x1488`（选中槽）、`+0x1160` 等同族字段附近找 EDIT 的对象偏移。
3. 拿到帧号/坐标后再实现，并加自检断言。

**本轮不改代码** —— 文档明确"文本含义 pending"，几何未知；按约定不臆造。

## 找到 phase 2 的密码 EDIT：`0x458BB0` 构造三个输入控件（2026-09-27 定论）

在 9 个按钮构造区（`0x456DBB`-`0x456EC2`）之后，构造函数**紧接着**调用 `0x458BB0`：

```asm
0x456EC8  call 0x417550              ; 第 9 个按钮 (p2-5, +0x1008)
0x456ECD  mov ecx, esi
0x456ECF  call 0x458bb0              ; **构造输入控件组**
0x456ED4  mov edx, [esi+0x113c]      ; 预载音频（此前已证）
0x456EDA  mov dword [esi+0x1160], 1  ; **打开 +0x1160 开关！**
0x456EE5  mov dword [esi+0x1164], 0  ; 累计清零
0x456EEF  call 0x45b7f0              ; 预备音频
```

`0x458BB0` 本体：

```asm
0x458BB0  push esi / mov esi, ecx                         ; this = 选角对象
0x458BB3  push 0x78 / push 0xD2 / lea eax,[esi+0x932] / push 0xC8 / push eax / call 0x449C50
0x458BCB  push 0x78 / push 0x117 / lea ecx,[esi+0x938] / push 0x104 / push ecx / call 0x449C50
0x458BE5  push 0x78 / push 0x14B / lea edx,[esi+0x93e] / push 0x140 / push edx / call 0x449C50
```

### 参数解读

| 控件对象 | 参数2 | 参数3 | 参数4 |
|---|---|---|---|
| `+0x932` | `0xC8` = **200** | `0xD2` = 210 | `0x78` = **120** |
| `+0x938` | `0x104` = **260** | `0x117` = 279 | `0x78` = **120** |
| `+0x93E` | `0x140` = **320** | `0x14B` = 331 | `0x78` = **120** |

**三个宽度均为 120 的输入控件，参数2 依次为 200 / 260 / 320。**
`0x449C50` 是被复用的控件构造/设矩形助手（本轮未反汇编它本体）。

### 与 PRE-16 的对应

PRE-16 记「phase 2 显示五控件**与密码 EDIT**」—— 本轮找到的就是它，
且**不是 1 个而是 3 个输入控件**（PRE-16 只提"密码 EDIT"，数量此前未记录）。

### 顺带解开了 `+0x1160`

`0x456EDA` 在构造完输入控件后**把 `+0x1160` 置 1** —— 这正是上一轮悬而未决的
「`+0x1160` 的写者」。所以：

- `+0x1160` 由**构造函数**置 1（不是运行期反复置位）；
- 结合 `0x457AB0` 的逻辑（`+0x1160` ≠ 0 时累计 delta，超 1000 ms 播一次并**复位为 0**），
  含义是：**「进入 phase 2 后，播一次 `CreateChr.mp3`，然后开关自动关掉」**
  —— 即**进入 phase 2 时播放一次** 27.6 秒的曲子（BGM），**不是**每秒重播。

**这解决了上一轮的悬案，且与 27.6 秒的时长自洽。**

### 下一步

1. 反汇编 `0x449C50`，确认参数语义（左/上/宽，或 左/右/上）。
2. 确认这三个控件的**用途**（三个输入框：可能是账号/密码/确认密码，或角色名等）。
3. 我方实现：phase 2 显示三个输入控件 + 进入 phase 2 时播一次 `CreateChr.mp3`
   （需先确认 Godot 能解码 mp3，或转成 ogg）。

## 更正上一节：`0x449C50` 是**通用三字结构写入器**，不是控件矩形设置（2026-09-27）

上一节据 `0x458BB0` 的三个调用推出"三个宽度 120 的输入控件，x=200/260/320"。
反汇编 `0x449C50` 本体后**该解读被推翻**：

```asm
0x449C50  mov eax, [esp+4]        ; 目标指针
0x449C54  mov cx,  [esp+8]        ; 参数2
0x449C59  mov dx,  [esp+0xc]      ; 参数3
0x449C5E  mov word [eax],   cx    ; +0 = 参数2
0x449C61  mov cx,  [esp+0x10]     ; 参数4
0x449C66  mov word [eax+2], dx    ; +2 = 参数3
0x449C6A  mov word [eax+4], cx    ; +4 = 参数4
0x449C6E  ret 0x10                ; 4 个参数（1 指针 + 3 word）
```

**它只是一个"往目标结构写 3 个 word"的助手**，不含任何坐标/尺寸语义。

### 反证：同一个助手被完全不同的值调用

紧随其后的 `0x449C80` 是**另一个对象的构造**，它也调 `0x449C50`，但值为：

```asm
0x449C8D  push 0xc8 / push 4  / lea eax,[esi+0x18] / push 0    / push eax / call 0x449c50
0x449CA1  push 0x64 / push 6  / lea ecx,[esi+0x1e] / push 0x50 / push ecx / call 0x449c50
0x449CB2  push 0x4b / push 5  / lea edx,[esi+0x24] / push 0xa0 / push edx / call 0x449c50
```

即目标分别为 `esi+0x18` / `esi+0x1E` / `esi+0x24`，三组值 `(0,4,0xC8)`、
`(0x50,6,0x64)`、`(0xA0,5,0x4B)` —— 结构与 `0x458BB0` 处**同形**
（stride 6、三个 word），但数值组完全不同且不成坐标形态。

**所以那 3 个 word 的语义未定**，**不能**当成 (左, 上, 宽) 或 (x, y, w)。

### 结论（保守）

- `0x458BB0` 确实构造**三个 stride=6 的三字段结构**，位于 `+0x932` / `+0x938` / `+0x93E`，
  由构造函数（`this = 选角对象`）初始化；
- 它们的**字段语义未知**，因此**不知道这 3 个结构的用途**
  （是否输入控件、是否密码框，**仍无证据**）；
- 上一轮"三个宽度 120 的输入控件"的说法**作废**。

### 与 PRE-16「密码 EDIT」的关系（不臆合）

PRE-16 说 phase 2 有"密码 EDIT"，本轮找到的是 3 个三字段结构，
**两者是否同一物，本轮不能判定** —— `0x449C50` 是通用写入器，不证明它们是 EDIT 控件。

**下一步**：找**谁读** `+0x932` / `+0x938` / `+0x93E` 这三个结构（读点会揭示语义）。
注意此前已知 `0x458EC0`（帧号公式）读 `+0x932` 起的表，但那是**另一个 30×6 的表**，
需先确认两者是否同一片区域，避免误合。

## 三个结构 `+0x932`/`+0x938`/`+0x93E` 只被写入、从未被直接读取（2026-09-27 定论）

用自测过的扫描器（先确认能找到已知写入点 `0x458BBA`）扫全 `.text` 引用这三处偏移的
指令，结果**只有 4 处，全部是 `lea`（取地址），且没有一处是读取**：

```
0x456BFB  lea  disp=0x932
0x458BBA  lea  disp=0x932   ; 0x458BB0 内的写入点
0x458BD2  lea  disp=0x938   ; 同上
0x458BEC  lea  disp=0x93E   ; 同上
```

另外扫 `mov reg,[base+disp32]` 形式（`8B` 编码）：**0 命中**。

### 结论

**这三个 stride=6 的三字段结构，在 `.text` 里只被写入（`0x458BB0` 内 3 次），
从未被直接读取。**

可能的解释（**均未验证**）：

1. 它们是**整体传给某个函数**的（`lea` 取地址后传参），由被调函数按指针访问 ——
   但 4 处 `lea` 中 3 处紧接着是 `call 0x449C50`（写入），第 4 处 `0x456BFB` 尚未追查；
2. 它们属于**更大的数组**，运行期用**计算基址**访问（故不出现字面偏移）；
3. 它们确实是**死数据/预留字段**。

### 对"phase 2 密码框"判断的影响

**这三个结构不可能是密码输入框** —— 输入框必然要被读取才能取到用户输入的值，
而这里没有任何读取点。

因此此前（第 316345C5 次提交）"找到 phase 2 的密码 EDIT：`0x458BB0` 构造三个输入控件"
**被本轮进一步否定**：它们不是输入控件。上一轮已把"3 个输入控件"作废，本轮给出**独立
的第二条反证**（无读取点）。

**PRE-16 的「密码 EDIT」仍未被定位。** 下一步不能继续在 `+0x932` 这一族上找。

### 下一步（改向）

应回到 PRE-16 的原文语境去找：它说的是 phase 2 状态**显示的控件**。
- 从 `0x457AB0`（phase 2 更新函数）**实际绘制的对象**入手，列出它在 phase 2 画了什么；
- 或从 `+0x930`（phase 字段）的所有读取者出发，看哪些绘制路径受 phase==2 门控。

## phase 2 的绘制清单 + 按钮 vtable 槽位映射（2026-09-27 定论）

从"phase 2 到底画了什么"切入（而非猜某个偏移的用途），列出绘制助手 `0x45FD50`
的全部调用者（自测确认能找到已知的 `0x457B3B`），共 23 处，其中**选角屏**（`0x457xxx`）
占 5 处：

```
0x457813
0x45794B
0x457B3B   ; 已知：phase 2 更新函数内
0x457C91
0x457DC6
```

### 1. 按钮 vtable 槽位映射（本轮新解）

`0x457DC6` 之后：

```asm
0x457DCB  lea esi, [ebx+0xd38]     ; 5 个按钮数组
0x457DD1  mov edi, 5
0x457DDA  call dword ptr [edx+4]   ; **调用 [+4]**
0x457DDD  add esi, 0xb4
0x457DE3  dec edi / jne 0x457dd6
```

综合此前证据，按钮对象的虚表槽位是：

| 槽位 | 用途 | 证据 |
|---|---|---|
| `+4` | **绘制** | 本处（`0x457DDA`） |
| `+0xC` | **点击处理** | `0x4599C0` |
| `+0x10` | **命中测试** | `0x459D37` |

**这是本轮的直接收获**：此前只知道 `+0xC`（点）、`+0x10`（命中），现在补上 `+4`（画）。

### 2. phase 2 在 (247, 384) 处绘制一个物件

```asm
0x457DB6  push eax
0x457DB7  push 0x180      ; 384
0x457DBC  push 0xf7       ; 247
0x457DC1  mov ecx, 0x8ab7a8
0x457DC6  call 0x45fd50
```

即**在 x=247, y=384 处画一个东西**（`0x8AB7A8` 是绘制上下文对象，
`0x45FD50` 是绘制入口，其参数含 `ecx=上下文` 与三个 push 值）。

**位置 (247, 384) 在角色槽下方、屏幕下半部** —— 与"输入框行"的形态相符，
但**本轮不据此断言它就是密码框**（下一次画的是什么由被调用的绘制函数决定，
本轮未读 `0x45FD50` 本体）。

### 3. 其余两处（已读，供后续定位）

`0x457813`：先 `call 0x466130`（帧调度）→ 取 `[edi+0x184]/[edi+0x188]` 的
`word [eax]`/`word [eax+2]`（宽高）→ 画；随后 `lea esi,[edi+0xcd8]`
（`-0x20` 处读门控）—— 这是 **phase 0/3 的角色槽区**（基址 `+0xCB8` 族）。

`0x457C91`：`mov edi,[esi-8]`；从 `[eax]`/`[eax+2]`/`[eax+4]`/`[eax+6]` 取
四个 word 加到 `edi`/`[esi-0xc]` 上再画 —— 这是**把动画帧画到槽位置上**。

### 下一步

反汇编 `0x45FD50` 本体，确认它的参数含义（上下文 / 目标对象 / x / y），
以及 `0x457DC6` 处 `push eax` 推的是什么对象 —— 那才是"phase 2 在 (247,384) 画的东西"。
若它是输入框，则 PRE-16 的「密码 EDIT」即定位完成。

## 坐标系定论：EI 选角屏是 **800×600**，我方按 640×480 布局（2026-09-27）

反汇编绘制入口 `0x45FD50`，发现它是**带屏幕裁剪的 blit**：

```asm
0x45FD50  sub esp, 0xec
0x45FD58  mov ebp, [esp+0xf8]      ; 参数：x
0x45FD60  cmp ebp, 0x320           ; **x > 800 -> return**
0x45FD69  jg  0x460204
0x45FD6F  mov eax, [esp+0x108]     ; 宽
0x45FD76  lea esi, [eax+ebp]       ; x + w
0x45FD81  mov ecx, [esp+0x104]     ; 参数：y
0x45FD88  cmp ecx, 0x258           ; **y > 600 -> return**
0x45FD8E  jg  0x460204
0x45FD94  mov edx, [esp+0x10c]     ; 高
0x45FD9B  lea edi, [ecx+edx]       ; y + h
0x45FDA6  cmp esi, 0x320 / jle 0x45fdbb   ; **x+w > 800 时裁剪**
0x45FDBF  test ebp / jge ...       ; x < 0 时裁剪
```

`0x320` = **800**，`0x258` = **600**。

### 定论

**EI 的屏幕坐标系是 800×600。** 这是**独立证据**（绘制函数的裁剪边界），
不是从某个控件矩形反推的。

与已有证据**吻合**：本文档 NOTICE-01（约第 345 行）记
「**800×600 原版父窗** `(107,110)`、584×252」；PRE-04/登录页记录也有 800×600 字样。

### 由此产生的**根本性问题**（我方现状）

我方 `SelectScene`（`ApplyLegacyEiSelectLayout()`）**按 640×480 布局**：

- F50 背景按 **640×480** 绘制；
- 四个 phase 0 按钮用原版坐标 `(440,93)` / `(79,243)` / `(259,49)` / `(28,438)`；
- 三个输入相关结构（已排除是输入框）也按 640 系理解过。

**在 640×480 画布上放 800×600 的坐标，右侧与下侧的元素会被裁掉或贴到边缘。**
这正解释了我此前看到的怪现象：截图 OCR 里「创建角色」出现在**右上角**、
「开始游戏」在**左上**——原版里它们分布在 800×600 的合理位置，
被我方压进 640×480 后就挤到了边上。

### 这是我方与 EI 的**结构性差异**（比单点坐标错更根本）

此前我只逐个核对"某按钮的坐标对不对"（结论：与 ctor 实参一致），
**却没有核对坐标系本身**。坐标值对、画布尺寸错，观感就是错的。

### 下一步

1. 确认 EI 的**控件坐标是相对 800×600 的屏幕**还是相对某父窗 —— 从 `0x45FD50`
   的调用点看，`0x457DC6` 处 push 的 x=247 / y=384 是**屏幕坐标**（因为裁剪用 800×600），
   故**选角屏控件坐标应为 800×600 系**。
2. 把 `ApplyLegacyEiSelectLayout()` 的基准从 640×480 改为 **800×600**，
   背景 F50（640×480）按原版在 800×600 中的位置摆（需确定其原点，可能是居中或左上）。
3. 改完用自检断言 + 截图验证（此时相信"截图 OCR 里按钮位置"是有意义的，
   因为 800×600 下它们不该再挤在角上）。

## `SelChr.mp3` 是 phase 0/3 的周期音效；存在「职业 选择」文本（2026-09-27）

反汇编 phase 0/3 的更新路径（`0x4577A0` 起，与 phase 2 的 `0x457AB0` **同构**）：

```asm
0x4577A7  mov ecx, [edi+0x1164]      ; 累计
0x4577AD  add ecx, eax               ; += delta
0x4577B7  cmp eax, 0x3e8             ; **超过 1000 ms**
0x4577BC  jbe 0x4577db
0x4577C0  push 0x47d624              ; **.\Sound\SelChr.mp3**
0x4577C5  mov ecx, 0x8ab130          ; 音频管理器
0x4577CA  call 0x45b390
0x4577CF  mov dword [edi+0x1160], ebx ; 复位开关（ebx = 0）
0x4577D5  mov dword [edi+0x1164], ebx
0x4577DB  lea ecx, [edi+0x14c] / push 0x32 / call 0x466130   ; 帧调度
0x4577F8  push 0xffff / push 0xffff
0x45780C  push ebx / push ebx        ; **背景画在 (0,0)**
0x457813  call 0x45fd50
```

### 发现 1：`SelChr.mp3` 是 **phase 0/3** 的 1000 ms 周期音效

`0x47D624` = **`.\Sound\SelChr.mp3`**。

**这与我的接线不符**：我把 `LegacySelChr`（指向 `SelChr.wav`）接在
`SelectScene.SetCharacters()`（拿到角色列表时）。**证据显示它属于 phase 0/3 的
周期播放路径**。与 phase 2 的 `CreateChr.mp3` 完全同构（同一对字段 `+0x1160`/`+0x1164`、
同一 1000 ms 门限、同一音频管理器、同一复位方式）。

→ **音效归属表应改为**：

| 阶段 | 音效 | 依据 |
|---|---|---|
| **phase 0 / 3** | **`SelChr.mp3`** | `0x4577C0` + `0x47D624` |
| **phase 2** | **`CreateChr.mp3`** | `0x457AE6` + `0x47D690` |
| 点「创建角色」(F51) | `CreateChr.wav` | `0x459AB6` 附近（早期记录） |
| 其他 | `StartGame.*` | 进入游戏路径 |

**注意三个音效各有归属，不能互换。** 我此前的接线把 `SelChr` 放在"拿到角色列表"，
属于**推断而非证据**，现予更正。

### 发现 2：F50 背景画在 (0,0)，不居中

`0x45780C` 用 `push ebx / push ebx` 且 `ebx` 在该函数中被用作 0
（`0x4577CF` 的 `mov [edi+0x1160], ebx` 是复位操作，ebx 必为 0）。
故 **F50 在屏幕上是 (0,0) 起、左上对齐**，不是居中。

→ 若把基准改为 800×600，背景仍应画在 **(0,0)**，右侧与下侧留空
（或由其它绘制填充）。

### 发现 3：存在「职业 选择」文本

```
0x47D640  创建
0x47D660  道士 职业 选择
0x47D680  武士 职业 选择
```

**选角屏确实存在"职业选择"语义的文本。** 这**支持**（但不等于证明）此前把
某些控件理解为与职业/外观选择相关。注意：这三个字符串与 `0x47D624` 相邻，
应属**同一处代码**（phase 0/3 或创建流程）使用 —— **具体引用点未查**，
故本条只作**文本存在性**记录，不据此改控件语义。

### 下一步

1. 查 `0x47D640` / `0x47D660` / `0x47D680` 的**引用点**，确定它们属于哪个阶段、
   显示在何处（这直接关系到选角屏上是否有"职业选择"控件及其文本）。
2. 按发现 1 更正 `LegacySelChr` 的触发点（从 `SetCharacters` 移到 phase 0/3 的周期音效）。
3. 按发现 2 把布局基准从 640×480 改为 800×600，背景仍放 (0,0)。

## 未解决冲突：屏幕是 800×600 还是基准 640×480（2026-09-27，本轮不改）

上一节据绘制入口 `0x45FD50` 的裁剪边界（`0x320`×`0x258` = 800×600）断定
"EI 选角屏坐标系是 800×600，我方 640×480 是结构性错误"。

**本轮准备实施时，发现代码里有一组相反的证据**，且带明确注释：

`GodotClient/Scripts/SelectScene.cs`：

```csharp
// 623 行附近
// **背景必须按 EI 原生 640x480 绘制在左上**，不能拉伸到 1024x768。
//   Interface1c.wil **F50**，w=640 h=480。
// 旧实现把 640x480 的图拉伸铺满 1024x768 并居中，正是审计文档 PRE-04 记录的
// 「贴图按原生 640x480 绘制在左上」不符。
...
// 49 行附近
// EI 两个角色槽的屏幕位置。原版以 640x480 中心 (320,240) 为基准
```

且此前的反汇编证据也支持**640×480 中心**：3D 模型绘制常量
`0x476398 = 320.0`、`0x476394 = 240.0`（即中心点 (320,240)）。

### 两组证据

| 来源 | 结论 | 性质 |
|---|---|---|
| `0x45FD50` 的裁剪边界 `0x320`/`0x258` | 屏幕 **800×600** | 直接反汇编 |
| 代码注释 + 模型常量 320.0/240.0 | 基准 **640×480** | 直接反汇编 + 已落地注释 |

### 一个能同时成立的解释（**属推断，不是证据**）

EI 的**屏幕输出**是 800×600；**F50 是一张 640×480 的图，画在 (0,0)**；
角色按 **640×480 的中心 (320,240)** 摆放。于是画面内容集中在左上 640×480 区域，
屏幕右侧与下侧留 160×120 空白。

**但这解释不了**：为什么若屏幕是 800×600，模型中心用的是 640×480 的中心而不是
屏幕中心 (400,300)？若角色该居中显示，用 (320,240) 就会偏左上。

### 本轮不改（理由）

我上一节已把"改成 800×600"写成待办。**但现在两组直接证据冲突且都能自洽**，
在做判别之前动手，就是在赌 —— 与我此前几次"先改再说"的错误同源。

### 判别方法（下一轮）

1. **看 F50 图内是否已含 800×600 的布局信息**：F50 是 640×480；
   若原版把 F50 贴到 (0,0)，则屏幕上 x>640 或 y>480 的控件（若有）应属**独立于背景**
   的元素。检查已知控件坐标是否有 >640 或 >480 的：
   - 若**全部** ≤640/≤480 ⇒ 内容确实都在 640×480 内 ⇒ 640 基准合理；
   - 若**有** >640 的 ⇒ 屏幕必为 800 宽 ⇒ 800 基准。
2. 目前已知控件坐标最大的是 p2-5 `(491,444)`、F86 `(450,444)`、
   p0-1 `(440,93)` 加宽 96 ⇒ 最大触及 **536 / 444** —— **全部在 640×480 内**，
   这一条**支持 640 基准**（但不排除 800 屏幕配 640 内容区）。
3. 找一个**坐标 >640 或 >480** 的原版控件（如登录页/配置窗）来判定屏幕宽高 ——
   文档里 NOTICE-01 记 F602 父窗 `(107,110)`、584×252，其右界 107+584=691 > 640
   ⇒ **屏幕至少 691 宽** ⇒ **支持 800 宽**。

**第 3 条是当前最有力的一条**，但它来自**另一个窗口**（通知窗），
需确认该窗口与选角屏是否同一坐标系后，才能用于选角屏。

**在判别完成前，不改任何坐标基准。**

## 冲突解决：屏幕 **800×600**，F50 是 640×480 的图放在 (0,0)（2026-09-27 定论）

上一节记的两组证据冲突，本轮用**新增的直接证据**解决。

### 决定性依据：裁剪是**硬编码立即数**

重读 `0x45FD50` 的两条边界比较：

```asm
0x45FD58  mov ebp, [esp+0xf8]     ; x
0x45FD60  cmp ebp, 0x320          ; **立即数 800**
0x45FD6F  mov eax, [esp+0x108]    ; 宽
0x45FD76  lea esi, [eax+ebp]      ; x + w
0x45FD81  mov ecx, [esp+0x104]    ; y
0x45FD88  cmp ecx, 0x258          ; **立即数 600**
```

**`0x320` / `0x258` 是立即数，不是从某字段读的** —— 即这是**编译期固定的屏幕尺寸**。

### 而且它是**全局**绘制入口

`0x45FD50` 有 **23 个调用者**，遍布各子系统（`0x402C99` 启动路径、`0x40B7FF`、
`0x429863`、`0x43985E`、`0x44B4F7`、`0x450594`、选角屏 5 处…）。
它不是某个窗口的私有绘制，而是**全局帧绘制入口**。

⇒ **屏幕就是 800×600。这一条不再有疑义。**

### 与 640×480 证据的关系：**两者都对，参照物不同**

| 量 | 值 | 参照物 |
|---|---|---|
| 屏幕裁剪边界 | **800×600** | 屏幕 |
| F50 背景图 | **640×480**，画在 **(0,0)** | 屏幕坐标 |
| 角色模型中心常量 `0x476398`/`0x476394` | **320.0 / 240.0** | **640×480 那张图自己的中心**，不是屏幕中心 |

即：**画面内容集中在屏幕左上 640×480 区域**，右侧与下侧留 160×120 的空白。

**上一节我提出过这个解释但标为"推断、不是证据"。现在它有了直接证据支撑**
（裁剪是立即数 + 该函数是全局入口），故升级为结论。

### 由此得到的**我方修正清单**（下一轮实施）

1. **画布基准应为 800×600**，不是 640×480。我方 `ApplyLegacyEiSelectLayout()` 需改基准。
2. **F50 仍画在 (0,0)**，**不要拉伸、不要居中**（第 623 行注释的结论仍然正确 ——
   它是针对"旧实现拉伸铺满 1024×768 并居中"的修正，与本条不冲突）。
3. **角色模型的中心仍是 (320,240)**（640×480 图的中心），**不要**改成屏幕中心
   (400,300) —— 这是最容易改错的一处：改了反而与 EI 不符。
4. 原版按钮坐标 `(440,93)`/`(79,243)`/`(259,49)`/`(28,438)`、phase 2 的
   `(450,444)`/`(491,444)`/`(266,419)`/`(308,419)`/`(352,419)` —— 全部是**屏幕坐标**，
   在 800×600 下本就成立，**数值不用改**。

**所以真正的改动只有第 1 条（基准）**，且必须**同时保**第 2、3 条不变
（把"背景和模型在左上 640×480 区域、控件在 800×600 屏幕坐标"这个混合布局表达出来）。

### 风险提示

若只把基准改成 800×600 而**顺带把背景或模型也居中**，就会引入**新的**与 EI 不符的差异。
改动时必须逐条对照上面 4 点。

## 更正：我方逻辑画布是 **1024×768**，不是 640×480（2026-09-27）

上一节我写"我方 `ApplyLegacyEiSelectLayout()` 按 640×480 布局"、并列出改动清单。
读代码后发现**该说法错误**：

`GodotClient/Scripts/SelectScene.cs` `BuildLegacySelectUi()`：

```csharp
// 623 行
// 布局基准 = 逻辑画布 1024x768，UiScaler 负责缩放 + 居中（同 LoginScene）。
var viewport = new Vector2(UiScaler.BaseWidth, UiScaler.BaseHeight);
```

即：

- **逻辑画布** = `UiScaler.BaseWidth` × `UiScaler.BaseHeight` = **1024×768**（与 `LoginScene` **共用**同一个 `UiScaler`）；
- **F50 背景**（640×480）在**这个 1024×768 画布里**画在 (0,0)（`Size=(640,480)`、`Position=Zero`）；
- 所有原版控件坐标（`(440,93)` 等）是**写入这个 1024×768 画布的绝对值**。

**所以真正的情形是**：

| | 画布 | 原版坐标的落点 |
|---|---|---|
| EI | **800×600** | 相对 800×600 的绝对位置 |
| 我方 | **1024×768** | 同样的绝对值，但画布大得多 |

**绝对坐标相同、画布更大 ⇒ 所有控件相对画面偏左上。** 这就是截图 OCR 里
"创建角色偏右上、开始游戏偏左上"的成因 —— 不是"640 画布挤到边上"（我上一轮的描述），
而是"**1024 画布显得控件偏左上**"。两种描述的现象方向一致，但**成因与改法不同**。

### 修正后的改动结论（仍是同一件事，但落点不同）

要改的不是"640→800"，而是：

**把选角屏的逻辑画布基准从 1024×768 改为 800×600。**

**难点**：`UiScaler.BaseWidth/BaseHeight` 是**选角屏与登录页共用的**。
- 若直接改全局常量 → **登录页布局同时受影响**，需一并核对登录页的所有坐标
  （登录页矩形 `(128,440)`/`(326,440)`、按钮 `(459,436)` 等，须确认它们也是 800×600 系）；
- 若局部改 → 需确认 `UiScaler` 是否支持按场景设置基准。

**两种做法都要先查清 `UiScaler` 的接口与共享范围**，不能直接改一个常量了事。

### 本轮不改代码

理由：改动的**影响面**（共享 `UiScaler`）与我上一轮理解的**不同且更大**，
必须先查 `UiScaler` 的接口、以及登录页是否也在 800×600 系下才动手。
上一轮我把它写成"只有第 1 条（基准）"，**低估了影响面**。

### 顺带确认上一轮清单里仍然正确的两条

- **F50 仍画 (0,0)、不拉伸不居中**：正确，且与本次改动不冲突；
- **模型中心仍是 (320,240)**（640×480 图的中心）：正确；改基准时**不要**顺手改成
  (400,300) 或画布中心。

### `UiScaler` 是全局 `const`，无法按场景设置基准（2026-09-27 查证）

读 `GodotClient/Scripts/UiScaler.cs`：

```csharp
public static class UiScaler
{
    public const float BaseHeight = 768f;
    public const float BaseWidth  = 1024f;

    public static float ComputeScale(Viewport viewport) { ... byHeight = size.Y/BaseHeight; byWidth = size.X/BaseWidth; ... }
    public static void UpdateScale(CanvasLayer layer, Viewport viewport)
    {
        ...
        Vector2 offset = (vp - new Vector2(BaseWidth, BaseHeight) * scale) / 2f;   // 居中
    }
    public static void AuditOverflow(CanvasLayer layer, string sceneName) { ... }
}
```

三个关键事实：

1. **`BaseWidth`/`BaseHeight` 是 `const`** —— 编译期常量，**全程序唯一**，
   所有场景（`LoginScene`、`SelectScene`、以及非 legacy 的现代 UI）**共用**。
2. **`UpdateScale` 用它做"缩放 + 居中"** —— 即基准也决定画面在窗口中的**居中偏移**。
3. **已有 `AuditOverflow(layer, sceneName)`** —— 会报告控件矩形是否超出基准
   （`End.X > BaseWidth + 2` 等），并打印"超出 右+N / 下+N"。**这是一个现成的审计工具。**

### 由此产生的结论（改动方案需重新设计）

**直接把 `1024×768` 改成 `800×600` 是错的** —— 那会同时改变：

- 选角屏（legacy，应为 800×600）；
- **登录页**（legacy，若其坐标也是 800×600 系则一致，需核对）；
- **非 legacy 的现代 UI**（现代 UI 的布局是按 1024×768 设计的，改了会全乱）。

**正确做法**：引入**按场景可设的基准**，legacy 场景用 800×600、现代场景保持 1024×768。
这是**一项功能改动**，不是改常量。

### 本轮不改代码

理由：改动需要新增"按场景基准"的机制，涉及 `UiScaler` 的公共接口与所有调用方
（`LoginScene`、`SelectScene`、现代场景），**影响面远大于我前两轮的估计**。
在没有把调用方逐一列清、并确认现代 UI 不受影响之前动手，会引入大范围回归。

### 下一步（下一轮）

1. **列出 `UiScaler.BaseWidth`/`BaseHeight`/`UpdateScale` 的全部调用方**；
2. 设计按场景基准的最小改动（例如给 `UpdateScale` 加可选基准参数，
   legacy 传 800×600，其余取默认）；
3. 核实**登录页坐标是否也是 800×600 系**（登录矩形 `(128,440)`、按钮 `(459,436)` 等）
   —— 若登录页也是 800×600 系，则 legacy 两个场景统一用 800×600，改动理由更充分；
4. 改完跑 `AuditOverflow` 看有无新溢出，再截图验证。

## 查证结果：`UiScaler` 只被两个 legacy 场景使用（2026-09-27）

按"不再估影响面、先查证"的要求，用 grep 列出全部调用方（不靠记忆）：

```
SelectScene.cs:114   UiScaler.UpdateScale(_uiLayer, GetViewport());
SelectScene.cs:119   GetViewport().SizeChanged += () => UiScaler.UpdateScale(...);
LoginScene.cs:84     UiScaler.UpdateScale(_uiLayer, GetViewport());
LoginScene.cs:90     GetViewport().SizeChanged += () => UiScaler.UpdateScale(...);
SelectScene.cs:624   var viewport = new Vector2(UiScaler.BaseWidth, UiScaler.BaseHeight);
LoginScene.cs:427    Vector2 viewport = new Vector2(UiScaler.BaseWidth, UiScaler.BaseHeight);
```

**全部调用方只有这两个场景类，共 6 处。**

### 而且这两个场景整体就是 legacy

`SelectScene._Ready()`：

```csharp
110  // 2 倍 UI 缩放：DX 旧版 UI 挂到缩放层，窗口放大时跟随缩放。
111  _uiLayer = new CanvasLayer { Name = "UiScaleLayer" };
112  AddChild(_uiLayer);
113  BuildLegacySelectUi();          // **无条件调用，没有分支**
114  UiScaler.UpdateScale(_uiLayer, GetViewport());
115  // 调试审计：ZIRCON_UI_AUDIT=1 时列出所有超出逻辑画布的控件
116  if (System.Environment.GetEnvironmentVariable("ZIRCON_UI_AUDIT") == "1")
117      UiScaler.AuditOverflow(_uiLayer, "SelectScene");
```

`BuildLegacySelectUi()` **无条件执行** ⇒ **该场景整体是 legacy 布局**，不存在"同一个场景里
现代 UI 与 legacy UI 并存、需要按模式切换基准"的情况。

### 更正我上一轮的结论

我上一轮说"直接改 `UiScaler` 常量会影响**现代 UI**，所以需要新增按场景基准的机制"。
**该结论错误**：

- `UiScaler` **只被这两个 legacy 场景使用**，没有任何"现代 UI"调用它；
- 因此**不需要**引入按场景基准的机制 —— 直接把基准改为 800×600 即可，
  影响面就是这两个场景**本身**（而它们本来就该是 800×600 系）。

**错误性质**：我在没 grep 的情况下估了影响面，把"全局 `const`"直接等同于"全局影响"。
`const` 只说明**值唯一**，不说明**被谁使用** —— 这两件事我混为一谈了。

### 附带：已有可用的验证工具

`UiScaler.AuditOverflow(layer, sceneName)`，由环境变量 `ZIRCON_UI_AUDIT=1` 触发，
会打印"超出 右+N / 下+N"。**改完基准后可用它检查有无新溢出。**

### 下一步（下一轮实施）

1. 把 `UiScaler.BaseWidth/BaseHeight` 从 `1024×768` 改为 **`800×600`**；
2. 同时**保持**：F50 仍画 (0,0)、不拉伸不居中；角色模型中心仍是 (640×480 图的中心 (320,240))；
   所有原版控件坐标数值不变；
3. 用 `ZIRCON_UI_AUDIT=1` 跑一次看有无溢出；
4. 截图 + OCR 验证按钮位置是否从"偏左上"回到原版比例；
5. `--legacy-select-selftest` 复验属性未变。

## 已实施：逻辑画布基准 1024×768 → **800×600**（2026-09-27，含验证）

`GodotClient/Scripts/UiScaler.cs`：

```csharp
public const float BaseHeight = 600f;   // 原 768f
public const float BaseWidth  = 800f;   // 原 1024f
```

并附注释记录依据（`0x45FD50` 的立即数裁剪边界 0x320/0x258 = 800/600 + 该函数是全局绘制
入口；本类只被两个 legacy 场景使用）、以及**三条"不要顺手改"**警告
（F50 仍 (0,0) 不拉伸；模型中心仍 (320,240)；控件坐标数值不变）。

### 验证（非仅 build）

1. **属性自检**：
   `[LegacySelectButtonSelfTest] PASS 9 个按钮的 Index/HoverIndex/Location/Size 全部匹配 EI ctor 实参`
   —— 基准改动**未影响**任何控件属性。
2. **缩放日志**：
   ```
   改动前: [UiScaler] scale=1.7135416 viewport=(2028,1316) offset=(136.67, 0)
   改动后: [UiScaler] scale=2       viewport=(2028,1316) offset=(214, 58)
   ```
   800×600 × 2 = 1600×1200，偏移 (214,58) ⇒ 右界 1814 ≤ 2028、下界 1258 ≤ 1316，
   **4:3 与窗口匹配**。此前 1024×768 的纵向偏移为 0（顶满高度）。
3. **截图 + OCR**（`dim ocr recognize`）读出四个按钮，且**顺序符合原版坐标关系**：
   ```
   开始游戏   <- (259,49)  更高更左
   创建角色   <- (440,93)
   删除角色   <- (79,243)
   结束       <- (28,438)
   ```
   改动前同样四钮可见但整体贴向画布左上；改动后落点回到 800×600 的比例位置。

截图归档 `.artifacts/legacy-800x600-base-2026-09-27/select_800.png`。

### 说明：本次是"基准"修正，不是"坐标"修正

四钮坐标数值**一个字都没改**（它们一直与 EI ctor 实参一致）；改的是**画布基准**。
这与前几轮的结论自洽：逐个坐标都对、整体比例不对 —— 问题在参照系不在数值。

## 登录页在 800×600 基准下的验证（2026-09-27，通过）

基准改动同时影响 `LoginScene`（两者共用 `UiScaler`），故必须单独验证登录页。

### 验证

1. **缩放日志**：`[UiScaler] scale=2 viewport=(2028, 1316) offset=(214, 58)` —— 与选角屏一致。
2. **截图 + OCR**（`dim ocr recognize`）读出登录页**全部控件**：

```
ZirconClient （DEBUG）
WEMADE ENTERTAINMENT
PRESENTS
创建帐号
修改密码
结束
连接游戏
test@test.com
正在连接服务端…
```

**无裁切、无错位。** 账号字段 `test@test.com` 也正常渲染。

### 结论

登录页的坐标（账号/密码框 `(128,440)` / `(326,440)`，连接钮 `(459,436)`，
创建帐号/修改密码/结束三钮 `(139,379)` / `(279,379)` / `(439,379)`）
**同样属于 800×600 系** ⇒ 基准改为 800×600 对**两个 legacy 场景同时正确**，
不需要分别处理。

截图归档 `.artifacts/legacy-800x600-base-2026-09-27/login_800.png`。

### 至此"坐标系"这一条完全闭合

- ✅ EI 屏幕 = 800×600（`0x45FD50` 的立即数裁剪边界 + 23 个调用者）
- ✅ 我方基准已改为 800×600（仅影响两个 legacy 场景，已 grep 查证）
- ✅ 选角屏验证通过（属性自检 PASS + 缩放日志 + 截图 OCR 四钮顺序正确）
- ✅ 登录页验证通过（缩放日志 + 截图 OCR 全部控件正常）

## 四个 mp3 的时长分类：两支 BGM + 两支过场音（2026-09-27）

82 机器 `/home/tetsuya/mir2ei/Sound/` 下与选角/创建相关的音频（均已取回
`LegacyEI/Sound/`）：

| 文件 | 时长 | 字节 | 类别 |
|---|---|---|---|
| **`SelChr.mp3`** | **28.226 s** | 451611 | **BGM**（phase 0/3） |
| **`CreateChr.mp3`** | **27.626 s** | 442020 | **BGM**（phase 2） |
| `StartGame.mp3` | **3.340 s** | 106880 | 短过场音 |
| `ToCreateChr.mp3` | **2.035 s** | 65130 | 短过场音 |
| `SelChr.wav` | 1.977 s（早前实测） | 357876 | **短音效**（与 mp3 **不同**） |
| `CreateChr.wav` | 1.977 s（早前实测） | 348844 | **短音效**（与 mp3 **不同**） |
| `StartGame.wav` | — | 580692 | — |

### 结论：`.mp3` 与同名 `.wav` 是**两类不同的东西**

同一名字下 `.mp3` 与 `.wav` **时长差一个数量级**（28 s vs 2 s），**不是同一音的两种编码**。

- **`.mp3`（~28 s）= 该阶段的 BGM**：与 `0x457AB0`/`0x4577A0` 里"进入后播一次、
  1000 ms 后复位开关"的逻辑吻合（播一次长曲子；开关复位只是表示"已播过"）；
- **`.wav`（~2 s）= 一次性短音效**：与按钮点击等即时反馈吻合。

### 对音效归属表的最终修正

| 触发 | 资源 | 依据 |
|---|---|---|
| **phase 0 / 3 进入** | **`SelChr.mp3`（BGM）** | `0x4577C0` + `0x47D624` |
| **phase 2 进入** | **`CreateChr.mp3`（BGM）** | `0x457AE6` + `0x47D690` |
| 点「创建角色」(F51) | `CreateChr.wav` 或 `ToCreateChr.mp3`（短音）**二者之一，未定** | `0x459AB6` 附近（早期记录，未记资源名） |
| 进游戏 | `StartGame.*`（短音 / 3.3 s 过场） | 进入游戏路径 |

**`ToCreateChr.mp3`（2.035 s）此前从未出现在任何记录里** —— 名字意为"前往创建角色"，
与 F51 → phase 1 的过渡吻合，**但尚无引用点证据**，只登记存在性与时长，不据此接线。

### 仍未解决：我方缺"按 phase 跑每帧更新"的循环

两个 BGM 的证据都指向同一个机制：

```asm
; phase 0/3 与 phase 2 同构
cmp  eax, 0x3E8          ; 累计 > 1000ms
jbe  skip
push <该阶段的 mp3 名>
mov  ecx, 0x8AB130       ; 音频管理器
call 0x45B390
mov  dword [本阶段+0x1160], 0   ; 复位开关
mov  dword [本阶段+0x1164], 0
```

**我方 `SelectScene` 没有承载这个的每帧更新**（无 `_Process` 里的 phase 计时）。
下一轮应加该循环，并按上表接线两个 BGM（进入 phase 时播一次、1000 ms 后标记已播）。

**注意**：Godot 能否直接解码 `.mp3` 尚未验证；若不能，需转 `.ogg`（本机 ffmpeg 缺
`libvorbis`，需另找工具。）

## 已实施：相位 BGM 计时循环（2026-09-27，含运行验证）

这是此前反复出现的"结构性缺失"——我方没有按相位跑的每帧更新，导致两个 BGM 无处承载。
本轮补上。

### 实现

`GodotClient/Scripts/SelectScene.cs`：

- 新增字段 `_phaseBgmArmed` / `_phaseBgmAccumMs`（镜像 EI 的 `+0x1160` / `+0x1164`）；
- `SetSelectPhase(phase)` 里**换相位即重新武装**：`_phaseBgmArmed = true; _phaseBgmAccumMs = 0;`
  （对应原版进入相位时把 `+0x1160` 置 1、`+0x1164` 清 0）；
- `_Process(delta)` 首部调用 `TickPhaseBgm(delta)`；
- `TickPhaseBgm`：未武装则返回；累加 `delta * 1000`；**超过 1000 ms** 才复位开关
  （`false` + 清零）并按相位播 BGM：

| 相位 | BGM | 证据 |
|---|---|---|
| 0 或 3 | `LegacySelChrBgm` | `0x4577C0` + `0x47D624` |
| 2 | `LegacyCreateChrBgm` | `0x457AE6` + `0x47D690` |
| 其他 | 不播 | — |

**注意**：既有的 `_Process` 已用于角色叠加层绘制，故把计时**合并进**它
（首次实现时我新建了同名方法，编译报 CS0111 重复定义 —— 已改为调用 `TickPhaseBgm`）。

### 运行验证

```
[LegacySelect] phase=2 (0=列表/1=创建中/2=动画列表/3=等待/4=进游戏)
[LegacySelect] 相位 BGM: phase=2 -> LegacyCreateChrBgm
```

- 日志确认 `phase=2` 触发 `LegacyCreateChrBgm`；
- 日志**无音频加载错误**（无 FileNotFound / LoadFromFile 失败）；
- 两个资源就位（`SelChr_bgm.wav` 1247694 B / `CreateChr_bgm.wav` 1221198 B）。

### 限制（如实记录）

**"播放成功"未在音频层面验证得到** —— 日志只证明走到了播放调用且无加载错误，
不能证明**扬声器有声**。无头环境下无法做音频回放验证。若要更强证据，需在
有音频设备的会话里跑，或检查 `AudioStreamPlayer.Playing` 状态。
**本轮不宣称"音效已验证发声"，只宣称"调用链与资源加载无错"。**

## phase 2 绘制点追查：帧 81（164×88 面板）@ (247,384)；帧 84（128×16）疑为输入条（2026-09-27）

### 对齐后的反汇编（上一节的反汇编起点错位，这里取对齐边界重读）

```asm
0x457D69  push 0x1e0(480) / push 0x280(640) / push ecx / push edx / push eax
0x457D76  push 0x1b2(434) / push 0xc9(201)
0x457D80  mov ecx, 0x8ab7a8
0x457D85  call 0x460cb0            ; 另一个绘制入口（参数含 640x480 与 (201,434)）
0x457D8A  push 0x51(81)            ; **帧号 81**
0x457D8C  mov ecx, esi
0x457D8E  call 0x466130            ; 帧调度
0x457D93  test eax, eax
0x457D95  je  0x457dcb
0x457D97  mov eax, [ebx+0x184] / mov ecx, [ebx+0x188]
0x457DAD  movsx edx, word [eax+2] / movsx eax, word [eax]   ; 取帧宽高
0x457DB4  push ecx / push edx / push eax
0x457DB7  push 0x180(384) / push 0xf7(247)
0x457DC1  mov ecx, 0x8ab7a8
0x457DC6  call 0x45fd50            ; **画帧 81 于 (247,384)**
```

**结论：phase 2 在 (247,384) 绘制的是 `Interface1c` 的帧 81。**

### 解码 78-85 帧（素材编辑器同一资源）

| 帧 | 尺寸 | 备注 |
|---|---|---|
| 78 | **空帧** | — |
| 79 | **空帧** | — |
| **80** | **640×480** | 全屏背景候选 |
| **81** | **164×88** | **phase 2 在 (247,384) 画的就是它** |
| **82** | **256×32** | 长条 |
| 83 | **空帧** | — |
| **84** | **128×16** | **输入条尺寸**，与登录页输入框（99×14）同量级 |
| 85 | **28×28** | 圆钮 |

`dim ocr recognize` 对这些帧输出**空字符串** ⇒ 它们是**纯图形帧，无文字**。

### 推断（标注为推断）

- **帧 81（164×88）**是一个**中等面板**，画在 (247,384) —— 该位置在角色槽下方。
  它**可能**是"创建/删除角色"的输入面板底板，即 PRE-16 所说「密码 EDIT」所在的那个
  容器。**但帧本身无文字，本轮不能据此断言它承载的是密码输入。**
- **帧 84（128×16）**的尺寸与"单行输入框"高度吻合，是输入框底图的**候选**。
  **尚未找到任何代码引用它** ⇒ 与 `+0x932` 族一样，**不能仅凭尺寸就断言用途**。

### 未闭合

- **帧 81 与 84 是否属 phase 2 的输入控件**：需要**引用点证据**（谁用这两个帧
  构造控件、谁读它的内容），**本轮未找到**。
- 因此 **PRE-16 的「密码 EDIT」仍未定位** —— 本轮把范围缩小到"帧 81/84 这两个候选"，
  但没有直接证据。

### 下一步（明确）

1. **找帧 81 与 84 的引用点**（`push 0x51` / `push 0x54` 形式的常数，或写入控件帧号字段的地方）
   —— 与 `+0x932` 族的教训一致：**尺寸/形态相似不等于用途**，必须有引用点。
2. 若找到帧 84 被用作某控件的帧号、且该控件被读取（取输入值），即可确认是输入框。

## 帧 81/84 的引用点：属另一个窗口的按钮组，**不是**选角屏密码框（2026-09-27）

### 扫描（含自测）

按 `push imm8`（`6A xx`）扫 `push 0x51` / `push 0x54`：

```
push 0x51(81) 共 7 处: 0x40CE8B 0x422E09 0x4279A7 0x427C45 0x429CBE 0x42CC7C 0x457D8A
push 0x54(84) 共 3 处: 0x427A11 0x427DBD 0x42CE90
```

**先记一次工具错误**：首次我用 `68`（push imm32）扫，报 **0 命中** —— 因为 `0x51=81 < 128`，
编译器用 `6A 51`（push imm8）。**而且我当时又没有先自测**（正是我刚立下的规则），
是靠"我知道 `0x457D8A` 就是 `push 0x51`"才发现矛盾。修正后自测 PASS 再扫。

### 读到引用点后：**不是选角屏**

`0x427980` 区段是**另一组按钮的构造**，用**同一个 ctor `0x417550`**：

```asm
0x4279A7  push 0x51(81) / push 0x50(80) / lea ecx,[esi+0x567c] / call 0x417550
0x4279DB  push 0x53(83) / push 0x52(82) / lea ecx,[esi+0x5730] / call 0x417550
0x427A0F  push 0x55(85) / push 0x54(84) / lea ecx,[esi+0x57e4] / call 0x417550
0x427A42  ... lea ecx,[esi+0x5898] ...                                ; 按钮#4
```

三点判定：

1. **对象偏移完全不同** —— `esi+0x567c` / `+0x5730` / `+0x57e4` / `+0x5898`，
   **不是**选角屏对象的 `+0x9E8` / `+0xD38` / `+0x10E4` 族 ⇒ **属于另一个窗口**。
2. 帧号**成对**出现（81/80、83/82、85/84），相邻两帧一组 ⇒ 形态是 (normal, hover)
   之类的二态对。
3. 但 `0x50`(80) 恰好是**那个 640×480 背景帧**，作为按钮 hover 帧不合理 ⇒
   那两个数**不是** (hover, normal)，其字段语义仍不明。

### 结论：这条线索**出局**

**帧 81/84 不属于选角屏**（对象偏移不同）。因此：

- 上一节"帧 84 是选角屏输入框底图候选"的推断**被否定**；
- 选角屏自己在 `0x457D8A` 处的 `push 0x51` 是**帧调度（绘制帧 81）**，不是构造控件；
- **PRE-16 的「密码 EDIT」仍未定位**，且**本轮又排除了一条**。

### 教训

**"尺寸/形态相似"与"被同一 ctor 构造"都不足以判定用途** —— 必须看**对象归属**
（哪一族偏移）。`0x417550` 是**通用按钮 ctor**，被整个程序多处复用；
只看"也调这个 ctor"就会误判成同一屏的控件。这与 `0x449C50` 的教训同源。

### 下一步（改向，不再追帧号）

帧号线索已耗尽（81/84 出局）。改为**从 phase 2 的绘制清单反向枚举控件**：
phase 2 的更新函数 `0x457AB0` 里除了画背景/槽/按钮，还调了 `0x4586F0`（`0x457B42`）。
**反汇编 `0x4586F0`** —— 它尚未读过，可能正是"画 phase 2 的控件组（含输入框）"的地方。

## `0x4586F0` = 画选中槽的角色 3D 模型；**密码 EDIT 标为受证据限制**（2026-09-27）

### `0x4586F0` 的判定

```asm
0x4586F0  sub esp, 0x6c
0x4586F7  mov eax, [esi+0x1488]      ; 选中槽
0x4586FD  cmp eax, -1 / je 0x4588fb  ; 无选中 -> 返回
0x458706  cmp eax, 2  / jge 0x4588fb ; 越界 -> 返回
0x45870F  mov eax, [esi+0x148c]      ; 另一索引
0x458715  push 0x3f800000            ; 1.0f
0x45871A  push 0x3e48c8c9            ; **0.196f（缩放常量）**
0x458724  lea eax, [eax+eax*8]
0x458731  lea ecx, [eax+eax+0x23]    ; idx*18 + 0x23
0x458755  mov dword [esp+0x28], 0x42c80000   ; 100.0f
0x45875D  mov dword [esp+0x2c], 0x41700000   ; 15.0f
0x458773  mov dword [esp+0x1c], 0x43e10000   ; 450.0f
0x458787  call 0x466800              ; 3D 绘制
```

**判定：这是"绘制选中槽的角色 3D 模型"**，不是控件组的绘制。

**附带印证**：这里的缩放常量 `0x3e48c8c9`（0.196f）与此前在别处查到的**完全一致**
（早期证据：调用者 `0x457A71` 传 scale `0x3e48c8c9`=0.196）—— 两处独立命中同一常量，
互相印证。

### 结论：PRE-16 的「密码 EDIT」**静态分析未能定位**

已排除的线索（**三条**）：

| 线索 | 排除依据 |
|---|---|
| `+0x932` / `+0x938` / `+0x93E` 三结构 | 全 `.text` **只写不读**，不可能是输入框 |
| 帧 81 / 84（含 128×16 候选） | 引用点在 `esi+0x567c` 族 ⇒ **属另一窗口** |
| `0x4586F0`（phase 2 最后未读绘制路径） | 是**画 3D 模型**，非控件组 |

**以及**：phase 2 的全部绘制调用（`0x45FD50` 的 5 个选角屏调用点）已逐一读过：
`0x457813`（槽区）、`0x45794B`、`0x457B3B`（背景+帧 81）、`0x457C91`（动画帧）、
`0x457DC6`（帧 81 + 遍历五钮）。

**没有一条指向输入框。**

### 处置（按上一轮的承诺）

**把 PRE-16 的「密码 EDIT」标为"受证据限制、当前静态分析无法定位"**，不再继续追。
理由：

1. 三条独立线索均已排除，且每条都用了不同的判据；
2. 继续在同一方向上猜测，等于用猜测填补证据空白（本任务反复出现的错误模式）；
3. 该控件若要定位，可能需**运行期调试**（在 EI 里查看 phase 2 时的控件对象表），
   而这超出当前静态工具链的能力。

**我方实现中不新增任何"密码框"** —— 因为不知道它的帧、坐标、尺寸与用途，
新增出来必然是错的（与"撤掉接错的 SelChr"同理：宁缺勿错）。

### 转向

剩余可推进项里，**不再有受此阻塞的**：

- 音效"实际发声"验证 —— 需带音频设备环境；
- 登录页 phase 2 服务器列表 —— 需运行期材料；
- phase 2 五钮动作 —— 需运行期指针。

**即：登录流程中，凡静态分析能闭合的，已全部闭合或明确标注受限。**

## 新线索：`0x4278xx`-`0x427Axx` 区域是「14px 高的成对帧控件行」= 表单候选（2026-09-27）

### 起因：换思路找"输入框控件类"的构造模式

前三轮都在选角屏自身找密码框（三条线索全排除）。本轮换思路：
**先看登录页已知的输入框（`(128,440)-(227,454)`、`(326,440)-(425,454)`）是怎么构造的**，
拿到"输入框类"的构造模式，再拿同一模式回选角屏找。

### 扫描结果

- **`push 99`（宽度 99）= 0 处** —— 说明矩形参数不是 (x, width) 形式，
  很可能是 **(left, right)**（`227-128 = 99`），或宽度来自别的常量。
- **`push 14`（高度 14）= 13 处**，其中 `0x4278FC` **落在上一轮那组按钮的同一区域**
  （`0x4279A7` 起构造 `esi+0x567c`/`+0x5730`/`+0x57e4`/`+0x5898` 四个按钮）。

### 该区域的特征（合并两轮观察）

```
0x4278FC  push 14                          ; **行高 14**
0x4279A7  push 0x51(81) / push 0x50(80)    -> call 0x417550   ; 成对帧
0x4279DB  push 0x53(83) / push 0x52(82)    -> call 0x417550
0x427A0F  push 0x55(85) / push 0x54(84)    -> call 0x417550
0x427A42  ... esi+0x5898 ...                                ; 第 4 个
```

- **`push 14`（行高）+ 多组成对帧（81/80、83/82、85/84…）**；
- 帧 `0x54`(84) 实测 **128×16**，与"14 px 行高 + 边框"吻合；
- 帧 `0x51`(81) 实测 **164×88**，与"14 px 行高"**不吻合** —— 这一点**未解释**，
  说明那两个数**不是 (frame, height)** 的简单对应。

### 为什么这可能是登录页的输入框

**形态吻合**：登录页的账号/密码框是 `(128,440)-(227,454)` 与 `(326,440)-(425,454)`
—— **高 14**、宽 99、两个并排（相距 198）。而这里**行高 14 + 多个成对帧控件**。

**但尚未证实**：本轮**没有**确认 `0x4278xx` 区域的对象属于**登录对象**
（登录对象是 `0x8A9520`，而这些控件的宿主对象偏移是 `esi+0x567c` 族，**身份未核**）。

### 与上一轮结论的关系

上一轮我判定"帧 81/84 属另一窗口 ⇒ 不是选角屏密码框"—— **该判定仍然成立**
（对象偏移不同）。本轮的新增点是：**那个"另一窗口"很可能就是登录页/账号表单**，
而不是随便什么窗口。若成立，则：

- 帧 81/84 的正确归属是**登录页的输入控件**（而不是选角屏的）；
- **选角屏的密码 EDIT 仍需单独定位**（它可能有自己的帧号）。

### 下一步（明确且可执行）

1. **确认宿主对象身份**：查 `esi+0x567c` 族属于哪个类 —— 若其类就是登录对象
   （`0x8A9520` 或其基类），则帧 81/84 属登录页输入框，本轮线索**归位**；
2. 若确认，则**核对登录页我方输入框的帧号**是否就是 81/84（我方现用 `Interface1c`
   的哪些帧？）—— 这可能直接修正登录页；
3. 之后再看选角屏是否有**同一 ctor 的另一次调用**（用不同帧号），即它的密码框。

**注**：本轮仍未在选角屏找到密码框；但把"帧 81/84"从"无关窗口"提升为"很可能是登录页
输入框"，这是一条**可验证**的进展。

## 输入框的构造模式：`SetRect(偏移, left, top, right, bottom)`（2026-09-27 取得关键模式）

### 来源：我方代码里已记录的原版证据

`GodotClient/Scripts/LoginScene.cs` 的注释（此前从原版取得、已落地）：

```
///   账号输入  SetRect(+0xF44, 0x80, 0x1B8, 0xE3, 0x1C6) = (128, 440) - (227, 454)，99x14
///   密码输入  SetRect(+0xF54, 0x146, 0x1B8, 0x1A9, 0x1C6) = (326, 440) - (425, 454)，99x14
```

### 参数解读（本轮核实）

| 实参 | 值 | 含义 |
|---|---|---|
| `+0xF44` / `+0xF54` | — | **控件在宿主对象中的偏移**（账号 / 密码） |
| `0x80` / `0x146` | 128 / 326 | **left** |
| `0x1B8` | 440 | **top**（两者共用） |
| `0xE3` / `0x1A9` | 227 / 425 | **right** |
| `0x1C6` | 454 | **bottom**（两者共用） |

⇒ 原版输入框由 **`SetRect(偏移, left, top, right, bottom)`** 设置，
**矩形用 (left, top, right, bottom) 而不是 (x, y, w, h)**。

### 这解释了我前几轮的一个失败

我扫 `push 99`（宽度）得 **0 处** —— 因为**宽度不是参数**，`227-128 = 99`
是由 left/right 之差隐含的。**又一次"用错误的参数形态去搜索"**。

### 对"选角屏密码框"的新方向

**正确的搜索目标**是：

1. **`SetRect` 函数本体**（先把它的签名/调用形式确认下来）；
2. 在**选角屏对象**（`esi` = 选角对象，字段族 `+0x9E8`/`+0xD38`/`+0x10E4`…）上
   找 `SetRect(偏移, l, t, r, b)` 的调用，**偏移落在 `+0x9xx` 或 `+0x1xxx` 区**；
3. 其中 **`top`/`bottom` 之差约 14** 的那些即"输入行"候选。

**注意**：上一轮那组 `0x4279A7` 的按钮（`esi+0x567c` 族）**不是** `SetRect` 调用
（它们是 `call 0x417550` 按钮 ctor），所以**登录页的输入框与那组按钮不是同一批**
—— 这一点此前混在一起了，现予澄清。

### 下一步

1. 找到 `SetRect` 本体（可从 `LoginScene` 注释里那两条记录的调用点反查，
   或从 `0x8A9520` 登录对象的构造里找）；
2. 用它作为"控件构造模式"的锚点，重扫选角屏对象的输入行。

## 决定性：`SetRect` 的调用形式与两个登录输入控件（2026-09-27）

用登录页输入框的已知实参反查：`push 0x1C6`(454) 全 `.text` **只有 4 处**（自测先过），
其中 `0x40284A` 与 `0x402867` **相邻两次调用**，正是两个输入框（bottom 共用 454）。

```asm
; --- 账号输入框 ---
0x40284A  push 0x1c6(454)          ; bottom
0x40284F  push 0xe3(227)           ; right
0x402854  push 0x1b8(440)          ; top
0x402859  lea  ecx, [ebx+0xf44]    ; &控件（账号）
0x40285F  push 0x80(128)           ; left
0x402864  push ecx                 ; 目标指针
0x402865  call ebp                 ; **间接调用 SetRect**

; --- 密码输入框 ---
0x402867  push 0x1c6(454)          ; bottom
0x40286C  push 0x1a9(425)          ; right
0x402871  push 0x1b8(440)          ; top
0x402876  lea  edx, [ebx+0xf54]    ; &控件（密码）
0x40287C  push 0x146(326)          ; left
0x402881  push edx
0x402882  call ebp                 ; **间接调用 SetRect**
```

### 结论 1：`SetRect(偏移, left, top, right, bottom)` 参数顺序**完全确认**

与我方 `LoginScene.cs` 注释里原先记录的**逐值一致**（`(128,440)-(227,454)`、
`(326,440)-(425,454)`）。**该记录此前是本仓库里可信度最高的一条**（现在有了调用点证据）。

### 结论 2：`call ebp` —— `SetRect` 是**间接调用**

两个调用都是 `call ebp`（函数指针），**不是直接 `call <地址>`**。

⇒ **这解释了我此前搜不到 SetRect 的另一半原因**：不是"没找到",而是**它根本不以直接调用形式出现**。
（前一半原因是矩形参数是 (left,right) 而非 (x,w)。）

### 结论 3（对后续搜索的价值）

**选角屏的输入控件会用同样的 `call ebp` 模式**。因此正确做法：

1. 找**同一函数里 `lea reg,[ebx+偏移]` 后紧跟 `push` ×4 + `call ebp`** 的模式；
2. 该函数的 `ebp` 在被调函数开头加载（需确认来源，可能是导入/运行期指针）；
3. **偏移** 与 **left/top/right/bottom** 给出控件位置；**top/bottom 之差约 14** 的即输入行。

而在**选角屏对象**（`esi`，字段族 `+0x9E8`/`+0xD38`/`+0x10E4`/`+0x1488`）里，
用该模式找即可 —— **不再依赖帧号或尺寸猜测**。

### 附带：登录页构造里还看到两个 `call 0x417550`

```asm
0x402823  call 0x417550    ; 控件在 ebx+0xbd0，push 0xf 作参数
0x402845  call 0x417550    ; 控件在 ebx+0xc84
0x402884  push 0x14 / mov ecx,0x8aa488 / call 0x4511d0
```

即登录页也有两个用**同一通用按钮 ctor** 的控件（`ebx+0xbd0`、`ebx+0xc84`）。
这与"`0x417550` 是通用 ctor、整个程序复用"的判断一致。

## 强证据：选角屏**没有** SetRect 形式的输入控件构造（2026-09-27，与 PRE-16 冲突）

### 扫描

按已确认的形式（`lea reg,[基址+偏移]` + 4×`push` + `call ebp`）扫描：

```
call ebp 全 .text: 88 处（通用间接调用，本身不具区分度）
选角屏区 0x456000-0x45A200: **仅 2 处** —— 0x4598AE, 0x459965
登录页区 0x402000-0x403000: 4 处 —— 0x40210C, 0x4027B9, 0x402865, 0x402882
```

（自测先过：确认能找到已知的 `0x402865`。）

### 选角屏那 2 处 `call ebp` 的身份已知

- `0x4598AE`：在 `0x459898`-`0x4598B4` 循环内 = **phase 0 的角色槽命中测试**
- `0x459965`：在 `0x45994F`-`0x4599A1` 循环内 = **phase 2 的角色槽命中测试**

**两处都是命中测试，不是 `SetRect`。** 二者的调用形态也不同（命中测试是
`push y / push x / push 对象 / call ebp`，不是 4 个矩形参数）。

### 结论

**选角屏没有用「4 个矩形参数 + call ebp」模式构造的输入控件。**

对照登录页：它**同时**有 2 处 `call 0x417550`（按钮）**和** 2 处 `SetRect`
（`0x402865`/`0x402882`，两个输入框）。**选角屏只有前者、没有后者。**

### 与 PRE-16 冲突

PRE-16 记：「完成后进入 phase2，播放角色列表动画并显示 F92/F95/F98/F86/F89 五控件
**与密码 EDIT**」。

但本轮的扫描显示：**选角屏代码区没有任何输入控件的构造**。两者不能同时成立。
可能的解释（**均未判定**）：

1. **PRE-16 的"密码 EDIT"是误记** —— 可能把**登录页**的输入框（账号/密码，
   `+0xF44`/`+0xF54`）误当作选角屏 phase 2 的控件。**本轮证据支持这一解释**：
   选角屏对象里没有对应的构造；
2. 该 EDIT 由**别处的通用代码**创建（不经选角屏自己的构造）—— 但那样它就不会是
   选角屏对象的一部分，与"显示在 phase 2"矛盾；
3. 它由**运行期动态创建**（静态不可见）。

### 处置

**倾向解释 1，但不裁定。** 理由：本轮证据只证明"选角屏代码区没有该模式"，
不能证明"PRE-16 记错"。**两个来源冲突且都有依据时，不擅自采信一方。**

**但仍不改我方实现**（不新增密码框）—— 因为：

- 若解释 1 成立 ⇒ 本来就不该有；
- 若解释 2/3 成立 ⇒ 缺构造证据，加出来必错。

### 下一步

**回查 PRE-16 的原始出处**：本文档 PRE-16 记录引用了
`login-flow-evidence.json::screens.parent.phase`。应**直接查该 JSON 的原文**，
看它到底怎么描述"密码 EDIT"—— 是 `screens.parent` 的字段，还是别的对象的字段。
**这可能一次性消解冲突**（关键在于该 EDIT 属于哪个对象）。

## 冲突消解：PRE-16 的「phase 2 密码 EDIT」是误记（2026-09-27 定论）

### JSON 原文

`Mir3-Research/docs/research/ei-ui-layout/login-flow-evidence.json`：

```json
.screens.char_select.edit_control => {
  "va": "0x402884",
  "detail": "0x4511D0(0x8AA488, 0x14) creates the child EDIT (class 0x47C438 'EDIT');
             account rect SetRect(+0xF44, 0x80, 0x1B8, 0xE3, 0x1C6)=(128,440,227,454),
             password rect SetRect(+0xF54, 0x146, 0x1B8, 0x1A9, 0x1C6)=(326,440,425,454)"
}
.screens.char_select.login_fields.password => { "field": "+0xE3D", "detail": "password string" }
```

### 与反汇编的对应（本轮已独立读到）

`va` 指向的正是我上一轮读到的位置：

```asm
0x402884  push 0x14 / mov ecx, 0x8aa488 / call 0x4511d0    ; 创建 EDIT 子控件
```

并紧邻此前的两个 `SetRect`：

```asm
0x402865  call ebp    ; SetRect(ebx+0xF44, 128,440,227,454) —— 账号
0x402882  call ebp    ; SetRect(ebx+0xF54, 326,440,425,454) —— 密码
```

**三者（`0x402865` / `0x402882` / `0x402884`）在代码上连续，是同一批控件的构造。**

### 消解：该 EDIT 属**登录表单**，不属 phase 2

1. `class 0x47C438 'EDIT'` 由 `0x4511D0(0x8AA488, 0x14)` 创建；
2. 其宿主偏移是 `+0xF44` / `+0xF54`，而 **`+0xF44`/`+0xF54` 是登录对象**（`0x8A9520`）
   的字段 —— 该对象在 `0x402xxx` 区构造（构造代码就在 `0x402823`/`0x402845`/`0x402865`
   `0x402882`/`0x402884` 这段）；
3. JSON 把它放在 `.screens.char_select` 下，但**该键名不能按字面理解** ——
   本文档 PRE-09 早已警告：
   > `phase` 字段明确 **phase1 = login form**（**不能仅凭 JSON 键名 `char_select`
   > 推成登录后的选角页**）
4. 本轮的独立扫描（选角屏区 `0x456000`-`0x45A200` 仅有 2 处 `call ebp`，且都是角色槽
   命中测试、**没有** SetRect 形态）与"选角屏无输入控件"**一致**。

### 结论

**PRE-16 中「完成后进入 phase2，…并显示 F92/F95/F98/F86/F89 五控件与密码 EDIT」
里的「密码 EDIT」是误记** —— 它实际是**登录表单的账号/密码 EDIT**（`+0xF44`/`+0xF54`），
被 `char_select` 这个键名带到了 phase 2 的描述里。

**我方不需要在 phase 2 实现任何密码框。** 此时：

- 加密码框 = 凭空造出原版没有的控件（错误）；
- 不加 = 与证据一致（正确）。

**此前连续多轮追查「phase 2 密码 EDIT」的结论：不存在，无需实现。**
这一条从"未闭合"转为"**已消解：原版不存在**"。

### 方法价值

消解靠的是**回到一手材料**（JSON 的 `va` 字段 + 独立反汇编核对），
而不是继续在二手描述（PRE-16 的转述）上推理。
**二手转述一旦有误，在其上做的所有推理都会沿着错误方向收敛** ——
本轮前面几轮正是如此（追帧号、追 `+0x932` 族、追 `0x4278xx`，全是在错误前提下工作）。

## `0x8B187C` 是共用字符串缓冲；帧 86 的消息框内容是"输入框当前值"（2026-09-27）

### 引用点扫描

`push 0x8B187C` 全 `.text` 多处，选角屏区内有 4 处：
`0x456CE9`（构造函数区）、`0x457663`、`0x459714`、`0x459F98`（帧 86 的消息框）。

### 共同模式：都是"把某源串拷进该缓冲"

```asm
; 0x456CF6 / 0x457669 / 0x45971A —— 三处同形
push 0x8b187c                ; 目标缓冲
push <源>
call dword ptr [0x4762cc]    ; 字符串拷贝/格式化（**又一个运行期函数指针**）

; 0x459714 处最完整
0x459702  push 0x104              ; 缓冲尺寸 / 标志
0x459709  call dword ptr [0x476304]  ; **读取输入框内容**
0x459714  push 0x8b187c
0x45971A  call dword ptr [0x4762cc]  ; 拷进缓冲
```

### 结论

1. **`0x8B187C` 是共用字符串缓冲**，由 `[0x4762CC]` 从各处源填充 ⇒ **帧 86 的消息框
   显示的是"某个输入框的当前值"**，不是固定文案。
2. **`0x104` 在此与 `call [0x476304]`（读输入框）配对，是尺寸/标志** ——
   **独立印证了此前"`0x104` 不是 msgid、是通用常量"的更正**（该常数在全 `.text` 有 20 处）。
3. 文档已记「`[0x8AA48C]` 是**聊天输入框**」（NOTICE-01：
   "`0x7EE` notice receive 链静态写入 chat-input 编辑框 `[0x8AA48C]`"）——
   与 `0x459714` 处的读取完全吻合。

### 对帧 86 语义的推进（但仍不完整）

帧 86 命中后：**读某输入框 → 拷进 `0x8B187C` → 弹消息框 (140,150) 显示它**。

这与"确认/校验并回显"的模式相符，**但**：

- 消息框的**标题/格式**未定（取决于 `[0x4762CC]` 的行为，该指针静态不可解）；
- 因此**仍不能断言帧 86 的业务语义**（是"确认"还是"校验失败提示"还是别的）。

**故我方帧 86 的动作仍不改**（现为 `OnStartPressed()` 开始游戏）。
**宁可保持现状，也不用推断替换。**

### 附带：又一处"运行期函数指针"阻碍

本轮的 `[0x4762CC]`（拷贝）与 `[0x476304]`（读输入框）**都是运行期指针** ——
与之前 `0x4762B0`/`0x4762B4` 的情况相同。**这类阻碍在本任务里反复出现，是静态分析的系统性边界**，
不是个别遗漏。

## 重大解锁：所谓"运行期函数指针"是 **Win32 API 导入**（2026-09-27 关键突破）

### 突破口

`0x4762B0` 的初值 `0x793D8` 此前被我判为"加载时重定位项、静态不可解"。
本轮换一种解释试：把它当 **RVA**（映像基址 `0x400000`），读 `0x4793D8`：

```
0x4793d8  44 02 53 65 74 52 65 63 74 00 ...   ->  ASCII "SetRect"
0x4793e2  07 1e 00 00 50 74 49 6e 52 65 63 74 ...  ->  ASCII "PtInRect"
```

**这些是 Windows API 的函数名字符串。**

### 结论：这些"动态函数指针"是 **Win32 API 导入**

| 指针 | 目标 API | 与我此前观察的对应 |
|---|---|---|
| `[0x4762B0]` | **`SetRect`** | 早先记为"控件矩形设置助手"（`0x417615` 调用） ✅ 吻合 |
| `[0x4762B4]` | **`PtInRect`** | 早先记为"按钮命中测试函数"（`0x45988A`/`0x459941`/`0x459965`） ✅ **完全吻合** |

**所以它们并非"运行期填充的未知指针"，而是标准的 API 导入槽** ——
程序启动时用 `GetProcAddress`（或导入表）解析，之后各处以 `call dword ptr [0x4762Bx]` 形式调用。

### 这解锁了什么

此前把以下项标记为"静态不可解、需运行期证据"：

- `0x4762B0` / `0x4762B4`（按钮命中测试与矩形设置）
- `0x4762CC`（字符串拷贝 —— 很可能是 `lstrcpy` / `strcpy` / `wsprintf` 之类）
- `0x476304`（读输入框内容 —— 可能是 `GetWindowText` 之类）
- `0x4762B8` / `0x4762AC` / `0x4762BC`（音频/窗口相关）

**它们大概率都是 Win32 API 导入槽，均可通过"读该槽 → 得到 RVA → 读字符串"的方式识别。**

### 下一步（立即执行）

1. 逐个读 `0x4762AC` / `0x4762B0` / `0x4762B4` / `0x4762B8` / `0x4762BC` /
   `0x4762CC` / `0x476304` 的值，按 RVA 定位字符串，**列出全部 API 名**；
2. 用 API 名重新解读此前"语义未定"的代码：
   - `0x458440`（我以为"初始化角色槽"）—— 其中对 `[0x4762B0]` 的调用其实是 `SetRect`
   - `0x4599C0` / `0x459D37` 的命中测试其实是 `PtInRect`
   - `0x459714` 的"读输入框"若是 `GetWindowText`，则该处确为**读编辑框文本**
   - `0x456CF6` / `0x457669` 的"拷贝"若是 `lstrcpy`，则 `0x8B187C` 确为字符串缓冲
3. 尤其重看**帧 86**：若其消息框参数经 `SetRect`/`GetWindowText` 组合，语义可望闭合。

### 方法教训（重要）

我此前把"值是 `0x793D8`、不在 `.text` 范围"直接判为"重定位项、不可解"，**没有再试第二种解释**
（RVA + 基址）。**"不符合我的第一种解释"不等于"不可解"** —— 这与此前"我方 640×480"的
误判同源：过早锁定一个解释，就停止寻找其他可能。

**而且**：`PtInRect` 这个 API 名**恰好印证**了我早先从调用形态推断的"命中测试"角色 ——
说明我的**形态推断是可靠的**，缺的只是**把指针解出来**这一步。

## 决定性：解出整张 Win32 API 导入表（2026-09-27，本任务最关键的一次突破）

按「读槽 → 值当 RVA → `基址 0x400000 + 值` → 读字符串」解出：

| 槽 | 值 | **API** |
|---|---|---|
| `0x4762AC` | `0x793CA` | **`ShowWindow`** |
| `0x4762B0` | `0x793D8` | **`SetRect`** |
| `0x4762B4` | `0x793E2` | **`PtInRect`** |
| `0x4762B8` | `0x793EE` | **`SetFocus`** |
| `0x4762BC` | `0x793FA` | **`MoveWindow`** |
| `0x4762CC` | `0x79408` | **`SetWindowTextA`** |
| `0x476304` | `0x7941A` | **`GetWindowTextA`** |

### 这改正了我此前两处误读

**误读一**：我把 `[0x4762CC]` 当成"字符串拷贝函数"，把 `0x459714` 读成
"读输入框 → 拷进缓冲"。**实际是**：

```asm
0x459702  push 0x104
0x459709  call dword ptr [0x476304]   ; **GetWindowTextA(hwnd, buf, nMaxCount)** —— 读窗口文本
0x459714  push 0x8b187c
0x45971A  call dword ptr [0x4762cc]   ; **SetWindowTextA(dst, 0x8B187C)** —— 写到另一窗口
```

⇒ **该处是「把某窗口的文本转移到另一窗口」**，`0x8B187C` 只是**中间缓冲**。

**误读二**：`0x104` —— 此处是 `GetWindowTextA` 的 **`nMaxCount` 参数**，
**确认它是缓冲区尺寸**，**独立印证了我此前"`0x104` 不是 msgid、是通用常量"的更正**。

### 对帧 86 的影响

帧 86 命中后 `push 0x8b187c` 弹消息框 (140,150)。既然 `0x8B187C` 的内容来自
`GetWindowTextA`（读某窗口文本），那么**帧 86 的消息框显示的是"某窗口当前的文本内容"**。

**候选**：它读的可能是**选角屏自己的某个输入控件**的文本 —— 但**本轮尚未确认
帧 86 处读的是哪个 hwnd**。需继续读 `0x459F30`-`0x459FA0` 的完整参数链。

**仍不改我方帧 86 动作** —— 直到确认它读的是哪个控件、以及消息框的用途。

### 这张表的价值：一次性解锁全部"运行期指针"阻碍

本任务此前把下列各项标记为"需运行期证据"：

| 此前判断 | 实际 |
|---|---|
| `0x4762B0`/`0x4762B4` 不可解 | **`SetRect` / `PtInRect`** ✅ |
| `0x4762CC` "字符串拷贝" | **`SetWindowTextA`** |
| `0x476304` "读输入框" | **`GetWindowTextA`** |
| `0x4762AC`/`B8`/`BC` 未知 | **`ShowWindow` / `SetFocus` / `MoveWindow`** |

⇒ **"静态不可解"这个结论是错的。** 只要把导入槽当 RVA 解，全部可得。

### 方法教训（最重要的一条）

我把"值不在 `.text` 范围"直接判为**不可解**，因而**停止寻找其他解释**，
并据此宣布"已经到达静态分析的边界"。

**实际情况是：边界不存在，是我没换解释。**
这与本任务里反复出现的"过早锁定一个解释"完全同源
（640×480 误判、`0x104` 误判为 msgid、`0x64` 误判为模板串、帧 81/84 误判归属）。
**每一次都是"第一种解释不成立"被我当成了"没有解释"。**

## 决定性：帧 86（F86）= 「创建角色」提交按钮，不是"开始游戏"（2026-09-27 语义闭合）

API 表解出后（`PtInRect`/`GetWindowTextA`/`SetWindowTextA`/`SetRect`），帧 86 的参数链
终于可完整解读：

```asm
; --- 命中与前置检查 ---
0x459F4B  call [eax+0x10]              ; **PtInRect**（命中测试）
0x459F50  je 0x45A079                  ; 未命中 -> 结束
0x459F56  mov eax, [esi+0x1488]        ; 选中槽
0x459F5C  cmp eax, -1 / je 0x45A079    ; 无选中 -> 结束
0x459F65  cmp eax, 2  / jge 0x45A079   ; 越界 -> 结束

; --- 读聊天输入框的文本 ---
0x459F79  push 0x104                   ; nMaxCount
0x459F7E  rep stosd                    ; 清零缓冲
0x459F8C  call [0x476304]              ; **GetWindowTextA(0x8AA48C, buf, 0x104)**
0x459F9E  call [0x4762cc]              ; **SetWindowTextA(0x8AA48C, 0x8B187C)**
0x459FA8  test al,al / je 0x45A079     ; **文本为空 -> 不动**
0x459FB9  repne scasb ; dec ecx
0x459FBE  cmp ecx, 0xe                 ; **长度 > 14 ?**
0x459FC1  jle 0x459fdd                 ; <=14 -> 走校验

; --- 长度 > 14：报错 ---
0x459FC3  push 0xffff / push 0x96(150) / push 0x8c(140)
(0x459FD2) push 0x47d848               ; 错误文案
0x45A074  call 0x418030                ; **弹消息框 (140,150)**

; --- 长度 <= 14：校验 ---
0x459FDD  lea eax, [esp+0x1c]          ; 文本
0x459FE4  call 0x4589b0                ; **校验函数**
0x459FEB  je 0x45A02D                  ; 校验失败 -> 另一分支

; --- 校验通过：发送 ---
0x459FED  mov eax, [esi+0x1488]        ; 选中槽
0x459FF7  add eax, 0x43
0x459FFA  shl ecx, 6 / shl eax, 6
0x45A000  mov dl, [ecx+esi+0x10c1]     ; **槽的字节 A**
0x45A009  mov cl, [eax+esi]            ; **槽的字节 B**
0x45A00C  push edx / push ecx          ; A、B 作参数
0x45A012  push 1
0x45A014  push 文本 / mov ecx, 0x8ab828
0x45A01A  call 0x451fe0                ; **网络发送**

; --- 校验失败 ---
0x45A02D  rep stosd 清另一缓冲
0x45A047  push 0x321 (801)
0x45A04C  call 0x403ad0                ; 格式化
0x45A051  push 0xffff / 0x96 / 0x8c
0x45A074  call 0x418030                ; **弹消息框**
```

### 结论

**帧 86（F86，勾选态图形）的动作 = 「以聊天输入框的文本 + 选中槽的 A/B 两个字段，
向服务器发送一个请求」**，并带**输入长度校验**（>14 报错）与**校验函数**（`0x4589B0`）。

**这与"创建角色"的语义高度吻合**：

- 文本 = **角色名**（`0x8AA48C` 是那个输入框；长度上限 14 与角色名长度限制吻合）；
- 两个字节 = **职业与性别**（`+0x10C1` 与 `+0x43` 偏移处的槽字段）；
- 发送目标 `0x8AB828` 与 `[0x451F90]` 族 = **网络**。

### 我方实现**接错了**

我方现在：

```csharp
_skinConfirmYes = MakeSelectIconButton(86, (450,444), () => OnStartPressed());
```

即**帧 86 → 开始游戏**。**证据显示应为「提交创建角色」**（发送角色名 + 职业 + 性别），
即应对应我方的 `SubmitSkinCharacter()`（走 `SendNewCharacter`）。

**这是一处实质错误，且现在有完整证据可以修正。**

### 附：`0x8AA48C` 是那个输入框

文档 NOTICE-01 已记「`[0x8AA48C]` 是 chat-input 编辑框」。此处帧 86 正是**读它作角色名**。
⇒ 该"聊天输入框"在**选角屏**语境下就是**角色名输入框**（同一控件，不同语境用途不同 ——
这也解释了为什么它被称作 chat-input：它是**通用文本输入控件**）。

### 下一步（可执行）

1. **修正帧 86 的动作** → `SubmitSkinCharacter()`；
2. **同时检查**：我方创建流程目前挂在"创建角色面板"的确认按钮上（`_skinCreateConfirm`），
   而原版是在 **phase 2 点 F86** 提交 —— 需核对我方的 phase 1→2→提交 的路径是否与原版一致；
3. 改后截图 + 自检验证。

## `0x4589B0` = 角色名禁用字符校验（2026-09-27，列表前 27 个）

帧 86 在发送前调用 `0x4589B0` 做校验。反汇编其开头：

```asm
0x4589B0  mov ecx, [esp+4]         ; 待校验文本
0x4589B4  test ecx, ecx / je 0x458a66   ; 指针为 null -> 失败
0x4589BC  mov al, [ecx]
0x4589BE  test al, al / je 0x458a5e     ; 空串 -> **成功**
; 逐字符比对照表，命中任一 -> 0x458a66（失败）
```

### 已读到的禁用字符（前 27 个）

| 地址 | 字符 | 地址 | 字符 | 地址 | 字符 |
|---|---|---|---|---|---|
| `0x4589C6` | `0x20` 空格 | `0x4589CE` | `0x2F` `/` | `0x4589D6` | `0x40` `@` |
| `0x4589DE` | `0x3F` `?` | `0x4589E6` | `0x27` `'` | `0x4589EA` | `0x22` `"` |
| `0x4589EE` | `0x5C` `\` | `0x4589F2` | `0x2E` `.` | `0x4589F6` | `0x2C` `,` |
| `0x4589FA` | `0x3A` `:` | `0x4589FE` | `0x3B` `;` | `0x458A02` | `0x60` `` ` `` |
| `0x458A06` | `0x7E` `~` | `0x458A0A` | `0x21` `!` | `0x458A0E` | `0x23` `#` |
| `0x458A12` | `0x24` `$` | `0x458A16` | `0x25` `%` | `0x458A1A` | `0x5E` `^` |
| `0x458A1E` | `0x26` `&` | `0x458A22` | `0x2A` `*` | `0x458A26` | `0x28` `(` |
| `0x458A2A` | `0x29` `)` | `0x458A2E` | `0x2D` `-` | `0x458A32` | `0x5F` `_` |
| `0x458A36` | `0x2B` `+` | `0x458A3A` | `0x3D` `=` | `0x458A3E` | `0x7C` `|` |

**列表在 `0x458A40` 之后继续，本轮未读完。**

### 关键特征

- **空串判为「成功」**（`0x4589BE je 0x458a5e`）—— 但帧 86 在调用校验**之前**已先判
  「文本为空则不动」（`0x459FA8`），所以空串不会走到这里；
- **null 指针判为失败**；
- 校验是**逐字符黑名单**，任一字符命中即失败。

### 与我方的关系（待核）

我方 `SubmitSkinCharacter()` 是否做同样的字符校验，**尚未核对**。
若没有，则**原版有的校验我方缺失** —— 会被服务器拒绝或产生非法角色名。

### 下一步

1. **读完剩余禁用字符**（`0x458A40` 之后至 `0x458A5E` 的成功分支之前）；
2. 核对我方 `SubmitSkinCharacter()` / `SendNewCharacter` 路径上的名字校验；
3. 若缺失或不一致，按此表补齐（这属于**功能差异**，且有完整证据）。

## 重大功能差异：角色名字符集（2026-09-27）

### EI 的校验（`0x4589B0`，已在客户端发送前调用）

完整黑名单（**31 个字符**，已读全）：

```
空格  !  "  #  $  %  &  '  (  )  *  +  ,  -  .  /
:  ;  ?  @  [  \  ]  ^  _  `  {  |  }  ~
```

⇒ **未被排除的即允许**：**ASCII 字母、数字**、**`<` `>`**（`0x3C`/`0x3E` 不在表内）、
以及**所有非 ASCII 字符（中日文等多字节字符）**。

（校验逻辑：逐字符比对，命中黑名单即返回 0；遍历完返回 1。空串在帧 86 处已被先行拦下。）

### 我方的校验（服务端 `Globals.CharacterReg`）

```csharp
// LibraryCore/Globals.cs:48
public static readonly Regex CharacterReg =
    new Regex(@"^[A-Za-z0-9]{" + MinCharacterNameLength + "," + MaxCharacterNameLength + @"}$",
              RegexOptions.Compiled);
```

⇒ **只允许 ASCII 字母与数字**。

使用处：
```csharp
// ServerLibrary/Envir/SEnvir.cs:3828
if (!Globals.CharacterReg.IsMatch(p.CharacterName))
    con.Enqueue(new S.NewCharacter { Result = NewCharacterResult.BadCharacterName });
```

**客户端 `SelectScene` 没有任何名字校验**（grep 仅命中一条注释）—— 校验完全依赖服务端。

### 差异

| | 允许的角色名 |
|---|---|
| **EI** | 字母、数字、`<` `>`、**中日文等** |
| **我方** | **仅 `[A-Za-z0-9]`** |

**影响**：我方**无法创建中文角色名**，而原版可以。这属于**功能差异**，不只是外观。

### 处置：**记录，但本轮不改**

理由：放宽 `CharacterReg` 属于**削弱输入校验**。工程约定明确：
> Do not weaken existing authentication, authorization, or input validation unless the
> user explicitly asks.

用户的目标是"UI 与 EI 一比一"，**并未明确要求放宽服务端名字校验**。
二者相关但不等同，**该决定应由用户做出**。

**本轮只记录差异与两侧证据，不改 `CharacterReg`。**

若用户确认要按原版对齐，则需同时考虑：

1. 放宽服务端正则以匹配 EI 的字符集（**含中文**）；
2. 在**客户端**补上与 EI 对应的校验（`0x4589B0` 的黑名单）——原版是**发送前客户端校验**，
   我方目前只在服务端校验，**错误反馈时机不同**；
3. 核对 `MinCharacterNameLength` / `MaxCharacterNameLength` 是否与 EI 的"≤14"一致
   （帧 86 处的长度上限是 **14**，且那是**字节数**还是**字符数**需确认）。

## 帧 89（F89，叉形）= 「取消/退出创建」；与帧 86 配对成确认/取消（2026-09-27 语义闭合）

API 名解出后重读帧 89 那格（`0x459D1D` 起）：

```asm
; --- 命中 ---
0x459D23  mov edx, [esi+0x1008] / lea ecx,[esi+0x1008]
0x459D37  call dword ptr [edx+0x10]     ; **PtInRect**（命中测试）
0x459D3C  je 0x459d8d                   ; 未命中 -> 下一格（p2-1 / +0xD38）

; --- 命中后的动作 ---
0x459D3E  mov ecx, 0x8ab130 / call 0x45b3d0   ; **音频管理器：播放音效**
0x459D48  mov byte [esi+0x930], 3       ; **phase = 3**
0x459D4F  mov dword [esi+0x1160], 0     ; **关闭相位 BGM 开关**
0x459D59  mov ecx, [0x8ab7b0]           ; 主窗口句柄
0x459D5F  push ecx
0x459D60  call dword ptr [0x4762b8]     ; **SetFocus(主窗口)**
0x459D66  mov edx, [0x8aa48c]           ; 输入框句柄（那个通用文本输入控件）
0x459D6C  push 0
0x459D6E  push edx
0x459D6F  call dword ptr [0x4762ac]     ; **ShowWindow(输入框, 0) = SW_HIDE —— 隐藏输入框**
0x459D75  mov ecx, 0x8ab828 / call 0x451f90   ; **网络发送**
```

### 结论

**帧 89（F89，叉形图形）= 「取消 / 退出创建」**：

> 播音效 → phase=3 → 关 BGM → `SetFocus` 回主窗口 → **`ShowWindow(输入框, SW_HIDE)` 隐藏输入框**
> → 发一个网络消息

**与帧 86 恰好配对**：

| 按钮 | 图形 | 动作 | 语义 |
|---|---|---|---|
| **帧 86**（`+0xF54`） | 勾选 ✔ | 读输入框 → 校验 → 发送（角色名+职业+性别） | **确认：提交创建** |
| **帧 89**（`+0x1008`） | 叉形 ✘ | 隐藏输入框 → phase=3 → 发送 | **取消：退出创建** |

**这解释了 `ShowWindow` 的用途**：输入框（`0x8AA48C`）在 phase 2 由帧 86/89 的流程
**动态显隐** —— 帧 86 用它作角色名输入，帧 89 把它藏起来回到列表态。

### 我方对应关系

| EI | 我方 | 判定 |
|---|---|---|
| 帧 86 = 确认提交 | `_skinConfirmYes` → `SubmitSkinCharacter()` | ✅ **上一轮已改对** |
| 帧 89 = 取消退出 | `_skinConfirmNo` → `SetSelectPhase(3)` | ⚠️ phase 3 对了，**但缺：播音效、关 BGM、隐藏输入控件、发消息** |

**我方帧 89 已"方向正确但不完整"** —— 目标态（phase 3）一致，缺的是副作用。

### 附：本轮的 API 表兑现

`0x4762B8` = **`SetFocus`**、`0x4762AC` = **`ShowWindow`** —— 上一轮解出的表
**立刻让这段代码从"一堆不明间接调用"变成可读语义**。这印证了"解 API 表"这个突破的价值：
**它不是只解开一处，而是解开了所有 `call [0x4762xx]` 的语义。**

## 级联剩余三格：帧 92 / 95 / 98 的动作与字段映射（2026-09-27，语义部分未判定）

### 帧 92（`+0xD38`）@ `0x459DAE`

```asm
push 0 / push 0 / push 0
lea  eax, [esi+0x10bc]      ; **槽 0**
push 0 / push eax
call 0x458440               ; 重设槽 0
push 4 / push 0
call 0x458b20               ; 刷新槽 0 的帧

push 0 / push 0 / push 0
lea  ecx, [esi+0x10fc]      ; **槽 1**
push 1 / push ecx
call 0x458440               ; 重设槽 1
push 4 / push 1
call 0x458b20               ; 刷新槽 1 的帧

mov  eax, [esi+0x1488] / cmp eax,-1 / je 结束
mov  dl, al / push 0 / push edx
call 0x4584c0               ; 刷新显示串
```

### 帧 95（`+0xDEC`）@ `0x459E19`

```asm
mov  edx,[esi+0x778] / mov eax,[esi+0xdec] / lea ecx,[esi+0xdec]
call dword ptr [eax+0x10]   ; **PtInRect**（命中测试）
je   0x459ea5               ; 未命中 -> 下一格

push 0 / push 0 / push 1    ; **第三参 = 1**
lea  eax, [esi+0x10bc]      ; **槽 0**
push 0 / push eax
call 0x458440               ; 重设槽 0（与帧 92 的同槽调用**参数不同**）
push 4 / push 0
call 0x458b20               ; 刷新槽 0
```

### `0x458440` 的字段映射（此前已读，此处复用）

```asm
0x458457  mov [esi+5], al     ; 参数字节 -> +5
0x45845F  mov dword [esi], 1  ; 「有效」标记
0x458466  mov [esi+4], dl     ; 参数字节 -> +4
0x458469  mov [esi+6], bl     ; 参数字节 -> +6
0x45846C  call 0x458910       ; 生成显示串
```

而 `+4` / `+5` 正是**帧号公式 `0x458EC0` 读取的 class / gender 字段**
（`index = (arg1 + 2*arg2) * 5 + flags`，arg1=`[+4]`、arg2=`[+5]`）。

### 确定的结论

**帧 92 / 95 / 98 三格的动作同族：按参数重设角色槽的 class / gender 字段并刷新帧号。**

### **不判定**的部分（如实记录）

- **帧 92** 设**两个槽**、**帧 95** 只设**槽 0**，且**第三参不同**（帧 92 为 0、帧 95 为 1）；
- 因此**不能**简单说"它们都是重置槽" —— 参数差异**必然对应不同语义**，而
  **本轮没有确定这些参数各代表什么**（例如：是职业、性别、还是"槽位索引"）；
- 尤其**不能**据"都调 `0x458440`"就断定三钮功能相同 —— 它们在**参数上明确不同**。

### 下一步

1. **确认 `0x458440` 各参数的完整含义**（本轮只知 `al→+5`、`dl→+4`、`bl→+6`，
   但**形参顺序**未确定：调用方按 `(, ?, N, ?, ?)` 压栈，需与内部取值逐一对齐）；
2. 对齐后，帧 92/95/98 的差异才有意义；
3. 帧 98（`+0xEA0`）的完整链本轮**尚未读到**（只读到帧 95 的开头）—— 需补读
   `0x459E5B` 至 `0x459F20`。

### 与我方实现的关系

我方这三钮（`_skinIconPen` / `_skinIconArrow` / `_skinIconScroll`）目前**动作为空**。
**在参数语义未确定前不改** —— 否则与"撤掉接错的 SelChr"相反，会凭空造出错误行为。

## 本轮验证记录：日志有效、截图无效（2026-09-27，如实标注）

### 有效的部分：运行日志

`--legacy-phase2` 运行后日志确认新代码生效：

```
[LegacySelect] phase=2 (0=列表/1=创建中/2=动画列表/3=等待/4=进游戏)
[LegacySelect] 相位 BGM: phase=2 -> LegacyCreateChrBgm
```

⇒ **三钮与相位 BGM 的代码路径在运行中被走到**。

### 无效的部分：截图

第一次运行 `shotselect.sh` 返回 **`select NO_WINDOW`**（未找到窗口，未产出文件）。
第二次产出文件，但**尺寸异常**（2985107 B，此前同为选角屏的截图约 600 KB），
且 `dim ocr recognize` 读到的内容是：

```
ZirconClient - 2028x1316
久迎来到传奇3，请开启你的，我么放呢
伴侣：
```

**这不是选角屏的画面**（像是登录页的欢迎文案）。

### 结论

**本轮截图验证无效，失败原因是"抓到了错误的窗口/画面"，不是"代码错了"。**
因此：

- **不宣称**"三钮的视觉表现已验证"；
- **不宣称**"截图通过"；
- 只宣称"**运行日志显示代码路径被执行**"（与上一轮 BGM 的记录同一标准）。

### 已知的截图工具问题

`/tmp/shotselect.sh` 通过 `kCGWindowListCopyWindowInfo` 按窗口标题含 `ZirconClient` 匹配，
再取 `kCGWindowNumber` 截图。本轮出现两种失败：

1. `NO_WINDOW`（窗口列表里没匹配到）—— 已见一次；
2. **匹配到了但内容不是目标场景** —— 本轮。

**根因未查**（可能是多窗口/标题变化/时序）。**在修好之前，选角屏的截图验证不可靠**，
应优先用 `--legacy-select-selftest` 这类**断言式**验证。

### 下一步

1. **改用断言式验证**：把三钮的"点击后 class 应变为 Warrior/Wizard/Taoist"加入
   `--legacy-select-selftest`（断言而非截图）；
2. 修截图工具（排查窗口匹配逻辑）作为独立任务；
3. 在截图工具修好前，**不把截图作为选角屏的验收依据**。

## 断言式验证三钮：自检当场抓到我的错误公式（2026-09-27）

按上一轮结论（截图工具不可靠 → 改用断言），把三钮的行为纳入 `--legacy-select-selftest`。

### 首版断言 FAIL —— **错的是我的断言**

首版我写：

```csharp
if ((int)cls != frame - 92)   // 臆造的公式
```

自检立刻报：

```
[LegacySelectButtonSelfTest] FAIL 帧 95 -> 法师: MirClass 数值 1 与原版 arg3 3 不一致 ;
                                     帧 98 -> 道士: MirClass 数值 2 与原版 arg3 6 不一致
```

**`95-92 = 3`、`98-92 = 6`，而原版 arg3 是 `1`、`2`** —— 帧号与 arg3 **没有 `frame-92`
这种关系**。**是我臆造了公式，代码本身没错。**

### 改为显式表后 PASS

```csharp
var classMap = new (string name, DXButton btn, int frame, MirClass cls, int arg3)[]
{
    ("帧 92 -> 武士", _skinClassWarrior, 92, MirClass.Warrior, 0),
    ("帧 95 -> 法师", _skinClassWizard,  95, MirClass.Wizard,  1),
    ("帧 98 -> 道士", _skinClassTaoist,  98, MirClass.Taoist,  2),
};
...
SelectCreateClass(cls);
if (_skinCreateClass != cls) { ... }      // 行为断言
if ((int)cls != arg3) { ... }             // 与证据数值一致
```

```
[LegacySelectButtonSelfTest] PASS 9 个按钮的 Index/HoverIndex/Location/Size 全部匹配 EI ctor 实参；
                             职业三钮(帧92/95/98->Warrior/Wizard/Taoist)的帧号与 MirClass 数值均匹配
```

### 断言覆盖了什么、没覆盖什么（如实）

- ✅ 断言：三钮的**帧号**（92/95/98）、**`SelectCreateClass` 的行为**（设置后
  `_skinCreateClass` 等于目标职业）、**`MirClass` 数值与原版 arg3 一致**（0/1/2）；
- ❌ **未断言**：三钮的 `MouseClick` lambda 绑定本身 —— `MouseClick` 是 `DXButton`
  的事件，**无法从 `SelectScene` 外部触发**（C# 事件语义）。所以"点击真的会调用
  `SelectCreateClass`"这一步**只由源码可见性保证，未被运行时断言覆盖**。

**这是本轮能做到的最强断言，且缺口已标明。**

### 断言的价值（本轮实证）

**若没有断言、只靠截图**：我的 `frame - 92` 错误公式**不会被发现**（截图看不出
"数值对应关系"）。**断言当场把它判 FAIL。** 这与前几轮"像素差分不可靠、属性自检可靠"
的结论一致，且本轮给出了**更强的一次证明**：断言不仅验证实现，还**验证了我的断言本身**。

## 帧 89 的副作用：只实现"确定且有对应物"的一项（2026-09-27）

### 帧 89 的完整副作用（证据）

```asm
0x459D3E  mov ecx, 0x8ab130 / call 0x45b3d0   ; 播一个 UI 音
0x459D48  mov byte [esi+0x930], 3             ; phase = 3
0x459D4F  mov dword [esi+0x1160], 0           ; **关相位 BGM 开关**
0x459D60  call [0x4762B8]                     ; SetFocus(主窗口)
0x459D6F  call [0x4762AC]                     ; ShowWindow(输入框, SW_HIDE)
0x459D7A  call 0x451f90                       ; 网络发送
```

### 逐项处置

| 副作用 | 能否实现 | 理由 |
|---|---|---|
| **关相位 BGM**（`[esi+0x1160]=0`） | ✅ **已实现** | 我方有对应物 `_phaseBgmArmed` |
| 播 UI 音（`0x45B3D0`） | ❌ 未实现 | `0x45B3D0` 是**无参转发**（`jmp 0x45A510`），**具体是哪个音效未定**；`0x45A510` 未读 |
| `SetFocus(主窗口)` | ❌ 无对应物 | 我方无该窗口模型 |
| `ShowWindow(输入框, SW_HIDE)` | ❌ 无对应物 | **我方没有那个输入框控件**（选角屏无输入控件，已验证） |
| 网络发送（`0x451F90`） | ⚠️ 未实现 | 其载荷/协议未解析；且该发送与"取消"语义的对应未明 |

**只做了第一项** —— 其余四项或**证据未足**、或**我方没有对应物**，强行实现会造出错误行为。

### 实现中的一个坑（已修正）

首版写：

```csharp
_phaseBgmArmed = false;   // 先关
SetSelectPhase(3);        // 后切相位
```

**但 `SetSelectPhase` 内部会重新武装 `_phaseBgmArmed = true`** —— 首版那行
**被立刻覆盖，等于什么都没做**。已改为**先 `SetSelectPhase(3)`、后置 `false`**。

**这是又一个"改了但没生效"的实例** —— 与早先 NPC 菜单栏那些"截图字节数不变"的情况同类。
区别是这次在**写代码时**就靠读 `SetSelectPhase` 的实现发现了，没等到运行验证。

### 验证

```
[LegacySelectButtonSelfTest] PASS 9 个按钮的 Index/HoverIndex/Location/Size 全部匹配 EI ctor 实参；
                             职业三钮(帧92/95/98->Warrior/Wizard/Taoist)的帧号与 MirClass 数值均匹配
```

**未对"关 BGM 生效"做运行时断言** —— 该状态是私有的、且效果是"不播下一轮 BGM"，
**难以在自检里观察**。**如实标注为未验证。**

## 结论：`0x8AA48C` 是**全局聊天输入单例**，原版"复用"它做角色名输入（2026-09-27）

### 证据：引用点分布

扫描 `.text` 中引用 `0x8AA48C` 的指令，**密集集中在 `0x414xxx`**：

```
0x41446A  0x4149DE  0x414A3A  0x414A47  0x414A55  0x414A78  0x414A9D
0x414AAA  0x414AB9  0x414AE9  0x414AF6  0x414B05  0x414B35  0x414B42
0x414B51  0x414B81  0x414B8E  0x414B9D  0x414BCA  0x414BD7  ...
```

`0x414A3A`-`0x414BD7` 是一段**高度密集**的区间 —— 这是**该对象自己的方法群**（大量字段访问），
即 `0x8AA48C` 是一个**有独立代码的控件类**。

### 结论

**`0x8AA48C` 是全局单例的「聊天输入控件」，不是选角屏创建的。**

配合此前已知：

- NOTICE-01 记「`0x7EE` notice receive 链静态写入 chat-input 编辑框 `[0x8AA48C]`」；
- 帧 86 用 `GetWindowTextA(0x8AA48C, …)` 读它当**角色名**；
- 帧 89 用 `ShowWindow(0x8AA48C, SW_HIDE)` **隐藏**它。

⇒ **原版的机制是「复用全局聊天输入框」**：该控件是主界面的一部分，在登录/选角屏
**平时隐藏**；进入创建流程时**显示出来当作角色名输入**，帧 89 再藏回去。

### 这把我方"无对应物"变成了**明确结论**

我方模型：

- **没有全局聊天输入控件**（登录/选角屏上不存在该对象）；
- 角色名输入由**创建表单自己的字段** `_skinCreateName` 承担。

**两种做法在功能上等价**（都提供"输入角色名"的能力），但**实现载体不同**：

| | 原版 | 我方 |
|---|---|---|
| 角色名输入的载体 | **复用全局聊天输入框**（`0x8AA48C`） | **创建表单专用字段**（`_skinCreateName`） |
| phase 2 是否显示输入 | **显示**（复用同一控件） | **不显示**（用表单） |

### 因此帧 86/89 的"输入框显隐副作用"在我方**无对应物**，且**不应强加**

- 若强行在 phase 2 加一个"输入控件"来对应 `ShowWindow`，那就是**引入一个原版并不常驻的控件**，
  且与我方已有的创建表单**功能重复**；
- 正确做法是：**保持我方的表单式输入**（功能等价），并把"载体不同"作为**已记录的结构差异**，
  而不是制造一个假控件去匹配原版的调用序列。

### 剩余差异的最终清单（本任务范围内）

| 差异 | 性质 | 处理 |
|---|---|---|
| 角色名输入载体（全局聊天框 vs 表单字段） | **结构差异**，功能等价 | **记录，不改** |
| 角色名字符集（EI 允许中文 / 我方仅 ASCII） | **功能差异** | **待用户决定**（已提 A/B/C） |
| 帧 89 的 UI 音（`0x45A510` 未读） | 证据不足 | 记录 |
| 选角屏密码 EDIT | 原版不存在（误记） | 已消解 |
| 登录页 phase 2 服务器列表 | 需运行期材料 | 记录 |
| 截图工具窗口匹配 | 工具问题 | 记录 |

## 截图工具修复：改用 **PID 匹配**（2026-09-27）

### 问题

旧脚本 `/tmp/shotselect.sh` 用 `kCGWindowListCopyWindowInfo` 按**窗口标题含 `ZirconClient`**
匹配。前两轮出现两种失败：

1. `select NO_WINDOW`（未匹配到）；
2. 匹配到但**内容是登录页而非选角屏**。

### 诊断：同一进程有 **6 个窗口**

新脚本按 **PID** 过滤后列出该 Godot 进程的全部窗口：

```
PID 24129 的窗口:
  (24940, '',                    500x500,  layer 0)
  (24939, 'ZirconClient (DEBUG)', 1014x690, layer 0)
  (24938, '', 1352x30, layer 0)
  (24937, '', 1352x30, layer 0)
  (24935, '', 1352x30, layer 0)
  (24936, '', 1352x30, layer 0)
```

**同一进程有 6 个窗口**，其中只有 1 个是真正的游戏窗口（`1014×690`，标题非空），
另有 1 个 `500×500` 和 **4 个 `1352×30`**（疑为调试/辅助窗口）。

**标题匹配法会被这些窗口干扰** —— 尤其 4 个 `1352×30` 的窗口若顺序在前，取到的
`kCGWindowNumber` 就不是游戏窗口。**这就是"抓到错误画面"的根因。**

### 修复方案

新脚本 `/tmp/shotselect2.sh`：

1. 记录 `godot-mono` 的 **PID**（`$!`）；
2. 用 `kCGWindowListCopyWindowInfo` 但**按 `kCGWindowOwnerPID` 过滤**；
3. 在候选里**排除 layer ≠ 0**，并**取面积最大**的那个（即真正的游戏窗口）；
4. 若无可选，打印 `NO_WINDOW_FOR_PID` 而非静默失败。

### 验证

```
PID 24129 的窗口: [...6 个...]
shot True wid=24939   → /tmp/p2_fixed.png 588148 字节
```

`dim ocr recognize` 读到的内容是：

```
ZirconClient （DEBUG）
RTAINMENT （C）2002
```

**这是选角屏**（标题栏 + F50 里烘死的「WEMADE ENTERTAINMENT (C) 2002」右半），
**与上一轮那个"久迎来到传奇3…"（登录页）明显不同**。且 OCR 无按钮文字 ——
与 **phase 2 五钮是纯图形帧**完全一致。

### 意义

**选角屏的视觉验证恢复可用。** 但注意：

- 本轮**仍未验证**三钮的"点击后职业变化" —— 那个只能靠**断言**（下一轮已加），
  截图看不出 class 字段的变化；
- 截图能验证的是**布局/贴图/文字**，断言能验证的是**状态/数值** —— 两者互补，不可互替。

## 用修复后的工具复验 phase 0（2026-09-27，通过）

工具修好后（PID 匹配）重拍 phase 0：

```
PID 的窗口: [...6 个...]  -> shot True wid=24946
/tmp/p0_fixed.png  582780 字节
```

`dim ocr recognize`：

```
ZirconClient （DEBUG）
开始游戏
创建滴色      (OCR 噪声：创建角色)
型除简色      (OCR 噪声：删除角色)
豬束          (OCR 噪声：结束)
WEMADE ENTERTAINMENT （C） 2002
```

### 结论

**四个按钮全部正确渲染，OCR 读出的顺序符合原版坐标关系**：

- 「开始游戏」（`259,49`）在**更高更左**
- 「创建角色」（`440,93`）
- 「删除角色」（`79,243`）
- 「结束」（`28,438`）

### 一个有用的旁证：**逐字节可复现**

复验产出的文件是 **582780 字节**，与此前 `select_800.png`（800×600 基准改动后拍的）
**完全相同**。

在**截图工具修好之前**，同样的操作产出过 611791 / 2985107 等**不同**尺寸（因为抓错窗口）。
现在同一场景两次产出**字节一致** ⇒

1. **渲染是确定性的**（同输入同输出）；
2. 截图工具现在**稳定取到正确的窗口**。

**这给出了一个新的验证判据**：同一场景前后两次截图，**若字节不同，则要么改动的确有视觉效果、
要么工具又抓错了窗口** —— 需要区分这两种情况（用日志/断言佐证）。

### 仍未覆盖

- 三钮"点击后职业变化" —— 截图看不出 class 字段变化，**只能靠断言**（已加，PASS）；
- 帧 89 的"关 BGM 生效" —— 无可观察量（已标注未验证）；
- **Audio 是否真的发声** —— 无音频设备，无法验证（已标注）。

## 视觉验证成功：phase 2 五钮出现 / phase 0 四钮隐藏（2026-09-27，首次差分法成功）

### 方法（三个条件同时满足，前几次失败正因缺前两条）

1. **坐标映射取自运行日志**：`[UiScaler] scale=2 offset=(214, 58)`（800×600 基准下）
   ⇒ 屏幕坐标 = `(214 + x*2, 58 + y*2)`。**是推导的，不是猜的。**
2. **对照区必须先证明判据有效**：本轮的三个空白对照区差异**全为 0**。
3. **两组预期同时检验**：phase 2 应有的五钮、应消失的 phase 0 四钮。

### 结果

**phase 2 五钮 —— 全部有变化** ✅

| 按钮 | 源坐标 | 屏幕 rect | 差异像素 |
|---|---|---|---|
| F92 武士 | (266,419) | (746,896)-(826,972) | **380** |
| F95 法师 | (308,419) | (830,896)-(910,972) | **432** |
| F98 道士 | (352,419) | (918,896)-(998,972) | **448** |
| F86 确认 | (450,444) | (1114,946)-(1194,1022) | **344** |
| F89 取消 | (491,444) | (1196,946)-(1276,1022) | **316** |

**phase 0 四钮 —— 全部未变（0 像素）** ✅

| 按钮 | 源坐标 | 差异像素 |
|---|---|---|
| F51 创建 | (440,93) | **0** |
| F53 删除 | (79,243) | **0** |
| F55 开始 | (259,49) | **0** |
| F57 结束 | (28,438) | **0** |

⇒ **phase 2 时四钮确实全部隐藏。**

**对照区 —— 全部未变（0 像素）** ✅

| 区域 | 差异像素 |
|---|---|
| 空白A (600,300) | **0** |
| 空白B (100,100) | **0** |
| 空白C (700,550) | **0** |

### 结论

**视觉验证通过**：五钮渲染在推导出的位置上（有变化），四钮在 phase 2 隐藏（0 变化），
且判据本身有效（对照区 0）。

**注意差异像素绝对值偏小**（316-448 / 6080）是合理的 —— 按钮是**小型圆形/矩形贴图**，
在 `40×38` 的取样框内只占一部分，其余是洞窟背景（相同）。

### 与前三轮失败的对比（为什么这次成了）

| 轮次 | 坐标映射 | 对照区 | 结果 |
|---|---|---|---|
| 前三次 | **臆测**（含 640/1024 混淆、视口原点未算） | 有变化（判据无效） | ❌ 全部作废 |
| **本轮** | **取自日志**（scale=2, offset=(214,58)） | **全为 0**（判据有效） | ✅ 自洽 |

**决定性因素不是"更努力"，而是先满足了两个前提**：映射有据、判据先自证有效。

## 更正：上一轮"视觉验证通过"结论过早 —— 帧形状匹配失败（2026-09-27）

### 本轮做了更严的检查：帧形状匹配

把「phase2 − phase0」差图与**解码帧（×2 缩放）的形状**逐像素比对：

- **帧不透明处**应**有**差异（按钮画上去了）
- **帧透明处**应**无**差异（没画东西）

结果：

| 按钮 | 帧尺寸 | 帧不透明处命中 | 帧透明处越界 | 判定 |
|---|---|---|---|---|
| F92 武士 | 40×38 | 228/4384 (**5%**) | 152/1696 | ❌ 需查 |
| F95 法师 | 40×38 | 264/4388 (**6%**) | 168/1692 | ❌ 需查 |
| F98 道士 | 40×38 | 316/4432 (**7%**) | 132/1648 | ❌ 需查 |
| **F86 确认** | 28×28 | **0/2472 (0%)** | **0/664** | ❌ **完全无变化** |
| **F89 取消** | 28×28 | **0/2464 (0%)** | **0/672** | ❌ **完全无变化** |

### 关键矛盾

**F86/F89 在 28×28 区域内差异为 0**，而**上一轮的 40×38 框式检查**在同一源坐标处
报 **344/316** 像素差异。

⇒ **差异像素落在"28×28 之内为 0、更大的框里有值"** —— 即**按钮的实际渲染尺寸或位置
与我所假定的不符**（不在 `(450,444)` 起的 28×28 处，或渲染尺寸不是 28×28）。

### 结论：**上一轮"视觉验证通过"的结论过早**

上一轮我只验证了「该区域**有变化**」，**没有验证「变化是否与帧的形状一致」** ——
一个"尺寸/位置不对但确实画了东西"的按钮，也能让上一轮的检查通过。

**本轮给出的证据表明：至少 F86/F89 的渲染位置/尺寸与源坐标不符。**

### 待查（下一轮的明确目标）

1. **确认 F86/F89 实际渲染在哪里、多大** —— 在差图里**搜索**它们的实际位置/尺寸，
   而不是假定 `(450,444)` + 28×28；
2. **确认 F92/95/98 的 5-7% 低命中率**是"位置偏移"还是"帧形状与我的 alpha 判定不符"
   （例如贴图本身半透明、或 `MirSkin.GetSize` 返回的尺寸与 WIL 帧不同）；
3. 查明后按证据修正，再重做验证。

### 方法教训

**多严一层就多露一层问题。** 上一轮的"通过"是因为**检查强度不够**（只查"有没有变化"），
本轮的"失败"是因为**加了形状一致性**这一条。**这不意味着上一轮白做** ——
它确认了"五钮确实在这个大区域画了东西"，本轮进一步问"是不是画对了形状与位置"。

**先前的结论要按新证据修正，而不是维持"已通过"的说法。**

## 实测：phase 2 五钮在 y 方向统一偏移 **+32 源像素**（2026-09-27）

### 方法

在差图（phase2 − phase0）里**实测**差异的包围盒，**不假定位置**，只给定邻域窗口搜索。

### 结果

| 按钮 | 假定起点（按 Location） | 实测包围盒起点 | 偏移（屏幕） | 偏移（源像素） |
|---|---|---|---|---|
| F92 武士 | (746,896) | (746,960) | **(+0, +64)** | (+0, **+32**) |
| F95 法师 | (830,896) | (750,960) | (-80, +64) | — |
| F98 道士 | (918,896) | (838,960) | (-80, +64) | — |
| F86 确认 | (1114,946) | (1114,1010) | **(+0, +64)** | (+0, **+32**) |
| F89 取消 | (1196,946) | (1116,1010) | (-80, +64) | — |

### 解读

1. **y 方向一律 `+64` 屏幕像素 = `+32` 源像素**（缩放 = 2）。**五个按钮完全一致**，
   是**系统性偏移**，不是个别错位。
2. **x 方向的 `-80` 是搜索窗口假象**：`80 = 40 × 2`，正是 `±80` 的邻域**越过了相邻按钮**
   （相邻间距 42 源像素 = 84 屏幕像素 < 80×2），把邻居的差异也框了进来。
   **真实 x 偏移为 0**（F92 与 F86 在无相邻干扰时都显示 `+0`）。
3. **所以真实偏移 = `(+0, +32)`（源像素）。**

### `32` 是什么

`Interface1c` 的帧自带 `offsetY`。早前读 WIL 帧头时已见**「WIL frame offset 均 (-24,-16)」**
之类的记录（PRE-09 记 F11–17 的 offset 为 `(-24,-16)`）。

⇒ **渲染时把帧自身的偏移也算进去了**，而我在验证时**假定只按 `Location` 定位** ——
**是"假定"错了，还是"实现"多了？本轮尚未判定**：

- 若原版 EI 也是 **Location + 帧偏移** 一起作用，则**我方实现正确**，是我的验证假定错了；
- 若原版只用 Location，则**我方多加了偏移**，需修正。

**这正是上一轮我所说的"检查强度不够时通过是廉价的"的具体案例** ——
上一轮的框式检查（40×38 的大框）**掩盖了 32 像素的偏移**，因为框足够大能盖住。

### 下一步

1. **核对 EI 的绘制是否含帧偏移**：看 `0x457DC6` 那处绘制帧 81 时，
   传给绘制函数的坐标是 `Location` 还是 `Location + 帧偏移`
   （此前已读到它 `push 0x180(384) / push 0xf7(247)` 然后 `call 0x45fd50`，
   而帧 81 的自身 offset 需从 WIL 读取）；
2. 若 EI 不含帧偏移 → 修正我方 `Location` 的算法；
3. 若含 → 修正**我的验证假定**（即上一轮的"偏差 +32"是预期的，不是缺陷）。
4. 无论哪种，**都要把结论写清楚**，而不是继续用"大致对上"的标准。

## 否定结果：帧 offset 恒定 `(-24,-16)`，不能解释 `+32` 偏移（2026-09-27）

### 读 WIL 帧头（`Interface1c.wil`）

| 帧 | 尺寸 | 帧头 w/h/x/y |
|---|---|---|
| 50 | 640×480 | w=640 h=480 **x=-24 y=-16** |
| 51 | 96×26 | w=96 h=26 **x=-24 y=-16** |
| 57 | 48×26 | w=48 h=26 **x=-24 y=-16** |
| 81 | 164×88 | w=164 h=88 **x=-24 y=-16** |
| 86 | 28×28 | **x=-24 y=-16** |
| 89 | 28×28 | **x=-24 y=-16** |
| 92 | 40×38 | **x=-24 y=-16** |
| 95 | 40×38 | **x=-24 y=-16** |
| 98 | 40×38 | **x=-24 y=-16** |

（帧头前 8 字节为 `w,h,x,y` 四个 int16；此处 `x=-24`、`y=-16`。）

### 结论：上一轮的假设**被否定**

我上一轮假设「`+32` 偏移来自帧自身的 `offsetY`」。**读帧头后发现：所有帧的 offset 都是
`(-24, -16)`，是恒定值，不随帧变化** ⇒ **它无法解释"五个按钮统一 +32"这一现象**。

（`(-24,-16)` 恒定的形态更像**统一的绘制锚点/原点约定**，不是逐帧定位偏移。
PRE-09 也记录过「WIL frame offset 均 (-24,-16)」——**"均"字正说明它是恒定的**，
我当时读到了却没意识到"恒定"意味着它解释不了逐帧差异。）

### 所以 `+32` 的成因**仍未查明**

可能的其他来源（**均未验证**）：

1. **我方代码**在设置 `Location` 时对 y 额外加了 32（例如某个 `SlotY` 常量或标签高度）；
2. **我方控件**（`DXButton`/`DXImageControl`）在绘制时对 y 加了固定偏移（如标题栏/边距）；
3. **帧 86/89/92/95/98 的贴图实际不是从 `Location` 开始画**，而是从 `Location + 32`
   （若控件有内部 padding）；
4. 我的**测量方法**本身在 y 上有系统性偏差（但对照区为 0，故此可能性较低）。

### 下一步

**从我方代码找**：读 `SelectScene` 里 phase 2 相关控件的 `Location`/`Size` 设置路径，
以及 `DXButton`/`DXImageControl` 的绘制实现中是否对 y 有附加偏移。
**这比继续在 WIL/反汇编里找更快**，因为 `+32` 是**我方渲染的实测结果**，
最可能出在我方代码里。

### 方法教训

**我上一轮把"matching a plausible cause"当成了"查明原因"** —— 看到 32 就联想到帧 offset，
**没有先去读帧头确认它是逐帧还是恒定**。读了才发现它是恒定的、根本解释不了。
**"假设一个合理解释"与"验证该解释"之间的差距，又一次成了错误的来源。**

## 用户决定：legacy 只对齐原版三职业，多余职业直接无视（2026-09-27）

**用户明确指示**：

> 我们原来的职业只有武士、道士、法师三种，我们移植到 godot 的不止这三种职业，
> 而实际上我们要的只有这三种，所以如果多的职业你可以直接无视，原版的，都只有三种，
> 我们跟原版对齐就行了。

### 据此核查我方现状（三处）

| 处 | 现状 | 是否暴露多余职业 |
|---|---|---|
| `_classBtn`（创建表单下拉） | 只有 3 项（战士/法师/道士） | ✅ 没有 |
| `_createClassButtons`（创建面板三按钮） | 只构造 3 个 | ✅ 没有 |
| `normalClass` / `pressedClass` | `{121,126,131,**136**}` / `{120,125,130,**135**}` —— **4 个元素** | ⚠️ 第 4 对是给多余职业预留的（循环上界为 3，当前无害但误导） |
| `baseFrame` 的 `_ => 1940` 兜底 | 注释写「其余（**含刺客**）本库无对应块，退回女性角色帧」 | ⚠️ **等于在代码里为第 4 职业留了位置** |

### 已改（收敛为显式三职业）

1. `baseFrame` 的 switch **显式列全 3×2=6 种组合**（补上原本落入 `_` 的
   `(Taoist, Female) => 1940`），兜底改为 `_ => 440` 并注明
   「其它职业不在 legacy 契约内，属越界调用时退回战士男帧」——
   **不再写成"刺客也走这里"**；
2. `baseFrames` 映射补上显式 `1940 => 15`（原落入 `_ => 15`，行为不变，仅为可读性）；
3. **删除第 4 对职业帧**：`normalClass`/`pressedClass` 由 4 元素减为 **3 元素**
   （`{121,126,131}` / `{120,125,130}`）。

### 验证

```
[LegacySelectButtonSelfTest] PASS 9 个按钮的 Index/HoverIndex/Location/Size 全部匹配 EI ctor 实参；
                             职业三钮(帧92/95/98->Warrior/Wizard/Taoist)的帧号与 MirClass 数值均匹配
```

`MirClass` 枚举本身仍保留 `Assassin = 3`（**不动**）—— 它是**共享定义**，现代游戏与
服务端可能仍用它；本次只收敛 **legacy 选角屏**的呈现，不删共享枚举值。

## `+32` y 偏移排查：已排除两条假设，成因仍未定位（2026-09-27）

### 已排除

| 假设 | 排除依据 |
|---|---|
| **帧自身 offset** 导致 | 读 WIL 帧头：**所有帧 offset 恒为 `(-24,-16)`**，不随帧变化，无法解释"五钮统一 +32" |
| **`UseOffSet` 应用了帧偏移** | `DXImageControl.UseOffSet` 是**默认 `false` 的公开字段**；`MakeSelectIconButton` **未设置它** ⇒ 帧偏移未参与 |

### 实测数据（重新核对）

- 假定：`Location.Y = 444`（F86），屏幕 `y = 58 + 444×2 = 946`
- **实测**：包围盒起点屏幕 `y = 1010`
- ⇒ 反推**实际源 y = (1010 − 58) / 2 = 476**
- **`476 − 444 = 32`** —— 与前面量到的 `+32` 源像素一致

### 已读但**不符合**的代码位置

`MakeSelectIconButton`（我方）：

```csharp
var size = MirSkin.GetSize(LibraryFile.Interface1c, frame);
...
var button = new DXButton
{
    LibraryFile = LibraryFile.Interface1c, Index = frame,
    HoverIndex = frame - 1, PressedIndex = frame,
    FixedSize = true, Size = size, Location = location,   // **无 y 偏移**
};
button.MouseClick += (o, e) => action();
```

`DXButton` 内部（已看的片段）只涉及 `_label`（文本子控件）的定位，**未见到对贴图的 y 偏移**。

### 剩余方向（下一步）

1. **`DXButton` 的贴图绘制实现**：它内部应有自己的图控件（`_image`?），
   需看它在 `DrawControl`/`_Draw` 里是否用了 `Location + something`；
   也需确认它如何取图（是否经 `DXImageControl` 而后者又加了偏移）；
2. **`FixedSize` 的语义**：若 `FixedSize = true` 时控件会按图片原始尺寸/偏移布局，
   可能引入 y 偏移；
3. **比较对照组**：**phase 0 的四钮**用 `SkinSelectButton`（另一条设置路径，视觉上位置正确、
   差分显示 0 偏移）—— **对比两条路径的差异**，最可能直接暴露 `+32` 来自何处。

### 为什么优先做第 3 条

phase 0 四钮**位置正确**（差分验证未变/未偏移），phase 2 五钮**偏移 32** ——
**两条不同的控件构造路径**。**对比它们**比继续单点猜测更快。

### 状态（诚实）

`+32` 偏移**已量化、已缩小范围（在我方渲染路径内）、已排除两条假设**，
但**成因尚未确定**。**未对其做任何"临时补偿"（如手动把 Location.Y 减 32）** ——
那是掩盖问题而非修正，且在成因不明时可能引入新的偏差。

## 能力解锁：`read` 工具可直接读图片；视觉验证从此可用（2026-09-27）

### 发现

此前一直以为"无 vision 能力，只能靠 OCR"（`dim modality list` 只列出 `ocr.recognize`）。
**但 `read` 工具本身可以直接读 PNG 并附到上下文供我直接查看。**

- 首次尝试的拼图（1248×2316）被拒：**"Image exceeds size constraints"** ⇒ 有**尺寸上限**；
- 改用 **400×648** 的裁剪图 ⇒ **成功**，我**直接看到了画面内容**。

**所以此前的"只能 OCR"是我自己的误判** —— 把"`dim image read` 未配置"等同于
"我无法看图"。**这是本任务里又一次"第一种路径不通就以为没有路径"。**
（与"`0x793D8` 不在 `.text` ⇒ 判为不可解"是同一个毛病。）

### 图像工具的正确用法（本轮确立）

1. **裁剪到小尺寸**（约 ≤800px 边长）再用 `read` 读取；
2. 大图**先裁**再读，不要试图直接读整张截图；
3. 需要**精确文字**时仍用 `dim ocr recognize`；需要**看形状/贴图/位置**时用 `read`。

### 本轮实际看到的（F86/F89 区域）

裁 `(1100,930)-(1300,1090)`，上=phase 0、下=phase 2：

- **phase 0**：只有洞窟背景与烘死的「WEMADE ENTERTAI…」文字，**无任何按钮**；
- **phase 2**：**两个圆形按钮 —— 左为绿色 ✔、右为绿色 ✘**，位置**压在背景最下缘文字行上**。

### 由此得到的三条结论

1. **F86/F89 确实渲染，且图形正确（左勾右叉）** ⇒
   - **印证我方命名** `_skinConfirmYes`（帧 86）/ `_skinConfirmNo`（帧 89）**正确**；
   - **印证文档 PRE-02**「F86/F87 勾选态图形、F89/F90 叉形图形」**完全吻合**。
2. **位置偏低是真实且肉眼可见的** —— 按钮压在背景最下缘文字上，与实测 `+32` 源像素一致。
3. **贴图没有串位** —— 图形内容正确，问题**只在位置**。

### 对 `+32` 排查的影响

现在可以**直接看**修改前后的画面，A/B 测试的判读不再依赖间接的像素统计。
**下一步做受控 A/B**：把 `MakeSelectIconButton` 与已知正确的 `SkinSelectButton` 的
设置差异（`Text = string.Empty`、`PressedIndex = hoverFrame`）逐项对齐，重建后**看图**，
若偏移消失则定位到具体那一项；若不变则排除该方向。

**与之前的"试错"区别**：这是**受控 A/B + 直接观察**，且每次只动一项。

## 更正：差分 0 被误读为"正确隐藏"（2026-09-27）

### 我此前的解读

差分检查中，**phase 0 四钮位置差异为 0**，我写成：

> 「phase 0 四钮 —— 全部未变（0 像素）⇒ **phase 2 时它们确实全部隐藏**」

### 这个解读**不成立**

**差异 0 只说明"两个相位下该处画面相同"**，它**同样兼容**另一种解释：

> **phase 0 时那四个按钮就没有渲染出来**（两相位都是空的，故差异为 0）。

**两种情形差分都是 0。我选了对自己有利的那一种，而没有去区分。**
这与本任务反复出现的"用一个合理解释代替验证"是同一毛病。

### 如何区分（下一步必做）

**在 phase 0 的截图里直接确认那四个按钮存在** —— 现在有了看图能力，做法是：

1. 裁 `(440,93)`、`(79,243)`、`(259,49)`、`(28,438)` 四处的屏幕区域（按 `scale=2,
   offset=(214,58)` 映射）；
2. **直接看图**确认「创建角色 / 删除角色 / 开始游戏 / 结束」四钮是否真的画在那里；
3. 若**没画** ⇒ 找到"为什么没渲染"（可能是位置错、可能被 `Visible=false`、可能被遮挡）；
   若**画了** ⇒ 此前的"隐藏"解读成立，但**仍需看它们在 phase 2 是否真的消失**（
   这次要用"phase 2 该处是否为背景"来判，而不是靠差分）。

### 顺带：本轮读到的 `DXButton.DrawControl` 分支

```csharp
protected override void DrawControl()
{
    if (LegacyHudCaption)
    {
        if (IsPressed || Pressed) { ... DrawTextureRect(texture, Rect2(Vector2.Zero, Size), false); }
        return;                     // 不调 base ⇒ 不加帧偏移
    }
    int index = GetCurrentIndex();
    if (index >= 0)
    {
        base.DrawControl();          // DXImageControl.DrawControl：UseOffSet 为真才加 off
        if (MirSkin.GetTexture(LibraryFile, index) == null) DrawFallbackButton();
        return;
    }
    ...
```

`DXButton : DXImageControl`（第 10 行）。五钮**未设 `LegacyHudCaption`**，走第二条路径，
而 `UseOffSet` 默认为 `false` ⇒ `off = Zero` ⇒ **`+32` 仍不是来自这里**。

**⇒ `+32` 的成因仍未定位，且现在多了一个更根本的疑点：四钮在 phase 0 到底画没画。**

### 状态（诚实）

- `+32` 偏移：已量化、已排除 3 条假设（帧 offset / `UseOffSet` / `DrawControl` 分支），**成因未定**；
- **四钮相位 0 渲染情况：此前"已正确隐藏"的结论作废**，需重新确认；
- **未做任何临时补偿**。

## 决定性：phase 0 四钮**不在算出的位置**（映射错），两组按钮坐标系不同（2026-09-27）

### 直接看图的结果

按映射 `(214 + x×2, 58 + y×2)` 裁出 phase 0 四钮位置，**直接看**：

| 位置 | 看到的内容 |
|---|---|
| F51 (440,93) | **纯洞窟背景** |
| F55 (259,49) | **纯洞窟背景** |
| F53 (79,243) | **纯洞窟背景** |
| F57 (28,438) | **纯洞窟背景** |

**四个位置全无按钮。** 而 **OCR 明确读到**「开始游戏 / 创建角色 / 删除角色 / 结束」。

⇒ **四钮确实渲染了，但不在算出的位置** ⇒ **我的坐标映射是错的**。

### 同时：phase 2 五钮**在**我裁的位置附近

上一轮裁 `(1100,930)-(1300,1090)`，**亲眼看到勾(✔)与叉(✘)** ——
而 F86/F89 的假定位置正是 `(1114,946)`/`(1196,946)`，**在该裁剪范围内**。

⇒ **phase 2 五钮大致在算出的位置，phase 0 四钮完全不在** ⇒ **两组按钮不在同一坐标系。**

### 与代码的吻合：phase 0 四钮是 **Reparent** 来的

`ApplyLegacyEiSelectLayout()` 开头有注释：

> **先把三个主按钮从 `_skinPanel` 摘到 `_uiLayer`，再设坐标** ——
> 顺序不能反：Reparent 默认**保留全局变换**，会把局部坐标换算成补偿值

即 phase 0 的四钮经历了 **Reparent**（带上原容器的全局变换补偿），
而 phase 2 五钮由 `MakeSelectIconButton` **直接 `AddChild(_uiLayer)`**。
**两条路径的坐标语义不同** ⇒ 同一批"原版坐标"落到屏幕上就不同。

### 由此**作废**的此前结论

| 此前结论 | 现状 |
|---|---|
| 「差分验证成功：五钮出现 / 四钮隐藏」（差分为 0 解读为"正确隐藏"） | ❌ **作废**（0 是因为**四钮不在那儿**，不是我解读的"隐藏"） |
| 「五钮在 y 方向统一偏移 **+32** 源像素」 | ⚠️ **需重新评估** —— 它建立在同一套（错误的）映射上 |
| 「phase 0 四钮坐标与 ctor 实参一致」 | ⚠️ **属性一致**（自检 PASS）不等于**渲染位置一致** |

**注意**：自检报的是**控件属性**（`Location` 字段值 = 原版坐标），这是**真的**；
但**属性值 ≠ 屏幕位置** —— 中间隔着 Reparent 的全局变换补偿。
**我此前把"属性断言 PASS"当成了"位置正确"的旁证，这一步不成立。**

### 下一步（明确）

1. **实测四钮的真实屏幕位置**：在 phase 0 截图里搜索四钮贴图（用其解码帧做模板匹配），
   得到真实位置；
2. **测出 Reparent 带来的实际坐标偏移量**，并判断它是否**应该存在**
   （原版没有 Reparent 这一层，故若补偿导致位置偏离原版坐标，即是缺陷）；
3. 修正：让四钮的渲染位置等于 1:1 映射下的原版坐标（可能需要改 Reparent 顺序或
   改设置 `Location` 的时机）；
4. 同时**重建正确的映射**（用已知位置的元素反求 `scale`/`offset`），再重做全部视觉验证。

## 更正：不是"两组坐标系不同"，而是**单一 y 偏移影响两组**（2026-09-27）

### 直接看整幅画面

把内容区（`scale=2, offset=(214,58)`，800×600 → 屏幕 `(214,58)-(1814,1258)`）
裁出并缩到 600×450 **直接看**，四个 phase 0 按钮**清晰可见**：

| 按钮 | 图上位置 | 换算回内容坐标 | 原版坐标 | y 偏差 |
|---|---|---|---|---|
| 开始游戏 | ~(200,72) | ~(267,96) | (259,49) | **+47** |
| 创建角色 | ~(350,103) | ~(467,137) | (440,93) | **+44** |
| 删除角色 | ~(85,208) | ~(113,277) | (79,243) | **+34** |
| 结束 | ~(30,352) | ~(40,469) | (28,438) | **+31** |

（图上读数是肉眼估算，故偏差值有 ±10 量级误差；但**方向与量级一致**。）

### 结论 1：四钮**确实渲染**，且 x 大致正确、**y 一律偏低约 +32～+47**

⇒ **上一轮"phase 0 四钮完全不在位置、两组坐标系不同"的结论下早了** ——
真相是**一个统一的 y 偏移影响了两组按钮**（phase 2 五钮量到 `+32`，四钮目测 `+31~47`）。

**"裁 (440,93) 看到纯背景"的原因也清楚了**：按钮实际在 y≈137，
**恰好落在我那个裁剪框（y≈93±60）之外** —— 不是"没渲染"。

### 结论 2：一个可疑的算术吻合

- 窗口 bounds：**1014×690**
- 截图尺寸：**2028×1316** = **1014×658** 的 2 倍
- ⇒ 窗口比内容**高 32 物理像素**（疑为标题栏）
- ⇒ 该 32 物理像素在 2× 截图里是 **64 像素** —— **正是我在差分量到的 `+64` 屏幕偏移**

**另一个旁证**：整屏截图的 OCR **第一行就是 `ZirconClient （DEBUG）`** ——
**标题栏确实在截图里**。

**⇒ 高度可疑的解释：截图包含了标题栏，而我的映射公式 `y = 58 + y_src×2`
是从"视口内容顶端"起算的，漏掉了标题栏那一段。** 若成立，则：

- **偏移不是我们代码的缺陷**，而是**我的映射漏算**；
- 修正映射（`y = 标题栏高 + 58 + y_src×2`）后，**所有此前的视觉验证需重做**。

**但这是推断，尚未证实** —— 需实测标题栏在截图中的像素高度，并与窗口 bounds 的差值（32×2=64）核对。

### 由此**仍然作废/待重做**的结论

| 结论 | 现状 |
|---|---|
| 「phase 0 四钮完全不在位置、两组坐标系不同」 | ❌ **本轮再度更正**（是统一偏移） |
| 「差分验证成功：五钮出现 / 四钮隐藏」 | ❌ 作废（映射未含标题栏） |
| 「五钮 y 偏移 +32 源像素」 | ⚠️ 数值可能对，但**归因可能错**（疑为映射问题而非代码问题） |
| 「四钮坐标与 ctor 实参一致」 | ✅ **属性层面成立**（自检 PASS），与渲染位置是两件事 |

### 下一步（决定性、可执行）

1. **实测截图顶部非内容区域的高度**：裁截图最上方一条（y=0..120）直接看，
   确认标题栏到内容区的分界像素位置；
2. 与窗口 bounds 差值（`690−658 = 32` 物理 = `64` 截图像素）**核对**；
3. 若吻合 ⇒ **修正映射公式**，然后**重做全部视觉验证**（四钮、五钮、登录页）；
4. 若不符 ⇒ 偏移确在我方代码，回到 `DXButton`/`Reparent` 方向继续查。

**现在有看图能力，第 1、2 步都能直接做。**

## 结案：`+32` 偏移是**测量误差**（截图含标题栏），我方代码无偏移（2026-09-27）

### 决定性证据一：尺寸算术

- **`/tmp/p0_fixed.png` 尺寸 = 2028×1380**
- 窗口 bounds = **1014×690**（来自 `kCGWindowListCopyWindowInfo`）
- **`1380 = 690 × 2`** ⇒ **截图 = 窗口 bounds（含标题栏）的 2 倍**
- 内容区高度 = 658 物理 ⇒ **`690 − 658 = 32` 物理像素 = 标题栏**
- **`32 × 2 = 64` 截图像素** —— **正是我在差分量到的 `+64` 屏幕偏移**

### 决定性证据二：直接看截图顶部

裁 `(600,0)-(1400,140)` 直接看，清楚分三段：

| 带 | 内容 | 含义 |
|---|---|---|
| y≈0–55 | 深灰/黑 | **标题栏** |
| y≈55–110 | 中灰 | **视口顶部的居中留白**（UiScaler 的 `offset.Y`） |
| y≈110–140 | 洞窟画面 | **游戏内容开始** |

**内容起点 ≈110–116**，而正确公式为：

```
y_screen = 64（标题栏） + 58（UiScaler 居中偏移） = 122
```

与目测 ~110–116 吻合（肉眼 ±10 内）。

### 结论

**我此前用的映射 `y_screen = 58 + y_src × 2` 漏掉了 64 像素的标题栏。**

⇒ **所谓的「五钮 y 方向统一偏移 +32 源像素」是我自己的测量误差，不是我方代码的缺陷。**

### 由此**全部澄清**的结论

| 此前结论 | 最终判定 |
|---|---|
| 五钮 y 偏移 +32 源像素 | ❌ **作废** —— 是测量误差（漏算标题栏 64px） |
| phase 0 四钮"不在位置、两组坐标系不同" | ❌ **作废** —— 同理，是映射漏算 |
| phase 0 四钮"完全没渲染" | ❌ 作废 —— 四钮**确实渲染**（整幅画面已直接看到） |
| 差分验证"五钮出现 / 四钮隐藏" | ⚠️ 数值层面作废（映射错）；但**结论方向**（phase 2 显示五钮、隐藏四钮）**与目视一致**，需用修正后的映射重做一次 |
| 四钮属性与 ctor 实参一致 | ✅ 成立（自检 PASS），且**渲染位置也无需修正** |

### 正确的映射（今后统一使用）

```
x_screen = 214 + x_src × 2
y_screen = 64 + 58 + y_src × 2 = 122 + y_src × 2
```

（`214` 与 `58` 来自 `[UiScaler] scale=2 offset=(214,58)` 日志，且已由顶部留白带目视核对；
`64` 来自窗口 bounds 与内容高度之差。）

### 教训（本任务里最值得记的一条）

**连续四轮都在追一个"不存在的缺陷"** —— 从"假设帧 offset"→"假设 UseOffSet"→
"假设 DrawControl 分支"→"假设两组坐标系不同"，每轮都排除一条、再提一条，
**却始终没有质疑最底层的那个前提：我的坐标映射对不对。**

**若第一轮就先验证映射**（用看图能力裁一张、直接看内容区从哪开始），
四轮排查可以省掉。**根因是"我没有验证测量工具本身"，而这不是第一次**
（早先像素差分的三次失败同源）。

**新规则**：**任何基于坐标的验证，第一步必须先用"直接看图"确认映射**
（已知元素的屏幕位置），再用于其他推断。

## 最终视觉验证：phase 2 五钮位置/贴图全部正确（2026-09-27，目视确认）

### 方法

用**修正后的映射** `x = 214 + x_src×2`、`y = 122 + y_src×2`（122 = 标题栏 64 + UiScaler 58），
裁 phase 2 内容区 `(214,122)-(1814,1322)` 并缩到 600×450 **直接看图**。

### 观察到的事实

| 元素 | 图上位置 | 换算回内容坐标 | 原版坐标 | 判定 |
|---|---|---|---|---|
| **三个职业圆钮** | ~(200–275, 325) | ~(267–367, 433) | 266 / 308 / 352 @ 419 | ✅ **吻合** |
| **✔ 与 ✘** | ~(340–380, 345) | ~(453–507, 460) | 450 / 491 @ 444 | ✅ **吻合** |
| 配置按钮（右上角） | ~(565, 25) | — | — | ✅ |
| **phase 0 四钮** | **完全不可见** | — | — | ✅ **确实隐藏** |

### 三个职业钮的图形（与文档对照）

图中三个圆钮依次是：**斜笔 / 环形箭头 / 卷页（文书）** ——
**与本文档 PRE-02 的描述逐项吻合**：

> F92/F93 是斜笔/金色圆形底图，F95/F96 是环形箭头图，F98/F99 是卷页/文书图

⇒ **我方按证据改的三钮命名（`_skinClassWarrior`/`_skinClassWizard`/`_skinClassTaoist`）
不仅语义正确，贴图也确实是那三张**。

### 三条结论

1. **五个 phase-2 按钮全部渲染，位置与贴图都正确** ✅
2. **phase 0 四钮确实隐藏** ✅ —— 这次是**目视确认**，取代此前（已作废的）差分推断
3. **`+32` 确系测量误差** ✅ —— 用修正映射后位置完全对上，**代码无需改动**

### 与前几轮的关系（诚实收尾）

- 前几轮基于错误映射得出的"偏移""坐标系不同""未渲染"**全部作废**；
- 但**由它们推动的排查**（读 WIL 帧头、查 `UseOffSet`、读 `DrawControl`、发现 Reparent）
  产出了**真实可用的知识**（帧 offset 恒为 `(-24,-16)`、`DXButton : DXImageControl`、
  Reparent 会保留全局变换的坑）；
- **净结果**：phase 0 与 phase 2 的按钮**位置与贴图均已正确**，**无需再改代码**。

### 我自己要记的

**"验证通过"与"验证失败"都必须先问"测的准不准"。** 本轮前四轮的全部结论都建立在一个
**未经验证的映射**上 —— 而验证映射只需**裁一张图看一眼**。**工具本身要先验证**，
这条在本任务里已经犯过三次（像素差分判据、扫描器偏移、坐标映射）。

## 最终验证（二）：phase 0 四钮位置同样正确（2026-09-27）

用修正映射（`x = 214 + x_src×2`、`y = 122 + y_src×2`）裁 phase 0 内容区并直接看：

| 按钮 | 图上位置 | 换算回内容坐标 | 原版坐标 | 判定 |
|---|---|---|---|---|
| 开始游戏 | ~(200,40) | ~(267,53) | (259,49) | ✅ |
| 创建角色 | ~(350,70) | ~(467,93) | (440,93) | ✅ **y 完全吻合** |
| 删除角色 | ~(85,180) | ~(113,240) | (79,243) | ✅ **y 吻合** |
| 结束 | ~(30,322) | ~(40,429) | (28,438) | ✅ **y 吻合** |

（x 方向有 ~8–27 的目测误差，来自我在 0.75 缩放图上估读文字左缘，非实际偏差。）

**phase 0 也确认无 phase 2 的控件**（三个职业钮与 ✔/✘ 均不可见）✅
**配置按钮**在右上角可见 ✅

### 选角屏最终结论

| 项 | 判定 | 依据 |
|---|---|---|
| **phase 0 四钮位置** | ✅ **正确** | 目视（修正映射）+ 自检属性一致 |
| **phase 0 四钮贴图/文字** | ✅ 正确 | OCR 读出四段文字 + 目视 |
| **phase 2 五钮位置** | ✅ **正确** | 目视（修正映射） |
| **phase 2 五钮贴图** | ✅ 正确 | 目视：斜笔 / 环形箭头 / 卷页 / ✔ / ✘ |
| **相位切换（显隐）** | ✅ 正确 | 目视两相位画面 |
| **背景 F50** | ✅ 正确 | 目视（640×480 洞窟画面） |

**⇒ 选角屏（phase 0 与 phase 2）的布局与贴图均已与 EI 对齐，代码无需位置改动。**

### 本任务中"坐标类"工作的最终账目

| 结论 | 现状 |
|---|---|
| 我方画布基准 1024×768 → **800×600** | ✅ **真实修正**（有代码改动，已验证） |
| 四钮 hover 帧 51/53/55/57 → **52/54/56/58** | ✅ 真实修正（phase 0 hover = normal+1） |
| 五钮 hover 帧 → **frame-1** | ✅ 真实修正（phase 2 hover = normal−1） |
| 帧 86 动作 → **SubmitSkinCharacter()** | ✅ 真实修正 |
| 三钮动作 → **选职业 0/1/2** | ✅ 真实修正 |
| 三职业收敛（删第 4 职业预留位） | ✅ 真实修正 |
| ~~五钮 y 偏移 +32~~ | ❌ **测量误差，非缺陷** |
| ~~两组按钮坐标系不同~~ | ❌ **测量误差，非缺陷** |
| ~~四钮未渲染~~ | ❌ **测量误差，非缺陷** |

**五处真实修正 + 三处"追了但不存在"的假缺陷。**

### 一句话总结这次排查的教训

**三处假缺陷的共同根因是同一个未经验证的测量前提**（坐标映射漏算标题栏 64px）。
若在第一次量到"+32"时就**裁一张图直接看**，三次排查都不会发生。
**"工具/前提先自证"应当排在"结论"之前** —— 这是本任务反复付出代价才确立的规则。

## F602 确认框：位置修正 + 端到端勾选验证（2026-09-27）

### 发现的缺陷

选角屏点「开始游戏」弹出 F602 确认框时，窗口渲染在画布左上角 `(0,0)`。
原版证据（`notice-prompt-window-evidence.json`，primary-static）：
主初始化 `0x427960` → 构造 `0x43E260` 传参 `[15, "GameInter", 602, 107, 110, 584, 252, 0, 3]`，
即 800×600 父原点下固定位置 `(107,110)`。

### 修正

- `NoticeDialog` 构造函数默认 `Location = (107,110)`（此前无默认，`WindowManager.Open` 也不设位置）。
  `GameScene.cs:4619` 与 `LegacyHudLayoutLab.cs:104` 已有显式 `(107,110)`，语义一致，无冲突。
- `DXCreatePreviewControl`（创建面板大预览）：刺客女发型 1 的 ProgUse 1160 帧改为
  在盔甲/武器**之前**绘制，对齐原版 `PreviewPanel_AfterDraw` 层序（此前画在最上层）。

### "공지 수정" 标题来源裁定

`grep` 全仓无 `공지` 字符串；直接解码 `GameInter.wil` F602（1024×256，alpha bbox
`(220,2)-(802,253)`），标题栏文字**烘焙在帧内**。我方 `HasTitle=false` 不生成 TitleLabel，
标题完全来自 WIL 帧 —— 与原版一致，无需代码。

### 端到端验证（Quartz CGEventPost 点击，非 osascript）

osascript System Events 点击在本机不生效（无辅助功能权限），Quartz `CGEventPost`
可用（以「结束」按钮点击使客户端退出为决定性证据）。屏幕↔逻辑映射
`screen = (338 + x, 220 + y)`（窗口 (338,188)，标题栏 32pt），用「开始游戏」命中反推确认。

流程：`--stay-select` 登录 → 点「开始游戏」→ 日志 `确认框 F602 已弹出`，
截图确认对话框位于 `(107,110)`（此前在左上角）→ 点 F606 勾选 →
日志 `确认框勾选 -> SendStartGame` → `[Game] 进入游戏! 玩家: TestHero, 位置: (165,237), 地图: 1`，
截图确认比奇城游戏画面（legacy HUD：顶部背包、底部红蓝球/技能槽）正常渲染。

### 遗留/偏离记录

- F602 勾选正文「文字暂留空」：原版 id15 是行会公告窗（行会路径注入文本），
  我方作为进游戏确认框复用，文案占空为已知偏离（NOTICE-01 已登记原版语义差异）。
- 创建面板名字框预填 `TestHero`：Zircon 扩展面板（PRE-16），原版从空白开始，
  保留为测试便利的已记录偏离。
- 同账号重名建角：`SEnvir.cs` 同账号跳过查重，属服务端行为，超出本次 UI 范围。

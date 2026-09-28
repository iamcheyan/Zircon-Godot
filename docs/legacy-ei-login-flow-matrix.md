# Legacy EI 登录—选角—创建—删除—进游戏 阶段差异矩阵

> 工作区：/home/tetsuya/.hermes/cache/scratch/zircon-ei-login-test
> 生成时间：2026-09-28
> 证据等级：primary-static = EI EXE .text 字节码；primary-resource-visual = WIL 帧像素核验；source-confirmed = 旧 Client/ C# 源码；runtime-verified = 本机 EI 或 Godot 实测；derived = 综合推导；candidate = 候选未闭合；visual-candidate = 仅图形。

## EI 视频/媒体 ffprobe 实测（本机 mir2ei/LegacyEI/Data）

| 文件 | 编码 | 尺寸 | 帧率 | 时长 | 帧数 | 音轨 |
|---|---|---|---|---|---|---|
| wemade.dat / .ogv | indeo5 / theora | 640×360 | 29.97 | 4.97s | 149 | dat 含 pcm_s16le，ogv 未保留音频（Vorbis 流 0 时长） |
| ei_Login.dat / .ogv | indeo5 / theora | 640×360 | 29.97 | 54.35s | 1629 | dat 含 pcm_s16le，ogv 无独立音轨 |
| CreateChr.dat / .ogv | indeo5 / theora | 640×**480** | 29.97 | **1.30s** | **39** | dat **无**音轨；CreateChr.wav 通过独立声音对象 +0x113C 播放 |
| StartGame.dat / .ogv | indeo5 / theora | 640×**480** | 29.97 | **1.37s** | **41** | dat **无**音轨；StartGame.wav 独立播放（+0x1144） |

注意：CreateChr/StartGame 都是 480 高（与 F50 同高），wemade/ei_Login 是 360 高（信箱式）。转码 ogv 时长/帧数/尺寸与 dat 一致。

## EI 选角 phase 字节状态机（[0x930]，primary-static）

| phase | 行为 | 进入写入点 |
|---|---|---|
| 0 | 4 按钮角色列表（F51/F53/F55 + 退出）；1s 周期 SelChr 音效/MP3 | ctor 0x456C03=0；0x45770F(3→0) |
| 1 | CreateChr.dat pump（+0x780），**无输入**；结束自动→2 | 0x459AC5 (F51 点击空槽) |
| 2 | 5 底部按钮（F92/F95/F98/F86/F89）+ 密码编辑；动画角色列表 | 0x45763D（phase 1 pump 结束） |
| 3 | 等待服务器：F89 确认发 msgid 0x64；冷却 | 0x45922F（server 0x209）、0x459D48（F89 点击） |
| 4 | StartGame.dat pump，结束后→0x4570A0 进入游戏（mode 3） | 0x459465（server 0x20D 成功） |

## 阶段矩阵

### S0 启动 WeMade Logo

| 维度 | EI 原始（primary-static） | 旧 Client C# (source-confirmed) | 当前 Godot | 差异/决策 |
|---|---|---|---|---|
| phase | intro 子阶段 [0x8A5]=0→1→2；[0x8A4] stage 0→1→2；0=加载 screen frame 0x3C (wemade.dat)；1=0x45C900(&+0x6F4 wemade.dat) → 2；2=frame +0x5B0 (Interface1c)+[0x8A4]=1 | LoginScene.ProcessingLogo；每帧 pump wemade.dat 直到结束，Dispose 后切登录页 | LoginScene.PlayLegacyBootLogo 播 wemade.ogv，Finished 时 QueueFree；不阻塞输入 | ogv 时长/dat 一致(4.97s)；Finish 后已移除。**已知差异**：Godot 不 pump 结束帧后"frame +0x5B0"过渡帧，但 wemade 最后一帧就是 logo 淡出到黑，等效。 |
| 音轨 | wemade.dat 内含 pcm 音频 | 随 AVI 播放 | ogv VolumeDb=-80（静音），未另处理音轨 | **问题**：ogv 未转音频，wemade 是无声 logo 的概率高（通常 WeMade logo 无 BGM），runtime 验证时确认。 |
| 尺寸 | 640×360（居中贴在 640×480 之上，留上下黑边） | 同 | 当前 PlayLegacyBootLogo 用什么尺寸？需要核实 LoginScene 代码 | 待核实 |

### S1 登录表单 ei_Login 背景

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 媒体 | ei_Login.dat 640×360 54s 循环（phase 1→2 加载 Interface1c F11/F13/F15/F17 按钮） | LoginScene 播放 ei_Login.avi 循环作为背景 | LoginScene.ApplyLegacyEiLoginLayout 播 ei_Login.ogv 循环；按钮位置待核 | 时长与 ogv 一致；循环逻辑已通过截图验证（68b2f970 提交）。 |

### S2 连接 / 等待角色列表 0x208

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 触发 | 登录成功 → mode=2 → parent 对象 ctor 0x456CB0，发送角色列表请求；server msg 0x208 (520) → 0x458FBD 清 +0xCB8 填槽，[0x1168]=-1，**phase=0** | CNetwork 收到角色列表后 new SelectScene | LoginScene 收到 LoginResult 时切 SelectScene；SetCharacters → RefreshList | Zircon 协议用 StartGame/LoginResult，EI 0x208 仅作语义参考。**差异**：RefreshList() 末尾调 SelectSkinCharacter(0)（自动选首项），原版 [0x1168]=-1 表示无选中，需确认是否应保持无选中直到用户点击。 |

### S3 洞窟选角 F50

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 背景 | Interface1c **F50** 640×480 @ (-24,-16) 偏移 (primary-static，interface1c-parent-context.json F50: w=640,h=480,offX=-24,offY=-16) | Client/Scenes/SelectScene 用 GameInter 面板+自定义绘 | BuildLegacySelectUi 使用 F50 @ 640×480 左上角 (L784-794) | 背景帧正确；但画布基准是 1024×768（UiScaler.BaseWidth），F50 原生 640×480 贴左上与 EI 800×600 窗口坐标一致。1024×768 下右侧/下方会露黑，需确认 EI 原版窗口 800×600 还是 1024×768（primary 帧头 offset 提示 800×600 时代）。 |
| 按钮 F51 创建 | +0x9E8, frame 51, **(440, 93)**, 96×26, 文字"创建角色"（visual high confidence） | Client 用 CreateButton @ 自己坐标 | _skinCreate 初始用 Index=-1 文本按钮 @ (120, 382) 80×21；之后 Apply... 重写到 (79,243) 53/54 帧（代码注释 L576 行写"p0-2 删除角色"却配 53 帧且坐标 (79,243)） | **严重错误**：当前 Godot 给创建按钮用了 Interface 库帧 51/52（坐标 79,243），**不是 Interface1c F51 @ (440,93)**。需要重新从 Interface1c 加载 F51/F53/F55 并使用 primary 坐标。 |
| 按钮 F53 删除 | +0xA9C, frame 53, 候选坐标（待从 setrect 提取），96×26，文字"删除角色"（visual high） | DeleteButton 自定义 | _skinDelete 同样错配坐标和帧 | 待精确定位 |
| 按钮 F55 开始 | +0xB50, frame 55, **(259, 49)**, 96×24, "开始游戏"（visual high） | StartButton 自定义 | _skinStart @ (25,382) 80×21 文本按钮 | 坐标/帧/库全部错误 |
| 按钮 F57 退出 | +0xC04, frame 57, 48×26 | ExitButton | _skinExit 存在但坐标待查 |  |
| F92/F95/F98/F86/F89 phase 2 | 底部 (450,444)/(491,444) 等；F86 28×28 toggle/delete 候选；F89 28×28 confirm→phase3→发 0x64（primary-static） | 无（Client C# 走 NewCharacterDialog 模态框） | _skinConfirmYes/No 已建 (450,444)/(491,444)，帧 86/85、89/88；class 按钮位置待核 | F89 确认已接线到 phase3，但 F86 实际是 toggle/delete 候选不是"Yes"；相位 2 是"创建编辑"的底部按钮不是删除确认。**需更正**。 |
| 角色槽数 | slot stride 0x40，idx 0..1（"2 slots"）来自研究版 EXE 反编；F603 显示 16+ rect 布局，**不是可视上限证据** | CharacterList max 4（List<SelectInfo>） | _characters.Count < 4 门控创建；UpdateCaveSlots 渲染 2 个洞窟槽 | 保留 2 槽视觉 + 4 槽协议上限，与现有实现一致；明确标注"2 槽视觉"= visual-candidate，不视为业务上限。 |
| 角色尺寸/锚点 | 0x458EC0 entity create，动画帧 [slot+0x3C]；帧由 Animationsc.wil 提供；原 3D 投影尺寸未直接给 RECT | SelectScene.CharacterAnimation 大尺寸居中 | _characterAnimation + 洞窟阴影；注释"脚底线/帧组"为推导值 | 角色尺寸/锚点需要在 runtime 截图与 EI 视频/截图对照验证，本轮保留现有值并标 visual-candidate。 |

### S4 创建过场（phase 1 CreateChr.dat）

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 触发 | F51 点击空槽 → 0x459AC5 phase=1，+0x1160=0，0x45BF30 加载 `.\Data\CreateChr.dat` 到 +0x780 + 0x45C4C0 pump；同时播 CreateChr.wav（+0x113C） | NewCharacterDialog 直接弹（Client C# 是现代版，非 EI phase 1/2） | 点 _skinCreate → SetSelectPhase(1) → PlayLegacyTransition("CreateChr") + SoundIndex.LegacyCreateChr → **立即** ShowCreateCharacterPanel() | **严重时序错误**：phase 1 是"过场播放中、无输入"，CreateChr 1.3s 播完 phase 自动切 2 才显示编辑表单；当前立即 ShowCreateCharacterPanel 导致面板与视频叠在一起。应在视频 Finished 回调里 ShowCreateCharacterPanel() 并 SetSelectPhase(2)。 |
| 视频尺寸 | 640×480（与 F50 同尺寸，填满洞窟） | N/A | Video 640×480 @ Vector2.Zero（代码 L710） | 正确 |
| 时长 | 39 帧 / 1.30s | N/A | ogv 1.30s 一致 | 一致 |
| 音效 | CreateChr.wav 一次性 | N/A | 已接 LegacyCreateChr | 已接 |

### S5 创建编辑（phase 2）

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| UI | 5 底部按钮 F92/F95/F98/F86/F89 + 密码编辑 + 角色动画；密码发送 0x64 '%s/%d'；class 通过 class name suffix 渲染（[男/[女 + 武士]/法师]/道士]） | NewCharacterDialog：260×650 面板，职业/性别/发型/发色/甲色/名字输入框 + 确认 | _skinCreatePanel 260×650 居中，含 class/gender/hair 等 + 名字框 + 确认按钮 | 旧 Client C# 形式与 Godot 类似，已实现；**但 EI 原版 phase 2 实际是密码编辑而非全属性编辑面板**（primary-static 显示 password edit）。因 Zircon 服务端不支持 EI 密码校验，保留 Zircon 风格建角面板可接受，但 phase 2 底栏 F92/F95/F98/F86/F89 不应同时显示（当前 SetSelectPhase(2) 会让 5 个按钮 Visible=true）。需避免两种 UI 重叠。 |
| 确认动作 | F89 (0x459D48) → phase=3，发 0x64 | 创建按钮 → SendNewCharacter | _skinCreateConfirm → SubmitSkinCharacter → 立即 SendNewCharacter | 不映射旧协议即可，但要保证 phase 正确：提交后 SetSelectPhase(3)（等待）。当前 SubmitSkinCharacter 只置 Enabled=false，没有切 phase。 |

### S6 创建结果

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 成功 | server case 0x209 会**再次**播放 CreateChr.dat 并发 0x64（密码提交）；最终回到 phase 0 重新 0x208 刷新 | OnNewCharacter 成功 → 回到 SelectScene 列表刷新 | OnNewCharacterResult 成功 → SetSelectPhase(2) + RefreshList + 若自动登录则 CallDeferred(AutoStartGame) | **问题**：成功后设 phase=2（5 按钮态），但 EI 成功路径是回 phase 0（列表 4 按钮）；需要改回 phase 0。 |
| 失败 | server case 0x20A error 弹错误框；0x20C 900 弹框 | MessageBox | _pendingNewCharResult 显示文字 | 已基本实现。 |

### S7 删除确认/执行

| 维度 | EI | 旧 C# (source-confirmed, L588-611) | Godot (L1303-1321) | 差异 |
|---|---|---|---|---|
| 触发 | F53 点击（具体 handler 未在 login-flow-evidence 中直接给出，候选 +0xA9C） | DeleteButton_MouseClick | OnDeletePressed |  |
| 确认 UI | （未直接闭合；证据链上 EI 可能是输入框 + 5 秒，也可能是不同对话框；标注 candidate） | **DXMessageBox YesNo，YesButton 默认禁用**，label 文字"Please wait X seconds before confirming"，倒计时 5 秒；到 5 秒后启用 Yes 并改 label；Yes 点击 → Enqueue C.DeleteCharacter{ CharacterIndex, **CheckSum=CEnvir.C** } | ConfirmationDialog（系统原生），Confirmed 立即 SendDeleteCharacter，**无 5 秒延迟**，**无 CheckSum 字段** | **严重差异（source-confirmed in old C#）**：旧 Client C# 强制 5 秒等待防误删 + CheckSum 校验；Godot 当前即时发送，需要改为 5 秒倒计时按钮，并在 DeleteCharacter 包上加 CheckSum 字段（如果协议允许）。注意：旧 C# 这部分**不是 EI 反编证据**，仅是 source-confirmed，标注为 source-confirmed，不作为 EI 铁律但作为保守实现（同系客户端行为）。 |
| 成功回包 | 未在 EI 0x458F80 表中直接看到删除回包 case（说明 EI 可能走另一消息链） | 删除成功 → 刷新列表 | OnDeleteCharacterResult Success → RemoveAll + RefreshList |  |

### S8 开始游戏/等待（phase 3 + F55→0x67→0x20D）

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 触发 | F55 点击 (0x459B16)，选槽 +0x1168 ∈ 0..1 → 发 msgid **0x67 '%s/%s'**（account/charname），**不切 phase**（phase 4 由 server case 0x20D 写入） | StartButton → `StartGame.dat` → cnsPlay/loading → 收到公告时确认 | OnStartPressed 发送 StartGame；成功回包后播放 StartGame.ogv，播完黑屏显示空内容 F602；勾选后创建 GameScene | EI PE 闭合了 0x20D→StartGame.dat→mode 3；参考 Pascal 源还记录了 cnsPlay/loading→SM_SENDNOTICE→DMessageDlg→CM_LOGINNOTICEOK。F602 与该登录公告 UI 的资源等价尚未闭合；此处按用户实机目标作为公告占位。 |
| 等待 phase 3 | server case 0x209 或 F89 确认写 phase=3；Delayed 结果重试循环 | StartGameResult.Delayed 3s 重试 | StartGameResult.Delayed → SetSelectPhase(3) + Timer 3s 重试 | 已实现 phase 3。 |
| 错误 | 0x20A/0x20C/0x20E 弹框 | MessageBox | 文字显示错误 | 基本可接受。 |

### S9 StartGame 过场进游戏（phase 4）

| 维度 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 触发 | server case 0x20D → 0x459465：[+0x930]=4，[+0x1160]=0，播 StartGame.wav，加载 StartGame.dat 并播放；结束 → 0x4570A0 (mode=3 进游戏) | StartGame.dat 播放结束后进入 cnsPlay/loading；服务端公告由 ClientGetSendNotice 模态显示，点 OK 发 CM_LOGINNOTICEOK | OnStartGameResult Success → phase 4 / StartGame.ogv；视频完整结束后黑屏显示 F602，点勾才创建 GameScene | 视频结束后保留 SelectScene 等用户确认；F602 是当前占位框，原版是否使用同一资源需继续核对。 |
| 视频 | 640×480, 1.37s, 41 帧；StartGame.dat 数据自带淡入黑尾帧 | Video.Play(StartGame.dat)，完成后继续 cnsPlay/loading | ogv 1.37s, 640×480，结束后显示公告框 | 视频结束后黑底保留到公告确认。 |

### S10 错误/取消/返回路径

| 路径 | EI | 旧 C# | Godot | 差异 |
|---|---|---|---|---|
| 取消建角 | phase 2 下走 F57/F89 等候选路径返回 phase 0 | CharacterBox.Visible=false; SelectScene.Visible=true | HideCreateCharacterPanel → SetSelectPhase(0) | 已存在。 |
| 取消开始 | 登录公告模态只接受 OK 后发 `CM_LOGINNOTICEOK` | 关闭登录公告窗口 | F602 的 X 会关闭后重新显示；勾选触发进入游戏 | 确认前维持黑底，避免服务端已返回成功后退回选角态。 |
| 断线/错误 | 0x20A/0x20C/0x20E/DisconnectedEvent | MessageBox | OnDisconnected/错误文字 | 基本路径有，但需要验证按钮状态在断线后正确复位。 |

## Godot 代码中已确认 bug 清单（实施 TODO）

1. **[已修复，待实机复验] SelectScene.cs StartGame 过场生命周期**：StartGame 视频现挂到 Root，播放完成后显示黑屏 F602 公告确认框；点勾才创建 GameScene。
2. **[高] SelectScene.cs L893-906**：Create 按钮点击同时启动 CreateChr 视频和 ShowCreateCharacterPanel，二者叠加。应只在视频 Finished 回调里切到 phase 2 并 ShowCreateCharacterPanel。
3. **[高] SelectScene.cs L1303-1321**：OnDeletePressed 使用通用 ConfirmationDialog 即时发包。需要改为带 5 秒倒计时按钮的 Legacy 对话框，Yes 默认禁用，5 秒后启用；发送时携带 CheckSum（若 Zircon C.DeleteCharacter 支持）。
4. **[中] SelectScene.cs L886-888**：三个主按钮用 Index=-1 文本按钮 + 错误坐标 (25/120/215, 382)，但 legacy skin 按钮在 BuildLegacySelectUi 末尾"先摘到 _uiLayer 再设坐标"（L1008-1017），实际位置被改写到 (79,243) 等错误值（代码注释 L576-577 把 p0-2 标成"删除角色"但坐标 (79,243) 对应旧版注释），且使用的是 Interface 库帧而非 Interface1c F51/F53/F55。需要按 primary-static 证据改：LibraryFile.Interface1c, Index 51/53/55，坐标 (440,93)/(?/243 待核)/(259,49)。F53 精确坐标需从 setrect_calls 或 window-control-position-analysis 中提取。
5. **[中] SelectScene.cs L1200**：创建成功回包 SetSelectPhase(2)；应为 SetSelectPhase(0)（回到 4 按钮列表）。
6. **[中] SelectScene.cs L767-773**：SubmitSkinCharacter 发送 SendNewCharacter 后未切到 phase 3（等待态），按钮状态应相应更新。
7. **[低] RefreshList L338**：无条件 SelectSkinCharacter(0) 自动选首项。原版 [0x1168]=-1（无选中），建议改成不自动选，只有用户点击才选中；避免自动进游戏路径出问题。但自动登录依赖 RefreshList→AutoStartGame，需保留 AutoLogin 路径的特判。
8. **[低] LoginScene wemade/ei_Login 视频当前位置/尺寸/VolumeDb**；需要在 LoginScene 中核实。

## 未闭合 / blocked 项

- F53 删除按钮 handler 0x459?? 未在现有 primary 证据中直接给出；坐标需从 setrect 提取。
- F92/F95/F98/F86 精确业务语义（scroll/page/delete?）是 candidate，暂不重映射到 Zircon 业务。
- EI phase 2 的"密码编辑"与 Zircon 的 NewCharacterDialog 不对等；保留 Zircon 风格创建面板，标注为 source-confirmed 而非 EI parity。
- 研究版 EXE (NAS/TMP) 与本机 mir2ei/Mir3.exe SHA 不同，部分 VA 在本机 EXE 上未闭合；F50/F51/F53/F55/按钮帧头与资源视觉 inspection 仍可在本机 Interface1c.wil 上复核，不受 EXE 身份差异影响。
- CreateChr.dat/StartGame.dat 不含音轨，.wav 独立播放已接线但 ogv 转换时未混流，当前 Godot 代码单独播 SoundIndex.LegacyCreateChr/LegacyStartGame，时序与 EI 一致（音效与视频同时启动）。

## 证据来源

- `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/login-flow-evidence.json`（primary-static，F311/F336/F349/F541/F603 汇总）
- `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/login-charselect-flow-evidence.json`（F349）
- `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/interface1c-parent-context.json`（F50/F51/F53/F55/F57 帧头与视觉标签）
- `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/interface1c-select-screen-context.json`（登录页按钮 F11/F13/F15/F17）
- `/home/tetsuya/development/Mir3-Research/docs/research/mir3-map-reconstruction/char-select-stage-machine-evidence.json`（F541 5-stage jt）
- `/home/tetsuya/development/Mir3-Research/docs/research/mir3-map-reconstruction/char-select-enter-layout-evidence.json`（F603 slot/entity/form）
- `Client/Scenes/SelectScene.cs`（source-confirmed）
- `GodotClient/Scripts/SelectScene.cs`、`LoginScene.cs`（当前实现）
- ffprobe 实测 4 个 dat/ogv 文件

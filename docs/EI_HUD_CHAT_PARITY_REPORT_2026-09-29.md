# EI HUD 聊天与详细聊天窗（F350）审计与验收报告

- Goal：`docs/GOAL_OPENCODE_EI_HUD_CHAT_PARITY.md`
- 日期：2026-09-29
- 分支：`origin/master`，起始 HEAD = `c00b34b0`
- 客户端：`GodotClient`（`--legacy-ui --legacy-hud` 为 EI legacy 模式）
- 服务端：**隔离副本** `127.0.0.1:7001`（见 §9）
- 结论一句话：**范围内两块聊天体验的可测项已全部走完真实客户端交互验收；
  三项有 EI 直接证据的缺陷（A1/A2/B1）已修复并验证；私聊服务端交付、
  中文输入、消息过滤面板、长行/上限等 4 类项未测或阻塞，均已如实列出。**

---

## 1. A/B 功能图

```
EI legacy（AutoLoginArgs.LegacyUi=true 且 LegacyHud=true）
│
├── 范围 A：常驻 HUD 聊天（F50 聊天槽 + 其下方独立输入条）
│   ├── ChatLogPanel        记录槽  视口 (224,491) 文本区 354×74（5 行 × 14px）
│   │                        右侧链条槽 x=356 —— evidence-only，鼠标不可拖
│   │                        空闲 >10s 且 Transparent+FadeOut 时整体 opacity=0（见 §11 unresolved）
│   ├── ChatTextBox         输入条  视口 (223,569) 354×16
│   │                        独立 LineEdit、独立历史 ↑/↓、独立草稿
│   └── 发送链路            ChatTextBox.Submit → GameScene.SendChat → C.Chat
│                            → 服务端 → S.Chat → GameScene.ReceiveChat
│
└── 范围 B：详细聊天窗 = id8 / GameInter **F350** → `LegacyChatDialog`
    ├── 打开/关闭           MainPanel.MailButton「聊天记录(Ctrl+R, R)」
    │                        裸 `R`、`Ctrl+R`（仅当无输入框焦点时）
    ├── 历史裁剪区          `_historyClip` 视口 (149,134) 485×266 = 19 行 × 14px
    ├── 输入框              `_input` 视口 (139,417) 499×15（与 A 的输入条**不是**同一个控件）
    ├── 6 个频道模板按钮    视口 (139+40i, 438)
    ├── 关闭按钮            视口 (646,456) 28×26
    └── 滚动                `_scrollUp`/`_scrollDown` 步进 19px + 鼠标滚轮
                             链条轨道 `ChatScrollRail` MouseFilter=Ignore —— 不可拖
```

**A/B 关系（实测）**

| 维度 | 结论 | 证据 |
|---|---|---|
| 消息来源 | **同一条** `GameScene.ReceiveChat` 同时驱动两个视图 | 日志 `[LegacyChat] receive … hudMessages=N … f350Visible=B` 每条消息同时打印两者 |
| 消息内容/过滤 | 同步（同一消息流，两个视图各自渲染） | HUD 与 F350 同批显示 `TestHero: EQAx…` |
| 输入框 | **各自独立**（`ChatTextBox._input` vs `LegacyChatDialog._input`） | 在 A 预填不改变 B 的输入，反之亦然（01/02 截图） |
| 焦点 | **各自独立**；B 打开会 GrabFocus 自己的输入，A 的输入焦点不受影响 | 见 §7 #5/#6/#7 |
| 打开 B 是否强制打开 | A1 修复后：**点击行只预填，不开窗** | `f350Visible=False` |

---

## 2. 术语（避免混淆）

| 本报告术语 | 含义 | 不是什么 |
|---|---|---|
| **F350 / 详细聊天窗** | EI `id 8`，`GameInter` 帧 750 系列窗口，Zircon 实现 `LegacyChatDialog` | 不是好友/邮件 `CommunicationDialog`，不是现代 `ChatOptionsDialog` |
| **HUD 聊天槽** | EI `F50` 聊天记录区 + 其下独立输入条 | 不是现代 `ChatTab`/`ChatLog` |
| **点击发送者预填私聊** | 点消息行 → 写入 `/名字 ` 到共享样式的编辑框 | **不是** `@mention`、**不是** autocomplete，无候选列表、无补全、不发包 |
| **频道模板按钮** | 6 个按钮把 `@拒绝 `/`!`/`!!`/`!~`/`@拒绝私聊`/`@拒绝行会聊天` **插入文本框** | 不发送（实测 26→26 次 `入队: Chat` 不变） |
| **evidence-only 链条轨道** | F350/历史槽右侧的视觉链条 | 不是可拖动滚动条 |

---

## 3. EI 原始证据路径与结论

| 证据 | 路径 | 用在哪 |
|---|---|---|
| `input.line_recall`（**primary-static**） | `Mir3-Research/docs/research/ei-ui-layout/chat-window-unified-model.json`：「PtInRect on line area, walk list, strip `/` or `(!)`, tokenize, `sprintf "/%s "`, write edit」 | A2 行点击预填格式 |
| 输入解析分隔符 | 同目录 `chat-window-render-evidence.json` → `input_parser`（分隔 `0x20` / `0x3A`） | `ChatLineRecall.ExtractSenderName` |
| 鼠标分发 | `chat-window-mouse-dispatch.json` | F350 行点击命中区、裁剪区滚轮 |
| 滚动条证据 | `chat-scrollbar-verification-evidence.json` | 判定链条轨道为 **evidence-only / 不可拖** |
| 键盘命令分发 | `chat-input-command-dispatch-evidence.json` | R / Ctrl+R 与输入焦点的优先级 |
| 运行时验收基线 | `chat-runtime-acceptance-2026-09-24.json`、`RESEARCH_LOG.md` Round 798/799 | 19 行 × 14px 几何 |
| 交叉印证（**secondary-source**，不替代 primary） | `reference/mir3-source/Source/Client/FState.pas` → `DBottomMouseDown` / `ExtractUserName`；`Source/Common/HUtil32.pas:743` → `GetValidStr3` | 服务端回显 `Name=> text` 的 `=` 分隔 |

> 研究仓库只读，未改动。旧 `Client/` 源码只作辅助参考，未用于取代 EI 证据。
> 资源身份见 §9；**未声称 EI 原版像素级一致**（`.wil`/`.Zl` 编码不同，见 §9 哈希）。

---

## 4. 代码入口

| 功能 | 文件:行 |
|---|---|
| 发送者名解析 + `/名字 ` 格式 | `GodotClient/Controls/ChatLineRecall.cs:26,:62` |
| HUD 行点击 → 预填 | `GodotClient/Controls/ChatLogPanel.cs:608-624`（`AttachPlayerNameAction`） |
| `StartPrivateMessage` 不再强制开窗 | `GodotClient/Scripts/GameScene.cs:384-391` |
| HUD 输入 `StartPM`（显示 + 焦点 + 尾随空格） | `GodotClient/Controls/ChatTextBox.cs:123-133` |
| F350 行点击 → 预填（**A2**） | `GodotClient/Controls/LegacyChatDialog.cs:84`（`_historyClip.MouseClick`）+ `:246,:249`（`OnHistoryClick`） |
| 焦点守卫（**B1**） | `GodotClient/Scripts/GameScene.cs:10865-10876`：`GuiGetFocusOwner() is LineEdit or TextEdit → return`，**排在 R 分支之前** |
| 裸 R / Ctrl+R 开关 F350 | `GodotClient/Scripts/GameScene.cs:10880-10895` |
| MailButton「聊天记录(Ctrl+R, R)」 | `GodotClient/Scripts/GameScene.cs:4755-4769`；文案 `GodotClient/Controls/MainPanel.cs:329` |
| F350 几何/滚动/模板/关闭 | `GodotClient/Controls/LegacyChatDialog.cs`：`OnHistoryWheel`、`ScrollBy`、`InsertChannelTemplate`、`CloseChat` |
| HUD 记录槽布局与淡出 | `GodotClient/Scripts/LegacyHudLayout.cs`；`ChatLogPanel.cs:145-165`（`faded`）、`:28`（`LegacyHudChatOpacity`） |
| ESC 关顶层窗 | `GodotClient/Scripts/GameScene.cs:11004`（`WindowManager.CloseTop`） |

---

## 5. 本次变更

| # | SHA | 内容 | 级别 |
|---|---|---|---|
| 1 | `c00b34b0` | **A1** 点击 HUD 发送者 → `/名字 ` 预填到 HUD 输入条、不再强制开 F350（新增 `ChatLineRecall.cs`、改 `ChatLogPanel`/`GameScene`/`ChatTextBox`，删除 `LegacyChatDialog.StartPrivateMessage` 死代码）；**A2** F350 历史行点击预填到 F350 输入；**B1** `_Input` 焦点守卫提到 R 分支之前 | 修复 |

> **提交纪律偏差（如实记录）**：goal 要求「每个独立改动单独 commit」，
> 但 A1/A2/B1 三个改动已由 Hermes 按用户明确指示合并为 `c00b34b0` 单提交推送。
> 本轮未拆分、未改写该提交（用户明确禁止），故此处只做记录，不做事后重写。
> 本轮**没有产生任何新的源码改动**，只新增本报告与截图。

---

## 6. 交互验收矩阵

判定含义：`verified`＝真实客户端跑通并有独立证据；`not tested`＝未跑；`blocked`＝条件不足；`unresolved`＝观察到但无 EI 证据裁决。

### 范围 A（常驻 HUD 聊天）

| # | 项目 | 结果 | 证据 |
|---|---|---|---|
| A1 | 输入条点击聚焦 → 输入普通文本 → Enter 提交 → 清空 → 服务端回显 | **verified** | `acc1.log`：`[Net] 入队: Chat` + `[LegacyChat] receive type=Normal textLength=19`，输入条清空、HUD 显示；截图 `01/02` |
| A2 | 5 行上限 + 最新消息锚点 | **verified** | 日志 `hudLines=5 hudSize=(372,74) hudTextArea=(354,74)`；新消息到达后视图回到最新 |
| A3 | 输入历史 ↑ 召回 / ↓ 恢复草稿 | **verified** | acc1 T2：↑＝`EQA1hello`，↓＝`DRAFTxyz` |
| A4 | 输入聚焦时裸 `R`、`Ctrl+R` 被输入框消费，**不开** F350 | **verified** | acc1 T3：`DRAFTxyzr`、`DRAFTxyzrr`，`ui_window_rects.json` 无 `LegacyChatDialog` |
| A5 | 无焦点时裸 `R` 开 / `R` 关 / `Ctrl+R` 开 | **verified** | 关闭X像素度量 `closeX_green` 0↔68；日志 `f350Visible=True/False` |
| A6 | HUD 行发送者点击 → 预填 `/名字 `、**不**开窗、不发包 | **verified** | 输入条亮像素 1496→**94**，与手输 `/TestHero ` 参考值 **94** 完全一致；随后发消息日志 `f350Visible=False`；`入队: Chat` 31→31（预填本身未发送），提交后 31→32 |
| A7 | 频道模板 6 按钮**仅插入**、不提交 | **verified** | 逐按钮插入后输入框亮像素 70/12/16/13/118/123；全过程 `入队: Chat` **26→26** |
| A8 | 中英文玩家名解析不限 ASCII | **verified（静态）** | `ChatLineRecall` 逐字符解析，无 ASCII 过滤（`ChatLineRecall.cs:45-57`）；运行时只用 `TestHero` 实测 |
| A9 | 中文（IME）输入 | **not tested** | 本轮未开输入法 |
| A10 | 消息类型过滤/颜色面板（`ChatOptionsDialog`） | **not tested** | 只读审查，未进面板点选 |
| A11 | 长行换行/裁剪、消息条数上限 | **not tested** | `hudMessages` 增长到 32 未截断，但未构造 >上限 与超长行 |

### 范围 B（F350 详细聊天窗）

| # | 项目 | 结果 | 证据 |
|---|---|---|---|
| B1 | MailButton「聊天记录(Ctrl+R, R)」开关 | **verified** | `closeX_green` 68 → 点击 → 0 → 再点 → 68（`mb_before/after1/after2.png`） |
| B2 | 关闭按钮关窗 | **verified** | 点击后 `closeX_green=0`，rects 仅剩 HUD 四窗 |
| B3 | `Ctrl+R` 开、裸 `R` 关（需先释放输入焦点） | **verified** | acc3：`Ctrl+R` → 68；`Return`+`R` → 0 |
| B4 | **输入焦点时不被 R 关闭**（B 范围硬性要求） | **verified** | acc2：F350 输入聚焦时 `r` 被输入框吃掉，需 `Return` 释放后 `R` 才关窗 |
| B5 | 19 行 × 14px 历史裁剪区、输入栏、关闭按钮、6 模板按钮命中 | **verified** | `ui_window_rects.json`：`LegacyChatDialog [114,106,572,388]`；子控件坐标与代码一致；6 个绿色模板图标聚类 x 中心 155/196/235/276/315/355（步进 40） |
| B6 | ↑/↓ 按钮滚动、滚轮滚动 | **verified** | 行带签名：↑×2 变化（meandiff 5.05）→ ↓×2 回到底部（diff 0）；滚轮 ↑/↓ 对称（uC/uD/uE/uF） |
| B7 | 顶部/底部边界钳制 | **verified** | 第 3 次 ↑、3 次滚轮 ↑ 后签名与顶部签名 **完全相同（diff 0）** |
| B8 | evidence-only 链条轨道**不可拖** | **verified** | 点击轨道中部 → 签名 diff **0**（未滚动） |
| B9 | 向上翻阅时新消息到达 → 视图钉住不跳 | **verified** | 顶部位置发消息 → 签名 diff **0**；之后滚轮 ↓ 才变化 |
| B10 | 关闭/重开历史保留 | **verified** | open#1 与 reopen 的裁剪区 **逐像素 identical（maxdiff 0, meandiff 0）** |
| B11 | F350 行发送者点击预填（**A2 修复项**） | **verified** | 清空后 8 亮像素 → 点击底行 → **126** 亮像素 |
| B12 | 长行在 19 行裁剪区内的行为 | **not tested** | 未构造超长行 |
| B13 | 窗口关闭后键盘焦点归还 | **verified（间接）** | 关窗后 `Ctrl+F12` 可用 → 焦点为 null；ESC 关窗亦可 |

### 焦点/热键守卫（跨 A/B）

| # | 项目 | 结果 | 证据 |
|---|---|---|---|
| G1 | **非聊天** LineEdit 聚焦时 `R` 不切换 F350（**B1 修复项**） | **verified** | `FilterDropDialog` 过滤框：`Ctrl+F12` 被拦（焦点=LineEdit）→ 敲 `R` → `closeX_green=0`（F350 未开）→ `Ctrl+R` → 仍 0 |
| G2 | 同上，焦点确认 | **verified** | 打开前后 `Ctrl+F12` 由可用 → BLOCKED，证明焦点确在 LineEdit 上 |
| G3 | ESC 关顶层窗 | **verified** | 连按 5 次 ESC 逐个关闭 FilterDrop/Group/Config/Character，回到仅 HUD |

### 视口 / 回归 / 构建

| # | 项目 | 结果 | 证据 |
|---|---|---|---|
| R1 | 800×600 游戏视口截图 | **verified** | 全部 legacy 截图（窗口标题 `ZirconClient - 800x600`） |
| R2 | `1024×768` 窗口/视口适配 | **verified** | legacy 会**强制回 800×600**：`[Display] --window 初始尺寸: 1024x768` → `[Display] Legacy window: 800x600 logical`；非 legacy 视口 1024×768（`MiniMapDialog [822,0,200,200]`，右边界 1022）→ 截图 `07` |
| R3 | 非 legacy（`--zircon-ui`）无回归 | **verified** | Enter 聚焦 `ChatTextBox [111,576,400,25]` → 输入 → `入队: Chat`；裸 `R` 打开 `RankingDialog`（现代语义）；rects **无** `LegacyChatDialog`；日志 0 exception |
| R4 | `dotnet build GodotClient/ZirconClient.csproj --no-incremental` | **verified** | **0 错误 / 3 警告**（`CS8632` TableSnapshotTool.cs:128、`CS0219` GameScene.cs:6603/6689，均为既有） |
| R5 | `git diff --check` | **verified** | 无输出 |
| R6 | 控制台错误检查 | **verified** | `acc2/acc3/regress` 三份日志 `exception|stacktrace` 计数均为 0 |
| R7 | 工作区审阅 | **verified** | 仅新增 `docs/EI_HUD_CHAT_PARITY_REPORT_2026-09-29.md` 与 `.artifacts/legacy-hud-chat-parity-2026-09-29/` |

---

## 7. 截图与日志索引

截图目录：`.artifacts/legacy-hud-chat-parity-2026-09-29/`（脱敏游戏 UI，无账号/密码/私人内容）

| 文件 | 内容 | 支撑项 |
|---|---|---|
| `01-hud-sender-name-prefill.png` | 点击 HUD 聊天行后，HUD 输入条出现 `/TestHero `，画面**无** F350 | A6 |
| `02-hud-input-prefill-reference.png` | 手输 `/TestHero ` 的参考图（与 01 亮像素同值 94） | A6 交叉验证 |
| `03-f350-open-with-history.png` | F350 打开（`closeX_green=68`、`f350Visible=True`、裁剪区亮像素 37524） | B1/B5 |
| `04-f350-scroll-to-top.png` | 滚动到顶部状态（签名与底部不同） | B6/B7 |
| `05-guard-filter-lineedit-r.png` | 非聊天输入框聚焦时敲 `R`，F350 未开（`closeX_green=0`） | G1 |
| `06-mailbutton-toggle-close.png` | MailButton 点击后 F350 关闭（`closeX_green=0`） | B1 |
| `07-nonlegacy-1024x768.png` | `--zircon-ui` 1024×768 视口 | R2/R3 |

日志（临时目录，不入库；关键行已摘录）：

| 文件 | 说明 |
|---|---|
| `/tmp/ei-flow/acc2.log` | 范围 A/B 主验收（32 条消息、滚动、模板、MailButton、A1） |
| `/tmp/ei-flow/acc3.log` | 焦点守卫（B1）与 R/Ctrl+R 开关 |
| `/tmp/ei-flow/acc4.log` | F350 打开 + 历史（截图 03） |
| `/tmp/ei-flow/regress.log` | 非 legacy 回归 |

摘录（只含长度/类型，不含正文之外的隐私；正文为构造的 `EQAx` 测试串）：

```
[LegacyChat] receive type=Normal textLength=19 hudVisible=True hudMessages=22
              hudTypeEnabled=True hudLines=5 hudSize=(372, 74)
              hudTextArea=(354, 74) f350Visible=True
[LegacyChat] receive type=Normal textLength=16 … f350Visible=False
[LegacyHud]  PASS viewport=(800, 600) location=(0, 464) …
[Display]    --window 初始尺寸: 1024x768
[Display]    Legacy window: 800x600 logical (x1 → 800x600 px)
```

判定工具（可复现）：`xdotool` 注入 + `scrot` 截图 + numpy 像素度量，
关键度量为 F350 关闭 X 图标绿色像素数（68=开 / 0=关）与历史裁剪区行带签名。

---

## 8. 资源身份

| 项 | 值 |
|---|---|
| `Debug/Client` | 软链 → `/home/tetsuya/mir2ei` |
| `Debug/Client/Data/GameInter.Zl` | 34114431 B，sha256 `32157af8…` |
| `LegacyEI/Data/GameInter.wil`（2002-10-25） | sha256 `7d42778e925e82f7c44d2c5f5c46643f3e898a97ed8cbef93df669865ebd87c7` |
| `LegacyEI/Data/GameInter.wix` | sha256 `c4c3cfd84fcb0dd90f5fccc6ecb48e52172ac21e945106f6fc688e7e8b2c0396` |
| ClientData 根 | `/home/tetsuya/development/zircon/ClientData` |
| 字体 | `GodotClient/Fonts/third_party/fusion-pixel/fusion-pixel-12px-proportional-zh_hans.otf` |
| 显示 | Xvfb `:100` 1024×768 + openbox |

> `.Zl` 与 `.wil` 是不同编码的两套资源，本报告**不主张**与 EI 原版像素级一致；
> 行为结论只在「同一份 `GameInter` 语义帧 + 上述 primary-static 证据」范围内成立。

---

## 9. 服务器与数据库隔离边界

| 项 | 值 |
|---|---|
| 隔离服务端 | `/tmp/ei-flow-srv`，`IPAddress=127.0.0.1 Port=7001`，`SinglePlayerDev`，启动命令 `cd /tmp/ei-flow-srv && dotnet ServerCore.dll` |
| 敏感配置 | `Server.ini` 内 `MasterPassword`/`SyncKey` 已是 `REDACTED`；本报告不含任何凭据 |
| 凭据使用 | 客户端 `--user test@test.com --char TestHero`，**省略 `--pass`**（`AutoLoginArgs.Password` 默认值，避免落盘） |
| 仓库库未被写 | `Debug/ServerCore/Database/Users.db` md5 前后均为 `138ac3549426fae0682a93af0afbed2d` |
| 隔离库 | `/tmp/ei-flow-srv/Database/Users.db` md5 `e6aaf0086a8fa0c0de56a5d60ef2deef` |
| Map 来源 | 隔离服务端 `Map/` 软链到仓库 `Debug/ServerCore/Map`（只读使用） |
| 禁止项 | 全程**未发送** `@`、`!`、`!!`、`!~` 命令；6 个模板按钮只测本地插入；发送用的都是 `EQAx…` 唯一普通文本 |
| 未触碰 | 7000 端口、生产库、`Mir3-Research` 工作树 |

---

## 10. 结论清单

### verified（真实交互跑通）

A1 输入提交与回显、A2 5 行上限与最新锚点、A3 历史上/下与草稿、A4 输入中 R/Ctrl+R 不误触发、
A5 无焦点 R 开关、A6 HUD 行点击预填不发包不改窗、A7 六模板按钮仅插入、A8 名字解析不限 ASCII、
B1 MailButton 开关、B2 关闭按钮、B3 Ctrl+R/R 开关、B4 输入焦点时不被 R 关闭、B5 几何与命中、
B6 按钮与滚轮滚动、B7 顶/底钳制、B8 链条轨道不可拖、B9 上翻时新消息锚定、B10 关/开历史保留、
B11 F350 行点击预填、B13 关窗后焦点归还、G1/G2 非聊天 LineEdit 守卫、G3 ESC 关窗、
R1 800×600 截图、R2 1024×768 适配、R3 非 legacy 回归、R4–R7 构建/diff/错误/工作区

### not tested

- A9 中文（IME）输入
- A10 消息类型过滤面板逐项点选（`ChatOptionsDialog`）
- A11 消息条数上限与超长行换行/裁剪
- B12 长行在 F350 裁剪区内的溢出表现

### blocked

- **私聊服务端交付（发包 + 回包）**：需要第二个隔离测试角色或第二客户端；本轮只验证「点击名字 → 预填 `/名字 `」，
  **未验证服务端是否投递**。已按 goal 要求降级记录，不宣称私聊闭环。

### unresolved

- **HUD 聊天空闲 >10s 整体隐藏**：`ChatLogPanel._Process` 计算
  `faded = FadeOut && Transparent && _idleSeconds > 10`，legacy 下 `opacity = 0`（非 legacy 为 0.15）。
  实测：新消息后亮像素 1631，空闲后 0，再发消息立即恢复。
  本轮**未改**——没有 EI 证据可裁决该行为是否为原版语义，故只记录为观察结果。
- HUD 行在淡出（opacity=0）时仍可点击触发预填（视觉不可见但可命中）——同源于上一条，未改。
- **提交粒度偏差**：A1/A2/B1 合并在 `c00b34b0` 一个提交（见 §5 注）。

---

## 11. 变更与 SHA

| 顺序 | SHA | 说明 | 远端核对 |
|---|---|---|---|
| 1 | `c00b34b0` | A1+A2+B1 聊天修复（Hermes 按用户指示提交，本轮未改写） | ✅ |
| 2 | `d616a618` | `docs: EI HUD 聊天与 F350 详细聊天窗审计验收报告与截图` | ✅ 回读 `refs/heads/master` = `d616a6183f6f6a8b7e055b1e627a2960a35ffbb6` |
| 3 | （本行所在提交） | `docs: 报告补记远端 SHA` | ✅ 最终 HEAD 以 `git ls-remote origin refs/heads/master` 回读为准 |

> 提交 1、2 的 SHA 已回读远端核实。提交 3 自身的 SHA 无法在文件内自证，
> 推送后已再次回读 `refs/heads/master` 核对（见 Goal 完成记录）。

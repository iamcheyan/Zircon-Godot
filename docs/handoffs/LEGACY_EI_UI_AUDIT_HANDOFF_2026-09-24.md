# Zircon 旧版 EI UI 审计接手说明

更新：2026-09-24
主目标：以目标 EI 旧版客户端为基准，逐个界面比对并修正 Godot 客户端；每个修改都按验收标准核对，不以编译通过代替行为/画面验收。

## 目标与验收要求

接手后继续按 [`../LEGACY_EI_UI_AUDIT_2026-09-23.md`](../LEGACY_EI_UI_AUDIT_2026-09-23.md) 开头写明的“完全一致”标准工作：

- 先从 EI 反编译代码和素材确认每个窗口实际包含的文本、按钮、状态、输入区域、绘制次序、坐标/裁切及滚动行为。
- 对照审计文档与 `GodotClient` 实现，记录每一处差异及证据等级；改完后复核同一窗口的画面与交互。
- 只有对应状态、控件、文字、命中矩形、键鼠动作和关闭/重开等可观察结果均有 EI 对照证据时，才标记通过。无证据或无法运行验证的部分记为阻塞/待验证，不猜测补齐。
- 继续边审计边修正，但每次提交要保持可独立检查；推送后让另一台机器按相同 Git commit 验证。

本轮用户临时提高聊天窗口优先级。聊天窗口达到可用/验收前，先完成下节，不需要回到上一轮的行会或其它窗口运行测试。

## 当前最高优先级：EI 聊天窗 id 8 / F350

### 原版证据

主要静态依据在 `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/`：

- `chat-window-unified-model.json`
- `chat-window-render-evidence.json`
- `chat-window-mouse-dispatch.json`
- `chat-scrollbar-verification-evidence.json`
- `chat-input-command-dispatch-evidence.json`

当前已记录的 EI id 8 几何：根 572×388、GameInter F350；历史区 `(40,29,491,279)`，19 行、14px 行距；输入区 `(25,311,499,15)`；底部六个 36×34 按钮位于 x=25/65/105/145/185/225、y=332；关闭按钮 `(532,350,28,26)`；上下滚动控件在 `(539,25)`、`(539,311)`，另有 F380 轨道/滚动量控件。六个按钮写入 `@拒绝 `、`!`、`!!`、`!~`、`@拒绝私聊`、`@拒绝行会聊天` 模板，不是现代七态聊天频道选择器。

该研究目录记录来自 primary-static disassembly/resource inspection；研究版 EI EXE 与本机运行资源根 EXE 身份尚未证明相同，不能把未核验的版本行为升级为目标客户端定论。勿把可用的较新 `Client/` 源码当成目标 EI 证据。

### 本轮代码状态

- 新增 `GodotClient/Controls/LegacyChatDialog.cs`：独立 F350 窗口、19 行消息历史、输入框、六个模板按钮、滚轮/上下按钮/轨道拖动、关闭及焦点释放。
- `GameScene.ReceiveChat()` 和 `OnChat()` 将入站消息送到常驻记录与新窗口；发送继续走 `SendChat()` / `C.Chat`，链接物品编号也转发。
- legacy 的裸 R 切换新窗；Enter/空格/斜线可打开并聚焦；`--legacy-open=chat` 走 `OpenChat()` 并打印 `inputFocus`；legacy 模式隐藏现代独立聊天输入栏。
- `docs/LEGACY_EI_UI_AUDIT_2026-09-23.md` 的 CHAT-02/04 与本轮记录已更新。
- 当前项目构建检查曾成功：`dotnet build GodotClient/ZirconClient.csproj --no-incremental`，0 错误、3 个既有警告。之后又补了启动直开焦点日志并重新构建，亦成功。

### 尚未通过的运行验收（接手后先做）

用户反馈当前 F350 里没有消息/输入，打不了字，底部图标也看不到。用户截图只有 618×184，EI 输入和按钮 y 坐标在该裁图下方；这可能解释图标不在截图内，但不能解释无法输入，不能据此结案。

1. 在本机 1024×768 的完整游戏窗口打开 `--legacy-open=chat`，查看 stdout `[LegacyOpen] ... inputFocus=...`；随后确认点击输入行、回车/空格打开后能输入字符，Enter 清空并发送一条无命令语义的普通本地聊天文本。不要试发六个拒绝/喊话模板。
2. 以完整 viewport 截图确认 F350 根位置、输入行、六个按钮和关闭图标；检查按钮纹理是否实际可取到、frame bounds 是否自然尺寸过大/为零、窗口 clip 是否误裁。
3. 滚动区域只用本地伪造/已有历史或安全系统消息验收上下按钮、wheel、轨道拖动与新消息到达后的滚动锚点；不要用会触发服务端业务的命令填充内容。
4. 修正后更新 CHAT-02/04 的证据与验收记录。用户已明确要求不重复坐骑 S/Ctrl+S、不跑需要第二玩家的完整交易、不输入已知会触发 Bad Request 的运行测试；继续跳过并在阻塞清单记因。

EI 资源帧已导出的证据图可查 `docs/evidence/legacy-ei-ui/`。在文件/原始 `GameInter.wil/.wix` 身份与路径缺失时，必须注明实际用的是哪套资源。不要把普通 `GameInter.Zl` 的同号帧默认视为 EI WIL 帧。

## 仓库、分支和双机同步

### 已核对的 Git 状态

本机 Zircon：

- `/home/tetsuya/development/Zircon`
- `origin` 是 `https://github.com/iamcheyan/Zircon-Godot.git`
- 分支 `ui/legacy-layout-lab`，本轮开始时 HEAD=`7131594e`，已跟踪 `origin/ui/legacy-layout-lab`。
- 本轮聊天代码、审计修改、服务状态记录和用户参考图需随本轮提交推送。

本机 Mir3-Research：

- `/home/tetsuya/development/Mir3-Research`
- `origin` 是 `https://github.com/iamcheyan/Mir3-Research.git`
- 本轮开始时本地 `master`=`e3379fc`；GitHub `origin/master` 已到 `77b1cff`，有两条本机尚未包含的 watchdog 提交。本轮 6 个未提交修改是 EI notice/banner 审计补充、README 的 EI 资源根约定及 map-link 生成时间更新。提交前先 fetch 并把今日提交基于最新 `origin/master` 发布，避免推旧 master。

Debian（`ssh debian`）已存在的工作树：

- `/home/tetsuya/development/Zircon` 是指向小写 `zircon` 的符号链接；该 repo 的 `origin` 是 `git@github.com:iamcheyan/Zircon.git`，不是本机工作分支使用的 `Zircon-Godot`。HEAD=`fbf07e6d`，另有未跟踪 `docs/reviews/`。不要在此目录直接覆盖/切成本机工作树。
- `/home/tetsuya/development/Mir3-Research` 的 origin 与本机相同，HEAD=`77b1cffc`；有多项 tracked edits 与 untracked goal/evidence/log 文件。保留这些改动，不直接 reset、clean、stash 或把它们混进 EI UI 提交。
- Debian 有 .NET SDK 10.0.400、Godot Mono 4.6.3，以及 Zircon `Debug/Client` 下的 `.Zl` 游戏素材；检查未发现 `/home/tetsuya/mir3ei/LegacyEI/Data/GameInter.wil/.wix`。所以可用来开发/编译，也可能用当前客户端资源跑基本流程；复现 EI WIL 帧必须先显式配置/同步已授权的资源或在本机完成，不可默认为两台机器素材相同。

### 推荐接手与验证流程

为保护 Debian 当前有改动的两个 checkout，先在旁边新建正确来源的工作副本，不要复用其错误来源的 Zircon clone，也不要运行 `/home/tetsuya/scripts/all_sync.sh`（它会对整个目录执行 `git add -A`，并可能强推多个仓库）：

```bash
cd /home/tetsuya/development
git clone --branch ui/legacy-layout-lab https://github.com/iamcheyan/Zircon-Godot.git Zircon-Godot-EI
git clone --branch ei-ui-audit-2026-09-24 https://github.com/iamcheyan/Mir3-Research.git Mir3-Research-EI
```

在 Debian 对单个仓库开发后，检查 diff、编译/适用的静态核对，按中文提交信息提交并推该工作分支。不要在共享 clone 上自动同步其它仓库。

要在本机测试 Debian 写的 Zircon 代码：

```bash
cd /home/tetsuya/development/Zircon
git fetch origin
git pull --ff-only origin ui/legacy-layout-lab
git rev-parse HEAD
dotnet build GodotClient/ZirconClient.csproj --no-incremental
cd /home/tetsuya/mir3ei
./login_game.sh --legacy-ui --legacy-hud --legacy-open=chat
```

末条脚本会结束已有 Godot 客户端并启动新进程；执行前确认没有要保留的游戏会话。开发机和测试机以同一 Git commit SHA 为代码身份，本机运行仍使用本机 `/home/tetsuya/mir3ei` 的游戏运行资源；不要把机器不同的 `Debug/Client` 文件或未纳入版本控制的素材差异误认为代码差异。

## 继续推进顺序

1. 接收本次两个仓库的提交/分支 SHA；Debian 克隆上述 EI 分支，核验 `git status` 与 `git rev-parse HEAD`。
2. 先修聊天窗输入焦点/按钮可见性并完成完整 viewport 截图和普通聊天回显；不发六个可能有副作用的模板命令。
3. 更新 CHAT-02/04 与证据链接，按用户要求每个可交付改动及时 commit/push。
4. 再按审计表从尚未闭合的窗口逐项推进；静态源码、EI 原始素材、屏幕截图/命中测试分别记证据，不用代码结构不同作为停止比较的理由。

# Godot 客户端 UI 全面交互审计与修复（2026-10-02）

真机审计：Godot 4.6.3 mono + Xvfb `:100` + openbox，客户端连本地 `127.0.0.1:7000`
ServerCore，legacy EI 界面（`--window=800x600`，逻辑画布 = 窗口像素，scale=1）。
所有结论来自**真实运行窗口**的鼠标/键盘注入 + `Ctrl+F12` 的 `WindowManager`
可见窗口矩形导出（`/tmp/ui_window_rects.json`）+ 全帧像素比对，非静态代码推断。

## 0. 环境与工具

| 项 | 值 |
|---|---|
| 客户端 | `godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 --window=800x600 --user <测试账号> --char <测试角色>` |
| 界面模式 | legacy EI（默认；`ZIRCON_LEGACY_UI_DATA_PATH=$HOME/mir2ei/LegacyEI/Data`） |
| 状态探针 | `Ctrl+F12` → `/tmp/ui_window_rects.json`（`GameScene.DumpVisibleWindowRects`，逐窗口 `DXWindow.Windows` 可见项的名称 + 位置 + 尺寸） |
| 输入注入 | `xdotool`（XTEST；`--window` 的 XSendEvent 合成事件 Godot 不接收，已弃用） |
| 截图 | `scrot` 后按窗口原点裁 800×600 |
| 工具脚本 | 本目录 `harness.sh`（截图/点击/按键封装） |

`Ctrl+F12` 探针有一个**已知副作用**：当原生 `LineEdit` 持有焦点时，`GameScene._Input`
会整段早退（`GameScene.cs:10926` `if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;`），
因此探针无输出。这本身是下述 ISSUE-3 的成因之一，也用于判定「输入焦点是否在文本框」。

## 1. 已确认并已修复的问题

### ISSUE-1 设置窗（id12 / F750）无法用同一入口关闭 — 已修复

- **复现**：进游戏 → 按 `N` → 设置窗打开（探针出现 `ConfigDialog:[276,113,248,264]`）→ 再按 `N` → **仍为打开**；HUD cap11 按钮（665,16,40×38）同样只能开不能关。
- **预期**：原版是 toggle。证据：旧 `Client/Scenes/GameScene.cs:1348` `case KeyBindAction.ConfigWindow: ConfigBox.Visible = !ConfigBox.Visible;`；`Client/Scenes/Views/MenuDialog.cs:116` 同。
- **根因**：`GameScene.OpenConfigDialog()` 只调 `WindowManager.Open`（无 toggle 分支），而 `N` 键、HUD cap11、菜单「设置」三个入口全部只走该方法。
- **修复**：`GameScene.OpenConfigDialog` 改 `WindowManager.Toggle`。
- **验证**：`N` 开 → 探针有 `ConfigDialog`；`N` 再按 → 探针无 `ConfigDialog`。截图 `evidence/10-fix-n-config-open.png` / `evidence/11-fix-n-config-closed.png`。
- **提交**：见本目录 `REPORT.md` 末「提交记录」。

### ISSUE-2 行会窗（id4 / F600）无法用同一入口关闭 — 已修复

- **复现**：按 `F` → `GuildDialog:[102,22,596,446]` 打开 → 再按 `F` → **仍为打开**；HUD cap6 按钮（616,82,28×26）同样只开不关。
- **预期**：原版是 toggle。证据：旧 `Client/Scenes/GameScene.cs:1403` `case KeyBindAction.GuildWindow: GuildBox.Visible = !GuildBox.Visible;`；`MenuDialog.cs:138` 同。
- **根因**：同 ISSUE-1，`OpenGuildDialog()` 只 Open。**注意**该方法是**共享入口**：服务端行会邀请包 `OnGuildInvite`（`GameScene.cs:2879`）也调它，那条路径必须「确保可见」而不是切换。
- **修复**：`OpenGuildDialog` 改 `WindowManager.Toggle`；新增 `EnsureGuildDialogOpen()`（只 Open），`OnGuildInvite` 改调它。
- **验证**：`F` 开 → 有 `GuildDialog`；`F` 再按 → 无。截图 `evidence/12-fix-f-guild-open.png` / `evidence/13-fix-f-guild-closed.png`。
- **回归**：服务端邀请路径仍为 open-only（未被 toggle 影响）。

### ISSUE-3 聊天窗（F350）打开后 Esc 无法关闭，且**所有热键同时失效** — 已修复

- **复现**：按 `R` 打开聊天窗（或点 HUD cap9）→ 按 `Esc` → 窗口不关；此时按 `Q`/`N`/`F` 等**全部无反应**；再按 `R` 也无法关闭。只有点击窗口右下 ✕（532,350）能关。
- **预期**：原版聊天条按 Esc 关闭。证据：旧 `Client/Scenes/Views/ChatTextBox.cs:302-309` `case (char)Keys.Escape:` → `e.Handled = true; DXTextBox.ActiveTextBox = null; TextBox.TextBox.Text = string.Empty; LinkedItemIndexes.Clear(); ToggleVisibility(e, false);`（即清空文本 + 隐藏聊天条）。
- **根因**（两层，均为真实缺陷）：
  1. `LegacyChatDialog` 的输入框 `DXTextInput` 暴露了 `Canceled` 事件，但**此前无人订阅**（`FilterDropDialog.cs:82/180` 定义并触发，`LegacyChatDialog.cs` 无订阅）→ Esc 只被 `LineEdit` 吞掉，无任何行为。
  2. `GameScene._Input` 首行 `if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;`（`GameScene.cs:10926`）——输入框持有焦点时**整段**早退，连 `Esc → WindowManager.CloseTop()`（`GameScene.cs:11080`）都不执行，于是所有窗口热键（Q/W/E/R/N/F…）一并失效。
- **修复**：
  - `LegacyChatDialog` 订阅 `_input.Canceled += CancelChatInput`；`CancelChatInput()` 清空文本 + 清链接物品 + `CloseChat()`（走 `WindowManager`，保证 Z 序与 `OpenWindows` 同步，避免「不可见但仍在列表」的残留）。
  - 与 `FilterDropDialog` 的既有 `Canceled` 用法保持一致，未改动共享基类行为。
- **验证**：按 `R` → 探针 `<no-dump: LineEdit focused>`（输入框确实获得焦点）→ 按 `Esc` → 探针恢复正常输出且无聊天窗；随后 `Q`/`N`/`F` 热键全部恢复。截图 `evidence/14-fix-r-chat-open.png` / `evidence/15-fix-esc-chat-closed.png`。
- **回归**：Esc 关窗后立即按 `Q`/`N`/`F`/`Z`/`S` 均正常开合（见 §2 矩阵）。

### ISSUE-4 服务端可重复投递的确认/邀请面板会**叠加重复**（用户报告的「重复弹出/重复出现」）— 已修复

- **复现**（headless 自检，可重跑）：`godot-mono --path GodotClient res://Scenes/UITestScene.tscn -- --popup-dedup-audit`
  - 修复前：`FAIL trade=1->2 ... marriage=3->4` —— `S.TradeRequest` / `S.MarriageInvite` 每到达一次就 `new` 一块面板并 `AddControl`，**第二块叠在第一块同一坐标上**。
  - 修复后：`PASS trade=1->1 group=3->3 guild=2->2 marriage=3->3`（重复投递不再增长）。
- **对照**：`GroupDialog.ShowInvite`（`:252`）与 `GuildDialog.ShowInvite`（`:586`）**已有** `if (_invitePanel != null) { RemoveControl; QueueFree; }` 守卫，行为正确（`3->3` / `2->2` 稳定）；缺守卫的是 `TradeDialog.ShowRequest` 与 `GuildDialog.ShowMarriageInvite`。
- **根因**：这两个方法每次调用都 `new DXControl` 面板 + `AddControl`，无旧面板摘除。
- **修复**：两处各加 `_requestPanel` / `_marriageInvitePanel` 字段守卫（先 `RemoveControl` + `QueueFree` 旧面板），并在 accept/decline 回调里把字段置 `null`（与同文件既有 `_invitePanel` 写法一致）。
- **验证**：
  - 新增自检 `--popup-dedup-audit`（`UITestScene.AuditPopupDedup`），断言「重复投递不增长」这一不变量；
  - **敏感性已验证**：临时移除两处守卫后自检立刻 `FAIL trade=1->2 marriage=3->4`，恢复守卫后 `PASS`，证明该自检能捕获此缺陷；
  - `dotnet build GodotClient/ZirconClient.csproj`：0 error。

### ISSUE-5 行会窗关闭钮点不到（被内容层盖住）— 已修复

- **复现**：按 `F` 打开行会窗 → 点右下关闭 ✕（逻辑 672,444）→ 窗口**不关**；`Esc` 能关。
- **根因**（命中探针实测，非推断）：`GuildDialog` 构造期先 `AddControl(_closeButton)`（`:72`），
  再 `AddControl(_content)`（`:76`）；legacy 布局把 `_content` 改成铺满整窗
  （`Location=Zero` / `Size=Size`，`:128-129`）。Godot 后添加的兄弟节点在**上层**，
  于是 `_content` 盖住关闭钮并吃掉点击。
  探针证据：`[HitProbe] mouseLogical=(672,444) top=DXControl owner=GuildDialog rect=(0,0)/(596,446)`
  —— top 是 `_content` 而不是 `DXButton`。
- **修复**：legacy 布局末尾对 `_closeButton` 调 `BringToFront()`。
- **验证**：真机 `F` 开 → 点 (672,444) → 探针无 `GuildDialog`；连续 3 轮「开→点关闭钮」全部 CLOSED。
  截图 `evidence/20-guild-open-with-close-visible.png` / `evidence/21-guild-closed-via-close-btn.png`。

### 附：坐标基准缺陷（审计工具本身，已修正，非产品缺陷）

首轮「关闭钮点不到」的结论里有一部分是**测量工具错误**：openbox 会 reparent 客户端窗口，
`xdotool getwindowgeometry` 返回的是**外框**位置，比客户区绝对原点偏移 `(1,24)`。
用外框原点点击会整体偏低 24px，表现为「点了没反应」。
改用 `xwininfo` 的 `Absolute upper-left` 作客户区原点后，同一批点击全部命中
（例：点 (222,11) 时命中探针回报 `mouseLogical=(222,11)`，此前为 `(223,35)`）。
`harness.sh` 与探针脚本已按此修正，后续结论均基于修正后的基准。

### ISSUE-6 设置窗开关状态变了但画面不刷新（第 2 次起点击零变化）— 已修复

- **复现**：按 `N` 打开设置窗 → 反复点第 3/4 个开关（逻辑 440,314 / 440,341）→
  第 1 次点击有视觉变化，**之后每次点击该区域像素零变化**（连点 3 轮：diff = 115 → 0 → 0）。
  开关 1/2（440,167 / 440,240）每轮都正常（231px 变化）。
- **根因**：`DXImageControl.DrawImage` 是**裸字段**（`DXImageControl.cs:45`），
  `ConfigDialog` 靠 `onButton.DrawImage = enabled; offButton.DrawImage = !enabled;`
  切换 ON/OFF 指示（`ConfigDialog.cs:236-237`）。裸字段赋值**不触发 `QueueRedraw`**，
  Godot 不会重绘该控件，于是状态已变而画面不变。
  开关 1/2 之所以「看起来正常」，是因为它们的 setter 额外调用了
  `ClientSettings.ApplyAudioSettings()` 等副作用，间接带动了重绘；3/4 的 setter
  只改纯内存/配置字段，没有副作用 → 立刻暴露。
- **修复**：`DrawImage` 改为属性，setter 在值变化时 `QueueRedraw()`。
- **验证**：真机 3 轮 on/off，ON 区域每轮 diff 稳定 **249px**（修复前 115→0→0）。
  截图 `evidence/24-before-config-toggle-on/off.png`（修复前）与
  `evidence/22-fixed-config-toggle-on.png` / `evidence/23-fixed-config-toggle-off.png`（修复后）。

## 2. 热键/窗口生命周期矩阵（修复后复跑）

方法：每个键 `按一次 → 探针 → 再按一次 → 探针 → Esc → 探针`。判定「开」= 探针出现该窗口；「关」= 消失。

| 键 | 窗口 | 开 | 同键再按 | Esc | 判定 |
|---|---|---|---|---|---|
| `Q` | 背包 InventoryDialog | ✅ | ✅ 关 | ✅ | PASS |
| `W` | 人物 CharacterDialog | ✅ | ✅ 关 | ✅ | PASS |
| `E` | 技能书 MagicDialog | ✅ | ✅ 关 | ✅ | PASS |
| `D` | 任务 QuestDialog | ✅ | ✅ 关 | ✅ | PASS |
| `G` | 组队 GroupDialog | ✅ | ✅ 关 | ✅ | PASS |
| `F` | 行会 GuildDialog | ✅ | ✅ 关（本轮修复） | ✅ | PASS |
| `N` | 设置 ConfigDialog | ✅ | ✅ 关（本轮修复） | ✅ | PASS |
| `V` | 小地图显隐 | ✅ | ✅ 关 | ✅ | PASS |
| `Z` | 腰带 BeltDialog | ✅ | ✅ 关 | ✅ | PASS |
| `S` | 坐骑 HorseDialog | ✅ | ✅ 关 | ✅ | PASS |
| `R` | 聊天窗 F350 | ✅ | ✅ 关（本轮修复 Esc 路径） | ✅ 关（本轮修复） | PASS |
| `B` | 技能条（非窗口，`_magicBar.Visible` 翻转） | 探针不可见（非 DXWindow） | — | — | 另测，见 §3 |
| `T` | 小地图 128↔256 | 探针不反映（同控件换尺寸） | — | — | 另测，见 §3 |
| `C` | 交易请求（发包，非窗口） | 需第二玩家 | — | — | BLOCKED（见 §4） |

`Esc` 在无可见窗口时按 `WindowManager.CloseTop()` 返回 false，不影响地图操作（实测 `C` 行无窗口时 Esc 无副作用）。

## 3. 非窗口 HUD 元素

| 元素 | 操作 | 结果 | 判定 |
|---|---|---|---|
| HUD cap11（665,16,40×38） | 点击 | 打开设置窗；再点关闭 | PASS（与 ISSUE-1 同路径） |
| HUD cap6（616,82,28×26） | 点击 | 打开行会窗；再点关闭 | PASS（与 ISSUE-2 同路径） |
| HUD 其余 cap / 球体 / 状态条 | — | 见 `GODOT_WINDOW_PARITY_MATRIX_2026-09-29.md`（几何/帧号已逐项 MATCH） | 本轮不重复 |

## 4. BLOCKED / 未覆盖（如实记录）

| 项 | 状态 | 原因 |
|---|---|---|
| 交易窗（F1050）全生命周期 | `BLOCKED` | 需**第二玩家**进入交易态，单人测试不可达 |
| NPC 对话/商店/仓库/任务发放等**服务端包驱动**窗口 | `BLOCKED` | 需对应 NPC 与业务状态；本轮仅覆盖客户端侧入口与几何 |
| 聊天窗 ✕（532,350）点击 | 已实测可关 | 但**原版该热区在窗口矩形外且不关窗**（`GODOT_UI_OPEN_DECISIONS_2026-09-29.md` B-2），Godot 侧语义差异已登记，非本轮引入 |
| `B` 技能条 / `T` 小地图尺寸 | `PARTIAL` | 二者不是 `DXWindow`，`Ctrl+F12` 探针不反映；需逐像素比对（未做） |
| 现代 Zircon UI（`--zircon-ui`）全套 | 未覆盖 | 本轮按仓库默认（legacy EI）审计；现代模式窗口另需一轮 |

## 5. 覆盖统计

- 本轮真机覆盖：**14 个热键入口 + 2 个 HUD 按钮**（对应 11 个 `DXWindow` + 1 个非窗口开关），
  每个均验证「开 → 同键关 → Esc 关」三段生命周期。
- 已修复：**4 个确认缺陷**（设置窗、行会窗、聊天窗 Esc/热键吞噬、邀请/确认面板重复叠加），
  全部经真实运行或可重跑自检复测，其中重复叠加缺陷的检测敏感性已验证。
- 未覆盖：交易（需第二玩家）、服务端包驱动的 NPC/商店/任务链、现代 UI、非窗口 HUD 元素的像素级复核。

## 6. 提交记录

| 内容 | 提交 |
|---|---|
| 修复设置窗/行会窗/聊天窗热键与 Esc（含证据截图） | 见下方推送记录 |


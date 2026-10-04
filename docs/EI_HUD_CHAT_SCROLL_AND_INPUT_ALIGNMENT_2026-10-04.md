# EI HUD 聊天框滚动条与回车输入焦点深度对齐报告

- **日期**：2026-10-04
- **分支**：`origin/master`（提交：`16bedcab`）
- **涉及模块**：HUD 聊天面板（`ChatLogPanel`）、HUD 聊天输入条（`ChatTextBox`）、详细聊天弹窗（`LegacyChatDialog`）、包裹滚动条（`InventoryDialog`）、输入分流（`GameScene`）
- **基准对照**：传奇 3 EI 官方 Delphi 源码（`Source/Client/PlayScn.pas`、`ClMain.pas`、`FState.pas`）、`LegacyEI/Data/GameInter.wil` 原版素材资产

---

## 1. 背景与问题描述

在进行客户端功能与交互审计过程中，用户反馈游戏主界面的聊天系统存在多项严重偏离原版体验的交互与视觉缺陷：
1. **回车键（Enter）行为严重错乱**：
   - 玩家在主界面按下回车（Enter）或空格（Space）时，本应激活底部 HUD 聊天槽下方的打字输入框（`ChatTextBox`），让玩家输入聊天文本或命令；
   - 实际表现却是**直接弹出了 572×388 的全屏详细聊天记录弹窗**（F350 `LegacyChatDialog`），打断正常游戏操作，常规打字功能被彻底劫持。
2. **HUD 聊天框滚动条样式错误**：
   - 聊天框右侧滚动条使用了现代 `DXVScrollBar` 的默认视觉（黑色底板、金色边框、现代上下箭头和滑块），直接破坏了 F50 经典界面的古风木桩木槽质感。
3. **滚动条完全无法滚动与拖动**：
   - 鼠标滚轮在聊天框内滚动毫无响应；
   - 鼠标点击或拖动右侧滚动轨道毫无反应。

---

## 2. 原始代码机制与原版设计深度溯源

通过审查 `reference/mir3-source/Source/Client/` 源码与 `LegacyEI/Data/GameInter.wil` 资产，确认原版的具体实现原理如下：

### 2.1 HUD 输入框与按键激活链路（`ClMain.pas` & `PlayScn.pas`）
- **按键分发**（`Source/Client/ClMain.pas:2811-2856`）：
  ```pascal
  // ClMain.pas
  byte(' '), 13: // 空格键与回车键 (VK_RETURN = 13)
  begin
    PlayScene.EdChat.Visible := TRUE;
    PlayScene.EdChat.SetFocus; // 激活底部常驻输入条 EdChat
    ...
  end;
  byte('@'), byte('!'), byte(','), byte('/'):
  begin
    PlayScene.EdChat.Visible := True;
    PlayScene.EdChat.SetFocus;
    // 自动预填前缀符号...
  end;
  ```
- **输入框生命周期与退出**（`Source/Client/PlayScn.pas:274-343`）：
  ```pascal
  // PlayScn.pas
  procedure TPlayScene.EdChatKeyPress(Sender: TObject; var Key: Char);
  begin
    if Key = #13 then begin // 按回车发送
      FrmMain.SendSay(EdChat.Text);
      EdChat.Text := '';
      EdChat.Visible := FALSE; // 发送后失焦并隐藏
      Key := #0;
    end;
    if Key = #27 then begin // 按 ESC 取消
      EdChat.Text := '';
      EdChat.Visible := FALSE; // 清空并失焦隐藏
      Key := #0;
    end;
  end;
  ```
- **详细聊天窗的打开途径**：
  原版中打开详细聊天历史窗 `DChat`（F350）的快捷键为 **`R` / `Ctrl+R`**（`ClMain.pas:2585-2589`），或点击 HUD 上的 cap9 按钮；回车键与空格键**绝对不会打开详细聊天窗**。

### 2.2 滚动条视觉与 Gauge 拖拽模型
- **素材定义**：
  - 包裹（`InventoryDialog`）：使用 `GameInter.wil` 第 280 帧（16×424 锁链，Y≈208 烤制圆点滑块）；
  - HUD 聊天框（`ChatLogPanel`）：使用 `GameInter.wil` 第 68 帧（12×154 锁链，Y≈75 烤制橙红圆点滑块）。
- **交互逻辑**：
  原版客户端并没有独立拉伸的滑块控件，而是采用标准的 `gauge` 交互模式（`0x42FFD0` → `F707 0x417D00`）：
  锁链贴图包含完整的链身与烤制好的圆点，控件通过视口矩形裁切多余链身，根据当前滚动进度纵向偏移锁链贴图，让圆点移动到对应位置。点击轨道任意位置按指针高度比例直接定位，按住拖动则平滑跟随鼠标。

---

## 3. 根本原因分析（Root Causes）

| 缺陷表现 | 对应代码位置 | 根本原因机制 |
|---|---|---|
| **回车键触发大弹窗** | `GameScene.cs:11310` | 在 `AutoLoginArgs.LegacyUi` 模式下，未判断大弹窗是否已打开，直接将按键交给了 `_legacyChatDialog.HandleGlobalKey`；而该函数收到 Enter/Space 键时强行调用了 `OpenChat(parent)` 打开 F350 弹窗。 |
| **滚动条现代边框侵入** | `ChatLogPanel.cs:109-130` | 直接将 `_scroll`（`DXVScrollBar`）设为 `Visible=true`，未隐藏 `Border`、`BackColour`、`UpButton`、`DownButton`，导致现代黑色矩形和金色描边覆盖在 HUD 上。 |
| **锁链无法点击与拖拽** | `ChatLogPanel.cs:124-126` | 缺少类似包裹的 `LegacyGaugeDragSurface` 覆盖层；仅靠 12×16 的空白 `PositionBar` 无法提供全轨道交互，且未滚动到底时交互直接被锁死。 |
| **滚轮完全失效（核心根因）** | `ChatLogPanel.cs:518-521` | 每条消息行 `DXLabel` 和物品链接为了实现点击私聊与物品悬浮，设置了 `MouseFilter = MouseFilterEnum.Stop`。鼠标在文字上时，**滚轮事件被文字标签拦截吸收**，未能冒泡给滚动条。 |

---

## 4. 重构与修复方案

### 4.1 提取并共享 `LegacyGaugeDragSurface`
将原先嵌套在 `InventoryDialog.cs` 内部的私有类提取为独立公共控件 [`LegacyGaugeDragSurface.cs`](file:///home/tetsuya/development/zircon/GodotClient/Controls/LegacyGaugeDragSurface.cs)：
- 支持统一的 Y 轴指针比例换算 `ApplyGaugeY(localMouse.Y)`；
- 支持自定义两端缓冲间距 `Pad`（包裹为 10f，HUD 聊天框为 8f）；
- 支持拖拽状态跟踪与鼠标滚轮事件统一转发给绑定的 `DXVScrollBar`。

### 4.2 重构 `ChatLogPanel` 滚动条（对齐包裹模型）
在 [`ChatLogPanel.cs`](file:///home/tetsuya/development/zircon/GodotClient/Controls/ChatLogPanel.cs) 中引入三层滚动模型：
1. **纯逻辑状态层**：`_scroll` 保持纯状态存储，关闭 `Border`，背景全透明，隐藏上下按钮与系统滑块（`Visible = false`）。
2. **视觉裁剪与滑动层**：
   - `_legacyScrollClip`：12×74 像素剪裁视口，精准定位在 HUD 聊天槽右侧黑槽（X=356，Y=0）；
   - `_legacyScrollTrack`：加载 `GameInter.wil` 帧 68（12×154 原始锁链）；
   - `RefreshLegacyChainPosition()`：根据 `_scroll.Value` 在 `[0, MaxValue - VisibleSize]` 的比例计算滑动偏移：
     $$\text{thumbY} = \text{pad} + t \times (\text{trackH} - 2 \times \text{pad})$$
     $$\text{Location.Y} = \text{thumbY} - \text{bakedDotY}\ (\text{bakedDotY}=75)$$
3. **全交互命中层**：
   - 引入 `_legacyGaugeDrag`，覆盖 12×74 的轨道，支持全轨道点击跳转与连续拖动；
   - **打通滚轮穿透**：在 `RebuildVisibleLines` 和 `AddLinkedItemLabels` 中，对所有生成的文字行 `line.MouseWheel` 和链接 `linked.MouseWheel` 显式挂接 `_scroll.DoMouseWheel`，彻底消除鼠标停在文字上无法滚动的死区。

### 4.3 修复按键路由与交互逻辑
1. **全局按键分流（`GameScene.cs`）**：
   - 仅当 `_legacyChatDialog.Visible == true` 时，全局按键才流向详细聊天弹窗；
   - 当详细聊天窗处于关闭状态时，回车（Enter）、空格（Space）、斜杠（`/`）、感叹号（`!`）、艾特（`@`）全部交由 HUD 输入框 `_chatTextBox.HandleGlobalKey(key)` 处理。
2. **弹窗自身防护（`LegacyChatDialog.cs`）**：
   - 在 `HandleGlobalKey` 开头加入 `if (!Visible) return false;`，禁止隐藏状态下被动唤醒；
   - 弹窗打开时按下回车只聚焦输入框，不再重复调用 `OpenChat`。
3. **HUD 输入框交互补齐（`ChatTextBox.cs`）**：
   - 增加 `_input.Canceled` 事件监听：玩家打字时按 `ESC` 键，立即清空文本并释放输入焦点（对齐 `PlayScn.pas:337`）；
   - 规范化 `@` 与 `!` 前缀判定，兼容所有输入法与键盘布局。
4. **PageUp / PageDown 快捷翻页（`GameScene.cs` & `ChatLogPanel.cs`）**：
   - 在非打字状态下按下 PageUp 或 PageDown，直接调用 `_chatLog.ScrollPage(-1)` 或 `_chatLog.ScrollPage(1)` 逐屏翻页，对齐原版 `ClMain.pas:2602-2615`。

---

## 5. 修改文件清单

| 文件路径 | 改动性质 | 核心修改点 |
|---|---|---|
| `GodotClient/Controls/LegacyGaugeDragSurface.cs` | **新增** | 独立公共控件，封装锁链/仪表拖动、点击与滚轮交互。 |
| `GodotClient/Controls/InventoryDialog.cs` | 修改 | 移除重复的私有嵌套类，改用公共 `LegacyGaugeDragSurface`。 |
| `GodotClient/Controls/ChatLogPanel.cs` | 修改 | 实现视口剪裁+F68锁链平移模型；隐藏默认边框；添加消息行/链接滚轮事件转发与 `ScrollPage`。 |
| `GodotClient/Controls/ChatTextBox.cs` | 修改 | 接入 `ESC` 键取消清空并失焦机制；规范化符号输入。 |
| `GodotClient/Controls/LegacyChatDialog.cs` | 修改 | 隐藏状态下不消费按键；公开 `ScrollBy` 接口供快捷键调用。 |
| `GodotClient/Scripts/GameScene.cs` | 修改 | 修正全局按键分流（未开窗时不弹窗）；增加 PageUp/PageDown 快捷翻页。 |

---

## 6. 验证记录

### 6.1 编译检查
```bash
dotnet build GodotClient/ZirconClient.csproj
```
- **结果**：`0 个错误`，`6 个警告`（均为既有的 CS8632 可空类型历史警告，无任何新增警告）。

### 6.2 自动化 UI 布局审计
```bash
godot-mono --path GodotClient --headless Scenes/UITestScene.tscn -- --ui-audit
```
- **结果**：全套 UI 审计通过：
  - `[UIHudAudit] PASS panel=(800, 136) buttons=16 click=hit`
  - `[ExpBarDiag] exp=0 max=0 tex=(164, 6) legacy=False visible=True pos=(235, 122) size=(164, 10)`
  - 聊天面板与输入栏未产生任何布局位移或尺寸断言失败。

### 6.3 游戏真实登录与全流程验证
在 Xvfb 虚拟显示环境下启动客户端连接本地服务器，完成完整登录流程验证：
```bash
xvfb-run -a godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 --user test@test.com --pass test123 --char TestHero --window
```
- **验证项**：
  1. WeMade 片头过场播放正常；
  2. 选角界面进入比奇城场景（Bichon Town 800×800）正常；
  3. 场景内 NPC（墨菲、伦纳德）与主角装备外观正常渲染；
  4. 聊天面板正常挂载初始化，未报任何 C# 空引用或越界异常；
  5. 服务端与客户端通信保持平稳，Ping/DayChanged 包周期正常。

---

## 7. 提交记录

- **Commit ID**：`16bedcab`
- **Commit Message**：`fix(chat): 修复HUD聊天框滚动条与回车输入焦点路由对齐原版`
- **推送分支**：`origin/master` (iamcheyan/Zircon fork)

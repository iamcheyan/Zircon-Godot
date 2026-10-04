# 全局技能快捷键绑定与施法链路修复报告

> 日期：2026-10-04  
> 涉及模块：客户端按键分发系统、技能窗口（`MagicDialog`）、旧版技能行（`LegacySkillRowView`）、快捷栏同步、施法逻辑（`UseMagicSlot`）  
> 提交哈希：`6e8687cf`（功能修复）、`e94333f7`（回归断言）  
> 影响范围：**全局所有职业与角色**（非单一角色补丁）

---

## 一、问题背景

在对技能全量恢复后的联机测试中，发现角色已掌握技能在界面中**无法绑定快捷键**，同时在游戏内也**无法使用快捷键施法**：
1. **技能界面无法绑定**：玩家打开技能书（按 E），点击或选中一个技能后，按下 F1~F12 等快捷键，界面没有任何反应，快捷栏不亮，按键没有被分配给技能。
2. **游戏内无法施法**：在关闭技能书后，按下 F1~F12 无法施法；即使通过其他途径尝试触发，战士的烈火剑法等技能依然无法蓄力。
3. **视觉反馈缺失**：旧版 EI 技能书界面的技能行中没有任何快捷键标识，玩家无法确认技能是否已绑定。

---

## 二、四大根本原因诊断

通过深入追踪 Godot 4 引擎输入分发机制、Zircon 自绘控件模型以及施法状态机，定位出四大核心病根：

### 1. `GameScene._Input` 窗口拦截早退导致按键吞没
在 `GodotClient/Scripts/GameScene.cs` 的 `_Input(InputEvent @event)` 中存在全局窗口门控：
```csharp
if (WindowManager.OpenWindows.Any(window => window != null && window.Visible))
{
    return;
}
```
当玩家打开技能书（`MagicDialog.Visible == true`）时，该门控直接执行了早退 `return`。全局按键分发在此处被切断，任何按键都无法向下传递给游戏逻辑。

### 2. Godot 4 控件事件分发机制导致的输入死锁
原代码试图在 `MagicDialog._UnhandledKeyInput` 和 `MagicCellView._UnhandledKeyInput` 中捕获按键：
- **Godot 4 官方规范**：对于 `Control` 节点，`_UnhandledKeyInput` **仅在该 Control 自身拥有键盘焦点（`HasFocus()`）或是拥有焦点的 Control 的父节点时，才会被分发**。
- **现状**：Zircon 的自定义控件体系继承自 `DXControl`，所有控件默认 `FocusMode = FocusModeEnum.None`。当技能窗口打开时，没有任何控件拥有键盘焦点（`GuiGetFocusOwner() == null`）。
- **结果**：引擎压根不会调用 `MagicDialog` 或 `MagicCellView` 的 `_UnhandledKeyInput`，写在其中的绑定逻辑永远处于死锁状态。

### 3. 旧版 EI 技能行无快捷键渲染（视觉黑盒）
在 `GodotClient/Controls/MagicDialog.cs` 的 `LegacySkillRowView._Draw()` 中：
- 代码只绘制了技能图标（MagicIcon）和技能名称；
- **完全没有绘制当前技能绑定的快捷键文本**。即便快捷键被写入数据，界面上依然一片空白，给玩家造成“绑定失败”的直观错觉。

### 4. 战士强化技能缺失发包链路与施法无反馈
在 `GameScene.cs` 的 `UseMagicSlot` 中：
- **战士技能分支遗漏**：原版传奇客户端对于烈火剑法（`FlamingSword`）、翔空剑法（`DragonRise`）、莲月剑法（`BladeStorm`）、移花接玉（`DemonicRecovery`）、铁布衫（`DefensiveBlow`）、破血狂杀（`OffensiveBlow`），按下快捷键时应发送 `C.MagicToggle` 激活蓄力/强化开关；而 GodotClient 遗漏了这 6 个技能的处理，直接滑落到普通法术逻辑构造了 `MirAction.Spell` 发送给服务端，导致服务端无法处理战士强化技能。
- **静默拦截无提示**：当角色等级低于技能所需等级（`NeedLevel1`）或当前魔法值（MP）不足时，原有逻辑直接静默 `return`，未向聊天框输出任何提示，导致玩家误以为按键失效。

---

## 三、架构级修复与技术实现

针对上述四大根因，进行了全链路贯通修复：

### 1. 全局输入显式优先路由（`GameScene.cs`）
在 `GameScene.cs` 的 `_Input` 窗口拦截门控之前，增加针对打开状态技能书的优先捕获：
```csharp
// 技能书/技能窗口处于打开状态时，优先让技能窗口捕获快捷键绑定/解绑事件（F1~F12 / Shift+F1~F12 / Delete / Backspace）
if (_magicDialog != null && _magicDialog.Visible && _magicDialog.HandleKeyInput(key))
{
    GetViewport()?.SetInputAsHandled();
    return;
}
```
当技能窗口打开时，按键在进入通用窗口拦截前被精准截获，避免了焦点死锁与事件吞没。

### 2. 统一快捷键管理核心（`MagicDialog.cs: HandleKeyInput`）
在 `MagicDialog` 中实现公共方法 `public bool HandleKeyInput(InputEventKey key)`，统一管理旧版 EI 界面与现代界面的快捷键绑定：
1. **按键支持**：
   - `F1` ~ `F12`：映射至当前技能栏组的 `Spell01` ~ `Spell12`；
   - `Shift + F1` ~ `Shift + F12`：映射至扩展快捷键 `Spell13` ~ `Spell24`（支持全套 24 快捷键）；
   - `Delete` / `Backspace`：显式清除当前技能的快捷键绑定；
   - **再次按下相同快捷键**：自动识别并解绑（符合原版操作习惯）。
2. **智能技能目标匹配**：
   - 优先选择当前点击选中的技能（`_legacySelectedSkill`）；
   - 若未显式点击，自动选择当前鼠标悬停的技能行（`_legacySkillRows.FirstOrDefault(r => r.IsHovered)`）；
   - 若均无，自动选定当前页第一个有效技能。
3. **同栏组互斥去重**：
   - 当将快捷键赋予新技能时，自动检索并清空该栏组内绑定了相同快捷键的其他技能，保证单键唯一性。
4. **实时同步与刷新**：
   - 调用 `game.SendMagicKey` 向服务端发送网络包，将改动持久化至服务端数据库；
   - 调用 `game.RefreshMagicBars()` 即时更新底部技能栏；
   - 对技能列表全部行调用 `QueueRedraw()`，即时渲染新按键；
   - 聊天框同步输出操作提示（如 `已将技能 [雷电术] 绑定至快捷键 F1` 或 `已解除技能 [雷电术] 的快捷键绑定`）。

### 3. 旧版技能行快捷键醒目渲染（`LegacySkillRowView._Draw`）
在 `LegacySkillRowView` 中增加快捷键渲染逻辑：
- 根据当前激活的技能栏组（`MagicBarSpellSet`）提取当前技能绑定的 `SpellKey`；
- 使用公用转换方法 `MagicDialog.SpellKeyText(key)`（如 `F1`、`F12`、`S+F1`）；
- 在技能行右侧区域（`X = 135`，垂直居中）以醒目的金橙色（`#D9730D`）绘制 `[F1]`、`[F2]` 等标签。

### 4. 补齐战士技能蓄力链路与友好提示（`GameScene.cs: UseMagicSlot`）
1. **补齐战士强化技能开关**：
   ```csharp
   case MagicType.FlamingSword:
   case MagicType.DragonRise:
   case MagicType.BladeStorm:
   case MagicType.DemonicRecovery:
   case MagicType.DefensiveBlow:
   case MagicType.OffensiveBlow:
       if (Library.Time.Now < magic.NextCast || magic.Cost > _currentMP) return;
       magic.NextCast = Library.Time.Now.AddSeconds(0.5D);
       SendMagicToggle(magic.Info.Magic, true);
       GD.Print($"[Magic] 激活技能强化/蓄力状态: {magic.Info.Name}");
       return;
   ```
2. **操作状态友好提示**：
   - 未绑定技能时：`ReceiveChat("当前快捷键未绑定技能，可按 E 打开技能书进行设置", MessageType.Hint)`；
   - 角色等级不足时：`ReceiveChat($"等级不足，无法使用技能 [{magic.Info.Local()}]（需要等级 {magic.Info.NeedLevel1}）", MessageType.Hint)`；
   - 魔法值不足时：`ReceiveChat($"魔法值不足，无法使用技能 [{magic.Info.Local()}]", MessageType.Hint)`。

---

## 四、修改文件与代码变更清单

| 文件 | 修改性质 | 主要变更内容 |
|---|---|---|
| [`GodotClient/Controls/DXControl.cs`](file:///home/tetsuya/development/zircon/GodotClient/Controls/DXControl.cs) | 属性可见性调整 | 将 `IsHovered` 属性 getter 改为 `public`，便于容器识别控件悬停状态 |
| [`GodotClient/Controls/MagicDialog.cs`](file:///home/tetsuya/development/zircon/GodotClient/Controls/MagicDialog.cs) | 核心功能实现 | 1. 新增 `SpellKeyText` 静态按键文本映射；<br>2. 新增 `HandleKeyInput` 全量快捷键绑定/解绑/去重方法；<br>3. `LegacySkillRowView` 增加 `Entry` 属性并在 `_Draw` 中绘制绑定的快捷键文本；<br>4. `MagicCellView` 补充 `Entry` 属性 |
| [`GodotClient/Scripts/GameScene.cs`](file:///home/tetsuya/development/zircon/GodotClient/Scripts/GameScene.cs) | 路由与释放完善 | 1. 在 `_Input` 窗口早退前优先路由快捷键至 `MagicDialog.HandleKeyInput`；<br>2. 在 `UseMagicSlot` 中为 6 种战士蓄力技能接入 `SendMagicToggle`；<br>3. 增加未绑定、等级不足、MP不足的聊天框提示 |
| [`GodotClient/Scripts/UITestScene.cs`](file:///home/tetsuya/development/zircon/GodotClient/Scripts/UITestScene.cs) | 自动化断言 | 在 `AuditMagic` 中增加按键文本映射（`SpellKeyText`）、组合键门控（Ctrl/Echo/Unpressed/Invalid）的自动化测试断言 |

---

## 五、验证与验收结果

### 1. 编译与语法检查
执行项目完整构建：
```bash
dotnet build GodotClient/ZirconClient.csproj
```
结果：**0 个错误，6 个历史警告**（均为既有的可空注释与未使用局部变量警告，未引入任何新警告）。

### 2. 自动化回归测试
运行无头 UI 自动化审计：
```bash
godot-mono --path GodotClient res://Scenes/UITestScene.tscn --headless -- --magic-audit
```
测试输出：
```text
[UIMagicAudit] PASS size=(419, 511) list=(15, 70)/(375, 418) scroll=(390, 68)/(20, 424) tabHeight=21
[UIMagicHotkeyAudit] PASS keyText/filter/ctrl-guard/echo-guard
```
- `keyTextOk`：`F1`、`F12`、`S+F1`、`S+F12` 映射完全正确；
- `keyFilter`：Ctrl 组合键保护（避免阻断切换技能栏）、按键弹起过滤、非按键字符过滤全部生效。

### 3. 实机操作指引
1. 游戏内按 **`E`** 打开技能书（Legacy EI 技能书界面或现代技能窗口）；
2. 鼠标点击任一技能（或鼠标直接悬停在技能行上），按下 **`F1` ~ `F12`**（或 **`Shift + F1` ~ `Shift + F12`**）：
   - 技能行右侧立即显示金色按键标识（如 `[F1]`）；
   - 屏幕下方快捷栏立即同步点亮该技能图标；
   - 聊天框提示：`已将技能 [xxx] 绑定至快捷键 F1`；
3. 再次对该技能按下同一个快捷键，或者按下 **`Delete` / `Backspace`**：
   - 快捷键立即解除，快捷栏图标清除，聊天框提示解绑；
4. 关闭技能书后，按下对应的快捷键：
   - 道士/法师法术正常锁定施放（自身增益/召唤无目标时自动对自身或脚下施放）；
   - 战士烈火剑法、翔空剑法等技能正常发送 Toggle 激活蓄力强化态。

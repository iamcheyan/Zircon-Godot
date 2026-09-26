# 施法期间人物突然移动/飘移调查

日期：2026-08-21

> 本轮仅调查和记录，不修改施法/移动代码。
> 结论是代码级根因假设，尚未通过可重复录屏场景最终闭合。

---

## 现象

用户观察到：

1. 人物抬手开始施法；
2. 施法动作尚未完成时，人物突然切回走路/跑步动画；
3. 人物在地图上出现飘移、滑行或动作混乱；
4. 施法动作、移动动作和跑步动作偶发互相覆盖。

---

## 原版动作语义

原版 `MapControl.ProcessInput()`（`Client/Scenes/Views/MapControl.cs:860-873`）处理顺序：

1. 如果存在 `User.MagicAction`，先检查 `User.NextActionTime` 和 `ActionQueue`；
2. 时间未到或动作队列非空时，直接返回；
3. 只有到达动作边界后才调用 `User.AttemptAction(User.MagicAction)`；
4. 成功提交施法后清空 `MagicAction`，本次输入循环不再进入移动分支。

原版移动条件还受 `MoveFrame`、`ActionTime`、`MagicTime`、`CanMove` 等状态门控；施法动作不会被普通移动分支直接覆盖。

---

## 当前 Godot 动作链

### 1. 施法输入

`GodotClient/Scripts/GameScene.cs:9847-9878`：

```csharp
if (Library.Time.Now < magic.NextCast || magic.Cost > _currentMP) return;
...
SuspendMovementForMagic();
if (IsPlayerWalking())
{
    _pendingMagicPacket = packet;
    _pendingMagicCastAtMs = _player.FrameStartMs + _player.MovementDurationMs;
    return;
}
_net.Connection.Enqueue(packet);
```

`SuspendMovementForMagic()`（`GameScene.cs:4632-4642`）只做：

- `_autoRun = false`；
- `MouseWalker.AutoRun = false`；
- `MouseWalker.SuspendUntilInputRelease()`。

它**没有清理已经发出的移动请求，也没有使旧移动回包失效**。

### 2. 新移动输入门控

`CanPlayerMove()`（`GameScene.cs:4623-4630`）包含：

```csharp
&& _pendingMagicPacket == null
&& !_player.IsSpellAnimation
```

这能阻止新的 `MouseWalker` 移动，但它只对新输入有效，不能阻止已经在网络中的 `S.ObjectMove` / `S.UserLocation` 回包改变动作。

### 3. 施法动画

`PlayerRenderer.IsSpellAnimation`（`PlayerRenderer.cs:105-113`）通过 `_spellType != None` 和 Combat/Channelling 动画判断施法状态。

施法动画结束后，`PlayerRenderer._Process()`（`:713-738`）将一次性动作切回 `Standing`，并在 `ApplyAnimation()`（`:262-278`）触发 `SpellAnimEnded`，由 `GameScene.OnObjectMagic()` 释放投射物。

这条链本身没有发现“按下技能立即切 Walking”的直接路径。

---

## 最可能的冲突点

### 根因 A：施法前已发出的移动回包迟到

流程可能是：

```text
按住鼠标移动
  ↓
客户端已发送 C.Move
  ↓
玩家按技能
  ↓
SuspendMovementForMagic 只停止后续 MouseWalker
  ↓
客户端进入施法动画
  ↓
旧 C.Move 的 S.ObjectMove 迟到
  ↓
OnObjectMove → ShowUserLocation
  ↓
纠正路径调用 _player.BeginMove(...)
  ↓
施法动画被替换为 Walking/Running
```

关键代码：

- `GameScene.OnObjectMove()`：`GameScene.cs:2051-2110`；
- `GameScene.ShowUserLocation()`：`GameScene.cs:7771-7836`；
- 纠正路径 `GameScene.cs:7817-7819`：

```csharp
_player.BeginMove(dir, distance, _playerHorse != HorseType.None, distance >= 2);
```

这里没有检查 `_player.IsSpellAnimation` 或当前是否存在待释放魔法。

### 根因 B：服务端拒绝旧移动后发送 UserLocation

`OnUserLocation()`（`GameScene.cs:1876-1891`）无条件执行：

```csharp
ApplyAuthoritativePlayerLocation(loc);
_player.PlayStandingForState();
```

如果这是施法期间某个旧移动请求的拒绝/纠正包，它会直接把当前施法动画切成 Standing。随后 MouseWalker 或其他动作又可能重新开始 Walking，造成“抬手 → 站立/走路 → 飘移”的混合表现。

### 根因 C：动作队列中混入 Walking

`PlayerRenderer.SetAnimation()`（`PlayerRenderer.cs:246-260`）在一次性动作未完成时，会将非 Standing/Dead 动作排入 `_animationQueue`：

```csharp
_animationQueue.Enqueue(anim);
_pendingSpellQueue.Enqueue(_pendingSpell);
```

因此如果施法期间有路径、移动、自动寻路或其他动作调用 `SetAnimation(Walking/Running)`，它不会立即显示，但会排队；施法结束后 `PlayerRenderer._Process()`（`:713-738`）会消费队列，出现：

```text
施法结束 → 立即切 Walking/Running
```

这可能是合法的施法后移动，也可能是施法前残留移动意图被错误保留。

### 根因 D：移动中的技能排队与实际施法状态之间存在窗口

`UseMagicSlot()`（`GameScene.cs:9865-9875`）在移动时把技能放入 `_pendingMagicPacket`，但并没有完全冻结服务端已经接受的移动状态。

`GameScene._Process()`（`:8164-8177`）的释放条件是：

```csharp
if (_pendingMagicPacket != null && CanPlayerTurn())
{
    bool walking = IsPlayerWalking();
    if (!walking || now >= _pendingMagicCastAtMs)
        enqueue magic;
}
```

`CanPlayerTurn()` 不检查 `_player.IsSpellAnimation`，所以 `_pendingMagicPacket` 的释放与移动动作、网络回包、动画队列之间存在边界窗口。

---

## 已排除的可能性

### 不是普通施法特效提前释放

`OnObjectMagic()` 会先播放起手特效，再注册 `SpellAnimEnded`；投射物不是收到网络包后立即创建。火球、冰箭、雷电球等普通技能的释放链已按动画结束处理。

### 不是 `SmoothMove` 本身直接切走路

`SmoothMove` 只影响 `PlayerRenderer` 的移动偏移插值（`PlayerRenderer.cs:742-776`），不会主动改变 `Animation`。它会放大动作切换错误的视觉表现，但不是直接原因。

### 不是服务端施法没有冷却

---

## 2026-08-21 修复结果

已执行以下修复：

1. `GameScene.OnObjectMagic()`：本地施法回包到达时，如果上一段移动插值仍在进行，先结束 `_moveFrameCount`、`OffsetX/Y` 和 `CameraOffset`，避免施法姿势继续沿移动偏移漂移；
2. `GameScene.ShowUserLocation()` / `OnUserLocation()`：施法动画期间收到迟到移动回包时，只更新权威位置，不调用 `BeginMove()` 或 `PlayStandingForState()`；
3. `PlayerRenderer.SetAnimation()`：施法动画期间拒绝迟到的 Walking/Running/HorseWalking/HorseRunning/CreepWalk 动作，避免移动动作进入队列覆盖施法；
4. 客户端构建并进入地图烟测通过：0 errors，保留项目已有 3 个 warning。

仍需真实操作验证的边界：移动中按技能、连续快速施法、施法期间保持鼠标移动，以及服务端拒绝旧移动请求后的动作恢复。

服务端在 `PlayerObject.cs:14831-14901` 检查 `ActionTime`、`MagicTime` 和 `magicObject.Magic.Cooldown`，客户端也检查 `magic.NextCast`。这是施法频率控制，不是动作冲突消除机制。

---

## 建议修复方案

### P0：施法状态下拒绝旧移动回包触发新 Walking

在 `ShowUserLocation()` 和 `OnObjectMove()` 的纠正路径增加状态分支：

- 若当前处于施法动画，位置可以校正，但不要调用 `BeginMove()`；
- 不要让迟到的移动回包覆盖当前施法动画；
- 必须记录并清理该移动回包对应的门控状态。

### P0：施法开始时使旧移动请求失效

`SuspendMovementForMagic()` 应增加“移动序列号/代次”或 pending move invalidation：

```text
施法开始 → moveGeneration++
旧 ObjectMove/ UserLocation 回包带旧 generation → 只校正位置，不重启动作
```

仅关闭 MouseWalker 不足以取消已经发出的 TCP 移动包。

### P1：清理施法期间排队的 Walking/Running

施法开始时：

- 清除 `_animationQueue` 中的 Walking/Running；或
- 记录动作来源，丢弃施法开始前的移动动作；
- 施法结束后只恢复一个合法的 Standing/State 动作，不直接播放过期 Walking。

### P1：严格分离“位置纠正”和“动作切换”

`OnUserLocation()` 当前无条件调用 `PlayStandingForState()`。建议：

- 施法中：只更新权威位置/偏移，不切换动画；
- 非施法中：才按原版纠正为 Standing；
- 只有新的服务端 `ObjectMove` 且确认是施法之后的合法移动，才开始 Walking。

### P2：增加动作来源诊断

在以下位置打印短期诊断日志：

- `UseMagicSlot()`：施法开始；
- `SuspendMovementForMagic()`：冻结移动；
- `OnObjectMove()` / `ShowUserLocation()`：移动回包与纠正；
- `OnUserLocation()`：服务端位置纠正；
- `PlayerRenderer.ApplyAnimation()`：每次动作切换及队列来源。

必须记录：

```text
时间、Animation、_spellType、_pendingMagicPacket、移动方向、distance、位置、是否纠正路径
```

这样可以区分 A/B/C/D 四类竞态，而不是仅凭截图猜测。

---

## 调查结论

当前代码的施法动画本身不是每次必然立即切走路；问题更像是**施法开始前后，已经发出的移动请求、迟到的服务端位置回包、PlayerRenderer 动作队列三者发生竞态**。

最高概率路径：

```text
移动包已发出
→ 按技能进入施法
→ SuspendMovementForMagic 只停止新移动
→ 旧 ObjectMove/UserLocation 迟到
→ ShowUserLocation/OnUserLocation 改变位置或动作
→ Walking/Running 覆盖或排队到施法后
→ 视觉上人物飘移、施法和走路动作混合
```

本轮未修改代码，仅记录了证据、排除项和下一步修复方案。

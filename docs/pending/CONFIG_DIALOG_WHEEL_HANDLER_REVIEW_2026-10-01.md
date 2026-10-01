# ConfigDialog 滚轮 Handler 重复累加 — 修复评审报告

**日期**：2026-10-01
**目标文件**：`GodotClient/Controls/ConfigDialog.cs`
**报告编号**：BUG-10（UI 滚动审计）

---

## 根因

`SelectTab(int tab)` 在每次执行时都会执行：

```csharp
_page.MouseWheel += (s, e) => { if (_scroll != null) _scroll.DoMouseWheel(s, e); };
```

`+=` 追加匿名 lambda 但从不解绑。切换 N 次页签后，`_page.MouseWheel` 持有 N 个 handler。
一次滚轮事件触发 N 次 `DoMouseWheel`，滚动速度随切换次数线性累加；旧闭包的 `_scroll`
字段引用与 `ConfigDialog` 生命周期挂钩，不会被 GC 提前回收。

---

## 改动

**唯一修改文件**：`GodotClient/Controls/ConfigDialog.cs`

1. **构造函数**（原 L54–56）——在 `AddControl(_page)` 之后、`SelectTab(0)` 之前，
   添加一次性具名订阅：
   ```csharp
   _page.MouseWheel += OnPageMouseWheel;
   ```

2. **SelectTab()**（原 L137）——移除其末尾的匿名 `+=`。

3. **新增具名方法** `OnPageMouseWheel`（紧接 `SelectTab` 之后）：
   ```csharp
   private void OnPageMouseWheel(object sender, MouseWheelEventArgs e)
   {
       if (_scroll != null) _scroll.DoMouseWheel(sender, e);
   }
   ```

`_scroll` 在每次 `SelectTab()` 冒头时重置为 `null`（L110），若当前页内容不超出则保持
`null`——`if (_scroll != null)` guard 确保无 scroll 页面不触发任何动作，F750 legacy
路径（`_page.Visible = false`）完全不受影响。

---

## 验证

| 步骤 | 结果 |
|---|---|
| `dotnet build GodotClient/ZirconClient.csproj --no-incremental` | ✅ 0 错误，3 个预存警告（均为无关文件） |
| 离线行为回归（`scratch/test_wheel_regression.py`）：切换 1/2/5 次后滚轮调用次数 | ✅ 修复后均为 1；修复前 2/5 次时分别 2/5 倍速（BUG 已重现并对比） |
| 无 scroll 页面（内容未溢出 340px）时触发滚轮 | ✅ 无异常，无副作用 |
| UI 运行验证（游戏内）| ⚠️ **未执行**（本次 goal 窗口内无连接配额；行为等价性由离线回归已覆盖） |

---

## 剩余风险

- **AuditLayout()** 内部调用 `SelectTab()` 5 次（L527–537）。修复后这些调用不再产生额外
  handler，此前若 `AuditLayout` 被重复调用则会在原 BUG 下加速累积——现在已消除。
- `DXControl.Dispose()` 不清空 `MouseWheel` invocation list。若 `ConfigDialog` 实例被
  Dispose 后 `_page` 节点以某种方式残留（非正常路径），handler 会随节点消亡。当前
  `WindowManager.Close(this)` 调用 QueueFree，节点树随之销毁，不存在游离 handler
  风险——但若未来 `_page` 被单独重建，需注意先 `-= OnPageMouseWheel`。
- `StorageDialog` / `AutoPotionDialog` 等其他控件的 MouseWheel 订阅模式各异，本次不修改。

---

## 并行边界合规

- 未接触 `NPCDialog.cs` 及任何 NPC 相关文件
- 未触碰 `.artifacts/npc-f1100-acceptance-2026-09-25/` 截图
- 未修改任何滚动控件基类或其他审计报告

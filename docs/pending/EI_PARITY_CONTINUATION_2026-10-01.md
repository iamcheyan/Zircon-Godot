# EI parity 续作：未闭合项核查记录（2026-10-01）

> 本文件属于本 goal 的**专属产物**，只记录被核实的结论与证据；不修改
> `Mir3-Research` 的既有总审计文档（其工作区存在其他 goal 的未提交改动）。
> 反汇编一律使用 `/home/tetsuya/mir2ei.before-path-fix-20260927-2330/Mir3.exe`
> （EI 3.0 原版，524288 字节；`.text` 完整，`.data` 为 `rsize=0x5000` 而 `vsize=0x49efd4`，
> 即大部分 `.data` 为加载期零填充，**其初始值不在文件内**）。
> 工具：`Mir3-Research/Tools/reverse-engineering/disasm_capstone.py --exe <上述路径>`（只读使用）。

---

## 1. B-11 确认框输入框位置：rect 身份已闭合，最终屏幕位置仍 BLOCKED

### 1.1 已核实（primary-static）

**rect 的位置与身份**

- `window-catalog-evidence.json` 记录：
  `MoveWindow([0x8AA48C] main window, [0x8AB7F0]+0xDF, [0x8AB7F4]+0x23A, 0x162, 0x10) via 0x4762BC [0x427620-0x42763B]`。
- 本次反汇编 `0x00427614-0x0042763B` **逐条复核并确认**：

```
0x427614: mov ecx, [0x8AB7F4]     ; rect.top
0x42761a: mov edx, [0x8AB7F0]     ; rect.left
0x427620: mov eax, [0x8AA48C]     ; HWND（聊天窗）
0x427625: push 0x10               ; 高 16
0x427627: add ecx, 0x23A          ; y = rect.top + 570
0x42762d: push 0x162              ; 宽 354
0x427632: add edx, 0xDF           ; x = rect.left + 223
0x427638: push ecx                ; y
0x427639: push edx                ; x
0x42763a: push eax                ; hwnd
0x42763b: call [0x4762BC]         ; MoveWindow(hwnd, x, y, 354, 16, 1)
```

- **该 RECT 是"主窗口客户区 rect"**：`0x4118E0` 处
  `lea ecx,[esp+0x10]; call [0x476240](GetCursorPos); push y; push x; push 0x8AB7F0; call [0x4762B4](PtInRect)`
  → 命中后再 `mov edx,[0x8AB7B0]; push &pt; push edx; call [0x476234](ScreenToClient)`。
  而 `layout.json` 的既有记录名即 **`main-window-hwnd.0x8ab7b0`**，且 `0x8AB7F0 = 0x8AB7B0 + 0x40`
  → 同一结构体：`{HWND@+0x00, …, RECT@+0x40}`。
  即：**RECT 存的是主窗口客户区的屏幕坐标**（与鼠标屏幕坐标做 PtInRect，再 ScreenToClient 到客户区）。
- 输入框是**聊天窗的子窗**（MoveWindow 的第一个参数是聊天窗 HWND），因此 `(x, y)` 是**父客户区相对**坐标。

### 1.2 未能闭合（因此不修、不猜）

- **RECT 的运行期数值不可静态获得**：对 `0x8AB7F0`/`0x8AB7F4` 做全文件字节模式扫描（共 22 + 20 处引用），
  **全部是读取**（`mov reg,[…]` / `PtInRect` 的取址），**`.text` 内没有任何写入**；
  而 `.data` 的该地址落在**零填充区**（文件内无初始值）→ 无法得知 `left/top` 的实际值。
  ⇒ 输入框的**最终屏幕位置**只能在原版运行态读取（Windows 环境），本机不可得。
- 原版三处调用点（转账金额 / 丢金币 / 建行会名称）**共用同一常量** `(rect.left+0xDF, rect.top+0x23A)`；
  端口 `LogoutConfirmDialog` 的输入框尺寸 `354×16` 与原版一致，但位置是"框内 (3,100)（水平居中、按钮上方）"。
  由于**无法确认端口该输入框对应原版哪一个业务对话框**，且**原版最终坐标不可得**，
  按纪律**不做结构性改动**（把输入框挂到 UI 层属结构变更）。

### 1.3 状态

`BLOCKED`（需原版运行环境读取 RECT 运行期值，以及确认对应业务对话框）。
**本条不构成客户端缺陷结论**；已把可静态闭合的部分（身份链）闭合，并记录精确的后续取证点：
`0x8AB7B0` 结构体的 `+0x40` RECT 在运行时的 left/top，以及三处 MoveWindow 各自的前置业务分支。

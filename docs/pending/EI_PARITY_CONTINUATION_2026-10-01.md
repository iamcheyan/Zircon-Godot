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

---

## 2. I-3 背包页签/动作钮「点击音效」：**已修复并验收**（原版索引 0x69=105）

### 2.1 已核实（primary-static，本次反汇编）

**三个页签控件是共享按钮类实例，帧号来自构造实参**

- `0x42E838-0x42E855`（背包构造）：`push 0x4046b0 / push 0x404690 / push 3 / lea eax,[esi+0x5c] / push 0xb4 / push eax / call 0x4686c4`
  → 以 `0x404690`（共享按钮类构造）建 **3 个 stride=0xB4 的子控件**（数组基址 `bag+0x5C`）。
- `0x42EAD8 / 0x42EB01 / 0x42EB27`：三处 `call 0x417550`（固定控件初始化）分别对应 `[esi+0x5C]`/`[esi+0x110]`/`[esi+0x1C4]`，
  实参含**帧号对**：
  - 页签1：`0xA1 / 0xA2` = **F161/F162**
  - 页签2：`0x108 / 0x109` = **F264/F265**
  - 页签3：`0x10B / 0x10C` = **F267/F268**
  （与既有矩阵 I-3 标题「F161/162 / F264/265 / F267/268」完全一致。）
- `0x417550` 语义（本次反汇编确认，`ret 0x24` = 9 实参）：
  `arg1`→`[+0x14]`（帧对象，用于取 `[+0x38]` 尺寸）、`arg2`→`[+0x18]`、`arg3`→`[+0x1C]`、`arg4`→**x**、`arg5`→**y**、
  `arg6`→`[+0x34]`（字符串，`repne scasb` 求长后拷贝）、`arg7`→`[+0x24]`（字节标志）、`arg8`→`[+0x20]`、`arg9`→`[+0x30]`；
  并以 **`SetRect(&[+0x4], x, y, x+w, y+h)`**（w/h 取自帧尺寸）建立命中矩形。

**点击处理确实是「只播音」且音效索引 = 0x69**

- `0x4177F0`（页签的 release 处理）：

```
0x4177f8: mov byte [ecx+0x25], 0      ; 清按下标志
0x4177fd: add ecx, 4                  ; &rect（控件 +0x4 的命中矩形）
0x417802: call [0x4762B4]             ; PtInRect(&rect, x, y)
0x41780a: je 0x417826                 ; 未命中 → 返回 0
0x41780c: push 0 / push 0 / push 0
0x417812: push 0x69                   ; ← 音效索引 0x69 = 105
0x417814: mov ecx, 0x8AB130           ; ← 声音管理器单例（layout.json: sound-manager.0x8ab130）
0x417819: call 0x45AFC0               ; PlaySound(index=0x69, 0, 0, 0)
0x41781e: mov eax, 1
```

→ **点击命中时播放音效 `0x69`(=105)，不改任何模式字节**（与既有证据「装饰按钮、只播音」一致，且给出确切索引）。

### 2.2 命中矩形（已补齐）

- 页签控件由 `0x417550` 以 `SetRect(&[+0x4], x, y, x+w, y+h)` 建命中矩形（w/h 取自帧尺寸）；
  三处构造调用的实参给出 x/y：
  - 关闭钮（F161/162）：`lea eax,[ebp+0xf9]` / `lea edx,[ebx+0x120]`
  - 动作钮（F264/265）：`lea ecx,[ebx+0x106]` / `push ebp`（此前 `add ebp,0xb0`）
  - 模式位（F267/268）：`push ebp` / `add ebx,0x11e`
- **反解验证**：模式位在既有证据中已知为 `(176,286) 64×20`，代入 `(ebp+0xb0, ebx+0x11e)` 得
  `ebp = ebx = 0` → 三者的**窗口相对**矩形为：
  **关闭钮 (249,288) 28×26、动作钮 (176,262) 64×20、模式位 (176,286) 64×20**。
- 运行期交叉核对（联机截图，窗口屏幕原点 (644,96)）：
  F264 在 (820,358) 处与素材**完全一致（差 0.0）**，与动作钮的预测 (176,262) 吻合；
  `(176,286)` 处为模式美术（差 22.3，随模式换帧）。
- 另：`F267/F268` 在 EI `GameInter.wil` 中**不可解码（空帧）** → 默认（无模式）时原版该处也不画美术，
  与端口 `_legacyModeArt.Index = -1` 一致。

### 2.3 修复与验收

**原版行为**：三个控件同属共享按钮类（类构造 `0x404690`，release `0x4177F0`），
命中时 `push 0x69; mov ecx,0x8AB130; call 0x45AFC0` → 播放 **索引 0x69 = 105**，**不改模式字节**。
端口 `ClientData/sounds.json`：`ButtonA=103.wav`、`ButtonB=104.wav`、**`ButtonC=105.wav`**
（而 `DXButton` 默认 `Sound = ButtonA`）→ 端口此前点这三处播的是 103.wav；且模式位是 `DXImageControl`
（无点击能力）→ **完全无声**。

**最小修复**（`GodotClient/Controls/InventoryDialog.cs`，提交 `6eb1e422`）：

- `_legacyActionButton.Sound = SoundIndex.ButtonC`；`CloseButton.Sound = SoundIndex.ButtonC`；
- 新增透明热区 `_legacyModeTabHotspot`（`Index = -1`，只播音、不改模式）覆盖模式位 (176,286) 64×20；
- `AuditLegacyEiLayout` 增加三处 Sound 断言作为回归护栏。
- 为使「动作音效」可断言，`GameScene.PlaySound` 补一行与 `SoundPlayback.Play` 同格式日志
  （该路径此前无任何日志，按钮音效不可验收）。

**行为验收**（联机，TestHero，map 1；命令与证据）：

| 步骤 | 结果 |
|---|---|
| `--legacy-audit`（实验室） | **PASS**（含新增三处 Sound 断言） |
| 开背包后点击模式位屏幕 (820,382) | 日志 `[Sound] 播放 ButtonC (105.wav, loop=False)` |
| 再点击动作钮屏幕 (820,358) | 日志 `[Sound] 播放 ButtonC (105.wav, loop=False)` |
| 统计 | `播放 ButtonC` = **2**，`播放 ButtonA` = **0** |

**状态**：`已修复并验收`。**残余（不修，非缺陷）**：端口以「一个随模式换帧的美术控件 + 透明热区」
表达原版的「三个独立按钮」，可见渲染与点击音效均已对齐；按钮的按下态帧（F162/265）端口已在按钮
`PressedIndex` 中设置，未单独截图验收。

---

## 3. ProgUse 帧 2/3 语义：**已闭合**（= 悬浮名牌血条的「填充段 / 底框段」）

### 3.1 出处定位

§11 第 10 项只写「两个帧号的业务语义未闭合」，本次先定位其出处：
`Mir3-Research/docs/research/ei-ui-layout/GODOT_UI_OPEN_DECISIONS_2026-09-29.md:289`
B-8 表格「悬浮名字 `0x0040B750` | 选择器 `0x566DD4`（ProgUse.wil）帧 2/3」。
`0x566DD4 = 0x5600FC + 86*0x144` → **元素 86 = ProgUse.wil**（与 `horse-window-render-evidence.json`
的「element 0x566DD4 (86=ProgUse.wil)」一致）。

### 3.2 素材实测（EI `LegacyEI/Data/ProgUse.wil`，count=560）

| 帧 | 尺寸 | 内容 |
|---|---|---|
| **F2** | 32×4 | **深色（近黑）横条** |
| **F3** | 32×4 | **红色横条**（带一条较亮条纹） |

（相邻 F0/F1 = 96×172 选人屏背景；F4/F5 = 104×6，另一组条。）

### 3.3 反汇编定案（`0x40B750`）

```
0x40b77a: mov al, byte [ebx+0x61BC8]     ; HP 字节（0..100）
0x40b784-0x40b794: fild → fmul [0x47647c] → fmul [0x476478] → 取整   ; 按 HP 比例算出宽度
0x40b799: push 2                          ; ← 帧号 2
0x40b79b: mov ecx, 0x566DD4               ; ← 元素 86（ProgUse）
0x40b7a0: mov [esp+0x20], 4               ; 高 4（与素材 F2 的 4 一致）
0x40b7c7: mov edi, [ebx+0xE4] / 0x40b7cd: mov esi, [ebx+0xE8]        ; 默认锚点 = HUD+0xE4/+0xE8
0x40b7d3: add edi, 7   / 0x40b7d6: sub esi, 0x38                     ; +7 / −0x38
0x40b7ee-0x40b7ff: 取尺寸 → push 0xffff,0xffff → call 0x45FD50        ; 绘制帧 2（宽度=HP 比例）
0x40b80e: push 3                          ; ← 帧号 3
0x40b815: call 0x466130                   ; 解析帧 3（随后整宽绘制）
```

**结论（primary-static）**：
- **ProgUse F2 = 悬浮名牌血条的「已填充」段** —— 宽度按 `[HUD+0x61BC8]`（HP 字节）比例缩放；
- **ProgUse F3 = 同一血条的「底/框」段** —— 整宽绘制（红条）；
- 两者尺寸 32×4，锚点为 `HUD+0xE4/+0xE8` 再偏移 `(+7, −0x38)`。

### 3.4 状态与后续

`已闭合`（语义、素材、锚点、缩放来源均 primary-static）。
**后续（另立，不在本项）**：端口目前只在悬停时绘制名字与高亮（研究侧 §10.20 已验证 3000ms 保持），
**未绘制该 ProgUse 血条**；实现它需要 `[HUD+0x61BC8]` 在端口的对应值（HP 百分比）与上述锚点换算，
属 B-8 目标框/悬停族的增量实现，需另行设计最小改动与截图验收。

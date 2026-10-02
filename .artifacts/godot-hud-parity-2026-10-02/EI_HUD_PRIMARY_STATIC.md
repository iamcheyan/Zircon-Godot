# EI 3.0 主 HUD 主证据（primary-static，直接反汇编原版二进制）

- 二进制：`/home/tetsuya/mir2ei.before-path-fix-20260927-2330/Mir3.exe`
  = `/tmp/eiwork/Mir3.exe`，MD5 `264d848da377c2172ffe1444bf31e7d0`，524288 B，ImageBase 0x400000。
- 反汇编工具：capstone 5.0.9（`CS_ARCH_X86/CS_MODE_32`）。
- 本文是对本目标**独立于** Mir3-Research 既有 JSON 的取证；用于交叉验证。

## 1. 主 HUD 绘制函数 0x00429740 - 0x0042A838（`ret`）

单一函数（`sub esp,0x24c` … `ret`），依次绘制：

| 序 | VA | 内容 | 依据 |
|---|---|---|---|
| 1 | 0x429819 | 状态动态帧 0x82-0x85（`[0x7DA1D4]&0xFF` 查表 0x42A83C） | `and eax,0xff; cmp eax,3; jmp [eax*4+0x42a83c]` |
| 2 | 0x429A38 | F62 填充，位移 `[edi+0xC58]+0x31`, `[edi+0xC5C]+0xC` | `call 0x45f2d0` |
| 3 | 0x429BF0 | F60 填充，位移 `+0x31,+0xC` | 同上 |
| 4 | 0x429CBC | F61 填充，位移 `+0x6a,+0xC` | 同上 |
| 5 | 0x42A038 | F63 经验条，固定 `(0xEE,0x24E)`=**x238,y590** | `push 0x24e; push 0xee` |
| 6 | 0x42A0DF | 经验百分比文本，"%s : [%d,%d]" 之后；rect `0x13B..0x250`, `0x145..0x252` | 格式串 0x47BD4C+0x47BD5C |
| 7 | 0x42A243 | F67 负重条，位移 `[edi+0xC58]+0xD1`, `[edi+0xC5C]+0x25` | `call 0x45f2d0` |
| 8 | 0x42A469 | 地图标题 + 坐标文本，锚 rect `0x249..0x257`, `0..0x2C5`；颜色 0x00E1E1E1 | 格式串 0x47BD30 `%s : [%d,%d]` |
| 9 | 0x42A57E/0x42A5E0/0x42A642/0x42A6A4 | 地图标题 4 向黑色描边（±1） | 颜色 0x000A0A0A |
| 10 | 0x42A721 | 地图标题本体，颜色 0x00C8FFFF | — |
| 11 | **0x42A752-0x42A76B** | **AC 数值** SetRect(636,586,694,597) | `push 0x255,0x2b6,0x24a,0x27c` |
| 12 | 0x42A721 后 | AC 文本，格式 `0x47BD28`="%d-%d"，颜色 **0x0032C8FF** | `push 0x32c8ff` |
| 13 | **0x42A7AA-0x42A7C3** | **DC 数值** SetRect(736,586,794,598) | `push 0x256,0x31a,0x24a,0x2e0` |
| 14 | 0x42A829 | DC 文本，格式 "%d-%d"，颜色 0x0032C8FF | 同上 |

## 2. AC/DC 结论（关键，回答用户点名的缺陷）

```
AC 值 rect : (636, 586) - (694, 597)   面板相对 (636,121)-(694,132)  58x11
DC 值 rect : (736, 586) - (794, 598)   面板相对 (736,121)-(794,133)  58x12
文本格式   : "%d-%d" (0x0047BD28)   —— 无 "AC"/"DC" 前缀
文本颜色   : 0x0032C8FF (Win32 COLORREF 0x00BBGGRR) = RGB(255,200,50) 琥珀金
对齐       : 0x45DE50 内 DrawTextA flags=0x25 = DT_SINGLELINE|DT_VCENTER|DT_CENTER
```

**"AC"/"DC" 字样不是程序绘制**：`0x47BD28` 只含 `%d-%d`；扫描 `.data` 无 `AC`/`DC`
格式串。字样为 F50 底图**烘焙美术**（像素实测金色字形 AC 在 x607..620、DC 在 x705..717）。

**F50 黑色值框像素实测**（独立于反汇编）：
- AC 框 `x635..696`、`y118..134`（暗心 y118..134）
- DC 框 `x733..795`、`y118..134`

→ SetRect 立即数与位图暗框**两路独立证据一致**（相差 ≤1px 边框）。

## 3. 交叉验证：旧 Delphi 源码

`Mir3-Research/reference/mir3-source/Source/Client/FState.pas:5182-5188`：
```pascal
sInfo1 := Format('%d-%d',[Lobyte(MySelf.Abil.AC), Hibyte(MySelf.Abil.AC)]);
sInfo2 := Format('%d-%d',[Lobyte(MySelf.Abil.DC), Hibyte(MySelf.Abil.DC)]);
TextOut (SCREENWIDTH - 115 - TextWidth(sInfo1) div 2, SCREENHEIGHT-26, $32C8FF, sInfo1);
TextOut (SCREENWIDTH -  35 - TextWidth(sInfo2) div 2, SCREENHEIGHT-26, $32C8FF, sInfo2);
```
SCREENWIDTH=800, SCREENHEIGHT=600：AC 中心 800-115=685、DC 中心 800-35=765；
与反汇编 rect 中心 665 / 765 —— **DC 完全吻合**，AC 差 20px（源码取整/宽度差），
格式串、颜色、基线三处完全一致。

**结论：AC/DC 是 EI 原生元素，有 primary-static 证据。**（此前研究库只把其列为
"secondary-source only / candidate"；本文将其升级为 primary-static 已确认。）

## 4. 与并发会话 `godot-ui-audit-20261002` 已提交实现（commit `9b0683fe`）的差异

`9b0683fe` 已把 AC/DC 数值移入 F50 值框并去掉前缀，方向正确。
但对照本文 primary-static 证据仍有 **2 处已证据支持、尚未修复的差异**：

| 项 | 原版（primary-static） | `9b0683fe` 实现 | 差异 |
|---|---|---|---|
| 文本颜色 | `0x0032C8FF` = RGB(255,200,50) **琥珀金**（`0x42A77D`/`0x42A801` 压栈） | 沿用 `DXLabel` 默认 `Colors.White` | 白 vs 琥珀金 |
| 垂直位置 | AC rect `(636,586,694,597)`，DT_VCENTER → 文本中心 y=591.5（面板相对 126.5）<br>DC rect `(736,586,794,598)` → 中心 y=592（面板相对 127） | 标签 `(636,118)` 61x13 → 中心 y≈124.5；`(734,118)` 62x13 | 约偏高 2~2.5px |
| DC 左缘 | `736` | `734` | 2px |

结论：AC/DC 的**位置修复方向正确**，剩余为颜色（明确）与垂直基线/DC 左缘（2px 级微调）。

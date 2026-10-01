# EI 技能书页签高亮修复（2026-10-02）

回归提交 `fd81d6a7` 关掉了技能书左页 8 个学派页签的按帧绘制，红色选中态整层消失。
本文件记录根因证据、修复与验收。证据图见 `docs/evidence/ei-skillbook-tabs/`。

## 1. 现象

Ctrl+E / E 打开技能书（EI legacy UI）左页：8 个分类页签由**红色美术**变成 F400 烘焙的
**蓝色基础态**。交接文档 `docs/pending/EI_SKILLBOOK_TAB_HIGHLIGHT_HANDOFF_2026-10-01.md` §1-§3
已定位。

## 2. 判定方法的纠正（原「真机隐藏法」废止）

`fd81d6a7` 的判定依据是「给按钮 `Modulate=alpha0` 后截图，看不出差别 → 美术已烘焙」。
该方法**不成立**：它无法区分「烘焙美术与控件帧同源」和「两份不同美术叠加」。

本次改用两条独立证据：

### 2.1 素材解码（wilsdk.py 读 GameInter.wil）

| 帧 | 尺寸 | 不透明像素 | 偏红像素 | 偏蓝像素 |
|---|---|---|---|---|
| 450..465 | 44x36 / 48x36 | 1398..1542 | 1248..1347 | **0** |

8 个页签的 16 帧（450/451 … 464/465）**全部是红色美术，无一帧为蓝**。
`Mir3-Research/Tools/common/wilsdk.py` 解码，独立于端口实现。

### 2.2 原版每帧都重绘这 8 个控件（Mir3.exe 反汇编，只读）

`0x00439500`（技能书 paint）末尾：

```
0x00439667  mov  ebx, 8
0x0043966c  mov  edx, [edi]        ; edi = this+0x2F4，stride 0xB4
0x00439670  call dword ptr [edx+4] ; vtable[+4] = 控件绘制
0x00439673  add  edi, 0xB4
0x00439679  dec  ebx
0x0043967a  jne  0x43966c
```

同函数前段对 3 个头部控件（`this+0xD8` 起）同样 `mov ebx,3` 循环。
`0x00417640`（控件绘制）在 `[esi+0x24]==1 && [esi+0x25]==0` 时读 `[esi+0x20]`（= 构造 arg2），
**只有该值为 -1 才走 fallback**。原版把普通帧放在 arg2，所以 `Index` 必须保持帧号；
`Modulate=alpha0` 更是纯粹让按钮不可见。

### 2.3 三处控件各自的真机对比（不是无脑回退）

| 控件 | 帧号 | 判定 | 依据 |
|---|---|---|---|
| 8 个页签 | 450..465 | **回退** | 解码帧全红；原版逐帧重绘；蓝=烘焙基础态 |
| `_tabPrevious`/`_tabNext` | 410/411、412/413 | **回退** | F410..413 是金色箭头，与 F400 烘焙箭头为两套美术 |
| `_legacyAuxControl` | 440/441 | **回退** | F440/441 为小叉；同 2.2 的 arg2 语义 |

注意：`_closeButton`（161/162，`Visible=false`）**未改**——它本就被隐藏，不属于本次回归。

## 3. 代码修复

`GodotClient/Controls/MagicDialog.cs`：

- `BuildLegacySchoolButtons()`：删除构造后追加的 `Index=-1; HoverIndex=-1; Modulate=alpha0`，
  恢复构造器里的 `Index=entry.frame` / `HoverIndex=entry.frame+1`；保留 `FixedSize=true`
  （否则 `DXImageControl.Index` setter 会把 `Size` 重算成 0，点击区失效）。
- `ApplyLegacyEiLayout()`：`_legacyAuxControl` 由 `Index=-1/HoverIndex=-1` 改为 `Index=440/HoverIndex=441`。
- `ConfigureLegacyPageControls()`：`_tabPrevious`/`_tabNext` 恢复 `Index/HoverIndex`，删除 `Modulate` 抑制。
- `AuditLegacyEiLayout()`：`tabsMatch`/`navigationMatch`/`_legacyAuxControl` 三处判据改回帧号断言
  （原判据把回归行为写成了期望值，所以 `--legacy-audit` 仍 PASS，缺陷没被拦住）。

## 4. 验收证据

1. `dotnet build GodotClient/ZirconClient.csproj` → **0 error**。
2. `dotnet build ServerCore/ServerCore.csproj -o Debug/ServerCore` → **0 error**。
3. `godot-mono --path GodotClient res://Scenes/LegacyHudLayoutLab.tscn -- --legacy-audit` →
   `magic=True`，`tabIdx=[450,452,454,456,458,460,462,464]`，`categoryPositions=True`，`nav=True`。
4. 真机（本地 ServerCore + `--legacy-ui` 完整登录 TestHero，Ctrl+E 打开技能书）截图：
   `docs/evidence/ei-skillbook-tabs/skillbook-ingame-after-fix-2026-10-02.png`
   —— 左页 8 个页签为**红色**；翻页箭头与右下关闭叉均正常绘制。
5. 四联对照图 `skillbook-tab-highlight-fix-2026-10-02.png`：
   F400 烘焙（蓝）/ F400+控件帧（红）/ 真机修复后（红）。

### 4.1 关于交接文档引用的 `24-skillbook-rightpage-name-match.png`

该图**不是**红色参考。它拍摄于 2026-10-01 15:18，**晚于** 14:43 的回归提交 `fd81d6a7`，
画面正是回归后的蓝色页签。校验方法（在已知的 `21-magic-tabs-before-after.png` 上标定过：
左半判 RED、右半判 BLUE，均正确）：

```
ev24 左列 vs 「F400+红色帧」 预测图： meanAbsDiff = 17.96
ev24 左列 vs 「F400 烘焙」     预测图： meanAbsDiff =  9.73   → ev24 = 蓝
```

故本次验收以 §2 的素材解码 + 反汇编 + 修复后真机截图为据，不引用 ev24 作为红色基准。

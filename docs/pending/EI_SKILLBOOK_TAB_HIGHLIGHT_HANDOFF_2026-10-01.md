# 交接：EI 技能书左侧分类页签高亮丢失（2026-10-01）

> 状态：**已定位到回归提交与判据错误，修复方案已给出但尚未落地**（本次会话按用户要求停在交接点）。
> 下一步动作见 §5，预计 3 处代码改动 + 1 处审计判据改动。
> 反汇编目标：`/home/tetsuya/mir2ei.before-path-fix-20260927-2330/Mir3.exe`
> （EI 3.0 原版 524288 字节，ImageBase 0x400000；**82 机上**该路径存在，本机没有该 EXE）。

---

## 1. 现象（用户报告）

技能书（EI legacy UI，Ctrl+E）左页：**以前左侧分类页签是有高亮的（红色选中态），现在没有了**。

对照证据（`Mir3-Research/docs/evidence/godot-runtime-acceptance-2026-09-30/`）：

| 文件 | 内容 | 页签外观 |
|---|---|---|
| `21-magic-tabs-before-after.png` | 左=BEFORE(port drew frames) / 右=AFTER(baked only) | BEFORE **红**，AFTER **蓝** |
| `24-skillbook-rightpage-name-match.png` | 2026-10-01 15:19 联机验收 | 页签**红**（=修复前状态） |
| 用户本次截图 | 2026-10-01 22:4x 实机 | 页签**蓝**，左页无红色高亮 |

用户所说"页面高亮"= **红色分类页签美术**。蓝的那份是 F400 里烘焙的另一套（基础/未选中）美术。

---

## 2. 回归提交

**`fd81d6a7`** `fix(ei技能书): 8 个学派页签与 3 个导航钮不再叠画帧（美术已烘焙进 F400）`（2026-10-01 14:43）

该提交的判定方法写的是「真机隐藏法」：临时给按钮 `Modulate=alpha0` 后截图，认为「隐藏前后完全一致 → 美术已烘焙在 F400」。改动：

```csharp
// GodotClient/Controls/MagicDialog.cs  BuildLegacySchoolButtons()
button.Index = -1;
button.HoverIndex = -1;
button.Modulate = new Color(1, 1, 1, 0);
```

同一提交还改了三处审计判据（`AuditLegacyEiLayout`），把「页签/导航钮 Index 应为帧号」改成「应为 -1」，**把回归行为写成了断言**，所以 `--legacy-audit` 仍然 PASS，缺陷没被拦住。

---

## 3. 为什么该判定是错的（本次会话新增证据）

### 3.1 解码帧与 F400 烘焙美术是两套不同颜色

用 `Mir3-Research/Tools/common/wilsdk.py` 解码 `~/mir2ei/LegacyEI/Data/GameInter.wil`：

```
帧号  不透明像素  偏红像素  偏蓝像素  判定
450      1449      1342        0     RED
451      1441      1347        0     RED
452      1449      1300        0     RED
…（450/451 … 464/465 共 16 帧，全部 RED，无一帧是蓝色）
```

即 `0x1C2/0x1C3 … 0x1D0/0x1D1`（450..465）**全是红色美术**。

F400 在页签位置（F400 坐标 x≈32、y≈84 起，步长 35）烘焙的那一套是**蓝色**。
两套美术**同时存在于素材里**，不是同一份。

### 3.2 原版每帧重绘都会画这 8 个页签控件

`0x00439500`（技能书 paint，F547 已记录）末尾：

```
0x00439667  mov  ebx, 8
0x0043966c  mov  edx, [edi]        ; edi = this+0x2F4 起，stride 0xB4
0x0043966e  mov  ecx, edi
0x00439670  call dword ptr [edx+4] ; vtable[+4] = 控件绘制
0x00439673  add  edi, 0xB4
0x00439679  dec  ebx
0x0043967a  jne  0x43966c
```

同一函数前段对 3 个头部控件（`this+0xD8` 起）也做了同样的事（`0x0043957f` 的 `mov ebx,3` 循环）。
**原版确实逐帧绘制这些控件**，所以「美术已烘焙 → 端口不该画」的前提不成立。

页签构造在 `0x00439300` 一带，每个控件走 `0x00417550`（按钮构造），实参形如：

```
0x00439314  push 1          ; arg?
0x00439316  lea  edx,[ebx+0x15]   ; y
0x00439319  push 0x47c330   ; 分类名字符串
0x0043931e  lea  eax,[ebp+5]      ; x
0x00439323  push 0x1c3      ; 帧号+1（悬停/选中）
0x00439328  push 0x1c2      ; 帧号（450）
0x0043932d  push edi        ; 库
```

`0x0043933c-0x00439340` 的 `push 0 / push -1` 对应对应 arg8=-1、arg9=0，
**但它们不是"不要画"**：`0x00417640`（控件绘制）在 `[esi+0x24]==1` 且 `[esi+0x25]==0` 时
读 `[esi+0x20]`（普通帧）→ 若为 -1 才走 fallback；原版把普通帧放在 **arg2=0x1C2**，
所以 `Index` 必须保持帧号，`Modulate=alpha0` 更是纯粹让按钮不可见。

### 3.3 结论

- 蓝色 = F400 烘焙的**基础态**美术；
- 红色 = 控件按帧号绘制的**当前态/选中态**美术；
- `fd81d6a7` 关掉控件绘制后，红色整层消失 → 用户看到的"高亮没了"。

---

## 4. 受影响的其它改动（同一提交，需一并复核）

| 控件 | 位置 | 提交前 | 提交后 | 备注 |
|---|---|---|---|---|
| 8 个学派页签 | `(5,21)`…`(2,266)` | `Index=450..464`, `HoverIndex=+1` | `Index=-1`, `HoverIndex=-1`, `Modulate=A0` | **本缺陷根因** |
| `_tabPrevious` / `_tabNext` | `(61,303)` / `(366,303)` | `410/411`、`412/413` | `Index=-1`+`Modulate=A0` | 翻页箭头；同法复核 |
| `_legacyAuxControl` | `(399,340)` | `440/441` | `Index=-1`+`Modulate=A0` | 关闭叉；同法复核 |
| `_closeButton` | `(418,348)` | — | 本就被 `Visible=false` 隐藏 | 不受影响 |

`b23d3399` / `a891c010` 对其它 11 个窗口做了同类改动，**本文件只报技能书**；
其余窗口是否也丢高亮，需要按同一方法（解码帧色 vs 烘焙色）逐个复核，不要默认它们是对的。

---

## 5. 建议修复（尚未落地）

### 5.1 代码（`GodotClient/Controls/MagicDialog.cs`）

`BuildLegacySchoolButtons()`（当前约 367-390 行）——去掉这三行，让页签恢复按帧绘制：

```diff
             button.MouseClick += (_, _) => SelectSchool(school);
             AddControl(button);
-            // 原版 8 个页签实参同为 arg8=-1、arg9=0（0x4392A5/0x4392D4 一带）；
-            // 真机隐藏法验证：隐藏后页签**完全一致**（美术已烘焙进 F400）→ 端口不叠画。
-            button.Index = -1;
-            button.HoverIndex = -1;
-            button.Modulate = new Color(1, 1, 1, 0);
             _schoolButtons[school] = button;
```

即恢复 `Index = entry.frame` / `HoverIndex = entry.frame + 1`（构造器里已有），
`FixedSize = true` 必须**保留**（否则 `Index` setter 会把 Size 重算成 0，点击区失效）。

`_tabPrevious` / `_tabNext` / `_legacyAuxControl` 三处同理按需回退（先各自做一次真机对比再决定）。

### 5.2 审计判据（`AuditLegacyEiLayout`）

当前判据把回归写成了期望值，必须同步改回：

```diff
         bool tabsMatch = expectedTabs.All(x => _schoolButtons.TryGetValue(x.school, out var button)
             && button.Location == x.location
-            && button.Index == -1 && button.HoverIndex == -1
+            && button.Index == x.frame && button.HoverIndex == x.frame + 1
             && button.Size == MirSkin.GetSize(LibraryFile.GameInter, x.frame));
```

`navigationMatch`、`_legacyAuxControl.Index` 两处同理。

### 5.3 验收

1. `dotnet build GodotClient/ZirconClient.csproj`（仓库根目录）；
2. `bash login_game.sh remote 192.168.3.82 legacy test` 真机进游戏 → Ctrl+E；
3. **截图对比**：左页 8 个页签应为红色，与
   `Mir3-Research/docs/evidence/godot-runtime-acceptance-2026-09-30/24-skillbook-rightpage-name-match.png`
   一致；蓝色 F400 烘焙层应被红色帧覆盖。

---

## 6. 判定方法教训（写进后续审计规范）

「真机隐藏法」（临时 `Modulate=alpha0` 后截图，若"看不出差别"就判定美术已烘焙）**不成立**：

- 本次 8 个页签隐藏前后**确实有明显差别**（红→蓝），但当时判成"完全一致"；
- 该方法无法区分「烘焙美术与控件帧是同一份」还是「两份不同的美术叠在一起」；
- 正确做法：**解码素材本身**（`wilsdk.py` 读 WIL 帧 + 颜色统计）与 **F400 同位置像素** 做
  `ImageChops.difference` 比对，用像素证据判定，而不是靠肉眼。

---

## 7. 环境状态（本次会话产生的变更，供服务器侧接续）

### 7.1 本机（macOS，`macbook-m1-max`）

- 仓库：`/Users/tetsuya/Development/Zircon`，分支 `master` @ `e4dcd484`（= `origin/master`）；
  `Mir3-Research` 分支 `ei-ui-audit-2026-09-24` @ `65fb20b2`。
- `brew install syncthing rsync` 已装：
  - `syncthing v2.1.5`，`brew services` 标签 `sh.brew.syncthing`（开机自启，已在跑）；
  - `rsync 3.5.1`（`/opt/homebrew/bin/rsync` 现在优先于 `/usr/bin` 的 openrsync 2.6.9）。
    **这是 `login_game.sh` remote 模式能跑通的前提**——openrsync 不认 `--info=stats2`。
- Syncthing 配置目录：`~/.local/state/syncthing` → 软链到
  `~/Library/Application Support/Syncthing`（`login_game.sh` 读前者，brew 默认用后者）。
- 本机设备 ID：`WY3EBPA-U7PHZRG-WYKM3WO-JZWO7SK-YZNYPXS-GQKVNXR-EOYWCVH-A64WTQD`
- 文件夹（均 `receiveonly`，对端 `debian` = `A43UXGE-Q2NW3JR-AWKZZNG-LAKZGYW-VGBLPA2-TYB6MIA-S5WHR75-VSKTKAK`）：
  - `mir2ei-client` → `/Users/tetsuya/mir2ei`（含 `LegacyEI/`、`Data/`、`Map/`、`Sound/`…）
  - `mir2ei-webdata` → `/Users/tetsuya/mir2ei-webdata`
- `~/mir2ei/WebData` 已由实体目录改为**软链** → `../mir2ei-webdata`（与 82 一致）；
  原 2.3G / 83490 文件实体目录移到 `/tmp/WebData-old-mir2ei`，**待用户确认后可删**。
  （`Debug/Client` 本身是 → `/Users/tetsuya/mir2ei` 的软链，`Debug/Client/WebData` 随之指向同一目标。）

### 7.2 服务器 82（`debian`，192.168.3.82）

- Zircon 工作树 `/home/tetsuya/development/zircon`：已 `git fetch` + `ff` 到 `e4dcd484`。
- Syncthing（`systemctl --user`，配置 `~/.local/state/syncthing`）已加入本机设备 ID，
  并加入 `mir2ei-client` / `mir2ei-webdata` 两个文件夹；已 restart，与本机连接正常（LAN 192.168.3.82:22000）。
- 原版 EXE 路径：`/home/tetsuya/mir2ei.before-path-fix-20260927-2330/Mir3.exe`。

### 7.3 remote 模式实测结论（2026-10-01 22:4x，已跑通）

`bash login_game.sh remote 192.168.3.82 legacy test` 全流程 OK：

```
同步远程代码 → 本机已快进
[1/4] 清理进程：无客户端；服务器已在运行(7000)，保留不重启
[1.5/4] Mir3-Research rsync（6 文件）+ Syncthing 等待 → "mir2ei-client 游戏资源已同步"
[2/4] 本机只建客户端；[3/4] 82 上重建并重启 ServerCore，PID 3543570
SSH 转发 127.0.0.1:7001 → 192.168.3.82:7000
[4/4] Godot 客户端启动，自动登录 test@test.com → StartGame 成功 → 进入 Numa Village(地图4)
```

遗留：`mir2ei-webdata` 首次全量同步约 4.4 万文件 / 1.5-2.1 GB，后台持续，不阻塞启动。

---

## 8. 未决事项

1. **技能书页签高亮修复**（§5）——主任务，尚未动代码。
2. `/tmp/WebData-old-mir2ei`（2.3G）是否删除；本机磁盘当时 96% 占用。
3. 本机 `Mir3-Research` 仓库里 `master` 与 `ei-ui-audit-2026-09-24` 已分叉
   （master 多 4 个提交：`pe_dis.py`、`.gitignore /.fresh/`、`CharacterEditor setexp`、`services.sh` macOS 修复），
   是否合并/推送由用户决定。
4. 本机 `Zircon` 里 2026-09-25 的旧 stash（7 个 GodotClient 文件，与当前 master 冲突）
   已按"保全优先"推到归档分支，见仓库分支列表。

---

## 9. 交接时的工作区状态（已推送、已干净）

### 9.1 本机 Zircon（`/Users/tetsuya/Development/Zircon`）

```
* master  c336fb6b  [origin/master]  ← 已推送，工作区干净
  archive/codex-sync-2026-09-25  ee113ff7  ← 已推送
```

- `c336fb6b` = 本交接文档（`docs/pending/EI_SKILLBOOK_TAB_HIGHLIGHT_HANDOFF_2026-10-01.md`）。
- 2026-09-25 的旧 stash（7 个 GodotClient 文件：ChatTextBox / CombatController / GameScene /
  MapObjectNode / MirEffectNode / PlayerRenderer / RenderOrder）**内容未丢**，
  已转成 `archive/codex-sync-2026-09-25` 分支并推送；stash 列表已清空。
  该分支与当前 master 有冲突（`--3way` 也不干净），**仅供取证**，不要直接 merge。
- ⚠️ GitHub 提示该仓库已改名：`iamcheyan/Zircon` → **`iamcheyan/Zircon-Godot`**
  （旧地址仍可推送，remote 暂未改）。

### 9.2 本机 Mir3-Research（`/Users/tetsuya/Development/Mir3-Research`）

```
* ei-ui-audit-2026-09-24  65fb20b2  [origin/...]  ← 工作区干净
  archive/mirror-2026-10-01    a2cd0d26  ← 已推送
  archive/mac-local-2026-09-27 2222a19e  ← 已推送
  master                      3ae3c60b
```

- `archive/mirror-2026-10-01`：交接前本机镜像里 D(82) 工作树的未提交改动
  （`Tools/NpcMover/write_alignment_reports.py`、`Tools/SystemDbProbe/Program.cs`、
  `Tools/maps/mapedit/map_links_v2.json`、两份 NPC 对齐报告/证据、
  `docs/research/map-editor-unknown-entities/UnknownEntityPlacements.json`）。
  **D 上仍是未提交状态**，本机为干净镜像；`login_game.sh remote` 会重新同步过来。
- `archive/mac-local-2026-09-27`：本机独有的 `Tools/common/pe_dis.py` `find_xref` 修复
  （修掉"一次性 disasm 遇首个坏字节即停"的漏扫 bug）+ 未跟踪的 `Tools/CharCreator/`
  （走游戏协议建角色/列角色/删角色的独立小工具）。
- 本机 `.fresh/`（Fresh 编辑器窗口状态）仍被 `git clean -e .fresh` 保留，非仓库内容。

### 9.3 服务器 82 接续步骤

```bash
# 82 上
cd /home/tetsuya/development/zircon && git fetch origin && git merge --ff-only origin/master
# 或直接拉改名后的地址：git remote set-url origin https://github.com/iamcheyan/Zircon-Godot.git
```

然后按 §5 修 `MagicDialog.cs`，`dotnet build ServerCore/ServerCore.csproj -o Debug/ServerCore`
（82 侧服务端）与 `dotnet build GodotClient/ZirconClient.csproj`（客户端，仓库根执行），
再按 §5.3 真机验收。

> 注：本机 remote 模式已实测可用（§7.3），所以也可以在 82 上只改代码、由本机
> `bash login_game.sh remote 192.168.3.82 legacy test` 拉起客户端做对比截图。

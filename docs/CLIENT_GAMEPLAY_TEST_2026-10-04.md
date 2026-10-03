# 客户端游戏内交互测试记录（2026-10-04）

> 目标：登录进游戏后清理背包，然后做各类交互测试（走动/跑动/技能/丢弃/捡起等），
> 发现问题就修，最后给出记录。
>
> 环境：debian(82) 本机 `ServerCore`(7000) + Xvfb `:123` + `godot-mono`
> legacy UI（`--legacy-ui --legacy-hud`），测试账号 `test@test.com / TestHero`。
> 输入用 `xdotool`（XTEST 真实事件），截图用 `ffmpeg x11grab`。

---

## 一、结论

| 项目 | 结果 |
|---|---|
| 背包清理 | ✅ 358 件 / 负重 4520 → **2 件 / 997** |
| 走动 | ✅ 正常 |
| 跑动 | ✅ 正常（右键按住） |
| 技能 | ✅ 正常（F1–F3 实测，其余槽位未学） |
| 丢弃 | ✅ 正常（数量框 → 落地） |
| 捡起 | ✅ 正常（物品 + 金币） |
| 装备 / 卸下 | ✅ 正常（拖拽 + 右键） |
| 物品移动 | ✅ 正常 |
| 聊天 | ✅ 正常（收发） |
| 窗口开关 | ✅ Q/W/E/N/S/Z/V/P/J/L/H/A 均正常 |
| 普通攻击 | ✅ 正常（选中目标 + `C.Attack` + 血条掉血） |
| GM 命令 | ✅ 可用（`@move` / `@monster`，经聊天框输入） |
| **发现并修复的 bug** | **1 个（严重）：legacy 背包丢弃后格子永久锁死** |
| 发现但未修复 | 服务端/客户端 System.db 地图表不一致（见第四节） |
| 其它发现 | `AGENTS.md` 的 `@spawn` 命令名有误，实为 `@monster`（见第五节） |

---

## 二、测试方法与实测证据

### 1. 走动（左键点地图）

左下角 `比奇县 [x,y]` 是**玩家坐标**（不随鼠标移动变化，已用移动鼠标对照确认）。

```
点 (520,250)  →  [81,121] → [85,118]
```

### 2. 跑动（按住右键）

关键代码：`IsRunInputHeld() => _runningTestRightHeld || _autoRun
|| Input.IsMouseButtonPressed(MouseButton.Right)`
—— **跑动是按住右键**，不是 Shift（Shift 只影响格内分堆）。

```
右键按住 3s，点 (400,430) 南向： [98,127] → [98,138]   = 11 格 / 3s ≈ 3.7 格/秒
```

对照：单击左键一次约 4 格（寻路到点）；走路速度约 1.7 格/秒。
日志旁证：首段先走一格并打印
`[RunDebug] BLOCKED canRun=False cooldownOk=True bag=1086/2516 wear=0/385`，
之后 `canRun` 置真转为跑动。

> 第一次跑动测试（东向）无位移：该方向被树/石挡住。换南向开阔草地后正常
> —— 属地形阻挡，非缺陷。

### 3. 技能（F1–F8）

```
F1 → [Magic] 发包 Fire Ball            Magic=FireBall            Set=1 Slot=1 目标=1057 钉耙猫
F2 → [Magic] 发包 Adamantine Fire Ball Magic=AdamantineFireBall  Set=1 Slot=2 目标=1057
F3 → [Magic] 发包 Scorched Earth       Magic=ScortchedEarth      Set=1 Slot=3 目标=0
     [Magic] OnObjectMagic type=FireBall cast=True targets=1
```

F4–F8 无输出：该角色只学了 3 个火系技能（技能书显示火系 6 项，另 3 项未学）。
火球术特效在截图里可见（施法动作 + 火焰轨迹）。

### 4. 丢弃（选中 → 点地面 → 数量框 → 确定）

```
点背包格(0,0)  → [CellDbg] _GuiInput grid=Inventory idx=0 item=Great Axe
点地面         → ItemAmountDialog visible=True pos=(291,227) size=(218,146)
按 Enter/确定  → 物品落地，负重 1086 → 1046（-40）
```

地面出现掉落物与 `炼狱` 名称标签（Alt 切换地面物品名显示）。

> 排查插曲：数量框打开后 `_amount.GrabFocus()` 抢走键盘焦点，导致
> **Ctrl+F12 调试键与 xdotool 点击都失效**，一度误判"对话框没渲染"。
> 用 ffmpeg 直接抓屏后确认对话框渲染正常。

### 5. 捡起（点地面物品）

```
点地面掉落物 → 负重 1046 → 1086，物品回到背包并带 "N"（新物品）角标
             → 地面 `炼狱` 标签消失
```

金币拾取同样正常（负重 +1）。

### 6. 装备 / 卸下

装备格命中映射（`[CellDbg] grid=Equipment idx=0 slot=N`）：

| 位置 | 槽位 | 含义 |
|---|---|---|
| (115,120) / (195,120) | 0 | Weapon（已有 `Fury`） |
| (75,75) | 1 | Armour |
| (195,75) | 3 | Torch |
| (95,75) | 4 | Necklace |
| (60,190) | 5 | BraceletL |
| (60,250) | 7 | RingL |

- **拖拽装备**：选背包武器 → 点武器槽 → 生效（角色手持裁决之杖）。
- **卸下**：点武器槽拿起 → 点背包空格 → 角色变空手，武器回背包（负重 1087 → 1174）。
- **右键装备**：背包内右键武器 → 负重 1087 → 997（-90 = 该武器重量），角色手持。

### 7. 聊天

点击聊天输入框后 `xdotool type` 可输入（**必须先点击输入框取得焦点**，
直接按 Enter 后打字无效 —— 见"未覆盖"一节）：

```
输入 "hello" → 回车
聊天区出现： TestHero: hello
          当前在线人数:1, 观察者在线人数:0
```

### 8. 窗口开关

| 键 | 窗口 |
|---|---|
| Q | 背包（legacy 下 Q=背包，W=状态） |
| W | 状态/装备窗 |
| E | 技能书 |
| N | 选项窗 |
| S | 坐骑窗（EI 语义） |
| Z | 腰带 |
| V | 小地图 |
| P | 组队 |
| J | 任务 |
| L | 任务追踪 |
| H | 帮助 |
| A | 自动喝药 |

`B` 在 legacy 下按设计切换技能条（非大地图）。

### 9. 普通攻击（点击怪物）

用 `@monster Pig 3` 在身前刷出 3 只怪（见第五节：实际命令名是 `@monster`），
再点击怪物：

```
[Combat] 选中目标: 蛤蟆 ObjectID=9572
[Combat] enqueue C.Attack action=Attack magic=None direction=DownRight
```

画面里角色进入攻击动作、怪物显示血条并掉血 —— 选中 / 追击 / 攻击链路正常。

---

## 三、发现并修复的 bug

### legacy 背包丢弃后格子永久锁死

**现象**：在 legacy 背包里丢弃一件物品后，**该格再也无法移动 / 装备 / 丢弃**
（点击无反应，连再次丢弃都被 `CanBeginItemDrop` 拒绝）。

**根因**：legacy 背包按 footprint first-fit 摆放，`DXItemCell.Slot` 会经
`DXItemGrid.ResolveOperationSlot` 解析成**服务端槽号**，而 `Cells[]` 是按
**格索引**排列的，两者**不相等**（实测：槽号 6 的记录显示在格 0）。

- 丢弃时 `source.Locked = true` 锁的是**显示格**（格 0）；
- 服务端回包 `OnItemDelete` 调 `UnlockCell(Inventory, 6)` → `cells[6]`
  → 解锁的是**另一格**，真正被锁的格永远保持 `Locked`。

**修复**（`36e4f24e`）：

1. `DXItemGrid.ResolveSlotCell(slot)` —— 服务端槽号 → 显示该记录的格索引
   （非 legacy 模式原样返回，行为不变）；
2. `UnlockCell` 先用它换算再索引 `Cells[]`；
3. 新增 `GameScene.RefreshInventorySlot(slot)`，替换 `NPCSocketPanels` 里三处
   直接用槽号索引 `InventoryCells[]` 的刷新（同类错误：刷新到另一格）。

**验证**（实机）：

| | 修复前 | 修复后 |
|---|---|---|
| 丢弃 + 捡回后该格 | `locked=True`，`MoveItem` 不被调用 | `locked=False`，`MoveItem` 正常 |
| 再次丢弃同一格 | 被拒绝 | 成功（负重 1174 → 1087） |
| 装备/移动该格物品 | 无反应 | 正常 |

`GroundLootChecks` 全部 PASS，客户端构建 0 错误。

---

## 四、发现但**未修复**：服务端 / 客户端 System.db 地图表不一致

测试 GM 传送时发现：**部分地图客户端加载不了，角色会停在旧地图上但坐标已被改写**
（客户端与服务端位置失联）。

### 现象

```
@move D203 → [Game] 地图切换: MapIndex=138          ✅ 正常切图
@move D201 → [Game] 地图切换: MapIndex=136
             [Game] 找不到地图: MapIndex=136        ❌ 停在旧图，坐标被改成 [22,34]
@move D101 → [Game] 找不到地图: MapIndex=26          ❌ 同上
```

`LoadPlayerMap()` 用 `Globals.MapInfoList.Binding.FirstOrDefault(m => m.Index == _playerMapIndex)`
查表；查不到只打印一行错误就 `return`，**不切图、不回滚坐标**，于是客户端停在
旧地图上渲染新坐标 —— 玩家与服务端失联。

### 根因：两个 System.db 不同步

| 库 | 路径 | 大小 | 有 `D201` |
|---|---|---|---|
| 服务端 | `Debug/ServerCore/Database/System.db` | 5.75 MB | ✅ |
| 客户端 | `Debug/Client/Data/System.db` | 11.2 MB | ❌ |

客户端 `MapInfo` 共 627 条且索引有大量缺口（缺 10、11、15、21–27…136…）。
服务端 `MapInfo` 含客户端没有的条目，`MapIndex=136`(D201)、`26`(D101) 即属此类。

`mir2ei/ARCHIVED_ASSETS_MANIFEST.md` §5 也印证了这个顺序：
> 「数据库未登记死重地图（167 个文件）：磁盘 `Map/` 原存 794 张，而 `System.db`
> 登记的仅 627 张……已全部移入归档」

—— 即**客户端库被当作基准**剔掉了 167 张地图文件，而服务端库仍保留这些地图。

### 影响

- `AGENTS.md` 的「常用矿区传送（测试用）」里 `@move D201`、`@move D101` **不可用**；
  `@move D202` / `@move D203` 正常。
- 任何**服务端有、客户端没有**的地图（含传送门/任务目的地指向的图）都会让
  客户端卡在旧图。玩家会看到自己在错误的地图上移动。

### 为什么不在这里修

1. 正确修法是**同步两库**（`Tools/dbeditor/sync.sh` 的流程），属数据操作，
   不是代码改动；
2. `AGENTS.md` 明确要求「**服务端运行中绝不写 System.db**」，当前服务端在跑；
3. 同步会重写两库、影响用户既有数据，需在停服并备份后由用户确认执行。

**建议**：停服 → 用 dbeditor 同步双库（或按需把缺失地图补进客户端库）→
重启后复测 `@move D201` / `@move D101`。

### 附：客户端对「找不到地图」的容错（可另议）

当前实现只打日志不切图也不回滚坐标，导致静默失联。更稳的做法是保留原图与
原坐标，并把错误显式提示给玩家。这条属于**独立的小改动**，本次未动。

---

## 五、其它发现与未覆盖

### 1. `AGENTS.md` 的 `@spawn` 命令名有误

`@spawn GhostSorcerer 3` 被服务端拒绝：
```
Command @SPAWN does not exist.
```
实际命令名取自 `SpawnMonster.VALUE`，即 **`@monster <名字> [数量]`**
（`ServerLibrary/Envir/Commands/Command/Admin/SpawnMob.cs:10`）。
建议把 `AGENTS.md` 里的 `@spawn 怪物 数量` 改成 `@monster 怪物 [数量]`。

已实测确认：`@monster Pig 3` 在身前刷出 3 只猪（`@spawn` 则报不存在）。
怪物名须与服务端 `MonsterInfo.MonsterName` 完全一致（如 `Pig` / `Guard` /
`Chicken`），否则回 `Could not find monster: <名字>`。

### 2. 环境限制（非产品缺陷）

- **直接打字（不先点输入框）无效**：`xdotool type` 需要先点击输入框取得焦点。
- **Shift + 点击**：`keydown shift` 后 `click` 在 Godot 里未形成带 Shift 修饰的
  鼠标事件。因此 **Shift 快丢**（Shift+点地面 = 丢 1 个）只做了代码级核对：
  `GameScene.cs` 里 `ShiftPressed` → `CanBeginItemDrop` → `Count = 1` → 直接
  return 不弹数量框；服务端只拒绝 `Count <= 0`，`Count = 1` 天然合法。

### 3. 本次未测

NPC 对话、商店买卖、仓库存取、修理/镶嵌、行会、组队交互、坐骑（无马）。
（大地图 `B` 在 legacy 下按设计切换技能条，不是窗口。）

背包里剩余 2 件（蓝甲 + 一件红色物品）为有意保留的测试样本。

---

## 六、复现用命令

```bash
# 服务端
cd /home/tetsuya/development/Zircon/Debug/ServerCore && nohup dotnet ServerCore.dll &

# 客户端（Xvfb :123）
export DISPLAY=:123
Xvfb :123 -screen 0 1280x1024x24 -nolisten tcp &
cd /home/tetsuya/development/Zircon
MIR3_EI_ROOT=$HOME/mir2ei ZIRCON_LEGACY_UI_DATA_PATH=$HOME/mir2ei/LegacyEI/Data \
  godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 \
  --user test@test.com --pass test123 --char TestHero --window=800x600

# 输入（窗口 800x600 位于屏幕 129,152）
xdotool key --window <WID> --clearmodifiers q          # 背包
xdotool mousemove --window <WID> 558 58 && xdotool click 1
# 截图
ffmpeg -f x11grab -video_size 800x600 -i :123+129,152 -frames:v 1 -y out.png
# 调试：Ctrl+F12 → /tmp/game_screenshot.png + /tmp/ui_window_rects.json
```

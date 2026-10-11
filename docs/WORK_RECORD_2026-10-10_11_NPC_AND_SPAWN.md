# 工作记录：NPC 全量修复 + 地图刷怪对齐 Mud3（2026-10-10 ~ 2026-10-11）

> 本文档记录本轮两个任务的**完整过程**：① 全量检查并修复 230 个活动 NPC 的对话与功能；
> ② 检测全部地图刷怪信息、与原版 Mud3 对照、逐图实机核对并重建。
> 记录范围包含：调查方法、数据实证、代码/数据改动、验证手段与证据、缺口与取舍、踩坑与运维注意、复现步骤、提交记录。
>
> 阅读约定：所有数字都来自本次会话内**实际运行的命令输出**；凡属推断的地方都显式标注「推断」。

---

## 0. 总览

| 任务 | 起点（修复前） | 终点（修复后） | 证据 |
|---|---|---|---|
| NPC 对话与功能 | 230 个活动 NPC 中 **115 个 `EntryPage=null`（点击毫无反应）**、传送动作 11/22 地图引用为空、大量占位/英文文本、仓库类无储物入口 | **230/230 有入口**、4288 页 / 2093 按钮 / 855 条件 / 752 动作 / 932 商品行，结构校验 **0 error**；实机巡检 227/230 全绿 + 3 个人工复核通过；功能断言 **4/4** | `docs/NPC_FUNCTIONAL_REBUILD_2026-10-10.md`、`docs/NPC_AUDIT.md`、`docs/NPC_FUNC_AUDIT.md`、`docs/screenshots/npc_audit/` |
| 地图刷怪 | 刷怪条目 1007（含 39 条损坏）、城镇/副本整片无刷怪、怪物是 Mir2 遗留且数量 250-600 | **210 张图按 Mud3 重建**（489 区域 / 1770 条），终态 **2156 条 / 282 图 / 0 异常**；服务端真值逐图核对 **210/210 覆盖、206 完全一致**，其余 4 处已查明 | `docs/MAP_SPAWN_REBUILD_2026-10-11.md`、`docs/spawn_audit/`、`docs/screenshots/spawn_audit/` |

提交范围：`922ffe06..df11c12a`（19 个提交），代码 36 个文件、+6306 行；截图/数据归档 283 个文件。
推送：`origin/master`（fork `iamcheyan/Zircon`，远端提示已改名 `Zircon-Godot`）。

---

## 1. 环境与运行方式（本次实际使用的）

| 项 | 值 |
|---|---|
| 开发机 | 本机即 82（debian），仓库 `/home/tetsuya/development/zircon` |
| 服务端 | `cd Debug/ServerCore && dotnet ServerCore.dll`（cwd 必须是该目录；`Database/System.db`、`Database/Users.db` 都在其下） |
| 客户端 | `godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 --window --user test@test.com --pass test123 --char TestHero --legacy-ui --legacy-hud` |
| 无头图形 | `Xvfb :150 -screen 0 1920x1200x24` + `openbox`；截图 `scrot`；模拟输入 `xdotool` |
| 构建 | 服务端 `dotnet build ServerCore/ServerCore.csproj --no-restore -o Debug/ServerCore`；客户端 `dotnet build GodotClient/ZirconClient.csproj` |
| 账号 | `test@test.com / test123`（角色 `TestHero`），`Account.Admin = True`（`Users.db` 在 `Debug/ServerCore/Database/`，可用 `ClassicMagicFixer gm <DB目录> <账号>` 查询/授予） |
| 原版资料 | Mud3（EI 3.0 ORIGIN）：`Mir3-Research/reference/mir3-source/Mud3-Config/Envir3/`；怪物表 `local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/monster.dat` |

**写库纪律（每次写 `System.db` 都遵守）**：先停服 → 备份 → 写 → 校验 → 同步 4 处镜像（仓库根 /
`Debug/ServerCore/Database/` / `/home/tetsuya/mir2ei/Data/` / `/home/tetsuya/mir2ei/Database/`）并逐处 SHA256 比对 → 重启服务端。
本次所有写库工具都内置了这步同步。

---

## 2. 第一部分：NPC 全量检查与修复

### 2.1 调查：先量出问题，不靠猜

新增只读导出工具 **`ClassicMagicFixer npcgraph <DB目录> <out.json>`**（`Tools/ClassicMagicFixer/NpcGraphAudit.cs`），
把 `NPCInfo → EntryPage → Buttons/SuccessPage` 可达闭包、全部 `NPCPage/NPCButton/NPCAction/NPCCheck/NPCGood/NPCType/NPCValue`、
物品表、地图表导成 JSON，随后用 Python 做不变量审计。**修复前的实测事实**：

1. **115/230 个活动 NPC 的 `NPCInfo.EntryPage == null`**
   → 服务端 `PlayerObject.NPCCall()` → `NPCObject.NPCCall(ob, null)` 立即 `return`，
   **既不回 `S.NPCResponse` 也不关闭对话** → 玩家点击完全没反应。这是"点击没反应"的第一主因。
2. **22 个 Teleport 动作里 11 个 `MapParameter1 == null`**
   → `NPCObject.DoActions` 的 Teleport 分支遇空地图直接 `continue` → 点传送不动。
3. **文本内容缺失/占位**：公告牌页 `Say = "……"`；收购类页 `Goods=0 / Types=['Nothing']`；部分页保留英文页名。
4. **仓库类 NPC 数据层没有任何储物入口**：Zircon 的仓库窗口只有快捷键（`KeyBindAction.StorageWindow`），
   原版 Mud3 的 `<寄存/@storage>` 无从落地。

### 2.2 修复：把原版脚本编译成 Zircon 数据

**权威数据源**（都在 `Mir3-Research/reference/mir3-source/Mud3-Config/Envir3/`）：

| 文件 | 作用 |
|---|---|
| `Merchant.txt`（537 行） | NPC → 脚本文件的权威摆放表（按 **地图+坐标** 精确匹配） |
| `Market_Def/*.txt`（411 个） | NPC 逻辑脚本：`#IF/#SAY/#ACT/#CALL/#INCLUDE` + `[Goods]` 商品清单 |
| `Convert_Def/**` | 玩家可见中文正文：`[@Label] { 文本 <文字/@命令(参数)> }` |
| `QuestDiary/Teleport/moverootin.txt` | 六面神石目的地 → `mapmove 地图 x y` 坐标表 |
| `Mon_Def/*.gen`、`monster.dat` | （第二部分使用）刷怪表与怪物名真值 |

**编译流水线**（Python 编译 → IR JSON → C# 落库）：

```
Envir3 原版脚本 ──(tools/mud3_npc_compile.py)──> tools/npc_dialog_ir.json
                                                        │
                       (ClassicMagicFixer applynpcir) ──┴─> System.db（4 处镜像 + SHA256）
```

**语义映射表**（Mud3 → Zircon，实现在 `tools/mud3_npc_compile.py`，1272 行）：

| Mud3 | Zircon 落地 | 备注 |
|---|---|---|
| `[@Label]` + 多组 `#IF` | `NPCPage`，条件不满足依次落到下一组 | 与 Mud3 顺序 `#IF` 等价 |
| `checkpkpoint/checklevel/checkgold/checkjob/checkitem[!]` | `NPCCheck`（PKPoints/Level/Gold/Class/HasItem，支持 `!` 取反） | |
| 脚本旗标条件（`check [x]`、`Equal N0{..}`、`IsAdmin`…） | 视为**不满足**（新号默认状态）；整块都不可求值时回退显示该块原文 | 避免整棵树变空 |
| `#ACT goto @x` | 路由页（`Say` 空 + `SuccessPage`） | 服务端 `NPCCall` 会沿 SuccessPage 继续 |
| `#CALL [file] @label`、跨文件 `goto` | 跨文件编译并保留调用链（`goto` 可回落调用者文件） | 等价 GSP 全局标签/返回语义 |
| `mapmove <地图> <x> <y>` | `NPCAction.Teleport`（地图按 `MapInfo.FileName` 绑定） | |
| `take/give 金币`、`take/give 物品` | `TakeGold/GiveGold/TakeItem/GiveItem` | |
| 文本 `<文字/@cmd(args)>` | `Say` 内 `[文字:id]` + `NPCButton`（id 0 = 关闭） | **解析不出的链接一律从文本剔除**（无死链契约） |
| `@buy/@sell/@repair/@storage`、`@TelePortRootin(城,价,方位)` | `BuySell` / `Repair` / `Storage` 动作 / 收费传送页（Gold 检查 + TakeGold + Teleport） | 传送坐标取自 `moverootin.txt` |
| `[Goods]` 清单 | `NPCGood`（`Rate=1`，即沿用物品 DB 售价） | 脚本里的数字是**库存量/补货周期，不是价格**（实测确认） |
| `<$USERNAME>` 等文本宏 | `<1:勇士>` + `NPCPage.Values(Field/Name)` | 服务端 `GetValues` 下发真值，客户端按 `<ID:默认>` 替换 |
| 原版由**客户端**提供的买卖/修理/储物界面 | 按 NPC 职能（`tools/npc_audit_manifest.json` 的 `services`）补挂入口按钮 | 已提供的能力不重复注入 |

**关键实现细节**：

* 入口页选择：按 `main` → `main_0_0` → 含 "main" 的标签 → 文件顺序，且必须满足
  **`simulate(entry) != None`**——用「普通新号画像」（PK=0、等级 1、金币 0、无物品、旗标全假）
  模拟服务端 `CheckPage` 的判定，确保点开**一定看得到内容**（否则会落在空对话框上）。
* 死链清理：按钮指向「对任何玩家都只是空页」的页面时，按钮与正文里对应的 `[文字:id]` **一起摘掉**。
* **条件必须有落点**：`NPCCheck.FailPage` 为空时服务端会**静默 return**（点了没反应）；
  编译期把缺失/被剪枝的失败目标统一回落入口页，终态 855 个检查全部有落点。
* 六面神石：原版个别方位正文是空菜单（`SnakeVallyTele_2` 只有"六面神石"四个字），
  按该城原版传送文本补齐目的地菜单（记为派生行为）。
* 变体名（`多钩猫0`/`僵尸1`…）按去尾数字的基础名映射（Zircon 库内只有基础怪）。

**代码改动清单（第一部分）**：

| 文件 | 改动 |
|---|---|
| `tools/mud3_npc_compile.py`（新，1272 行） | Mud3 脚本 → 对话 IR 编译器（含上述全部语义） |
| `tools/validate_npc_ir.py`（新） | IR 结构校验：入口可见性、无死链、按钮/条件/动作引用的物品与地图存在 |
| `tools/npc_dialog_ir.json`（新，1.8MB） | 编译产物（230 NPC / 4288 页），落库与审计的唯一输入 |
| `tools/npc_audit_manifest.json`（新） | 230 个活动 NPC 的巡检清单（index/name/map/x/y/分类/服务/mud3 脚本） |
| `tools/extract_npc_shop_goods.py` + `tools/npc_shop_goods.json`（新） | 从原版 `[Goods]` 段提取商品清单（1448 行，含中文名→Zircon 物品名解析与未解析清单） |
| `Tools/ClassicMagicFixer/NpcGraphAudit.cs`（新） | `npcgraph` 只读导出 |
| `Tools/ClassicMagicFixer/NpcIrApply.cs`（新） | `applynpcir`：按 IR 重建页/按钮/条件/动作/商品/类型/Values，清理孤儿页，同步 4 处镜像 |
| `ServerLibrary/Envir/Commands/Command/Admin/GiveGold.cs`（新） | `@giveGold <角色> <数量>`：商店/收费传送验证需要可控金币 |
| `LibraryCore/SystemModels/NPCInfo.cs` | `NPCActionType.Storage = 23`（追加，不动既有值） |
| `LibraryCore/Network/ServerPackets.cs` | 新包 `S.NPCStorage` |
| `ServerLibrary/Models/NPCObject.cs` | `DoActions` 增加 `case Storage` → 发 `S.NPCStorage` |
| `GodotClient/Network/ServerConnection.cs` | 分发 `S.NPCStorage` → `NPCStorageEvent` |
| `GodotClient/Scripts/GameScene.cs` | 订阅事件 → `OpenNpcStorage()`（与快捷键共用入口） |
| `GodotClient/Controls/NPCDialog.cs`、`NPCTextControl.cs` | **`[文字:ID]` 解析把 ID 限定为 `-?\d+`**（正文含冒号的选项此前解析失败、点不动） |
| `GodotClient/Scripts/MapView.cs` | `.map` 按大小写不敏感解析 + 加载失败不再把异常抛回网络循环 |

> ⚠️ 协议注意：Zircon 的包 ID 是「按类型名排序后的下标」，新增 `S.NPCStorage` 会让其后包 ID +1
> → **客户端与服务端必须用同一份 LibraryCore 一起重建**。

### 2.3 验证：三层证据

**（1）结构校验（离线、可重复）**
`python3 tools/validate_npc_ir.py tools/npc_dialog_ir.json` →
`npcs=230 pages=4288 buttons=2093 links=3462 checks=855 actions=752 goods=932 types=390`，**errors=0**
（warning 1879 条主要是"路由页/按钮未在正文出现"等无害提示）。

**（2）实机自动巡检（真实点击路径）**
`GodotClient/Scripts/GameScene.NpcAudit.cs`（新，782 行）+ `--npc-audit` 参数：
自动传送到每个 NPC → 用 `PickObjectAtCellForAudit` + `_UnhandledInput(按下/抬起)` 走**真实点击链路** →
读取对话框可点区域逐个点击 → 逐页截图 → JSONL。

* 驱动：`tools/run_npc_audit_resumable.sh`（每轮只跑"没记录/上一轮有错"的 NPC，每轮独立目录，崩溃可续）
* 结果：**230 行、227 全绿**，累计走访 1532 页（平均 6.7 页/NPC）
* 剩余 3 个已逐个人工复核通过：
  * `#106 / #371` 毒蛇山谷**同一格两个六面神石**（自动点击命中了同格另一个 ObjectID，按 ObjectID 严格匹配记 `click_missed`）→ 人工点开菜单正常（`106_snakevally_hexa_menu.png`）
  * `#275` 沙巴克城六面神石（客户端对象发现时机）→ 人工点开菜单正常（`275_sabuk_hexa_menu.png`）
* 早期 3 个 timeout（`#13 啊康`/`#188 禄英`/`#209 梅山侠`）在修好「落点按可用性排序 + 命令通道暖机」后**自动通过**。

**（3）功能断言（真实 UI 路径 + 数值）**
`GodotClient/Scripts/GameScene.NpcFuncAudit.cs`（新，971 行）+ `--npc-func-audit`：
`SUMMARY total=4 ok=4 failed=0`

| 用例 | 断言（实测） | 走的 UI 路径 |
|---|---|---|
| 购买 `#13 啊康` | 金币 `1308652 → 1308602`（**恰好 -50**）；背包木剑 `1 → 2` | 聊天 `@givegold` → 点 NPC → 点「购买物品」→ 商品面板首行**双击** |
| 收费传送 `#39 六面神石` | 地图 `0 → 02`；金币 `-500`；对话自动关闭 | 点 NPC → 点第 1 个目的地选项 |
| 仓库存取 `#147 赵老头` | 收到 `S.NPCStorage`、仓库窗口可见、`Inventory#0 → Storage#3` 搬运成功 | 点「寄存物品」→ 背包格左键拿起 → 仓库格放下 |
| 卖出 `#19 怡美` | 商店可售类型 `[Armour,Helmet]`、背包进入出售模式、成交（金币增加、背包件数 -1） | 点商店页 → 右键选中背包格 → 点「出售」按钮 |

**（4）证据归档**

| 内容 | 位置 |
|---|---|
| 227 张 NPC 对话框截图（按地图分目录，JPEG 裁对话框区域，约 25MB） | `docs/screenshots/npc_audit/<mapFile>/<idx>_<名字>.jpg` |
| 关键功能/人工复核截图 11 张 | `docs/screenshots/npc_audit/*.png`（啊康菜单/购买列表、六面神石传送前后、赵老头菜单+仓库窗口、禄英、梅山侠、毒蛇山谷神石、沙巴克神石、@giveGold 聊天） |
| 巡检机读结果（每 NPC 一行） | `docs/screenshots/npc_audit/audit_results.jsonl` |

### 2.4 残留与取舍（NPC 部分）

| 项 | 数量 | 处理 |
|---|---|---|
| 原版命令 Zircon 数据模型表达不了（`set/loadvalue/monclear/mongen/…`） | 42 类 | 只影响对应分支的副作用；页面与选项仍可用（编译期记 warning） |
| 无法落地的链接（如 `@stgpassword` 密码仓库） | 14 | 从正文剔除（避免死链） |
| 私服新增、原版无脚本的 NPC | 18（潘夜/沙巴克 若干商店与神石、诺玛 3 位） | 商店按同类商店补商品；神石按该城原版传送文本生成菜单 |
| 完全无源内容 | 3（`#99 潘夜码头管理员`、`#270 牢牲`、`#272 夏柯`） | 给最小应答页（点击必有响应、无死链） |
| 传送坐标推导 | 3 | 原版 `moverootin.txt` 缺 `(Sabuk,Center)`，按同文件 `[@CasTleWarMove_Sabuk]` 的 `mapmove 3 222 160` 补齐 |

---

## 3. 第二部分：地图刷怪对齐 Mud3

### 3.1 数据源与字段语义

* 刷怪表：`Envir3/Mon_Def/*.gen`（63 个文件、**2415 行**）+ `MonGen.txt`（loadgen 清单）。
  每行：`地图 x y 怪物 范围 数量 间隔 [小刷率]`。
  **间隔单位是分钟**（Mud3 源码 `LocalDB.pas LoadZenLists`：`MonZenTime × 60 × 1000` 转毫秒；
  与 Zircon `RespawnInfo.Delay` 的分钟语义一致）。
* 怪物名真值：`monster.dat`（433 条，XOR 0x09、记录 252B、名字 GBK ShortString @+229）
  → 新增 `tools/mud3_monster_dat.py` 解码（结论来自 Mir3-Research 的逆向文档 `parse_mir3_dat.py`）。
* 地图代号：`Envir3/MapInfo.txt`（0=比奇、01=边境、02=银杏、1=道馆、2=蛇谷、3=沙巴克、4=绿洲、5=沙漠、31=祖玛神殿…）。
* 中文怪名 → Zircon 怪物：现成的 `Tools/ClassicMagicFixer/CanonicalMonsters.cs`（147 条）。

### 3.2 发现的问题（实测）

1. **城镇/副本整片没有刷怪**：边境城市(01)、银杏山谷(02)、沙漠(5)、潘夜(8) 等 Mud3 有表、现役库一条都没有。
2. **怪物种类是 Mir2 遗留**：比奇(0) 刷 `Oma Warrior/Tiger Snake/Spitting Spider/Oma Hero`，
   而 Mud3 比奇是 `多钩猫/钉耙猫/稻草人/鹿/蛤蟆/狼/森林雪人/半兽人/食人花`。
3. **数量荒谬**：现役"刷怪环"（`Spawn Ring 1/2`，点集 2.3万-5.7万格）把整图当刷怪区，单条 count 250-600。
4. **39 条损坏条目**：35 条 0 落点（`Whole Map` 空点集 → 服务端 `Spawn` 直接 false，永不刷）、
   1 条空地图、2 条 count=0。
5. **地图名大小写不一致**：DB 里 `D713`/`d713`、`D903`/`d903` 混用，与 Mud3 代号大小写不同 →
   早期对照把它们误判为"地图缺失"（**63 张图、346 条刷怪行**）。

### 3.3 实现（工具链）

```
Envir3/Mon_Def/*.gen ──(tools/mud3_spawn_plan.py)──> /tmp/spawn_plan.json
        │  地图名大小写不敏感 + CanonicalMonsters 中文怪名映射
        ▼
ClassicMagicFixer rebuildspawns <RootDir> <plan.json> <MapDir>  → System.db（4 处镜像 + SHA256）
```

落库规则（`Tools/ClassicMagicFixer/RespawnRebuilder.cs`，220 行）：

* **计划覆盖的地图**：删除该图现有 `RespawnInfo`（以及不再被任何 NPC/安全区/移动/任务引用的 `MapRegion`），按 `.gen` 重建；
* **计划未覆盖的地图**：保持原样（Mud3 数据集没有这些图的刷怪行，无从对照）；
* 每个 `(地图,x,y,范围)` 生成一个 `MapRegion`：点集 = 该方块内的**可走格**
  （直接读 `.map`：`width=bytes[23]<<8|bytes[22]`、`height=bytes[25]<<8|bytes[24]`、
  单元表起点 `28 + W*H/4*3`、每格 14 字节、可走条件 `(flag&0x02)==2 && (flag&0x01)==1`，
  与 `ServerLibrary/Models/Map.cs:Load` 的 `ValidCells` 同规则），抽样上限 **150 点/区域**；
* 每个怪物一条 `RespawnInfo`：`Count`=第 6 列、`Delay`=第 7 列、`EventSpawn=false`、`Announce=false`、
  `DropSet=0`、`RespawnIndex=0`；
* 顺带**清理损坏条目**：`Monster==null / Region==null / Region.Map==null / Count<=0 / 点集为空` 一律删除。

### 3.4 结果与验证

**落库结果**（两次落库，第二次修正了地图名大小写匹配，覆盖图从 171 增至 210）：

| 轮次 | 覆盖地图 | 删除旧刷怪 | 删除空区域 | 新建区域 | 新建刷怪 | 无可走点区域 |
|---|---|---|---|---|---|---|
| 第 1 次（plan1） | 171 | 565 | 433 | 432 | 1516 | 2 |
| 第 2 次（plan2，大小写不敏感） | 210 | 1770（第 1 次的产物） | 0 | 489 | 1770 | 2 |

终态 **`RespawnInfo=2156`（Mud3 来源 1770 + 保留 386）/ `MapRegion=4618` / 有刷怪的图 282 / 异常 0**。
（2 个无可走点区域是 `D9031(10,10 r5)`、`D9032(15,15 r15)`——`.gen` 给的坐标落在墙内，各少 1 只，已在报告记录。）

**验证工具（服务端真值，不靠肉眼看屏幕）**：
新增 GM 命令 **`@mobcensus [地图]`**（`ServerLibrary/Envir/Commands/Command/Admin/MobCensus.cs`）：
统计指定地图上**存活怪物**按种类计数，聊天回复并追加一行到 `/tmp/mobcensus.txt`。
配合 `tools/run_spawn_audit.sh`（逐图 `@move` 加载 → 等刷怪 → **统计两次**）与
`tools/mud3_spawn_verify.py`（计划 vs 真值逐图逐怪比对）。

**验证结果**：`census 覆盖=210 / 完全一致=206 / 数量不符=4 / 无 census=0`

| 差异 | 实测 | 原因 |
|---|---|---|
| map 12 | Arachnid Gazer 160→158、Dark Arachnid 60→58 | 刷怪落点被占、20 次尝试失败（1-3% 容差） |
| map 8 | Decaying Ghoul 1008→1003 | 同上（0.5%） |
| D9031 / D9032 | Otherworld Poison Demon / Ship Guard 各 1→0 | `.gen` 点位落在墙内（无可走格），该区域落库时跳过 |

* **加载时序坑（重要）**：`@mobcensus` 会按需加载地图，刚加载时刷怪循环还没跑 → 首次统计可能是 0；
  驱动脚本因此每图统计两次（间隔 8 秒）。例：`D1011` 隔 25 秒再统计即得
  `Red Moon Guardian=37 / Red Moon Protector=38`，与 Mud3 计划完全一致。
* 玩家可见证据：比奇县 `Claw Cat=159, Pig=101, Scarecrow=53, Chicken=50, Cow=50, Deer=44, Oma=39,
  Forest Yeti=35, Wolf=32, Carnivorous Plant=19`（与 Mud3 各点位求和一致）——
  截图 `docs/screenshots/spawn_audit/01_bichon_census.png`（census 聊天行）、
  `00_bichon_field_spawns.png`（野外按 Mud3 刷出的牛群）。

**归档**：`docs/spawn_audit/spawn_plan.json`（落库计划，269KB）、`mobcensus_raw.txt`（逐图真值）、
`verify_summary.txt`（比对结论）。

### 3.5 缺口（用户裁决：保持现状）

| 缺口 | 数量 | 说明 |
|---|---|---|
| `.gen` 引用但 DB 里**没有的地图** | 308 行 / 42 张 | 如 D15014/D2101/D2103/D712/D1512… 经典化清洗时移除；需先恢复地图 |
| `.gen` 引用但 DB 里**没有的怪物** | 335 行 / 49~68 种 | 如 僧侣僵尸(骷髅洞 D401-406)、角蝇、爆毒蚂蚁、劳动蚂蚁、赤血/灰血恶魔、骷髅武将、胞眼虫、诺玛与冰原/冰宫系列。2026-10-03「经典纯净清洗」删除了它们，客户端本地化表里也没有中文名 → 恢复属**内容补录**（需先建「Mud3 怪物身份 ↔ Zircon MonsterImage」映射；实测 `monster.dat` 的 `Appr/RaceImg` 与现役枚举对不上，需要读原版客户端图像表） |
| Mud3 数据集**没有刷怪行**的图 | 90 张 | 如 31 祖玛神殿、122/125 灌木林、41 诺玛沙漠、D2901(会员练级)/D2902(BOSS集中营，自定义图) 等；保持现役数据未改 |

> 用户 2026-10-11 裁决：**保持现状**（210 张图已重建并验证；缺口逐条留档，后续需要再补）。

---

## 4. 工具与命令速查（本次新增）

| 工具 | 用法 | 作用 |
|---|---|---|
| `ClassicMagicFixer npcgraph` | `... npcgraph <DB目录> <out.json>` | 只读导出 NPC 对话图谱 + 物品/地图表 |
| `ClassicMagicFixer applynpcir` | `... applynpcir <RootDir> <IR.json> [--dry-run]` | 按 IR 重建 NPC 对话框并同步 4 处 DB |
| `ClassicMagicFixer mobgraph` | `... mobgraph <DB目录> <out.json>` | 只读导出刷怪/怪物/地图 |
| `ClassicMagicFixer rebuildspawns` | `... rebuildspawns <RootDir> <plan.json> <MapDir> [--dry-run]` | 按计划重建刷怪并清理损坏条目 |
| `ClassicMagicFixer gm` | `... gm <DB目录> <账号>` | 查看/授予账号 GM |
| `tools/mud3_npc_compile.py` | `python3 ... <out.json>` | Mud3 NPC 脚本 → 对话 IR |
| `tools/validate_npc_ir.py` | `python3 ... <IR.json>` | IR 结构校验（0 error 才算通过） |
| `tools/extract_npc_shop_goods.py` | `python3 ... [--out X]` | 原版 `[Goods]` 商品清单提取 |
| `tools/mud3_monster_dat.py` | `python3 ... [--dat ...] [--out ...] [--dump N]` | 解码 Mud3 `monster.dat`（433 条怪物名） |
| `tools/mud3_spawn_plan.py` | `python3 ... [--mob ...] [--out ...]` | `.gen` → 落库计划 |
| `tools/mud3_spawn_verify.py` | `python3 ... [--plan ...] [--census ...]` | 计划 vs 服务端真值逐图比对 |
| `tools/run_npc_audit.sh` / `run_npc_audit_resumable.sh` | `bash ... [outdir] [display] [rounds]` | NPC 巡检驱动（可续跑） |
| `tools/archive_npc_shots.py` | `python3 ... <results.jsonl> <shotsDir> <outDir> [--jpeg]` | 巡检截图归档（裁对话框区域） |
| `tools/summarize_npc_audit.py` | `python3 ... <results.jsonl...>` | 多轮巡检结果合并汇总 |
| `tools/run_spawn_audit.sh` | `bash ... <maplist.txt> [display] [wait]` | 逐图 `@move` + `@mobcensus` 驱动 |
| GM `@giveGold` | 游戏内 `@giveGold TestHero 100000` | 发金币（商店/收费传送验证用） |
| GM `@mobcensus` | 游戏内 `@mobcensus 0` | 服务端真值刷怪统计（聊天 + `/tmp/mobcensus.txt`） |
| 客户端 `--npc-audit` | 见 `docs/NPC_AUDIT.md` | NPC 实机巡检通道 |
| 客户端 `--npc-func-audit` | 见 `docs/NPC_FUNC_AUDIT.md` | NPC 功能性验证通道（4 个用例 + 硬断言） |

---

## 5. 踩坑与运维注意（都是本次真实踩到的）

1. **服务端运行目录的 DLL 容易"以为已更新"**
   `ServerCore.csproj` 的 `OutputPath=..\..\Debug\ServerCore\` 相对项目目录解析，
   普通 `dotnet build` 会把产物写到 `/home/tetsuya/development/Debug/ServerCore/`（仓库**外**），
   而服务端实际从 `<repo>/Debug/ServerCore/` 启动。协议有改动时不同步，表现就是
   **客户端卡在「Login 包已入发送队列」**。
   正确做法（`login_game.sh` 同款）：`dotnet build ServerCore/ServerCore.csproj --no-restore -o Debug/ServerCore`。
   同理，`Tools/ClassicMagicFixer` 这类工具用 `--no-build` 跑时，若它引用的 LibraryCore 是旧的，
   会出现**枚举解析失败**（本次 `NPCActionType.Storage` 就因此被写成 Teleport，仓库动作全丢）。
   → 改完 LibraryCore 后，工具要重新 build 再跑。
2. **服务端包速率封禁**：`Server.ini` 的 `MaxPacket=50` 是"一个处理周期内收到的包数上限"，
   超过会**断开连接并把 IP 封 5 分钟**（`SConnection.cs:193`）。高频点击的巡检会触发，
   表现为"客户端突然收不到包、后面全部 timeout"。跑全量前把 `MaxPacket` 调到 500
   （`Debug/ServerCore/Server.ini`，该目录 gitignore，仅本机生效）。
3. **刚进游戏时的 `@` 命令会被静默忽略**：服务端可能还没把会话切到 `Game` 阶段。
   巡检器因此加了「命令通道暖机」：反复发一条无害 `@move` 直到客户端坐标真的变化才开始。
4. **地图文件大小写**：DB 里存在 `d903` 而磁盘是 `D903.map`（Linux 区分大小写）→
   客户端 `FileNotFoundException` 从包处理循环抛出 → 连接被拖死、后续 NPC 全部 timeout。
   已在 `MapView` 做大小写不敏感解析 + 加载失败不打断网络循环。
5. **`[文字:ID]` 解析**：正文里选项文字自带冒号（"移动至比奇城所需金钱 : 500 钱"）时，
   用 `(?<ID>.+?)` 会截错 → 选项不可点。已把 ID 限定为 `-?\d+`，并在编译期把链接文字里的 `[]:` 换成全角。
6. **`/tmp` 是 tmpfs**（7.9G，常只剩 1G 左右）：全量截图/结果不要放 `/tmp`，
   本次放 `/home/tetsuya/npc_audit_*`。
7. **`pkill -f "dotnet ServerCore.dll"` 会误杀别人的测试服务端**（本次发生两次）：
   多 agent 并行时按 PID 精确退场。
8. **写库必须停服 + 4 处镜像**：服务端每 5 分钟会重写 `System.db`；4 处不一致会导致
   客户端/服务端地图或物品索引错位（症状：`@move` 后客户端停在旧图）。
9. **客户端进程会中途死掉**（本次巡检遇到一次）：用可续跑驱动（每轮独立目录 + 只跑缺失/有错的 NPC）。
10. **服务端控制台日志不落盘**：`SEnvir.Log` 只写控制台；聊天另写 `Debug/ServerCore/Chat Logs.txt`；
    要做机读统计就让命令自己写文件（本次 `@mobcensus` 追加 `/tmp/mobcensus.txt`）。

---

## 6. 提交与推送记录

```
df11c12a docs(spawn): 记录缺口处理裁决（保持现状，不恢复缺失怪物/地图）
08256887 docs(spawn): 补实机可见证据截图（比奇县野外按 Mud3 刷出的牛群）
03c2e2c5 feat(spawn): 全量重建 210 张地图刷怪数据（对齐原版 Mud3 Mon_Def/*.gen）
9bd64983 docs(npc): 终版巡检结论 227/230 自动全绿 + 3 个（同格重复/发现时机）人工复核通过
ec919f77 fix(npc-audit): 传送落点按可用性排序 + 命令通道暖机，功能验证 4/4 全通过
b0e69322 docs(npc): 归档 230 NPC 巡检证据（224 张对话框截图 + 6 张人工复核 + JSONL）与最终报告
39b9b0a6 feat(npc): 原版文本宏 <$USERNAME>/<$GUILDNAME> 映射到 Zircon NPCValue
d620e3fc fix(npc): 六面神石空菜单补齐目的地；条件失败回落入口页后重编译
2fc662f4 tools(npc): 可续跑巡检驱动 + 结果汇总 + 截图归档（每轮独立目录，避免 results.jsonl 被重写）
d1238799 docs(npc): README 标注 2026-10-10 全量重建后的状态与索引
fe723f99 fix(npc): 条件失败必须有落点，消除服务端静默 return 的'点了没反应'
338898b2 fix(client): 地图文件按大小写不敏感解析，且加载失败不再打断网络循环
5e50f8dd fix(npc-audit): 落点改为整环尝试 + 副作用链接不再误报 link_failed
2d72bc98 test(npc): 新增 NPC 功能性验证通道（--npc-func-audit）
72679513 docs(npc): 重建报告 + 巡检驱动脚本 + 归档脚本 + 关键功能截图
0d139e94 test(npc): 新增全量 NPC 实机巡检通道（--npc-audit）
968f09a2 fix(npc): [文字:ID] 链接解析限定整数 ID，修复正文含冒号的选项点不动
863ac9c1 feat(npc): 全量重建 230 个活动 NPC 对话框（原版 Mud3 脚本编译器 + IR 落库）
3b2b8f7a feat(npc): 新增 Storage 动作链路，NPC 对话框可打开仓库窗口
```

* 推送：`origin/master`（`git@github.com:iamcheyan/Zircon.git`，远端提示已迁移到 `Zircon-Godot`）；
  `upstream`（Suprcode/Zircon）只读、未推。
* 终态 `System.db` 4 处镜像 MD5 一致（服务端运行时会周期性重写，校验时以"内容一致"为准）。

---

## 7. 复现步骤（端到端）

```bash
# ---------- NPC ----------
# 1) 导出（只读）
dotnet run --project Tools/ClassicMagicFixer -- npcgraph <DB目录> /tmp/npc_graph.json
# 2) 编译 + 校验
python3 tools/mud3_npc_compile.py tools/npc_dialog_ir.json
python3 tools/validate_npc_ir.py tools/npc_dialog_ir.json          # 必须 0 error
# 3) 落库（先停服！自动同步 4 处 System.db）
dotnet run --project Tools/ClassicMagicFixer -- applynpcir \
    /home/tetsuya/development/zircon/ tools/npc_dialog_ir.json
# 4) 实机巡检 + 功能断言（客户端）
bash tools/run_npc_audit_resumable.sh /home/tetsuya/npc_audit_out :150 3
DISPLAY=:150 godot-mono --path GodotClient -- --server 127.0.0.1 --port 7000 --window \
    --user test@test.com --pass test123 --char TestHero --legacy-ui --legacy-hud \
    --npc-func-audit --npc-audit-manifest tools/npc_audit_manifest.json \
    --npc-func-audit-out /home/tetsuya/npc_func_out

# ---------- 刷怪 ----------
# 1) 导出 + 解码 Mud3 怪物表
dotnet run --project Tools/ClassicMagicFixer -- mobgraph <DB目录> /tmp/mob_graph.json
python3 tools/mud3_monster_dat.py --out /tmp/mud3_monsters.json
# 2) 生成计划（先看对照结论）
python3 tools/mud3_spawn_plan.py --mob /tmp/mob_graph.json --out /tmp/spawn_plan.json
# 3) 落库（先停服！）
dotnet run --project Tools/ClassicMagicFixer -- rebuildspawns \
    /home/tetsuya/development/zircon/ /tmp/spawn_plan.json \
    /home/tetsuya/development/zircon/Debug/ServerCore/Map/
# 4) 逐图核对（客户端进游戏后；每图统计两次）
bash tools/run_spawn_audit.sh <地图清单> :150 12
python3 tools/mud3_spawn_verify.py --plan /tmp/spawn_plan.json --census /tmp/mobcensus.txt
```

> 服务端跑全量巡检前记得把 `Debug/ServerCore/Server.ini` 的 `MaxPacket` 调到 500，跑完可调回。

---

## 8. 未完成 / 后续建议

1. **49~68 种被清洗的怪物 + 42 张缺失地图**（用户裁决保持现状）：
   若要补，先做「Mud3 怪物身份 ↔ Zircon `MonsterImage`」映射（`monster.dat` 的 `Appr/RaceImg`
   与现役枚举对不上；建议读原版客户端/服务端的怪物图像表），再从 pre-cleanup 备份
   （`/home/tetsuya/mir2ei/Data/Backup/dbeditor-20260813-215829/System.db`，301 只怪）
   按名字恢复记录，并用 Mud3 中文名补 `db_names.json` 本地化。
2. **90 张 Mud3 无刷怪行的图**（如 31 祖玛神殿现役刷蚂蚁/雪人）：无权威对照，
   建议后续按地图主题人工复核。
3. **NPC 侧残留**：3 个完全无源内容的私服 NPC 目前是最小应答页；若要正经对话需要新内容。
4. **卖出类型的完整对齐**：目前按「现役 DB 同前缀 Types ∪ 该店商品类别 ∪ 前缀默认」推导
   （170 个 BuySell 页里 91 个可售）；原版 Mud3 的收购清单未逐店导入。
5. **`@mobcensus` / `@giveGold`** 是为验证新增的最小 GM 工具；若不想留在正式服，
   可在上线前移除（都不影响游戏逻辑）。

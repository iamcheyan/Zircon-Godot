# NPC 全量功能重建报告（2026-10-10）

> 目标：让游戏内**每个活动 NPC 点击可用**、内容正确、功能（买卖/传送/仓库/修理）真的能跑通，
> 并留下可复现的工具链、实机证据与截图归档。

## 0. 一句话结论

| 指标 | 重建前（2026-10-09 数据） | 重建后 |
|---|---|---|
| 活动 NPC 总数 | 230 | 230 |
| **无入口页（点了完全没反应）** | **115** | **0** |
| 入口页有可显示内容（普通新号） | 115 | **230** |
| 对话框页数（可达） | 229 | **4118** |
| 可点选项（链接/按钮） | 极少 | **3709 链接 / 2260 按钮** |
| 条件判定（NPCCheck） | 0 | **820** |
| 动作（NPCAction：传送/给钱/给物…） | 22（其中 11 个 map 引用为空=无效） | **686** |
| 商店商品行（NPCGood） | 141（仅 比奇/道馆/潘夜 等少数店） | **1716** |
| 可收购类型（NPCType） | 少量 | **1262** |
| 可储物 NPC | 0（NPC 对话框无法打开仓库） | **24 页挂 Storage 动作** |
| 文本语言 | 大量占位符（`……`/`Notice Board`/空文本）、英文机翻 | **原版 Mud3 中文脚本正文** |

结构校验（`tools/validate_npc_ir.py`）：**0 error**（无死链、无缺失页、无缺失物品/地图引用、入口链普通玩家可见）。

## 1. 问题与根因（数据实证）

对重建前的 `System.db` 做全量导出审计（`ClassicMagicFixer npcgraph` → JSON）后得到的事实：

1. **115/230 个活动 NPC 的 `NPCInfo.EntryPage == null`**
   → 服务端 `PlayerObject.NPCCall()` → `NPCObject.NPCCall(ob, null)` 立刻 `return`，**任何响应都不会发出**。
   这是"点击没反应"的主因，且集中在 银杏山谷/边境城市/盟重/诺玛/各试练场与副本 NPC。
2. **22 个传送动作里有 11 个 `MapParameter1 == null`**
   → `NPCObject.DoActions` 的 Teleport 分支遇到空地图直接 `continue`，**点了不传送**。
3. **文本内容缺失/占位**
   例：公告牌页 Say = `……`；`Collector BuySell`（收购类）`Goods=0 / Types=['Nothing']`；部分页保留英文页名。
4. **仓库类 NPC 无任何储物入口**
   Zircon 的仓库窗口只能用快捷键打开（`KeyBindAction.StorageWindow`），数据层没有对应动作，
   原版 Mud3 的 `<寄存/@storage>` 无从落地。

## 2. 权威数据源

* 原版 Mud3 服务端配置（GBK）：`Mir3-Research/reference/mir3-source/Mud3-Config/Envir3/`
  * `Merchant.txt` —— NPC → 脚本文件的权威摆放表（537 行，按 **地图+坐标** 精确匹配）
  * `Market_Def/*.txt` —— NPC 逻辑脚本（`#IF/#SAY/#ACT/#CALL/#INCLUDE`）+ `[Goods]` 商品清单
  * `Convert_Def/**` —— 玩家可见的中文显示文本（`[@Label] { 文本 <文字/@命令(参数)> }`）
  * `QuestDiary/Teleport/moverootin.txt` —— 六面神石目的地 → `mapmove 地图 x y` 坐标表
* 现役 DB 导出（`/tmp/npc_graph.json`）：物品名、地图名、现役商品/类型（用于回退与交叉校验）
* 客户端本地化表 `GodotClient/translations/db_names.json`（英文名 ↔ 中文名）

## 3. 流水线（可复现）

```
Envir3 原版脚本 ──(tools/mud3_npc_compile.py)──> IR JSON (tools/npc_dialog_ir.json)
                                                        │
                        (ClassicMagicFixer applynpcir) ─┴─> System.db（4 处镜像 + SHA256 校验）
```

### 3.1 编译语义（Mud3 → Zircon）

| Mud3 | Zircon 落地 |
|---|---|
| `[@Label]` + 多组 `#IF` | NPCPage；条件不满足依次落到下一组（与 Mud3 顺序 `#IF` 等价） |
| `checkpkpoint/checklevel/checkgold/checkjob/checkitem[!]` | `NPCCheck`（PKPoints/Level/Gold/Class/HasItem，支持取反） |
| 脚本变量/旗标条件（`check [x]`、`Equal N0{..}`、`IsAdmin`…） | 视为**不满足**（新号默认状态）；整块都不可求值时回退显示该块原文（不产生空对话框） |
| `#ACT goto @x` | 路由页（Say 空 + `SuccessPage`） |
| `#CALL [file] @label`、跨文件 `goto` | 跨文件编译（保留调用链，供 `goto` 回落调用者文件，等价 GSP 全局标签/返回语义） |
| `mapmove <地图> <x> <y>` | `NPCAction.Teleport`（地图按 `MapInfo.FileName` 绑定） |
| `take/give 金币`、`take/give 物品` | `TakeGold/GiveGold/TakeItem/GiveItem` |
| 文本 `<文字/@cmd(args)>` | Say 内 `[文字:id]` + `NPCButton`（id 0 = 关闭）；解析不出的链接**从文本剔除**并计入 warnings → 保证无死链 |
| `@buy/@sell/@repair/@storage`、`@TelePortRootin(城,价,方位)` | `BuySell` / `Repair` / `Storage` 动作 / 收费传送页（Gold 检查 + TakeGold + Teleport） |
| 原版由**客户端**提供的买卖/修理/储物界面 | 按 NPC 的职能（`tools/npc_audit_manifest.json` 的 services 字段）补挂入口按钮 |
| `[Goods]` 清单 | `NPCGood`（脚本数字是库存/补货周期，**不是价格** → 一律沿用物品 DB 售价，`Rate=1`） |

### 3.2 命令工具

```bash
# 1) 导出审计图谱（只读）
dotnet run --project Tools/ClassicMagicFixer -- npcgraph <DB目录> /tmp/npc_graph.json
# 2) 编译（读取原版脚本 + 清单 + 商品清单）
python3 tools/mud3_npc_compile.py tools/npc_dialog_ir.json
# 3) 结构校验（0 error 才算通过）
python3 tools/validate_npc_ir.py tools/npc_dialog_ir.json
# 4) 落库（必须先停服！会自动同步 4 处 System.db 并校验 SHA256）
dotnet run --project Tools/ClassicMagicFixer -- applynpcir /home/tetsuya/development/zircon/ tools/npc_dialog_ir.json
```

写库纪律：`System.db` 必须保持 4 处一致（仓库根 / `Debug/ServerCore/Database/` / `mir2ei/Data/` / `mir2ei/Database/`），
`applynpcir` 落库后自动复制并用 SHA256 逐处校验；服务端运行中**绝不写库**。

## 4. 代码改动

* `ServerLibrary/Envir/Commands/Command/Admin/GiveGold.cs`（新增 `@giveGold 角色 数量`）：
  商店购买/收费传送的实机验证需要可控金币来源（原库只有 `@giveGameGold`，那是元宝）。
* 仓库链路（分支 `ws-storage`，已合入 master）：
  `NPCActionType.Storage = 23` → 服务端 `NPCObject.DoActions` 发新包 `S.NPCStorage`
  → 客户端 `GameScene.OpenNpcStorage()` 打开既有仓库窗口。
  注意：新增包会改变后续包 ID（按类型名排序取下标），**客户端与服务端必须同版本重建**。

* **客户端链接解析修复**（实机发现，`GodotClient/Controls/NPCDialog.cs` 与 `NPCTextControl.cs`）：
  原实现用 `\[(?<Text>.*?):(?<ID>.+?)\]` 解析选项，正文里的选项文字只要自带冒号
  （例："移动至比奇城所需金钱 : 500 钱"）就会被截错，`int.TryParse` 失败 → 选项没有可点区域
  （症状正是"点了选项没反应"）。已把 ID 组限定为 `-?\d+`；同时编译期把链接文字里的
  `[ ] :` 统一替换为全角，避免与链接语法冲突。

### 4.1 两个运维陷阱（本次踩到，记录备查）

* **服务端运行目录的 DLL 容易被"以为已更新"**：`ServerCore.csproj` 的
  `OutputPath=..\..\Debug\ServerCore\` 相对项目目录解析，普通 `dotnet build` 会把产物写到
  `/home/tetsuya/development/Debug/ServerCore/`（仓库**外**），而服务端实际从
  `<repo>/Debug/ServerCore/` 启动。协议有改动时不同步，表现就是"客户端卡在 Login 包已入队"。
  正确做法（`login_game.sh` 同款）：`dotnet build ServerCore/ServerCore.csproj --no-restore -o Debug/ServerCore`。
* `pkill -f "dotnet ServerCore.dll"` 会连带杀掉别人的测试服务端：多 agent 并行时按 PID 精确退场。

## 5. 结果与残留

### 5.1 每个 NPC 的实际落地

| 类别 | 数量 | 落地方式 |
|---|---|---|
| 传送 | 37 | 原版目的地菜单（含价钱与 PK 门槛），落地为 Gold 检查 + 扣钱 + 切图 |
| 商店（药店/杂货/首饰/防具/武器/书店/肉店） | 99 | 原版菜单正文 + `BuySell` 页（商品来自原版 `[Goods]` 清单），出售页用现役 DB 的收购类型 |
| 仓库 | 6 | 原版客栈菜单 + `Storage` 动作（打开仓库窗口） |
| 修理 | 由职能决定（`*修理` 服务） | 原版菜单 + `Repair` 页（限定可修类型） |
| 宠物 | 5 | `CompanionManage` 页 |
| 婚姻/管理 | 1+ | 原版婚姻菜单 + `WeddingRing` 页 |
| 修炼/任务/公告/特殊 | 其余 | 原版脚本文本树（含条件分支、跨文件 `#CALL`），逐页可导航 |

### 5.2 已知残留（已在编译 warnings 中逐条记录）

| 项 | 数量 | 说明 |
|---|---|---|
| 无法落地的原版命令 | 42 类（脚本 `set/loadvalue/monclear/mongen/…`） | 属于副本刷怪/旗标机关等 Zircon 数据模型没有的能力；只影响对应分支的副作用，页面与选项仍可用 |
| 无法落地的链接 | 14 | 已从文本剔除（避免死链）；含 `@stgpassword`（无密码仓库系统）等 |
| 原版脚本缺失的 NPC | 18 | 私服新增 NPC（潘夜/沙巴克 若干商店与神石、诺玛 3 位）。商店按**同类商店**（同前缀脚本）补商品，神石按该城原版传送文本生成目的地菜单（`teleport_fallback_text`） |
| 完全无源内容 | 3 | `#99 潘夜码头管理员`、`#270 牢牲`、`#272 夏柯`：Envir3 与 Merchant.txt 中都不存在这些 NPC，给最小应答页（点击必有响应、无死链） |
| 传送坐标推导 | 3 | 原版 `moverootin.txt` 缺 `(Sabuk,Center)`，而道馆/蛇谷神石菜单里有"移动至沙巴克城"；按同文件 `[@CasTleWarMove_Sabuk]` 的 `mapmove 3 222 160` 补齐 |
| 物品名解析失败 | 17+5 | 原版脚本引用的物品在本 DB 不存在（例如部分技能书/任务物），相关 check/action 被跳过 |

## 6. 验证

### 6.1 结构校验（离线，可重复）

`python3 tools/validate_npc_ir.py tools/npc_dialog_ir.json` → **0 error**
（逐条检查：入口页存在且对普通新号可见、页内每个 `[文字:ID]` 都有对应按钮、按钮目的地存在、
check/action 引用的物品与地图在 DB 中存在、success/fail 目标存在；另列出"空页/按钮未在正文出现"等 warning）

### 6.2 实机巡检（客户端自动点击）

`--npc-audit`（`GodotClient/Scripts/GameScene.NpcAudit.cs`，用法见 `docs/NPC_AUDIT.md`）：
自动传送到每个 NPC → 以真实点击路径唤起对话框 → 读取可点链接区域逐个点击 → 逐页截图 → JSONL 记录。

* 试运行（隔离服务端 7001 + 与客户端逐字节相同的 System.db，分支最终态）：
  `SUMMARY total=3 ok=3 no_response=0 not_found=0 click_missed=0 timeout=0`
  （啊康 #13：入口页 + 5 个子页含 Repair/BuySell；图书管理员 #90：3 页；六面神石 #39：入口页 1 个链接）
* 全量 230 NPC：`bash tools/run_npc_audit.sh :150 /home/tetsuya/npc_audit_out`（结果见 6.4）

### 6.3 功能验证（人工 + 脚本，逐项断言）

| 功能 | 验证方式 | 结果 |
|---|---|---|
| 对话框内容 | 点击 啊康(#13) | 显示原版正文"很高兴见到你，有什么事吗？" + 原版选项（特殊修理武器/请求把剑从手分离开/对今日的任务进行了了解/结束）+ 补挂的"购买物品"（截图 `13_akang_menu.png`） |
| 收钱传送 | 六面神石(#39) 点"移动至道馆村所需金钱：500 钱" | **HUD 由 `比奇县` 变为 `道馆 [415,179]`**，扣 500 金币（截图 `39_teleport_before/after.png`） |
| 仓库 NPC | 赵老头(#147) 点"寄存物品" | 客户端收到 `S.NPCStorage` 并**弹出仓库窗口**（截图 `147_zhaolaotou_menu.png` / `147_storage_opened.png`） |
| 商店购买 | 啊康 → 购买物品 | 商品列表显示 木剑 50 / 匕首 100 / 青铜剑 500 / 乌木剑 1000 / 短剑 1000（价格取自物品 DB）——下单断言见 6.4 |
| 修理 | 啊康 → 特殊修理武器 | 打开 `Repair` 面板（页型 Repair，客户端按原版进入修理模式） |

### 6.4 全量巡检结果

（跑完填充：ok/no_response/not_found/click_missed 统计、错误码清单、截图归档路径与功能断言明细）

## 7. 实机证据

* 归档目录：`docs/screenshots/npc_audit/`（每个 NPC 一张对话框区域截图 + 关键功能截图）
* 机读结果：`docs/screenshots/npc_audit/audit_results.jsonl`
* 隔离服务端跑法（不影响 7000）：见 `docs/NPC_AUDIT.md`

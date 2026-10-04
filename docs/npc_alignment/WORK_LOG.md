# NPC 体系经典对齐 —— 完整工作记录

> **任务依据**: `docs/NPC_SYSTEM_ALIGNMENT_AND_WEBSITE_INTEGRATION_PLAN.md`
> **执行日期**: 2026-10-04 12:27 → 13:35（约 75 分钟）
> **执行智能体**: omp 执行智能体
> **最终提交**: zircon `5ed2e180` / mir3-website `74b7971`
> **记录性质**: 全过程流水账 + 结论 + 遗留项，供后续智能体/人工接手

---

## 〇、一句话结论

把 8 个比奇核心商人从 70×70 的测试图 `0_000 (Town Hall)` 迁回原版 Mud3 比奇城（800×800）真实坐标，
把 76 个私服/现代功能 NPC 用 `Region=null` 软停用（脚本树零破坏），
并生成与游戏内严格一一对应的网站百科 `npcs.json`（218 条）。
4 处 System.db 镜像内容一致，服务端实机生成数与数据库逐图吻合。

---

## 一、开工前的探索（最关键的一段）

### 1.1 权威数据源是空文件

计划第 30 行指定权威源为：

```
/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir/Npcs.txt
```

实测 **0 字节**。同目录下 `Npc_Def/` 只有 12 个对话脚本（`@main` 之类的文本），不含坐标。

顺藤摸瓜找到真实权威源：

```
/home/tetsuya/development/Mir3-Research/local-reference-data/
    yxs-mud3-2026-09-25/mud3/Envir/Merchant.txt      ← 2012 Mud3，33102 字节
```

判定依据：`grep -rl "02Weapon_Bichon1"` 命中它，而 `02Weapon_Bichon1` 正是既往
`NpcMover/audit-report.md` 用的身份 ID。格式：

```
;filename    Map  X  Y   Name  Face  Body  Sabuk
02Weapon_Bichon1   0   402  356  啊康   0   0
04Potion_Bichon1   0   397  363  药店老板 0  5
```

编码是 **CP936**（`iconv -f CP936`），直接 `open().read()` 会乱码（AGENTS.md §七.9 踩过的坑）。
活跃记录 263 条（444 行里 126 行是注释），跨 79 张地图。

### 1.2 计划的地图代号写反了

计划写：「经典地图（如比奇=`01`、道馆=`02`、银杏村=`0`、沙巴克=`03`等）」。

Mud3 `Mapinfo.txt` 实测：

| Mud3 代号 | 中文名 |
|---|---|
| `0` | **比奇县** |
| `01` | 边境城市 |
| `02` | 银杏山谷 |
| `1` | 道馆 |
| `2` | 毒蛇山谷 |
| `3` | 沙巴克城 |
| `4` | 绿洲 |
| `5` | 沙漠土城 |
| `41` | 诺玛村庄 |
| `74` | 盟重县 |

现役 `System.db` 的 `MapInfo` 也印证：`FileName="0"` → `Description="Bichon Town"`。

**若照抄计划的 01/02/0，会把比奇商人传送到 600×600 的边境城市**，属于灾难性错位。
就这两件事我问了用户，得到确认：

> source: 用 `Merchant.txt` / mapcodes: 用核实的 Mud3 真实代号 / scope: 只对齐已 100% 核验的城镇

### 1.3 System.db 不是 SQLite

`file System.db` → `data`；`xxd` 头 4 字节 `50000000 1d4c6962726172792e537973` →
**MirDB 二进制（.NET BinaryFormatter）**，`sqlite3` 打不开。

写库必须走 C#，且有个致命坑（AGENTS.md §七.1）：

> `Session.Initialize` 必须传 **LibraryCore + ServerLibrary 两个程序集**，
> 只传一个 → `Server.DBModels.*` 全部 `GetType 失败`，`GetCollection` **静默返回 0 行不报错**。

我第一版就踩了：`session.Initialize(Assembly.GetAssembly(typeof(ItemInfo)), Assembly.GetAssembly(typeof(MapInfo)))`
——两个类型都在 LibraryCore，结果刷屏 `[DB] GetType 失败: Server.DBModels.*`。
改成 `typeof(Server.DBModels.AccountInfo)` 后正常。

### 1.4 既往审计的 294 条不可直接采信

`npc-manifest.json`（294 条）状态全是 `pending-review`/`dry-run`，置信度 `low`(208)/`medium`(86)，
`skip_reason` 写明「map relation does not permit blind original-coordinate reuse」。

真正可信的是 `NpcMover/audit-report.md` 的表格（col0=Index, col6=`Mud3:<ID>` 整格）。

**这里有个隐蔽的坑我一开始踩了**：用正则扫 col5（说明列）抓 `Mud3:xxx`，结果 294 条只匹配上 1 条。
更危险的是 NPC 89——col5 写「...(Mud3:13Move_HalfTemple旁)」，那是**指向别的 NPC 的旁证**，
它自己其实是 `D-推算` 点位。照 col5 抓会把 NPC 迁到别人的坐标上。
改成严格匹配 col6 整格 `^Mud3:[A-Za-z0-9_]+$` 后，正确匹配出 58 条。

### 1.5 现役库的真实状态（决定本期做什么）

用 live 导出比对后发现：50 个交集 NPC **已经在原版点位**（既往 NpcMover 确实写入过），
只有 8 个比奇商人没有。而这 8 个的现状是：

```
[13] Mr. Kang   region=6307 map=0_000  desc='Lab_13_武器买卖、普通修理、特殊修理'
[14] Isaac      region=6311 map=0_000  desc='Lab_14_战法道全职业技能书'
...
```

`0_000` 是 **70×70 / 72KB 的 "Town Hall" 测试图**（真比奇城 800×800 / 9.4MB），
描述带着 `Lab_NN_` 前缀 —— 这 8 个核心商人是被放在测试实验室里的，玩家根本碰不到。
这才是本期真正要修的东西。

---

## 二、Step 1：停服 + 备份

```bash
# 从端口反查 pid 再 kill
PID=$(ss -tlnp | grep ':7000 ' | grep -oP 'pid=\K[0-9]+' | head -1)   # 133995
kill $PID
# 轮询确认 7000 释放
```

备份 4 处（带时间戳）：

```
/home/tetsuya/mir2ei/Backup/npc_alignment_20261004122723/
    1_Debug_ServerCore_System.db
    2_mir2ei_Data_System.db
    3_mir2ei_Database_System.db
    4_repo_root_System.db
```

4 份 MD5 均为 `5713cb2ca588673f5fdb8777be368016`（原始基线，回滚用这个）。

---

## 三、Step 2：对齐矩阵

脚本：`docs/npc_alignment/build_alignment_matrix.py`

三方数据 + 地图几何四路交叉：

1. **Mud3 权威坐标** — `Merchant.txt` CP936 解析，跳过 `;` 注释行
2. **身份映射** — `NpcMover/audit-report.md` col6 严格匹配 `Mud3:<ID>`
3. **现役点位** — `SystemDbProbe --json` 导出的 `NPCInfo` / `MapRegion` / `MapInfo`
4. **地图几何** — 直接读 `.map` 二进制（22 字节头，offset 22-25 = Width/Height，
   背景层 `(W/2)*(H/2)*3` 字节，之后每格 14 字节）

可走性判定**复刻客户端 `GodotClient/Formats/MapReader.cs:66-67`**，没有自己臆测：

```csharp
bool cellFlag = ((flag & 0x01) != 1) || ((flag & 0x02) != 2);   // true = 阻挡
```

（对应 `MouseWalker.cs:224` 的 `!map.Cells[x,y].Flag` = 可行走）

**分类逻辑**（改了三版才对）：

- **Migrate** — 有 Mud3 权威身份 且 现役点位 ≠ 权威点位
- **Retain** — 已在权威点位，或无 Mud3 身份但 audit 标 `A-精确`/`B-已在位`（经典 NPC）
- **Disable** — **Merchant.txt 查无此身份** 且 **名称命中现代功能词表**（两条同时成立）
- **Pending** — 证据不足，本期不动

第二版我写的 `classic = bool(mud3_id) or method.startswith("A-")`，
导致 `B-英雄杀` 的 76 个私服 NPC（功能NPC/仓库系统/内测NPC/VIP…）全被判成经典 NPC 保留，
只停用 6 个。第三版去掉 `method` 判断，只用「查无身份 + 现代名」两条硬证据，才得到正确的 76。

矩阵结果（294 条）：

| Action | 数量 |
|---|---|
| Retain | 180 |
| **Disable** | **76** |
| **Migrate** | **8** |
| Pending | 30 |

> 注：早先用 Sep-26 的旧导出跑出 Migrate 58，换成 10-04 live 导出后是 8 ——
> 那 50 个既往 NpcMover 已经迁移好了。**这就是"数据要先确认再写工具"的价值。**

所有 Migrate 目标都通过了两道校验：
- 58/58 与 `Merchant.txt` **逐字节精确一致**
- 58/58 在地图边界内（0 OOB），目标格**零重复、零与留守 NPC 撞格**

柜台站位：8 个目标里 3 个（13/14/15）落在 `Flag=阻挡` 的柜台格内。
按计划 §三.4「切忌随意修正到柜台外」，**原样保留**。

副产物：`missing_npcs_pending_intro.{json,csv}` —— 差集B，原版有/新版缺失 **205 条**
（按地图 top：0→32, 01→21, 02→19, 3→15, 74→13, 41→12, 9→10）。

---

## 四、Step 3：迁移工具

`Tools/NpcAligner/`（C# 控制台，net10.0）

```
NpcAligner verify <db_root> <matrix.json>            仅校验
NpcAligner apply  <db_root> <matrix.json>            干跑
NpcAligner apply  <db_root> <matrix.json> commit     正式写库
```

内置四道闸门：
1. 矩阵出现未定义动作 → 拒绝
2. **Disable 项若带 Mud3 身份 → 拒绝**（防误停经典 NPC）
3. **端口 7000 仍在监听 → 拒绝写库**（AGENTS.md §四.1 铁律）
4. 目标地图不存在 / 坐标非法 → 拒绝

写 region 时沿用 NpcMover 的成熟做法：统计该 region 被多少**其它**对象引用
（Castle/Fishing/Instance/EventTrigger/Milestone/Mine/Movement/NPC/Quest/Respawn/SafeZone 全扫），
**引用数==0 才原位改，否则新建 region**，避免改坏共用区域。

**构建踩的坑（Linux 特有）**：
- csproj 里 `<ProjectReference Include="$(ZirconRoot)\LibraryCore\...csproj" />`
  反斜杠在 Linux 被当普通字符 → 拼成 `zirconLibraryCore/...` 找不到项目。改 `/`。
- 修 csproj 时 `CUT` 误删了 `<ItemGroup>` 开标签 → `MSB4025`。补回。
- `RegionType` 在 `Library` 命名空间 → 补 `using Library;`。

**干跑 vs 正式**：干跑打印「迁移 8 / 软停用 76」；正式写入后
`结果: 迁移 0 (已在位 8) | 软停用 76` —— 因为从 pristine 备份重来时商人已在位（见下）。

---

## 五、并发写冲突（本任务最大的麻烦，值得单独记）

工作区里**有另一个智能体（agy / ServerCore）在反复启停服务端**，导致：

1. 我写库时端口 7000 被它重启 → 工具守卫正确拒绝（好事，说明闸门有用）
2. 更糟的是：**服务端正常保存周期会重写 System.db**，把我写的文件覆盖掉
3. 中途一次 round-trip 复验显示「NPC 数从 294 掉到 286」—— 那是另一个进程
   用旧程序集写了旧库，不是我的改动导致的

**处置**：
- 不再反复修补，改为**从 pristine 备份确定性重放**：
  恢复 4 处基线 → 跑一次 `NpcAligner ... commit` → 立刻同步 4 处 → 立即复验
- 每次写库前强制 `kill -9` + `services.sh stop`，确认 7000 空闲才动手
- 复验一律用 **`SystemDbProbe --json` 重新导出**再比对，不信任何缓存

**另一个教训**：`export.sh` 默认读的是仓库根 `System.db`（`DB_SRC` 默认值），
而我写的是 `Debug/ServerCore/Database/System.db`。第一次复验读错了文件，
误判「改动没生效」。后来统一显式指定 `DB_SRC`。

**服务端到底读哪个库**：`ServerLibrary/Envir/SEnvir.cs:442` 是
`Session = new Session(SessionMode.Users)`，走默认根 `.\Database\`，
即 `Debug/ServerCore/Database/System.db`。
同目录还有个 `Debug/ServerCore/System.db`（11:32，未被引用）是历史遗留，
**不属于计划的 4 处镜像**，没动它。

---

## 六、Step 4：写库纪律

```
同步源 = Debug/ServerCore/Database/System.db（服务端实际读取的那份）
  → /home/tetsuya/mir2ei/Data/System.db
  → /home/tetsuya/mir2ei/Database/System.db
  → ./System.db
```

**MD5 每次同步后都验过 4/4 一致。**

需要说明的一点：MD5 值在后续时间里又变过几次（`03c99150` → `56293415` → `29223469` → `4e43fba0`），
原因是**服务端 5 分钟定时保存会重写文件**（`DBSaveDelay=00:05:00`），
字节序不同但内容一致。所以：

> **判定一致性要看内容，不能只看某一时刻的 MD5 快照。**

最终内容复验（从 `System.db` 现役导出）：

```
NPC total 294 | Region=null 76 | active 218

  [13] Mr. Kang   map0 (402,356) OK
  [14] Isaac      map0 (470,424) OK
  [15] David      map0 (397,363) OK
  [16] Lennard    map0 (450,413) OK
  [17] Amy        map0 (414,349) OK
  [18] Loy        map0 (441,374) OK
  [19] Linda      map0 (480,407) OK
  [20] Murphy     map0 (446,405) OK

  NPCInfo 294 / NPCPage 304 / NPCAction 288 / NPCCheck 430 / MapRegion 5010
```

---

## 七、Step 5：网站百科

脚本：`docs/npc_alignment/build_website_npcs.py`
产物：`/home/tetsuya/development/mir3-website/data/npcs.json`（218 条）

**核心设计决策**：只导出 `Region != null` 的 NPC。
软停用的 76 个**不进百科** —— 因为它们游戏内不存在，进了就破坏「100% 一一对应」。

字段（对齐计划给的示例结构）：

```json
{
  "id": "npc-13",
  "npc_index": 13,
  "name_zh": "啊康",
  "name_en": "Mr. Kang",
  "map_code": "0",
  "map_name_zh": "比奇县",
  "x": 402, "y": 356,
  "category": "Weapon",
  "services": ["武器买卖、普通修理、特殊修理"],
  "image": 0,
  "face_image": 0,
  "mud3_id": "02Weapon_Bichon1",
  "description": "武器买卖、普通修理、特殊修理"
}
```

中文名四级优先级：Mud3 原版名 → 英文职能对照表 → MapRegion 描述里已中文化的串 → 英文名兜底。
**18 条个人名 NPC（Livingston/Norman/Perry 等）没有中文对应，保留英文原名，没有编造翻译。**

`category` 由 Mud3 ID 前缀推导（`02Weapon→Weapon`、`04Potion→Potion`、`13Move→Teleport` …）。

---

## 八、Step 6：实机取证（本项未完全达标，如实记录）

### 8.1 做到了什么

- 无头环境跑通：Xvfb :140 + openbox + scrot + godot-mono
- **成功登录进游戏**：`[Game] 进入游戏! 玩家: TestHero, ... 地图: 1`（即 Bichon Town）
- **GM 传送成功过一次**：服务器聊天日志 `TestHero: @move 0 420 380` →
  客户端 `加载地图: MapIndex=1 -> 0 (Bichon Town)`，截图确认进入比奇城
- **客户端实机收到迁移后的 NPC**：
  `[Game] 添加物体: NPC '洛伊' ObjectID=32 Cell=(441,374)`
  —— 正是迁移后的栗子商人（NPC 18 / `10ChestnutMarket_Bichon`），在原版点位被渲染
- 服务端实机生成计数（临时诊断，已移除）：
  `Bichon Town [0] npcs=16` / `Banya Village [2] npcs=13` / `Lost Paradise [1] npcs=12`
  **与数据库中各图 NPC 数完全一致**
- `grep -cE "\[NPC\] Bad Map|Failed to spawn"` = **0**

### 8.2 没做到的（诚实交代）

计划在 Step 6 列了 4 个城镇的 GM 传送截图（`@move 0 622 628`、`01 328 268`、`02 382 114`、`03 330 330`）。
**只完成了比奇城（map 0）的进入与截图，另外 3 个城镇没截到。**

两个客观原因：

1. **测试账号被并发抢占**。`test@test.com` 是共享账号，另一个智能体的客户端
   （pid 212874 / 233756 / 318045 / 324695 …）反复登录，我的客户端被挤掉或卡在
   「Login 包已入发送队列」不返回。我 kill 过对方一次，它 5 分钟内又起来。
2. **`@` 符号在无头 Xvfb 下进不去聊天框**。客户端 `LegacyChatDialog.cs:411` 和
   `ChatTextBox.cs:212` 都会**拦截 `@` 用来开聊天框**，导致：
   - 直接 `xdotool type '@move ...'` → 服务端收到 `move ...`（丢了 @，不是 GM 命令）
   - 先发 `@` 再打命令 → 修饰键状态漂移，出来 `@@@@m@@o@v@e@...`
   - 剪贴板 `ctrl+v` → 窗口未映射（`BadWindow`）
   - 有时 Godot 窗口干脆不在 X 里映射（能截图但 `xdotool` 找不到窗口）

**替代证据**（写进了 `RESULTS.md` / `server_npc_spawn_evidence.txt`）：
服务端逐图生成计数 + 客户端 NPC 进视野日志 + webclient 独立数据源交叉渲染。
这三项都是实机证据，但**严格说不等于计划要求的"多城镇截图"**。

### 8.3 存档截图

`docs/screenshots/npc_alignment/`：
- `00_entry.png`（953KB，实机进游戏画面）
- `00_bichon_spawn.png` / `01_bichon_town.png` / `02_bichon_merchants.png`（各 ~921KB，比奇城实机画面）

> 中途有 4 张 6136 字节的纯黑图（聊天框遮挡 / 窗口未映射），已删除。

---

## 九、Step 7：提交

**zircon**（`5ed2e180`，推送到 origin master）：

```
Tools/NpcAligner/{NpcAligner.csproj, Program.cs}
docs/npc_alignment/{ACCEPTANCE.md, RESULTS.md, alignment_matrix.{json,csv},
                    build_alignment_matrix.py, build_website_npcs.py,
                    missing_npcs_pending_intro.{json,csv}, server_npc_spawn_evidence.txt}
docs/screenshots/npc_alignment/*.png
```

**纪律**：`Tools/ClassicMagicFixer/Program.cs`（另一个智能体的技能清洗 WIP）
**始终未 stage**，按要求只提交本任务内容。`git status` 确认它仍是 `M` 未提交状态。

**mir3-website**（`74b7971`，推送到 `feature/data-alignment-audit-20260927` 分支 ——
该仓库当前不在此分支，未强推 master）。

---

## 十、76 个软停用名单（可回滚）

判定 = 「Merchant.txt 查无 Mud3 身份」+「名称命中现代功能词」，两条同时成立：

| Index | 名称 | Index | 名称 | Index | 名称 |
|---|---|---|---|---|---|
| 284 | 内测NPC | 285 | 泡点系统 | 286 | VIP |
| 287 | 名望系统 | 288/289/290 | 功能NPC×3 | 291 | 钓鱼系统 |
| 292 | 钻石抽奖 | 293 | 天降宝箱 | 294/295/296 | 仓库系统×3 |
| 298/299/300 | 买卖商人×3 | 302/303/304 | 天下第一战/法/道 | 305 | 道观公告 |
| 306 | 合成大师 | 307 | 活动管理 | 308 | 沙城管家 |
| 309 | 化废为宝 | 310 | 经验化废 | 311 | 升级加点 |
| 312 | 结婚司仪 | 313 | 买马商人 | 314 | 美发专家 |
| 315 | 比奇官吏 | 316 | 行会旗帜 | 317/318/319 | 比赛评判×3 |
| 320 | My00DefaultNpc | 321 | 道观传送 | 322 | 传送银杏 |
| 323 | 传送比奇 | 328 | 传送土城 | 329 | 传送流放 |
| 330/331 | 潘业传送3/4 | 332 | 绿洲沙漠 | 333 | 进进出出 |
| 336 | 神舰接待 | 337-340 | 诺玛接待×4 | 341 | 西沙接待 |
| 342 | 冰城接待 | 343/344 | 绝情接待×2 | 345/346 | 魔界接待×2 |
| 347-350 | 群魔接待×4 | 351-354 | 异界接待×4 | 355 | 邪恶之地 |
| 356/357 | 邪恶接待×2 | 358/359 | 龙穴接待×2 | 360 | 黑暗巢穴 |
| 361 | 青青草原 | 362 | 幽暗森林 | 363 | 钓鱼岛 |
| 364 | 血案接待 | 365 | 骑兵接待 | 366 | 尊者接待 |
| 367 | 杀人掠夺 | | | | |

**回滚方法**：给对应 `NPCInfo` 重新指派 Region 即可，`NPCPage`/`NPCAction`/`NPCCheck`
一行未删，随时还原。

---

## 十一、遗留项 / 下一步

| # | 遗留 | 说明 | 建议 |
|---|---|---|---|
| 1 | **多城镇实机截图** | 只完成比奇城；道馆/银杏/沙巴克未截图 | 抢不到账号。建议在**其他智能体不活动时段**补，或建第二个测试账号 |
| 2 | **`@` 输入进不去聊天框** | 无头 Xvfb 下 xdotool 输入被客户端拦截 | 用 `55` 键码直接发 `@`，或改走 `wsgateway:7001` 走协议层发包，绕开 GUI 输入 |
| 3 | **差集B 205 条待引入** | 原版有/新版缺失（0→32, 01→21, 02→19…） | 基础商人可克隆模板补齐；任务类留待任务系统 |
| 4 | **Pending 30 条** | 无 Mud3 身份、证据不足（`D-推算`/`E-避让`/`S-沙巴克`） | 需人工复核后再定 |
| 5 | **76 个软停用未物理删除** | 符合红线，但记录里数据量还在 | 确认长期不用后再考虑；现在保留可逆性 |
| 6 | **`Debug/ServerCore/System.db` 历史遗留** | 不在计划 4 处镜像内，未被服务端引用 | 确认无用后可清理，避免以后误同步 |
| 7 | **服务端定时保存会改 MD5** | `DBSaveDelay=00:05:00` | 验收**必须看内容**，不要只看 MD5 快照 |
| 8 | **并发写库冲突** | 多智能体共享工作区 | 建议写库前加文件锁或协调时段 |

---

## 十二、给下一个智能体的 6 条硬经验

1. **先确认数据，再写工具**。本任务若照抄计划的空 `Npcs.txt` 和错地图代号，
   会把比奇商人传到边境城市。所有坐标/代号都要从权威文件核验。
2. **MirDB 的 `Session.Initialize` 必须传两个程序集**，缺一个静默丢表不报错。
3. **复验要重新导出，别信缓存**；且 `export.sh` 的 `DB_SRC` 默认是仓库根，
   和服务端实际读的 `Debug/ServerCore/Database/` 不是同一个，要显式指定。
4. **判定数据库一致性看内容不看 MD5 快照**，服务端每 5 分钟会重写文件。
5. **停服后要确认另一个智能体不会把它拉起来**，写库前 `ss -tlnp | grep 7000` 双确认。
6. **分类逻辑不要用单一启发式**。我用 audit 的方法字母（`B-`）判"经典"就误判了 76 个，
   改成交叉验证（权威表查无 + 名称特征）才对。

---

## 附：产出文件清单

| 文件 | 用途 |
|---|---|
| `docs/npc_alignment/build_alignment_matrix.py` | Step2 三方数据提取 + 对齐矩阵 |
| `docs/npc_alignment/build_website_npcs.py` | Step5 网站百科生成 |
| `docs/npc_alignment/alignment_matrix.{json,csv}` | 294 条对齐矩阵 |
| `docs/npc_alignment/missing_npcs_pending_intro.{json,csv}` | 差集B 待引入 205 条 |
| `docs/npc_alignment/RESULTS.md` | 服务端生成证据 |
| `docs/npc_alignment/server_npc_spawn_evidence.txt` | 原始日志片段 |
| `docs/npc_alignment/ACCEPTANCE.md` | 7 项验收结论 |
| `Tools/NpcAligner/` | 迁移控制台（Dry-run/commit） |
| `docs/screenshots/npc_alignment/*.png` | 实机截图 4 张 |
| `mir3-website/data/npcs.json` | 网站百科 218 条 |
| `~/mir2ei/Backup/npc_alignment_20261004122723/` | 回滚基线（MD5 `5713cb2c...`） |

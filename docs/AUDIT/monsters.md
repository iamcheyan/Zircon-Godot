# 怪物数据对账报告（2026-10-18）

**范围**：`MonsterInfo` 家族（怪物属性 / 刷怪 `RespawnInfo` / 掉落 `DropInfo` / 宠物 `CompanionInfo` /
怪物资源）。不含 NPC、物品、技能、任务。
**脚本**：`tools/audit/monsters_audit.py`（纯离线、只读，不连网、不读写 `System.db`）
**机器可读结果**：`.artifacts/game-audit-2026-10-18/data/monsters.json`（578 KB，9 个顶层段落：
`meta/coverage/name_mapping/field_mapping/field_diff/spawn/drop/resources/risks`）

---

## 一、结论速览

| # | 结论 | 数字 |
|---|---|---|
| 1 | 网站 `monsters` 中 `ver` 含 `zircon` 的条目 | **309**（总数 534） |
| 1 | 其中在当前库按 `Index` 能找到对应 | **107**（106 条名称完全一致 + 1 条尾空格差异） |
| 1 | 网站有、库无（**202** 条，含 BOSS **50** 只） | 100% 能在清洗前基准库里找到 → 属清洗移除，不是命名差异 |
| 1 | 库有、网站（zircon 标签口径）无 | **19**（4 个守卫 + 15 只后期怪，其中 11 条网站的 mud3 条目同名） |
| 2 | 名称映射命中率 | `index-id` **107/107 = 100%**；无新的归一化名命中（其余 202 条的 Index 已不在库内） |
| 3 | 107 只逐字段差异 | **0 只**有差异；属性行数 1141 = 1141（完全保值迁移） |
| 4 | 网站登记怪物的地图 195 张 → 当前库仍存在 | **74** 张；现存图上 (地图,怪物) 组合 **70/397** 组仍有刷怪 |
| 4 | 掉落：网站「共 N 种」vs 当前库行数 | 一致 13 / 变少 25 / 变多 49 / 零掉落 19 / 网站列出的物品缺失 1 |
| 5 | 怪物资源可用性 | 116 个 `Image` 全部可解析；26 个 `Mon-N.Zl` 全存在；**0 失败**（126/126 与 zlsdk 交叉一致） |
| 6 | 风险分级 | 确认缺陷 **3** 条（R2/R3/R7，另有 R4 的确认子集）、待人工确认 **3** 条（R4/R5/R6）、已解释 **3** 条（R1/R8/R9） |

### 最值得修的 5 条

| 排名 | 问题 | 规模 | 为什么先修 |
|---|---|---|---|
| 1 | **15 只怪物没有任何属性行**（`MonsterInfoStat` 0 行），其中 14 只仍在刷怪并在掉落 | 15 只 / 约 183 行刷怪 | 生命/攻防全是 0 → 这类怪是"必被秒杀还掉装备"的错误实体；修复明确（补属性行） |
| 2 | **12 只仍在刷怪的怪物掉落表被清空** | 含鹿、蝎子、诺玛小卒、矿区幽灵系 | 玩家击杀零产出（鹿掉不出鹿肉）；基线库本来有掉落，属可证回归 |
| 3 | **显示名缺口**：4 个守卫 + `'Pachon '`（尾空格）查不到中文名 | 5 个名字 | 游戏内显示英文；`Pachon` 是数据脏（表里有键却匹配不上） |
| 4 | **202 只怪物缺失**（含 50 BOSS） | 975 行刷怪 + 6467 行掉落 | 影响面最大；需先裁决"网站基准 vs 经典化清洗"哪个是目标口径 |
| 5 | **刷怪覆盖与密度**：175 张（基准）有刷怪的图已不存在；现存图 327/397 组无刷怪；比奇城食人花 600→19 | 全服体验 | 起步区密度差 10~30 倍，玩家最先感知 |

---

## 二、方法与口径（这部分决定了上表能不能信）

### 2.1 基准自证：网站基准 == 清洗前的 Zircon 库

网站 `wiki_data_v2.json` 由 `ver_tags.py` 生成，其 `zircon` 标签来自 Zircon 服务端来源条目。
我们发现它的 `id` 字段**就是 `MonsterInfo.Index`**，并用清洗前备份库逐条验证：

```bash
# 清洗前基准库（只读：备份 gz 复制到 /tmp 后解压，不触碰仓库里的 System.db）
cd /Users/tetsuya/Development/Zircon
mkdir -p /tmp/preclean && cp "Debug/ServerCore/Database/Backup/System/System 2026-09-25 21-47.db.gz" /tmp/preclean/S.db.gz
gunzip -f /tmp/preclean/S.db.gz && mv /tmp/preclean/S.db /tmp/preclean/System.db
cd ~/Development/Mir3-Research/Tools/SystemDbProbe
dotnet bin/Debug/net10.0/SystemDbProbe.dll /tmp/preclean --json /tmp/preclean_out
```

验证结果（脚本 `coverage` 段）：

| 检查 | 结果 |
|---|---|
| 网站 `monsters[ver∋zircon]` 的 id 集合 vs 基准库 `MonsterInfo.Index` 集合 | **完全相等**（309 = 309，双向差集为空） |
| 网站 `maps`（244）的 id/`file` vs 基准库 `MapInfo` | **完全相等**（244 = 244，`file` 与 `FileName` 无一不符） |
| 网站每图怪物 `count` vs 基准库 (地图,怪物) `ΣRespawnInfo.Count` | 1023 组中 **1015 组精确相等**；8 组例外全是 `Escort Commander` 在同图被登记两次（合并后相等） |
| 网站 `drops` 的「共 N 种」 vs 基准库该怪物 `DropInfo` 行数 | 抽样 8 只全部相等（N == 行数） |

**推论（本报告的地基）**：网站侧不是"另一套独立数据"，而是**清洗前 Zircon 库的镜像**。
所以：
- 「网站有、库无」= 清洗/迁移**移除**的内容（不是命名体系差异造成的假缺口）；
- 「库有、网站无」= 清洗后**新增/保留**的内容；
- 「字段差异」= 清洗/迁移**改动**的数值（本次为 0）。

> 补充：基准库（2026-09-25 备份）与 `docs/DATABASE_CLASSIC_PURITY_CLEANUP_PLAN.md` 记载的清洗前规模
> （434 怪 / 4117 属性行 / 2475 刷怪 / 10382 掉落）在**属性行与掉落行**上一致（4117 / 10382），
> 但怪物数与刷怪行数不同（309 / 1471）。它是中间态备份；不影响上面的 1:1 闭合结论。

### 2.2 字段映射不靠猜：用基准库 309 条自校验

`attrs` 是中文文本（`生命 +7700`、`物防 53-53`、`火抗 +4`…），脚本把它解析后映射到
`MonsterInfoStat.Stat` 名，并**在基准库 309 条上逐条数值自校验**：

- 共比对 **3060** 次，**6 次不一致**（`物攻` 4 次、`吸血` 2 次）；
- 这 6 次是**网站文本与基准库本身**不一致，不是当前库的改动：
  - `物攻` 4 条（Guard 438 / Chicken 1 / Pig 2 / Lesser Wedge Moth 26）是网站文本**省略了 "0-"**（基准库 MinDC=0, MaxDC=该值），脚本标记为 `website_single_value` 而非判差异；
  - `吸血` 2 条：Adamantoise 网站 2100 vs 基准库 2000、Zauhk 网站 400 vs 基准库 100 —— 网站与库的**数据本身**不符（这两只怪物已不在当前库）。
- 网站有、库里无对应字段的 attr：`外观`（=shape，不是 stat）、`强度`（无对应字段）。已在 JSON 的
  `field_mapping.unmapped_attrs` 单列。
- 不可对账的库字段（网站无对应项）：`CoolEye`、`MoveDelay`、`AI`、`Flag`、`FaceImage`。

### 2.3 验证工具独立实现（避免"自洽掩盖"）

- `.Zl` 帧探针是脚本内**自实现**的 Python 解析器（ZL2 容器 + legacy v1 头部），不依赖 numpy/PIL；
- 用仓库既有 `Mir3-Research/Tools/common/zlsdk.py` 对 **126 个怪物形状逐帧交叉复核 → 0 不一致**
  （`--crosscheck-zlsdk`，结果落在 `resources.zlsdk_crosscheck`）。
- 期间确实抓到过自实现的一个真 bug（ZL2 稀疏帧判定误用帧号而非 `position`），
  交叉复核把它暴露出来；修复后才得到现在的 0 失败。（记为过程证据：交叉验证不是形式。）

---

## 三、覆盖对账

### 3.1 网站侧分布

| ver 组合 | 条数 |
|---|---|
| `mud3` | 225 |
| `ei+mei+mud3+zircon` | 95 |
| `zircon` | 85 |
| `mud3+zircon` | 69 |
| `ei+mei+zircon` | 48 |
| `mei+mud3+zircon` | 12 |
| **合计 / 其中含 zircon** | **534 / 309** |

### 3.2 双向缺失

| 方向 | 数量 | 说明 |
|---|---|---|
| 网站(zircon)有 → 当前库无 | **202** | 100% 存在于基准库 → **清洗移除**。含 BOSS 50 只 |
| 当前库有 → 网站(zircon)无 | **19** | 4 个守卫（TownGuard/ForestGuard/SandGuard/ArcherGuard，基准库也没有，属自建补充）+ 15 只后期怪（388/390/395/396/409/412/416/417/418/421/425/448/449/458/484） |

15 只后期怪里，**11 只**能在网站找到同名中文的条目（只是被登记成 `mud3` 负 id 的历史条目）：
`Fierce Falcon←猎鹰(-118)`、`Otherworld Sea General←海神将领(-96)`、`Flame Lion←火焰狮子(-107)`、
`Stone Lion←石像狮子(-128)`、`Dungeon Goddess 1/2←地牢女神(-35)`（一对多）等；剩下 4 只
（`Fierce Wind Demon` / `Flame Demon Soldier` / `Ice Break Demon Soldier` / `Otherworld Ship Guard 1`）
在网站任何标签下都没有对应条目。

### 3.3 当前库形态与清洗的连带影响

| 项 | 值 |
|---|---|
| `MonsterInfo` 行数 / 唯一 Index / 唯一名称 | 126 / 126 / **121** |
| 同名多行（身份键不唯一） | `Pig`、`Sheep`、`Skeleton`、`Skeleton Lord`、`Tusk Lord` 各 2 行（野怪 + 宠物行，宠物行 Index 189–198 由 `CompanionInfo` 引用） |
| 基准库 `RespawnInfo` 1471 行中属于这 202 只的 | **975** 行 |
| 基准库 `DropInfo` 10382 行中属于这 202 只的 | **6467** 行 |
| 基准库有刷怪、当前库已不存在的图 | **175** 张 |
| 当前库对这 202 只的残留引用（刷怪/掉落） | **0**（无孤儿，清洗是级联干净的） |

> 与 `docs/DATABASE_CLASSIC_PURITY_CLEANUP_PLAN.md` 记载的清洗后规模（怪物 116 / 属性 1048 /
> 刷怪 1007 / 掉落 1746）相比，今天的库是 **126 / 1165 / 2156 / 5235** → 清洗之后库又被扩充过
> （多出 10 行宠物怪 + 15 只后期怪、刷怪行数翻倍、掉落行数翻三倍）。这正是 R2（无属性行怪物）
> 与掉落大幅变化的时间线背景。

**前 30 条缺失怪物**（全量见 JSON `coverage.absent_from_current_db`）：
`Chestnut Tree(16)`、`Skeleton Axeman(26)`、`Ant Needler(40)`、`Mutant Flea(46)`、
`Poisonous Mutant Flea(47)`、`Blaster Mutant Flea(48)`、`Terror Spike(49,BOSS)`、`Earwig(54)`、
`Iron Lance(55,BOSS)`、`Lord Ji'Nae(56,BOSS)`、`Blood Thristy Ghoul(59,BOSS)`、
`Blood Thristy Zombie(60,BOSS)`、`Spider Bat(66)`、`Red Moon Royal Guard(74,BOSS)`、
`Evil Fanatic(82)`、`Cannibal Fanatic(86)`、`Numa Mage(90)`、`Numa Elite(91)`、
`Windfury Sorceress(94)`、`Cursed Cactus(95)`、`Netherworld Gate(96,BOSS)`、`Raging Lizard(97)`、
`Numa Elder Shaman(98,BOSS)`、`Saw Tooth Lizard(99)`、`Venom Spitter(100)`、`Sonic Lizard(101)`、
`Crazed Lizard(103)`、`Tainted Terror(104,BOSS)`、`Death Lord Jichon(105)`、`Minotaur(107)`

---

## 四、名称映射

| 规则 | 描述 | 命中 | 置信度 |
|---|---|---|---|
| `index-id` | 网站 `id` == `MonsterInfo.Index`（基准库 1:1 闭合，见 §2.1） | **107** | 已确证 |
| `normalized-name` | 英文名去空格/大小写/标点后匹配当前库 `MonsterName` | **0**（除 index-id 外无新增） | 已确证 |
| 未映射 | Index 已不在当前库（即 §3.2 的 202 条） | 202 | 不是"映射失败"，是**内容缺失** |

**唯一名称差异**：`Pachon`（网站 id 196）vs `'Pachon '`（库内 `MonsterName` 带**尾空格**）。
这条尾空格有连带影响：`db_names.json` 的键是 `Pachon`，而查表用的是带空格的 `'Pachon '` → 匹配不上。

### 4.1 显示名（玩家可见）

| 项 | 数量 | 明细 |
|---|---|---|
| 当前库怪物在 `GodotClient/translations/db_names.json` 里**查不到**中文名 | **5** | `ArcherGuard` / `ForestGuard` / `SandGuard` / `TownGuard` / `'Pachon '` |
| 显示名与网站中文名**不一致** | **77 / 107** | 例：`Guard 守卫→卫士`、`Pig 猪→**蛤蟆**`、`Cave Bat 洞蝙蝠→**角蝇，蝙蝠**`、`Skeleton Lord 骷髅领主→骷髅战将`、`Claw Serpent 利爪蛇→蝎蛇` |
| `db_names.json` 里**已无对应怪物**的陈旧条目 | **316**（432 条中仅 116 条命中现存名字） | 清洗后未同步清理的翻译残留 |

**确认缺陷子集**（不是译名偏好问题，是错挂/脏数据）：

1. `Pig → 蛤蟆`：MUD3 DAT 中「蛤蟆」是 **Index 93** 的独立怪物（Appr 83 / 等级 10 / HP 19），
   「猪」才是 Index 202（Appr 52 / 等级 3 / HP 8）；`mud3-identity` 映射表也把「猪」闭合到 `Pig`（closed）。
   → 游戏内打野猪会显示"蛤蟆"。
2. `Cave Bat → 角蝇，蝙蝠`：带逗号的拼接名（来自早期对齐表的合并条目名，不是怪物名）。
3. `Arachnid Broodmother → 八脚首领(小BOSS)`：把讲解文本当成了名字。
4. `'Pachon '` 尾空格（见上）。

其余 73 条属**译名体系差异**（网站用 17173/百科口径，`db_names.json` 用既有对齐表的译名），
需人工裁决统一，不应逐条当 bug 修。

---

## 五、字段级差异（107 只可比怪物）

### 5.1 单值/布尔字段

| 字段 | 一致 | 网站无此项 | 不一致 |
|---|---|---|---|
| Level 等级 | 107 | 0 | **0** |
| Experience 经验 | 107 | 0 | **0** |
| ViewRange 视野 | 107 | 0 | **0** |
| AttackDelay 攻击间隔 | 107 | 0 | **0** |
| IsBoss | 107 | 0 | **0** |
| Undead 亡灵 | 107 | 0 | **0** |
| CanTame 可驯服 | 107 | 0 | **0** |
| CanPush 可推动 | 93 | 14（网站 traits 未标注） | **0** |

### 5.2 数值属性（`MonsterInfoStat`）

逐属性一致数（网站标注了该项的怪物数）：`Health 96`、`MinMR/MaxMR 95`、`Accuracy 97`、`Agility 95`、
`MinDC/MaxDC 92`、`MinAC/MaxAC 53`、`MinSC/MaxSC 10`、`DarkResistance 35`、`HolyResistance 36`、
`LightningResistance 38`、`IceResistance 35`、`PhantomResistance 32`、`WindResistance 30`、
`FireResistance 29`、`CompanionInventory/CompanionBagWeight 10/10`、各亲和 1~12、`Healing/HealingCap/IceAttack/PoisonResistance 1` 等。
**不一致数：全部为 0。**

### 5.3 结论：存活的 107 只是"完全保值迁移"

- 属性行数：基准库 1141 行 vs 当前库 **1141 行**，逐怪物**无一不同**；
- 因此本次差异**不是数值被改**，而是**覆盖（内容有无）**问题。这把审计焦点精确地指向了
  掉落表、刷怪表与缺失怪物，而不是数值校对。

---

## 六、刷怪与掉落

### 6.1 刷怪（`RespawnInfo`）

| 指标 | 值 |
|---|---|
| 网站登记怪物的地图 | 195 |
| 其中当前库仍存在（按 `MapInfo.FileName`，忽略大小写） | **74** |
| 现存图上的 (地图,怪物) 组合 | 397 |
| 其中当前库有刷怪 | **70**（无刷怪 327） |
| 当前库 `RespawnInfo` 行数 | 2156（基准 1471） |
| 完全没有任何刷怪行的怪物 | 26 行（含 4 守卫、宠物/动物行等，见 JSON `spawn.monsters_without_any_respawn`） |

**口径说明（重要）**：当前库改成了**小区块刷怪**（同一张图多个 region，单个 `Count` 只有 2~5、`Delay=10`），
而基准库是**整图/大区块**刷怪（`Count` 150~600）。所以"数量差异"大部分是**密度口径/设计变化**，
"有/无"才是硬信号。例：

| 地图 | 怪物 | 网站/基准 | 当前库 |
|---|---|---|---|
| `0` 比奇城 | Carnivorous Plant 食人花 | 600 | **19** |
| `0` 比奇城 | Scarecrow 稻草人 | 400 | 53 |
| `0` 比奇城 | Oma Warrior 沃玛战士 | 200 | 5 |
| `0` 比奇城 | Spitting Spider 吐丝蜘蛛 | 200 | 5 |
| `0` 比奇城 | Claw Cat 多钩猫 | 250 | 159 |

**"网站登记但该图刷不出"前 10 条**（全量见 `spawn.maps[].monsters`）：
`D2204 Escort Commander 888`、`D1205 Pink Goddess Of Underground 750`、`D1205 Vicious Mutant Captain 600`、
`D1011 Frost/Shock/Fury/Flame/Minotaur 581/581/581/581/572`、`D1011 Banya Left/Right Guard 570/570`。

> 注：这些地图本身仍在当前库（否则会被归入"地图已移除"）。`D1011`/`D1205`/`D2204` 这类图在当前库
> 换了刷怪内容（非经典怪被清掉），因此基准里登记的怪不再出现；之所以仍算"图存在"，是因为文件名匹配上了。

### 6.2 掉落（`DropInfo`）

| 指标 | 值 |
|---|---|
| 当前库 `DropInfo` 行数 | 5235（基准 10382） |
| 悬空外键（怪物 / 物品） | **0 / 0** |
| 当前库空引用 | **0**（基准库里原有 1 行 `DropInfo.Monster` 空、4 行 `RespawnInfo.Region` 空，已消失） |
| 零掉落行数的怪物 | **37** 行（其中 19 行基准库本来有掉落 → 见下） |
| 物品级：有物品丢失 / 有新增 | **94 只 / 67 只** |
| 同一 (怪物, 物品) 重复登记的行 | **2884** 行 |

**确认缺陷：12 只"仍在刷怪 + 基准库本来有掉落 + 现在 0 行掉落"**

| 怪物 | 基准掉落行 | 当前 | 现刷怪总量 | 丢失物品样例 |
|---|---|---|---|---|
| Ghost Mage (33) | 79 | 0 | 310 | Carrot、Gold、Healing Potion … |
| Voracious Ghost (34) | 76 | 0 | 375 | Carrot、Gold、Cloak … |
| Devouring Ghost (35) | 76 | 0 | 375 | Carrot、Gold×360、Zombie Bone … |
| Corpse Raising Ghost (36) | 76 | 0 | 375 | Carrot、Gold、Healing Potion … |
| Bone Soldier (119) | 72 | 0 | 146 | Amber Necklace、Bamboo Necklace … |
| Skeleton Enforcer (120) | 76 | 0 | 2 | … |
| Mutant Lizard (106) | 42 | 0 | 490 | … |
| Numa Grunt (89) | 23 | 0 | 2335 | Gold×1800、Jewel、Healing Potion(II) … |
| Venomous Arachnid (71) | 20 | 0 | 400 | … |
| **Deer 鹿 (10)** | 10 | 0 | 191 | **Deer Meat**、Carrot、Crystal … |
| Scorpion 蝎子 (25) | 8 | 0 | 60 | **Scorpion Tail**、Carrot … |
| Sabuk Lord (220) | 3 | 0 | 800 | White Whistle、Crystal、Masters Emblem |

另有 7 只（`Corpse Devourer`/`Visceral Worm`/`Crazed Warrior`/`Bone Captain`/`Tusk Lord`/`Jin Skeleton`/`Shinsu`）
同样是"基准有掉落、现在 0 行"，只是当前不刷怪 → 目前对玩家无影响，但仍属回归。
剩余 18 只零掉落是**设计如此**（4 守卫、宠物行 189–198、`Larva`、`Skeleton(144)`、`Dungeon Goddess 1`），
基准库同样是 0 行。

**参考：零掉落 37 行的构成** = 19 行"基准有→现在无"（12 只在刷、7 只不刷）+ 18 行"基准也无"（设计）。

行数维度另有：25 只行数变少、49 只变多（变多与"同一物品多行"（2884 行）相关，需确认是否是有意的多次摇奖）。

---

## 七、怪物资源可用性

| 检查 | 结果 |
|---|---|
| `MonsterInfo.Image` 能否在 `GodotClient/Formats/MonsterLookup.cs` 找到 | **116/116 全部命中**（0 条需补） |
| 对应 `Mon-N.Zl` 是否存在（`~/mir2ei/Data/`） | 用到的 26 个库**全部存在** |
| 形状块帧存在性（`FrameSet.DefaultMonster` 动作表，8 方向） | 99 只全动作齐；26 只用自定义 FrameSet（仅容器级验证）；**1 只**单方向固定怪（`Dungeon Goddess 2`）；**0 失败** |
| 与 `zlsdk.py` 逐帧交叉复核 | 126 个怪物形状，**0 不一致** |

**卡点（验证深度，不是缺陷）**：26 只使用自定义 FrameSet（`ForestYeti`/`ZumaKing`/`CarnivorousPlant`/
`Companion_*` 等，来自 `LibraryCore/FrameSet.cs` 的 97 个命名集）的怪物只验证了"库存在 + 形状块有帧"，
**没有**按各自 FrameSet 的方向数/帧数逐动作断言；客户端 `SetAnimation` 里的条件分支（如 `Shinsu` 的
`Extra` 分支）也未建模。下一步命令：

```bash
# 只对自定义 FrameSet 怪物补动作级断言（先找 FrameSet.cs 对应块）
rg -n "ForestYeti|ZumaKing|Companion_Pig" LibraryCore/FrameSet.cs   # 取 start/count/offset
# 再用脚本内 ZlProbe.has_frame(base + start + dir*offset + k) 逐动作核对
```

---

## 八、风险与修复建议（分级）

| ID | 等级 | 分级 | 结论 | 证据等级 | 建议动作 |
|---|---|---|---|---|---|
| **R1** | 高 | 已解释（清洗）＋**口径待裁决** | 202 只怪物缺失（含 50 BOSS；连带 975 行刷怪 + 6467 行掉落） | 数据级对账（1:1 闭合） | 若网站基准=目标版本 → 回填 `MonsterInfo`+`MonsterInfoStat`+`RespawnInfo`+`DropInfo`；若经典化=目标 → 更新网站 `ver` 标签，避免重复判缺口 |
| **R2** | 高 | **确认缺陷** | **15 只怪物 `MonsterInfoStat` 0 行**（其中 14 只仍在刷怪）。`Stats` 索引器缺键返回 0（`LibraryCore/Stat.cs:18`），`MonsterObject.RefreshStats` 只叠加 `MonsterInfo.Stats`（`ServerLibrary/Models/MonsterObject.cs:735`）→ 生命/攻防全 0 | 静态代码推断（**未实机验证**） | 为 388/390/395/396/409/412/416/417/418/421/425/448/449/458/484 补属性行；数值参照基准库同类怪与网站 `attrs` |
| **R3** | 中 | 确认缺陷 | 5 个名字无中文显示名（4 守卫 + `'Pachon '`） | 数据级对账 + `LocalizedName` 回落路径 | 补 `db_names.json`；把 `MonsterInfo.MonsterName='Pachon '` 的尾空格修掉 |
| **R4** | 中 | 待人工确认（含确认子集） | 77 条显示名与网站不一致；`Pig→蛤蟆` 等 3 条属确认缺陷 | 第三方交叉验证（MUD3 DAT） | 先修 3 条脏名，其余按 17173 口径批量裁决 |
| **R5** | 中 | 待人工确认 | 94 只有物品丢失 / 67 只有新增 | 数据级对账 | 核对丢失物品是否在清洗白名单内；新增需确认非重复登记 |
| **R6** | 中 | 待人工确认 | 刷怪覆盖与密度：175 张图刷怪消失；现存图 327/397 组无刷怪；比奇城 600→19 | 数据级对账（基准 ΣCount 已校验） | 先裁决密度口径，再逐图核对 `not_spawned` |
| **R7** | 高 | **确认缺陷** | **12 只仍在刷怪的怪物 0 掉落行**（含鹿/蝎子/矿区幽灵系/诺玛小卒） | 数据级对账（基准库 + 网站文本） | 按经典骨架重建 `DropInfo`（金币 + 肉/材料 + 药水 + 经典装备），核对 `Chance` 分母 |
| **R8** | — | 已解释（无问题） | 引用完整性：怪物相关外键 0 悬空（基准库原有的 5 处空引用已消失） | 数据级对账 | 写进回归检查 |
| **R9** | — | 已解释（通过）＋验证深度待补 | 资源 126 个形象全部可解析；26 只仅容器级验证 | 资源探测 + zlsdk 交叉 | 补 26 只自定义 FrameSet 的动作级断言 |

> 期望：实机验证 > 静态代码推断 > 猜测（`docs/AUDIT/README.md` §4.3）。
> 本报告数据级结论均属"数据级对账（可复现）"；R2 的**后果**属静态代码推断，实机（怪物血量条/一击秒杀）
> 尚未验证；资源类结论属资源级探测。

---

## 九、复现

```bash
# 0) 依赖：清洗前基准库导出（§2.1，只读，落在 /tmp）
#    —— 不提供它脚本也能跑，但会失去「缺失怪物归因」与「基准库自校验」两段

# 1) 主对账（默认路径即可）
cd /Users/tetsuya/Development/Zircon
python3 tools/audit/monsters_audit.py --crosscheck-zlsdk
# macOS 本机 python3 可能需显式 python3.14

# 2) 只看摘要（stdout）
python3 tools/audit/monsters_audit.py --crosscheck-zlsdk --no-resources

# 3) 关键结果查询
python3 - <<'PY'
import json
d = json.load(open('.artifacts/game-audit-2026-10-18/data/monsters.json'))
print(len(d['coverage']['absent_from_current_db']), 'missing monsters')
print(d['field_diff']['current_db_monsters_without_stats'])
print(d['drop']['item_level_summary'])
print([ (r['id'], r['kind'], r['title']) for r in d['risks'] ])
PY
```

脚本参数：`--wiki` / `--db-export` / `--baseline-dir` / `--client-data` / `--out` / `--no-resources` / `--crosscheck-zlsdk`。
默认值写死为本机 macOS 绝对路径（wiki 快照、`~/audit-raw/db-export-2026-10-18/out`、`~/mir2ei/Data`、`/tmp/preclean_out`、Zircon 仓库），
换机/换快照时用参数覆盖即可；脚本本身无网络、无写库、无副作用（只写 `--out`）。

---

## 十、未决与限制（诚实清单）

1. **R2 的实际游戏表现未实机验证**：结论"生命/攻防=0"来自代码路径推断（`Stat` 索引器 + `RefreshStats`），
   未在实机里观察这 15 只怪的血条与一击伤害。建议实机复核（怪物截图巡检正排在队列里）。
2. **26 只自定义 FrameSet 怪物未做动作级断言**：不是失败，是未验证；需按各自 FrameSet 参数补测。
3. **刷怪"数量"口径未裁决**：当前库小区块刷怪（Count 2–5 / Delay 10）与基准整图刷怪（Count 150–600）
   不是同一种模型，本报告只断言"有/无"，数量差异单列为待确认。
4. **掉落"变多"未定性**：2884 行同一 (怪物, 物品) 重复登记，需判断是有意的多次摇奖还是导入重复。
5. **宠物行（189–198）不在"无属性行"名单里**：它们只有 `CompanionInventory`/`CompanionBagWeight` 两行，
   战斗属性来自 `CompanionLevelInfo`（15 行）。这是设计，不是缺陷，但本报告未审计宠物数值曲线（不属怪物族群）。
6. **R1 的裁决不是技术问题**：202 只缺失是"网站基准 vs 经典化清洗"两种目标版本之间的取舍，
   需要项目所有者定口径后才能出修复清单。

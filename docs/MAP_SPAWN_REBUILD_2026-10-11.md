# 地图刷怪全量重建与 Mud3 对照报告（2026-10-11）

> 本轮两个任务的完整过程记录（含 NPC 部分）见 [`WORK_RECORD_2026-10-10_11_NPC_AND_SPAWN.md`](WORK_RECORD_2026-10-10_11_NPC_AND_SPAWN.md)。

> 目标：逐张地图核对刷怪信息，与原版 Mud3（EI 3.0 ORIGIN）刷怪表对照，
> 修正错误的怪物/数量/落点，并用**服务端真值**逐图验证。

## 0. 一句话结论

| 指标 | 重建前 | 重建后 |
|---|---|---|
| 刷怪条目（RespawnInfo） | 1007（含 35 条 0 落点=永不刷、1 条空地图、2 条 count=0 的垃圾） | **2156（0 异常条目）** |
| 有刷怪的图 | 196 | **282** |
| 与 Mud3 刷怪表一致的图 | — | **210 张图按 Mud3 重建**（1770 条刷怪行） |
| 城镇刷怪 | 边境城市/银杏山谷等**完全没有刷怪** | 按 Mud3 补齐（如 01=415 只、02=499 只） |
| 怪物种类错误 | 比奇刷 Oma Warrior/Tiger Snake 等 Mir2 遗留怪，数量 200-600 | 按 Mud3（鸡/猪/牛/鹿/稻草人/多钩猫/钉耙猫/蛤蟆/狼/森林雪人/食人花…），数量按原表 |
| 单图怪量荒谬 | D2902=4800（自定义图，保留）、比奇 600 只食人花 | 比奇 627 只（按 Mud3 各点求和） |

## 1. 数据源（权威）

| 数据 | 位置 | 用途 |
|---|---|---|
| Mud3 刷怪表 | `Mir3-Research/.../Mud3-Config/Envir3/Mon_Def/*.gen`（63 个文件、2415 行）+ `MonGen.txt`（loadgen 清单） | 权威刷怪记录：`地图 x y 怪物 范围 数量 间隔(分) [小刷率]` |
| Mud3 怪物表 | `local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/monster.dat`（433 条，XOR 0x09/记录 252B） | 怪物名真值（`tools/mud3_monster_dat.py` 解码） |
| Mud3 地图表 | `Envir3/MapInfo.txt` | 地图代号 ↔ 中文名（0=比奇、01=边境、02=银杏、1=道馆、2=蛇谷、3=沙巴克、4=绿洲、5=沙漠、31=祖玛神殿…） |
| 现役怪物映射 | `Tools/ClassicMagicFixer/CanonicalMonsters.cs`（147 条 中文名→MonsterInfo.Index） | 中文怪名 → Zircon 怪物 |
| 现役刷怪导出 | `Tools/ClassicMagicFixer mobgraph` | 落库前后对照 |

`.gen` 的字段含义（`MonGen.txt` 解析器：Mud3 源码 `LocalDB.pas LoadZenLists` §14.2）：
第 5 列=范围（半径，格）、第 6 列=数量、**第 7 列=刷怪间隔，单位分钟**（代码里 ×60×1000 转毫秒，
与 Zircon `RespawnInfo.Delay` 的分钟语义一致）。

## 2. 发现的问题（数据实证）

1. **城镇/副本整片没有刷怪**：边境城市(01)、银杏山谷(02)、沙漠(5)、潘夜(8)、以及大量 D10xx/D15xx
   副本在 Mud3 里都有刷怪表，但现役库**一条都没有**。
2. **怪物种类不对**：比奇(0) 现役刷 `Oma Warrior/Tiger Snake/Spitting Spider/Oma Hero` 等 Mir2 遗留怪，
   而 Mud3 的比奇是 `多钩猫/钉耙猫/稻草人/鹿/蛤蟆/狼/森林雪人/半兽人/食人花`。
3. **数量荒谬**：现役"刷怪环"（`Spawn Ring 1/2`）把整图当刷怪区，单条 count 高达 250-600；
   Mud3 是"每个点位 2-5 只"。
4. **损坏条目**：35 条 0 落点（`Whole Map` 空点集 → 服务端 `Spawn` 直接 false，永不刷）、
   1 条空地图、2 条 count=0。
5. **地图名大小写不一致**：DB 里同时存在 `D713`/`d713`、`D903`/`d903` 这类写法，
   与 Mud3 代号大小写不同 → 早期对照把它们误判为"地图缺失"（63 张图、346 条刷怪行）。
   客户端已同步修复 `.map` 文件大小写不敏感解析（见 NPC 报告 4.1）。

## 3. 做法（可复现流水线）

```
Envir3/Mon_Def/*.gen ──(tools/mud3_spawn_plan.py)──> /tmp/spawn_plan.json
        │  （地图名大小写不敏感匹配 + CanonicalMonsters 中文怪名映射）
        ▼
ClassicMagicFixer rebuildspawns <RootDir> <plan.json> <MapDir>   → System.db（4 处镜像 + SHA256）
```

落库规则：
* **计划覆盖的地图**：删除该图现有 RespawnInfo（以及不再被引用的 MapRegion），按 `.gen` 重建；
* **计划未覆盖的地图**：保持原样（Mud3 数据集没有这些图的刷怪行，无从对照）；
* 每个 `(地图,x,y,范围)` 生成一个 `MapRegion`，点集 = 该方块内的**可走格**（读 `.map` 判定
  `(flag & 0x02) == 2 && (flag & 0x01) == 1`，与 `Map.Load` 的 ValidCells 同规则），
  抽样上限 150 点/区域，避免 DB 膨胀；
* 每个怪物一条 RespawnInfo：`Count`=第 6 列、`Delay`=第 7 列（分钟）、`EventSpawn=false`、
  `Announce=false`（Mud3 的喊话在 `GenMsg.txt`，本数据集为空）、`RespawnIndex=0`；
* **顺带清理**：`Monster==null / Region==null / Region.Map==null / Count<=0 / 落点为空` 的条目一律删除。

变体怪（`.gen` 里带尾数字的名字，如 `多钩猫0`/`僵尸1`/`森林雪人0`，Mud3 里是独立记录）
按**去尾数字的基础怪**映射（Zircon 库内只有基础怪），共 609 行；属已知近似。

## 4. 结果

* 计划：`.gen` 2415 行 → **491 个区域 / 1772 条刷怪 / 覆盖 210 张图**（其中 2 个区域因坐标落在墙内、无可走格而跳过：`D9031(10,10 r5)`、`D9032(15,15 r15)`，各少 1 只）；
* 落库：删除旧刷怪 565 条 + 覆盖图重建 1770 条、删除空区域 433 个、**新建区域 489 个 / 新建刷怪 1770 条**、
  清理损坏条目 39 条；
* 终态：**RespawnInfo 2156 条 / 282 张图 / 0 异常**。

## 5. 验证（服务端真值逐图核对）

工具：新增 GM 命令 **`@mobcensus [地图]`**（`ServerLibrary/.../Admin/MobCensus.cs`）——
打印指定地图上**活着的怪物**按种类统计，并追加一行到 `/tmp/mobcensus.txt`；
配合 `tools/run_spawn_audit.sh`（逐图 `@move` 加载 → 等刷怪 → `@mobcensus`）与
`tools/mud3_spawn_verify.py`（计划 vs 真值逐图逐怪比对）。

* **210 张图全部跑到**：`census 覆盖=210 / 完全一致=206 / 数量不符=4 / 无 census=0`
  （`docs/spawn_audit/verify_summary.txt`、`mobcensus_raw.txt`）；
* 4 处差异全部查明且属正常范围：
  | 地图 | 差异 | 原因 |
  |---|---|---|
  | map 12 | Arachnid Gazer 160→158、Dark Arachnid 60→58 | 刷怪落点被占/20 次尝试失败（约 1-3%） |
  | map 8 | Decaying Ghoul 1008→1003 | 同上（0.5%） |
  | D9031 / D9032 | Otherworld Poison Demon / Ship Guard 各 1→0 | `.gen` 给的点位落在墙内（无可走格），落库时该区域被跳过 |
* **加载时序**要点：`@mobcensus` 会按需加载地图，刚加载时刷怪循环还没跑 → 首次统计可能为 0；
  驱动脚本因此每图统计两次（间隔 8s）。例如 D1011 隔 25 秒再统计即得
  `Red Moon Guardian=37 / Red Moon Protector=38`，与 Mud3 计划完全一致；
* 玩家可见证据：比奇县 `Claw Cat=159, Pig=101, Scarecrow=53, Chicken=50, Cow=50, Deer=44, Oma=39,
  Forest Yeti=35, Wolf=32, Carnivorous Plant=19`（与 Mud3 各点位求和完全一致）——
  截图 `docs/screenshots/spawn_audit/01_bichon_census.png`。

## 6. 缺口（本次无法用 Mud3 对照的部分，逐条列明）

| 缺口 | 数量 | 说明 |
|---|---|---|
| `.gen` 引用但 DB 里没有的**地图** | 308 行 / 42 张 | 如 D15014/D2101/D2103/D712/D1512… 这些图在现役库中不存在（经典化清洗时移除）；需先恢复地图才能落刷怪 |
| `.gen` 引用但 DB 里没有的**怪物** | 335 行 / 49 种 | 如 僧侣僵尸(D401-406 骷髅洞)、角蝇、爆毒蚂蚁、劳动蚂蚁、赤血/灰血恶魔、骷髅武将、胞眼虫、诺玛系列、冰原/冰宫系列。这些怪在 2026-10-03 的「经典纯净清洗」中被删，且客户端本地化表里也没有中文名 → 恢复属**内容补录**任务（怪物记录+名字+外观） |
| Mud3 数据集**没有刷怪行**的图 | 90 张 | 如 31 祖玛神殿、122/125 灌木林、41 诺玛沙漠、D2901(会员练级)/D2902(BOSS集中营) 等；保持现役数据，未做改动。其中 D2901/D2902 是私服自定义地图 |

> **用户裁决（2026-10-11）**：这两类缺口**保持现状**——210 张图已按 Mud3 重建并验证，
> 缺失的怪物/地图暂不恢复（恢复会与 2026-10-03「经典纯净清洗」的决定相反，且需要先做
> Mud3 怪物身份 ↔ Zircon MonsterImage 的映射工程）。后续若要做，按本节的清单逐条推进即可。

## 7. 复现

```bash
# 1) 导出现役刷怪（只读）
dotnet run --project Tools/ClassicMagicFixer -- mobgraph <DB目录> /tmp/mob_graph.json
# 2) 解码 Mud3 怪物表（确认中文怪名真值）
python3 tools/mud3_monster_dat.py --out /tmp/mud3_monsters.json
# 3) 生成落库计划
python3 tools/mud3_spawn_plan.py --mob /tmp/mob_graph.json --out /tmp/spawn_plan.json
# 4) 落库（必须先停服；自动同步 4 处 System.db）
dotnet run --project Tools/ClassicMagicFixer -- rebuildspawns \
    /home/tetsuya/development/zircon/ /tmp/spawn_plan.json \
    /home/tetsuya/development/zircon/Debug/ServerCore/Map/
# 5) 实机核对：客户端进游戏后
bash tools/run_spawn_audit.sh <地图清单> :150 12
python3 tools/mud3_spawn_verify.py --plan /tmp/spawn_plan.json --census /tmp/mobcensus.txt
```

> 注：`@mobcensus` 是本次新增的审计用 GM 命令（聊天回复 + 追加 `/tmp/mobcensus.txt`），
> 与 `@giveGold` 同属"为了能验证"而加的最小工具。

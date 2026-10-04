# 大刀守卫 / 弓箭守卫 / 带刀卫士 地图坐标审计（2026-10-04）

## 0. 结论摘要

| 结论 | 置信度 |
| --- | --- |
| MUD3 原版 `GuardList.txt` 的 **32 条守卫摆放**，在 Zircon System.db 中 **地图 / X / Y / 朝向 100% 完全一致** | 高（逐条字段比对，见 §3） |
| 三种守卫在 Godot 客户端 **均能正常渲染**（含大刀守卫、弓箭守卫、带刀侍卫） | 高（游戏内实拍截图，见 §5） |
| 修复 2 处数据缺陷：①银杏山谷 14 条**完全重复**的守卫记录；②银杏山谷弓箭守卫 **#158 落在不可站立格，永不刷出** | 高（修复前后服务端日志 + round-trip 复检，见 §6） |
| 「现在没有出现在地图里」**不是坐标错位导致**：原版坐标没有一条丢失；主因是视野/遮挡/惰性加载 + 上述 1 条永不刷出的弓箭守卫 | 中高（见 §7） |
| 弓箭守卫 16 条、带刀侍卫 4 条为 **Zircon 自加摆放，原版无对照**，无法做「与原版一致」校验 | 高（原版全文检索，见 §2） |

---

## 1. 名称对应

用户口称的三个名字与代码/原版的对应关系（**「带刀卫士」在两仓库中均无出现**，按语义就近对应）：

| 口称 | Zircon `MonsterInfo` | `MonsterImage` | 图库/Shape | AI | MUD3 对应 |
| --- | --- | --- | --- | --- | --- |
| 大刀守卫 | `Guard` #1 | `Guard = 36` | `Mon-3.Zl` shape 6 | -1（`MonsterObject.GetMonster` 转 `GuardObject`） | `卫士`（`GuardList.txt` 32 条全为它） |
| 弓箭守卫 | `ArcherGuard` #5 | `ArcherGuard = 96` | `Mon-9.Zl` shape 6 | -3（→ `ArcherGuardObject`） | 名字见 `Abusive.txt:645`，**无任何摆放记录** |
| 带刀卫士 | `TownGuard` #2 | `TownGuard = 124` | `Mon-12.Zl` shape 4 | -1 | `带刀侍卫`（`MonAis.txt:12`、`Monitems.ini:377`），**无任何摆放记录** |

`GodotClient/Formats/MonsterLookup.cs` 中四种守卫的图库映射齐全；`Mon-3.Zl / Mon-9.Zl / Mon-12.Zl` 均存在。

---

## 2. 原版证据（MUD3）

权威摆放文件：`/data/NAS/TMP/EI3.0英雄杀服务端/Mud3/Envir/GuardList.txt`（**GB18030 编码**）

格式：`怪物名  图名  x,y : 朝向`，共 32 行有效记录，全部怪物为 `卫士`，只分布在 3 张图：

| 原版图名（`MapInfo.txt`） | 文件名 | 条数 | Zircon `MapInfo` |
| --- | --- | --- | --- |
| 比奇城 | `0` | 4 | Index 1 `Bichon Town` |
| 道馆 | `1` | 11 | Index 5 `Lost Paradise` |
| 银杏山谷 | `02` | 17 | Index 616 `银杏山谷` |

**编码注意（本次踩坑记录）**：`grep "卫士"` 对该文件恒返回 0 —— 原版是 GB18030，必须解码后再匹配。
按 GB18030 全量检索 `Mud3/` 后：

| 关键词 | 命中文件 | 说明 |
| --- | --- | --- |
| `卫士` | `Envir/GuardList.txt`(32)、`Monitems.ini`(卫士/卫士1)、`StrRes.txt`(雇佣守卫文案)、`Mon_Def/*.gen`(仅**沃玛卫士/祖玛卫士**，另一类怪) | 原版城防守卫摆放 = GuardList 这 32 条 |
| `弓箭守卫` | 仅 `Mir3Server/DBserver/Abusive.txt:645`（屏蔽词表） | **原版无摆放、无爆率表** → 原版没有弓箭守卫站岗 |
| `带刀` | `MonAis.txt:12`、`Monitems.ini:377` | 原版有 `带刀侍卫` 的 AI 脚本与爆率表，**但无摆放记录** |

---

## 3. 逐条对照：原版 ↔ 当前（修复后）

字段：`图 / X,Y / 原版朝向 / 当前怪物 / 当前朝向 / 当前 GuardInfo Index`。
朝向数值按 `LibraryCore/Enum.cs` `MirDirection`：`0=Up 1=UpRight 2=Right 3=DownRight 4=Down 5=DownLeft 6=Left 7=UpLeft` —— 与 MUD3 数值编码一致。
**全部 32 条：地图、坐标、朝向三项全中。**

### 3.1 银杏山谷 `02`（17 条）

| X,Y | 原版朝向 | 当前怪物 | 当前朝向 | Index |
| --- | --- | --- | --- | --- |
| 232,180 | 7 UpLeft | Guard | UpLeft | 96 |
| 228,184 | 7 UpLeft | Guard | UpLeft | 97 |
| 244,168 | 7 UpLeft | Guard | UpLeft | 98 |
| 247,165 | 7 UpLeft | Guard | UpLeft | 99 |
| 274,167 | 1 UpRight | Guard | UpRight | 100 |
| 277,170 | 1 UpRight | Guard | UpRight | 101 |
| 283,176 | 1 UpRight | Guard | UpRight | 102 |
| 287,180 | 1 UpRight | Guard | UpRight | 103 |
| 289,205 | 3 DownRight | Guard | DownRight | 104 |
| 286,208 | 3 DownRight | Guard | DownRight | 105 |
| 277,233 | 3 DownRight | Guard | DownRight | 106 |
| 274,236 | 3 DownRight | Guard | DownRight | 107 |
| 229,239 | 5 DownLeft | Guard | DownLeft | 108 |
| 227,237 | 5 DownLeft | Guard | DownLeft | 109 |
| 244,194 | 3 DownRight | Guard | DownRight | 128 |
| 253,187 | 5 DownLeft | Guard | DownLeft | 129 |
| 259,193 | 5 DownLeft | Guard | DownLeft | 130 |

### 3.2 道馆 `1`（11 条）

| X,Y | 原版朝向 | 原版怪物 | 当前怪物 | 当前朝向 | Index |
| --- | --- | --- | --- | --- | --- |
| 371,160 | 5 DownLeft | 卫士 | **TownGuard** | DownLeft | 131 |
| 375,164 | 5 DownLeft | 卫士 | **TownGuard** | DownLeft | 132 |
| 368,112 | 7 UpLeft | 卫士 | **TownGuard** | UpLeft | 133 |
| 372,108 | 7 UpLeft | 卫士 | **TownGuard** | UpLeft | 134 |
| 414,166 | 3 DownRight | 卫士 | **TownGuard** | DownRight | 135 |
| 417,163 | 3 DownRight | 卫士 | **TownGuard** | DownRight | 136 |
| 411,115 | 5 DownLeft | 卫士 | **TownGuard** | DownLeft | 137 |
| 414,118 | 5 DownLeft | 卫士 | **TownGuard** | DownLeft | 138 |
| 385,119 | 3 DownRight | 卫士 | **TownGuard** | DownRight | 139 |
| 410,131 | 5 DownLeft | 卫士 | **TownGuard** | DownLeft | 140 |
| 326,96 | 3 DownRight | 卫士 | **TownGuard** | DownRight | 141 |

> **唯一差异：模型被替换。** 坐标/朝向与原版逐条一致，但怪物由 `Guard`(大刀守卫) 换成 `TownGuard`(带刀侍卫)，
> 由 `Tools/ClassicMagicFixer/SetupGuardsSystem.cs` 的「道馆 → TownGuard」步骤主动设置（见 `docs/SYSTEM_COMPANION_MONSTERS_AND_PURITY_ALIGNMENT_2026-10-04.md`）。
> **是否改回原版大刀守卫属产品决策，本次未改动。**

### 3.3 比奇城 `0`（4 条）

| X,Y | 原版朝向 | 当前怪物 | 当前朝向 | Index |
| --- | --- | --- | --- | --- |
| 457,359 | 3 DownRight | Guard | DownRight | 142 |
| 455,361 | 3 DownRight | Guard | DownRight | 143 |
| 453,363 | 3 DownRight | Guard | DownRight | 144 |
| 451,365 | 3 DownRight | Guard | DownRight | 145 |

---

## 4. 当前 GuardInfo 全量（修复后 96 条）

| 图 | 地图 | Guard | TownGuard | ForestGuard | ArcherGuard | 与原版关系 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `0` | Bichon Town 比奇 | 23 | - | - | 8 | 原版 4 条全中 + **19 大刀 + 8 弓箭为自加** |
| `1` | Lost Paradise 道馆 | - | 15 | - | - | 原版 11 条全中（模型换成 TownGuard）+ **4 条自加** |
| `02` | 银杏山谷 | 17 | - | - | 8 | 原版 17 条全中（修复后重复已删）+ **8 弓箭自加** |
| `01` | 边境城市 | 9 | - | - | - | **原版无此图摆放**，全为自加 |
| `2` | Banya Village（启动预载图） | 7 | - | - | - | 全为自加 |
| `12` | Banya Island | 5 | - | - | - | 全为自加 |
| `74` | 盟重县 | - | - | 4 | - | 全为自加 |

自加记录的坐标**全部落在可站立格**（离线校验，规则同 `ServerLibrary/Models/Map.cs:82`），但**原版没有对照，无法做「与原版一致」校验** —— 若目标是「完全复刻原版摆放」，这 47 条需要产品决策是否保留/删除，本次未动。

---

## 5. 运行时验证（行为验证 ≥ 编译验证）

验证链路：`Xvfb :100` + Godot 4.6.3 mono + 本地 `ServerCore`（端口 7000）+ 测试号 `test@test.com / TestHero`，
用 `/tmp/movetool`（MirDB Users 会话，**服务端停止时**改 `CurrentMap/CurrentLocation`）把角色放到指定格，登录后 `ffmpeg x11grab` 抓帧。

| # | 位置 | 观察 | 证据 |
| --- | --- | --- | --- |
| 01 | 比奇 `0` (454,362) | **4 名大刀守卫**沿城墙列队渲染，即原版 4 条坐标 | `docs/screenshots/guards/01_bichon_original_mud3_guards_454_362.png` |
| 02 | 比奇 `0` (143,214) | 收到 31 个怪物包（=23 大刀 + 8 弓箭全数下发），树木遮挡导致画面只露 1~2 个 | `docs/screenshots/guards/02_bichon_gate_guards_and_archers_143_214.png` |
| 03 | 道馆 `1` (400,130) | 收到 4 个怪物包，带刀侍卫模型渲染正常 | `docs/screenshots/guards/03_dao_town_townguards_400_130.png` |
| 04 | 银杏山谷 `02` (284,205) | **2 名弓箭守卫（持弓+箭袋）+ 「卫士」悬停名标签** 清晰可见 | `docs/screenshots/guards/04_ginkgo_guards_archers_284_205_postfix.png` |
| 05 | 同上局部放大 | 弓箭守卫模型细节 | `docs/screenshots/guards/05_ginkgo_archers_zoom_postfix.png` |
| 06 | 同上局部放大 | 悬停名 `卫士`（`chinese_alias.json`: `卫士 = Guard`） | `docs/screenshots/guards/06_ginkgo_guard_weishi_zoom_postfix.png` |

服务端侧：`Map.CreateGuards()` 失败会打 `Failed to spawn Guard ...`。启动预载的 3 张图（比奇 `0`、盟重 `2`、道馆 `1`）
在修复前后均为 **0 条失败**；银杏山谷 `02` 为惰性加载，进图时才有结论（见 §6）。

---

## 6. 缺陷与修复

### 缺陷 A：银杏山谷 14 条完全重复的守卫记录（已删）

`GuardInfo` #114–#127 与 #96–#109 **地图 / X / Y / 怪物 / 朝向五项全部相同**。
`MapObject.Spawn()` 只校验格子是否可站立、不校验占位，因此两条都会刷出 → **同格叠放双怪**（伤害翻倍、渲染重叠、记录数虚高）。

修复：删除 #114–#127。修复后银杏山谷大刀守卫 = **17 条 = 原版 17 条，不多不少**。

### 缺陷 B：弓箭守卫 #158 落在不可站立格，永不刷出（已挪格）

`SetupGuardsSystem.cs` 硬编码部署 `(287, 203)`，该格 `.map` flag 不含 `0x02|0x01`，
`Map.Load()` 会跳过该格（`ServerLibrary/Models/Map.cs:82`）→ `GetCell()` 返回 null → `Spawn()` 返回 false → **这只守卫从未出现过**。

- 修复前（14:42:49 进图）：`Failed to spawn Guard Map:银杏山谷, Location: 287, 203`
- 修复后（15:01:56 进图）：`Failed to spawn 计数: 0`
- 修复：挪到最近可站立且未被占用的格 `(286,203)`；同步修掉 `SetupGuardsSystem.cs` 里同名硬编码（否则工具重跑会再插回坏格）。

证据：`docs/screenshots/guards/server_log_02_before_after_fix.txt`

### 修复手段（可复现）

新增 `Tools/ClassicMagicFixer/GuardAligner.cs`，按 `Map.cs:82` 同一规则扫描全部 `GuardInfo`：不可站立→挪到最近有效格；同图同格同怪→只留最小 Index。

```bash
# 服务端必须先停（写库纪律）
pkill -f "[S]erverCore\.dll"
dotnet build Tools/ClassicMagicFixer/ClassicMagicFixer.csproj
# 预检（只读）
dotnet run --project Tools/ClassicMagicFixer/ClassicMagicFixer.csproj --no-build -- \
  guardfix /home/tetsuya/development/zircon/Debug/ServerCore/Database/ --dry-run
# 执行 + 四镜像同步 + MD5 校验
dotnet run --project Tools/ClassicMagicFixer/ClassicMagicFixer.csproj --no-build -- \
  guardfix /home/tetsuya/development/zircon/Debug/ServerCore/Database/
# round-trip 复检（应输出 0 条）
dotnet run --project Tools/ClassicMagicFixer/ClassicMagicFixer.csproj --no-build -- \
  guardaudit /home/tetsuya/development/zircon/Debug/ServerCore/Database/
# 全量导出核对
dotnet run --project Tools/ClassicMagicFixer/ClassicMagicFixer.csproj --no-build -- \
  guarddump /home/tetsuya/development/zircon/Debug/ServerCore/Database/
```

写库纪律执行情况：写前手工备份 `Database/Backup/System.db.before-guardfix-20261004-145211`；
写库时服务端已停止；写后 4 处 `System.db` MD5 全部一致 = `73597601dba191b3e1cc8e6caacce64f`；
`guardaudit` 复检重复=0、无效格=0。
注：`System.db` 被 `.gitignore` 忽略，**数据库改动不随 git 分发**，测试机需自行同步这 4 处文件。

---

## 7. 为什么「看起来没出现在地图里」

按可能性排序，前 3 条是主因，第 4 条是本次修掉的真 bug：

1. **视野与遮挡**：守卫分散在 7 张图；镜头只渲染玩家周围 ~18 格，且**树木/屋顶绘制在角色之上**。
   实测站在 (250,172) 时，同屏 6 只守卫/弓箭手中多数被屋顶与松树完全挡住（对比 `02` 与 `04` 截图）。
2. **惰性加载**：`Config.LazyLoadMaps=True`，启动只预载比奇 `0`、盟重 `2`、道馆 `1`；
   银杏山谷 `02`、边境城市 `01` 等**进图那一刻才创建守卫**，从后台日志看不到它们的生成过程。
3. **站位本身在城外**：比奇原版 4 条在 (451~457, 359~365) 的城墙外侧，出生点附近走一圈未必遇到；
   银杏山谷 17 条沿村外一圈分布，站在村中心也看不到。
4. **数据缺陷**（已修）：1 只弓箭守卫因落点无效**永远**不刷出；14 条重复记录造成同格叠放，
   让「按记录数对账」与「实际看到的守卫数」对不上。

---

## 8. 未覆盖项与待决策

| 项 | 状态 | 说明 |
| --- | --- | --- |
| 地图 `01` / `12` / `74` / `2` 的守卫进图实测 | **未做**（离线校验通过） | 这 4 张图的记录离线校验全部有效且无重复；受「一图一次换图循环」限制未逐图进场。启动图 `0`/`1`/`2` 与 `02` 已实测 0 失败。 |
| 47 条原版无对照的自加守卫 | 待决策 | 若目标是「完全等于 MUD3 摆放」需删除或迁移，本次未动 |
| 道馆 11 条由 `卫士`→`TownGuard` | 待决策 | 坐标一致，仅模型替换；改回即一行工具逻辑 |
| 弓箭守卫 16 条坐标 | 无原版对照 | 只能校验「能刷出/能渲染」，已验证 |

## 9. 本次改动清单

| 类型 | 路径 | 说明 |
| --- | --- | --- |
| 新增工具 | `Tools/ClassicMagicFixer/GuardAligner.cs` | 守卫挪格 + 去重 + 四镜像同步（含 `--dry-run`） |
| 工具入口 | `Tools/ClassicMagicFixer/Program.cs` | 新增 `guardfix` / `guardaudit` / `guarddump` |
| 缺陷源头修复 | `Tools/ClassicMagicFixer/SetupGuardsSystem.cs` | 弓箭守卫 `(287,203)` → `(286,203)` |
| 数据修复 | `System.db` ×4 镜像（gitignore，不入库） | GuardInfo 110 → 96，MD5 `73597601...` |
| 截图/日志 | `docs/screenshots/guards/01~06*.png`、`server_log_02_before_after_fix.txt` | 行为证据 |

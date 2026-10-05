# 守卫摆放回归原版 Mir3 1.45（2026-10-05）

## 0. 结论

上一轮（`GUARD_COORDINATE_MIGRATION_AND_RECOVERY_2026-10-05.md`）把**英雄杀私服裁剪版**
当成了原版守卫真值，导致比奇城只剩 4 个守卫、且都不在城门大道上。本轮改用
**原版 Mir3 1.45 GuardList（117 条）** 全量重建 GuardInfo：

| 项 | 修复前 | 修复后 |
| --- | ---: | ---: |
| GuardInfo 总数 | 73 | **123** |
| 比奇 `0` 大刀守卫 | 4 | **27** |
| 覆盖地图 | 英雄杀 3 张主要图 | 0/01/02/1/2/4/5/8/74 九张 |
| `System.db`（四处镜像）MD5 | `827ddc873e37a9cb0d00fecf22002460` | `afae709c3dd0605cea873019400e8a95` |
| 重复点 / 不可站立格 | — | 0 / 0（`guardaudit`） |

实机截图：比奇县城墙西桥 `(389,329)` 与南门大道 `(456,384)` 均有大刀守卫站岗
（`docs/screenshots/guards/09_bichon_west_wall_guards_389_329.png`、
`10_bichon_south_gate_guards_456_384.png`）。

## 1. 根因：两套「原版」守卫表被混淆

| 数据源 | 条数 | 性质 |
| --- | ---: | --- |
| `/data/NAS/TMP/Mud3/Envir/GuardList.txt` | **117** | **原版 Mir3 1.45 布局**；与坚果云「传奇3 1.45 数据 / 1.45原版资_Envir/GuardList.txt」**sha256 完全相同**（`cef315090639747b91b35943a5936e9038c7ec6f19e7e9db5015c66169149b68`） |
| `/data/NAS/TMP/EI3.0英雄杀服务端/Mud3/Envir/GuardList.txt` | 32 | 2013–2015 年私服（含泡点/内测/VIP 系统）**裁剪版**：比奇只留 4 个守卫（`451..457,359..365`），删光边境城市/毒蛇山谷/绿洲/沙漠土城/潘夜岛/盟重县的守卫 |

`docs/EI_ALIGNMENT_2026-08-11.md` 明确写了当时「以 EI 英雄杀服务端为权威数据源」，
其后所有守卫审计都只核对了那 32 条，因此得到「坐标 100% 正确」的错误结论。
`MIr3-Research` 的 F511 早就记录 Mud3 的 GuardList 是 **117** 条
（`docs/research/mir3-map-reconstruction/envir-guard-quest-files-evidence.json`）。

## 2. 证据链（为什么 117 条才是当前地图的真值）

1. **守卫/地图文件同源**：`/home/tetsuya/mir2ei/Map` 与 `/data/NAS/TMP/Mud3/Map`
   逐地图比对，可站立格（`Map.cs:82` 判定）差异：
   `0/1/02/12/6/D71601 = 0.00%`，`2 = 5`、`01 = 24`、`4 = 406`、`8 = 169`、`74 = 303` 格
   （≤0.08%），且这些差分格的图层索引也一致 → **运行时地图就是原版地图**。
2. **117 条坐标全部落在原版城镇**：把每条坐标与 Zircon 现有 NPC 区域交叉比对，
   比奇 27 条环绕 NPC 群 `(375..498,288..463)`，毒蛇山谷 4 条环绕 `(306..361,193..244)`，
   道馆 8 条环绕 `(363..416,113..214)`，潘夜岛 14 条环绕 `(224..288,210..291)`，
   绿洲 4 条环绕 `(424..470,46..99)`，沙漠土城 18 条环绕 `(112..227,169..289)`。
   而修复前的比奇 4 条、毒蛇山谷 7 条都不在这些城镇位置上。
3. **出生点吻合**：Mud3 `Envir/StartPoint.txt` 比奇出生点 `(458,398)/(421,367)/(375,309)`，
   原版守卫 `(457,386)/(454,383)` 就在城门旁的大道上——正是玩家看到的空荡荡的门口。
4. **后代服务端沿用**：2022 年「传奇3ei复刻版 Mir2ei」`GUARDLIST.TXT` 的比奇守卫
   与 117 条中的 27 条逐条相同（另加 4 个弓箭守卫），并把 `卫士1` 直接改名 `大刀守卫`，
   证明 117 条就是这一系服务端沿用的原版摆放。

## 3. 采纳口径（2026-10-05 用户决策）

- **位置**：一律取原版 117 条。
- **怪物种类**：保留 Zircon 地域特色模型 —— 道馆 `1` 用 `TownGuard`、盟重县 `74` 用
  `ForestGuard`、沙漠系 `4/5` 用 `SandGuard`，其余原版 `卫士/卫士1` 统一用大刀守卫 `Guard`。
- **弓箭守卫**：Zircon 自加项（原版 GuardList 无弓箭守卫），**一律保留**。

## 4. 实现

- 新工具：`Tools/ClassicMagicFixer/GuardLayoutRestore.cs`，命令
  `guardlayout <RootDir> [--dry-run] [--map-path <Map目录>]`。
  - 原版 117 条以字符串常量内嵌（来源与 sha256 写在类注释里），不依赖外部文件路径。
  - 用与 `ServerLibrary/Models/Map.cs:82` **同一判定**校验可站立性；不可站立时就近挪格，
    挪不到则跳过并打印（`--map-path` 默认取 `<RootDir>/../Map`）。
  - 删除受管地图上的**非弓箭守卫**记录后按原版重建，幂等可重跑。
  - 写库后把 `System.db` 镜像到仓库根 / `Debug/ServerCore/Database` / `mir2ei/Data` /
    `mir2ei/Database` 四处并逐个校验 MD5。
- `Tools/ClassicMagicFixer/SetupGuardsSystem.cs`：删除已被取代的「旧比奇/道馆点位清理」
  与「弓箭手坐标迁移」两段一次性逻辑（原版清单会整体重建，留着只会互相打架）；
  地域模型切换补上 `5 → SandGuard`，并修正 `74` 的注释（`74` 是**盟重县**，不是白日门）。

## 5. 逐图结果

| 地图 | 说明 | 原版条数 | 写入 | 怪物 |
| --- | --- | ---: | ---: | --- |
| `0` | 比奇城 | 27 | 27（+8 弓箭） | Guard |
| `01` | 边境城市 | 9 | 9 | Guard |
| `02` | 银杏山谷 | 14 | 14 | Guard |
| `1` | 道馆 | 8 | 8 | TownGuard（原版为卫士1） |
| `2` | 毒蛇山谷（Zircon 名 Banya Village） | 4 | 4 | Guard |
| `4` | 绿洲（Numa Village） | 4 | 4 | SandGuard（原版沙漠战士） |
| `5` | 沙漠土城 | 18 | 18（+8 弓箭） | SandGuard（原版沙漠战士） |
| `8` | 潘夜岛（Frost Village） | 14 | 14 | Guard |
| `74` | 盟重县 | 4 | 4 | ForestGuard |
| `9` | 失乐园 | 14 | 0 | Zircon `MapInfo` 未注册该图，跳过 |
| `D71601` | EI 小图 | 1 | 0 | 原版 `(24,53)` 超出该图 `50×50` 边界且附近无可站立格，跳过 |
| `12` | Banya Island | 原版无 | 5（保留） | Zircon 自加，原版无对照 |

两处偏差均已在工具输出中标注：`5 (240,166) → (237,164)`（就近可站立格）。

## 6. 执行记录

```bash
# 服务端必须先停
kill -TERM <ServerCore pid>
stamp=pre-guardlayout-20261005-214532
mkdir -p Debug/ServerCore/Database/Backup/$stamp
cp -a System.db Debug/ServerCore/Database/Backup/$stamp/repo-System.db
cp -a Debug/ServerCore/Database/System.db Debug/ServerCore/Database/Backup/$stamp/server-System.db
cp -a /home/tetsuya/mir2ei/Data/System.db Debug/ServerCore/Database/Backup/$stamp/client-data-System.db
cp -a /home/tetsuya/mir2ei/Database/System.db Debug/ServerCore/Database/Backup/$stamp/client-database-System.db

dotnet build Tools/ClassicMagicFixer/ClassicMagicFixer.csproj --no-incremental
dotnet Tools/ClassicMagicFixer/bin/Debug/net10.0/ClassicMagicFixer.dll \
  guardlayout Debug/ServerCore/Database/ --dry-run     # 先看清单
dotnet Tools/ClassicMagicFixer/bin/Debug/net10.0/ClassicMagicFixer.dll \
  guardlayout Debug/ServerCore/Database/               # 写入 + 四镜像同步
dotnet Tools/ClassicMagicFixer/bin/Debug/net10.0/ClassicMagicFixer.dll \
  guardaudit Debug/ServerCore/Database/                # 期望 0 重复 / 0 失效
```

- 备份：`Debug/ServerCore/Database/Backup/pre-guardlayout-20261005-214532/`。
- 回滚：服务端停止后把四份备份分别拷回原路径即可（脚本见 §6 的 `cp` 反写）。
- `git`：`System.db` 被 `.gitignore` 忽略，**数据库改动不随 git 分发**，
  其他机器要自行同步这四处文件。

## 7. 验证

| 项 | 结果 |
| --- | --- |
| 写入结果逐条比对原版清单 | 101/101 命中（除 §5 两处已标注偏差） |
| `guardaudit` | 0 重复点 |
| 服务端启动 | `Map loaded: Bichon Town [0]/Banya Village [2]/Lost Paradise [1]`，**0 条 `Failed to spawn Guard`** |
| 实机 `(389,329)` | 城墙西桥两名大刀守卫（截图 09） |
| 实机 `(456,384)` | 南门大道两侧两名大刀守卫（截图 10） |

## 8. 未覆盖 / 待决策

| 项 | 状态 |
| --- | --- |
| 失乐园 `9`（14 条昂克战士2） | Zircon `MapInfo` 无此图；如要补，需要先注册地图与怪物 |
| `D71601` 单条守卫 | 原版坐标越界，未强行落位 |
| `12` Banya Island 5 条守卫 | Zircon 自加，原版无对照，本次保留 |
| `卫士1` 与 `卫士` 的贴图差异 | 原版 `卫士1` 用 `Mon-12` shape 3，`卫士` 用 `Mon-3` shape 6；本次按「大刀守卫」统一，未做区分 |

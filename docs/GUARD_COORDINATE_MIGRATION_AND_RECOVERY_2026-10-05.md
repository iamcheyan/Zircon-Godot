# 守卫坐标错位调查、修复与复用流程（2026-10-05）

本文记录大刀守卫、带刀侍卫和弓箭守卫最近一次坐标错位的根因、证据、修改方式、实机验证及数据库回滚步骤。以后处理地图替换后 NPC/怪物坐标异常时，可按“地图身份 → 参照物 → 全量记录 → 可站立性 → 游戏内表现”的顺序复查。

## 1. 结论

本次不是单一的坐标偏移量或 X/Y 互换，而是两类数据问题叠加：

1. EI 新地图替换了旧版地图尺寸和城镇所在区域，但数据库仍留有一批旧版 GuardInfo。旧点位本身合法，却落在新版比奇树林、城外空地或道馆不对应的位置。
2. 最近新增的弓箭手使用了旧坐标/旧地图假设：比奇弓箭手仍在旧版约 `(119..209,179..248)` 区域；盟重弓箭手被放入 `02`。MUD3 的 MapInfo 证明 `02` 是银杏山谷，盟重（沙漠土城）文件名是 `5`。

NPC 坐标没有同样的错位，是因为当前 NPC 数据已随 EI 新地图重新校准；守卫点位是另一组独立的 `GuardInfo`，不会自动随 NPC 或地图资源迁移。将 NPC 当参照物并与 MUD3 规则、实际地图和运行时画面交叉核对，才能发现这一点。

修复后数据库 GuardInfo 从 96 条变为 73 条：保留 MUD3 原版 32 个大刀守卫点，删除 23 个确认属于旧地图的比奇/道馆大刀守卫点，迁移 16 个弓箭手（8 个比奇坐标更新、8 个从银杏 `02` 迁往盟重 `5`）。本次安排的 16 个弓箭手均在游戏内确认生成；比奇大刀守卫及弓箭手、盟重弓箭手均有实机截图。

## 2. 地图身份与证据链

### 2.1 不能靠中文地图名或旧编号猜地图

权威映射来自 MUD3 的 `/data/NAS/TMP/EI3.0英雄杀服务端/Mud3/Envir/MapInfo.txt`（GB18030）：

| 地图文件名 | 地图身份 | 本次用途 |
| --- | --- | --- |
| `0` | 比奇城 | 原版大刀守卫及自定义弓箭手 |
| `1` | 道馆 | 原版守卫/带刀侍卫 |
| `02` | 银杏山谷 | 不能作为盟重点位目标 |
| `5` | 沙漠土城（盟重） | 自定义弓箭手的盟重目标地图 |

当前运行资源 `/home/tetsuya/mir2ei/Map` 与 MUD3 对应地图文件哈希一致：地图 `0`、`1`、`02`、`5` 分别为 800×800、600×600、600×600、350×350。旧 Zircon 备份中的比奇/道馆图为 350×350。地图更新改变了城镇区域和尺寸，所以旧地图上的守卫绝对坐标不能直接沿用。

相关原始资料：

- MUD3 `Envir/MapInfo.txt`：地图文件名到地图身份的依据。
- MUD3 `Envir/GuardList.txt`（GB18030）：32 个原版城防大刀守卫坐标。
- `/home/tetsuya/development/Mir3-Research/docs/map-data-migration-investigation.md`：旧版与 EI 地图尺寸迁移记录。
- `/home/tetsuya/development/Mir3-Research/docs/research/mir3-map-reconstruction/envir-guard-quest-files-evidence.json`：逆向证据索引。
- `/home/tetsuya/development/Mir3-Research/docs/source-vs-reverse/config.md`：原版配置文件及解析器考证入口。

### 2.2 MUD3 原版守卫与当前数据库

`GuardList.txt` 共 32 行有效记录，全部是大刀守卫 `卫士`：比奇 4 个、道馆 11 个、银杏 17 个。MUD3 比奇城门官方点位是 `(457,359)`、`(455,361)`、`(453,363)`、`(451,365)`。这 32 个地图/X/Y/方向元组在修复前后的 Zircon System.db 中均与原版一致。

这只能证明这 32 条对应记录没错，不能证明数据库里没有别的错点。修复前还有 19 个旧比奇 Guard、4 个旧道馆 TownGuard，它们不属于上述原版清单。旧审计只对比了原版 32 条，因而误以为整体坐标都正确；还漏掉了自定义弓箭手错误坐标和错误地图归属。

### 2.3 NPC 对比为何能定位版本

旧备份（2026-08-13）中的比奇 NPC 主要在约 `x=130..189, y=178..263`，盟重 NPC 在约 `x=138..195, y=167..219`。当前 EI 数据中的比奇 NPC 位于约 `x=375..498, y=288..463`，盟重 NPC 位于约 `x=363..416, y=112..214`。MUD3 当前 Merchant 配置中比奇商人锚点如 `(459,358)`、`(469,368)`、`(466,372)`，与新版地图上的城门/城墙及官方 GuardList `(451..457,359..365)` 同区域。

因此当前数据库 NPC 与新版 EI 地图对齐，而旧 GuardInfo 仍保留旧图坐标。这是数据集合之间版本不一致，并非统一加减某个常数：不同地图尺寸、区域和墙门布局都不同，不能用一个全局偏移公式迁移。

## 3. 本次缺陷明细

修复前的 96 条 GuardInfo 分布：

| 内容 | 条数 | 问题 |
| --- | ---: | --- |
| 比奇旧大刀守卫 | 19 | 旧版 350×350 坐标，新图上落入树林/城外区域 |
| 道馆旧 TownGuard | 4 | 旧点位，与当前道馆城镇区域不符 |
| 比奇弓箭手 | 8 | 坐标仍沿用旧比奇约 100–200 区域 |
| `02` 上的所谓盟重弓箭手 | 8 | `02` 实为银杏山谷，地图选错；位置也不对应盟重城防 |
| MUD3 原版大刀守卫 | 32 | 点位正确，保留 |
| 其他地图/自定义记录 | 25 | 不属于本次确认的旧点，保留 |

弓箭手坐标迁移表（左侧为数据库旧值，右侧为新值）：

| 来源地图 | 旧坐标 | 目标地图 | 新坐标 |
| --- | --- | --- | --- |
| 比奇 `0` | `(141,244)` | `0` | `(447,355)` |
|  | `(150,248)` | `0` | `(449,353)` |
|  | `(119,228)` | `0` | `(451,355)` |
|  | `(125,238)` | `0` | `(452,357)` |
|  | `(201,179)` | `0` | `(458,358)` |
|  | `(209,187)` | `0` | `(460,359)` |
|  | `(133,213)` | `0` | `(459,363)` |
|  | `(145,206)` | `0` | `(457,367)` |
| 银杏 `02`（误作盟重） | `(272,165)` | 沙漠土城 `5` | `(220,156)` |
|  | `(279,172)` | `5` | `(224,156)` |
|  | `(242,166)` | `5` | `(226,160)` |
|  | `(249,163)` | `5` | `(220,164)` |
|  | `(286,203)` | `5` | `(222,124)` |
|  | `(291,207)` | `5` | `(225,131)` |
|  | `(225,235)` | `5` | `(230,128)` |
|  | `(231,241)` | `5` | `(234,128)` |

新增位置根据新版地图上的墙、塔楼、城门/传送广场区域选取；逐格检查地图可站立标志，再通过实际登录、传送、截图确认实体生成。地图可站立检查只回答“能否刷在这一格”，不能单独证明“这里是合适的城防位置”。

## 4. 修改位置与行为

实现位于 [`SetupGuardsSystem.cs`](../Tools/ClassicMagicFixer/SetupGuardsSystem.cs)，入口 [`Program.cs`](../Tools/ClassicMagicFixer/Program.cs) 的 `setupguards` 命令支持 `--dry-run`。

修复逻辑做了以下处理：

1. 通过 `MapInfo.FileName` 识别比奇 `0` 和盟重 `5`；不再把 `02` 当盟重。
2. 对已写入数据库的 16 条旧弓箭手记录按明确的旧地图+旧坐标表迁移。只有命中已知旧元组才移动，避免误动其他自定义记录；重跑是幂等的。
3. 只删除 map `0` 上明确列出的 19 个旧 `Guard` 点、map `1` 上明确列出的 4 个旧 `TownGuard` 点。保留 MUD3 官方点位及其它地图记录。
4. 确保新版位置上存在 8 个比奇、8 个盟重弓箭手；已存在目标记录时不重复创建。
5. `--dry-run` 运行完整迁移和删除逻辑但不调用 `Save`，也不同步数据库镜像。

服务端 [`Map.CreateGuards()`](../ServerLibrary/Models/Map.cs) 会根据 `GuardInfo` 尝试生成实体；地图格不可用时记录 `Failed to spawn Guard`。该日志只说明生成失败，未出现该日志也不能证明位置正确，因此还需要核对实际场景中的地图、对象、坐标和可见范围。

## 5. 可复用的调查与修复流程

### A. 先确定运行时正在用哪份地图和数据库

1. 从启动脚本/服务配置确认客户端地图资源目录、服务端 System.db 路径和当前服务器实例。
2. 读取 map 文件的尺寸、哈希，并与原版 MUD3 地图和已知迁移文档核对。不要只凭资源文件名、地图中文名或“地图编号看起来像”作结论。
3. 检查四份 System.db 是否一致。运行中不要写数据库。

### B. 分开收集每种对象的真值

1. NPC 是地图空间参照物，不是守卫数据的自动来源。统计当前 NPC 坐标，并与旧备份、MUD3 Merchant/NPC 配置比较。
2. 原版 GuardList 是原版大刀守卫摆放的真值。完整比对地图、X、Y、方向，并另行找出 Zircon 自定义点位。
3. 从 DB 导出**全部** GuardInfo，而非仅查询能匹配原版的行。按地图、怪物种类和坐标分组，查找额外旧记录、重复记录、落在旧城镇区域的记录。
4. 不要因一组官方点 100% 匹配就推出全表正确；注意审计结论的分母必须是“所有记录”。

### C. 选择坐标时交叉验证

1. 先用当前 MapInfo 证明目标地图身份，避免 `02` 一类前导零编号产生误解。
2. 在当前地图图像/小地图上定位城墙、城门、箭楼和 NPC 聚集区；旧坐标不能只做比例缩放。
3. 对每个候选格检查地图尺寸和站立标志，排除墙体、越界和不可刷格。
4. 登录当前构建，传送到每个候选区附近，检查地图名/玩家坐标和实体是否生成。需要多处截图覆盖地图视野；“屏幕没看到”先检查镜头范围、建筑遮挡和实体列表/小地图，不要直接判定没生成。
5. 如有逆向结论，记录配置文件编码、原版解析器入口和源文件路径，便于别人复现。

### D. 安全地写入 System.db

以下是本仓库目前的四份镜像路径。按项目规则操作前确认本地服务器已停止，并先备份全部镜像：

```bash
cd /home/tetsuya/development/zircon
pgrep -af 'ServerCore.dll'   # 确认目标测试服停止；不要对仍在运行的 System.db 写入

stamp=pre-guard-coordinate-fix-YYYYMMDD-HHMMSS
mkdir -p "Debug/ServerCore/Database/Backup/$stamp"
cp -a System.db "Debug/ServerCore/Database/Backup/$stamp/repo-System.db"
cp -a Debug/ServerCore/Database/System.db "Debug/ServerCore/Database/Backup/$stamp/server-System.db"
cp -a /home/tetsuya/mir2ei/Data/System.db "Debug/ServerCore/Database/Backup/$stamp/client-data-System.db"
cp -a /home/tetsuya/mir2ei/Database/System.db "Debug/ServerCore/Database/Backup/$stamp/client-database-System.db"

dotnet Tools/ClassicMagicFixer/bin/Debug/net10.0/ClassicMagicFixer.dll setupguards \
  /home/tetsuya/development/zircon/Debug/ServerCore/Database/ --dry-run
# 检查迁移/删除数量和每条旧→新坐标，再去掉 --dry-run 执行。
```

工具会把写入结果镜像到四个 System.db 路径。随后用工具只读导出 `GuardInfo` 并检查全表，再运行 `guardaudit`（重复点预期为 0）。比较四份文件哈希必须完全相同：

```bash
sha256sum System.db Debug/ServerCore/Database/System.db \
  /home/tetsuya/mir2ei/Data/System.db /home/tetsuya/mir2ei/Database/System.db
```

若读回核验失败，在服务器保持停止时从本次备份恢复四份原文件，再检查原因；不要在运行中的服务端上覆盖数据库。数据库恢复后重新核对四份哈希，再启动服务器。实际使用时将 `YYYYMMDD-HHMMSS` 替换为唯一时间戳。

### E. 运行时验收

1. 重启测试服并用测试角色完整登录，不以编译通过替代行为验证。
2. 在游戏中传送到地图和新坐标附近，确认左下角地图名/坐标，并检查守卫模型及名称。
3. 查服务日志中的 `Failed to spawn Guard`；同时用视野内实拍确认成功生成。
4. 保存修复前后截图、服务日志、DB 计数和镜像哈希，记录截图视角/角色坐标。发生遮挡时移动到实体附近或换视角再下结论。

## 6. 本次验证记录

- 修改前：96 条 GuardInfo；修改后：73 条。16 条弓箭手迁移，23 条旧地图大刀守卫记录删除；官方 MUD3 32 个坐标保留。
- 修改工具 dry-run：预期迁移 16、删除 23，显示未写 DB/未同步镜像。
- 修改前对四份 System.db 完成备份；修改后四份镜像哈希一致（MD5 `827ddc873e37a9cb0d00fecf22002460`）。
- `guardaudit` 重复点预检为 0。
- `dotnet build Tools/ClassicMagicFixer/ClassicMagicFixer.csproj --no-incremental`：通过。
- `dotnet build GodotClient/ZirconClient.csproj --no-incremental`：通过。
- XFCE 实机登录本地测试服成功。比奇 `(454,362)` 画面可见官方大刀守卫和城墙弓箭手；截图 [`07_bichon_guards_after_coordinate_fix.png`](screenshots/guards/07_bichon_guards_after_coordinate_fix.png)。盟重 `(222,124)` 附近可见弓箭手；截图 [`08_mongchon_archer_after_coordinate_fix.png`](screenshots/guards/08_mongchon_archer_after_coordinate_fix.png)。
- Tab 大地图在无输入焦点时可显示/隐藏；聊天输入框聚焦后按 Tab 不会打开大地图。

## 7. 本次文件关联

- 修复工具：`Tools/ClassicMagicFixer/SetupGuardsSystem.cs`、`Tools/ClassicMagicFixer/Program.cs`
- 旧审计的更正说明：[`GUARD_MAP_COORDINATE_AUDIT_2026-10-04.md`](GUARD_MAP_COORDINATE_AUDIT_2026-10-04.md)
- 守卫体系方案地图编号更正：[`TOWN_GUARDS_AND_ARCHERS_SYSTEM_SPEC_2026-10-04.md`](TOWN_GUARDS_AND_ARCHERS_SYSTEM_SPEC_2026-10-04.md)

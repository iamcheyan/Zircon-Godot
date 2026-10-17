# NPC 放置缺口核查（2026-10-17）

起因：用户要求「所有在用 NPC 都测一遍，能补的都补回来」。
全量实机巡检（230 NPC / 1533 页 / 1533 张截图，见
[NPC_FULL_SWEEP_2026-10-17.md](NPC_FULL_SWEEP_2026-10-17.md)）只报出 2 处异常，
都在**六面神石（传送石）**上，于是顺着数据链路逐层核查。

## 一、结论速览

| 层 | 事实 | 判定 |
|---|---|---|
| 数据库 | `NPCInfo` 共 **294** 条 | — |
| 数据库 | 经「地图区域 → NPC」反向链接可被服务端收集的 = **230** 条 | 与清单 230 完全吻合 |
| 数据库 | 不可达 **64** 条 | **是预留模板，不是丢失**（无地图/无坐标，见 §3） |
| 实机 | 巴尼亚传送石（#106，2/314,193）**能开**（单独跑 pages=2） | 正常 |
| 实机 | 萨巴克传送石（#275/#279/#374，3 图）**一个都不出现** | **已解决**：#275 登记格不可站 → 已挪到 `(222,157)`，见 §4/§8 |
| Mud3 源 | 萨巴克有 **4** 个传送石落点；库内 3 条、其中 2 个坐标与源不符 | 见 §5 |

## 二、核查方法（可复现）

1. **读库**（只读）：
   ```bash
   export MIR3_ZIRCON_ROOT=/Users/tetsuya/Development/Zircon
   dotnet run --project ~/Development/Mir3-Research/Tools/SystemDbProbe -- \
     "Debug/ServerCore/Database/" --json /tmp/dbdump/npc_now.json
   ```
   → 各表导出到 `/tmp/dbdump/npc_now.json/*.json`。
2. **源 ↔ 库对照**（只读，本轮新增）：`tools/mud3_npc_placement_diff.py`
   （注意：库内 NPC 已按「纯正中文命名」重命名，**不能按名字匹配**，需配合
   `tools/aligned_npcs_230.json` 的 `mud3_match` 使用）。
3. **实机放置核查**：巡检 `--npc-audit-only <index>`，未找到时会自动打印
   **当前地图上所有 NPC 对象**（本轮给 `GameScene.NpcAudit.cs` 加的自带诊断），
   用来区分「位置不对」还是「整条没刷出来」。

## 三、那 64 条不可达 NPC 是什么

`泡点系统 / VIP / 名望系统 / 钓鱼系统 / 钻石抽奖 / 天降宝箱 / 仓库系统×3 /
买卖商人×3 / 天下第一战法道 / 血案接待 / 骑兵接待 / 尊者接待 / 杀人掠夺 …`

它们的 `Region` 为空、地图与坐标均为 `None` —— 是**功能预留模板**，
原版里由服务端按玩法动态放置（或由 Zircon 用窗口实现：仓库=StorageDialog、
钓鱼=FishingDialog 等）。**不是需要「补回来」的丢失内容**，不该硬塞到地图上。

## 四、萨巴克传送石：已定位，只有 1 颗真坏

服务端加了「实际落格」诊断后（`[NPC] 地图 X [file] 实际落格 N 个：名字#index@(x,y)`），
真相如下：

| NPC | 库内登记落点 | 服务端**启动时**实际落格 | 实机（地图加载后若干秒） | 判定 |
|---|---|---|---|---|
| #275 六面神石 | (222,159) | **(0,0)** ✗ | 可达、可点开（`pages=2 clicked=True`） | 登记格不可站 → 启动瞬间落到原点，之后才归位；**已修复：登记格改为 (222,157)**（§8） |
| #279 六面神石 | (51,222) | (51,222) ✓ | 正常 | 此前"缺失"是巡检半径 6 格太窄的误报（已放宽到 24 格） |
| #374 六面神石 | (71,140) | (71,140) ✓ | 正常 | 同上 |
| #276/#277/#278/#280/#281/#282/#283 | — | 与登记一致 ✓ | 正常 |

服务端 `[NPC] 地图 Sabuk Keep [3] 实际落格 10 个：…` 证明**十颗全刷出来了**，
且无一条 `Failed to spawn`。所以：

- **内容没丢**，不存在"要补回来的 NPC"；
- 唯一缺陷是 **#275 的落点不可站**，被引擎放到了 (0,0)（因此巡检在 (222,157)
  周围永远找不到它）。

### 修复配方（一行数据，需走写库纪律）

把 #275 的 `MapRegion`（Index 5090，`3 / Teleport Stone Castle`）中心从
(222,159) 挪到可站格 —— 实机已验证 **(222,157) 可站**（`@move 3 222 157` 成功）：

```bash
cd ~/Development/Mir3-Research
python3 Tools/NpcMover/place.py move 275 222 157 3   # 写 dbeditor workspace（不碰 .db）
python3 Tools/NpcMover/place.py diff                 # 预览待入库改动
# 停服后：bash Tools/dbeditor/sync.sh                  # 校验→临时副本→语义 diff→备份→安装→报告
```

> 结论：**内容没丢，不存在"要补回来的 NPC"**。全量巡检里唯一剩下的异常是 #106 的
> 一次偶发点击失败（单独重跑 `pages=2` 通过）；#275 只需把登记落点挪到可站格，
> 即可消除"启动瞬间落到 (0,0)"的瞬态。

## 五、Mud3 源里的萨巴克传送石（信息项）

`Envir3/Merchant.txt` 中 `13Move_Sabuk` 共 **4** 个落点：

| Mud3 坐标 | 库内 | 状态 |
|---|---|---|
| (222,159) | #275 | 落格 (0,0)，待按 §4 挪到可站格 |
| (71,140) | #374 | 正常 |
| (51,222) | #279 | 正常（坐标与源不同，功能正常） |
| (294,539) / (49,566) | 无 | 库内没有这两颗；属"经典纯净"清洗时裁掉的冗余落点，**不建议补**（同一服务已有 3 颗） |


## 六、本轮新增/改动的工具

| 文件 | 作用 |
|---|---|
| `tools/mud3_npc_placement_diff.py` | Mud3 `Merchant.txt` ↔ System.db 的 NPC 放置只读对照 |
| `tools/npc_sweep_report.py` | 全量 NPC 巡检报告生成器（含功能断言与灰块回归） |
| `GameScene.NpcAudit.cs` 改动 | ① 按整份清单去重同坐标重复登记；② 未找到 NPC 时打印该图全部 NPC 对象 |

## 七、写库纪律（真要补的时候）

- 服务端**运行中绝不写** `System.db`；写前备份。
- 写完必须 4 处同步且 MD5 一致（服务端 `Debug/ServerCore/Database/`、
  `~/mir2ei/Data/`、`~/mir2ei/Database/`、仓库根 `System.db`）。
- 走既有管线：`NpcMover/place.py`（写 dbeditor 工作区）→ 停服 → `dbeditor/sync.sh` → 起服。

> 写库注意：`Tools/dbeditor/sync.sh` 里 `ZIRCON` 硬编码为 Linux 路径
> `/home/tetsuya/development/zircon`，在 macOS 上必须先改成本机仓库根再跑，
> 否则会写到不存在的路径。workspace 里已有一条历史待同步改动
> （`MapRegion #11369 modified PointRegion`），入库前需确认它是否要一起提交。
>
> **上面两点已在 §8.2 / §8.5 处理完毕**（历史改动已回退；macOS 适配清单已列全）。

## 八、修复执行记录（2026-10-17 已执行、已入库、已实机验证）

> **先读 §9**：本节记录的是 10-17 在 Mac 上的那次执行（当时确实安装并逐字节验证通过），
> 但 Mac 的 `~/mir2ei` 是 Syncthing **receiveonly 副本**，写入会在下一次同步时被对端静默还原。
> 10-18 已在**发送端 debian(192.168.3.82)** 重新入库并双向核对，最终状态见 §9。

### 8.1 变更内容

| 表 | 行 | 改前 | 改后 |
|---|---|---|---|
| `MapRegion` | **#5090**（NPC **#275** 六面神石；地图 `3` = Sabuk Keep；描述 `Teleport Stone Castle`） | (222,159) | **(222,157)** |

- 旧格 `flag=0x00`（`flag&3=0` → **阻挡**），新格 `flag=0x03`（通行）。
  复核用**独立于工具**的自写解析器（`.map` 头 26B + `w=u16@22` / `h=u16@24` +
  细胞区起点 `28+(w/2)*(h/2)*3` + 每格 14B、`flag@+0`）——不以 `npcedit` 的结果为准。
- 同一次横查：**全库 230 个在用 NPC 的登记格 100% 通行，0 阻挡、0 越界**
  → 这说明落格缺陷只有 #275 这一处，现已清零。

### 8.2 顺带处理：那条历史待同步改动（#11369）

workspace 里 `MapRegion #11369`（比奇 `Hexa Holy Stone`，NPC #370）曾被改成 X=380，
是一条未提交的悬空改动。复核：(379,444) 与 (380,444) **都通行**，且服务端一直按
库值正确落格（`六面神石#370@(379,444)`）→ **该改动不必要**，已用
`place.py move 370 379 444 0` 改回库值，使本次入库**只带 8.1 这一处变更**
（`place.py diff` 随后只剩 1 条）。

### 8.3 入库流程与证据（macOS 本机）

1. **停服**（7001 无监听）+ 备份 4 条逻辑路径 → `/tmp/db_backup_20261017_140636/`。
2. **影子导入**（`DBImporter --workspace <ws> --src <库>`，`--src` 只读）：
   `变更 1 处：MapRegion×1`、`引用完整性校验通过`、`新版本 2026.10.17.1`；
   执行前后**原库 MD5 未变**（`083532c9…`）→ 证明导入器不动原库。
3. **round-trip 语义对比**：`compare_sync.py` → `[OK] round-trip 语义一致`。
4. **独立字节级对比**（不共用工具逻辑，最硬的一条证据）：
   原库 vs 影子库**等长 3,385,647 B，仅 2 字节不同** ——
   偏移 `1788025`：`0x9F→0x9D`（159→157，正是本次变更）；
   偏移 `3382879`：版本号末位 `'6'→'7'`（`2026.10.17.0 → 2026.10.17.1`）。
   **除这两字节外，全库逐字节相同。**
5. **备份 + 安装**：`mir2ei/Database/Backup/System/System 2026-10-17 14-12.db.gz`
   与 `System-client 2026-10-17 14-12.db.gz`；写入两个物理文件后，
   4 条逻辑路径 MD5 全为 `88c4aef7…`（改前 `083532c9…`），软链未被破坏。
6. **读回验证**（从已安装库重新导出）：`Region#5090 = (222,157)` ✓、
   `NPC#275` 区域名 `3 / Teleport Stone Castle` ✓、库版本 `2026.10.17.1` ✓。
7. **基线重置**：`_baseline/*` 与 `baseline.json` 对齐新库
   （`server_md5 = 88c4aef7…`，`version = 2026.10.17.1`）；`place.py diff` 清空。

### 8.4 行为验证（起服 + 实机进图）

- 服务端日志：`[NPC] 地图 Sabuk Keep [3] 实际落格 10 个：六面神石#279@(51,222),
  六面神石#275@(222,157),…` ✓ —— 从「启动瞬间落到 (0,0) 再归位」变为**直接落在 (222,157)**。
- 全日志 `@(0,0)` 计数 = **0**（此前 #275 是唯一一条）。
- 单点巡检 `--npc-audit-only 275`：`SUMMARY total=1 ok=1 no_response=0 not_found=0
  click_missed=0 timeout=0`；点击成功、2 页、`errors: []`；截图
  `/tmp/npc_275_after/shots/275_0_p38023.png`、`275_1_p-1.png`。
- `tools/npc_audit_manifest.json` 中 #275 的登记坐标同步改为 (222,157)。

### 8.5 macOS 上跑 `dbeditor/sync.sh` 的必要适配（本轮实测）

原脚本是 Linux 专用，在 macOS 直接跑会失败或写错位置：

| 项 | Linux 原脚本 | macOS 需要 |
|---|---|---|
| 仓库根 | `ZIRCON=/home/tetsuya/development/zircon` | 本机仓库根（注意大小写：`…/Development/Zircon`） |
| 摘要算法 | `md5sum … \| cut -d' ' -f1` | `md5 -q` |
| 取报告字段 | `grep -oP '(?<=^DB_OUT=).*'` | BSD grep 无 `-P` → `sed -n 's/^DB_OUT=//p'` |
| 安装目标 | 4 条路径直接 `install` | 只写 **2 个物理文件**：`~/mir2ei/Database/System.db`（服务端读取）、`~/mir2ei/Data/System.db`（客户端读取）；**不要**写 `Zircon/Debug/ServerCore/Database/…` 或 `Zircon/Debug/Client/…` —— 它们是指向上述二处的**软链**，`install` 会把软链替换成实体文件 |
| 第 4 份 | `$ZIRCON/System.db` | 本机不存在（此地的“4 拷贝一致”等价于 2 物理文件 + 2 软链） |

另：`DBImporter` / `SystemDbProbe` 必须 `export MIR3_ZIRCON_ROOT=/Users/tetsuya/Development/Zircon`
才能编译（否则 `CS0246: 未找到 Library`），且 Mir3-Research 目录里遗留的 Linux 二进制
要重新 `dotnet build`（否则 `Exec format error`）。

## 九、更正与最终入库位置（2026-10-18）

### 9.1 关键更正：库的"真身"在 debian，Mac 上的写入会被回退

- `~/mir2ei` 是 **Syncthing 同步目录**：debian 侧为 **sendonly**（名为 "Mir2EI client — Debian source"），
  Mac 侧为 **receiveonly**（"Mir2EI client"）。receiveonly 副本上的本地修改会被静默还原
  —— **这正是 10-17 在 Mac 上装好的库（`88c4aef7…`）后来回到旧值（`083532c9…`）的原因**
  （无 conflict 副本，文件 mtime 保持原样）。
- 结论：**改 `~/mir2ei` 里的数据库必须在发送端 debian(192.168.3.82) 做**，
  这与 AGENTS 的「开发只在 82 进行」「System.db 必须多处一致」两条约定一致。
- 两台机器的库副本形态不同：
  | 机器 | `Debug/ServerCore/Database` | 实体副本数 |
  |---|---|---|
  | Mac | 软链 → `~/mir2ei/Database` | **2**（`mir2ei/Database` + `mir2ei/Data`；`Debug/Client` 亦为软链） |
  | debian | **实体目录** | **4**（`Debug/ServerCore/Database`、`Debug/Client/Data`、`~/mir2ei/Database`、`<repo>/System.db`） |

### 9.2 导入器的参照物：工作区 vs `_baseline`（不是 vs 现有库）

`DBImporter` 的改动集 = **`workspace/*.json` 与 `workspace/_baseline/*.json` 的差**；
`--src` 只是"被改的那份库"（先复制、再套用差量），因此：

- `sync.sh` 第 7 步"重置基线"会把**待同步状态消费掉**：一旦基线被重置成新库状态，
  再跑就是 `NO_CHANGES=1`，即使磁盘上的库并不是那个状态。
- 若要用同一份工作区**重新应用**已丢失的改动：先把 `_baseline` 里对应行**还原成库中的实际值**，
  再跑 `sync.sh`。本次即如此（把 `_baseline/MapRegion.json` 的 #5090 改回 (222,159)），
  随后 `place.py diff` 恰好显示 **1 处**改动，可作入库前的自检。

### 9.3 debian 上的最终入库（本次唯一有效写入）

| 项 | 值 |
|---|---|
| 备份 | 4 份实体副本 → `~/db_backups/debian-20261018_075740/`；`sync.sh` 另存 4 份 gz 到 `Debug/ServerCore/Database/Backup/System/System-copy* 2026-10-18 07-58.db.gz` |
| 变更 | `MapRegion#5090` (222,159) → **(222,157)**；`变更 1 处：MapRegion×1`、引用完整性通过、`round-trip 语义一致` |
| 安装 | 4 份实体副本全部 `083532c9…` → **`1a5e0e48…`**；库版本 `2026.10.18.1` |
| 字节级核对 | 与备份库比：**等长，仅 2 字节不同** —— 偏移 `1788025` `0o237→0o235`（159→157）、偏移 `3382879` `'6'→'8'`（版本末位） |
| 读回 | 从已安装库重导出：`Region#5090 = (222,157)`、版本 `2026.10.18.1`；`place.py diff` 清空 |
| 同步到 Mac | 约 90 秒后 4 条逻辑路径全为 `1a5e0e48…`（Syncthing 推送） |

### 9.4 工具修复（Mir3-Research `Tools/dbeditor`，提交 `7f1ebaa3`）

- `sync.sh`：端口探测改用 `lsof` 取监听进程名，**非 dotnet/ServerCore 不算服务端**
  （macOS 上 7000 被 AirPlay 接收器 ControlCenter 占用，之前每次同步都误报）；非服务端监听时
  给导入器传 `--no-port-check`。
- `sync.sh`：安装目标改为 **realpath 去重后的全部实体副本**（Mac 2 份 / debian 4 份），
  既保证"每一份都一致"，又不会把软链替换成实体文件。
- `DBImporter`：端口提示改为 7000/7001 都探，并说明可用 `--no-port-check` 静音。

### 9.5 审计截图：/tmp 证据已被重启清空 → 改为入库到 `.artifacts/`

2026-10-18 07:5x 复查时发现 `/tmp` 已被清空（机器重启），**10-17 那批 `_tmp` 截图（1533 张巡检
+ 34 张 UI 遮罩审计等）全部丢失**。因此本轮改为：巡检脚本与截图产物**落进仓库**：

- 巡检脚本：`tools/npc_full_sweep.sh`（Linux/macOS 通用、分块可续跑）；
- 截图压缩：`tools/pack_audit_shots.sh`（把整屏 PNG 缩成可入库的 JPEG 缩略图）；
- 产物目录：`.artifacts/npc-full-sweep-2026-10-18/`（见该目录 `REPORT.md`）；
- 原图（整屏 PNG）留在本地 `~/audit-raw/`，不入库（体积原因）。

> 复跑（2026-10-18，新库）的逐 NPC 结果与截图索引见
> [NPC_FULL_SWEEP_2026-10-17.md](NPC_FULL_SWEEP_2026-10-17.md) 的复跑章节与
> `.artifacts/npc-full-sweep-2026-10-18/REPORT.md`。

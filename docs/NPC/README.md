# NPC 文档目录

> 传奇 3（Zircon）NPC 体系的对齐记录、全景清单与职能分类。
> 建立于 2026-10-04。

## 快速入口

| 想知道 | 看哪份 |
|---|---|
| **每个城市有哪些 NPC、在哪个坐标** | [`ROSTER.md`](ROSTER.md) |
| **这些 NPC 分别干什么、卖什么** | [`FUNCTIONS.md`](FUNCTIONS.md) |
| 这次对齐做了什么、怎么做的 | [`WORK_LOG.md`](WORK_LOG.md) |
| **这些 NPC 有什么问题、缺什么** | [`ANALYSIS.md`](ANALYSIS.md) |
| 验收结论 | [`ACCEPTANCE.md`](ACCEPTANCE.md) |
| 原版有但还没有的 NPC | `missing_npcs_pending_intro.json`（205 条） |

## 一句话数据

**294 个 NPC 条目 = 218 在役（游戏内生成）+ 76 软停用**（`Region=null`，私服功能 NPC）。
分布在 **58 张地图**（全库 627 张）。

## 文件清单

### 文档
| 文件 | 内容 |
|---|---|
| `ROSTER.md` | 逐地图 NPC 坐标明细（58 张图，每图一张表） |
| `FUNCTIONS.md` | 职能分类：6 大类 / 28 个职能，说明各自卖什么、干什么 |
| `ANALYSIS.md` | **问题清单与优先级**：58 个 NPC 显示英文名、比奇缺 3 项业态、软停用风险等 |
| `ROSTER.csv` | 机读版，294 条（含软停用），字段：idx/name/name_zh/map/map_cn/x/y/category/service/mud3_id/active |
| `WORK_LOG.md` | 完整工作记录：探索发现、6 步 SOP、踩坑、遗留项 |
| `ACCEPTANCE.md` | 7 项硬指标验收结论 |
| `RESULTS.md` | 服务端 NPC 生成实机证据 |
| `server_npc_spawn_evidence.txt` | 原始服务端日志片段 |

### 数据 / 脚本
| 文件 | 内容 |
|---|---|
| `alignment_matrix.json` / `.csv` | 三方数据对齐矩阵（294 条 × Migrate/Disable/Retain/Pending） |
| `missing_npcs_pending_intro.json` / `.csv` | 差集B：原版有、新版缺失的 NPC（205 条待引入） |
| `build_roster.py` | 生成 `ROSTER.md` / `ROSTER.csv` |
| `build_alignment_matrix.py` | 生成对齐矩阵 |
| `build_website_npcs.py` | 生成网站百科 `npcs.json` |

## 数据来源与权威性

```
Mud3 Merchant.txt (2012)          ← 原版权威坐标 + 中文名
  Mir3-Research/local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/

System.db (现役)                  ← 当前游戏实际运行的 NPC
  Debug/ServerCore/Database/System.db   （服务端实际读取的那份）

*.map                             ← 地图几何，做边界/柜台校验
  /home/tetsuya/mir2ei/Map/
```

> **⚠️ 权威源提醒**：原计划指定的 `Mud3-Config/Envir/Npcs.txt` 是 **0 字节空文件**，
> 不能作为数据源。实际用的是上面的 `Merchant.txt`。
> 地图代号也以 Mud3 `Mapinfo.txt` 为准：**比奇=`0`、道馆=`1`、银杏=`02`、沙巴克=`3`**。

## 重新生成

```bash
export MIR3_ZIRCON_ROOT=/home/tetsuya/development/zircon/
cd /home/tetsuya/development/Mir3-Research
DB_SRC=/home/tetsuya/development/zircon/System.db bash Tools/dbviewer/export.sh /tmp/dbv_roster
python3 /home/tetsuya/development/zircon/docs/NPC/build_roster.py
```

## 关键概念

- **在役 vs 软停用**：`Region != null` 服务端才生成 NPC（`SEnvir.cs:927` `if (info.Region == null) continue;`）。
  软停用是**可逆的**，`NPCPage`/`NPCAction`/`NPCCheck` 脚本树一行未删。
- **柜台内站位**：部分原版 NPC 站在阻挡格（柜台）里，这是**原版真实站位**，
  不要「修正」到柜台外，否则会破坏还原度。
- **地图代号 ≠ Zircon MapInfo Index**：`@move 0 420 380` 里的 `0` 是
  `MapInfo.FileName`，不是 `MapInfo.Index`（比奇城的 Index 是 1）。

## 相关（仓库其它位置）

| 位置 | 内容 |
|---|---|
| `Tools/NpcAligner/` | NPC 坐标迁移工具（Dry-run / commit 双模式） |
| `docs/NPC_SYSTEM_ALIGNMENT_AND_WEBSITE_INTEGRATION_PLAN.md` | 本次执行的原始计划书 |
| `docs/screenshots/npc_alignment/` | 实机截图 |
| `/home/tetsuya/development/mir3-website/data/npcs.json` | 网站百科数据源（218 条） |
| `/home/tetsuya/mir2ei/Backup/npc_alignment_20261004122723/` | 回滚基线 |

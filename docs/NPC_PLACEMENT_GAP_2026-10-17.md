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
| 实机 | 萨巴克传送石（#275/#279/#374，3 图）**一个都不出现** | **未解决**，见 §4 |
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
| #275 六面神石 | (222,159) | **(0,0)** ✗ | 可达、可点开（`pages=2 clicked=True`） | 登记格不可站 → 启动瞬间落到原点，之后才归位；**建议挪到可站格** |
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

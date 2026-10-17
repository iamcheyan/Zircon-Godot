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

## 四、萨巴克传送石为什么不出来（未解决）

已排除的因素（全部有证据）：

| 假设 | 核查结果 |
|---|---|
| 记录缺失 | ✗ 记录在（#275/#279/#374），`Image=56`、`EntryPage` 均存在且 `NPCPage` 表里查得到 |
| 区域/链接缺失 | ✗ 三个区域都有 `NPCs` 反向链接指向自己 |
| 区域类型/尺寸异常 | ✗ 与「能用的」#39/#118 完全同构（`RegionType=None`、`Size=1/0`、单点区域） |
| 地图指错 | ✗ 全部指向 `MapIdx=7`（FileName「3」= Sabuk Keep），且该图**只有一条** MapInfo |
| 服务端刷怪失败 | ✗ 服务端日志无 `[NPC] Failed to spawn` / `Bad Map` |
| 客户端未收到 | ✓ 实机在该图只收到 3 个商人（#276/#278/#283），**同区域家族的传送石一个都没有** |
| 寻路/视野范围 | ✗ 商人距离玩家 11~15 格仍被收到，传送石就在 (222,159)、距玩家 2 格 |

服务端代码（`ServerLibrary/Envir/SEnvir.cs::CreateNPCs`）是按**地图加载时**
从 `targetMap.Regions[].NPCs` 收集并 `NPCObject.Spawn` 的，失败会打日志：

```csharp
foreach (NPCInfo info in npcInfos) {
    if (info.Region == null) continue;
    if (targetMap != null && info.Region.Map != targetMap) continue;
    Map map = GetMap(info.Region.Map, instance, instanceSequence);
    if (map == null) { Log("[NPC] Bad Map, ..."); continue; }
    NPCObject ob = new NPCObject { NPCInfo = info };
    if (!ob.Spawn(info.Region, instance, instanceSequence))
        Log($"[NPC] Failed to spawn NPC, Region: {info.Region.ServerDescription}, NPC: {info.NPCName}");
}
```

数据与日志都正常却不出，**下一步只能做服务端侧诊断**：在 `SEnvir.CreateNPCs`
里加一条「本次为地图 X 创建了 N 个 NPC（逐个打印 index/name/坐标）」的诊断行，
重编译服务端并重启，即可判定是「压根没进循环」「Spawn 静默失败」还是
「刷出来了但没进给玩家的对象包」。

## 五、Mud3 源里的萨巴克传送石（待补）

`Envir3/Merchant.txt` 中 `13Move_Sabuk` 共 **4** 个落点：

| Mud3 坐标 | 库内对应 | 状态 |
|---|---|---|
| (222,159) | #275 | 有记录、不出现（§4） |
| (71,140) | #374 | 有记录、不出现（§4） |
| (51,222) | #279 | 有记录（坐标与源不符）、不出现（§4） |
| (294,539) | 无 | **库内缺失**，可补 |
| (49,566) | 无 | **库内缺失**，可补 |

> 补这两条的前提是先解决 §4 的「刷不出来」——否则补进去同样不会出现。

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

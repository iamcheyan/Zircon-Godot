# 全游戏审计（2026-10-18 起）

目标：对着**网站基准数据**，把 Zircon（传奇3 移植）的**技能、怪物、地图、魔法、物品、
地图连接点坐标、NPC、任务、宠物、术语**做一次**完整**核对——数据级逐项对账 + 实机截图留档 +
可复现脚本，最后给总报告。

本目录（`docs/AUDIT/`）是这次审计的入口。**"完整"的定义**见 §2，它是可核查的清单而不是口号。

---

## 一、基准与数据源（两边都要固定版本，便于复现）

| 侧 | 来源 | 位置 | 快照 |
|---|---|---|---|
| 网站 | `wiki_data_v2.json`（结构化快照：monsters 534 / items 2203 / skills 218 / npcs 125 / quests 34 / maps 244 / companion 10 / ei_maps 544 / terminology 1701） | `Mir3-Research/docs/research/ei-ui-layout/artifacts/web-entity-audit-2026-09-26/raw/` | 2026-09-26 |
| 网站（原始页缓存） | 17173 / lomcn / sina 等 HTML 与 JSON 缓存（同上 `raw/` 目录） | 同上 | 2026-09-26 |
| 服务端库 | `System.db` 全表导出（81 张表） | `~/audit-raw/db-export-2026-10-18/out/*.json` | 库版本 `2026.10.18.1`，MD5 `1a5e0e4829abc98d7c6021650fd8c956` |
| 客户端资源 | 地图 `.map`、图库清单 `LibraryCore/Libraries.cs`、`mir2ei` 资源 | `/Users/tetsuya/mir2ei/Map/`、`LibraryCore/` | 随仓库 HEAD |
| 原版源 | Mud3 `Envir3/*.txt`、EI 客户端资源 | `Mir3-Research` 内既有考古产物 | 随仓库 HEAD |

> 库快照与"库真身在哪台机器"的背景见
> `docs/NPC_PLACEMENT_GAP_2026-10-17.md` §9（Mac 的 `~/mir2ei` 是 Syncthing receiveonly 副本，
> 写库要在 debian/82 做）。

## 二、"完整"的可核查定义

审计由三部分组成，三部分都要齐：

**A. 数据级对账（穷尽、精确、可复现）** —— 每个族群必须给出：
1. 双向覆盖：网站有而库无 / 库有而网站无（含数量与全量清单）；
2. 名称映射：映射规则 + 命中率 + 未映射清单（**不许猜**）；
3. 字段级差异：逐字段"一致 / 不一致 / 网站无此字段 / 库无此字段"的统计与样例；
4. 引用完整性：实体之间的引用是否指向存在对象（怪物↔掉落↔刷怪、NPC↔页↔动作、地图↔区域↔连接、物品↔套装）；
5. 结论分级：**确认缺陷** / **待人工确认** / **已解释（清洗或自建）**。

**B. 实机证据（截图留档并入库）**：
- NPC：全部在用 NPC（230）逐页截图 —— 已完成过一轮，2026-10-18 复跑中；
- 地图：全部有连接点的地图 + 全部**可疑**连接点，实机传送后截图；
- 怪物：全部库内怪物（126）实机刷出后截图；
- 技能：技能窗口/特效画廊截图；
- 物品：物品图标总表（离线渲染）+ 抽样实机截图。

**C. 报告**：
- 每族群一篇（本目录下 `items.md` / `monsters.md` / `skills.md` / `maps_connections.md` / `npc_quests_companion.md`）；
- 一篇总报告（`docs/GAME_AUDIT_2026-10-18.md`）：跨族群结论、TOP 缺陷、可执行修复清单、复现手册。

## 三、族群分工与产物

| 族群 | 脚本 | 机器可读结果 | 报告 |
|---|---|---|---|
| 物品（含套装/货币/商店/图标） | `tools/audit/items_audit.py` | `.artifacts/game-audit-2026-10-18/data/items.json` | `docs/AUDIT/items.md` |
| 怪物（含属性/刷怪/掉落） | `tools/audit/monsters_audit.py` | `…/data/monsters.json` | `docs/AUDIT/monsters.md` |
| 技能/魔法（含图标/职业/曲线） | `tools/audit/skills_audit.py` | `…/data/skills.json` | `docs/AUDIT/skills.md` |
| 地图与连接点坐标 | `tools/audit/maps_connections_audit.py` | `…/data/maps_connections.json` | `docs/AUDIT/maps_connections.md` |
| NPC/任务/宠物/术语 | `tools/audit/npc_quests_audit.py` | `…/data/npc_quests_companion.json` | `docs/AUDIT/npc_quests_companion.md` |

产物根目录：`.artifacts/game-audit-2026-10-18/`（`data/` 机器可读结果、`shots/` 实机截图、
`thumbs/` 入库用缩略图、`sheets/` 图标与总表）。

## 四、验证纪律（本次审计必须遵守）

1. **校验逻辑独立实现**：核对 `.map` 通行性/连接点等，另写解析实现，不复用生产工具的函数
   （生产工具与校验工具共用同一错误时，错误会被"自洽"掩盖）。
2. **不猜**：名称映射优先用既有映射表；无法映射的单列"待人工确认"。
3. **区分证据等级**：实机验证 > 静态代码推断 > 猜测。报告里必须标明每条结论属于哪一级。
4. **只读**：审计过程不改 `System.db`、不改 `~/mir2ei`、不改客户端资源；需要修的先出清单。
5. **截图入库要压过**：整屏 PNG 原图留本地 `~/audit-raw/`，入库用
   `tools/pack_audit_shots.sh` 压成缩略图（400px/q70，约 42KB/张）。

## 五、进度台账

| 阶段 | 内容 | 状态 |
|---|---|---|
| 地基 | 库全表导出（81 表）+ 网站快照确认 | ✅ 2026-10-18 |
| 数据级 | 物品对账 | 🔄 子代理进行中 |
| 数据级 | 怪物对账（含刷怪/掉落） | 🔄 子代理进行中 |
| 数据级 | 技能/魔法对账 | 🔄 子代理进行中 |
| 数据级 | 地图与连接点坐标审计 | 🔄 子代理进行中 |
| 数据级 | NPC/任务/宠物/术语对账 | 🔄 子代理进行中 |
| 实机 | 230 NPC 复跑（新库 + #275 修复后） | 🔄 后台运行，chunk 逐块产出 |
| 实机 | 地图与连接点截图 | ⏳ 待巡检结束后跑 |
| 实机 | 怪物截图（126） | ⏳ 待巡检结束后跑 |
| 实机 | 技能画廊 / 物品图标总表 | ⏳ |
| 汇总 | 总报告 + 修复清单 | ⏳ |

## 六、复现（一条条照抄即可）

```bash
# 1) 导出现库全表（只读；需 MIR3_ZIRCON_ROOT 才能编译探针）
cd ~/Development/Mir3-Research
export MIR3_ZIRCON_ROOT=/Users/tetsuya/Development/Zircon
mkdir -p ~/audit-raw/db-export-2026-10-18/{out,db}
cp /Users/tetsuya/mir2ei/Database/System.db ~/audit-raw/db-export-2026-10-18/db/System.db
dotnet run --project Tools/SystemDbProbe --no-build -- \
  --json ~/audit-raw/db-export-2026-10-18/out/ ~/audit-raw/db-export-2026-10-18/db/

# 2) 各族群对账（纯离线）
cd /Users/tetsuya/Development/Zircon
python3 tools/audit/items_audit.py
python3 tools/audit/monsters_audit.py
python3 tools/audit/skills_audit.py
python3 tools/audit/maps_connections_audit.py
python3 tools/audit/npc_quests_audit.py

# 3) 实机巡检（需先起服务端；脚本可续跑，按块跳过已完成）
bash /tmp/zircon_run_server.sh &        # 服务端
# ⚠ macOS 上 7000 常被系统 AirPlay 接收器（ControlCenter）占用，此时客户端会「连上 7000
#   但一直不完成登录」（连到的是 AirPlay 不是游戏服）。跑巡检前务必确认服务端真实端口：
#     lsof -nP -p $(pgrep -f ServerCore.dll) -a -iTCP -sTCP:LISTEN | grep LISTEN
#   然后客户端用同一个端口，例如 --port 7001。此坑只影响 Mac 本机巡检，不影响 82 上的正常流程。
OUT=$HOME/audit-raw/npc-full-sweep-$(date +%Y-%m-%d) bash tools/npc_full_sweep.sh

# 4) 截图入库（缩略图）
bash tools/pack_audit_shots.sh ~/audit-raw/npc-full-sweep-2026-10-18/shots \
     .artifacts/game-audit-2026-10-18/thumbs/npc 400 70
```

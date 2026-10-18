# 全游戏审计总报告（2026-10-18）

> 审计范围与"完整性"定义见 [AUDIT/README](AUDIT/README.md)；各族群明细见 [AUDIT/items](AUDIT/items.md)、
> [AUDIT/monsters](AUDIT/monsters.md)、[AUDIT/skills](AUDIT/skills.md)、
> [AUDIT/maps_connections](AUDIT/maps_connections.md)、[AUDIT/npc_quests_companion](AUDIT/npc_quests_companion.md)。
> 机器可读结果在 `.artifacts/game-audit-2026-10-18/data/`，实机截图在 `.artifacts/game-audit-2026-10-18/thumbs/`。

## 零、一句话结论

对"网站基准（实为清洗前库镜像）↔ 现库 ↔ 客户端资源 ↔ 原版源码"做了五族穷尽对账 + 实机取证后，
**发现并已修掉 4 类会造成玩家可见故障的缺陷**（跨天崩服、完成任务断会话、39 张地图不加载、修理界面不可用），
另有 3 类数据回退（零属性怪物、掉落被清空、物品词条丢失）与传送端点问题在修；
口径类问题（是否回填被"经典纯净清洗"移除的 200 只怪物 / 581 件物品 / 34 条任务奖励）**未擅自恢复**，单列待产品决策。

## 一、基准的真实身份（方法学上最关键的一条）

网站快照 `wiki_data_v2.json` 经逐项核对，**就是本仓清洗前数据库的镜像**：

- 怪物：网站 309 条 zircon 怪物的 `id` 集合与清洗前备份（`Backup/System/System 2026-09-25 21-47.db.gz`）的
  `MonsterInfo.Index` 集合**完全相等**；244 张地图同样相等；每图刷怪数量 1023 组中 1015 组精确相等。
- 物品：网站 `img.frame` 来自旧库导出，且 `ver_tags.py` 会给每条无条件打 `zircon` 标签 → 网站不是独立第三方数据。

**推论**：所有"网站有、库里没有"的差异 = 本仓 2026-10-03「经典纯净清洗」的移除结果，不是外部资料缺口。
因此本次审计的判定基准改为**双证据链**：网站快照 + 清洗前库基线（Aug 13 dbeditor baseline / Sep 25 备份）。

## 二、修复台账（全部走：影子导入 → 82 入库 → 字节级核对 → 实机复验 → 提交）

| # | 缺陷 | 玩家可见症状 | 修复 | 提交 | 验证 |
|---|---|---|---|---|---|
| 1 | **跨天崩服**：`EnvirLoop` 日界分支里 `member.Account` 未判空 | 每天 00:00 服务端整机停摆（`Network Stopped.`） | 判空跳过 + 计数日志（仅异常时打印） | `d743432c` | 服务端日志逐字对上（23:59:59 空引用 + EnvirLoop + Network Stopped）；重启后 0 异常；完整闭环待下一个午夜 |
| 2 | **完成任务即断会话**：`QuestReward` #143/#145/#148 的 `Item` 为空 → `ItemCheck` 构造函数空引用 → 收包 catch 后 rethrow | 完成"望海楼之约（战/法）""刺杀剑术觉醒"当场掉线 | 奖励循环 + 名望奖励循环加守卫（缺 Item 跳过，其余照发） | `c26b7bd8` | 实机复跑任务类 NPC（#105/#135）ok=2，服务端存活 |
| 3 | **39 张发行地图不加载**：单元格 13 字节/格，客户端按 14 读抛 `EndOfStreamException`、服务端越界 | 进这些图地形全空 / 服务端可能异常 | 两边按文件长度自动识别格大小（13/14） | `74240000` | `--map-audit` 39 张全跑：**loaded=39 failed=0**；服务端逐张记录"单元格 13 字节/格"且无异常 |
| 4 | **修理界面不可用 / 样式错乱**：修理面板在 legacy EI 下无皮肤处理、11×5 维修格根本没画、面板过宽导致控件落在木框外、费用行重复标签 | 修理时看不到能放装备的格子、面板"透视"、按钮浮在草地上 | 补 EI 三段式木质框架（只用中段平铺）+ **自绘 11×5 维修格底** + 收窄到可见区 384px + 费用行去重/移位 + 修理钮内移 | `21574cc9`/`8769e6dc`/`be1d6aa7`/`194ac2a4`/`3e2aa601` | 每轮实机 `ok=1`；4 轮独立看图复核："背景不透明化与标签去重是实质修复""E 明显变好" |
| 5 | 韩文烤字混排 | 修理/出售/储存按钮显示韩文 수리/판매/보관 | 三个模式改用中文文字钮 | `39aae281` | OCR 客观核对：出现"修理物品/维修费用/背包/修理"，无韩文 |
| 6 | 地图文件名大小写无回退（**加固**，非当前缺陷） | 未来 DB 与磁盘漂移时 Linux 上 `Map not found` | 服务端加与客户端同款的大小写回退 | `ca322e4e` | 用库内真实名字跑 10 张：loaded=10 failed=0 |
| 7 | 审计证据链 | — | 230 NPC 复跑 1535 张截图（缩略图 41MB 入库）+ 五族报告/脚本/JSON | `21574cc9` | 230/230、零异常 |

## 三、各族群核心发现（详见各分册）

- **物品**：19 件经典药水丢 `Health`/`Mana`（回血=0）；7 个元素护身符丢 `*Affinity`；4 件消耗品 `Shape` 被改
  （**重生丹 → 复活整体失效**）；25/30 套装零成员；499/499 物品图标全部可解析。
- **怪物**：15 只怪物 `MonsterInfoStat` 0 行但仍在刷怪（血/攻防全 0）；12 只仍在刷怪的掉落被清空；
  107 只存活怪物属性**逐字段 0 差异**（问题在覆盖而非数值）；202 只被清洗移除（含 50 BOSS）。
- **技能**：相对清洗前快照**少 113 条 `MagicInfo`**（其中 112 个服务端早已有实现类 → 学不到、窗口不显示，刺客 53 个全灭）；
  4 个技能图标指向不存在帧；7 本技能书图标用人参素材；legacy 技能窗只有 8 个页签 → 14 个技能完全不可见。
- **地图/连接**：1757 条连接、3514 端点中 **10 个端点不可站**（踩门没反应 / 回程门不存在）；
  8 张在库却 0 刷怪的图；30 张只进不出 / 12 张只出不进。
- **NPC/任务/宠物/术语**：**27 个在用 NPC 点了没有可交互项**（19 个仅静态确证）；#106 六面神石点击失败复现；
  34 条经典任务零奖励（且当前模型无经验字段）；8 条击杀任务缺怪物明细（永远无法推进）；
  宠物 10/10 字段零分歧；术语表 1701 条质量良好但跨表分歧 222/456/176。

## 四、口径类问题（**未擅自恢复**，待决策）

1. **被清洗移除的内容**：202 只怪物（975 行刷怪 + 6467 行掉落）、581 件物品、175 张图。
   恢复与否属玩法口径（经典纯净 vs 完整内容），本次只修"仍在使用却坏了"的对象。
2. **34 条经典任务的奖励**：网站基准每条都标 `Experience ×N`，但现库 `QuestInfo`/`QuestReward` 无经验字段
   → 属**模型扩展 + 数值回填**，不是 bug 修复。
3. **套装成员**：25/30 套装零成员是有意精简还是导入遗漏，需口径确认后统一处理。

## 五、还有待完成（本报告随进度更新）

| 项 | 状态 |
|---|---|
| 零属性怪物 + 掉落被清空 + 物品词条回退（数据补回） | 补丁脚本生成中（dry-run 复核后才 apply） |
| 10 个不可站传送端点 | 补丁脚本生成中 |
| 套装成员回填 | 待上两项的口径与管线复用 |
| HUD 全入口实机走查（通道已就绪 `--hud-ui-audit`） | 待地图走查结束后跑 |
| 全量地图实机截图（412 张有连接点的图，通道 `--map-audit`） | 运行中 |
| 怪物实机截图（126 只，通道 `--monster-audit`） | 通道已就绪，待跑 |
| 各分册 / 本节随修复更新 | 进行中 |

## 六、复现手册

```bash
# 库全表导出（只读）
cd ~/Development/Mir3-Research && export MIR3_ZIRCON_ROOT=/Users/tetsuya/Development/Zircon
dotnet run --project Tools/SystemDbProbe --no-build -- \
  --json ~/audit-raw/db-export-2026-10-18/out/ ~/audit-raw/db-export-2026-10-18/db/

# 五族数据级对账（纯离线、stdout 摘要 + JSON 详表）
cd /Users/tetsuya/Development/Zircon
python3 tools/audit/items_audit.py && python3 tools/audit/monsters_audit.py \
 && python3 tools/audit/skills_audit.py && python3 tools/audit/maps_connections_audit.py \
 && python3 tools/audit/npc_quests_audit.py

# 实机通道（需先起服务端 127.0.0.1:7001；均为 GM 命令驱动）
bash /tmp/zircon_run_server.sh &
OUT=$HOME/audit-raw/npc-full-sweep-$(date +%Y-%m-%d) bash tools/npc_full_sweep.sh   # 230 NPC 逐页
godot-mono --path GodotClient -- --map-audit --map-audit-only "D601,D602" --map-audit-out /tmp/map_audit
godot-mono --path GodotClient -- --monster-audit --monster-audit-only "Guard,Pig" --monster-audit-out /tmp/monster_audit
godot-mono --path GodotClient -- --hud-ui-audit --hud-ui-audit-out /tmp/hud_ui_audit

# 截图压缩入库（400px/q70，约 27KB/张）
bash tools/pack_audit_shots.sh <src> .artifacts/game-audit-2026-10-18/thumbs/<name> 400 70
```

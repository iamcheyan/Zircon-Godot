# NPC / 任务 / 宠物 / 术语本地化 四方对账（2026-10-18）

只读数据对账：**网站基准**（Mir3-Research 的 EI 资料站抓取快照）vs **当前 System.db 全表导出**。
覆盖 `npcs` / `quests` / `companion` / `terminology` 四块。

- 脚本：[`tools/audit/npc_quests_audit.py`](../../tools/audit/npc_quests_audit.py)（纯离线，不联网、不读 System.db、不跑客户端）
- 结果 JSON：`.artifacts/game-audit-2026-10-18/data/npc_quests_companion.json`（约 400 KB，含全部逐条明细）
- 输入快照：网站 `wiki_data_v2.json` 2026-09-26；库导出 `/Users/tetsuya/audit-raw/db-export-2026-10-18/out/`（2026-10-18 08:05）

> 本文所有数字均来自上面这次运行的 JSON，可用同一命令复现（见 §2）。
> 实机巡检（`~/audit-raw/npc-full-sweep-2026-10-18/`）在本文写作时**仍在跑**，快照时已回 **135/230**。

---

## 1. 结论速览

| # | 区块 | 一句话结论 |
|---|---|---|
| 1 | NPC 双向对账 | 网站 125 / 库 294（在用 230 + 预留模板 64）。网站有库无 **47**：37 是「地图本身已被经典纯净清洗移除」，9 是「功能已被库内另一个 index 承下」，真正可疑只有 **1** 条（#89 六角圣石）。库有网站无：**152**（在用）+ 64 模板。**join 可信度两项硬证据：78/78 职能位一致、任务挂载数 0 处不一致**；但**中文名 0/78 一致**（库内原版中文名 vs 网站音译名，两套体系） |
| 2 | NPC 服务链路 | 页图**完全闭合**（入口页/成功页/按钮/动作/商店 0 处悬挂）。但 **27 个在用 NPC 点了没有可交互项**：4 个走原版 `fallback:entry` 兜底页、1 个入口文本为空、22 个只有一句旁白/描述 |
| 3 | 任务对账 | 网站 34 / 库 38（多出的 4 条是 Zircon 自建样板）。**34 条经典任务在库内一条奖励都没有**，而网站明确标注 `Experience ×N`；**5 条任务**（含 8 条击杀任务）因怪物明细被清洗而永远无法完成；**3 条自建任务的奖励行 `Item` 为空**，玩家点完成会触发服务端空引用→掉线 |
| 4 | 宠物对账 | 网站 10 / 库 10，**字段零分歧**（名称、monster_id、价格、可购买性全对）。仅 1 处格式瑕疵（#8 `Pachon ` 名称带尾空格）。等级表 1–15 连续，技能表覆盖 7 个等级共 48 行 |
| 5 | 术语本地化 | 术语表 1701 条**质量良好**（无空值、无中英同形）。内部冲突 8 项（7 项是别名/地图占位，1 项 `Jack`/`Jake`→`杰克` 是真冲突）。真正的风险不在术语表本身，而在**多张中文词表互相不一致**：术语表 vs `zh-glossary` 分歧 222、vs `db_names` 分歧 456、vs `chinese_alias` 分歧 176；同一文件内 `items`↔`magics` 段还自相矛盾 86/92 条；库内 NPC 中文名与网站词表 **0/78 相同** |

### 玩家可见影响排序（Top 5）

1. **完成自建任务会被踢下线**（`QuestReward.Item` 为空 → `CanGainItems` 空引用）— 望海楼之约（战士/法师）、刺杀剑术觉醒
2. **34 条经典任务零奖励**（网站写明 `Experience ×N`），完成之后什么都没发生
3. **5 条任务永远无法完成**（击杀任务缺怪物明细）+ **2 条任务可瞬间完成**（无目标任务 `IsComplete` 恒真）
4. **27 个在用 NPC 点了没反应**（4 个明确是占位兜底页，1 个是空白框 + 直接传送）
5. **#106 六面神石仍是"点不到"**（2026-10-17 已记录，本轮实机复现 `click_missed`）

---

## 2. 方法与可复现命令

### 2.1 直接复现

```bash
cd /Users/tetsuya/Development/Zircon
python3.14 tools/audit/npc_quests_audit.py            # stdout 摘要 + 刷新 JSON
python3.14 tools/audit/npc_quests_audit.py --help      # 所有输入路径可覆盖
```

可选参数（默认值即本次使用的输入）：

```bash
--web   <wiki_data_v2.json>        # 网站基准
--dbdir <db-export-.../out>        # System.db 全表导出目录
--sweep <npc-full-sweep-...>       # 实机巡检产物（可选，用于交叉验证）
--out   <result.json>
```

脚本行为约束：不联网；不打开/写 `System.db`；不启停客户端或服务端；只写 `--out` 指定的那个 JSON（其余输出进 stdout）。
确定性已验证：连跑两次除 `meta.generatedAt` 外逐字节一致。

### 2.2 判定口径（避免误报）

| 口径 | 定义 | 为什么这样定 |
|---|---|---|
| **join 方式** | 网站 `npcs[].id` / `quests[].id` / `companion[].id` **直接等于** 库 `NPCInfo.Index` / `QuestInfo.Index` / `CompanionInfo.Index` | 独立验证：78 个两侧都有的 NPC，其 `quests_in/quests_out` 与库内 `StartQuests/FinishQuests` 数量 **78/78 全等**；companion 的 `monster_id`/`price`/`available` 10/10 全等。名称本身不可用作 key（库内 NPC 用原版中文名 啊康/恩实，网站用英文 Mr. Kang/Amy） |
| **在用 NPC** | `NPCInfo.Region != null` → 230 条，与 `tools/npc_audit_manifest.json` 的 230 条**完全相等** | 反向链接（地图区域→NPC）能收集到的就是这 230 条 |
| **预留模板** | `Region == null` → 64 条，无地图无坐标 | 与 [`NPC_PLACEMENT_GAP_2026-10-17.md`](../NPC_PLACEMENT_GAP_2026-10-17.md) §3 结论一致：**不是丢失，是功能预留** |
| **可达页** | 从 `EntryPage` 出发沿 `SuccessPage` + `NPCButton.DestinationPage` 做 BFS | 这只用于找结构性断链；**页数与实机点开数不同不代表缺陷**（脚本链中间页实机不单列） |
| **点了没反应** | 可达集合内既无 `NPCButton`、又无 `NPCAction`、又无 `NPCGood/NPCType`，且所有可达页 `DialogType` 都是 `None` | 「只剩一个关闭按钮」也算无交互 |
| **任务能否完成** | 击杀任务只有当 `QuestTaskMonsterDetails` 命中才累加进度 | 依据 `ServerLibrary/Models/MonsterObject.cs:3070-3095`（`valid` 只在明细命中时为真） |
| **SuccessPage 死循环** | 只统计**全部由空 Say 页**构成的环 | 依据 `ServerLibrary/Models/NPCObject.cs:24-52`：只有 Say 为空才自动跳 SuccessPage。**环里只要有非空 Say 页就会停下，不是缺陷**（本库有 5 个这种"回退环"，已单列排除） |

### 2.3 复用的既有成果（未重复造轮子）

| 来源 | 复用点 |
|---|---|
| [`docs/NPC_FULL_SWEEP_2026-10-17.md`](../NPC_FULL_SWEEP_2026-10-17.md) | 230 NPC 全量巡检基线（1535 页/1535 截图，仅 1 处 `click_missed`）；服务类型覆盖表 |
| [`docs/NPC_PLACEMENT_GAP_2026-10-17.md`](../NPC_PLACEMENT_GAP_2026-10-17.md) | 「230 在用 + 64 预留模板」的口径与结论；#275 萨巴克传送石修复记录 |
| `tools/npc_audit_manifest.json` | 230 条在用清单（本脚本核对后确认与 `Region != null` 集合完全相等） |
| `tools/aligned_npcs_230.json` | NPC 重命名后的 mud3 对齐映射（本轮不新增名字匹配） |
| `~/audit-raw/npc-full-sweep-2026-10-18/`（后台在跑） | 逐 NPC 实机点击结果，用于把"静态推断"升级为"已实机证实" |
| `docs/DATABASE_CLASSIC_PURITY_CLEANUP_PLAN.md` | 清洗前后行数（`QuestReward 42→4`、`QuestTaskMonsterDetails 55→40`），解释了本轮的缺失成因 |
| `Mir3-Research/Tools/NpcMover/*` | 放置/对齐工具链（本轮只读核对，未调用它们的写入路径） |

---

## 3. NPC 对账

### 3.1 总量

| 项 | 数量 |
|---|---|
| 网站 `npcs` | 125（`ver` 全部为 `["zircon"]`，无需按版本过滤） |
| 库 `NPCInfo` | 294 |
| ├ 在用（`Region != null`） | **230**（= 巡检清单 230，完全相等） |
| └ 功能预留模板（`Region == null`） | **64** |
| 两侧都有（id 直接 join） | **78** |
| └ 其中「职能位」双向一致 | **78 / 78**（用同一套功能桶归一化后比对，见 §3.5） |
| └ 其中「中文名」一致 | **0 / 78** ← 两套命名体系（库内原版中文名 vs 网站音译名） |
| 网站有、库无 | **47** |
| 库有、网站无 | 在用 **152** + 模板 **64** |
| 双向任务挂载数不一致 | **0** |

### 3.2 网站有、库无（47）— 三类归因

| 类别 | 数量 | 明细 |
|---|---|---|
| ① 地图已被经典纯净清洗移除（连带 NPC） | **37** | Bichon Castle(10) ×16、Taoist Temple(11) ×11、Homeland TroopBase(D005) ×5、Infernal Island(7) ×2、Lost Land(D3400) ×2、Banya Temple Lv 5(D1005) ×1。这 6 张图**不在**当前 `MapInfo`(627) 内 |
| ② 功能已被库内另一个 index 覆盖 | **9** | 见下表，全在 Sabuk Keep(3) |
| ③ 真正可疑的缺失 | **1** | #89 六角圣石「传送到巴尼亚神殿」(map D10031) |

② 明细（网站 NPC → 库内实际承下该功能的 NPC）：

| 网站 | 功能 | 库内承下者 |
|---|---|---|
| #47 Maddox `Weapon Refiner` | 武器精炼 | #276 沙巴克炼化师 `3 / Refine Smith` |
| #49 Perry `Basic Potion Main - Castle` | 药水 | #277 沙巴克药师 `3 / Potion Store` |
| #51 Kellen `Essentials Main - Castle` | 杂货 | #278 沙巴克杂货商 `3 / Essential Store` |
| #57 Denise `Jewellery Main` | 首饰 | #280 沙巴克首饰商 `3 / Accessory Store` |
| #58 Mark `Collector BuySell` | 收购 | #281 沙巴克鉴宝师 `3 / Collector Store` |
| #59 Kimberly `Armour Main` | 防具 | #282 沙巴克裁缝 `3 / Armour Store` |
| #64 Jarvis `Weapon Main` | 武器 | #283 沙巴克武器商 `3 / Weapon Store` |
| #40 / #55 `SK Teleporter` | 传送 | #275 / #279 六面神石（另有 #374） |

> 结论：Sabuk Keep 的**功能覆盖是完整的**，缺的是「NPC 实体身份/外观/名字与官网一致」。
> 库内这 8 条是 Zircon 自建的「沙巴克套件」（`IR276|service:entry` 等自造页），衬衣/头像与原版 EI 的 Maddox/Jarvis 不同。
> 属于**取舍而非缺陷**，但若要"官网一致"，这是 9 处待决。

③ #89 细节：`D10031 = Banya Temple Lv 3-W` **在 MapInfo 内（Index 18）**，且有 9 个 `MapRegion`、与 `D1002/D1004` 双向 Movement 各 1 条 → **可以步行进入**，只是没有传送 NPC。
影响：便利性缺口，非阻断。建议补 1 条 NPCInfo（或确认"故意不设"）。

### 3.3 库有、网站无（在用 152 + 模板 64）

| 类别 | 数量 | 说明 |
|---|---|---|
| 模板（`Region == null`） | 64 | 泡点系统/VIP/名望系统/钓鱼系统/钻石抽奖/仓库系统×3/买卖商人×3/天下第一战法道/各图接待（神舰/诺玛/西沙/冰城/绝情/魔界/群魔/异界/邪恶/龙穴）… 与旧文档结论一致：**服务端按玩法动态放置或由窗口实现**，不应硬塞进地图 |
| 在用 · 商人 | 63 | 主要是**地牢类的补给商**（努玛/潘夜/沙巴克/蛇谷系），官网 125 条只收录"城镇服务位" |
| 在用 · 传送 | 39 | 各图 / 各层入口六面神石 |
| 在用 · 仓库 | 6 | 客栈保管员 / 赵老头等 |
| 在用 · 其它 | 44 | 剧情/任务/场景 NPC（万事通、龙血先生、变异骷髅、石堆、书架…） |

> 判定：**不是"多余内容"**。网站 `npcs` 是一个"城镇服务位 + 告示牌 + 传送石"的精选集，不是全量 NPC 清单；库内的地牢商人与剧情 NPC 属于正常补充。
> 唯一需要人工确认的是：官网收录的城镇服务位 vs 库内地牢商人的**命名风格不一致**（见 §6）。

### 3.4 双向清单获取

JSON 里是**全量**：
`npc.webOnly`（47，每条带 `kind` / `mapName` / `coveredBy`）、`npc.dbOnlyInUse`（152）、`npc.dbReservedNames`（64）。

### 3.5 join 可信度与「两套命名体系」（重要）

对 78 个两侧都有的 NPC 做两项独立核对（`npc.namingCompare`）：

| 核对 | 结果 | 含义 |
|---|---|---|
| 职能位（区域描述 vs 官网 name/desc，按功能桶归一化） | **78 / 78 一致** | index join **语义上站得住**：同一 index 两侧指的是同一个职能位，不是巧合 |
| 中文名（官网 `zh` vs 库 `NPCName`） | **0 / 78 一致** | **两套完全不同的命名体系** |

例（左=官网英文名/音译，右=库内名字）：

| index | 区域描述（两侧一致） | 官网 name / zh | 库 `NPCName` |
|---|---|---|---|
| 13 | Weapon Store | Mr. Kang / 康先生 | **啊康** |
| 15 | Potion Store | David / 大卫 | **药店老板** |
| 17 | Accessory Store | Amy / 艾米 | **恩实** |
| 18 | Collector Store | Loy / 洛伊 | **栗子商人** |
| 19 | Armour Store | Linda / 琳达 | **怡美** |
| 22 | Book Store | Gresham / 格雷沙姆 | **天星** |
| 21 | Weapon Store | Hardy / 哈迪 | **王铁匠** |
| 39 | Teleport Stone | Hexa Holy Stone / 六角圣石 | **六面神石** |

> 结论：**库内的中文名是原版 Mud3 的中文名**（更"传奇味"：啊康/恩实/栗子商人/王铁匠），
> 而网站基准与 `ClientData/zh` 词表用的是**英文名的音译**（康先生/艾米/洛伊/哈迪）。
> 这不是数据缺陷——但**任何人拿网站词表去核对游戏内名字，都会 100% 对不上**（0/78）。
> 若将来有"攻略站 ↔ 游戏名"联动需求，必须先建一张 index 级权威对照表。

---

## 4. NPC 服务链路完整性

### 4.1 结构完整性：干净

| 检查 | 结果 |
|---|---|
| `NPCInfo.EntryPage` 指向不存在的页 | **0** |
| `NPCPage.SuccessPage` 指向不存在的页 | **0** |
| `NPCButton.Page` / `DestinationPage` 指向不存在的页 | **0 / 0** |
| `NPCAction` / `NPCGood` / `NPCType` / `NPCValue` / `NPCCheck` 的 `Page` 悬挂 | **0 / 0 / 0 / 0 / 0** |
| 纯空 Say 页构成的 `SuccessPage` 死循环（会挂死 `NPCCall` 的 `while(true)`） | **0** |
| `SuccessPage` 成环但停在有文本页（原版"回退"写法，非缺陷） | 5（#105/#241/#243/#244/#245） |

> 也就是说：**入口页缺失**在结构层是 0；问题全部出在"页面存在但没有可点内容"。

### 4.2 逐 NPC 交互性（230 个在用）

| 指标 | 数量 |
|---|---|
| 有按钮 / 有动作 / 有商店 / 有特殊对话框 | 203（= 230 − 27） |
| **27 个确认缺陷** | 见下表 |
| 8 个仅提示（原版 `_test` 页命名、`mugong_*` 练习场页） | 不影响交互，仅命名遗留 |

27 个确认缺陷（三小类）：

| 小类 | 数量 | 特征 | 明细 |
|---|---|---|---|
| **A. 原版兜底页 `fallback:entry`** | 4 | Say 就是"这里暂时没有可以交涉的事务。"，只有一个「离开」 | #99 潘夜码头管理员、#270 牢牲、#272 夏柯、#273 紫霞神女 |
| **B. 入口 Say 为空** | 1 | 打开即空白框，但**带 Teleport 动作**（无提示直接传送） | #195 清明子（`IR195|amyen:mugong_poison_test_next`，动作 → 地图 `1_002` (11,11)） |
| **C. 只有一句旁白/描述，无任何可点项** | 22 | 场景道具 / 未接的剧情位 | #141 石母、#157 智善、#158 无名商人、#159 云发、#162 黄河大侠、#193 无名老人、#198/#199/#201/#202/#203 变异骷髅、#232 泼皮路白、#238/#239 云发、#240 嘉登、#241 无理的道士、#243/#244 署箭、#245 牛老道、#246 石堆、#247 书架、#274 云中子 |

> C 类里 **#246 石堆 / #247 书架 / #141 石母 / #232 泼皮路白** 明显是"可点击场景物"（描述式文案），
> **#243/#244 署箭、#241 无理的道士、#245 牛老道、#159/#238/#239 云发** 只有一句「（。。。。。）」——
> 这两组的**原版设计意图不同**，建议拆开验收：前者大概率是有意为之，后者是剧情脚本占位。

### 4.3 实机交叉验证（本文快照 135/230）

| NPC | 静态判定 | 实机结果 | 关系 |
|---|---|---|---|
| #99 潘夜码头管理员 | A 兜底页 | 1 页（36177，links=[0]） | ✅ 证实 |
| #141 石母 | C 旁白 | 1 页（36431，links=[0]） | ✅ 证实 |
| #157 智善 | C 旁白 | 1 页（36718，links=[0]） | ✅ 证实 |
| #158 无名商人 | C 旁白 | 1 页（36719，links=[0]） | ✅ 证实 |
| #159 云发 | C 旁白 | 1 页（36720，**links=[] 连关闭项都没有**） | ✅ 证实（最差） |
| #162 黄河大侠 | C 旁白 | 1 页（36725，links=[0]） | ✅ 证实 |
| #193 无名老人 | C 旁白 | 1 页（37201，links=[0]） | ✅ 证实 |
| **#195 清明子** | B 空白 + Teleport | **`entryPage=-1`，唯一一条 `type="entry_action"`** | ✅ **证实"点击后不弹框、直接传送"** |
| #106 六面神石 (Banya Village 314,193) | 已知问题 | `clicked=false`，`errors=["click_missed"]` | ⚠️ **仍未解决**（10-17 已记录，本轮复现） |
| #196 清明子 | 非缺陷（有文本 + Teleport） | 1 页（37206，links=[0]） | ✅ 与静态一致（有提示文本后传送，正常） |
| #194 清明子 | 非缺陷 | 2 页，3 个选项 | ✅ 与静态一致 |
| #105 月老司仪 | 非缺陷 | 5 页，含 `DialogType=WeddingRing` | ✅ 与静态一致 |
| #118 六面神石 (map 8) | 非缺陷 | 11 个选项，点第一个触发传送 | ✅ 与静态一致 |
| 其余 122 个 | 非缺陷 | 全部 `clicked=true`、0 报错 | ✅ 与静态一致 |

> **零反证**：实机已覆盖 135 个，其中 **8 个落在本报告的缺陷清单里，8/8 与静态预测完全一致**；
> 另外 127 个静态判定正常的 NPC 实机也全部正常（含 #106 这一例既有问题）。
> 剩余 19 个缺陷 NPC 的 `liveStatus = "not_swept"`，属**静态确证、待实机复核**（§8.2）。
> 巡检继续跑完后重跑一次脚本即可自动刷新（`npcService.service[].liveStatus` / `npcService.live`）。

**#106 值得单独说明**：库里 #106 的 `Region` 是 `2 / 2 / Item Fragment`（对应官网的 Jack「Item Fragment」），
但它的 `EntryPage` 是 `IR106|SnakeVallyTele:SnakeVally_Teleport_North`（一个**传送石脚本**）。
即：**index 106 被登记成了"物品碎片商人"的位置，实际 NPC 是蛇谷传送石**。实机点不到，与本条 Region/坐标登记错位一致。

---

## 5. 任务对账

### 5.1 总量与双向差异

| 项 | 数量 | 明细 |
|---|---|---|
| 网站 `quests` | 34 | — |
| 库 `QuestInfo` | 38 | — |
| 网站有库无 | **0** | — |
| 库有网站无 | **4** | #63/#64/#65 望海楼之约（战/法/道）、#66 刺杀剑术觉醒 — **Zircon 自建样板** |

### 5.2 结构完整性

| 检查 | 结果 |
|---|---|
| 每条任务的 `StartNPC` / `FinishNPC` 指向存在的 NPC | 38/38 ✅ |
| `AcceptText` / `ProgressText` / `CompletedText` / `ArchiveText` 非空 | 38/38 ✅ |
| `QuestTask.ItemParameter` → `ItemInfo` 悬挂 | **0** |
| `QuestTaskMonsterDetails.Monster` → `MonsterInfo` 悬挂 | **0** |
| `QuestTask.RegionParameter` → `MapRegion` 悬挂 | **0** |
| `QuestReward.Item` → `ItemInfo` 悬挂 | **0**（但见 5.3.3） |
| 孤儿行 | `QuestTask` **1**（#35：无 Quest、Amount 0）、`QuestRequirement` 0、`QuestReward` 0、`MonsterDetails` 0 |

### 5.3 三类确认缺陷

#### 5.3.1 击杀任务缺怪物明细 → 任务永远无法推进（5 个任务 / 8 条任务行 + 1 条孤儿）

依据 `ServerLibrary/Models/MonsterObject.cs:3070-3095`：击杀任务的进度**只在 `QuestTaskMonsterDetails` 命中时累加**；
明细为空 → 该任务永远停在 0，`UserQuest.IsComplete` 永远为 false → **整条任务链卡死**。

| 任务 | 任务行 | 数量 | 网站点名的怪 | 库内是否还有 | 可否直接修 |
|---|---|---|---|---|---|
| #17 Time for the Fleas to Flee | 20,21,22,23 | 50/50/50/1 | Mutant Flea ×3、Terror Spike | ❌ 全被清洗 | 需恢复怪物或改目标 |
| #36 Reclaiming the Carved Tomb Pt. 3 | 46 | 20 | Wedge Moth Larva | ❌（`Wedge Moth` 在） | ✅ 可改指 `Wedge Moth` |
| #37 Reclaiming the Carved Tomb Pt. 4 | 47 | 1 | Razor Tusk | ❌ | 需恢复怪物 |
| **#56 Descent into Darkness Pt. 1** | **66** | **40** | **Bone Soldier** | **✅ 在（Index 119）** | ✅ **直接补明细即可** |
| **#57 Descent into Darkness Pt. 2** | **67** | **50** | **Bone Captain** | **✅ 在（Index 118）** | ✅ **直接补明细即可** |

> 成因清楚：`docs/DATABASE_CLASSIC_PURITY_CLEANUP_PLAN.md` 记载 `QuestTaskMonsterDetails 55 → 40`，
> 被删的 15 行正是指向被清洗怪物的明细，但**任务行本身没删** → 留下"有目标数量、无目标怪物"的空壳任务。
> JSON 里 `quests.fixableKillTasks` 直接给出可立即修的候选。

#### 5.3.2 34 条经典任务零奖励（网站写 `Experience ×N`）

- 库 `QuestReward` 只剩 4 行（清洗前 42 行），且**全部属于 Zircon 自建的 #63/#64/#66**。
- 网站 34 条经典任务的 `rewards` 字段**每条都写了 `Experience ×N（全职业）`**（288 ~ 158800）。
- 服务端 `PlayerObject.QuestComplete()` 只按 `quest.Rewards` 发物品，`QuestInfo`/`QuestReward` **都没有经验字段**
  （`LibraryCore/SystemModels/QuestInfo.cs` 全表字段确认）。

> 结果：34 条任务接取→杀怪→交付流程走完，**玩家收货为零**。
> 这是一处"模型层缺字段"的问题，不是漏填：要么给 `QuestReward` 加 `Experience` 字段，要么用 `ItemEffect.Experience` 的物品当奖励载体。

#### 5.3.3 自建任务奖励行 `Item` 为空 → 完成时服务端空引用，玩家掉线

| 任务 | 奖励行 | 现状 |
|---|---|---|
| #63 望海楼之约（战士） | 143 | `Item` **缺**，Amount 2 |
| #64 望海楼之约（法师） | 145 | `Item` **缺**，Amount 2 |
| #65 望海楼之约（道士） | — | **整条无 QuestReward 行** |
| #66 刺杀剑术觉醒 | 148 / 149 | 148 `Item` **缺**；149 正常（Gold ×3000） |

调用链（已逐行核对）：

```
C.QuestComplete → SConnection.Process (SConnection.cs:1309) → PlayerObject.QuestComplete (PlayerObject.cs:3672)
  → new ItemCheck(reward.Item, ...)            ItemInfo 重载 → Item = null, Info = null   (ItemCheck.cs:30-39)
  → CanGainItems(false, checks)                check.Info.ItemEffect → NullReferenceException (PlayerObject.cs:6336)
  → SEnvir 会话循环 catch → 断开该连接（"failed; disconnecting that session"）  (SEnvir.cs:1446-1454)
```

> 症状预测：**玩家点"完成任务"→ 掉线（DisconnectReason.Crashed）**，世界循环不受影响。
> `ItemCheck` 里**没有** null 守卫；`CanGainItems` 里也**没有**。属于代码缺陷，最省事的修法是在 `QuestComplete` 的奖励循环里 `if (reward.Item == null) continue;`。

### 5.4 另外 2 条"可瞬间完成"的任务

| 任务 | 问题 |
|---|---|
| #12 Curing the Poison Pt. 4 | 无任何 `QuestTask` |
| #13 Curing the Poison Pt. 5 | 无任何 `QuestTask` |

`ServerLibrary/DBModels/UserQuest.cs:138`：`IsComplete => Tasks.Count == QuestInfo.Tasks.Count && Tasks.All(Completed)`
→ 任务数为 0 时**接取瞬间即判定可交付**。这两个任务在链上（12 → 13 → 14），会让玩家"白拿一步"。
注：网站这两条的 `goals` 也是空的 → **原版可能确实是纯对话推进**，所以本条属于"待确认是否为设计意图"，而非板上钉钉的 bug。

---

## 6. 宠物对账

| 项 | 数量 |
|---|---|
| 网站 `companion` | 10 |
| 库 `CompanionInfo` | 10 |
| 字段分歧（名称/monster_id/价格/available） | **0** |
| 数据格式瑕疵 | 10（`Description` 全空 ×10；#8 名称 `Pachon ` 带**尾空格** ×1） |
| `MonsterInfo` 引用有效性 | 10/10 ✅（189–198，逐条命中） |
| 等级表 `CompanionLevelInfo` | 15 行，Level **1–15 连续** ✅ |
| 技能表 `CompanionSkillInfo` | 48 行，覆盖 Level 3/5/7/10/11/13/15（7 个等级） |

逐条（库内）：

| Index | 名称 | MonsterInfo | 价格 | Available |
|---|---|---|---|---|
| 1 | Pig | 189 Pig | 500000 | **true** |
| 2 | Tusk Lord | 190 | 750000 | false |
| 3 | Skeleton Lord | 191 | 750000 | false |
| 4 | Griffin | 192 | 750000 | false |
| 5 | Dragon | 193 | 750000 | false |
| 6 | Donkey | 194 | 750000 | false |
| 7 | Sheep | 195 | 750000 | false |
| 8 | `Pachon `（尾空格） | 196 | 750000 | false |
| 9 | Panda | 197 | 750000 | false |
| 10 | Rabbit | 198 | 750000 | false |

> 结论：宠物系统**数据层健康**。仅 9/10 不可购买（`Available=false`）这一条与官网一致（官网同样只有 Pig 是 `available:true`），
> 属**既有设计**而非缺陷；#8 的尾空格是唯一建议顺手修的数据瑕疵（`_Identity` 与 `MonsterInfo.MonsterName` 都带）。

---

## 7. 术语本地化

### 7.1 术语表自身健康度（1701 条）

| 检查 | 结果 | 评价 |
|---|---|---|
| 空值 | **0** | ✅ |
| 英文 == 中文（未翻译） | **0** | ✅ |
| 中文值含拉丁字母 | **5** | 可接受：`GM Map`→`GM 测试图`、`PVP Champion`→`PVP 冠军徽章` 等 |
| 同一中文值被多个英文名共用 | **8** | 见下表，7 项是别名/占位，1 项是真冲突 |

| 中文值 | 英文 key | 判定 |
|---|---|---|
| 预留新图 | D3101/D3102/D3103/D3106/D3901–D3906（10 个） | 占位符，正常 |
| 未命名(预留) | 15/15_001/15_002/15_003 | 占位符，正常 |
| 召唤傀儡 | `SummonPuppet` / `Summon Puppet` | 同一术法两种拼写，正常 |
| 召唤骷髅 | `Evil Slayer` / `Summon Skeleton` | 别名，可接受 |
| 金甲骷髅 | `Jin Skeleton` / `Summon Jin Skeleton` | 别名，可接受 |
| 镀金手镯 | `Gold Plated Bracelet` / `Gold-Plated Bracelet` | 连字符变体，正常 |
| 飞升手镯 | `Bracelet Of Ascension` / `Ascension Bracelet` | 变体，正常 |
| **杰克** | **`Jack` / `Jake`** | **真冲突**：#106(Jack) 与 #139(Jake) 是两个不同 NPC，被折叠进一个中文名 |

### 7.2 覆盖率（用术语表回查库内命名）

| 实体集 | 有术语条目 | 缺 | 缺失样例 |
|---|---|---|---|
| `MonsterInfo`（126） | 106 | **20** | TownGuard / ForestGuard / SandGuard / ArcherGuard / `Pachon ` / Fierce Falcon / Dungeon Goddess 1–2 / Otherworld* 系列 |
| `ItemInfo`（499） | 491 | **8** | `Gold`/`Fame Point`/`Contribution Point`（货币，无需译）+ `ScortchedEarth`/`BlowEarth`/`GreaterFrozenEarth`/`ThunderStrike`/`MeteorShower`（物品名与技能名撞车，疑似数据脏） |

> 缺失的 20 个怪物**全部是守卫/系统怪/后期内容**，属于清洗后 D 类保留项，不是漏译。

### 7.3 真正的风险：三张中文词表互相不一致

先说口径：`zh-glossary.json` 与 `db_names.json` 的 **`items` 段里同时装有物品名与法术/技能名**
（例如 `Dragon Rise`、`Asteroid` 都在 `items` 段），与 `magics` 段重复。
所以直接按段比会把同一英文名算两次。本报告的「命名分歧」定义为：
**同一英文名在术语表里的中文，不属于该文件给出的中文集合**；并单独统计「文件内 sections 自相矛盾」。

| 对照 | 英文 key 交集 | **命名分歧** | 文件内 sections 自相矛盾 | 说明 |
|---|---|---|---|---|
| 网站术语表 vs `ClientData/zh/zh-glossary.json` | 1500 | **222** | **86** | 例：`Adamantine Fire Ball` 术语表`大火球`，glossary 的 `magics` 段是`金刚火球`、`items` 段是`大火球` |
| 网站术语表 vs `GodotClient/Translations/db_names.json` | 1500 | **456** | **92** | 例：`Iron Sword` 术语表`铁剑` / db_names`短剑`；`Dragon Rise` `龙影剑法` / `翔空剑法` |
| 网站术语表 vs `ServerLibrary/chinese_alias.json`（反向后） | 1354 | **176** | 3 | 例：`Ant Commander` `蚁军统领` / `蚂蚁指挥官`；`Arch Lich Taedu` `大巫妖·泰杜` / `大巫妖泰度` |

> 「文件内 sections 自相矛盾 86 / 92」这一项值得单独看：**同一个文件里，法术名在 `items` 段和 `magics` 段被译成两个不同的中文**
> （`Asteroid`：`items`=`陨石术`、`magics`=`陨石`）。这不是跨表分歧，而是生成流程本身没收敛。

`db_names.json` 是 Godot 客户端**实际显示名**的来源（`GodotClient/Scripts/LocalizedName.cs:32` 从
`res://translations/db_names.json` 读取），`chinese_alias.json` 是服务端别名映射。
→ 也就是说：**同一个英文名在"网站资料"和"游戏内显示"可能是两个中文名**，玩家对照攻略站时会发现名字对不上。

分歧集中在三类，且**风格差异大于错译**：
1. 技能/法术名逐字对译 vs 中文习惯叫法（`Asteroid` 陨石术/陨石）；
2. 怪物名"意译 vs 音译"（`Banyo*` 潘约/潘夜、`Sama` 萨玛/沙玛）；
3. 后期装备的直译长名（术语表已给出`阿尔法·残酷之锤`这种高质量译名，而 glossary / db_names 仍是英文原文 passthrough）。

### 7.4 库内中文命名的多实体同名

| 中文名 | 实体数 | index |
|---|---|---|
| 六面神石 | **37** | 39,41–45,56,106,118,152–154,176,219,227,228,233,236,252–254,271,275,279,368–371,374–382 |
| 变异骷髅 | 8 | 197–204 |
| 栗子商人 | 4 | 18,26,151,175 |
| 万事通 | 4 | 96,102,155,177 |
| 霹雳尊者 | 4 | 179,181–183 |
| 云发 | 3 | 159,238,239 |
| 清明子 | 3 | 194,195,196 |
| 仓库系统 | 3 | 294–296（模板） |

> 「六面神石 ×37」是传送石的**分类名**，合理；但 **#106 六面神石被登记在 `Item Fragment` 区域**（见 §4.3）说明
> 「同名多实体」里至少混了 1 个**错位登记**。建议按 Region 复核 index 106。

### 7.5 本地化进度

| 实体 | 库内中文名比例 |
|---|---|
| NPC 名（294） | **292 / 294**（2 个非中文：`VIP`(286)、`My00DefaultNpc`(320)，都是模板） |
| 任务名（38） | **4 / 38** — 34 条经典任务名与全部对白（`AcceptText` 等）**仍是英文** |
| 宠物名（10） | 0 / 10（沿用英文，与官网一致） |

另需注意：库内 NPC 的中文名与网站/词表的中文名**完全不是一套**（0/78，见 §3.5）。
即"NPC 有中文名"这件事成立，但**与资料站对不上号**。

---

## 8. 确认缺陷 vs 待确认（按玩家可见影响排序）

### 8.1 确认缺陷（有代码/数据确证）

| 级别 | 缺陷 | 证据 | 影响面 | 建议修法 |
|---|---|---|---|---|
| **P0** | 自建任务奖励行 `Item` 为空 → 完成时服务端空引用、玩家掉线 | `QuestReward` #143/#145/#148；`ItemCheck.cs:30-39` + `PlayerObject.cs:6336` + `SEnvir.cs:1446-1454` | 4 个任务中的 3 个 | `PlayerObject.QuestComplete` 奖励循环加 `if (reward.Item == null) continue;`，并补 #65 的奖励行 |
| **P0** | 34 条经典任务零奖励（网站写 `Experience ×N`） | `QuestReward` 仅 4 行且全属自建任务；`QuestInfo.cs` 无经验字段 | 全部经典任务 | 扩展 `QuestReward`（或 `QuestInfo`）加经验字段并回填；短期可用 `ItemEffect.Experience` 物品顶 |
| **P0** | 5 个任务因击杀任务缺怪物明细而无法完成 | `MonsterDetails` 空；`MonsterObject.cs:3070-3095` | #17/#36/#37/**#56**/**#57** | 先修可立即修的：#56→Bone Soldier(119)、#57→Bone Captain(118)、#36→Wedge Moth；#17/#37 需恢复怪物（Mutant Flea 系、Razor Tusk）或重定目标 |
| **P1** | 4 个 NPC 走 `fallback:entry` 兜底页（"这里暂时没有可以交涉的事务"） | NPCInfo 99/270/272/273；#99 已实机证实（1 页、只有关闭），#270/#272/#273 待跑 | 4 | 接真实脚本页，或从地图撤下 |
| **P1** | #195 清明子入口 Say 为空但带 Teleport 动作 → **点击后不弹框直接传送**（本轮实机证实，`entryPage=-1` / `type="entry_action"`） | `IR195|amyen:mugong_poison_test_next`，Say 空、Action=Teleport→`1_002`(11,11) | 1 | 补 Say 或撤掉该 NPC |
| **P1** | #106 六面神石登记在 `Item Fragment` 区域、实机点不到 | Region `2 / 2 / Item Fragment` 但 EntryPage 是 `SnakeVallyTele`；**本轮实机 `click_missed` 复现** | 1（+官网 Jack 未落地） | 复核 index 106 的 Region/坐标；并按 [NPC_PLACEMENT_GAP](../NPC_PLACEMENT_GAP_2026-10-17.md) 的方法核查"同坐标双登记" |
| **P2** | #89 六角圣石（→ 巴尼亚神殿 D10031）缺失 | 网站 125 条里唯一"非地图裁剪、功能未覆盖"的缺失 | 1 | 补 NPCInfo（可选，该图可步行进入） |
| **P2** | 术语表与客户端/服务端词表命名分歧 222 / 456 / 176，且单文件内 items↔magics 自相矛盾 86 / 92 | §7.3 | 玩家对照攻略站时名字对不上 | 指定唯一权威（建议 `zh-glossary`，它来自网站 master.json），反向回灌术语表与 `db_names.json`；同时收敛 items/magics 段 |
| **P2** | 库内 NPC 中文名与网站/词表**完全不是一套**（职能位 78/78 一致，中文名 0/78 一致） | 库 `NPCName` vs 网站 `zh`（§3.5） | 任何"攻略站 ↔ 游戏内名字"联动都会 100% 对不上 | 以库内原版中文名为权威，反向修网站词表；或建 index 级对照表 |
| **P3** | `CompanionInfo` #8 名称尾空格 | `_Identity == "Pachon "` | 1 | 去空格 |
| **P3** | `QuestTask` #35 孤儿行（无 Quest、Amount 0） | 库内确证 | 0（不显示） | 删除 |

### 8.2 待人工确认（静态推断，未实机证实）

| 项 | 为什么不确定 | 建议验证方式 |
|---|---|---|
| 22 个"只有一句旁白"的 NPC（§4.2 C 类） | 其中"石堆/书架/石母/泼皮路白"看起来是**有意的场景物**；而"署箭/无理的道士/牛老道/云发×3"只有「（。。。。。）」更像漏接脚本。原版脚本意图需人判 | 实机逐个点开截图（巡检快照已到 135，C 类里 #141/#157/#158/#159/#162/#193 已证实，其余 16 个待跑完）；对照 Mud3 原始脚本判断（**注意**：`MIR3_MUD3_ROOT=/home/tetsuya/NAS/TMP/Mud3` 在 Linux 机上，本机 macOS 无 `~/NAS`，需在 82 上查） |
| #12/#13 无目标任务是否算 bug | 网站这两条的 `goals` 也是空的 → 可能原版就是纯对话推进；但服务端 `IsComplete` 会立刻为真 | 实机跑一遍"接取→立刻交付"，看是否符合预期节奏 |
| `ItemInfo` 里 5 个技能名同名物品（`BlowEarth`/`ThunderStrike`/`MeteorShower`…） | 可能是"技能书"命名约定，也可能是数据脏 | 查这几条的 `ItemType`/`ItemEffect` 与图标库 |
| Sabuk Keep 9 个官网 NPC 是否要"还原本体身份" | 功能已覆盖，缺的是外观/名字；属产品取舍 | 与美术/剧情口径确认 |
| 术语分歧 222/456/176 该以哪张表为准 | 三张表各有来源（网站 master.json / 客户端翻译工具），本报告无权定权威 | 由内容负责人定：确认权威表后一次性回灌 |
| #106 的正确 Region/坐标 | 需要结合地图落点核查（本任务只读） | 用 `NPC_PLACEMENT_GAP` §2 的 `mud3_npc_placement_diff.py` 方法 |

---

## 9. 卡点与不确定项

1. **实机巡检未跑完**：快照时 135/230（27 个缺陷里 8 个已实机复核）。§8.2 的 C 类里还有 16 个未被实机覆盖，
   所以本轮把它们标成"静态确证 + 待实机复核"，而不是"已证实"。**已覆盖的 8 个缺陷全部命中、零反证**。
   等巡检跑完，用同一条命令重跑脚本即可自动升级（脚本会把巡检结果合并进 `npcService.service[].liveStatus`
   与 `npcService.live.defectsLiveConfirmed`）。
2. **"奖励=经验"这一条无法从库内验证**：库模型根本没有经验字段，所以"34 条任务零奖励"是
   **依据网站基准**得出的结论；如果产品口径是"经典任务本来就不给奖励"，那条需降级。服务端代码只能证明"当前不发奖励"。
3. **`quests` 的 NPC 对不上官网人名**：官网写 `Sara`/`Seven`/`Murray`/`Max`，库内 StartNPC 是 `金莲`/`流浪卢`/`蛇谷老太`/`栗子商人`。
   但**数量 78/78 完全吻合**（`quests_in/out` 与 `StartQuests/FinishQuests` 逐条相等），且这些库内名字就是**原版中文名**，
   所以本报告按"同一实体的两种命名"处理，未记为缺陷。若要严格核对"官网人名 ↔ 库内 NPC"的实体同一性，需要一张英文→中文的 NPC 权威映射。`zh-glossary.npcs` 只有 255 条（key 是脚本名如 `02Weapon_Bichon1` 与英文显示名 `Mr. Kang` 的混合），库内 228 个不同 NPC 名里只有 **152** 个能在它的中文值里找到。
4. **网站 125 条 NPC 的"收录口径"未知**：它既不是全量（库内在用 230），也不完全等于城镇服务位（缺了库内一整套地牢商人）。
   本轮只能按"官网有 → 库内应当有"单向判定，反向的 152 条不做缺陷判定。
5. **未核验**：`terminology` 的日文列、`NpcMover` 的写入路径、以及 `GodotClient` 里 `db_names.json` 的运行时接管优先级
   （`LocalizedName.cs` 是客户端脚本，本任务未启动客户端验证其生效范围与覆盖顺序）。
6. **NPC 中文名的"权威方"未定**：本报告只证明了库内与网站两套名字**不一致**（0/78），
   并且库内那套看起来是原版 Mud3 中文名（啊康/恩实/栗子商人/王铁匠 + `NPC_PLACEMENT_GAP` §2 记载过"已按纯正中文命名重命名"）。
   但"哪套该是权威"需要内容负责人拍板，本任务不下结论。

---

> **现状（2026-10-18 晚，修复后）**：本册记录的是**审计当日的发现**，不是当前状态。
> 各项修复进展、验证方式与仍未做的边界，见总报告 [GAME_AUDIT_2026-10-18.md](../GAME_AUDIT_2026-10-18.md)
> 的「修复台账」（已到第 20 行）与「口径类问题」；过程与教训见 [WORKLOG.md](WORKLOG.md)。
> 与修复相关的关键变化（摘要）：
> - 数据类：580 处清洗回归内容按旧库原值补回；15 只零属性怪物按同等级参照怪推导补属性；
>   16+2 条技能显示名从枚举标识改回旧库显示名；34 条经典任务恢复 `Experience` 令牌奖励 + 反链；
>   怪物名尾空格 `Pachon ` 已 trim。
> - 代码类：跨天行会重置的空引用（及同形状 91 处）、`@monster` 名字含空格刷不出、任务/名望奖励空 Item 守卫、
>   13 字节/格地图、地图名大小写回退、技能窗新学派专属页签、两处「控件被压住/尺寸被清成 0」的点击缺陷。
> - 口径未做（需决策）：被有意清洗的 199 怪物 / 582 物品 / 175 地图 / 8 图 32 行刷怪；
>   3 条空 `Item` 奖励行（旧库无来源）；5–6 个无交互 NPC（缺 MUD3 脚本源）。

# 15 只「0 属性行」怪物的属性补全（推导）—— 2026-10-18

**范围**：审计报告 `docs/AUDIT/monsters.md` 的 **R2**（15 只怪物 `MonsterInfoStat` 0 行，
其中 14 只仍在刷怪）。
**脚本**：`tools/restore/fill_zero_stat_monsters.py`（默认 dry-run，纯离线，不连网、
不读写 `System.db`、不跑 `sync.sh`）
**预览**：`/tmp/zero_stat_fill_preview.json`（dry-run 产出）
**机器可读明细**：同一份 preview 里的 `targets[]`（每只怪物的参照集成员及其全部 Stat 原值、
中位数、取整后值、被排除的 Stat 与出现率）与 `new_rows[]`（226 行成品）

---

## 一、结论速览

| 项 | 数字 |
|---|---|
| 目标怪物 | **15**（388/390/395/396/409/412/416/417/418/421/425/448/449/458/484） |
| 新增 `MonsterInfoStat` 行 | **226**（工作区 1165 → 1391 行） |
| 新行 `Index` 号段 | **5072 – 5297**（现有最大 Index 5071 + 1；实测库内计数器恰为 5071，无重映射） |
| 每只行数 | 9 只 × 16 行 + 4 只 × 16 行 + 2 只 × 9 行（449/458 少抗性，见 §5） |
| 动到的 Stat | Health/MinAC/MaxAC/MinMR/MaxMR/MinDC/MaxDC/Accuracy/Agility（各 15）+ 7 种抗性（各 13） |
| 参照层级 | **15/15 全部落到 tier3**（没有一只有同等级或 ±5 级内的同类可对照，见 §5） |
| 未动的文件 | `MonsterInfo.json`（回链纯派生，见 §6）、`RespawnInfo.json`、`DropInfo.json`、所有其它表 |
| 未做的验证 | **没有**实机/服务端验证（未启动客户端或服务端）；未跑 `sync.sh`，未写 `System.db` |

产出的性质是**推导（derived）**，不是**恢复（restore）**——这 15 只从来没有过属性数据（§2）。

---

## 二、为什么是「推导」而不是「恢复」

任务前提是「旧库里没有同名行，即没有可恢复的原值」。我用**独立于审计脚本**的一条路径复核了这件事：

```bash
# 清洗前基准库（只读复制，不碰仓库里的 System.db）
cp "Debug/ServerCore/Database/Backup/System/System 2026-09-25 21-47.db.gz" /tmp/preclean_zero/S.db.gz
gunzip -f /tmp/preclean_zero/S.db.gz && mv /tmp/preclean_zero/S.db /tmp/preclean_zero/System.db
cd Tools/SystemDbProbe && dotnet run --no-build -- --json /tmp/preclean_zero/out /tmp/preclean_zero
```

结果（`/tmp/preclean_zero/out/`，309 只怪物 / 4117 条属性行，与审计记载的清洗前规模一致）：

- 15 只的 `Index`（388…484）**在清洗前基准库中全部不存在**；
- 15 只的 `MonsterName`（`Fierce Falcon`、`Stone Lion`…）**在清洗前基准库中一个都没有同名行**
  （因此也不存在「原行是否带属性」的问题）；
- 所以任何数值都无从「读回」，只能按同等级/同类怪推导：**推导值 = 参照集中位数**，
  不是历史值，也不代表原版数值。

审计 §3.2 还记录：15 只里有 11 只在网站上能找到同名的 `mud3` 历史条目（如 `猎鹰`、
`海神将领`、`火焰狮子`、`石像狮子`、`地牢女神`）。那是**另一套版本口径**的数据（MUD3 DAT 的
等级/血量体系与现库不同），不能当作现库原值；本补丁没有使用它。若要做交叉参考，
应作为独立议题单独立项（见 §9「不确定项」）。

---

## 三、方法与规则（写死在脚本里）

### 3.1 参照集分级（R1）

对每只目标怪物：

| 层级 | 定义 |
|---|---|
| tier 1 | `MonsterInfo.Level` **完全相同**、且**有属性行**的怪物 |
| tier 2 | tier 1 为空时，取 \|ΔLevel\| ≤ 5 的怪物 |
| tier 3 | tier 2 仍为空时，按 \|ΔLevel\| 由小到大（并列取 Level 小的）依次并入**有属性行**的等级，直到参照怪物数 ≥ `--min-ref`（默认 5） |

参照集只包含「有属性行」的怪物，并排除 15 只目标自身（它们本来就没有行）。

### 3.2 取值（R2 / R3）

- 每个 Stat 取参照集的**中位数**（`statistics.median`，中位数对 Lv250 档里的
  守卫/BOSS 极值和 `Larva`(HP 5) 这类异常行天然稳健）；
- 只补参照集里**出现率 ≥ `--min-presence`（默认 0.5）**的 Stat——避免给某只怪凭空塞冷门抗性；
- 取整：`Health` 与任何 \|中位数\| ≥ 1000 的 Stat 取到最近的 **10** 的倍数；其余取最近整数；
  **半值远离零**（`3.5→4`、`4.5→5`、`-1.5→-2`、`-4.5→-5`），刻意不用 Python 内置
  `round()` 的银行家取整（`round(4.5)==4` 会让人看不出规律）；
- 不变量（违反即 fail fast，退出码 3）：`Health > 0`；成对 Stat `MinAC/MaxAC`、`MinMR/MaxMR`、
  `MinDC/MaxDC`、`MinMC/MaxMC`、`MinSC/MaxSC` 满足 Min ≤ Max。

### 3.3 落点（R4 / R5 / R6）

- 新行 `Index` 从现有最大 Index + 1 起顺序分配；`Monster` 引用形如
  `{"Index": 388, "Name": "Fierce Falcon"}`，指向**现库**该怪物的 Index/Name（脚本会校验名称一致）；
- 只增不改：`MonsterInfoStat.json` 里已有的 1165 行原样保留（写后逐行比对），不改顺序；
- **不动 `MonsterInfo.json`**（§6 说明为什么不需要）；
- 幂等：已有属性行的目标一律跳过（`--apply` 之后重跑 = 0 行待补）。

---

## 四、参照集与推导值

### A. 每只怪物的参照集

| 目标 | 名称 | lv | 参照层级 | 参照等级（Δ） | 参照怪物数 | 参照成员 | 补行数 |
|---|---|---|---|---|---|---|---|
| 388 | Fierce Falcon | 105 | tier3 最近可用等级 | 88(−17), 85(−20), 80(−25) | 5 | 252 Sama Wind Guardian(lv88), 277 Evil Snake(lv85), 183 Millipede(lv80), 186 Banyo Warrior(lv80), 221 Oyoung Beast(lv80) | 16 |
| 390 | Fierce Wind Demon | 105 | tier3 最近可用等级 | 88(−17), 85(−20), 80(−25) | 5 | 同上 | 16 |
| 395 | Dungeon Goddess 1 | 55 | tier3 最近可用等级 | 47(−8), 46(−9), 45(−10) | 10 | 85 Evil Elephant(47), 83 Monkey(46), 84 Evil Monkey(46), 76 Zuma Sharpshooter(45), 77 Zuma Fanatic(45), 78 Zuma Guardian(45), 79 Vicious Rat(45), 109 Banya Right Guard(45), 111 Banya Left Guard(45), 113 Flame Minotaur(45) | 16 |
| 396 | Dungeon Goddess 2 | 55 | tier3 最近可用等级 | 47(−8), 46(−9), 45(−10) | 10 | 同上 | 16 |
| 409 | Otherworld Sea General | 100 | tier3 最近可用等级 | 88(−12), 85(−15), 80(−20) | 5 | 252 Sama Wind Guardian(88), 277 Evil Snake(85), 183 Millipede(80), 186 Banyo Warrior(80), 221 Oyoung Beast(80) | 16 |
| 412 | Otherworld Poison Demon | 100 | tier3 最近可用等级 | 88(−12), 85(−15), 80(−20) | 5 | 同上 | 16 |
| 416 | Otherworld Ship Guard | 100 | tier3 最近可用等级 | 88(−12), 85(−15), 80(−20) | 5 | 同上 | 16 |
| 417 | Otherworld Ship Guard 1 | 100 | tier3 最近可用等级 | 88(−12), 85(−15), 80(−20) | 5 | 同上 | 16 |
| 418 | Otherworld Red Mage | 100 | tier3 最近可用等级 | 88(−12), 85(−15), 80(−20) | 5 | 同上 | 16 |
| 421 | Otherworld Tentacle Demon | 100 | tier3 最近可用等级 | 88(−12), 85(−15), 80(−20) | 5 | 同上 | 16 |
| 425 | Otherworld Light Guard | 100 | tier3 最近可用等级 | 88(−12), 85(−15), 80(−20) | 5 | 同上 | 16 |
| 448 | Flame Lion | 55 | tier3 最近可用等级 | 47(−8), 46(−9), 45(−10) | 10 | 同 395 | 16 |
| 449 | Flame Demon Soldier | 180 | tier3 最近可用等级 | **250(+70)** | 24 | 1 Guard, 2 TownGuard, 3 ForestGuard, 4 SandGuard, 5 ArcherGuard, 23 Oma Hero, 30 Skeleton Lord, 37 Ghoul Champion, 42 Ant Commander, 64 Uma Anguisher, 65 Uma King, 68 Larva, 73 Arachnid Broodmother, 75 Red Moon The Fallen, 80 Zuma Keeper, 81 Zuma King, 87 Crazed Warrior, 88 Spiked Beetle, 114 Banya Guardian, 115 Emperor Sa'Woo, 120 Skeleton Enforcer, 121 Arch Lich Taedu, 128 Tusk Lord, 220 Sabuk Lord（全部 lv250） | 9 |
| 458 | Ice Break Demon Soldier | 180 | tier3 最近可用等级 | **250(+70)** | 24 | 同上 | 9 |
| 484 | Stone Lion | 55 | tier3 最近可用等级 | 47(−8), 46(−9), 45(−10) | 10 | 同 395 | 16 |

参照成员的**逐只全部 Stat 原值**在 preview JSON 的 `targets[].ref_members[]` 里，可直接独立重算中位数。

### B. 推导出的 Stat 值

| 目标 | 名称 | Stat=值 |
|---|---|---|
| 388 | Fierce Falcon | Health=5650, MinAC=80, MaxAC=80, MinMR=60, MaxMR=60, MinDC=150, MaxDC=225, Accuracy=30, Agility=23, FireResistance=0, IceResistance=0, LightningResistance=3, WindResistance=2, HolyResistance=0, DarkResistance=0, PhantomResistance=0 |
| 390 | Fierce Wind Demon | 同 388 |
| 395 | Dungeon Goddess 1 | Health=1200, MinAC=5, MaxAC=5, MinMR=12, MaxMR=12, MinDC=38, MaxDC=56, Accuracy=22, Agility=18, FireResistance=4, IceResistance=−2, LightningResistance=5, WindResistance=−2, HolyResistance=5, DarkResistance=−4, PhantomResistance=−1 |
| 396 | Dungeon Goddess 2 | 同 395 |
| 409 | Otherworld Sea General | 同 388 |
| 412 | Otherworld Poison Demon | 同 388 |
| 416 | Otherworld Ship Guard | 同 388 |
| 417 | Otherworld Ship Guard 1 | 同 388 |
| 418 | Otherworld Red Mage | 同 388 |
| 421 | Otherworld Tentacle Demon | 同 388 |
| 425 | Otherworld Light Guard | 同 388 |
| 448 | Flame Lion | 同 395 |
| 449 | Flame Demon Soldier | Health=3300, MinAC=18, MaxAC=33, MinMR=33, MaxMR=52, MinDC=113, MaxDC=213, Accuracy=25, Agility=23 |
| 458 | Ice Break Demon Soldier | 同 449 |
| 484 | Stone Lion | 同 395 |

> 同参照集的怪物拿到**同一组值**——这是「取中位数」的必然结果（不是 bug）：同一批怪在数据里
> 本来就没有区分彼此的信息。想要个体差异，只能在规则之外引入设计参数（§9）。

### C. 出现率不足被排除的 Stat

| 参照集 | 排除（出现率） |
|---|---|
| lv80–88（388/390/409/412/416/417/418/421/425） | FireAffinity(20%), PhysicalResistance(20%) |
| lv45–47（395/396/448/484） | DarkAffinity(30%), MaxSC(30%), MinSC(30%), FireAffinity(20%), LightningAffinity(10%) |
| lv250（449/458） | LightningResistance(42%), HolyResistance(38%), PhantomResistance(38%), WindResistance(38%), DarkResistance(33%), FireResistance(33%), IceResistance(33%), FireAffinity(8%), DarkAffinity(4%), IceAttack(4%), LightningAffinity(4%), MaxSC(4%), MinSC(4%), PhantomAffinity(4%), PoisonResistance(4%), WindAffinity(4%) |

---

## 五、参照集是否充分（诚实结论：**不充分，15/15 都落到 tier3**）

现库的等级档位是**断层**的（`MonsterInfo` 126 只，有属性行的等级只有
0/5/6/7/8/9/10/13/15/16/17/18/20/25/30/33/35/40/45/46/47/68/72/80/85/88/250）。15 只目标怪物
**恰好各自占满**了自己所在等级：lv55 全是目标（395/396/448/484），lv100 全是目标（7 只），
lv105 全是目标（2 只），lv180 全是目标（2 只）。所以：

| 目标等级 | tier1 同等级 | tier2 ±5 级 | 实际用的 tier3 | 评价 |
|---|---|---|---|---|
| 55（4 只） | 空 | 空（50–60 内无他怪） | lv47/46/45（Δ8–10，10 只参照） | **较好**：档位相邻、样本 10 只，数值可信度最高 |
| 100（7 只） | 空 | 空（95–105 内无他怪） | lv88/85/80（Δ12–20，5 只参照） | **偏弱**：往低 12–20 级借，且参照只有 5 只 |
| 105（2 只） | 空 | 空 | lv88/85/80（Δ17–25，5 只参照） | **偏弱**（同上，距离更远） |
| 180（2 只） | 空 | 空 | lv250（Δ70，24 只参照） | **最弱**：临档只有 GM 守卫/BOSS 档；本方向无同档数据 |

脚本对「参照等级离目标超过 `--warn-delta`（默认 30）」会打告警：449/458（Δ70）命中告警。

两点必须摆在明面上：

1. **规则里没有等级缩放**（只取中位数），所以 lv100/105 的 9 只怪拿到的是 **lv80–88 档**
   的数值（Health 5650 / MinDC 150 / MaxDC 225），相对它们 300 万经验、仍然刷在
   D2701/D2901/D1101 一带的定位**偏弱**；lv55 的 4 只拿到 lv45–47 档（Health 1200），
   相对同图其它怪属于中上——这部分是符合预期的。
2. **449/458（lv180）是外推而不是同档对照**：参照集是 lv250 的 24 只（含 5 个 GM 守卫
   `Guard/TownGuard/ForestGuard/SandGuard/ArcherGuard` 和 8 只 BOSS）。中位数把它们压住了
   （Health 中位数 3300、MaxDC 中位数 213 都不受 50000/5000 极值支配），但**档位本身差 70 级**，
   这两只最终 Health 3300 / MaxDC 213，相对 lv180 的设定可能仍偏低。这两只是**必须人工裁决**
   的项（见 §9）。

---

## 六、为什么不需要动 `MonsterInfo.json`（反链一节）

`MonsterInfoStat.Monster` 是 `[IsIdentity] [Association("MonsterInfoStats")]`
（`LibraryCore/SystemModels/MonsterInfoStat.cs`），父侧
`MonsterInfo.MonsterInfoStats` 是 `[Association("MonsterInfoStats", true)]`
（`LibraryCore/SystemModels/MonsterInfo.cs:254`）。源码级证据链：

1. 子行 `Monster` setter → `OnChanged` → `DBObject.CreateLink`
   （`LibraryCore/MirDB/DBObject.cs:202-254`）→ 找到父的 `DBBindingList<MonsterInfoStat>` 并
   `Add(this)`：**内存里的回链由子行驱动**；
2. 落盘时只序列化「`DBObject` 类型的属性（ref）」——`DBObject.Save()`
   （`LibraryCore/MirDB/DBObject.cs:138-147`）遍历 mapping 的 ref 字段写 Index，
   `DBBindingList` **不入库**；
3. 载入时由子行 ref 重建父的回链：`DBObject.Load` 收集 `Session.Relationships`，
   `DBRelationship.ConsumeKeys`（`LibraryCore/MirDB/DBRelationship.cs:19-35`）
   对每个子行执行 `target.Key.SetValue(targetOb, linkOb)` → 再次触发 `CreateLink`；
4. 父的 `MonsterInfo.OnLoaded()` → `StatsChanged()`（`LibraryCore/SystemModels/MonsterInfo.cs:293`）
   遍历 `MonsterInfoStats` 重算 `Stats`
   ——这正是 `MonsterObject.RefreshStats`（`ServerLibrary/Models/MonsterObject.cs:730`，
   `Stats.Add(MonsterInfo.Stats)` 在 :735）读取的对象。

dbeeditor 导入器同样按此语义工作：`DBImporter` 的 `ReflistDerived()` 判定该 reflist 是派生的，
**跳过**对 `MonsterInfo.MonsterInfoStats` 的写入（`Tools/dbeditor/importer/Program.cs:85-100, 349-352`）。
所以本补丁**只追加子行**，父行不用改、也不该改。§8 的沙箱实测印证了这一点：导入后
`MonsterInfo#388.MonsterInfoStats` 自动出现 5072–5087 共 16 条回链，`Stats` 同步重算为非空。

---

## 七、风险与不确定项

| # | 项 | 说明 |
|---|---|---|
| 1 | **未实机验证** | 未启动服务端/客户端，没有在游戏内确认血量条、伤害、掉落表现。间接证据：sandbox 导入后由引擎自身代码路径（`OnLoaded → StatsChanged`）算出的 `MonsterInfo.Stats` 已非空（§8）。R2 的「一键秒杀」后果仍是代码推断。 |
| 2 | **lv100/105 数值偏弱** | 参照档位 lv80–88（Δ12–25），且无等级缩放（§5.1）。若要求「同级强度」，需要额外引入设计参数（例如按等级强度曲线放大），**本补丁刻意没做**。 |
| 3 | **lv180 两只属外推** | 参照档位 lv250（Δ70，混合 GM 守卫与 BOSS）。建议人工复核 449/458 后再决定是否采用（§5.2）。 |
| 4 | **同参照集 = 同数值** | 9 只 lv100/105 怪拿到完全相同的属性；4 只 lv55 怪同理。这不是错误，而是「中位数推导」的固有信息量上限。 |
| 5 | **0 值抗性行会被 `Stats` 索引器吞掉** | `MonsterStats` 的索引器赋 0 即删键（`LibraryCore/Stat.cs:18-24`）：FireResistance=0 等行**存在于 `MonsterInfoStat` 表**，但聚合后的 `MonsterInfo.Stats` 里看不到（等价于无该抗性，语义无害）。保留 0 值行是为了与同类怪的行结构一致（参照集里 100% 的怪都有这些抗性行）。 |
| 6 | **推导≠原版** | 没有任何原值可恢复（§2）；即便 11 只在 MUD3 网站有同名条目，那也是另一版本口径，未采用。想做版本对齐需要单独立项。 |
| 7 | **`MonsterInfoStat` 的身份键** | `(Monster, Stat)` 双 `[IsIdentity]`，脚本已校验新行不产生重复对（`checks.duplicate_monster_stat` 为空）。 |
| 8 | **跨补丁：当前工作区若跑 `sync.sh` 会把另一补丁的行**重复**写入** | 见 §8.3，属于既有状态问题，与本补丁无关，但**挡在**本补丁的落库路径上。 |

---

## 八、验证（做了什么、没做什么）

### 8.1 dry-run（未触碰工作区）

```bash
python3 tools/restore/fill_zero_stat_monsters.py            # 226 行；preview → /tmp/zero_stat_fill_preview.json
md5 -q …/workspace/MonsterInfoStat.json                     # bb58e13df736e204bd00e55cb2369738（前后一致）
python3 tools/restore/fill_zero_stat_monsters.py --preview /tmp/p2.json   # 二次 dry-run 结果逐字节一致（幂等）
```

### 8.2 沙箱端到端（全在 /tmp，未碰真库/真工作区）

```bash
# 1) 工作区副本 + 应用
cp -R Tools/dbeditor/workspace /tmp/ws_trial1
python3 tools/restore/fill_zero_stat_monsters.py --workspace /tmp/ws_trial1 --apply \
        --preview /tmp/zero_stat_fill_preview_apply.json       # 1165 行原样保留 + 追加 226 行
# 2) 用真实导入器写一份 System.db 副本（只读真库，产物落 /tmp）
cp Debug/ServerCore/Database/System.db /tmp/zero_stat_probe/System.db
cd Tools/dbeditor && MIR3_ZIRCON_ROOT=<Zircon 仓库根> dotnet run --project importer -- \
        --workspace /tmp/ws_trial1 --src /tmp/zero_stat_probe/System.db \
        --report /tmp/importer_trial.txt --no-port-check
# 3) round-trip 重导出 + 语义对比
cd ../SystemDbProbe && dotnet run --no-build -- --json /tmp/rt_probe/out /tmp/rt_probe
cd ../dbeditor && python3 compare_sync.py /tmp/ws_trial1 /tmp/rt_probe/out /tmp/importer_trial.txt
```

结果：

| 检查 | 结果 |
|---|---|
| 导入器 | 退出 0；`[+] MonsterInfoStat#5072…5297` 共 **226** 行，全部标注「（Index 自洽）」 |
| Index 重映射 | **无** `REMAP=MonsterInfoStat` → 工作区分配 5072–5297 与库内计数器完全一致 |
| round-trip 行数 | `MonsterInfoStat` 1391 行；226/226 新行按 Index 命中，**Amount 差异 0 处**，缺失 0 行 |
| 回链（引擎自己算的） | `MonsterInfo#388.MonsterInfoStats` = 5072–5087（16 条）；`#388.Stats` = Health 5650 / MinAC 80 / … 非空 |
| `compare_sync.py` 对 `MonsterInfoStat` | **0 处差异** |
| 写入文件 | 纯追加、`count` 字段同步为 1391、`json.dumps(indent=2, ensure_ascii=False)` 与工作区原风格逐字节一致 |

### 8.3 顺带发现：另一补丁在 `sync.sh` 路径上有**重复写入**风险（不在本任务范围）

沙箱导入的报告是 `变更 806 处：MapRegion×6, MonsterInfo×12, ItemInfo×147, MonsterInfoStat×226,
ItemInfoStat×27, MagicInfo×6, DropInfo×382`——其中 **MonsterInfoStat×226 是本补丁**，其余 580 处
属于工作区里**已经提交但未同步**的另一补丁（Mir3-Research `e1fa3e3a data(dbeditor): 补回三类确认缺陷（580 处变更）`）。
问题在于这 580 处的数据**已经写进 `System.db`**（`System.db` md5 于 15:12 变化；当前库导出与工作区
在 DropInfo/ItemInfoStat/MonsterInfo 上逐行一致），而 `workspace/_baseline/` 仍是补丁前状态
（`DropInfo` baseline 5235 行 vs 工作区 5617 行）。导入器按「工作区 vs `_baseline`」算差异，
所以再跑一次 `sync.sh` 会**把同样 382+27 行再加一遍**：

- 沙箱实测：round-trip 的 `DropInfo` = **5999** 行 = 5617 + 382；`DropInfo#21517→21899 … 21898→22280`
  成批重映射（就是重复行的新 Index）；
- `compare_sync.py` 因此报 **573 处差异**（DropInfo 382 / ItemInfo 152 / ItemInfoStat 27 / MonsterInfo 12），
  与 `MonsterInfoStat` 无关。

**建议（需另一补丁负责人裁决，本补丁不代做）**：跑 `sync.sh` 前先把已落库的表在工作区侧对齐基线
（`cp workspace/<table>.json workspace/_baseline/`），或从 15:12 之前的库备份重做一次干净同步；
否则本补丁的 226 行会与那 382+27 行重复行一起进库。

> 另有一处**与本补丁无关**的既有不一致：`MapInfo#9.MiniMap` 工作区是 `8`、当前库是 `288`
> （`MapInfo.json` 未随库更新；由于它不在「工作区 vs 基线」的差异集内，`sync.sh` 不会碰它）。

---

## 九、apply / 回滚

### apply（写的是 dbeditor 工作区，不是数据库）

```bash
cd /Users/tetsuya/Development/Zircon
python3 tools/restore/fill_zero_stat_monsters.py --apply
#   → 备份 /tmp/dbeditor_ws_backup_stats_<时间戳>/{MonsterInfoStat.json, zero_stat_fill_preview.json, sha256.txt}
#   → 追加 226 行（1165 → 1391），写后自校验：老行原样 + 新行与计划一致 + count 同步
```

可选开关：`--min-ref N`（tier3 参照下限，默认 5）、`--min-presence F`（默认 0.5）、
`--warn-delta N`（默认 30）、`--targets 388,390,…`、`--preview <路径>`、`--backup-root <目录>`。

真正落库仍需另行执行（**本脚本不做、本次未执行**）：

```bash
bash /Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/sync.sh    # 见 §8.3 的前置注意
```

### 回滚

| 场景 | 做法 |
|---|---|
| 刚 apply、还没 sync | `cp /tmp/dbeditor_ws_backup_stats_<时间戳>/MonsterInfoStat.json <workspace>/MonsterInfoStat.json`；或在 Mir3-Research 仓库 `git checkout -- Tools/dbeditor/workspace/MonsterInfoStat.json`（该文件在 Git 跟踪下） |
| 已 sync 进库 | 用 `sync.sh` 第 5 步打出的备份 `Debug/ServerCore/Database/Backup/System/System-copy*<时间戳>.db.gz` 还原**全部实体副本**（4 处一致性要求见 AGENTS.md）；或直接把 `MonsterInfoStat` 里 `Index >= 5072` 且 `Monster.Index ∈ {388,390,395,396,409,412,416,417,418,421,425,448,449,458,484}` 的行删掉后再同步一次 |
| 只想停掉某一只 | 同上，按 `Monster.Index` 过滤删除该怪的 16（或 9）行 |

### 幂等

`--apply` 之后再跑 dry-run：15 只全部命中「已有属性行，跳过」（`skipped_targets`），新增 0 行。

---

## 十、复现清单

```bash
cd /Users/tetsuya/Development/Zircon
python3 tools/restore/fill_zero_stat_monsters.py                 # dry-run（默认）
python3 -c "import json;d=json.load(open('/tmp/zero_stat_fill_preview.json'));print(d['totals'],d['checks'])"
python3 tools/restore/fill_zero_stat_monsters.py --min-ref 10    # 敏感度：参照集变宽后的另一种取值
python3 tools/restore/fill_zero_stat_monsters.py --apply         # 才写工作区（带备份）
```

敏感度参考（同一脚本、不同 `--min-ref`，用于评审「要不要更宽的参照集」）：

| `--min-ref` | lv100/105 的参照等级 | Health | MaxAC | MaxDC |
|---|---|---|---|---|
| 5（默认） | 88, 85, 80 | 5650 | 80 | 225 |
| 10 | 88, 85, 80, 72, 68, 47, 46 | 2400 | 23 | 125 |
| 15 | 88, 85, 80, 72, 68, 47, 46, 45 | 1400 | 7 | 71 |

（参照集越大，被并入的低等级档越多 → 数值越被拉低。默认 5 是为了把等级窗口压到最紧：
lv100/105 组 Δ ≤ 25，lv55 组 Δ ≤ 10。）

若把 lv250 里那 5 个 GM 守卫（`Guard/TownGuard/ForestGuard/SandGuard/ArcherGuard`）剔出参照集
（本补丁按规则**保留**它们），449/458 会变成：Health 3300→**2400**、MaxAC 33→**18**、
MaxMR 52→**33**、MinDC 113→**88**、MaxDC 213→**188**，并且 LightningResistance 会因出现率升到
52% 被**新增**（2）。即「守卫要不要算同类」是一个会同时改数值与改 Stat 集合的判断，需要人工裁决。

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

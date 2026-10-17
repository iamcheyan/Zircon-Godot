# Zircon 物品数据对账报告（2026-10-18）

**范围**：把「网站侧基准快照」的物品数据与「当前服务端库（2026-10-18 新库）」逐项对账，并额外对照
**清洗前旧库基线**（Aug 13）给出回归结论。

**本文全部数字由脚本算出，可复现**：

```bash
cd /Users/tetsuya/Development/Zircon
python3 tools/audit/items_audit.py                 # 摘要 + 写 JSON
# 需要真实解码抽样验证图标时（BC7 需要 texture2ddecoder）：
uv run --with texture2ddecoder --with pillow python3 tools/audit/items_audit.py --decode-sample 8
```

- 机器可读结果：`.artifacts/game-audit-2026-10-18/data/items.json`
- 脚本：`tools/audit/items_audit.py`（纯离线；不联网、不打开/读写 `System.db`、不启动客户端或服务端）
- 约束遵守情况：只读输入；仅新建本报告、脚本与 JSON 三个文件；未触碰 `~/mir2ei`、未 `git add/commit`

## 0. 输入与前提

| 输入 | 路径 | 规模 / 校验 |
|---|---|---|
| 网站基准快照 | `Mir3-Research/docs/research/ei-ui-layout/artifacts/web-entity-audit-2026-09-26/raw/wiki_data_v2.json` | 2,845,679 B，sha256 `013fff2f…0434`，快照时间 `2026-08-11T09:04:42Z`（`_meta`） |
| 当前库导出（本次对账对象） | `/Users/tetsuya/audit-raw/db-export-2026-10-18/out/*.json` | ItemInfo 499 / ItemInfoStat 1537 / DropInfo 5235 / SetInfo 30 / SetInfoStat 200 / CurrencyInfo 5 / CurrencyInfoImage 8 / StoreInfo 1 / WeaponCraftStatInfo 110 |
| 清洗前旧库基线（回归对照） | `Mir3-Research/Tools/dbeditor/workspace/baseline/*.json`（Aug 13 21:26） | ItemInfo 1078 / ItemInfoStat 3196 / DropInfo 10382 / StoreInfo 92 |
| 既有成果（交叉验证） | `…/website-alignment-2026-09-26/item-manifest.json` | 371 条，`confirmed-name` 47 / `pending-legacy-name` 58 / `legacy-source-only` 266 |
| 客户端物品图库 | `/Users/tetsuya/mir2ei/Data/Storeitem.Zl`（+ 同目录 `Storeitems.Zl`） | 经 `Mir3-Research/Tools/common/zlsdk.py` 读取 |

### 前提勘误：`ver` 含 `zircon` ≠ 库里存在

`Mir3-Research/Tools/common/ver_tags.py:177` 对**每一条** items 无条件 `vs = ["zircon"]`，
所以「ver 含 zircon」只表示该条出现在网站物品表；`ei`/`mei` 才是 WIL 素材存在性（`wil_probe` 探帧）。
本报告用它仅作「网站基准范围」的界线，不作为存在性证据。

另外：该快照的 `img.frame` 来自 `SystemDbProbe --images`（读的是**旧库**），
所以它是「清洗前状态」的证据，不是网站独立数据 —— 本文在使用处都会写明方向。

## 1. 覆盖对账

| 指标 | 数值 |
|---|---:|
| 网站 items 总数 | 2,203 |
| 其中 ver 含 zircon（=本次基准） | 1,078 |
| 其中 legacy/MUD3-DAT 独有（不在本次范围） | 1,125 |
| 当前库 ItemInfo | 499 |
| 名称映射命中（精确 / 归一化） | 494 / 3 → **合计 497** |
| 网站→库命中率 | 46.1%（497/1,078） |
| 库→网站覆盖率 | **99.6%**（497/499） |
| 网站有、库里没有 | **581** |
| 库里有、网站没有 | **2** |
| 待人工确认（库内多条同名候选） | 0 |
| 同名撞车退回未匹配 | 1（`Gold-Plated Bracelet` vs `Gold Plated Bracelet`，库里只有后者） |

### 1.1 网站有、库里没有：581 条（清洗后的内容缺口）

按网站类型分布（全量清单在 JSON `coverage.wiki_present_db_missing`）：

| 网站类型 | 网站条数 | 其中库里没有 | 库内该类型总数 |
|---|---:|---:|---:|
| 技能书 | 173 | 114 | 61 |
| 消耗品 | 118 | 87 | 31 |
| 武器 | 124 | 77 | 47 |
| 戒指 | 100 | 39 | 61 |
| 无（Nothing） | 104 | 39 | 65 |
| 项链 | 92 | 37 | 55 |
| 手镯 | 86 | 37 | 49 |
| 护甲 | 104 | 30 | 74 |
| 头盔 | 32 | 20 | 12 |
| 火把 | 24 | 20 | 4 |
| 暗石 | 20 | **18** | 2 |
| 盾牌 | 12 | 12 | 0 |
| 鞋子 | 21 | 10 | 11 |
| 徽章/马甲/系统物品/精炼材料/宠物·… | 9/7/5/5/… | 全部 | 0 |

按 ver 组成：仅 `zircon` 340 条；含 `ei`+`mei`（原版素材存在）206 条；含 `mud3` 36 条。
按分类：消耗品 225 / 首饰 113 / 材料 87 / 护甲 79 / 武器 77。

前 30 条（完整清单见 JSON）：

| 网站名 | 中文 | 类型 | ver | 套装 |
|---|---|---|---|---|
| Afterlife | 来世 | 武器 | zircon | |
| Assassin's Edge | 刺客之刃 | 武器 | zircon | |
| Blade Of The Dragon Lord | 龙帝之刃 | 武器 | zircon | Dragon Lord |
| Blades Of Sama / Burning / Unholy / Wraith Blade Of Sama | 萨玛系四刃 | 武器 | zircon | |
| Celestia, Fan of the Heavens | 天界·天堂之扇 | 武器 | zircon | |
| Chaos, The Spiral Death | 混沌·螺旋死亡 | 武器 | zircon | |
| Chaotic Heaven Blade (2) | 混沌天刃（二型） | 武器 | zircon | |
| Crescent Moon Cry of the Fallen | 残月·亡者之泣 | 武器 | zircon | |
| Deluge, Winter's Might | 洪水·凛冬之力 | 武器 | zircon | |
| Divine Command | 神谕之令 | 武器 | zircon | |
| Nemesis, The Blade of Betrayal | 复仇女神·背叛之刃 | 武器 | zircon | |
| Ruination | 覆灭 | 武器 | zircon | |
| Staff Of Retribution | 惩戒法杖 | 武器 | **ei+mei**+zircon | |
| StormCaller / Valkyrie / Lifebinder / Phoenix Glaive | 高级现代武器 | 武器 | zircon | |
| Taoist's / Warrior's / Wizard's Edge | 三职业之刃 | 武器 | zircon | |
| Trainee's / Apprentice's / Expert's / Razor Sharp Hand Blade | 手刃系列 | 武器 | zircon | |
| 18 块暗石（Burning/Frozen/Gusting/Shocking × I–V 共 20 块中缺 18，仅存 Gusting IV/V） | 暗石 | 暗石 | zircon/ei+mei | |
| Mark Of X [P]/[T] 20 件 | 印记 | 火把 | zircon | |

> 说明：缺口集中在资料站特有的现代内容与刺客职业物品（当前库 `RequiredClass` 取值集合为
> `All/WarWizTao/Wizard/Taoist/Warrior`，**没有 Assassin**，也没有任何 Shield 类型物品）。
> 是否应该补回属于玩法口径决策，本报告只给数量与清单。

### 1.2 库里有、网站没有：2 条

| 库 Index | 库名 | 类型 | 判定 |
|---|---|---|---|
| 1365 | `ScortchedEarth` | Book | **拼写错误**：网站/旧库写作 `Scorched Earth`（焰天火雨），库名多了 `t`；与同批 6 本书的命名风格也不一致 |
| 1368 | `ThunderStrike` | Book | 命名差异：网站同法术名为 `Lightning Strike`（雷击术），库名取自 MagicInfo 194 的 `ThunderStrike`；需要人工决定统一口径 |

### 1.3 与既有成果交叉验证（复用说明）

- 复用了 `website-alignment-2026-09-26/item-manifest.json`（中文名→Zircon Index）作为**独立映射**交叉核对：
  本次英文名映射与 manifest 在 **36 条上一致、0 条冲突**。
- 但 manifest 是 2026-09-26 针对旧库（1078 行）生成的：其中有 **11 条候选 Index 在当前库已不存在**
  → 说明「旧库里存在、现库已删」这件事在旧成果里就已经可见。
- 复用 `ver_tags.py` 的 tag 语义（见 §0 勘误）；复用 `Tools/common/zlsdk.py` 读 ZL2 图库。
- 本次新增、旧成果没有的能力：**按名称 join 到清洗前旧库基线做回归对照**（§6），以及**相对图库的图标可用性全量判定**（§5）。

## 2. 名称映射规则与命中率

库里 `ItemInfo.ItemName` 是**英文**（499 条全为 ASCII），网站 `name` 也是英文，`zh` 才是中文 —— 因此不需要靠中文猜。

| 规则 | 定义 | 命中 | 命中率(相对 1078) |
|---|---|---:|---:|
| `exact_name` | 网站 `name` == 库 `ItemName` | 494 | 45.8% |
| `normalized_name` | 去掉非字母数字后一致（空格/连字符/撇号/大小写差异） | 3 | 0.3% |
| `unmatched` | 以上都不命中 | 581 | 53.9% |

冲突处理（不猜）：

- 库内多条归一化同名 → `ambiguous`，排除出字段对比（本次 0 条）。
- 多条网站条目归一化同名而库里只有一条 → **只保留 `exact_name` 那条**，其余退回未匹配
  （`Gold-Plated Bracelet` 4 级/400/帧 648 与 `Gold Plated Bracelet` 25 级/10000/帧 645 是网站上的两条独立词条）。
- **待人工确认清单**：0 条（没有无法映射却硬塞的条目）。

## 3. 字段级差异（497 对可映射条目）

| 字段 | 一致 | 一致(网站缺省按默认语义) | 不一致 | 仅编码不同 | 网站无此字段 | 库无对应 |
|---|---:|---:|---:|---:|---:|---:|
| ItemType（类型） | 497 | 0 | 0 | 0 | 0 | 0 |
| Price（价格） | 497 | 0 | 0 | 0 | 0 | 0 |
| Weight（重量） | 497 | 0 | 0 | 0 | 0 | 0 |
| Durability（耐久） | 410 | 0 | **5** | 0 | 82 | 0 |
| Rarity（稀有度） | 181 | 315 | **1** | 0 | 315 | 0 |
| CanRepair（可修理） | 485 | 0 | 0 | 0 | 12 | 0 |
| RequiredAmount（需求等级） | 329 | 0 | **5** | 5 | 158 | 0 |
| RequiredClass（需求职业） | 496 | 0 | **1** | 0 | 0 | 0 |
| Image（图标帧号） | 492 | 0 | **5** | 0 | 0 | 0 |
| 另有 16 个库侧独有字段（Shape/ItemEffect/StackSize/SellRate/CanSell/CanTrade/CanDeathDrop/CanAutoPot/BuffIcon/PartCount/StartItem/RequiredGender/Description…） | — | — | — | — | 497（网站快照无对应字段，无法对账） | — |

不一致样例（全部在 JSON `field_diff.fields.*.diff_samples`）：

- **Durability / Image / RequiredAmount = 同一批 7 本技能书**：`BlowEarth`、`GreaterFrozenEarth`、
  `MeteorShower`、`Purification`、`Transparency`（另 2 本 `ScortchedEarth`/`ThunderStrike` 因命名未匹配）。
  网站（=清洗前状态）：Image 304、耐久 100、等级 23/38/43；当前库：Image 70、耐久 0、等级 0。
- **RequiredClass**：`Reflect Damage`（移花接玉）网站/旧库写「战士」，当前库为 `Taoist`。
  该书的 `Shape=18` → `MagicInfo[18].RequiredClass = Taoist`，**库内自洽**，因此这里更可能是
  网站/旧库的口径问题（见 §7 待人工确认）。
- **Rarity**：`Renounce` 网站 Elite、当前库 Common（旧库也是 Elite → 属回归，见 §6）。
- **RequiredAmount 仅编码不同（5 条）**：`Platinum Necklace/Ring`、`Ruby Ring`、`Skeleton Ring`、
  `Gold Dragon Ring` —— 数值相同，网站写「等级 N」，库改用属性门槛（`RequiredType=MC/SC/DC`）。
  服务端确实支持：`ServerLibrary/Models/PlayerObject.cs:7542-7551` 按 `RequiredType` 取 `Stats[MaxAC/MaxMR/MaxDC/MaxMC]` 比较。

### 3.1 词条（网站 `attrs` ↔ 库 `ItemInfoStat`）

解析规则：`物攻/魔攻/道术/物防/魔防` 的「A-B」→ `Min*/Max*` 两个词条，「+N」→ `Max*` 一词条；
其余按 `CN_STAT_MAP` 单一映射（完整映射表在脚本里）。负号不算区间分隔符。

| 结果 | 条数 |
|---|---:|
| 完全一致 | **314 / 497（63.2%）** |
| 只差 Min/Max 落位（网站「+N」，库落 Min*） | 19（合并计算后 **67.0%**） |
| 网站多词条（库里没有） | **29** |
| 库多词条（网站没写） | 0 |
| 两侧各有差异 | 0 |
| 两侧都空 | 135 |

网站多出来的 29 条全部是同一类问题（见 §7 已确认缺陷）：19 件药水（Health/Mana）、
7 个元素护身符（XX 亲和）、2 支明亮火把/蜡烛（Light 25）、1 件重生丹（物品复活时间）。

> 未映射标签 0 条；「亲和」类标签的映射属命名推断（网站只写了中文名，未由数据证实），
> 但本次由旧库基线**直接证实**（旧库同名词条就叫 `FireAffinity` 等，见 §6）。

Drops：`DropInfo` 5235 行**全部**指向存在的 ItemInfo（0 条悬空），且每件物品的内嵌
`ItemInfo.Drops` 长度与 `DropInfo` 行数**完全一致**（0 处不一致）。网站 `drops` 文本是
「物品名 ×数量 1/概率 …共 N 种」，条数口径与库不同（例：Wood Sword 网站写「共 9 种」、库 18 行），
本报告不做条数差判定。

## 4. 套装 / 货币 / 商店 / 武器打造

### 4.1 套装（SetInfo 30 / SetInfoStat 200）

| 指标 | 数值 |
|---|---:|
| 套装总数 | 30 |
| **成员数为 0 的套装** | **25** |
| 有成员的套装 | 5（Vigor 3、Life Stealing 3、Ancient Kingdom 4、Summoning 4、Gale Set 2 = 16 件） |
| 网站声明「属于某套装」的物品 | 88 |
| 库内 `ItemInfo.Set` 挂钩的物品 | **16** |

- 25 个零成员套装：`Spirit Caller, Numa, Carnage, Stars, Serenity, Dragon Lord, Earth, Purity,
  Crescent, Abyss, Garnet, Lapis Lazuli, Malachite, Obsidian, Sapphire, Alexandrite,
  new war/sin/tao/wiz set, new boot, OdynSin/OdynSon/OdynMythical/OdynElemental`。
- 这些套装的 `SetInfoStat`（共 200 行）**永远不生效**，因为成员关系只来自 `ItemInfo.Set`。
- 缺口明细（26 组）：全部表现为「网站列出的成员物品在当前库不存在」（例：Dragon Lord 5/6 件缺失、
  Spirit Caller 5/5 缺失、new war/sin/tao/wiz set 各 3/3 缺失）。**没有**「物品在库但没挂 Set」的情况。
- 网站名与库名集合完全一致（只在库里有/只在网站有的套装名均为空）。

### 4.2 货币（CurrencyInfo 5 / CurrencyInfoImage 8）

| Index | 名称 | Type | DropItem | 图片行数 |
|---|---|---|---|---|
| 1 | Gold | Gold | ItemInfo 1 | 8（帧 120–127，面额 0/100/200/500/1000/1e6/5e6/1e7） |
| 2 | Game Gold | GameGold | 无 | 0 |
| 3 | Hunt Gold | HuntGold | 无 | 0 |
| 4 | Fame Point | FP | ItemInfo 1127 | 0（靠 `DropItem.Image`=4010 回退） |
| 5 | Contribution Point | CP | ItemInfo 1128 | 0（回退 4012） |

无缺口：`DropItem` 全部指向存在的物品；8 张货币图的帧号在 Storeitem.Zl 全部可解析。
`GameGold/HuntGold` 没有 DropItem 也没有图，属设计（不参与物品图标，见
`GodotClient/Controls/DXItemCell.cs:546` 的取图规则）。网站基准里「类型 货币」只有 3 条
（Gold/Fame/Contribution），与本表口径不同，无法逐项对账。

### 4.3 商店（StoreInfo）

当前库只有 **1 行**（Index 119 → `Scroll Of Town Portal`，Price 10，`Filter=Consumable, Lazy`），
该物品存在。**清洗前基线是 92 行** → 见 §6、§7（回归项）。

网站基准快照里**没有商场/GameStore 表**，因此无法与快照逐项对账；要判定「应有多少行」需要另一个证据源
（旧库 baseline 的 `StoreInfo.json` 已给出 92 行这一历史事实；另有 `SystemDbProbe --stores` 的 stores.json 路径）。

### 4.4 武器打造（WeaponCraftStatInfo 110）

- 110 行 = 22 个属性 × 5 档；每档 `Weight` 为 50/20/10/1 + 一条 Weight=100 的保底档。
- 自洽检查：4 档（50/20/10/1）按权重排列时 Max 值递增 —— `non_monotonic_ladder` 为空，**无异常**。
- `RequiredClass` 取值：All 55 / AssWar 25 / Wizard 15 / Taoist 15。
  `AssWar = Warrior|Assassin`（`LibraryCore/Enum.cs:77`），而当前库 499 件物品**没有任何 Assassin 职业物品**
  → AssWar 档位是否可达需要玩法证据（待人工确认）。
- 网站基准没有打造表，无法对账。

## 5. 图标可用性（499/499 可解析）

判定口径与客户端一致：`GodotClient/Formats/ZlReader.cs:135-160` 在「index 越界 / 帧头为空 /
ZL2 payload entry 缺失」时都取不到纹理。用 `zlsdk.ZlLibrary.header()/is_blank()` 复刻同一口径。

| 指标 | 数值 |
|---|---:|
| 图库（客户端实际加载：`Libraries.cs:21` → `Data\StoreItem.Zl`） | `Storeitem.Zl`（md5 `dab3dfa6…7692`，2,761,318 B） |
| 帧槽位（metadata count） | 8,590 |
| 有 payload 的帧（可用） | 2,370 |
| 被测物品 Icon（`ItemInfo.Image`） | 499 |
| **解析成功** | **499** |
| 解析失败 / 越界 | 0 |
| 货币图（CurrencyInfoImage 帧 120–127） | 8/8 成功 |
| 帧头自洽（StoredImageDataSize == BC7 块数×16） | 379/379 用到的帧全部自洽 |
| 真实解码抽样（8 帧，BC7 → RGBA） | 8/8 解码成功且不透明像素 > 0 |
| 独立交叉校验（另一份 `Storeitems.Zl`） | 共同帧 379，帧头一致 366，**13 帧不一致** |

- **不能用「疑似越界」充当结论**：`Image` 最大 6010（`Fame Point`），仍在 8,590 槽位内且能解码。
- 帧号复用情况（正常现象，列出供参考）：帧 304 被 54 本书共用；帧 70 被 8 件共用
  （`Ginseng Of Eternity` + 7 本技能书）；帧 300 被 5 种肉共用。
- **13 帧不一致**（各自宽高/偏移不同）列为待人工确认：帧 9, 28, 38, 53, 66, 67, 68, **70**, 96, 301,
  1411, 3134, 3135。主图库解析与 BC7 载荷长度自洽，所以倾向主图库正确；仍需看图确认。
  渲染命令（BC7 需 texture2ddecoder）：
  ```bash
  uv run --with texture2ddecoder --with pillow python3 -c "import sys;sys.path.insert(0,'/Users/tetsuya/Development/Mir3-Research/Tools/common');from zlsdk import ZlLibrary;ZlLibrary('/Users/tetsuya/mir2ei/Data/Storeitem.Zl').decode(70).convert('RGBA').save('/tmp/f70.png')"
  ```
- 图库名大小写：`Libraries.cs` 写 `Data\StoreItem.Zl`，磁盘上是 `Storeitem.Zl`；在 Linux（82）上
  `MirSkin.ResolvePath` 会按 OrdinalIgnoreCase 枚举出正确文件，**已核对不构成缺陷**。

## 6. 回归对照（当前库 vs 清洗前旧库基线）— 本节是最硬的证据

旧库基线：`Mir3-Research/Tools/dbeditor/workspace/baseline/`（Aug 13 21:26），与本报告对账的当前库是**同一份库的两个时点**。

### 6.1 规模变化

| 表 | 旧库 | 当前库 | 变化 |
|---|---:|---:|---|
| ItemInfo | 1,078 | 499 | −579 |
| ItemInfoStat | 3,196 | 1,537（含 266 条孤儿） | −1,659 |
| DropInfo | 10,382 | 5,235 | −5,147 |
| **StoreInfo** | **92** | **1** | **−91** |
| SetInfo / SetInfoStat | 30 / 200 | 30 / 200 | 0 |
| WeaponCraftStatInfo | 110 | 110 | 0 |
| CurrencyInfo / CurrencyInfoImage | 5 / 8 | 5 / 8 | 0 |

按名称 join：存活 497 件；旧库独有 **578** 件（= 上面 §1.1 的缺口）；新库独有 2 件（`ScortchedEarth`、`ThunderStrike`）。

### 6.2 字段回归（存活 497 件中）

| 字段 | 变化条数 | 样例 |
|---|---:|---|
| Shape | 26 | **4 件消耗品被改成 0**：三瓶修理油 11/12/13 → 0、`Pill Of Reincarnation` 15 → 0（服务端按 Shape 分发，全部失效）；**21 本书**的法术 Shape 重编号（旧 35…133 → 新 175…197，与新 MagicInfo 索引一致，属**重构**非缺陷）；`Talisman Of Soul` 1 → 0 |
| Image | 5 | 5 本书 304 → 70 |
| Durability | 5 | 5 本书 100 → 0 |
| RequiredAmount | 5 | 5 本书 23/38/38/43/43 → 0 |
| CanDeathDrop | 5 | 5 本书 True → False |
| Rarity | 1 | `Renounce` Elite → Common |
| RequiredClass | 1 | `Reflect Damage` Warrior → Taoist（与 MagicInfo 自洽，见 §3） |
| 其余 18 个字段 | 0 | — |

### 6.3 词条丢失（**29 件物品的全部词条消失**）

| 组 | 件数 | 旧库词条 | 当前库 |
|---|---:|---|---|
| 药水（Healing Potion I–V / Life Pill I–V / Mana Potion I–IV / Mana Pill I–IV / Rejuvenation Potion (II)） | 19 | Health 30/70/110/170/250、Mana 40/110/180/250 | **无任何词条** |
| 元素护身符（Talisman Of Darkness/Fire/Holiness/Ice/Illusions/Lightning/Wind Storm） | 7 | Dark/Fire/Holy/Ice/Phantom/Lightning/WindAffinity 各 1 | **无任何词条** |
| 明亮蜡烛 / 明亮火把 | 2 | Light 25 | **无任何词条** |
| Pill Of Reincarnation | 1 | ItemReviveTime 300 | **无任何词条**；且 `Shape` 15→0（见 C4），复活分支 `PlayerObject.cs:6803-6811` 同时依赖两者 → **复活功能整体失效** |

另有 8 件物品的词条**数值被改**（`Scimitar` MaxSC 1→2、`Rusty/Cracked/Worn Signet Of Moon` MinSC 2/2/5→3/3/6、
`Cracked Bracer Of Moon` MinSC 1→5、`Rusty Pendant Of Moon` MinSC 2→7 等）——这些是平衡改动还是误改，需要有口径记录。

### 6.4 Index 漂移

**172 件存活物品的 `ItemInfo.Index` 与旧库不同**（例：`Commoner Outfit (M)` 127→1168、
`Light Armour (M)` 188→1172、`Healing Potion` 133→1323）。
库内引用已同步（DropInfo/StoreInfo/CurrencyInfo 0 悬空），但**任何按 Index 持久化的外部数据**
（`Users.db` 的背包/腰带/AutoPotion 链接/邮件附件/拍卖、旧导出、外部工具快照）都需要单独核对。

## 7. 结论：已确认缺陷 vs 待人工确认

### 7.1 已确认缺陷（有代码路径或旧库基线证据）

| # | 缺陷 | 影响面 | 证据 |
|---|---|---|---|
| C1 | 19 件经典药水无 `Health/Mana` 词条 → 回血/回蓝为 0 | 玩家核心体验 | `PlayerObject.cs:6571-6575`（Potion 分支读 `Info.Stats[Stat.Health]/[Stat.Mana]`）、`:555`（自动药水判定）；旧库有 Health/Mana |
| C2 | 7 个元素护身符无 `XXAffinity` → 元素魔法取不到属性 | 元素系魔法/暗石机制 | `ElementalHurricane.cs:21`、`Spiritualism.cs:19`、`ElementalPuppet.cs:21`、`EvilSlayer.cs:63`；旧库有各 Affinity=1 |
| C3 | 明亮蜡烛/火把无 `Light` → 照明不生效（基础蜡烛/火把正常） | 夜间视野 | `PlayerObject.cs:16916` 下发 `Light = Stats[Stat.Light]`；旧库有 Light 25 |
| C4 | 4 件消耗品 `Shape` 被改成 0（三瓶修理油应 11/12/13、重生丹应 15）→ 修理油「用了没反应」、**重生丹不再复活** | 装备维修、死亡复活 | `PlayerObject.cs:6730/6747/6757`（修理油）、`:6803-6811`（case 15 复活丹：设 `Dead=false`、`SetHP/SetMP`、`ReincarnationPillTime += Stats[Stat.ItemReviveTime]`）；旧库 Shape 11/12/13/15 |
| C5 | 25/30 套装零成员（网站声明 88 条成员，库里只挂 16 条） | 套装加成全部失效 | `ItemInfo.Set` 为唯一成员来源；`SetInfoStat` 200 行永不生效 |
| C6 | 581 条网站条目在当前库无对应（含 18/20 块暗石、20/24 火把、12/12 盾牌） | 内容缺口 | §1.1 清单 |
| C7 | 266 条 `ItemInfoStat` 父物品已不存在（残留行） | 运行期无害（只经 `ItemInfo.ItemStats` 读），影响编辑器与库体积 | `ItemInfoStat.cs:7-9` 的 `[IsIdentity] Item`；`ItemInfo.cs:450,488` |
| C8 | 图标可用性：499/499 可解析（无缺陷，作为已验证项列出） | — | §5 |

### 7.2 待人工确认（证据不足以定性）

| # | 事项 | 为什么不能定性 | 下一步 |
|---|---|---|---|
| H1 | 7 本技能书 Image=70 / 耐久 0 / 需求等级 0（另 2 本命名未匹配） | 图标美术语义无法离线判定（本机无图像理解模型）；等级 0 可能是有意的「无等级门槛」 | 游戏内看这 7 本书的图标与需求；渲染帧 70/304 对比 |
| H2 | StoreInfo 92 → 1 行 | 无「应有行数」的独立证据；可能是经典化裁剪 | 与玩法口径确认；旧库 StoreInfo.json 里 92 行的 TopN 供参考 |
| H3 | 172 件物品 Index 漂移 | 库内引用一致；`Users.db` 未纳入本次范围 | 读 `Users.db` 统计有多少玩家物品引用了漂移后的 Index |
| H4 | `Reflect Damage` 职业 Warrior(网站/旧库) vs Taoist(现库) | 库内自洽（书与 MagicInfo 一致） | 与玩法口径确认，若网站错则记录为「网站口径问题」 |
| H5 | WeaponCraftStatInfo 的 AssWar 档位可达性 | 需玩法层证据 | 结合武器打造的 class 判定代码确认 Assassin 是否参与 |
| H6 | 13 个图库帧号在两份库中几何不同 | 需看图；两份库都是「转换产物」 | 渲染对照图后人工查看 |
| H7 | 8 件物品词条数值被改（Scimitar 等） | 可能是平衡改动 | 找改动记录；否则列入「需回滚/确认」 |
| H8 | `ScortchedEarth` 拼写、`ThunderStrike` 命名 | 命名口径 | 决定是否改名为 `Scorched Earth` / `Lightning Strike`（改名会牵动网站与文案） |
| H9 | 1125 条 legacy/MUD3-DAT 独有物品（ver 无 zircon） | 本次范围外（其中 251 条连 `img` 都没有） | 单独一轮对账 |

## 8. 建议修复顺序（含改哪张表/哪个字段）

> **写库纪律（务必遵守）**：服务端运行中绝不写 `System.db`；写前备份；写完 round-trip 读回验证；
> 保持 4 处镜像 MD5 一致（`Debug/ServerCore/Database/System.db`、`/home/tetsuya/mir2ei/Data/System.db`、
> `/home/tetsuya/mir2ei/Database/System.db`、`Development/zircon/System.db`）。

1. **恢复 30 行物品词条（C1/C2/C3）** —— `ItemInfoStat` 表，字段 `Item/Stat/Amount`
   （19 件药水 20 行 + 7 个护身符 + 2 支亮火把 + 重生丹 1 行）。
   数值可直接从 `Tools/dbeditor/workspace/baseline/ItemInfoStat.json` 按名称 join 取回（本报告已确认数值一致）。
   实现方式二选一：① 在 `Server` 的系统数据编辑器里逐条加（30 行，量小）；② 写一个小工具
   （模板见 `tools/ClassicItemFixer/Program.cs`：`new Session(SessionMode.System, root)` + `session.GetCollection<ItemInfoStat>()`，
   按 `ItemInfo.ItemName` 匹配旧值写入，`session.Save(true)`），随后同步 4 处镜像。
2. **4 件消耗品 `ItemInfo.Shape` 改回旧值（C4）** —— `Superior Repair Oil`=11、`Accessory Repair Oil`=12、
   `Armour Repair Oil`=13、`Pill Of Reincarnation`=15（各一行；重生丹还要同时恢复 `ItemReviveTime` 词条）。
   若坚持用 0，必须改 `ServerLibrary/Models/PlayerObject.cs` 的 Shape 分发（不推荐，会与上游/客户端语义背离）。
3. **套装修复（C5）**：先决定 30 个套装里哪些要保留；对要保留的，补齐成员物品并设置
   `ItemInfo.Set`（`ItemInfo.cs:435` 的关联字段），否则直接删掉无用 `SetInfo/SetInfoStat` 行以免误导编辑器。
4. **技能书补齐（H1）**：`Image` 304、`Durability` 100、`RequiredAmount` 按网站值；确认图标美术后再定。
5. **内容缺口决策（C6）**：按类型逐组决策（暗石 18 块、盾牌 12 件、Mark 印记 20 件…）。补回需要
   从旧库/MUD3 源数据导入并重新生成 `DropInfo` 与 NPC 商品引用。
6. **清理 266 条孤儿 `ItemInfoStat`（C7）** —— 删除 `Item` 指向不存在物品的行（删除前先 dump 一份）。
7. **Index 漂移核对（H3）** —— 用 `Users.db` 逐表检查是否还有旧 Index 引用。

## 9. 复现与验证命令

```bash
# 1) 主对账（离线，约 1 秒）
cd /Users/tetsuya/Development/Zircon && python3 tools/audit/items_audit.py

# 2) 带真实图标解码抽样（需 BC7 解码器）
uv run --with texture2ddecoder --with pillow python3 tools/audit/items_audit.py --decode-sample 8

# 3) 只做回归对照（指定别的旧库导出目录）
python3 tools/audit/items_audit.py --baseline-export /path/to/old/export --json-only

# 4) 关键独立复核（不依赖本脚本的解析逻辑）
#    a. 旧库是否真有这些词条
python3 -c "import json;rows=json.load(open('/Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/workspace/baseline/ItemInfoStat.json'))['rows'];print([r for r in rows if isinstance(r.get('Item'),dict) and r['Item']['Name'] in ('Healing Potion','Talisman Of Fire','Bright Torch','Superior Repair Oil')])"
#    b. 服务端确实按 Shape/词条分发（源码位置见 §7.1）
#    c. 图标帧是否可取（与客户端同口径）
python3 -c "import sys;sys.path.insert(0,'/Users/tetsuya/Development/Mir3-Research/Tools/common');from zlsdk import ZlLibrary;z=ZlLibrary('/Users/tetsuya/mir2ei/Data/Storeitem.Zl');print(z.header(70), z.is_blank(70))"
```

中间对照图（人工看图用，非交付文件）：`/tmp/frames_70_304_1042_1_120.png`。

## 10. 不确定与未覆盖

- **没有做**：任何游戏内验证（未启动客户端/服务端）、`System.db` 直读（只用导出 JSON）、
  `Users.db` 侧引用核对、legacy/MUD3-DAT 1125 条物品、Drops 条数口径逐项比对。
- **无法离线定性**：图标美术语义（本机 `dim modality` 只有 OCR，无图像理解模型）、
  清洗是否为有意裁剪、Index 漂移对玩家存档的实际影响。
- **口径提醒**：`wiki_data_v2.json` 的 items 快照既含网站信息（attrs/meta/class/desc）
  又含旧库信息（`img.frame`），因此「网站基准」并不是完全独立的第三方数据；本次用
  旧库 baseline 作为第二条证据链来消歧。
- 孤儿 `ItemInfoStat` 的「父行是否真的不存在」这一条，是由导出 JSON 缺 `Item` 键推断的
  （未直接读 `System.db`）。如需坐实，可在**只读副本**上执行：
  `SELECT COUNT(*) FROM ItemInfoStat s LEFT JOIN ItemInfo i ON i.Index=s.Item WHERE i.Index IS NULL;`

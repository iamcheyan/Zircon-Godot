# Mud3 内容回迁 + 旧版背包：调查交接（2026-10-03）

> **性质**：调查与实施方案。本文只记录已核实结论与建议落地顺序。
> **禁止**：在用户明确授权前写 `System.db`、改 C#、改 `db_names.json`。
> **读者**：实施智能体。验收由本次调查会话负责。
> **范围**：当天三件事——(1) 用 Mud3 整理装备/怪物/名字；(2) 旧装备多格占用；(3) 背包右侧锁链滚动与「负重没满就能一直放」。

---

## 0. 一句话结论

引擎继续用 Zircon。世界内容以 Mud3 为权威。玩家看到的中文名走 `db_names.json`，**不要**把中文写进 `ItemInfo.ItemName` / `MonsterInfo.MonsterName`。背包多格占用和锁链滚动都是 **Godot 客户端布局问题**，不是数据库字段；服务端背包仍是 48 条记录数组，负重是第二条上限。

三件事可以并行，但背包两件事有依赖：先修 `HostGrid` 和占用，滚动才有「高于 6 行」的内容可滚。

| 工作流 | 改什么 | 不改什么 |
|---|---|---|
| A 身份表 + 显示名 | `mud3_identity` + `db_names.json` | `System.db` 身份键 |
| B 数值 / 掉落 / 补缺 / 裁剪 | `System.db`（经 dbeditor） | Users.db、引擎系统代码 |
| C 多格占用 | Godot `DXItemGrid.CreateGrid` 的 `HostGrid` + 刷新 | ItemInfo 新字段 |
| D 锁链滚动 | 行数随占用增高、物品变更后重算、F280 命中区 | 把背包做成无限槽；不要拿聊天 F68 冒充 F280 |

---

## 1. 工作流 A+B：把数据库整理清楚（Mud3 内容回迁）

### 1.1 用户目标（已确认）

地图已经按 EI/Mud3 迁过。卡住的是装备、怪物、名字。Zircon `System.db` 是后期 LOMCN 库：多怪、多装、数值对不上、身份名是英文。手翻英文又累又不准。要用 Mud3 里已经有的中文名和数值。

理解成：**引擎继续用 Zircon，世界内容回到 Mud3。**

### 1.2 现状：数据在，生产映射不在

| 层 | 现在是什么 | 权威源 |
|---|---|---|
| 引擎 | Zircon C# + GodotClient | 本仓库代码 |
| 世界静态库 | `System.db`（MirDB，不是 SQLite） | ItemInfo 1078、MonsterInfo **434**、DropInfo 10382、MagicInfo 174；英文 `[IsIdentity]` |
| 老世界 DAT | `stditem.dat` 1143 / `monster.dat` 433 / `magic.dat` 105 | GBK 名 + 老数值；已解码 |
| 刷怪 / NPC / 传送 | `Mon_Def/*.gen`、`Merchant.txt`、`Mapinfo.txt` | 地图迁移已在用 |
| 掉落 | Mud3 `MonItems/*.txt` 约 280 表 | 明文 GBK，还没进库 |
| 显示名 | `GodotClient/translations/db_names.json` | 客户端查表；库里英文名不动 |

本地 Mud3 副本（NAS `/home/tetsuya/NAS/TMP/Mud3` 当时不可用）：

`Mir3-Research/local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/`

解码产物：

`Mir3-Research/docs/research/mud3-dat-decoded/{stditem,magic,monster}.json` 与 `comparison.md`

客户端查表：`GodotClient/Scripts/LocalizedName.cs` 读 `res://translations/db_names.json` 的 `items` / `monsters` / `npcs` / `magics` / `maps`。服务端 `SEnvir.GetItemInfo` / `GetMonsterInfo`、GM、掉落、商店仍按英文身份名查找。

**硬约束：不要把中文写进 `ItemName` / `MonsterName`。** 那是 `[IsIdentity]`。写成「金创药」之后 `@make Healing Potion`、掉落、任务、商店外键都会断。

### 1.3 为什么现有中文又累又不准

`db_names.json` 覆盖已经很高（物品约 1055/1076、怪物 426、技能 174 全有中文），但很多是按 **Zircon 英文词义意译**，不是 Mud3 官方名。

| Zircon 英文身份 | 现在显示 | Mud3 官方名 |
|---|---|---|
| `Oma` | 祖玛 | 半兽人 |
| `Uma King` | 祖玛王 | 沃玛教主 |
| `Zuma King` | 祖玛王 | 祖玛教主 |
| `Dragon Rise` | 龙影剑法 | 翔空剑法 |
| `Destructive Surge` | 破血狂杀 | 十方斩 |
| `Might` | 蛮力 | 破血狂杀 |
| `Beckon` | 召唤 | 斗转星移 |
| `Adamantine Fire Ball` | 金刚火球 | 大火球 |
| `Commoner Outfit (M)` | 平民布衣（男） | 布衣（男） |

Uma / Zuma 都译成「祖玛王」，沃玛线和祖玛线拧在一起。Zircon 英文不是传奇3 中文的反译，里面还有后期私服原创（Nemesis / Valkyrie / 苏美尔 Boss）。

正确做法：显示名以 Mud3 DAT 的 GBK 名为准，再挂到 Zircon `Index` 上。技能已有高质量对照（约 61/105 对上，学习等级三元组同源）。物品、怪物仍是锚点，不是全表。

网站（`mir3-website` / 百科 / Legacy Atlas）和 dbeditor 的 `__zh` 必须读**同一张身份表**，禁止再各翻各的。

### 1.4 三件事必须分开做

#### A. 玩家看到的名字（最快、最安全）

产出一张 canonical 身份表，建议路径：

`Mir3-Research/docs/research/mud3-identity/`

字段示例：

```text
mud3_name, kind, zircon_index, zircon_en, confidence, evidence
祖玛教主, monster, 81, Zuma King, closed, hp+image+spawn
金创药（小）, item, 133, Healing Potion, closed, price+heal
凝霜, item, null, null, missing, stditem only
Doom Claw, monster, 309, Doom Claw, zircon-only, —
```

规则：

- 一对一、证据闭合 → 写入 `db_names.json` 的 `zh`
- 一对多（尸王、掷斧骷髅）→ `pending`，人工勾，**禁止自动猜 Index**
- Mud3 有、Zircon 无 → `missing`，留给导入
- Zircon 有、Mud3 无 → `zircon-only`，中文可空或标「未开放」

这一步**不写 System.db**。改完 `db_names.json`，进游戏就能看到「祖玛教主」「金创药（小）」。

证据优先级（高到低）：

1. 已验证数值锚点（价格、回复量、学习等级）
2. 外观：`Looks` ↔ `ItemInfo.Image`，`Appr` ↔ `MonsterImage`
3. 刷怪 / 掉落共现（同图、同掉落组）
4. 网站 / 17173 / 术语表只做候选，不当唯一证据

现成材料：

- `Mir3-Research/docs/research/mud3-dat-decoded/`
- `build_comparison.py` 的 `SKILL_MAP` / `ITEM_MAP` / `MONSTER_MAP`
- NpcMover 的 `WEBSITE_MONSTER_TO_INDEXES`（一对多不自动挑）
- `docs/terminology/`
- dbeditor workspace JSON

#### B. 数值回到老版（只写已验证字段）

Mud3 和 Zircon **同源但不是同一套平衡**。例：祖玛教主老版 HP 14000 / 攻 70–175，Zircon 21000 / 255–360。药水价格、木剑/铁剑/布衣是对齐的。技能学习等级三元组约 61 条完全一致，修炼经验量纲不统一（×10 / ×100 / ×1）。

只覆盖 `verified` / `high` 字段（见 `comparison.md`）：

- 物品：价格、重量、耐久、需求等级、药品回复量、职业/类型
- 怪物：等级、HP、经验、AC/MAC、DC 上下限；攻速/移速中等把握
- 技能：学习等级直接用；耗蓝/威力增量已验证的用；修炼经验先统一量纲再写

`Attr1–4`、`Luck`、怪物抗性位、一堆 `Unk*` **先不写**。写进去会静默把装备魔法/幸运弄错。

#### C. 内容集合：补缺 + 裁剪

数量级：

- 物品：Mud3 1143 vs Zircon 1078。按中文名粗对大约只有约 60 件共享；Mud3 独有约 289（含井中月 / 凝霜 / 裁决 / 屠龙 / 太阳水——其中不少是改名后没对上，不是真缺）
- 怪物：Mud3 433 条 ≈ 262 种基础 + 变体（`0` / `9` / `61` / `96+`）。Zircon **434** 条里混着苏美尔、兵马俑、Doom Claw
- 技能：老版 105（含 41 条装备附魔变体）vs Zircon 174（刺客一整棵约 53 个是后期的）

Zircon 独有地图刷怪已经拿掉，**MonsterInfo / ItemInfo / DropInfo 行还在**。所以游戏里仍可能 `@spawn Doom Claw`，背包里仍有 Elite 英文刀。

### 1.5 推荐落地顺序

#### 第 0 周：定内容边界

先定复刻档，后面「该不该进库」都按这档裁：

- **档 A：EI 2.0 / 早期传奇3**（比奇、沃玛、祖玛、赤月、真天宫、幽灵船、诺玛）——和「用 Mud3 数据」最贴
- **档 B：传奇3 中后期**（龙渊、冰宫、潘夜、刺客）
- **档 C：西沙**

当前地图已经偏传奇3 全图。装备怪物若严格跟 Mud3 DAT，会出现「地图上有西沙，库里没有西沙装备」。这是产品选择。建议：**地图可以留全，物品/怪物/掉落先跟 Mud3 DAT**；西沙以后当独立内容包。

刺客、坐骑、伙伴、钓鱼、精炼、商城：引擎有，Mud3 没有。先藏入口，不要删代码。

#### 第 1 步：身份表（1 张表驱动所有工具）

只读生成器，输入：

- `stditem.json` / `monster.json` / `magic.json`
- 当前 ItemInfo / MonsterInfo / MagicInfo（dbeditor workspace）
- `Looks` / `Appr` / `Image` 对照
- `Mon_Def` 刷怪名、`MonItems` 掉落名
- 现有 `ITEM_MAP` / `MONSTER_MAP` / `SKILL_MAP`

输出：`closed` / `pending` / `missing` / `zircon-only` 四类清单 + 人工审核页（可挂 dbeditor 或现有 wiki）。

**禁止自动在多个候选里挑一个 Index。**

验收：抽 50 个经典名（金创药、木剑、布衣、半兽人、沃玛教主、祖玛教主、赤月恶魔、火球术、半月弯刀）全部 `closed`，且 Uma ≠ Zuma。

#### 第 2 步：只改显示名

用 `closed` 行重写 `db_names.json` 的 `zh`。技能直接用已有 `SKILL_MAP`（「龙影剑法」改回「翔空剑法」这类）。

进游戏看：地面物、背包、怪物名牌、技能书、NPC 商品。服务端聊天/GM 仍是英文身份，可以接受。中文 GM 命令后做：给 `GetItemInfo` 加别名表，`@make 金创药` → 查身份表 → `Healing Potion`。

这一步做完，翻译工时和不准就消掉。

#### 第 3 步：数值覆盖（分批）

1. 药水 / 肉 / 矿石 / 基础武器防具（已交叉验证）
2. 技能学习等级 + 已验证耗蓝/威力
3. 普通怪 HP/攻防/经验
4. Boss HP/攻防（单独验收；老数值在 Zircon 公式下会偏弱）

每批：工作区 diff → 停服写库 → 游戏内 `@make` / 打一只怪看血条和伤害。Boss 不要和普通怪同一天灌。

#### 第 4 步：补 Mud3 有、库里没有的东西

典型候选：凝霜、裁决之杖、屠龙、太阳水、白野猪、食人花（先确认是真缺定义，不是改名没对上）。

新建 `ItemInfo` / `MonsterInfo` 时：

- 身份名用稳定英文（官方英译或拼音，一旦定了不再改）
- `zh` 立刻写入身份表
- `Image` / `MonsterImage` 必须对上现有图库；对不上就先占位，不编外观
- 变体（`半兽人0`、`骷髅61`）默认不建新怪，除非刷怪/任务真用到

#### 第 5 步：掉落和商店（比数值更影响「像不像」）

Mud3 掉落在 `MonItems/*.txt`，不是 DAT。Zircon 是 DropInfo 10382 行，含大量 Elite / 后期 Boss。

1. 解析全部 MonItems → `{怪物中文名, 物品中文名, 概率, 数量}`
2. 经身份表换成 Index
3. 对已 `closed` 的怪：**替换**其 DropInfo，不要和 Zircon 掉落合并
4. NPC 商品从 Merchant / NPC 脚本进 `NPCGood`
5. 没闭合的怪/物进 `pending` 掉落报告，不写库

#### 第 6 步：藏起 Zircon 独有内容

不要物理删记录（任务、套装、代码可能还引用 Index）。用标记：

- `zircon-only` 物品：关掉 `CanDrop` / `CanSell`，商店下架，掉落表去掉
- `zircon-only` 怪物：清 `RespawnInfo`（地图迁移基本已清），GM 仍可刷便于测试
- Elite / 苏美尔 / Doom Claw 整族按身份表一键下线

以后要开「现代档」把标记翻回来即可。

#### 第 7 步：引擎做不到 1:1 的，单独决策

| 老版机制 | Zircon | 建议 |
|---|---|---|
| 41 条装备技能（聚集/连锁/通天/分散、元素幽灵盾） | 装备词缀 | 先做成固定属性，不做 41 个独立技能 |
| 修炼经验三种量纲 | 已统一绝对值 | 导入时换算，不要原样拷贝 |
| 高级武器 `NeedLevel=138`（38+100 未开放标记） | 已清理 | 导入时还原「未开放」或改成 38 |
| 变体怪 `61/62` HP=1 | 无此体系 | 任务需要再单开 |
| 刺客 / 伙伴 / 钓鱼 / 精炼 | 有整套系统 | 内容档关掉，代码留着 |

### 1.6 数据流

```text
Mud3 DAT / MonItems / Mon_Def
        ↓ 解码（已完成）
JSON 权威源
        ↓ 身份表（缺的就是这一层）
   ┌────┴────┐
显示名         数值/掉落/刷怪
db_names.json  System.db（经 dbeditor）
   ↓              ↓
游戏里中文名     血、攻、价格、爆率
```

缺的不是解码，是 **身份表 + 分通道写入**。

### 1.7 写库纪律（无例外）

来源：`Mir3-Research/AGENTS.md`、`Zircon/AGENTS.md`。

1. `System.db` 是 MirDB，不是 SQLite。读走 `Tools/SystemDbProbe`，写走 dbeditor workspace JSON → `Tools/dbeditor/sync.sh` → `Tools/DBImporter`。
2. 服务端在跑（`:7000` 有监听）→ **绝不写库**。
3. 流程：停服 → 备份双库 → 干跑 → 写测试副本 → round-trip 读回 → 写真库 → **双库同步**（`Debug/ServerCore/Database/System.db` 与 `Debug/Client/Data/System.db`）→ 重启 → 游戏内实测。
4. 不要绕过缓冲区直写 `.db`。不要写 `Users.db`。
5. 校验器和导入器不要共用同一份猜测逻辑（地图迁移已踩过：工具和校验共用错误约定会「自洽通过」）。
6. `Users.db` 里已有角色会脏：改 Index / 删物品后背包可能指向空洞。测试号可清；正式号要迁移或允许残留。

### 1.8 建议（实施时仍有效）

1. 先做名字，再做数值。名字错，数值对了也不像那个游戏。
2. 一对多一律人工。
3. 网站当审核台，不当翻译源。最终中文名仍来自 DAT。
4. 最小一周闭环：修 `db_names.json` 里已证实错译（Oma/Uma/Zuma、技能名、布衣/金创药）→ 生成未闭合清单按武器→防具→Boss→普通怪闭合 → 药水和基础武器价格/回复量进库并游戏内买一次、喝一次。

---

## 2. 工作流 C：旧装备占用多格

### 2.1 用户问题

旧版一件装备占好几格（衣服/武器大约 3～4 格高）。贴图已经换成旧版，格子占用还没有。格子定义找不到。

### 2.2 格子不在数据库里

`ItemInfo` **没有** 宽/高/占用格数字段。EI 也不靠策划表填「占几格」。

占用公式（EI + 当前 Godot 意图）：

```text
width  = max(1, ceil(Inventory帧宽 / 36))
height = max(1, ceil(Inventory帧高 / 36))
```

格子是 **6 列 × 36px** 的占位网格。权威图库是 **`LibraryFile.Inventory`**（Inventory.wil / Inventory.Zl），不是现代 `StoreItems`。

实测（`zlsdk` 读 Inventory.Zl）：

| 物品 | Image | 帧尺寸 | 占用 |
|---|---|---|---|
| 木剑 | 1042 | 16×102 | 1×3 |
| 男布衣 | 940 | 48×98 | 2×3 |

EI 证据：

- 网格：6 列，原点 `(25, 41)`，步长 36（`bag-grid-geometry-evidence.json`，hit-test `0x42F150` / `0x42F240`）
- 物品记录：46 条（`0x2E`），`bag+0x774`，步长 `0xC2C`（`bag-list-fill-chain-evidence.json`）
- 可视占位：独立 WORD 表 `bag+0x324`，6 列/行，空=`0xFFFF`，首格标记 `slot+0x3E8`
- 放置：`0x42F440` 用帧尺寸 `0x42F6D0` 在 cell-table 里铺 footprint

Zircon 协议 **没有** 这张 cell-table。`ClientUserItem` / `C.ItemMove` / `S.ItemMove` 只有记录槽位。服务端 `PlayerObject.Inventory = new UserItem[Globals.InventorySize]`，`InventorySize = 48`。一件装备在服务器上永远占 **1 个记录槽**。多格只是客户端把同一条记录画到相邻格子上。

### 2.3 Godot 已经写了 first-fit，但没接上

相关代码：

- `GodotClient/Controls/DXItemGrid.cs`
  - `UseLegacyFootprints`
  - `GetLegacyFootprint`：读 `LibraryFile.Inventory` + `item.Info.Image`，`ceil(size/36)`
  - `RebuildLegacyFootprints` / `GetItemForCell` / `ResolveOperationSlot` / `IsLegacyFootprintPlaceholder`
- `GodotClient/Controls/InventoryDialog.cs` `ApplyLegacyEiLayout()`：已设 `UseLegacyFootprints = true`、`ItemLibraryFile = Inventory`、可视 `6×6`、位置 `(25, 41)`
- `GodotClient/Controls/DXItemCell.cs`：`Item` / `Slot` / `IsLegacyFootprintPlaceholder` **全部依赖 `HostGrid`**

WinForms 对照：`Client/Controls/DXItemGrid.cs` 的 `CreateGrid()` 给每个 cell 赋 `HostGrid = this`。

Godot `CreateGrid()`（约 273–307 行）创建 cell 时 **没有** `HostGrid = this`。全仓库 Godot 侧没有任何地方给背包格子赋 `HostGrid`。

因此：

- `cell.Item` 走 `ItemGrid[Slot]` 一对一，不走 `GetItemForCell`
- 占位格不会被标成 placeholder，大图标会在每个格重复画，或只画在自己的 1×1 槽里
- `RefreshItemGrids()` 只 `RefreshItem()`，连 `Grid.RefreshGrid()`（会 `RebuildLegacyFootprints`）都不走

`HUD_ZIRCON_DIFF_MATRIX_2026-09-24.md` 曾用真实 Armour 截图宣称 2×3 已闭合。当前代码路径上 `HostGrid` 仍为空，实施时以源码为准，不要被那份验收矩阵带偏。若当时截图为真，之后必有回归（`CreateGrid` 重建格子时丢掉 `HostGrid`）。

### 2.4 实施要点（C）

1. `DXItemGrid.CreateGrid()` 里 `cell.HostGrid = this`（对齐 WinForms）。
2. `ItemGrid` setter 已会 `RebuildLegacyFootprints`；`FillItems` / `AddItems` / `OnItemChanged` 之后必须再走 `RefreshGrid()` 或 `ConfigureLegacyInventoryGrid()`，不能只 `RefreshItem()`。
3. 不要给 `ItemInfo` 加占用字段来「定义格子」。占用跟贴图走。
4. 拖放/使用必须走 `ResolveOperationSlot`，点衣服下半身格应对准那条记录槽，不要对准视觉格号。
5. 服务端继续 48 槽数组。一件 2×3 衣服仍只占 1 条记录。

验收：

- 木剑视觉 1×3，只画一次，点任意占用格拿到的是同一把剑
- 男布衣 2×3，同理
- 小药水仍 1×1
- 协议 `ItemMove` 的 Slot 仍是 0..47 记录槽
- `--legacy-audit` 若仍断言 `GridSize == 6×6`，占用长高后会失败，必须改审计（见工作流 D）

---

## 3. 工作流 D：右侧锁链滚动 / 「负重没满就能一直放」

### 3.1 用户问题

旧背包可以滚动：格子看起来无限，右边有锁链，拖锁链上下滚。人物负重没到顶就能一直放东西。现在滚动没了。

### 3.2 先纠正两个误解

**格子不是无限的。** EI 客户端硬上限是 **46 条物品记录**（`0x42F280` 扫描 `0x2E` 槽）。Zircon 是 **48**（`Globals.InventorySize`）。满了就不能再获得新记录，哪怕负重还有空。

**滚动存在，是因为可视占用网格比 6 行视口高。** 一件 1×3 的剑占 3 行视觉格、只占 1 条记录。46 把剑 first-fit 后远高于 6 行，所以要滚。玩家体感「格子无限」，实际是「记录有上限 + 视觉格随 footprint 变高 + 负重是另一条上限」。

负重是独立 UI：mode-0 画 `负重:%d / 总量:%d`（`0x47BDFC`，字 `0x7DA11D` / `0x7DA11F`），不是槽位数。

服务端 `CanGainItems`：

- **永远**扫 `Inventory` 空槽；没空槽返回 false
- `checkWeight==true` 时再比 `BagWeight + weight` 与 `Stats[Stat.BagWeight]`
- 多数拾取/收获走 `CanGainItems(false, …)`（只卡槽）
- NPC 购买等走 `CanGainItems(true, …)`（槽 + 负重）

所以「负重没满就能一直放」在 Zircon **本来就不成立**，在 EI 也只是因为 46 槽对休闲局通常比负重宽。不要把 `InventorySize` 改成无限去迎合这句话。

### 3.3 EI 锁链是什么

不是聊天那条 F68 锁链。

| 项目 | EI 事实 |
|---|---|
| 控件 | 共享 gauge `0x4179B0`，成员 `bag+0x278` |
| 贴图 | GameInter **F280**，16×424 |
| 位置 | `(window.x+0xF8, window.y-0xA5)` = `(248, -165)` |
| 填充/滚动值 | `[this+0x58]`，gauge max `0x5E=94` |
| 视口 | ctor arg3 = 6 行 |
| 绘制行窗 | `max(0, [this+0x58]-5) .. [this+0x58]+6`（证据写约 11 行扫描） |
| 点击 | `0x42FFD0` → F707 `0x417D00`，ratio `[+0x284]` 写回 count `[0x58]` |
| 负重文字 | 与 F280 分离，标题区 `(0x86,0x18)-(0xF0,0x26)` |

证据：`inventory-window-render-evidence.json` `paint_geometry[0]` / `[4]`；`bag-window-draw-evidence.json`；`bag-window-init-click-use-evidence.json`。

注意：同一份证据说 `[this+0x58]` 在 `.text` 里除 reset 外没有静态写点，该 EI build 里条可能画空。玩家记忆中的「拖锁链」仍对应该 gauge 的命中（ctor 存了 `PtInRect`）。实施时按「F280 是可拖的竖轨，值驱动滚动行窗」做，不要做成聊天 F68 整条锁链跟着滑块平移。

### 3.4 Godot 现在为什么滚不动

骨架已经有一半，正式路径接不上。

已有：

- `ApplyLegacyEiLayout()`：静态 F280 轨 `(248, -165)` 16×424，`MouseFilter=Ignore`
- 透明 `DXVScrollBar` 叠在同一位置，按钮 `DrawImage=false`，`VisibleSize=6`，`Change=1`
- `ConfigureLegacyInventoryGrid()`：`rows = GetLegacyRequiredRows(6)`，`GridSize=(6, rows)`，`VisibleHeight=6`，`MaxValue=rows`
- `LegacyScrollChanged` → `Grid.ScrollValue`
- `DXVScrollBar.Value` 钳位 `[MinValue, MaxValue-VisibleSize]`；`PositionBar.Enabled` 仅当 `MaxValue - MinValue > VisibleSize`

缺口（按因果顺序）：

1. **行数只算一次，而且算在空背包上。**
   `GameScene` 约 4739–4745 行：

   ```text
   ApplyLegacyCoreTestLayouts()          // GridSize 被设成 6×6
   Grid.ItemGrid = Inventory             // 此时数组还是空的
   ConfigureLegacyInventoryGrid()        // GetLegacyRequiredRows → 6
   Grid.CreateGrid()                     // 36 个格子
   ```

   真正的物品在更晚的 `InitHudData` → `FillItems(info.Items)`（约 5626）。之后 `RefreshItemGrids()` **只** `foreach InventoryCells RefreshItem()`，不调用 `ConfigureLegacyInventoryGrid()`，也不 `Grid.RefreshGrid()`。

   结果：终身 6×6、滚动范围 `MaxValue=6, VisibleSize=6` → `range=0` → 滑块 `Enabled=false`，滚轮也钳回 0。

2. **`HostGrid` 未赋值**（工作流 C）。即便补了行数重算，占用仍是 1×1，48 件最多 8 行；大件不会把网格撑高。

3. **审计把 6×6 锁死。** `AuditLegacyEiLayout` 要求 `Grid.GridSize == (6, 6)`。谁按占用把 `GridSize.Y` 拉高，`--legacy-audit` 就失败。应改成：可视 6 行、`VisibleHeight==6`、位置 `(25,41)`，**允许** `GridSize.Y >= 6`。

4. **拖柄几何在窗口外面。** 滚动条控件放在窗口相对 `(248, -165)` 以对齐 EI blit。`PositionBar` 默认相对滚动条 `y=16` → 窗口 `y=-149`。窗口若裁剪子控件，手柄点不到。F280 轨本身 `MouseFilter=Ignore`。滚轮只绑在 `_legacyScrollBar` 上；格子会 `AcceptEvent()` 吃掉滚轮，在物品上滚无效。

5. **聊天锁链代码不能复用。** `DXVScrollBar.LegacyChainTrack` 画的是 GameInter **F68**（12×154，聊天用）。背包必须继续用 F280 gauge。

6. **48 槽硬顶。** 即使滚动完美，第 49 件仍会被 `CanGainItems` 拒。不要在客户端假装无限。

历史夹具：`HUD_ZIRCON_DIFF_MATRIX` 记载曾把同一条 Gold 复制进 48 槽、first-fit 得 8 行、滚轮/下箭头/拖柄回顶通过，**临时注入已回退**。那次证明滚动控件本身能工作，前提是 Configure 时数组里已经有溢出内容。生产路径没有这个前提。

### 3.5 实施要点（D）

1. 先做工作流 C 的 `HostGrid`。
2. 在每次背包数组变化后调用 `ConfigureLegacyInventoryGrid()`（`FillItems` / `AddItems` / `OnItemChanged` / 移动成功回包）。内部已有的 `GetLegacyRequiredRows` 会按 footprint 长高。
3. `CreateGrid()` 之后重新挂 `InventoryCells = Grid.Cells`（行数变了数组长度会变）。
4. 改 `AuditLegacyEiLayout`：检查 `VisibleHeight==6` 和原点，不要检查 `GridSize==(6,6)`。
5. F280 命中：让用户能在窗口内看见的那一段竖轨上拖/点/滚。不要指望一个 16×34、出现在 `y=-149` 的透明 `PositionBar`。可考虑：
   - 把可点区域裁到窗口客户区（大约 y=0..259），Y 映射到 `Value`
   - 或给整段可见轨绑 `MouseWheel` / 点击
   - 格子区域也要把滚轮转给同一条滚动条
6. 负重条继续用现有两段文字（`负重:` 灰 + `/ 总量:` 粉）。不要把 F280 当横向 F360 负重条。F280 是竖轨。
7. 服务端 `InventorySize=48` 保持。若产品以后要「只卡负重」，那是协议+存档变更，不在本次背包 UI 修复范围。

验收：

- 登录后已有物品（超过 6 行占用）立刻可滚，不必重启窗口
- 拾取一件 1×3 武器后行数增加，锁链可拖
- 可视始终 6 行，原点 `(25,41)`，步长 36
- 滚轮在格子上和轨上都能动
- 拖可见锁链，行窗跟着变
- 48 槽满时负重未满也捡不起来（与服务器一致）
- 空背包或不足 6 行时滑块禁用，不误拖

---

## 4. 实施顺序与分工建议

```text
C1  HostGrid = this
C2  FillItems/AddItems 后 RefreshGrid
D1  物品变更后 ConfigureLegacyInventoryGrid
D2  修正 AuditLegacyEiLayout
D3  F280 可见区命中 + 格子滚轮
A1  身份表生成器 + 审核清单
A2  用 closed 行重写 db_names.json
B*  经授权后再写 System.db（数值/补缺/掉落/裁剪）
```

C 与 D 是同一批客户端改动，建议同一个 PR。A 可以完全并行，且不写库。B 必须等用户授权 + 停服写库纪律。

不要把占用写进 ItemInfo 来「顺便」解决格子。不要把 `InventorySize` 改成无限来「顺便」解决滚动。

---

## 5. 关键文件

### 内容 / 数据库

| 路径 | 用途 |
|---|---|
| `LibraryCore/SystemModels/ItemInfo.cs` | `[IsIdentity] ItemName` |
| `LibraryCore/SystemModels/MonsterInfo.cs` | `[IsIdentity] MonsterName` |
| `ServerLibrary/Envir/SEnvir.cs` | `GetItemInfo` / `GetMonsterInfo` |
| `GodotClient/Scripts/LocalizedName.cs` | 读 `db_names.json` |
| `GodotClient/translations/db_names.json` | 显示名 |
| `Mir3-Research/docs/research/mud3-dat-decoded/` | DAT 解码与对照 |
| `Mir3-Research/Tools/dbeditor/` + `DBImporter` | 唯一写库通道 |
| `LibraryCore/Globals.cs` | `InventorySize = 48` |
| `ServerLibrary/Models/PlayerObject.cs` | `CanGainItems`、`Inventory` 数组 |

### 背包 UI

| 路径 | 用途 |
|---|---|
| `GodotClient/Controls/DXItemGrid.cs` | footprint、滚动、**缺 HostGrid** |
| `GodotClient/Controls/DXItemCell.cs` | Item/Slot 依赖 HostGrid |
| `GodotClient/Controls/InventoryDialog.cs` | F250/F280、Configure、错误的 6×6 审计 |
| `GodotClient/Controls/DXVScrollBar.cs` | 钳位；`LegacyChainTrack` 是 F68 |
| `GodotClient/Scripts/GameScene.cs` | 绑定顺序、FillItems、RefreshItemGrids |
| `Client/Controls/DXItemGrid.cs` | WinForms 对照（有 HostGrid） |

### EI 证据

| 路径 | 用途 |
|---|---|
| `Mir3-Research/docs/research/ei-ui-layout/inventory-window-render-evidence.json` | F280、负重文字、行窗 |
| `Mir3-Research/docs/research/ei-ui-layout/bag-list-fill-chain-evidence.json` | 46 槽 + cell-table |
| `Mir3-Research/docs/research/ei-ui-layout/bag-grid-46-slot-evidence.json` | 46 槽几何 |
| `Mir3-Research/docs/research/mir3-map-reconstruction/bag-grid-geometry-evidence.json` | 6 列 36px `(25,41)` |
| `Mir3-Research/docs/research/mir3-map-reconstruction/bag-window-draw-evidence.json` | F707 绘制 |
| `Mir3-Research/docs/research/mir3-map-reconstruction/bag-window-init-click-use-evidence.json` | 锁链点击 |
| `Mir3-Research/docs/research/ei-ui-layout/HUD_ZIRCON_DIFF_MATRIX_2026-09-24.md` | 历史夹具（注入已回退） |

---

## 6. 验收清单（调查方回头用）

### 内容

- [ ] 身份表存在且 Uma King ≠ Zuma King
- [ ] `db_names.json` 抽检 50 经典名与 Mud3 DAT 一致
- [ ] `ItemName` / `MonsterName` 仍是英文
- [ ] 未授权写库则 `System.db` 无数值/掉落变更
- [ ] 若已写库：双库同步、round-trip、游戏内 `@make` / 打怪验证该批字段

### 背包占用

- [ ] `CreateGrid` 赋值 `HostGrid`
- [ ] 木剑 1×3、布衣 2×3、只绘制一次
- [ ] 点 footprint 任意格，操作的是同一记录槽
- [ ] 服务端仍 48 槽

### 背包滚动

- [ ] 登录已有溢出占用即可滚（不依赖空窗后再开）
- [ ] 拾取后行数增加，无需重开背包
- [ ] 可见锁链可拖，格子上滚轮有效
- [ ] 审计不再要求 `GridSize==(6,6)`
- [ ] 48 满且负重未满 → 仍捡不起来
- [ ] 未使用聊天 F68 冒充背包轨

---

## 7. 本次明确不做

- 不写 System.db / Users.db
- 不改 C#、不改 `db_names.json`
- 不把中文写入身份键
- 不把占用做成 DB 字段
- 不把背包改成无限槽
- 不把聊天 F68 锁链套到背包 F280 上

---

## 8. 实施期独立复核补注（2026-10-03）

> 本节由实施会话在动代码前对原文逐条复核后追加，只记录**与原文不一致**或**原文缺**的事实。

- **MonsterInfo 数量更正**：原文 §1.2 / §1.4 写「309」。用 `Mir3-Research/Tools/SystemDbProbe`
  直接读当前双库（`Debug/ServerCore/Database/System.db`、`Debug/Client/Data/System.db`）均为
  **怪物 434 / 物品 1078 / 魔法 174 / 地图 627 / NPC 294 / 刷新点 2475 / 任务 38**。
  旧值 309 来自更早快照的 `docs/database/views/`（`_summary.md`、`views/README.md` 同写 309/244/125），
  `mud3-dat-decoded/comparison.md` §6 标题「老版 433 → Zircon 309」沿用了它。**以 SystemDbProbe 实读为准。**
- **双库不同步**：当前 `Debug/Client/Data/System.db`（版本 2026.09.26.1）与
  `Debug/ServerCore/Database/System.db`（2026.09.26.4）md5/大小不同，虽计数相同但不是同一份。
  §1.7 第 3 条要求写库后双库同步，实施前需先确认以哪份为基线、并各自备份。
- **C/D 代码事实复核通过**：`DXItemGrid.CreateGrid()` 确实未赋 `HostGrid`（WinForms 对照在
  `Client/Controls/DXItemGrid.cs` 有赋）；`GameScene` 绑定顺序确实在空背包时算一次行数；
  `AuditLegacyEiLayout` 确实断言 `GridSize==(6,6)`；`DXVScrollBar.LegacyChainTrack` 确实是 GameInter F68。
  文中行号是近似值（实际绑定在 `GameScene.cs` 4730–4736、`FillItems` 调用在 5614）。
- **占用帧尺寸独立复核**：用 `Tools/common/zlsdk.py` 直读 `mir2ei/Data/Inventory.Zl`，
  Image 1042=16×102（木剑→1×3）、Image 940=48×98（男布衣→2×3），与 §2.2 表一致（换算未复用端口 C# 逻辑）。
- **EI 占用表与滚动条模型（实施时按此实现，勿用 anchor==cellIndex 判原点）**：
  - 占用表 `bag+0x2C4` 每格一个 WORD：`0xFFFF`=空；否则存槽号；**原点格额外 +0x3E8(1000)**。
    绘制只画 `word>=0x3E8` 的格（原点格），其余被覆盖格不重复画
    （`inventory-window-render-evidence.json` `0x0042F79C`）。
  - 绘制行窗 = `max(0,[this+0x58]-5) .. [this+0x58]+6`（约 11 行），6 列、36px 步距、原点 `(25,41)`。
  - 滚动条是 **F707 gauge**（`bag+0x278`），贴图 **GameInter 280**（16×424，竖向，
    填充区 12×218），值 `[0x58]` 范围 0..`0x5E`(94)，**可点击/拖拽**（`0x417D00`，ratio `[0x284]`），
    不是「滑块在轨上」的现代滚动条；聊天 F68 那条链 ≠ 本轨。
  - 悬停/选中高亮覆盖**整个 footprint**，不是单格。
- **`Commoner Outfit (M)` 的 Zircon `ItemInfo.Image` = 941**（帧 48×100→2×3）；
  §2.2 表里的 940 是 Mud3 `Looks`，不是当前库 Image。实现按 `ItemInfo.Image` 取帧，
  不要用 Mud3 `Looks` 直接当 Image。

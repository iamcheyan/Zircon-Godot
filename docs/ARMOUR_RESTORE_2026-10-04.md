# 衣服（Armour）恢复记录（2026-10-04）

## 背景

「经典纯净」清洗（`DATABASE_CLASSIC_PURITY_CLEANUP_PLAN.md`）把 `System.db` 里的
**全部 104 件 `ItemType.Armour`（衣服）** 一并删掉了 —— 清洗前后对比：

| ItemType | 清洗前 | 清洗后（纯净库） |
|---|---:|---:|
| Armour | 104 | **0** |
| Shield | 12 | 0 |
| Emblem | 9 | 0 |
| HorseArmour | 7 | 0 |
| Amulet | 9 | 0 |
| Poison | 2 | 0 |
| Book | 173 | 0 |
| Flower | 2 | 0 |

衣服是角色外观与防御的核心装备，缺失后：测试角色换库时身上所有衣服失效、
NPC 商店无衣可卖、任何 `Armour` 槽位都是空的。

## 本次改动

把 **104 件 Armour（男 52 / 女 52）** 从清洗前备份库恢复到当前库：

- 来源：`Debug/ServerCore/Database/Backup/pre-dbsync-20261004-094734/ServerCore_Database_System.db`
- 工具：临时 C# 工具（`LibraryCore` + MirDB `CreateNewObject()`），
  逐条复制 `ItemInfo` 标量字段 + 其 `ItemStats`（共 773 条附加属性），
  **不复制 `Drops`**（避免重新引入指向已删怪物的掉落规则）。
- 结果：`ItemInfo` 326 → **430** 条，`ItemType.Armour` 0 → **104**。

各职业分布（与清洗前一致）：

| RequiredClass | 件数 |
|---|---:|
| WarWizTao | 40 |
| Assassin | 30 |
| Warrior / Wizard / Taoist | 各 10 |
| All | 4 |

## 安装位置（4 处必须一致）

```
Debug/ServerCore/Database/System.db    ← 服务端读取
/home/tetsuya/mir2ei/Data/System.db    ← 客户端读取（Debug/Client -> mir2ei 软链）
/home/tetsuya/mir2ei/Database/System.db
/home/tetsuya/development/zircon/System.db
```

安装后 4 处 MD5 均为 **`cce452bdef99680dde760015e7cb6a51`**。

- 备份（改动前）：`Debug/ServerCore/Database/Backup/pre-armour-restore-20261004-105308/`
- 回滚：把该目录下的三份 `.db` 覆盖回对应位置即可。

## 测试角色装备情况（TestHero，Warrior / Male / Lv255）

用 `@make` 给了 **52 件男性衣服**（各职业齐全），并右键装备：

```
背包 58 件 / 负重 2350（上限 2516）
  其中 Armour 50 件：All 1、Assassin 15、Taoist 5、Warrior 5、WarWizTao 19、Wizard 5
装备栏 3 件 / 负重 24：
  War Blade          @ 武器槽
  Light Armour (M)   @ 衣服槽
  Armoured Bracer Of Ancient Kingdom @ 手镯槽
```

> 剩 2 件未进包（`Santa Outfit (M)` 等），不影响「各职业都有几套」的目标。

## ⚠️ 遗留：dbeditor 工作区尚未同步

`Tools/dbeditor/workspace/ItemInfo.json` 与 `ItemInfoStat.json` 仍是 **326 / 764 条**
的旧状态。下次执行 `sync.sh` 时，工作区会被当作期望状态写回双库，
**衣服会再次被删掉**。

要长期保留衣服，需要二选一：

1. 把本次新增的 104 件 Armour（+773 条 ItemStats）补进
   `workspace/ItemInfo.json`、`workspace/ItemInfoStat.json`，
   并把 `_baseline/` 一并更新（等价于 `SystemDbProbe --json` 重导出）；
2. 或在清洗方案里把 `Armour`（以及 Shield / Emblem / HorseArmour / Amulet /
   Poison / Book / Flower）列入白名单，重新走一次清洗流程。

（`Tools/SystemDbProbe` 在当前仓库中不存在，`sync.sh` 第 3 步会因此失败，
恢复流程前需先补上该工具或改写该步。）

## 其它仍缺失的 ItemType

`Shield`、`Emblem`、`HorseArmour`、`Amulet`、`Poison`、`Book`、`Flower` 目前
**仍为 0 条**。本次只按要求恢复衣服；如需这几类，可用同一工具按类型再跑一次：

```bash
# 工具位于 /tmp/restore（临时），参数：<旧库root> <目标库root> <ItemType>
dotnet run -- <旧库root> <目标库root> Shield
```

# zh — Zircon 中文词表

英文名 ↔ 中文名的**唯一事实源**。物品/技能/地图/怪物/NPC 全覆盖，外加从
`System.db` 派生的分类、职业、性别、等级、重量等事实字段。

## 文件

| 文件 | 是什么 | 谁读 |
|---|---|---|
| `zh-glossary.json` | 完整词表（英文名 → 中文 + 分类 + 数值） | 人查、工具消费 |
| `build_zh_glossary.py` | 生成器 | 改数据源后重跑 |
| `zh.py` | 查询 CLI | 人 / agent |
| `../ServerLibrary/chinese_alias.json` | **派生**别名表（中文名 → 英文名） | `SEnvir.LoadChineseAliases`，供 GM 命令 `@make` 按中文名发物品 |

`chinese_alias.json` 由本目录的生成器产出，**不要手工编辑** —— 下次重跑会覆盖。

## 数据源

- 译名：`mir3-website/dist/data/alignment/master.json` → `translation_baseline`
  （items 1076 / magics 174 / maps 531 / monsters 426 / npcs 255，**全条目都有中文名**）
- 分类事实：`System.db` 的 `ItemInfo` 表，经 `Mir3-Research/Tools/SystemDbProbe`
  导出。MirDB 不是 SQLite，探针会**拷副本**再读，不会碰运行中的库。

译名的 key 与 `System.db` 的 `ItemName` **完全一致**（物品 1076 条双向零缺口），
所以词表 key 可直接喂给 `SEnvir.GetItemInfo` / `GetMonsterInfo`。

## 用法

```bash
cd ClientData/zh

./zh.py 铁板甲                        # 中英模糊搜
./zh.py --en "Light Armour (M)" --full # 精确查 + 全部字段
./zh.py --cat 盔甲 --gender 男        # 按分类/性别筛
./zh.py --cat 盔甲 --class 战士 --maxlevel 30

python3 build_zh_glossary.py           # 重新生成
python3 build_zh_glossary.py --check   # 校验现有文件与数据源一致（CI 用）
```

## GM 命令用法

服务端 `SEnvir.LoadChineseAliases()` 在启动时读 `chinese_alias.json`
（按 `AppDomain.CurrentDomain.BaseDirectory` 定位，即**服务端 exe 所在目录**）。
因此：

```bash
# 改了词表/别名后，要同步到服务端目录并重启才生效
cp ServerLibrary/chinese_alias.json <ServerCore目录>/chinese_alias.json
```

之后游戏内可直接用中文名：

```
@make 铁板甲（男）
@make 金创药 500
```

物品名带空格的问题另有一层修复：`PlayerObject.SplitCommandArgs` 支持
`@make "Iron Plate Armour"` 用双引号保住内部空格。

## System.db 有多份副本

本机实测内容不同（326 / 1076 条物品都存在）。生成器按
`SYSTEM_DB_CANDIDATES` 顺序试，取**第一条能覆盖词表全部物品名**的，
并在 stderr 打印用了哪一份。全都不覆盖时退回覆盖最多者并报缺口数。
新增副本把路径追加进 `SYSTEM_DB_CANDIDATES` 即可，顺序即优先级。

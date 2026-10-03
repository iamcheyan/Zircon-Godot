#!/usr/bin/env python3
"""从 mir3-website 的 translation_baseline 生成 Zircon 完整中文词表。

源: /home/tetsuya/development/mir3-website/dist/data/alignment/master.json
    translation_baseline.{items,magics,maps,monsters,npcs}
    英文名 -> {"zh": 中文, "ja": 日文}

产出（本脚本所在目录的上级）:
    zh-glossary.json      完整词表（英文->中文 + 分类 + 数值）
    ServerLibrary/chinese_alias.json   服务端 GM 命令用的别名表（中文->英文）

两侧的对应关系已核对: System.db 1076 件物品与 baseline.items 1076 条
**双向零缺口**，故别名表可以由词表无损生成，不再需要手工补条目。

用法:
    python3 build_zh_glossary.py            # 生成
    python3 build_zh_glossary.py --check    # 只校验现有文件是否与源一致（CI 用）
"""
from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

WEBSITE_MASTER = Path(
    "/home/tetsuya/development/mir3-website/dist/data/alignment/master.json"
)

HERE = Path(__file__).resolve().parent
ZIRCON_ROOT = HERE.parent.parent
OUT_GLOSSARY = HERE / "zh-glossary.json"
OUT_ALIAS = ZIRCON_ROOT / "ServerLibrary" / "chinese_alias.json"
SYSTEM_DB_PROBE = Path(
    "/home/tetsuya/development/Mir3-Research/Tools/SystemDbProbe/"
    "bin/Debug/net10.0/SystemDbProbe.dll"
)

# System.db 有多份副本且内容不同（实测 326 / 1078 条物品都存在）。按顺序试，
# 取**第一条能覆盖词表全部物品名**的 —— 覆盖不足会让分类字段大面积留空。
# 新增副本把路径追加进来即可，顺序即优先级。
SYSTEM_DB_CANDIDATES = [
    Path("/home/tetsuya/development/Debug/ServerCore/Database/System.db"),
    Path("/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db"),
]

SCHEMA_VERSION = 1


def load_source() -> dict:
    with WEBSITE_MASTER.open(encoding="utf-8") as f:
        master = json.load(f)
    baseline = master["translation_baseline"]
    return {
        "captured_at": master.get("captured_at"),
        "baseline": baseline,
        "schema_version": master.get("schema_version"),
    }


def _probe_items(db: Path) -> dict[str, dict] | None:
    """把一份 System.db 拷进临时目录后用 SystemDbProbe 导出 ItemInfo。"""
    with tempfile.TemporaryDirectory(prefix="zhglossary-") as tmp:
        work = Path(tmp) / "db"
        work.mkdir()
        shutil.copy2(db, work / "System.db")
        out = Path(tmp) / "out.json"
        proc = subprocess.run(
            ["dotnet", str(SYSTEM_DB_PROBE), str(work), "--json", str(out)],
            capture_output=True,
            text=True,
            timeout=300,
        )
        dump = out / "ItemInfo.json"
        if proc.returncode != 0 or not dump.exists():
            return None
        with dump.open(encoding="utf-8") as f:
            rows = json.load(f)["rows"]
    return {r["ItemName"]: r for r in rows}


def load_item_facts(baseline_items: dict) -> dict[str, dict]:
    """从 System.db 取分类/职业/性别/数值等事实字段。

    MirDB 不是 SQLite，且探针必须对着**副本**跑（不能操作运行中的库）。
    逐个试 SYSTEM_DB_CANDIDATES，取第一条能覆盖词表全部物品名的库；
    全都不覆盖时退回覆盖最多的一条，并打印缺口数便于判断是词表超前还是库落后。
    探针整体缺失时降级为"只出词表、分类留空"，不阻断生成。
    """
    if not SYSTEM_DB_PROBE.exists():
        print("warn: SystemDbProbe 缺失，分类字段留空", file=sys.stderr)
        return {}

    wanted = set(baseline_items)
    best: dict[str, dict] = {}

    for db in SYSTEM_DB_CANDIDATES:
        if not db.exists():
            continue
        facts = _probe_items(db)
        if facts is None:
            print(f"warn: 探测失败 {db}", file=sys.stderr)
            continue
        missing = wanted - set(facts)
        if not missing:
            print(f"info: 词表分类取自 {db}（{len(facts)} 条，全覆盖）", file=sys.stderr)
            return facts
        print(
            f"warn: {db} 缺 {len(missing)} 条词表物品（如 {sorted(missing)[:3]}），试下一个",
            file=sys.stderr,
        )
        if len(facts) > len(best):
            best = facts

    if best:
        missing = wanted - set(best)
        print(
            f"warn: 无库全覆盖词表，退回覆盖最多者，{len(missing)} 条分类留空",
            file=sys.stderr,
        )
    return best



# ItemType / RequiredClass / RequiredGender -> 中文。
# 分类用词沿用 mir3-website data/items.json 已有的分类名，保持两边一致。
TYPE_ZH = {
    "Weapon": "武器",
    "Armour": "盔甲",
    "Helmet": "头盔",
    "Shoes": "鞋子",
    "Necklace": "项链",
    "Bracelet": "手镯",
    "Ring": "戒指",
    "Torch": "火把",
    "DarkStone": "黑暗石",
    "LightStone": "光明石",
    "Fire": "火",
    "Amulet": "护身符",
    "Consumable": "消耗品",
    "Book": "技能书",
    "Ore": "矿石",
    "Meat": "肉",
    "Nothing": "普通道具",
    "Currency": "货币",
    "ItemPart": "部件",
    "Bundle": "礼包",
}

CLASS_ZH = {
    "All": "通用",
    "Warrior": "战士",
    "Wizard": "法师",
    "Taoist": "道士",
    "Assassin": "刺客",
    "WarWizTao": "战士/法师/道士",
    "AssWar": "刺客/战士",
}

GENDER_ZH = {
    "Male": "男",
    "Female": "女",
    "None": "无限制",
    0: "无限制",
    1: "男",
    2: "女",
}



def build_items(baseline_items: dict, facts: dict) -> dict:
    items = {}
    for en, tr in sorted(baseline_items.items()):
        row = facts.get(en)
        entry = {
            "zh": tr["zh"],
            "ja": tr.get("ja", ""),
            "category": TYPE_ZH.get(row["ItemType"], row["ItemType"]) if row else "",
            "itemType": row["ItemType"] if row else "",
            "requiredClass": CLASS_ZH.get(str(row.get("RequiredClass")), "") if row else "",
            "requiredGender": GENDER_ZH.get(row.get("RequiredGender"), "") if row else "",
        }
        if row:
            entry["level"] = row.get("RequiredAmount")
            entry["weight"] = row.get("Weight")
            entry["stackSize"] = row.get("StackSize")
            entry["price"] = row.get("Price")
        items[en] = entry
    return items


def build_alias_table(baseline: dict) -> dict:
    """服务端 GM 命令别名表: 中文名 -> 英文名。

    与旧的手工版同结构（items / monsters），但由词表无损生成。
    """
    items: dict[str, str] = {}
    for en, tr in baseline["items"].items():
        zh = (tr.get("zh") or "").strip()
        if not zh:
            continue
        # 中文名冲突时保留先出现的（baseline 已按英文名排序，行为确定）。
        items.setdefault(zh, en)

    monsters: dict[str, str] = {}
    for en, tr in baseline["monsters"].items():
        zh = (tr.get("zh") or "").strip()
        if zh:
            monsters.setdefault(zh, en)

    return {"items": items, "monsters": monsters}


def render(check_only: bool) -> int:
    src = load_source()
    baseline = src["baseline"]
    facts = load_item_facts(baseline["items"])

    glossary = {
        "schemaVersion": SCHEMA_VERSION,
        "source": {
            "path": str(WEBSITE_MASTER),
            "capturedAt": src["captured_at"],
            "websiteSchemaVersion": src["schema_version"],
            "license": "mir3-website 本地数据，未随仓库分发；本文件为派生结果",
        },
        "note": (
            "英文名与 System.db 完全一致（物品 1076 条双向零缺口），故本表"
            "的 key 可直接用于 SEnvir.GetItemInfo / GetMonsterInfo 的匹配。"
            "分类字段来自 System.db 的 ItemType/RequiredClass/RequiredGender。"
        ),
        "counts": {
            "items": len(baseline["items"]),
            "magics": len(baseline["magics"]),
            "maps": len(baseline["maps"]),
            "monsters": len(baseline["monsters"]),
            "npcs": len(baseline["npcs"]),
        },
        "items": build_items(baseline["items"], facts),
        "magics": {k: v for k, v in sorted(baseline["magics"].items())},
        "maps": {k: v for k, v in sorted(baseline["maps"].items())},
        "monsters": {k: v for k, v in sorted(baseline["monsters"].items())},
        "npcs": {k: v for k, v in sorted(baseline["npcs"].items())},
    }

    alias = build_alias_table(baseline)

    if check_only:
        ok = True
        for path, obj in ((OUT_GLOSSARY, glossary), (OUT_ALIAS, alias)):
            if not path.exists():
                print(f"MISSING {path}")
                ok = False
                continue
            with path.open(encoding="utf-8") as f:
                cur = json.load(f)
            if cur == obj:
                print(f"OK      {path}")
            else:
                print(f"DRIFT   {path}")
                ok = False
        return 0 if ok else 1

    with OUT_GLOSSARY.open("w", encoding="utf-8") as f:
        json.dump(glossary, f, ensure_ascii=False, indent=2)
        f.write("\n")
    with OUT_ALIAS.open("w", encoding="utf-8") as f:
        json.dump(alias, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(f"wrote {OUT_GLOSSARY}")
    print(f"wrote {OUT_ALIAS}")
    print(
        "counts: items={counts[items]} magics={counts[magics]} maps={counts[maps]} "
        "monsters={counts[monsters]} npcs={counts[npcs]}".format(**glossary)
    )
    print(f"alias: items={len(alias['items'])} monsters={len(alias['monsters'])}")
    return 0


if __name__ == "__main__":
    sys.exit(render("--check" in sys.argv))

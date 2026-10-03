#!/usr/bin/env python3
"""zh-glossary.json 查询工具。

例:
    # 按中文名找英文名（GM 命令 @make 用这个）
    ./zh.py 铁板甲
    ./zh.py --en "Iron Plate Armour"

    # 按分类/职业/性别筛男装盔甲
    ./zh.py --cat 盔甲 --gender 男
    ./zh.py --cat 盔甲 --class 战士

    # 看某件的完整事实
    ./zh.py --en "Light Armour (M)" --full

    # 模糊搜索（中英皆可）
    ./zh.py 战甲
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
GLOSSARY = HERE / "zh-glossary.json"


def load() -> dict:
    if not GLOSSARY.exists():
        sys.exit(f"缺 {GLOSSARY}，先跑 build_zh_glossary.py 生成")
    with GLOSSARY.open(encoding="utf-8") as f:
        return json.load(f)


def main() -> int:
    ap = argparse.ArgumentParser(description="查询 Zircon 中文词表")
    ap.add_argument("term", nargs="?", help="中文名或英文名（模糊匹配）")
    ap.add_argument("--en", help="精确英文名")
    ap.add_argument("--cat", help="按分类筛，如 盔甲/武器/消耗品")
    ap.add_argument("--class", dest="cls", help="按职业筛，如 战士/法师/道士/刺客/通用")
    ap.add_argument("--gender", help="按性别筛：男/女/无限制")
    ap.add_argument("--maxlevel", type=float, help="等级上限（<=）")
    ap.add_argument("--minlevel", type=float, help="等级下限（>=）")
    ap.add_argument("--full", action="store_true", help="打印完整字段")
    ap.add_argument("--limit", type=int, default=60, help="最多显示条数")
    args = ap.parse_args()

    g = load()
    items = g["items"]

    rows: list[tuple[str, dict]] = []
    if args.en:
        if args.en in items:
            rows = [(args.en, items[args.en])]
        else:
            print(f"词表无此英文名: {args.en}")
            return 1
    else:
        term = args.term or ""
        low = term.lower()
        for en, v in items.items():
            if term and low not in en.lower() and term not in v["zh"]:
                continue
            rows.append((en, v))

    if args.cat:
        rows = [r for r in rows if r[1]["category"] == args.cat]
    if args.cls:
        rows = [r for r in rows if r[1]["requiredClass"] == args.cls]
    if args.gender:
        rows = [r for r in rows if r[1]["requiredGender"] == args.gender]
    if args.minlevel is not None:
        rows = [r for r in rows if (r[1].get("level") or 0) >= args.minlevel]
    if args.maxlevel is not None:
        rows = [r for r in rows if (r[1].get("level") or 0) <= args.maxlevel]

    if not rows:
        print("无匹配")
        return 1

    if args.full:
        for en, v in rows:
            print(f"{v['zh']}  ({en})")
            for k, val in v.items():
                if k in ("zh", "ja") or val in ("", None):
                    continue
                print(f"    {k}: {val}")
            print()
    else:
        print(f"{'中文名':<20} {'英文名':<40} {'分类':<8} {'职业':<14} {'性别':<6} 等级")
        for en, v in rows[: args.limit]:
            print(
                f"{v['zh']:<20} {en:<40} {v['category']:<8} "
                f"{v['requiredClass']:<14} {v['requiredGender']:<6} {v.get('level') or '-'}"
            )
        if len(rows) > args.limit:
            print(f"... 还有 {len(rows) - args.limit} 条（--limit 调整）")

    return 0


if __name__ == "__main__":
    sys.exit(main())

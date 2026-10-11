#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 Mud3 刷怪表（Envir3/Mon_Def/*.gen）编译成 Zircon 落库计划（JSON）。

映射链：.gen 怪物中文名 → CanonicalMonsters.cs（中文名 → Zircon MonsterInfo.Index）→ 现役 DB 的怪物。
变体名（`多钩猫0`、`僵尸1`、`森林雪人0`…）按去尾数字的基础名映射（Zircon 库内只有基础怪）。

产出:
  { "regions": [ {mapFile, x, y, range, genFile, pointsHint, respawns:[{monsterIndex,monsterName,count,delay}]} ],
    "skipped": { "noMap": [...], "noMonster": [...] }, "stats": {...} }

用法: python3 tools/mud3_spawn_plan.py [--mob /tmp/mob_graph.json] [--out /tmp/spawn_plan.json]
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO, "tools"))
from mud3_spawn_compare import parse_gens  # noqa: E402

CANON = os.path.join(REPO, "Tools", "ClassicMagicFixer", "CanonicalMonsters.cs")


def load_canonical() -> dict:
    src = open(CANON, encoding="utf-8").read()
    return {m.group(1): int(m.group(2)) for m in re.finditer(r'\["([^"]+)"\]\s*=\s*(\d+)', src)}


def base(zh: str) -> str:
    return re.sub(r"[0-9]+$", "", zh).strip()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mob", default="/tmp/mob_graph.json")
    ap.add_argument("--out", default="/tmp/spawn_plan.json")
    args = ap.parse_args()

    mob = json.load(open(args.mob, encoding="utf-8"))
    canon = load_canonical()
    byidx = {m["index"]: m for m in mob["monsters"]}
    # 地图名大小写不敏感匹配：DB 里存在 'd713' 而 Mud3 写 'D713' 这类差异（63 张图受影响）
    zfiles = {m["fileName"].lower(): m["fileName"] for m in mob["maps"]}
    rows = parse_gens()

    # 现役刷怪覆盖的图（要整图替换）
    current_maps = collections.Counter(r["mapFile"] for r in mob["respawns"])

    groups: dict[tuple, dict] = {}
    skipped = {"noMap": [], "noMonster": []}
    variant_mapped = 0
    for r in rows:
        map_file = zfiles.get(r["map"].lower())
        if map_file is None:
            skipped["noMap"].append(r)
            continue
        idx = canon.get(r["mon"])
        if idx is None:
            idx = canon.get(base(r["mon"]))
            if idx is not None:
                variant_mapped += 1
        if idx is None or idx not in byidx:
            skipped["noMonster"].append(r)
            continue
        key = (map_file, r["x"], r["y"], r["range"])
        g = groups.setdefault(key, {"mapFile": map_file, "x": r["x"], "y": r["y"],
                                    "range": r["range"], "genFile": r["gen"], "respawns": []})
        g["respawns"].append({
            "monsterIndex": idx,
            "monsterName": byidx[idx]["name"],
            "count": r["num"],
            "delay": r["delay"],
            "sourceName": r["mon"],
        })

    plan = {
        "regions": list(groups.values()),
        "replacedMaps": sorted({g["mapFile"] for g in groups.values()}),
        "skipped": {"noMap": [{"map": r["map"], "mon": r["mon"], "num": r["num"]} for r in skipped["noMap"]],
                    "noMonster": [{"map": r["map"], "mon": r["mon"], "num": r["num"]} for r in skipped["noMonster"]]},
        "stats": {
            "genRows": len(rows),
            "regions": len(groups),
            "respawns": sum(len(g["respawns"]) for g in groups.values()),
            "mapsToReplace": len({g["mapFile"] for g in groups.values()}),
            "skippedNoMap": len(skipped["noMap"]),
            "skippedNoMonster": len(skipped["noMonster"]),
            "variantMappedRows": variant_mapped,
            "currentRespawnMaps": len(current_maps),
        },
    }
    json.dump(plan, open(args.out, "w", encoding="utf-8"), ensure_ascii=False)
    s = plan["stats"]
    print(f"[spawn_plan] gen 行={s['genRows']} → 区域={s['regions']} 刷怪条目={s['respawns']} "
          f"覆盖地图={s['mapsToReplace']}；跳过(无地图)={s['skippedNoMap']} 跳过(无怪物)={s['skippedNoMonster']} "
          f"变体按基础怪映射={s['variantMappedRows']}")
    print(f"[spawn_plan] -> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

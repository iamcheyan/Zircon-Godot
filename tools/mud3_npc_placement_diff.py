#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Mud3 放置表(Merchant.txt) ↔ Zircon System.db 的 NPC 放置对照。

用途：找出"Mud3 源里有、但游戏里没放"的 NPC（用户要求把能补的都补回来）。
只读：不写任何库。

用法:
  python3 tools/mud3_npc_placement_diff.py [--dump /tmp/dbdump/npc_now.json] [--out /tmp/npc_placement_diff.json]
"""
from __future__ import annotations

import argparse
import json
import os
import re
from collections import defaultdict

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MUD3 = os.environ.get("MUD3_ROOT",
    os.path.expanduser("~/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir3"))


def load_db(dump_dir):
    def tab(name):
        with open(os.path.join(dump_dir, name + ".json"), encoding="utf-8") as f:
            return json.load(f)["rows"]
    maps = {r["Index"]: r for r in tab("MapInfo")}
    regions = {r["Index"]: r for r in tab("MapRegion")}
    npcs = tab("NPCInfo")
    out = []
    for n in npcs:
        reg = regions.get((n.get("Region") or {}).get("Index"))
        if reg is None:
            continue
        mi = maps.get((reg.get("Map") or {}).get("Index"))
        pr = reg.get("PointRegion") or {}
        out.append({
            "index": n["Index"], "name": n.get("NPCName") or "",
            "mapFile": (mi or {}).get("FileName") or "", "mapIndex": (mi or {}).get("Index"),
            "x": pr.get("CenterX"), "y": pr.get("CenterY"),
        })
    return out


def load_mud3():
    rows = []
    with open(os.path.join(MUD3, "Merchant.txt"), "rb") as f:
        for raw in f:
            line = raw.decode("cp936", errors="replace").rstrip("\r\n")
            if not line.strip() or line.lstrip().startswith(";"):
                continue
            parts = line.split()
            if len(parts) < 6:
                continue
            script, mapfile, x, y = parts[0], parts[1], parts[2], parts[3]
            name = parts[4]
            if not re.fullmatch(r"\d+", mapfile) or not re.fullmatch(r"-?\d+", x) or not re.fullmatch(r"-?\d+", y):
                continue
            rows.append({"script": script, "mapFile": mapfile, "x": int(x), "y": int(y), "name": name})
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", default="/tmp/dbdump/npc_now.json")
    ap.add_argument("--out", default="/tmp/npc_placement_diff.json")
    args = ap.parse_args()

    db = load_db(args.dump)
    mud3 = load_mud3()

    db_by_key = defaultdict(list)
    for n in db:
        db_by_key[(n["mapFile"], n["name"])].append(n)

    missing, moved, matched = [], [], 0
    for r in mud3:
        cands = db_by_key.get((r["mapFile"], r["name"]), [])
        if not cands:
            missing.append(r)
            continue
        if any(c["x"] == r["x"] and c["y"] == r["y"] for c in cands):
            matched += 1
        else:
            moved.append({**r, "db": [{"index": c["index"], "x": c["x"], "y": c["y"]} for c in cands]})

    result = {
        "mud3_rows": len(mud3), "db_npcs": len(db),
        "matched": matched, "missing_count": len(missing), "moved_count": len(moved),
        "missing": missing, "moved": moved,
    }
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)

    print(f"Mud3 放置行 {len(mud3)} / 库内 NPC {len(db)}")
    print(f"坐标一致 {matched} / 库中缺失 {len(missing)} / 坐标不同 {len(moved)}")
    by_map = defaultdict(list)
    for m in missing:
        by_map[m["mapFile"]].append(m)
    for mapfile, rows in sorted(by_map.items(), key=lambda kv: -len(kv[1]))[:12]:
        sample = ", ".join(f"{r['name']}({r['x']},{r['y']})" for r in rows[:4])
        print(f"  地图 {mapfile}: 缺 {len(rows)} 个  例: {sample}")
    print(f"明细: {args.out}")


if __name__ == "__main__":
    main()

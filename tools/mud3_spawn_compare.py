#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Mud3 刷怪表（Envir3/Mon_Def/*.gen）与 Zircon 现役 RespawnInfo 的对照审计。

用法:
  python3 tools/mud3_spawn_compare.py [--mob /tmp/mob_graph.json] [--out /tmp/spawn_compare.json]

产出:
  - 控制台摘要（按地图列出：Mud3 有而 Zircon 缺 / Zircon 多出 / 数量不符 / 怪物名解析不了）
  - JSON 明细（供后续修复脚本消费）
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MUD3 = os.environ.get(
    "MUD3_ROOT",
    "/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir3")
GEN_DIR = os.path.join(MUD3, "Mon_Def")


def read_gbk(path: str) -> str:
    with open(path, "rb") as f:
        return f.read().decode("cp936", errors="replace")


def load_monster_aliases() -> dict:
    """中文怪物名 → Zircon MonsterInfo.MonsterName。"""
    out = {}
    p = os.path.join(REPO, "Debug", "ServerCore", "chinese_alias.json")
    if os.path.exists(p):
        data = json.load(open(p, encoding="utf-8"))
        for zh, en in (data.get("monsters") or {}).items():
            out[zh] = en
    p2 = os.path.join(REPO, "GodotClient", "translations", "db_names.json")
    if os.path.exists(p2):
        data = json.load(open(p2, encoding="utf-8"))
        for en, langs in (data.get("monsters") or {}).items():
            zh = (langs or {}).get("zh")
            if zh:
                out.setdefault(zh, en)
    return out


def parse_gens() -> list[dict]:
    """解析全部 .gen：map x y mon range num time [cRatio]。"""
    rows = []
    for fn in sorted(os.listdir(GEN_DIR)):
        if not fn.lower().endswith(".gen"):
            continue
        src = read_gbk(os.path.join(GEN_DIR, fn))
        for line in src.splitlines():
            s = line.strip()
            if not s or s.startswith(";") or s.startswith("[") or s.startswith("#"):
                continue
            parts = s.split()
            if len(parts) < 6:
                continue
            mapcode, x, y = parts[0], parts[1], parts[2]
            if not re.fullmatch(r"-?\d+", x) or not re.fullmatch(r"-?\d+", y):
                continue
            mon = parts[3]
            try:
                rng = int(parts[4]); num = int(parts[5])
            except ValueError:
                continue
            delay = 0
            if len(parts) > 6:
                try:
                    delay = int(parts[6])
                except ValueError:
                    delay = 0
            rows.append({"gen": fn, "map": mapcode, "x": int(x), "y": int(y),
                         "mon": mon, "range": rng, "num": num, "delay": delay})
    return rows


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mob", default="/tmp/mob_graph.json")
    ap.add_argument("--out", default="/tmp/spawn_compare.json")
    args = ap.parse_args()

    mob = json.load(open(args.mob, encoding="utf-8"))
    alias = load_monster_aliases()
    zmon_names = {m["name"] for m in mob["monsters"]}
    zmon_names_lower = {n.lower() for n in zmon_names}

    def resolve(zh: str):
        en = alias.get(zh)
        if en and en in zmon_names:
            return en
        if zh in zmon_names:
            return zh
        if zh.lower() in zmon_names_lower:
            return next(n for n in zmon_names if n.lower() == zh.lower())
        return None

    gens = parse_gens()
    unresolved = collections.Counter()
    per_map_mud3 = collections.defaultdict(lambda: collections.Counter())
    for g in gens:
        en = resolve(g["mon"])
        if en is None:
            unresolved[g["mon"]] += 1
            continue
        per_map_mud3[g["map"]][en] += g["num"]

    per_map_z = collections.defaultdict(lambda: collections.Counter())
    for r in mob["respawns"]:
        if not r["monster"]:
            continue
        per_map_z[r["mapFile"]][r["monster"]] += r["count"]

    detail = {"maps": {}, "unresolvedMonsters": dict(unresolved),
              "mud3GenRows": len(gens), "zirconRespawns": len(mob["respawns"])}
    only_mud3_maps = []
    for mapcode in sorted(set(per_map_mud3) | set(per_map_z)):
        m3 = per_map_mud3.get(mapcode, collections.Counter())
        z = per_map_z.get(mapcode, collections.Counter())
        missing = {k: v for k, v in m3.items() if k not in z}
        extra = {k: v for k, v in z.items() if k not in m3}
        diff = {k: (m3[k], z[k]) for k in set(m3) & set(z) if m3[k] != z[k]}
        if missing or extra or diff:
            detail["maps"][mapcode] = {"missing": missing, "extra": extra, "countDiff": diff}
        if m3 and not z:
            only_mud3_maps.append(mapcode)

    json.dump(detail, open(args.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    print(f"[spawn_compare] Mud3 gen 行={len(gens)}  Zircon 刷怪条目={len(mob['respawns'])}")
    print(f"[spawn_compare] 未解析怪物名 {len(unresolved)} 种：" +
          ", ".join(f"{k}×{v}" for k, v in unresolved.most_common(20)))
    print(f"[spawn_compare] 有差异的地图 {len(detail['maps'])} 张；"
          f"Mud3 有刷怪但 Zircon 完全没有的地图 {len(only_mud3_maps)} 张")
    print("  仅 Mud3 有刷怪的地图:", ", ".join(only_mud3_maps[:40]))
    print("\n== 差异最大的 15 张图 ==")
    ranked = sorted(detail["maps"].items(),
                    key=lambda kv: -(len(kv[1]["missing"]) + len(kv[1]["extra"]) + len(kv[1]["countDiff"])))
    for mapcode, d in ranked[:15]:
        print(f"  map {mapcode}: 缺 {len(d['missing'])} 种 {list(d['missing'])[:4]} | "
              f"多 {len(d['extra'])} 种 {list(d['extra'])[:4]} | 数量不符 {len(d['countDiff'])} 种")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

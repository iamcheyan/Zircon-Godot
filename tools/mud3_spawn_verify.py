#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""刷怪核对：把服务端真值（@mobcensus 输出）与 Mud3 计划逐图逐怪比对。

用法:
  python3 tools/mud3_spawn_verify.py [--plan /tmp/spawn_plan.json] [--census /tmp/mobcensus.txt]
输出：一致 / 数量不符 / 无 census 的清单；退出码 0 = 无不一致。
"""
from __future__ import annotations

import argparse
import collections
import json

GUARDS = {"Guard", "ArcherGuard", "ForestGuard", "TownGuard", "SandGuard"}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--plan", default="/tmp/spawn_plan.json")
    ap.add_argument("--census", default="/tmp/mobcensus.txt")
    args = ap.parse_args()

    plan = json.load(open(args.plan, encoding="utf-8"))
    exp = collections.defaultdict(collections.Counter)
    for r in plan["regions"]:
        for rp in r["respawns"]:
            exp[r["mapFile"]][rp["monsterName"]] += rp["count"]

    got = {}
    for line in open(args.census, encoding="utf-8"):
        parts = line.rstrip("\n").split("\t")
        if len(parts) < 3:
            continue
        d = {}
        for kv in parts[2].split(","):
            if "=" in kv:
                k, v = kv.rsplit("=", 1)
                d[k] = int(v)
        got[parts[0]] = d

    ok = 0
    mismatch = []
    missing = []
    for m, e in sorted(exp.items()):
        g = got.get(m)
        if g is None:
            missing.append(m)
            continue
        g = {k: v for k, v in g.items() if k not in GUARDS}
        if g == dict(e):
            ok += 1
            continue
        diff = []
        for k, v in sorted(e.items()):
            if g.get(k, 0) != v:
                diff.append(f"{k}: 期望{v} 实得{g.get(k, 0)}")
        for k, v in sorted(g.items()):
            if k not in e:
                diff.append(f"{k}: 期望0 实得{v}")
        mismatch.append((m, diff))

    print(f"[spawn_verify] 计划地图={len(exp)}  census 覆盖={len(got)}  完全一致={ok}  "
          f"数量不符={len(mismatch)}  无 census={len(missing)}")
    for m, d in mismatch[:30]:
        print(f"  差异 map {m}: " + "; ".join(d[:6]))
    if missing:
        print("  无 census:", missing[:30])
    return 1 if (mismatch or missing) else 0


if __name__ == "__main__":
    raise SystemExit(main())

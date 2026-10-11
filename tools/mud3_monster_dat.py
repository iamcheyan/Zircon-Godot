#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""解码 Mud3（EI 3.0 ORIGIN / yxs）的 monster.dat 怪物表。

格式（来自 Mir3-Research/Tools/reverse-engineering/parse_mir3_dat.py 的逆向结论）：
  头部 4 字节 = 记录数；每条 252 字节，整体按单字节 0x09 异或；
  名称 = GBK ShortString @ 记录内偏移 229；+248 dword = 类型 id。

用法:
  python3 tools/mud3_monster_dat.py [--dat <path>] [--out /tmp/mud3_monsters.json] [--dump N]
"""
from __future__ import annotations

import argparse
import json
import os
import struct

DEFAULT_DAT = ("/home/tetsuya/development/Mir3-Research/local-reference-data/"
               "yxs-mud3-2026-09-25/mud3/Envir/monster.dat")
RECSZ = 252
XOR = 0x09
NAME_OFF = 229


def decode(path: str):
    raw = open(path, "rb").read()
    count = struct.unpack_from("<I", raw, 0)[0]
    body = bytes(b ^ XOR for b in raw[4:])
    recs = []
    for i in range(count):
        rec = body[i * RECSZ:(i + 1) * RECSZ]
        if len(rec) < RECSZ:
            break
        name = ""
        n = rec[NAME_OFF]
        if 0 < n <= 20:
            name = rec[NAME_OFF + 1:NAME_OFF + 1 + n].decode("cp936", errors="replace")
        type_id = struct.unpack_from("<I", rec, 248)[0]
        recs.append({"index": i, "name": name, "typeId": type_id, "raw": rec})
    return count, recs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dat", default=DEFAULT_DAT)
    ap.add_argument("--out", default="/tmp/mud3_monsters.json")
    ap.add_argument("--dump", type=int, default=0, help="打印前 N 条的字段分布")
    args = ap.parse_args()

    count, recs = decode(args.dat)
    print(f"[monster.dat] count={count} decoded={len(recs)}")
    named = [r for r in recs if r["name"]]
    print(f"[monster.dat] 有名字的记录 {len(named)}；示例：" +
          ", ".join(r["name"] for r in named[:15]))

    if args.dump:
        for r in recs[:args.dump]:
            ints = struct.unpack_from("<63I", r["raw"], 0)
            print(f"--- #{r['index']} '{r['name']}' typeId={r['typeId']}")
            print("    dwords:", " ".join(f"{v}" for v in ints[:30]))

    out = [{"index": r["index"], "name": r["name"], "typeId": r["typeId"]} for r in recs]
    json.dump(out, open(args.out, "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    print(f"[monster.dat] -> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

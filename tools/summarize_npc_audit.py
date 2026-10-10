#!/usr/bin/env python3
"""合并多轮 NPC 巡检结果并汇总（供报告使用）。

用法: python3 tools/summarize_npc_audit.py <results.jsonl> [more.jsonl ...]
输出: 每个 NPC 取最后一次记录，打印统计 + 错误清单 + Markdown 表格。
"""
from __future__ import annotations

import collections
import json
import sys


def main() -> int:
    merged: dict[int, dict] = {}
    for path in sys.argv[1:]:
        try:
            rows = [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]
        except FileNotFoundError:
            continue
        for r in rows:
            # 后跑的结果覆盖先跑的；有错误时只在“新一轮更干净”时覆盖
            prev = merged.get(r["index"])
            if prev is None or len(r.get("errors") or []) <= len(prev.get("errors") or []):
                merged[r["index"]] = r

    rows = [merged[k] for k in sorted(merged)]
    ok = [r for r in rows if not r.get("errors")]
    err = [r for r in rows if r.get("errors")]
    err_kinds = collections.Counter()
    for r in err:
        for e in set(r["errors"]):
            err_kinds[e] += 1

    pages = sum(len(r.get("pages") or []) for r in rows)
    print(f"[summarize] npcs={len(rows)} ok={len(ok)} with_errors={len(err)} "
          f"pages={pages} avg={pages / max(1, len(rows)):.1f}")
    print(f"[summarize] error kinds: {dict(err_kinds)}")
    if err:
        print("[summarize] 仍有错误的 NPC：")
        for r in err:
            print(f"   #{r['index']:>3} {r['name']} map={r.get('map')} "
                  f"({r.get('x')},{r.get('y')}) {r['errors']}")
    print("\n| NPC | 名字 | 地图 | 入口页 | 页数 | 错误 |")
    print("|---|---|---|---|---|---|")
    for r in rows:
        print(f"| #{r['index']} | {r['name']} | {r.get('map')} | {r.get('entryPage')} | "
              f"{len(r.get('pages') or [])} | {'—' if not r.get('errors') else ','.join(sorted(set(r['errors'])))} |")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

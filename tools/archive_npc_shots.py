#!/usr/bin/env python3
"""把 NPC 巡检截图归档：每个 NPC 取一张（默认第 1 张）并裁出对话框区域。

用法:
  python3 tools/archive_npc_shots.py <results.jsonl> <截图目录> [输出目录] [--full]

输出: <输出目录>/<mapFile>/<index>_<name>.png，并打印统计。
--full 时保留整屏（默认裁对话框区域，便于在仓库里长期保存且文字可读）。
"""
from __future__ import annotations

import json
import os
import re
import sys

from PIL import Image

# 客户端 1280x975 窗口内，NPC 对话框（原版 F1100/F1101/F1102 三段式）大致占据的区域
CROP = (360, 130, 900, 660)


def safe(name: str) -> str:
    return re.sub(r"[^\w\u4e00-\u9fff-]", "_", name or "npc")


def main() -> int:
    results = sys.argv[1]
    shots_dir = sys.argv[2]
    out_dir = sys.argv[3] if len(sys.argv) > 3 else "docs/screenshots/npc_audit"
    full = "--full" in sys.argv
    jpeg = "--jpeg" in sys.argv

    rows = [json.loads(l) for l in open(results, encoding="utf-8") if l.strip()]
    os.makedirs(out_dir, exist_ok=True)
    archived = 0
    total = 0
    missing = 0
    for r in rows:
        idx = r.get("index")
        shots = r.get("shots") or []
        if not shots and r.get("shot"):
            shots = [r["shot"]]
        if not shots:
            # 巡检器把截图记在每个 page 上（pages[].shot），取第一张（入口页）
            shots = [pg.get("shot") for pg in (r.get("pages") or []) if pg.get("shot")]
        if not shots:
            missing += 1
            continue
        src = shots[0]
        if not os.path.isabs(src):
            src = os.path.join(shots_dir, os.path.basename(src))
        if not os.path.exists(src):
            missing += 1
            continue
        im = Image.open(src).convert("RGB")
        if not full:
            im = im.crop(CROP)
        map_file = safe(r.get("map") or "unknown")
        sub = os.path.join(out_dir, map_file)
        os.makedirs(sub, exist_ok=True)
        ext = ".jpg" if jpeg else ".png"
        dst = os.path.join(sub, f"{idx:03d}_{safe(r.get('name', ''))}{ext}")
        if jpeg:
            im.save(dst, quality=88, optimize=True)
        else:
            im.save(dst, optimize=True)
        total += os.path.getsize(dst)
        archived += 1

    print(f"[archive_npc_shots] npcs={len(rows)} archived={archived} missing={missing} "
          f"bytes={total/1024/1024:.1f}MiB -> {out_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

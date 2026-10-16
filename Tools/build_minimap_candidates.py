#!/usr/bin/env python3
"""build_minimap_candidates.py — 为可疑的小地图绑定生成"三图对照"，供人工裁决。

背景：`MapInfo.MiniMap` 指向 `MiniMap.Zl` 的帧。当前库 frame 0..286 来自 EI
客户端（`FMMap`/`MMap`），只有 Zircon 同名 `.map` 与 EI 逐字节相同时才成立。
凡 Zircon 用了别的版本、或绑定帧号越界（旧库 537 帧时代的绑定），小地图就会
画错或空白。本脚本对每张可疑地图并排输出：

    [旧 Zircon 库对应帧]  [当前绑定帧]  [当前地图离线渲染]

人工看第三张（地图真值）与哪张艺术图一致，即可决定该地图该用哪个帧 ——
用旧库帧的话，按 `apply_zircon_minimap_overrides.py` 追加成 frame ≥ 287 的
覆盖帧再 `DbMigrationTool set-minimap`。

依赖
----
- `Tools/merge_minimap_override.py`（读 ZL2）、`sys.path` 里的 Mir3-Research
  `Tools/maps`（`zlsdk` 读旧 ZL 库、`mapviewer` 渲染地图）
- 旧库与旧 DB：`zircon-backup-20260811-095139/MiniMap-original.Zl` 与
  `System-client-original.db`（旧 DB 的 `report` 文本用来取旧绑定帧号）

用法
----
    # 旧 DB 的 MapInfo 报告（只读）
    dotnet run --project Tools/DbMigrationTool -- \
        --root /tmp/olddb report > /tmp/old_mapinfo.txt

    MAPVIEW_POOL_WORKERS=2 python3 Tools/build_minimap_candidates.py \
        --mapinfo /tmp/mapinfo.txt \
        --old-mapinfo /tmp/old_mapinfo.txt \
        --zircon-map /Users/tetsuya/mir2ei/Map \
        --minimap-zl /Users/tetsuya/mir2ei/Data/MiniMap.Zl \
        --old-minimap-zl "/Volumes/NAS/TMP/zircon-backup-20260811-095139/MiniMap-original.Zl" \
        --client-root /Users/tetsuya/mir2ei \
        --out /tmp/minimap_candidates

输出：`<out>/<地图文件名>.png` 三联图 + `<out>/INDEX.md` 表格（含建议）。
"""
from __future__ import annotations

import argparse
import os
import re
import struct
import sys
from pathlib import Path

LINE = re.compile(r"^\s*(\d+)\s+(\S+)\s+(\d+)\s+(.*)$")
EI_LAYOUT_FRAMES = 287
PANEL_W = 420


def parse_report(path: str) -> dict[str, tuple[int, str]]:
    out: dict[str, tuple[int, str]] = {}
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = LINE.match(line)
            if m:
                out[m.group(2)] = (int(m.group(3)), m.group(4).strip())
    return out


def map_dims(path: str):
    try:
        with open(path, "rb") as fh:
            head = fh.read(28)
        if len(head) < 28:
            return None
        return struct.unpack_from("<H", head, 22)[0], struct.unpack_from("<H", head, 24)[0]
    except OSError:
        return None


def zl_frame_count(path: str) -> int | None:
    try:
        with open(path, "rb") as fh:
            head = fh.read(16)
    except OSError:
        return None
    if len(head) < 11 or head[:3] != b"ZL2":
        return None
    return struct.unpack_from("<i", head, 7)[0]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mapinfo", required=True)
    ap.add_argument("--old-mapinfo", required=True)
    ap.add_argument("--zircon-map", required=True)
    ap.add_argument("--ei-map", default="/Volumes/NAS/TMP/EI传奇3.0客户端/Map")
    ap.add_argument("--minimap-zl", required=True)
    ap.add_argument("--old-minimap-zl", required=True)
    ap.add_argument("--client-root", required=True, help="含 Map/ 与 Data/Map Data/ 的客户端根目录")
    ap.add_argument("--mapviewer-dir",
                    default="/Users/tetsuya/Development/Mir3-Research/Tools/maps")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    from PIL import Image, ImageDraw
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    sys.path.insert(0, args.mapviewer_dir)
    import mapviewer  # noqa: E402
    from zlsdk import ZlLibrary  # noqa: E402

    cur = parse_report(args.mapinfo)
    old = parse_report(args.old_mapinfo)
    cur_frames = zl_frame_count(args.minimap_zl)
    old_lib = ZlLibrary(args.old_minimap_zl)
    cur_lib = ZlLibrary(args.minimap_zl)
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    cache = mapviewer.MapCache(str(Path(args.client_root) / "Map"))
    pool = mapviewer.FramePool(str(Path(args.client_root) / "Data" / "Map Data"))

    rows: list[tuple[str, int | None, int | None, str]] = []
    items = []
    for name, (mini, desc) in sorted(cur.items(), key=lambda kv: (kv[1][0], kv[0])):
        if mini <= 0:
            continue
        ei_path = Path(args.ei_map) / f"{name}.map"
        zl_path = Path(args.zircon_map) / f"{name}.map"
        if not zl_path.is_file():
            continue
        suspicious = False
        if mini >= (cur_frames or EI_LAYOUT_FRAMES):
            suspicious = True
        elif mini < EI_LAYOUT_FRAMES:
            if not ei_path.is_file():
                suspicious = True
            else:
                import hashlib
                same = (hashlib.md5(zl_path.read_bytes()).hexdigest()
                        == hashlib.md5(ei_path.read_bytes()).hexdigest())
                suspicious = not same
        if suspicious:
            items.append((name, mini, desc, zl_path))

    print(f"可疑地图 {len(items)} 张，逐张生成三联图…")
    render_cache = out_dir / "_renders"
    render_cache.mkdir(exist_ok=True)
    for n, (name, mini, desc, zl_path) in enumerate(items, 1):
        old_idx = old.get(name, (0, ""))[0] or None
        panels = []
        for label, frame in (("old Zircon frame", old_idx), ("current binding", mini)):
            img = None
            note = ""
            if frame:
                lib = old_lib if label.startswith("old") else cur_lib
                if 0 <= frame < lib.count:
                    try:
                        img = lib.decode(frame)
                    except Exception as exc:  # noqa: BLE001
                        note = f"decode failed: {exc}"
                else:
                    note = "out of range"
            else:
                note = "no old entry"
            if img is None:
                canvas = Image.new("RGB", (PANEL_W, PANEL_W), (45, 45, 45))
                ImageDraw.Draw(canvas).text((8, 8), f"{label}: frame {frame}\n{note}\n(no art -> blank minimap)",
                                            fill=(220, 180, 180))
                panels.append(canvas)
            else:
                canvas = Image.new("RGB", (PANEL_W, PANEL_W), (30, 30, 30))
                scaled = img.convert("RGBA")
                scaled.thumbnail((PANEL_W, PANEL_W - 24), Image.LANCZOS)
                bg = Image.new("RGB", scaled.size, (30, 30, 30))
                bg.paste(scaled, mask=scaled.split()[3])
                canvas.paste(bg, ((PANEL_W - bg.width) // 2, 24))
                ImageDraw.Draw(canvas).text((8, 6), f"{label}: frame {frame}  {img.size}", fill=(230, 230, 160))
                panels.append(canvas)
        # 地图渲染（真值）—— 带磁盘缓存，重跑不必重渲染
        cached = render_cache / f"{name}.png"
        try:
            if not cached.is_file():
                w, h = cache.get(f"{name}.map")[:2]
                zoom = mapviewer.map_ladder(w, h, mapviewer.LAYOUT_RECT)[-1]
                payload = mapviewer.render_full_map(cache, pool, f"{name}.map", zoom,
                                                    fmt="PNG", layout=mapviewer.LAYOUT_RECT)
                cached.write_bytes(payload)
            img = Image.open(cached).convert("RGB")
            canvas = Image.new("RGB", (PANEL_W, PANEL_W), (30, 30, 30))
            img.thumbnail((PANEL_W, PANEL_W - 24), Image.LANCZOS)
            canvas.paste(img, ((PANEL_W - img.width) // 2, 24))
            ImageDraw.Draw(canvas).text((8, 6), f"current map render (truth)  {img.size}", fill=(160, 230, 160))
            panels.append(canvas)
        except Exception as exc:  # noqa: BLE001
            canvas = Image.new("RGB", (PANEL_W, PANEL_W), (45, 45, 45))
            ImageDraw.Draw(canvas).text((8, 8), f"render failed: {exc}", fill=(220, 180, 180))
            panels.append(canvas)

        sheet = Image.new("RGB", (PANEL_W * 3 + 16, PANEL_W), (15, 15, 15))
        for i, p in enumerate(panels):
            sheet.paste(p, (i * (PANEL_W + 8), 0))
        sheet.save(out_dir / f"{name}.png")
        zd = map_dims(str(zl_path))
        rows.append((name, mini, old_idx, desc))
        print(f"  [{n}/{len(items)}] {name} ({desc}) 当前帧={mini} 旧库帧={old_idx} 尺寸={zd}")

    idx = out_dir / "INDEX.md"
    with idx.open("w", encoding="utf-8") as fh:
        fh.write("# 小地图绑定候选对照（2026-10-16）\n\n")
        fh.write("每行三联图：`<地图>.png` = [旧 Zircon 库帧] [当前绑定帧] [当前地图渲染(真值)]\n\n")
        fh.write("| 地图 | 说明 | 当前绑定帧 | 旧库帧 | 地图尺寸 | 备注 |\n|---|---|---|---|---|---|\n")
        for name, mini, old_idx, desc in rows:
            zd = map_dims(str(Path(args.zircon_map) / f"{name}.map"))
            note = "帧号越界（小地图空白）" if mini >= (cur_frames or EI_LAYOUT_FRAMES) else "与 EI 版地图不同源"
            fh.write(f"| `{name}` | {desc} | {mini} | {old_idx or '—'} | "
                     f"{zd[0]}x{zd[1]} | {note} |\n")
    print(f"\n完成 {len(rows)} 张 → {idx}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""apply_zircon_minimap_overrides.py — 在 EI 布局之外追加 Zircon 专属地图的小地图。

背景
----
`convert_ei_minimap.py` 生成的 `MiniMap.Zl` 布局是：

    frame 0      = 空白占位（DB 索引 1-based，比奇绑 FMMap f0 -> frame 1）
    frame 1-31   = EI FMMap.wil f0-30   城镇
    frame 32-286 = EI MMap.wil  f0-254  洞穴

287 帧全部被 EI 帧位占满。Zircon 专属地图（地图文件与 EI 同名图不同的，
例如 `3.map` 沙巴克城用的是 Zircon 原版地图，与 EI 沙巴克布局不同）在 EI
绑定表里没有可用帧 —— 把它的图写进任意一个 EI 帧位，都会覆盖另一张 EI 地图
正在使用的小地图。

2026-08-11 的「沙巴克混合资源修正」正是踩了这个坑：把沙巴克小地图写进
frame 7，而 frame 7 = EI `FMMap` f6，是地图 `4`（`4.map` 与 EI 同名图 md5
相同）的帧位，导致 `4.map` 的小地图显示成沙巴克。

做法
----
把这些地图的小地图**追加在 EI 布局之后**（frame 287 起），EI 布局 0-286
保持与转换脚本输出逐帧一致。追加得到的帧号由本脚本打印，供
`DbMigrationTool set-minimap <地图文件名> <帧号>` 写回 System.db。

用法
----
    python3 apply_zircon_minimap_overrides.py <base.Zl> <overrides_dir> <out.Zl>

`<overrides_dir>` 下按 `<地图文件名>.png` 命名（如 `3.png`），按文件名排序后
依次追加；可选同名 `<地图文件名>.json` 提供 `{"offsetX":0,"offsetY":0,
"shadowType":0}`，缺省为 `(0,0)` / `0`。

完整流程（先转换，再叠加）：

    PYTHONPATH=<Mir3-Research>/Tools/common \
      python3 Tools/convert_ei_minimap.py <EI Data 目录> /tmp/minimap-base.zl
    python3 Tools/apply_zircon_minimap_overrides.py \
      /tmp/minimap-base.zl Tools/minimap_overrides <输出 MiniMap.Zl>
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from merge_minimap_override import read_zl2  # noqa: E402
from zl2writer import write_zl2  # noqa: E402

# EI 布局固定占用 0..286（0 空白 + FMMap 31 + MMap 255）。
EI_LAYOUT_FRAMES = 287


def collect_overrides(overrides_dir: str) -> list[tuple[str, str]]:
    """返回 [(地图文件名, png 路径)]，按地图文件名排序。"""
    items = []
    for name in sorted(os.listdir(overrides_dir)):
        if not name.lower().endswith(".png"):
            continue
        map_name = name[:-4]
        items.append((map_name, os.path.join(overrides_dir, name)))
    return items


def apply(base_zl: str, overrides_dir: str, out_zl: str) -> dict:
    frames = read_zl2(Path(base_zl))
    if len(frames) != EI_LAYOUT_FRAMES:
        raise SystemExit(
            f"[ERR] base 帧数 {len(frames)} != EI 布局 {EI_LAYOUT_FRAMES}；"
            "请先用 convert_ei_minimap.py 生成干净的 EI 布局库"
        )

    from PIL import Image

    applied = []
    for map_name, png in collect_overrides(overrides_dir):
        meta_path = png[:-4] + ".json"
        meta = {"offsetX": 0, "offsetY": 0, "shadowType": 0}
        if os.path.exists(meta_path):
            with open(meta_path, encoding="utf-8") as fh:
                meta.update(json.load(fh))
        image = Image.open(png).convert("RGBA")
        index = len(frames)  # 追加到 EI 布局之后
        frames.append({
            "image": image,
            "offsetX": int(meta["offsetX"]),
            "offsetY": int(meta["offsetY"]),
            "shadowType": int(meta["shadowType"]),
        })
        applied.append((map_name, index, image.size))
        print(f"  map {map_name} -> frame {index}  size={image.size}  "
              f"offset=({meta['offsetX']},{meta['offsetY']})  source={png}")

    os.makedirs(os.path.dirname(os.path.abspath(out_zl)), exist_ok=True)
    stats = write_zl2(out_zl, frames)
    return {"base_frames": EI_LAYOUT_FRAMES, "total_frames": len(frames),
            "applied": applied, **stats}


def main() -> int:
    if len(sys.argv) != 4:
        print(__doc__)
        return 2
    base_zl, overrides_dir, out_zl = sys.argv[1], sys.argv[2], sys.argv[3]
    print(f"base={base_zl}\noverrides={overrides_dir}\n-> {out_zl}")
    stats = apply(base_zl, overrides_dir, out_zl)
    print(f"  frames={stats['total_frames']} "
          f"(EI 布局 {stats['base_frames']} + Zircon 追加 {len(stats['applied'])}) "
          f"payloads={stats['payload_count']} size={stats['file_size']:,} bytes")
    print()
    print("System.db 写回（客户端与服务器两份都要）：")
    for map_name, index, _ in stats["applied"]:
        print(f"  DbMigrationTool set-minimap {map_name} {index}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""check_minimap_binding.py — 检查 System.db 的小地图绑定是否与实际地图文件同源。

背景（2026-10-16 沙漠土城事故）
-------------------------------
`MiniMap.Zl` 的 frame 1-286 是 EI 客户端 `FMMap.wil`/`MMap.wil` 的转换结果，
`MapInfo.MiniMap` 由 Mud3 `Envir/MiniMap.txt` 推导。**只有当 Zircon 的同名
`.map` 与 EI 的那张逐字节相同时，这套绑定才成立**。凡是 Zircon 用了不同版本
地图的（如 `5.map` 350×350 vs EI 400×400、`3.map` 350×350 vs EI 400×600），
小地图就会画出另一张图的布局 —— 症状是"人站在沙漠里、小地图显示在城里"。

修法是取旧 Zircon 小地图库（`MiniMap-original.Zl`）里该地图的帧，追加成
frame ≥ 287 的"自建覆盖帧"，再 `DbMigrationTool set-minimap <地图> <帧号>`。

本脚本用来**批量找出还没修的那些地图**。

输入
----
1. `DbMigrationTool report` 的输出文本（含每行 `<Index> <FileName> <MiniMap> <Description>`）
   例：
     cd GodotClient && dotnet run --project ../Tools/DbMigrationTool -- --root <Data 目录> report > /tmp/mapinfo.txt
2. Zircon 客户端 Map 目录
3. EI 客户端 Map 目录（NAS 上的 `EI传奇3.0客户端/Map`）
4. （可选）部署中的 `MiniMap.Zl`，用于判定绑定是否越界（默认只按 287 分界）

判定
----
- `MiniMap == 0`                    → 未绑定（不显示小地图），跳过
- `287 <= MiniMap < 帧数`            → 自建覆盖帧（Zircon 专属艺术图），视为 OK
- `MiniMap >= 帧数`                  → **越界**（旧库 537 帧时代的绑定，EI 版库装不下 → 小地图空白，待修）
- Zircon 与 EI 的 `.map` md5 相同    → OK（EI 帧就是这张图的）
- 尺寸或内容不同                     → **待修**（尺寸不同必然错位；内容不同可能错位）

用法
----
    python3 Tools/check_minimap_binding.py /tmp/mapinfo.txt \\
        /Users/tetsuya/mir2ei/Map "/Volumes/NAS/TMP/EI传奇3.0客户端/Map" \\
        /Users/tetsuya/mir2ei/Data/MiniMap.Zl

退出码：有待修条目时为 1，否则 0。
"""
from __future__ import annotations

import hashlib
import os
import re
import struct
import sys

EI_LAYOUT_FRAMES = 287   # EI 布局占 0..286，之后的帧是 Zircon 追加覆盖帧


def zl_frame_count(path: str) -> int | None:
    """只读 ZL2 头里的 image_count（帧数）。"""
    try:
        with open(path, "rb") as fh:
            head = fh.read(16)
    except OSError:
        return None
    if len(head) < 11 or head[:3] != b"ZL2":
        return None
    return struct.unpack_from("<i", head, 7)[0]


LINE = re.compile(r"^\s*(\d+)\s+(\S+)\s+(\d+)\s+(.*)$")


def map_dims(path: str) -> tuple[int, int] | None:
    try:
        with open(path, "rb") as fh:
            head = fh.read(28)
        if len(head) < 28:
            return None
        return struct.unpack_from("<H", head, 22)[0], struct.unpack_from("<H", head, 24)[0]
    except OSError:
        return None


def md5_of(path: str) -> str:
    h = hashlib.md5()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def parse_report(text: str):
    for line in text.splitlines():
        m = LINE.match(line)
        if not m:
            continue
        idx, file_name, mini_map, desc = m.group(1), m.group(2), int(m.group(3)), m.group(4).strip()
        if file_name.isdigit() or file_name[:1].isalnum():
            yield file_name, mini_map, desc


def main(argv: list[str]) -> int:
    if len(argv) not in (4, 5):
        print(__doc__)
        return 2
    report_path, zircon_map_dir, ei_map_dir = argv[1], argv[2], argv[3]
    frames = zl_frame_count(argv[4]) if len(argv) == 5 else None
    limit = frames if frames else EI_LAYOUT_FRAMES
    rows = list(parse_report(open(report_path, encoding="utf-8", errors="replace").read()))

    ok = override = out_of_range = fix_dim = fix_content = no_ei = no_zl = 0
    pending: list[str] = []
    for file_name, mini_map, desc in rows:
        if mini_map <= 0:
            continue
        if mini_map >= limit:
            out_of_range += 1
            pending.append(f"  [帧号越界]   MiniMap={mini_map:<4} {file_name:<10} "
                           f"（库共 {limit} 帧）{desc}")
            continue
        if mini_map >= EI_LAYOUT_FRAMES:
            override += 1
            continue
        zl_path = os.path.join(zircon_map_dir, f"{file_name}.map")
        ei_path = os.path.join(ei_map_dir, f"{file_name}.map")
        if not os.path.isfile(zl_path):
            no_zl += 1
            continue
        if not os.path.isfile(ei_path):
            no_ei += 1
            pending.append(f"  [无 EI 对照] MiniMap={mini_map:<4} {file_name:<10} {desc}")
            continue
        zd, ed = map_dims(zl_path), map_dims(ei_path)
        if md5_of(zl_path) == md5_of(ei_path):
            ok += 1
            continue
        if zd != ed:
            fix_dim += 1
            pending.append(f"  [尺寸不同]   MiniMap={mini_map:<4} {file_name:<10} "
                           f"{zd[0]}x{zd[1]} vs EI {ed[0]}x{ed[1]}  {desc}")
        else:
            fix_content += 1
            pending.append(f"  [内容不同]   MiniMap={mini_map:<4} {file_name:<10} "
                           f"{zd[0]}x{zd[1]} 与 EI 同尺寸但 md5 不同  {desc}")

    print(f"报告条目 {len(rows)} 条，已绑定小地图 "
          f"{ok + override + out_of_range + fix_dim + fix_content + no_ei + no_zl} 条")
    print(f"  OK（.map 与 EI 逐字节相同）    : {ok}")
    print(f"  OK（自建覆盖帧 {EI_LAYOUT_FRAMES}..{limit - 1}）      : {override}")
    print(f"  待修 · 帧号越界（小地图空白）   : {out_of_range}")
    print(f"  待修 · 尺寸不同（必然错位）     : {fix_dim}")
    print(f"  待修 · 内容不同（可能错位）     : {fix_content}")
    print(f"  无 EI 对照                     : {no_ei}")
    print(f"  本地缺 .map                    : {no_zl}")
    if pending:
        print("\n明细：")
        for line in pending:
            print(line)
    print("\n结论：" + ("存在待修绑定，见上" if (out_of_range or fix_dim or fix_content or no_ei)
                       else "全部绑定与地图同源"))
    return 1 if (out_of_range or fix_dim or fix_content or no_ei) else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_connection_endpoints.py —— 把「端点落在不可站格」的传送点（MapRegion.PointRegion）
修到半径内的最近可站格。

来源：docs/AUDIT/maps_connections.md §4.1（审计脚本 tools/audit/maps_connections_audit.py
     产出的 .artifacts/game-audit-2026-10-18/data/maps_connections.json）。

设计约束（见 AGENTS.md「验证深度约定」）：
  * .map 解析**完全自写**，不 import 任何生产解析（mapedit / mapio / MapReader / Map.cs）。
    通行判据 `flag & 0x03 == 0x03`；格区起点 28 + (w/2)*(h/2)*3；格长 13 或 14 **按文件
    实际长度自动识别**（发行数据里有 39 张 13 字节/格的图，硬编码 14 会误判为全阻挡）。
  * 索引约定（列优先 idx = x*Height + y）用**独立的锚点数据**自检：拿库里的 NPC/刷怪/
    安全区/端点区域坐标（另一条产出流程写下的真实可站点）分别按两种约定求「命中可站格」
    的比例，列优先必须显著高于行优先，否则拒绝执行。（注意：空间连通性 coherence 对行/列
    转置不变，**不能**用来判定索引约定，只能判定步长。）
  * 只改数据（dbeditor 工作区 JSON），不改 MovementInfo、不写 System.db、不跑 sync.sh、
    不碰 ~/mir2ei、不做 git 操作、不启停服务端/客户端。默认 dry-run。

用法：
    python3 tools/restore/fix_connection_endpoints.py              # dry-run（默认）
    python3 tools/restore/fix_connection_endpoints.py --apply      # 写工作区（先备份到 /tmp）
    python3 tools/restore/fix_connection_endpoints.py --max-radius 12   # 放宽搜索半径
"""

from __future__ import annotations

import argparse
import hashlib
import json
import mmap
import os
import re
import shutil
import sys
import time
from collections import OrderedDict
from pathlib import Path

# ---------------------------------------------------------------- 默认路径

DEFAULT_ZIRCON = Path("/Users/tetsuya/Development/Zircon")
DEFAULT_AUDIT = DEFAULT_ZIRCON / ".artifacts/game-audit-2026-10-18/data/maps_connections.json"
DEFAULT_WORKSPACE = Path("/Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/workspace")
DEFAULT_MAPS_DIR = Path("/Users/tetsuya/mir2ei/Map")
DEFAULT_PREVIEW = Path("/tmp/connection_fix_preview.json")

HARD_FLAGS = ("source_blocked", "destination_blocked")     # 需要修
WARN_FLAGS = ("source_blocked_warn",)                      # 多格区域质心告警：按约定跳过

WALKABLE_MASK = 0x03
WALKABLE_VALUE = 0x03
HEADER_SIZE = 28          # 地面层起点
GROUND_CELL_BYTES = 3
VALID_STRIDES = (13, 14)


class MapParseError(RuntimeError):
    pass


class ConventionError(RuntimeError):
    pass


# ---------------------------------------------------------------- .map 解析（自写 + mmap）

class MapFile:
    """只读 .map 视图。元数据（宽高/步长/偏移）在构造时算好；字节用 mmap 惰性读。"""

    def __init__(self, path: Path):
        self.path = Path(path)
        self.size = self.path.stat().st_size
        if self.size < HEADER_SIZE + 4:
            raise MapParseError(f"{self.path.name}: 文件过短（{self.size} 字节）")
        with self.path.open("rb") as fh:
            self.width = int.from_bytes(fh.read(24)[22:24], "little")
            fh.seek(24)
            self.height = int.from_bytes(fh.read(2), "little")
        if self.width <= 0 or self.height <= 0:
            raise MapParseError(f"{self.path.name}: 头异常 w={self.width} h={self.height}")
        self.ground_len = HEADER_SIZE + (self.width // 2) * (self.height // 2) * GROUND_CELL_BYTES
        body = self.size - self.ground_len
        cells = self.width * self.height
        stride, rem = divmod(body, cells)
        if rem != 0 or stride not in VALID_STRIDES:
            raise MapParseError(
                f"{self.path.name}: 尺寸不符任一布局（w={self.width} h={self.height} len={self.size} "
                f"格区={body} → {body}/{cells}={stride} 余 {rem}；13/14 字节每格都不满足）"
            )
        self.stride = stride
        self._fh = None
        self._mm = None

    # --- 生命周期（由 MapStore 的 LRU 管理） ---

    def _open(self) -> mmap.mmap:
        if self._mm is None:
            self._fh = self.path.open("rb")
            self._mm = mmap.mmap(self._fh.fileno(), 0, access=mmap.ACCESS_READ)
        return self._mm

    def close(self) -> None:
        if self._mm is not None:
            self._mm.close()
            self._mm = None
        if self._fh is not None:
            self._fh.close()
            self._fh = None

    # --- 单元格（约定：idx = x*Height + y，flag 在格内 +0） ---

    def _byte(self, x: int, y: int, col_major: bool = True) -> int:
        idx = x * self.height + y if col_major else y * self.width + x
        return self._open()[self.ground_len + idx * self.stride]

    def flag(self, x: int, y: int) -> int:
        return self._byte(x, y)

    def in_bounds(self, x: int, y: int) -> bool:
        return 0 <= x < self.width and 0 <= y < self.height

    def walkable(self, x: int, y: int) -> bool:
        if not self.in_bounds(x, y):
            return False
        return (self._byte(x, y) & WALKABLE_MASK) == WALKABLE_VALUE

    def walkable_at(self, x: int, y: int, col_major: bool) -> bool:
        if not self.in_bounds(x, y):
            return False
        try:
            return (self._byte(x, y, col_major) & WALKABLE_MASK) == WALKABLE_VALUE
        except (ValueError, IndexError, mmap.error):
            return False

    def walkable_neighbours(self, x: int, y: int) -> int:
        n = 0
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                if self.walkable(x + dx, y + dy):
                    n += 1
        return n

    def count_walkable(self) -> int:
        return sum(1 for x in range(self.width) for y in range(self.height) if self.walkable(x, y))


class MapStore:
    """地图文件解析器 + 打开 fd 的 LRU（412 张图不会被一次性 mmap 进内存）。"""

    def __init__(self, maps_dir: Path, max_open: int = 24):
        self.maps_dir = Path(maps_dir)
        self.max_open = max_open
        self._maps: dict[str, MapFile] = {}
        self._open: OrderedDict[str, None] = OrderedDict()

    def resolve(self, want: str, hints: list[str] | None = None) -> Path | None:
        for name in [want] + [h for h in (hints or []) if h]:
            stem = name[:-4] if name.lower().endswith(".map") else name
            exact = self.maps_dir / f"{stem}.map"
            if exact.exists():
                return exact
            low = f"{stem}.map".lower()
            if self.maps_dir.is_dir():
                for cand in self.maps_dir.iterdir():
                    if cand.name.lower() == low:
                        return cand
        return None

    def get(self, map_file: str, hints: list[str] | None = None):
        """返回 (MapFile, path, error)。路径解析 → 解析元数据（缓存）→ 打开（LRU）。"""
        path = self.resolve(map_file, hints)
        if path is None:
            return None, None, f"地图文件缺失（{map_file}.map）"
        key = path.name.lower()
        mf = self._maps.get(key)
        if mf is None:
            try:
                mf = MapFile(path)
            except MapParseError as exc:
                return None, path, str(exc)
            self._maps[key] = mf
        self._touch(key)
        return mf, path, None

    def _touch(self, key: str) -> None:
        self._open.pop(key, None)
        self._open[key] = None
        while len(self._open) > self.max_open:
            old, _ = self._open.popitem(last=False)
            self._maps[old].close()

    def declared(self, key: str) -> MapFile | None:
        return self._maps.get(key)

    def all_loaded(self):
        return list(self._maps.items())

    def close(self) -> None:
        for mf in self._maps.values():
            mf.close()
        self._open.clear()


def nearest_walkable(mf: MapFile, x: int, y: int, max_radius: int | None):
    """返回 (best, candidates)：|dx|+|dy| <= max_radius 的可站格，按

        (曼哈顿距离, -8 邻域可站数, |dx|, |dy|, dx, dy)

    排序：第一项=最小位移；第二项在同距离时优先更开阔的格（避免落进 1 邻域墙缝）。
    """
    if max_radius is None:
        max_radius = max(mf.width, mf.height)
    found = []
    for dx in range(-max_radius, max_radius + 1):
        rem = max_radius - abs(dx)
        for dy in range(-rem, rem + 1):
            d = abs(dx) + abs(dy)
            if d == 0:
                continue
            nx, ny = x + dx, y + dy
            if not mf.walkable(nx, ny):
                continue
            found.append({
                "x": nx, "y": ny, "distance_manhattan": d,
                "distance_euclid": round((dx * dx + dy * dy) ** 0.5, 3),
                "dx": nx - x, "dy": ny - y,
                "walkable_neighbours": mf.walkable_neighbours(nx, ny),
            })
    if not found:
        return None, []
    found.sort(key=lambda c: (c["distance_manhattan"], -c["walkable_neighbours"],
                              abs(c["dx"]), abs(c["dy"]), c["dx"], c["dy"]))
    return found[0], found


def component_size(mf: MapFile, x: int, y: int, cap: int = 200000) -> int:
    """4 连通（上下左右）可站区域的格数——判断落点是「主活动区」还是「孤立小口袋」。

    落点若在一个几十格的孤岛里，玩家传过去出不来，比不可站更糟。
    """
    if not mf.walkable(x, y):
        return 0
    seen = {(x, y)}
    stack = [(x, y)]
    while stack and len(seen) < cap:
        cx, cy = stack.pop()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = cx + dx, cy + dy
            if (nx, ny) in seen or not mf.walkable(nx, ny):
                continue
            seen.add((nx, ny))
            stack.append((nx, ny))
    return len(seen)


# ---------------------------------------------------------------- 索引约定自检

def convention_selfcheck(store: MapStore, ws_dir: Path, sample_maps: int = 48,
                         sample_refs: int = 800) -> dict:
    """用库里的「真实可站坐标」锚点判定索引约定。

    锚点来自 NPCInfo / RespawnInfo / SafeZoneInfo / MovementInfo 引用的单点 MapRegion
    —— 这些坐标由另一条产出流程写下（NPC/刷怪/安全区必须落在可站地面），与 .map 解析彼此独立。
    正确约定下命中率应显著高于错误约定；若不然，说明索引约定理解错了，必须拒绝改动。
    """
    regions = {int(r["Index"]): r
               for r in json.load((ws_dir / "MapRegion.json").open(encoding="utf-8"))["rows"]}
    refs = []
    for table, field in (("NPCInfo.json", "Region"), ("RespawnInfo.json", "Region"),
                         ("SafeZoneInfo.json", "Region"), ("MovementInfo.json", "SourceRegion"),
                         ("MovementInfo.json", "DestinationRegion")):
        path = ws_dir / table
        if not path.exists():
            continue
        for row in json.load(path.open(encoding="utf-8"))["rows"]:
            ref = row.get(field)
            if ref:
                refs.append(int(ref["Index"]))
            if len(refs) >= sample_refs:
                break

    stats = {
        "anchors_total": 0, "maps_used": 0,
        "colmajor_hits": 0, "rowmajor_hits": 0,
        "walkable_cells": 0, "map_cells": 0,
        "col_rate": None, "row_rate": None, "walkable_base_rate": None,
        "verdict": None, "note": None,
    }
    seen_maps = set()
    per_map: dict[str, int] = {}
    per_map_cap = 8                     # 每图最多取几个锚点，保证跨图多样性
    for ridx in refs:
        row = regions.get(ridx)
        if not row:
            continue
        mapname = (row.get("Map") or {}).get("Name")
        if mapname is None:
            continue
        if per_map.get(mapname, 0) >= per_map_cap:
            continue
        if mapname not in seen_maps and len(seen_maps) >= sample_maps:
            continue
        pr = row.get("PointRegion") or {}
        if int(pr.get("PointCount", 0)) != 1:
            continue
        x, y = int(pr["CenterX"]), int(pr["CenterY"])
        mf, _p, err = store.get(mapname)
        if mf is None or not mf.in_bounds(x, y):
            continue
        if mapname not in seen_maps:
            seen_maps.add(mapname)
            stats["walkable_cells"] += mf.count_walkable()
            stats["map_cells"] += mf.width * mf.height
        per_map[mapname] = per_map.get(mapname, 0) + 1
        stats["anchors_total"] += 1
        stats["colmajor_hits"] += 1 if mf.walkable_at(x, y, True) else 0
        stats["rowmajor_hits"] += 1 if mf.walkable_at(x, y, False) else 0
    stats["maps_used"] = len(seen_maps)
    if stats["anchors_total"] < 50:
        raise ConventionError(
            f"锚点样本太少（{stats['anchors_total']}），无法验证索引约定，拒绝执行")
    stats["col_rate"] = round(stats["colmajor_hits"] / stats["anchors_total"], 4)
    stats["row_rate"] = round(stats["rowmajor_hits"] / stats["anchors_total"], 4)
    if stats["map_cells"]:
        stats["walkable_base_rate"] = round(stats["walkable_cells"] / stats["map_cells"], 4)
    if stats["col_rate"] <= stats["row_rate"] + 0.15:
        raise ConventionError(
            f"索引约定自检失败：列优先(x*H+y)命中 {stats['col_rate']} 未显著高于"
            f"行优先(y*W+x) {stats['row_rate']}（{stats['anchors_total']} 个锚点）→ 拒绝改动坐标")
    stats["verdict"] = "col-major x*Height+y 通过"
    stats["note"] = ("锚点=NPC/刷怪/安全区/传送端点区域的单点坐标（另一条产出流程写下，"
                     "预期落在可站地面）；walkable_base_rate 是这些图的平均可站格占比，"
                     "即错误约定下的期望命中率")
    return stats


def stride_selfcheck(store: MapStore, targets: list[tuple[str, int, int]]) -> list[dict]:
    """对涉及的地图做一次步长对照（13 vs 14）——错误步长下同一坐标的可站性会翻转。"""
    out = []
    for map_file, x, y in targets:
        mf, path, err = store.get(map_file)
        if mf is None:
            out.append({"map": map_file, "error": err})
            continue
        out.append({
            "map": path.name, "dims": [mf.width, mf.height], "stride": mf.stride,
            "file_size": mf.size,
            "size_matches_13": mf.size == (HEADER_SIZE + (mf.width // 2) * (mf.height // 2)
                                           * GROUND_CELL_BYTES) + mf.width * mf.height * 13,
            "size_matches_14": mf.size == (HEADER_SIZE + (mf.width // 2) * (mf.height // 2)
                                           * GROUND_CELL_BYTES) + mf.width * mf.height * 14,
            "walkable_cells": mf.count_walkable(),
            "probe": {"x": x, "y": y, "flag": mf.flag(x, y) if mf.in_bounds(x, y) else None,
                      "walkable": mf.walkable(x, y)},
        })
    return out


# ---------------------------------------------------------------- 工作区写入（定点文本替换）

def find_row_spans(text: str) -> dict[int, tuple[int, int]]:
    """用 raw_decode 扫出 "rows" 数组里每个元素的字符区间（键 = Index）。"""
    key = text.index('"rows"')
    pos = text.index("[", key) + 1
    dec = json.JSONDecoder()
    spans: dict[int, tuple[int, int]] = {}
    n = len(text)
    while True:
        while pos < n and text[pos] in " \t\r\n,":
            pos += 1
        if pos >= n or text[pos] == "]":
            break
        obj, end = dec.raw_decode(text, pos)
        if isinstance(obj, dict) and "Index" in obj:
            spans[int(obj["Index"])] = (pos, end)
        pos = end
    return spans


def splice_point_region(text: str, span: tuple[int, int], new_x: int, new_y: int) -> str:
    """在指定的行区间内替换 PointRegion 的 CenterX/CenterY（其余字节一律不动）。"""
    start, end = span
    chunk = text[start:end]
    pre = chunk.index('"PointRegion"')
    tail = chunk[pre:]
    m = re.search(r'("PointCount"\s*:\s*\d+\s*,\s*"CenterX"\s*:\s*)(-?\d+)'
                  r'(\s*,\s*"CenterY"\s*:\s*)(-?\d+)', tail)
    if not m:
        raise RuntimeError("行内 PointRegion 不符合质心摘要格式，拒绝文本替换")
    new_tail = tail[:m.start()] + m.group(1) + str(new_x) + m.group(3) + str(new_y) + tail[m.end():]
    return text[:start] + chunk[:pre] + new_tail + text[end:]


def apply_edits(ws_file: Path, edits: list[dict], backup_root: Path) -> dict:
    original_text = ws_file.read_text(encoding="utf-8")
    before = json.loads(original_text)
    spans = find_row_spans(original_text)
    text = original_text
    for e in sorted(edits, key=lambda e: spans[e["region_index"]][0], reverse=True):
        idx = e["region_index"]
        if idx not in spans:
            raise RuntimeError(f"找不到行 Index={idx} 的字符区间")
        text = splice_point_region(text, spans[idx], e["new_x"], e["new_y"])

    after = json.loads(text)                                  # 语法校验
    if after.get("count") != before.get("count"):
        raise RuntimeError("count 字段变化，中止写入")
    want = {e["region_index"]: (e["new_x"], e["new_y"]) for e in edits}
    changed_rows = []
    for b, a in zip(before["rows"], after["rows"]):
        if b == a:
            continue
        if b["Index"] not in want:
            raise RuntimeError(f"意外的行改动：Index={b['Index']}")
        changed_rows.append(b["Index"])
        pa = a["PointRegion"]
        if (pa["CenterX"], pa["CenterY"]) != want[b["Index"]]:
            raise RuntimeError(f"Index={b['Index']} 目标坐标不符：{pa}")
        pb_expected = dict(b["PointRegion"])
        pb_expected["CenterX"], pb_expected["CenterY"] = want[b["Index"]]
        if pb_expected != pa:
            raise RuntimeError(f"Index={b['Index']} 的 PointRegion 结构被意外改动：{pb_expected} -> {pa}")
        if {k: v for k, v in b.items() if k != "PointRegion"} != \
                {k: v for k, v in a.items() if k != "PointRegion"}:
            raise RuntimeError(f"Index={b['Index']} 出现了 PointRegion 之外的改动，中止")
    if sorted(changed_rows) != sorted(want):
        raise RuntimeError(f"应改 {sorted(want)} 行，实际改动 {sorted(changed_rows)} 行")

    backup_root.mkdir(parents=True, exist_ok=True)
    backup_file = backup_root / ws_file.name
    shutil.copy2(ws_file, backup_file)
    ws_file.write_text(text, encoding="utf-8")
    return {
        "backup_file": str(backup_file),
        "backup_md5": md5_of(backup_file),
        "written_md5": md5_of(ws_file),
        "changed_rows": sorted(changed_rows),
        "bytes_before": len(original_text), "bytes_after": len(text),
    }


def md5_of(path: Path) -> str:
    return hashlib.md5(Path(path).read_bytes()).hexdigest()


# ---------------------------------------------------------------- CLI

def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="把不可站的传送端点（MapRegion.PointRegion）修到最近可站格（默认 dry-run）",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter)
    p.add_argument("--apply", action="store_true", help="写入 dbeditor 工作区（先备份到 /tmp）")
    p.add_argument("--audit", type=Path, default=DEFAULT_AUDIT, help="审计 JSON（maps_connections.json）")
    p.add_argument("--workspace", type=Path, default=DEFAULT_WORKSPACE, help="dbeditor workspace 目录")
    p.add_argument("--maps-dir", type=Path, default=DEFAULT_MAPS_DIR, help=".map 目录（只读）")
    p.add_argument("--preview", type=Path, default=DEFAULT_PREVIEW, help="预览 JSON 输出路径")
    p.add_argument("--max-radius", type=int, default=8,
                   help="环形搜索半径（曼哈顿距离上限）；超出即标为需人工")
    p.add_argument("--max-open-maps", type=int, default=24, help="同时 mmap 的地图数上限（fd 保护）")
    p.add_argument("--backup-dir", type=Path, default=None,
                   help="备份目录（默认 /tmp/dbeditor_ws_backup_connfix_<时间戳>）")
    p.add_argument("--only", default=None, help="只处理指定 connection Index（逗号分隔）")
    p.add_argument("--quiet", action="store_true", help="只输出摘要")
    return p


# ---------------------------------------------------------------- 主流程

def main(argv=None) -> int:
    args = build_parser().parse_args(argv)

    audit_path: Path = args.audit
    ws_dir: Path = args.workspace
    maps_dir: Path = args.maps_dir
    ws_region_file = ws_dir / "MapRegion.json"
    for pth, what in ((audit_path, "审计 JSON"), (ws_region_file, "工作区 MapRegion.json"),
                      (maps_dir, "地图目录")):
        if not pth.exists():
            print(f"[X] 找不到{what}：{pth}", file=sys.stderr)
            return 2

    audit = json.load(audit_path.open(encoding="utf-8"))
    ws_region = json.load(ws_region_file.open(encoding="utf-8"))
    ws_rows = {int(r["Index"]): r for r in ws_region["rows"]}

    store = MapStore(maps_dir, max_open=args.max_open_maps)
    only = {int(x) for x in args.only.replace(" ", "").split(",") if x} if args.only else None

    # ---------- 0) 索引约定自检（独立锚点） ----------
    try:
        conv = convention_selfcheck(store, ws_dir)
    except ConventionError as exc:
        print(f"[X] {exc}", file=sys.stderr)
        store.close()
        return 3

    # ---------- 1) 收集审计中的可疑端点 ----------
    fixes, manuals, skipped, pending = [], [], [], []
    map_by_file: dict[str, MapFile] = {}
    suspicious = audit["connections"]["suspicious"]

    for entry in suspicious:
        mv = int(entry["movement_index"])
        if only and mv not in only:
            continue
        flags = set(entry.get("flags") or [])
        for role in ("source", "destination"):
            sev = entry[role]
            region_index = int(sev["region_index"])
            map_file = sev["map_file"]
            old_x, old_y = int(sev["x"]), int(sev["y"])
            hard = f"{role}_blocked" in flags
            warn = f"{role}_blocked_warn" in flags
            if not hard and not warn:
                continue

            base = {
                "movement_index": mv,
                "movement_identity": entry["identity"],
                "endpoint_role": role,
                "region_index": region_index,
                "region_identity": (ws_rows.get(region_index) or {}).get("_Identity"),
                "map_file": map_file,
                "old_x": old_x, "old_y": old_y,
                "audit_point_count": sev.get("point_count"),
                "audit_walkable": sev.get("walkable"),
                "audit_severity": sev.get("severity"),
                "audit_nearest": sev.get("nearest_walkable"),
                "flags": sorted(flags),
            }
            if warn and not hard:
                base["status"] = "skipped_multi_point_warn"
                base["reason"] = ("审计为多格区域质心告警（PointCount>1：质心被挡不代表区域不可用）；"
                                  "按任务约定不改多点区域")
                skipped.append(base)
                continue

            # ---- 独立复核 1：工作区行 / 区域形状 / 连接引用 ----
            row = ws_rows.get(region_index)
            if row is None:
                base["status"] = "manual_region_missing"
                base["reason"] = f"工作区 MapRegion 没有 Index={region_index}"
                manuals.append(base)
                continue
            hints = [f"{(row.get('Map') or {}).get('Name', '')}.map"]
            prows = row.get("PointRegion")
            if prows is None:
                base["status"] = "manual_no_point_region"
                base["reason"] = "工作区该区域没有 PointRegion（可能只有 BitRegion），不碰"
                manuals.append(base)
                continue
            if int(prows.get("PointCount", 0)) != 1:
                base["status"] = "manual_multi_point"
                base["reason"] = (f"PointCount={prows.get('PointCount')}（多点区域）："
                                  "改质心会要求整体平移，超出本任务范围（审计也只给了 warn 级）")
                manuals.append(base)
                continue
            if (int(prows["CenterX"]), int(prows["CenterY"])) != (old_x, old_y):
                # 工作区坐标已被改过：若是「已修到可站格」，属幂等命中，不是异常
                wsx, wsy = int(prows["CenterX"]), int(prows["CenterY"])
                mf_chk, _p, _e = store.get(map_file, hints)
                if mf_chk is not None and mf_chk.in_bounds(wsx, wsy) and mf_chk.walkable(wsx, wsy):
                    base["status"] = "already_fixed_in_workspace"
                    base["reason"] = (f"工作区已是 ({wsx},{wsy})（可站）≠ 审计旧坐标 ({old_x},{old_y})"
                                      "：这是**已修/已被别处修过**的幂等命中，本脚本不再改动")
                    base["ws_current"] = [wsx, wsy]
                    skipped.append(base)
                    continue
                base["status"] = "manual_ws_audit_mismatch"
                base["reason"] = (f"工作区坐标 ({wsx},{wsy}) ≠ 审计坐标 ({old_x},{old_y})，"
                                  "且新坐标不可站 → 情况不明，先对齐再谈修复")
                manuals.append(base)
                continue
            ref_field = "SourceMovements" if role == "source" else "DestinationMovements"
            refs = [int(x["Index"]) for x in (row.get(ref_field) or []) if "Index" in x]
            base["movement_refs_ok"] = mv in refs
            if refs and mv not in refs:
                base["status"] = "manual_movement_ref_mismatch"
                base["reason"] = (f"工作区该区域的 {ref_field} 不含连接 #{mv}（={refs}）"
                                  "→ 审计与工作区引用不一致，交人工")
                manuals.append(base)
                continue

            # ---- 独立复核 2：自己解析地图，确认它确实不可站 ----
            mf, mpath, err = store.get(map_file, hints)
            if mf is None:
                base["status"] = "manual_map_missing" if mpath is None else "manual_map_parse_error"
                base["reason"] = err
                manuals.append(base)
                continue
            base["map_file_resolved"] = str(mpath)
            base["map_dims"] = [mf.width, mf.height]
            base["map_stride"] = mf.stride
            if not mf.in_bounds(old_x, old_y):
                base["status"] = "manual_out_of_bounds"
                base["reason"] = f"({old_x},{old_y}) 越界（图 {mf.width}x{mf.height}）"
                manuals.append(base)
                continue
            base["my_walkable"] = mf.walkable(old_x, old_y)
            base["my_flag"] = mf.flag(old_x, old_y)
            base["my_walkable_neighbours"] = mf.walkable_neighbours(old_x, old_y)
            if base["my_walkable"]:
                base["status"] = "already_walkable"
                base["reason"] = ("独立解析判定该格可站（flag&3==3）→ 无需修改"
                                  "（重复运行时即幂等命中）")
                skipped.append(base)
                continue

            # ---- 候选格 ----
            best, cands = nearest_walkable(mf, old_x, old_y, args.max_radius)
            far, _ = nearest_walkable(mf, old_x, old_y, None)
            base["nearest_any_distance"] = far
            if best is None:
                base["status"] = "manual_no_walkable_in_radius"
                base["reason"] = (f"半径 {args.max_radius} 内没有可站格；全图最近可站格 "
                                  f"{far and (far['x'], far['y'])}（曼哈顿 "
                                  f"{far and far['distance_manhattan']}）→ 位移过大，交人工判断")
                if far:
                    base["nearest_any_component_cells"] = component_size(mf, far["x"], far["y"])
                    base["walkable_cells_total"] = mf.count_walkable()
                manuals.append(base)
                continue

            nm = re.search(r"@\((\d+)\s*,\s*(\d+)\)", str(row.get("_Identity") or ""))
            name_coord = None
            if nm:
                nx, ny = int(nm.group(1)), int(nm.group(2))
                inb = mf.in_bounds(nx, ny)
                name_coord = {
                    "x": nx, "y": ny, "in_bounds": inb,
                    "walkable": bool(inb and mf.walkable(nx, ny)),
                    "manhattan_from_old": abs(nx - old_x) + abs(ny - old_y),
                    "usable": bool(role == "source" and inb and mf.walkable(nx, ny)),
                    "role_hint": ("源侧：名称坐标通常就是本图门位（审计 §4.2，1558 个源侧区域里 1521 个与质心一致）"
                                  if role == "source" else
                                  "落点侧：名称坐标指的是**源地图**的门口，不能用于本图判定（审计 §4.2）"),
                }
            map_by_file[map_file] = mf
            pending.append(dict(base, status="fix", candidates=cands,
                                name_coord_alternative=name_coord))

    # ---------- 2) 选点：同图坐标占用冲突规避 + 保持原有共格关系 ----------
    occupied: dict[str, dict[tuple[int, int], list[int]]] = {}
    for r in ws_region["rows"]:
        pr = r.get("PointRegion")
        if not pr or int(pr.get("PointCount", 0)) != 1:
            continue
        mname = (r.get("Map") or {}).get("Name")
        occupied.setdefault(mname, {}).setdefault(
            (int(pr["CenterX"]), int(pr["CenterY"])), []).append(int(r["Index"]))
    vacated = {p["region_index"] for p in pending}    # 这些行会离开旧坐标 → 旧格自动腾空

    groups: dict[tuple, list] = {}
    for p in pending:
        groups.setdefault((p["map_file"], p["old_x"], p["old_y"]), []).append(p)
    picked: dict[str, set] = {}
    pick_report = []
    for key in sorted(groups, key=lambda k: (k[0], k[2], k[1])):
        members = groups[key]
        map_name, old_x, old_y = key
        taken = picked.setdefault(map_name, set())
        cand_list = members[0]["candidates"]
        chosen, avoided = None, []
        for cand in cand_list:
            cell = (cand["x"], cand["y"])
            others = [i for i in occupied.get(map_name, {}).get(cell, []) if i not in vacated]
            if others or cell in taken:
                avoided.append({"cell": list(cell), "distance_manhattan": cand["distance_manhattan"],
                                "occupied_by_regions": others, "already_assigned": cell in taken})
                continue
            chosen = cand
            break
        if chosen is None:
            for m in members:
                m["status"] = "manual_no_free_walkable_in_radius"
                m["reason"] = (f"半径 {args.max_radius} 内所有可站格都被其它区域占用或已被本次分配："
                               f"{avoided} → 交人工，避免门口堆叠")
                m.pop("candidates", None)
                manuals.append(m)
            pick_report.append({"map": map_name, "old_cell": [old_x, old_y], "picked": None,
                                "avoided": avoided})
            continue
        new_cell = (chosen["x"], chosen["y"])
        taken.add(new_cell)
        pick_report.append({"map": map_name, "old_cell": [old_x, old_y],
                            "picked": list(new_cell), "avoided": avoided,
                            "regions": [m["region_index"] for m in members]})
        mf = map_by_file[map_name]
        for m in members:
            m.pop("candidates", None)
            m.update({
                "new_x": chosen["x"], "new_y": chosen["y"],
                "displacement_manhattan": chosen["distance_manhattan"],
                "displacement_euclid": chosen["distance_euclid"],
                "delta": [chosen["dx"], chosen["dy"]],
                "new_walkable_neighbours": chosen["walkable_neighbours"],
                "new_flag": mf.flag(chosen["x"], chosen["y"]),
                "new_cell_component_cells": component_size(mf, chosen["x"], chosen["y"]),
                "walkable_cells_total": mf.count_walkable(),
                "old_cell_walkable_neighbours": m["my_walkable_neighbours"],
                "same_distance_candidates": [
                    c for c in cand_list if c["distance_manhattan"] == chosen["distance_manhattan"]],
                "collision_avoided": avoided,
                "shared_old_cell_group": [x["region_index"] for x in members],
                "evidence": (f"自写解析器：旧格 flag={m['my_flag']}（&3!=3 不可站）→ 新格 "
                             f"flag={chosen and mf.flag(chosen['x'], chosen['y'])}（&3==3 可站）；"
                             f"曼哈顿位移 {chosen['distance_manhattan']}；"
                             f"新格 8 邻域可站 {chosen['walkable_neighbours']}/8"),
            })
            if chosen["distance_manhattan"] <= 2 and chosen["walkable_neighbours"] >= 4 and not avoided:
                m["risk"] = "low"
            elif chosen["distance_manhattan"] <= 4 and chosen["walkable_neighbours"] >= 2:
                m["risk"] = "medium"
            else:
                m["risk"] = "high"
            if m["new_cell_component_cells"] < 50:      # 孤立小口袋：传过去出不来
                m["risk"] = "high"
                m["risk_note"] = ((m.get("risk_note", "") + " ").strip() +
                                  f"落点所在连通区只有 {m['new_cell_component_cells']} 格（孤立口袋）")
            if avoided:
                if m["risk"] == "low":
                    m["risk"] = "medium"
                m["risk_note"] = (f"最近格已被占用，退让到 {new_cell}（避免的候选：{avoided}）")
            fixes.append(m)

    # ---------- 3) 全局独立复核：3514 个端点逐条与审计对比 ----------
    verify = {"checked": 0, "agree": 0, "mismatch": []}
    if only is None:
        for rec in audit["connections"]["records"]:
            for role in ("source", "destination"):
                sev = rec[role]
                if sev.get("walkable") is None or not sev.get("map_file_present"):
                    continue
                mf, _p, err = store.get(sev["map_file"])
                if mf is None:
                    verify["mismatch"].append({"movement_index": rec["movement_index"],
                                               "role": role, "reason": err})
                    continue
                verify["checked"] += 1
                mine = mf.walkable(int(sev["x"]), int(sev["y"]))
                if mine == bool(sev["walkable"]):
                    verify["agree"] += 1
                else:
                    verify["mismatch"].append({
                        "movement_index": rec["movement_index"], "role": role,
                        "region_index": sev["region_index"], "map": sev["map_file"],
                        "xy": [sev["x"], sev["y"]], "audit_walkable": sev["walkable"],
                        "my_walkable": mine, "my_flag": mf.flag(int(sev["x"]), int(sev["y"])),
                    })

    # 步长对照（涉及的地图）
    stride_checks = stride_selfcheck(store, sorted({(f["map_file"], f["old_x"], f["old_y"])
                                                     for f in fixes + manuals + skipped}))

    summary = {
        "fix_count": len(fixes),
        "manual_count": len(manuals),
        "skipped_count": len(skipped),
        "fix_risk": {k: sum(1 for f in fixes if f.get("risk") == k) for k in ("low", "medium", "high")},
        "endpoint_verify": {"checked": verify["checked"], "agree": verify["agree"],
                            "mismatch": len(verify["mismatch"])},
    }

    preview = {
        "generated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "mode": "apply" if args.apply else "dry-run",
        "inputs": {
            "audit": str(audit_path), "audit_md5": md5_of(audit_path),
            "workspace_region_file": str(ws_region_file),
            "workspace_region_md5": md5_of(ws_region_file),
            "maps_dir": str(maps_dir),
        },
        "conventions": {
            "walkable": "(flag & 0x03) == 0x03",
            "layout": "地面层 28 + (w/2)*(h/2)*3；格区每格 13|14 字节（按文件长度自动识别，flag@+0）",
            "cell_index": "idx = x*Height + y（列优先）",
            "search": f"半径（曼哈顿）<= {args.max_radius}；同距离按 8 邻域可站数 → |dx|,|dy|,dx,dy 排序",
            "scope": "只改 MapRegion.PointRegion 单点区域；MovementInfo 不动",
            "no_new_overlap": "新坐标不得与未腾空的既有区域同格；同一旧格的多条连接保持共格",
        },
        "convention_selfcheck": conv,
        "stride_checks": stride_checks,
        "pick_report": pick_report,
        "summary": summary,
        "fixes": fixes,
        "manual": manuals,
        "skipped": skipped,
        "endpoint_verification": verify,
        "apply_result": None,
    }
    args.preview.write_text(json.dumps(preview, ensure_ascii=False, indent=2), encoding="utf-8")

    # ---------- 4) --apply ----------
    if args.apply and fixes:
        stamp = time.strftime("%Y%m%d-%H%M%S")
        backup_root = Path(args.backup_dir) if args.backup_dir else \
            Path(f"/tmp/dbeditor_ws_backup_connfix_{stamp}")
        res = apply_edits(ws_region_file, fixes, backup_root)
        res["backup_dir"] = str(backup_root)
        (backup_root / "manifest.json").write_text(json.dumps({
            "generated_at": preview["generated_at"],
            "original_file": str(ws_region_file),
            "original_md5": preview["inputs"]["workspace_region_md5"],
            "written_md5": res["written_md5"],
            "changed_rows": res["changed_rows"],
            "edits": [{"region_index": f["region_index"], "movement_index": f["movement_index"],
                       "role": f["endpoint_role"], "map": f["map_file"],
                       "old": [f["old_x"], f["old_y"]], "new": [f["new_x"], f["new_y"]],
                       "displacement_manhattan": f["displacement_manhattan"],
                       "risk": f.get("risk")} for f in fixes],
        }, ensure_ascii=False, indent=2), encoding="utf-8")
        preview["apply_result"] = res
        args.preview.write_text(json.dumps(preview, ensure_ascii=False, indent=2), encoding="utf-8")

    # ---------- 5) 控制台报告 ----------
    print(f"=== fix_connection_endpoints [{'APPLY' if args.apply else 'DRY-RUN'}] "
          f"{preview['generated_at']} ===")
    print(f"输入：审计 {audit_path}")
    print(f"      工作区 {ws_region_file}  md5={preview['inputs']['workspace_region_md5']}")
    print(f"索引约定自检：{conv['verdict']} —— 锚点 {conv['anchors_total']} 个 / {conv['maps_used']} 张图，"
          f"列优先命中 {conv['col_rate']} vs 行优先 {conv['row_rate']}")
    print(f"端点复核：{verify['checked']} 个端点与审计逐条比对，一致 {verify['agree']}，"
          f"不一致 {len(verify['mismatch'])}")
    for m in verify["mismatch"][:10]:
        print(f"   !! #{m.get('movement_index')} {m.get('role')} {m.get('map')} {m.get('xy')} "
              f"审计={m.get('audit_walkable')} 本脚本={m.get('my_walkable')} {m.get('reason', '')}")
    print()
    if not args.quiet:
        print(f"{'连接':>6} {'角色':<11} {'区域':>6} {'地图':<8} {'旧坐标':<11} {'新坐标':<11} "
              f"{'位移':>4} {'邻域':>4} {'连通区':>7} {'风险':<6} 备注")
        for f in fixes:
            note = f.get("risk_note", "")
            if f.get("name_coord_alternative") and f["name_coord_alternative"].get("usable"):
                nc = f["name_coord_alternative"]
                note = (note + " " if note else "") + \
                    f"[注：区域名门位 ({nc['x']},{nc['y']}) 也可站，位移 {nc['manhattan_from_old']}]"
            print(f"{f['movement_index']:>6} {f['endpoint_role']:<11} {f['region_index']:>6} "
                  f"{f['map_file']:<8} {str((f['old_x'], f['old_y'])):<11} "
                  f"{str((f['new_x'], f['new_y'])):<11} {f['displacement_manhattan']:>4} "
                  f"{f['new_walkable_neighbours']:>4} {f.get('new_cell_component_cells', 0):>7} "
                  f"{f.get('risk', ''):<6} {note}")
    print()
    if manuals:
        print("— 需人工（本脚本不动）—")
        for m in manuals:
            print(f"  #{m['movement_index']} {m['endpoint_role']:<11} {m['map_file']:<8} "
                  f"({m['old_x']},{m['old_y']}) [{m['status']}] {m['reason']}")
    if skipped:
        print("— 跳过 —")
        for m in skipped:
            extra = f" 工作区现值={m['ws_current']}" if m.get("ws_current") else ""
            print(f"  #{m['movement_index']} {m['endpoint_role']:<11} {m['map_file']:<8} "
                  f"({m['old_x']},{m['old_y']}) [{m['status']}] {m['reason']}{extra}")
    print()
    print(f"合计：可修 {summary['fix_count']}（低/中/高风险 {summary['fix_risk']['low']}/"
          f"{summary['fix_risk']['medium']}/{summary['fix_risk']['high']}），需人工 "
          f"{summary['manual_count']}，跳过 {summary['skipped_count']}")
    print(f"预览 JSON：{args.preview}")
    if args.apply:
        if preview["apply_result"]:
            print(f"已写入工作区；备份 → {preview['apply_result']['backup_dir']}")
            print(f"改动行 {preview['apply_result']['changed_rows']}，"
                  f"新 md5={preview['apply_result']['written_md5']}")
            print("下一步（本脚本不执行）：bash "
                  "/Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/sync.sh")
        else:
            print("无可修项，工作区未改动。")
    else:
        print("（dry-run，工作区未改动；加 --apply 才写入）")
    store.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())

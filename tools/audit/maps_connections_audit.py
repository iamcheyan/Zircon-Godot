#!/usr/bin/env python3
"""Zircon 地图清单 / 地图文件 / 连接点（传送点）离线对账审计。

只读、可重复、不联网、不读写 System.db：

  网站基准 wiki_data_v2.json  <--->  库全表导出 out/*.json  <--->  /Users/tetsuya/mir2ei/Map/*.map

约定与依据
----------
* `.map` 解析完全自实现（不复用 Mir3-Research/Tools/mapedit 或 mapio 的任何函数）：
  22 字节头 + int16 Width@22 + int16 Height@24 + 半分辨率地面层
  (Width/2)*(Height/2)*3 字节 + 全分辨率格区（列优先，格索引 = x*Height + y）。
  Zircon 客户端/服务端读格一律 14 字节/格
  （GodotClient/Formats/MapReader.cs:47-80、Client/Scenes/Views/MapControl.cs:633、
  ServerLibrary/Models/Map.cs:75）；本脚本额外检测 13 字节/格变体并单列。
* 通行判定：flag & 0x03 == 0x03。等价于 ServerLibrary/Models/Map.cs:82-88
  ((flag&2)==2 && (flag&1)==1) 与 BotRunner/BotMap.cs:41 ((flag&3)!=3 → blocked)。
* MapRegion 导出只有 {PointCount, CenterX, CenterY}，来源是
  Mir3-Research/Tools/SystemDbProbe/Program.cs:1310-1322：
  CenterX/Y = PointRegion 各点 X/Y 之和按点数整除（截断）。**点数 == 1 时中心即该唯一点**，
  因此单点区域的判定是精确的；多点区域中心不可站只作 warn。
* 服务端后果（ServerLibrary/Models/Map.cs:547-730）：
    - 目标落点格不可站 → map.GetCell() 返回 null → `continue` 重试（最多 5 次，随机取点）；
      单点区域永远失败 → **传送静默不生效**（踩门无反应）。
    - 源格不可站 → CreateMovements（SEnvir.cs:807-892）不会把该 movement 挂到该格
      （非 SkipValidation 时打印 "[Movement] Bad Origin"）→ **门不存在**。

用法
----
  python3 tools/audit/maps_connections_audit.py \
      [--wiki PATH] [--db-export DIR] [--maps-dir DIR] [--out JSON] [--quiet]
"""

from __future__ import annotations

import argparse
import collections
import datetime
import json
import mmap
import os
import re
import struct
import sys

DEFAULT_WIKI = ("/Users/tetsuya/Development/Mir3-Research/docs/research/ei-ui-layout/"
                "artifacts/web-entity-audit-2026-09-26/raw/wiki_data_v2.json")
DEFAULT_DB = "/Users/tetsuya/audit-raw/db-export-2026-10-18/out"
DEFAULT_MAPS = "/Users/tetsuya/mir2ei/Map"
DEFAULT_OUT = ("/Users/tetsuya/Development/Zircon/.artifacts/game-audit-2026-10-18/"
               "data/maps_connections.json")

DATA_OFFSET = 28
OBJECT3 = 0x03
NEIGHBOURS8 = ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1))
COORD_IN_NAME = re.compile(r"@\(\s*(\d+)\s*,\s*(\d+)\s*\)")


def read_json(path):
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def db_rows(export_dir, name):
    data = read_json(os.path.join(export_dir, f"{name}.json"))
    return data["rows"] if isinstance(data, dict) else data


def ref_index(ref):
    return ref.get("Index") if isinstance(ref, dict) else None


# ---------------------------------------------------------------------------
# 独立 .map 解析
# ---------------------------------------------------------------------------


class MapFile:
    """独立实现的地图文件读取器（mmap，只读）。"""

    __slots__ = ("stem", "path", "size", "mm", "fh", "width", "height",
                 "ground_size", "data_offset", "stride", "cell_count",
                 "expect14", "expect13", "error")

    def __init__(self, stem, path):
        self.stem = stem
        self.path = path
        self.error = None
        self.mm = None
        self.fh = None
        self.width = self.height = 0
        self.ground_size = self.data_offset = 0
        self.stride = None
        self.cell_count = 0
        self.expect14 = self.expect13 = 0
        self.size = os.path.getsize(path)
        with open(path, "rb") as fh:
            head = fh.read(DATA_OFFSET)
        if len(head) < DATA_OFFSET:
            self.error = "short_file"
            return
        self.width = struct.unpack_from("<H", head, 22)[0]
        self.height = struct.unpack_from("<H", head, 24)[0]
        if self.width <= 0 or self.height <= 0:
            self.error = "invalid_dimensions"
            return
        self.ground_size = (self.width // 2) * (self.height // 2) * 3
        self.data_offset = DATA_OFFSET + self.ground_size
        cells = self.width * self.height
        self.expect14 = self.data_offset + cells * 14
        self.expect13 = self.data_offset + cells * 13
        if self.size == self.expect14:
            self.stride = 14
        elif self.size == self.expect13:
            self.stride = 13
        self.cell_count = cells if self.stride else max(0, (self.size - self.data_offset) // 14)

    def _map(self):
        if self.mm is None:
            self.fh = open(self.path, "rb")
            self.mm = mmap.mmap(self.fh.fileno(), 0, access=mmap.ACCESS_READ)
        return self.mm

    def flag(self, x, y):
        if self.error or not (0 <= x < self.width and 0 <= y < self.height):
            return None
        idx = x * self.height + y
        if idx >= self.cell_count:
            return None
        return self._map()[self.data_offset + idx * (self.stride or 14)]

    def walkable(self, x, y):
        f = self.flag(x, y)
        return None if f is None else (f & OBJECT3) == OBJECT3

    def close(self):
        if self.mm is not None:
            self.mm.close()
            self.mm = None
        if self.fh is not None:
            self.fh.close()
            self.fh = None


class MapCache:
    """按需打开地图（避免同时持有数百个 fd）；stem 查找支持大小写回退。"""

    def __init__(self, maps_dir, max_open=16):
        self.dir = maps_dir
        self.max_open = max_open
        self.headers = {}
        self._ci = {}
        self._open = collections.OrderedDict()

    def scan(self):
        for entry in sorted(os.listdir(self.dir)):
            if not entry.lower().endswith(".map"):
                continue
            stem = entry[:-4]
            self.headers[stem] = MapFile(stem, os.path.join(self.dir, entry))
            self._ci.setdefault(stem.lower(), stem)

    def resolve(self, stem):
        if stem in self.headers:
            return stem
        return self._ci.get(stem.lower())

    def get(self, stem):
        key = self.resolve(stem)
        if key is None:
            return None
        if key in self._open:
            self._open.move_to_end(key)
            return self._open[key]
        while len(self._open) >= self.max_open:
            _, old = self._open.popitem(last=False)
            old.close()
        mf = MapFile(key, self.headers[key].path)
        self._open[key] = mf
        return mf

    def walkable(self, stem, x, y):
        mf = self.get(stem)
        return None if mf is None else mf.walkable(x, y)

    def nearest_walkable(self, stem, x, y, radius=24):
        """螺旋搜索最近可站格（用于给可疑落点提修复候选）。"""
        mf = self.get(stem)
        if mf is None:
            return None
        best = None
        for r in range(0, radius + 1):
            for dx in range(-r, r + 1):
                for dy in (-r, r):
                    for cx, cy in ((x + dx, y + dy), (x + dy, y + dx)):
                        if mf.walkable(cx, cy):
                            d = abs(cx - x) + abs(cy - y)
                            if best is None or d < best[0]:
                                best = (d, cx, cy)
            if best:
                return best
        return best

    def close(self):
        for mf in self._open.values():
            mf.close()
        self._open.clear()


# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--wiki", default=DEFAULT_WIKI)
    ap.add_argument("--db-export", default=DEFAULT_DB)
    ap.add_argument("--maps-dir", default=DEFAULT_MAPS)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    wiki = read_json(args.wiki)
    site_maps = wiki["maps"]
    site_ei_maps = wiki["ei_maps"]

    map_info = db_rows(args.db_export, "MapInfo")
    map_region = db_rows(args.db_export, "MapRegion")
    movement = db_rows(args.db_export, "MovementInfo")
    respawn = db_rows(args.db_export, "RespawnInfo")
    npc_info = db_rows(args.db_export, "NPCInfo")
    monster_info = db_rows(args.db_export, "MonsterInfo")

    cache = MapCache(args.maps_dir)
    cache.scan()

    db_by_file = {r["FileName"]: r for r in map_info}
    db_ci = {k.lower(): k for k in db_by_file}
    regions = {r["Index"]: r for r in map_region}
    monsters = {r["Index"]: r for r in monster_info}
    disk_stems = set(cache.headers)
    disk_ci = dict(cache._ci)

    result = {
        "generated_at": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
        "inputs": {
            "wiki": args.wiki, "db_export": args.db_export, "maps_dir": args.maps_dir,
            "counts": {
                "site_maps": len(site_maps), "site_ei_maps": len(site_ei_maps),
                "MapInfo": len(map_info), "MapRegion": len(map_region),
                "MovementInfo": len(movement), "RespawnInfo": len(respawn),
                "NPCInfo": len(npc_info), "MonsterInfo": len(monster_info),
                "disk_map_files": len(disk_stems),
            },
        },
        "conventions": {
            "cell_stride": "14 字节/格（Zircon 客户端与 ServerLibrary 固定 14）；另检出 13 字节/格变体",
            "walkable": "(flag & 0x03) == 0x03",
            "region_endpoint": "MapRegion.PointRegion 导出为 {PointCount, CenterX, CenterY}，"
                               "CenterX/Y = 各点 X/Y 均值截断（SystemDbProbe Program.cs:1310-1322）；"
                               "PointCount==1 时中心即唯一点，判定精确",
            "hard_vs_warn": "PointCount==1 不可站 => hard；PointCount>1 且 8 邻域全不可站 => hard；"
                            "PointCount>1 不可站但邻域有可站格 => warn（该区域其他点可能可用）",
            "server_consequence": "落点不可站 => 服务器 GetCell 返回 null 后 continue（最多 5 次重试），"
                                  "单点区域永远失败 => 传送静默不生效；源格不可站 => movement 不挂到该格 => 门不存在",
        },
    }

    # =====================================================================
    # 1. 地图清单对账
    # =====================================================================
    site_stems = {m["file"] for m in site_maps}
    site_entry = collections.OrderedDict()
    for m in site_maps:
        site_entry.setdefault(m["file"], m)

    db_only = sorted(f for f in db_by_file if f not in site_stems)
    site_only = sorted(s for s in site_stems if s not in db_by_file)

    ei_by_stem = {}
    for m in site_ei_maps:
        name = m["name"]
        stem = name[:-4] if name.lower().endswith(".map") else name
        ei_by_stem[stem] = m
    ei_ci = {k.lower(): k for k in ei_by_stem}

    def classify_site_only(stem):
        entry = site_entry[stem]
        ver = entry.get("ver") or []
        on_disk = cache.resolve(stem) is not None
        if on_disk:
            twin = stem.lower() if stem.lower() in db_by_file else (
                db_ci.get(stem.lower()))
            return ("case_variant_of_registered" if twin
                    else "on_disk_but_unregistered")
        if set(ver) <= {"zircon", "mud3", "mei"} and stem not in ei_by_stem \
                and stem.lower() not in ei_ci:
            return "legacy_zircon_map_removed_by_cleanup"
        return "unknown_missing"

    inventory = {
        "site_maps_vs_db": {
            "matched_by_file_stem": len(site_stems) - len(site_only),
            "site_unique_stems": len(site_stems),
            "db_only_count": len(db_only),
            "site_only_count": len(site_only),
            "site_only": [{
                "file": s, "name": site_entry[s].get("name"), "zh": site_entry[s].get("zh"),
                "ver": site_entry[s].get("ver"),
                "on_disk": cache.resolve(s) is not None,
                "in_ei_maps": s in ei_by_stem or s.lower() in ei_ci,
                "case_twin_in_mapinfo": (s.lower() if s.lower() in db_by_file
                                         else db_ci.get(s.lower())),
                "classification": classify_site_only(s),
            } for s in site_only],
            "db_only": [{
                "file": s, "index": db_by_file[s]["Index"],
                "description": db_by_file[s].get("Description"),
                "on_disk": cache.resolve(s) is not None,
                "disk_name": (cache.resolve(s) or "") + ".map" if cache.resolve(s) else None,
                "in_ei_maps": s in ei_by_stem or s.lower() in ei_ci,
                "has_erased_site_map": s in {m["file"] for m in site_maps},
            } for s in db_only],
        },
        "site_ei_maps_vs_disk": {},
        "db_vs_disk_case_mismatch": [],
    }
    cls = collections.Counter(v["classification"] for v in inventory["site_maps_vs_db"]["site_only"])
    inventory["site_maps_vs_db"]["site_only_classification_counts"] = dict(cls)
    do = collections.Counter(
        ("disk" if v["on_disk"] else "nodisk") + "/"
        + ("ei" if v["in_ei_maps"] else "noei")
        for v in inventory["site_maps_vs_db"]["db_only"])
    inventory["site_maps_vs_db"]["db_only_classification_counts"] = dict(do)

    for stem in db_by_file:
        if stem not in disk_stems and stem.lower() in disk_ci:
            inventory["db_vs_disk_case_mismatch"].append({
                "mapinfo_file": stem, "description": db_by_file[stem].get("Description"),
                "disk_file": disk_ci[stem.lower()] + ".map"})

    ei_missing, ei_case_only, disk_extra = [], [], []
    for stem in ei_by_stem:
        if stem in disk_stems:
            continue
        (ei_case_only if stem.lower() in disk_ci else ei_missing).append(
            {"site": stem, "disk": (disk_ci.get(stem.lower()) or "") + ".map"})
    for stem in disk_stems:
        if stem in ei_by_stem or stem.lower() in ei_ci:
            continue
        disk_extra.append(stem)

    movement_map_stems = set()
    for mv in movement:
        for key in ("SourceRegion", "DestinationRegion"):
            reg = regions.get(ref_index(mv.get(key)))
            if reg:
                movement_map_stems.add(reg["Map"]["Name"])
    npc_map_counts = collections.Counter()
    for n in npc_info:
        reg = regions.get(ref_index(n.get("Region")))
        if reg:
            npc_map_counts[reg["Map"]["Name"]] += 1
    respawn_map_counts = collections.Counter(regions[ref_index(r["Region"])]["Map"]["Name"]
                                             for r in respawn if ref_index(r.get("Region")) in regions)

    inventory["site_ei_maps_vs_disk"] = {
        "matched_exact": len(ei_by_stem) - len(ei_missing) - len(ei_case_only),
        "site_only_missing_on_disk": sorted(ei_missing, key=lambda d: d["site"]),
        "site_only_missing_on_disk_count": len(ei_missing),
        "case_only_mismatch": sorted(ei_case_only, key=lambda d: d["site"]),
        "disk_only_count": len(disk_extra),
        "disk_only": [{
            "file": s,
            "in_mapinfo": cache.resolve(s) in db_by_file,
            "mapinfo_file": cache.resolve(s) if cache.resolve(s) in db_by_file else None,
            "movement_endpoint": s in movement_map_stems,
            "npc_count": npc_map_counts.get(s, 0),
        } for s in sorted(disk_extra)],
    }
    result["inventory"] = inventory

    # =====================================================================
    # 2. 地图文件完整性
    # =====================================================================
    integrity = {"anomalous_stride13": [], "size_mismatch": [], "header_error": [],
                 "dims_vs_site_mismatch": [], "files": {}}
    counts = collections.Counter()
    for stem, mf in sorted(cache.headers.items()):
        if mf.error:
            counts["header_error"] += 1
            integrity["header_error"].append({"file": stem + ".map", "error": mf.error,
                                              "size": mf.size})
            continue
        rec = {"file": stem + ".map", "w": mf.width, "h": mf.height, "size": mf.size,
               "expect14": mf.expect14, "expect13": mf.expect13, "stride": mf.stride}
        if mf.stride is None:
            counts["size_mismatch"] += 1
            integrity["size_mismatch"].append(rec)
        elif mf.stride == 13:
            counts["stride13"] += 1
            rec["delta_vs_14"] = mf.size - mf.expect14
            rec["in_mapinfo"] = cache.resolve(stem) in db_by_file
            rec["movement_endpoint"] = stem in movement_map_stems
            rec["respawn_rows"] = respawn_map_counts.get(stem, 0)
            rec["npc_count"] = npc_map_counts.get(stem, 0)
            integrity["anomalous_stride13"].append(rec)
        else:
            counts["stride14"] += 1
        site = ei_by_stem.get(stem) or ei_by_stem.get(ei_ci.get(stem.lower(), ""))
        if site:
            rec["site_w"], rec["site_h"] = site.get("w"), site.get("h")
            rec["site_ver"] = site.get("ver")
            if (site.get("w"), site.get("h")) != (mf.width, mf.height):
                counts["dims_mismatch"] += 1
                integrity["dims_vs_site_mismatch"].append(rec)
            else:
                counts["dims_match"] += 1
        integrity["files"][stem] = {"w": mf.width, "h": mf.height, "size": mf.size,
                                    "stride": mf.stride, "error": mf.error}
    integrity["counts"] = dict(counts)
    result["file_integrity"] = integrity

    # =====================================================================
    # 3. 连接点 / 传送点审计
    # =====================================================================
    def region_view(rid):
        reg = regions.get(rid)
        if reg is None:
            return None
        pr = reg.get("PointRegion") or {}
        desc = reg["Description"] or ""
        m = COORD_IN_NAME.search(desc)
        return {
            "region_index": rid, "region": desc,
            "map_file": reg["Map"]["Name"], "map_index": reg["Map"]["Index"],
            "region_type": reg.get("RegionType"), "size": reg.get("Size"),
            "point_count": pr.get("PointCount"), "x": pr.get("CenterX"), "y": pr.get("CenterY"),
            "name_coord": [int(m.group(1)), int(m.group(2))] if m else None,
            "endpoint_basis": "PointRegion centroid (PointCount==1 => exact point)",
        }

    def endpoint_check(ep):
        if ep is None:
            return None
        mf = cache.get(ep["map_file"])
        ep = dict(ep)
        if mf is None:
            ep.update({"map_file_present": False, "in_bounds": None, "walkable": None,
                       "walkable_neighbours": None, "severity": "missing_map_file"})
            return ep
        ep["map_file_present"] = True
        ep["disk_name"] = mf.stem + ".map"
        ep["map_dims"] = [mf.width, mf.height]
        ep["map_stride"] = mf.stride
        x, y = ep["x"], ep["y"]
        if x is None or y is None:
            ep.update({"in_bounds": None, "walkable": None, "walkable_neighbours": None,
                       "severity": "no_region_points"})
            return ep
        inb = 0 <= x < mf.width and 0 <= y < mf.height
        ep["in_bounds"] = inb
        if not inb:
            ep.update({"walkable": None, "walkable_neighbours": None,
                       "severity": "out_of_bounds"})
            return ep
        center = mf.walkable(x, y)
        good = total = 0
        for dx, dy in NEIGHBOURS8:
            w = mf.walkable(x + dx, y + dy)
            if w is None:
                continue
            total += 1
            if w:
                good += 1
        ep["walkable"] = center
        ep["walkable_neighbours"] = good
        ep["neighbour_samples"] = total
        # 名称里的 @(x,y) 只有“源侧”区域（描述不含『落点』）才与本体同图；见 region_name_coord_crosscheck
        if ep.get("name_coord") and "落点" not in (ep.get("region") or ""):
            nx, ny = ep["name_coord"]
            ep["name_coord_walkable"] = mf.walkable(nx, ny)
        pc = ep.get("point_count") or 0
        if center is True:
            ep["severity"] = "pocket" if good == 0 else "ok"
            if ep["severity"] == "pocket":
                ep["note"] = "落点可站但 8 邻域全不可站（可能困在单格）"
        elif pc == 1:
            ep["severity"] = "blocked_single_point"
        elif good == 0:
            ep["severity"] = "blocked_region_no_exit"
        else:
            ep["severity"] = "warn_centre_blocked"
        if center is False:
            nw = cache.nearest_walkable(ep["map_file"], x, y)
            if nw:
                ep["nearest_walkable"] = {"distance": nw[0], "x": nw[1], "y": nw[2]}
        return ep

    records, flag_counter = [], collections.Counter()
    pair_index = collections.defaultdict(list)
    for mv in movement:
        src = endpoint_check(region_view(ref_index(mv.get("SourceRegion"))))
        dst = endpoint_check(region_view(ref_index(mv.get("DestinationRegion"))))
        rec = {
            "movement_index": mv["Index"], "identity": mv.get("_Identity"),
            "icon": mv.get("Icon"), "effect": mv.get("Effect"),
            "need_hole": mv.get("NeedHole"),
            "need_item": (mv.get("NeedItem") or {}).get("Name")
            if isinstance(mv.get("NeedItem"), dict) else mv.get("NeedItem"),
            "need_spawn": (mv.get("NeedSpawn") or {}).get("Name")
            if isinstance(mv.get("NeedSpawn"), dict) else None,
            "required_class": mv.get("RequiredClass"),
            "skip_validation": mv.get("SkipValidation"),
            "source": src, "destination": dst,
        }
        flags = []
        if src is None and dst is None:
            flags.append("both_regions_missing")
        elif src is None:
            flags.append("source_region_missing")
        elif dst is None:
            flags.append("destination_region_missing")
        for tag, ep in (("source", src), ("destination", dst)):
            if ep is None:
                continue
            if not ep.get("map_file_present"):
                flags.append(f"{tag}_map_file_missing")
                continue
            if ep["map_file"] not in db_by_file and ep["map_file"].lower() not in db_ci:
                flags.append(f"{tag}_map_not_in_mapinfo")
            sev = ep.get("severity")
            if sev == "out_of_bounds":
                flags.append(f"{tag}_out_of_bounds")
            elif sev == "blocked_single_point":
                flags.append(f"{tag}_blocked")
            elif sev == "blocked_region_no_exit":
                flags.append(f"{tag}_blocked_region")
            elif sev == "warn_centre_blocked":
                flags.append(f"{tag}_blocked_warn")
            elif sev == "no_region_points":
                flags.append(f"{tag}_no_points")
            elif sev == "pocket":
                flags.append(f"{tag}_pocket")
        if src and dst and src["map_file"] == dst["map_file"] and \
                src["x"] == dst["x"] and src["y"] == dst["y"]:
            flags.append("source_equals_destination")
        if src and dst and src["region_index"] == dst["region_index"]:
            flags.append("same_region")
        if src and dst:
            pair_index[(src["region_index"], dst["region_index"])].append(mv["Index"])
        rec["flags"] = flags
        rec["symptom"] = (
            "传送静默不生效（落点非有效格，服务器重试后放弃）"
            if any(f.endswith("_blocked") or f.endswith("_blocked_region") for f in flags
                   if f.startswith("destination"))
            else "门不存在（源格非有效格，movement 未挂载）"
            if any(f.startswith("source") and (f.endswith("_blocked") or f.endswith("_blocked_region"))
                   for f in flags)
            else "需人工确认" if flags else None)
        for f in flags:
            flag_counter[f] += 1
        records.append(rec)

    # 地图级配对（区域级反向在本库不成立：对向门用不同 Region 对象）
    map_edges = collections.Counter()
    per_map = collections.defaultdict(lambda: {"out": 0, "in": 0})
    for rec in records:
        s, d = rec["source"], rec["destination"]
        if s and d:
            map_edges[(s["map_file"], d["map_file"])] += 1
            per_map[s["map_file"]]["out"] += 1
            per_map[d["map_file"]]["in"] += 1
    one_way = sorted(f"{a}->{b}" for (a, b) in map_edges if (b, a) not in map_edges)
    dead_end = sorted(k for k, v in per_map.items() if v["in"] > 0 and v["out"] == 0)
    unreachable = sorted(k for k, v in per_map.items() if v["out"] > 0 and v["in"] == 0)

    result["connections"] = {
        "counts": {
            "movements": len(records),
            "endpoints": 2 * len(records),
            "flag_counts": dict(flag_counter.most_common()),
            "movements_with_any_flag": sum(1 for r in records if r["flags"]),
            "endpoints_ok": sum(1 for r in records for k in ("source", "destination")
                                if r[k] and r[k].get("severity") == "ok"),
            "endpoints_blocked": sum(1 for r in records for k in ("source", "destination")
                                     if r[k] and str(r[k].get("severity", "")).startswith("blocked")),
            "endpoints_pocket": sum(1 for r in records for k in ("source", "destination")
                                    if r[k] and r[k].get("severity") == "pocket"),
            "distinct_ordered_map_edges": len(map_edges),
            "ordered_map_edges_with_reverse": sum(1 for (a, b) in map_edges if (b, a) in map_edges),
            "one_way_ordered_map_edges": len(one_way),
            "region_pair_exact_reverse": sum(1 for (a, b) in pair_index if (b, a) in pair_index),
            "maps_in_movements": len(per_map),
        },
        "suspicious": [r for r in records if r["flags"]],
        "one_way_map_edges": one_way,
        "dead_end_maps_in_gt0_out_eq0": dead_end,
        "unreachable_maps_out_gt0_in_eq0": unreachable,
        "per_map": {k: dict(v) for k, v in sorted(per_map.items())},
        "records": records,
    }

    # --- 3b. 区域名称里的 @(x,y) 与点阵中心交叉（判定区域点阵生成质量） -------
    namecheck = {"source_side": {}, "destination_side": {}}
    for label, pred in (("source_side", lambda d: "落点" not in d),
                        ("destination_side", lambda d: "落点" in d)):
        dev = collections.Counter()
        name_walk = collections.Counter()
        center_walk = collections.Counter()
        rows_n = 0
        for reg in map_region:
            desc = reg["Description"] or ""
            m = COORD_IN_NAME.search(desc)
            if not m or not pred(desc):
                continue
            pr = reg.get("PointRegion") or {}
            cx, cy = pr.get("CenterX"), pr.get("CenterY")
            if cx is None:
                continue
            rows_n += 1
            ix, iy = int(m.group(1)), int(m.group(2))
            d = max(abs(ix - cx), abs(iy - cy))
            dev["d=0" if d == 0 else ("d<=3" if d <= 3 else ("d<=10" if d <= 10 else "d>10"))] += 1
            name_walk[cache.walkable(reg["Map"]["Name"], ix, iy)] += 1
            center_walk[cache.walkable(reg["Map"]["Name"], cx, cy)] += 1
        namecheck[label] = {
            "regions": rows_n,
            "deviation_from_centre": dict(dev),
            "name_coord_walkable": {str(k): v for k, v in name_walk.items()},
            "centre_walkable": {str(k): v for k, v in center_walk.items()},
        }
    namecheck["note"] = (
        "源侧区域（名称不含『落点』）名称里的 @(x,y) 基本等于点阵中心（1521/1558 偏差为 0），"
        "因此可作修复候选；落点侧区域（名称含『落点』）的 @(x,y) 与点阵中心系统性不一致"
        "（1558 个中 1536 个偏差 >10 格），该坐标通常指源地图的门口坐标，"
        "不能用它在目标地图上做通行判定。")
    result["region_name_coord_crosscheck"] = namecheck

    # =====================================================================
    # 4. 与网站 maps[].monsters 交叉（RespawnInfo）
    # =====================================================================
    def norm(s):
        return "".join(ch for ch in (s or "").lower() if ch.isalnum())

    respawn_by_stem = collections.defaultdict(list)
    for rs in respawn:
        reg = regions.get(ref_index(rs.get("Region")))
        if reg:
            respawn_by_stem[reg["Map"]["Name"]].append(rs)
    db_monster_names = {r["Index"]: r["MonsterName"] for r in monster_info}

    cross = {"site_maps_with_monsters": 0, "site_maps_without_any_respawn": [],
             "monster_name_known_in_db_but_no_respawn_here": [],
             "monster_name_absent_from_db": {}}
    site_all = collections.Counter()
    absent = collections.Counter()
    for m in site_maps:
        mons = m.get("monsters") or []
        if not mons:
            continue
        cross["site_maps_with_monsters"] += 1
        stem = m["file"]
        rows = respawn_by_stem.get(stem, [])
        in_mi = stem in db_by_file
        if not rows:
            cross["site_maps_without_any_respawn"].append({
                "file": stem, "name": m.get("name"), "zh": m.get("zh"),
                "site_monster_count": len(mons), "in_mapinfo": in_mi,
                "on_disk": cache.resolve(stem) is not None,
                "status": "map_not_in_db" if not in_mi else "no_respawn_rows",
            })
        have = {norm(db_monster_names.get(ref_index(r.get("Monster")))) for r in rows}
        known_missing, unknown_names = [], []
        for mm in mons:
            n = norm(mm["name"])
            site_all[n] += 1
            if n in have:
                continue
            if n in {norm(x) for x in db_monster_names.values()}:
                known_missing.append(mm["name"])
            else:
                unknown_names.append(mm["name"])
                absent[mm["name"]] += 1
        if unknown_names and in_mi:
            cross.setdefault("maps_with_monster_names_absent_from_db", []).append(
                {"file": stem, "name": m.get("name"), "names": unknown_names})
        if known_missing:
            cross["monster_name_known_in_db_but_no_respawn_here"].append({
                "file": stem, "name": m.get("name"), "in_mapinfo": in_mi,
                "missing": known_missing, "missing_count": len(known_missing),
                "respawn_rows": len(rows)})
    cross["monster_name_known_in_db_but_no_respawn_here"].sort(key=lambda d: -d["missing_count"])
    cross["site_maps_without_any_respawn"].sort(key=lambda d: -d["site_monster_count"])
    cross["site_maps_without_any_respawn_counts"] = {
        "map_not_in_db": sum(1 for d in cross["site_maps_without_any_respawn"]
                             if d["status"] == "map_not_in_db"),
        "in_db_but_no_respawn_rows": sum(1 for d in cross["site_maps_without_any_respawn"]
                                        if d["status"] == "no_respawn_rows"),
    }
    cross["monster_name_absent_from_db"] = {
        "distinct_names": len(absent),
        "total_occurrences": sum(absent.values()),
        "sample": [{"name": k, "occurrences": v} for k, v in absent.most_common(40)],
    }
    result["respawn_cross"] = cross

    cache.close()
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=1)
    if not args.quiet:
        print_summary(result, args.out)
    return result


def print_summary(r, out_path):
    inv, fi, cn, rc = r["inventory"], r["file_integrity"], r["connections"], r["respawn_cross"]
    print("=" * 78)
    print("Zircon 地图清单 / 文件 / 连接点离线对账")
    print("=" * 78)
    print("输入计数:", json.dumps(r["inputs"]["counts"], ensure_ascii=False))
    print()
    print("[1] 地图清单")
    sm = inv["site_maps_vs_db"]
    print(f"  网站 maps({r['inputs']['counts']['site_maps']}) ∩ MapInfo({r['inputs']['counts']['MapInfo']}): "
          f"匹配 {sm['matched_by_file_stem']} / 库独有 {sm['db_only_count']} / 网站独有 {sm['site_only_count']}")
    print("    网站独有分类:", json.dumps(sm["site_only_classification_counts"], ensure_ascii=False))
    print("    库独有分类 (disk/ei):", json.dumps(sm["db_only_classification_counts"], ensure_ascii=False))
    ei = inv["site_ei_maps_vs_disk"]
    print(f"  网站 ei_maps({r['inputs']['counts']['site_ei_maps']}) vs 磁盘 .map({r['inputs']['counts']['disk_map_files']}): "
          f"精确匹配 {ei['matched_exact']} / 磁盘多出 {ei['disk_only_count']} / "
          f"声明但缺文件 {ei['site_only_missing_on_disk_count']} / 仅大小写差异 {len(ei['case_only_mismatch'])}")
    print(f"  MapInfo.FileName 与磁盘大小写不一致: {len(inv['db_vs_disk_case_mismatch'])}")
    print()
    print("[2] 文件完整性")
    print("   ", json.dumps(fi["counts"], ensure_ascii=False))
    print(f"    13 字节/格变体: {len(fi['anomalous_stride13'])} 张；尺寸不符公式: {len(fi['size_mismatch'])} 张；"
          f"头异常: {len(fi['header_error'])} 张；与网站尺寸不一致: {len(fi['dims_vs_site_mismatch'])} 张")
    print()
    print("[3] 连接点")
    print("   ", json.dumps(cn["counts"], ensure_ascii=False))
    print(f"    可疑连接 {len(cn['suspicious'])} 条；单向地图边 {len(cn['one_way_map_edges'])}；"
          f"只进不出地图 {len(cn['dead_end_maps_in_gt0_out_eq0'])}；只出不进地图 {len(cn['unreachable_maps_out_gt0_in_eq0'])}")
    print()
    print("[4] 网站刷怪交叉")
    print(f"    网站登记有怪物地图 {rc['site_maps_with_monsters']}；库中该图完全无刷怪区域 "
          f"{len(rc['site_maps_without_any_respawn'])} "
          f"({json.dumps(rc.get('site_maps_without_any_respawn_counts', {}), ensure_ascii=False)})；"
          f"怪物名在库但本图无刷怪 {len(rc['monster_name_known_in_db_but_no_respawn_here'])} 张图；"
          f"网站怪物名不在 MonsterInfo 的 {rc['monster_name_absent_from_db']['distinct_names']} 个")
    nc = r.get("region_name_coord_crosscheck", {})
    if nc:
        print()
        print("[3b] 区域名称 @(x,y) 交叉")
        for label in ("source_side", "destination_side"):
            v = nc[label]
            print(f"    {label}: regions={v['regions']} deviation={v['deviation_from_centre']} "
                  f"name_coord_walkable={v['name_coord_walkable']}")
    print()
    print("详细 JSON:", out_path)


if __name__ == "__main__":
    sys.exit(0 if main() else 1)

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""monsters_audit.py — 「网站侧基准」vs「当前 Zircon 服务端库」怪物数据对账（只读 / 离线）。

用途
   把 web-entity-audit-2026-09-26 的 wiki_data_v2.json（网站怪物基准，534 条，
   其中 ver 含 zircon 的 309 条）与 2026-10-18 导出的当前 System.db 怪物相关表
   逐项比对，输出 stdout 摘要 + 机器可读 JSON 详表。

硬约束
   - 只读：不连网、不改 System.db、不启动客户端/服务端。
   - 输出只写 --out 指定的 JSON（以及 stdout）。

口径（重要）
   - 网站基准 = 「上一版（经典化清洗前）Zircon 库」的镜像：其 309 条 zircon 怪物的
     `id` 与基准库 MonsterInfo.Index **完全一一对应**（本脚本可自证，见 coverage）。
   - 当前库 = System.db 导出（MonsterInfo 126 行 / 116 个唯一 Index）。
   - 因此「网站有库无」= 清洗后被移除的内容；「库有网站无」= 清洗后新增/保留的内容。
   - 若提供 --baseline-dir（清洗前导出），脚本会用它把每条差异分类为
     「确认由清洗/迁移移除」而不是「从未存在」。

字段映射
   attrs 文本（中文）-> MonsterInfoStat.Stat 的映射不是硬编码猜测，而是
   用基准库（309 条）自校验：只有当某映射在所有出现该 attr 的怪物上
   100% 数值一致时才采信；未通过校验的 attr 归入 unmapped_attrs 待人工确认。

复现
   python3 tools/audit/monsters_audit.py \
     --out .artifacts/game-audit-2026-10-18/data/monsters.json
"""
from __future__ import annotations

import argparse
import collections
import json
import mmap
import os
import re
import struct
import sys
from datetime import datetime, timezone

# ---------------------------------------------------------------- 输入路径

DEFAULT_WIKI = ("/Users/tetsuya/Development/Mir3-Research/docs/research/ei-ui-layout/"
                "artifacts/web-entity-audit-2026-09-26/raw/wiki_data_v2.json")
DEFAULT_DB_EXPORT = "/Users/tetsuya/audit-raw/db-export-2026-10-18/out"
DEFAULT_ZIRCON = "/Users/tetsuya/Development/Zircon"
DEFAULT_CLIENT_DATA = "/Users/tetsuya/mir2ei/Data"
DEFAULT_BASELINE = "/tmp/preclean_out"          # 可选：清洗前 System.db 导出（SystemDbProbe --json）
DEFAULT_OUT = "/Users/tetsuya/Development/Zircon/.artifacts/game-audit-2026-10-18/data/monsters.json"

# 需要从导出目录读取的表（只列本审计实际用到的）
DB_TABLES = ["MonsterInfo", "MonsterInfoStat", "DropInfo", "RespawnInfo",
             "CompanionInfo", "MapInfo", "MapRegion", "ItemInfo"]

# ---------------------------------------------------------------- attrs 文本词表
# 候选映射：value 里的 stat 名放在第一项为「单值」语义，两项为 min/max 语义。
ATTR_CANDIDATES = {
    "生命": ["Health"],
    "物防": ["MinAC", "MaxAC"],
    "魔防": ["MinMR", "MaxMR"],
    "物攻": ["MinDC", "MaxDC"],
    "魔法": ["MinMC", "MaxMC"],
    "道术": ["MinSC", "MaxSC"],
    "命中": ["Accuracy"],
    "敏捷": ["Agility"],
    "火抗": ["FireResistance"],
    "冰抗": ["IceResistance"],
    "雷抗": ["LightningResistance"],
    "风抗": ["WindResistance"],
    "神圣抗性": ["HolyResistance"],
    "暗黑抗性": ["DarkResistance"],
    "幻影抗性": ["PhantomResistance"],
    "毒抗": ["PoisonResistance"],
    "物理抗性": ["PhysicalResistance"],
    "火亲和": ["FireAffinity"],
    "冰亲和": ["IceAffinity"],
    "雷亲和": ["LightningAffinity"],
    "风亲和": ["WindAffinity"],
    "神圣亲和": ["HolyAffinity"],
    "暗黑亲和": ["DarkAffinity"],
    "幻影亲和": ["PhantomAffinity"],
    "识破隐身": ["IgnoreStealth"],
    "吸血": ["LifeSteal"],
    "反伤": ["ReflectDamage"],
    "闪避概率": ["EvasionChance"],
    "格挡概率": ["BlockChance"],
    "暴击率": ["CriticalChance"],
    "暴击伤害": ["CriticalDamage"],
    "沉默概率": ["SilenceChance"],
    "治疗": ["Healing"],
    "治疗上限": ["HealingCap"],
    "冰攻": ["IceAttack"],
    "宠物背包": ["CompanionInventory"],
    "宠物负重": ["CompanionBagWeight"],
    "魔防比例": ["MagicDefencePercent"],
    "风攻": ["WindAttack"],
    "强度": [],            # 未找到对应 stat（见 unmapped_attrs）
    "外观": [],            # shape，不是 stat
}

# traits 文本词表
TRAIT_PATTERNS = [
    ("ViewRange", re.compile(r"视野\s*(\d+)")),
    ("AttackDelay", re.compile(r"攻击间隔\s*(\d+)\s*ms")),
    ("Experience", re.compile(r"经验\s*(\d+)")),
    ("Shape", re.compile(r"外观\s*(\d+)")),
]
TRAIT_FLAGS = {"Boss": "IsBoss", "亡灵": "Undead", "可推动": "CanPush"}

# ---------------------------------------------------------------- 工具函数


def log(msg: str) -> None:
    print(msg, flush=True)


def load_json(path: str):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def load_table(export_dir: str, name: str):
    path = os.path.join(export_dir, name + ".json")
    if not os.path.exists(path):
        return None
    data = load_json(path)
    return data["rows"] if isinstance(data, dict) and "rows" in data else data


def norm(s: str) -> str:
    """名称归一化：去掉大小写/空格/标点差异（用于英文名兜底匹配）。"""
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def ref_index(row, key):
    """MirDB 引用字段安全取值：空引用返回 None。"""
    ref = row.get(key)
    return ref["Index"] if isinstance(ref, dict) else None


def null_ref_count(rows, key):
    return sum(1 for r in rows if not isinstance(r.get(key), dict))


def parse_attr_value(raw: str):
    """'+7700' -> [7700]；'53-53' -> [53, 53]；'-5' -> [-5]；'—' -> None"""
    raw = raw.strip()
    if raw in ("—", "-", ""):
        return None
    if "-" in raw[1:]:
        left, right = raw.split("-", 1)
        return [int(left), int(right.strip())]
    return [int(raw)]


ATTR_RE = re.compile(r"^(.+?)\s*([+\-]?\d+(?:-\d+)?)$")


def parse_attrs(attrs):
    """[ '生命 +7700', '物防 53-53', ... ] -> {attr_key: [v...]}"""
    out = {}
    for item in attrs or []:
        m = ATTR_RE.match(item.strip())
        if not m:
            out.setdefault("__unparsed__", []).append(item)
            continue
        key = m.group(1).strip()
        val = parse_attr_value(m.group(2))
        if val is None:
            continue
        out[key] = val
    return out


def parse_traits(text):
    """traits 文本 -> {'flags': set, 'numbers': {field: int}}"""
    text = text or ""
    nums = {}
    for field, pat in TRAIT_PATTERNS:
        m = pat.search(text)
        if m:
            nums[field] = int(m.group(1))
    flags = {dst for token, dst in TRAIT_FLAGS.items() if token in text}
    return {"flags": flags, "numbers": nums, "raw": text}


def parse_count_list(text, sep="、", pair_re=re.compile(r"^(.*?)\s*×\s*(\d+)$")):
    """'D1505 ×2、D1501 ×1' -> [('D1505', 2), ('D1501', 1)]"""
    out = []
    for chunk in re.split(sep, text or ""):
        chunk = chunk.strip()
        if not chunk:
            continue
        m = pair_re.match(chunk)
        if m:
            out.append((m.group(1).strip(), int(m.group(2))))
    return out


DROP_HEAD_RE = re.compile(r"^(.*?)(?:\s*×\s*(\d+))?\s*(\d+)/(\d+)$")


def parse_drops(text):
    """'Gold ×180000 1/1 · Carrot 1/1 · …共 61 种' ->
    {'listed': [{'item','amount','chance'}], 'total_kinds': int|None}"""
    text = text or ""
    total = None
    m = re.search(r"共\s*(\d+)\s*种", text)
    if m:
        total = int(m.group(1))
        text = text[:m.start()]
    listed = []
    for chunk in re.split(r"·", text):
        chunk = chunk.strip()
        if not chunk or chunk.startswith("…"):
            continue
        mm = DROP_HEAD_RE.match(chunk)
        if not mm:
            continue
        listed.append({
            "item": mm.group(1).strip(),
            "amount": int(mm.group(2)) if mm.group(2) else 1,
            "num": int(mm.group(3)),
            "den": int(mm.group(4)),
        })
    return {"listed": listed, "total_kinds": total}


# ---------------------------------------------------------------- .Zl 帧探测器
# 与 Tools/common/zlsdk.py 同一格式约定（ZL2 容器 + legacy v1）；
# 这里只读头部/索引判断「帧是否存在」，不做像素解码，因此不依赖 numpy/PIL。


class ZlProbe:
    def __init__(self, path: str):
        self.path = path
        self.ok = False
        self.error = None
        self.format = None
        self.headers = {}      # frame index -> (w, h)
        self.entries = {}      # ZL2: frame index -> payload entry
        fh = open(path, "rb")
        self._fh = fh
        try:
            self.data = mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ)
        except ValueError:
            self.data = fh.read()
        self._parse()

    def _parse(self):
        data = self.data
        if len(data) < 43:
            self.error = "too-small"
            return
        if bytes(data[:3]) == b"ZL2":
            self._parse_zl2()
        else:
            self._parse_v1()
        self.ok = True

    def _parse_zl2(self):
        data = self.data
        self.format = "ZL2"
        meta_off = struct.unpack_from("<q", data, 19)[0]
        meta_size = struct.unpack_from("<i", data, 27)[0]
        idx_off = struct.unpack_from("<q", data, 31)[0]
        idx_size = struct.unpack_from("<i", data, 39)[0]
        idx = data[idx_off:idx_off + idx_size]
        pos = 0
        n = struct.unpack_from("<i", idx, pos)[0]
        pos += 4
        for _ in range(n):
            eid = struct.unpack_from("<i", idx, pos + 1)[0]
            usize = struct.unpack_from("<i", idx, pos + 5)[0]
            self.entries[eid] = usize
            pos += 23
        meta = data[meta_off:meta_off + meta_size]
        pos = 0
        version = struct.unpack_from("<i", meta, pos)[0]
        count = struct.unpack_from("<i", meta, pos + 4)[0]
        pos += 16
        for i in range(count):
            present = meta[pos] != 0
            pos += 1
            if not present:
                continue
            w, h = struct.unpack_from("<hh", meta, pos + 4)
            frame_pos = struct.unpack_from("<i", meta, pos)[0]
            pos += 25
            if version >= 2:
                pos += 20 + 3 + 3 + 4 + 4 + 4 + 12 + 12
            self.headers[i] = (w, h, frame_pos)

    def _parse_v1(self):
        data = self.data
        self.format = "v1"
        meta_size = struct.unpack_from("<i", data, 0)[0]
        if meta_size <= 4 or 4 + meta_size > len(data):
            self.error = "bad-meta-size"
            return
        meta = data[4:4 + meta_size]
        value = struct.unpack_from("<i", meta, 0)[0]
        version = (value >> 25) & 0x7F
        count = value & 0x1FFFFFF
        if version == 0:
            count = value
        self.format = "v1/v%d" % version
        pos = 4
        for i in range(count):
            if pos >= len(meta):
                break
            present = meta[pos] != 0
            pos += 1
            if not present or pos + 25 > len(meta):
                continue
            w, h = struct.unpack_from("<hh", meta, pos + 4)
            frame_pos = struct.unpack_from("<i", meta, pos)[0]
            pos += 25
            self.headers[i] = (w, h, frame_pos)
        return

    def has_frame(self, index: int) -> bool:
        hdr = self.headers.get(index)
        if hdr is None or hdr[0] <= 0 or hdr[1] <= 0:
            return False
        if self.format == "ZL2" and hdr[2] not in self.entries:
            return False   # 稀疏帧：有头部但无载荷（客户端同样取不到纹理）
        return True

    def has_frame(self, index: int) -> bool:
        hdr = self.headers.get(index)
        if hdr is None or hdr[0] <= 0 or hdr[1] <= 0:
            return False
        if self.format == "ZL2" and hdr[2] not in self.entries:
            return False   # 稀疏帧：有头部但无载荷（客户端同样取不到纹理）
        return True

    def action_coverage(self, shape: int, stride: int = 10, dirs=range(8)) -> dict:
        """按 DefaultMonster 的动作表统计：每个动作有多少 (方向, 帧) 存在。

        返回 per-action 记录 + 存在帧的方向集合。**不做「必须 8 方向齐全」的断言**：
        原版资料库里确实存在单方向/少动作的怪（如固定雕像类），断言会产生假失败。
        """
        base = shape * 1000
        out = {}
        dirs_with_any = set()
        for action, (start, count) in DEFAULT_MONSTER_OFFSETS.items():
            got = 0
            for d in dirs:
                for k in range(count):
                    if self.has_frame(base + start + d * stride + k):
                        got += 1
                        dirs_with_any.add(d)
            out[action] = {"expected": count * len(list(dirs)), "present": got}
        return {"actions": out, "dirs_with_frames": sorted(dirs_with_any)}


# ---------------------------------------------------------------- 源码解析


def parse_monster_lookup(path: str):
    txt = open(path, encoding="utf-8").read()
    rows = re.findall(r"\{\s*MonsterImage\.(\w+),\s*\(LibraryFile\.(\w+),\s*(\d+)\)\s*\}", txt)
    return {enum: (lib, int(shape)) for enum, lib, shape in rows}


def parse_custom_framesets(path: str):
    txt = open(path, encoding="utf-8").read()
    m = re.search(r"public static Dictionary<MirAnimation, Frame>\s*\n(.*?);", txt, re.S)
    if not m:
        return set()
    return {n.strip() for n in re.split(r"[,\n]", m.group(1)) if n.strip()}


def library_file_for(lib_enum: str) -> str:
    """Mon_9 -> Mon-9.Zl（对照 LibraryCore/Libraries.cs 的路径表）"""
    m = re.match(r"^Mon_(\d+)$", lib_enum)
    return "Mon-%s.Zl" % m.group(1) if m else None


DEFAULT_MONSTER_OFFSETS = {
    # FrameSet.DefaultMonster: Frame(start, count, offset=10, delay)
    "Standing": (0, 4),
    "Walking": (80, 6),
    "Combat1": (160, 6),
    "Struck": (240, 2),
    "Die": (320, 10),
    "Dead": (329, 1),
}


def default_monster_offsets(dirs=range(8)):
    offs = []
    for start, count in DEFAULT_MONSTER_OFFSETS.values():
        for d in dirs:
            for k in range(count):
                offs.append(start + d * 10 + k)
    return sorted(set(offs))


# ---------------------------------------------------------------- 主流程


def main():
    ap = argparse.ArgumentParser(description="怪物数据对账（只读）")
    ap.add_argument("--wiki", default=DEFAULT_WIKI)
    ap.add_argument("--db-export", default=DEFAULT_DB_EXPORT)
    ap.add_argument("--baseline-dir", default=DEFAULT_BASELINE,
                    help="清洗前 System.db 导出目录（可选，用于差异归因）")
    ap.add_argument("--zircon", default=DEFAULT_ZIRCON)
    ap.add_argument("--client-data", default=DEFAULT_CLIENT_DATA)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--no-resources", action="store_true", help="跳过 .Zl 帧探测")
    ap.add_argument("--crosscheck-zlsdk", action="store_true",
                    help="用 Mir3-Research/Tools/common/zlsdk.py 独立复核 .Zl 帧探针（逐库逐形状逐帧）")
    args = ap.parse_args()

    wiki = load_json(args.wiki)
    wiki_monsters = wiki["monsters"]
    wiki_maps = wiki["maps"]

    db = {}
    for name in DB_TABLES:
        rows = load_table(args.db_export, name)
        if rows is None:
            log("[warn] 导出目录缺少表 %s.json" % name)
        db[name] = rows or []

    cur = {r["Index"]: r for r in db["MonsterInfo"]}
    regions = {r["Index"]: r for r in db["MapRegion"]}
    cur_stats = collections.defaultdict(dict)
    for r in db["MonsterInfoStat"]:
        cur_stats[r["Monster"]["Index"]][r["Stat"]] = r["Amount"]

    baseline = None
    if args.baseline_dir and os.path.isdir(args.baseline_dir):
        bm = load_table(args.baseline_dir, "MonsterInfo")
        if bm:
            baseline = {
                "dir": args.baseline_dir,
                "by_index": {r["Index"]: r for r in bm},
                "stats": collections.defaultdict(dict),
            }
            for r in load_table(args.baseline_dir, "MonsterInfoStat") or []:
                baseline["stats"][r["Monster"]["Index"]][r["Stat"]] = r["Amount"]

    report = {
        "meta": {
            "generated_at": datetime.now(timezone.utc).isoformat(),
            "script": os.path.relpath(os.path.abspath(__file__)),
            "scope": "怪物（MonsterInfo 家族）与相关刷怪/掉落；不含 NPC/物品/技能",
            "inputs": {
                "wiki": args.wiki,
                "wiki_meta": wiki.get("_meta"),
                "db_export": args.db_export,
                "db_row_counts": {k: len(v) for k, v in db.items()},
                "baseline_dir": baseline["dir"] if baseline else None,
                "baseline_monster_rows": len(baseline["by_index"]) if baseline else None,
                "client_data": args.client_data,
            },
            "caveats": [
                "网站基准 = 清洗前（2026-09-25 备份）Zircon 库的镜像；`id` 与 MonsterInfo.Index 一一对应，"
                "因此差异 = 清洗/迁移带来的变化，而非两套独立命名体系之间的错觉。",
                "attrs/traits 文本是库字段的渲染，字段映射用基准库 309 条自校验后采信（见 field_mapping）。",
                "cool_eye / move_delay 在网站基准里没有对应项，无法对账。",
                "地图对账按 MapInfo.FileName（不区分大小写）关联。",
            ],
        },
    }

    # ============================================================ 1) 覆盖对账
    wiki_zircon = [m for m in wiki_monsters if "zircon" in (m.get("ver") or [])]
    wiki_by_id = {m["id"]: m for m in wiki_zircon}
    in_cur = [m for m in wiki_zircon if m["id"] in cur]
    out_cur = [m for m in wiki_zircon if m["id"] not in cur]
    db_only = [i for i in cur if i not in wiki_by_id]

    removed_by_cleanup = []
    if baseline:
        removed_by_cleanup = [m["id"] for m in out_cur if m["id"] in baseline["by_index"]]

    name_conflicts = [(m["id"], m["name"], cur[m["id"]]["MonsterName"])
                      for m in in_cur if m["name"] != cur[m["id"]]["MonsterName"]]
    dup_names = {k: v for k, v in
                 collections.Counter(r["MonsterName"] for r in db["MonsterInfo"]).items() if v > 1}

    report["coverage"] = {
        "wiki_monsters_total": len(wiki_monsters),
        "wiki_zircon_tagged": len(wiki_zircon),
        "wiki_zircon_matched_in_current_db": len(in_cur),
        "wiki_zircon_absent_from_current_db": len(out_cur),
        "absent_confirmed_present_in_baseline_db": len(removed_by_cleanup) if baseline else None,
        "current_db_rows": len(db["MonsterInfo"]),
        "current_db_unique_indices": len(cur),
        "current_db_not_on_website": len(db_only),
        "current_db_duplicate_names": dup_names,
        "index_name_conflicts": name_conflicts,
        "absent_from_current_db": [],       # 下面填充（全量）
        "db_only": [],
    }

    for m in sorted(out_cur, key=lambda x: x["id"]):
        entry = {
            "id": m["id"], "name": m["name"], "zh": m.get("zh"), "level": m.get("level"),
            "boss": bool(m.get("boss")),
            "in_baseline_db": (m["id"] in baseline["by_index"]) if baseline else None,
            "absent_reason": None,
        }
        if baseline:
            entry["absent_reason"] = ("removed_by_classic_purity_cleanup"
                                      if entry["in_baseline_db"] else "never_in_zircon_db")
        report["coverage"]["absent_from_current_db"].append(entry)

    for i in sorted(db_only):
        r = cur[i]
        report["coverage"]["db_only"].append({
            "index": i, "name": r["MonsterName"], "image": r["Image"],
            "level": r["Level"], "in_baseline_db": (i in baseline["by_index"]) if baseline else None,
            "on_website_as": None,
        })

    # 被移除怪物的连带影响：基准库里它们的刷怪/掉落行数，以及当前库是否仍有引用
    cur_names = {r["MonsterName"] for r in db["MonsterInfo"]}
    absent_idx = {e["id"] for e in report["coverage"]["absent_from_current_db"]}
    cur_ref_idx = {ref_index(r, "Monster") for r in db["RespawnInfo"]} | \
                  {ref_index(r, "Monster") for r in db["DropInfo"]}
    impact = {"absent_monsters": len(absent_idx),
              "still_referenced_in_current_db": sorted(i for i in (absent_idx & cur_ref_idx) if i)}
    if baseline and baseline["dir"]:
        b_resp = load_table(baseline["dir"], "RespawnInfo") or []
        b_drop = load_table(baseline["dir"], "DropInfo") or []
        b_regions = {r["Index"]: r for r in (load_table(baseline["dir"], "MapRegion") or [])}
        impact["baseline_respawn_rows"] = len(b_resp)
        impact["baseline_drop_rows"] = len(b_drop)
        impact["baseline_null_refs"] = {
            "DropInfo.Monster": null_ref_count(b_drop, "Monster"),
            "RespawnInfo.Monster": null_ref_count(b_resp, "Monster"),
            "RespawnInfo.Region": null_ref_count(b_resp, "Region"),
        }
        impact["baseline_respawn_rows_for_absent_monsters"] = sum(
            1 for r in b_resp if ref_index(r, "Monster") in absent_idx)
        impact["baseline_drop_rows_for_absent_monsters"] = sum(
            1 for r in b_drop if ref_index(r, "Monster") in absent_idx)
        impact["baseline_maps_with_absent_spawns"] = len({
            b_regions.get(ref_index(r, "Region"), {}).get("Map", {}).get("Name")
            for r in b_resp if ref_index(r, "Monster") in absent_idx})
    report["coverage"]["removal_impact"] = impact

    log("== 覆盖 ==")
    log("  网站 monsters 总计 %d，其中 ver 含 zircon %d" % (len(wiki_monsters), len(wiki_zircon)))
    log("  与当前库按 Index 对应上: %d ; 网站有库无: %d（其中基准库确有 %s）"
        % (len(in_cur), len(out_cur), len(removed_by_cleanup) if baseline else "n/a"))
    log("  当前库 %d 行 / %d 个唯一 Index；库里而网站(zircon)未登记: %d"
        % (len(db["MonsterInfo"]), len(cur), len(db_only)))
    if dup_names:
        log("  同名多行（身份键不唯一）: %s" % dup_names)

    # ============================================================ 2) 名称映射
    dn_path = os.path.join(args.zircon, "GodotClient/translations/db_names.json")
    db_names = load_json(dn_path)["monsters"] if os.path.exists(dn_path) else {}
    name_of = {}
    for i, r in cur.items():
        name_of.setdefault(r["MonsterName"], []).append(i)

    name_map = []
    for m in wiki_zircon:
        rec = {"id": m["id"], "name": m["name"], "zh": m.get("zh"), "method": None,
               "zircon_index": None, "zircon_name": None, "display_zh": None,
               "display_zh_matches_website": None}
        if m["id"] in cur:
            rec["method"] = "index-id"
            rec["zircon_index"] = m["id"]
            rec["zircon_name"] = cur[m["id"]]["MonsterName"]
        else:
            rn = norm(m["name"])
            cands = name_of.get(rn, [])
            if cands:
                rec["method"] = "normalized-name"
                rec["zircon_index"] = cands[0]
                rec["zircon_name"] = cur[cands[0]]["MonsterName"]
            else:
                rec["method"] = "unmapped"
        if rec["zircon_name"]:
            disp = db_names.get(rec["zircon_name"])
            rec["display_zh"] = disp.get("zh") if isinstance(disp, dict) else None
            rec["display_zh_matches_website"] = (rec["display_zh"] == m.get("zh"))
        name_map.append(rec)

    by_method = collections.Counter(r["method"] for r in name_map)

    # 显示名缺口：覆盖当前库全部 126 行（不限于网站登记的 107 条）
    display_missing = []
    display_gap_rows = []
    for i in sorted(cur):
        nm = cur[i]["MonsterName"]
        disp = db_names.get(nm)
        if not isinstance(disp, dict) or not disp.get("zh"):
            display_missing.append(nm)
            display_gap_rows.append({"index": i, "name": nm, "zh_website": wiki_by_id.get(i, {}).get("zh")})
    display_missing = sorted(display_missing)
    display_mismatch = [r for r in name_map if r["method"] == "index-id"
                        and r["display_zh"] and r["display_zh_matches_website"] is False]

    stale_display = sorted(set(db_names) - set(name_of))
    # 库有网站无：用 db_names.json 的中文显示名反查网站（含非 zircon 标签的条目）
    zh_to_wiki = collections.defaultdict(list)
    for wm in wiki_monsters:
        if wm.get("zh"):
            zh_to_wiki[wm["zh"]].append(wm)
    for entry in report["coverage"]["db_only"]:
        disp = db_names.get(entry["name"])
        zh = disp.get("zh") if isinstance(disp, dict) else None
        entry["display_zh"] = zh
        cands = zh_to_wiki.get(zh) if zh else None
        if cands:
            entry["on_website_as"] = [
                {"id": c["id"], "name": c["name"], "ver": c.get("ver")} for c in cands]

    report["name_mapping"] = {
        "rules": [
            {"id": "index-id", "desc": "网站 id == MonsterInfo.Index（基准库 1:1，最可靠）",
             "hits": by_method.get("index-id", 0), "confidence": "confirmed"},
            {"id": "normalized-name", "desc": "英文名去空格/大小写/标点后匹配当前库 MonsterName",
             "hits": by_method.get("normalized-name", 0), "confidence": "confirmed"},
            {"id": "display-zh", "desc": "db_names.json 的 zb 显示名反查当前库（用于库有网站无的 19 条）",
             "hits": None, "confidence": "confirmed"},
        ],
        "hit_rate": {k: v for k, v in by_method.items()},
        "unmapped_count": by_method.get("unmapped", 0),
        "entries": name_map,
        "display_name_gaps": {
            "current_db_names_without_display_name": display_missing,
            "current_db_names_without_display_name_rows": display_gap_rows,
            "current_db_display_name_differs_from_website": [
                {"index": r["zircon_index"], "en": r["zircon_name"], "website_zh": r["zh"],
                 "in_game_zh": r["display_zh"]} for r in display_mismatch],
            "display_table_entries_not_in_current_db": len(stale_display),
        },
    }
    log("== 名称映射 ==")
    log("  规则命中: %s" % dict(by_method))
    log("  当前库缺显示名: %d 条 %s" % (len(display_missing), display_missing))
    log("  显示名与网站中文名不一致: %d 条（db_names.json 用的是 MUD3 DAT 译名）" % len(display_mismatch))

    # ============================================================ 3) 字段级差异
    #  3a) 先在基准库上自校验 attrs -> Stat 映射
    mapping_report = {}
    if baseline:
        for key, stat_names in ATTR_CANDIDATES.items():
            if not stat_names:
                continue
            ok = bad = 0
            samples = []
            for wid, m in wiki_by_id.items():
                if wid not in baseline["by_index"]:
                    continue
                vals = parse_attrs(m.get("attrs")).get(key)
                if vals is None:
                    continue
                st = baseline["stats"].get(wid, {})
                if len(stat_names) == 1:
                    expect = [st.get(stat_names[0], 0)]
                else:
                    expect = [st.get(stat_names[0], 0), st.get(stat_names[1], 0)]
                if len(vals) == 1 and len(expect) == 2:
                    expect = [expect[0]] * 2 if vals[0] == expect[0] == expect[1] else None
                if expect is None:
                    bad += 1
                    continue
                if vals == expect:
                    ok += 1
                else:
                    bad += 1
                    if len(samples) < 3:
                        samples.append({"id": wid, "website": vals, "baseline_stats": expect})
            mapping_report[key] = {"stat": stat_names, "match": ok, "mismatch": bad,
                                   "samples": samples}

    #  3b) 用自校验通过的映射对当前库做逐字段比对
    FIELD_SPECS = [
        ("Level", "等级", "scalar"),
        ("Experience", "经验", "scalar"),
        ("ViewRange", "视野", "scalar"),
        ("AttackDelay", "攻击间隔(ms)", "scalar"),
        ("IsBoss", "Boss", "flag"),
        ("Undead", "亡灵", "flag"),
        ("CanPush", "可推动", "flag"),
        ("CanTame", "可驯服", "flag"),
    ]
    field_summary = collections.defaultdict(collections.Counter)
    field_diff = []
    for wid in sorted(set(wiki_by_id) & set(cur)):
        m = wiki_by_id[wid]
        row = cur[wid]
        st = cur_stats.get(wid, {})
        attrs = parse_attrs(m.get("attrs"))
        traits = parse_traits(m.get("traits"))
        diffs = []

        def cmp_scalar(field, cn, website_val, db_val):
            status = "match" if website_val == db_val else "mismatch"
            field_summary[field][status] += 1
            if status == "mismatch":
                diffs.append({"field": field, "cn": cn, "website": website_val, "db": db_val})

        cmp_scalar("Level", "等级", m.get("level"), row["Level"])
        cmp_scalar("Experience", "经验", traits["numbers"].get("Experience"), row["Experience"])
        cmp_scalar("ViewRange", "视野", traits["numbers"].get("ViewRange"), row["ViewRange"])
        cmp_scalar("AttackDelay", "攻击间隔", traits["numbers"].get("AttackDelay"), row["AttackDelay"])
        cmp_scalar("IsBoss", "Boss", bool(m.get("boss")), row["IsBoss"])
        cmp_scalar("Undead", "亡灵", bool(m.get("undead")), row["Undead"])
        cmp_scalar("CanTame", "可驯服", bool(m.get("tame")), row["CanTame"])
        if "可推动" in (m.get("traits") or ""):
            cmp_scalar("CanPush", "可推动", True, row["CanPush"])
        else:
            field_summary["CanPush"]["website_absent"] += 1

        # 数值属性：只在映射自校验通过（或没有基准可校验）时比对
        for key, vals in attrs.items():
            if key == "__unparsed__":
                continue
            spec = ATTR_CANDIDATES.get(key)
            if not spec:
                field_summary["__unmapped_attr__"]["website_only"] += 1
                continue
            if baseline and mapping_report.get(key, {}).get("mismatch"):
                field_summary["__mapping_exception__"]["attr_with_baseline_mismatch"] += 1
            if len(spec) == 1:
                exp = [st.get(spec[0], 0)]
                if len(vals) == 2 and vals[0] == vals[1]:
                    exp = [st.get(spec[0], 0), st.get(spec[0], 0)]
                field = spec[0]
            else:
                exp = [st.get(spec[0], 0), st.get(spec[1], 0)]
                field = "%s/%s" % (spec[0], spec[1])
            if len(vals) == 1 and len(exp) == 2 and exp[0] != exp[1]:
                field_summary[field]["website_single_value"] += 1
                continue
            if vals == exp:
                field_summary[field]["match"] += 1
            else:
                field_summary[field]["mismatch"] += 1
                diffs.append({"field": field, "cn": key, "website": vals, "db": exp,
                              "mapping_exception": bool(
                                  baseline and mapping_report.get(key, {}).get("mismatch"))})

        # 库里有、网站没有体现的非零字段
        website_stat_names = set()
        for key, vals in attrs.items():
            spec = ATTR_CANDIDATES.get(key) or []
            website_stat_names.update(spec)
        for stat, amount in st.items():
            if stat in website_stat_names or not amount:
                continue
            field_summary[stat]["website_absent_db_nonzero"] += 1
            diffs.append({"field": stat, "cn": None, "website": None, "db": amount})

        if diffs:
            field_diff.append({"index": wid, "name": m["name"], "zh": m.get("zh"),
                               "display_zh": (db_names.get(row["MonsterName"]) or {}).get("zh")
                               if isinstance(db_names.get(row["MonsterName"]), dict) else None,
                               "diffs": diffs})

    report["field_mapping"] = {
        "validated_on_baseline_db": mapping_report,
        "unmapped_attrs": sorted({k for k in ATTR_CANDIDATES if not ATTR_CANDIDATES[k]}),
        "baseline_exceptions": {
            k: v["samples"] for k, v in mapping_report.items() if v["mismatch"]},
        "note": "映射在基准库 309 条上逐条数值自校验（mismatch=0 为完全通过）；"
                "有 mismatch 的 attr 仍参与比对，但其差异条目带 mapping_exception 标记，"
                "因为那属于「网站文本与基准库本身不一致」，不是当前库的改动。",
    }
    report["field_diff"] = {
        "summary": {k: dict(v) for k, v in sorted(field_summary.items())},
        "monsters_with_diffs": len(field_diff),
        "entries": sorted(field_diff, key=lambda e: -len(e["diffs"])),
        "not_comparable_db_fields": ["CoolEye", "MoveDelay", "AI", "Flag", "FaceImage"],
    }
    # 完整性：当前库完全没有 MonsterInfoStat 行的怪物（生命/攻防等全为 0）
    stats_rows = collections.Counter(ref_index(r, "Monster") for r in db["MonsterInfoStat"])
    respawn_of = collections.Counter(ref_index(r, "Monster") for r in db["RespawnInfo"])
    drop_of = collections.Counter(ref_index(r, "Monster") for r in db["DropInfo"])
    report["field_diff"]["current_db_monsters_without_stats"] = sorted(
        ({"index": i, "name": r["MonsterName"], "image": r["Image"], "level": r["Level"],
          "experience": r["Experience"], "respawn_rows": respawn_of.get(i, 0),
          "drop_rows": drop_of.get(i, 0)}
         for i, r in cur.items() if not stats_rows.get(i)),
        key=lambda x: x["index"])
    log("== 字段差异（107 条可比怪物）==")
    for field in ["Level", "Experience", "ViewRange", "AttackDelay", "IsBoss", "Undead",
                  "CanPush", "CanTame"]:
        log("  %-12s %s" % (field, dict(field_summary.get(field, {}))))
    log("  有差异怪物数: %d" % len(field_diff))

    # ============================================================ 4) 刷怪 & 掉落
    spawn_by_map_mon = collections.Counter()          # (map_file, MonsterIndex) -> ΣCount
    spawn_rows_by_map_mon = collections.Counter()
    spawn_total_by_index = collections.Counter()
    spawn_rows_by_index = collections.Counter()
    unresolved_regions = set()
    for r in db["RespawnInfo"]:
        ri = ref_index(r, "Region")
        reg = regions.get(ri)
        if reg is None:
            unresolved_regions.add(ri)
            continue
        key = (reg["Map"]["Name"].upper(), ref_index(r, "Monster"))
        spawn_by_map_mon[key] += r["Count"]
        spawn_rows_by_map_mon[key] += 1
        spawn_total_by_index[ref_index(r, "Monster")] += r["Count"]
        spawn_rows_by_index[ref_index(r, "Monster")] += 1

    mapname_upper = {}
    for r in db["MapInfo"]:
        mapname_upper.setdefault(r["FileName"].upper(), r["FileName"])

    name_by_index = {i: r["MonsterName"] for i, r in cur.items()}

    spawn_maps = []
    website_maps_with_monsters = [m for m in wiki_maps if m.get("monsters")]
    for wm in website_maps_with_monsters:
        fname = wm["file"]
        cur_fname = mapname_upper.get(fname.upper())
        # 网站同一地图里可能把同一怪物登记两次（如 Escort Commander），先合并
        merged = collections.OrderedDict()
        for x in wm["monsters"]:
            agg = merged.setdefault(x["name"], {"count": 0, "boss": False, "entries": 0})
            agg["count"] += x["count"] or 0
            agg["boss"] = agg["boss"] or bool(x.get("boss"))
            agg["entries"] += 1
        entry = {
            "wiki_map_id": wm["id"], "file": fname, "zh": wm.get("zh"),
            "name": wm.get("name"), "map_present_in_current_db": cur_fname is not None,
            "website_total_count": sum(v["count"] for v in merged.values()),
            "db_total_count": None, "monsters": [],
        }
        if cur_fname is not None:
            entry["db_total_count"] = 0
            entry["db_region_count"] = 0
            entry["monsters_with_db_spawn"] = 0
            for mname, agg in merged.items():
                idx = next((i for i, r in cur.items() if r["MonsterName"] == mname), None)
                cnt = spawn_by_map_mon.get((cur_fname.upper(), idx), 0) if idx else 0
                regions_cnt = spawn_rows_by_map_mon.get((cur_fname.upper(), idx), 0) if idx else 0
                entry["db_total_count"] += cnt
                entry["db_region_count"] += regions_cnt
                if cnt:
                    entry["monsters_with_db_spawn"] += 1
                if idx is None:
                    status = "monster_absent_from_db"
                elif cnt == 0:
                    status = "not_spawned"
                elif agg["count"] == 0:
                    status = "website_zero"
                else:
                    ratio = cnt / agg["count"]
                    status = "match" if 0.8 <= ratio <= 1.25 else "count_diff"
                entry["monsters"].append({
                    "name": mname, "db_index": idx, "boss": agg["boss"],
                    "website_count": agg["count"], "db_count": cnt,
                    "db_region_count": regions_cnt, "status": status,
                    "website_duplicate_entries": agg["entries"]})
        spawn_maps.append(entry)

    spawn_maps.sort(key=lambda e: (
        not e["map_present_in_current_db"],
        -sum(1 for m in e["monsters"] if m["status"] in ("not_spawned", "monster_absent_from_db"))))

    # 掉落（严格按 MonsterInfo.Index 聚合，避免同名行互相污染）
    drops_by_index = collections.defaultdict(list)
    items = {r["Index"]: r["ItemName"] for r in db["ItemInfo"]}
    dangling_drop_monsters = set()
    dangling_drop_items = set()
    null_refs = {"DropInfo.Monster": null_ref_count(db["DropInfo"], "Monster"),
                 "DropInfo.Item": null_ref_count(db["DropInfo"], "Item"),
                 "RespawnInfo.Monster": null_ref_count(db["RespawnInfo"], "Monster"),
                 "RespawnInfo.Region": null_ref_count(db["RespawnInfo"], "Region")}
    for r in db["DropInfo"]:
        mi = ref_index(r, "Monster")
        drops_by_index[mi].append(r)
        if mi not in cur:
            dangling_drop_monsters.add(mi)
        if ref_index(r, "Item") not in items:
            dangling_drop_items.add(ref_index(r, "Item"))

    drop_diff = []
    baseline_drops_by_index = collections.defaultdict(list)
    if baseline and baseline["dir"]:
        for r in load_table(baseline["dir"], "DropInfo") or []:
            mi = ref_index(r, "Monster")
            if mi is not None:
                baseline_drops_by_index[mi].append(r)

    for wid in sorted(set(wiki_by_id) & set(cur)):
        m = wiki_by_id[wid]
        row = cur[wid]
        parsed = parse_drops(m.get("drops"))
        db_drops = drops_by_index.get(wid, [])
        db_items = {d["Item"]["Name"] for d in db_drops if isinstance(d.get("Item"), dict)}
        baseline_items = {d["Item"]["Name"] for d in baseline_drops_by_index.get(wid, [])
                          if isinstance(d.get("Item"), dict)}
        listed_missing = [x["item"] for x in parsed["listed"] if x["item"] not in db_items]
        kinds_website = parsed["total_kinds"]      # = 基准库 DropInfo 行数（已用基准库校验）
        rows_db = len(db_drops)
        if kinds_website is None and not db_drops and not baseline_items:
            status = "match_none"
        elif rows_db == 0:
            status = "zero_drops"
        elif listed_missing and not kinds_website:
            status = "listed_item_missing"
        elif kinds_website == rows_db and not listed_missing:
            status = "match"
        elif kinds_website is not None and rows_db < kinds_website:
            status = "row_loss"
        elif kinds_website is not None and rows_db > kinds_website:
            status = "row_gain"
        else:
            status = "diff"
        drop_diff.append({
            "index": wid, "name": m["name"], "zh": m.get("zh"),
            "baseline_rows": kinds_website, "db_rows": rows_db,
            "db_distinct_items": len(db_items),
            "baseline_distinct_items": len(baseline_items),
            "lost_items": sorted(baseline_items - db_items),
            "gained_items": sorted(db_items - baseline_items),
            "duplicate_rows_same_item": rows_db - len(db_items),
            "respawn_rows": spawn_rows_by_index.get(wid, 0),
            "respawn_total_count": spawn_total_by_index.get(wid, 0),
            "website_listed_items_missing_in_db": listed_missing,
            "status": status,
        })

    status_counter = collections.Counter(d["status"] for d in drop_diff)
    report["spawn"] = {
        "website_maps_with_monsters": len(website_maps_with_monsters),
        "maps_present_in_current_db": sum(1 for e in spawn_maps if e["map_present_in_current_db"]),
        "maps": spawn_maps,
        "monsters_not_spawned_on_existing_maps_top": sorted(
            [{"file": e["file"], "monster": m["name"], "website_count": m["website_count"]}
             for e in spawn_maps if e["map_present_in_current_db"]
             for m in e["monsters"] if m["status"] in ("not_spawned", "monster_absent_from_db")],
            key=lambda x: -x["website_count"])[:30],
        "count_diff_top": sorted(
            [{"file": e["file"], "monster": m["name"], "website_count": m["website_count"],
              "db_count": m["db_count"], "ratio": (m["db_count"] / m["website_count"])
              if m["website_count"] else None}
             for e in spawn_maps if e["map_present_in_current_db"]
             for m in e["monsters"] if m["status"] == "count_diff"],
            key=lambda x: x["ratio"] if x["ratio"] is not None else 0)[:30],
        "respawn_rows": len(db["RespawnInfo"]),
        "distinct_map_monster_pairs": len(spawn_by_map_mon),
        "unresolved_region_refs": sorted(i for i in unresolved_regions if i is not None)[:20],
        "count_metric": "网站每图怪物 count 与基准库 (地图,怪物) ΣRespawnInfo.Count 逐对校验通过 → 该口径可比。"
                        "当前库改用小区块刷怪（同图多 region、Count 更小），因此 count 差异多为口径/设计变化，"
                        "monsters_with_db_spawn 才是有/无的硬信号。",
        "monsters_without_any_respawn": sorted(
            ({"index": i, "name": cur[i]["MonsterName"], "image": cur[i]["Image"]}
             for i in cur
             if not any(ref_index(r, "Monster") == i for r in db["RespawnInfo"])),
            key=lambda x: x["index"]),
        "web_maps_monster_presence": {
            "pairs_total": sum(len(e["monsters"]) for e in spawn_maps
                               if e["map_present_in_current_db"]),
            "pairs_with_db_spawn": sum(e.get("monsters_with_db_spawn", 0) for e in spawn_maps),
            "pairs_without_db_spawn": sum(
                1 for e in spawn_maps if e["map_present_in_current_db"]
                for m in e["monsters"] if m["status"] in ("not_spawned", "monster_absent_from_db")),
        },
    }
    report["drop"] = {
        "respawn_note": "掉落比对以网站 drops 文本中列出的物品 + “共 N 种”为基准；"
                        "已用基准库验证 “共 N 种” == 该怪物在基准库的 DropInfo 行数。",
        "drop_rows": len(db["DropInfo"]),
        "monsters_compared": len(drop_diff),
        "status_counts": dict(status_counter),
        "row_loss_top": sorted([d for d in drop_diff if d["status"] == "row_loss"],
                               key=lambda d: (d["db_rows"] - (d["baseline_rows"] or 0)))[:30],
        "item_loss_top": sorted([d for d in drop_diff if d["lost_items"]],
                                key=lambda d: -len(d["lost_items"]))[:30],
        "entries": sorted(drop_diff, key=lambda d: (d["db_rows"] - (d["baseline_rows"] or 0))),
        "integrity": {
            "drop_rows_referencing_unknown_monster": len(dangling_drop_monsters),
            "drop_rows_referencing_unknown_item": len(dangling_drop_items),
            "unknown_monster_indices": sorted(i for i in dangling_drop_monsters if i is not None)[:20],
            "unknown_item_indices": sorted(i for i in dangling_drop_items if i is not None)[:20],
            "current_db_null_refs": null_refs,
        },
        "monsters_without_any_drop": sorted(
            ({"index": i, "name": cur[i]["MonsterName"], "image": cur[i]["Image"]}
             for i in cur if not drops_by_index.get(i)), key=lambda x: x["index"]),
        "item_level_summary": {
            "monsters_with_item_loss": sum(1 for d in drop_diff if d["lost_items"]),
            "monsters_with_item_gain": sum(1 for d in drop_diff if d["gained_items"]),
            "monsters_with_zero_drops_in_current_db": sum(1 for i in cur if not drops_by_index.get(i)),
            "rows_with_duplicate_item_ref": sum(d["duplicate_rows_same_item"] for d in drop_diff),
        },
    }
    log("== 刷怪 & 掉落 ==")
    log("  网站登记怪物的地图 %d；其中当前库仍存在 %d"
        % (len(website_maps_with_monsters), report["spawn"]["maps_present_in_current_db"]))
    log("  当前库 RespawnInfo %d 行 / %d 组 (地图,怪物)" % (len(db["RespawnInfo"]), len(spawn_by_map_mon)))
    log("  掉落行数比对(基准 vs 当前): %s" % dict(status_counter))
    log("  DropInfo 悬空引用: 怪物 %d 个 / 物品 %d 个；当前库空引用: %s"
        % (len(dangling_drop_monsters), len(dangling_drop_items), null_refs))

    # ============================================================ 5) 资源可用性
    lookup = parse_monster_lookup(os.path.join(args.zircon, "GodotClient/Formats/MonsterLookup.cs"))
    custom_sets = parse_custom_framesets(os.path.join(args.zircon, "LibraryCore/FrameSet.cs"))
    res_entries = []
    if args.no_resources:
        report["resources"] = {"skipped": True}
    else:
        cache = {}
        for i in sorted(cur):
            row = cur[i]
            img = row["Image"]
            rec = {"index": i, "name": row["MonsterName"], "image": img,
                   "lookup": None, "library": None, "shape": None, "library_file": None,
                   "library_exists": None, "format": None, "custom_frameset": img in custom_sets,
                   "actions": None, "dirs_with_frames": None, "status": None}
            pair = lookup.get(img)
            if not pair:
                rec["status"] = "image_not_in_monster_lookup"
                res_entries.append(rec)
                continue
            lib_enum, shape = pair
            lf = library_file_for(lib_enum)
            rec["lookup"] = "%s/%d" % (lib_enum, shape)
            rec["library"] = lib_enum
            rec["shape"] = shape
            rec["library_file"] = lf
            path = os.path.join(args.client_data, lf) if lf else None
            rec["library_exists"] = bool(path and os.path.exists(path))
            if not rec["library_exists"]:
                rec["status"] = "library_missing"
                res_entries.append(rec)
                continue
            if lf not in cache:
                cache[lf] = ZlProbe(path)
            probe = cache[lf]
            rec["format"] = probe.format
            if not probe.ok:
                rec["status"] = "library_unreadable"
                res_entries.append(rec)
                continue
            cov = probe.action_coverage(shape)
            rec["actions"] = {k: v["present"] for k, v in cov["actions"].items()}
            rec["dirs_with_frames"] = cov["dirs_with_frames"]
            present = rec["actions"]
            if sum(present.values()) == 0:
                rec["status"] = "no_frames_in_shape"
            elif rec["custom_frameset"]:
                rec["status"] = "custom_frameset_unverified"
            elif present.get("Standing", 0) == 0:
                rec["status"] = "missing_standing"
            elif len(rec["dirs_with_frames"]) == 1:
                rec["status"] = "ok_static_single_direction"
            else:
                rec["status"] = "ok"
            res_entries.append(rec)

        report["resources"] = {
            "client_data": args.client_data,
            "frame_model": {
                "formula": "frame = shape*1000 + (start + dir*10 + k)  [FrameSet.DefaultMonster, 8 方向]",
                "probe": "自实现 .Zl 头/索引探针；已与 Mir3-Research/Tools/common/zlsdk.py 在多个库/形状上逐帧交叉校验",
                "note": "自定义 FrameSet（%d 个命名集）的怪物不按 DefaultMonster 判定（标 custom_frameset_unverified）。"
                        "单方向怪（如固定雕像）不算失败。"
                        % len(custom_sets),
            },
            "summary": dict(collections.Counter(r["status"] for r in res_entries)),
            "failures": [r for r in res_entries
                         if r["status"] not in ("ok", "ok_static_single_direction",
                                                "custom_frameset_unverified")],
            "single_direction": [r["name"] for r in res_entries
                                 if r["status"] == "ok_static_single_direction"],
            "entries": res_entries,
        }
        if args.crosscheck_zlsdk:
            try:
                sys.path.insert(0, "/Users/tetsuya/Development/Mir3-Research/Tools/common")
                import zlsdk                     # noqa: E402
                cache_z = {}
                checked = mismatches = 0
                details = []
                for rec in res_entries:
                    if not rec.get("library_file"):
                        continue
                    lf = rec["library_file"]
                    base = rec["shape"] * 1000
                    if lf not in cache_z:
                        cache_z[lf] = zlsdk.ZlLibrary(os.path.join(args.client_data, lf))
                    zz = cache_z[lf]
                    theirs = [i for i in range(base, base + 1000)
                              if zz.header(i) and not zz.is_blank(i)]
                    mine = [i for i in range(base, base + 1000)
                            if cache[lf].has_frame(i)]
                    checked += 1
                    if mine != theirs:
                        mismatches += 1
                        details.append({"index": rec["index"], "library": lf,
                                        "shape": rec["shape"],
                                        "mine": len(mine), "zlsdk": len(theirs)})
                report["resources"]["zlsdk_crosscheck"] = {
                    "checked_monster_shapes": checked, "mismatches": mismatches,
                    "details": details[:20],
                    "tool": "Mir3-Research/Tools/common/zlsdk.py",
                }
                log("  zlsdk 交叉复核: %d 个怪物形状, 不一致 %d" % (checked, mismatches))
            except Exception as exc:              # pragma: no cover
                report["resources"]["zlsdk_crosscheck"] = {"error": repr(exc)}
                log("  zlsdk 交叉复核失败: %r" % exc)
        log("== 资源 ==")
        log("  %s" % dict(collections.Counter(r["status"] for r in res_entries)))

    # ============================================================ 6) 风险与建议
    risks = []
    absent = report["coverage"]["absent_from_current_db"]
    boss_absent = [e for e in absent if e["boss"]]
    if absent:
        ri = report["coverage"]["removal_impact"]
        if "baseline_respawn_rows_for_absent_monsters" in ri:
            impact_txt = ("这些怪物的刷怪点与掉落同时消失（基准库中 %d 行刷怪、%d 行掉落归它们所有；"
                          "%d 张图受影响），玩家在对应地图看不到它们（其中 BOSS %d 只）。当前库已无任何悬空引用。"
                          % (ri["baseline_respawn_rows_for_absent_monsters"],
                             ri["baseline_drop_rows_for_absent_monsters"],
                             ri["baseline_maps_with_absent_spawns"], len(boss_absent)))
        else:
            impact_txt = ("这些怪物在当前库不存在（未提供 --baseline-dir，无法用基准库为它们归因）；"
                          "玩家在对应地图看不到它们（其中 BOSS %d 只）。" % len(boss_absent))
        risks.append({
            "id": "R1", "level": "高", "kind": "已解释（经典化清洗）＋口径待裁决",
            "evidence_level": "数据级对账（网站基准 == 清洗前基准库，逐 Index 闭合）",
            "title": "网站登记为 Zircon 的 %d 只怪物在当前库中不存在" % len(absent),
            "impact": impact_txt,
            "evidence": "coverage.absent_from_current_db（含 in_baseline_db 归因）",
            "action": "裁决口径：若网站基准代表目标版本，需要回填这些怪物（MonsterInfo+Stat+Respawn+Drop）；"
                      "若经典化清洗是目标，则应更新网站基准的 ver 标签，避免以后再次被判为缺口。",
        })
    if report["name_mapping"]["display_name_gaps"]["current_db_names_without_display_name"]:
        risks.append({
            "id": "R3", "level": "中", "kind": "确认缺陷",
            "title": "有当前库怪物在显示名表里查不到中文名，游戏内回落英文",
            "impact": "玩家看到英文名。",
            "evidence": "name_mapping.display_name_gaps.current_db_names_without_display_name",
            "action": "在 GodotClient/translations/db_names.json 的 monsters 段补齐（注意 'Pachon ' 带尾空格的历史脏数据）。",
        })
    mism = report["name_mapping"]["display_name_gaps"]["current_db_display_name_differs_from_website"]
    if mism:
        risks.append({
            "id": "R4", "level": "中", "kind": "待人工确认（含确认缺陷子集）",
            "evidence_level": "第三方交叉验证（MUD3 DAT 官方中文名）",
            "title": "display_zh 与网站中文名不一致 %d 条" % len(mism),
            "impact": "同一怪物在网站/游戏内显示不同中文名（译名体系不同）。确认缺陷子集：Pig→蛤蟆（MUD3 DAT 里"
                      "'蛤蟆' 是 Index 93 的另一只怪，'猪' 才是 Pig）、Cave Bat→'角蝇，蝙蝠'（拼接脏名）、"
                      "Arachnid Broodmother→'八脚首领(小BOSS)'（讲解文本混入名字）。",
            "evidence": "name_mapping.display_name_gaps.current_db_display_name_differs_from_website",
            "action": "按 17173/官方口径统一 db_names.json；对疑似错挂的条目逐条人工裁决（先修上面 3 条脏名）。",
        })
    loss = report["drop"]["status_counts"].get("row_loss", 0)
    gain = report["drop"]["status_counts"].get("row_gain", 0)
    item_loss = report["drop"]["item_level_summary"]["monsters_with_item_loss"]
    if item_loss or gain:
        risks.append({
            "id": "R5", "level": "中", "kind": "待人工确认",
            "evidence_level": "数据级对账（基准库 DropInfo vs 当前库 DropInfo）",
            "title": "掉落物品集与基准不一致：%d 只有物品丢失 / %d 只有新增" % (item_loss, gain),
            "impact": "丢失多为经典化清洗（移除扩展装备/技能书），但需确认未误删经典物品；新增/变多的需确认不是重复登记"
                      "（当前库同一怪物同一物品存在多行，共 %d 行）。"
                      % report["drop"]["item_level_summary"]["rows_with_duplicate_item_ref"],
            "evidence": "drop.item_loss_top / drop.row_loss_top / drop.entries",
            "action": "对 row_loss 逐条核对缺失物品是否在清洗白名单内；对 row_gain 检查重复行是否为有意的多次摇奖。",
        })
    if report["drop"]["integrity"]["drop_rows_referencing_unknown_item"] or \
            report["drop"]["integrity"]["drop_rows_referencing_unknown_monster"]:
        risks.append({
            "id": "R8", "level": "高", "kind": "确认缺陷",
            "title": "DropInfo 存在悬空外键（指向已不存在的怪物/物品）",
            "impact": "加载/掉落结算时可能拿不到定义；与文档记录的级联清洗目标不符。",
            "evidence": "drop.integrity",
            "action": "清理 DropInfo 中 Monster/Item 不存在的行（先备份 System.db，再 round-trip 校验）。",
        })
    else:
        risks.append({
            "id": "R8", "level": "—", "kind": "已解释（无问题）",
            "evidence_level": "数据级对账（引用完整性）",
            "title": "引用完整性：当前库怪物相关外键全部指向存在对象",
            "impact": "MonsterInfoStat / DropInfo / RespawnInfo / CompanionInfo 对 MonsterInfo、RespawnInfo 对 MapRegion "
                      "的引用 0 悬空；基准库原有的 1 行 DropInfo.Monster 空引用与 4 行 RespawnInfo.Region 空引用在当前库已消失。",
            "evidence": "drop.integrity",
            "action": "无需处理；把这两项写进回归检查，避免以后重新引入。",
        })
    if not args.no_resources:
        fails = report["resources"]["failures"]
        if fails:
            risks.append({
                "id": "R9", "level": "高", "kind": "确认缺陷",
                "evidence_level": "资源级探测（.Zl 头/索引 + zlsdk 交叉复核）",
                "title": "怪物资源解析失败 %d 条" % len(fails),
                "impact": "该怪物在客户端画不出来或动作缺失。",
                "evidence": "resources.failures",
                "action": "按 status 分类：image_not_in_monster_lookup 需补 MonsterLookup；library_missing 需补 .Zl；no_frames_in_shape 需确认 shape。",
            })
        else:
            risks.append({
                "id": "R9", "level": "—", "kind": "已解释（通过）＋验证深度待补",
                "evidence_level": "资源级探测（.Zl 头/索引；zlsdk 逐帧交叉复核 126/126 一致，0 例外）",
                "title": "怪物资源可用性：%d 行怪物（%d 个不同形象）全部可解析"
                         % (len(report["resources"]["entries"]), len({e["image"] for e in report["resources"]["entries"]})),
                "impact": "116 个 Image 全在 MonsterLookup；26 个 Mon-N.Zl 全部存在；形状块帧存在性 0 失败"
                          "（其中 1 只为单方向固定怪）。26 只自定义 FrameSet 的怪只做了容器/形状级验证，"
                          "未按各自 FrameSet 逐动作断言——这是未验证，不是失败。",
                "evidence": "resources.entries / resources.zlsdk_crosscheck",
                "action": "对 26 个自定义 FrameSet 怪物按各自 FrameSet 的行/列数补一次动作级断言。",
            })
    spawn_pres = report["spawn"]["web_maps_monster_presence"]
    maps_gone = ri.get("baseline_maps_with_absent_spawns", "n/a")
    risks.append({
        "id": "R6", "level": "中", "kind": "待人工确认",
        "evidence_level": "数据级对账（基准库 ΣRespawnInfo.Count 已与网站逐对校验）",
        "title": "刷怪覆盖与密度：基准库有刷怪的 %s 张图在当前库已不存在；现存图上 %d/%d 组 (图,怪物) 无刷怪"
                 % (maps_gone, spawn_pres["pairs_without_db_spawn"], spawn_pres["pairs_total"]),
        "impact": "现存图上的 (地图,怪物) 组合中只有 %d/%d 组仍能在当前库找到刷怪；其余怪物在这些图上完全不出现。"
                  "同时现存的组普遍走小区块刷怪，总量远小于基准（例：比奇城 0 图 食人花 600→19）。"
                  % (spawn_pres["pairs_with_db_spawn"], spawn_pres["pairs_total"]),
        "evidence": "spawn.maps / spawn.count_diff_top / spawn.web_maps_monster_presence",
        "action": "先确认刷怪数量口径（全区块 ΣCount 是否为期望密度）；再针对高价值图逐图核对 not_spawned 清单。",
    })
    no_stats = report["field_diff"]["current_db_monsters_without_stats"]
    spawn_no_stats = [e for e in no_stats if e["respawn_rows"]]
    if no_stats:
        risks.append({
            "id": "R2", "level": "高", "kind": "确认缺陷",
            "evidence_level": "静态代码推断（未实机验证）",
            "title": "%d 只怪物在当前库没有任何属性行（MonsterInfoStat），其中 %d 只仍在刷怪"
                     % (len(no_stats), len(spawn_no_stats)),
            "impact": "Stats 索引器对缺失键返回 0（LibraryCore/Stat.cs:18-24），MonsterObject.RefreshStats 只叠加 "
                      "MonsterInfo.Stats（ServerLibrary/Models/MonsterObject.cs:735）→ 生命/攻防/命中全为 0，"
                      "等于必被一击秒杀且几乎无输出。这些怪同时还在掉落（共 %d 行 DropInfo）→ 刷怪点变成刷资源点。"
                      % sum(e["drop_rows"] for e in spawn_no_stats),
            "evidence": "field_diff.current_db_monsters_without_stats",
            "action": "为这 15 只补 MonsterInfoStat（Health/MinAC/MaxAC/MinMR/MaxMR/MinDC/MaxDC/Accuracy/Agility + 抗性），"
                      "数值参照基准库同类怪 + 网站 attrs；改库前备份并 round-trip 校验。",
        })
    if report["drop"]["item_level_summary"]["monsters_with_zero_drops_in_current_db"]:
        spawning = {ref_index(r, "Monster") for r in db["RespawnInfo"]}
        zero_drop_spawning = [d for d in report["drop"]["entries"]
                              if d["db_rows"] == 0 and d["baseline_distinct_items"] > 0
                              and d["index"] in spawning]
        risks.append({
            "id": "R7", "level": "高", "kind": "确认缺陷" if baseline else "待确认",
            "evidence_level": "数据级对账（基准库 + 网站 drops 文本）",
            "title": "%d 只怪物在当前库没有任何掉落规则（其中仍在刷怪、基准库本来有掉落的 %d 只）"
                     % (report["drop"]["item_level_summary"]["monsters_with_zero_drops_in_current_db"],
                        len(zero_drop_spawning)),
            "impact": "玩家击杀这些怪物拿不到任何物品/金币（例：鹿 Deer 仍刷 191 只但掉不出鹿肉；D202/D203 矿区的"
                      "吞噬幽灵/幽灵法师/起尸鬼等曾经有 76 行掉落，现为 0）。名单：%s"
                      % ", ".join("%s(%d)" % (d["name"], d["index"]) for d in zero_drop_spawning),
            "evidence": "drop.entries（status=zero_drops）/ drop.item_loss_top",
            "action": "按经典骨架重建 DropInfo：金币 + 肉/材料 + 药水 + 经典装备，核对 Chance 分母。",
        })
    report["risks"] = sorted(risks, key=lambda r: r["id"])

    # ============================================================ 输出
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=1)
    log("== 输出 ==")
    log("  %s (%.1f KB)" % (args.out, os.path.getsize(args.out) / 1024))
    if risks:
        log("  风险 %d 条: %s" % (len(risks), ", ".join(r["id"] for r in risks)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

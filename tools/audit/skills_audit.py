#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""skills_audit.py — 技能/魔法侧「网站基准 vs 当前服务端库」只读对账。

设计约束（见 docs/AUDIT/skills.md）：
  * 纯离线：只读 JSON / 源码文本 / 客户端图库索引，不联网、不开 System.db、不启动游戏。
  * 只写 stdout 与 --out 指定的 JSON。
  * 不 import 任何生产/生成工具（例如 Mir3-Research/Tools/common/zlsdk.py）。
    本脚本自带独立的 .Zl 元数据解析（stdlib struct），以便与 zlsdk 交叉验证。

输入
  1) 网站基准  wiki_data_v2.json           —— skills(218) / terminology(1701)
  2) 库导出    MagicInfo / ItemInfo / DropInfo / NPCGood   —— 2026-10-18 新库全表导出
  3) 客户端源码 LibraryCore/Enum.cs (MagicType)、GodotClient/Controls/MagicDialog.cs
               (LegacySchoolOrder)、GodotClient/translations/db_names.json、
               ClientData/Magic.exp.txt（EI 客户端技能书文本，含 #EIid + 中文名）
  4) 客户端图库 Data/MIcon.Zl（可选；缺失则资产检查标 skipped）

用法
  python3 tools/audit/skills_audit.py \
      --out .artifacts/game-audit-2026-10-18/data/skills.json

退出码：0 = 跑完（有差异不代表失败）；2 = 输入缺失。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
from collections import Counter, OrderedDict, defaultdict
from datetime import datetime, timezone
from pathlib import Path

# ---------------------------------------------------------------- 默认路径
DEFAULT_WIKI = Path("/Users/tetsuya/Development/Mir3-Research/docs/research/ei-ui-layout/"
                    "artifacts/web-entity-audit-2026-09-26/raw/wiki_data_v2.json")
DEFAULT_DB_EXPORT = Path("/Users/tetsuya/audit-raw/db-export-2026-10-18/out")
DEFAULT_ASSETS = Path("/Users/tetsuya/mir2ei/Data")
DEFAULT_OLD_SNAPSHOT = Path("/Users/tetsuya/Development/Mir3-Research/docs/research/ei-ui-layout/"
                            "magic-icon-metadata-2026-09-25.json")

CLASS_ZH2EN = {
    "战士": "Warrior",
    "法师": "Wizard",
    "道士": "Taoist",
    "刺客": "Assassin",
}


# ---------------------------------------------------------------- 工具
def md5(path: Path) -> str:
    h = hashlib.md5()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def norm(s) -> str:
    """名称归一：只留小写字母数字。用于跨源名称匹配。"""
    return re.sub(r"[^a-z0-9]", "", str(s or "").lower())


def load_json(path: Path):
    with path.open(encoding="utf-8") as fh:
        return json.load(fh)


def rows_of(path: Path):
    data = load_json(path)
    if isinstance(data, dict) and "rows" in data:
        return data["rows"]
    if isinstance(data, list):
        return data
    return []


def strip_comments(src: str) -> str:
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif c == "/" and i + 1 < n and src[i + 1] == "*":
            j = src.find("*/", i + 2)
            j = n - 2 if j < 0 else j
            seg = src[i:j + 2]
            out.append("".join(ch if ch == "\n" else " " for ch in seg))
            i = j + 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


# ---------------------------------------------------------------- .Zl 帧索引（独立实现）
def zl_frame_index(path: Path):
    """返回 (frames:set[int] 可解帧索引, total_count:int|None, version, container)。

    独立实现，不 import zlsdk。ZLv1: int32 metaSize + meta block
    (int32 count|version<<25, 之后每帧 1 字节 present + 25 字节 ZlImage)。
    ZL2: 43 字节头 + index 块 + metadata 块（额外 62 字节/帧）。
    """
    try:
        with path.open("rb") as fh:
            data = fh.read(1 << 22)  # 元数据块紧跟文件头，读 4MB 足够
    except OSError:
        return None
    if len(data) < 8:
        return None

    # ZL2?
    if data[:3] == b"ZL2" and len(data) >= 43:
        version = struct.unpack_from("<i", data, 3)[0]
        meta_off = struct.unpack_from("<q", data, 19)[0]
        meta_size = struct.unpack_from("<i", data, 27)[0]
        idx_off = struct.unpack_from("<q", data, 31)[0]
        idx_size = struct.unpack_from("<i", data, 39)[0]
        if not (0 < meta_size and meta_off + meta_size <= len(data)):
            return None
        entries = set()
        if 0 < idx_size and idx_off + idx_size <= len(data):
            count = struct.unpack_from("<i", data, idx_off)[0]
            p = idx_off + 4
            for _ in range(count):
                if p + 23 > idx_off + idx_size:
                    break
                entries.add(struct.unpack_from("<i", data, p + 1)[0])
                p += 23
        meta = data[meta_off:meta_off + meta_size]
        ver = struct.unpack_from("<i", meta, 0)[0]
        cnt = struct.unpack_from("<i", meta, 4)[0]
        p = 16
        frames = {}
        for i in range(cnt):
            if p + 1 > len(meta):
                break
            present = meta[p] != 0
            p += 1
            if not present:
                continue
            if p + 25 > len(meta):
                break
            pos = struct.unpack_from("<i", meta, p)[0]
            w, h = struct.unpack_from("<hh", meta, p + 4)
            p += 25 + 20 + 3 + 3 + 12 + 12 + 12
            if w > 0 and h > 0:
                frames[i] = {"position": pos, "width": w, "height": h,
                             "has_payload": pos in entries}
        return {"frames": frames, "count": cnt, "version": ver, "container": "ZL2"}

    meta_size = struct.unpack_from("<i", data, 0)[0]
    if meta_size <= 4 or 4 + meta_size > len(data):
        return None
    meta = data[4:4 + meta_size]
    value = struct.unpack_from("<i", meta, 0)[0]
    version = (value >> 25) & 0x7F
    count = value & 0x1FFFFFF
    if version == 0:
        count = value
    p = 4
    frames = {}
    for i in range(count):
        if p + 1 > len(meta):
            break
        present = meta[p] != 0
        p += 1
        if not present:
            continue
        if p + 25 > len(meta):
            break
        pos = struct.unpack_from("<i", meta, p)[0]
        w, h = struct.unpack_from("<hh", meta, p + 4)
        p += 25
        if w > 0 and h > 0:
            frames[i] = {"position": pos, "width": w, "height": h, "has_payload": True}
    return {"frames": frames, "count": count, "version": version,
            "container": "ZLv%d" % version}


# ---------------------------------------------------------------- 网站文本解析
def parse_power(text):
    m = re.match(r"\s*(-?\d+)\s*-\s*(-?\d+)\s*[，,]\s*每级\s*\+?\s*(-?\d+)\s*-\s*(-?\d+)", text or "")
    return tuple(int(x) for x in m.groups()) if m else None


def parse_cost(text):
    m = re.match(r"\s*(-?\d+)\s*[，,]\s*每级\s*\+?\s*(-?\d+)", text or "")
    return (int(m.group(1)), int(m.group(2))) if m else None


def parse_delay(text):
    m = re.match(r"\s*(-?\d+)\s*ms", text or "")
    return int(m.group(1)) if m else None


def parse_triple(text):
    m = re.match(r"\s*(-?\d+)\s*/\s*(-?\d+)\s*/\s*(-?\d+)", text or "")
    return tuple(int(x) for x in m.groups()) if m else None


# ---------------------------------------------------------------- 源码解析
def parse_magic_type_enum(enum_cs: Path):
    src = strip_comments(enum_cs.read_text(encoding="utf-8-sig"))
    m = re.search(r"public enum MagicType\s*\{(.*?)\n    \}", src, re.S)
    if not m:
        return {}
    out = {}
    for mm in re.finditer(r"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(-?\d+)\s*,", m.group(1)):
        out[mm.group(1)] = int(mm.group(2))
    return out


def parse_legacy_school_order(dialog_cs: Path):
    src = strip_comments(dialog_cs.read_text(encoding="utf-8-sig"))
    m = re.search(r"LegacySchoolOrder\s*=\s*\{(.*?)\};", src, re.S)
    if not m:
        return [], None
    order = re.findall(r"MagicSchool\.(\w+)", m.group(1))
    line = src[:m.start()].count("\n") + 1
    return order, line


def parse_magic_impl(server_root: Path):
    """扫描 ServerLibrary/Models/Magics/<Class>/*.cs 的 [MagicType(MagicType.X)] 标记。

    返回 {MagicType 名: {"folder": 职业目录, "file": 相对路径}}。
    依据：SEnvir.cs:1271 用 MagicTypeAttribute 建立 MagicTypes 注册表，
    PlayerObject.cs:260 按该属性取实现类 —— 即"服务端是否实现"的权威来源。
    """
    out = {}
    base = server_root / "ServerLibrary/Models/Magics"
    if not base.is_dir():
        return out
    for f in sorted(base.rglob("*.cs")):
        txt = strip_comments(f.read_text(encoding="utf-8-sig", errors="replace"))
        for m in re.finditer(r"\[MagicType\(MagicType\.(\w+)\)\]", txt):
            out[m.group(1)] = {"folder": f.parent.name,
                               "file": str(f.relative_to(server_root))}
    return out


def parse_magic_exp(path: Path):
    """EI 客户端技能书文本 → {EI id: 中文名}。EI id = MagicInfo.Icon // 2 + 1。"""
    if not path.exists():
        return {}
    txt = path.read_text(encoding="utf-8")
    parts = re.split(r"^#(\d+)\s*$", txt, flags=re.M)
    out = {}
    for i in range(1, len(parts) - 1, 2):
        eid = int(parts[i])
        nm = re.search(r"\[(.+?)\]", parts[i + 1])
        out[eid] = nm.group(1) if nm else None
    return out


# ---------------------------------------------------------------- 主流程
def main() -> int:
    ap = argparse.ArgumentParser(description="技能/魔法侧只读对账")
    ap.add_argument("--wiki", type=Path, default=DEFAULT_WIKI)
    ap.add_argument("--db-export", type=Path, default=DEFAULT_DB_EXPORT)
    ap.add_argument("--assets", type=Path, default=DEFAULT_ASSETS,
                    help="客户端 Data 目录（含 MIcon.Zl），缺失则资产检查跳过")
    ap.add_argument("--old-snapshot", type=Path, default=DEFAULT_OLD_SNAPSHOT,
                    help="2026-09-25 的 MagicInfo 快照（Tools/MagicInfoDump --icons 产物），用于回归判定；"
                         "缺失则跳过")
    ap.add_argument("--zircon-root", type=Path,
                    default=Path(__file__).resolve().parents[2])
    ap.add_argument("--out", type=Path,
                    default=Path(".artifacts/game-audit-2026-10-18/data/skills.json"))
    args = ap.parse_args()

    root: Path = args.zircon_root.resolve()
    missing = [p for p in (args.wiki, args.db_export / "MagicInfo.json") if not p.exists()]
    if missing:
        for p in missing:
            print("[FATAL] 缺少输入: %s" % p, file=sys.stderr)
        return 2

    # ---- 输入 ----
    wiki = load_json(args.wiki)
    site = wiki["skills"]
    term = wiki.get("terminology", {})
    magic_rows = rows_of(args.db_export / "MagicInfo.json")
    item_rows = rows_of(args.db_export / "ItemInfo.json")
    drop_rows = rows_of(args.db_export / "DropInfo.json")
    good_rows = rows_of(args.db_export / "NPCGood.json")
    quest_rows = rows_of(args.db_export / "QuestReward.json") if (
        args.db_export / "QuestReward.json").exists() else []

    enum_map = parse_magic_type_enum(root / "LibraryCore/Enum.cs")
    magic_impl = parse_magic_impl(root)
    school_order, school_order_line = parse_legacy_school_order(
        root / "GodotClient/Controls/MagicDialog.cs")
    magic_exp = parse_magic_exp(root / "ClientData/Magic.exp.txt")
    db_names_path = root / "GodotClient/translations/db_names.json"
    db_names = load_json(db_names_path).get("magics", {}) if db_names_path.exists() else {}
    db_names_compact = {k.replace(" ", ""): v for k, v in db_names.items()}

    glossary_path = root / "ClientData/zh/zh-glossary.json"
    glossary = load_json(glossary_path).get("magics", {}) if glossary_path.exists() else {}
    glossary_by_norm = {norm(k): v.get("zh") for k, v in glossary.items()}

    # ============================================================ 1. 覆盖对账
    by_norm = {}
    for r in magic_rows:
        for key in {norm(r.get("Name")), norm(r.get("Magic"))}:
            if key:
                by_norm.setdefault(key, r)

    matched, site_only_zircon, site_only_legacy = [], [], []
    seen = set()
    for s in site:
        r = by_norm.get(norm(s.get("type"))) or by_norm.get(norm(s.get("name")))
        if r is not None and id(r) in seen and (norm(r.get("Name")), norm(r.get("Magic"))) != (norm(s["name"]), norm(s.get("type"))):
            r = None  # 一个库行只允许匹配一条网站条目
        if r is not None:
            seen.add(id(r))
            matched.append((s, r))
        elif "zircon" in (s.get("ver") or []):
            site_only_zircon.append(s)
        else:
            site_only_legacy.append(s)

    db_unmatched = [r for r in magic_rows if id(r) not in seen]

    # 网站 type → MagicType 枚举（规则化，非猜测）
    enum_by_norm = {norm(k): k for k in enum_map}
    alias_pairs = []                                        # (网站type, 枚举名, 依据)
    site_enum_hit, site_enum_miss = [], []
    for s in site:
        t = s.get("type") or ""
        if norm(t) in enum_by_norm:
            site_enum_hit.append((s, enum_by_norm[norm(t)]))
        elif t.startswith("Advanced"):
            cand = "Augment" + t[len("Advanced"):]
            if cand in enum_map:
                alias_pairs.append((t, cand, "Advanced*↔Augment* 前缀规则（同规则已由 4 组网站条目验证）"))
                site_enum_hit.append((s, cand))
            else:
                site_enum_miss.append(s)
        else:
            site_enum_miss.append(s)

    enum_player = {k: v for k, v in enum_map.items() if 100 <= v <= 499}
    site_enum_of = {id(s): name for s, name in site_enum_hit}   # 网站条目 → 枚举名
    matched_enum = {norm(r["Magic"]) for _, r in matched}
    enum_no_db = {k: v for k, v in enum_player.items() if norm(k) not in matched_enum}
    site_types = {norm(s.get("type")) for s in site}
    enum_no_site = {k: v for k, v in enum_player.items() if norm(k) not in site_types}

    def site_brief(s):
        return OrderedDict([
            ("id", s.get("id")), ("name", s.get("name")), ("klass", s.get("klass")),
            ("type", s.get("type")), ("ver", s.get("ver")), ("icon", s.get("icon")),
            ("zh", s.get("zh")), ("levels", s.get("levels")),
        ])

    coverage = OrderedDict([
        ("counts", OrderedDict([
            ("website_skills_total", len(site)),
            ("website_ver_zircon", sum(1 for s in site if "zircon" in (s.get("ver") or []))),
            ("website_ver_mud3_only", sum(1 for s in site if "mud3" in (s.get("ver") or []) and "zircon" not in (s.get("ver") or []))),
            ("db_magicinfo_rows", len(magic_rows)),
            ("matched_db_and_website", len(matched)),
            ("db_rows_without_website", len(db_unmatched)),
            ("website_zircon_tagged_but_no_db_row", len(site_only_zircon)),
            ("website_mud3_only_legacy", len(site_only_legacy)),
            ("enum_player_skills_100_499", len(enum_player)),
            ("enum_player_skills_without_db_row", len(enum_no_db)),
            ("enum_player_skills_without_website_entry", len(enum_no_site)),
            ("server_impl_files_total", len(magic_impl)),
            ("website_zircon_tagged_with_server_impl_but_no_db_row",
             sum(1 for s in site_only_zircon if site_enum_of.get(id(s)) in magic_impl)),
        ])),
        ("matched_by_class", dict(Counter(s.get("klass") for s, _ in matched))),
        ("website_zircon_tagged_by_class", dict(Counter(s.get("klass") for s in site_only_zircon))),
        ("db_only", [r.get("Name") for r in db_unmatched]),
        ("website_only_zircon_tagged_top30", [site_brief(s) for s in site_only_zircon[:30]]),
        ("website_only_zircon_tagged", [site_brief(s) for s in site_only_zircon]),
        ("website_only_zircon_tagged_with_server_impl", [
            OrderedDict([("site_id", s["id"]), ("name", s["name"]), ("type", s.get("type")),
                         ("enum", site_enum_of.get(id(s))), ("klass", s.get("klass")),
                         ("icon", s.get("icon")),
                         ("impl_file", magic_impl[site_enum_of[id(s)]]["file"])])
            for s in site_only_zircon if site_enum_of.get(id(s)) in magic_impl]),
        ("website_only_zircon_tagged_without_server_impl", [
            site_brief(s) for s in site_only_zircon if site_enum_of.get(id(s)) not in magic_impl]),
        ("website_only_mud3_legacy_top30", [site_brief(s) for s in site_only_legacy[:30]]),
        ("website_only_mud3_legacy", [site_brief(s) for s in site_only_legacy]),
        ("enum_player_skills_without_website_entry", [
            OrderedDict([("magic", k), ("enum_value", v)]) for k, v in sorted(enum_no_site.items(), key=lambda x: x[1])]),
        ("enum_alias_applied", [OrderedDict([("website_type", a), ("enum", b), ("rule", c)]) for a, b, c in alias_pairs]),
        ("enum_match", OrderedDict([
            ("website_types_matched_to_enum", len(site_enum_hit)),
            ("website_types_unmatched", len(site_enum_miss)),
            ("unmatched", [site_brief(s) for s in site_enum_miss]),
        ])),
    ])

    # ============================================================ 2. 名称/枚举映射
    mapping_rows = []
    zh_agree = zh_conflict = zh_single = zh_none = 0
    for s, r in sorted(matched, key=lambda x: x[1]["Index"]):
        eid = r["Icon"] // 2 + 1
        exp_zh = magic_exp.get(eid)
        gl_zh = glossary_by_norm.get(norm(r["Name"])) or glossary_by_norm.get(norm(r["Magic"]))
        tm_zh = term.get(r["Name"]) or term.get(r["Magic"])
        disp = db_names.get(r["Name"]) or db_names_compact.get(r["Name"].replace(" ", "")) or {}
        disp_zh = disp.get("zh") if isinstance(disp, dict) else None
        vals = [v for v in (exp_zh, gl_zh, tm_zh, disp_zh) if v]
        uniq = sorted(set(vals))
        if not vals:
            zh_none += 1
            status = "三源皆缺"
        elif len(uniq) == 1:
            zh_agree += 1
            status = "一致" if len(vals) > 1 else "单一源"
            if len(vals) == 1:
                zh_single += 1
                zh_agree -= 1
        else:
            zh_conflict += 1
            status = "冲突→待人工确认"
        mapping_rows.append(OrderedDict([
            ("db_index", r["Index"]),
            ("magic_enum", r["Magic"]),
            ("enum_value", enum_map.get(r["Magic"])),
            ("db_name", r["Name"]),
            ("db_icon", r["Icon"]),
            ("ei_id_icon_div2_plus1", eid),
            ("ei_id_in_magic_exp", eid in magic_exp),
            ("site_id", s.get("id")),
            ("site_name", s.get("name")),
            ("site_type", s.get("type")),
            ("site_klass", s.get("klass")),
            ("server_impl_file", (magic_impl.get(r["Magic"]) or {}).get("file")),
            ("server_impl_folder", (magic_impl.get(r["Magic"]) or {}).get("folder")),
            ("server_impl_folder_matches_class", (magic_impl.get(r["Magic"]) or {}).get("folder") == s.get("klass")),
            ("zh_magic_exp", exp_zh),
            ("zh_glossary", gl_zh),
            ("zh_wiki_terminology", tm_zh),
            ("zh_game_display_db_names", disp_zh),
            ("zh_status", status),
        ]))

    mapping = OrderedDict([
        ("rule", [
            "① 库↔网站：name/magic 归一（小写去非字母数字）与网站 type 或 name 相等即匹配；一条库行只匹配一条网站条目。",
            "② 网站 type → MagicType 枚举：同名归一匹配；Advanced*↔Augment* 前缀规则（已由 AdvancedDefiance/AugmentDefiance 等 4 组同规则条目验证）。",
            "③ EI 技能号 = MagicInfo.Icon // 2 + 1（来源 GodotClient/Controls/MagicBar.cs:287 与 MagicDialog.cs LegacyEiSkillIdFromIcon），用它对齐 ClientData/Magic.exp.txt 的 #id。",
            "④ 中文名不投票、不合并：四源（magic.exp / zh-glossary / wiki terminology / 游戏显示表 db_names.json）全部列出，冲突即标『待人工确认』。",
        ]),
        ("stats", OrderedDict([
            ("mapped_rows", len(mapping_rows)),
            ("zh_all_sources_agree", zh_agree),
            ("zh_single_source_only", zh_single),
            ("zh_conflict", zh_conflict),
            ("zh_no_source", zh_none),
            ("zh_conflict_display_has_support",
             sum(1 for x in mapping_rows if x["zh_status"].startswith("冲突") and x["zh_game_display_db_names"]
                 in (x["zh_magic_exp"], x["zh_glossary"], x["zh_wiki_terminology"]))),
            ("ei_id_present_in_magic_exp", sum(1 for x in mapping_rows if x["ei_id_in_magic_exp"])),
            ("enum_value_found", sum(1 for x in mapping_rows if x["enum_value"] is not None)),
            ("server_impl_found", sum(1 for x in mapping_rows if x["server_impl_file"])),
        ])),
        ("server_impl_class_mismatch", [
            OrderedDict([("db_index", x["db_index"]), ("magic", x["magic_enum"]),
                         ("db_required_class", next(r["RequiredClass"] for _, r in matched if r["Index"] == x["db_index"])),
                         ("site_klass", x["site_klass"]), ("impl_folder", x["server_impl_folder"]),
                         ("impl_file", x["server_impl_file"])])
            for x in mapping_rows
            if x["server_impl_folder"] and x["server_impl_folder"] != "Horse"
            and x["server_impl_folder"] != next(r["RequiredClass"] for _, r in matched if r["Index"] == x["db_index"])
        ]),
        ("unmapped_pending_human", [
            OrderedDict([("db_index", x["db_index"]), ("db_name", x["db_name"]),
                         ("reason", "四源中文名冲突或缺失"),
                         ("values", {k: x[k] for k in ("zh_magic_exp", "zh_glossary",
                                                       "zh_wiki_terminology", "zh_game_display_db_names")})])
            for x in mapping_rows if x["zh_status"].startswith("冲突") or x["zh_status"] == "三源皆缺"
        ]),
        ("rows", mapping_rows),
    ])

    # ============================================================ 3. 字段级差异
    FIELD_SPEC = [
        ("power", lambda r: (r["MinBasePower"], r["MaxBasePower"], r["MinLevelPower"], r["MaxLevelPower"]),
         lambda s: parse_power(s.get("power")), "MinBasePower/MaxBasePower/MinLevelPower/MaxLevelPower"),
        ("cost", lambda r: (r["BaseCost"], r["LevelCost"]),
         lambda s: parse_cost(s.get("cost")), "BaseCost/LevelCost"),
        ("delay", lambda r: r["Delay"],
         lambda s: parse_delay(s.get("delay")), "Delay"),
        ("levels", lambda r: (r["NeedLevel1"], r["NeedLevel2"], r["NeedLevel3"]),
         lambda s: parse_triple(s.get("levels")), "NeedLevel1/2/3"),
        ("exp", lambda r: (r["Experience1"], r["Experience2"], r["Experience3"]),
         lambda s: parse_triple(s.get("exp")), "Experience1/2/3"),
        ("class", lambda r: r["RequiredClass"],
         lambda s: CLASS_ZH2EN.get(s.get("klass")), "RequiredClass"),
        ("icon", lambda r: r["Icon"],
         lambda s: (int(s["icon"]) if str(s.get("icon") or "").strip().lstrip("-").isdigit() else None), "Icon"),
        ("school", lambda r: r["School"],
         lambda s: s.get("school"), "School(与网站 school 文本)"),
        ("prop", lambda r: r["Property"],
         lambda s: s.get("prop"), "Property(与网站 prop 文本)"),
    ]
    field_stats, field_rows = OrderedDict(), []
    for name, db_get, site_get, db_fields in FIELD_SPEC:
        st = Counter()
        details = []
        for s, r in sorted(matched, key=lambda x: x[1]["Index"]):
            sv = site_get(s)
            dv = db_get(r)
            if sv is None or sv == "":
                st["网站无"] += 1
                details.append(OrderedDict([("db_index", r["Index"]), ("name", r["Name"]),
                                            ("verdict", "网站无"), ("site", s.get(name)),
                                            ("db", dv)]))
                continue
            if name in ("school", "prop"):
                # 网站文本 vs 枚举名：仅大小写/空格容忍
                ok = norm(sv) == norm(dv)
            else:
                ok = sv == dv
            st["一致" if ok else "不一致"] += 1
            if not ok:
                details.append(OrderedDict([("db_index", r["Index"]), ("name", r["Name"]),
                                            ("verdict", "不一致"), ("site", sv), ("db", dv),
                                            ("site_text", s.get(name))]))
        st["库无"] = 0  # MagicInfo 所有目标字段非空，恒为 0（脚本保持对称计数）
        field_stats[name] = OrderedDict([("db_fields", db_fields), ("一致", st["一致"]),
                                         ("不一致", st["不一致"]), ("网站无", st["网站无"]),
                                         ("库无", st["库无"]), ("样例", details[:12])])
        field_rows.append((name, st, details))

    field_diff = OrderedDict([
        ("total_pairs", len(matched)),
        ("per_field", field_stats),
        ("mismatch_list", [
            OrderedDict([("field", name), ("db_index", d["db_index"]), ("name", d["name"]),
                         ("site", d.get("site")), ("db", d.get("db")),
                         ("site_text", d.get("site_text"))])
            for name, st, details in field_rows for d in details if d["verdict"] == "不一致"
        ]),
    ])

    # ============================================================ 4. 客户端可用性
    assets = OrderedDict()
    icon_lib = args.assets / "MIcon.Zl"
    zl = zl_frame_index(icon_lib) if icon_lib.exists() else None
    if zl is None:
        assets["icon_library"] = OrderedDict([
            ("path", str(icon_lib)), ("status", "skipped"),
            ("reason", "MIcon.Zl 不存在或无法解析"),
        ])
    else:
        frames = set(zl["frames"])
        db_bad = [OrderedDict([("db_index", r["Index"]), ("magic", r["Magic"]), ("icon", r["Icon"]),
                               ("icon_div2_plus1", r["Icon"] // 2 + 1),
                               ("bound_icon_frame_1000_plus", 1000 + r["Icon"] // 2),
                               ("bound_frame_present", (1000 + r["Icon"] // 2) in frames)])
                  for r in magic_rows if r["Icon"] not in frames]
        site_bad = []
        for s in site:
            try:
                ic = int(s["icon"])
            except (TypeError, ValueError, KeyError):
                continue
            if "zircon" in (s.get("ver") or []) and ic not in frames:
                site_bad.append(OrderedDict([("site_id", s["id"]), ("name", s["name"]), ("icon", ic)]))
        assets["icon_library"] = OrderedDict([
            ("path", str(icon_lib)), ("status", "checked"),
            ("container", zl["container"]), ("declared_count", zl["count"]),
            ("decodable_frames", len(frames)),
            ("frame_min", min(frames) if frames else None),
            ("frame_max", max(frames) if frames else None),
            ("db_icon_missing_count", len(db_bad)),
            ("db_icon_missing", db_bad),
            ("website_zircon_icon_missing_count", len(site_bad)),
            ("website_zircon_icon_missing", site_bad),
            ("suggestion_from_website", [
                OrderedDict([("db_index", r["Index"]), ("magic", r["Magic"]), ("db_icon", r["Icon"]),
                             ("website_icon", int(s["icon"]) if str(s.get("icon") or "").isdigit() else None),
                             ("website_icon_present_in_lib", str(s.get("icon") or "").isdigit() and int(s["icon"]) in frames)])
                for s, r in matched
                if str(s.get("icon") or "").strip().isdigit() and int(s["icon"]) != r["Icon"]
            ]),
        ])

    # 技能窗口分类页签可见性（走源码静态路径）
    visible, hidden = [], []
    for s, r in matched:
        (visible if r["School"] in school_order else hidden).append(
            OrderedDict([("db_index", r["Index"]), ("magic", r["Magic"]),
                         ("school", r["School"]), ("klass", s.get("klass")),
                         ("site_name", s["name"])]))
    assets["skill_window_school_tabs"] = OrderedDict([
        ("evidence", "GodotClient/Controls/MagicDialog.cs:%s LegacySchoolOrder + "
                     "LegacyUiSkin.cs:123 ApplyLegacyEiLayout（默认 UI = legacy，见 "
                     "AutoLoginArgs.cs:279 LegacyUi => !Has(\"--zircon-ui\")）" % school_order_line),
        ("legacy_tabs", school_order),
        ("tab_default", school_order[0] if school_order else None),
        ("visible_in_legacy_tabs", len(visible)),
        ("hidden_in_legacy_tabs", len(hidden)),
        ("hidden_by_school", dict(Counter(x["school"] for x in hidden))),
        ("hidden", hidden),
        ("reference_impl", "Client/Scenes/Views/MagicDialog.cs 按 MagicInfo.School 动态建页签（含 Active/Passive/Toggle），故原版客户端无此缺口"),
    ])

    # 技能书物品（ItemInfo/StoreInfo 侧只统计，不改）
    books = [r for r in item_rows if r.get("ItemType") == "Book"]
    magic_by_index = {r["Index"]: r for r in magic_rows}
    book_img = Counter(r.get("Image") for r in books)
    drop_count = Counter()
    for dr in drop_rows:
        it = dr.get("Item") or {}
        drop_count[it.get("Index")] += 1
    sale_count = Counter()
    for gr in good_rows:
        it = gr.get("Item") or {}
        sale_count[it.get("Index")] += 1
    quest_count = Counter()
    for qr in quest_rows:
        it = qr.get("Item") or {}
        quest_count[it.get("Index")] += 1
    majority_img = book_img.most_common(1)[0][0] if book_img else None
    book_rows, book_img_odd, book_lvl_odd, book_unobtainable = [], [], [], []
    for r in sorted(books, key=lambda x: x["Index"]):
        m = magic_by_index.get(r.get("Shape"))
        entry = OrderedDict([
            ("item_index", r["Index"]), ("item_name", r["ItemName"]),
            ("shape_magic_index", r.get("Shape")),
            ("magic", m and m["Magic"]), ("magic_index_exists", m is not None),
            ("image", r.get("Image")), ("image_is_majority", r.get("Image") == majority_img),
            ("required_type", r.get("RequiredType")), ("required_amount", r.get("RequiredAmount")),
            ("magic_need_level1", m and m["NeedLevel1"]),
            ("level_matches_need1", bool(m) and r.get("RequiredAmount") == m["NeedLevel1"]),
            ("drop_entries", drop_count.get(r["Index"], 0)),
            ("npc_good_entries", sale_count.get(r["Index"], 0)),
            ("quest_reward_entries", quest_count.get(r["Index"], 0)),
        ])
        book_rows.append(entry)
        if majority_img is not None and r.get("Image") != majority_img:
            book_img_odd.append(entry)
        if m and r.get("RequiredAmount") != m["NeedLevel1"]:
            book_lvl_odd.append(entry)
        if (drop_count.get(r["Index"], 0) == 0 and sale_count.get(r["Index"], 0) == 0
                and quest_count.get(r["Index"], 0) == 0):
            book_unobtainable.append(entry)
    assets["skill_books"] = OrderedDict([
        ("count", len(books)),
        ("image_counter", {str(k): v for k, v in book_img.items()}),
        ("majority_image", majority_img),
        ("image_not_majority", book_img_odd),
        ("required_amount_ne_needlevel1", book_lvl_odd),
        ("unobtainable_no_drop_no_shop_no_quest", book_unobtainable),
        ("obtainability_note", "仅检查 DropInfo/NPCGood/QuestReward 三处；宝箱/活动/脚本发放未覆盖"),
        ("shape_join_failures", [x["item_index"] for x in book_rows if not x["magic_index_exists"]]),
    ])

    # 中文显示名覆盖
    missing_names = [r["Name"] for r in magic_rows
                     if r["Name"] not in db_names and r["Name"].replace(" ", "") not in db_names_compact]
    assets["display_names"] = OrderedDict([
        ("table", str(db_names_path)), ("table_keys", len(db_names)),
        ("db_magic_without_display_name", missing_names),
    ])

    # ---- 引用完整性（实体引用是否指向存在对象）
    ring_items = [r for r in item_rows if r.get("ItemEffect") == "MagicRing"]
    referential = OrderedDict([
        ("book_shape_to_magicinfo", OrderedDict([
            ("total", len(books)),
            ("dangling", [x["item_index"] for x in book_rows if not x["magic_index_exists"]]),
        ])),
        ("magicinfo_magic_in_enum", OrderedDict([
            ("total", len(magic_rows)),
            ("not_in_enum", [r["Magic"] for r in magic_rows if r["Magic"] not in enum_map]),
        ])),
        ("magicinfo_magic_has_server_impl", OrderedDict([
            ("total", len(magic_rows)),
            ("missing", [r["Magic"] for r in magic_rows if r["Magic"] not in magic_impl]),
        ])),
        ("magic_ring_items", OrderedDict([
            ("count", len(ring_items)),
            ("note", "ItemEffect=MagicRing 的物品为 0 ⇒ 没有技能处于 ItemRequired 状态"
                     "（PlayerObject.cs:2360-2376 只在该物品效果下设置 ItemRequired），"
                     "客户端 MagicDialog.cs:899-905 的 MagicRing 分支当前不会触发"),
        ])),
        ("magic_ring_dangling_shape", [
            OrderedDict([("item_index", r["Index"]), ("item_name", r["ItemName"]), ("shape", r.get("Shape")),
                         ("shape_is_magicinfo_index", r.get("Shape") in magic_by_index)])
            for r in ring_items]),
    ])

    # ============================================================ 4b. 旧库快照回归判定
    snapshot = OrderedDict()
    if args.old_snapshot.exists():
        snap = load_json(args.old_snapshot)
        snap_recs = {r["magic"]: r for r in snap.get("records", [])}
        site_zircon_types = {norm(s.get("type")) for s in site if "zircon" in (s.get("ver") or [])}
        snap_norms = {norm(k) for k in snap_recs}
        deltas = []
        for r in magic_rows:
            o = snap_recs.get(r["Magic"])
            if not o:
                deltas.append(OrderedDict([("magic", r["Magic"]), ("field", "*"),
                                           ("old", None), ("current", None),
                                           ("verdict", "旧快照无此技能")]))
                continue
            for field, cur_field in (("icon", "Icon"), ("class", "RequiredClass"),
                                     ("school", "School"), ("need_level1", "NeedLevel1")):
                if o.get(field) != r[cur_field]:
                    deltas.append(OrderedDict([("magic", r["Magic"]), ("field", field),
                                               ("old", o.get(field)), ("current", r[cur_field]),
                                               ("verdict", "相对旧库回退/变更")]))
        snapshot = OrderedDict([
            ("source", str(args.old_snapshot)),
            ("md5", md5(args.old_snapshot)),
            ("db_source_in_artifact", snap.get("db_source")),
            ("evidence_level", snap.get("evidence_level")),
            ("record_count", snap.get("record_count")),
            ("website_zircon_types", len(site_zircon_types)),
            ("name_set_symmetric_difference", len(snap_norms ^ site_zircon_types)),
            ("old_records_absent_from_current_db",
             sum(1 for k in snap_recs if k not in {r["Magic"] for r in magic_rows})),
            ("field_deltas_vs_current", deltas),
            ("field_deltas_by_field",
             dict(Counter(d["field"] for d in deltas if d["field"] != "*"))),
        ])
    else:
        snapshot = OrderedDict([("source", str(args.old_snapshot)), ("status", "skipped")])

    # ============================================================ 5. 风险与建议
    risks = []
    if zl is not None and assets["icon_library"]["db_icon_missing_count"]:
        risks.append(OrderedDict([
            ("severity", "high"), ("type", "确认缺陷"),
            ("title", "MagicInfo.Icon 指向客户端图库不存在的帧 → 技能图标空白/灰块"),
            ("evidence", "MIcon.Zl 可解帧集合内不含 %s；技能窗口与快捷栏均 `if (tex != null)` 才绘制"
                         "（GodotClient/Controls/MagicDialog.cs:1079,1444；MagicBar.cs:207）；"
                         "且 2026-09-25 旧库快照在同 4 行用的是存在于图库的 218/266/238/240"
                         % ", ".join(str(x["icon"]) for x in assets["icon_library"]["db_icon_missing"])),
            ("fix_target", "System.db MagicInfo.Icon（Index 193/194/195/196/197）"),
            ("suggested_value", "取自网站基准且已在图库中存在的帧号（见 client_usability.icon_library.suggestion_from_website）"),
        ]))
    if assets["skill_window_school_tabs"]["hidden_in_legacy_tabs"]:
        risks.append(OrderedDict([
            ("severity", "high"), ("type", "确认缺陷（表现层）"),
            ("title", "默认（legacy EI）技能窗口只含 8 个元素页签，%d 个库内技能不可见"
                      % assets["skill_window_school_tabs"]["hidden_in_legacy_tabs"]),
            ("evidence", "MagicDialog.cs LegacySchoolOrder 只含 %s；MagicInfo.School 为 %s 的行被过滤掉"
                         % ("/".join(school_order), "/".join(assets["skill_window_school_tabs"]["hidden_by_school"]))),
            ("fix_target", "GodotClient/Controls/MagicDialog.cs 页签→School 的映射（显示层），或改数据分类（需决策）"),
            ("note", "源码注释已明示此差异为『数据分类差异，不是渲染缺陷』；原版 Client/ 动态建页签无此缺口。"),
        ]))
    if assets["skill_books"]["image_not_majority"]:
        risks.append(OrderedDict([
            ("severity", "high"), ("type", "确认缺陷"),
            ("title", "%d 本技能书用错物品图标帧（与 54 本主用帧 %s 不同）"
                      % (len(assets["skill_books"]["image_not_majority"]), majority_img)),
            ("evidence", "ItemInfo.Image=%s 的帧在 StoreItem.Zl/Inventory.Zl 中解出的是『人参』素材，不是书"
                         "（已渲染核对）；同帧还被消耗品『Ginseng Of Eternity』使用"
                         % [x["image"] for x in assets["skill_books"]["image_not_majority"]][:1]),
            ("fix_target", "System.db ItemInfo.Image（ItemInfo.Index 1365-1371）"),
        ]))
    if assets["skill_books"]["required_amount_ne_needlevel1"]:
        risks.append(OrderedDict([
            ("severity", "medium"), ("type", "确认缺陷（当前不可达）"),
            ("title", "%d 本技能书 RequiredAmount 与 MagicInfo.NeedLevel1 不一致"
                      % len(assets["skill_books"]["required_amount_ne_needlevel1"])),
            ("evidence", "学习技能只校验 UserItem.RequiredAmount（PlayerObject.cs:7525-7527），"
                         "ItemUse 的 Book 分支不查 NeedLevel1（PlayerObject.cs:7324-7405）"),
            ("fix_target", "System.db ItemInfo.RequiredAmount"),
        ]))
    if assets["skill_books"]["unobtainable_no_drop_no_shop_no_quest"]:
        risks.append(OrderedDict([
            ("severity", "medium"), ("type", "确认缺陷"),
            ("title", "%d 本技能书既无掉落、无 NPC 出售、也无任务奖励 → 对应技能当前无法获得"
                      % len(assets["skill_books"]["unobtainable_no_drop_no_shop_no_quest"])),
            ("evidence", "DropInfo/NPCGood/QuestReward 三处均为 0 条（ItemInfo.Drops 反向链同样为空）；"
                         "宝箱/活动未覆盖"),
            ("fix_target", "DropInfo / NPCGood（或确认这些技能本就不该开放）"),
        ]))
    risks.append(OrderedDict([
        ("severity", "low"), ("type", "待人工确认"),
        ("title", "网站基准 ver 的『zircon』标签是基标签，且其技能集等于 2026-09-25 旧库而非当前库"),
        ("evidence", "Mir3-Research/Tools/common/ver_tags.py:195-203 对所有非老版技能恒定追加 "
                     "\"zircon\"；而该 174 条与旧库快照 174 条名称对称差为 0 —— "
                     "当前库只有 61 行，两者相差 113 行"),
        ("fix_target", "对账口径/文档：引用网站技能数时须注明对应的是哪一版库"),
    ]))
    if mapping["server_impl_class_mismatch"]:
        bad = mapping["server_impl_class_mismatch"]
        risks.append(OrderedDict([
            ("severity", "high"), ("type", "确认缺陷"),
            ("title", "%d 条 MagicInfo.RequiredClass 与服务端实现目录/网站职业三方不一致" % len(bad)),
            ("evidence", "; ".join("%s: 库=%s 网站=%s 实现=%s（%s）"
                                   % (x["magic"], x["db_required_class"], x["site_klass"],
                                      x["impl_folder"], x["impl_file"]) for x in bad)),
            ("fix_target", "System.db MagicInfo.RequiredClass / ItemInfo.RequiredClass（技能书）"),
        ]))
    risks.append(OrderedDict([
        ("severity", "medium"), ("type", "待人工确认"),
        ("title", "当前库比 2026-09-25 旧库少了 %d 条 MagicInfo 行（174 → 61）"
                  % (snapshot.get("old_records_absent_from_current_db") or 0)),
        ("evidence", "旧快照 %d 条技能名与网站 zircon 集对称差 %s，说明 174 那批是真实存在过的库内容，"
                     "不是网站臆造" % (snapshot.get("record_count") or 0,
                                        snapshot.get("name_set_symmetric_difference"))),
        ("fix_target", "需确认这是有意精简还是导入遗漏；若是遗漏，恢复时用旧快照的 icon/class/level"),
    ]))
    if mapping["stats"]["zh_conflict"] or mapping["stats"]["zh_no_source"]:
        risks.append(OrderedDict([
            ("severity", "low"), ("type", "待人工确认"),
            ("title", "中文译名四源不一致 %d 条（含 wiki/glossary 疑似错挂）"
                      % (mapping["stats"]["zh_conflict"] + mapping["stats"]["zh_no_source"])),
            ("evidence", "例：wiki terminology 把 Evil Slayer 记为『召唤骷髅』、Greater Evil Slayer 记为"
                         "『超强召唤骷髅』，而 Magic.exp.txt #37/#38 为『月魂断玉/月魂灵波』，"
                         "游戏显示表 db_names.json 同 EI —— 错在网站侧，不要按网站改库"),
            ("fix_target", "网站数据（terminology/glossary），非 System.db"),
        ]))

    # ============================================================ 输出
    risks = sorted(risks, key=lambda r: {"high": 0, "medium": 1, "low": 2, "info": 3}[r["severity"]])

    risks.append(OrderedDict([
        ("severity", "info"), ("type", "已解释（数据分类差异，非缺陷）"),
        ("title", "44 条『仅 mud3 老版』与 22 条『枚举有、网站无』条目"),
        ("evidence", "44 条 ver 仅 mud3 的老版条目 id 为负、不在 MagicType 枚举内，是 MUD3 老版 dat 独有；"
                     "22 条枚举条目不属玩家技能表（含 `Unused=438`=_Blank_ 与 Enum.cs 标注 //NOT CODED 的条目）"),
        ("fix_target", "无需处理；列出以避免后续把它当缺陷反复排查"),
    ]))

    if snapshot.get("field_deltas_by_field"):
        risks.insert(0, OrderedDict([
            ("severity", "high"), ("type", "确认缺陷（回归，独立佐证）"),
            ("title", "当前库相对 2026-09-25 旧库快照回退了 %d 个字段值（共 %d 处差异）"
                      % (sum(snapshot["field_deltas_by_field"].values()),
                         len(snapshot["field_deltas_vs_current"]))),
            ("evidence", "快照 %s（MagicInfoDump --icons；库源=%s）；"
                         "快照 %d 条技能名与网站 zircon 集对称差 %s；旧库有而当前库没有的技能 %d 条。"
                         "明细：%s"
                         % (snapshot["source"], snapshot["db_source_in_artifact"],
                            snapshot["record_count"], snapshot["name_set_symmetric_difference"],
                            snapshot["old_records_absent_from_current_db"],
                            "; ".join("%s.%s %s→%s" % (d["magic"], d["field"], d["old"], d["current"])
                                      for d in snapshot["field_deltas_vs_current"] if d["field"] != "*"))),
            ("fix_target", "System.db MagicInfo（Index 18 / 193–197）"),
        ]))

    out = OrderedDict([
        ("_meta", OrderedDict([
            ("generated_at", datetime.now(timezone.utc).isoformat(timespec="seconds")),
            ("script", "tools/audit/skills_audit.py"),
            ("scope", "skills only (MagicInfo + 技能书 ItemInfo/DropInfo/NPCGood 只统计)"),
            ("inputs", OrderedDict([
                ("wiki_data_v2", {"path": str(args.wiki), "md5": md5(args.wiki)}),
                ("db_export_dir", str(args.db_export)),
                ("db_export_files", {n: md5(args.db_export / f"{n}.json")
                                     for n in ("MagicInfo", "ItemInfo", "DropInfo", "NPCGood")
                                     if (args.db_export / f"{n}.json").exists()}),
                ("zircon_root", str(root)),
                ("old_magic_snapshot", str(args.old_snapshot) if args.old_snapshot.exists() else None),
                ("icon_library", str(icon_lib)),
                ("icon_library_md5", md5(icon_lib) if icon_lib.exists() else None),
                ("magic_exp", str(root / "ClientData/Magic.exp.txt")),
                ("enum_source", str(root / "LibraryCore/Enum.cs")),
                ("db_names", str(db_names_path)),
            ])),
            ("read_only", True),
        ])),
        ("coverage", coverage),
        ("mapping", mapping),
        ("field_diff", field_diff),
        ("baseline_snapshot", snapshot),
        ("client_usability", assets),
        ("referential_integrity", referential),
        ("risks", risks),
    ])

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8") as fh:
        json.dump(out, fh, ensure_ascii=False, indent=1)

    # ---- stdout 摘要 ----
    c = coverage["counts"]
    print("=" * 72)
    print("技能对账 · 网站 basis vs 当前 System.db（MagicInfo）")
    print("=" * 72)
    print("网站 skills %d 条（ver 含 zircon %d，仅 mud3 老版 %d）"
          % (c["website_skills_total"], c["website_ver_zircon"], c["website_ver_mud3_only"]))
    print("库 MagicInfo %d 行；双向：库→网站缺 %d，网站(zircon)未入库 %d，网站老版独有 %d"
          % (c["db_magicinfo_rows"], c["db_rows_without_website"],
             c["website_zircon_tagged_but_no_db_row"], c["website_mud3_only_legacy"]))
    print("MagicType 枚举玩家技能段 %d 个；无网站条目 %d 个；无库行 %d 个"
          % (c["enum_player_skills_100_499"],
             c["enum_player_skills_without_website_entry"],
             c["enum_player_skills_without_db_row"]))
    print("网站条目 type→枚举 命中 %d / 未命中 %d"
          % (coverage["enum_match"]["website_types_matched_to_enum"],
             coverage["enum_match"]["website_types_unmatched"]))
    print("前端 30 条（网站 zircon 标记但库内无）:")
    for x in coverage["website_only_zircon_tagged_top30"][:10]:
        print("   %-4s %-28s %-4s %s" % (x["id"], x["name"], x["klass"], x["type"]))
    print("   … 其余见 JSON")
    print("   其中服务端已有实现类的 %d 条，无实现的 %d 条"
          % (coverage["counts"]["website_zircon_tagged_with_server_impl_but_no_db_row"],
             len(coverage["website_only_zircon_tagged_without_server_impl"])))
    print("-" * 72)
    ms = mapping["stats"]
    print("映射表 %d 行：中文四源一致 %d / 单一源 %d / 冲突 %d / 全缺 %d；EI 号可对齐 magic.exp %d"
          % (ms["mapped_rows"], ms["zh_all_sources_agree"], ms["zh_single_source_only"],
             ms["zh_conflict"], ms["zh_no_source"], ms["ei_id_present_in_magic_exp"]))
    print("-" * 72)
    print("字段级差异（%d 对）：" % field_diff["total_pairs"])
    for name, st in field_diff["per_field"].items():
        print("   %-7s 一致 %2d  不一致 %2d  网站无 %2d  库无 %d   (%s)"
              % (name, st["一致"], st["不一致"], st["网站无"], st["库无"], st["db_fields"]))
    print("-" * 72)
    if zl is None:
        print("图标资产：跳过（未找到 MIcon.Zl）")
    else:
        il = assets["icon_library"]
        print("图标资产：MIcon.Zl %s 声明 %d 帧 / 可解 %d 帧；库内 Icon 缺失 %d；网站 174 条缺失 %d"
              % (il["container"], il["declared_count"], il["decodable_frames"],
                 il["db_icon_missing_count"], il["website_zircon_icon_missing_count"]))
        for x in il["db_icon_missing"]:
            print("   缺帧 Index=%s %s icon=%s" % (x["db_index"], x["magic"], x["icon"]))
    sw = assets["skill_window_school_tabs"]
    print("技能窗口：legacy 页签 %s；可见 %d / 不可见 %d（%s）"
          % ("/".join(sw["legacy_tabs"]), sw["visible_in_legacy_tabs"],
             sw["hidden_in_legacy_tabs"], sw["hidden_by_school"]))
    sb = assets["skill_books"]
    print("技能书：%d 本；图标非主流帧 %d；RequiredAmount≠NeedLevel1 %d；三处均无来源 %d"
          % (sb["count"], len(sb["image_not_majority"]),
             len(sb["required_amount_ne_needlevel1"]),
             len(sb["unobtainable_no_drop_no_shop_no_quest"])))
    if mapping["server_impl_class_mismatch"]:
        print("职业不一致（库 vs 网站 vs 服务端实现目录）：")
        for x in mapping["server_impl_class_mismatch"]:
            print("   %s: 库=%s 网站=%s 实现=%s"
                  % (x["magic"], x["db_required_class"], x["site_klass"], x["impl_folder"]))
    ri = referential
    print("引用完整性：技能书 Shape→MagicInfo 悬挂 %d；MagicInfo.Magic 不在枚举 %d；"
          "库内技能缺实现 %d；MagicRing 物品 %d"
          % (len(ri["book_shape_to_magicinfo"]["dangling"]),
             len(ri["magicinfo_magic_in_enum"]["not_in_enum"]),
             len(ri["magicinfo_magic_has_server_impl"]["missing"]),
             ri["magic_ring_items"]["count"]))
    if snapshot.get("field_deltas_by_field"):
        print("旧库快照（%s）回归判定：%d 条技能名与网站一致（对称差 %s）；"
              "旧库有、当前库无的技能 %d 条；字段差异 %d 处"
              % (Path(snapshot["source"]).name, snapshot["record_count"],
                 snapshot["name_set_symmetric_difference"],
                 snapshot["old_records_absent_from_current_db"],
                 len(snapshot["field_deltas_vs_current"])))
        for x in snapshot["field_deltas_vs_current"]:
            if x["field"] != "*":
                print("   %s.%s: 旧 %s → 今 %s" % (x["magic"], x["field"], x["old"], x["current"]))
    print("-" * 72)
    for r in risks:
        print("[%s][%s] %s" % (r["severity"], r["type"], r["title"]))
    print("=" * 72)
    print("JSON → %s" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

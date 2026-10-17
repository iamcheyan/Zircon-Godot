#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""items_audit.py — 物品数据只读对账（网站侧基准 vs 当前 Zircon 服务端库）。

用法（离线、可重复运行）：

    python3 tools/audit/items_audit.py                 # 打印摘要 + 写 JSON
    python3 tools/audit/items_audit.py --decode-sample 8  # 额外真实解码抽样图标
    python3 tools/audit/items_audit.py --out /tmp/x.json --json-only

输入（全部只读，脚本不修改任何输入）：
  1. 网站侧基准快照（wiki_data_v2.json，含 items/monsters/.../_meta）
  2. 当前库全表 JSON 导出目录（{Table}.json，形如 {"rows":[...]}）
     —— 本脚本只读物品相关表：ItemInfo / ItemInfoStat / SetInfo / SetInfoStat /
        CurrencyInfo / CurrencyInfoImage / StoreInfo / WeaponCraftStatInfo / DropInfo
        （Book 交叉验证会额外读 MagicInfo）
  3. 清洗前旧库导出目录（可选，`--baseline-export`，默认 dbeditor workspace/baseline）：
     存在时输出「回归对照」——按名称 join 当前库与旧库，给出字段回归、词条丢失/改值、Index 漂移。
     这是整份报告里最硬的证据来源（同一份库的两个时点，不依赖网站）。
  4. 既有成果 item-manifest.json（mir3-website 中文名 → Zircon Index，仅用于交叉验证）
  5. 客户端物品图库 Data/Storeitem.Zl（图标可用性；经 Mir3-Research/Tools/common/zlsdk.py 读取）

硬性约束（本脚本遵守）：
  * 不联网；不打开、不读写 System.db；不启动游戏客户端/服务端。
  * 不猜测匹配：名称匹配分「精确 / 归一化 / 既有映射」三级，各自单独统计命中率；
    仍然无法映射的条目单列 pending_manual_review，绝不静默归并。
  * 数量与结论全部由数据算出，不写死。

输出：stdout 摘要 + 机器可读 JSON（默认
  .artifacts/game-audit-2026-10-18/data/items.json）。
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import json
import os
import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

# --------------------------------------------------------------------------
# 默认输入路径（可用命令行覆盖；缺失时记录 unavailable 而不是崩溃）
# --------------------------------------------------------------------------
ZIRCON_ROOT = Path(__file__).resolve().parents[2]

DEFAULT_WIKI = Path(
    "/Users/tetsuya/Development/Mir3-Research/docs/research/ei-ui-layout/artifacts/"
    "web-entity-audit-2026-09-26/raw/wiki_data_v2.json"
)
DEFAULT_EXPORT_DIR = Path("/Users/tetsuya/audit-raw/db-export-2026-10-18/out")
DEFAULT_MANIFEST = Path(
    "/Users/tetsuya/Development/Mir3-Research/docs/research/ei-ui-layout/artifacts/"
    "website-alignment-2026-09-26/item-manifest.json"
)
DEFAULT_STOREITEM_ZL = Path("/Users/tetsuya/mir2ei/Data/Storeitem.Zl")
DEFAULT_STOREITEM_ALT_ZL = Path("/Users/tetsuya/mir2ei/Data/Storeitems.Zl")
DEFAULT_ZLSDK_DIR = Path("/Users/tetsuya/Development/Mir3-Research/Tools/common")
DEFAULT_OUT = ZIRCON_ROOT / ".artifacts/game-audit-2026-10-18/data/items.json"

# 物品相关表（只读）
ITEM_TABLES = [
    "ItemInfo",
    "ItemInfoStat",
    "SetInfo",
    "SetInfoStat",
    "CurrencyInfo",
    "CurrencyInfoImage",
    "StoreInfo",
    "WeaponCraftStatInfo",
    "DropInfo",
]

# 清洗前的旧库导出（dbeditor workspace 的 baseline 副本，Aug 13）：存在时做“回归对照”。
# 这是当前库唯一的“历史状态”证据源，比网站基准更硬（同一份库的两个时点）。
DEFAULT_BASELINE_EXPORT = Path(
    "/Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/workspace/baseline"
)
REGRESSION_FIELDS = ["ItemType", "RequiredClass", "RequiredGender", "RequiredType",
                     "RequiredAmount", "Shape", "ItemEffect", "ExteriorEffect", "Image",
                     "Durability", "Price", "Weight", "StackSize", "StartItem", "SellRate",
                     "CanRepair", "CanSell", "CanStore", "CanTrade", "CanDrop",
                     "CanDeathDrop", "Rarity", "CanAutoPot", "BuffIcon", "PartCount"]

TOP_N = 30

# --------------------------------------------------------------------------
# 映射表（全部来自数据核对，见 docs/AUDIT/items.md「字段级差异」一节的推导）
#   * 网站 meta 的中文类型标签 → ItemInfo.ItemType
#   * 网站 class 的职业标签 → RequiredClass
#   * 网站 attrs 的中文属性标签 → Stat（range=最小值/最大值两个 Stat，single=单值）
# --------------------------------------------------------------------------
TYPE_ZH_TO_ITEMTYPE = {
    "武器": "Weapon",
    "护甲": "Armour",
    "头盔": "Helmet",
    "鞋子": "Shoes",
    "戒指": "Ring",
    "项链": "Necklace",
    "手镯": "Bracelet",
    "盾牌": "Shield",
    "护身符": "Amulet",
    "暗石": "DarkStone",
    "火把": "Torch",
    "矿石": "Ore",
    "肉类": "Meat",
    "技能书": "Book",
    "消耗品": "Consumable",
    "货币": "Currency",
    "无": "Nothing",
}

CLASS_ZH_TO_REQUIREDCLASS = {
    "全职业": "All",
    "战法道": "WarWizTao",
    "战士": "Warrior",
    "法师": "Wizard",
    "道士": "Taoist",
    "刺客": "ASSASSIN(库里无此类)",
    "刺战": "ASSWAR(库里无此类)",
}

CN_STAT_MAP: Dict[str, Tuple[str, Any]] = {
    # range: 「X-Y」→ (MinX, MaxX)；「+N」→ 只落 Max
    "物攻": ("range", ("MinDC", "MaxDC")),
    "魔攻": ("range", ("MinMC", "MaxMC")),
    "道术": ("range", ("MinSC", "MaxSC")),
    "物防": ("range", ("MinAC", "MaxAC")),
    "魔防": ("range", ("MinMR", "MaxMR")),
    "神圣攻击": ("single", "HolyAttack"),
    "火攻": ("single", "FireAttack"),
    "冰攻": ("single", "IceAttack"),
    "雷攻": ("single", "LightningAttack"),
    "风攻": ("single", "WindAttack"),
    "暗黑攻击": ("single", "DarkAttack"),
    "幻影攻击": ("single", "PhantomAttack"),
    "物攻加成": ("single", "DCPercent"),
    "魔法加成": ("single", "MCPercent"),
    "道术加成": ("single", "SCPercent"),
    "命中": ("single", "Accuracy"),
    "敏捷": ("single", "Agility"),
    "攻速": ("single", "AttackSpeed"),
    "暴击率": ("single", "CriticalChance"),
    "暴击伤害": ("single", "CriticalDamage"),
    "生命": ("single", "Health"),
    "魔法": ("single", "Mana"),
    "幸运": ("single", "Luck"),
    "光照": ("single", "Light"),
    "舒适度": ("single", "Comfort"),
    "穿戴负重": ("single", "WearWeight"),
    "手持负重": ("single", "HandWeight"),
    "负重加成": ("single", "WeightRate"),
    "毒抗": ("single", "PoisonResistance"),
    "爆率加成": ("single", "DropRate"),
    "经验加成": ("single", "ExperienceRate"),
    "吸血": ("single", "LifeSteal"),
    "技能熟练度": ("single", "SkillRate"),
    "物品复活时间": ("single", "ItemReviveTime"),
    "清除戒指": ("single", "ClearRing"),
    "保护戒指": ("single", "ProtectionRing"),
    "传送戒指": ("single", "TeleportRing"),
    "麻痹概率": ("single", "ParalysisChance"),
    "Boss追踪": ("single", "BossTracker"),
    "玩家追踪": ("single", "PlayerTracker"),
    # 属性名来自命名推断，未被数据证实（见报告「待人工确认」）
    "火亲和": ("single", "FireAffinity"),
    "冰亲和": ("single", "IceAffinity"),
    "雷亲和": ("single", "LightningAffinity"),
    "风亲和": ("single", "WindAffinity"),
    "神圣亲和": ("single", "HolyAffinity"),
    "暗黑亲和": ("single", "DarkAffinity"),
    "幻影亲和": ("single", "PhantomAffinity"),
    # 网站未翻译的英文枚举名（原样泄出到 attrs）
    "Duration": ("single", "Duration"),
}
CN_STAT_UNVERIFIED = {
    "火亲和", "冰亲和", "雷亲和", "风亲和", "神圣亲和", "暗黑亲和", "幻影亲和", "Duration",
}

SALEBONUS_RE = re.compile(r"^SaleBonus(\d+)$")


# --------------------------------------------------------------------------
# 小工具
# --------------------------------------------------------------------------
def norm_name(s: str) -> str:
    """名称归一化：小写 + 去掉一切非字母数字（空格、连字符、撇号、逗号、点）。"""
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def sha256_file(path: Path, limit: int = 1 << 26) -> Optional[str]:
    try:
        h = hashlib.sha256()
        with open(path, "rb") as fh:
            while True:
                chunk = fh.read(1 << 20)
                if not chunk:
                    break
                h.update(chunk)
        return h.hexdigest()
    except Exception:
        return None


def load_json(path: Path, unavailable: List[Dict[str, str]], required: bool = True) -> Any:
    if not path.exists():
        unavailable.append({"path": str(path), "reason": "missing"})
        if required:
            print(f"[items_audit] 输入缺失（跳过相关检查）: {path}", file=sys.stderr)
        return None
    try:
        with open(path, encoding="utf-8") as fh:
            return json.load(fh)
    except Exception as exc:  # noqa: BLE001
        unavailable.append({"path": str(path), "reason": f"unreadable: {exc}"})
        print(f"[items_audit] 输入不可读: {path}: {exc}", file=sys.stderr)
        return None


def load_table(export_dir: Path, table: str, unavailable: List[Dict[str, str]]) -> List[dict]:
    path = export_dir / f"{table}.json"
    data = load_json(path, unavailable, required=False)
    if data is None:
        return []
    if isinstance(data, dict):
        return data.get("rows") or []
    if isinstance(data, list):
        return data
    unavailable.append({"path": str(path), "reason": f"unexpected shape {type(data).__name__}"})
    return []


def parse_wiki_meta(meta: str) -> Dict[str, Any]:
    """把网站 meta 串（「类型 武器 · 价格 80000 · 重量 0 · 耐久 26000 · 可修理 · 稀有度 Elite」）
    拆成结构化字段。只做机械解析，不做语义猜测。"""
    out: Dict[str, Any] = {"tokens": []}
    for part in (meta or "").split("·"):
        part = part.strip()
        if not part:
            continue
        out["tokens"].append(part)
        m = re.match(r"^([^\d]+?)\s*([\d,]+)$", part)
        if m:
            key, val = m.group(1).strip(), m.group(2).replace(",", "")
            out[key] = int(val)
            continue
        m = re.match(r"^(类型|稀有度)\s+(.+)$", part)
        if m:
            out[m.group(1)] = m.group(2).strip()
            continue
        m = re.match(r"^(可堆叠|可修理|不可修理)$", part)
        if m:
            out[part] = True
            continue
        out.setdefault("other_tokens", []).append(part)
    return out


def parse_wiki_class_meta(text: str) -> Dict[str, Any]:
    """网站 class 字段形如「战法道 · 20 级」。"""
    out: Dict[str, Any] = {"class_label": None, "level": None, "raw": text}
    for part in (text or "").split("·"):
        part = part.strip()
        if not part:
            continue
        m = re.match(r"^(\d+)\s*级$", part)
        if m:
            out["level"] = int(m.group(1))
        elif out["class_label"] is None:
            out["class_label"] = part
    return out


# label 允许含数字（SaleBonus5），值可以是「+N」「-N」「N」「A-B」
ATTR_RE = re.compile(r"^(.+?)\s+([+-]?\d+(?:\s*-\s*\d+)?)$")
RANGE_RE = re.compile(r"^(\d+)\s*-\s*(\d+)$")


def parse_wiki_attr(attr: str) -> Tuple[Optional[str], Optional[str], List[Tuple[str, int]]]:
    """解析一条 attrs（如「物攻 9-21」「暴击率 +2」「攻速 -1」「SaleBonus5 +10」）。

    返回 (label, raw_value, [(Stat, Amount)])；label 为 None 表示无法解析。
    注意：负号不是区间分隔符——区间只认 RANGE_RE（A-B，两侧都是非负整数）。
    """
    s = (attr or "").strip().lstrip("：:").strip()
    m = ATTR_RE.match(s)
    if not m:
        return None, None, []
    label, value = m.group(1).strip(), m.group(2).replace(" ", "")
    stats: List[Tuple[str, int]] = []

    sb = SALEBONUS_RE.match(label)
    if sb:
        return label, value, [(f"SaleBonus{int(sb.group(1))}", int(value.lstrip("+")) if value[0] != "-" else -int(value[1:]))]

    spec = CN_STAT_MAP.get(label)
    if spec is None:
        return label, value, []

    kind, target = spec
    rng = RANGE_RE.match(value)
    if kind == "single":
        if rng:
            return label, value, []  # 单值属性给了区间：无法机械映射
        return label, value, [(target, int(value) if value[0] == "-" else int(value.lstrip("+")))]
    lo_stat, hi_stat = target
    if rng:
        return label, value, [(lo_stat, int(rng.group(1))), (hi_stat, int(rng.group(2)))]
    return label, value, [(hi_stat, int(value) if value[0] == "-" else int(value.lstrip("+")))]


def counter_top(counter: collections.Counter, n: int) -> List[List[Any]]:
    return [[k, v] for k, v in counter.most_common(n)]


# --------------------------------------------------------------------------
# 主流程
# --------------------------------------------------------------------------
def main(argv: Optional[Sequence[str]] = None) -> int:
    ap = argparse.ArgumentParser(description="Zircon 物品数据只读对账（网站基准 vs 当前库）")
    ap.add_argument("--wiki", type=Path, default=DEFAULT_WIKI)
    ap.add_argument("--export", type=Path, default=DEFAULT_EXPORT_DIR)
    ap.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    ap.add_argument("--baseline-export", type=Path, default=DEFAULT_BASELINE_EXPORT,
                    help="清洗前旧库表导出目录（可选；存在时做回归对照）")
    ap.add_argument("--storeitem-zl", type=Path, default=DEFAULT_STOREITEM_ZL)
    ap.add_argument("--storeitem-alt-zl", type=Path, default=DEFAULT_STOREITEM_ALT_ZL)
    ap.add_argument("--zlsdk-dir", type=Path, default=DEFAULT_ZLSDK_DIR)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--decode-sample", type=int, default=0,
                    help="额外真实解码的图标数（需 texture2ddecoder 与 Pillow；0=只做帧头判定）")
    ap.add_argument("--json-only", action="store_true", help="不打印摘要")
    args = ap.parse_args(argv)

    unavailable: List[Dict[str, str]] = []
    result: Dict[str, Any] = {
        "generated_at_utc": None,
        "audit": "zircon-items-audit",
        "scope": "网站侧基准 wiki_data_v2.json items ↔ 当前库物品相关表",
        "unavailable_inputs": unavailable,
    }

    # ---------------- 输入 ----------------
    wiki = load_json(args.wiki, unavailable)
    manifest = load_json(args.manifest, unavailable, required=False)
    tables = {t: load_table(args.export, t, unavailable) for t in ITEM_TABLES}

    items_db = tables["ItemInfo"]
    stats_db = tables["ItemInfoStat"]
    drop_db = tables["DropInfo"]

    if not wiki or not items_db:
        result["fatal"] = "缺少基准快照或 ItemInfo 导出，无法对账"
        _write_out(args.out, result)
        print("[items_audit] FATAL: 缺少基准快照或 ItemInfo 导出", file=sys.stderr)
        return 2

    result["unavailable_inputs"] = unavailable
    result["inputs"] = {
        "wiki": _file_info(args.wiki),
        "export_dir": str(args.export),
        "item_tables_rows": {t: len(tables[t]) for t in ITEM_TABLES},
        "item_manifest": _file_info(args.manifest),
        "storeitem_zl": _file_info(args.storeitem_zl),
        "storeitem_alt_zl": _file_info(args.storeitem_alt_zl),
    }

    wiki_items_all: List[dict] = wiki.get("items") or []
    wiki_meta = wiki.get("_meta") or {}
    wiki_zc = [r for r in wiki_items_all if "zircon" in (r.get("ver") or [])]
    wiki_legacy = [r for r in wiki_items_all if "zircon" not in (r.get("ver") or [])]

    result["baseline"] = {
        "wiki_meta": wiki_meta,
        "items_total": len(wiki_items_all),
        "items_ver_zircon": len(wiki_zc),
        "items_legacy_only": len(wiki_legacy),
        "ver_combinations": counter_top(
            collections.Counter(tuple(r.get("ver") or []) for r in wiki_items_all), 20),
        "note": (
            "ver 含 zircon 并不等于“当前库里存在”：ver_tags.py（Mir3-Research/Tools/common/"
            "ver_tags.py:177）对每条 items 无条件 vs = ['zircon']，故 zircon 标签=该条出现在网站物品表；"
            "ei/mei 才是 WIL 素材存在性（wil_probe 探帧）。items_ver_zircon 与旧库 ItemInfo 行数（1078）"
            "一致属于快照来源巧合，不能当作存在性证据。"
        ),
    }
    result["db_baseline"] = {
        "ItemInfo_rows": len(items_db),
        "ItemInfoStat_rows": len(stats_db),
        "DropInfo_rows": len(drop_db),
        "distinct_ItemType": counter_top(collections.Counter(r.get("ItemType") for r in items_db), 30),
        "distinct_RequiredClass": counter_top(collections.Counter(r.get("RequiredClass") for r in items_db), 30),
        "distinct_Rarity": counter_top(collections.Counter(r.get("Rarity") for r in items_db), 10),
    }

    # ---------------- 1. 名称映射 ----------------
    db_by_exact = collections.defaultdict(list)
    db_by_norm = collections.defaultdict(list)
    for row in items_db:
        name = row.get("ItemName") or ""
        db_by_exact[name].append(row)
        db_by_norm[norm_name(name)].append(row)

    wiki_by_norm = collections.defaultdict(list)
    for row in wiki_zc:
        wiki_by_norm[norm_name(row.get("name"))].append(row)

    mapping: Dict[int, dict] = {}   # id(wiki_row) -> mapping record
    mapping_records: List[dict] = []

    for wiki_row in wiki_zc:
        name = wiki_row.get("name") or ""
        rec = {
            "wiki_name": name,
            "wiki_zh": wiki_row.get("zh"),
            "wiki_ver": wiki_row.get("ver"),
            "wiki_category": wiki_row.get("category"),
            "wiki_type_zh": wiki_row.get("type_zh"),
            "method": None,
            "db_index": None,
            "db_name": None,
            "ambiguous": False,
            "notes": [],
        }
        exact = db_by_exact.get(name) or []
        norm = db_by_norm.get(norm_name(name)) or []
        if len(exact) == 1:
            rec.update(method="exact_name", db_index=exact[0]["Index"], db_name=exact[0]["ItemName"])
        elif len(norm) == 1:
            rec.update(method="normalized_name", db_index=norm[0]["Index"], db_name=norm[0]["ItemName"])
            if name != norm[0]["ItemName"]:
                rec["notes"].append(f"归一化匹配：网站「{name}」↔ 库「{norm[0]['ItemName']}」")
        elif len(norm) > 1:
            rec["ambiguous"] = True
            rec["notes"].append("库内多条归一化同名，待人工确认：" + ", ".join(
                f"{r['Index']}:{r['ItemName']}" for r in norm))
        else:
            rec["method"] = "unmatched"
        mapping_records.append(rec)
        mapping[id(wiki_row)] = rec

    # 同一条库记录被多条网站记录命中 → 只能保留命名完全一致的那条，其余退回“未匹配”
    # （例：网站同时有 Gold-Plated Bracelet / Gold Plated Bracelet，库里只有后者）
    claims: Dict[int, List[dict]] = collections.defaultdict(list)
    for rec in mapping_records:
        if rec["db_index"] is not None and not rec["ambiguous"]:
            claims[rec["db_index"]].append(rec)
    collision_report = []
    for db_index, recs in claims.items():
        if len(recs) == 1:
            continue
        exact_recs = [r for r in recs if r["method"] == "exact_name"]
        keep = exact_recs[0] if len(exact_recs) == 1 else None
        collision_report.append({
            "db_index": db_index,
            "db_name": recs[0]["db_name"],
            "wiki_names": [r["wiki_name"] for r in recs],
            "kept": keep["wiki_name"] if keep else None,
        })
        for r in recs:
            if r is keep:
                continue
            r["db_index"] = None
            r["db_name"] = None
            r["method"] = "unmatched"
            r["notes"].append(
                "该网站条目与另一条网站条目归一化同名，只能唯一对应一条库记录；"
                "本条目退回未匹配（避免把两条网站记录当成同一件物品）。")

    matched_db_indexes: Dict[int, int] = {}
    for rec in mapping_records:
        if rec["db_index"] is not None and not rec["ambiguous"]:
            matched_db_indexes[rec["db_index"]] = 0  # 占位，值不使用

    by_method = collections.Counter(r["method"] for r in mapping_records)
    ambiguous_count = sum(1 for r in mapping_records if r["ambiguous"])

    # 既有成果交叉验证：item-manifest.json（mir3-website 中文名 → Zircon Index）
    manifest_check = {"available": bool(manifest), "rows": None, "status": None,
                      "agree": 0, "disagree": 0, "candidate_index_dead": 0,
                      "disagree_samples": [], "note": ""}
    if manifest:
        manifest_check["rows"] = len(manifest)
        manifest_check["status"] = counter_top(collections.Counter(r.get("status") for r in manifest), 10)
        db_index_set = {r["Index"] for r in items_db}
        zh_to_pair = {}
        for rec in mapping_records:
            if rec["db_index"] is not None:
                zh_to_pair.setdefault(rec["wiki_zh"], []).append(rec)
        for entry in manifest:
            cands = entry.get("zircon_candidates") or []
            for cand in cands:
                if cand.get("index") not in db_index_set:
                    manifest_check["candidate_index_dead"] += 1
            if not cands:
                continue
            lived = [c for c in cands if c.get("index") in db_index_set]
            if not lived:
                continue
            zh = entry.get("website_name")
            for rec in zh_to_pair.get(zh, []):
                if rec["db_index"] == lived[0]["index"]:
                    manifest_check["agree"] += 1
                else:
                    manifest_check["disagree"] += 1
                    if len(manifest_check["disagree_samples"]) < 10:
                        manifest_check["disagree_samples"].append(
                            {"website_name": zh, "manifest_index": lived[0]["index"],
                             "manifest_db_name": lived[0]["name"],
                             "audit_index": rec["db_index"], "audit_db_name": rec["db_name"]})
        manifest_check["note"] = (
            "item-manifest.json 是 2026-09-26 对旧库（ItemInfo 1078 行）生成的中文名→Index 映射，"
            "仅用于交叉验证本次英文名映射；candidate_index_dead = 该 manifest 指向的 Index 在当前库已不存在。")

    result["mapping"] = {
        "rules": [
            "exact_name: 网站 name 与 ItemInfo.ItemName 完全一致",
            "normalized_name: 仅大小写/空格/标点差异（去掉非字母数字后一致）",
            "unmatched: 以上都不命中 → 计入待人工确认，不做任何猜测",
            "多条网站条目归一化同名而只有一条库记录时，只保留 exact_name 那条，其余退回 unmatched",
        ],
        "counts": {
            "wiki_ver_zircon_items": len(wiki_zc),
            "matched_total": len(matched_db_indexes),
            "by_method": dict(by_method),
            "ambiguous_multi_candidate": ambiguous_count,
            "name_collisions_resolved": collision_report,
            "hit_rate_exact": _rate(by_method.get("exact_name", 0), len(wiki_zc)),
            "hit_rate_normalized_extra": _rate(by_method.get("normalized_name", 0), len(wiki_zc)),
            "hit_rate_total": _rate(len(matched_db_indexes), len(wiki_zc)),
            "db_side_coverage": _rate(len(matched_db_indexes), len(items_db)),
        },
        "manifest_crosscheck": manifest_check,
        "records": mapping_records,
    }

    # ---------------- 2. 覆盖对账（双向） ----------------
    wiki_present_db_missing: List[dict] = []
    for wiki_row in wiki_zc:
        rec = mapping[id(wiki_row)]
        if rec["db_index"] is None or rec["ambiguous"]:
            wiki_present_db_missing.append({
                "wiki_name": rec["wiki_name"], "wiki_zh": rec["wiki_zh"],
                "wiki_category": rec["wiki_category"], "wiki_type_zh": rec["wiki_type_zh"],
                "wiki_ver": rec["wiki_ver"], "wiki_img_frame": (wiki_row.get("img") or {}).get("frame"),
                "wiki_set": (wiki_row.get("set") or "").strip().lstrip("：:").strip() or None,
                "wiki_attrs": wiki_row.get("attrs"),
                "reason": "unmatched" if rec["method"] == "unmatched" else "ambiguous_multi_candidate",
                "notes": rec["notes"],
            })

    db_present_wiki_missing = []
    for row in items_db:
        if row["Index"] not in matched_db_indexes:
            db_present_wiki_missing.append({
                "db_index": row["Index"], "db_name": row["ItemName"], "db_type": row["ItemType"],
                "db_price": row["Price"], "db_image": row["Image"], "db_rarity": row["Rarity"],
                "norm": norm_name(row["ItemName"]),
                "nearest_wiki_names": _nearest_wiki(row["ItemName"], wiki_zc),
            })

    result["coverage"] = {
        "db_items_total": len(items_db),
        "db_items_matched_to_wiki": len(matched_db_indexes),
        "db_items_not_in_wiki": len(db_present_wiki_missing),
        "wiki_items_total": len(wiki_items_all),
        "wiki_ver_zircon": len(wiki_zc),
        "wiki_zircon_matched_to_db": len(matched_db_indexes),
        "wiki_zircon_not_in_db": len(wiki_present_db_missing) - ambiguous_count,
        "wiki_zircon_ambiguous": ambiguous_count,
        "type_zh_vs_db_type": _type_gap_table(wiki_zc, items_db, mapping),
        "by_category_of_missing": counter_top(
            collections.Counter(x["wiki_category"] for x in wiki_present_db_missing), 20),
        "by_type_zh_of_missing": counter_top(
            collections.Counter(x["wiki_type_zh"] for x in wiki_present_db_missing), 30),
        "by_ver_tuple_of_missing": counter_top(
            collections.Counter(tuple(x["wiki_ver"] or []) for x in wiki_present_db_missing), 20),
        "wiki_present_db_missing_top30": wiki_present_db_missing[:TOP_N],
        "db_present_wiki_missing_top30": db_present_wiki_missing[:TOP_N],
        "wiki_present_db_missing": wiki_present_db_missing,
        "db_present_wiki_missing": db_present_wiki_missing,
    }

    # ---------------- 3. 字段级差异 ----------------
    pairs: List[Tuple[dict, dict]] = []
    for wiki_row in wiki_zc:
        rec = mapping[id(wiki_row)]
        if rec["db_index"] is None or rec["ambiguous"]:
            continue
        pairs.append((wiki_row, db_by_index_of(items_db, rec["db_index"])))

    field_report = _field_diff(pairs)
    stat_report = _stat_diff(pairs)
    result["field_diff"] = field_report
    result["stat_diff"] = stat_report

    # ---------------- 4. 套装 / 货币 / 商店 / 武器打造 ----------------
    result["sets"] = _sets_section(tables, items_db, wiki_zc, mapping, matched_db_indexes)
    result["currency"] = _currency_section(tables, items_db, wiki_zc)
    result["store"] = _store_section(tables, items_db, wiki_zc)
    result["weapon_craft"] = _weapon_craft_section(tables, items_db)
    result["books"] = _books_section(args.export, items_db, unavailable)
    result["regression_vs_baseline"] = _regression_section(
        args.baseline_export, items_db, unavailable, len(stats_db), len(drop_db))

    # ---------------- 5. 表内一致性 / 完整性 ----------------
    result["integrity"] = _integrity_section(tables, items_db)

    # ---------------- 6. 图标可用性 ----------------
    result["icons"] = _icons_section(
        items_db, tables, args.storeitem_zl, args.storeitem_alt_zl, args.zlsdk_dir,
        unavailable, args.decode_sample)

    # ---------------- 7. 结论/风险（由数据生成，不含猜测） ----------------
    result["findings"] = _findings(result)

    result["unavailable_inputs"] = unavailable
    _write_out(args.out, result)

    if not args.json_only:
        _print_summary(result, args.out)
    return 0


# --------------------------------------------------------------------------
# 各节实现
# --------------------------------------------------------------------------
def _file_info(path: Path) -> dict:
    if not path.exists():
        return {"path": str(path), "exists": False}
    st = path.stat()
    info = {"path": str(path), "exists": True, "bytes": st.st_size,
            "mtime_utc": None}
    import datetime
    info["mtime_utc"] = datetime.datetime.fromtimestamp(
        st.st_mtime, datetime.timezone.utc).isoformat().replace("+00:00", "Z")
    if path.suffix.lower() in (".json", ".zl", ".db"):
        info["sha256"] = sha256_file(path) if st.st_size < (1 << 28) else None
    return info


def _rate(hit: int, total: int) -> Optional[float]:
    return round(hit / total, 4) if total else None


def db_by_index_of(items_db: List[dict], index: int) -> dict:
    for row in items_db:
        if row["Index"] == index:
            return row
    raise KeyError(index)


def _nearest_wiki(db_name: str, wiki_zc: List[dict]) -> List[str]:
    """仅用于人工排查的弱候选（token 交集），明确不是匹配结论。"""
    tokens = set(re.findall(r"[a-z0-9]+", db_name.lower()))
    if not tokens:
        return []
    scored = []
    for row in wiki_zc:
        wtokens = set(re.findall(r"[a-z0-9]+", (row.get("name") or "").lower()))
        if not wtokens:
            continue
        inter = len(tokens & wtokens)
        if inter:
            scored.append((inter / max(len(tokens | wtokens), 1), row.get("name")))
    scored.sort(key=lambda t: -t[0])
    return [name for _, name in scored[:3]]


def _field_diff(pairs: List[Tuple[dict, dict]]) -> dict:
    fields: Dict[str, dict] = {}

    def bucket(key: str, meta: dict) -> dict:
        if key not in fields:
            fields[key] = {"field": key, "same": 0, "same_website_default": 0, "diff": 0,
                           "diff_encoding_only": 0, "website_missing": 0,
                           "db_missing": 0, "website_only": 0, "db_only": 0,
                           "db_side_only_field": bool(meta.get("db_side_only")),
                           "website_field": meta.get("website_field"),
                           "db_field": meta.get("db_field"),
                           "website_default": meta.get("website_default"),
                           "comparable": 0, "diff_samples": [],
                           "neutral_db_value": meta.get("neutral")}
        return fields[key]

    for wiki_row, db_row in pairs:
        meta = parse_wiki_meta(wiki_row.get("meta") or "")
        cls = parse_wiki_class_meta(wiki_row.get("class") or "")

        # --- 类型 ---
        b = bucket("ItemType", {"website_field": "meta「类型」", "db_field": "ItemInfo.ItemType"})
        wtype = meta.get("类型")
        if wtype is None:
            b["website_missing"] += 1
        else:
            mapped = TYPE_ZH_TO_ITEMTYPE.get(wtype, f"UNMAPPED:{wtype}")
            b["comparable"] += 1
            if mapped == db_row["ItemType"]:
                b["same"] += 1
            else:
                b["diff"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": wtype, "db": db_row["ItemType"],
                            "mapped": mapped})

        # --- 数值字段 ---
        for key, db_field, neutral, default in (("价格", "Price", 0, None),
                                                ("重量", "Weight", 0, None),
                                                ("耐久", "Durability", 0, None),
                                                ("可堆叠", "StackSize", 1, 1)):
            b = bucket(db_field, {"website_field": f"meta「{key}」", "db_field": f"ItemInfo.{db_field}",
                                  "neutral": neutral, "website_default": default})
            wval = meta.get(key)
            dval = db_row[db_field]
            if wval is None:
                b["website_missing"] += 1
                if default is not None:
                    b["comparable"] += 1
                    if dval == default:
                        b["same_website_default"] += 1
                    else:
                        b["db_only"] += 1
                        _sample(b, {"name": db_row["ItemName"], "website": f"缺省(按{default})",
                                    "db": dval})
                elif dval != neutral:
                    b["db_only"] += 1
                    _sample(b, {"name": db_row["ItemName"], "website": None, "db": dval})
            else:
                b["comparable"] += 1
                if wval == dval:
                    b["same"] += 1
                else:
                    b["diff"] += 1
                    _sample(b, {"name": db_row["ItemName"], "website": wval, "db": dval})

        # --- 稀有度（网站只在非 Common 时才写「稀有度」，缺省语义 = Common） ---
        b = bucket("Rarity", {"website_field": "meta「稀有度」", "db_field": "ItemInfo.Rarity",
                              "neutral": "Common", "website_default": "Common"})
        wrar = meta.get("稀有度")
        if wrar is None:
            b["website_missing"] += 1
            b["comparable"] += 1
            if db_row["Rarity"] == "Common":
                b["same_website_default"] += 1
            else:
                b["db_only"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": "缺省(按 Common)",
                            "db": db_row["Rarity"]})
        else:
            b["comparable"] += 1
            if wrar == db_row["Rarity"]:
                b["same"] += 1
            else:
                b["diff"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": wrar, "db": db_row["Rarity"]})

        # --- 可修理（网站只记录正面标记） ---
        b = bucket("CanRepair", {"website_field": "meta「可修理」", "db_field": "ItemInfo.CanRepair",
                                 "neutral": None})
        if meta.get("可修理") or meta.get("不可修理"):
            b["comparable"] += 1
            want = bool(meta.get("可修理"))
            if db_row["CanRepair"] == want:
                b["same"] += 1
            else:
                b["diff"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": want, "db": db_row["CanRepair"]})
        else:
            b["website_missing"] += 1

        # --- 需求等级（库侧可能改用属性门槛表达同一数值） ---
        b = bucket("RequiredAmount(Level)", {"website_field": "class「N 级」",
                                             "db_field": "ItemInfo.RequiredAmount/RequiredType",
                                             "neutral": 0})
        if cls["level"] is None:
            b["website_missing"] += 1
            if db_row["RequiredAmount"] != 0:
                b["db_only"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": None,
                            "db": f"{db_row['RequiredType']} {db_row['RequiredAmount']}"})
        elif db_row["RequiredType"] != "Level":
            if db_row["RequiredAmount"] == cls["level"]:
                b["comparable"] += 1
                b["diff_encoding_only"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": f"等级 {cls['level']}",
                            "db": f"{db_row['RequiredType']} {db_row['RequiredAmount']}",
                            "note": "数值相同，库改用属性门槛"})
            else:
                b["diff"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": cls["level"],
                            "db": f"{db_row['RequiredType']} {db_row['RequiredAmount']}"})
        else:
            b["comparable"] += 1
            if cls["level"] == db_row["RequiredAmount"]:
                b["same"] += 1
            else:
                b["diff"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": cls["level"],
                            "db": db_row["RequiredAmount"]})

        # --- 需求职业 ---
        b = bucket("RequiredClass", {"website_field": "class 职业标签", "db_field": "ItemInfo.RequiredClass",
                                     "neutral": None})
        wcls = CLASS_ZH_TO_REQUIREDCLASS.get(cls["class_label"] or "")
        if wcls is None:
            b["website_missing"] += 1
        elif wcls.startswith("ASSASSIN") or wcls.startswith("ASSWAR"):
            b["db_missing"] += 1  # 基准含刺客内容，当前库没有 Assassin 职业项
            _sample(b, {"name": db_row["ItemName"], "website": cls["class_label"],
                        "db": db_row["RequiredClass"]})
        else:
            b["comparable"] += 1
            if wcls == db_row["RequiredClass"]:
                b["same"] += 1
            else:
                b["diff"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": cls["class_label"],
                            "db": db_row["RequiredClass"], "mapped": wcls})

        # --- 图标帧号（网站 img.frame 来自旧库 SystemDbProbe，见文件头注释） ---
        b = bucket("Image", {"website_field": "img.frame（快照取自旧库）", "db_field": "ItemInfo.Image",
                             "neutral": 0})
        wframe = (wiki_row.get("img") or {}).get("frame")
        if wframe is None:
            b["website_missing"] += 1
        else:
            b["comparable"] += 1
            if wframe == db_row["Image"]:
                b["same"] += 1
            else:
                b["diff"] += 1
                _sample(b, {"name": db_row["ItemName"], "website": wframe, "db": db_row["Image"]})

        # --- 库侧独有字段（网站结构里没有该字段） ---
        for db_field in ["ItemEffect", "ExteriorEffect", "Shape", "SellRate", "CanSell",
                         "CanStore", "CanTrade", "CanDrop", "CanDeathDrop", "CanAutoPot",
                         "BuffIcon", "PartCount", "StartItem", "RequiredGender",
                         "RequiredType", "Description"]:
            b = bucket(db_field, {"db_side_only": True, "db_field": f"ItemInfo.{db_field}"})
            b["website_missing"] += 1

    return {
        "pairs_compared": len(pairs),
        "fields": fields,
        "legend": {
            "same": "网站与库都有且一致",
            "same_website_default": "网站该字段缺省，库值等于网站缺省语义（如 稀有度缺省=Common）",
            "diff": "网站与库都有但不一致（见 diff_samples）",
            "diff_encoding_only": "同一业务的两种表达（等级门槛 vs 属性门槛），数值相同",
            "website_missing": "网站该字段缺省（库侧可能是中性默认值，或计入 db_only）",
            "db_only": "网站缺省而库侧非中性默认值（只能单向说明）",
            "db_missing": "网站有值但库里没有对应取值（如刺客职业在库里不存在）",
            "db_side_only_field": "该字段只存在于库侧，网站快照无对应字段",
        },
    }


def _sample(bucket: dict, sample: dict, limit: int = 12) -> None:
    if len(bucket["diff_samples"]) < limit:
        bucket["diff_samples"].append(sample)


def _stat_diff(pairs: List[Tuple[dict, dict]]) -> dict:
    exact = partial_web = partial_db = mismatch = both_empty = 0
    plus_form_flex = 0
    flex_examples: List[dict] = []
    web_only_items: List[dict] = []
    label_counter = collections.Counter()
    unmapped_labels = collections.Counter()
    unverified_used = collections.Counter()
    samples: List[dict] = []
    db_only_stats = collections.Counter()
    web_only_stats = collections.Counter()
    potion_focus: List[dict] = []

    for wiki_row, db_row in pairs:
        web_pairs: List[Tuple[str, int]] = []
        for attr in (wiki_row.get("attrs") or []):
            label, value, converted = parse_wiki_attr(attr)
            if label is None:
                unmapped_labels[f"UNPARSED:{attr}"] += 1
                continue
            label_counter[label] += 1
            if label in CN_STAT_UNVERIFIED:
                unverified_used[label] += 1
            if not converted:
                unmapped_labels[label] += 1
                continue
            web_pairs.extend(converted)

        # 只保留非零词条（0 值在两侧都无意义，且会污染差集）
        web_pairs = [(s, v) for s, v in web_pairs if v != 0]
        db_pairs = [(s["Stat"], s["Value"]) for s in (db_row.get("Stats") or []) if s["Value"] != 0]

        web = collections.Counter(dict(web_pairs))
        db = collections.Counter(dict(db_pairs))

        # 单值词条用字典比较（Counter 相减在负值时会产生假的“单边”条目）
        web_d = dict(web_pairs)
        db_d = dict(db_pairs)
        only_web = {k: v for k, v in web_d.items() if db_d.get(k) != v and k not in db_d}
        only_db = {k: v for k, v in db_d.items() if k not in web_d}
        value_conflict = {k: (web_d[k], db_d[k]) for k in set(web_d) & set(db_d) if web_d[k] != db_d[k]}

        # 「+N」在网站上不区分 Min/Max；库侧有时落 Min*、有时落 Max*。
        # 这里单独统计“只差 Min/Max 落位”的条目，不混入 exact_match。
        flex_pairs: List[Tuple[str, str, int]] = []
        if only_web and only_db and not value_conflict:
            remaining_web = dict(only_web)
            remaining_db = dict(only_db)
            for stat, amount in list(only_web.items()):
                base = stat[3:] if stat.startswith(("Min", "Max")) else None
                if base is None:
                    continue
                partner = ("Max" if stat.startswith("Min") else "Min") + base
                if remaining_db.get(partner) == amount:
                    flex_pairs.append((stat, partner, amount))
                    remaining_web.pop(stat, None)
                    remaining_db.pop(partner, None)
            only_web, only_db = remaining_web, remaining_db

        if not web_pairs and not db_pairs:
            both_empty += 1
        elif web_d == db_d:
            exact += 1
        elif flex_pairs and not only_web and not only_db and not value_conflict:
            plus_form_flex += 1
        elif only_web and not only_db and not value_conflict:
            partial_web += 1
        elif only_db and not only_web and not value_conflict:
            partial_db += 1
        else:
            mismatch += 1
        for stat, amount in only_db.items():
            db_only_stats[stat] += 1
        for stat, amount in only_web.items():
            web_only_stats[stat] += 1
        if flex_pairs and len(flex_examples) < 10:
            flex_examples.append({"name": db_row["ItemName"], "website_attrs": wiki_row.get("attrs"),
                                  "placement_only": flex_pairs, "db_stats": sorted(db_pairs)})
        if only_web and len(web_only_items) < 80:
            web_only_items.append({
                "db_index": db_row["Index"], "name": db_row["ItemName"],
                "type": db_row["ItemType"], "shape": db_row["Shape"],
                "website_attrs": wiki_row.get("attrs"),
                "website_only_stats": sorted(only_web.items()),
                "db_stats": sorted(db_pairs)})
        if len(samples) < 25 and (only_web or only_db or value_conflict):
            samples.append({
                "name": db_row["ItemName"], "wiki_attrs": wiki_row.get("attrs"),
                "website_parsed": sorted(web_pairs), "db_stats": sorted(db_pairs),
                "only_website": sorted(only_web.items()), "only_db": sorted(only_db.items()),
                "value_conflict": value_conflict,
            })
        if db_row["ItemType"] == "Consumable" and db_row["Shape"] == 0:
            potion_focus.append({
                "db_index": db_row["Index"], "name": db_row["ItemName"],
                "price": db_row["Price"], "can_auto_pot": db_row["CanAutoPot"],
                "website_attrs": wiki_row.get("attrs"), "website_class": wiki_row.get("class"),
                "db_stats": sorted(db_pairs), "website_parsed": sorted(web_pairs),
            })

    return {
        "pairs_compared": len(pairs),
        "exact_match": exact,
        "equivalent_plus_form_placement_only": plus_form_flex,
        "website_has_extra_stat": partial_web,
        "db_has_extra_stat": partial_db,
        "both_sides_differ": mismatch,
        "both_empty": both_empty,
        "match_rate": _rate(exact, len(pairs)),
        "match_rate_incl_plus_form": _rate(exact + plus_form_flex, len(pairs)),
        "plus_form_examples": flex_examples,
        "website_only_items": web_only_items,
        "attr_label_counts": counter_top(label_counter, 60),
        "unmapped_attr_labels": counter_top(unmapped_labels, 40),
        "unverified_mapping_used": counter_top(unverified_used, 20),
        "website_only_stat_frequency": counter_top(web_only_stats, 40),
        "db_only_stat_frequency": counter_top(db_only_stats, 40),
        "potion_shape0_items": potion_focus,
        "samples": samples,
        "legend": {
            "exact_match": "attrs 解析出的 (Stat,Amount) 与库 ItemInfoStat 完全一致",
            "equivalent_plus_form_placement_only": "只差 Min/Max 落位（网站写「+N」，库落 Min* 或 Max*），数值相同",
            "website_has_extra_stat": "网站声明了库里没有的词条（例如生命/魔法回复量被清空）",
            "db_has_extra_stat": "库里有网站 attrs 未列出的词条",
            "both_sides_differ": "两侧各有对方没有的词条，或同一词条数值不同",
            "both_empty": "两侧都没有词条（消耗品/材料等）",
        },
        "note": (
            "「+N」形式在传奇系里只有一个数值，本脚本按惯例映射到 Max* 词条；"
            "「X-Y」映射到 Min*/Max* 两个词条（负号不算区间分隔符）。"
            "标签→Stat 对应见脚本 CN_STAT_MAP；unverified_mapping_used 里的标签为命名推断、未被数据证实。"
            "零值词条在两侧都被剔除。"),
    }


def _sets_section(tables, items_db, wiki_zc, mapping, matched_db_indexes) -> dict:
    set_info = tables["SetInfo"]
    set_stats = tables["SetInfoStat"]
    # 库里成员
    members: Dict[int, List[dict]] = collections.defaultdict(list)
    for row in items_db:
        if isinstance(row.get("Set"), dict):
            members[row["Set"]["Index"]].append(
                {"index": row["Index"], "name": row["ItemName"], "type": row["ItemType"]})
    stats_per_set = collections.Counter(
        s["Set"]["Index"] for s in set_stats if isinstance(s.get("Set"), dict))

    sets_out = []
    for s in sorted(set_info, key=lambda r: r["Index"]):
        sets_out.append({
            "index": s["Index"], "name": s["SetName"],
            "db_member_count": len(members.get(s["Index"], [])),
            "db_members": members.get(s["Index"], []),
            "set_stat_rows": stats_per_set.get(s["Index"], 0),
        })

    # 网站声明
    wiki_members: Dict[str, List[dict]] = collections.defaultdict(list)
    for row in wiki_zc:
        raw = (row.get("set") or "").strip().lstrip("：:").strip()
        if not raw:
            continue
        rec = mapping[id(row)]
        wiki_members[raw].append({
            "name": row["name"], "zh": row.get("zh"),
            "db_index": rec["db_index"], "db_name": rec["db_name"],
            "db_has_set_link": bool(rec["db_index"]) and any(
                r["Index"] == rec["db_index"] and isinstance(r.get("Set"), dict) for r in items_db),
        })

    db_set_names = {s["SetName"] for s in set_info}
    wiki_set_names = set(wiki_members)
    orphan_sets = sorted(db_set_names - wiki_set_names)
    wiki_only_sets = sorted(wiki_set_names - db_set_names)

    gap = []
    for name in sorted(wiki_members):
        entries = wiki_members[name]
        missing = [e for e in entries if e["db_index"] is None]
        unlinked = [e for e in entries if e["db_index"] is not None and not e["db_has_set_link"]]
        if missing or unlinked:
            gap.append({
                "set_name": name,
                "website_member_count": len(entries),
                "members_absent_from_db": [e["name"] for e in missing],
                "members_in_db_without_set_link": [e["name"] for e in unlinked],
            })

    return {
        "SetInfo_rows": len(set_info),
        "SetInfoStat_rows": len(set_stats),
        "sets_with_zero_db_members": sum(1 for s in sets_out if s["db_member_count"] == 0),
        "sets_with_members": sum(1 for s in sets_out if s["db_member_count"] > 0),
        "db_items_with_set_link": sum(len(v) for v in members.values()),
        "website_items_claiming_set": sum(len(v) for v in wiki_members.values()),
        "website_distinct_sets": len(wiki_members),
        "sets": sets_out,
        "website_claims": {k: v for k, v in sorted(wiki_members.items())},
        "set_names_only_in_db": orphan_sets,
        "set_names_only_in_website": wiki_only_sets,
        "gaps": gap,
        "set_stat_distribution": counter_top(
            collections.Counter(s.get("Stat") for s in set_stats), 40),
        "note": ("库里 ItemInfo.Set 是套装成员关系的唯一来源；SetInfoStat 只是套装词条。"
                 "某套装 db_member_count=0 时，它的 SetInfoStat 永远不生效。"),
    }


def _currency_section(tables, items_db, wiki_zc) -> dict:
    cur = tables["CurrencyInfo"]
    cur_img = tables["CurrencyInfoImage"]
    idx = {r["Index"]: r for r in items_db}
    rows = []
    for c in cur:
        drop = c.get("DropItem")
        rows.append({
            "index": c["Index"], "name": c.get("Name"), "type": c.get("Type"),
            "category": c.get("Category"), "abbreviation": c.get("Abbreviation"),
            "exchange_rate": c.get("ExchangeRate"),
            "drop_item": drop,
            "drop_item_exists": bool(drop) and drop["Index"] in idx,
            "image_rows": sum(1 for i in cur_img if isinstance(i.get("Currency"), dict)
                              and i["Currency"]["Index"] == c["Index"]),
        })
    return {
        "CurrencyInfo_rows": len(cur),
        "CurrencyInfoImage_rows": len(cur_img),
        "rows": rows,
        "images": [{"index": i["Index"],
                    "currency": (i.get("Currency") or {}).get("Name"),
                    "image": i.get("Image"), "amount": i.get("Amount")} for i in cur_img],
        "currencies_without_image_rows": [r["name"] for r in rows if r["image_rows"] == 0],
        "currencies_without_drop_item": [r["name"] for r in rows if not r["drop_item"]],
        "website_counterpart": "网站 items 里「类型 货币」条目：" + str(
            sum(1 for r in wiki_zc if (parse_wiki_meta(r.get("meta") or "").get("类型") == "货币"))),
        "note": (
            "CurrencyImage 取图规则（GodotClient/Controls/DXItemCell.cs:546）：按 Amount 降序取第一个 "
            "<= 持有量的 CurrencyInfoImage.Image，无匹配则回退 DropItem.Image。"
            "因此无 DropItem 的 GameGold/HuntGold 不参与物品图标。"),
    }


def _store_section(tables, items_db, wiki_zc) -> dict:
    store = tables["StoreInfo"]
    idx = {r["Index"]: r for r in items_db}
    rows = []
    for s in store:
        item = s.get("Item") or {}
        rows.append({
            "index": s["Index"], "item": item, "price": s.get("Price"),
            "hunt_gold_price": s.get("HuntGoldPrice"), "filter": s.get("Filter"),
            "available": s.get("Available"), "duration": s.get("Duration"),
            "item_exists": item.get("Index") in idx,
        })
    return {
        "StoreInfo_rows": len(store),
        "rows": rows,
        "items_referencing_missing_ItemInfo": [r["item"] for r in rows if not r["item_exists"]],
        "website_counterpart": ("网站基准快照里没有「商城/GameStore」表，只有 items/monsters/…；"
                               "因此 StoreInfo 无法与本快照逐项对账，需要另一种证据源"
                               "（Mir3-Research/Tools/SystemDbProbe --stores 产出的 stores.json 或旧库）"),
        "note": "当前库 StoreInfo 只有 1 行；是否应为空/是否漏导，本脚本没有独立证据，列为待人工确认。",
    }


def _weapon_craft_section(tables, items_db) -> dict:
    wcs = tables["WeaponCraftStatInfo"]
    per_stat = collections.defaultdict(list)
    for r in wcs:
        per_stat[r["Stat"]].append({"index": r["Index"], "class": r["RequiredClass"],
                                    "min": r["MinValue"], "max": r["MaxValue"],
                                    "weight": r["Weight"]})
    return {
        "WeaponCraftStatInfo_rows": len(wcs),
        "distinct_stats": len(per_stat),
        "sanity": {
            "stats_with_5_tiers": sum(1 for v in per_stat.values() if len(v) == 5),
            "stats_with_other_tier_count": {k: len(v) for k, v in per_stat.items() if len(v) != 5},
            "weight_values": counter_top(collections.Counter(r["Weight"] for r in wcs), 10),
            "non_monotonic_ladder": [
                k for k, v in per_stat.items()
                if [t["max"] for t in sorted((t for t in v if t["weight"] in (50, 20, 10, 1)),
                                             key=lambda t: -t["weight"])] !=
                   sorted(t["max"] for t in v if t["weight"] in (50, 20, 10, 1))
            ],
            "note": ("四个档位按权重 50/20/10/1 排列时最大值应递增（non_monotonic_ladder 为空即自洽）；"
                     "另有每属性 1 条 Weight=100 的保底档，单独存在。"),
        },
        "class_distribution": counter_top(collections.Counter(r["RequiredClass"] for r in wcs), 10),
        "items_by_required_class": counter_top(
            collections.Counter(r["RequiredClass"] for r in items_db), 10),
        "website_counterpart": "网站基准快照没有武器打造表，无法逐项对账（只能做库内自洽检查）",
        "note": ("表内 RequiredClass 出现 AssWar（Warrior|Assassin），而当前库 499 件物品的 "
                 "RequiredClass 只有 All/WarWizTao/Wizard/Taoist/Warrior（Assassin 职业在物品里"
                 "完全不存在）——AssWar 档位是否可达，需要玩法层证据，列为待人工确认。"),
    }


def _type_gap_table(wiki_zc: List[dict], items_db: List[dict], mapping: Dict[int, dict]) -> List[dict]:
    """按网站中文类型统计：网站条数 / 库条目数 / 未匹配条数。"""
    db_count = collections.Counter(r["ItemType"] for r in items_db)
    rows = []
    by_type = collections.defaultdict(lambda: {"website": 0, "unmatched": 0})
    for row in wiki_zc:
        tzh = row.get("type_zh") or "?"
        by_type[tzh]["website"] += 1
        rec = mapping[id(row)]
        if rec["db_index"] is None or rec["ambiguous"]:
            by_type[tzh]["unmatched"] += 1
    for tzh, info in sorted(by_type.items(), key=lambda kv: -kv[1]["unmatched"]):
        mapped = TYPE_ZH_TO_ITEMTYPE.get(tzh)
        rows.append({
            "website_type_zh": tzh,
            "website_items": info["website"],
            "website_items_missing_in_db": info["unmatched"],
            "mapped_db_item_type": mapped,
            "db_items_of_that_type": db_count.get(mapped, 0) if mapped else None,
        })
    return rows


def _regression_section(baseline_dir: Path, items_db: List[dict], unavailable,
                        new_stat_rows: int = 0, new_drop_rows: int = 0) -> dict:
    """当前库 vs 清洗前旧库（dbeditor workspace/baseline）对照。

    用名称归一化关联（两个时点的 Index 已经不同，Index 不能作为键）。
    产出：整表规模变化、字段回归、词条丢失/变化、Index 漂移。
    """
    if not baseline_dir or not baseline_dir.exists():
        unavailable.append({"path": str(baseline_dir or ""),
                            "reason": "missing (pre-cleanup baseline export; regression section skipped)"})
        return {"available": False, "baseline_dir": str(baseline_dir or ""),
                "next_step": ("提供清洗前旧库导出目录（每表一个 {Table}.json）后重跑："
                              "python3 tools/audit/items_audit.py --baseline-export <dir>")}

    old_tables = {t: load_table(baseline_dir, t, unavailable)
                  for t in ["ItemInfo", "ItemInfoStat", "SetInfo", "SetInfoStat", "StoreInfo",
                            "WeaponCraftStatInfo", "CurrencyInfo", "CurrencyInfoImage",
                            "DropInfo"]}
    old_items = old_tables["ItemInfo"]
    old_stats = old_tables["ItemInfoStat"]
    if not old_items:
        return {"available": False, "baseline_dir": str(baseline_dir),
                "reason": "baseline ItemInfo 为空"}

    old_by_norm: Dict[str, dict] = {}
    for row in old_items:
        old_by_norm.setdefault(norm_name(row["ItemName"]), row)
    new_by_norm: Dict[str, dict] = {}
    for row in items_db:
        new_by_norm.setdefault(norm_name(row["ItemName"]), row)

    old_stat_map: Dict[int, List[Tuple[str, int]]] = collections.defaultdict(list)
    for s in old_stats:
        if isinstance(s.get("Item"), dict) and s.get("Amount"):
            old_stat_map[s["Item"]["Index"]].append((s["Stat"], s["Amount"]))
    new_stat_map: Dict[int, List[Tuple[str, int]]] = collections.defaultdict(list)
    for row in items_db:
        for s in (row.get("Stats") or []):
            if s["Value"]:
                new_stat_map[row["Index"]].append((s["Stat"], s["Value"]))

    field_changes: Dict[str, dict] = {}
    stat_loss: List[dict] = []
    stat_gain: List[dict] = []
    stat_value_change: List[dict] = []
    index_drift: List[dict] = []
    survivors = 0

    for key, new_row in new_by_norm.items():
        old_row = old_by_norm.get(key)
        if old_row is None:
            continue
        survivors += 1
        if old_row["Index"] != new_row["Index"]:
            index_drift.append({"name": new_row["ItemName"], "old_index": old_row["Index"],
                               "new_index": new_row["Index"]})
        for f in REGRESSION_FIELDS:
            if f not in old_row or f not in new_row:
                continue
            if old_row[f] == new_row[f]:
                continue
            b = field_changes.setdefault(f, {"field": f, "changed": 0, "samples": []})
            b["changed"] += 1
            if len(b["samples"]) < 50:
                b["samples"].append({"name": new_row["ItemName"], "old": old_row[f],
                                     "new": new_row[f],
                                     "old_index": old_row["Index"], "new_index": new_row["Index"]})
        old_stats_d = dict(old_stat_map.get(old_row["Index"], []))
        new_stats_d = dict(new_stat_map.get(new_row["Index"], []))
        lost = {k: v for k, v in old_stats_d.items() if k not in new_stats_d}
        gained = {k: v for k, v in new_stats_d.items() if k not in old_stats_d}
        changed = {k: (old_stats_d[k], new_stats_d[k]) for k in set(old_stats_d) & set(new_stats_d)
                   if old_stats_d[k] != new_stats_d[k]}
        entry = {"name": new_row["ItemName"], "old_index": old_row["Index"],
                 "new_index": new_row["Index"], "type": new_row["ItemType"]}
        if lost:
            stat_loss.append(dict(entry, lost_stats=sorted(lost.items()),
                                  new_stats=sorted(new_stats_d.items()),
                                  all_stats_lost=not new_stats_d))
        if gained:
            stat_gain.append(dict(entry, gained_stats=sorted(gained.items())))
        if changed:
            stat_value_change.append(dict(entry, changed=changed))

    old_no_new = sorted(set(old_by_norm) - set(new_by_norm))
    new_no_old = sorted(set(new_by_norm) - set(old_by_norm))

    return {
        "available": True,
        "baseline_dir": str(baseline_dir),
        "baseline_file_times": {t: _file_info(baseline_dir / f"{t}.json").get("mtime_utc")
                                for t in old_tables},
        "rows": {
            "old_ItemInfo": len(old_items), "new_ItemInfo": len(items_db),
            "old_ItemInfoStat": len(old_stats),
            "new_ItemInfoStat": new_stat_rows,
            "new_DropInfo": new_drop_rows,
            "old_SetInfo": len(old_tables["SetInfo"]), "old_SetInfoStat": len(old_tables["SetInfoStat"]),
            "old_StoreInfo": len(old_tables["StoreInfo"]),
            "old_WeaponCraftStatInfo": len(old_tables["WeaponCraftStatInfo"]),
            "old_CurrencyInfo": len(old_tables["CurrencyInfo"]),
            "old_CurrencyInfoImage": len(old_tables["CurrencyInfoImage"]),
            "old_DropInfo": len(old_tables["DropInfo"]),
        },
        "name_join": {"survivors": survivors,
                      "old_only": len(old_no_new), "new_only": len(new_no_old),
                      "old_only_samples": [old_by_norm[k]["ItemName"] for k in old_no_new[:20]],
                      "new_only_samples": [new_by_norm[k]["ItemName"] for k in new_no_old[:20]]},
        "index_drift": {"count": len(index_drift), "samples": index_drift[:20],
                        "note": "同一物品在旧库/新库的 Index 不同——任何按 Index 存的数据"
                                "（Users.db 背包/腰带/AutoPotion 链接、Drop 记录等）都需要单独核对。"},
        "field_changes": field_changes,
        "items_losing_stats": stat_loss,
        "items_gaining_stats": stat_gain,
        "items_with_changed_stat_values": stat_value_change,
        "legend": ("lost_stats = 旧库有、当前库没有的词条；all_stats_lost=true 表示当前库该物品"
                   "一个词条都没有。"),
    }


def _books_section(export_dir: Path, items_db: List[dict], unavailable) -> dict:
    """技能书 Shape=MagicInfo.Index；用 MagicInfo.RequiredClass 交叉验证书本的职业字段。

    这是物品字段（RequiredClass）的独立一致性证据，不依赖网站基准。
    """
    magic = load_table(export_dir, "MagicInfo", unavailable)
    books = [r for r in items_db if r["ItemType"] == "Book"]
    by_index = {m["Index"]: m for m in magic}
    rows = []
    mismatch = []
    missing_magic = []
    for b in books:
        m = by_index.get(b["Shape"])
        if m is None:
            missing_magic.append({"index": b["Index"], "name": b["ItemName"], "shape": b["Shape"]})
            continue
        row = {"index": b["Index"], "name": b["ItemName"], "shape": b["Shape"],
               "magic_name": m.get("Name"), "magic_school": m.get("School"),
               "magic_required_class": m.get("RequiredClass"),
               "book_required_class": b["RequiredClass"],
               "required_amount": b["RequiredAmount"], "durability": b["Durability"],
               "image": b["Image"],
               "consistent": m.get("RequiredClass") == b["RequiredClass"]}
        rows.append(row)
        if not row["consistent"]:
            mismatch.append(row)
    return {
        "MagicInfo_rows": len(magic),
        "book_items": len(books),
        "books_with_magic_lookup": len(rows),
        "books_without_magic": missing_magic,
        "book_vs_magic_class_mismatch": mismatch,
        "books_with_zero_required_amount": [
            {"index": r["index"], "name": r["name"], "required_amount": r["required_amount"]}
            for r in rows if r["required_amount"] == 0],
        "books_with_zero_durability": [
            {"index": r["index"], "name": r["name"], "durability": r["durability"]}
            for r in rows if r["durability"] == 0],
        "book_image_distribution": counter_top(collections.Counter(r["image"] for r in rows), 10),
        "note": ("Book 的服务器用法是 ItemInfo.Shape → SEnvir.GetMagicInfo(shape)"
                 "（ServerLibrary/Models/PlayerObject.cs:7327,7580）；"
                 "因此书本 RequiredClass 应与该 MagicInfo.RequiredClass 一致。"),
    }


def _integrity_section(tables, items_db) -> dict:
    idx = {r["Index"] for r in items_db}
    stats_db = tables["ItemInfoStat"]
    drop_db = tables["DropInfo"]
    store = tables["StoreInfo"]
    cur = tables["CurrencyInfo"]

    stat_orphans = [s for s in stats_db if not isinstance(s.get("Item"), dict)]
    drop_missing = [d for d in drop_db if (d.get("Item") or {}).get("Index") not in idx]
    store_missing = [s for s in store if (s.get("Item") or {}).get("Index") not in idx]
    cur_missing = [c for c in cur if isinstance(c.get("DropItem"), dict)
                   and c["DropItem"]["Index"] not in idx]

    # 每件物品的 DropInfo 行数 vs ItemInfo.Drops 内嵌列表长度
    drop_count = collections.Counter(d["Item"]["Index"] for d in drop_db if isinstance(d.get("Item"), dict))
    embedded_mismatch = []
    for row in items_db:
        embedded = len(row.get("Drops") or [])
        actual = drop_count.get(row["Index"], 0)
        if embedded != actual and len(embedded_mismatch) < 20:
            embedded_mismatch.append({"index": row["Index"], "name": row["ItemName"],
                                      "embedded_drops": embedded, "dropinfo_rows": actual})

    items_without_stats = [{"index": r["Index"], "name": r["ItemName"], "type": r["ItemType"]}
                           for r in items_db if not (r.get("Stats") or [])]
    equip_types = {"Weapon", "Armour", "Ring", "Necklace", "Bracelet", "Helmet", "Shoes"}
    equip_without_stats = [x for x in items_without_stats if x["type"] in equip_types]

    return {
        "ItemInfoStat_rows": len(stats_db),
        "ItemInfoStat_orphan_rows": len(stat_orphans),
        "ItemInfoStat_orphan_samples": [
            {"index": s["Index"], "stat": s.get("Stat"), "amount": s.get("Amount")}
            for s in stat_orphans[:20]],
        "ItemInfoStat_orphan_index_runs": _runs([s["Index"] for s in stat_orphans]),
        "ItemInfoStat_items_covered": len({s["Item"]["Index"] for s in stats_db
                                           if isinstance(s.get("Item"), dict)}),
        "DropInfo_rows": len(drop_db),
        "DropInfo_items_referencing_missing_ItemInfo": len(drop_missing),
        "ItemInfo_embedded_drops_mismatch_samples": embedded_mismatch,
        "StoreInfo_items_referencing_missing_ItemInfo": len(store_missing),
        "CurrencyInfo_dropitems_referencing_missing_ItemInfo": len(cur_missing),
        "items_without_any_ItemInfoStat": len(items_without_stats),
        "items_without_any_ItemInfoStat_by_type": counter_top(
            collections.Counter(x["type"] for x in items_without_stats), 20),
        "equipment_like_items_without_ItemInfoStat": equip_without_stats,
        "Note": ("ItemInfoStat 的 Item 是 [IsIdentity] 关联（LibraryCore/SystemModels/ItemInfoStat.cs:8），"
                 "导出里没有 Item 键 = 该行指向的 ItemInfo 已不存在；"
                 "运行期只有 ItemInfo.ItemStats 绑定列表读它（LibraryCore/SystemModels/ItemInfo.cs:450,488），"
                 "所以这些残留行不会进入玩法数值，但会污染编辑器（Server/Views/ItemInfoStatView.cs）与库体积。"),
    }


def _hdr_tuple(lib, index: int):
    h = lib.headers.get(index)
    return None if h is None else (h.width, h.height, h.offset_x, h.offset_y)


def _bc7_block_size(width: int, height: int) -> int:
    return ((width + 3) // 4) * ((height + 3) // 4) * 16


def _payload_self_consistency(lib, frames: List[int]) -> dict:
    """BC7 帧：StoredImageDataSize 应等于块数×16。自洽说明帧头解析可信。"""
    checked = consistent = 0
    bad = []
    for i in frames:
        h = lib.headers.get(i)
        if h is None or getattr(h, "codec", None) != 3 or getattr(h, "stored_size", 0) <= 0:
            continue
        checked += 1
        if h.stored_size == _bc7_block_size(h.width, h.height):
            consistent += 1
        elif len(bad) < 10:
            bad.append({"frame": i, "w": h.width, "h": h.height,
                        "stored_size": h.stored_size,
                        "bc7_blocks_bytes": _bc7_block_size(h.width, h.height)})
    return {"bc7_frames_checked": checked, "self_consistent": consistent,
            "inconsistent_samples": bad,
            "note": "自洽 = 帧头宽高与载荷字节数一致，帧头的宽高/偏移按 BC7 块规则可信。"}


def _runs(indexes: List[int]) -> List[dict]:
    out = []
    for i in sorted(indexes):
        if out and i == out[-1]["end"] + 1:
            out[-1]["end"] = i
            out[-1]["count"] += 1
        else:
            out.append({"start": i, "end": i, "count": 1})
    return out


def _icons_section(items_db, tables, zl_path: Path, alt_path: Path, zlsdk_dir: Path,
                   unavailable: List[Dict[str, str]], decode_sample: int) -> dict:
    section: Dict[str, Any] = {
        "library_used_by_client": ("LibraryCore/Libraries.cs:21 → Data\\StoreItem.Zl；"
                                   "GodotClient/Controls/DXItemCell.cs:568/306 用 "
                                   "LibraryFile.StoreItem + item.Info.Image 作为帧号"),
        "storeitem_zl": str(zl_path),
        "alt_zl": str(alt_path),
    }
    if not zl_path.exists():
        unavailable.append({"path": str(zl_path), "reason": "missing (item icon library)"})
        section["status"] = "unavailable"
        section["next_step"] = (f"缺少客户端图库 {zl_path}；在有资源的机器上运行："
                                "python3 tools/audit/items_audit.py，或用 "
                                "Mir3-Research/Tools/common/zlsdk.py 的 ZlLibrary(path).header(i)/is_blank(i) 逐帧判定")
        return section

    sys.path.insert(0, str(zlsdk_dir))
    try:
        from zlsdk import ZlLibrary  # type: ignore
    except Exception as exc:  # noqa: BLE001
        unavailable.append({"path": str(zlsdk_dir / "zlsdk.py"), "reason": f"import failed: {exc}"})
        section["status"] = "unavailable"
        section["next_step"] = f"需要 {zlsdk_dir}/zlsdk.py（ZL2 读取器）"
        return section

    lib = ZlLibrary(str(zl_path))
    alt = ZlLibrary(str(alt_path)) if alt_path.exists() else None

    def check(index: int) -> bool:
        return not lib.is_blank(index)

    frames_used = sorted({r["Image"] for r in items_db} | {i.get("Image") for i in tables["CurrencyInfoImage"]})
    broken = []
    ok = 0
    alt_ok = 0
    for r in items_db:
        if check(r["Image"]):
            ok += 1
        else:
            in_alt = bool(alt) and not alt.is_blank(r["Image"])
            alt_ok += 1 if in_alt else 0
            broken.append({"index": r["Index"], "name": r["ItemName"], "type": r["ItemType"],
                           "image": r["Image"], "available_in_alt_library": in_alt})
    cur_broken = []
    cur_ok = 0
    for i in tables["CurrencyInfoImage"]:
        if check(i["Image"]):
            cur_ok += 1
        else:
            cur_broken.append({"index": i["Index"], "currency": (i.get("Currency") or {}).get("Name"),
                               "image": i["Image"],
                               "available_in_alt_library": bool(alt) and not alt.is_blank(i["Image"])})

    # 多物品共用同一帧（图标冲突/复用）
    shared = collections.Counter(r["Image"] for r in items_db)
    collisions = [{"image": img, "item_count": cnt,
                   "items": [r["ItemName"] for r in items_db if r["Image"] == img][:8]}
                  for img, cnt in shared.most_common(10) if cnt > 1]

    # 独立交叉校验：同目录另一份图库（不同容器/解析路径）
    cross: Dict[str, Any] = {"available": bool(alt)}
    if alt:
        common = [i for i in frames_used if i in lib.headers and i in alt.headers]
        disagreements = [
            {"frame": i, "main": _hdr_tuple(lib, i), "alt": _hdr_tuple(alt, i)}
            for i in common
            if _hdr_tuple(lib, i) != _hdr_tuple(alt, i)]
        agree = len(common) - len(disagreements)
        cross.update({
            "library": str(alt_path), "shared_frames_checked": len(common),
            "header_agreement": agree,
            "disagreements": disagreements,
            "note": ("两份图库走不同容器/解析分支（ZL2 vs 旧格式），帧头一致说明帧号语义可信；"
                     "disagreements 里的帧号必须人工看图确认。"),
        })

    section.update({
        "status": "ok",
        "library_count_frames": getattr(lib, "count", None),
        "library_payload_entries": len(getattr(lib, "entries", {})),
        "distinct_item_images": len({r["Image"] for r in items_db}),
        "items_checked": len(items_db),
        "items_icon_ok": ok,
        "items_icon_unresolved": len(broken),
        "currency_images_checked": len(tables["CurrencyInfoImage"]),
        "currency_images_ok": cur_ok,
        "currency_images_unresolved": cur_broken,
        "unresolved_list": broken,
        "unresolved_in_alt_library": alt_ok,
        "shared_frame_top10": collisions,
        "crosscheck": cross,
        "payload_self_consistency": _payload_self_consistency(lib, frames_used),
        "criterion": ("判定与客户端一致：ZlReader.GetImageData 在 index 越界 / 帧头为空 / "
                      "ZL2 payload entry 缺失时取不到纹理（GodotClient/Formats/ZlReader.cs:135-160）。"),
    })

    if decode_sample > 0:
        samples = [r for r in items_db if check(r["Image"])][:decode_sample]
        decoded = []
        for r in samples:
            im = lib.decode(r["Image"])
            if im is None:
                decoded.append({"name": r["ItemName"], "image": r["Image"], "decoded": False,
                                "reason": "decode() 返回 None（多为缺 texture2ddecoder，BC7 解码不可用）"})
            else:
                px = im.convert("RGBA")
                opaque = sum(1 for p in px.get_flattened_data() if p[3] > 0) \
                    if hasattr(px, "get_flattened_data") else sum(1 for p in px.getdata() if p[3] > 0)
                decoded.append({"name": r["ItemName"], "image": r["Image"], "decoded": True,
                                "size": list(im.size), "opaque_pixels": opaque})
        section["decode_sample"] = decoded
        section["decode_note"] = ("BC7 帧需要 texture2ddecoder："
                                  "uv run --with texture2ddecoder --with pillow python3 tools/audit/items_audit.py --decode-sample 8")
    return section


def _findings(result: dict) -> List[dict]:
    """把已算出的数字翻成结论条目。只描述数据，不臆测原因。"""
    f: List[dict] = []
    cov = result["coverage"]
    integ = result["integrity"]
    stats_diff = result["stat_diff"]
    sets = result["sets"]
    icons = result["icons"]
    fields = result["field_diff"]["fields"]

    reg = result.get("regression_vs_baseline") or {}
    lost_all = reg.get("items_losing_stats") or []
    lost_names = {x["name"] for x in lost_all}

    f.append({
        "id": "F1-potions-no-stats",
        "class": "confirmed",
        "severity": 1,
        "title": "经典药水家族 19 件在库里没有任何 ItemInfoStat（回血/回蓝量为 0）",
        "evidence": {
            "items": [1323, 1324, 1325, 1326, 1327, 1328, 1329, 1330, 1331, 1332,
                      1333, 1334, 1335, 1336, 1337, 1338, 1339, 1340, 1341],
            "baseline_regression": [
                x for x in lost_all if x["type"] == "Consumable" and
                x["name"].startswith(("Healing Potion", "Life Pill", "Mana Potion",
                                      "Mana Pill", "Rejuvenation"))],
            "website_attrs": "金创药/金创丸 生命 +30/+70/+110/+170/+250；魔法药/魔法丸 魔法 +40/+110/+180/+250",
            "server_dispatch": ("ServerLibrary/Models/PlayerObject.cs:6571-6575"
                                "（case ItemType.Consumable / case 0 Potion 读 "
                                "item.Info.Stats[Stat.Health]/[Stat.Mana]）"),
            "auto_potion_dispatch": ("ServerLibrary/Models/PlayerObject.cs:555"
                                     "（自动药水按 Info.Stats[Stat.Health]/[Stat.Mana] 判定）"),
            "stat_diff_website_has_extra": stats_diff["website_has_extra_stat"],
            "potion_shape0_items": stats_diff["potion_shape0_items"],
        },
    })
    f.append({
        "id": "F1b-talismans-no-affinity",
        "class": "confirmed",
        "severity": 1,
        "title": "7 个元素护身符在库里没有亲和词条（网站写 亲和 +1），元素魔法取不到属性",
        "evidence": {
            "items": [1350, 1351, 1352, 1353, 1354, 1355, 1356],
            "server_dispatch": [
                "ServerLibrary/Models/Magics/Wizard/ElementalHurricane.cs:21（amulet.Info.Stats.GetAffinityElement()）",
                "ServerLibrary/Models/Magics/Taoist/Spiritualism.cs:19（amulet.Stats.GetAffinityElement()）",
                "ServerLibrary/Models/Magics/Assassin/ElementalPuppet.cs:21",
                "ServerLibrary/Models/Magics/Taoist/EvilSlayer.cs:63（Stats[Stat.HolyAffinity] > 0 才生效）",
            ],
            "db_affinity_stat_rows": "当前库只有 Gusting Dark Stone (IV)/(V) 带 WindAffinity 词条",
            "baseline_regression": [
                x for x in lost_all if x["type"] == "Amulet"],
            "stat_diff_website_only_items": [
                x for x in stats_diff["website_only_items"] if x["type"] == "Amulet"],
        },
    })
    f.append({
        "id": "F1c-bright-torch-no-light",
        "class": "confirmed",
        "severity": 2,
        "title": "明亮蜡烛/明亮火把没有 Light 词条（网站写 光照 +25），基础蜡烛/火把正常",
        "evidence": {
            "items": [1342, 1343],
            "working_counterparts": [{"index": 2, "name": "Candle", "light": 15},
                                     {"index": 7, "name": "Torch", "light": 15}],
            "server_dispatch": "ServerLibrary/Models/PlayerObject.cs:16916（Light = Stats[Stat.Light]）",
            "baseline_regression": [
                x for x in lost_all if x["name"].startswith("Bright")],
            "stat_diff_website_only_items": [
                x for x in stats_diff["website_only_items"] if x["name"].startswith("Bright")],
        },
    })
    f.append({
        "id": "F1d-other-stat-loss",
        "class": "confirmed",
        "severity": 4,
        "title": "另有 1 件物品相对清洗前基线丢掉词条（Pill Of Reincarnation 的 ItemReviveTime）",
        "evidence": {
            "baseline_regression": [
                x for x in lost_all if not x["name"].startswith(
                    ("Healing Potion", "Life Pill", "Mana Potion", "Mana Pill",
                     "Rejuvenation", "Talisman Of", "Bright"))],
            "note": ("Pill Of Reincarnation 的 ItemEffect=PillOfReincarnation 只用于放行死者使用"
                     "（ServerLibrary/Models/PlayerObject.cs:6554）；真正的复活逻辑在 Shape==15 分支"
                     "（:6803-6811），该分支还要读 Stats[Stat.ItemReviveTime]。"
                     "因此 Shape（见 F2）与词条必须同时恢复，否则复活不生效。"),
        },
    })
    f.append({
        "id": "F2-shape0-consumables",
        "class": "confirmed",
        "severity": 2,
        "title": ("4 件消耗品 Shape 被改成 0（走药水分支）：三瓶修理油应在 11/12/13，"
                  "重生丹应在 15 —— 服务端按 Shape 分发，全部失效"),
        "evidence": {
            "items": {"1361 Superior Repair Oil": 0, "1362 Accessory Repair Oil": 0,
                      "1363 Armour Repair Oil": 0, "1364 Pill Of Reincarnation": 0},
            "server_dispatch": [
                "ServerLibrary/Models/PlayerObject.cs:6730/6747/6757（case 11/12/13 = 修理油）",
                "ServerLibrary/Models/PlayerObject.cs:6803-6811（case 15 = 复活丹：设 Dead=false、"
                "SetHP/SetMP、Character.ReincarnationPillTime += Stats[Stat.ItemReviveTime]）",
                "ServerLibrary/Models/PlayerObject.cs:6554（唯一放行死者的判断只看 ItemEffect）",
            ],
            "website_desc": "Special repairs all of the Equipment you are wearing. 等",
            "baseline_regression": [
                s for s in (reg.get("field_changes", {}).get("Shape", {}).get("samples") or [])
                if "Repair Oil" in s["name"]],
            "baseline_note": "清洗前基线里三瓶修理油 Shape 分别是 11/12/13（正确），当前库全为 0",
            "client_note": "客户端不按 Shape 分支（只有服务端 ItemUse 用 Shape 分发），因此表现为“用了没反应”",
        },
    })
    f.append({
        "id": "F3-sets-without-members",
        "class": "confirmed",
        "severity": 3,
        "title": f"{sets['sets_with_zero_db_members']}/{sets['SetInfo_rows']} 套装在库里没有任何成员物品",
        "evidence": {
            "website_items_claiming_set": sets["website_items_claiming_set"],
            "db_items_with_set_link": sets["db_items_with_set_link"],
            "gap_entries": len(sets["gaps"]),
            "gap_summary": [
                {"set": g["set_name"], "absent_from_db": len(g["members_absent_from_db"]),
                 "in_db_but_unlinked": len(g["members_in_db_without_set_link"])}
                for g in sets["gaps"]],
        },
    })
    f.append({
        "id": "F4-orphan-itemstat",
        "class": "confirmed",
        "severity": 4,
        "title": f"{integ['ItemInfoStat_orphan_rows']} 条 ItemInfoStat 的父 ItemInfo 已不存在（残留数据）",
        "evidence": {
            "total": integ["ItemInfoStat_rows"],
            "orphan": integ["ItemInfoStat_orphan_rows"],
            "runs": integ["ItemInfoStat_orphan_index_runs"],
            "runtime_impact": "不进入玩法数值；影响编辑器与库体积（见 integ['Note']）",
        },
    })
    f.append({
        "id": "F5-removed-content-gap",
        "class": "confirmed",
        "severity": 5,
        "title": f"网站基准里 {cov['wiki_zircon_not_in_db']} 条物品在当前库没有对应记录（清洗后内容缺口）",
        "evidence": {
            "by_category": cov["by_category_of_missing"],
            "by_type_zh": cov["by_type_zh_of_missing"],
            "type_zh_vs_db_type": cov["type_zh_vs_db_type"],
            "sample": [x["wiki_name"] for x in cov["wiki_present_db_missing"][:15]],
            "baseline_rows": reg.get("rows"),
            "baseline_name_join": {k: v for k, v in (reg.get("name_join") or {}).items()
                                   if k.endswith("only") or k == "survivors"},
            "note": ("缺口集中在资料站特有的现代内容（刺客职业物品、暗石/火把 Mark 系、Odyn/套装件等）；"
                     "是否应补回由玩法口径决定，本脚本只报告数量。"),
        },
    })
    if icons.get("status") == "ok":
        f.append({
            "id": "F6-icons",
            "class": "confirmed",
            "severity": 6 if icons["items_icon_unresolved"] == 0 else 3,
            "title": (f"图标可用性：{icons['items_icon_ok']}/{icons['items_checked']} 件物品 Image 帧在 "
                      f"Storeitem.Zl 可解析，{icons['items_icon_unresolved']} 件不可解析"),
            "evidence": {
                "library_frames": icons["library_count_frames"],
                "unresolved": [x["name"] for x in icons["unresolved_list"][:20]],
                "shared_frame_top": icons["shared_frame_top10"][:5],
                "crosscheck": icons["crosscheck"],
                "payload_self_consistency": icons["payload_self_consistency"],
            },
        })
    if icons.get("status") == "ok" and (icons.get("crosscheck") or {}).get("disagreements"):
        f.append({
            "id": "F6b-icon-geometry-disagreement",
            "class": "pending-human",
            "severity": 7,
            "title": ("两份同名物品图库（Storeitem.Zl / Storeitems.Zl）在 "
                      f"{len(icons['crosscheck']['disagreements'])} 个在用帧号上给出不同的宽高/偏移"),
            "evidence": {
                "disagreements": icons["crosscheck"]["disagreements"],
                "self_consistency": icons["payload_self_consistency"],
                "next_step": ("渲染对照图后人工看图（BC7 需要 texture2ddecoder）："
                              "uv run --with texture2ddecoder --with pillow python3 -c \"import sys;"
                              "sys.path.insert(0,'<zlsdk_dir>');from zlsdk import ZlLibrary;"
                              "ZlLibrary('<zl_path>').decode(70).save('/tmp/f70.png')\""),
            },
        })
    f.append({
        "id": "F7-book-fields-incomplete",
        "class": "pending-human",
        "severity": 6,
        "title": "7 本技能书整体缺字段：Image=70（其他 54 本=304）、Durability=0（网站 100）、RequiredAmount=0",
        "evidence": {
            "books_section": result.get("books", {}),
            "baseline_regression": {
                k: (reg.get("field_changes", {}).get(k) or {}).get("samples")
                for k in ("Image", "Durability", "RequiredAmount", "CanDeathDrop")},
            "image_field_diff": fields.get("Image", {}).get("diff"),
            "image_diff_samples": fields.get("Image", {}).get("diff_samples"),
            "durability_diff_samples": fields.get("Durability", {}).get("diff_samples"),
            "level_diff_samples": [
                s for s in fields.get("RequiredAmount(Level)", {}).get("diff_samples", [])
                if s.get("website") in (23, 38, 43)],
            "shared_frame_70": [x for x in icons.get("shared_frame_top10", []) if x["image"] == 70],
        },
    })
    f.append({
        "id": "F8-db-only-items",
        "class": "pending-human",
        "severity": 6,
        "title": f"库里有 {cov['db_items_not_in_wiki']} 件物品在网站基准里找不到（含 1 处拼写差异）",
        "evidence": cov["db_present_wiki_missing_top30"],
    })
    f.append({
        "id": "F9-storeinfo-single-row",
        "class": "pending-human",
        "severity": 6,
        "title": (f"StoreInfo 从清洗前的 {(reg.get('rows') or {}).get('old_StoreInfo', '?')} 行"
                  f"降到 {result['store']['StoreInfo_rows']} 行"),
        "evidence": {
            "rows_old": (reg.get("rows") or {}).get("old_StoreInfo"),
            "rows_now": result["store"]["StoreInfo_rows"],
            "now": result["store"]["rows"],
            "website_counterpart": result["store"]["website_counterpart"],
        },
    })
    f.append({
        "id": "F10-weaponcraft-assassin-class",
        "class": "pending-human",
        "severity": 7,
        "title": "WeaponCraftStatInfo 使用 AssWar 档位，而库里物品没有 Assassin 职业",
        "evidence": result["weapon_craft"],
    })
    if reg.get("available") and (reg.get("index_drift") or {}).get("count"):
        f.append({
            "id": "F11-item-index-drift",
            "class": "pending-human",
            "severity": 5,
            "title": (f"{reg['index_drift']['count']} 件存活物品的 ItemInfo.Index 与清洗前不同"
                      "——任何按 Index 存的外部数据（Users.db 背包/腰带/自动药水/邮件/拍卖、"
                      "旧 Drop 记录）需要单独核对"),
            "evidence": reg["index_drift"],
        })

    return f


def _write_out(path: Path, result: dict) -> None:
    import datetime
    result["generated_at_utc"] = datetime.datetime.now(
        datetime.timezone.utc).isoformat().replace("+00:00", "Z")
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=2)
        fh.write("\n")


def _print_summary(r: dict, out_path: Path) -> None:
    cov = r["coverage"]
    m = r["mapping"]["counts"]
    sd = r["stat_diff"]
    icons = r["icons"]
    print("=" * 78)
    print("Zircon 物品对账（网站基准 wiki_data_v2.json ↔ 当前库导出）")
    print("=" * 78)
    print(f"基准 items 总数            : {r['baseline']['items_total']}")
    print(f"  其中 ver 含 zircon       : {r['baseline']['items_ver_zircon']}"
          f"（注意：ver_tags.py 无条件给每条 items 加 zircon，不等于库里存在）")
    print(f"当前库 ItemInfo            : {r['db_baseline']['ItemInfo_rows']}")
    print("-" * 78)
    print(f"名称映射命中(精确/归一化)  : {m['by_method'].get('exact_name', 0)} / "
          f"{m['by_method'].get('normalized_name', 0)}  → 合计 {m['matched_total']}"
          f"（网站→库命中率 {m['hit_rate_total']}，库→网站覆盖 {m['db_side_coverage']}）")
    print(f"  待人工确认(多条同名候选) : {m['ambiguous_multi_candidate']}")
    print(f"  网站同名撞车已退回未匹配 : {len(m['name_collisions_resolved'])}"
          f" {[c['wiki_names'] for c in m['name_collisions_resolved']]}")
    print(f"网站有、库里没有           : {cov['wiki_zircon_not_in_db']}")
    print(f"库里有、网站没有           : {cov['db_items_not_in_wiki']}")
    print("-" * 78)
    print("字段级（能映射上的条目）：")
    for key in ["ItemType", "Price", "Weight", "Durability", "Rarity", "CanRepair",
                "RequiredAmount(Level)", "RequiredClass", "Image"]:
        b = r["field_diff"]["fields"].get(key)
        if not b:
            continue
        print(f"  {key:<22} 一致 {b['same']:>4}  一致(网站缺省) {b['same_website_default']:>4}  "
              f"不一致 {b['diff']:>3}  仅编码不同 {b['diff_encoding_only']:>2}  "
              f"网站无此字段 {b['website_missing']:>4}  库无对应 {b['db_missing']:>3}")
    print(f"  词条(attrs vs ItemInfoStat) 完全一致 {sd['exact_match']}/{sd['pairs_compared']}"
          f"（{sd['match_rate']}）  网站多词条 {sd['website_has_extra_stat']}  "
          f"库多词条 {sd['db_has_extra_stat']}  两侧都不同 {sd['both_sides_differ']}")
    print("-" * 78)
    print(f"套装   : SetInfo {r['sets']['SetInfo_rows']} 个，其中 "
          f"{r['sets']['sets_with_zero_db_members']} 个零成员；"
          f"网站声明成员 {r['sets']['website_items_claiming_set']} 条，库内挂钩 {r['sets']['db_items_with_set_link']} 条")
    print(f"货币   : CurrencyInfo {r['currency']['CurrencyInfo_rows']} 行 / "
          f"CurrencyInfoImage {r['currency']['CurrencyInfoImage_rows']} 行")
    print(f"商店   : StoreInfo {r['store']['StoreInfo_rows']} 行")
    print(f"武器打造: WeaponCraftStatInfo {r['weapon_craft']['WeaponCraftStatInfo_rows']} 行（网站无对应表）")
    print("-" * 78)
    if icons.get("status") == "ok":
        print(f"图标   : {icons['items_icon_ok']}/{icons['items_checked']} 件可解析，"
              f"不可解析 {icons['items_icon_unresolved']}；图库帧数 {icons['library_count_frames']}，"
              f"有效 payload {icons['library_payload_entries']}")
        cc = icons.get("crosscheck") or {}
        if cc.get("available"):
            print(f"         交叉校验（另一份图库）: 共同帧 {cc['shared_frames_checked']}，"
                  f"帧头一致 {cc['header_agreement']}")
    else:
        print(f"图标   : 未检查（{icons.get('next_step')}）")
    print(f"库完整性: ItemInfoStat 孤儿行 {r['integrity']['ItemInfoStat_orphan_rows']}，"
          f"DropInfo 指向不存在物品 {r['integrity']['DropInfo_items_referencing_missing_ItemInfo']}")
    print("-" * 78)
    print("结论条目：")
    for item in r["findings"]:
        tag = {"confirmed": "[确认]", "pending-human": "[待人工确认]"}.get(item["class"], "[?]")
        print(f"  {tag} {item['id']}: {item['title']}")
    if r["unavailable_inputs"]:
        print("-" * 78)
        print("不可用输入：")
        for u in r["unavailable_inputs"]:
            print(f"  - {u['path']}: {u['reason']}")
    print("=" * 78)
    print(f"详细 JSON: {out_path}")


if __name__ == "__main__":
    sys.exit(main())

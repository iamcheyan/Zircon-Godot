#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""NPC / 任务 / 宠物 / 术语本地化 四方只读对账。

数据源全部为**离线快照**，本脚本：
  * 不联网
  * 不打开/写入 System.db
  * 不启动客户端或服务端
  * 只在 stdout 打印摘要，并把详表写到一个 JSON 文件

输入（默认值可用命令行覆盖）：
  --web     网站基准 wiki_data_v2.json（npcs/quests/companion/terminology/maps/monsters）
  --dbdir   System.db 全表导出目录（每表一个 <Table>.json，形如 {"count":N,"rows":[...]}）
  --manifest  tools/npc_audit_manifest.json（在用 NPC 清单，230 条）
  --sweep   （可选）实机巡检产物目录，读 chunk_*/results.jsonl 做交叉验证
  --glossary（可选）ClientData/zh/zh-glossary.json
  --dbnames （可选）GodotClient/Translations/db_names.json
  --aliases （可选）ServerLibrary/chinese_alias.json
  --out     结果 JSON 路径

用法：
  python3.14 tools/audit/npc_quests_audit.py
  python3.14 tools/audit/npc_quests_audit.py --out /tmp/x.json --sweep ~/audit-raw/npc-full-sweep-2026-10-18

判定口径（重要，避免误报）：
  * 网站 id == 库 Index 的直接 join（已用 NPC 任务挂载数 78/78 零差异验证，见报告）。
  * 「可达页」= 从 EntryPage 出发，沿 SuccessPage 与 NPCButton.DestinationPage 做 BFS。
    这不等于实机点开的页数（实机只在按钮上点一次，脚本链中间页不单列），
    因此两者页数不同不代表缺陷。
  * 「无交互」= 可达集合里既无 NPCButton、又无 NPCAction、又无 NPCGood/NPCType，
    且所有可达页 DialogType 都是 None。
  * KillMonster 任务是否有 MonsterDetails 决定它能否推进
    （ServerLibrary/Models/MonsterObject.cs 只在 MonsterDetails 命中时累加进度）。
"""

from __future__ import annotations

import argparse
import collections
import glob
import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone

# ---------------------------------------------------------------- 路径默认值

HOME = os.path.expanduser("~")
DEFAULT_WEB = os.path.join(
    HOME,
    "Development/Mir3-Research/docs/research/ei-ui-layout/artifacts/"
    "web-entity-audit-2026-09-26/raw/wiki_data_v2.json",
)
DEFAULT_DBDIR = os.path.join(HOME, "audit-raw/db-export-2026-10-18/out")
DEFAULT_MANIFEST = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "npc_audit_manifest.json"
)
DEFAULT_SWEEP = os.path.join(HOME, "audit-raw/npc-full-sweep-2026-10-18")
ZIRCON_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_OUT = os.path.join(
    ZIRCON_ROOT, ".artifacts/game-audit-2026-10-18/data/npc_quests_companion.json"
)

CJK = re.compile(r"[\u3400-\u9fff]")


# ---------------------------------------------------------------- 工具函数


def log(msg: str) -> None:
    print(msg, flush=True)


def sha256(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_json(path: str):
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


class Table:
    """System.db 单表导出。导出器会省略空关联键，因此所有取值都走 .get。"""

    def __init__(self, dbdir: str, name: str):
        self.name = name
        self.path = os.path.join(dbdir, name + ".json")
        if not os.path.exists(self.path):
            self.rows, self.declared = [], None
            self.missing = True
            return
        raw = load_json(self.path)
        self.missing = False
        if isinstance(raw, dict) and "rows" in raw:
            self.rows = raw["rows"] or []
            self.declared = raw.get("count")
        else:  # 兼容裸数组
            self.rows = raw or []
            self.declared = len(self.rows)

    def __len__(self):
        return len(self.rows)

    def by_index(self, key: str = "Index"):
        return {r[key]: r for r in self.rows if key in r}

    def ref(self, row: dict, key: str):
        v = row.get(key)
        return v.get("Index") if isinstance(v, dict) else None

    def ref_name(self, row: dict, key: str):
        v = row.get(key)
        return v.get("Name") if isinstance(v, dict) else None


# 网站 desc / 库 Region 描述 的功能归一化关键词表。
# 用于判断「官网某商人缺失」是否已被库内**另一个 Index** 的功能位覆盖。
FUNC_TOKENS = [
    ("weapon_refine", ("weapon refiner", "refine smith", "weapon craft", "refiner")),
    ("weapon", ("weapon main", "weapon store")),
    ("potion", ("basic potion", "potion store", "potion main")),
    ("essential", ("essentials", "essential store", "essentials main")),
    ("accessory", ("jewellery", "accessory store", "jewellery main", "accessory refiner")),
    ("collector", ("collector",)),
    ("armour", ("armour main", "armour store", "armour")),
    ("meat", ("meat main", "butcher", "meat")),
    ("book", ("book main", "book store", "book")),
    ("notice", ("notice board",)),
    ("companion", ("companion main", "companion")),
    ("horse", ("horse main", "horse", "stables")),
    ("marriage", ("marriage",)),
    ("fragment", ("item fragment",)),
    ("emblem", ("emblem",)),
    ("teleport", ("teleport",)),
    ("storage", ("storage", "warehouse", "inn")),
]


def func_of(*texts) -> str:
    blob = " ".join(t for t in texts if t).lower()
    for name, toks in FUNC_TOKENS:
        if any(t in blob for t in toks):
            return name
    return "other"


# ---------------------------------------------------------------- 主流程


def main() -> int:
    ap = argparse.ArgumentParser(description="NPC/任务/宠物/术语 只读对账")
    ap.add_argument("--web", default=DEFAULT_WEB)
    ap.add_argument("--dbdir", default=DEFAULT_DBDIR)
    ap.add_argument("--manifest", default=DEFAULT_MANIFEST)
    ap.add_argument("--sweep", default=DEFAULT_SWEEP)
    ap.add_argument("--glossary", default=os.path.join(ZIRCON_ROOT, "ClientData/zh/zh-glossary.json"))
    ap.add_argument("--dbnames", default=os.path.join(ZIRCON_ROOT, "GodotClient/Translations/db_names.json"))
    ap.add_argument("--aliases", default=os.path.join(ZIRCON_ROOT, "ServerLibrary/chinese_alias.json"))
    ap.add_argument("--out", default=DEFAULT_OUT)
    args = ap.parse_args()

    web = load_json(args.web)
    web_npcs = {n["id"]: n for n in web.get("npcs", [])}
    web_quests = {q["id"]: q for q in web.get("quests", [])}
    web_companions = {c["id"]: c for c in web.get("companion", [])}
    web_maps = {}
    for m in web.get("maps", []):
        web_maps.setdefault(str(m.get("file")), m)
    terminology = web.get("terminology", {}) or {}

    db = {t: Table(args.dbdir, t) for t in (
        "NPCInfo", "NPCPage", "NPCButton", "NPCCheck", "NPCAction", "NPCType",
        "NPCGood", "NPCValue", "QuestInfo", "QuestTask", "QuestRequirement",
        "QuestReward", "QuestTaskMonsterDetails", "CompanionInfo",
        "CompanionLevelInfo", "CompanionSkillInfo", "MapInfo", "MapRegion",
        "MonsterInfo", "ItemInfo",
    )}

    npc_info = db["NPCInfo"].rows
    npc_idx = {r["Index"]: r for r in npc_info}
    page_idx = db["NPCPage"].by_index()
    mapinfo = {r["FileName"]: r for r in db["MapInfo"].rows}
    monster_names = {r["Index"]: r["MonsterName"] for r in db["MonsterInfo"].rows}
    item_names = {r["Index"]: r["ItemName"] for r in db["ItemInfo"].rows}

    def region_name(row):
        reg = row.get("Region")
        return reg.get("Name") if isinstance(reg, dict) else None

    def region_map(row):
        rn = region_name(row)
        return rn.split(" / ")[0] if rn else None

    # ---- 页链索引 -------------------------------------------------------
    buttons = collections.defaultdict(list)
    for b in db["NPCButton"].rows:
        buttons[db["NPCButton"].ref(b, "Page")].append(b)
    actions = collections.defaultdict(list)
    for a in db["NPCAction"].rows:
        actions[db["NPCAction"].ref(a, "Page")].append(a)
    goods = collections.defaultdict(list)
    for g in db["NPCGood"].rows:
        goods[db["NPCGood"].ref(g, "Page")].append(g)
    types = collections.defaultdict(list)
    for t in db["NPCType"].rows:
        types[db["NPCType"].ref(t, "Page")].append(t)

    def reach(entry):
        seen, stack = {entry}, [entry]
        while stack:
            cur = stack.pop()
            page = page_idx.get(cur)
            if not page:
                continue
            nxt = []
            succ = page.get("SuccessPage")
            if isinstance(succ, dict):
                nxt.append(succ["Index"])
            for b in buttons.get(cur, []):
                d = b.get("DestinationPage")
                if isinstance(d, dict):
                    nxt.append(d["Index"])
            for n in nxt:
                if n not in seen:
                    seen.add(n)
                    stack.append(n)
        return seen

    # ---- 清单 -----------------------------------------------------------
    manifest = load_json(args.manifest) if os.path.exists(args.manifest) else {"npcs": []}
    manifest_idx = {n["index"] for n in manifest.get("npcs", [])}

    in_use = [r for r in npc_info if region_name(r)]
    reserved = [r for r in npc_info if not region_name(r)]
    in_use_idx = {r["Index"] for r in in_use}

    # =====================================================================
    # 1. NPC 双向对账
    # =====================================================================
    log("=" * 72)
    log("[1] NPC 对账  网站 %d  vs  库 %d（在用 %d / 预留 %d）"
        % (len(web_npcs), len(npc_info), len(in_use), len(reserved)))
    log("=" * 72)

    db_funcs = collections.defaultdict(set)   # map -> {func}
    db_func_owner = collections.defaultdict(list)
    for r in in_use:
        mp = region_map(r)
        f = func_of(region_name(r))
        db_funcs[mp].add(f)
        db_func_owner[(mp, f)].append(r["Index"])

    web_only = []
    for i in sorted(web_npcs):
        if i in npc_idx:
            continue
        w = web_npcs[i]
        wmap = str(w.get("map"))
        wfunc = func_of(w.get("desc"))
        entry = {
            "id": i,
            "name": w.get("name"),
            "zh": w.get("zh"),
            "desc": w.get("desc"),
            "map": wmap,
            "mapName": (web_maps.get(wmap) or {}).get("name"),
            "func": wfunc,
        }
        if wmap not in mapinfo:
            entry["kind"] = "map_absent_in_db"      # 地图整体不在库（经典清洗移除）
            mp_name = (web_maps.get(wmap) or {}).get("name")
            entry["reason"] = "库内无该地图：%s" % (mp_name or wmap)
        elif wfunc in db_funcs.get(wmap, set()):
            entry["kind"] = "func_covered_other_index"
            entry["coveredBy"] = db_func_owner[(wmap, wfunc)]
        else:
            entry["kind"] = "suspicious_missing"
        web_only.append(entry)

    kind_count = collections.Counter(e["kind"] for e in web_only)

    db_only_in_use = []
    for r in in_use:
        if r["Index"] in web_npcs:
            continue
        e = r["EntryPage"]["Index"]
        R = reach(e)
        has_shop = any(goods.get(p) for p in R)
        has_selltype = any(types.get(p) for p in R)
        has_action = any(actions.get(p) for p in R)
        act_types = sorted({a["ActionType"] for p in R for a in actions.get(p, [])})
        db_only_in_use.append({
            "index": r["Index"],
            "name": r["NPCName"],
            "region": region_name(r),
            "map": region_map(r),
            "func": func_of(region_name(r)),
            "reachedPages": len(R),
            "hasShop": has_shop,
            "hasSellType": has_selltype,
            "actionTypes": act_types,
            "kind": ("teleporter" if "Teleport" in act_types
                     else "merchant" if (has_shop or has_selltype)
                     else "storage" if "Storage" in act_types
                     else "other"),
        })
    db_only_kind = collections.Counter(e["kind"] for e in db_only_in_use)

    # 任务挂载数双向核对（join 正确性的独立验证）
    attach_mismatch = []
    for r in in_use:
        i = r["Index"]
        if i not in web_npcs:
            continue
        s = len(r.get("StartQuests") or [])
        f = len(r.get("FinishQuests") or [])
        w = web_npcs[i]
        if s != w.get("quests_in") or f != w.get("quests_out"):
            attach_mismatch.append({
                "index": i, "name": r["NPCName"],
                "db": [s, f], "web": [w.get("quests_in"), w.get("quests_out")],
            })

    log("  网站有、库无 : %d  (地图缺失 %d / 功能已被他 index 覆盖 %d / 真可疑 %d)"
        % (len(web_only), kind_count["map_absent_in_db"],
           kind_count["func_covered_other_index"], kind_count["suspicious_missing"]))
    log("  库有、网站无 : 在用 %d（传送 %d / 商人 %d / 仓库 %d / 其它 %d）+ 预留模板 %d"
        % (len(db_only_in_use), db_only_kind["teleporter"], db_only_kind["merchant"],
           db_only_kind["storage"], db_only_kind["other"], len(reserved)))
    log("  双向任务挂载数不一致 : %d（0 = join 可信）" % len(attach_mismatch))

    # 中文命名体系核对：join 得到的两侧 NPC，其「功能位描述」是否一致、中文名是否一致。
    # 功能位一致但中文名全不同 → join 可信 + 两套命名体系（原版中文名 vs 英文音译）。
    naming = []
    for r in in_use:
        i = r["Index"]
        if i not in web_npcs:
            continue
        w = web_npcs[i]
        reg = region_name(r) or ""
        reg_desc = reg.split(" / ")[-1]
        # 用同一套功能桶（FUNC_TOKENS）判两侧说的是不是一个职能位，避免同义写法被误判。
        # 网站侧 name 与 desc 是两个字段，任一命中即算职能位一致（desc 常被写成泛化的 Notice Board）。
        role_bucket = func_of(reg_desc)
        same_role = role_bucket in (func_of(w.get("desc")), func_of(w.get("name")))
        naming.append({
            "index": i, "dbName": r["NPCName"], "webName": w.get("name"),
            "webZh": w.get("zh"), "dbRegion": reg, "webDesc": w.get("desc"),
            "sameRole": same_role,
            "sameZhName": (w.get("zh") or "").strip() == (r["NPCName"] or "").strip(),
        })
    role_same = sum(1 for x in naming if x["sameRole"])
    zh_same = sum(1 for x in naming if x["sameZhName"])
    log("  join 到的 %d 个 NPC：功能位描述一致 %d，中文名一致 %d"
        % (len(naming), role_same, zh_same))

    # =====================================================================
    # 2. NPC 服务链路完整性
    # =====================================================================
    log("")
    log("=" * 72)
    log("[2] NPC 服务链路完整性（%d 个在用 NPC）" % len(in_use))
    log("=" * 72)

    FALLBACK_RE = re.compile(r"fallback", re.I)
    TESTPAGE_RE = re.compile(r"_test|mugong_|gmatch", re.I)
    service = []
    for r in in_use:
        e = r["EntryPage"]["Index"]
        R = reach(e)
        page = page_idx.get(e, {})
        dts = sorted({page_idx[p].get("DialogType") for p in R if p in page_idx
                      and page_idx[p].get("DialogType") not in (None, "None")})
        act_types = sorted({a["ActionType"] for p in R for a in actions.get(p, [])})
        say = page.get("Say") or ""
        # 去掉 [xxx:0] 这类“关闭/离开”选项后还剩什么？
        say_body = re.sub(r"\[[^\]]*:0\]", "", say).strip()
        entry_buttons = len(buttons.get(e, []))
        all_buttons = sum(len(buttons.get(p, [])) for p in R)
        has_shop = any(goods.get(p) for p in R)
        has_selltype = any(types.get(p) for p in R)
        has_action = any(actions.get(p) for p in R)
        flags = []
        # 玩家点开之后完全没得点：全链无按钮、无动作、无商店、无特殊对话框
        if not all_buttons and not has_action and not has_shop \
                and not has_selltype and not dts:
            flags.append("dead_dialog")
        # 入口页连文本都没有（去掉关闭项后）
        if not say_body:
            flags.append("blank_entry_say")
        if FALLBACK_RE.search(page.get("Description", "")):
            flags.append("fallback_entry")
        elif TESTPAGE_RE.search(page.get("Description", "")):
            flags.append("test_named_entry")
        if len(R) <= 1 and not entry_buttons:
            flags.append("single_page_no_buttons")
        service.append({
            "index": r["Index"], "name": r["NPCName"], "region": region_name(r),
            "map": region_map(r),
            "entryPage": e, "entryPageName": page.get("Description"),
            "entryDialogType": page.get("DialogType"),
            "reachedPages": len(R), "entryButtons": entry_buttons,
            "allButtons": all_buttons,
            "dialogTypes": dts, "actionTypes": act_types,
            "shopPages": sum(1 for p in R if goods.get(p)),
            "sellTypePages": sum(1 for p in R if types.get(p)),
            "sayChars": len(say_body),
            "flags": flags,
        })
    flag_count = collections.Counter(f for s in service for f in s["flags"])
    DEFECT_FLAGS = {"dead_dialog", "blank_entry_say", "fallback_entry"}
    broken = [s for s in service if DEFECT_FLAGS & set(s["flags"])]
    info_only = [s for s in service
                 if not (DEFECT_FLAGS & set(s["flags"])) and s["flags"]]
    log("  链路确认缺陷条目 : %d   仅提示条目（原版 test 页命名等）: %d"
        % (len(broken), len(info_only)))
    for k, v in sorted(flag_count.items()):
        log("    - %-24s %d" % (k, v))

    # 缺陷 / 提示 明细
    for s in service:
        if s["flags"]:
            log("      %-6s %-14s %-42s %s" % (s["index"], s["name"],
                                               (s["entryPageName"] or "")[:42], s["flags"]))

    # 悬挂引用（结构性检查）
    # 注意：导出器省略空关联键，取不到的键必须跳过，否则会把“无 EntryPage 的预留模板”误报成悬挂。
    dangling = {
        "EntryPage": [r["Index"] for r in npc_info
                      if isinstance(r.get("EntryPage"), dict)
                      and r["EntryPage"]["Index"] not in page_idx],
        "SuccessPage": [r["Index"] for r in db["NPCPage"].rows
                        if isinstance(r.get("SuccessPage"), dict)
                        and r["SuccessPage"]["Index"] not in page_idx],
        "Button.Page": [b["Index"] for b in db["NPCButton"].rows
                        if isinstance(b.get("Page"), dict)
                        and b["Page"]["Index"] not in page_idx],
        "Button.DestinationPage": [b["Index"] for b in db["NPCButton"].rows
                                   if isinstance(b.get("DestinationPage"), dict)
                                   and b["DestinationPage"]["Index"] not in page_idx],
    }
    for tbl, key in (("NPCAction", "Page"), ("NPCGood", "Page"), ("NPCType", "Page"),
                     ("NPCValue", "Page"), ("NPCCheck", "Page")):
        dangling[tbl + "." + key] = [
            r["Index"] for r in db[tbl].rows
            if isinstance(r.get(key), dict) and r[key]["Index"] not in page_idx
        ]
    log("  悬挂页引用 : %d 项非空（全部为 0 表示页图闭合）"
        % sum(1 for v in dangling.values() if v))

    # SuccessPage 环分析。
    # 语义依据 ServerLibrary/Models/NPCObject.cs::NPCCall：
    #   * 只有当页 Say 为空时才自动跳 SuccessPage；
    #   * 因此“纯空页构成的 SuccessPage 环”会让 while(true) 永不退出（服务端线程挂死），
    #     而“环里存在非空 Say 页”则只是正常回退，不会死循环。
    succ_of = {}
    for p, row in page_idx.items():
        s = row.get("SuccessPage")
        if isinstance(s, dict) and s["Index"] != p:   # 自指 = 无后续（原版约定）
            succ_of[p] = s["Index"]
    empty_say = {p for p, row in page_idx.items() if not (row.get("Say") or "").strip()}
    loops, seen_loops = [], set()
    for p in sorted(empty_say):
        trail, cur = {}, p
        while cur is not None and cur in empty_say and cur not in trail:
            trail[cur] = len(trail)
            cur = succ_of.get(cur)
        if cur is not None and cur in trail:
            loop = tuple(x for x, i in sorted(trail.items(), key=lambda kv: kv[1])
                         if i >= trail[cur])
            key = frozenset(loop)
            if key not in seen_loops:
                seen_loops.add(key)
                loops.append(loop)
    loop_pages = {x for l in loops for x in l}
    loop_entries = [(r["Index"], r["NPCName"]) for r in in_use
                    if r["EntryPage"]["Index"] in loop_pages]
    loop_buttons = [(b["Index"], (b.get("Page") or {}).get("Name"), b.get("ButtonID"))
                    for b in db["NPCButton"].rows
                    if isinstance(b.get("DestinationPage"), dict)
                    and b["DestinationPage"]["Index"] in loop_pages]
    log("  纯空页 SuccessPage 死循环组件 : %d（入口命中 %d / 按钮命中 %d）"
        % (len(loops), len(loop_entries), len(loop_buttons)))

    # 环形但含有非空 Say 的链（原版脚本的“回退页”写法，NPCCall 会在有文本的页停下，非缺陷）
    ring_safe = []
    for r in in_use:
        trail, cur = {}, r["EntryPage"]["Index"]
        while cur is not None and cur not in trail:
            trail[cur] = 1
            cur = succ_of.get(cur)
        if cur is not None:
            ring_safe.append(r["Index"])
    log("  （成功页链成环但停在有文本页的 NPC : %d，非缺陷）" % len(ring_safe))

    # 实机巡检交叉验证
    live = {"available": False}
    if args.sweep and os.path.isdir(args.sweep):
        rows = []
        for f in sorted(glob.glob(os.path.join(args.sweep, "chunk_*/results.jsonl"))):
            with open(f, "r", encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if line:
                        try:
                            rows.append(json.loads(line))
                        except json.JSONDecodeError:
                            pass
        if rows:
            by_id = {r["index"]: r for r in rows}
            live = {
                "available": True,
                "dir": args.sweep,
                "done": len(rows),
                "clickedFalse": [r["index"] for r in rows if not r.get("clicked")],
                "withErrors": {str(r["index"]): r.get("errors") for r in rows if r.get("errors")},
                "livePagesMin": min(len(r.get("pages") or []) for r in rows),
                "livePagesMax": max(len(r.get("pages") or []) for r in rows),
            }
            for s in service:
                lr = by_id.get(s["index"])
                if lr is None:
                    s["liveStatus"] = "not_swept"
                elif not lr.get("clicked"):
                    s["liveStatus"] = "click_missed"
                    s["liveErrors"] = lr.get("errors")
                else:
                    s["liveStatus"] = "ok"
                    s["livePages"] = [p["page"] for p in (lr.get("pages") or [])]
            confirmed = [s["index"] for s in broken if s.get("liveStatus") == "ok"]
            live["defectsLiveConfirmed"] = confirmed
            live["defectsStillStaticOnly"] = [s["index"] for s in broken
                                              if s.get("liveStatus") == "not_swept"]
            log("  实机巡检（进行中）: 已回 %d/%d，点击失败 %d，报错 %d；"
                "本次缺陷中已被实机复核 %d 个" %
                (len(rows), len(in_use), len(live["clickedFalse"]),
                 len(live["withErrors"]), len(confirmed)))

    # =====================================================================
    # 3. 任务对账
    # =====================================================================
    log("")
    log("=" * 72)
    log("[3] 任务对账  网站 %d  vs  库 %d" % (len(web_quests), len(db["QuestInfo"])))
    log("=" * 72)

    quests = {r["Index"]: r for r in db["QuestInfo"].rows}
    tasks_by_quest = collections.defaultdict(list)
    for t in db["QuestTask"].rows:
        tasks_by_quest[db["QuestTask"].ref(t, "Quest")].append(t)
    details_by_task = collections.defaultdict(list)
    for d in db["QuestTaskMonsterDetails"].rows:
        details_by_task[db["QuestTaskMonsterDetails"].ref(d, "Task")].append(d)
    reqs_by_quest = collections.defaultdict(list)
    for r in db["QuestRequirement"].rows:
        reqs_by_quest[db["QuestRequirement"].ref(r, "Quest")].append(r)
    rewards_by_quest = collections.defaultdict(list)
    for r in db["QuestReward"].rows:
        rewards_by_quest[db["QuestReward"].ref(r, "Quest")].append(r)
    kill_task_rows = [t for t in db["QuestTask"].rows
                      if t.get("Task") == "KillMonster" and not details_by_task.get(t["Index"])]

    exp_re = re.compile(r"Experience\s*[×x]\s*([0-9]+)")
    quest_rows = []
    for qi in sorted(quests):
        q = quests[qi]
        w = web_quests.get(qi)
        tasks = tasks_by_quest.get(qi, [])
        reqs = reqs_by_quest.get(qi, [])
        rewards = rewards_by_quest.get(qi, [])
        issues = []
        if not tasks:
            issues.append("no_task")
        if not rewards:
            issues.append("no_reward")
        for t in tasks:
            if t.get("Task") == "KillMonster" and not details_by_task.get(t["Index"]):
                issues.append("kill_task_without_monster#%d" % t["Index"])
            if t.get("Task") == "GainItem" and not isinstance(t.get("ItemParameter"), dict):
                issues.append("gain_task_without_item#%d" % t["Index"])
            if t.get("Amount", 0) <= 0:
                issues.append("task_amount_zero#%d" % t["Index"])
        for t in tasks:
            if t.get("Task") == "KillMonster":
                for d in details_by_task.get(t["Index"], []):
                    mi = db["QuestTaskMonsterDetails"].ref(d, "Monster")
                    if mi not in monster_names:
                        issues.append("dead_monster_ref#%d" % t["Index"])
        web_exp = None
        if w:
            m = exp_re.search(w.get("rewards") or "")
            web_exp = int(m.group(1)) if m else None
        if web_exp and not rewards:
            issues.append("web_exp_%d_unrepresented" % web_exp)
        if w and not tasks and (w.get("goals") or "").strip() in ("", "："):
            issues.append("web_also_no_goal")
        start = db["QuestInfo"].ref(q, "StartNPC")
        finish = db["QuestInfo"].ref(q, "FinishNPC")
        if start not in npc_idx:
            issues.append("start_npc_missing")
        if finish not in npc_idx:
            issues.append("finish_npc_missing")
        quest_rows.append({
            "index": qi,
            "name": q.get("QuestName"),
            "type": q.get("QuestType"),
            "inWeb": qi in web_quests,
            "startNPC": {"index": start, "name": npc_idx.get(start, {}).get("NPCName")},
            "finishNPC": {"index": finish, "name": npc_idx.get(finish, {}).get("NPCName")},
            "tasks": [{"index": t["Index"], "type": t.get("Task"),
                       "amount": t.get("Amount"),
                       "item": db["QuestTask"].ref_name(t, "ItemParameter"),
                       "monsters": [db["QuestTaskMonsterDetails"].ref_name(d, "Monster")
                                    for d in details_by_task.get(t["Index"], [])]}
                      for t in tasks],
            "requirements": len(reqs),
            "rewards": [{"index": r["Index"],
                         "item": db["QuestReward"].ref_name(r, "Item") or "(无 Item → 无实际发放)",
                         "amount": r.get("Amount")} for r in rewards],
            "webExperience": web_exp,
            "webGoals": (w or {}).get("goals"),
            "issues": issues,
        })

    orphan_tasks = [t for t in db["QuestTask"].rows
                    if db["QuestTask"].ref(t, "Quest") not in quests]
    orphan_reqs = [r for r in db["QuestRequirement"].rows
                   if db["QuestRequirement"].ref(r, "Quest") not in quests]
    orphan_rewards = [r for r in db["QuestReward"].rows
                      if db["QuestReward"].ref(r, "Quest") not in quests]
    orphan_details = [d for d in db["QuestTaskMonsterDetails"].rows
                      if db["QuestTaskMonsterDetails"].ref(d, "Task")
                      not in {t["Index"] for t in db["QuestTask"].rows}]

    web_only_quests = sorted(set(web_quests) - set(quests))
    db_only_quests = sorted(set(quests) - set(web_quests))

    issue_count = collections.Counter(i.split("#")[0].split("_unrepresented")[0]
                                      for r in quest_rows for i in r["issues"])
    log("  网站有库无 : %d   %s" % (len(web_only_quests), web_only_quests))
    log("  库有网站无 : %d   %s" % (len(db_only_quests), db_only_quests))
    log("  无任务(no_task) : %d   %s"
        % (issue_count["no_task"], [r["index"] for r in quest_rows if "no_task" in r["issues"]]))
    log("  无奖励(no_reward) : %d" % issue_count["no_reward"])
    log("  KillMonster 任务缺 MonsterDetails（永远无法推进）: %d 条（其中孤儿任务 %d 条）"
        % (len(kill_task_rows), len([t for t in kill_task_rows
                                     if db["QuestTask"].ref(t, "Quest") not in quests])))
    log("  网站标注了经验奖励但库内无任何奖励 : %d"
        % sum(1 for r in quest_rows if any("web_exp_" in i for i in r["issues"])))
    log("  孤儿行  QuestTask=%d QuestRequirement=%d QuestReward=%d MonsterDetails=%d"
        % (len(orphan_tasks), len(orphan_reqs), len(orphan_rewards), len(orphan_details)))

    # =====================================================================
    # 4. 宠物对账
    # =====================================================================
    log("")
    log("=" * 72)
    log("[4] 宠物对账  网站 %d  vs  库 %d" % (len(web_companions), len(db["CompanionInfo"])))
    log("=" * 72)

    comps = {r["Index"]: r for r in db["CompanionInfo"].rows}
    comp_rows = []
    for ci in sorted(set(comps) | set(web_companions)):
        c, w = comps.get(ci), web_companions.get(ci)
        diffs, notes = [], []
        if c and w:
            mi = db["CompanionInfo"].ref(c, "MonsterInfo")
            if mi != w.get("monster_id"):
                diffs.append("monster_id %s != %s" % (mi, w.get("monster_id")))
            if c.get("Price") != w.get("price"):
                diffs.append("price %s != %s" % (c.get("Price"), w.get("price")))
            if bool(c.get("Available")) != bool(w.get("available")):
                diffs.append("available %s != %s" % (c.get("Available"), w.get("available")))
            if (c.get("_Identity") or "").strip() != (w.get("name") or "").strip():
                diffs.append("name %r != %r" % (c.get("_Identity"), w.get("name")))
            if mi is not None and mi not in monster_names:
                diffs.append("monster %s 不存在于 MonsterInfo" % mi)
        elif c:
            diffs.append("库有网站无")
        else:
            diffs.append("网站有库无")
        if c:
            if not (c.get("Description") or "").strip():
                notes.append("Description 为空（网站同样未提供描述，非分歧）")
            if (c.get("_Identity") or "") != (c.get("_Identity") or "").strip():
                notes.append("名称含首尾空格（_Identity=%r）" % c.get("_Identity"))
        comp_rows.append({
            "index": ci,
            "inDb": bool(c), "inWeb": bool(w),
            "dbName": (c or {}).get("_Identity"),
            "webName": (w or {}).get("name"),
            "monster": db["CompanionInfo"].ref(c, "MonsterInfo") if c else None,
            "monsterName": monster_names.get(db["CompanionInfo"].ref(c, "MonsterInfo")) if c else None,
            "price": (c or {}).get("Price"), "available": (c or {}).get("Available"),
            "diffs": diffs, "notes": notes,
        })
    levels = sorted(r["Level"] for r in db["CompanionLevelInfo"].rows)
    skill_levels = sorted({r["Level"] for r in db["CompanionSkillInfo"].rows})
    log("  逐条字段差异 : %d 条" % sum(1 for c in comp_rows if c["diffs"]))
    for c in comp_rows:
        if c["diffs"]:
            log("      %-3s %-14s %s" % (c["index"], c["dbName"], c["diffs"]))
    log("  等级表 1..%d 连续=%s；技能表覆盖等级 %s（%d 行）"
        % (max(levels or [0]), levels == list(range(1, (max(levels) if levels else 0) + 1)),
           skill_levels, len(db["CompanionSkillInfo"])))

    # =====================================================================
    # 5. 术语 / 本地化
    # =====================================================================
    log("")
    log("=" * 72)
    log("[5] 术语本地化  网站术语表 %d 条" % len(terminology))
    log("=" * 72)

    inv = collections.defaultdict(list)
    for en, zh in terminology.items():
        inv[zh].append(en)
    term_dup_zh = {zh: ens for zh, ens in inv.items() if len(ens) > 1}
    term_en_eq_zh = [k for k, v in terminology.items() if k == v]
    term_empty = [k for k, v in terminology.items() if not (v or "").strip()]
    term_latin = [k for k, v in terminology.items() if re.search(r"[A-Za-z]", v or "")]

    cross = {}
    for label, path, sections in (
        ("zh-glossary", args.glossary, ("items", "magics", "maps", "monsters", "npcs")),
        ("db_names", args.dbnames, ("items", "monsters", "npcs", "magics", "maps")),
    ):
        if not os.path.exists(path):
            cross[label] = {"available": False}
            continue
        data = load_json(path)
        # 这些文件的 items 段同时装有物品名与法术/技能名，与 magics 段会重复。
        # 因此先按「英文名 → 该文件给出的中文名集合」做合并，再判分歧，
        # 否则同一英文名会被计两次、把分歧数虚高。
        en2zh = collections.defaultdict(set)
        for sec in sections:
            for en, v in (data.get(sec) or {}).items():
                zh = v.get("zh") if isinstance(v, dict) else None
                if zh and zh.strip():
                    en2zh[en].add(zh)
        overlap = [en for en in en2zh if en in terminology]
        conflicts = [en for en in overlap if terminology[en] not in en2zh[en]]
        internal = {en: sorted(v) for en, v in en2zh.items() if len(v) > 1}
        cross[label] = {
            "available": True, "path": path,
            "overlap": len(overlap),
            "disagreements": len(conflicts),
            "internalSectionConflicts": len(internal),
            "detail": [{"en": en, "terminology": terminology[en],
                        "file": sorted(en2zh[en])} for en in sorted(conflicts)],
            "internalDetail": dict(sorted(((k, v) for k, v in internal.items()))[:200]),
        }

    # ServerLibrary/chinese_alias.json 是「中文 → 英文」的反向表，先翻转再比。
    if os.path.exists(args.aliases):
        raw = load_json(args.aliases)
        en2zh = collections.defaultdict(set)
        for sec, m in (raw or {}).items():
            if not isinstance(m, dict):
                continue
            for zh, en in m.items():
                if isinstance(en, str) and en:
                    en2zh[en.lstrip("!")].add(zh)
        overlap = [en for en in en2zh if en in terminology]
        conflicts = [en for en in overlap if terminology[en] not in en2zh[en]]
        cross["chinese_alias"] = {
            "available": True, "path": args.aliases,
            "overlap": len(overlap), "disagreements": len(conflicts),
            "internalSectionConflicts": sum(1 for v in en2zh.values() if len(v) > 1),
            "detail": [{"en": en, "terminology": terminology[en],
                        "file": sorted(en2zh[en])} for en in sorted(conflicts)],
            "internalDetail": {},
        }

    npc_name_dup = collections.defaultdict(list)
    for r in npc_info:
        npc_name_dup[r["NPCName"]].append(r["Index"])
    npc_dups = {k: v for k, v in npc_name_dup.items() if len(v) > 1}

    covered_monsters = sum(1 for n in monster_names.values() if n in terminology)
    missing_monsters = sorted(n for n in monster_names.values() if n not in terminology)
    covered_items = sum(1 for n in item_names.values() if n in terminology)
    missing_items = sorted(n for n in item_names.values() if n not in terminology)

    log("  术语表内部：中文值被多英文名共用 %d 项；英文==中文 %d；空值 %d"
        % (len(term_dup_zh), len(term_en_eq_zh), len(term_empty)))
    for zh, ens in sorted(term_dup_zh.items(), key=lambda x: -len(x[1])):
        log("      %-12s <- %s" % (zh, ens))
    for label, c in cross.items():
        if c.get("available"):
            log("  %-12s ∩术语表 %d，命名分歧 %d，文件内 sections 自相矛盾 %d"
                % (label, c["overlap"], c["disagreements"], c["internalSectionConflicts"]))
        else:
            log("  %-12s 不可用（缺文件）" % label)
    log("  库内怪物名有术语条目 : %d/%d（缺 %d）" % (covered_monsters, len(monster_names),
                                                    len(missing_monsters)))
    log("  库内物品名有术语条目 : %d/%d（缺 %d）" % (covered_items, len(item_names),
                                                    len(missing_items)))
    log("  库内 NPC 同名多实体 : %d 组" % len(npc_dups))
    for k, v in sorted(npc_dups.items(), key=lambda x: -len(x[1]))[:8]:
        log("      %-10s ×%d  %s" % (k, len(v), v))
    log("  库内任务名仍为英文 : %d/%d" % (sum(1 for r in quests.values()
                                             if not CJK.search(r.get("QuestName") or "")),
                                         len(quests)))
    log("  库内 NPC 名为中文 : %d/%d" % (sum(1 for r in npc_info
                                             if CJK.search(r.get("NPCName") or "")), len(npc_info)))

    # =====================================================================
    # 6. 风险排序
    # =====================================================================
    risks = []

    def risk(sev, cat, title, count, evidence, fix):
        risks.append({"severity": sev, "category": cat, "title": title,
                      "count": count, "evidence": evidence, "fix": fix})

    kill_broken = [r for r in quest_rows
                   if any(i.startswith("kill_task_without_monster") for i in r["issues"])]
    # 对每条无怪明细的击杀任务，尝试从「网站 goals 点名的怪名」里找出**库内仍存在**的候选，
    # 并排除该任务所属任务里已经被其它 task 用掉的怪。
    fixable = []
    for r in kill_broken:
        used = {m for t in r["tasks"] for m in (t["monsters"] or [])}
        mentioned = [n for n in monster_names.values()
                     if re.search(r"(?<![A-Za-z])" + re.escape(n) + r"(?![A-Za-z])",
                                  r["webGoals"] or "")]
        for t in r["tasks"]:
            if t["monsters"]:
                continue
            cands = [n for n in mentioned if n not in used]
            fixable.append({"quest": r["index"], "questName": r["name"], "task": t["index"],
                            "amount": t["amount"],
                            "webGoals": r["webGoals"],
                            "candidatesExistingMonsters": sorted(set(cands)),
                            "actionable": bool(cands)})
    risk("P0", "quests", "KillMonster 任务缺 MonsterDetails → 永远无法推进（%d 任务 / %d 条）"
         % (len(kill_broken), len(kill_task_rows)),
         len(kill_broken), [r["index"] for r in kill_broken],
         "QuestTaskMonsterDetails 补行；网站点名且库内仍有该怪的可直接补（actionable=true）")
    risk("P0", "quests", "网站标注经验奖励、库内 QuestReward 无对应行 → 完成零回报",
         sum(1 for r in quest_rows if any(i.startswith("web_exp_") for i in r["issues"])),
         [r["index"] for r in quest_rows if any(i.startswith("web_exp_") for i in r["issues"])],
         "QuestReward 只支持给物品（无经验字段），需先扩模型或在 QuestInfo 上加经验字段")
    risk("P0", "quests", "QuestReward 行缺 Item 关联（Amount 无法发放）",
         sum(1 for r in db["QuestReward"].rows if not isinstance(r.get("Item"), dict)),
         [r["Index"] for r in db["QuestReward"].rows if not isinstance(r.get("Item"), dict)],
         "QuestReward.Item 置空会导致发放跳过；核对 143/145/148")
    risk("P1", "quests", "任务完全无目标（no_task），交付条件空",
         issue_count["no_task"],
         [r["index"] for r in quest_rows if "no_task" in r["issues"]],
         "QuestTask 补行或从 NPC StartQuests 摘除")
    risk("P1", "npc", "入口页无可交互内容（点了只剩关闭）",
         len([s for s in service if "dead_dialog" in s["flags"]]),
         [s["index"] for s in service if "dead_dialog" in s["flags"]],
         "补 NPCButton/NPCAction 或修 EntryPage")
    risk("P1", "npc", "入口 Say 为空（打开即空白对话框）",
         len([s for s in service if "blank_entry_say" in s["flags"]]),
         [s["index"] for s in service if "blank_entry_say" in s["flags"]],
         "补 Say 文本")
    risk("P1", "npc", "EntryPage 指向原版 fallback 占位页（脚本层无真实对话）",
         len([s for s in service if "fallback_entry" in s["flags"]]),
         [s["index"] for s in service if "fallback_entry" in s["flags"]],
         "接真实脚本页或从地图撤下")
    risk("P2", "npc", "网站在用 NPC 在库内彻底缺失（非地图裁剪、功能也未覆盖）",
         kind_count["suspicious_missing"],
         [e for e in web_only if e["kind"] == "suspicious_missing"],
         "建 NPCInfo + Region 登记")
    risk("P2", "companion", "宠物表与网站零分歧，但有 %d 条数据格式瑕疵（含 1 处名称首尾空格）"
         % sum(1 for c in comp_rows if c["notes"]),
         sum(1 for c in comp_rows if c["diffs"] or c["notes"]),
         [{"index": c["index"], "diffs": c["diffs"], "notes": c["notes"]}
          for c in comp_rows if c["diffs"] or c["notes"]],
         "对齐 CompanionInfo 字段；_Identity 去空格")
    risk("P2", "localization", "库内 34 条经典任务名/对白仍为英文",
         sum(1 for r in quests.values() if not CJK.search(r.get("QuestName") or "")),
         [], "补 QuestInfo 文本或客户端翻译层")
    risk("P3", "localization", "术语表与库内/客户端词表命名分歧",
         sum(c.get("disagreements", 0) for c in cross.values() if c.get("available")),
         {k: v.get("disagreements") for k, v in cross.items() if v.get("available")},
         "以网站 master.json 词表为唯一权威，回灌 terminology")

    log("")
    log("=" * 72)
    log("[6] 风险排序")
    log("=" * 72)
    for r in sorted(risks, key=lambda x: x["severity"]):
        log("  [%s] %-12s %-52s n=%s" % (r["severity"], r["category"], r["title"], r["count"]))

    # =====================================================================
    # 输出
    # =====================================================================
    result = {
        "meta": {
            "generatedAt": datetime.now(timezone.utc).isoformat(),
            "script": os.path.relpath(os.path.abspath(__file__), ZIRCON_ROOT),
            "readOnly": True,
            "inputs": {},
        },
        "npc": {
            "counts": {
                "web": len(web_npcs),
                "db": len(npc_info),
                "dbInUse": len(in_use),
                "dbReservedTemplate": len(reserved),
                "md5LikeMatch": len(set(web_npcs) & set(npc_idx)),
                "manifest": len(manifest_idx),
                "manifestEqualsInUse": manifest_idx == in_use_idx,
            },
            "webOnly": web_only,
            "webOnlyByKind": dict(kind_count),
            "dbOnlyInUse": db_only_in_use,
            "dbOnlyByKind": dict(db_only_kind),
            "dbReservedNames": [r["NPCName"] for r in reserved],
            "questAttachMismatch": attach_mismatch,
            "namingCompare": {
                "joined": len(naming),
                "sameRole": role_same,
                "sameZhName": zh_same,
                "detail": naming,
            },
        },
        "npcService": {
            "service": service,
            "broken": broken,
            "flagCounts": dict(flag_count),
            "dangling": {k: v for k, v in dangling.items()},
            "live": live,
            "successPageLoops": {
                "pureEmptyLoopComponents": [list(l) for l in loops],
                "entryHit": loop_entries,
                "buttonHit": loop_buttons,
                "ringButStopsAtTextNPCs": ring_safe,
            },
        },
        "quests": {
            "counts": {
                "web": len(web_quests), "db": len(quests),
                "webOnly": web_only_quests, "dbOnly": db_only_quests,
                "noTask": [r["index"] for r in quest_rows if "no_task" in r["issues"]],
                "noReward": [r["index"] for r in quest_rows if "no_reward" in r["issues"]],
                "killTaskWithoutMonster": [
                    {"task": t["Index"], "quest": db["QuestTask"].ref(t, "Quest"),
                     "questName": quests.get(db["QuestTask"].ref(t, "Quest"), {}).get("QuestName"),
                     "amount": t.get("Amount")}
                    for t in kill_task_rows],
                "killTaskWithoutMonsterFixable": fixable,
            },
            "quests": quest_rows,
            "orphans": {
                "tasks": [t["Index"] for t in orphan_tasks],
                "requirements": [r["Index"] for r in orphan_reqs],
                "rewards": [r["Index"] for r in orphan_rewards],
                "monsterDetails": [d["Index"] for d in orphan_details],
            },
            "issueCounts": dict(issue_count),
            "fixableKillTasks": fixable,
        },
        "companion": {
            "counts": {"web": len(web_companions), "db": len(comps),
                       "levels": levels, "levelsContiguous": levels == list(range(1, (max(levels) if levels else 0) + 1)),
                       "skillRows": len(db["CompanionSkillInfo"]),
                       "skillLevels": skill_levels},
            "companions": comp_rows,
        },
        "terminology": {
            "counts": {
                "entries": len(terminology),
                "duplicateZh": len(term_dup_zh),
                "enEqualsZh": len(term_en_eq_zh),
                "emptyZh": len(term_empty),
                "latinInZh": len(term_latin),
                "monsterCovered": covered_monsters,
                "monsterMissing": len(missing_monsters),
                "itemCovered": covered_items,
                "itemMissing": len(missing_items),
                "npcNameDuplicates": len(npc_dups),
                "questNamesStillEnglish": sum(1 for r in quests.values()
                                              if not CJK.search(r.get("QuestName") or "")),
            },
            "duplicateZh": {k: v for k, v in term_dup_zh.items()},
            "latinInZh": term_latin,
            "crossSource": {k: {kk: vv for kk, vv in v.items()
                                if kk not in ("detail", "internalDetail")} |
                            ({"detailTop": v["detail"][:60]} if v.get("detail") else {})
                            for k, v in cross.items()},
            "crossSourceDetail": {k: v.get("detail", []) for k, v in cross.items()},
            "crossSourceInternalDetail": {k: v.get("internalDetail", {}) for k, v in cross.items()},
            "monstersMissingTerminology": missing_monsters,
            "itemsMissingTerminology": missing_items,
            "dbNpcDuplicateNames": npc_dups,
        },
        "risks": risks,
    }

    for key, path in (("web", args.web), ("manifest", args.manifest),
                      ("glossary", args.glossary), ("dbnames", args.dbnames),
                      ("aliases", args.aliases)):
        if os.path.exists(path):
            result["meta"]["inputs"][key] = {
                "path": path, "bytes": os.path.getsize(path), "sha256": sha256(path)}
        else:
            result["meta"]["inputs"][key] = {"path": path, "missing": True}
    result["meta"]["inputs"]["dbdir"] = {
        "path": args.dbdir,
        "tables": {t: {"rows": len(db[t]), "declared": db[t].declared, "missing": db[t].missing}
                   for t in db},
    }
    if os.path.isdir(args.sweep):
        result["meta"]["inputs"]["sweep"] = {"path": args.sweep,
                                             "results": live.get("done", 0)}

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(result, fh, ensure_ascii=False, indent=1)
    log("")
    log("结果 JSON → %s（%.1f KB）" % (args.out, os.path.getsize(args.out) / 1024))
    return 0


if __name__ == "__main__":
    sys.exit(main())

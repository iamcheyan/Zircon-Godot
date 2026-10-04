#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
NPC 对齐矩阵生成 (Step 2)

三方数据源:
  1) 原版权威坐标: Mud3 Envir/Merchant.txt (CP936)  -- 318 条活跃记录
  2) 身份映射:      NpcMover/audit-report.md        -- Zircon NPC Index <-> Mud3 ID
  3) 现役数据库:    System.db 导出 (SystemDbProbe --json)
  4) 地图几何:      mir2ei/Map/*.map                -- 边界 + 可走性(复刻 MapReader 公式)

输出: alignment_matrix.json / .csv
Action 取值:
  Retain   已在原版权威点位, 不动
  Migrate  需迁移到原版权威点位
  Disable  新版多余 NPC -> 软停用 (Region=null), 严禁物理 DELETE
  Pending  证据不足, 本期不动 (进待引入/待复核清单)
"""
import csv
import json
import os
import re
import struct
import sys

MUD3_MERCHANT = "/home/tetsuya/development/Mir3-Research/local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/Merchant.txt"
MUD3_MAPINFO = "/home/tetsuya/development/Mir3-Research/local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/Mapinfo.txt"
AUDIT_REPORT = "/home/tetsuya/development/Mir3-Research/Tools/NpcMover/audit-report.md"
DB_EXPORT = os.environ.get("NPC_ALIGN_DB_EXPORT", "/tmp/dbviewer_live")
MAP_DIR = "/home/tetsuya/mir2ei/Map"
OUT_DIR = "/home/tetsuya/development/zircon/docs/npc_alignment"


def read_cp936(path):
    with open(path, "rb") as f:
        return f.read().decode("cp936", errors="replace")

# ---------- 1. 原版权威 NPC 坐标 ----------
def load_mud3_merchants():
    raw = read_cp936(MUD3_MERCHANT)
    out = {}
    section = None
    for line in raw.splitlines():
        s = line.strip()
        if not s:
            continue
        if s.startswith(";"):
            m = re.match(r"^;\[(.+)\]", s)
            if m:
                section = m.group(1)
            continue
        # 格式: <Mud3ID> <Map> <X> <Y> <中文名> <Face> <Body>
        f = s.split()
        if len(f) < 5:
            continue
        mid, mp, x, y, zh = f[0], f[1], f[2], f[3], f[4]
        if not re.fullmatch(r"-?\d+", x) or not re.fullmatch(r"-?\d+", y):
            continue
        out[mid] = {
            "mud3_id": mid,
            "map": mp,
            "x": int(x),
            "y": int(y),
            "name_zh": zh,
            "section": section or "",
        }
    return out


# ---------- 2. Mud3 地图代号 -> 中文名 ----------
def load_mud3_mapnames():
    raw = read_cp936(MUD3_MAPINFO)
    names = {}
    for line in raw.splitlines():
        s = line.strip()
        m = re.match(r"^\[(\S+)\s+(\S+)", s)
        if m:
            names[m.group(1)] = m.group(2)
    return names


# ---------- 3. 身份映射 (Zircon Index -> Mud3 ID) ----------
def load_audit_map():
    """解析 audit-report.md 表格。
    | Index | NPCName | 方式 | 旧位置 | 新位置 | 说明 | 来源 | 备注 |
    说明列对 C-语义 含 "Mud3:02Weapon_Bichon1" 的权威 ID。
    """
    rows = {}
    with open(AUDIT_REPORT, encoding="utf-8") as f:
        for line in f:
            if not line.startswith("|"):
                continue
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            if len(cells) < 6:
                continue
            try:
                idx = int(cells[0])
            except ValueError:
                continue
            name, method, old, new, note = cells[1], cells[2], cells[3], cells[4], cells[5]
            # 权威 Mud3 ID 严格取整格 "Mud3:<ID>" (col6)。col5 说明列里的括号引用
            # 可能是指向别的 NPC 的旁证 (如 #89 写 "...13Move_HalfTemple旁" 但它自身
            # 是 D-推算 点位)，用正则扫 col5 会把 NPC 迁到别人的坐标上。
            mid = cells[6] if len(cells) > 6 and re.fullmatch(r"Mud3:[A-Za-z0-9_]+", cells[6]) else None
            rows[idx] = {
                "npc_index": idx,
                "npc_name": name,
                "method": method,
                "old_pos": old,
                "new_pos": new,
                "mud3_id": mid[5:] if mid else None,
            }
    return rows


# ---------- 4. 现役数据库 ----------
def load_db():
    def load(name):
        with open(os.path.join(DB_EXPORT, name + ".json"), encoding="utf-8") as f:
            return json.load(f)["rows"]

    maps = {r["Index"]: r for r in load("MapInfo")}
    regions = {r["Index"]: r for r in load("MapRegion")}
    npcs = load("NPCInfo")

    for n in npcs:
        rg = n.get("Region")
        r = regions.get(rg["Index"]) if isinstance(rg, dict) else None
        if r is None:
            n["_map"] = n["_x"] = n["_y"] = None
            n["_region_identity"] = None
            continue
        mi = r["Map"]["Index"]
        pr = r.get("PointRegion") or {}
        n["_map"] = maps[mi]["FileName"] if mi in maps else None
        n["_map_index"] = mi
        n["_map_desc"] = maps[mi]["Description"] if mi in maps else None
        n["_x"] = pr.get("CenterX")
        n["_y"] = pr.get("CenterY")
        n["_region_identity"] = r.get("_Identity")
    return maps, npcs


# ---------- 5. 地图几何: 边界 + 可走性 ----------
_MAP_CACHE = {}


def load_map_geometry(name):
    if name in _MAP_CACHE:
        return _MAP_CACHE[name]
    path = os.path.join(MAP_DIR, name + ".map")
    if not os.path.exists(path):
        _MAP_CACHE[name] = None
        return None
    with open(path, "rb") as f:
        head = f.read(26)
    w, h = struct.unpack("<hh", head[22:26])
    with open(path, "rb") as f:
        f.seek(26 + 2 + (w // 2) * (h // 2) * 3)
        data = f.read(w * h * 14)
    _MAP_CACHE[name] = (w, h, data)
    return _MAP_CACHE[name]


def cell_blocked(mapname, x, y):
    """复刻 GodotClient/Formats/MapReader.cs 的 Flag 语义。
    Flag = ((flag & 0x01) != 1) || ((flag & 0x02) != 2)，True = 阻挡。
    """
    g = load_map_geometry(mapname)
    if g is None:
        return None
    w, h, data = g
    if not (0 <= x < w and 0 <= y < h):
        return "out-of-bounds"
    flag = data[(y * w + x) * 14]
    return ((flag & 0x01) != 1) or ((flag & 0x02) != 2)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    merchants = load_mud3_merchants()
    mapnames = load_mud3_mapnames()
    audit = load_audit_map()
    maps, npcs = load_db()

    print("=" * 78)
    print("数据源统计")
    print("=" * 78)
    print(f"  Mud3 Merchant.txt 权威 NPC : {len(merchants)}")
    print(f"  audit-report 身份映射条目  : {len(audit)}")
    print(f"  System.db 现役 NPC        : {len(npcs)}")

    rows = []
    counts = {}
    for n in npcs:
        idx = n["Index"]
        a = audit.get(idx)
        cur_map, cur_x, cur_y = n["_map"], n["_x"], n["_y"]

        tgt = None
        if a and a["mud3_id"]:
            tgt = merchants.get(a["mud3_id"])

        rec = {
            "npc_index": idx,
            "npc_name": n["NPCName"],
            "category": n.get("Category"),
            "region_identity": n["_region_identity"],
            "cur_map": cur_map,
            "cur_map_name_zh": mapnames.get(cur_map),
            "cur_x": cur_x,
            "cur_y": cur_y,
            "mud3_id": a["mud3_id"] if a else None,
            "audit_method": a["method"] if a else None,
            "target_map": tgt["map"] if tgt else None,
            "target_x": tgt["x"] if tgt else None,
            "target_y": tgt["y"] if tgt else None,
            "target_name_zh": tgt["name_zh"] if tgt else None,
            "mud3_section": tgt["section"] if tgt else None,
        }

        if tgt:
            # 交集类: 有 Mud3 权威身份 -> 比对现役点位与原版权威点位
            same = (cur_map == tgt["map"] and cur_x == tgt["x"] and cur_y == tgt["y"])
            if same:
                rec["action"] = "Retain"
                rec["reason"] = "已位于原版权威点位"
            else:
                # 几何校验: 边界 + 柜台位 (复刻 MapReader 的 Flag 语义)
                inb = cell_blocked(tgt["map"], tgt["x"], tgt["y"])
                rec["target_blocked"] = inb
                if inb == "out-of-bounds":
                    rec["action"] = "Pending"
                    rec["reason"] = "目标坐标越界"
                else:
                    rec["action"] = "Migrate"
                    rec["reason"] = (
                        "原版柜台内(阻挡格, 按原版保留)" if inb
                        else "原版可行走点位"
                    )
        else:
            # 无 Mud3 权威身份映射时, 用 audit-report 的归类判定来历:
            #   A-精确 / B-英雄杀(已在位)  -> 原版既有点位且已核验, 属经典 NPC, 保留
            #   其余 (D-推算/E-避让/S-沙巴克) -> 证据不足, 本期不动, 进待复核清单
            method = a["method"] if a else None
            if method and method.startswith(("A-", "B-")):
                rec["action"] = "Retain"
                rec["reason"] = f"原版既有点位且已核验 ({method})"
            else:
                rec["action"] = "Pending"
                rec["reason"] = "无 Mud3 权威身份映射, 证据不足待复核"
        counts[rec["action"]] = counts.get(rec["action"], 0) + 1
        rows.append(rec)

    # 差集A: 新版多余 NPC —— 经典版(2012 Mud3 Merchant.txt)不存在的私服/现代功能 NPC。
    # 判定只用两条硬证据, 不用 audit-report 的归类字母 (B-英雄杀 既包含经典 NPC,
    # 也包含私服功能 NPC, 不足以区分):
    #   1) 在 Merchant.txt 权威表里查无此 Mud3 身份 (mud3_id 为空);
    #   2) NPC 名称命中现代功能词表。
    # 两条同时成立才判为多余。绝不物理 DELETE, 仅 Region=null 软停用。
    MODERN_TOKENS = (
        "内测", "泡点", "VIP", "名望", "功能NPC", "仓库系统", "买卖商人",
        "天下第一", "道观公告", "合成大师", "活动管理", "沙城管家", "化废为宝",
        "经验化废", "结婚司仪", "买马商人", "美发专家", "比奇官吏", "行会旗帜",
        "比赛评判", "My00DefaultNpc", "道观传送", "传送", "进进出出", "接待",
        "潘业传送", "神舰接待", "异界接待", "绿洲沙漠", "升级加点", "钻石抽奖",
        "天降宝箱", "钓鱼", "邪恶之地", "邪恶接待", "黑暗巢穴", "青青草原",
        "幽暗森林", "钓鱼岛", "血案", "骑兵", "尊者", "杀人掠夺", "龙穴",
    )
    for r in rows:
        name = (r["npc_name"] or "").strip()
        modern = any(tok in name for tok in MODERN_TOKENS)
        r["disable_candidate"] = bool(modern and not r["mud3_id"])
        if r["disable_candidate"]:
            prev = counts.get(r["action"], 0)
            counts[r["action"]] = prev - 1
            r["action"] = "Disable"
            r["reason"] = "经典版无此 NPC (Merchant.txt 查无 + 现代功能名), 软停用 Region=null"
            counts["Disable"] = counts.get("Disable", 0) + 1

    # 差集B: 原版 Merchant.txt 有, 现役库缺失 -> 待引入清单
    have_ids = {r["mud3_id"] for r in rows if r["mud3_id"]}
    missing = [
        {"mud3_id": m["mud3_id"], "map": m["map"], "x": m["x"], "y": m["y"],
         "name_zh": m["name_zh"], "section": m["section"]}
        for m in merchants.values() if m["mud3_id"] not in have_ids
    ]

    print()
    print("=" * 78)
    print("对齐矩阵动作分布")
    print("=" * 78)
    for k in sorted(counts):
        print(f"  {k:9} {counts[k]}")

    with open(os.path.join(OUT_DIR, "alignment_matrix.json"), "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=2)

    cols = ["npc_index", "npc_name", "category", "cur_map", "cur_x", "cur_y",
            "target_map", "target_x", "target_y", "target_name_zh",
            "mud3_id", "target_blocked", "action", "reason"]
    with open(os.path.join(OUT_DIR, "alignment_matrix.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore")
        w.writeheader()
        for r in rows:
            w.writerow(r)

    with open(os.path.join(OUT_DIR, "missing_npcs_pending_intro.json"), "w", encoding="utf-8") as f:
        json.dump(missing, f, ensure_ascii=False, indent=2)
    with open(os.path.join(OUT_DIR, "missing_npcs_pending_intro.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["mud3_id", "map", "x", "y", "name_zh", "section"])
        w.writeheader()
        for m in missing:
            w.writerow(m)

    print()
    print("=" * 78)
    print("Migrate 明细 (需迁移到原版权威点位)")
    print("=" * 78)
    for r in rows:
        if r["action"] == "Migrate":
            b = "柜台内(阻挡)" if r.get("target_blocked") else "可行走"
            print(f"  [{r['npc_index']:3}] {r['npc_name'][:14]:16} "
                  f"{r['cur_map']}({r['cur_x']},{r['cur_y']}) -> "
                  f"{r['target_map']}({r['target_x']},{r['target_y']}) "
                  f"[{b}] {r['mud3_id']} {r['target_name_zh']}")

    print()
    print("=" * 78)
    print("Disable 明细 (新版多余 NPC -> 软停用 Region=null, 严禁物理 DELETE)")
    print("=" * 78)
    dis = [r for r in rows if r["action"] == "Disable"]
    for r in dis:
        print(f"  [{r['npc_index']:3}] {r['npc_name'][:20]:22} "
              f"现位置 {r['cur_map']}({r['cur_x']},{r['cur_y']})")
    print(f"  合计 {len(dis)}")

    print()
    print("=" * 78)
    print("差集B 原版有/新版缺失 待引入清单 (前 25 条)")
    print("=" * 78)
    for m in missing[:25]:
        print(f"  {m['map']:6}({m['x']:3},{m['y']:3})  {m['mud3_id']:26} {m['name_zh']}")
    print(f"  合计 {len(missing)}")

    print()
    print(f"输出: {OUT_DIR}/alignment_matrix.json")
    print(f"      {OUT_DIR}/alignment_matrix.csv")
    print(f"      {OUT_DIR}/missing_npcs_pending_intro.json")
    print(f"      {OUT_DIR}/missing_npcs_pending_intro.csv")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成网站百科 NPC 数据源 (Step 5)

数据源: 本次对齐后写库完成的 System.db 实时快照 (/tmp/dbv_v2)
        + Mud3 Merchant.txt 原版权威中文名
输出:   /home/tetsuya/development/mir3-website/data/npcs.json

原则: 与游戏内实际点位 100% 一一对应。
      只导出 Region != null (游戏内真实生成的) NPC —— 软停用的 76 个不进百科。
"""
import json
import os
import re
import sys

DB_EXPORT = os.environ.get("NPC_ALIGN_DB_EXPORT", "/tmp/dbv_v2")
MERCHANT = ("/home/tetsuya/development/Mir3-Research/local-reference-data/"
            "yxs-mud3-2026-09-25/mud3/Envir/Merchant.txt")
MAPINFO = ("/home/tetsuya/development/Mir3-Research/local-reference-data/"
           "yxs-mud3-2026-09-25/mud3/Envir/Mapinfo.txt")
MATRIX = "/home/tetsuya/development/zircon/docs/npc_alignment/alignment_matrix.json"
OUT = "/home/tetsuya/development/mir3-website/data/npcs.json"


def read_cp936(path):
    with open(path, "rb") as f:
        return f.read().decode("cp936", errors="replace")


def load_merchants():
    """Mud3 ID -> {map,x,y,name_zh}"""
    out = {}
    for line in read_cp936(MERCHANT).splitlines():
        s = line.strip()
        if not s or s.startswith(";"):
            continue
        f = s.split()
        if len(f) < 5:
            continue
        if not re.fullmatch(r"-?\d+", f[2]) or not re.fullmatch(r"-?\d+", f[3]):
            continue
        out[f[0]] = {"map": f[1], "x": int(f[2]), "y": int(f[3]), "name_zh": f[4]}
    return out


def load_mapnames():
    out = {}
    for line in read_cp936(MAPINFO).splitlines():
        m = re.match(r"^\[(\S+)\s+(\S+)", line.strip())
        if m:
            out[m.group(1)] = m.group(2)
    return out


# Mud3 ID 前缀 -> 职能分类
CATEGORY_BY_PREFIX = [
    ("01Meet", "Butcher", "屠夫 / 肉店"),
    ("02Weapon", "Weapon", "武器店"),
    ("03Armor", "Armor", "防具店"),
    ("03Shoes", "Shoes", "鞋店"),
    ("04PotionMake", "PotionMaker", "药剂师"),
    ("04Potion", "Potion", "药店"),
    ("05Book", "Book", "书店"),
    ("06Inn", "Inn", "客栈"),
    ("07Grocery", "Grocery", "杂货商"),
    ("08Accessory", "Accessory", "首饰店"),
    ("09", "Misc", "特殊服务"),
    ("10Material", "Material", "材料商"),
    ("10ChestnutMarket", "Collector", "收购商"),
    ("13Move", "Teleport", "传送"),
    ("14Doctor", "Doctor", "万事通"),
    ("14Quest", "Quest", "任务"),
    ("15Magic", "Trainer", "技能导师"),
    ("17", "Event", "活动"),
    ("20Gmatch", "Event", "活动"),
]


def classify(mud3_id, npc_name):
    if not mud3_id:
        return "Other", ""
    for prefix, cat, label in CATEGORY_BY_PREFIX:
        if mud3_id.startswith(prefix):
            return cat, label
    return "Other", ""


def main():
    merchants = load_merchants()
    mapnames = load_mapnames()

    def load(name):
        with open(os.path.join(DB_EXPORT, name + ".json"), encoding="utf-8") as f:
            return json.load(f)["rows"]

    maps = {r["Index"]: r for r in load("MapInfo")}
    regions = {r["Index"]: r for r in load("MapRegion")}
    npcs = load("NPCInfo")

    # 对齐矩阵提供 Index -> Mud3 ID / 原版中文名 的权威映射
    mud3_by_index = {}
    with open(MATRIX, encoding="utf-8") as f:
        for row in json.load(f):
            if row.get("mud3_id"):
                mud3_by_index[row["npc_index"]] = row

    # 无 Mud3 身份但属于经典职能的 NPC: 英文名 -> 中文名/职能
    # (取自 Zircon 现役 NPCInfo 的英文命名与原版职能对应)
    EN2ZH = {
        "Warrior Trainer": ("战士师父", "战士技能导师"),
        "Wizard Teacher": ("魔法师", "魔法师导师"),
        "Taoist Mentor": ("道士", "道士导师"),
        "Village Elder": ("村长", "村长"),
        "Companion Manager": ("宠物管理员", "宠物管理"),
        "Notice Board": ("公告板", "公告板"),
        "Hexa Holy Stone": ("六面神石", "传送神石"),
        "Administrator": ("管理员", "行会/攻城管理"),
        "Dock Manager": ("码头管理员", "码头管理"),
        "Cory": ("科里", "仓库管理员"),
        "Healer": ("治愈师", "治疗"),
    }

    out = []
    skipped_disabled = 0
    for n in npcs:
        rg = n.get("Region")
        if not isinstance(rg, dict):
            skipped_disabled += 1          # Region=null = 软停用, 游戏内不生成
            continue
        rr = regions.get(rg["Index"])
        if rr is None:
            continue
        mi = rr["Map"]["Index"]
        m = maps.get(mi)
        if m is None:
            continue
        pr = rr.get("PointRegion") or {}
        x, y = pr.get("CenterX"), pr.get("CenterY")
        if x is None or y is None:
            continue

        map_code = m["FileName"]
        row = mud3_by_index.get(n["Index"])
        mud3_id = row["mud3_id"] if row else None
        # 中文名优先级:
        #   1) 对齐矩阵记录的 Mud3 原版名 (最权威, 直接来自 Merchant.txt)
        #   2) 英文名 -> 经典职能中文名对照表
        #   3) MapRegion 描述里已中文化的职能串
        #   4) 英文原名兜底
        desc = (rr.get("Description") or "").strip()
        desc = re.sub(r"^\S+\s*/\s*", "", desc)   # 去掉 "0 / " 地图前缀
        desc = re.sub(r"^Lab_\d+_", "", desc)      # 去掉 "Lab_13_" 编号前缀

        name_zh, role_zh = None, None
        if row and row.get("target_name_zh"):
            name_zh = row["target_name_zh"]
        elif mud3_id and mud3_id in merchants:
            name_zh = merchants[mud3_id]["name_zh"]
        elif n["NPCName"] in EN2ZH:
            name_zh, role_zh = EN2ZH[n["NPCName"]]
        if not name_zh:
            name_zh = desc or n["NPCName"]
        # 描述是纯英文时不要当中文名用
        if re.fullmatch(r"[A-Za-z0-9_ ]+", name_zh or ""):
            name_zh = role_zh or n["NPCName"]

        category, services = classify(mud3_id, n["NPCName"])
        if not services and role_zh:
            services = [role_zh]
        elif not services and desc and not re.fullmatch(r"[A-Za-z0-9_ ]+", desc):
            services = [desc]

        out.append({
            "id": f"npc-{n['Index']}",
            "npc_index": n["Index"],
            "name_zh": name_zh,
            "name_en": n["NPCName"],
            "map_code": map_code,
            "map_name_zh": mapnames.get(map_code) or m.get("Description") or "",
            "x": x,
            "y": y,
            "category": category,
            "services": [services] if services else [],
            "image": n.get("Image"),
            "face_image": n.get("FaceImage"),
            "mud3_id": mud3_id,
            "description": desc,
        })

    out.sort(key=lambda r: (r["map_code"], r["y"], r["x"]))

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(f"导出 {len(out)} 个在役 NPC -> {OUT}")
    print(f"软停用(Region=null)已排除: {skipped_disabled}")

    import collections
    bymap = collections.Counter(r["map_code"] for r in out)
    print("按地图分布 (top 12):")
    for k, v in bymap.most_common(12):
        print(f"  map {k:8} {v:3}  {mapnames.get(k, '')}")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 NPC 全景清单 (docs/NPC/ROSTER.md)

数据源: 现役 System.db 实时导出 (SystemDbProbe --json)
        + Mud3 Merchant.txt 原版中文名/权威 ID
输出:   docs/NPC/ROSTER.md   —— 218 在役 + 76 软停用, 按地图分组

用法:
    cd /home/tetsuya/development/Mir3-Research
    DB_SRC=/home/tetsuya/development/zircon/System.db bash Tools/dbviewer/export.sh /tmp/dbv_roster
    python3 /home/tetsuya/development/zircon/docs/NPC/build_roster.py
"""
import collections
import csv
import json
import os
import re
import struct

DB_EXPORT = os.environ.get("NPC_ROSTER_DB_EXPORT", "/tmp/dbv_roster")
MERCHANT = ("/home/tetsuya/development/Mir3-Research/local-reference-data/"
            "yxs-mud3-2026-09-25/mud3/Envir/Merchant.txt")
MATRIX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "alignment_matrix.json")
MAPINFO = ("/home/tetsuya/development/Mir3-Research/local-reference-data/"
           "yxs-mud3-2026-09-25/mud3/Envir/Mapinfo.txt")
MAP_DIR = "/home/tetsuya/mir2ei/Map"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ROSTER.md")
OUT_CSV = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ROSTER.csv")

MAP_CN_FALLBACK = {
    "0": "比奇县", "01": "边境城市", "02": "银杏山谷", "1": "道馆",
    "2": "毒蛇山谷", "3": "沙巴克城", "4": "绿洲", "5": "沙漠土城",
    "41": "诺玛村庄", "74": "盟重县", "12": "灌木林", "8": "潘夜岛",
    "81": "流放岛", "9": "失乐园",
}


def read_cp936(path):
    with open(path, "rb") as f:
        return f.read().decode("cp936", errors="replace")


def load_merchants():
    """Mud3 ID -> 原版中文名"""
    out = {}
    for line in read_cp936(MERCHANT).splitlines():
        s = line.strip()
        if not s or s.startswith(";"):
            continue
        f = s.split()
        if len(f) < 5 or not re.fullmatch(r"-?\d+", f[2]):
            continue
        out[f[0]] = f[4]
    return out


def load_mud3_mapnames():
    out = {}
    for line in read_cp936(MAPINFO).splitlines():
        m = re.match(r"^\[(\S+)\s+(\S+)", line.strip())
        if m:
            out[m.group(1)] = m.group(2)
    return out


def map_dims(code):
    p = os.path.join(MAP_DIR, code + ".map")
    if not os.path.exists(p):
        return None
    with open(p, "rb") as f:
        h = f.read(26)
    return struct.unpack("<hh", h[22:26])


def blocked(code, x, y):
    """复刻 GodotClient/Formats/MapReader.cs:67 的 Flag 语义; True=阻挡。"""
    d = map_dims(code)
    if d is None or x is None or y is None:
        return None
    w, h = d
    if not (0 <= x < w and 0 <= y < h):
        return "oob"
    with open(os.path.join(MAP_DIR, code + ".map"), "rb") as f:
        f.seek(26 + 2 + (w // 2) * (h // 2) * 3)
        data = f.read(w * h * 14)
    flag = data[(y * w + x) * 14]
    return ((flag & 0x01) != 1) or ((flag & 0x02) != 2)


# Mud3 ID 前缀 -> (分类, 职能说明)
# 顺序即优先级：更长/更具体的前缀排在前面，避免 09Repair 抢走 09Repair_Euhang。
CATEGORY = [
    ("09Reinstatement", "复职服务", "职业复职/转职"),
    ("09Repair_Euhang", "修理工", "装备修理"),
    ("09Repair", "修理工", "装备修理"),
    ("09CancleWeapon", "修理工", "武器修理"),
    ("09WideUse", "万能服务", "多功能服务"),
    ("09NotBlocker", "路引", "路引发放"),
    ("09NightMarket", "夜市", "夜市交易"),
    ("09ChangeMoney", "兑换商", "货币兑换"),
    ("09HairDying", "美容师", "美发染发"),
    ("09HorseMarket", "马市", "马匹交易"),
    ("09Bonus", "属性加成", "属性加成"),
    ("09Lucky", "幸运服务", "幸运服务"),
    ("09Tavern", "酒馆", "酒馆"),
    ("08Astrologist", "占卜屋", "占卜"),
    ("08Accessory", "首饰店", "首饰买卖、修理"),
    ("07Grocery", "杂货商", "杂货买卖"),
    ("06Inn", "客栈/仓库", "仓储、休息"),
    ("05Book", "书店", "书刊买卖"),
    ("04PotionMake", "药剂师", "特殊药制造"),
    ("04Potion", "药店", "常规药买卖"),
    ("03Shoes", "鞋店", "鞋类买卖"),
    ("03Armor", "防具店", "护甲买卖、修理"),
    ("02Weapon", "武器店", "武器买卖、修理"),
    ("01Meet", "屠夫/肉店", "肉类买卖、收购"),
    ("10ChestnutMarket", "收购商", "珍宝收购"),
    ("10Material", "材料商", "材料买卖"),
    # Zircon 显示名 "Hexa Holy Stone" = Mud3 13Move（六面神石），同样属传送
    ("Hexa", "传送", "传送神石"),
    ("13Move", "传送", "传送神石"),
    ("14Doctor", "万事通", "情报问答"),
    ("14Quest", "任务 NPC", "任务发布/推进"),
    ("15Magic", "技能导师", "职业技能传授"),
    ("17", "活动 NPC", "活动/事件"),
    ("20Gmatch", "活动 NPC", "活动/赛事"),
]

# 职能大类（ROSTER 汇总 + FUNCTIONS 文档用）
FUNCTION_GROUPS = [
    ("商店类", ["武器店", "防具店", "鞋店", "药店", "药剂师", "书店", "杂货商",
                "首饰店", "材料商", "收购商", "屠夫/肉店"]),
    ("服务类", ["客栈/仓库", "修理工", "万能服务", "美容师", "兑换商", "属性加成",
                "幸运服务", "夜市", "酒馆", "占卜屋", "马市", "路引", "复职服务"]),
    ("传送类", ["传送"]),
    ("任务类", ["任务 NPC", "活动 NPC", "万事通"]),
    ("导师类", "技能导师"),
    ("其他", ["原版ID命名", "未命名", "其他"]),
]


def classify(npc_name, mud3_id):
    """按 Mud3 ID 前缀判职能。

    mud3_id 来自对齐矩阵，但矩阵只覆盖有审计记录的条目；大量 NPC 是
    A-精确类（审计表只记了位置、没记 Mud3 ID），此时 mud3_id 为空。
    这些 NPC 的 NPCName 本身就是 Mud3 ID 风格（02Weapon_Kugkyung），
    所以回退用 NPCName 判前缀，仍判不出才归入"原版ID命名"。
    """
    for key in (mud3_id, npc_name):
        if not key:
            continue
        for prefix, cat, svc in CATEGORY:
            if key.startswith(prefix):
                return cat, svc
    if not npc_name:
        return "未命名", ""
    if re.fullmatch(r"[0-9A-Za-z_]+", npc_name):
        return "原版ID命名", ""
    return "其他", ""


def main():
    merchants = load_merchants()
    mapnames = load_mud3_mapnames()

    def load(name):
        with open(os.path.join(DB_EXPORT, name + ".json"), encoding="utf-8") as f:
            return json.load(f)["rows"]

    maps = {r["Index"]: r for r in load("MapInfo")}
    regions = {r["Index"]: r for r in load("MapRegion")}
    npcs = load("NPCInfo")

    with open(MATRIX, encoding="utf-8") as f:
        matrix = {r["npc_index"]: r for r in json.load(f)}

    records = []
    for n in npcs:
        rg = n.get("Region")
        rr = regions.get(rg["Index"]) if isinstance(rg, dict) else None
        row = matrix.get(n["Index"], {})
        mud3_id = row.get("mud3_id")
        name_zh = row.get("target_name_zh") or (merchants.get(mud3_id) if mud3_id else None)
        if rr is None:
            records.append({
                "idx": n["Index"], "name": n["NPCName"] or "(无名)", "active": False,
                "map": None, "map_cn": None, "x": None, "y": None,
                "category": "已软停用", "service": row.get("reason", ""),
                "mud3_id": mud3_id, "name_zh": name_zh, "blocked": None,
            })
            continue
        mi = rr["Map"]["Index"]
        m = maps[mi]
        pr = rr.get("PointRegion") or {}
        x, y = pr.get("CenterX"), pr.get("CenterY")
        code = m["FileName"]
        cat, svc = classify(n["NPCName"], mud3_id)
        records.append({
            "idx": n["Index"], "name": n["NPCName"] or "(无名)", "active": True,
            "map": code,
            "map_cn": mapnames.get(code) or m.get("Description") or MAP_CN_FALLBACK.get(code, ""),
            "x": x, "y": y, "category": cat, "service": svc,
            "mud3_id": mud3_id, "name_zh": name_zh,
            "blocked": blocked(code, x, y) if x is not None else None,
        })

    active = [r for r in records if r["active"]]
    disabled = [r for r in records if not r["active"]]

    bymap = collections.defaultdict(list)
    for r in active:
        bymap[r["map"]].append(r)
    for v in bymap.values():
        v.sort(key=lambda r: (r["y"] if r["y"] is not None else 0,
                              r["x"] if r["x"] is not None else 0))

    L = []
    A = L.append
    A("# NPC 全景清单（ROSTER）\n")
    A("> **数据源**: 现役 `System.db` 实时导出（`SystemDbProbe --json`）")
    A("> + Mud3 `Merchant.txt`（2012 原版权威坐标/中文名）")
    A("> **生成脚本**: `docs/NPC/build_roster.py`（数据永远来自实役库，勿手工编辑）")
    A("> **生成时间**: 2026-10-04\n")

    A("## 总览\n")
    A(f"| 项目 | 数量 |")
    A("|---|---|")
    A(f"| NPC 条目总数 | **{len(records)}** |")
    A(f"| 在役（`Region != null`，游戏内生成） | **{len(active)}** |")
    A(f"| 软停用（`Region = null`，游戏内不生成） | **{len(disabled)}** |")
    A(f"| 有 NPC 的地图 | **{len(bymap)}** / 全库 {len(maps)} 张 |")
    A(f"| 仅 1 个 NPC 的地图 | **{sum(1 for v in bymap.values() if len(v) == 1)}** |")
    A("")
    A("> 软停用 NPC 未出现在 `mir3-website/data/npcs.json`——游戏内不存在的不进百科，")
    A("> 以保证百科与游戏世界 100% 一一对应。\n")

    # 城镇速查
    A("## 城镇速查（≥5 个 NPC）\n")
    A("| 地图代号 | 图名 | NPC 数 | 构成 |")
    A("|---|---|---|---|")
    major = sorted(((c, v) for c, v in bymap.items() if len(v) >= 5),
                   key=lambda t: -len(t[1]))
    for code, items in major:
        cats = collections.Counter(r["category"] for r in items if r["category"])
        comp = "、".join(f"{k}×{v}" for k, v in cats.most_common(4))
        A(f"| `{code}` | {items[0]['map_cn']} | **{len(items)}** | {comp} |")
    A("")

    # 全地图统计
    A("## 全地图 NPC 数量统计\n")
    A("| 地图代号 | 图名 | NPC 数 |")
    A("|---|---|---|")
    for code, items in sorted(bymap.items(), key=lambda t: (-len(t[1]), t[0])):
        A(f"| `{code}` | {items[0]['map_cn']} | {len(items)} |")
    A(f"| **合计** | **{len(bymap)} 张图** | **{len(active)}** |")
    A("")

    # 逐图明细
    A("## 逐地图 NPC 明细\n")
    for code, items in sorted(bymap.items(), key=lambda t: (-len(t[1]), t[0])):
        d = map_dims(code)
        dim = f"{d[0]}×{d[1]}" if d else "?"
        A(f"### `{code}` — {items[0]['map_cn']}（{len(items)} 个 / 地图 {dim}）\n")
        A("| Index | 原版中文名 | NPCName | 坐标 (X, Y) | 职能 | 服务 | 柜台内 |")
        A("|---|---|---|---|---|---|---|")
        for r in items:
            zh = r["name_zh"] or "—"
            blk = "是" if r["blocked"] is True else ("越界" if r["blocked"] == "oob" else "否")
            A(f"| {r['idx']} | {zh} | `{r['name']}` | ({r['x']}, {r['y']}) | "
              f"{r['category']} | {r['service'] or '—'} | {blk} |")
        A("")

    # 软停用
    A("## 软停用 NPC 名单（76 个，游戏内不生成，可回滚）\n")
    A("判定依据：`Merchant.txt` 查无 Mud3 身份 **且** 名称命中现代功能词（两条同时成立）。")
    A("回滚：重新给 `NPCInfo` 指派 `Region` 即可，`NPCPage`/`NPCAction`/`NPCCheck` 一行未删。\n")
    A("| Index | NPCName | 原位置 | 停用原因 |")
    A("|---|---|---|---|")
    dis_by_name = collections.defaultdict(list)
    for r in sorted(disabled, key=lambda r: r["idx"]):
        dis_by_name[r["name"]].append(r)
    for r in sorted(disabled, key=lambda r: r["idx"]):
        idxs = ",".join(str(x["idx"]) for x in dis_by_name[r["name"]])
        A(f"| {idxs} | {r['name']} | — | Region=null 软停用 |")
    A("")

    with open(OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(L) + "\n")

    cols = ["idx", "name", "name_zh", "map", "map_cn", "x", "y", "category",
            "service", "mud3_id", "active"]
    with open(OUT_CSV, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore")
        w.writeheader()
        for r in sorted(records, key=lambda r: r["idx"]):
            w.writerow(r)

    print(f"在役 {len(active)} / 软停用 {len(disabled)} / 合计 {len(records)}")
    print(f"地图 {len(bymap)} 张")
    print(f"输出: {OUT}")
    print(f"      {OUT_CSV}")


if __name__ == "__main__":
    main()

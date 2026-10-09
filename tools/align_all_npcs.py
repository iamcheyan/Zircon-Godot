#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 Zircon 230 个活动 NPC 的全量权威中文名、坐标比对、职能分类与外观模型数据
"""

import json
import os
import re

MUD3_PATH = "/home/tetsuya/development/Mir3-Research/local-reference-data/yxs-mud3-2026-09-25/mud3/Envir/Merchant.txt"
ZIRCON_CURRENT_PATH = "/tmp/zircon_npcs_current.json"
WEB_NPCS_PATH = "/home/tetsuya/development/mir3-website/data/npcs.json"
OUT_ALIGNED_PATH = "/home/tetsuya/development/zircon/tools/aligned_npcs_230.json"

# 地图代号与中文名映射
MAP_NAME_ZH = {
    "0": "比奇县",
    "0_003": "比奇县牢房",
    "01": "边境城市",
    "01_001": "边境志善屋",
    "01_003": "边境试练场",
    "02": "银杏山谷",
    "02_001": "银杏试练场1",
    "02_003": "银杏试练场3",
    "02_004": "银杏试练场4",
    "1": "道馆",
    "1_001": "道馆武器仓库",
    "1_002": "道馆本馆",
    "1_003": "道馆洗衣居",
    "1_004": "道馆书房",
    "1_005": "道馆物品研究所",
    "1_006": "道馆药剂师居所",
    "1_007": "道馆仓库",
    "1_008": "道馆试练场1",
    "1_009": "无名老人隐居地",
    "1_011": "道馆试练场2",
    "1_013": "道馆试练场3",
    "2": "潘夜村",
    "3": "沙巴克城",
    "4": "努玛村",
    "4_001": "努玛武器店",
    "4_003": "努玛中药商",
    "4_004": "努玛布匹店",
    "4_005": "努玛占卜屋",
    "5": "盟重土城",
    "5_002": "盟重武器商",
    "5_003": "盟重药房",
    "5_004": "盟重服饰店",
    "5_005": "盟重饰品店",
    "5_006": "盟重仓库",
    "6": "沙漠绿洲",
    "8": "雪原村",
    "12": "潘夜岛",
    "41": "诺玛沙漠",
    "74": "盟重县",
    "75": "石阁庙",
    "81": "流放岛",
    "D001": "骷髅洞穴1层",
    "D002": "骷髅洞穴2层",
    "D012_001": "天然洞穴2层",
    "D022": "沃玛神殿1层",
    "D022_001": "沃玛神殿1层暗室",
    "D023_001": "沃玛神殿师徒关",
    "D404_002": "废矿矿山地下2层",
    "D431": "北部矿山入口",
    "D5071": "祖玛神殿7层大厅",
    "D900": "神舰入口",
    "D90221": "神舰医务室",
    "D90323": "神舰海图室",
    "D11031": "潘夜神殿3层西部",
    "D1105": "祖玛神殿5层",
    "D1110": "潘夜神殿大厅",
    "D1115": "潘夜神殿8层",
    "DM001": "兽人古墓奸商",
    "DM002": "兽人古墓王陵",
    "E002": "半兽天然洞穴",
    "E002_001": "半兽天然洞穴深处",
    "E402_001": "北部连接通路"
}

def load_mud3_merchants():
    merchants = []
    with open(MUD3_PATH, "r", encoding="gbk", errors="ignore") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith(";"):
                continue
            parts = line.split()
            if len(parts) >= 5:
                try:
                    merchants.append({
                        "file": parts[0],
                        "map": parts[1],
                        "x": int(parts[2]),
                        "y": int(parts[3]),
                        "name": parts[4],
                        "face": int(parts[5]) if len(parts) > 5 else 0,
                        "body": int(parts[6]) if len(parts) > 6 else 0
                    })
                except:
                    pass
    return merchants

def categorize_npc(name, entry_page, orig_cat):
    """推导规范的职能分类与服务描述"""
    entry_l = (entry_page or "").lower()
    name_l = name.lower()
    
    if "teleport" in entry_l or "move" in entry_l or "stone" in name_l or "六面神石" in name:
        return "传送", ["城镇/地牢传送", "区域传送"]
    elif "weapon" in entry_l or "铁匠" in name or "兵器" in name or "炼化" in name or "refin" in entry_l:
        return "武器", ["武器买卖", "武器普通修理", "武器特殊修理"]
    elif "armour" in entry_l or "armor" in entry_l or "裁缝" in name or "防具" in name or "服饰" in name:
        return "防具", ["衣服与头盔买卖", "防具备用修理"]
    elif "jewel" in entry_l or "accessory" in entry_l or "首饰" in name or "饰品" in name or "项链" in name:
        return "首饰", ["项链/手镯/戒指买卖", "首饰修理"]
    elif "potion" in entry_l or "药" in name or "医" in name:
        return "药店", ["金创药/魔法药买卖", "万年雪霜与疗伤药"]
    elif "essential" in entry_l or "grocery" in entry_l or "杂货" in name or "老金" in name:
        return "杂货", ["随机卷轴/回城卷", "火把/护身符/修复油"]
    elif "meet" in entry_l or "butcher" in entry_l or "肉" in name or "屠夫" in name:
        return "肉店", ["肉品收购与买卖", "纯度肉质提炼"]
    elif "book" in entry_l or "书" in name:
        return "书店", ["基础与进阶技能书买卖"]
    elif "inn" in entry_l or "storage" in entry_l or "仓库" in name or "保管" in name:
        return "仓库", ["个人仓库保管", "物品存取"]
    elif "trainer" in entry_l or "教头" in name or "导师" in name:
        return "导师", ["职业技能学习", "境界突破指导"]
    elif "companion" in entry_l or "宠物" in name or "驯兽" in name:
        return "宠物", ["宠物领养", "宠物复活与管理"]
    elif "notice" in entry_l or "告示" in name or "公告" in name:
        return "公告", ["村落悬赏", "大事公告"]
    elif "marriage" in entry_l or "月老" in name or "司仪" in name or "管理员" in name:
        return "管理", ["行会事务", "婚姻公证与活动管理"]
    elif "quest" in entry_l or "任务" in name:
        return "任务", ["剧情对话", "任务接取与交付"]
    elif "shoes" in entry_l or "鞋" in name:
        return "鞋店", ["鞋靴买卖与修理"]
    elif "collect" in entry_l or "栗子" in name or "鉴宝" in name or "收购" in name:
        return "收购", ["特殊材料收购", "古物鉴定"]
    else:
        return "特殊", ["场景剧情互动", "向导问询"]

def main():
    mud3_merchants = load_mud3_merchants()
    with open(ZIRCON_CURRENT_PATH, "r", encoding="utf-8") as f:
        z_npcs = [n for n in json.load(f) if n["HasRegion"] and n["HasMap"]]
        
    web_npcs_dict = {}
    if os.path.exists(WEB_NPCS_PATH):
        with open(WEB_NPCS_PATH, "r", encoding="utf-8") as f:
            for w in json.load(f):
                web_npcs_dict[w["npc_index"]] = w

    aligned_list = []
    
    for z in z_npcs:
        idx = z["Index"]
        z_map = z["MapFile"]
        z_x = z["X"]
        z_y = z["Y"]
        z_name = z["NPCName"]
        entry = z["EntryPage"]
        
        # 匹配 MUD3
        same_map = [m for m in mud3_merchants if m["map"].lower() == z_map.lower()]
        exact = [m for m in same_map if m["x"] == z_x and m["y"] == z_y]
        
        assigned_zh = ""
        orig_mud3_name = ""
        coord_status = "exact"
        mud3_ref = None
        
        if exact:
            mud3_ref = exact[0]
            orig_mud3_name = mud3_ref["name"]
            assigned_zh = orig_mud3_name
            coord_status = "exact_match"
        else:
            # 尝试根据已有的 web_npcs 对应
            w = web_npcs_dict.get(idx)
            if w and w.get("name_zh") and w.get("name_zh") != "None":
                assigned_zh = w["name_zh"]
            
            # 查找同地图近距离 (<= 5)
            closest = None
            if same_map:
                closest = min(same_map, key=lambda m: abs(m["x"] - z_x) + abs(m["y"] - z_y))
                dist = abs(closest["x"] - z_x) + abs(closest["y"] - z_y)
                if dist <= 5:
                    mud3_ref = closest
                    orig_mud3_name = closest["name"]
                    coord_status = f"near_match (dist={dist}, mud3_xy=({closest['x']},{closest['y']}))"

        # 特殊处理六面神石
        if "Hexa" in z_name or "Stone" in z_name or entry == "SK1 Teleporter" or entry == "MW Teleporter" or entry == "BI Teleporter" or entry == "Teleport Banya Hall":
            assigned_zh = "六面神石"
            
        # 特殊处理水井
        if entry == "Well" or idx == 38:
            assigned_zh = "古井"
            
        # 沙巴克城商人规范名
        if z_map == "3":
            if idx == 276: assigned_zh = "沙巴克炼化师"
            elif idx == 277: assigned_zh = "沙巴克药师"
            elif idx == 278: assigned_zh = "沙巴克杂货商"
            elif idx == 279: assigned_zh = "六面神石"
            elif idx == 280: assigned_zh = "沙巴克首饰商"
            elif idx == 281: assigned_zh = "沙巴克鉴宝师"
            elif idx == 282: assigned_zh = "沙巴克裁缝"
            elif idx == 283: assigned_zh = "沙巴克武器商"
            
        # 努玛村商人规范名
        if z_map == "4":
            if idx == 66: assigned_zh = "努玛药剂师"
            elif idx == 68: assigned_zh = "努玛首饰商"
            elif idx == 69: assigned_zh = "努玛收藏家"
            elif idx == 70: assigned_zh = "努玛裁缝"
            elif idx == 93: assigned_zh = "努玛告示牌"
            elif idx == 103: assigned_zh = "努玛驯兽师"
            elif idx == 136: assigned_zh = "努玛鉴宝师"
            
        # 潘夜岛商人规范名
        if z_map == "12":
            if idx == 45: assigned_zh = "六面神石"
            elif idx == 81: assigned_zh = "潘夜铁匠"
            elif idx == 82: assigned_zh = "潘夜药师"
            elif idx == 83: assigned_zh = "潘夜杂货商"
            elif idx == 84: assigned_zh = "潘夜首饰商"
            elif idx == 85: assigned_zh = "潘夜鉴宝商"
            elif idx == 86: assigned_zh = "潘夜裁缝"
            elif idx == 94: assigned_zh = "潘夜岛告示牌"
            elif idx == 99: assigned_zh = "潘夜码头管理员"
            elif idx == 104: assigned_zh = "潘夜驯兽师"
            
        # 比奇城月老/管理员
        if idx == 105:
            assigned_zh = "月老司仪"
            
        # 遗落地带杂货
        if idx == 24:
            assigned_zh = "避难所杂货商"

        # 如果还没有中文名，或者还是英文，强制规范
        if not assigned_zh or re.match(r'^[a-zA-Z0-9_\-\s]+$', assigned_zh):
            # 从原版对应或 EntryPage 赋予
            if orig_mud3_name:
                assigned_zh = orig_mud3_name
            else:
                cat_temp, _ = categorize_npc(z_name, entry, "")
                map_zh = MAP_NAME_ZH.get(z_map, z["MapDesc"])
                assigned_zh = f"{map_zh}{cat_temp}商"

        # 整理分类与服务
        cat, services = categorize_npc(assigned_zh, entry, z["Category"])
        map_title = MAP_NAME_ZH.get(z_map, z["MapDesc"])

        aligned_item = {
            "id": f"npc-{idx}",
            "npc_index": idx,
            "name_zh": assigned_zh,
            "name_en": z_name,
            "map_code": z_map,
            "map_name_zh": map_title,
            "map_desc_en": z["MapDesc"],
            "x": z_x,
            "y": z_y,
            "category": cat,
            "services": services,
            "image": z["Image"],
            "face_image": z["FaceImage"],
            "entry_page": entry,
            "mud3_match": {
                "mud3_name": orig_mud3_name if orig_mud3_name else None,
                "coord_status": coord_status,
                "mud3_file": mud3_ref["file"] if mud3_ref else None
            }
        }
        aligned_list.append(aligned_item)

    print(f"Total aligned NPCs: {len(aligned_list)}")
    
    # 检查是否还有任何英文名
    has_english = [a for a in aligned_list if re.match(r'^[a-zA-Z0-9_\-\s]+$', a["name_zh"])]
    print(f"NPCs with purely english/code name_zh: {len(has_english)}")
    for h in has_english:
        print(f"  Index {h['npc_index']}: {h['name_zh']} (en: {h['name_en']})")

    # 保存
    with open(OUT_ALIGNED_PATH, "w", encoding="utf-8") as f:
        json.dump(aligned_list, f, ensure_ascii=False, indent=2)
    print(f"Saved aligned data to {OUT_ALIGNED_PATH}")

    # 同时更新 mir3-website/data/npcs.json
    with open(WEB_NPCS_PATH, "w", encoding="utf-8") as f:
        json.dump(aligned_list, f, ensure_ascii=False, indent=2)
    print(f"Updated {WEB_NPCS_PATH} with {len(aligned_list)} records")

if __name__ == "__main__":
    main()

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""NPC 对话 IR 结构校验：保证「每个 NPC 有入口、每页无死链、引用对象存在」。

用法: python3 tools/validate_npc_ir.py /tmp/npc_dialog_ir.json
退出码 0 = 通过。
"""
from __future__ import annotations

import json
import re
import sys

DIALOG_TYPES = {
    "None", "BuySell", "Repair", "Refine", "RefineRetrieve", "CompanionManage", "WeddingRing",
    "RefinementStone", "MasterRefine", "WeaponReset", "ItemFragment", "AccessoryRefineUpgrade",
    "AccessoryRefineLevel", "AccessoryReset", "WeaponCraft", "AccessoryRefine", "RollDie",
    "RollYut", "Consignment", "Socketing", "SocketCombine",
}
ACTION_TYPES = {
    "Teleport", "TakeGold", "GiveGold", "TakeItem", "GiveItem", "Storage", "ChangeElement",
    "ChangeHorse", "Marriage", "Divorce", "RemoveWeddingRing", "ResetWeapon",
    "GiveItemExperience", "SpecialRefine", "Rebirth", "GiveCurrency", "TakeCurrency",
    "AddDataList", "RemoveDataList", "ClearDataList", "ChangeDataValue", "SetDataValue",
    "PromoteFame", "Message",
}
CHECK_TYPES = {
    "Level", "Class", "Gender", "Gold", "HasItem", "PKPoints", "HasWeapon", "WeaponLevel",
    "WeaponElement", "WeaponCanRefine", "Horse", "Marriage", "WeddingRing", "CanGainItem",
    "CanResetWeapon", "Random", "WeaponAddedStats", "Currency", "RollResult", "CheckDataList",
    "CheckDataValue", "CheckFame",
}
LINK_RE = re.compile(r"\[([^\[\]]*?):(-?\d+)\]")
EXIT_LABEL_RE = re.compile(r"\[[^\[\]]*:0\]")


def main() -> int:
    path = sys.argv[1] if len(sys.argv) > 1 else "/tmp/npc_dialog_ir.json"
    doc = json.load(open(path, encoding="utf-8"))
    graph = json.load(open("/tmp/npc_graph.json", encoding="utf-8"))
    items = {i["name"] for i in graph["items"]}
    maps = {m["fileName"] for m in graph["maps"]}

    errors: list[str] = []
    warnings: list[str] = []
    stats = {"npcs": 0, "pages": 0, "buttons": 0, "links": 0, "checks": 0, "actions": 0,
             "goods": 0, "types": 0}

    for npc in doc["npcs"]:
        stats["npcs"] += 1
        idx = npc["index"]
        pages = {p["key"]: p for p in npc["pages"]}
        if not npc.get("entry"):
            errors.append(f"npc {idx}: no entry")
            continue
        if npc["entry"] not in pages:
            errors.append(f"npc {idx}: entry {npc['entry']} missing")
        else:
            # 服务端 NPCCall 会沿 SuccessPage 走到第一页有 Say/动作的页；若链末啥都没有 = 点了没反应
            cur, seen = pages[npc["entry"]], set()
            while cur is not None and cur["key"] not in seen:
                seen.add(cur["key"])
                if cur["say"] or cur["actions"] or cur["goods"] or cur["types"]:
                    break
                cur = pages.get(cur["success"]) if cur["success"] else None
            if cur is None or not (cur["say"] or cur["actions"] or cur["goods"] or cur["types"]):
                errors.append(f"npc {idx}: entry chain has no visible page")

        for p in npc["pages"]:
            stats["pages"] += 1
            if p["type"] not in DIALOG_TYPES:
                errors.append(f"npc {idx} page {p['key']}: bad dialog type {p['type']}")
            say = p["say"]
            ids = [int(m.group(2)) for m in LINK_RE.finditer(say)]
            stats["links"] += len(ids)
            button_ids = [b["id"] for b in p["buttons"]]
            stats["buttons"] += len(button_ids)
            for i in ids:
                if i == 0:
                    continue
                if i not in button_ids:
                    errors.append(f"npc {idx} page {p['key']}: text link {i} has no button")
            for b in button_ids:
                if b <= 0:
                    errors.append(f"npc {idx} page {p['key']}: invalid button id {b}")
                if b not in ids:
                    warnings.append(f"npc {idx} page {p['key']}: button {b} not linked in text")
                dest = next(x["dest"] for x in p["buttons"] if x["id"] == b)
                if dest not in pages:
                    errors.append(f"npc {idx} page {p['key']}: button {b} dest {dest} missing")
            for c in p["checks"]:
                stats["checks"] += 1
                if c["type"] not in CHECK_TYPES:
                    errors.append(f"npc {idx} page {p['key']}: bad check {c['type']}")
                if c.get("item") and c["item"] not in items:
                    errors.append(f"npc {idx} page {p['key']}: check item {c['item']} missing")
                if c.get("fail") and c["fail"] not in pages:
                    errors.append(f"npc {idx} page {p['key']}: check fail {c['fail']} missing")
            for a in p["actions"]:
                stats["actions"] += 1
                if a["type"] not in ACTION_TYPES:
                    errors.append(f"npc {idx} page {p['key']}: bad action {a['type']}")
                if a["type"] == "Teleport" and a.get("map") not in maps:
                    errors.append(f"npc {idx} page {p['key']}: teleport map {a.get('map')} missing")
                if a["type"] in ("GiveItem", "TakeItem") and a.get("item") not in items:
                    errors.append(f"npc {idx} page {p['key']}: {a['type']} item {a.get('item')} missing")
            for g in p["goods"]:
                stats["goods"] += 1
                if g["item"] not in items:
                    errors.append(f"npc {idx} page {p['key']}: good {g['item']} missing")
            stats["types"] += len(p["types"])
            if p["success"] and p["success"] not in pages:
                errors.append(f"npc {idx} page {p['key']}: success {p['success']} missing")
            if not say and not p["actions"] and not p["goods"] and not p["types"] and p["type"] == "None":
                warnings.append(f"npc {idx} page {p['key']}: empty page")

    print(f"[validate_npc_ir] {stats}")
    print(f"  errors={len(errors)} warnings={len(warnings)}")
    for e in errors[:25]:
        print("  ERR ", e)
    for w in warnings[:10]:
        print("  warn", w)
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())

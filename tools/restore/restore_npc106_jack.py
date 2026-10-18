#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""restore_npc106_jack.py —— 恢复被 NpcMover 计划改写的 #106「Jack（物品碎片商人）」（默认 dry-run）。

背景（docs/AUDIT/WORKLOG.md 第 20 段）：
  清洗前旧库里 #106 = Jack，图 2 / 区域 `2 / Item Fragment`，位置 2(150,180)，
  入口页 #170「Item Fragment - Main」，整棵对话子树（页面/按钮/检查/动作）负责
  「碎片兑换」：100x Fragment + 20,000 金 → Fragment (II)；25x Fragment (II) + 50,000 金 → Fragment (III)。
  NpcMover 计划把 #106 改写成了**第二颗六面神石**（NPCName=六面神石、EntryPage=SnakeVallyTele）
  并搬到 #371 所在格 2(314,193) → ①两个 NPC 同格（游戏只实例化一个，#106 成死入口，即审计 P1
  「#106 点不到」）②官网登记的 Jack 与整棵兑换子树一并消失。

  前提已核实：兑换用物品 Fragment / Fragment (II) / Fragment (III)（#829/#830/#831）在当前库中
  **仍然存在**，因此恢复后兑换是可跑通的功能，不是空壳。

修复 = 从旧库按引用闭包恢复（不发明内容）：
  1) 从页面 #170 出发，递归收集 NPCPage / NPCButton / NPCCheck / NPCAction 四表子树；
     仅插入「库中尚无同 _Identity 行」的那些（dbeditor 导入器会自动重映射索引，
     实测 170–173 -> 38147–38150、160–163 -> 19656–19659）；
  2) #106 恢复 NPCName=Jack / FaceImage / EntryPage=#170（含 _Identity 派生字段）；
  3) 区域 #1448（`2 / Item Fragment`）点位放回 (150,180)。

写库链路同既有脚本：本脚本 --apply 改 dbeditor 工作区 → 82 上跑 sync.sh。

用法：
  python3 tools/restore/restore_npc106_jack.py --db-export <当前库导出目录>            # dry-run
  python3 tools/restore/restore_npc106_jack.py --db-export <...> --verify              # /tmp 副本全套
  python3 tools/restore/restore_npc106_jack.py --db-export <...> --apply
  python3 tools/restore/restore_npc106_jack.py --rollback /tmp/dbeditor_ws_backup_<ts>
"""
from __future__ import annotations
import argparse, json, sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import restore_cleaned_content as rcc  # noqa: E402

ROOT_PAGE = 170
NPC_INDEX = 106
REGION_INDEX = 1448
REGION_POINT = (150, 180)
TABLES = ("NPCPage", "NPCButton", "NPCCheck", "NPCAction")
# 引用字段 -> 目标表（用于净化不可解析的悬空引用）
FIELD_TABLE = {
    "Page": "NPCPage", "DestinationPage": "NPCPage", "FailPage": "NPCPage", "SuccessPage": "NPCPage",
    "Buttons": "NPCButton", "Checks": "NPCCheck", "Actions": "NPCAction",
    "ItemParameter1": "ItemInfo", "ItemParameter2": "ItemInfo",
    "MapParameter1": "MapInfo", "MapParameter2": "MapInfo",
    "MonsterParameter1": "MonsterInfo", "MonsterParameter2": "MonsterInfo",
}
# 每个表的字段 -> 它指向的表
REF_MAP = {
    "NPCPage": {"Buttons": "NPCButton", "Checks": "NPCCheck", "Actions": "NPCAction"},
    "NPCButton": {"Page": "NPCPage", "DestinationPage": "NPCPage"},
    "NPCCheck": {"Page": "NPCPage", "FailPage": "NPCPage", "SuccessPage": "NPCPage"},
    "NPCAction": {"Page": "NPCPage", "FailPage": "NPCPage", "SuccessPage": "NPCPage"},
}


def closure(by_table: dict) -> dict:
    """从根页面出发，沿引用闭包收集四表行。"""
    got = {t: {} for t in TABLES}
    queue = [("NPCPage", ROOT_PAGE)]
    while queue:
        table, idx = queue.pop()
        if idx in got[table]:
            continue
        row = by_table[table].get(idx)
        if row is None:
            continue
        got[table][idx] = row
        for field, target in REF_MAP[table].items():
            v = row.get(field)
            if v is None:
                continue
            refs = v if isinstance(v, list) else [v]
            for ref in refs:
                if isinstance(ref, dict) and ref.get("Index") is not None:
                    queue.append((target, ref["Index"]))
    return got


def main():
    ap = argparse.ArgumentParser(description="恢复 #106 Jack（默认 dry-run）")
    ap.add_argument("--workspace", type=Path, default=rcc.DEFAULT_WORKSPACE)
    ap.add_argument("--old-export", type=Path, default=rcc.DEFAULT_OLD_EXPORT)
    ap.add_argument("--db-export", type=Path, default=None,
                    help="当前库的真实导出目录（SystemDbProbe --json），用于判断哪些行真的缺失")
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--verify-db", type=Path, default=None)
    ap.add_argument("--out", type=Path, default=Path("/tmp/restore_npc106_preview.json"))
    ap.add_argument("--rollback", type=Path)
    args = ap.parse_args()

    stamp = time.strftime("%Y%m%d-%H%M%S")
    if args.rollback:
        rcc.rollback(args.rollback, args.workspace)
        return 0

    rcc.ensure_old_export(args.old_export, False)
    old = {t: rcc.by_index(rcc.load_table(args.old_export, t)[0]) for t in TABLES}
    sub = closure(old)
    print("旧库闭包（从页面 #%d 出发）：" % ROOT_PAGE)
    for t in TABLES:
        print("   %-10s %2d 行 %s" % (t, len(sub[t]), sorted(sub[t])[:14]))

    # 当前库真实内容（用于判断缺失），无导出时退化为用工作区（注意工作区只是部分覆盖）
    ref_dir = args.db_export
    if ref_dir is None:
        print("[!] 未给 --db-export，退化为用工作区判断缺失（工作区是部分覆盖，可能漏判）")
        ref_dir = args.workspace
    existing = {t: set() for t in TABLES}
    for t in TABLES:
        for r in rcc.load_table(ref_dir, t)[0]:
            existing[t].add(r.get("_Identity"))

    # ---- 净化：剥掉目标在库中不存在的悬空引用（例：NPCAction#89-91 的 MapParameter1 -> MapInfo#563）----
    db_index = {}
    pruned = []
    for field, tgt in FIELD_TABLE.items():
        if tgt not in db_index:
            try:
                db_index[tgt] = {r["Index"] for r in rcc.load_table(ref_dir, tgt)[0]}
            except Exception:
                db_index[tgt] = None  # 导出里没有这张表 -> 不做判断
        # 关键：闭包内即将一起插入的行也算「有效目标」，否则新页面/按钮之间的
        # 互相引用会被误判为悬空而剥掉（曾导致新增子树 Page/Buttons 全空）。
        if db_index.get(tgt) is not None and tgt in sub:
            db_index[tgt] |= set(sub[tgt].keys())
    for t in TABLES:
        for i, row in sub[t].items():
            for field, tgt in FIELD_TABLE.items():
                if field not in row or db_index.get(tgt) is None:
                    continue
                v = row[field]
                if isinstance(v, dict) and v.get("Index") is not None and v["Index"] not in db_index[tgt]:
                    pruned.append("%s#%d.%s -> %s#%d 不存在，已剥离" % (t, i, field, tgt, v["Index"]))
                    row[field] = None
    if pruned:
        print("\n净化悬空引用 %d 处：" % len(pruned))
        for p in pruned:
            print("   " + p)

    print("\n按「页面用 _Identity、其余三表按索引」判断需插入：")
    additions = {}
    for t in TABLES:
        if t == "NPCPage":
            # 页面 _Identity 是唯一页名，可作身份判据
            miss = [r for i, r in sorted(sub[t].items()) if r.get("_Identity") not in existing[t]]
        else:
            # 按钮/检查/动作的 _Identity 是 "1"/"2"/"3" 这类通用标签，不能当身份；
            # 全部纳入闭包，由 dbeditor 导入器按索引自动重映射（实测 160-163 -> 19656-19659）
            miss = [r for i, r in sorted(sub[t].items())]
        if miss:
            additions[t] = miss
        print("   %-10s 闭包 %2d 行 -> 需插入 %2d 行 %s" % (t, len(sub[t]), len(miss), sorted(r["Index"] for r in miss)))

    # ---- #106 / 区域 #1448 ----
    old_npcs = rcc.by_index(rcc.load_table(args.old_export, "NPCInfo")[0])
    old_npc = old_npcs[NPC_INDEX]
    cur_npc = rcc.by_index(rcc.load_table(args.workspace, "NPCInfo")[0])[NPC_INDEX]
    modifications = []
    if cur_npc.get("NPCName") != old_npc.get("NPCName"):
        modifications.append({"table": "NPCInfo", "Index": NPC_INDEX, "field": "NPCName", "new": old_npc.get("NPCName")})
        if "_Identity" in cur_npc:
            modifications.append({"table": "NPCInfo", "Index": NPC_INDEX, "field": "_Identity", "new": old_npc.get("_Identity")})
    if cur_npc.get("FaceImage") != old_npc.get("FaceImage"):
        modifications.append({"table": "NPCInfo", "Index": NPC_INDEX, "field": "FaceImage", "new": old_npc.get("FaceImage")})
    if (cur_npc.get("EntryPage") or {}).get("Index") != ROOT_PAGE:
        modifications.append({"table": "NPCInfo", "Index": NPC_INDEX, "field": "EntryPage",
                              "new": {"Index": ROOT_PAGE, "Name": old_npc.get("EntryPage", {}).get("Name")}})
    cur_regions = rcc.by_index(rcc.load_table(args.workspace, "MapRegion")[0])
    cur_region = cur_regions.get(REGION_INDEX)
    if cur_region is None:
        raise rcc.Blocked("工作区没有区域 #%d" % REGION_INDEX)
    pr = cur_region.get("PointRegion") or {}
    if (pr.get("CenterX"), pr.get("CenterY")) != REGION_POINT:
        modifications.append({"table": "MapRegion", "Index": REGION_INDEX, "field": "PointRegion",
                              "new": {"PointCount": 1, "CenterX": REGION_POINT[0], "CenterY": REGION_POINT[1]}})
    old_region = rcc.by_index(rcc.load_table(args.old_export, "MapRegion")[0]).get(REGION_INDEX)
    if old_region:
        for f in ("_Identity", "Description"):
            if cur_region.get(f) != old_region.get(f):
                modifications.append({"table": "MapRegion", "Index": REGION_INDEX, "field": f, "new": old_region.get(f)})

    print("\n#106 修改 %d 处：" % len([m for m in modifications if m["table"] == "NPCInfo"]))
    for m in modifications:
        if m["table"] == "NPCInfo":
            print("   %s -> %r" % (m["field"], m["new"]))
    print("区域 #%d 的修改 %d 处" % (REGION_INDEX, len([m for m in modifications if m["table"] == "MapRegion"])))

    # ---- 自检：闭包内部引用必须齐全 ----
    problems = []
    for t, rows in sub.items():
        for i, r in rows.items():
            for field, target in REF_MAP[t].items():
                v = r.get(field)
                refs = v if isinstance(v, list) else ([v] if v else [])
                for ref in refs:
                    if isinstance(ref, dict) and ref.get("Index") is not None and ref["Index"] not in sub[target]:
                        problems.append("%s#%d.%s 引用的 %s#%d 不在闭包内" % (t, i, field, target, ref["Index"]))

    args.out.write_text(json.dumps(
        {"closure": {t: sorted(sub[t]) for t in TABLES},
         "additions": {t: sorted(r["Index"] for r in v) for t, v in additions.items()},
         "modifications": modifications, "problems": problems}, ensure_ascii=False, indent=2), encoding="utf-8")
    print("\n预览 -> %s" % args.out)
    if problems:
        print("[X] 自检未通过：")
        for p in problems:
            print("   " + p)
        return 1
    print("[✓] 自检通过：闭包完整、引用可解析")

    if args.verify:
        return rcc.verify(args.workspace, additions, modifications, stamp, args.verify_db)
    if not args.apply:
        print("\n（dry-run；确认后加 --apply，再在 82 上跑 sync.sh）")
        return 0

    files = {"NPCPage.json", "NPCButton.json", "NPCCheck.json", "NPCAction.json",
             "NPCInfo.json", "MapRegion.json"}
    bdir = rcc.backup_workspace(args.workspace, stamp, sorted(files))
    changed = rcc.write_tables(args.workspace, additions, modifications)
    print("\n[✓] 已写工作区：%s\n[✓] 备份：%s" % (changed, bdir))
    print("下一步：82 上 cd ~/development/Mir3-Research && bash Tools/dbeditor/sync.sh")
    return 0


if __name__ == "__main__":
    sys.exit(main())

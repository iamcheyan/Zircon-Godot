#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""restore_quest_rewards_and_skill_names.py —— 第二批「仍在使用却坏了」的数据修复（默认 dry-run）。

两项都已取证（证据与数字见 docs/AUDIT/WORKLOG.md 第 8 段）：

  组 A  技能显示名退化成枚举标识
        现库 61 条 MagicInfo 里有 16 条的 Name 是 `LightningBeam` 这类枚举标识，而旧库同名技能
        的显示名是 `Lightning Beam`。这些技能**仍在被使用**：它们的技能书物品还在 NPC 售卖、
        掉落表与 NPC 检查里（17 本），玩家在技能窗看到的却是坏名字。
        → 按"去除非字母数字后的字母序列相同"匹配旧库显示名，逐条改 Name（歧义则跳过并报告）。

  组 B  34 条经典任务没有任何奖励
        旧库这 34 条任务都有 QuestReward 行，且**全部**是 `ItemEffect = "Experience"` 的
        「Experience」令牌物品 ×N（服务端 PlayerObject 里确有 ItemEffect.Experience 分支）。
        现库连该令牌物品本身都没有 → 整条奖励链缺失。
        → 恢复 1 行 Experience 物品 + 34 行 QuestReward，并同步 QuestInfo.Rewards 反链
          （服务端发放走 `foreach (reward in quest.Rewards)`，**反链不补等于没补**）。

不恢复（口径，见 docs/GAME_AUDIT_2026-10-18.md 第四节）：
  * 8 张 0 刷怪地图的 32 行旧刷怪 —— 依赖 14 个已删怪物，属恢复被有意清洗的内容；
  * 199 只已删怪物 / 582 件已删物品 —— 实测当前库对其零引用。

本脚本 **只打印 + 写 /tmp 预览**；`--apply` 才写 dbeditor 工作区（写前整份备份到 /tmp）。
写库链路（本脚本只做第 1 步）：
  1. 本脚本 --apply 改 dbeditor 工作区 JSON
  2. 在 82 上跑 Tools/dbeditor/sync.sh（导入器只应用「工作区 vs _baseline」的差）

不变量（脚本自检，违反则拒绝写）：
  * 新行 Index 在该表工作区里未被占用；
  * 行内所有 ref/reflist 指向当前工作区里存在的对象（按 Index）；
  * 幂等：已等价存在的行不重复加、已改对的 Name 不重复改。

用法：
  python3 tools/restore/restore_quest_rewards_and_skill_names.py            # dry-run
  python3 tools/restore/restore_quest_rewards_and_skill_names.py --verify   # /tmp 副本上跑导入器 + round-trip
  python3 tools/restore/restore_quest_rewards_and_skill_names.py --apply    # 写工作区（先备份）
  python3 tools/restore/restore_quest_rewards_and_skill_names.py --rollback /tmp/dbeditor_ws_backup_<ts>
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
# 复用第一批脚本的机制（工作区读写、备份/回滚、verify、名字归一化）
import restore_cleaned_content as rcc  # noqa: E402

TABLES = ["MagicInfo", "ItemInfo", "QuestInfo", "QuestReward"]


NAME_FIELDS = {"MonsterInfo": "MonsterName", "ItemInfo": "ItemName", "MagicInfo": "Name",
               "NPCInfo": "NPCName", "MapInfo": "FileName", "QuestInfo": "QuestName",
               "SetInfo": "SetName", "CurrencyInfo": "Name"}


def build_name_trim_mods(ws_dir):
    """名字字段的首尾空白（实测全库仅 MonsterInfo#196 'Pachon ' 一处）。
    引用一律按 Index，不按名字，所以 trim 安全；_Identity 是派生字段需同步改。"""
    mods, notes = [], []
    for table, field in NAME_FIELDS.items():
        path = Path(ws_dir) / f"{table}.json"
        if not path.exists():
            continue
        doc = json.loads(path.read_text(encoding="utf-8"))
        rows = doc["rows"] if isinstance(doc, dict) else doc
        for r in rows:
            v = r.get(field)
            if isinstance(v, str) and v != v.strip():
                mods.append({"table": table, "Index": r["Index"], "field": field, "new": v.strip()})
                if isinstance(r.get("_Identity"), str) and r["_Identity"] != r["_Identity"].strip():
                    mods.append({"table": table, "Index": r["Index"], "field": "_Identity",
                                 "new": r["_Identity"].strip()})
                notes.append(f"[trim] {table}#{r['Index']} {v!r} → {v.strip()!r}")
    return mods, notes


def letters_only(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def load_ws(ws_dir: Path, table: str):
    return rcc.load_table(ws_dir, table)


def next_free(base_set, start=1):
    i = start
    while i in base_set:
        i += 1
    return i


def next_high(base_set):
    """取“最大 Index + 1”。新对象一律用高号位，避免占用低号位可能与
    硬编码/旧约定相撞的 Index（首批脚本也是这个取向）。"""
    return (max(base_set) + 1) if base_set else 1


# --------------------------------------------------------------------------
# 组 A：技能显示名（枚举标识 → 旧库显示名）
# --------------------------------------------------------------------------

def build_skill_name_mods(cur_magic, old_magic):
    """返回 (modifications, notes)。modifications 元素 {"table","Index","field","new"}。"""
    old_by_letters = {}
    for r in old_magic:
        nm = r.get("Name")
        if nm:
            old_by_letters.setdefault(letters_only(nm), set()).add(nm)

    # 两条需要显式裁决的（字母匹配法覆盖不到）：
    #   #191 "ScortchedEarth" 是 "Scorched Earth" 的拼写错误 → 按旧库原名修（difflib 0.96）；
    #   #194 "ThunderStrike" 旧库没有对应技能（最近的 Thunder Storm 是另一个技能）→ 属新内容，
    #        按与其余 16 条相同的命名约定**机械拆分**为 "Thunder Strike"，并在 notes 里写明来源。
    EXPLICIT = {
        191: ("Scorched Earth", "旧库原名（ScortchedEarth 是拼写错误）"),
        194: ("Thunder Strike", "旧库无对应技能；按同一命名约定机械拆分（非旧库原值）"),
    }

    mods, notes = [], []
    for r in cur_magic:
        name = r.get("Name") or ""
        if r["Index"] in EXPLICIT:
            target, why = EXPLICIT[r["Index"]]
            if name != target:
                mods.append({"table": "MagicInfo", "Index": r["Index"], "field": "Name", "new": target})
                if "_Identity" in r:
                    mods.append({"table": "MagicInfo", "Index": r["Index"], "field": "_Identity", "new": target})
                notes.append(f"[组A] #{r['Index']} {name!r} → {target!r}（{why}）")
            continue
        if " " in name:
            continue                       # 已是显示名（含空格）→ 不动
        cands = old_by_letters.get(letters_only(name), set())
        if len(cands) == 1:
            target = next(iter(cands))
            if target != name:
                mods.append({"table": "MagicInfo", "Index": r["Index"],
                             "field": "Name", "new": target})
                # _Identity 是派生自 Name 的导出身份字段，必须同步改，否则 round-trip 会报差异
                # （2026-10-18 --verify 实测：只改 Name 会留下 16 处 _Identity 不一致）。
                if "_Identity" in r:
                    mods.append({"table": "MagicInfo", "Index": r["Index"],
                                 "field": "_Identity", "new": target})
        elif len(cands) > 1:
            notes.append(f"[组A] #{r['Index']} {name!r} 旧库有多个字母相同的候选 "
                         f"{sorted(cands)} → 跳过（不猜）")
    return mods, notes


# --------------------------------------------------------------------------
# 组 B：Experience 令牌物品 + 34 条任务奖励 + QuestInfo.Rewards 反链
# --------------------------------------------------------------------------

def find_experience_item(rows):
    return [r for r in rows if r.get("ItemEffect") == "Experience"]


def build_quest_reward_adds(cur_item, cur_quest, cur_reward, old_item, old_reward,
                            workspace_item_dir):
    """返回 (additions, mods, notes)。additions: {table: [rows]}；mods 用于补 QuestInfo.Rewards。"""
    notes = []

    # --- B1: Experience 令牌物品 ---
    additions = {}
    existing = find_experience_item(cur_item)
    if len(existing) == 1:
        exp_index = existing[0]["Index"]
        notes.append(f"[组B] 现库已有 Experience 令牌物品 #{exp_index}，不再新增")
    elif len(existing) > 1:
        raise rcc.Blocked(f"现库有 {len(existing)} 条 ItemEffect=Experience 的物品，身份不唯一")
    else:
        old_exp = find_experience_item(old_item)
        if len(old_exp) != 1:
            raise rcc.Blocked(f"旧库 Experience 令牌物品不是唯一一条：{len(old_exp)}")
        used = {r["Index"] for r in cur_item}
        exp_index = next_high(used)
        new_item = dict(old_exp[0])
        new_item["Index"] = exp_index
        additions["ItemInfo"] = [new_item]
        notes.append(f"[组B] 新增 Experience 令牌物品：旧库 #{old_exp[0]['Index']} → 新 #{exp_index}"
                     f"（Index 已被占用，按名字重映射而非硬搬旧 Index）")

    # --- B2: 34 条 QuestReward ---
    quest_by_index = rcc.by_index(cur_quest)
    item_by_index = rcc.by_index(cur_item)
    if "ItemInfo" in additions:
        for r in additions["ItemInfo"]:
            item_by_index[r["Index"]] = r

    used_reward = {r["Index"] for r in cur_reward}
    new_rewards, back_refs = [], []

    for old_row in old_reward:
        q_ref = old_row.get("Quest") or {}
        q_index = q_ref.get("Index")
        if q_index not in quest_by_index:
            notes.append(f"[组B] 旧奖励 #{old_row['Index']} 指向的任务 #{q_index} 现库不存在 → 跳过")
            continue
        q_identity = quest_by_index[q_index].get("_Identity")

        # 幂等：现库已有等价行（同任务 + 同物品 + 同数量 + 同职业 + 同 Choice）就不加
        dup = [r for r in cur_reward
               if (r.get("Quest") or {}).get("Index") == q_index
               and (r.get("Item") or {}).get("Index") == exp_index
               and r.get("Amount") == old_row.get("Amount")
               and r.get("Class") == old_row.get("Class")
               and r.get("Choice") == old_row.get("Choice")]
        if dup:
            notes.append(f"[组B] 任务 #{q_index} 已有等价奖励行 #{dup[0]['Index']} → 跳过")
            back_refs.append((q_index, dup[0]["Index"]))
            continue

        idx = old_row["Index"]
        if idx in used_reward:
            idx = next_high(used_reward | {x["Index"] for x in new_rewards})
            notes.append(f"[组B] 旧奖励 #{old_row['Index']} 的 Index 已被占用 → 新分配 #{idx}")
        used_reward.add(idx)

        row = dict(old_row)
        row["Index"] = idx
        row["Quest"] = {"Index": q_index, "Name": q_identity}
        row["Item"] = {"Index": exp_index, "Name": "Experience"}
        new_rewards.append(row)
        back_refs.append((q_index, idx))

    if new_rewards:
        additions["QuestReward"] = new_rewards

    # --- B3: QuestInfo.Rewards 反链（服务端发放就靠它）---
    mods = []
    by_q = {}
    for q_index, r_index in back_refs:
        by_q.setdefault(q_index, []).append(r_index)
    for q_index, idxs in sorted(by_q.items()):
        row = quest_by_index[q_index]
        cur_refs = row.get("Rewards")
        if cur_refs is None:
            mods.append({"table": "QuestInfo", "Index": q_index, "field": "Rewards",
                         "new": [{"Index": i, "Name": None} for i in sorted(idxs)]})
        else:
            have = {r["Index"] for r in cur_refs}
            merged = list(cur_refs) + [{"Index": i, "Name": None} for i in sorted(idxs) if i not in have]
            if merged != cur_refs:
                mods.append({"table": "QuestInfo", "Index": q_index, "field": "Rewards",
                             "new": merged})
    return additions, mods, notes


# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description="第二批数据修复（技能显示名 + 任务奖励），默认 dry-run")
    ap.add_argument("--workspace", type=Path, default=rcc.DEFAULT_WORKSPACE)
    ap.add_argument("--old-export", type=Path, default=rcc.DEFAULT_OLD_EXPORT)
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--verify-db", type=Path, default=None)
    ap.add_argument("--out", type=Path, default=Path("/tmp/restore_batch2_preview.json"))
    ap.add_argument("--rollback", type=Path)
    ap.add_argument("--trim-names", action="store_true",
                    help="额外把所有名字字段的首尾空白去掉（引用按 Index，安全）")
    args = ap.parse_args()

    stamp = time.strftime("%Y%m%d-%H%M%S")
    if args.rollback:
        rcc.rollback(args.rollback, args.workspace)
        return 0

    rcc.ensure_old_export(args.old_export, False)

    cur_magic, _ = load_ws(args.workspace, "MagicInfo")
    cur_item, _ = load_ws(args.workspace, "ItemInfo")
    cur_quest, _ = load_ws(args.workspace, "QuestInfo")
    cur_reward, _ = load_ws(args.workspace, "QuestReward")
    old_magic, _ = rcc.load_table(args.old_export, "MagicInfo")
    old_item, _ = rcc.load_table(args.old_export, "ItemInfo")
    old_reward, _ = rcc.load_table(args.old_export, "QuestReward")

    print(f"现库：MagicInfo {len(cur_magic)}、ItemInfo {len(cur_item)}、"
          f"QuestInfo {len(cur_quest)}、QuestReward {len(cur_reward)}")
    print(f"旧库：MagicInfo {len(old_magic)}、ItemInfo {len(old_item)}、QuestReward {len(old_reward)}")

    mods_a, notes_a = build_skill_name_mods(cur_magic, old_magic)
    additions, mods_b, notes_b = build_quest_reward_adds(
        cur_item, cur_quest, cur_reward, old_item, old_reward, args.workspace)
    modifications = mods_a + mods_b
    notes = notes_a + notes_b
    if args.trim_names:
        mods_t, notes_t = build_name_trim_mods(args.workspace)
        modifications += mods_t
        notes += notes_t
        print(f"\n== 名字首尾空白：{len(mods_t)} 处 ==")
        for m in mods_t:
            print(f"  {m['table']}#{m['Index']}.{m['field']} → {m['new']!r}")

    print(f"\n== 组 A：技能显示名修改 {len(mods_a)} 条 ==")
    for m in mods_a:
        cur_name = rcc.by_index(cur_magic)[m["Index"]].get("Name")
        print(f"  #{m['Index']}: {cur_name!r} → {m['new']!r}")

    print(f"\n== 组 B：新增行 ==")
    for t, rows in additions.items():
        print(f"  {t}: +{len(rows)}")
    print(f"  QuestInfo.Rewards 反链更新: {len(mods_b)} 条任务")

    if notes:
        print("\n== 说明/跳过 ==")
        for n in notes:
            print("  " + n)

    # ---- 自检 1：Index 唯一 ----
    problems = []
    for table, rows in additions.items():
        cur_rows = {"MagicInfo": cur_magic, "ItemInfo": cur_item,
                    "QuestInfo": cur_quest, "QuestReward": cur_reward}[table]
        used = {r["Index"] for r in cur_rows}
        for r in rows:
            if r["Index"] in used:
                problems.append(f"{table}#{r['Index']} Index 与现有行冲突")
            used.add(r["Index"])

    # ---- 自检 2：引用完整性（在"打完补丁后"的视角上验）----
    after_item = list(cur_item) + additions.get("ItemInfo", [])
    after_quest = list(cur_quest) + additions.get("QuestInfo", [])
    after_reward = list(cur_reward) + additions.get("QuestReward", [])
    item_idx = {r["Index"] for r in after_item}
    quest_idx = {r["Index"] for r in after_quest}
    for r in after_reward:
        q = (r.get("Quest") or {}).get("Index")
        it = (r.get("Item") or {}).get("Index")
        if q is not None and q not in quest_idx:
            problems.append(f"QuestReward#{r['Index']} 引用的任务 #{q} 不存在")
        if it is not None and it not in item_idx:
            problems.append(f"QuestReward#{r['Index']} 引用的物品 #{it} 不存在")
    reward_idx = {r["Index"] for r in after_reward}
    for m in mods_b:
        if m["field"] != "Rewards":
            continue
        for ref in m["new"]:
            if ref["Index"] not in reward_idx:
                problems.append(f"QuestInfo#{m['Index']}.Rewards 引用的奖励 #{ref['Index']} 不存在")

    preview = {"stamp": stamp, "additions": {t: len(v) for t, v in additions.items()},
               "modifications": modifications, "notes": notes, "problems": problems}
    args.out.write_text(json.dumps(preview, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n预览已写：{args.out}")

    if problems:
        print("\n[X] 自检未通过，拒绝写：")
        for p in problems:
            print("   " + p)
        return 1
    print("\n[✓] 自检通过：Index 无冲突、引用全部可解析")

    if args.verify:
        return rcc.verify(args.workspace, additions, modifications, stamp, args.verify_db)

    if not args.apply:
        print("\n（dry-run；确认无误后加 --apply 写工作区，再在 82 上跑 sync.sh）")
        return 0

    files = set()
    for t in additions:
        files.add(f"{t}.json")
    for m in modifications:
        files.add(f"{m['table']}.json")
    bdir = rcc.backup_workspace(args.workspace, stamp, sorted(files))
    changed = rcc.write_tables(args.workspace, additions, modifications)
    print(f"\n[✓] 已写工作区：{changed}")
    print(f"[✓] 备份：{bdir}（回滚：--rollback {bdir}）")
    print("下一步：在 82 上 cd ~/development/Mir3-Research && bash Tools/dbeditor/sync.sh")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""fill_zero_stat_monsters.py —— 为「有刷怪但 MonsterInfoStat 0 行」的怪物**推导**属性行。

背景（审计 R2，docs/AUDIT/monsters.md）
---------------------------------------
当前库有 15 只怪物在 `MonsterInfoStat` 里一行都没有：

    388 Fierce Falcon (105) / 390 Fierce Wind Demon (105) / 395 Dungeon Goddess 1 (55) /
    396 Dungeon Goddess 2 (55) / 409 Otherworld Sea General (100) / 412 Otherworld Poison Demon (100) /
    416 Otherworld Ship Guard (100) / 417 Otherworld Ship Guard 1 (100) / 418 Otherworld Red Mage (100) /
    421 Otherworld Tentacle Demon (100) / 425 Otherworld Light Guard (100) / 448 Flame Lion (55) /
    449 Flame Demon Soldier (180) / 458 Ice Break Demon Soldier (180) / 484 Stone Lion (55)

它们**从没有过**属性数据（清洗前旧库里也没有同名行，见审计 §3.2），因此本脚本产出的是
**推导值（derived）**，不是历史值的恢复（restore）。链路：
`MonsterInfoStat` 行 → `MonsterInfo.StatsChanged()`（`LibraryCore/SystemModels/MonsterInfo.cs`
`OnLoaded` 时重算 `Stats`）→ `MonsterObject.RefreshStats`（`ServerLibrary/Models/MonsterObject.cs`）
只叠加 `MonsterInfo.Stats`。0 行 ⇒ 生命/攻防/命中全 0 ⇒ 一击秒杀。

推导规则（写死在脚本里，逐条可在 preview 里复核）
------------------------------------------------
R1 参照集分级（同一只怪只用一个参照集）
    tier 1：`MonsterInfo.Level` 完全相同、且**有属性行**的怪物；
    tier 2：tier 1 为空时，取 |ΔLevel| <= 5 的怪物；
    tier 3：tier 2 仍为空时，按 |ΔLevel| 由小到大（并列时取 Level 小的）依次并入**有属性行**的
            等级，直到参照怪物数 >= `--min-ref`（默认 5）为止。
    参照集只包含「有属性行」的怪物，且排除本脚本的目标怪物（它们本来就没有行）。
R2 每个 Stat 取参照集的**中位数**；只补参照集里出现率 >= `--min-presence`（默认 0.5）的 Stat。
R3 取整：`Health` 取最近的 10 的倍数；其它 Stat 取最近整数；任何 |中位数| >= 1000 的 Stat 也按
    10 取整。**半值远离零取整**（不是 Python 内置 round 的银行家取整）。`Health` 必须 > 0。
R4 新行 `Index` 从那之后未占用的号段顺序分配（现有最大 Index + 1）；`Monster` 引用指向现库该
    怪物的 `{Index, Name}`。
R5 **不碰 `MonsterInfo.json`**——`MonsterInfoStat.Monster` 是 `[Association("MonsterInfoStats")]`，
    子行 setter 触发 `DBObject.CreateLink` 自动把子行挂进父的
    `MonsterInfo.MonsterInfoStats`（`DBBindingList` 不入库，只在 `Session` 载入时由子行 ref 重建：
    `DBRelationship.ConsumeKeys`），父的 `MonsterInfoStats` 就是纯派生回链，因此工作区
    `MonsterInfo.json` 无需（也不应）跟着改。见报告「反链」一节。
R6 幂等：已有属性行的目标一律跳过；重复 dry-run 输出一致。

用法
----
    # 只看（默认 dry-run，绝不写工作区）：
    python3 tools/restore/fill_zero_stat_monsters.py

    # 真写（先备份到 /tmp/dbeditor_ws_backup_stats_<时间戳>/）：
    python3 tools/restore/fill_zero_stat_monsters.py --apply

写库链：改的是 dbeditor 工作区 JSON；真正落库要另跑
`Tools/dbeditor/sync.sh`（本脚本不跑它、不碰 System.db、不启停服务）。

退出码：0 正常 / 2 参数或 IO 错误 / 3 推导结果不满足不变量（Health<=0、Min>Max 等）/ 4 写后校验失败。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import statistics
import sys
import time
from collections import defaultdict
from pathlib import Path

# --- 审计 R2 的 15 只（现库 MonsterInfo.Index） ---
DEFAULT_TARGETS = [388, 390, 395, 396, 409, 412, 416, 417, 418, 421, 425, 448, 449, 458, 484]

DEFAULT_WORKSPACE = Path(
    os.environ.get("MIR3_DBEDITOR_WS")
    or "/Users/tetsuya/Development/Mir3-Research/Tools/dbeditor/workspace"
)

# Stat 顺序镜像 LibraryCore/Stat.cs 的 `public enum Stat`（新行按此顺序排列，与既有行风格一致）；
# 表里出现但不在该枚举里的 Stat 追加在末尾并按字母序。
STAT_ENUM_ORDER = [
    "BaseHealth", "BaseMana", "Health", "Mana", "MinAC", "MaxAC", "MinMR", "MaxMR", "MinDC", "MaxDC",
    "MinMC", "MaxMC", "MinSC", "MaxSC", "Accuracy", "Agility", "AttackSpeed", "Light", "Luck",
    "FireAttack", "FireResistance", "IceAttack", "IceResistance", "LightningAttack",
    "LightningResistance", "WindAttack", "WindResistance", "HolyAttack", "HolyResistance",
    "DarkAttack", "DarkResistance", "PhantomAttack", "PhantomResistance", "LifeSteal",
    "ExperienceRate", "DropRate", "None", "SkillRate", "PickUpRadius", "Healing", "HealingCap",
    "Invisibility", "FireAffinity", "IceAffinity", "LightningAffinity", "WindAffinity",
    "HolyAffinity", "DarkAffinity", "PhantomAffinity", "ReflectDamage", "WeaponElement",
    "Redemption", "HealthPercent", "CriticalChance", "SaleBonus5", "SaleBonus10", "SaleBonus15",
    "SaleBonus20", "MagicShield", "Cloak", "CloakDamage", "TheNewBeginning", "Brown", "PKPoint",
    "GlobalShout", "MCPercent", "JudgementOfHeaven", "Transparency", "CelestialLight",
    "DarkConversion", "RenounceHPLost", "BagWeight", "WearWeight", "HandWeight", "GoldRate",
    "AvailableHuntGold", "AvailableHuntGoldCap", "ItemReviveTime", "MaxRefineChance",
    "CompanionInventory", "CompanionBagWeight", "DCPercent", "SCPercent", "CompanionHunger",
    "PetDCPercent", "BossTracker", "PlayerTracker", "CompanionRate", "WeightRate",
    "MagicDefencePercent", "PhysicalDefencePercent", "ManaPercent", "RecallSet",
    "MonsterExperience", "MonsterGold", "MonsterDrop", "MonsterDamage", "MonsterHealth",
    "ItemIndex", "CompanionCollection", "ProtectionRing", "ClearRing", "TeleportRing",
    "BaseExperienceRate", "BaseGoldRate", "BaseDropRate", "FrostBiteDamage",
    "MaxMonsterExperience", "MaxMonsterGold", "MaxMonsterDrop", "MaxMonsterDamage",
    "MaxMonsterHealth", "CriticalDamage", "Experience", "DeathDrops", "PhysicalResistance",
    "FragmentRate", "MapSummoning", "FrostBiteChance", "ParalysisChance", "SlowChance",
    "SilenceChance", "BlockChance", "EvasionChance", "IgnoreStealth", "FootballArmourAction",
    "PoisonResistance", "Rebirth", "Focus", "SizePercent", "GrowthLevel", "Invincibility",
    "SuperiorMagicShield", "DefensiveMastery", "SoulResonance", "Fame", "ElementalSwords",
    "RoamDistance", "VengeanceSource", "VengeancePercent", "VengeanceResponseChance",
    "CraftingSuccess", "ThrowDistance", "AutoCast", "Flexibility", "FloatStrength", "ReelBonus",
    "NibbleChance", "FinderChance", "Random1", "Random2", "Counter1", "Counter2", "Duration",
]
ENUM_INDEX = {s: i for i, s in enumerate(STAT_ENUM_ORDER)}

# 成对 Stat 的不变量（Min <= Max）。中位数是单调的，取整后仍应成立；不成立则 fail fast。
PAIRS = [("MinAC", "MaxAC"), ("MinMR", "MaxMR"), ("MinDC", "MaxDC"), ("MinMC", "MaxMC"),
         ("MinSC", "MaxSC")]
# 取整单位：Health 与大数值（|中位数| >= ROUND10_THRESHOLD）按 10 取整。
ROUND10_STATS = {"Health"}
ROUND10_THRESHOLD = 1000


def round_half_away_from_zero(x: float) -> int:
    """普通四舍五入（半值远离零），不用 Python 内置 round 的银行家取整。"""
    return int(x + 0.5) if x >= 0 else -int(-x + 0.5)


def fatal(msg: str, code: int = 2):
    """明确退出码：2 参数/IO/引用错误；3 推导结果不满足不变量。"""
    print(msg, file=sys.stderr)
    raise SystemExit(code)


def stat_sort_key(stat: str):
    return (0, ENUM_INDEX[stat], stat) if stat in ENUM_INDEX else (1, 0, stat)


def load_rows(path: Path, expect_rows: bool = True) -> dict:
    if not path.exists():
        fatal(f"[X] 找不到 {path}", 2)
    data = json.loads(path.read_text(encoding="utf-8"))
    if expect_rows and not isinstance(data, dict):
        fatal(f"[X] {path} 顶层应为对象（含 count/rows）", 2)
    return data


def fill_targets(monsters: dict, stats_by_mon: dict, targets: list[int], min_ref: int,
                 min_presence: float) -> tuple[list[dict], list[int]]:
    """按 R1/R2/R3 为每只怪物算出参照集与要补的 Stat。返回 (计划, 已跳过的目标)。"""
    # 有属性行的怪物 → 等级池（排除目标自身）
    level_pool: dict[int, list[int]] = defaultdict(list)
    for idx, mon in monsters.items():
        if idx in targets:
            continue
        if stats_by_mon.get(idx):
            level_pool[mon["level"]].append(idx)
    for lv in level_pool:
        level_pool[lv].sort()

    plans, skipped = [], []
    for t in sorted(targets):
        mon = monsters.get(t)
        if mon is None:
            fatal(f"[X] 目标 {t} 不在 MonsterInfo 里（现库没有这只怪）", 2)
        if stats_by_mon.get(t):
            skipped.append(t)
            continue
        lv = mon["level"]

        # R1 分级
        if lv in level_pool:
            tier, chosen_levels = 1, [lv]
        else:
            near5 = sorted((L for L in level_pool if abs(L - lv) <= 5), key=lambda L: (abs(L - lv), L))
            if near5:
                tier, chosen_levels = 2, near5
            else:
                tier = 3
                chosen_levels = []
                need = min_ref
                for L in sorted(level_pool, key=lambda L: (abs(L - lv), L)):
                    chosen_levels.append(L)
                    if sum(len(level_pool[x]) for x in chosen_levels) >= need:
                        break
        refs: list[int] = []
        for L in chosen_levels:
            refs.extend(level_pool[L])
        if not refs:
            fatal(f"[X] 目标 {t}（{mon['name']} lv{lv}）参照集为空，无法推导", 3)

        # R2 中位数 + 出现率
        per_stat_values: dict[str, list[int]] = defaultdict(list)
        for r in refs:
            for st, val in stats_by_mon[r].items():
                per_stat_values[st].append(val)

        filled, excluded = {}, {}
        for st, vals in per_stat_values.items():
            presence = len(vals) / len(refs)
            if presence + 1e-9 < min_presence:
                excluded[st] = round(presence, 4)
                continue
            filled[st] = statistics.median(vals)

        # R3 取整
        amounts, amounts_median = {}, {}
        for st, med in filled.items():
            amounts_median[st] = med
            if st in ROUND10_STATS or abs(med) >= ROUND10_THRESHOLD:
                amt = round_half_away_from_zero(med / 10.0) * 10
            else:
                amt = round_half_away_from_zero(med)
            if st == "Health" and amt <= 0:
                amt = 10          # 不变量：Health > 0
            amounts[st] = amt
        if "Health" not in amounts:
            fatal(f"[X] 目标 {t}（{mon['name']}）参照集里 Health 出现率不足，拒绝补行", 3)

        # 成对不变量
        for lo, hi in PAIRS:
            if lo in amounts and hi in amounts and amounts[lo] > amounts[hi]:
                fatal(
                    f"[X] {t}（{mon['name']}）取整后 {lo}={amounts[lo]} > {hi}={amounts[hi]}，"
                    "推导结果不满足不变量", 3)

        order = sorted(amounts, key=stat_sort_key)
        plans.append({
            "index": t,
            "name": mon["name"],
            "level": lv,
            "experience": mon.get("experience"),
            "image": mon.get("image"),
            "tier": tier,
            "tier_label": {1: "同等级", 2: "同等级 ±5", 3: "最近可用等级"}[tier],
            "ref_levels": chosen_levels,
            "ref_deltas": [L - lv for L in chosen_levels],
            "ref_max_abs_delta": max(abs(L - lv) for L in chosen_levels),
            "ref_members": [{"index": r, "name": monsters[r]["name"],
                             "level": monsters[r]["level"],
                             "stats": dict(sorted(stats_by_mon[r].items()))} for r in refs],
            "stats_median": {st: amounts_median[st] for st in order},
            "stats_amount": {st: amounts[st] for st in order},
            "excluded_stats": dict(sorted(excluded.items(), key=lambda kv: (-kv[1], kv[0]))),
        })
    return plans, skipped


def build_rows(plans: list[dict], first_index: int) -> list[dict]:
    """R4：顺序分配 Index，构造工作区行。"""
    rows, nxt = [], first_index
    for plan in plans:
        plan["rows"] = []
        for st in sorted(plan["stats_amount"], key=stat_sort_key):
            row = {
                "Index": nxt,
                "_Identity": f"{plan['name']} / {st}",
                "Monster": {"Index": plan["index"], "Name": plan["name"]},
                "Stat": st,
                "Amount": plan["stats_amount"][st],
            }
            plan["rows"].append(row)
            rows.append(row)
            nxt += 1
    return rows


def main() -> int:
    ap = argparse.ArgumentParser(description="为 0 属性行的怪物按同等级/同类怪中位数推导 MonsterInfoStat 行")
    ap.add_argument("--workspace", default=str(DEFAULT_WORKSPACE), help="dbeditor 工作区目录")
    ap.add_argument("--apply", action="store_true", help="真正写入工作区（默认 dry-run）")
    ap.add_argument("--preview", default="/tmp/zero_stat_fill_preview.json", help="dry-run 预览 JSON 路径")
    ap.add_argument("--backup-root", default="/tmp", help="--apply 时的备份父目录")
    ap.add_argument("--targets", default="", help="覆盖目标列表，逗号分隔 Index；默认审计 R2 的 15 只")
    ap.add_argument("--min-ref", type=int, default=5, help="tier3 参照集最少怪物数（默认 5）")
    ap.add_argument("--min-presence", type=float, default=0.5, help="Stat 出现率阈值（默认 0.5）")
    ap.add_argument("--warn-delta", type=int, default=30, help="参照等级离目标超过此级数则告警（默认 30）")
    args = ap.parse_args()

    ws = Path(args.workspace)
    stat_path = ws / "MonsterInfoStat.json"
    mon_path = ws / "MonsterInfo.json"
    targets = ([int(x) for x in args.targets.split(",") if x.strip()] if args.targets
               else list(DEFAULT_TARGETS))

    mon_data = load_rows(mon_path)
    stat_data = load_rows(stat_path)
    monsters = {r["Index"]: {"name": r["MonsterName"], "level": r["Level"],
                             "experience": r.get("Experience"), "image": r.get("Image")}
                for r in mon_data["rows"]}
    old_rows = stat_data["rows"]
    old_indexes = {r["Index"] for r in old_rows}
    stats_by_mon: dict[int, dict[str, int]] = defaultdict(dict)
    for r in old_rows:
        stats_by_mon[r["Monster"]["Index"]][r["Stat"]] = r["Amount"]

    plans, skipped = fill_targets(monsters, stats_by_mon, targets, args.min_ref, args.min_presence)
    first_index = max(old_indexes) + 1 if old_indexes else 1
    new_rows = build_rows(plans, first_index)

    # ---- 校验（写前） ----
    checks: dict[str, object] = {}
    checks["index_start"] = first_index
    checks["index_collision"] = sorted({r["Index"] for r in new_rows} & old_indexes)
    dupes = defaultdict(int)
    for r in old_rows + new_rows:
        dupes[(r["Monster"]["Index"], r["Stat"])] += 1
    checks["duplicate_monster_stat"] = [f"{k[0]}/{k[1]}" for k, v in dupes.items() if v > 1]
    checks["monster_ref_missing"] = [r["Monster"]["Index"] for r in new_rows
                                     if r["Monster"]["Index"] not in monsters]
    checks["name_mismatch"] = [f"{r['Monster']['Index']}:{r['Monster']['Name']}!={monsters[r['Monster']['Index']]['name']}"
                               for r in new_rows if r["Monster"]["Index"] in monsters
                               and r["Monster"]["Name"] != monsters[r["Monster"]["Index"]]["name"]]
    checks["health_not_positive"] = [r["Index"] for r in new_rows
                                     if r["Stat"] == "Health" and r["Amount"] <= 0]

    preview = {
        "meta": {
            "generated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
            "script": "tools/restore/fill_zero_stat_monsters.py",
            "workspace": str(ws),
            "mode": "apply" if args.apply else "dry-run",
            "targets": targets,
            "args": {"min_ref": args.min_ref, "min_presence": args.min_presence,
                     "warn_delta": args.warn_delta},
            "source_md5": {"MonsterInfoStat.json": hashlib.md5(stat_path.read_bytes()).hexdigest(),
                           "MonsterInfo.json": hashlib.md5(mon_path.read_bytes()).hexdigest()},
            "rules": {
                "R1_reference_tier": "tier1 同等级 → tier2 |Δ|<=5 → tier3 最近等级并入至 min-ref",
                "R2_median_and_presence": f"Stat 取参照集中位数，只补出现率 >= {args.min_presence} 的 Stat",
                "R3_rounding": "Health 与大数值(|median|>=1000)按 10 取整，其余取最近整数，半值远离零",
                "R4_index": "现有最大 Index + 1 顺序分配；Monster 引用现库 Index/Name",
                "R5_backlinks": "默认不改 MonsterInfo.json（回链由子行 Association 维护）",
                "R6_idempotent": "已有属性行的目标一律跳过",
            },
        },
        "totals": {
            "targets_requested": len(targets),
            "targets_with_plan": len(plans),
            "targets_skipped_already_filled": len(skipped),
            "new_rows": len(new_rows),
            "new_index_range": [first_index, first_index + len(new_rows) - 1] if new_rows else None,
            "rows_per_target": {str(p["index"]): len(p["rows"]) for p in plans},
            "rows_per_target_prev": {str(t): 0 for t in targets},
            "stat_multiset": {s: sum(1 for r in new_rows if r["Stat"] == s)
                              for s in sorted({r["Stat"] for r in new_rows}, key=stat_sort_key)},
            "workspace_rows_before": len(old_rows),
            "workspace_rows_after": len(old_rows) + len(new_rows),
        },
        "targets": plans,
        "skipped_targets": skipped,
        "checks": checks,
        "new_rows": new_rows,
        "warnings": [],
    }
    for p in plans:
        if p["ref_max_abs_delta"] > args.warn_delta:
            preview["warnings"].append(
                f"{p['index']} {p['name']} lv{p['level']}：参照集最近的等级是 "
                f"{p['ref_levels'][0]}（Δ{p['ref_deltas'][0]}），超过 ±{args.warn_delta} 的告警阈值"
                "—— 该怪的数值是外推，不是同档对照")

    # ---- 控制台输出 ----
    print("=" * 78)
    print(f"[i] 工作区 {ws}")
    print(f"[i] 模式 {'APPLY' if args.apply else 'DRY-RUN'}｜目标 {len(targets)} 只"
          f"｜参照规则 tier1 同等级 → tier2 ±5 级 → tier3 最近等级(至 {args.min_ref} 只)")
    print(f"[i] MonsterInfoStat.json 现有 {len(old_rows)} 行，最大 Index {max(old_indexes)}"
          f" → 新行 Index 从 {first_index} 起")
    print("-" * 78)
    for p in plans:
        print(f"[+] {p['index']:>4} {p['name']:<26} lv{p['level']:<4} tier{p['tier']} "
              f"参照 {len(p['ref_members'])} 只 @ 等级 {p['ref_levels']} (Δ{p['ref_deltas']}) "
              f"→ {len(p['rows'])} 行")
        print(f"      参照成员: " + ", ".join(
            f"{m['index']}:{m['name']}(lv{m['level']})" for m in p["ref_members"]))
        print(f"      Stat: " + ", ".join(f"{k}={v}" for k, v in p["stats_amount"].items()))
        if p["excluded_stats"]:
            print(f"      出现率不足(<{args.min_presence})未补: " + ", ".join(
                f"{k}({pct:.0%})" for k, pct in p["excluded_stats"].items()))
    for t in skipped:
        print(f"[=] {t:>4} {monsters[t]['name']:<26} 已有属性行，跳过（幂等）")
    if not plans:
        print("[=] 没有需要补行的目标。")
    print("-" * 78)
    print(f"[i] 行数汇总：{len(old_rows)} → {len(old_rows) + len(new_rows)}（新增 {len(new_rows)}）")
    print("[i] Stat 多集：" + ", ".join(f"{k}×{v}" for k, v in preview["totals"]["stat_multiset"].items()))
    for w in preview["warnings"]:
        print(f"[!] {w}")
    for k, v in checks.items():
        if k == "index_start":
            continue
        if v:
            print(f"[X] 校验失败 {k}: {v}")
            return 3
    print("[OK] 写前校验通过（无 Index 冲突 / 无重复 (Monster,Stat) / 引用与名称自洽 / Health>0）")

    if not args.apply:
        Path(args.preview).write_text(json.dumps(preview, ensure_ascii=False, indent=2),
                                     encoding="utf-8")
        print(f"[i] dry-run 预览 JSON → {args.preview}（未触碰工作区）")
        return 0

    # ---- APPLY ----
    stamp = time.strftime("%Y%m%d-%H%M%S")
    backup_dir = Path(args.backup_root) / f"dbeditor_ws_backup_stats_{stamp}"
    backup_dir.mkdir(parents=True, exist_ok=True)
    backup_file = backup_dir / "MonsterInfoStat.json"
    backup_file.write_bytes(stat_path.read_bytes())
    (backup_dir / "zero_stat_fill_preview.json").write_text(
        json.dumps(preview, ensure_ascii=False, indent=2), encoding="utf-8")
    (backup_dir / "sha256.txt").write_text(
        "MonsterInfoStat.json (before)\n"
        f"{hashlib.sha256(stat_path.read_bytes()).hexdigest()}  MonsterInfoStat.json\n"
        f"{hashlib.sha256(mon_path.read_bytes()).hexdigest()}  MonsterInfo.json\n",
        encoding="utf-8")
    print(f"[i] 已备份 → {backup_file}")

    merged = {"count": len(old_rows) + len(new_rows), "rows": old_rows + new_rows}
    tmp_out = stat_path.with_suffix(".json.tmp")
    tmp_out.write_text(json.dumps(merged, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(tmp_out, stat_path)

    # 写后校验：老行逐字节保留、新行可解析、计数一致
    back = json.loads(stat_path.read_text(encoding="utf-8"))
    ok = True
    if back["count"] != len(merged["rows"]):
        print(f"[X] count 字段不一致：{back['count']} != {len(merged['rows'])}"); ok = False
    if back["rows"][:len(old_rows)] != old_rows:
        print("[X] 原有行被改动（不是纯追加）"); ok = False
    if back["rows"][len(old_rows):] != new_rows:
        print("[X] 追加的行与计划不一致"); ok = False
    if len(back["rows"]) != len(old_rows) + len(new_rows):
        print("[X] 行数不符"); ok = False
    if ok:
        mon_after = hashlib.md5(mon_path.read_bytes()).hexdigest()
        print(f"[OK] 写后校验通过：{len(old_rows)} 行原样保留 + 追加 {len(new_rows)} 行 = "
              f"{len(back['rows'])} 行")
        print(f"[i] MonsterInfo.json 未改动（md5 {mon_after}）")
    else:
        print(f"[X] 写后校验失败；原文件备份在 {backup_file}")
        return 4

    Path(args.preview).write_text(json.dumps(preview, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[i] 预览 JSON → {args.preview}")
    print("[i] 下一步（本脚本不做）：bash Tools/dbeditor/sync.sh 才会真正写 System.db")
    return 0


if __name__ == "__main__":
    sys.exit(main())

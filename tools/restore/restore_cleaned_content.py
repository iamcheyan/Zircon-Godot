#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""restore_cleaned_content.py —— 三类「清洗回归内容」的可复核补丁脚本（默认 dry-run）。

背景（审计证据见 docs/AUDIT/monsters.md 与 docs/AUDIT/items.md）：

  组 1  MonsterInfoStat 0 行的 15 只怪物（R2）        → 生命/攻防全 0
  组 2  仍在刷怪但 DropInfo 被清空的 12 只怪物（R7）  → 击杀零产出
  组 3  物品词条回退（C1/C2）：19 件药水 Health/Mana、7 个元素护身符 *Affinity
  组 4  物品 ItemInfo.Shape 被改成 0（C4）：三瓶修理油 11/12/13、重生丹 15

本脚本 **只打印 + 写 /tmp 预览**，不碰工作区；`--apply` 才写 dbeditor 工作区
（写前把被改文件整份备份到 /tmp/dbeditor_ws_backup_<时间戳>/）。

写库链路（本脚本只做第 1 步）：
  1. 本脚本 --apply  改 dbeditor 工作区 JSON
  2. 在 82 上跑 Tools/dbeditor/sync.sh（导入器只应用「工作区 vs _baseline」的差）

不变量（脚本会自检并拒绝写）：
  * 每一行新记录的 `Index` 在该表工作区/基线里都未被占用；
  * 行内所有 ref/reflist 都指向「当前工作区里存在」的对象按 Index；
  * 物品身份一律按名字匹配（现库 vs 旧库 Index 已漂移 172 件），不按 Index 硬搬；
  * 幂等：已存在的行不会重复加（按行内容做多重集差，见 multiset_diff）。

用法：
  python3 tools/restore/restore_cleaned_content.py                 # dry-run（默认）
  python3 tools/restore/restore_cleaned_content.py --verify        # 额外在 /tmp 副本上跑导入器 + round-trip 对比
  python3 tools/restore/restore_cleaned_content.py --apply         # 写工作区（先备份）
  python3 tools/restore/restore_cleaned_content.py --rollback /tmp/dbeditor_ws_backup_<ts>

注意：本脚本不跑 sync.sh、不写 System.db、不碰 ~/mir2ei、不启停服务。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from collections import Counter, defaultdict
from pathlib import Path

# ============================================================================
# 常量区 1/4：路径
# ============================================================================

ZIRCON_ROOT = Path(os.environ.get("MIR3_ZIRCON_ROOT", "/Users/tetsuya/Development/Zircon"))
MIR3_ROOT = Path(os.environ.get("MIR3_RESEARCH_ROOT", "/Users/tetsuya/Development/Mir3-Research"))

DEFAULT_WORKSPACE = MIR3_ROOT / "Tools/dbeditor/workspace"

# 旧库基线 A（最权威：清洗前 2026-09-25 21:47 备份，§2.1 已证 = 网站基准镜像）
#   组 2（掉落）用它；组 3/4 的数值与基线 B 逐条比对必须一致，否则中止。
OLD_PRECLEAN_GZ = (ZIRCON_ROOT / "Debug/ServerCore/Database/Backup/System"
                   / "System 2026-09-25 21-47.db.gz")
DEFAULT_OLD_EXPORT = Path("/tmp/preclean_out")

# 旧库基线 B（Aug 13 21:26，ItemInfo 1078 / ItemInfoStat 3196）
OLD_BASELINE_DIR = MIR3_ROOT / "Tools/dbeditor/workspace/baseline"

# 组 1 的**候选**证据（不是数值来源，只用于报告；见 GROUP_MONSTER_STATS 注释）
MUD3_DAT = MIR3_ROOT / "docs/research/mud3-dat-decoded/monster.json"
MUD3_IDENTITY = MIR3_ROOT / "docs/research/mud3-identity/monsters.json"

DBEDITOR_DIR = MIR3_ROOT / "Tools/dbeditor"
DB_IMPORTER_PROJ = DBEDITOR_DIR / "importer/DBImporter.csproj"
COMPARE_SYNC = DBEDITOR_DIR / "compare_sync.py"
SYSTEM_DB_PROBE_PROJ = MIR3_ROOT / "Tools/SystemDbProbe/SystemDbProbe.csproj"
SYSTEM_DB = ZIRCON_ROOT / "Debug/ServerCore/Database/System.db"

DEFAULT_PREVIEW = Path("/tmp/restore_patch_preview.json")

# ============================================================================
# 常量区 2/4：组 1 —— 15 只 `MonsterInfoStat` 0 行的怪物（审计 R2）
# ============================================================================
#
# 【数值来源：无 —— 本组默认 BLOCKED，不写任何行】
#
# 审计 R2 建议「数值参照基准库同类怪与网站 attrs」。本脚本逐一核对了本机所有
# Zircon 期数据库副本，结论是**这批怪物在任何 Zircon 库里都没有过属性行**：
#
#   /tmp/preclean_out（2026-09-25 备份，309 怪 / 4117 行）→ 15 只全为 0 行
#   workspace/baseline（Aug-13，434 怪 / 4117 行）        → 15 只全为 0 行
#   mir2ei/Backup/clean_165708（清洗后 116 怪 / 1048 行） → 15 只全为 0 行
#   mir2ei/Data/Backup/dbeditor-20260813-215829          → 15 只全为 0 行
#   Development/Debug/ServerCore/Database/System.db（11.6MB 大库）→ 0 行
#
# 它们由 EI 迁移加入（Tools/DbMigrationTool/Program.cs:1136 ImportMonsters()：
# 只写 MonsterName/Image/AI/Level/Experience/... 从不写 MonsterInfoStat），
# 所以「旧库原值」对这批对象根本不存在。
#
# MUD3 DAT（docs/research/mud3-dat-decoded/monster.json）+ mud3-identity 里能给
# 13 只找到**候选**记录，但两组独立证据表明不能据此写库：
#   (a) 8 条是 mud3-identity 标注 confidence=pending、
#       「未定案，禁止据此写库」（Fierce Wind Demon→294 潘夜风魔 等）；
#   (b) 等级对不上：Fierce Wind Demon 现库 Lv105 vs DAT#294 Lv45；
#       Flame Demon Soldier 现库 Lv180 vs DAT#297 Lv45；
#       Flame Lion 现库 Lv55/Exp800000 vs DAT#9 Lv48/Exp1600。
# 用这些数会凭空造出一套与现库体量不匹配的平衡数值（正是「编译通过但逻辑全错」的坑）。
#
# 因此本组默认只报告 + 列出候选证据；要看数值请用 --monster-stats-file 显式提供
# 经人裁决的数值文件（schema 见 MONSTER_STATS_FILE_SCHEMA）。
GROUP_MONSTER_STATS = [
    # (Index, MonsterName, Image, 审计记的刷怪行数, 审计记的掉落行数)
    (388, "Fierce Falcon", "Shinsu", 4, 41),
    (390, "Fierce Wind Demon", "FierceWindDemon", 15, 54),
    (395, "Dungeon Goddess 1", "DungeonGoddess1", 0, 0),      # 0 刷怪 + 0 掉落，可能刻意不用
    (396, "Dungeon Goddess 2", "DungeonGoddess2", 15, 36),
    (409, "Otherworld Sea General", "NetherWorldGate", 21, 23),
    (412, "Otherworld Poison Demon", "VenomSpitter", 7, 24),
    (416, "Otherworld Ship Guard", "InfernalSoldier", 3, 24),
    (417, "Otherworld Ship Guard 1", "InfernalSoldier", 4, 26),
    (418, "Otherworld Red Mage", "NewMob7", 18, 24),
    (421, "Otherworld Tentacle Demon", "TaintedTerror", 7, 24),
    (425, "Otherworld Light Guard", "BloodStone", 27, 24),
    (448, "Flame Lion", "FlameLion", 15, 38),
    (449, "Flame Demon Soldier", "FlameMob", 18, 42),
    (458, "Ice Break Demon Soldier", "IceMob", 17, 48),
    (484, "Stone Lion", "RazorTusk", 12, 79),
]

MONSTER_STATS_FILE_SCHEMA = """{
  "values": [
    {"monster": "<MonsterName（现库唯一名）>", "monster_index": <Index>,
     "stats": {"Health": 12000, "MinAC": 30, "MaxAC": 40, "...": 0}}
  ]
}
来源必须是人裁决过的数值（例如上游 Zircon 库 / 原版服务端配置），并写进 docs。"""

# ============================================================================
# 常量区 3/4：组 2 —— 仍在刷怪但掉落被清空的 12 只怪物（审计 R7）
# ============================================================================
# 数值来源：旧库基线 A（2026-09-25 清洗前备份）该怪物的 DropInfo 行**全量**。
# 只有「物品仍存在于现库」的行会被写（旧库 581 件物品已被清洗移除，见报告 blocked 段）。
GROUP_MONSTER_DROPS = [
    # (Index, MonsterName)
    (10, "Deer"),                     # 鹿：Deer Meat / Crystal / Oil Of Benediction …
    (25, "Scorpion"),                 # 蝎子
    (33, "Ghost Mage"),               # 矿区幽灵系
    (34, "Voracious Ghost"),
    (35, "Devouring Ghost"),
    (36, "Corpse Raising Ghost"),
    (71, "Venomous Arachnid"),
    (89, "Numa Grunt"),               # 诺玛小卒
    (106, "Mutant Lizard"),
    (119, "Bone Soldier"),
    (120, "Skeleton Enforcer"),
    (220, "Sabuk Lord"),
]

# ============================================================================
# 常量区 4/4：组 3 / 组 4 —— 物品词条（ItemInfoStat）与 Shape（ItemInfo）
# ============================================================================
# 词条数值来源：旧库基线 B（Aug-13）的 ItemInfoStat；脚本会同时比对基线 A，
# 二者必须逐条一致，否则中止。下表把期望值也写死，作为**独立于来源**的断言：
# 从旧库读到的值必须等于这里的期望值，不等就打日志并算 BLOCKED（防来源漂移）。
GROUP_ITEM_STATS = [
    # (ItemName, [(Stat, Amount), ...], 分组说明)
    # --- C1：19 件经典药水丢 Health/Mana ---
    ("Healing Potion", [("Health", 30)], "C1 药水"),
    ("Healing Potion (II)", [("Health", 70)], "C1 药水"),
    ("Healing Potion (III)", [("Health", 110)], "C1 药水"),
    ("Healing Potion (IV)", [("Health", 170)], "C1 药水"),
    ("Healing Potion (V)", [("Health", 250)], "C1 药水"),
    ("Life Pill", [("Health", 30)], "C1 药水"),
    ("Life Pill (II)", [("Health", 70)], "C1 药水"),
    ("Life Pill (III)", [("Health", 110)], "C1 药水"),
    ("Life Pill (IV)", [("Health", 170)], "C1 药水"),
    ("Life Pill (V)", [("Health", 250)], "C1 药水"),
    ("Mana Potion", [("Mana", 40)], "C1 药水"),
    ("Mana Potion (II)", [("Mana", 110)], "C1 药水"),
    ("Mana Potion (III)", [("Mana", 180)], "C1 药水"),
    ("Mana Potion (IV)", [("Mana", 250)], "C1 药水"),
    ("Mana Pill", [("Mana", 40)], "C1 药水"),
    ("Mana Pill (II)", [("Mana", 110)], "C1 药水"),
    ("Mana Pill (III)", [("Mana", 180)], "C1 药水"),
    ("Mana Pill (IV)", [("Mana", 250)], "C1 药水"),
    ("Rejuvenation Potion (II)", [("Health", 110), ("Mana", 180)], "C1 药水"),
    # --- C2：7 个元素护身符丢 *Affinity ---
    ("Talisman Of Darkness", [("DarkAffinity", 1)], "C2 护身符"),
    ("Talisman Of Fire", [("FireAffinity", 1)], "C2 护身符"),
    ("Talisman Of Holiness", [("HolyAffinity", 1)], "C2 护身符"),
    ("Talisman Of Ice", [("IceAffinity", 1)], "C2 护身符"),
    ("Talisman Of Illusions", [("PhantomAffinity", 1)], "C2 护身符"),
    ("Talisman Of Lightning", [("LightningAffinity", 1)], "C2 护身符"),
    ("Talisman Of Wind Storm", [("WindAffinity", 1)], "C2 护身符"),
]

# 明确**不在**任务三类的 C3（明亮蜡烛/火把丢 Light 25）；要补需显式 --include-light
EXTRA_ITEM_STATS_LIGHT = [
    ("Bright Candle", [("Light", 25)], "C3 照明（默认不写）"),
    ("Bright Torch", [("Light", 25)], "C3 照明（默认不写）"),
]
# 重生丹 ItemReviveTime 300：重生丹在组 4 里要改回 Shape=15，而复活分支同时依赖两者
# （ServerLibrary/Models/PlayerObject.cs 的 case 15 分支读 Stats[Stat.ItemReviveTime]），
# 只改 Shape 是半修。默认不写（严格守住三类范围），要补需显式 --include-revive-time。
EXTRA_ITEM_STATS_REVIVE = [
    ("Pill Of Reincarnation", [("ItemReviveTime", 300)], "C4 配套（默认不写）"),
]

GROUP_ITEM_SHAPE = [
    # (ItemName, 期望的旧 Shape) —— C4：现库被改成 0
    ("Superior Repair Oil", 11),      # 修武器油
    ("Accessory Repair Oil", 12),     # 修首饰油
    ("Armour Repair Oil", 13),        # 修防具油
    ("Pill Of Reincarnation", 15),    # 重生丹
]

# 本脚本涉及的 3 张表（新增行的表）
TABLES_TO_LOAD = ["MonsterInfo", "ItemInfo", "MonsterInfoStat", "ItemInfoStat", "DropInfo"]


# ============================================================================
# 通用工具
# ============================================================================

def norm_name(s: str) -> str:
    """名称归一化：仅保留 [a-z0-9]（审计 items.md §2 的 normalized_name 规则）。"""
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def md5_of(path: Path) -> str:
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_table(dirpath: Path, table: str):
    """读 probe/editor 导出的 {count, rows} JSON。返回 (rows, count)。"""
    p = Path(dirpath) / f"{table}.json"
    if not p.exists():
        raise SystemExit(f"[X] 缺少表文件：{p}")
    doc = json.loads(p.read_text(encoding="utf-8"))
    if isinstance(doc, list):
        return doc, len(doc)
    return doc["rows"], doc.get("count", len(doc["rows"]))


def by_index(rows):
    return {r["Index"]: r for r in rows}


class Blocked(Exception):
    pass


# ============================================================================
# 当前工作区（= 现库）视角
# ============================================================================

class CurrentState:
    def __init__(self, ws_dir: Path):
        self.ws_dir = ws_dir
        self.rows = {}
        self.counts = {}
        for t in TABLES_TO_LOAD:
            rows, cnt = load_table(ws_dir, t)
            self.rows[t] = rows
            self.counts[t] = cnt
        # 名称索引（现库）
        self.monsters_by_name = defaultdict(list)
        for r in self.rows["MonsterInfo"]:
            self.monsters_by_name[r["MonsterName"]].append(r)
        self.items_exact = {}
        self.items_norm = defaultdict(list)
        for r in self.rows["ItemInfo"]:
            self.items_exact.setdefault(r["ItemName"], []).append(r)
            self.items_norm[norm_name(r["ItemName"])].append(r)

    def monster(self, name: str):
        """按名字取唯一怪物行（同名多行 → Blocked）。"""
        cands = self.monsters_by_name.get(name, [])
        if len(cands) == 0:
            raise Blocked(f"现库无此怪物名：{name!r}")
        if len(cands) > 1:
            raise Blocked(f"现库同名怪物 {len(cands)} 行，身份不唯一：{name!r}"
                          f" → Index {[c['Index'] for c in cands]}")
        return cands[0]

    def item(self, name: str):
        """按名字取唯一物品行：先精确、再归一化；多候选 → Blocked。"""
        exact = self.items_exact.get(name, [])
        if len(exact) == 1:
            return exact[0], "exact"
        if len(exact) > 1:
            raise Blocked(f"现库同名物品 {len(exact)} 行：{name!r}")
        nrm = self.items_norm.get(norm_name(name), [])
        if len(nrm) == 1:
            return nrm[0], "normalized"
        if len(nrm) > 1:
            raise Blocked(f"归一化后 {len(nrm)} 个候选：{name!r} → "
                          f"{[c['ItemName'] for c in nrm]}")
        raise Blocked(f"现库无此物品（精确/归一化都没命中）：{name!r}")

    def used_indexes(self, table: str, baseline_dir: Path):
        used = set()
        for r in self.rows[table]:
            used.add(r["Index"])
        if baseline_dir and (Path(baseline_dir) / f"{table}.json").exists():
            rows, _ = load_table(baseline_dir, table)
            used.update(r["Index"] for r in rows)
        return used

    def rows_of(self, table: str, field: str, index: int):
        return [r for r in self.rows[table]
                if isinstance(r.get(field), dict) and r[field].get("Index") == index]


# ============================================================================
# 旧库来源
# ============================================================================

def ensure_old_export(old_export: Path, prepare: bool):
    """确认旧库基线 A 的导出存在；--prepare-old 时从 .gz 备份生成（只写 /tmp）。"""
    need = ["MonsterInfo", "ItemInfo", "DropInfo"]
    if all((old_export / f"{t}.json").exists() for t in need):
        return
    if not prepare:
        raise SystemExit(
            f"[X] 旧库导出不完整：{old_export}\n"
            f"    请先准备（只写 /tmp，不碰仓库 System.db）：\n"
            f"      python3 {sys.argv[0]} --prepare-old\n"
            f"    或手动：\n"
            f"      mkdir -p {old_export} && gzcat '{OLD_PRECLEAN_GZ}' > {old_export}/System.db\n"
            f"      MIR3_ZIRCON_ROOT={ZIRCON_ROOT} dotnet run --project {SYSTEM_DB_PROBE_PROJ} "
            f"--no-build -- --json {old_export}/ {old_export}/")
    if not OLD_PRECLEAN_GZ.exists():
        raise SystemExit(f"[X] 找不到清洗前备份：{OLD_PRECLEAN_GZ}")
    old_export.mkdir(parents=True, exist_ok=True)
    db = old_export / "System.db"
    print(f"[prepare] 解压备份 → {db}")
    with open(db, "wb") as out:
        subprocess.run(["gzcat", str(OLD_PRECLEAN_GZ)], stdout=out, check=True)
    env = dict(os.environ, MIR3_ZIRCON_ROOT=str(ZIRCON_ROOT))
    print("[prepare] 运行 SystemDbProbe 导出 JSON（只读）")
    subprocess.run(["dotnet", "build", str(SYSTEM_DB_PROBE_PROJ), "-v", "q"], check=True,
                   env=env, cwd=str(MIR3_ROOT))
    subprocess.run(["dotnet", "run", "--project", str(SYSTEM_DB_PROBE_PROJ), "--no-build",
                    "--", "--json", str(old_export) + "/", str(old_export) + "/"],
                   check=True, env=env, cwd=str(MIR3_ROOT))


class OldState:
    """旧库两个基线，以及（仅作证据的）MUD3 DAT / mud3-identity。"""

    def __init__(self, old_export: Path, baseline_dir: Path):
        self.drops, _ = load_table(old_export, "DropInfo")
        self.monsters, _ = load_table(old_export, "MonsterInfo")
        self.items, _ = load_table(old_export, "ItemInfo")
        self.item_name_by_index = {r["Index"]: r["ItemName"] for r in self.items}
        self.monster_ids_by_name = defaultdict(list)
        for r in self.monsters:
            self.monster_ids_by_name[r["MonsterName"]].append(r["Index"])

        # 基线 B（Aug-13）里的词条 / 物品
        self.b_item_stats, _ = load_table(baseline_dir, "ItemInfoStat")
        self.b_items, _ = load_table(baseline_dir, "ItemInfo")
        self.b_item_by_name = {r["ItemName"]: r for r in self.b_items}
        self.b_stats_by_item = defaultdict(list)
        for r in self.b_item_stats:
            if isinstance(r.get("Item"), dict):
                self.b_stats_by_item[r["Item"]["Name"]].append(r)

        # 交叉核对：基线 A 的词条也必须一致
        a_stats, _ = load_table(old_export, "ItemInfoStat")
        self.a_stats_by_item = defaultdict(list)
        for r in a_stats:
            if isinstance(r.get("Item"), dict):
                self.a_stats_by_item[r["Item"]["Name"]].append(r)

    def old_drop_rows_for_monster(self, name: str):
        ids = set(self.monster_ids_by_name.get(name, []))
        return [r for r in self.drops
                if isinstance(r.get("Monster"), dict) and r["Monster"]["Index"] in ids], ids

    def expected_stats(self, item_name: str):
        """基线 B 里该物品的词条 (Stat, Amount) 列表，按 Stat 排序返回。"""
        out = []
        for r in self.b_stats_by_item.get(item_name, []):
            out.append((r["Stat"], r["Amount"]))
        return sorted(out)

    def baseline_a_stats(self, item_name: str):
        return sorted((r["Stat"], r["Amount"]) for r in self.a_stats_by_item.get(item_name, []))

    def old_shape(self, item_name: str):
        r = self.b_item_by_name.get(item_name)
        return None if r is None else r.get("Shape")

    def mud3_candidates(self):
        """{现库怪物名: [候选证据 dict]}，仅用于报告组 1（绝不写库）。"""
        out = defaultdict(list)
        if not (MUD3_IDENTITY.exists() and MUD3_DAT.exists()):
            return out
        ident = json.loads(MUD3_IDENTITY.read_text(encoding="utf-8"))
        dat_rows = {r["Index"]: r for r in json.loads(
            MUD3_DAT.read_text(encoding="utf-8"))["records"]}
        for e in ident.get("entries", []):
            if e.get("kind") != "monster":
                continue
            rec = dat_rows.get(e.get("mud3_index"))
            out[e.get("zircon_en")].append({
                "mud3_index": e.get("mud3_index"),
                "mud3_name": e.get("mud3_name"),
                "confidence": e.get("confidence"),
                "evidence": e.get("evidence"),
                "mud3_values": None if not rec else {
                    k: rec.get(k) for k in
                    ("Level", "Exp", "HP", "ACMin", "ACMax", "MAC", "DCMin", "DCMax", "MC", "Appr")},
            })
        return out


# ============================================================================
# 组 1：怪物属性行
# ============================================================================

def build_monster_stats(cur: CurrentState, monster_stats_file: Path | None):
    """返回 (to_add, blocked, notes)。默认 BLOCKED：无权威来源。"""
    to_add, blocked, notes = [], [], []
    provided = {}
    if monster_stats_file:
        doc = json.loads(Path(monster_stats_file).read_text(encoding="utf-8"))
        for v in doc.get("values", []):
            provided[v["monster"]] = v
        notes.append(f"使用 --monster-stats-file {monster_stats_file}（{len(provided)} 条人工裁决值）")

    used = cur.used_indexes("MonsterInfoStat", None)
    next_index = (max(used) + 1) if used else 1

    for idx, name, image, respawn_hint, drop_hint in GROUP_MONSTER_STATS:
        try:
            mon = cur.monster(name)
        except Blocked as e:
            blocked.append({"group": "monster_stats", "monster": name,
                            "monster_index": idx, "reason": str(e)})
            continue
        existing = cur.rows_of("MonsterInfoStat", "Monster", mon["Index"])
        if existing:
            notes.append(f"{name}({mon['Index']}) 已有 {len(existing)} 行属性 → 跳过")
            continue
        respawns = len(cur.rows_of("RespawnInfo", "Monster", mon["Index"])) \
            if "RespawnInfo" in cur.rows else None
        entry = {"group": "monster_stats", "monster": name, "monster_index": mon["Index"],
                 "image": image,
                 "level": mon.get("Level"), "experience": mon.get("Experience"),
                 "spawned": respawns if respawns is not None else respawn_hint}
        sup = provided.get(name)
        if not sup:
            entry.update({"reason": "无权威数值来源（旧库各版本均为 0 行；MUD3 DAT 候选与现库等级/体量不符）",
                          "status": "BLOCKED"})
            blocked.append(entry)
            continue
        if sup.get("monster_index") not in (None, mon["Index"]):
            blocked.append({**entry, "reason": f"提供的 monster_index={sup['monster_index']} "
                                                 f"与现库 {mon['Index']} 不符"})
            continue
        rows = []
        for stat, amount in sup["stats"].items():
            rows.append({"Index": next_index, "_Identity": f"{name} / {stat}",
                         "Monster": {"Index": mon["Index"], "Name": name},
                         "Stat": stat, "Amount": int(amount)})
            next_index += 1
        to_add.extend(rows)
        entry["rows"] = len(rows)
        notes.append(f"{name}({mon['Index']}) 采用外部裁决值 {len(rows)} 行")
    return to_add, blocked, notes


# ============================================================================
# 组 2：怪物掉落
# ============================================================================

def multiset_diff(wanted, existing_counter):
    """按内容多重集做差：返回 (待新增, 已存在计数)。幂等的核心。"""
    remain = Counter(existing_counter)
    add, already = [], 0
    for key, row in wanted:
        if remain[key] > 0:
            remain[key] -= 1
            already += 1
        else:
            add.append(row)
    return add, already


def drop_key(row):
    return json.dumps([row["Monster"]["Index"], row["Item"]["Index"], row["Chance"],
                       row["Amount"], row["DropSet"], row["PartOnly"], row["EasterEvent"]],
                      sort_keys=True)


def build_monster_drops(cur: CurrentState, old: OldState):
    to_add, blocked, notes, per_monster = [], [], [], []
    used = cur.used_indexes("DropInfo", None)
    next_index = (max(used) + 1) if used else 1

    for idx, name in GROUP_MONSTER_DROPS:
        try:
            mon = cur.monster(name)
        except Blocked as e:
            blocked.append({"group": "monster_drops", "monster": name,
                            "monster_index": idx, "reason": str(e)})
            continue
        old_rows, old_ids = old.old_drop_rows_for_monster(name)
        if not old_rows:
            blocked.append({"group": "monster_drops", "monster": name, "monster_index": idx,
                            "reason": f"旧库（基线 A）无掉落行（旧怪物 Index={sorted(old_ids)}）"})
            continue
        wanted, m_blocked = [], []
        for r in sorted(old_rows, key=lambda x: x["Index"]):
            old_item = r.get("Item")
            if not isinstance(old_item, dict):
                m_blocked.append({"old_drop_index": r["Index"], "item": None,
                                  "reason": "旧行 Item 为空引用"})
                continue
            old_name = old.item_name_by_index.get(old_item["Index"], old_item.get("Name"))
            try:
                item, how = cur.item(old_name)
            except Blocked as e:
                m_blocked.append({"old_drop_index": r["Index"], "item": old_name,
                                  "reason": str(e), "old_values":
                                  {"Chance": r["Chance"], "Amount": r["Amount"],
                                   "DropSet": r["DropSet"], "PartOnly": r["PartOnly"],
                                   "EasterEvent": r["EasterEvent"]}})
                continue
            row = {"Index": None, "_Identity": f"{name} / {item['ItemName']}",
                   "Monster": {"Index": mon["Index"], "Name": name},
                   "Item": {"Index": item["Index"], "Name": item["ItemName"]},
                   "Chance": r["Chance"], "Amount": r["Amount"], "DropSet": r["DropSet"],
                   "PartOnly": r["PartOnly"], "EasterEvent": r["EasterEvent"]}
            wanted.append((drop_key(row), row, how))
        existing = Counter(drop_key(r) for r in cur.rows["DropInfo"]
                           if isinstance(r.get("Monster"), dict)
                           and r["Monster"]["Index"] == mon["Index"])
        add, already = multiset_diff([(k, r) for k, r, _ in wanted], existing)
        for row in add:
            row["Index"] = next_index
            next_index += 1
            to_add.append(row)
        per_monster.append({
            "monster": name, "monster_index": mon["Index"],
            "old_rows": len(old_rows), "to_add": len(add), "already_present": already,
            "blocked_rows": len(m_blocked),
            "normalized_matches": sum(1 for _, _, how in wanted if how == "normalized"),
            "current_drop_rows": len(existing),
        })
        if m_blocked:
            notes.append(f"{name}: {len(m_blocked)} 行受阻（物品已被清洗移除等）")
        blocked.extend([{**b, "group": "monster_drops", "monster": name,
                         "monster_index": mon["Index"]} for b in m_blocked])
    return to_add, blocked, notes, per_monster


# ============================================================================
# 组 3：物品词条
# ============================================================================

def build_item_stats(cur: CurrentState, old: OldState, include_light: bool,
                     include_revive: bool):
    to_add, blocked, notes, per_item = [], [], [], []
    groups = list(GROUP_ITEM_STATS)
    if include_light:
        groups += list(EXTRA_ITEM_STATS_LIGHT)
    if include_revive:
        groups += list(EXTRA_ITEM_STATS_REVIVE)
    used = cur.used_indexes("ItemInfoStat", None)
    next_index = (max(used) + 1) if used else 1

    for name, expect, tag in groups:
        try:
            item, how = cur.item(name)
        except Blocked as e:
            blocked.append({"group": "item_stats", "item": name, "stat": None,
                            "reason": str(e)})
            continue
        want = sorted(expect)
        src_a = old.baseline_a_stats(name)
        src_b = old.expected_stats(name)
        if not src_b:
            blocked.append({"group": "item_stats", "item": name, "item_index": item["Index"],
                            "reason": "旧库基线 B 无该物品词条"})
            continue
        if src_b != src_a:
            blocked.append({"group": "item_stats", "item": name, "item_index": item["Index"],
                            "reason": f"两个旧库基线不一致 A={src_a} B={src_b}"})
            continue
        if src_b != want:
            blocked.append({"group": "item_stats", "item": name, "item_index": item["Index"],
                            "reason": f"旧库值 {src_b} != 常量表期望 {want}（来源漂移，拒绝写）"})
            continue
        existing = Counter()
        for r in cur.rows_of("ItemInfoStat", "Item", item["Index"]):
            existing[json.dumps([r["Stat"], r["Amount"]], sort_keys=True)] += 1
        already = 0
        for stat, amount in want:
            key = json.dumps([stat, amount], sort_keys=True)
            if existing[key] > 0:
                existing[key] -= 1
                already += 1
                continue
            to_add.append({"Index": next_index, "_Identity": f"{item['ItemName']} / {stat}",
                           "Item": {"Index": item["Index"], "Name": item["ItemName"]},
                           "Stat": stat, "Amount": amount})
            next_index += 1
        per_item.append({"item": name, "item_index": item["Index"], "match": how,
                         "tag": tag, "stats": want, "to_add": len(want) - already,
                         "already_present": already})
    return to_add, blocked, notes, per_item


# ============================================================================
# 组 4：物品 Shape
# ============================================================================

def build_item_shape(cur: CurrentState, old: OldState):
    mods, blocked, notes = [], [], []
    for name, old_shape in GROUP_ITEM_SHAPE:
        try:
            item, how = cur.item(name)
        except Blocked as e:
            blocked.append({"group": "item_shape", "item": name, "reason": str(e)})
            continue
        src = old.old_shape(name)
        if src != old_shape:
            blocked.append({"group": "item_shape", "item": name, "item_index": item["Index"],
                            "reason": f"旧库基线 B Shape={src} != 期望 {old_shape}"})
            continue
        cur_shape = item.get("Shape")
        if cur_shape == old_shape:
            notes.append(f"{name}({item['Index']}) Shape 已是 {old_shape} → 跳过")
            continue
        if cur_shape != 0:
            blocked.append({"group": "item_shape", "item": name, "item_index": item["Index"],
                            "reason": f"现库 Shape={cur_shape} 非 0（不是清洗回归），拒绝覆盖"})
            continue
        mods.append({"table": "ItemInfo", "Index": item["Index"], "item": name,
                     "kind": "shape", "field": "Shape",
                     "old": cur_shape, "new": old_shape})
    return mods, blocked, notes


# --- 派生回链（reflist）同步 -------------------------------------------------
# 为什么必须做：ItemInfo.Drops / ItemInfo.ItemStats / MonsterInfo.Drops /
# MonsterInfo.MonsterInfoStats 都是「派生回链」（子行带 ref 指回父行），导入器写库时
# 会跳过这些字段（由子行 setter 自动维护）；但 sync.sh 的 round-trip 对比
# （compare_sync.py:129 对 reflist_derived 字段也按 Index 集合比对）会拿「工作区」
# 与「重导出」逐行比。工作区若不补这些回链，sync 会在第 4/7 步整体失败。
# 名字用子行 _Identity 的约定（Probe 的 reflist 元素 Name 就是子行 _Identity）。

def build_backlink_updates(cur: CurrentState, additions):
    parents = defaultdict(list)
    for r in additions.get("DropInfo", []):
        parents[("MonsterInfo", "Drops", r["Monster"]["Index"])].append(
            {"Index": r["Index"], "Name": r["_Identity"]})
        parents[("ItemInfo", "Drops", r["Item"]["Index"])].append(
            {"Index": r["Index"], "Name": r["_Identity"]})
    for r in additions.get("ItemInfoStat", []):
        parents[("ItemInfo", "ItemStats", r["Item"]["Index"])].append(
            {"Index": r["Index"], "Name": r["_Identity"]})
    for r in additions.get("MonsterInfoStat", []):
        parents[("MonsterInfo", "MonsterInfoStats", r["Monster"]["Index"])].append(
            {"Index": r["Index"], "Name": r["_Identity"]})

    updates = []
    idx_maps = {t: by_index(cur.rows[t]) for t in {k[0] for k in parents}}
    for (table, field, index), new_elems in sorted(parents.items()):
        row = idx_maps[table][index]
        cur_val = row.get(field)
        new_val = list(cur_val) if isinstance(cur_val, list) else []
        new_val.extend(sorted(new_elems, key=lambda x: x["Index"]))
        updates.append({
            "table": table, "Index": index, "kind": "backlink", "field": field,
            "name": row.get("MonsterName") or row.get("ItemName"),
            "old_count": len(cur_val) if isinstance(cur_val, list) else 0,
            "new_count": len(new_val), "new": new_val,
        })
    return updates


# ============================================================================
# 写工作区
# ============================================================================

def append_rows(rows, new_rows):
    """把新行追加到 rows（按 Index 排序追加），返回新列表与更新后的 count。"""
    out = list(rows)
    out.extend(sorted(new_rows, key=lambda r: r["Index"]))
    return out, len(out)


def write_tables(ws_dir: Path, additions, modifications):
    """additions: {table: [rows]}；modifications: [mod dict]。返回被改文件名列表。"""
    changed = []
    for table, new_rows in additions.items():
        path = Path(ws_dir) / f"{table}.json"
        doc = json.loads(path.read_text(encoding="utf-8"))
        rows = doc["rows"] if isinstance(doc, dict) else doc
        merged, count = append_rows(rows, new_rows)
        doc = {"count": count, "rows": merged}
        path.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")
        changed.append(path.name)
    if modifications:
        by_table = defaultdict(list)
        for m in modifications:
            by_table[m["table"]].append(m)
        for table, mods in by_table.items():
            path = Path(ws_dir) / f"{table}.json"
            doc = json.loads(path.read_text(encoding="utf-8"))
            rows = doc["rows"] if isinstance(doc, dict) else doc
            idx = by_index(rows)
            for m in mods:
                idx[m["Index"]][m["field"]] = m["new"]
            path.write_text(json.dumps({"count": len(rows), "rows": rows},
                                       ensure_ascii=False, indent=2), encoding="utf-8")
            if path.name not in changed:
                changed.append(path.name)
    return changed


def backup_workspace(ws_dir: Path, stamp: str, files):
    bdir = Path(f"/tmp/dbeditor_ws_backup_{stamp}")
    bdir.mkdir(parents=True, exist_ok=True)
    manifest = {}
    for fn in files:
        src = Path(ws_dir) / fn
        if src.exists():
            shutil.copy2(src, bdir / fn)
            manifest[fn] = {"md5": md5_of(src), "bytes": src.stat().st_size}
    (bdir / "manifest.json").write_text(
        json.dumps({"workspace": str(ws_dir), "created": stamp, "files": manifest},
                   ensure_ascii=False, indent=2), encoding="utf-8")
    return bdir


def rollback(backup_dir: Path, ws_dir: Path):
    man = json.loads((backup_dir / "manifest.json").read_text(encoding="utf-8"))
    for fn, meta in man["files"].items():
        src = backup_dir / fn
        dst = Path(ws_dir) / fn
        shutil.copy2(src, dst)
        ok = md5_of(dst) == meta["md5"]
        print(f"[rollback] {fn} ← {src}  md5={'OK' if ok else 'MISMATCH'}")
    print(f"[rollback] 完成（工作区 {ws_dir}，来自 {backup_dir}）")


# ============================================================================
# --verify：在 /tmp 副本上跑一遍真实写库链路（不碰真库/真工作区）
# ============================================================================

def verify(cur_ws: Path, additions, modifications, stamp: str, src_db_path: Path = None):
    print("\n== --verify：/tmp 副本上跑 DBImporter + round-trip（不碰真库/真工作区）==")
    src_db_path = Path(src_db_path or SYSTEM_DB)
    tmp_root = Path(tempfile.mkdtemp(prefix=f"/tmp/restore_verify_{stamp}_"))
    ws = tmp_root / "workspace"
    shutil.copytree(cur_ws, ws)
    if not src_db_path.exists():
        print(f"[verify] 跳过：找不到 {src_db_path}")
        return 3
    src_db = tmp_root / "System.db"
    shutil.copy2(src_db_path, src_db)        # 只读复制，绝不写原库
    print(f"[verify] 源库副本：{src_db_path}")
    changed = write_tables(ws, additions, modifications)
    print(f"[verify] 副本工作区已打补丁：{changed}")
    env = dict(os.environ, MIR3_ZIRCON_ROOT=str(ZIRCON_ROOT))
    subprocess.run(["dotnet", "build", str(DB_IMPORTER_PROJ), "-v", "q"], check=True,
                   env=env, cwd=str(MIR3_ROOT))
    report = tmp_root / "importer_report.txt"
    p = subprocess.run(["dotnet", "run", "--project", str(DB_IMPORTER_PROJ), "--no-build",
                        "--", "--workspace", str(ws), "--src", str(src_db),
                        "--report", str(report), "--no-port-check"],
                       env=env, cwd=str(MIR3_ROOT), capture_output=True, text=True)
    print(p.stdout[-4000:] if p.stdout else "")
    if p.returncode != 0:
        print(f"[verify] ✗ 导入器退出码 {p.returncode}（{report}）")
        return 1
    db_out = [l[len("DB_OUT="):] for l in
              (report.read_text(encoding="utf-8") if report.exists() else "").splitlines()
              if l.startswith("DB_OUT=")]
    if not db_out:
        print(f"[verify] 跳过 round-trip：报告里没有 DB_OUT（{report}）")
        return 0
    rt_dir = tmp_root / "out"
    subprocess.run(["dotnet", "run", "--project", str(SYSTEM_DB_PROBE_PROJ), "--no-build",
                    "--", "--json", str(rt_dir) + "/", str(Path(db_out[-1]).parent) + "/"],
                   check=True, env=env, cwd=str(MIR3_ROOT))
    c = subprocess.run([sys.executable, str(COMPARE_SYNC), str(ws), str(rt_dir), str(report)],
                       capture_output=True, text=True)
    print(c.stdout or c.stderr)
    print(f"[verify] {'✓ round-trip 一致' if c.returncode == 0 else '✗ round-trip 有差异'}"
          f"（exit={c.returncode}，全部产物在 {tmp_root}）")
    return c.returncode


# ============================================================================
# 主流程
# ============================================================================

def main():
    ap = argparse.ArgumentParser(
        description="三类清洗回归内容的可复核补丁（默认 dry-run，不写工作区）",
        formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--workspace", type=Path, default=DEFAULT_WORKSPACE,
                    help=f"dbeditor 工作区（默认 {DEFAULT_WORKSPACE}）")
    ap.add_argument("--old-export", type=Path, default=DEFAULT_OLD_EXPORT,
                    help=f"旧库基线 A 导出目录（默认 {DEFAULT_OLD_EXPORT}）")
    ap.add_argument("--old-baseline", type=Path, default=OLD_BASELINE_DIR,
                    help=f"旧库基线 B 目录（默认 {OLD_BASELINE_DIR}）")
    ap.add_argument("--prepare-old", action="store_true",
                    help="旧库导出缺失时，从 .gz 备份生成（只写 /tmp）")
    ap.add_argument("--apply", action="store_true", help="写工作区（默认只 dry-run）")
    ap.add_argument("--out", type=Path, default=DEFAULT_PREVIEW,
                    help=f"预览 JSON 输出路径（默认 {DEFAULT_PREVIEW}）")
    ap.add_argument("--verify", action="store_true",
                    help="在 /tmp 副本上跑 DBImporter + round-trip 对比")
    ap.add_argument("--verify-db", type=Path, default=None,
                    help=f"--verify 用的源 System.db（默认 {SYSTEM_DB}）；"
                         "要复现历史结论可指向补丁前的库副本")
    ap.add_argument("--rollback", type=Path, help="从备份目录回滚工作区，然后退出")
    ap.add_argument("--include-light", action="store_true",
                    help="额外补组 3 之外的 C3：明亮蜡烛/火把 Light 25")
    ap.add_argument("--include-revive-time", action="store_true",
                    help="额外补重生丹 ItemReviveTime 300（C4 配套，默认不写）")
    ap.add_argument("--monster-stats-file", type=Path, default=None,
                    help="组 1 的人工裁决数值文件（默认不写组 1）")
    args = ap.parse_args()

    stamp = time.strftime("%Y%m%d-%H%M%S")

    if args.rollback:
        rollback(args.rollback, args.workspace)
        return 0

    ensure_old_export(args.old_export, args.prepare_old)

    cur = CurrentState(args.workspace)
    old = OldState(args.old_export, args.old_baseline)
    # 需要 RespawnInfo 只用于组 1 的报告
    try:
        cur.rows["RespawnInfo"], _ = load_table(args.workspace, "RespawnInfo")
    except SystemExit:
        pass

    add_monster_stats, blocked1, notes1 = build_monster_stats(cur, args.monster_stats_file)
    add_drops, blocked2, notes2, per_monster = build_monster_drops(cur, old)
    add_item_stats, blocked3, notes3, per_item = build_item_stats(
        cur, old, args.include_light, args.include_revive_time)
    mods_shape, blocked4, notes4 = build_item_shape(cur, old)

    additions = {}
    if add_monster_stats:
        additions["MonsterInfoStat"] = add_monster_stats
    if add_item_stats:
        additions.setdefault("ItemInfoStat", []).extend(add_item_stats)
    if add_drops:
        additions["DropInfo"] = add_drops

    mods_backlink = build_backlink_updates(cur, additions)
    modifications = mods_shape + mods_backlink

    blocked = blocked1 + blocked2 + blocked3 + blocked4
    notes = notes1 + notes2 + notes3 + notes4

    # ---- 自检：引用完整性 + Index 唯一 ----
    problems = []
    for table, rows in additions.items():
        seen = set(cur.used_indexes(table, args.old_baseline))
        for r in rows:
            if r["Index"] in seen:
                problems.append(f"{table}#{r['Index']} Index 重复")
            seen.add(r["Index"])
    for r in additions.get("DropInfo", []):
        if r["Monster"]["Index"] not in {m["Index"] for m in cur.rows["MonsterInfo"]}:
            problems.append(f"DropInfo#{r['Index']} Monster 悬空")
        if r["Item"]["Index"] not in {i["Index"] for i in cur.rows["ItemInfo"]}:
            problems.append(f"DropInfo#{r['Index']} Item 悬空")
    for field, target_table in (("Monster", "MonsterInfo"), ("Item", "ItemInfo")):
        valid = {x["Index"] for x in cur.rows[target_table]}
        for table in ("MonsterInfoStat", "ItemInfoStat"):
            for r in additions.get(table, []):
                if field in r and r[field]["Index"] not in valid:
                    problems.append(f"{table}#{r['Index']}.{field} 悬空引用")
    if problems:
        print("[X] 自检失败，拒绝继续：")
        for p in problems[:20]:
            print("   ", p)
        return 2

    # ---- 索引分配摘要 ----
    alloc = {}
    for table, rows in additions.items():
        if rows:
            idx = sorted(r["Index"] for r in rows)
            alloc[table] = {"count": len(idx), "from": idx[0], "to": idx[-1]}

    # ---- 打印摘要 ----
    print("=" * 78)
    print(f"组 1  怪物属性行 MonsterInfoStat")
    print(f"      目标对象 {len(GROUP_MONSTER_STATS)}；可写 {len(add_monster_stats)} 行；"
          f"BLOCKED {len(blocked1)} 个对象")
    if blocked1:
        print("      ★ 数值来源不存在（旧库各版本均 0 行；MUD3 DAT 候选未定案/等级不符）"
              " → 默认不写，见报告 unresolved_class_1")
    print(f"组 2  怪物掉落 DropInfo")
    print(f"      目标对象 {len(GROUP_MONSTER_DROPS)}；将新增 {len(add_drops)} 行；"
          f"受阻 {len(blocked2)} 行")
    for m in per_monster:
        print(f"        {m['monster_index']:>4} {m['monster']:<22} 旧 {m['old_rows']:>3} → "
              f"新增 {m['to_add']:>3}，已存在 {m['already_present']}，受阻 {m['blocked_rows']:>3}"
              f"（现库原行 {m['current_drop_rows']}）")
    print(f"组 3  物品词条 ItemInfoStat")
    print(f"      目标对象 {len(per_item)}；将新增 {len(add_item_stats)} 行；受阻 {len(blocked3)} 条")
    for it in per_item:
        if it["to_add"]:
            print(f"        {it['item_index']:>4} {it['item']:<26} {it['tag']:<8} "
                  f"{it['stats']}  ×{it['to_add']}"
                  + ("  [由归一化名匹配]" if it["match"] == "normalized" else ""))
    print(f"组 4  物品 Shape ItemInfo")
    print(f"      将修改 {len(mods_shape)} 行；受阻 {len(blocked4)} 条")
    for m in mods_shape:
        print(f"        {m['Index']:>4} {m['item']:<26} Shape {m['old']} → {m['new']}")
    print(f"      派生回链同步：{len(mods_backlink)} 行"
          f"（{len({(m['table'], m['field']) for m in mods_backlink})} 个字段）"
          f"—— 不做会卡在 sync.sh 第 4/7 步的 round-trip 对比")
    print("-" * 78)
    print(f"合计：新增 {sum(len(v) for v in additions.values())} 行，"
          f"修改 {len(modifications)} 行，受阻 {len(blocked)} 条")
    print(f"索引分配：{json.dumps(alloc, ensure_ascii=False)}")
    if notes:
        print("备注：")
        for n in notes[:20]:
            print("   -", n)
    print("=" * 78)

    # ---- 预览 JSON ----
    preview = {
        "generated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "script": str(Path(__file__).resolve().relative_to(ZIRCON_ROOT))
        if str(Path(__file__).resolve()).startswith(str(ZIRCON_ROOT))
        else str(Path(__file__).resolve()),
        "mode": "apply" if args.apply else "dry-run",
        "workspace": str(args.workspace),
        "sources": {
            "old_export_preclean": {"path": str(args.old_export),
                                    "DropInfo_md5": md5_of(args.old_export / "DropInfo.json")
                                    if (args.old_export / "DropInfo.json").exists() else None},
            "old_baseline_aug13": {"path": str(args.old_baseline),
                                   "ItemInfoStat_md5":
                                       md5_of(args.old_baseline / "ItemInfoStat.json")
                                   if (args.old_baseline / "ItemInfoStat.json").exists() else None},
            "mud3_dat": str(MUD3_DAT) if MUD3_DAT.exists() else None,
            "mud3_identity": str(MUD3_IDENTITY) if MUD3_IDENTITY.exists() else None,
        },
        "summary": {
            "monster_stats": {"targets": len(GROUP_MONSTER_STATS),
                              "rows_to_add": len(add_monster_stats),
                              "blocked_objects": len(blocked1)},
            "monster_drops": {"targets": len(GROUP_MONSTER_DROPS),
                              "old_rows": sum(m["old_rows"] for m in per_monster),
                              "rows_to_add": len(add_drops),
                              "already_present": sum(m["already_present"] for m in per_monster),
                              "blocked_rows": len(blocked2), "per_monster": per_monster},
            "item_stats": {"targets": len(per_item), "rows_to_add": len(add_item_stats),
                           "per_item": per_item,
                           "include_light": args.include_light,
                           "include_revive_time": args.include_revive_time},
            "item_shape": {"rows_to_modify": len(mods_shape), "mods": mods_shape},
            "backlinks": {"rows_to_modify": len(mods_backlink), "mods": mods_backlink},
            "index_allocation": alloc,
            "totals": {"rows_to_add": sum(len(v) for v in additions.values()),
                       "rows_to_modify": len(modifications),
                       "blocked": len(blocked)},
        },
        "rows_to_add": additions,
        "rows_to_modify": {"ItemInfo": [m for m in modifications if m["table"] == "ItemInfo"],
                           "MonsterInfo": [m for m in modifications
                                           if m["table"] == "MonsterInfo"]},
        "blocked": blocked,
        "notes": notes,
        "unresolved_class_1": unresolved_report(cur, old),
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(preview, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"预览已写：{args.out}")

    if args.verify:
        rc = verify(args.workspace, additions, modifications, stamp, args.verify_db)
        if rc != 0:
            print("[verify] 未通过，已停止（未改工作区）")
            return rc

    if not args.apply:
        print("dry-run：未改工作区。加 --apply 才会写入。")
        return 0

    if not additions and not modifications:
        print("[apply] 无改动（幂等：目标内容已在工作区里），不写文件。")
        return 0

    files = sorted({f"{t}.json" for t in additions} | {f"{m['table']}.json" for m in modifications})
    bdir = backup_workspace(args.workspace, stamp, files)
    print(f"[apply] 备份 → {bdir}")
    changed = write_tables(args.workspace, additions, modifications)
    print(f"[apply] 已写入：{changed}")
    print(f"[apply] 下一步（在 82 上）：bash {DBEDITOR_DIR}/sync.sh")
    return 0


def unresolved_report(cur: CurrentState, old: OldState):
    """组 1 的候选证据（只报告，不写库）。"""
    cands = old.mud3_candidates()
    out = []
    for idx, name, image, _r, _d in GROUP_MONSTER_STATS:
        try:
            mon = cur.monster(name)
            have = len(cur.rows_of("MonsterInfoStat", "Monster", mon["Index"]))
        except Blocked as e:
            out.append({"monster": name, "monster_index": idx, "error": str(e)})
            continue
        out.append({
            "monster": name, "monster_index": mon["Index"], "image": image,
            "level": mon.get("Level"), "experience": mon.get("Experience"),
            "already_has_stats": have,
            "status": "BLOCKED_NO_SOURCE",
            "candidates": cands.get(name, []),
        })
    return out


if __name__ == "__main__":
    sys.exit(main())

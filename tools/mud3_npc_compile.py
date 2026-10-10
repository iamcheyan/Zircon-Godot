#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mud3 NPC 脚本 → Zircon NPC 对话框 编译器（解析 + IR 生成）

输入（只读）：
  - 原版 Mud3 脚本树（GBK）：<MUD3_ROOT>/{Market_Def,QuestDiary,Convert_Def,...}
  - Merchant.txt：NPC → 脚本文件的权威摆放表（按 地图+坐标 精确匹配）
  - tools/npc_audit_manifest.json：现役 230 个活动 NPC（index/name/mapFile/x/y/category/services）
  - tools/npc_shop_goods.json：商店物品清单（可缺）
  - /tmp/npc_graph.json：现役 DB 导出（物品名/地图名/现役商店商品与类型）

输出：IR JSON（由 Tools/ClassicMagicFixer 的 applynpcir 模式落库）

编译语义（与 Zircon 服务端 NPCObject.CheckPage/DoActions 对齐）：
  - 逻辑标签的多个 #IF 组 → 多页，条件失败依次落到下一组（等价 Mud3 顺序 #IF）；
  - checkpkpoint/checklevel/checkgold/checkjob/checkitem → NPCCheck（支持 `!` 取反）；
  - 无法求值的条件（check [flag]/checkmagic/checkhum...）按“假”处理（新号默认状态）；
  - #ACT goto → 路由页（Say 空 + SuccessPage）；#CALL → 跨文件调用（goto 可回落调用者文件，
    与原版 GSP 的全局标签/返回语义一致）；
  - mapmove → Teleport；take/give 金币/物品 → TakeGold/GiveGold/TakeItem/GiveItem；
  - 文本 <文字/@cmd(args)> → [文字:id] + NPCButton；@exit → id 0；@buy/@sell/@repair/@storage
    等功能链接 → 对应 DialogType 功能页（文本仍取自原脚本）；
  - 服务能力（manifest.services）缺失的功能入口按 Zircon 机制补按钮（原版由客户端提供买卖/修理界面）；
  - 无法落地的链接一律从文本剔除并计入 warnings（**保证无死链**）。
"""
from __future__ import annotations

import collections
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
MUD3_ROOT = os.environ.get(
    "MUD3_ROOT",
    "/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir3")
GRAPH = os.environ.get("NPC_GRAPH", "/tmp/npc_graph.json")

FUNC_LINKS = {
    "buy": "buy", "sell": "sell", "repair": "repair", "pre_repair": "repair",
    "special_repair": "repair", "srepairfunc": "repair",
    "storage": "storage", "pregetback": "storage", "getback": "storage",
    "buy_portalscroll": "portalscroll",
}
# 服务能力 → Zircon 机制
SVC_SHOP_HINTS = ("买卖", "商店", "出售", "购买")
SVC_SELL_HINTS = ("收购", "回收", "悬赏")
SVC_REPAIR_HINT = "修理"
SVC_STORAGE_HINTS = ("仓库", "存取")
SVC_COMPANION_HINTS = ("随从", "灵兽", "宠物")
# moverootin.txt 原表缺少 (Sabuk,Center) 项，但同文件 [@CasTleWarMove_Sabuk] 给出进城坐标；
# 道馆/蛇谷六面神石的菜单里有「移动至沙巴克城」选项，缺这条会让该选项消失。
EXTRA_TELEPORT = {("sabuk", "center"): ("3", 222, 160)}
ENTRY_ALIAS = "#entry"
MAX_DEPTH = 22
MAX_PAGES = 400


def read_gbk(path: str) -> str:
    with open(path, "rb") as f:
        return f.read().decode("cp936", errors="replace")


# --------------------------------------------------------------------------------------
class ScriptRepo:
    def __init__(self, root: str):
        self.root = root
        self.by_base = collections.defaultdict(list)
        for dirpath, _dirs, files in os.walk(root):
            for fn in files:
                if fn.lower().endswith(".txt"):
                    self.by_base[fn.lower()].append(os.path.join(dirpath, fn))
        self._logic = {}
        self._text = {}

    def resolve(self, ref: str, want_convert: bool = False) -> str | None:
        """按 basename + 路径线索（ref 的目录分量）解析，避免同名文件误选。"""
        norm = ref.replace("\\", "/")
        parts = [p.lower() for p in norm.split("/") if p and p not in (".", "..")]
        base = parts[-1] if parts else norm.lower()
        dirs = parts[:-1]
        cands = self.by_base.get(base)
        if not cands:
            return None
        target_convert = ("convert_def" in dirs) or want_convert

        def score(path: str) -> tuple:
            pd = [p for p in path.lower().replace("\\", "/").split("/") if p]
            dirs_pd = pd[:-1]
            exact = sum(1 for d in dirs_pd if d in dirs)
            last = max((i for i, p in enumerate(dirs_pd) if p in dirs), default=-1)
            extra = (len(dirs_pd) - 1 - last) if last >= 0 else 99
            convert_ok = ("convert_def" in path.lower()) == target_convert
            return (-exact, extra, 0 if convert_ok else 1, path)

        return min(cands, key=score)

    def logic(self, path: str):
        if path not in self._logic:
            self._logic[path] = parse_logic(read_gbk(path))
        return self._logic[path]

    def text(self, path: str):
        if path not in self._text:
            self._text[path] = parse_text(read_gbk(path))
        return self._text[path]

    def scripts_for(self, script: str, map_code: str):
        logic = None
        for cand in (f"{script}-{map_code}.txt", f"{script}.txt"):
            p = self.resolve(cand)
            if p and "convert_def" not in p.lower():
                logic = p
                break
        if logic is None:
            for base, paths in self.by_base.items():
                if base.startswith(script.lower() + "-") and "convert_def" not in paths[0].lower():
                    logic = sorted(paths)[0]
                    break
        text = self.resolve(os.path.basename(logic), want_convert=True) if logic else None
        return logic, text


# --------------------------------------------------------------------------------------
RE_LABEL = re.compile(r"^\[~?@(\S+?)\]\s*$")
RE_INCLUDE = re.compile(r"#INCLUDE\s*\[\s*([^\]]+?)\s*\]\s*@?([\w]*)", re.I)
RE_CALL = re.compile(r"#CALL\s*\[\s*([^\]]+?)\s*\]\s*@?([\w]*)", re.I)


class Group:
    __slots__ = ("conds", "says", "acts", "else_says", "else_acts")

    def __init__(self):
        self.conds: list[str] = []
        self.says: list[tuple] = []
        self.acts: list[tuple] = []
        self.else_says: list[tuple] = []
        self.else_acts: list[tuple] = []


class Block:
    __slots__ = ("label", "groups", "index")

    def __init__(self, label, index):
        self.label = label
        self.groups: list[Group] = []
        self.index = index


def parse_logic(text: str) -> dict[str, Block]:
    blocks: dict[str, Block] = {}
    cur: Block | None = None
    grp: Group | None = None
    mode = None
    counter = 0

    def new_group():
        nonlocal grp
        grp = Group()
        if cur is not None:
            cur.groups.append(grp)

    for raw in text.splitlines():
        s = raw.strip()
        if not s or s.startswith(";") or s in ("{", "}"):
            continue
        m = RE_LABEL.match(s)
        if m:
            label = m.group(1)
            if label not in blocks:
                counter += 1
                cur = Block(label, counter)
                blocks[label] = cur
            else:
                cur = blocks[label]
            grp = None
            mode = None
            continue
        low = s.lower()
        if low.startswith("#if"):
            new_group()
            mode = "cond"
            rest = s[3:].strip()
            if rest and grp is not None:
                grp.conds.append(rest)
            continue
        if low.startswith("#call"):
            m = RE_CALL.search(s)
            if m:
                if grp is None:
                    new_group()
                grp.acts.append(("call", m.group(1), m.group(2)))
            continue
        if low.startswith("#include"):
            m = RE_INCLUDE.search(s)
            if m:
                if grp is None:
                    new_group()
                grp.says.append(("inc", m.group(1), m.group(2)))
            continue
        if low.startswith("#say"):
            if grp is None:
                new_group()
            mode = "say"
            continue
        if low.startswith("#elseact"):
            if grp is None:
                new_group()
            mode = "else_act"
            continue
        if low.startswith("#elsesay"):
            if grp is None:
                new_group()
            mode = "else_say"
            continue
        if low.startswith("#act"):
            if grp is None:
                new_group()
            mode = "act"
            continue
        if low.startswith("#define") or low.startswith("#elseif"):
            continue
        if low.startswith("#call"):
            m = RE_CALL.search(s)
            if m and grp is not None:
                grp.acts.append(("call", m.group(1), m.group(2)))
            continue
        if low.startswith("#include"):
            m = RE_INCLUDE.search(s)
            if m and grp is not None:
                grp.says.append(("inc", m.group(1), m.group(2)))
            continue
        if cur is None:
            continue
        if grp is None:
            new_group()
        if mode == "cond":
            grp.conds.append(s)
        elif mode == "say":
            m = RE_INCLUDE.search(s)
            if m:
                grp.says.append(("inc", m.group(1), m.group(2)))
            elif grp.says and grp.says[-1][0] == "raw":
                grp.says[-1] = ("raw", grp.says[-1][1] + "\n" + s)
            else:
                grp.says.append(("raw", s))
        elif mode == "act":
            grp.acts.append(("cmd", s))
        elif mode == "else_say":
            m = RE_INCLUDE.search(s)
            if m:
                grp.else_says.append(("inc", m.group(1), m.group(2)))
            elif grp.else_says and grp.else_says[-1][0] == "raw":
                grp.else_says[-1] = ("raw", grp.else_says[-1][1] + "\n" + s)
            else:
                grp.else_says.append(("raw", s))
        elif mode == "else_act":
            grp.else_acts.append(("cmd", s))
    return blocks


def parse_text(text: str) -> dict[str, str]:
    out: dict[str, str] = {}
    label = None
    buf: list[str] = []
    for raw in text.splitlines():
        s = raw.strip()
        if not s or s.startswith(";;"):
            continue
        m = RE_LABEL.match(s)
        if m:
            if label is not None and label not in out:
                out[label] = "\n".join(buf).strip("\n")
            label = m.group(1)
            buf = []
            continue
        if s in ("{", "}"):
            continue
        if label is not None:
            buf.append(raw)
    if label is not None and label not in out:
        out[label] = "\n".join(buf).strip("\n")
    return out


RE_LINK = re.compile(r"<([^<>]*?)/?\s*@([\w]+)(?:\(([^)<>]*)\))?\s*>", re.S)


def unescape_mud3(text: str) -> str:
    out: list[str] = []
    for line in text.split("\n"):
        line = line.rstrip()
        m = re.search(r"((?:\\[ \t]*)+)$", line)
        if m:
            n = m.group(1).count("\\")
            out.append(line[: m.start()].rstrip())
            out.extend([""] * max(1, n - 1))
        else:
            out.append(line)
    res = "\n".join(out)
    return re.sub(r"\n{3,}", "\n\n", res).strip("\n")


# --------------------------------------------------------------------------------------
class Compiler:
    def __init__(self, repo: ScriptRepo, npc: dict, ctx: dict):
        self.repo = repo
        self.npc = npc
        self.ctx = ctx
        self.pages: dict[str, dict] = {}
        self.order: list[str] = []
        self.warnings: list[str] = []
        self.entry: str | None = None
        self.logic: str | None = None
        self.text_path: str | None = None
        self.callstack: list[str] = []
        self.busy: set[str] = set()

    def warn(self, msg: str):
        m = msg.split(":")[0]
        if m not in self.warnings:
            self.warnings.append(m)

    def page(self, key: str) -> dict:
        p = self.pages.get(key)
        if p is None:
            p = {"key": key, "type": "None", "say": "", "buttons": [], "checks": [],
                 "actions": [], "goods": [], "types": [], "success": None, "currency": "",
                 "entry": False}
            self.pages[key] = p
            self.order.append(key)
        return p

    def visible(self, key: str, seen=None) -> bool:
        """页（含 success 链）是否有可显示内容。"""
        seen = seen or set()
        while key and key in self.pages and key not in seen:
            seen.add(key)
            p = self.pages[key]
            if p["say"] or p["actions"] or p["buttons"] or p["checks"] or p["goods"] or p["types"]:
                return True
            key = p["success"]
        return False

    # ---------- 主流程 ----------
    def compile(self) -> dict:
        script = self.npc.get("mud3File") or ""
        logic = text = None
        if script:
            logic, text = self.repo.scripts_for(script, self.npc["mapFile"])
        self.logic, self.text_path = logic, text

        if logic is not None:
            blocks = self.repo.logic(logic)
            labels = [b.label for b in sorted(blocks.values(), key=lambda b: b.index)]
            ordered = ([l for l in ("main", "main_0_0") if l in labels]
                       + [l for l in labels if "main" in l.lower() and l not in ("main", "main_0_0")]
                       + [l for l in labels if l not in ("main", "main_0_0") and "main" not in l.lower()])
            for label in ordered:
                key = self.node(logic, label, 1, None)
                if key and key in self.pages and self.visible(key):
                    # 入口页必须让**普通玩家**（无 PK/等级/物品/旗标）也能看到内容，
                    # 否则点开就是空对话框（服务端沿 SuccessPage 走完就关闭）。
                    if self.simulate(key) is not None:
                        self.entry = key
                        break
        # 传送文本入口 / 服务能力入口（无脚本或脚本无可用文本时）
        if self.entry is None and self.npc.get("category") == "传送":
            self.entry = self.make_teleport_entry()
        if self.entry is None and self.capabilities():
            self.entry = self.make_service_entry()
        if self.entry is None:
            # 无任何原版内容可依（私服新增 NPC）：给最小应答页，保证点击必有响应且无死链
            self.warn("no_source_content")
            key = "fallback:entry"
            fp = self.page(key)
            fp["say"] = "这里暂时没有可以交涉的事务。\n\n[离开:0]"
            self.entry = key
        if self.entry is None:
            self.warn("no_entry_page")
        else:
            self.page(self.entry)["entry"] = True
            self.inject_services()
        # 把创建时未知的“返回入口”占位符解析成真正的入口 key
        for pg in self.pages.values():
            for b in pg["buttons"]:
                if b["dest"] == ENTRY_ALIAS:
                    b["dest"] = self.entry
            for c in pg["checks"]:
                if c.get("fail") == ENTRY_ALIAS:
                    c["fail"] = self.entry
            if pg["success"] == ENTRY_ALIAS:
                pg["success"] = self.entry
        return {"index": self.npc["index"], "name": self.npc["name"],
                "mapFile": self.npc["mapFile"], "x": self.npc["x"], "y": self.npc["y"],
                "source": os.path.relpath(logic, self.repo.root) if logic else None,
                "entry": self.entry, "pages": self.final_pages(), "warnings": self.warnings}
    TELE_TEXT_BY_MAP = {
        "0": "BiChonTele.txt", "01": "KugKyungTele.txt", "02": "EunHangTele.txt",
        "1": "DoKwanTele.txt", "2": "SnakeVallyTele.txt", "3": "SabukTele.txt",
        "4": "OasisTele.txt", "5": "SamakTele.txt", "74": "MongChonTele.txt",
        "41": "Numa.txt", "8": "VanyaTele.txt", "12": "VanyaTele.txt",
        "9": "VanyaTele.txt",
    }

    def make_teleport_entry(self) -> str | None:
        """无 13Move 脚本的六面神石：用该城原版传送文本生成目的地菜单。"""
        stem = self.TELE_TEXT_BY_MAP.get(self.npc["mapFile"], "BiChonTele.txt")
        if self.npc["mapFile"] not in self.TELE_TEXT_BY_MAP:
            self.warn(f"teleport_fallback_text:{self.npc['mapFile']}")
        path = self.repo.resolve(f"Convert_Def/QuestDiary/Teleport/{stem}", want_convert=True)
        if path is None:
            self.warn(f"teleport_text_missing:{stem}")
            return None
        blocks = self.repo.text(path)
        best = None
        for label, txt in blocks.items():
            n = txt.count("TelePortRootin")
            if n and (best is None or n > best[1]):
                best = (label, n, txt)
        if best is None:
            self.warn(f"teleport_text_no_links:{stem}")
            return None
        key = "tp:entry"
        p = self.page(key)
        p["say"] = (self.render(best[2], key, 2) + "\n\n[离开:0]").strip()
        if not p["buttons"]:
            self.warn("teleport_entry_no_buttons")
            return None
        return key

    def final_pages(self) -> list[dict]:
        if self.entry is None:
            return []

        def dead(k: str, seen=None) -> bool:
            """对**任何**玩家都只是空对话框的页 → 视为死链，摘掉指向它的按钮。"""
            seen = seen or set()
            if k is None or k not in self.pages or k in seen:
                return True
            seen.add(k)
            p = self.pages[k]
            if p["say"] or p["actions"] or p["buttons"] or p["goods"] or p["types"]:
                return False
            branches = [c["fail"] for c in p["checks"]] if p["checks"] else [p["success"]]
            branches = [b for b in branches if b] or [None]
            return all(dead(b, seen) for b in branches)

        def strip_dead_links(pg):
            """按钮被摘掉后，Say 里对应的 [文字:id] 必须一起摘掉，否则留下点不动的死链。"""
            keep = {b["id"] for b in pg["buttons"]}
            def repl(m):
                return "" if int(m.group(2)) not in keep and int(m.group(2)) != 0 else m.group(0)
            pg["say"] = re.sub(r"\[([^\[\]]*?):(-?\d+)\]", repl, pg["say"])
            pg["say"] = re.sub(r"[ \t]+\n", "\n", pg["say"])
            pg["say"] = re.sub(r"\n{3,}", "\n\n", pg["say"]).strip()

        for _ in range(4):
            for p in self.pages.values():
                before = len(p["buttons"])
                p["buttons"] = [b for b in p["buttons"] if not dead(b["dest"])]
                if len(p["buttons"]) != before:
                    strip_dead_links(p)
                for c in p["checks"]:
                    if c.get("fail") and dead(c["fail"]):
                        c["fail"] = None
                if p["success"] and dead(p["success"]):
                    p["success"] = None

        seen = set()
        stack = [self.entry]
        while stack:
            k = stack.pop()
            if k in seen or k not in self.pages:
                continue
            seen.add(k)
            p = self.pages[k]
            for b in p["buttons"]:
                stack.append(b["dest"])
            if p["success"]:
                stack.append(p["success"])
            for c in p["checks"]:
                if c.get("fail"):
                    stack.append(c["fail"])
        for k in list(seen):
            self.pages[k]["buttons"] = [b for b in self.pages[k]["buttons"] if b["dest"] in seen]
            for c in self.pages[k]["checks"]:
                if c.get("fail") and c["fail"] not in seen:
                    c["fail"] = None
            if self.pages[k]["success"] and self.pages[k]["success"] not in seen:
                self.pages[k]["success"] = None
        return [self.pages[k] for k in self.order if k in seen]

    # ---------- 逻辑节点 ----------
    def node(self, path: str, label: str, depth: int, variant: str | None) -> str | None:
        base = f"{os.path.splitext(os.path.basename(path))[0]}:{label}"
        key = base if not variant else f"{base}#{variant}"
        if key in self.busy or depth > MAX_DEPTH or len(self.pages) > MAX_PAGES:
            self.warn("depth_limit" if depth > MAX_DEPTH else "cycle")
            return key if key in self.pages else None
        blocks = self.repo.logic(path)
        if label not in blocks:
            self.warn(f"unresolved_label:{label}")
            return None

        block = blocks[label]
        self.busy.add(key)
        self.callstack.append(path)
        try:
            gkeys, supported, unsupported, fails = [], [], [], []
            for i, grp in enumerate(block.groups):
                gkey = key if i == 0 else f"{key}#{i}"
                gkeys.append(gkey)
                gp = self.page(gkey)
                chks = []
                bad = False
                for cond in grp.conds:
                    chk = self.map_cond(cond)
                    if chk is None:
                        bad = True
                    else:
                        chks.append(chk)
                supported.append(chks)
                unsupported.append(bad)
                gp["checks"].extend(chks)
                use_else = bad and not chks
                says = grp.else_says if use_else else grp.says
                acts = [] if use_else else grp.acts
                for item in says:
                    if item[0] == "raw":
                        txt, lbl = item[1], "inline"
                    else:
                        txt, lbl = self.text_block(item[1], item[2]), item[2]
                    if txt is None:
                        self.warn(f"missing_text:{lbl}")
                        continue
                    gp["say"] = (gp["say"] + "\n" + self.render(txt, gkey, depth + 1)).strip("\n")
                for act in acts:
                    self.apply_act(gp, act, gkey, depth)
                fails.append("__next__")
                if (chks or use_else) and (grp.else_acts or grp.else_says):
                    ekey = f"{key}#else{i}"
                    ep = self.page(ekey)
                    for item in grp.else_says:
                        txt = item[1] if item[0] == "raw" else self.text_block(item[1], item[2])
                        if txt:
                            ep["say"] = (ep["say"] + "\n" + self.render(txt, ekey, depth + 1)).strip("\n")
                    for act in grp.else_acts:
                        self.apply_act(ep, act, ekey, depth)
                    fails[-1] = ekey
            for i, gkey in enumerate(gkeys):
                target = fails[i]
                if target == "__next__":
                    target = gkeys[i + 1] if i + 1 < len(gkeys) else None
                for c in supported[i]:
                    c["fail"] = target
                page = self.page(gkey)
                if unsupported[i] and not supported[i] and not page["say"]:
                    page["success"] = target
                elif (not page["say"] and not page["actions"] and not page["buttons"]
                      and page["success"] is None):
                    # 无条件且无内容的组 = 纯路由（#IF 空 + #ACT goto 之外的写法）
                    page["success"] = target

            # 兜底：整块条件都不可求值（脚本变量/旗标）且没有 else 分支时，
            # 直接把第一个有原文的组当作默认分支显示，而不是让整棵树变空。
            if True:
                hp = self.page(key)
                if (not hp["say"] and not hp["actions"] and not hp["buttons"]
                        and self.simulate(key) is None):
                    for grp in block.groups:
                        for item in grp.says:
                            txt = item[1] if item[0] == "raw" else self.text_block(item[1], item[2])
                            if txt:
                                hp["say"] = (hp["say"] + "\n" + self.render(txt, key, depth + 1)).strip("\n")
                        if hp["say"]:
                            for act in grp.acts:
                                if act[0] == "cmd" and act[1].lower().startswith("goto"):
                                    continue
                                self.apply_act(hp, act, key, depth)
                            break
        finally:
            self.callstack.pop()
            self.busy.discard(key)
        return key

    def map_cond(self, cond: str) -> dict | None:
        parts = cond.split()
        if not parts:
            return None
        name = parts[0].lower()
        negate = name.startswith("!")
        if negate:
            name = name[1:]
        a = parts[1:]
        try:
            if name == "checkpkpoint":
                v = int(a[0]) if a else 2
                return {"type": "PKPoints", "op": "LessThan" if negate else "GreaterThanOrEqual", "i1": v}
            if name == "checklevel":
                v = int(a[0])
                return {"type": "Level", "op": "LessThan" if negate else "GreaterThanOrEqual", "i1": v}
            if name == "checkgold":
                v = int(a[0])
                return {"type": "Gold", "op": "LessThan" if negate else "GreaterThanOrEqual", "i1": v}
            if name == "checkjob":
                v = int(a[0])
                return {"type": "Class", "op": "NotEqual" if negate else "Equal", "i1": v}
            if name in ("checkitem", "checkitemw") and a:
                item = self.resolve_item(a[0])
                if item:
                    return {"type": "HasItem", "op": "LessThan" if negate else "GreaterThanOrEqual",
                            "item": item, "i1": int(a[1]) if len(a) > 1 and a[1].isdigit() else 1}
                self.warn("cond_item_unresolved")
                return None
        except (ValueError, IndexError):
            return None
        self.warn(f"unsupported_cond:{name}")
        return None

    def apply_act(self, page: dict, act: tuple, page_key: str, depth: int):
        if act[0] == "call":
            _, ref, label = act
            p = self.repo.resolve(ref)
            if p is None:
                self.warn(f"call_unresolved:{os.path.basename(ref)}")
                return
            tgt = self.node(p, label or "main", depth + 1, None)
            if tgt:
                if page["say"]:
                    page["success"] = tgt
                else:
                    page["success"] = tgt
            return
        cmd = act[1]
        parts = cmd.split()
        if not parts:
            return
        op = parts[0].lower()
        a = parts[1:]
        if op == "goto":
            lbl = a[0].lstrip("@") if a else ""
            tgt = self.resolve_goto(lbl, depth)
            if tgt:
                page["success"] = tgt
            else:
                self.warn(f"goto_unresolved:{lbl}")
        elif op == "break":
            return
        elif op == "mapmove":
            if len(a) >= 3 and a[0] in self.ctx["maps"]:
                page["actions"].append({"type": "Teleport", "map": a[0], "x": int(a[1]), "y": int(a[2])})
            else:
                self.warn("mapmove_invalid")
        elif op in ("take", "give") and a and a[0] == "金币":
            amt = a[-1].replace("%D0{FARE}", "0")
            if amt.isdigit():
                page["actions"].append({"type": "TakeGold" if op == "take" else "GiveGold", "i1": int(amt)})
            else:
                self.warn(f"{op}gold_var")
        elif op in ("take", "give"):
            item = self.resolve_item(a[0]) if a else None
            cnt = int(a[1]) if len(a) > 1 and a[1].isdigit() else 1
            if item:
                page["actions"].append({"type": "TakeItem" if op == "take" else "GiveItem",
                                        "item": item, "i1": cnt})
            else:
                self.warn(f"{op}_item_unresolved:{a[0] if a else '?'}")
        else:
            self.warn(f"unsupported_cmd:{op}")

    def resolve_goto(self, label: str, depth: int) -> str | None:
        """goto 先在当前文件找标签，再回落到调用链上的文件（GSP 全局标签/返回语义）。"""
        for path in reversed(self.callstack or [self.logic]):
            if path and label in self.repo.logic(path):
                return self.node(path, label, depth + 1, None)
        return None

    # ---------- 渲染 ----------
    def text_block(self, ref: str | None, label: str) -> str | None:
        if ref:
            path = self.repo.resolve(ref, want_convert=True)
        else:
            path = self._text_for_current()
        if path is None:
            return None
        return self.repo.text(path).get(label)

    def _text_for_current(self) -> str | None:
        cur = self.callstack[-1] if self.callstack else self.logic
        if cur and "convert_def" in cur.lower():
            return cur
        if cur and cur == self.logic and self.text_path:
            return self.text_path
        if cur is None:
            return None
        return self.repo.resolve(os.path.basename(cur), want_convert=True)

    def render(self, raw: str, page_key: str, depth: int) -> str:
        page = self.page(page_key)
        text = unescape_mud3(raw)
        used = {b["id"] for b in page["buttons"]}
        nxt = [1]

        def repl(m):
            # 链接文字里不能出现 []/: —— 它们会与 [文字:ID] 语法冲突（客户端解析会截错）
            label_text = m.group(1).strip().replace("[", "（").replace("]", "）").replace(":", "：")
            cmd = m.group(2)
            args = (m.group(3) or "").strip()
            if cmd.lower() in ("exit", "close"):
                return f"[{label_text}:0]"
            dest = self.resolve_link(cmd, args, depth)
            if dest is None:
                self.warn(f"dropped_link:{cmd.lower()}:{args[:24]}")
                return ""
            while nxt[0] in used:
                nxt[0] += 1
            rid = nxt[0]
            nxt[0] += 1
            used.add(rid)
            page["buttons"].append({"id": rid, "dest": dest})
            return f"[{label_text}:{rid}]"

        text = RE_LINK.sub(repl, text)
        text = re.sub(r"^[ \t]*_+", "", text, flags=re.M)     # Mud3 文本行首的 "_" 是对齐标记
        text = re.sub(r"[ \t]+\n", "\n", text)
        return re.sub(r"\n{3,}", "\n\n", text).strip()

    def resolve_link(self, cmd: str, args: str, depth: int) -> str | None:
        low = cmd.lower()
        kind = FUNC_LINKS.get(low)
        if low.startswith("teleportrootin"):
            return self.teleport_page(args)
        if low in ("exit", "close"):
            return None
        chain = list(reversed(self.callstack or [self.logic]))
        if self.logic and self.logic not in chain:
            chain.append(self.logic)
        for path in chain:
            if path and cmd in self.repo.logic(path):
                key = self.node(path, cmd, depth + 1, f"func:{kind}" if kind else None)
                if key and kind:
                    self.apply_functionality(key, kind)
                return key
        if kind:
            return self.func_page(kind, args)
        cur = self.callstack[-1] if self.callstack else self.logic
        if cur:
            tp = self.repo.resolve(os.path.basename(cur), want_convert=True)
            if tp and cmd in self.repo.text(tp):
                key = f"text:{cmd}"
                p = self.page(key)
                p["say"] = self.render(self.repo.text(tp)[cmd], key, depth + 1)
                return key
        return None

    def apply_functionality(self, key: str, kind: str):
        """把 func:<kind> 的功能语义（DialogType/商品/动作）落到已编译的原版文本页上。"""
        p = self.pages.get(key)
        if p is None:
            return
        if kind == "buy":
            p["type"] = "BuySell"
            p["goods"] = self.shop_goods()
        elif kind == "sell":
            p["type"] = "BuySell"
            p["types"] = self.sell_types()
        elif kind == "portalscroll":
            p["type"] = "BuySell"
            p["goods"] = [{"item": "Town Portal Scroll", "price": 0}]
        elif kind == "repair":
            p["type"] = "Repair"
            p["types"] = self.repair_types()
        elif kind == "storage":
            if not any(a["type"] == "Storage" for a in p["actions"]):
                p["actions"].append({"type": "Storage"})
        if not p["say"] and not p["actions"] and not p["goods"] and not p["types"]:
            p["say"] = "[返回:1]\n[离开:0]"
        if not p["buttons"] and "say" in p and p["say"]:
            p["buttons"] = [{"id": 1, "dest": ENTRY_ALIAS}]

    def func_page(self, kind: str, args: str) -> str | None:
        key = f"func:{kind}"
        p = self.page(key)
        if kind == "storage":
            p["actions"] = [{"type": "Storage"}]
        elif kind == "buy":
            p["type"] = "BuySell"
            p["goods"] = self.shop_goods()
        elif kind == "sell":
            p["type"] = "BuySell"
            p["types"] = self.sell_types()
        elif kind == "portalscroll":
            p["type"] = "BuySell"
            p["goods"] = [{"item": "Town Portal Scroll", "price": 0}]
        elif kind == "repair":
            p["type"] = "Repair"
            p["types"] = self.repair_types()
        if not p["say"]:
            p["say"] = "[返回:1]\n[离开:0]"
            p["buttons"] = [{"id": 1, "dest": ENTRY_ALIAS}]
        return key

    def teleport_page(self, args: str) -> str | None:
        parts = [x.strip() for x in args.split(",")]
        if len(parts) < 2:
            self.warn("teleport_args")
            return None
        dest, price = parts[0], parts[1]
        pos = parts[2] if len(parts) > 2 else "Center"
        loc = (self.ctx["moverootin"].get((dest.lower(), pos.lower()))
               or self.ctx["moverootin"].get((dest.lower(), "center")))
        if loc is None:
            loc = EXTRA_TELEPORT.get((dest.lower(), pos.lower())) \
                or EXTRA_TELEPORT.get((dest.lower(), "center"))
            if loc:
                self.warn(f"teleport_coords_derived:{dest}")
        if loc is None:
            self.warn(f"teleport_no_coords:{dest}")
            return None
        mapcode, x, y = loc
        if mapcode not in self.ctx["maps"]:
            self.warn(f"teleport_map_missing:{mapcode}")
            return None
        try:
            fare = int(price)
        except ValueError:
            fare = 0
        key = f"tp:{dest}:{pos}"
        p = self.page(key)
        if fare > 0:
            ng = self.page("func:nogold")
            if not ng["say"]:
                ng["say"] = "[返回:1]\n[离开:0]"
                ng["buttons"] = [{"id": 1, "dest": ENTRY_ALIAS}]
            p["checks"] = [{"type": "Gold", "op": "GreaterThanOrEqual", "i1": fare, "fail": "func:nogold"}]
            p["actions"] = [{"type": "TakeGold", "i1": fare},
                            {"type": "Teleport", "map": mapcode, "x": x, "y": y}]
        else:
            p["actions"] = [{"type": "Teleport", "map": mapcode, "x": x, "y": y}]
        return key

    def prefix(self) -> str:
        script = self.npc.get("mud3File") or ""
        if script:
            return re.sub(r"_\w+$", "", script)
        return self.CATEGORY_PREFIX.get(self.npc.get("category", ""), self.npc["name"])

    CATEGORY_PREFIX = {
        "武器": "02Weapon", "防具": "03Armor", "药店": "04Potion", "书店": "05Book",
        "仓库": "06Inn", "杂货": "07Grocery", "首饰": "08Accessory", "收购": "10ChestnutMarket",
        "肉店": "01Meet", "公告": "14Quest", "宠物": "12Pet", "管理": "00default",
    }

    def shop_goods(self) -> list[dict]:
        out, seen = [], set()
        for key in {self.npc.get("mud3File") or "", self.prefix()}:
            if not key:
                continue
            for skey, entry in self.ctx["shop_goods"].items():
                if skey != key and not skey.startswith(key + "_"):
                    continue
                for g in entry.get("goods", []):
                    item = g.get("en") or self.resolve_item(g["name_zh"])
                    if not item or item in seen or item not in self.ctx["db_item_names"]:
                        continue
                    seen.add(item)
                    # 脚本 [Goods] 段的数字是库存量/补货周期，不是价格 → price=0 表示沿用物品 DB 售价
                    out.append({"item": item, "price": 0})
        if not out:
            for name, price in self.ctx["prefix_goods"].get(self.prefix(), []):
                if name in seen or name not in self.ctx["db_item_names"]:
                    continue
                seen.add(name)
                out.append({"item": name, "price": price})
        return out

    def sell_types(self) -> list[str]:
        t = list(self.ctx["prefix_types"].get(self.prefix(), []))
        if t:
            return t
        svc = " ".join(self.npc.get("services", []))
        if any(h in svc for h in SVC_SELL_HINTS) or "买卖" in svc:
            return ["Weapon", "Armour", "Helmet", "Necklace", "Bracelet", "Ring", "Shoes"]
        return []

    def repair_types(self) -> list[str]:
        t = [x for x in self.sell_types()
             if x in ("Weapon", "Armour", "Helmet", "Necklace", "Bracelet", "Ring", "Shoes")]
        return t or ["Weapon"]

    def capabilities(self) -> dict:
        svc = " ".join(self.npc.get("services", []))
        cat = self.npc.get("category", "")
        caps = {}
        if self.shop_goods():
            caps["buy"] = True
        if any(h in svc for h in SVC_SHOP_HINTS):
            caps["buy"] = bool(self.shop_goods())
        if any(h in svc for h in SVC_SELL_HINTS) or "收购" in svc:
            caps["sell"] = bool(self.sell_types())
        if SVC_REPAIR_HINT in svc:
            caps["repair"] = True
        if any(h in svc for h in SVC_STORAGE_HINTS) or cat == "仓库":
            caps["storage"] = True
        if any(h in svc for h in SVC_COMPANION_HINTS) or cat == "宠物":
            caps["companion"] = True
        if "婚姻" in svc or "结婚" in svc:
            caps["wedding"] = True
        return caps

    # 普通新号的门槛画像（用于入口页/死链判定：确保“正常玩家点开一定能看到内容”）
    TYPICAL = {"PKPoints": 0, "Level": 1, "Gold": 0, "Class": -1, "HasItem": 0}

    def typical_pass(self, chk: dict) -> bool:
        ctype = chk["type"]
        if ctype not in self.TYPICAL:
            return False                      # 未知门槛（旗标/坐骑/婚姻…）按不满足处理
        have = self.TYPICAL[ctype]
        want = chk.get("i1", 0)
        op = chk.get("op", "Equal")
        return {
            "Equal": have == want,
            "NotEqual": have != want,
            "LessThan": have < want,
            "LessThanOrEqual": have <= want,
            "GreaterThan": have > want,
            "GreaterThanOrEqual": have >= want,
        }.get(op, False)

    def simulate(self, key: str | None) -> str | None:
        """模拟一个「普通玩家」点击该页后实际会看到的页 key；返回 None 表示点了没反应。"""
        seen = set()
        cur = key
        while cur is not None and cur not in seen:
            seen.add(cur)
            p = self.pages.get(cur)
            if p is None:
                return None
            # 服务端 NPCCall 先过 CheckPage：条件不满足就走 FailPage。
            blocked = [c for c in p["checks"] if not self.typical_pass(c)]
            if blocked:
                target = next((c["fail"] for c in blocked if c.get("fail")), None)
                if target:
                    cur = target
                    continue
            if p["say"] or p["actions"] or p["goods"] or p["types"]:
                return cur
            cur = p["success"]
        return None

    def display_target(self, key: str | None) -> dict | None:
        """入口页若只是路由/条件页，返回普通玩家实际会看到的那一页。"""
        land = self.simulate(key)
        return self.pages.get(land) if land else None

    def make_service_entry(self) -> str:
        """无脚本文本时的入口页：仅列出能力入口（不做剧情文本）。"""
        key = "service:entry"
        p = self.page(key)
        p["say"] = "[离开:0]"
        self.entry = key
        return key

    def inject_services(self):
        """把服务能力对应的功能入口补进入口页（原版这些界面由客户端提供）。"""
        caps = self.capabilities()
        svc = " ".join(self.npc.get("services", []))
        entry = self.display_target(self.entry) if self.entry else None
        if entry is None:
            return
        self.entry = next(k for k, v in self.pages.items() if v is entry)
        have = {b["dest"] for b in entry["buttons"]}
        next_id = max([b["id"] for b in entry["buttons"]] or [0]) + 1
        added = []

        def add(label: str, dest: str):
            nonlocal next_id
            if dest in have:
                return
            entry["buttons"].append({"id": next_id, "dest": dest})
            have.add(dest)
            added.append(f"[{label}:{next_id}]")
            next_id += 1

        # 原版菜单里已经提供该功能时不再重复注入（例如 <购买/@buy> 已指向 BuySell 页）
        provided = set()
        for b in entry["buttons"]:
            dp = self.pages.get(b["dest"])
            if dp is None:
                continue
            if dp["type"] == "BuySell":
                provided.add("sell" if dp["types"] else "buy")
            elif dp["type"] == "Repair":
                provided.add("repair")
            elif dp["type"] == "CompanionManage":
                provided.add("companion")
            elif dp["type"] == "WeddingRing":
                provided.add("wedding")
            if any(a["type"] == "Storage" for a in dp["actions"]):
                provided.add("storage")

        if caps.get("buy") and "buy" not in provided:
            add("购买物品", self.func_page("buy", ""))
        if caps.get("sell") and "sell" not in provided:
            add("卖出物品", self.func_page("sell", ""))
        if caps.get("repair") and "repair" not in provided:
            add("修理装备", self.func_page("repair", ""))
        if caps.get("storage") and "storage" not in provided:
            add("存取物品", self.func_page("storage", ""))
        if caps.get("wedding") and "wedding" not in provided:
            key = "func:wedding"
            wp = self.page(key)
            wp["type"] = "WeddingRing"
            wp["say"] = "[返回:1]\n[离开:0]"
            wp["buttons"] = [{"id": 1, "dest": ENTRY_ALIAS}]
            add("婚戒管理", key)
        if caps.get("companion") and "companion" not in provided:
            key = "func:companion"
            cp = self.page(key)
            cp["type"] = "CompanionManage"
            cp["say"] = "[返回:1]\n[离开:0]"
            cp["buttons"] = [{"id": 1, "dest": self.entry}]
            add("随从管理", key)
        if added:
            entry["say"] = (entry["say"] + "\n\n" + "\n".join(added)).strip()

    def resolve_item(self, zh: str) -> str | None:
        """中文物品名 → 本 DB 的 ItemInfo.ItemName；解析不到或本 DB 无此物品都返回 None。"""
        if not zh:
            return None
        zh = zh.strip().strip("　")
        name = self.ctx["item_alias"].get(zh) or zh
        return name if name in self.ctx["db_item_names"] else None


# --------------------------------------------------------------------------------------
def parse_moverootin(path: str) -> dict:
    out = {}
    dest = pos = None
    for line in read_gbk(path).splitlines():
        s = line.strip()
        if not s or s.startswith(";"):
            continue
        m = re.match(r'^Equal\s+A0\{DESTINATION\}\s*"?([\w]+)"?', s, re.I)
        if m:
            dest = m.group(1)
            continue
        m = re.match(r'^Equal\s+A1\{POSITION\}\s*"?([\w]+)"?', s, re.I)
        if m:
            pos = m.group(1)
            continue
        m = re.match(r"^mapmove\s+(\S+)\s+(\d+)\s+(\d+)", s, re.I)
        if m and dest and pos:
            out[(dest.lower(), pos.lower())] = (m.group(1), int(m.group(2)), int(m.group(3)))
            dest = pos = None
    return out


def parse_merchant(path: str) -> dict:
    """(map, x, y) → 脚本名；用于给没有 mud3_file 的 NPC 找权威脚本。"""
    out = {}
    for line in read_gbk(path).splitlines():
        s = line.strip()
        if not s or s.startswith(";") or s.startswith("//"):
            continue
        parts = s.split()
        if len(parts) < 5:
            continue
        script, mapcode, x, y = parts[0], parts[1], parts[2], parts[3]
        if not re.fullmatch(r"-?\d+", x) or not re.fullmatch(r"-?\d+", y):
            continue
        out[(mapcode, int(x), int(y))] = script
    return out


def parse_client_item_names() -> dict:
    """客户端本地化表 db_names.json：英文名 → 中文名，反向得到 中文 → 英文。"""
    path = os.path.join(REPO, "GodotClient", "translations", "db_names.json")
    if not os.path.exists(path):
        return {}
    data = json.load(open(path, encoding="utf-8"))
    out = {}
    for en, langs in (data.get("items") or {}).items():
        zh = (langs or {}).get("zh")
        if zh:
            out.setdefault(zh, en)
    return out


def parse_item_aliases() -> dict:
    path = os.path.join(REPO, "Tools", "ClassicMagicFixer", "Mud3ItemAliases.cs")
    if not os.path.exists(path):
        return {}
    src = open(path, encoding="utf-8").read()
    return {m.group(1): m.group(2) for m in re.finditer(r'\["([^"]+)"\]\s*=\s*"([^"]+)"', src)}


def build_prefix_data(graph: dict, manifest: dict):
    npc_by_index = {n["index"]: n for n in manifest["npcs"]}
    prefix_types: dict[str, set] = collections.defaultdict(set)
    prefix_goods: dict[str, list] = collections.defaultdict(list)
    owner_of = {n["entryPage"]: n for n in graph["npcs"] if n["entryPage"] >= 0}
    for p in graph["pages"]:
        owner = owner_of.get(p["index"])
        if owner is None:
            continue
        mf = npc_by_index.get(owner["index"], {}).get("mud3File", "")
        if not mf:
            continue
        prefix = re.sub(r"_\w+$", "", mf)
        for t in p["types"]:
            prefix_types[prefix].add(t["itemType"])
        for g in p["goods"]:
            if g["item"] and g["itemType"] != "Currency":
                prefix_goods[prefix].append((g["item"], g["price"]))
    return {k: sorted(v) for k, v in prefix_types.items()}, dict(prefix_goods)


def resolve_scripts(manifest: dict, merchant: dict) -> dict:
    """给每个 NPC 补上权威脚本名（Merchant.txt 坐标优先，其次清单里的 mud3File）。"""
    out = {}
    for n in manifest["npcs"]:
        key = (n["mapFile"], n["x"], n["y"])
        script = merchant.get(key)
        if script is None:
            best, bestd = None, 99
            for (mc, mx, my), sc in merchant.items():
                if mc != n["mapFile"]:
                    continue
                d = max(abs(mx - n["x"]), abs(my - n["y"]))
                if d < bestd:
                    best, bestd = sc, d
            if best is not None and bestd <= 2:
                script = best
        out[n["index"]] = script or (n.get("mud3File") or "")
    return out


def main() -> int:
    graph = json.load(open(GRAPH, encoding="utf-8"))
    manifest = json.load(open(os.path.join(REPO, "tools", "npc_audit_manifest.json"), encoding="utf-8"))
    sg_path = os.path.join(REPO, "tools", "npc_shop_goods.json")
    shop_goods = json.load(open(sg_path, encoding="utf-8")).get("scripts", {}) if os.path.exists(sg_path) else {}

    merchant = parse_merchant(os.path.join(MUD3_ROOT, "Merchant.txt"))
    scripts = resolve_scripts(manifest, merchant)
    resolved = sum(1 for k, v in scripts.items() if v)
    missing = [k for k, v in scripts.items() if not v]

    prefix_types, prefix_goods = build_prefix_data(graph, manifest)
    ctx = {
        "maps": {m["fileName"] for m in graph["maps"]},
        "db_item_names": {i["name"] for i in graph["items"]},
        "item_alias": {**parse_client_item_names(), **parse_item_aliases()},
        "moverootin": parse_moverootin(os.path.join(MUD3_ROOT, "QuestDiary", "Teleport", "moverootin.txt")),
        "shop_goods": shop_goods,
        "prefix_types": prefix_types,
        "prefix_goods": prefix_goods,
    }

    repo = ScriptRepo(MUD3_ROOT)
    results, stats = [], collections.Counter()
    warn_hist = collections.Counter()
    for npc in manifest["npcs"]:
        n = dict(npc)
        n["mud3File"] = scripts.get(npc["index"], "")
        c = Compiler(repo, n, ctx)
        out = c.compile()
        stats["npcs"] += 1
        stats["pages"] += len(out["pages"])
        stats["npc_with_pages"] += 1 if out["pages"] else 0
        stats["entries"] += 1 if out["entry"] else 0
        for w in out["warnings"]:
            warn_hist[w] += 1
        results.append(out)

    doc = {"npcs": results, "stats": dict(stats), "warnings": dict(warn_hist),
           "scripts_resolved": resolved, "scripts_missing": missing}
    out_path = sys.argv[1] if len(sys.argv) > 1 else "/tmp/npc_dialog_ir.json"
    json.dump(doc, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"[mud3_npc_compile] npcs={stats['npcs']} entries={stats['entries']} pages={stats['pages']} "
          f"with_pages={stats['npc_with_pages']} scripts_resolved={resolved} missing={len(missing)} -> {out_path}")
    for k, v in warn_hist.most_common(25):
        print(f"    {k:34} {v}")
    if missing:
        print("    missing scripts for NPCs:", missing[:20])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

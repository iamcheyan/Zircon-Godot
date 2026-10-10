#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""extract_npc_shop_goods.py -- Mud3 商店脚本 -> Zircon 商品清单提取器。

用途
----
扫描原始 Mud3 服务端脚本 `Market_Def/**`（递归，含 `event/` 子目录）里的
「物品行」，按 label 归属，映射到 Zircon ItemInfo 名字，输出
`tools/npc_shop_goods.json`，并打印统计 + 与现役 DB(NPCGood) 的交叉校验。

=============================  重要语义说明  =============================
Mud3 的 `[Goods]` 段表头在脚本里自带注释：

    [Goods]
    ;ItemName      Volume     Hour
    木剑            100        1

即第 1 个数字是 **Volume（库存/补货量）**，第 2 个数字是 **Hour（补货周期，
小时）**，两者都 **不是金币价格**（Mud3 的金币价格来自物品数据库
StdItems/ItemInfo，不由商店脚本给出）。

本脚本按约定 schema 输出 `price`(= 脚本第 1 个数字, 即 Volume) 与
`count`(= 脚本第 2 个数字, 即 Hour)，**同时**额外附带：

    name        -> 映射到的 Zircon ItemInfo.ItemName（映射不上为 null）
    item_index  -> Zircon ItemInfo.Index（映射不上为 null）
    db_price    -> Zircon ItemInfo.Price（真正的金币价格，映射不上为 null）

也就是说 `price`/`count` 是**忠实照抄脚本列**，真正的价格请用 `db_price`。
=========================================================================

物品行判定（requirement 1）
---------------------------
逐行扫描（已 strip），命中条件：
  a) 非空，且行首字符不在 `#;[]{}<>@%/+` 中；
  b) 形如 `中文名 数字 [数字]`（`^(\\S+)\\s+(\\d+)(?:\\s+(\\d+))?$`）；
  c) 名字 token 至少含一个 CJK 字符。
c) 是「中文名」的字面落地：Mud3 脚本里 `checkpkpoint 2` / `checklevel 5` /
`CheckDailyQuest 437` 这类**命令 token + 数字**同样满足 a)+b)，只有要求
名字含 CJK 才能把它们排除。实测全库 411 个脚本中，满足 a)+b)+c) 的行
100% 落在 `[Goods]` 段内（见 tools 报告）。

归属（requirement 2）
---------------------
每个物品行归属到它上方最近的 `[...]` 段头（`[Goods]` / `[@xxx]` / `[...]`），
输出 `label` 字段时去掉方括号（如 `Goods`、`@sell`）。数据集里所有物品行都
落在 `[Goods]` 内，因此 label 基本恒为 `Goods`；保留通用逻辑以便脚本变体。

名称归一化 + 映射（requirement 3）
---------------------------------
1. 去掉半角/全角空格与制表符（`re.sub(r'\\s+','',name)`）。
2. 用 `Mud3ItemAliases.cs`（本项目内自行解析，不复用 C# 构建）做中->英映射；
   查找时同时尝试「原样」和「全角括号()->半角()」两种形态（脚本里大量使用
   `金创药（小）`、`布衣（男）`，而别名表键为 `金创药(小)`、`布衣(男)`）。
3. 用 `/tmp/npc_graph.json` 的 `items[].name` 校验目标存在性；命中则输出
   `name` + `item_index` + `db_price`。
4. 如果映射不上，但归一化后的中文名**本身**就在 DB 名字集合里（DB 里也有
   中文名的情况），也算命中（resolution=db_same_name）。
5. 其余计入 `unresolved`；`unresolved_detail` 区分原因：
   - `no_alias`：别名表里没有这个中文名，DB 里也没有。
   - `alias_target_missing`：别名表里有，但映射到的英文名不在 DB 导出里。

buy_nodes（requirement 4）——“玩家卖给 NPC”的节点
-------------------------------------------------
一个 label 段被判为 buy_node，只要满足以下**任一**证据（evidence 字段记录）：

  E1 `script:checkitem+take/give`（脚本直证）
     同一 label 段内既有「玩家持有检查」(`checkitem`/`checkitemw`，允许前置
     `!`)，又有「回收/给钱动作」(`take`/`takew`/`give`/`givew`/`givegold`/
     `addgold`/`takegold`)。这是脚本层面对“玩家把东西交给 NPC”的实现。
     实测只有 6 个段命中：`02Weapon_{Bichon1,Samak-5,Samak1-5_002}`
     的 `@remove_sword` / `@remove_sword_1`（武器回收/销毁服务）。

  E2 `convert_text:<关键词>`（文本语义）
     该 label 段的 `#INCLUDE [..\\Convert_Def\\...] @Xxx` 指向的 Convert_Def
     页面文本（先剥掉 `<链接/@cmd>` 标记，避免菜单项误伤）含收购语义词：
     `收购` / `买入` / `卖给我` / `回收` / `拿来卖` / `出个好价钱` /
     `给个好价钱` / `抬上来`。实测 88 个段命中，全部都是 `@sell` 或
     `@qweapon` 等“我收购”页；其反例 `欢迎光临，这里出售一些简单的药品`
     （`@main_0_0`，NPC 在卖）不含上述词，不会被误判。

  E3 `label:@sell`（结构约定）
     label 归一化后等于 `sell`。Mud3 的商店菜单固定是
     `<购买/@buy>…<出售/@sell>…`，`@sell` 即“玩家出售”入口；有些商店的
     sell 页文案是中性的（如 `你想出售饰品？`），单靠 E2 会漏，故补这条
     结构性证据。实测 107 个 `[@sell]` 段（另 `[@SellHorse]` 卖马、
     `[@sellitem_1]` 等不等价，已排除）。

  节点的 `items` = 该段内 `take/takew/give/givew/givegold/addgold` 命令
  里出现的非货币物品中文名（按出现顺序去重，排除 `金币`）；若为空（典型
  的 `[@sell]` 段只有一行 `#INCLUDE`），回退为**该脚本提取出的商品名列表**
  （Zircon 买回的东西就是店里卖的东西）。

交叉校验（requirement 5/额外）
------------------------------
用 `/home/tetsuya/development/zircon/tools/aligned_npcs_230.json`（230 个
活动 NPC + `mud3_match.mud3_file` 前缀）连接脚本；用 `/tmp/npc_graph.json`
的 `npcs[].entryPage` + `pages[].buttons[].dest` 走图求该 NPC 现役可达的
`pages[].goods[]`。输出：
  - 只有脚本清单、现役 DB 一个商品都没有的 NPC（本次要修的目标）；
  - 两边都有但清单不一致的 NPC（差异样例 + 计数）；
  - 只有现役 DB 商品、没有脚本清单的 NPC。

编码
----
Market_Def 里 `09Reinstatement_Bichon-0.txt` 是 UTF-8(with BOM)+CRLF，其余
为 GBK。逐文件先试 `utf-8-sig`，失败回退 `gbk(replace)`。

可重跑 / 幂等
-------------
输出按文件名排序、key 顺序固定、不含时间戳，重复运行字节一致。

用法
----
    python3 tools/extract_npc_shop_goods.py            # 默认路径
    python3 tools/extract_npc_shop_goods.py --out X.json --quiet
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import OrderedDict, defaultdict

# --------------------------------------------------------------------------
# 默认路径（均可用 CLI 覆盖）
# --------------------------------------------------------------------------
ZIRCON_ROOT = "/home/tetsuya/development/zircon"
MUD3_ENVIR = ("/home/tetsuya/development/Mir3-Research/reference/"
              "mir3-source/Mud3-Config/Envir3")
DEFAULT_MARKET_DIR = os.path.join(MUD3_ENVIR, "Market_Def")
DEFAULT_CONVERT_DIR = os.path.join(MUD3_ENVIR, "Convert_Def", "Market_Def")
DEFAULT_ALIASES = os.path.join(
    ZIRCON_ROOT, "Tools", "ClassicMagicFixer", "Mud3ItemAliases.cs")
DEFAULT_NPC_GRAPH = "/tmp/npc_graph.json"
DEFAULT_ALIGNED = os.path.join(ZIRCON_ROOT, "tools", "aligned_npcs_230.json")
DEFAULT_OUT = os.path.join(ZIRCON_ROOT, "tools", "npc_shop_goods.json")

# --------------------------------------------------------------------------
# 正则 / 常量
# --------------------------------------------------------------------------
CJK_RE = re.compile(r"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\u3005\u3007]")
BLOCK_RE = re.compile(r"^\s*\[([^\[\]\r\n]+)\]\s*$")
ITEM_RE = re.compile(r"^(\S+)\s+(\d+)(?:\s+(\d+))?$")
EXCLUDED_FIRST_CHARS = set("#;[]{}<>@%/+")

INCLUDE_RE = re.compile(r"#INCLUDE\s+\[([^\]]+)\]\s+(@?\S+)", re.I)
LINK_RE = re.compile(r"<[^<>]*>")
ALIAS_RE = re.compile(r'\["((?:[^"\\]|\\.)*)"\]\s*=\s*"((?:[^"\\]|\\.)*)"\s*,')

# E1: 玩家持有检查 / 回收动作
CHECK_CMD_RE = re.compile(r"^\s*!?\s*(checkitem|checkitemw|checkitemwq)\b", re.I)
ACTION_CMD_RE = re.compile(
    r"^\s*!?\s*(take|takew|give|givew|givegold|addgold|takegold|givemoney|"
    r"takemoney)\b", re.I)
HANDOVER_CMDS = ("take", "takew", "give", "givew", "givegold", "addgold",
                 "givemoney", "takemoney", "takegold")
HANDOVER_RE = re.compile(
    r"^\s*!?\s*(%s)\s+(\S+)" % "|".join(HANDOVER_CMDS), re.I)

# E2: Convert_Def 文本里的“我收购”语义词
BUY_TEXT_KEYWORDS = ("收购", "买入", "卖给我", "回收", "拿来卖",
                     "出个好价钱", "给个好价钱", "抬上来")

# 货币名（出现在 take/give 里不算“物品”）
CURRENCY_NAMES = {"金币", "金元", "元宝", "钱", "银两"}


def read_text(path):
    """先按 UTF-8(with BOM) 读，失败回退 GBK（个体文件编码混杂）。"""
    raw = open(path, "rb").read()
    try:
        return raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        return raw.decode("gbk", "replace")


def has_cjk(text):
    return bool(CJK_RE.search(text))


def norm_name(name):
    """去掉半角/全角空格与制表符。"""
    return re.sub(r"\s+", "", name)


def fold_parens(text):
    """全角括号 -> 半角括号（别名表两种写法混用）。"""
    return text.replace("（", "(").replace("）", ")")


def name_lookup_forms(text):
    """归一化形态 + 括号折叠形态，保持顺序去重。"""
    out = []
    for cand in (text, fold_parens(text)):
        if cand not in out:
            out.append(cand)
    return out


def clean_label(label):
    """段头/label 归一化：BLOCK_RE 已去掉方括号，这里只去空白。"""
    return label.strip()


# --------------------------------------------------------------------------
# 解析
# --------------------------------------------------------------------------
def split_blocks(text):
    """把脚本切成 [(label|None, [lines]), ...]，label 去方括号。"""
    blocks = []
    cur_label = None
    buf = []
    for raw_line in text.splitlines():
        line = raw_line.rstrip("\r")
        m = BLOCK_RE.match(line)
        if m:
            if cur_label is not None or buf:
                blocks.append((cur_label, buf))
            cur_label = clean_label(m.group(1))
            buf = []
            continue
        buf.append(line)
    if cur_label is not None or buf:
        blocks.append((cur_label, buf))
    return blocks


def parse_convert_file(path):
    """Convert_Def 文件 -> {label(normalized, 无@): [文本行]}"""
    out = defaultdict(list)
    cur = None
    for raw_line in read_text(path).splitlines():
        line = raw_line.rstrip("\r")
        m = BLOCK_RE.match(line)
        if m:
            cur = clean_label(m.group(1)).lstrip("@")
            continue
        if cur is not None:
            out[cur].append(line)
    return out


def load_convert_dir(path):
    conv = {}
    if not os.path.isdir(path):
        return conv
    for name in sorted(os.listdir(path)):
        full = os.path.join(path, name)
        if not os.path.isfile(full) or not name.lower().endswith(".txt"):
            continue
        conv[name] = parse_convert_file(full)
    return conv


def build_convert_lookup(conv, convert_dir):
    """basename(小写) -> {label: text}；覆盖 Convert_Def 及其所有子目录。

    Market_Def 脚本的 `#INCLUDE` 也可能指向 Convert_Def/QuestDiary/**，
    所以除了 Market_Def 目录，再递归 Convert_Def 树兜底（同名以
    Market_Def 优先）。
    """
    lookup = {}
    for name, blocks in conv.items():
        lookup[name.lower()] = blocks
    root_dir = os.path.dirname(convert_dir)  # .../Convert_Def
    for root, dirs, files in os.walk(root_dir):
        dirs.sort()
        for name in sorted(files):
            if not name.lower().endswith(".txt"):
                continue
            lookup.setdefault(name.lower(), parse_convert_file(
                os.path.join(root, name)))
    return lookup


def _unescape_cs(s):
    """C# 字面量里只可能出现 \\" 与 \\\\（本仓库生成文件实测无转义）。"""
    return s.replace('\\"', '"').replace("\\\\", "\\")


def load_aliases(path):
    """解析 C# `Mud3ItemAliases.cs` 的 `["中"] = "En",` 字典。"""
    text = open(path, "r", encoding="utf-8").read()
    alias = OrderedDict()
    for m in ALIAS_RE.finditer(text):
        alias[_unescape_cs(m.group(1))] = _unescape_cs(m.group(2))
    # 建索引：原样 / 去空格 / 折括号 / 去空格+折括号
    index = {}
    for zh, en in alias.items():
        for form in name_lookup_forms(zh):
            index.setdefault(form, en)
            index.setdefault(norm_name(form), en)
    return alias, index


def load_db_items(path):
    """-> (name->item dict, ordered names list)"""
    data = json.load(open(path, "r", encoding="utf-8"))
    items = OrderedDict()
    for it in data.get("items", []):
        items[it["name"]] = it
    return items


# --------------------------------------------------------------------------
# 单个脚本的提取
# --------------------------------------------------------------------------
def resolve_name(name_zh, alias_index, db_items):
    """-> (zircon_name|None, item|None, resolution, unresolved_reason|None)"""
    forms = name_lookup_forms(norm_name(name_zh))
    target = None
    for form in forms:
        if form in alias_index:
            target = alias_index[form]
            break
    if target and target in db_items:
        return target, db_items[target], "alias", None
    if target:
        # 别名存在但目标不在 DB 导出里 -> 未解析（原因可辨）
        return None, None, "unresolved", "alias_target_missing:%s" % target
    for form in forms:
        if form in db_items:
            return form, db_items[form], "db_same_name", None
    return None, None, "unresolved", "no_alias"


def collect_handover_items(lines):
    """段内 take/give 命令里的物品中文名（顺序去重，排除货币）。"""
    out = []
    for line in lines:
        m = HANDOVER_RE.match(line)
        if not m:
            continue
        arg = norm_name(m.group(2))
        if not arg or arg in CURRENCY_NAMES or arg in out:
            continue
        if not has_cjk(arg):
            continue
        out.append(arg)
    return out


def make_buy_node_evidence(lines, label, convert_lookup):
    """返回 (evidence[list[str]], convert_page|None, semantic_texts[list])"""
    evidence = []
    convert_page = None
    texts = []
    has_check = any(CHECK_CMD_RE.match(ln) for ln in lines)
    has_action = any(ACTION_CMD_RE.match(ln) for ln in lines)
    if has_check and has_action:
        evidence.append("script:checkitem+take/give")
    seen_pages = set()
    for line in lines:
        for m in INCLUDE_RE.finditer(line):
            base = os.path.basename(m.group(1).replace("\\", "/")).lower()
            cl = clean_label(m.group(2)).lstrip("@")
            blocks = convert_lookup.get(base)
            if not blocks or cl not in blocks:
                continue
            if cl in seen_pages:
                continue
            seen_pages.add(cl)
            if convert_page is None:
                convert_page = cl
            body = LINK_RE.sub("", "\n".join(blocks[cl]))
            texts.append((cl, body))
    for cl, body in texts:
        hits = [kw for kw in BUY_TEXT_KEYWORDS if kw in body]
        if hits:
            evidence.append("convert_text:%s@%s" % ("/".join(hits), cl))
    if label and norm_name(label).lstrip("@").lower() == "sell":
        evidence.append("label:@sell")
    return evidence, convert_page, texts


def extract_script(path, rel_path, alias_index, db_items, convert_lookup):
    text = read_text(path)
    blocks = split_blocks(text)

    goods = []
    buy_nodes = []
    unresolved_names = OrderedDict()  # zh -> reason

    for label, lines in blocks:
        label_out = label if label is not None else "(head)"
        for raw_line in lines:
            line = raw_line.strip()
            if not line or line[0] in EXCLUDED_FIRST_CHARS:
                continue
            m = ITEM_RE.match(line)
            if not m:
                continue
            name_zh = norm_name(m.group(1))
            if not has_cjk(name_zh):
                # 形如 `checkpkpoint 2` / `CheckDailyQuest 437` 的命令行
                continue
            price = int(m.group(2))
            count = int(m.group(3)) if m.group(3) is not None else None
            zircon_name, item, resolution, reason = resolve_name(
                name_zh, alias_index, db_items)
            entry = OrderedDict()
            entry["name_zh"] = name_zh
            entry["price"] = price          # Mud3 [Goods] 第 1 列 = Volume
            entry["count"] = count          # Mud3 [Goods] 第 2 列 = Hour
            entry["label"] = label_out
            entry["name"] = zircon_name
            entry["item_index"] = item["index"] if item else None
            entry["db_price"] = item.get("price") if item else None
            entry["resolution"] = resolution
            goods.append(entry)
            if resolution == "unresolved":
                unresolved_names.setdefault(name_zh, reason)

        evidence, convert_page, _texts = make_buy_node_evidence(
            lines, label, convert_lookup)
        if evidence:
            items = collect_handover_items(lines)
            node = OrderedDict()
            node["label"] = label_out
            node["items"] = items
            node["evidence"] = evidence
            if convert_page:
                node["convert_page"] = convert_page
            buy_nodes.append(node)

    # buy_node 的 items 回退：段内没有显式物品命令时，用该脚本的商品名列表
    script_goods_zh = []
    for g in goods:
        if g["name_zh"] not in script_goods_zh:
            script_goods_zh.append(g["name_zh"])
    for node in buy_nodes:
        if not node["items"]:
            node["items"] = list(script_goods_zh)

    record = OrderedDict()
    record["file"] = rel_path
    record["goods"] = goods
    record["buy_nodes"] = buy_nodes
    record["unresolved"] = list(unresolved_names.keys())
    record["unresolved_detail"] = OrderedDict(
        (k, v) for k, v in unresolved_names.items())
    return record


# --------------------------------------------------------------------------
# DB 侧交叉校验
# --------------------------------------------------------------------------
def build_page_graph(npc_graph):
    pages = {p["index"]: p for p in npc_graph.get("pages", [])}
    return pages


def reachable_pages(start, pages):
    if start is None or start < 0 or start not in pages:
        return set()
    seen = set()
    stack = [start]
    while stack:
        idx = stack.pop()
        if idx in seen or idx not in pages:
            continue
        seen.add(idx)
        for btn in pages[idx].get("buttons", []):
            dest = btn.get("dest")
            if isinstance(dest, int) and dest > 0:
                stack.append(dest)
        sp = pages[idx].get("successPage")
        if isinstance(sp, int) and sp > 0:
            stack.append(sp)
    return seen


def db_goods_for_npc(npc, pages):
    """现役 DB 里该 NPC 可达的 pages[].goods[] 条目。"""
    out = []
    for idx in sorted(reachable_pages(npc.get("entryPage", -1), pages)):
        for g in pages[idx].get("goods", []):
            out.append(g)
    return out


def match_scripts_for_prefix(prefix, script_keys, stems_by_key):
    """mud3_file 前缀 -> 命中的脚本文件 stem 列表（`<prefix>-<mapcode>`）。"""
    hits = []
    for key, stem in stems_by_key.items():
        if stem == prefix or stem.startswith(prefix + "-"):
            hits.append(key)
    return sorted(hits)


def crosscheck(aligned, npc_graph, scripts):
    pages = build_page_graph(npc_graph)
    npcs_by_index = {}
    for n in npc_graph.get("npcs", []):
        npcs_by_index[n["index"]] = n
    stems_by_key = {k: os.path.splitext(os.path.basename(v["file"]))[0]
                    for k, v in scripts.items()}

    report = OrderedDict()
    report["aligned_npcs"] = len(aligned)
    npc_rows = []
    counts = defaultdict(int)
    detail = OrderedDict()
    for a in aligned:
        idx = a.get("npc_index")
        prefix = (a.get("mud3_match") or {}).get("mud3_file")
        db_goods = db_goods_for_npc(npcs_by_index.get(idx, {}), pages)
        db_names = []
        for g in db_goods:
            if g.get("item") and g["item"] not in db_names:
                db_names.append(g["item"])
        script_keys = match_scripts_for_prefix(prefix, scripts, stems_by_key) \
            if prefix else []
        script_names = []
        script_files = []
        for k in script_keys:
            script_files.append(scripts[k]["file"])
            for g in scripts[k]["goods"]:
                if g["name"] and g["name"] not in script_names:
                    script_names.append(g["name"])
        row = OrderedDict()
        row["npc_index"] = idx
        row["name_zh"] = a.get("name_zh")
        row["name_en"] = a.get("name_en")
        row["map"] = a.get("map_desc_en")
        row["mud3_prefix"] = prefix
        row["script_files"] = script_files
        row["db_goods_count"] = len(db_names)
        row["script_goods_count"] = len(script_names)
        row["db_only"] = sorted(set(db_names) - set(script_names))
        row["script_only"] = sorted(set(script_names) - set(db_names))
        if not prefix:
            cls = "no_mud3_match"
        elif not script_keys:
            cls = "mud3_file_not_found"
        elif not script_names and not db_names:
            cls = "both_empty"
        elif script_names and not db_names:
            cls = "script_only_no_db_goods"      # <- 本次要修的目标
        elif db_names and not script_names:
            cls = "db_only_no_script_goods"
        elif set(script_names) == set(db_names):
            cls = "identical"
        else:
            cls = "differs"
        row["class"] = cls
        counts[cls] += 1
        npc_rows.append(row)
        if cls in ("script_only_no_db_goods", "differs", "db_only_no_script_goods"):
            detail.setdefault(cls, []).append(row)

    # 商品行级别的差异统计
    diff_goods = 0
    lacking_goods = 0
    for row in npc_rows:
        if row["class"] == "differs":
            diff_goods += len(row["script_only"]) + len(row["db_only"])
        if row["class"] == "script_only_no_db_goods":
            lacking_goods += row["script_goods_count"]

    report["counts"] = OrderedDict(sorted(counts.items()))
    report["summary"] = OrderedDict()
    report["summary"]["npcs_matched_to_script"] = sum(
        v for k, v in counts.items()
        if k not in ("no_mud3_match", "mud3_file_not_found"))
    report["summary"]["scripts_with_goods"] = sum(
        1 for s in scripts.values() if s["goods"])
    report["summary"]["npcs_with_db_goods"] = sum(
        1 for row in npc_rows if row["db_goods_count"])
    report["summary"]["npcs_missing_db_goods_but_have_script_goods"] = counts.get(
        "script_only_no_db_goods", 0)
    report["summary"]["npcs_with_differing_goods"] = counts.get("differs", 0)
    report["summary"]["diff_item_slots_in_differing_npcs"] = diff_goods
    report["summary"]["goods_slots_to_add_for_empty_npcs"] = lacking_goods
    report["fix_targets"] = [
        OrderedDict((k, row[k]) for k in (
            "npc_index", "name_zh", "name_en", "map", "mud3_prefix",
            "script_files", "script_goods_count"))
        for row in detail.get("script_only_no_db_goods", [])
    ]
    report["differs_samples"] = detail.get("differs", [])[:25]
    report["db_only_samples"] = detail.get("db_only_no_script_goods", [])[:25]
    report["identical_samples"] = [
        row for row in npc_rows if row["class"] == "identical"][:10]
    report["npc_rows"] = npc_rows
    return report


# --------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------
def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--market-dir", default=DEFAULT_MARKET_DIR)
    ap.add_argument("--convert-dir", default=DEFAULT_CONVERT_DIR)
    ap.add_argument("--aliases", default=DEFAULT_ALIASES)
    ap.add_argument("--npc-graph", default=DEFAULT_NPC_GRAPH)
    ap.add_argument("--aligned", default=DEFAULT_ALIGNED)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args(argv)

    alias, alias_index = load_aliases(args.aliases)
    db_items = load_db_items(args.npc_graph)
    convert_lookup = build_convert_lookup(load_convert_dir(args.convert_dir),
                                         args.convert_dir)

    script_files = []
    for root, dirs, files in os.walk(args.market_dir):
        dirs.sort()
        for name in sorted(files):
            if name.lower().endswith(".txt"):
                script_files.append(os.path.join(root, name))
    script_files.sort()

    scripts = OrderedDict()
    for full in script_files:
        rel = os.path.relpath(full, os.path.dirname(args.market_dir)).replace(
            os.sep, "/")
        key = os.path.splitext(os.path.basename(full))[0]
        scripts[key] = extract_script(
            full, rel, alias_index, db_items, convert_lookup)

    # stats
    total_goods = sum(len(s["goods"]) for s in scripts.values())
    unresolved_rows = 0
    unresolved_distinct = OrderedDict()   # zh -> reason（首次出现顺序）
    unresolved_rows_by_name = defaultdict(int)
    resolution_kinds = defaultdict(int)
    for s in scripts.values():
        for g in s["goods"]:
            resolution_kinds[g["resolution"]] += 1
            if g["resolution"] == "unresolved":
                unresolved_rows += 1
                unresolved_rows_by_name[g["name_zh"]] += 1
                unresolved_distinct.setdefault(
                    g["name_zh"], s["unresolved_detail"][g["name_zh"]])
    stats = OrderedDict()
    stats["scripts"] = len(scripts)
    stats["goods"] = total_goods
    stats["unresolved"] = sum(len(s["unresolved"]) for s in scripts.values())

    # 交叉校验
    try:
        npc_graph = json.load(open(args.npc_graph, "r", encoding="utf-8"))
        aligned = json.load(open(args.aligned, "r", encoding="utf-8"))
        cc = crosscheck(aligned, npc_graph, scripts)
    except FileNotFoundError as exc:
        cc = OrderedDict([("error", "missing input: %s" % exc)])

    out = OrderedDict()
    out["legend"] = OrderedDict([
        ("schema", "scripts[key].goods[] 中 price=脚本第1列(Volume 库存量)、"
                   "count=脚本第2列(Hour 补货周期)，均非金币价格；"
                   "金币价格见 db_price(ItemInfo.Price) / name / item_index。"),
        ("resolution", "alias=别名表命中且目标在DB; db_same_name=DB中文同名; "
                       "unresolved=未解析"),
        ("buy_node_evidence",
         "script:checkitem+take/give | convert_text:<kw>@<page> | label:@sell"),
    ])
    out["scripts"] = scripts
    out["stats"] = stats
    out["resolution_kinds"] = OrderedDict(sorted(resolution_kinds.items()))
    out["unresolved_detail"] = OrderedDict(
        sorted(unresolved_distinct.items(),
               key=lambda kv: (-unresolved_rows_by_name[kv[0]], kv[0])))
    out["crosscheck"] = cc

    payload = json.dumps(out, ensure_ascii=False, indent=2, sort_keys=False)
    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write(payload)
        fh.write("\n")

    if not args.quiet:
        print("=" * 72)
        print("Mud3 商店脚本 -> Zircon 商品清单")
        print("=" * 72)
        print("扫描脚本数           : %d (%s)" % (len(scripts), args.market_dir))
        print("  其中有商品行的脚本 : %d" % sum(1 for s in scripts.values()
                                                if s["goods"]))
        goods_by_label = defaultdict(int)
        for s in scripts.values():
            for g in s["goods"]:
                goods_by_label[g["label"]] += 1
        print("商品行按 label 分布  : %s" % dict(sorted(goods_by_label.items())))
        print("解析结果             : %s" % dict(sorted(resolution_kinds.items())))
        print("商品行总数           : %d" % total_goods)
        print("未解析行数           : %d (distinct %d)"
              % (unresolved_rows, len(unresolved_distinct)))
        print("buy_nodes 总数       : %d" % sum(len(s["buy_nodes"])
                                                for s in scripts.values()))
        ev = defaultdict(int)
        for s in scripts.values():
            for n in s["buy_nodes"]:
                for e in n["evidence"]:
                    if e.startswith("convert_text:"):
                        ev["convert_text:" + e.split(":", 1)[1].split("@")[0]] += 1
                    else:
                        ev[e] += 1
        print("  evidence 分布      : %s" % dict(sorted(ev.items())))
        print("-" * 72)
        print("未解析名称 Top 30 (name, 出现行数, 原因):")
        for name, cnt in sorted(
                unresolved_rows_by_name.items(),
                key=lambda kv: (-kv[1], kv[0]))[:30]:
            print("   %3d  %-14s %s" % (cnt, name, unresolved_distinct[name]))
        print("-" * 72)
        if "error" in cc:
            print("crosscheck: %s" % cc["error"])
        else:
            s = cc["summary"]
            print("NPC 脚本匹配数       : %d / %d"
                  % (s["npcs_matched_to_script"], cc["aligned_npcs"]))
            print("有商品行的脚本数     : %d" % s["scripts_with_goods"])
            print("现役 DB 有商品的 NPC : %d" % s["npcs_with_db_goods"])
            print("DB 无商品但有脚本清单: %d 个 NPC <- 本次要修"
                  % s["npcs_missing_db_goods_but_have_script_goods"])
            print("两边都有但清单不一致 : %d 个 NPC (差异条目 %d)"
                  % (s["npcs_with_differing_goods"],
                     s["diff_item_slots_in_differing_npcs"]))
            print("class 分布           : %s" % dict(cc["counts"]))
            print("修复目标(无 DB 商品但有脚本清单)前 15 个:")
            for row in cc["fix_targets"][:15]:
                print("   #%-4s %-10s %-14s %-26s goods=%d %s"
                      % (row["npc_index"], row["name_zh"], row["map"] or "",
                         ",".join(row["script_files"]).replace("Market_Def/", ""),
                         row["script_goods_count"], ""))
        print("-" * 72)
        print("样例商店 (中文 -> Zircon / DB价格):")
        samples = [k for k in ("02Weapon_Bichon2-0", "08Accessory_Eunhang-02",
                               "04Potion_Bichon1-0") if k in scripts]
        for k in samples:
            s = scripts[k]
            print(" [%s] %s  goods=%d" % (k, s["file"], len(s["goods"])))
            for g in s["goods"][:8]:
                print("     %-8s -> %-28s db_price=%s (script %s/%s)"
                      % (g["name_zh"], g["name"] or "<unresolved>",
                         g["db_price"], g["price"], g["count"]))
        print("=" * 72)
        print("写出: %s" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

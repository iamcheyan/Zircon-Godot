#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""fix_npcpage_goods_marker.py —— 去掉 NPCPage 正文里无人解析的 `[Goods]` 标记行（默认 dry-run）。

背景（docs/AUDIT/WORKLOG.md 第 21 段）：
  客户端只在 `DialogType == BuySell` 且 `_page.Goods` 非空时才渲染商店货架
  （GodotClient/Controls/NPCDialog.cs:515），而 `[Goods]` 这个旧式文本标记在
  GodotClient / Client / ServerLibrary / LibraryCore 里**没有任何解析**（全虚拟 grep 为空）。
  于是把货单写在正文末尾的页面，会在游戏里把 `[Goods]` 与价目原样打印出来 —— 这正是
  用户看到"沙漠里的商人对话很怪"的那一处（#218 啊全，其入口页 #37375）。

做法（最小、零信息损失、不发明内容）：只删除 `[Goods]` 这个**标记本身**，保留其后的价目行
  （那些行读起来就是一张价目表，对玩家仍有信息价值）。不改动任何结构化字段。

用法：
  python3 tools/restore/fix_npcpage_goods_marker.py                       # dry-run
  python3 tools/restore/fix_npcpage_goods_marker.py --verify              # /tmp 副本 round-trip
  python3 tools/restore/fix_npcpage_goods_marker.py --apply
  python3 tools/restore/fix_npcpage_goods_marker.py --rollback /tmp/dbeditor_ws_backup_<ts>
"""
from __future__ import annotations
import argparse, json, re, sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import restore_cleaned_content as rcc  # noqa: E402

MARKER = "[Goods]"


def strip_marker(say: str) -> str:
    """删除 [Goods] 标记，保留其后文本。"""
    out = re.sub(r"[ \t]*\[Goods\][ \t]*\r?\n?", "", say)
    return out


def main():
    ap = argparse.ArgumentParser(description="去掉 NPCPage 正文里的 [Goods] 死标记（默认 dry-run）")
    ap.add_argument("--workspace", type=Path, default=rcc.DEFAULT_WORKSPACE)
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--verify-db", type=Path, default=None)
    ap.add_argument("--out", type=Path, default=Path("/tmp/fix_goods_marker_preview.json"))
    ap.add_argument("--rollback", type=Path)
    args = ap.parse_args()

    stamp = time.strftime("%Y%m%d-%H%M%S")
    if args.rollback:
        rcc.rollback(args.rollback, args.workspace)
        return 0

    pages, _ = rcc.load_table(args.workspace, "NPCPage")
    modifications = []
    for p in pages:
        say = p.get("Say") or ""
        if MARKER in say:
            modifications.append({"table": "NPCPage", "Index": p["Index"], "field": "Say",
                                  "new": strip_marker(say)})

    print("含 %s 的页面：%d 个（只删标记，保留价目文本）" % (MARKER, len(modifications)))
    for m in modifications[:6]:
        old = next(p.get("Say") for p in pages if p["Index"] == m["Index"])
        i = old.find(MARKER)
        print("   页#%s  改前…%r  改后…%r" % (m["Index"], old[max(0, i - 24):i + 30],
                                             m["new"][max(0, i - 24):i + 30]))
    args.out.write_text(json.dumps({"count": len(modifications),
                                    "indices": [m["Index"] for m in modifications]},
                                   ensure_ascii=False, indent=2), encoding="utf-8")
    print("预览 -> %s" % args.out)

    if args.verify:
        return rcc.verify(args.workspace, {}, modifications, stamp, args.verify_db)
    if not args.apply:
        print("\n（dry-run；确认后加 --apply，再在 82 上跑 sync.sh）")
        return 0

    bdir = rcc.backup_workspace(args.workspace, stamp, ["NPCPage.json"])
    changed = rcc.write_tables(args.workspace, {}, modifications)
    print("\n[✓] 已写工作区：%s\n[✓] 备份：%s" % (changed, bdir))
    print("下一步：82 上 cd ~/development/Mir3-Research && bash Tools/dbeditor/sync.sh")
    return 0


if __name__ == "__main__":
    sys.exit(main())

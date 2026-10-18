#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""npc_placement_health.py —— NPC / 卫士 / 刷怪 位置的**整体体检**（只读、可复跑）。

为什么需要它：NPC 位置是从 MUD3 坐标移植过来的（见 Mir3-Research/Tools/NpcMover/audit-report.md），
而移植后的 .map 与 MUD3 原图可能不完全一致 → "坐标系对但落点看起来在郊外"。本工具用**四条互相独立**
的判据交叉，避免只看单一来源自证：

  判据 1  与 NpcMover 计划的一致性（计划 = MUD3 对照结果）
  判据 2  商店/服务类 NPC 是否落在**本图 NPC 聚落**之外（聚落 = 同图 NPC 的中位数 ± 范围）
  判据 3  落点是否可站 / 是否在该图**主连通区**（BFS 连通分量）—— 抓"封在死区里，玩家走不到"
  判据 4  商店类 NPC 是否落在**没有安全区**的图上（安全区 = 游戏自己的城镇标记）

判据 2/4 的已知盲区（写在结论里，别当成没问题）：
  * 判据 2 用 NPC 自己定义聚落 → 若整图 NPC 一起错位，它看不出来；故配合判据 4 与人工看截图。
  * 判据 4 会把"室内分图"（如 1_006 药铺内、4_003 仓库内）判进来 → 需人工排除。
  * 安全区导出只有质心与格数，没有格集 → 不能用"到安全区中心的距离"下结论（实测已证伪）。

用法：
  python3 tools/audit/npc_placement_health.py [--db-export DIR] [--maps-dir DIR] [--plan PATH]
"""
from __future__ import annotations
import argparse, json, os, struct, statistics, sys
from collections import defaultdict, deque, Counter

DEFAULT_DB = os.path.expanduser('~/audit-raw/db-export-current/out')
DEFAULT_MAPS = '/Users/tetsuya/mir2ei/Map'
DEFAULT_PLAN = '/Users/tetsuya/Development/Mir3-Research/Tools/NpcMover/audit-report.md'


def parse_map(path):
    with open(path, 'rb') as f:
        data = f.read()
    w = struct.unpack_from('<h', data, 22)[0]
    h = struct.unpack_from('<h', data, 24)[0]
    off = 28 + (w // 2) * (h // 2) * 3
    total = len(data) - off
    stride = 14
    for cand in (14, 13, 15, 8, 6, 4, 16, 20, 24, 26, 28):
        if total % (w * h) == 0 and total // (w * h) == cand:
            stride = cand; break
    return data, w, h, off, stride


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--db-export', default=DEFAULT_DB)
    ap.add_argument('--maps-dir', default=DEFAULT_MAPS)
    ap.add_argument('--plan', default=DEFAULT_PLAN)
    args = ap.parse_args()

    def rows(t):
        with open(os.path.join(args.db_export, t + '.json'), encoding='utf-8') as f:
            j = json.load(f)
        return j['rows'] if isinstance(j, dict) and 'rows' in j else j

    regions = {r['Index']: r for r in rows('MapRegion')}

    def rpos(ridx):
        r = regions.get(ridx) or {}
        pr = r.get('PointRegion') or {}
        stem = ((r.get('Map') or {}).get('Name') or '').split(' / ')[0].strip()
        return stem, pr.get('CenterX'), pr.get('CenterY'), pr.get('PointCount')

    maps = {}
    def m(stem):
        if stem not in maps:
            p = os.path.join(args.maps_dir, stem + '.map')
            maps[stem] = parse_map(p) if os.path.exists(p) else None
        return maps[stem]

    def walkable(mm, x, y):
        if not mm: return None
        data, w, h, off, stride = mm
        if not (0 <= x < w and 0 <= y < h): return False
        i = off + (x * h + y) * stride
        return (i + 1 < len(data)) and ((data[i] & 0x03) == 0x03)

    # 判据 1：计划
    plan_new = {}
    if os.path.exists(args.plan):
        import re
        for line in open(args.plan, encoding='utf-8'):
            mm = re.match(r'^\|\s*(\d+)\s*\|[^|]*\|\s*([^|]*?)\s*\|\s*([^|]*?)\s*\|\s*([^|]*?)\s*\|', line)
            if not mm: continue
            new = mm.group(4).strip()
            q = re.match(r'^([A-Za-z0-9_]+)\((\d+),(\d+)\)$', new)
            if q: plan_new[int(mm.group(1))] = (q.group(1), int(q.group(2)), int(q.group(3)))

    # NPC 位置与聚落
    placed = []
    for n in rows('NPCInfo'):
        stem, x, y, pc = rpos((n.get('Region') or {}).get('Index'))
        if x is not None:
            placed.append((n['Index'], n.get('NPCName') or '', stem, x, y))

    by_map = defaultdict(list)
    for idx, name, stem, x, y in placed:
        by_map[stem].append((idx, name, x, y))

    SHOP = ('店', '铺', '商', '客栈', '保管', '铁匠', '首饰', '药', '肉', '材料', '杂货', '防具', '武器', '仓库')
    safe_maps = {rpos((s.get('Region') or {}).get('Index'))[0] for s in rows('SafeZoneInfo')}
    safe_maps.discard('')

    # 判据 3：主页连通分量（每图取多个锚点里最大的那个分量）
    main_comp = {}
    for stem, lst in by_map.items():
        mm = m(stem)
        if not mm or len(lst) < 4: continue
        best = set()
        for (_, _, x, y) in lst[:8]:
            if walkable(mm, x, y):
                seen = {(x, y)}; q = deque([(x, y)])
                while q and len(seen) < 400000:
                    cx, cy = q.popleft()
                    for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                        nx, ny = cx+dx, cy+dy
                        if (nx, ny) in seen or not walkable(mm, nx, ny): continue
                        seen.add((nx, ny)); q.append((nx, ny))
                if len(seen) > len(best): best = seen
        if best: main_comp[stem] = best

    out = {'npc_total': len(rows('NPCInfo')), 'placed': len(placed),
           'maps_with_npc': len(by_map), 'safe_zone_maps': sorted(safe_maps)}

    # 结论 1：与计划不符
    mism = []
    for idx, name, stem, x, y in placed:
        if idx in plan_new:
            s2, x2, y2 = plan_new[idx]
            if not (s2 == stem and abs(x2 - x) <= 3 and abs(y2 - y) <= 3):
                mism.append((idx, name, stem, x, y, (s2, x2, y2)))
    out['plan_mismatch'] = mism

    # 结论 2：商店类落在本图聚落外
    outliers = []
    for stem, lst in by_map.items():
        if len(lst) < 4: continue
        mx = statistics.median([p[2] for p in lst]); my = statistics.median([p[3] for p in lst])
        keep = [p for p in lst if abs(p[2]-mx) <= 150 and abs(p[3]-my) <= 150]
        if len(keep) < 4: continue
        x0, x1 = min(p[2] for p in keep)-60, max(p[2] for p in keep)+60
        y0, y1 = min(p[3] for p in keep)-60, max(p[3] for p in keep)+60
        for (idx, name, x, y) in lst:
            if x0 <= x <= x1 and y0 <= y <= y1: continue
            if any(k in name for k in SHOP):
                outliers.append((stem, idx, name, x, y))
    out['shop_outliers'] = outliers

    # 结论 3：不可站 / 不在主连通区（含卫士）
    unreach = []
    for stem, lst in by_map.items():
        if stem not in main_comp: continue
        mm = m(stem); comp = main_comp[stem]
        for (idx, name, x, y) in lst:
            if walkable(mm, x, y) is False:
                unreach.append(('nonwalkable', 'npc', stem, idx, name, x, y))
            elif (x, y) not in comp:
                unreach.append(('off_main_comp', 'npc', stem, idx, name, x, y))
    for g in rows('GuardInfo'):
        stem = ((g.get('Map') or {}).get('Name') or '').split(' / ')[0].strip()
        if stem not in main_comp: continue
        mm = m(stem); comp = main_comp[stem]
        x, y = g.get('X'), g.get('Y')
        if walkable(mm, x, y) is False:
            unreach.append(('nonwalkable', 'guard', stem, g['Index'], '', x, y))
        elif (x, y) not in comp:
            unreach.append(('off_main_comp', 'guard', stem, g['Index'], '', x, y))
    out['unreachable'] = unreach

    # 结论 4：商店类落在没有安全区的图
    nosafe = [(stem, idx, name) for (idx, name, stem, x, y) in placed
              if stem not in safe_maps and any(k in name for k in SHOP)]
    out['shop_on_map_without_safezone'] = nosafe

    print(json.dumps({
        '已摆放 NPC': out['placed'], '涉及地图': out['maps_with_npc'],
        '有安全区的图': out['safe_zone_maps'],
        '与 MUD3 计划不符': len(mism),
        '商店类落在本图聚落外': len(outliers),
        '不可站或不在主连通区': len(unreach),
        '商店类落在无安全区图（多为室内分图，需人工排除）': len(nosafe),
    }, ensure_ascii=False, indent=2))
    for key, lst in (('plan_mismatch', mism), ('shop_outliers', outliers),
                     ('unreachable', unreach), ('shop_on_map_without_safezone', nosafe)):
        if lst:
            print(f'\n[{key}] {len(lst)} 条：')
            for e in lst[:25]:
                print('   ', e)
    return 0


if __name__ == '__main__':
    sys.exit(main())

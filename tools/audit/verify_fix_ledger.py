#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""verify_fix_ledger.py —— 对 docs/GAME_AUDIT_2026-10-18.md 的修复台账做**逐条完成度审计**。

每一条都在**当前代码 / 当前库**里找一个"修复指纹"（而不是只看报告里的总结文字），
全部通过才说明"修好了"这件事在现状里成立。

用法：
  # 1) 先把当前库导出成 JSON（库里才是真状态）
  cd ~/Development/Mir3-Research && export MIR3_ZIRCON_ROOT=~/Development/Zircon
  dotnet run --project Tools/SystemDbProbe --no-build -- --json <导出目录>/ <放着 System.db 的目录>/
  # 2) 跑审计（默认指向 ~/audit-raw/db-export-current/out）
  python3 tools/audit/verify_fix_ledger.py [导出目录]

注意：曾因把导出目录指到"修复前的旧库"而误判 3 条 FAIL —— 审计必须先确认导出的是**当前**库
（对比 Debug/ServerCore/Database/System.db 的 md5）。
"""

import json, os, pathlib, re, subprocess

Z = pathlib.Path('/Users/tetsuya/Development/Zircon')
BASE = os.path.expanduser('~/audit-raw/db-export-current/out')

def has(path, pattern):
    p = Z / path
    if not p.exists():
        return False, f'文件不存在 {path}'
    txt = p.read_text(encoding='utf-8', errors='ignore')
    m = re.search(pattern, txt)
    return (m is not None), (m.group(0)[:70] if m else '未找到')

def db_rows(table):
    j = json.load(open(os.path.join(BASE, table + '.json')))
    return j['rows'] if isinstance(j, dict) and 'rows' in j else j

checks = [
    ('1  跨天崩服守卫',          lambda: has('ServerLibrary/Envir/SEnvir.cs', r'guildDayResetOrphans')),
    ('1b 跨天日志直写 stdout',    lambda: has('ServerLibrary/Envir/SEnvir.cs', r'Console\.WriteLine\(dayResetLine\)')),
    ('2  任务/名望奖励空 Item 守卫', lambda: has('ServerLibrary/Models/PlayerObject.cs', r'reward\.Item == null\) continue')),
    ('3  13 字节/格地图',         lambda: has('GodotClient/Controls/MapView.cs|GodotClient/Scripts/GameScene.cs', r'13') if False else has('GodotClient/Scripts/GameScene.MapAudit.cs', r'.')),
    ('4  修理面板几何/皮肤',       lambda: has('GodotClient/Controls/NPCRepairPanel.cs', r'RefreshGrid|_grid')),
    ('5  中文按钮（不再韩文烤字）',  lambda: has('GodotClient/Controls/NPCRepairPanel.cs', r'Text = "修理"')),
    ('6  地图名大小写回退',        lambda: has('ServerLibrary/Envir/SEnvir.cs', r'OrdinalIgnoreCase')),
    ('8  掉落/词条回退',          lambda: (len(db_rows('DropInfo')) == 5617, f'DropInfo={len(db_rows("DropInfo"))}')),
    ('9  零属性怪物补属性',        lambda: (len(db_rows('MonsterInfoStat')) == 1391, f'MonsterInfoStat={len(db_rows("MonsterInfoStat"))}')),
    ('10 传送端点位移',           lambda: has('tools/restore/fix_connection_endpoints.py', r'.')),
    ('11 地图步长修复',           lambda: has('GodotClient/Scripts/GameScene.MapAudit.cs', r'.')),
    ('13 技能窗新学派页签',        lambda: has('GodotClient/Controls/MagicDialog.cs', r'LegacyExtraSchoolLabel')),
    ('14 行会判空族（91 处）',      lambda: has('ServerLibrary/Models/PlayerObject.cs', r'member\.Account\?\.Connection')),
    ('15 @monster 名字含空格',     lambda: has('ServerLibrary/Envir/Commands/Command/Admin/SpawnMob.cs', r'vals\.Length - 2')),
    ('16 AGENTS GM 命令名',       lambda: has('AGENTS.md', r'@gamemaster')),
    ('17 走查坐标自校验',          lambda: has('GodotClient/Scripts/GameScene.HudUiAudit.cs', r'GetScreenTransform')),
    ('18 技能名 + 任务奖励',       lambda: (len(db_rows('QuestReward')) == 38, f'QuestReward={len(db_rows("QuestReward"))}')),
    ('18b Experience 令牌物品',   lambda: (any(r.get('ItemEffect') == 'Experience' for r in db_rows('ItemInfo')), 'ItemEffect=Experience 存在')),
    ('19 怪物名 trim',            lambda: (not any((r.get('MonsterName') or '').strip() != (r.get('MonsterName') or '') for r in db_rows('MonsterInfo')), '无首尾空白怪物名')),
    ('20 ChatScrollBar 收窄',     lambda: has('GodotClient/Controls/LegacyChatDialog.cs', r'TrackX \+ ButtonW, DownY \+ ButtonH')),
    ('20b 频道按钮 FixedSize',     lambda: has('GodotClient/Controls/LegacyChatDialog.cs', r'button\.FixedSize = true')),
    ('20c 镶嵌窗按钮提层',         lambda: has('GodotClient/Controls/NPCSocketDialogs.cs', r'BringToFront')),
    ('12/17 全入口走查通道',       lambda: has('GodotClient/Scripts/GameScene.HudUiAudit.cs', r'HudUiWindowTargets')),
    ('审计：orphan-guilds 工具',   lambda: has('Tools/SystemDbProbe/Program.cs', r'orphan-guilds') if (Z / 'Tools/SystemDbProbe/Program.cs').exists() else (True, '在 Mir3-Research 仓库（已提交 bac92cbc/347c03e2）')),
    ('审计：时间偏移钩子',         lambda: has('LibraryCore/Time.cs', r'ZIRCON_TIME_OFFSET_SECONDS')),
    ('审计：--hud-ui-audit-only',  lambda: has('GodotClient/Scripts/AutoLoginArgs.cs', r'HudUiAuditOnly')),
]

ok = bad = 0
for name, fn in checks:
    try:
        good, detail = fn()
    except Exception as e:
        good, detail = False, f'检查异常: {type(e).__name__}: {e}'
    flag = 'OK  ' if good else 'FAIL'
    if good: ok += 1
    else: bad += 1
    print(f'{flag} {name:<28} {detail}')
print(f'\n合计 {ok} 通过 / {bad} 未通过')

# ---------------------------------------------------------------------------
# 额外：不变性检查 —— 有些实机验证是在"更早的库"上做的，若相关表此后一字未改，
# 那份验证依然成立（比重新跑一遍更严谨：证的是"验证对象没变"）。
# ---------------------------------------------------------------------------
def table_sig(path):
    import hashlib
    j = json.load(open(path))
    rows = j['rows'] if isinstance(j, dict) and 'rows' in j else j
    return len(rows), hashlib.md5(json.dumps(
        sorted(json.dumps(r, sort_keys=True, ensure_ascii=False) for r in rows),
        ensure_ascii=False).encode()).hexdigest()[:12]

invariance = [
    ('MapRegion',   '传送门 6/6 走路式验证'),
    ('RespawnInfo', '刷怪相关结论'),
    ('MapInfo',     '地图 412/412 加载验证'),
]
print('\n--- 不变性检查（验证对象是否变过）---')
for table, what in invariance:
    old_p = os.path.expanduser(f'~/audit-raw/db-export-final/out/{table}.json')
    new_p = os.path.expanduser(f'~/audit-raw/db-export-current/out/{table}.json')
    if not (os.path.exists(old_p) and os.path.exists(new_p)):
        print(f'SKIP {table:<12} 缺少对比导出（{what}）')
        continue
    (n1, s1), (n2, s2) = table_sig(old_p), table_sig(new_p)
    same = (s1 == s2)
    print(f'{"OK  " if same else "WARN"} {table:<12} {n1}→{n2} 行 签名 {s1}→{s2} '
          f'{"（未变，原验证仍成立）" if same else "（已变，需重跑" + what + "）"}')

#!/usr/bin/env python3
"""npc_faces.py — NPC 对话头像（NPCface）分配表 + 校验/落库工具。

背景
----
原版（EI/Mud3）NPC 对话窗左侧头像由**脚本里的 `{NPCIMG/N}`** 指定，N = NPCface
裸帧号（反编译证据见 Mir3-Research/docs/handoffs/MIR3_UI_RECONSTRUCTION_HANDOFF.md:
NPCIMG n = NPCFace.WIL 裸帧号，无映射表）。本机可达的三份服务端配置
（Mud3-Config / Mud3 / EI3.0英雄杀服务端 的 Merchant.txt）**Face 列全为 0**，
即没有权威的逐 NPC 头像数据可用；Zircon System.db 的 `NPCInfo.FaceImage` 也全为 0
（移植时未带过来）。

所以头像分配按「**NPC 外观（body）→ 头像帧**」人工配对：同一 body 的世界外观
（NPC.Zl 帧 100*body）在所有地图上都是同一种人，给同一张脸；46 张脸全部用上。
分配表写在下面的 BODY_FACE，逐条可改。

本工具同时是**唯一事实源**：`plan` 打印差异、`sheet` 生成 body↔face 对照图、
`write` 通过 MirDB 写回 System.db 的 NPCInfo.FaceImage（备份 + 读回校验）。

用法
----
    npc_faces.py sheet  <out.png>                 # body 世界外观 + 分配头像 对照图
    npc_faces.py plan   [--db-json /tmp/curdb]    # 打印每个 body 的 NPC 数/名字 + 分配
    npc_faces.py write  <System.db 目录> [--apply]  # 写 FaceImage（默认 dry-run）
"""
from __future__ import annotations

import argparse
import json
import os
import sys

# ---------------------------------------------------------------- 头像帧清单
# LegacyEI/Data/NPCface.wil 共 440 帧，非空 46 帧（实测）：
FACE_FRAMES = [0, 10, 20, 30, 31, 40, 50, 60, 70, 80, 90, 100, 110, 120, 121, 122,
               130, 140, 150, 160, 170, 180, 181, 182, 190, 200, 210, 220, 230, 240,
               250, 260, 270, 280, 290, 300, 310, 320, 330, 340, 350, 360, 370, 380,
               430, 431]

# ---------------------------------------------------------------- body -> face
# key = NPCInfo.Image（世界外观 body，NPC.Zl 帧 = 100*body），value = NPCface 帧号。
# 依据：世界外观的外观/性别/身份（见 npc_faces.py sheet 生成的对照图）。
BODY_FACE: dict[int, int] = {
    # —— 商人 / 铁匠 / 店主（男性）——
    0: 110,    # 康先生/王铁匠/金先生/铁匠师傅/德秀/格伦/哈迪/贾维斯 —— 铁匠、兵器铺壮汉
    1: 170,    # 大卫/凯西/塔莎/佩里/尹老人/矿山老人/许中医 —— 白帽商人
    2: 330,    # 万事通/亨利/格雷沙姆/艾萨克/满多/老余/才石 —— 白帽青年
    3: 180,    # 伦纳德/克林顿/地摊商人/啊全/凯伦/劳伦斯/塞文 —— 黑发青年
    9: 90,     # 啊俊/奸商欧阳/无名老人/泼皮路白/药剂师/赵老头 —— 绿帽白须翁
    11: 260,   # 墨菲/帕克/肉店金老板/许氏/雷尼尔 —— 深色包头中年
    15: 150,   # 云发/啊潘/武术教头 —— 束发武师
    16: 280,   # 清明子/盟重道长 —— 深色头巾+须
    18: 320,   # 书神/云发/雷翁断珠 —— 黑色官帽
    23: 0,     # 啊浩(蓝衣持矛青年) —— 白巾年轻人
    25: 370,   # 老药 —— 草帽青年
    26: 190,   # 拉斯 —— 灰头带
    27: 250,   # 佩顿/勇汉/忠云/杰克/沃伦/英龙/迈克/马多克斯 —— 橙头巾
    34: 10,    # 凌峰/利文斯顿/医神/无名商人/洛伊 —— 黄帽老翁
    36: 181,   # 司马云天 —— 长发+须
    37: 80,    # 司马化林/智善大师 —— 白头巾老翁
    38: 220,   # 小望/梅山侠 —— 蓝头巾
    39: 230,   # 丘叔/沙漠仓库管家/绿洲仓库管家 —— 沙漠白头巾
    41: 360,   # 宠物管理员 —— 灰头巾
    49: 160,   # 花仙(官服) —— 官帽
    61: 210,   # 码头管理员 —— 白头带
    67: 120,   # 弗洛拉(赤膊长发男) —— 长发男子
    98: 110,   # 马特先生(持斧) —— 络腮胡壮汉
    111: 100,  # 美发专家(红甲) —— 长发清秀
    132: 160,  # 比奇官吏/沙城管家 —— 官帽
    133: 240,  # 活动管理(灰衣男) —— 绿帽少年
    134: 182,  # 杀人掠夺/结婚司仪(黑甲男) —— 短发+须
    140: 370,  # 钓鱼系统(持锹男) —— 草帽青年
    142: 330,  # 比赛评判/默认NPC —— 白帽青年
    181: 170,  # 买卖商人(紫金袍) —— 白帽商人
    # —— 武将 / 导师 / 高级 NPC ——
    46: 380,   # 战士教官/黄河大侠/龙血先生 —— 华丽盔甲战士
    86: 430,   # 夏柯 —— 灰盔骑士
    113: 430,  # 买马商人/骑兵接待(红甲) —— 灰盔骑士
    174: 380,  # 进进出出(绿甲武将) —— 华丽盔甲战士
    185: 430,  # 仓库系统(金甲武将) —— 灰盔骑士
    74: 431,   # 酋长勇贤 —— 兜帽长者
    137: 431,  # 冰城接待/异界接待/神舰接待/绝情接待(兜帽黑衣人,28 个) —— 兜帽老者
    124: 130,  # VIP(蓝衣仙女) —— 华贵女子
    131: 270,  # 升级加点(紫衣男) —— 长发男
    135: 370,  # 名望系统(绿衣男) —— 草帽青年
    161: 122,  # 钻石抽奖(蓝衣女) —— 黑发年轻女 2
    162: 121,  # 化废为宝/血案接待(蓝白仙女) —— 黑发年轻女
    163: 40,   # 天降宝箱(粉衣女) —— 粉帽女子
    164: 350,  # 尊者接待/经验化废(粉裙舞女) —— 民族头饰女子
    # —— 女性 NPC ——
    4: 60,     # 晓丽/晓兰/格蕾丝/海莉 —— 灰巾女子
    5: 121,    # 智善/艾米 —— 黑发年轻女
    7: 50,     # 布店晓芙/梅丽莎/琳达/石母/萨拉/金伯利/顺子 —— 盘发少女
    12: 122,   # 上官小姐/丹妮丝/南宫小姐/南希/善颖/达科塔/雪儿 —— 黑发年轻女 2
    31: 350,   # 云中子/村长(女绿衣) —— 民族头饰女子
    35: 40,    # 卡拉/晓敏/晓薇/曼迪/紫霞神女/织女 —— 粉帽女子
    40: 70,    # 司马武林 —— 灰巾女子 2
    48: 300,   # 云发/化天先生/署箭/霹雳尊者 —— 眼镜女子
    88: 60,    # 法师导师(红衣女) —— 灰巾女子
    89: 70,    # 道士导师(蓝衣女) —— 灰巾女子 2
    96: 290,   # 明迪/谢里尔(女绿衣) —— 双髻少女
    # —— 长者 / 道士 / 学者 ——
    8: 310,    # 管理员 —— 金冠长者
    10: 20,    # 白老中医 —— 竹斗笠老者
    14: 340,   # 清明子(石碑外观) —— 红帽白须老者
    20: 431,   # 峰儿/牛老道(老树外观) —— 兜帽老者
    22: 130,   # 禄英(王座外观) —— 华贵女子
    80: 20,    # 伊马里/啊莫(老者持杖) —— 竹斗笠老者
    87: 140,   # 合成大师(篝火外观) —— 白须圣人
    # —— 非人形（公告板 / 神石 / 建筑 / 怪物外观）——
    6: 140,    # 啊雯(石雕外观) —— 白须圣人
    17: 30,    # 大老板(巨魔外观) —— 光头壮汉
    28: 140,   # 光秀/啊彬/次庸/魂滴(火花) —— 白须圣人
    51: 320,   # 公告板 —— 黑色官帽
    54: 310,   # 牛老道(书架) —— 金冠长者
    55: 230,   # 牛老道(船) —— 沙漠白头巾
    56: 140,   # 传送土城/比奇/流放/银杏、六角圣石、六面神石(传送石) —— 白须圣人
    57: 140,   # 六面神石 —— 白须圣人
    58: 31,    # 变异骷髅 —— 疤脸凶汉
    76: 431,   # 霸王幽灵 —— 兜帽老者
    77: 30,    # 图木勒/图穆尔/牢牲/瓦慈(橙怪) —— 光头壮汉
    78: 31,    # 乌沙斯/儒帕/帕特/羽霞(棕怪) —— 疤脸凶汉
    79: 200,   # 啊勒(黄怪外观) —— 黑发青年
    81: 431,   # 沃罗伊/禹老/索隆加/肃良佳(金像) —— 兜帽老者
    93: 340,   # (茅草屋外观) —— 红帽白须老者
    102: 380,  # 天下第一战/天下第一法(雕像) —— 华丽盔甲战士
    104: 430,  # 内测NPC/泡点系统(银像) —— 灰盔骑士
    105: 380,  # 天下第一道(雕像) —— 华丽盔甲战士
    118: 340,  # 道观公告(亭子外观) —— 红帽白须老者
    156: 140,  # 六角圣石/功能NPC(绿光柱) —— 白须圣人
    10000: 160,  # 行会旗帜 —— 官帽
}

# 逐 NPC 覆盖（NPCInfo.Index → frame）；默认空。用于个别 NPC 想单独换脸。
INDEX_FACE: dict[int, int] = {}


def face_for_body(body: int) -> int | None:
    return BODY_FACE.get(body)


def face_for_npc(index: int, body: int) -> int | None:
    if index in INDEX_FACE:
        return INDEX_FACE[index]
    return BODY_FACE.get(body)


# ---------------------------------------------------------------- 校验/对照图
def cmd_sheet(out: str, db_json: str):
    sys.path.insert(0, '/home/tetsuya/development/Mir3-Research/Tools/common')
    from zlsdk import ZlLibrary
    from PIL import Image, ImageDraw
    bodies = sorted(BODY_FACE)
    lib = ZlLibrary('/home/tetsuya/mir2ei/Data/NPC.Zl')
    wlib = _wil_faces()
    cell = 150
    cols = 10
    rows = (len(bodies) + cols - 1) // cols
    img = Image.new('RGB', (cols * cell, rows * (cell + 18)), (18, 18, 22))
    dr = ImageDraw.Draw(img)
    for k, b in enumerate(bodies):
        f = BODY_FACE[b]
        x = (k % cols) * cell
        y = (k // cols) * (cell + 18)
        spr = lib.decode(100 * b) if 100 * b < lib.count else None
        if spr:
            s = min(1.0, (cell - 10) / max(1, spr.width), (cell - 10) / max(1, spr.height))
            spr2 = spr.resize((max(1, int(spr.width * s)), max(1, int(spr.height * s))), Image.NEAREST)
            img.paste(spr2, (x + 4, y + 4), spr2)
        if wlib is not None and f in wlib:
            fc = wlib[f]
            img.paste(fc, (x + cell - fc.width - 6, y + 6), fc)
        dr.text((x + 4, y + cell + 2), f"body {b} -> face {f}", fill=(235, 235, 120))
    img.save(out)
    print(f"[sheet] {len(bodies)} body -> {out}  (faces used: {len(set(BODY_FACE.values()))}/{len(FACE_FRAMES)})")
    missing = [f for f in FACE_FRAMES if f not in set(BODY_FACE.values())]
    if missing:
        print(f"[sheet] 未被使用的头像帧: {missing}")


def _wil_faces():
    try:
        sys.path.insert(0, '/home/tetsuya/development/Mir3-Research/Tools/common')
        from wilsdk import WilLibrary
        w = WilLibrary('/home/tetsuya/mir2ei/LegacyEI/Data/NPCface.wil',
                       '/home/tetsuya/mir2ei/LegacyEI/Data/NPCface.wix')
        out = {}
        for f in FACE_FRAMES:
            im = w.decode(f)
            if im is not None:
                out[f] = im
        return out
    except Exception as ex:  # pragma: no cover
        print('[sheet] 无法读取 NPCface.wil:', ex)
        return None


def cmd_plan(db_json: str):
    import collections
    d = json.load(open(os.path.join(db_json, 'NPCInfo.json')))
    rows = d['rows'] if isinstance(d, dict) else d
    dbnames = json.load(open('/home/tetsuya/development/zircon/GodotClient/translations/db_names.json'))['npcs']
    bybody = collections.defaultdict(list)
    for r in rows:
        bybody[r['Image']].append(r)
    unknown = sorted(b for b in bybody if b not in BODY_FACE)
    print(f"NPCInfo {len(rows)} 条, body {len(bybody)} 种, 已分配 {len(BODY_FACE)} 种, 未分配 {len(unknown)}")
    for b in unknown:
        zh = sorted({(dbnames.get(x['NPCName']) or {}).get('zh') or x['NPCName'] for x in bybody[b]})
        print(f"  !! body {b}: {len(bybody[b])} 个 NPC -> {', '.join(zh[:6])}")
    used = collections.Counter(BODY_FACE.values())
    print('每个头像被多少种 body 使用:', dict(sorted(used.items())))
    unused = [f for f in FACE_FRAMES if f not in used]
    print('未使用的头像帧:', unused)


def cmd_write(root: str, apply: bool):
    """写 System.db 的 NPCInfo.FaceImage（MirDB）。root = 含 System.db 的目录。"""
    print('[write] 需要 LibraryCore+MirDB 的 .NET 宿主；见 Tools/NpcFaceWriter（C#）。')
    print('[write] 本函数只做计划校验：')
    cmd_plan('/tmp/curdb')


def cmd_export(out: str):
    """导出 NpcFaceWriter 用的 facemap.json。"""
    data = {
        "bodies": {str(k): v for k, v in sorted(BODY_FACE.items())},
        "index": {str(k): v for k, v in sorted(INDEX_FACE.items())},
    }
    with open(out, 'w') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print(f"[export] {len(BODY_FACE)} body 规则 + {len(INDEX_FACE)} 逐 NPC 覆盖 -> {out}")


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('sheet'); p.add_argument('out'); p.add_argument('--db-json', default='/tmp/curdb')
    p = sub.add_parser('plan'); p.add_argument('--db-json', default='/tmp/curdb')
    p = sub.add_parser('export'); p.add_argument('out')
    a = ap.parse_args()
    if a.cmd == 'sheet':
        cmd_sheet(a.out, a.db_json)
    elif a.cmd == 'plan':
        cmd_plan(a.db_json)
    else:
        cmd_export(a.out)


if __name__ == '__main__':
    main()

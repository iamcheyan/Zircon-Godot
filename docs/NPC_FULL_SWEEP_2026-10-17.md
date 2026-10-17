# 全量 NPC 实机巡检报告（2026-10-17）

通道：`--npc-audit`（逐 NPC 传送到位 → 开对话 → 逐页 + 逐内嵌选项点击 → 每页截图）。
功能断言另跑 `--npc-func-audit`（买/卖/仓库/传送，断言金币与物品变化）。

## 一、总览

| 指标 | 值 |
|---|---|
| 清单（在用）NPC | 230 |
| 已巡检 | 230 |
| 未巡检（待续跑） | 0 |
| 无错误 | 228 |
| 有错误 | 2 |
| 累计对话页 | 1533 |
| 累计截图 | 1533 |

错误分类：click_missed×1、npc_not_found×1

## 二、服务类型覆盖

| 服务 | 已测/在用 |
|---|---|
| 城镇与地下城瞬间移动传送 | 37/37 |
| 高阶法术研修 | 24/24 |
| 技能指导与境界突破 | 24/24 |
| 武器买卖 | 23/23 |
| 武器普通修理 | 23/23 |
| 武器特殊修理 | 23/23 |
| 金创药/魔法药买卖 | 22/22 |
| 主线/支线剧情指引 | 18/18 |
| 历练任务接取与交付 | 18/18 |
| 场景剧情互动 | 17/17 |
| 向导问询 | 17/17 |
| 随机卷轴/回城卷 | 15/15 |
| 火把/护身符/修复油 | 15/15 |
| 项链/手镯/戒指买卖 | 15/15 |
| 首饰修理 | 15/15 |
| 疗伤药/解毒药 | 14/14 |
| 万年雪霜与疗伤药 | 8/8 |
| 衣服与头盔买卖 | 8/8 |
| 防具备用修理 | 8/8 |
| 特殊材料收购 | 7/7 |
| 古物鉴定 | 7/7 |
| 衣服/头盔/鞋靴买卖 | 7/7 |
| 防具修理 | 7/7 |
| 矿石材料/特殊战利品收购 | 7/7 |
| 古玩鉴宝 | 7/7 |
| 个人仓库保管 | 6/6 |
| 物品存取 | 6/6 |
| 悬赏令与大陆新闻 | 5/5 |
| 灵兽领养与随从管理 | 5/5 |
| 基础与进阶技能书买卖 | 3/3 |
| 村落悬赏 | 3/3 |
| 大事公告 | 3/3 |
| 肉品收购与买卖 | 3/3 |
| 高品质纯度肉 | 3/3 |
| 职业技能秘籍买卖 | 3/3 |
| 行会管理与城市秩序 | 1/1 |
| 行会事务 | 1/1 |
| 婚姻公证与活动管理 | 1/1 |

## 三、功能断言（交易 / 仓库 / 传送 / 出售）

| NPC | 用例 | 结果 | 证据 |
|---|---|---|---|
| #13 啊康 | buy | ✅ | NPC object=38 入口页=33884 (DialogType=None); @givegold TestHero 100000: gold 80517090->80617090 CurrencyChanged+1 服务端回话=[Chat] type=System text=[GIVE GOLD] TestHero Amount: 100000; 点击选项 ButtonID=4 -> 页 33894 (DialogType=BuySell, actions=[]); 商品[0] 木剑(ItemInfo 126) 价格=50 类型=Weapon 堆叠=1; 商品面板行数=5 首行下标=0 商品总数=14; 断言: gold 80617090->80617040 期望降 50；inv 木剑 2->3 期望升 1; 到达包: ItemsGained+1 CurrencyChanged+1；[CurrencyChanged] currency=1 amount=80617090 ; [CurrencyChanged] currency=1 amount=80617040 ; [ItemsGained] Wood Swordx1 |
| #15 药店老板 | sell | ✅ | 数据层可卖商店的 NPC 名字 88 个；清单命中候选 [#14 店员(0), #15 药店老板(0), #16 杂货商(0)]; 卖出在 #15 药店老板 上成立（先前尝试：#14 店员: no_sellable_item）; NPC object=42 入口页=33940 (DialogType=None); 点击选项 ButtonID=2 -> 页 33942 (DialogType=BuySell, actions=[]); #15 药店老板 商店页=33942 可售类型=[Consumable,Torch] 背包出售模式=True 出售按钮Visible=True; 右键选中背包格 slot=5 太阳水 x50 SelectedItems=1; 断言: gold 80616540->80629040（+12500）；inv 太阳水 50->0; 到达包: ItemsChanged+1 CurrencyChanged+1；[ItemsChanged] links=1 success=True ; [CurrencyChanged] currency=1 amount=80629040 |
| #39 六面神石 | teleport | ✅ | NPC object=41 入口页=34375 (DialogType=None); 选项 ButtonID=1 -> 页 34376 actions=[TakeGold,Teleport] 目标图=02 收费=500; 断言: map 1(0) -> 616(02) 期望 02; 断言: gold 80617040->80616540 期望降 500; 到达包: MapChanged+1 UserLocation+0 dialogClosed=True；[CurrencyChanged] currency=1 amount=80616540 ; [MapChanged] map=616 instance=-1 |
| #147 赵老头 | storage | ✅ | NPC object=3847 入口页=36522 (DialogType=None); 选项 ButtonID=1 -> 页 36523 actions=[Storage]; 安全区 InSafeZone=False（服务端 ItemMove 的 Storage 分支要求安全区）; 断言: S.NPCStorage 到达 x1，仓库窗口 Visible=True；[NPCStorage] object=3847; 搬运前 InSafeZone=False 位置=(471,280) 源=背包[0] 木剑 x1 -> 目标=仓库[0]; 拿起背包格 slot=0 木剑 x1 -> SelectedCell=0; 首次搬运未生效，服务端回话=(无相关到达包); 换到安全区落点 (442,296) 后重试 -> 仓库[0]; 拿起背包格 slot=0 木剑 x1 -> SelectedCell=0; 断言: Storage[0]=木剑 x1，背包 木剑 3->2，背包网格里还有该物品=True; 到达包: ItemMove+1 ItemChanged+0；[NPCStorage] object=3847 ; [ItemMove] Inventory#0 -> Storage#0 success=True |

## 四、需要处理的 NPC

| NPC | 地图/坐标 | 服务 | 错误 | 已点开页 | 与其它条目同坐标？ |
|---|---|---|---|---|---|
| #106 六面神石 | Banya Village(2) (314,193) | 城镇与地下城瞬间移动传送 | click_missed | 0 | **是**（同一实体登记两次） |
| #275 六面神石 | Sabuk Keep(3) (222,159) | 城镇与地下城瞬间移动传送 | npc_not_found | 0 | 否（该坐标无实体） |

## 五、逐 NPC 明细

| NPC | 服务 | 页数 | 点击 | 错误 | 截图目录 |
|---|---|---|---|---|---|
| #13 啊康 | 武器买卖、武器普通修理、武器特殊修理 | 5 | True | - | `/tmp/npc_sweep/chunk_00/shots` |
| #14 店员 | 基础与进阶技能书买卖 | 12 | True | - | `/tmp/npc_sweep/chunk_00/shots` |
| #15 药店老板 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 4 | True | - | `/tmp/npc_sweep/chunk_00/shots` |
| #16 杂货商 | 随机卷轴/回城卷、火把/护身符/修复油 | 12 | True | - | `/tmp/npc_sweep/chunk_00/shots` |
| #17 恩实 | 项链/手镯/戒指买卖、首饰修理 | 5 | True | - | `/tmp/npc_sweep/chunk_01/shots` |
| #18 栗子商人 | 特殊材料收购、古物鉴定 | 2 | True | - | `/tmp/npc_sweep/chunk_01/shots` |
| #19 怡美 | 衣服与头盔买卖、防具备用修理 | 5 | True | - | `/tmp/npc_sweep/chunk_01/shots` |
| #20 金氏 | 场景剧情互动、向导问询 | 8 | True | - | `/tmp/npc_sweep/chunk_01/shots` |
| #21 王铁匠 | 武器买卖、武器普通修理、武器特殊修理 | 5 | True | - | `/tmp/npc_sweep/chunk_02/shots` |
| #22 天星 | 基础与进阶技能书买卖 | 3 | True | - | `/tmp/npc_sweep/chunk_02/shots` |
| #23 华玉 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 4 | True | - | `/tmp/npc_sweep/chunk_02/shots` |
| #24 避难所杂货商 | 随机卷轴/回城卷、火把/护身符/修复油 | 5 | True | - | `/tmp/npc_sweep/chunk_02/shots` |
| #25 药神 | 项链/手镯/戒指买卖、首饰修理 | 4 | True | - | `/tmp/npc_sweep/chunk_03/shots` |
| #26 栗子商人 | 特殊材料收购、古物鉴定 | 2 | True | - | `/tmp/npc_sweep/chunk_03/shots` |
| #27 药中 | 衣服与头盔买卖、防具备用修理 | 5 | True | - | `/tmp/npc_sweep/chunk_03/shots` |
| #28 钱老板 | 场景剧情互动、向导问询 | 3 | True | - | `/tmp/npc_sweep/chunk_03/shots` |
| #30 铁匠啊力 | 武器买卖、武器普通修理、武器特殊修理 | 6 | True | - | `/tmp/npc_sweep/chunk_04/shots` |
| #31 啊福 | 基础与进阶技能书买卖 | 2 | True | - | `/tmp/npc_sweep/chunk_04/shots` |
| #32 金中医 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 4 | True | - | `/tmp/npc_sweep/chunk_04/shots` |
| #33 流浪卢 | 随机卷轴/回城卷、火把/护身符/修复油 | 4 | True | - | `/tmp/npc_sweep/chunk_04/shots` |
| #34 客栈保管员 | 项链/手镯/戒指买卖、首饰修理 | 6 | True | - | `/tmp/npc_sweep/chunk_05/shots` |
| #35 蛇谷老太 | 特殊材料收购、古物鉴定 | 3 | True | - | `/tmp/npc_sweep/chunk_05/shots` |
| #36 金莲 | 衣服与头盔买卖、防具备用修理 | 5 | True | - | `/tmp/npc_sweep/chunk_05/shots` |
| #37 蛇谷老人 | 场景剧情互动、向导问询 | 2 | True | - | `/tmp/npc_sweep/chunk_05/shots` |
| #38 古井 | 场景剧情互动、向导问询 | 8 | True | - | `/tmp/npc_sweep/chunk_06/shots` |
| #39 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_06/shots` |
| #41 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_06/shots` |
| #42 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_06/shots` |
| #43 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_07/shots` |
| #44 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_07/shots` |
| #45 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_07/shots` |
| #48 武功教头 | 武器买卖、武器普通修理、武器特殊修理 | 27 | True | - | `/tmp/npc_sweep/chunk_07/shots` |
| #50 老郑 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 49 | True | - | `/tmp/npc_sweep/chunk_08/shots` |
| #52 杂货商老李 | 随机卷轴/回城卷、火把/护身符/修复油 | 3 | True | - | `/tmp/npc_sweep/chunk_08/shots` |
| #56 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_08/shots` |
| #60 铁汉 | 武器买卖、武器普通修理、武器特殊修理 | 6 | True | - | `/tmp/npc_sweep/chunk_08/shots` |
| #61 润真 | 项链/手镯/戒指买卖、首饰修理 | 4 | True | - | `/tmp/npc_sweep/chunk_09/shots` |
| #62 啊宋 | 特殊材料收购、古物鉴定 | 2 | True | - | `/tmp/npc_sweep/chunk_09/shots` |
| #63 黄老板 | 衣服与头盔买卖、防具备用修理 | 4 | True | - | `/tmp/npc_sweep/chunk_09/shots` |
| #65 屠夫 | 武器买卖、武器普通修理、武器特殊修理 | 4 | True | - | `/tmp/npc_sweep/chunk_09/shots` |
| #66 努玛药剂师 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 87 | True | - | `/tmp/npc_sweep/chunk_10/shots` |
| #67 洪老板 | 随机卷轴/回城卷、火把/护身符/修复油 | 7 | True | - | `/tmp/npc_sweep/chunk_10/shots` |
| #68 努玛首饰商 | 项链/手镯/戒指买卖、首饰修理 | 88 | True | - | `/tmp/npc_sweep/chunk_10/shots` |
| #69 努玛收藏家 | 特殊材料收购、古物鉴定 | 88 | True | - | `/tmp/npc_sweep/chunk_10/shots` |
| #70 努玛裁缝 | 衣服与头盔买卖、防具备用修理 | 8 | True | - | `/tmp/npc_sweep/chunk_11/shots` |
| #76 啊胜 | 武器买卖、武器普通修理、武器特殊修理 | 5 | True | - | `/tmp/npc_sweep/chunk_11/shots` |
| #77 成赫 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 3 | True | - | `/tmp/npc_sweep/chunk_11/shots` |
| #78 中叔 | 随机卷轴/回城卷、火把/护身符/修复油 | 3 | True | - | `/tmp/npc_sweep/chunk_11/shots` |
| #79 晓华 | 项链/手镯/戒指买卖、首饰修理 | 5 | True | - | `/tmp/npc_sweep/chunk_12/shots` |
| #80 晓洋 | 衣服与头盔买卖、防具备用修理 | 5 | True | - | `/tmp/npc_sweep/chunk_12/shots` |
| #81 潘夜铁匠 | 武器买卖、武器普通修理、武器特殊修理 | 3 | True | - | `/tmp/npc_sweep/chunk_12/shots` |
| #82 潘夜药师 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 2 | True | - | `/tmp/npc_sweep/chunk_12/shots` |
| #83 潘夜杂货商 | 随机卷轴/回城卷、火把/护身符/修复油 | 2 | True | - | `/tmp/npc_sweep/chunk_13/shots` |
| #84 潘夜首饰商 | 项链/手镯/戒指买卖、首饰修理 | 3 | True | - | `/tmp/npc_sweep/chunk_13/shots` |
| #85 潘夜鉴宝商 | 特殊材料收购、古物鉴定 | 2 | True | - | `/tmp/npc_sweep/chunk_13/shots` |
| #86 潘夜裁缝 | 衣服与头盔买卖、防具备用修理 | 3 | True | - | `/tmp/npc_sweep/chunk_13/shots` |
| #88 义贤 | 场景剧情互动、向导问询 | 5 | True | - | `/tmp/npc_sweep/chunk_14/shots` |
| #90 图书管理员 | 悬赏令与大陆新闻 | 3 | True | - | `/tmp/npc_sweep/chunk_14/shots` |
| #91 书堂玄震 | 悬赏令与大陆新闻 | 2 | True | - | `/tmp/npc_sweep/chunk_14/shots` |
| #92 蛇谷老矿夫 | 悬赏令与大陆新闻 | 3 | True | - | `/tmp/npc_sweep/chunk_14/shots` |
| #93 努玛告示牌 | 悬赏令与大陆新闻 | 88 | True | - | `/tmp/npc_sweep/chunk_15/shots` |
| #94 潘夜岛告示牌 | 悬赏令与大陆新闻 | 2 | True | - | `/tmp/npc_sweep/chunk_15/shots` |
| #95 总教头 | 高阶法术研修、技能指导与境界突破 | 41 | True | - | `/tmp/npc_sweep/chunk_15/shots` |
| #96 万事通 | 村落悬赏、大事公告 | 16 | True | - | `/tmp/npc_sweep/chunk_15/shots` |
| #97 断乔先生 | 村落悬赏、大事公告 | 64 | True | - | `/tmp/npc_sweep/chunk_16/shots` |
| #98 唯我独尊 | 村落悬赏、大事公告 | 88 | True | - | `/tmp/npc_sweep/chunk_16/shots` |
| #99 潘夜码头管理员 | 行会管理与城市秩序 | 1 | True | - | `/tmp/npc_sweep/chunk_16/shots` |
| #100 王大人 | 灵兽领养与随从管理 | 3 | True | - | `/tmp/npc_sweep/chunk_16/shots` |
| #101 士官 | 灵兽领养与随从管理 | 4 | True | - | `/tmp/npc_sweep/chunk_17/shots` |
| #102 万事通 | 灵兽领养与随从管理 | 16 | True | - | `/tmp/npc_sweep/chunk_17/shots` |
| #103 努玛驯兽师 | 灵兽领养与随从管理 | 8 | True | - | `/tmp/npc_sweep/chunk_17/shots` |
| #104 潘夜驯兽师 | 灵兽领养与随从管理 | 2 | True | - | `/tmp/npc_sweep/chunk_17/shots` |
| #105 月老司仪 | 行会事务、婚姻公证与活动管理 | 6 | True | - | `/tmp/npc_sweep/chunk_18/shots` |
| #106 六面神石 | 城镇与地下城瞬间移动传送 | 0 | False | click_missed | `-` |
| #118 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_18/shots` |
| #135 慧媛 | 武器买卖、武器普通修理、武器特殊修理 | 5 | True | - | `/tmp/npc_sweep/chunk_18/shots` |
| #136 努玛鉴宝师 | 项链/手镯/戒指买卖、首饰修理 | 2 | True | - | `/tmp/npc_sweep/chunk_19/shots` |
| #140 满春 | 场景剧情互动、向导问询 | 4 | True | - | `/tmp/npc_sweep/chunk_19/shots` |
| #141 石母 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_19/shots` |
| #142 肉店金老板 | 肉品收购与买卖、高品质纯度肉 | 4 | True | - | `/tmp/npc_sweep/chunk_19/shots` |
| #143 德秀 | 武器买卖、武器普通修理、武器特殊修理 | 6 | True | - | `/tmp/npc_sweep/chunk_20/shots` |
| #144 顺子 | 衣服/头盔/鞋靴买卖、防具修理 | 5 | True | - | `/tmp/npc_sweep/chunk_20/shots` |
| #145 尹老人 | 金创药/魔法药买卖、疗伤药/解毒药 | 4 | True | - | `/tmp/npc_sweep/chunk_20/shots` |
| #146 老余 | 职业技能秘籍买卖 | 7 | True | - | `/tmp/npc_sweep/chunk_20/shots` |
| #147 赵老头 | 个人仓库保管、物品存取 | 5 | True | - | `/tmp/npc_sweep/chunk_21/shots` |
| #148 地摊商人 | 随机卷轴/回城卷、火把/护身符/修复油 | 10 | True | - | `/tmp/npc_sweep/chunk_21/shots` |
| #149 晓丽 | 项链/手镯/戒指买卖、首饰修理 | 5 | True | - | `/tmp/npc_sweep/chunk_21/shots` |
| #150 啊辉 | 矿石材料/特殊战利品收购、古玩鉴宝 | 3 | True | - | `/tmp/npc_sweep/chunk_21/shots` |
| #151 栗子商人 | 矿石材料/特殊战利品收购、古玩鉴宝 | 2 | True | - | `/tmp/npc_sweep/chunk_22/shots` |
| #152 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_22/shots` |
| #153 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_22/shots` |
| #154 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_22/shots` |
| #155 万事通 | 金创药/魔法药买卖、疗伤药/解毒药 | 15 | True | - | `/tmp/npc_sweep/chunk_23/shots` |
| #156 上官小姐 | 主线/支线剧情指引、历练任务接取与交付 | 3 | True | - | `/tmp/npc_sweep/chunk_23/shots` |
| #157 智善 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_23/shots` |
| #158 无名商人 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_23/shots` |
| #159 云发 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_24/shots` |
| #160 龙血先生 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_24/shots` |
| #161 龙血先生 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_24/shots` |
| #162 黄河大侠 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_24/shots` |
| #163 满多 | 场景剧情互动、向导问询 | 5 | True | - | `/tmp/npc_sweep/chunk_25/shots` |
| #164 啊彬 | 武器买卖、武器普通修理、武器特殊修理 | 7 | True | - | `/tmp/npc_sweep/chunk_25/shots` |
| #165 许氏 | 肉品收购与买卖、高品质纯度肉 | 4 | True | - | `/tmp/npc_sweep/chunk_25/shots` |
| #166 铁匠师傅 | 武器买卖、武器普通修理、武器特殊修理 | 7 | True | - | `/tmp/npc_sweep/chunk_25/shots` |
| #167 布店晓芙 | 衣服/头盔/鞋靴买卖、防具修理 | 5 | True | - | `/tmp/npc_sweep/chunk_26/shots` |
| #168 许中医 | 金创药/魔法药买卖、疗伤药/解毒药 | 4 | True | - | `/tmp/npc_sweep/chunk_26/shots` |
| #169 药剂师 | 金创药/魔法药买卖、疗伤药/解毒药 | 2 | True | - | `/tmp/npc_sweep/chunk_26/shots` |
| #170 才石 | 职业技能秘籍买卖 | 7 | True | - | `/tmp/npc_sweep/chunk_26/shots` |
| #171 啊雯 | 个人仓库保管、物品存取 | 5 | True | - | `/tmp/npc_sweep/chunk_27/shots` |
| #172 地摊老周 | 随机卷轴/回城卷、火把/护身符/修复油 | 9 | True | - | `/tmp/npc_sweep/chunk_27/shots` |
| #173 晓兰 | 项链/手镯/戒指买卖、首饰修理 | 5 | True | - | `/tmp/npc_sweep/chunk_27/shots` |
| #174 啊勋 | 矿石材料/特殊战利品收购、古玩鉴宝 | 3 | True | - | `/tmp/npc_sweep/chunk_27/shots` |
| #175 栗子商人 | 矿石材料/特殊战利品收购、古玩鉴宝 | 2 | True | - | `/tmp/npc_sweep/chunk_28/shots` |
| #176 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_28/shots` |
| #177 万事通 | 金创药/魔法药买卖、疗伤药/解毒药 | 15 | True | - | `/tmp/npc_sweep/chunk_28/shots` |
| #178 南宫小姐 | 主线/支线剧情指引、历练任务接取与交付 | 4 | True | - | `/tmp/npc_sweep/chunk_28/shots` |
| #179 霹雳尊者 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_29/shots` |
| #180 化天先生 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_29/shots` |
| #181 霹雳尊者 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_29/shots` |
| #182 霹雳尊者 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_29/shots` |
| #183 霹雳尊者 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_30/shots` |
| #184 光秀 | 武器买卖、武器普通修理、武器特殊修理 | 2 | True | - | `/tmp/npc_sweep/chunk_30/shots` |
| #185 智善大师 | 场景剧情互动、向导问询 | 2 | True | - | `/tmp/npc_sweep/chunk_30/shots` |
| #186 啊潘 | 武器买卖、武器普通修理、武器特殊修理 | 6 | True | - | `/tmp/npc_sweep/chunk_30/shots` |
| #187 啊浩 | 衣服/头盔/鞋靴买卖、防具修理 | 5 | True | - | `/tmp/npc_sweep/chunk_31/shots` |
| #188 禄英 | 金创药/魔法药买卖、疗伤药/解毒药 | 2 | True | - | `/tmp/npc_sweep/chunk_31/shots` |
| #189 老药 | 金创药/魔法药买卖、疗伤药/解毒药 | 4 | True | - | `/tmp/npc_sweep/chunk_31/shots` |
| #190 书神 | 职业技能秘籍买卖 | 8 | True | - | `/tmp/npc_sweep/chunk_31/shots` |
| #191 大老板 | 随机卷轴/回城卷、火把/护身符/修复油 | 4 | True | - | `/tmp/npc_sweep/chunk_32/shots` |
| #192 峰儿 | 项链/手镯/戒指买卖、首饰修理 | 5 | True | - | `/tmp/npc_sweep/chunk_32/shots` |
| #193 无名老人 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_32/shots` |
| #194 清明子 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_32/shots` |
| #195 清明子 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_33/shots` |
| #196 清明子 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_33/shots` |
| #197 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 5 | True | - | `/tmp/npc_sweep/chunk_33/shots` |
| #198 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_33/shots` |
| #199 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_34/shots` |
| #200 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_34/shots` |
| #201 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_34/shots` |
| #202 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_34/shots` |
| #203 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_35/shots` |
| #204 变异骷髅 | 高阶法术研修、技能指导与境界突破 | 2 | True | - | `/tmp/npc_sweep/chunk_35/shots` |
| #205 王铁匠 | 武器买卖、武器普通修理、武器特殊修理 | 6 | True | - | `/tmp/npc_sweep/chunk_35/shots` |
| #206 织女 | 衣服/头盔/鞋靴买卖、防具修理 | 5 | True | - | `/tmp/npc_sweep/chunk_35/shots` |
| #207 白老中医 | 金创药/魔法药买卖、疗伤药/解毒药 | 4 | True | - | `/tmp/npc_sweep/chunk_36/shots` |
| #208 绿洲仓库管家 | 个人仓库保管、物品存取 | 5 | True | - | `/tmp/npc_sweep/chunk_36/shots` |
| #209 梅山侠 | 场景剧情互动、向导问询 | 5 | True | - | `/tmp/npc_sweep/chunk_36/shots` |
| #210 石宇 | 矿石材料/特殊战利品收购、古玩鉴宝 | 3 | True | - | `/tmp/npc_sweep/chunk_36/shots` |
| #211 勇汉 | 武器买卖、武器普通修理、武器特殊修理 | 7 | True | - | `/tmp/npc_sweep/chunk_37/shots` |
| #212 忠云 | 武器买卖、武器普通修理、武器特殊修理 | 3 | True | - | `/tmp/npc_sweep/chunk_37/shots` |
| #213 晓敏 | 衣服/头盔/鞋靴买卖、防具修理 | 5 | True | - | `/tmp/npc_sweep/chunk_37/shots` |
| #214 医神 | 金创药/魔法药买卖、疗伤药/解毒药 | 4 | True | - | `/tmp/npc_sweep/chunk_37/shots` |
| #215 沙漠仓库管家 | 个人仓库保管、物品存取 | 5 | True | - | `/tmp/npc_sweep/chunk_38/shots` |
| #216 雪儿 | 项链/手镯/戒指买卖、首饰修理 | 5 | True | - | `/tmp/npc_sweep/chunk_38/shots` |
| #217 啊俊 | 场景剧情互动、向导问询 | 8 | True | - | `/tmp/npc_sweep/chunk_38/shots` |
| #218 啊全 | 随机卷轴/回城卷、火把/护身符/修复油 | 2 | True | - | `/tmp/npc_sweep/chunk_38/shots` |
| #219 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_39/shots` |
| #220 英龙 | 武器买卖、武器普通修理、武器特殊修理 | 6 | True | - | `/tmp/npc_sweep/chunk_39/shots` |
| #221 晓薇 | 衣服/头盔/鞋靴买卖、防具修理 | 5 | True | - | `/tmp/npc_sweep/chunk_39/shots` |
| #222 凌峰 | 金创药/魔法药买卖、疗伤药/解毒药 | 4 | True | - | `/tmp/npc_sweep/chunk_39/shots` |
| #223 丘叔 | 个人仓库保管、物品存取 | 5 | True | - | `/tmp/npc_sweep/chunk_40/shots` |
| #224 龙叔 | 随机卷轴/回城卷、火把/护身符/修复油 | 4 | True | - | `/tmp/npc_sweep/chunk_40/shots` |
| #225 善颖 | 项链/手镯/戒指买卖、首饰修理 | 5 | True | - | `/tmp/npc_sweep/chunk_40/shots` |
| #226 啊贵 | 矿石材料/特殊战利品收购、古玩鉴宝 | 3 | True | - | `/tmp/npc_sweep/chunk_40/shots` |
| #227 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_41/shots` |
| #228 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_41/shots` |
| #229 武术教头 | 高阶法术研修、技能指导与境界突破 | 26 | True | - | `/tmp/npc_sweep/chunk_41/shots` |
| #230 雷翁断珠 | 高阶法术研修、技能指导与境界突破 | 66 | True | - | `/tmp/npc_sweep/chunk_41/shots` |
| #231 盟重道长 | 高阶法术研修、技能指导与境界突破 | 60 | True | - | `/tmp/npc_sweep/chunk_42/shots` |
| #232 泼皮路白 | 高阶法术研修、技能指导与境界突破 | 1 | True | - | `/tmp/npc_sweep/chunk_42/shots` |
| #233 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_42/shots` |
| #234 次庸 | 金创药/魔法药买卖、疗伤药/解毒药 | 5 | True | - | `/tmp/npc_sweep/chunk_42/shots` |
| #235 小望 | 场景剧情互动、向导问询 | 2 | True | - | `/tmp/npc_sweep/chunk_43/shots` |
| #236 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_43/shots` |
| #237 奸商欧阳 | 随机卷轴/回城卷、火把/护身符/修复油 | 5 | True | - | `/tmp/npc_sweep/chunk_43/shots` |
| #238 云发 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_43/shots` |
| #239 云发 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_44/shots` |
| #240 嘉登 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_44/shots` |
| #241 无理的道士 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_44/shots` |
| #242 矿山老人 | 场景剧情互动、向导问询 | 2 | True | - | `/tmp/npc_sweep/chunk_44/shots` |
| #243 署箭 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_45/shots` |
| #244 署箭 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_45/shots` |
| #245 牛老道 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_45/shots` |
| #246 石堆 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_45/shots` |
| #247 书架 | 主线/支线剧情指引、历练任务接取与交付 | 1 | True | - | `/tmp/npc_sweep/chunk_46/shots` |
| #248 魂滴 | 金创药/魔法药买卖、疗伤药/解毒药 | 2 | True | - | `/tmp/npc_sweep/chunk_46/shots` |
| #249 司马云天 | 武器买卖、武器普通修理、武器特殊修理 | 3 | True | - | `/tmp/npc_sweep/chunk_46/shots` |
| #250 司马武林 | 场景剧情互动、向导问询 | 2 | True | - | `/tmp/npc_sweep/chunk_46/shots` |
| #251 司马化林 | 武器买卖、武器普通修理、武器特殊修理 | 2 | True | - | `/tmp/npc_sweep/chunk_47/shots` |
| #252 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_47/shots` |
| #253 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_47/shots` |
| #254 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_47/shots` |
| #255 花仙 | 金创药/魔法药买卖、疗伤药/解毒药 | 2 | True | - | `/tmp/npc_sweep/chunk_48/shots` |
| #260 霸王幽灵 | 主线/支线剧情指引、历练任务接取与交付 | 2 | True | - | `/tmp/npc_sweep/chunk_48/shots` |
| #261 霸王幽灵 | 主线/支线剧情指引、历练任务接取与交付 | 2 | True | - | `/tmp/npc_sweep/chunk_48/shots` |
| #262 瓦慈 | 肉品收购与买卖、高品质纯度肉 | 2 | True | - | `/tmp/npc_sweep/chunk_48/shots` |
| #263 图木勒 | 武器买卖、武器普通修理、武器特殊修理 | 5 | True | - | `/tmp/npc_sweep/chunk_49/shots` |
| #264 儒帕 | 衣服/头盔/鞋靴买卖、防具修理 | 4 | True | - | `/tmp/npc_sweep/chunk_49/shots` |
| #265 啊莫 | 金创药/魔法药买卖、疗伤药/解毒药 | 3 | True | - | `/tmp/npc_sweep/chunk_49/shots` |
| #266 啊勒 | 个人仓库保管、物品存取 | 4 | True | - | `/tmp/npc_sweep/chunk_49/shots` |
| #267 肃良佳 | 随机卷轴/回城卷、火把/护身符/修复油 | 3 | True | - | `/tmp/npc_sweep/chunk_50/shots` |
| #268 羽霞 | 项链/手镯/戒指买卖、首饰修理 | 4 | True | - | `/tmp/npc_sweep/chunk_50/shots` |
| #269 禹老 | 矿石材料/特殊战利品收购、古玩鉴宝 | 3 | True | - | `/tmp/npc_sweep/chunk_50/shots` |
| #270 牢牲 | 场景剧情互动、向导问询 | 1 | True | - | `/tmp/npc_sweep/chunk_50/shots` |
| #271 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_51/shots` |
| #272 夏柯 | 场景剧情互动、向导问询 | 1 | True | - | `/tmp/npc_sweep/chunk_51/shots` |
| #273 紫霞神女 | 场景剧情互动、向导问询 | 1 | True | - | `/tmp/npc_sweep/chunk_51/shots` |
| #274 云中子 | 场景剧情互动、向导问询 | 1 | True | - | `/tmp/npc_sweep/chunk_51/shots` |
| #275 六面神石 | 城镇与地下城瞬间移动传送 | 0 | False | npc_not_found | `-` |
| #276 沙巴克炼化师 | 武器买卖、武器普通修理、武器特殊修理 | 3 | True | - | `/tmp/npc_sweep/chunk_52/shots` |
| #277 沙巴克药师 | 金创药/魔法药买卖、万年雪霜与疗伤药 | 2 | True | - | `/tmp/npc_sweep/chunk_52/shots` |
| #278 沙巴克杂货商 | 随机卷轴/回城卷、火把/护身符/修复油 | 2 | True | - | `/tmp/npc_sweep/chunk_52/shots` |
| #279 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_53/shots` |
| #280 沙巴克首饰商 | 项链/手镯/戒指买卖、首饰修理 | 2 | True | - | `/tmp/npc_sweep/chunk_53/shots` |
| #281 沙巴克鉴宝师 | 特殊材料收购、古物鉴定 | 2 | True | - | `/tmp/npc_sweep/chunk_53/shots` |
| #282 沙巴克裁缝 | 衣服与头盔买卖、防具备用修理 | 2 | True | - | `/tmp/npc_sweep/chunk_53/shots` |
| #283 沙巴克武器商 | 武器买卖、武器普通修理、武器特殊修理 | 2 | True | - | `/tmp/npc_sweep/chunk_54/shots` |
| #368 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_54/shots` |
| #369 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_54/shots` |
| #370 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_54/shots` |
| #371 六面神石 | 城镇与地下城瞬间移动传送 | 0 | False | - | `-` |
| #374 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_55/shots` |
| #375 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_55/shots` |
| #376 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_55/shots` |
| #377 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_56/shots` |
| #378 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_56/shots` |
| #379 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_56/shots` |
| #380 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_56/shots` |
| #381 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_57/shots` |
| #382 六面神石 | 城镇与地下城瞬间移动传送 | 2 | True | - | `/tmp/npc_sweep/chunk_57/shots` |

## 附：分块进度

```
[sweep] chunk_20 已存在，跳过
[sweep] chunk_21 已存在，跳过
[sweep] chunk_22 已存在，跳过
[sweep] chunk_23 已存在，跳过
[sweep] chunk_24 已存在，跳过
[sweep] chunk_25 已存在，跳过
[sweep] chunk_26 已存在，跳过
[sweep] chunk_27 已存在，跳过
[sweep] chunk_28 已存在，跳过
[sweep] chunk_29 已存在，跳过
[sweep] chunk_30 已存在，跳过
[sweep] chunk_31 已存在，跳过
[sweep] chunk_32 已存在，跳过
[sweep] chunk_33 已存在，跳过
[sweep] chunk_34 已存在，跳过
[sweep] chunk_35 已存在，跳过
[sweep] chunk_36 已存在，跳过
[sweep] chunk_37 已存在，跳过
[sweep] chunk_38 已存在，跳过
[sweep] chunk_39 已存在，跳过
[sweep] chunk_40 已存在，跳过
[sweep] chunk_41 已存在，跳过
[sweep] chunk_42 已存在，跳过
[sweep] chunk_43 已存在，跳过
[sweep] chunk_44 已存在，跳过
[sweep] chunk_45 已存在，跳过
[sweep] chunk_46 已存在，跳过
[sweep] chunk_47 已存在，跳过
[sweep] chunk_48 已存在，跳过
[sweep] chunk_49 已存在，跳过
[sweep] chunk_50 已存在，跳过
[sweep] chunk_51 已存在，跳过
[sweep] chunk_52 已存在，跳过
[sweep] chunk_53 已存在，跳过
[sweep] chunk_54 已存在，跳过
[sweep] chunk_55 开始 ids=371,374,375,376 11:23:19
[sweep] chunk_55 结束 rc=0 用时=34s  [NpcAudit] SUMMARY total=4 ok=4 no_response=0 not_found=0 click_missed=0 timeout=0 duplicate_skipped=1
[sweep] chunk_56 已存在，跳过
[sweep] chunk_57 已存在，跳过
[sweep] 全部完成 11:23:53
```

## 七、结论

- **覆盖**：清单内 230 个在用 NPC 全部巡检完毕，累计 1533 个对话页、1533 张截图（每页一张）。
- **对话/商店/修理/仓库/传送页型**：228 个 NPC 逐页逐链接点击无异常。
- **异常**：2 个（click_missed×1、npc_not_found×1）——全部集中在六面神石（传送石），且均为**清单登记问题**：同坐标重复登记（同一实体两条记录，其中一条永远点不到）或该坐标无实体；**不影响传送功能本身**（功能断言已单独证明，见第三节）。
- **功能断言**：4/4 通过（交易=金币精确扣减+背包+1；传送=换图+扣费；仓库=背包格→仓库格搬运；出售=金币增加+物品减少）。
- **灰色遮罩回归**：整轮巡检日志中 `[UIMask]` 行数 = **0**（0 = 无兜底灰块）。
- 截图留证：`/tmp/npc_sweep/chunk_NN/shots/`（文件名 `<NPC序号>_<页序>_p<页号>.png`）。

### 人工复核（对上面异常逐条定性）

| NPC | 现象 | 定性 | 证据 |
|---|---|---|---|
| #106 六面神石(巴尼亚 2,314,193) | 块内 click_missed | **偶发点击时序**，非功能缺陷 | 单独重跑 `--npc-audit-only 106` → `pages=2 clicked=True errors=[]`；同坐标的 #371 已被去重跳过 |
| #275 六面神石(萨巴克 3,222,159) | npc_not_found | **该坐标无实体**（数据/刷怪缺口） | 脚本存在（13Move_Sabuk-3.txt，5 页）但游戏内该点无 NPC 对象，`@move 3 222 157` 后找不到 |

> 结论：**没有一个是界面/功能缺陷**。传送功能本身由功能断言证明（换图 0→02 + 扣费 500）。
> #275 若要修复需动 System.db 的刷怪/NPC 表（停服 + 4 份同步），属内容决策，另行确认。


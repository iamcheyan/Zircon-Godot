# 数据库经典化纯净清洗方案与执行记录 (Classic Purity Cleanup Plan)

## 一、背景与目标

当前 Zircon 数据库（`System.db`）继承自新版/拓展版，包含大量后期或非经典内容（如雪原、诺玛拓展、刺客职业、变态装备与私服怪物）。
为了实现对“传奇3经典版（以 17173 官方资料站为权威基准）”的纯净还原，并保证游戏生态、刷怪、掉落、商店的彻底闭环，现基于已建立的权威对齐总表（`canonical_identity.json`）执行全量数据收敛与清洗。

### 核心清洗目标与最终结果
1. **怪物（MonsterInfo）**：收敛至 **154 只经典野怪**（占 Zircon 106 个底层模型 + 10 个系统必备宠物/守卫），彻底清除雪原、诺玛后期非经典怪物。
2. **物品（ItemInfo）**：收敛至 **371 件经典装备与消耗品**（占 Zircon 253 个底层模型 + 货币与新手任务必需道具），彻底清除拓展版装备。
3. **技能（MagicInfo）**：收敛至 **61 门三职业经典技能**（占 Zircon 59 门底层法术），彻底清除刺客（Assassin）系与非经典扩充技能。

---

## 二、级联依赖与安全清理策略

MirDB 采用对象关系强关联（`[Association]` 属性）。简单物理硬删除主表会导致加载或反序列化时产生“孤儿外键引用”崩溃。因此必须执行严格的**级联清洗（Cascading Cleanup）**：

| 表名称 | 清洗前行数 | 清洗后行数 | 减少行数 | 处理说明 |
| :--- | :---: | :---: | :---: | :--- |
| **MonsterInfo** | 434 | **116** | -318 | 保留 17173 经典 154 怪 + 守卫与伴侣，清除雪原诺玛私服怪 |
| **ItemInfo** | 1078 | **326** | -752 | 保留 17173 经典 371 物品 + 货币/矿石/新手任务信物 |
| **MagicInfo** | 174 | **59** | -115 | 保留经典 61 门三职业核心技能，移除刺客与非经典技能 |
| **MonsterInfoStat** | 4117 | **1048** | -3069 | 级联清除被删怪物的攻防属性 |
| **ItemInfoStat** | 3196 | **764** | -2432 | 级联清除被删装备的附加属性 |
| **RespawnInfo** | 2475 | **1007** | -1468 | 级联清除雪原、诺玛等非经典地图的野怪刷新点 |
| **DropInfo** | 10382 | **1746** | -8636 | 级联清除被删怪物掉落以及非经典装备掉落规则 |
| **NPCGood** | 332 | **63** | -269 | 级联清除 NPC 商店中出售的非经典货架商品 |
| **StoreInfo** | 92 | **1** | -91 | 级联清除商城中的非经典商品 |
| **QuestTaskMonsterDetails** | 55 | **40** | -15 | 级联清除任务目标中指向被删怪物的条目 |
| **QuestReward** | 42 | **4** | -38 | 级联清除任务奖励中指向被删物品的条目 |

---

## 三、备份与一键回滚机制

备份已于物理写入前完成，归档于：
`Debug/ServerCore/Database/Backup/classic-purity-20261003-snapshot/`
- `ServerCore_Database_System.db` (MD5: `d9b99f02ef6b00ed51ef2ba13fd25a52`)
- `mir2ei_Data_System.db` (MD5: `c07a3c24ebcf62e837000b4f95595c12`)
- `root_System.db` (MD5: `1a7ee9e24d38a78630cc204e490ef5c4`)

### 一键回滚命令 (Emergency Rollback)
若清洗后出现任何异常，可直接在仓库根目录执行以下命令完全复原：
```bash
BACKUP_DIR="/home/tetsuya/development/zircon/Debug/ServerCore/Database/Backup/classic-purity-20261003-snapshot"
cp -f "$BACKUP_DIR/ServerCore_Database_System.db" "/home/tetsuya/development/zircon/Debug/ServerCore/Database/System.db"
cp -f "$BACKUP_DIR/mir2ei_Data_System.db" "/home/tetsuya/mir2ei/Data/System.db"
cp -f "$BACKUP_DIR/mir2ei_Database_System.db" "/home/tetsuya/mir2ei/Database/System.db"
cp -f "$BACKUP_DIR/root_System.db" "/home/tetsuya/development/zircon/System.db"
echo "Rollback completed successfully."
```

---

## 四、执行全流程记录

1. **环境隔离与守护屏蔽**：
   - 创建 `~/.omp/mir3-goal-watchdog.off` 屏蔽自动守护拉起，停止正在运行的旧服务端与客户端进程。
2. **白名单级联裁剪与引用完整性校验**：
   - 提取 `canonical_identity.json` 中 154 怪物、371 物品、61 技能；
   - 补充系统机制必备守护白名单（Guard 守卫、Castle 沙巴克控制、Currency 货币、Mine 矿石、QuestTask 新手任务信物）；
   - 通过 `DBImporter` 严格阶段 B 引用完整性校验：**0 处悬空引用**。
3. **数据库生成与多库原子安装**：
   - 成功构建纯净版 `System.db`（版本自动递增至 `2026.10.03.1`）；
   - 原子安装至：
     - `Debug/ServerCore/Database/System.db`
     - `/home/tetsuya/mir2ei/Data/System.db`
     - `/home/tetsuya/mir2ei/Database/System.db`
     - `/home/tetsuya/development/zircon/System.db`
   - 通过 `SystemDbProbe --json` 全量回读重导出覆盖 `workspace/` 与 `_baseline/`。
4. **游戏实机启动与冒烟测试**：
   - 服务端启动：仅耗时 **2 秒**，无任何孤儿外键或空引用报错；成功加载 239 物品中文别名与 147 怪物中文别名。
   - 客户端启动：成功加载 326 件纯净物品、116 只纯净怪物、59 门纯净技能。
   - 真实登录验证：使用测试账号 `test@test.com` 成功建立连接并鉴权通过。

# 系统机制伴生怪物与技能实体对齐与原生恢复方案 (2026-10-04)

## 1. 问题背景与根本原因分析

### 1.1 核心问题
2026-10-04 在测试道士职业使用核心技能「召唤骷髅」时，服务端抛出未处理异常导致连接中断，随后全服主循环停滞，角色卡死不能移动。

### 1.2 根因定位与排查
经过对源码、清洗前备份库（`classic-purity-20261003-snapshot`）与当前库的交叉比对，确定事故原因为：
1. **野外怪物 vs 系统机制实体的认知混淆**：
   - 2026-10-03 执行的《数据库经典化纯净清洗方案》以“17173 经典怪物图鉴（154 只野怪）”为硬性白名单，对 `MonsterInfo` 表进行了级联物理删除（从 434 裁剪至 116）。
   - 17173 官方资料站的怪物图鉴只展示“**野外地图刷新的野生怪**”（例如地牢一二层的野怪骷髅 `Index: 27, Skeleton`）。
   - **道士的召唤兽（召唤骷髅、神兽、金刚骷髅）和 Boss 伴生怪（赤月恶魔吐出的幼虫）在传奇全系历史上均不是野外自然刷新的野怪**，因此 17173 图鉴根本未收录它们。
   - 清洗脚本当时仅对大刀守卫（`Guard #1`）等极少数系统怪物设置了硬编码保护，未将道士召唤兽与机制伴生怪列入保护白名单，导致它们被一刀切清洗删除。
2. **底层强类型查找依赖 `MonsterFlag`**：
   - 服务端技能逻辑并非根据怪物名称字符串模糊匹配，而是通过底层强类型标识查找：
     ```csharp
     // SummonSkeleton.cs
     var info = SEnvir.MonsterInfoList.Binding.FirstOrDefault(x => x.Flag == MonsterFlag.Skeleton);
     // SummonJinSkeleton.cs
     var info = SEnvir.MonsterInfoList.Binding.FirstOrDefault(x => x.Flag == MonsterFlag.JinSkeleton);
     // SummonShinsu.cs
     var info = SEnvir.MonsterInfoList.Binding.FirstOrDefault(x => x.Flag == MonsterFlag.Shinsu);
     ```
   - 随从实体具有专属属性配置、抗性成长、AI（如 `AI: 52 WhiteBone`、`AI: 53 Shinsu`）和专属模型（手持巨斧的白色变异骷髅 `WhiteBone`，区别于野怪小骷髅 `Skeleton`）。
   - 清洗后上述实体从库中消失，导致技能找不到对应实体抛出异常。
3. **骨魔洞怪物映射错位误删**：
   - 骨魔洞的 `Bone Captain`（原版 Index 118）和 `Bone Soldier`（原版 Index 119）本身是 17173 经典怪物（分别对应鬼骨将与骷髅士兵），但由于原映射表 `canonical_identity.json` 将其错指给了 116 弓箭手与 120，导致清洗时未被命中，被当成非经典怪误删。而 Boss AI（AI: 43）有生成骨魔洞士兵/队长的强依赖。
4. **服务端缺乏异常隔离**：
   - `SEnvir.cs` 的主循环在分发玩家连接数据包时，若单个连接在执行动作时抛出未捕获异常，异常直接击穿主循环，导致世界逻辑帧停止推进，全体在线角色表现为永久卡死。

### 1.3 前序补丁方案的缺陷
前序智能体编写了 `RestoreRequiredMonsters.cs`，使用自增 `CreateNewObject()` 在当前数据库末尾新建了 6 条怪物记录（分配了 `487~492` 的全新编号）。
**该补丁方案存在以下严重缺陷**：
1. **破坏原生数据库编号体系**：将原本经典的系统核心实体编号（68, 118, 119, 144, 145, 146）篡改为 487~492，导致数据库编号断裂且无法溯源；
2. **污染怪物数据纯净度**：使库内出现两条同名的 `Skeleton`（27 与 487），产生数据冗余与歧义；
3. **脱离规范管理**：没有将这批实体纳入系统双轨白名单管理，下一次若重新执行纯净化清洗，487~492 仍然会被当作私服怪清除。

---

## 2. 系统双轨实体分类与保护体系

为了彻底杜绝此类问题，确立 **「经典野怪」与「系统机制伴生实体」双轨白名单体系**：

### 轨道 A：经典野怪 (Wild Monsters)
- 依据 17173 官方资料站 154 只经典野怪收录；
- 具有野外刷新（`RespawnInfo`）与爆率（`DropInfo`）；
- 严格受经典地图与纯净生态管控。

### 轨道 B：系统机制与技能伴生实体 (System & Companion Entities)
- 受系统最高级别保护白名单锁定，**永久不可被野怪清洗脚本裁剪**；
- 无野外自然刷新点，无常规野怪爆率；
- 属于技能释放（如道士召唤）、Boss 技能召唤、沙巴克机制、守卫机制的核心底层依赖。

#### 核心保护实体清单：
| 原生 Index | 实体名称 (MonsterName) | 实体模型 (Image) | AI 类型 | 标记 (MonsterFlag) | 业务依赖说明 |
|:---:|:---|:---|:---:|:---|:---|
| **1** | Guard | Guard | 1 | None | 盟重/比奇大刀守卫（系统安全区必备） |
| **68** | Larva | Larva | 18 | `MonsterFlag.Larva` | 赤月恶魔（Red Moon The Fallen）喷吐幼虫机制依赖 |
| **118** | Bone Captain | BoneCaptain | 0 | `MonsterFlag.BoneCaptain` | 骨魔洞队长/精英怪，Boss AI 43 召唤依赖 |
| **119** | Bone Soldier | BoneSoldier | 42 | `MonsterFlag.BoneSoldier` | 骨魔洞骷髅士兵，Boss AI 43 召唤依赖 |
| **144** | Skeleton | WhiteBone | 52 | `MonsterFlag.Skeleton` | **道士「召唤骷髅」技能核心随从实体**（手持大斧白色变异骷髅） |
| **145** | Jin Skeleton | WhiteBone | 52 | `MonsterFlag.JinSkeleton` | **道士「超强召唤骷髅」技能核心随从实体** |
| **146** | Shinsu | Shinsu | 53 | `MonsterFlag.Shinsu` | **道士「召唤神兽」技能核心随从实体**（喷火神兽） |

---

## 3. 实施执行步骤

### 步骤 1：物理清理当前脏数据
- 从当前 `System.db` 中彻底删除临时追加的自增实体：
  - `Index: 487 (Skeleton)`
  - `Index: 488 (Jin Skeleton)`
  - `Index: 489 (Shinsu)`
  - `Index: 490 (Larva)`
  - `Index: 491 (Bone Captain)`
  - `Index: 492 (Bone Soldier)`
- 级联清除这些临时怪在 `MonsterInfoStat` 表中的所有挂载属性。

### 步骤 2：精确恢复原生 Index 与原生基础属性
- 从原始备份库（`Debug/ServerCore/Database/Backup/classic-purity-20261003-snapshot/ServerCore_Database_System.db`）中，按**原始原生 Index**提取对应的 6 只怪物及其攻防血量属性（`MonsterInfoStat`）：
  - 恢复 `Index: 68`（Larva）
  - 恢复 `Index: 118`（Bone Captain）
  - 恢复 `Index: 119`（Bone Soldier）
  - 恢复 `Index: 144`（Skeleton，AI 52，WhiteBone）
  - 恢复 `Index: 145`（Jin Skeleton，AI 52，WhiteBone）
  - 恢复 `Index: 146`（Shinsu，AI 53，Shinsu）
- 保持 `Binding` 集合按 `Index` 升序排列，二分查找与外键关联完美保持一致。

### 步骤 3：修复映射表与审计工具
- 在 `docs/canonical_identity.json` 中修正骨魔洞怪物的映射，避免将 118/119 错配到弓箭手；
- 在 `Tools/ClassicMagicFixer/RuntimeDataAudit.cs` 中确立双轨依赖审计用例，确保后续任何清洗或迁移均必须通过 `auditdeps` 自动化回归检验；
- 移除前序临时工具 `RestoreRequiredMonsters.cs`。

### 步骤 4：确认服务端稳定性与容错加固
- 保留 `SEnvir.cs` 中的 `connection.Process()` 异常隔离（单个连接崩坏不影响世界循环）；
- 保留 `SummonSkeleton.cs` / `SummonJinSkeleton.cs` / `SummonShinsu.cs` 的安全查找防空机制。

### 步骤 5：四库原子同步与 MD5 检验
按照《AGENTS.md》铁律，将修复后的 `System.db` 原子同步至全部 4 处镜像：
1. `Debug/ServerCore/Database/System.db`（服务端运行目录）
2. `/home/tetsuya/mir2ei/Data/System.db`（客户端读取目录）
3. `/home/tetsuya/mir2ei/Database/System.db`
4. `/home/tetsuya/development/zircon/System.db`
确保 4 份文件的 MD5 严格完全一致。

### 步骤 6：全套验证与实机验收
1. 执行 `dotnet build GodotClient/ZirconClient.csproj` 确认客户端编译；
2. 执行 `Tools/ClassicMagicFixer auditdeps` 确认数据依赖审计全绿；
3. 执行服务端与无头测试客户端冒烟，验证道士释放「召唤骷髅」时正确召唤出手持双刃斧头的变异白骷髅，且服务端运行顺畅稳定。

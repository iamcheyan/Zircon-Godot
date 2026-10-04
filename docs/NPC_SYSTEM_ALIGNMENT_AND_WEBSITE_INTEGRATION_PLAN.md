# 经典传奇 3 NPC 体系对齐与网站百科同步实施规划（SOP）

> **文档性质**：执行智能体（Agent）标准化作业指引（SOP） & 验收标准  
> **制定时间**：2026-10-04  
> **执行状态**：待执行（由指定智能体按本方案执行，完成后提交本智能体验收）

---

## 一、任务背景与核心目标

### 1. 现状痛点
- **地图已经典化，但 NPC 错位严重**：当前游戏世界已全面切换为 EI 经典原版地图（.map），但 Zircon 的原生 NPC 数据来源于上游/私服版本；
- **私服多余 NPC 泛滥**：存在大量现代功能 NPC、私服活动传送员、未开放玩法的 NPC；
- **原版站位与柜台脱节**：许多经典商人未站在原版的店铺柜台内，部分甚至卡在墙体内、浮在水面或位于荒郊野外；
- **百科资料站缺少 NPC 模块**：官方资料站（`/home/tetsuya/development/mir3-website`）具备怪物、装备、技能、地图，但唯独缺失 NPC 结构化百科。

### 2. 核心目标
1. **正统原版坐标归位**：以原版 Mud3 及 17173 百科数据为准，将经典城镇（银杏村、比奇城、道馆、潘夜村、沙巴克、绿洲、失乐园等）的所有核心功能 NPC 坐标精准还原到经典地图对应点位；
2. **多余 NPC 软停用（零破坏）**：对 Zircon 自带但经典版不存在的 NPC 执行“软停用”，不在游戏世界生成，同时保护底层脚本链条完整；
3. **百科同步沉淀**：在 `mir3-website` 建立 `data/npcs.json`，让百科网站与游戏内实际 NPC 坐标 100% 严丝合缝；
4. **闭环实机验收**：使用测试账号登录并使用 GM 命令传送至各大主要城镇现场截图取证。

---

## 二、数据源定义与参考资料

执行智能体必须严格从以下权威源读取数据，严禁凭空臆造或通过大模型幻觉生成坐标：

| 数据源 | 路径 / 来源 | 作用说明 |
|---|---|---|
| **原版 Mud3 配置** | `/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir/Npcs.txt` | 原版权威 NPC 列表、所属地图编号、$(X, Y)$ 坐标、外观 Appr |
| **已整理 NPC 证据** | `/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/artifacts/website-alignment-2026-09-26/npc-manifest.json` | 包含此前审计的 294 个 NPC 名称映射、地图关系及坐标证据 |
| **Zircon 现役数据库** | `System.db`（`NPCInfo` 表、`MapRegion` 表、`MapInfo` 表） | 当前游戏运行中的 NPC 及其关联的地图区域 |
| **网站百科根目录** | `/home/tetsuya/development/mir3-website/` | 17173 重构版资料站，数据落地于 `data/` 目录 |

---

## 三、处理分类与核心设计铁律

### 1. 【交集类】：双方共有的经典功能 NPC（重点执行）
- **覆盖范围**：各大城镇的铁匠、屠夫、药店老板、杂货商、首饰商、仓库保管员、老兵、书店老板、服装店老板等（约 70~100 个核心 NPC）；
- **操作规则**：
  1. 通过英文名/中文别名/职能与原版对齐；
  2. 将其关联的 `MapRegion` 目标地图设为经典地图（如比奇=`01`、道馆=`02`、银杏村=`0`、沙巴克=`03`等）；
  3. 将坐标 $(X, Y)$ 更新为原版权威点位；
  4. 核对其 `Image`（模型外型）与 `FaceImage`（对话头像）是否符合 EI 原版。

### 2. 【差集类 A】：新版多余 NPC（必须软停用，严禁物理删除！）
- **🔴 绝对红线**：**严禁从 `NPCInfo` 表执行物理 `DELETE`！**
  - **原因**：每个 `NPCInfo` 级联绑定着 `NPCPage`（对话树）、`NPCAction`（执行逻辑如存仓、修理、行会创建等）、`NPCCheck`（条件分支）。直接物理删除会导致孤立脚本碎片，甚至导致引用了该 NPC Index 的系统功能抛空指针异常。
- **🟢 软停用标准做法**：
  - Zircon 服务端生成 NPC 逻辑（`ServerLibrary/Envir/SEnvir.cs:926`）：
    ```csharp
    foreach (NPCInfo info in npcInfos)
    {
        if (info.Region == null) continue; // 无 Region 则服务端静默跳过生成！
        ...
    }
    ```
  - **实现方式**：将多余 NPC 的 `Region` 字段置为 `null`（或者关联到一个未挂载在任何已启用地图的“保留区域”）。
  - **效果**：游戏内不刷新，世界纯净，脚本完好无损，随时可逆。

### 3. 【差集类 B】：原版有、新版缺失的 NPC
- 原版特有的特殊任务引导员、副村落小 NPC；
- **策略**：
  - 本期先整理输出一份《原版经典 NPC 缺口与待引入清单》；
  - 核心功能商人若确实缺失，可克隆相近的通用商人 NPC 模板挂载坐标补齐；剧情任务类 NPC 留待任务系统重构时专项补齐。

### 4. 物理合法性与防卡墙规则
- **边界校验**：更新后的目标坐标必须满足 $0 \le X < MapInfo.Width$ 且 $0 \le Y < MapInfo.Height$；
- **柜台与朝向**：原版商铺 NPC 通常站在店铺柜台内（原版地图中柜台本身通常是阻挡不可行走，但 NPC 初始就放置在柜台内面向柜台外），执行时必须核对原版真实站位，切忌随意“修正”到柜台外。

---

## 四、执行智能体标准化操作步骤（SOP）

执行智能体必须严格按以下 6 个步骤流水线式推进，严禁跳步：

### Step 1：停止服务与数据备份
1. 检查并停止后台服务端（`kill` ServerCore 进程，确认 7000 端口释放）；
2. 备份当前真实的 4 处 `System.db` 到临时备份目录（附带当前时间戳）：
   ```bash
   mkdir -p /home/tetsuya/mir2ei/Backup/npc_alignment_$(date +%Y%m%d%H%M%S)
   cp Debug/ServerCore/Database/System.db /home/tetsuya/mir2ei/Backup/npc_alignment_$(date +%Y%m%d%H%M%S)/
   ```

### Step 2：数据提取与对齐矩阵生成
1. 编写提取脚本（推荐 Python 或 C#），分别解析：
   - Mud3 的 `Envir/Npcs.txt`
   - 当前 `System.db` 的 `NPCInfo` + `MapRegion`
   - `Mir3-Research` 中已有的 `npc_manifest.json`
2. 输出对齐矩阵表格/JSON：
   - 包含：`NpcName`, `Category`, `OldMap`, `OldX`, `OldY`, `ZirconCurrentMap`, `ZirconCurrentX`, `ZirconCurrentY`, `Action(Migrate/Disable/Retain)`。

### Step 3：编写专用迁移工具并应用（Dry-run -> 真实写库）
1. 编写专门的数据迁移控制台程序（可放置于 `Tools/NpcAligner/`）；
2. 先执行 Dry-run 模式：打印出将要移动的 NPC 数量、将要软停用（解绑 Region）的 NPC 数量；
3. 执行正式写入，对符合条件的 NPC 迁移坐标，对超纲 NPC 软停用。

### Step 4：严格执行《写库纪律》（4 处 MD5 必须完全一致）
1. 迁移工具修改 `System.db` 后，**必须同步覆盖以下 4 处镜像**：
   - `Debug/ServerCore/Database/System.db`
   - `/home/tetsuya/mir2ei/Data/System.db`
   - `/home/tetsuya/mir2ei/Database/System.db`
   - `./System.db`（仓库根目录）
2. 运行 `md5sum` 命令，验证 4 个文件的 MD5 严格对齐相同！

### Step 5：同步沉淀网站百科（mir3-website）
1. 将本次对齐后的纯净经典 NPC 清单生成为结构化 JSON：
   - 输出目标：`/home/tetsuya/development/mir3-website/data/npcs.json`
   - JSON 结构示例：
     ```json
     [
       {
         "id": 1,
         "name_zh": "银杏村铁匠",
         "name_en": "Ginkgo_Blacksmith",
         "map_code": "0",
         "map_name_zh": "银杏山谷",
         "x": 622,
         "y": 628,
         "category": "WeaponRepair",
         "services": ["武器买卖", "普通修理", "特殊修理"],
         "image": 10
       }
     ]
     ```
2. 确保 `mir3-website` 的数据结构完整，可在 Flask/静态站中被检索。

### Step 6：实机启动与多城镇现场截图取证
1. 重新启动 `./ServerCore`（端口 7000 就绪）；
2. 启动客户端连接本地测试账号（`test@test.com` / `test123` / `TestHero`）；
3. 利用 GM 命令依次传送到代表性城镇并截图：
   - `@move 0 622 628`（银杏村）
   - `@move 01 328 268`（比奇城中央）
   - `@move 02 382 114`（道馆）
   - `@move 03 330 330`（沙巴克城）
4. 截取高清现场画面，保存至 `docs/screenshots/npc_alignment/` 目录。

---

## 五、验收标准（Checklist for 验收智能体）

执行智能体完成工作后，验收智能体将依照下表逐一核验，全部通过方可结项：

- [ ] **1. 写库合规性**：4 处 `System.db` 的 MD5 绝对一致，且服务端重启后日志无任何 `[NPC] Bad Map` 或 `Failed to spawn NPC` 报错；
- [ ] **2. 零物理破坏**：`NPCInfo`、`NPCPage`、`NPCAction` 等脚本数据未被物理 DELETE，多余 NPC 均通过 `Region=null` 实现优雅停用；
- [ ] **3. 经典城镇 NPC 就位**：银杏村、比奇城、道馆等主流城镇的核心铁匠、屠夫、药店、仓库、老兵站在经典原版柜台/点位上，不卡墙、不浮空；
- [ ] **4. 交互正常**：点击 NPC 能够正常弹出对话框，买卖、修理、仓库等基础功能未受损；
- [ ] **5. 网站百科文件存在**：`/home/tetsuya/development/mir3-website/data/npcs.json` 已生成且格式规范、内容与游戏世界严格对齐；
- [ ] **6. 实机截图证据完备**：`docs/screenshots/npc_alignment/` 内存放了真实进入游戏截取的各大城镇 NPC 站位图；
- [ ] **7. 提交纪律**：修改按 Git 规范提交并推送至 `origin master`，不包含无关 WIP。

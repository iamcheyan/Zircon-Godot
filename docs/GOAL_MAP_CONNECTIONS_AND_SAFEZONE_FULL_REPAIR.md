# GOAL: 全游戏地下城连接与主城安全区全量修复任务规范

> **目标类型**：自动化数据修复与全服连通性治理  
> **执行执行体**：数据治理智能体 / MapConnectionFixer  
> **适用版本**：Zircon C# (Mir3 EI 800×800 经典纯净版)  
> **前置依赖**：已归档工具 [`Tools/MapConnectionFixer/`](file:///home/tetsuya/development/zircon/Tools/MapConnectionFixer/)

---

## 1. 任务背景与核心目标

经过前期排查，在传奇 3 地图更换为 EI 原版（如比奇 800×800）及经典纯净清洗后：
1. **主城安全区偏移**：除比奇（0）和道馆（1）已修复外，盟重土城（4）、沙漠绿洲（5）、蛇谷（2）、潘夜岛（8）等城镇安全区仍停留在旧版非官方坐标，偏离原版达 100~300 格，导致玩家回城/复活掉入野外荒地；
2. **地下城/洞窟层级断连**：全库目前有 627 张地图、1034 个传送门，但大量核心洞窟（天然洞穴 D001-D003、比奇矿区 D202-D203、跳蚤洞 D401-D406、沃玛神殿 D1001-D1002、祖玛神殿 D1104-D1105、潘夜石窟 D1401-D1405 等）**层级通道完全未连通**，玩家进洞后无法下到深层。

**本任务目标**：基于官方权威配置数据，将全游戏所有**主城安全区**与**地下城/洞窟进出层级连接**全量自动化校准并连通，实现 0 断连、0 坏点、0 报错。

---

## 2. 权威数据来源与解析规则

所有数据以仓库内权威官方配置文件为唯一真理标准：

### 2.1 地图连接真理源：`Mapinfo.txt`
- **路径**：`/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir/Mapinfo.txt`
- **连接行语法**：
  ```text
  <源地图名> <源X>,<源Y> -> <目标地图名> <目标X>,<目标Y>
  ```
- **典型范例**：
  ```text
  D001 30,328 -> D002 34,323      ; 天然洞穴1层 -> 2层
  D002 34,322 -> D001 30,329      ; 天然洞穴2层 -> 1层
  D002 117,95 -> D003 106,83      ; 天然洞穴2层 -> 3层
  D003 106,82 -> D002 117,96      ; 天然洞穴3层 -> 2层
  D202 54,287 -> D203 14,24       ; 比奇废矿2层 -> 3层
  D401 76,15 -> D411 59,8         ; 跳蚤洞1层 -> 跳蚤洞2层
  D1001 199,286 -> D1002 135,178  ; 沃玛神殿1层 -> 2层
  D1401 198,187 -> D1402 18,17    ; 潘夜石窟1层 -> 2层
  ```
- **正则提取模式**：
  ```csharp
  Regex regex = new Regex(@"^([A-Za-z0-9_]+)\s+(\d+),(\d+)\s*->\s*([A-Za-z0-9_]+)\s+(\d+),(\d+)");
  ```

### 2.2 安全区真理源：`StartPoint.txt`
- **路径**：`/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir/StartPoint.txt`
- **配置行语法**：
  ```text
  <地图名> <中心X> <中心Y> [<保护半径>] [<附加标志>]
  ```
- **官方完整数据表**：
  | 地图编号 | 地图名称 | 中心坐标 (X, Y) | 建议保护半径 (R) | 说明 |
  | :--- | :--- | :--- | :--- | :--- |
  | **01** | 边境城市 | `(439, 304)` | 15 | 银杏山谷边境城 |
  | **02** | 银杏山谷 | `(265, 207)` | 15 | 新手村 |
  | **0** | 比奇县城 | `(458, 398)` | 18 | 已修好，维持现状 |
  | **1** | 道馆 | `(423, 102)` | 15 | 已相符，校验保护区 |
  | **2** | 蛇谷（山谷村） | `(342, 222)` | 15 | 需修正（当前在 145, 161） |
  | **4** | 盟重省（土城） | `(457, 77)` | 18 | 需修正（当前在 164, 130） |
  | **41** | 诺玛沙漠 | `(167, 94)` | 15 | 需校验 |
  | **5** | 沙漠绿洲 | `(216, 185)` | 16 | 需修正（当前在 55, 203） |
  | **74** | 诺玛村落/沙漠村 | `(315, 287)` | 15 | 需校验 |
  | **8** | 潘夜岛 | `(237, 273)` | 16 | 需修正（当前在 219, 142） |
  | **9** | 死谷/灌木林 | `(193, 572)` | 15 | 需校验 |
  | **81** | 流放岛/雪原 | `(130, 273)` | 15 | 需校验 |

---

## 3. 技术铁律与工程规范（绝对遵循，违者重构）

### 铁律 1：停服写库
在执行任何修改 `System.db` 的操作之前，**必须确保服务端进程全部停止**：
```bash
pgrep -fa ServerCore  # 必须无活跃 ServerCore 进程
```
若有进程运行，必须先终止，否则服务端进程在退出时会用内存脏数据覆盖你的修改。

### 铁律 2：MirDB 对象解耦约束（防一对多引用覆盖）
在 `SafeZoneInfo` 中：
- `SafeZoneInfo.Region` 代表**安全区保护范围**（禁止 PK，半径通常为 15~18）；
- `SafeZoneInfo.BindRegion` 代表**出生/回城着陆点**（通常为中心点周围 5×5，共 25 点）；
- **严禁**将两者赋值为同一个 `MapRegion` 实例！必须实例化为两个独立的 `MapRegion`，否则修改 BindRegion 会把安全保护区清空或缩成 25 个点。

### 铁律 3：地图二进制阻挡剔除（防 Bad Location / Bad Origin）
传奇 3 地图阻挡在 `.map` 二进制文件中由第 14 字节的标记位决定：
```csharp
// 可行走格子判定逻辑（非墙壁、非障碍物）
bool isWalkable = (flag & 0x02) == 2 && (flag & 0x01) == 1;
```
- 所有写入 `SafeZoneInfo.Region.PointRegion` 的坐标点，**必须通过目标地图的阻挡检测**，过滤掉不可行走点（墙体、推车、建筑）；
- 所有写入 `MovementInfo.SourceRegion` 与 `DestinationRegion` 的落点，**必须确保落在可行走点上**；
- 只有严格经过阻挡剔除，服务端启动时才不会输出大量 `[Safe Zone] Bad Location` 和 `[Movement] Bad Origin`。

### 铁律 4：System.db 5 处镜像同步与 MD5 强校验
修改完 `Debug/ServerCore/Database/System.db` 后，**必须同步到以下全部 4 处镜像**，确保 5 处 MD5 严格相同：
1. `Debug/ServerCore/Database/System.db`（服务端实际读取源）
2. `/home/tetsuya/mir2ei/Data/System.db`（客户端读取源）
3. `/home/tetsuya/mir2ei/Database/System.db`
4. `/home/tetsuya/development/zircon/System.db`
5. `/home/tetsuya/development/Debug/ServerCore/Database/System.db`

---

## 4. 工具使用与自动化实施蓝图

已建立的基础工具位于：[`Tools/MapConnectionFixer/`](file:///home/tetsuya/development/zircon/Tools/MapConnectionFixer/)。
当前工具已内置：
- MirDB Session 读写与对象创建；
- `.map` 文件二进制阻挡解析函数 `LoadValidCells(mapPath)`；
- 5 处镜像自动同步函数 `SyncSystemDb()`；
- 全库体检命令：`dotnet run --project Tools/MapConnectionFixer/MapConnectionFixer.csproj -- --audit`。

### 推荐扩展方案：在 `Tools/MapConnectionFixer/Program.cs` 增加两大批处理指令

#### 模块 A：`--fix-safezones`（主城安全区全量修正）
1. 遍历 `StartPoint.txt` 中的 12 个主城安全区定义；
2. 查找 `MapInfo.FileName == mapName`；
3. 加载该地图的 `.map` 阻挡文件，计算中心 `(X, Y)` 半径 `R` 内的可行走点集合，赋予 `Region`；
4. 计算中心 `(X, Y)` 邻近 `[-2..2]` 矩形内的可行走点集合，赋予独立的 `BindRegion`；
5. 保存并同步镜像。

#### 模块 B：`--fix-dungeons`（地下城/洞窟层级连接全量打通）
1. 读取并逐行解析 `Mapinfo.txt`；
2. 过滤提取格式为 `SourceMap Sx,Sy -> DestMap Dx,Dy` 的连接行；
3. 在 System.db 的 `MapInfo` 库中查找：
   - 若 `SourceMap` 和 `DestMap` 都在库中登记（存在 MapInfo）：
     - 检查是否已经存在该连接（防止重复插入）；
     - 读取两张地图的 `.map` 文件，校验 `(Sx, Sy)` 与 `(Dx, Dy)` 是否可行走（若不可行走，向周围 8 方向寻找最近的可行走格子）；
     - 创建/更新 `SourceRegion`（赋予 Cave/Exit/Teleport 图标）与 `DestinationRegion`；
     - 绑定到 `MovementInfo`；
4. 统计新增与更新的地下城连接数量，保存并同步。

---

## 5. 验收标准与交付清单

任务执行完毕后，执行智能体必须达到以下 4 条硬性验收指标：

1. **体检验证（Audit Clean）**：
   运行 `dotnet run --project Tools/MapConnectionFixer/MapConnectionFixer.csproj -- --audit`：
   - 所有的地下城抽检链（天然洞穴、比奇矿区、跳蚤洞、沃玛神殿、祖玛神殿、石墓、潘夜石窟）在库内已登记的层级全部显示 **`✔ 双向连通`**；
   - 12 个主要主城安全区的偏差检测全部显示 **`✔ 与原版相符`**。
2. **服务端无头加载 0 报错（Server Boot Clean）**：
   启动 `./ServerCore`（端口 7000），观察启动日志：
   - **0 个 `[Safe Zone] Bad Location`**；
   - **0 个 `[Movement] Bad Origin`**；
   - 所有已连通地图加载成功，无异常崩溃或死循环。
3. **数据库一致性**：
   运行 `md5sum`，5 处 `System.db` 的 MD5 必须完全一致。
4. **Git 提交纪律**：
   - 必须通过 `dotnet build GodotClient/ZirconClient.csproj` 验证客户端编译通过；
   - 必须通过 `dotnet build ServerCore/ServerCore.csproj` 验证服务端编译通过；
   - 仅暂存（stage）本次工具代码、文档等相关改动，严禁 stage 他人的 WIP 内容；
   - 遵循中文 commit 规范（例如 `fix(map): 批量修复全服地下城层级连接与主城安全区坐标`），并 `git push origin master`。

---

## 6. 附录：核心地图与洞窟代码速查对照

| 地图代码 | 名称 | 权威连接关系与层级 |
| :--- | :--- | :--- |
| **0** | 比奇县 | `0 ↔ D001` (天然洞穴), `0 ↔ D401` (跳蚤洞), `0 ↔ 1` (道馆), `0 ↔ 2` (蛇谷), `0 ↔ 3` (沙巴克) |
| **D001-D003** | 天然洞穴 | `D001 (1-2层) ↔ D002 ↔ D003` |
| **D011-D012** | 骷髅洞 | `D011 ↔ D012` |
| **D202-D203** | 比奇废矿区 | `D202 (2层) ↔ D203 (3层)` |
| **D401-D406** | 绝望谷/跳蚤洞 | `D401 ↔ D411 ↔ D412 ↔ D413 ↔ D414 ↔ D415 ↔ D416` (详见 Mapinfo.txt 对应分支) |
| **D1001-D1002**| 沃玛神殿 | `D1001 ↔ D1002` |
| **D1101-D1105**| 祖玛神殿 | `D1101 ↔ D1102`, `D1104 ↔ D1105` |
| **D1201-D1205**| 石墓(猪洞) | `D1201 ↔ D1202 ↔ D1204 ↔ D1205` |
| **D1401-D1405**| 潘夜石窟 | `D1401 ↔ D1402 ↔ D1403 ↔ D1404 ↔ D1405` |
| **1** | 道馆 | 关口连接比奇 `0`、银杏山谷 `01`、毒蛇山谷 `2` |
| **2** | 蛇谷 | 关口连接比奇 `0`、盟重土城 `4`、道馆 `1` |
| **4** | 盟重省(土城)| 关口连接蛇谷 `2`、石墓 `D1201`、祖玛 `D1101` |
| **5** | 沙漠绿洲 | 绿洲集市安全区 `(216, 185)`，连接跳蚤洞与沙漠副本 |

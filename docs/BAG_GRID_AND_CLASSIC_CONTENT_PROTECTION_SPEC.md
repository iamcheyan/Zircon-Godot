# 背包网格与经典纯净内容保护规范及事故复盘

> **文档性质**：架构保护规范 & 事故复盘追责档案  
> **生效时间**：2026-10-04  
> **状态**：**强制执行（所有提交必须遵守）**

---

## 一、事故背景与复盘

2026-10-04 用户反馈在游戏内发现以下严重劣化现象（用户现场取证图 `/tmp/ksp-clip-1791082490064-3001643.png`）：
1. **背包网格体系彻底崩溃**：大件衣服（道袍/重盔甲/恶魔长袍等）原有的 $2\times 3$ 规整拟真大格全部消失，退化为 $1\times 1$ 单格挤压堆叠；
2. **画面撕裂与切片残片**：多件装备在大图标切片渲染时相互覆盖穿透，相邻格子残存切片；每一个格子上均被强行盖上了红圈斜杠（🚫，无法穿戴标志）；
3. **私服/刺客装备倒灌污染**：背包与数据库中重新充斥了此前已严格清洗剔除的刺客（Assassin）专属衣服（如暗影道袍、潜行战甲等），破坏了经典纯净 1.45/三职业基准。

---

## 二、事故根本原因追查

经全仓库 commit 历史与代码审查，本次事故系两次不负责任的盲目修改叠加所致：

### 1. 背包网格退化根因：commit `0038e5a0` 错误修改图库绑定
- **错误逻辑**：在 `DXItemGrid.cs` 的 `GetLegacyFootprint` 中，擅自将尺寸获取修改为固定查 `StoreItem`：
  ```csharp
  // 错误历史代码：
  var texture = MirSkin.GetTexture(GridItemLibrary, item.Info.Image); // 固定为 StoreItem
  ```
- **灾难传导链**：
  1. 经典传奇 3（EI）中，物品在背包里的多格拟真手绘大图（如衣服 $48\times 100$ 像素）存放在 **`Inventory.wil`** 中；
  2. 而现代 Zircon 的 `StoreItem.Zl` 库中的对应索引全部为 **$24\times 22$ 像素的缩略小图标**；
  3. 尺寸计算查 `StoreItem` 时，$(24+35)/36 = 1$，$(22+35)/36 = 1$，导致**所有大件衣服被判定为 $1\times 1$ 单格**；
  4. 背包 First-Fit 算法以为所有物品都是 $1\times 1$，将几十件物品密集排在第一行与后续单格中；
  5. 但格子的实际绘制层（`InventoryDialog` 中的 `ItemLibraryFile = LibraryFile.Inventory`）却去 `Inventory.wil` 抓取了 $48\times 100$ 的大图；
  6. 结果：**尺寸是 1x1，渲染却是 2x3**。切片算法在单格内强行截取局部，导致上下左右相邻格子出现物品切片重叠残留；且因为判定为单格，每个格子都被 `DrawItemBadges` 盖上了无法穿戴的 🚫 徽章！

### 2. 刺客装备倒灌根因：commit `26742b5f` 粗暴恢复未做职业过滤
- **错误逻辑**：在补回误删的衣服数据时，没有对照经典三职业规范做过滤，把全部 104 件原始数据粗暴导回，其中夹带了 **30 件刺客（Assassin）衣服**与 **1 件刺客头盔**；
- 并且在测试脚本中粗暴使用 `@make` 刷给 `TestHero` 角色，导致测试角色背包里充满了刺客专属装备。

---

## 三、修复详情与实机验收

### 1. 彻底清除刺客数据（回退至 399 件经典纯净装备）
- 编写专用清洗工具对 `System.db` 进行强力清洗：
  - 剔除了全部 30 件刺客专属衣服（Shape 901..930 等）；
  - 剔除了 1 件刺客头盔（Laurel Mask）；
  - 级联清除了 `Users.db` 中测试角色 `TestHero` 背包及身上的所有刺客物品；
- **System.db 四处镜像 MD5 严格对齐**：
  ```
  5713cb2ca588673f5fdb8777be368016  Debug/ServerCore/Database/System.db
  5713cb2ca588673f5fdb8777be368016  /home/tetsuya/mir2ei/Data/System.db
  5713cb2ca588673f5fdb8777be368016  /home/tetsuya/mir2ei/Database/System.db
  5713cb2ca588673f5fdb8777be368016  ./System.db
  ```
  当前数据库包含 399 件经典纯净战法道三职业与基础道具。

### 2. 恢复背包尺寸双层回退机制
修改 `GodotClient/Controls/DXItemGrid.cs` 中的 `GetLegacyFootprint`：
```csharp
    private void GetLegacyFootprint(ClientUserItem item, out int width, out int height)
    {
        width = height = 1;
        if (item?.Info == null) return;

        // 优先从 LegacyEI 原版 Inventory.wil 读取经典多格大图标（衣服 2x3, 武器 1x3/1x4 等）；
        // 缺图或索引超 1440 的后期/杂物道具，平滑回退至 StoreItem。
        var texture = MirSkin.GetTexture(ItemLibraryFile, item.Info.Image);
        if (texture == null)
        {
            texture = MirSkin.GetTexture(LibraryFile.StoreItem, item.Info.Image);
        }
        if (texture == null) return;

        Vector2 size = texture.GetSize();
        width = Math.Max(1, ((int)size.X + DXItemCell.CellWidth - 1) / DXItemCell.CellWidth);
        height = Math.Max(1, ((int)size.Y + DXItemCell.CellHeight - 1) / DXItemCell.CellHeight);
    }
```
**关键原则**：计算尺寸使用的图库，必须与格子渲染时使用的图库（`ItemLibraryFile`，在背包中即 `Inventory.wil`）严格保持同源！

### 3. 实机运行与截图验收
通过 Xvfb 启动客户端，登录本地 7000 服务端，按 `Q` 呼出背包并滚轮滚动，验证结果如下：

| 验证项 | 预期行为 | 实测结果 |
|---|---|---|
| **衣服大件尺寸** | 规整占据 $2\times 3$ 大格，居中对齐 | **PASS**（见验收截图） |
| **武器尺寸** | 规整占据 $1\times 3$ 或 $1\times 4$ 大格 | **PASS**（见验收截图） |
| **切片残留 & 徽章** | 无任何残片重叠；占位格无重复 🚫 徽章 | **PASS** |
| **锁链滚动条** | F280 锁链对齐黑槽，金色圆点随滚轮准确上下滑动 | **PASS** |
| **属性悬浮卡** | 鼠标指向装备（如炼狱）即时弹出准确信息 | **PASS** |
| **职业纯净度** | 背包内刺客装备全部消失，全为经典三职业装备 | **PASS** |

验收截图存放路径：
- `docs/screenshots/bag_grid_restored/inventory_restored.png`（背包展开多格排布效果）
- `docs/screenshots/bag_grid_restored/inventory_scrolled.png`（背包下滚与炼狱属性悬浮）

---

## 四、铁律规范：严禁胡搞与破坏约束

所有开发人员及 AI Agent 必须无条件遵守以下铁律：

### 🔴 铁律一：严禁擅自改动背包网格（`DXItemGrid` / `DXItemCell`）的图库与尺寸逻辑
1. **严禁修改 `GetLegacyFootprint` 的图库顺序**：必须优先使用 `ItemLibraryFile`（`Inventory.wil`）查大图尺寸；
2. **严禁将背包图库强行降级为 `StoreItem`**：EI 的拟真多格体系依托于 `Inventory.wil` 的大图，`StoreItem` 仅作为后期无大图物品的回退，不可颠倒主次；
3. **严禁随意改动 6 列网格几何常量与切片计算公式**：单元格 $36\times 36$，步长 $36$，必须严格锁死。

### 🔴 铁律二：严禁向游戏数据中注入刺客（Assassin）及私服超纲内容
1. 本项目目标是**正统传奇 3（Mir 3 EI 1.45）经典纯净三职业**；
2. 数据库 `System.db` 的 `ItemInfo` 严禁出现 `RequiredClass = Assassin` 的任何装备；
3. 任何恢复数据操作，必须经过白名单/经典物品映射表核验，绝不允许未经审查全量盲导。

### 🔴 铁律三：数据库修改必须保持 4 处镜像 MD5 绝对一致
修改 `System.db` 时必须遵守《写库纪律》：
1. 服务端运行期间**绝不允许**写库，必须先停止 ServerCore；
2. 每次修改必须同步覆盖以下 4 处路径，并运行 `md5sum` 核对无误：
   - `Debug/ServerCore/Database/System.db`
   - `/home/tetsuya/mir2ei/Data/System.db`
   - `/home/tetsuya/mir2ei/Database/System.db`
   - `./System.db`（仓库根目录）

### 🔴 铁律四：验证深度必须达到真实客户端实机运行（行为验证 ≥ 编译验证）
1. `dotnet build` 成功**绝不代表逻辑正确**；
2. 凡涉及 UI、背包、地图、数据库、登录的任何修改，**必须启动服务端与 GodotClient 实际进游戏截图取证**；
3. 提交 PR 或 Commit 时，必须提供截图证据并记录在文档中。未经实机验证的提交一律视为无效提交。

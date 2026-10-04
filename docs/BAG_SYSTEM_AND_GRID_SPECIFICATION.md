# Zircon 传奇3 背包系统架构与物品网格（Footprint）机制规范

本文档详尽记录 Zircon 客户端背包系统的核心架构、原版（Legend of Mir 3 EI）多格物品占位（Footprint）计算机制、First-Fit 摆放算法、稀疏槽位与网格行数适配、F280 锁链滚动条与金色滑块几何映射，以及全窗口滚轮事件响应机制。本文档作为背包模块的技术白皮书与后续维护基准。

---

## 目录
1. [背包系统架构总览](#一背包系统架构总览)
2. [物品占格与 Footprint 尺寸计算原理](#二物品占格与-footprint-尺寸计算原理)
3. [网格摆放算法（First-Fit Placement）与槽位映射](#三网格摆放算法first-fit-placement与槽位映射)
4. [稀疏槽位撑爆网格行数 Bug 深度排查与修复](#四稀疏槽位撑爆网格行数-bug-深度排查与修复)
5. [F280 锁链滚动条、金色滑块与几何对齐](#五f280-锁链滚动条金色滑块与几何对齐)
6. [滚轮事件处理与逐行平滑滚动机制](#六滚轮事件处理与逐行平滑滚动机制)
7. [关键代码速查与维护要点](#七关键代码速查与维护要点)

---

## 一、背包系统架构总览

### 1.1 服务端数据模型 (Server-Side)
- **数据结构**：服务端的玩家背包由 `PlayerObject.Inventory` 维护，类型为 `UserItem[]`。
- **稀疏槽位设计**：每个物品在背包中拥有唯一的槽位索引（`Slot`）。由于装备移动、掉落拾取、穿戴脱下等操作，物品并不一定按连续升序排列在 `0, 1, 2, ...`，而是稀疏分布（例如一个角色可能仅在 Slot 1、13、25、115、186 拥有物品）。
- **容量自动扩容**：服务端与客户端通过 `EnsureInventoryCapacity` 保证数组长度覆盖最大槽位：`ItemGrid.Length >= MaxSlot + 1`。

### 1.2 客户端表现层分层 (Godot Client)
客户端背包界面的主要控件构成如下：
```
InventoryDialog (DXWindow 根窗口, 承载 F250 底图)
 ├── _titleLabel / _legacyModeLabel (包袱 / 修补 / 变卖 / 储存 模式标题)
 ├── CloseButton (关闭按钮, 右上角)
 ├── Grid (DXItemGrid, 物品展示与放置网格)
 │    ├── Cells[] (DXItemCell[], 基础单元格集合)
 │    └── _legacyHighlight (DXControl, 悬停/选中的多格绿色高亮边框)
 ├── _legacyScrollClip (DXControl, 滚动条视口裁剪区)
 │    └── _legacyScrollTrack (DXImageControl, F280 锁链滑动贴图)
 ├── _legacyScrollBar (DXVScrollBar, 纯逻辑垂直滚动条控制器, Visible=false)
 ├── _legacyGaugeDrag (LegacyGaugeDragSurface, 锁链轨道的命中与拖拽层)
 └── 底部面板 (金币 GoldLabel, 游戏币 GGLabel, 负重条 WeightBar)
```

### 1.3 原版 EI (F250) 与现代布局差异
- **现代 Zircon 布局**：每个物品固定占用 1×1 单元格，行数固定，滚动条为标准的 WinForms/DX 滚动条。
- **经典 EI 布局 (`_legacyEiLayout = true`)**：
  - 背包背景为经典 F250 贴图（宽度 280px，高度约 400px）。
  - 可见物品网格严格限定为 **6 列 × 6 行** 可视区（每格步距 36×36 像素）。
  - 物品具有真实的体积极限（如衣服 2×2、双手武器 1×3 或 2×3、小药瓶 1×1）。
  - 滚动条采用嵌入式的铜制锁链与滑动金珠圆点（F280 贴图）。

---

## 二、物品占格与 Footprint 尺寸计算原理

### 2.1 占格规则与物理体积
在经典传奇3中，不同种类的物品占用背包网格中不同的空间大小：

| 物品类别 | 经典占格 (宽 × 高) | 典型装备示例 |
| :--- | :---: | :--- |
| **大型武器** | 2 × 3 或 1 × 3 | 屠龙、裁决之杖、炼狱、命运之刃、修罗、降魔 |
| **轻型武器** | 1 × 2 或 1 × 3 | 乌木剑、铁剑、短剑、青铜剑、偃月、降魔 |
| **重型衣服/盔甲** | 2 × 2 | 重盔甲、魔法长袍、灵魂战衣、战神盔甲、恶魔长袍 |
| **大型头盔** | 2 × 2 | 骷髅头盔、黑铁头盔 |
| **小型头盔/帽子** | 1 × 1 或 1 × 2 | 道士头盔、神秘头盔、魔法头盔 |
| **首饰（项链/手镯/戒指）**| 1 × 1 | 绿色项链、阎罗手套、力量戒指、红宝石戒指 |
| **鞋子/靴子** | 1 × 1 | 鹿皮靴、赤飞靴、黑铁靴 |
| **消耗品与杂物** | 1 × 1 | 强效金创药、强效魔法药、随机传送卷、铁矿、回城卷 |

### 2.2 图标纹理采样与尺寸换算公式
客户端动态计算物品尺寸的核心代码位于 `GodotClient/Controls/DXItemGrid.cs` 中的 `GetLegacyFootprint` 方法：

```csharp
private static void GetLegacyFootprint(ClientUserItem item, out int width, out int height)
{
    width = height = 1;
    if (item?.Info == null) return;

    var texture = MirSkin.GetTexture(GridItemLibrary, item.Info.Image);
    if (texture == null)
    {
        texture = MirSkin.GetTexture(LibraryFile.Inventory, item.Info.Image);
    }
    if (texture == null) return;

    Vector2 size = texture.GetSize();
    width = Math.Max(1, ((int)size.X + DXItemCell.CellWidth - 1) / DXItemCell.CellWidth);
    height = Math.Max(1, ((int)size.Y + DXItemCell.CellHeight - 1) / DXItemCell.CellHeight);
}
```

#### 数学换算原理：
单元格尺寸为：
$$CellWidth = 36\text{ px}, \quad CellHeight = 36\text{ px}$$

对于任意贴图尺寸 $(W_{tex}, H_{tex})$，其占格通过天花板除法（Ceil Division）计算：
$$W = \max\left(1, \left\lfloor\frac{W_{tex} + 35}{36}\right\rfloor\right), \quad H = \max\left(1, \left\lfloor\frac{H_{tex} + 35}{36}\right\rfloor\right)$$

- 例如：
  - 衣服贴图宽度 70px，高度 72px：
    $$W = \lfloor(70+35)/36\rfloor = 2, \quad H = \lfloor(72+35)/36\rfloor = 2 \implies 2 \times 2$$
  - 项链贴图宽度 32px，高度 30px：
    $$W = \lfloor(32+35)/36\rfloor = 1, \quad H = \lfloor(30+35)/36\rfloor = 1 \implies 1 \times 1$$
  - 武器贴图宽度 36px，高度 105px：
    $$W = \lfloor(36+35)/36\rfloor = 1, \quad H = \lfloor(105+35)/36\rfloor = 3 \implies 1 \times 3$$

### 2.3 必须使用 `StoreItem.Zl` 的原因（关键教训）
在早期版本中，曾试图从 `LibraryFile.Inventory` 读取贴图尺寸。这引发了灾难性的 Bug：
1. `LegacyEI/Data/inventory.wil` 仅包含 **1440 帧**。
2. 经典高等级装备在 `System.db` 中的 `ItemInfo.Image` 位于更高索引区（如 7913、8187、8192 等）。
3. 当在 `inventory.wil` 查找高索引时全部命中空帧（`null`），导致所有高级装备的 footprint 退化成 $1 \times 1$。
4. 随后实际单元格渲染时又从 `StoreItem.Zl`（8590 帧）读取了真实的大图，大图以单格坐标强行绘制，导致**大量装备互相重叠挤压在第 1 行，16 件装备只看得到 3 件**。
5. **结论与铁律**：Footprint 尺寸探测图库必须与单元格渲染图库严格同源（即 `GridItemLibrary = LibraryFile.StoreItem`）。

---

## 三、网格摆放算法（First-Fit Placement）与槽位映射

### 3.1 First-Fit 矩形扫描摆放
因为现代网络协议中，服务器仅向客户端下发物品所在的稀疏 `Slot`，并不存储该物品在 6 列网格中的二维坐标 $(X, Y)$。客户端必须完全拟真原版 EI 的占用排布：从网格左上角 $(0, 0)$ 开始逐行从左到右扫描，寻找首个足以容纳 $(W \times H)$ 矩形的空位。

```csharp
private static bool TryPlace(bool[,] occupied, int width, int height, out int x, out int y)
{
    int columns = occupied.GetLength(0);
    int rows = occupied.GetLength(1);
    width = Math.Min(width, columns);
    for (y = 0; y + height <= rows; y++)
    {
        for (x = 0; x + width <= columns; x++)
        {
            bool free = true;
            for (int row = y; row < y + height && free; row++)
                for (int col = x; col < x + width; col++)
                    if (occupied[col, row]) { free = false; break; }
            if (free) return true;
        }
    }
    x = y = -1;
    return false;
}
```

### 3.2 原点格（Origin Cell）与占位格（Placeholder Cell）
当一个 $2 \times 2$ 的衣服放置在 $(x=1, y=2)$ 时，它占用了 4 个格子：
$$(1, 2), (2, 2), (1, 3), (2, 3)$$
- **原点格 (Origin Cell)**：左上角格子 $(1, 2)$。
  - 标记 `_legacyCellOrigins[cell] = true`。
  - **唯一负责绘制整张贴图**的格子。
  - 负责显示物品数量、持久度条等覆盖物。
- **占位格 (Placeholder Cell)**：其余 3 个被覆盖的格子。
  - 标记 `_legacyCellOrigins[cell] = false`。
  - 不绘制独立图标，防止大图被重复绘制 4 次造成画面撕裂或重叠黑边。
  - 当鼠标在占位格上悬停或点击时，其操作通过映射表重定向到该物品。

### 3.3 双向映射表与丢弃/锁定死锁修复
在 `DXItemGrid.cs` 中维护了三张核心映射表：
1. `_legacyCellAnchors[cellIndex] -> slot`：表示网格第 `cellIndex` 格属于服务端的哪个物品槽位（所有被覆盖的格都填入该 slot）。
2. `_legacyCellOrigins[cellIndex] -> bool`：表示该格是否为该物品的原点。
3. `_legacySlotOriginCell[slot] -> cellIndex`：表示服务端槽位号为 `slot` 的物品，其原点格在客户端的哪个单元格。

#### 槽位不等于单元格（Slot != CellIndex）修复：
在非 footprint 模式下，第 5 个槽位的物品就放在第 5 个单元格。但在 footprint 模式下：
- 例如服务端 Slot 6 的重盔甲，可能被 First-Fit 算法摆放在第 0 个单元格。
- **历史严重 Bug**：旧代码在玩家拖出丢弃物品时，客户端将单元格 0 锁为等待服务端确认（`cell.Locked = true`）。当服务端处理完成回包通知 `Slot 6 已删除/更新` 时，旧代码直接调用 `Cells[6].Locked = false` 解锁。
- **后果**：第 6 格被解锁了，但第 0 格永久保持 `Locked = true`，导致该格子彻底卡死，无法再移动物品或穿戴装备。
- **解决方案**：引入 `ResolveSlotCell(int slot)` 方法：
  ```csharp
  public int ResolveSlotCell(int slot)
  {
      if (!UseLegacyFootprints) return slot;
      if (slot < 0 || slot >= _legacySlotOriginCell.Length) return -1;
      return _legacySlotOriginCell[slot];
  }
  ```
  所有收到服务端回包针对 `slot` 的 UI 刷新与解锁操作，全部必须经过 `ResolveSlotCell` 转换为客户端可视格索引。

---

## 四、稀疏槽位撑爆网格行数 Bug 深度排查与修复

### 4.1 现象与根因剖析
在先前的版本中，测试账号登录后打开背包，发现滚动条异常狭长，轻轻一滚就拉到几十甚至上百行，整个背包被拉扯出大量无意义的空行，而真正的物品散落视口外。

#### 根因：
服务端同步物品槽位时，存在高槽位索引（例如伴侣、任务、特定临时槽等，使槽位索引达到 186）。
客户端调用 `EnsureInventoryCapacity(186)` 后，`ItemGrid` 数组长度扩充到了 187。
旧版 `GetLegacyRequiredRows` 计算代码如下：
```csharp
// 错误旧代码：
int rows = Math.Max(minimumRows, ItemGrid.Length); // 直接把 187 长度当成 187 行！
```
导致 6 列网格被计算为 $187 \text{ 行} \times 6 = 1122 \text{ 格}$！而实际上背包里总共只有十几件物品，它们在 First-Fit 紧凑排布下仅需 7~8 行。

### 4.2 修复算法：自适应探测与翻倍扩容
新算法 `GetLegacyRequiredRows` 的设计原则：
1. **行数必须由实际二维摆放结果决定，绝不信任数组长度**。
2. **可视行数起步**：最少 6 行（F250 视口高度）。
3. **最坏情况预估**：物品数量为 $N$，初始行数至少预估为 $\lceil N / 6 \rceil$。
4. **自适应翻倍重试**：模拟摆放，若当前高度无法容纳所有物品，则行数翻倍（`rows *= 2`），再次尝试，直至全部摆下。上限 4096 兜底。
5. **紧缩截断**：摆放成功后，取所有已放置物品的最大 $Y + H$ 作为最终网格行数。

```csharp
public int GetLegacyRequiredRows(int minimumRows = 6)
{
    if (!UseLegacyFootprints || ItemGrid == null) return Math.Max(1, minimumRows);

    int columns = Math.Max(1, GridSize.X);
    int rows = Math.Max(1, minimumRows);
    int maxRow = minimumRows;

    int itemCount = 0;
    for (int i = 0; i < ItemGrid.Length; i++)
        if (ItemGrid[i]?.Info != null) itemCount++;
    rows = Math.Max(rows, (int)Math.Ceiling(itemCount / (double)columns));

    while (true)
    {
        var occupied = new bool[columns, rows];
        maxRow = minimumRows;
        bool placedAll = true;
        for (int slot = 0; slot < ItemGrid.Length; slot++)
        {
            var item = ItemGrid[slot];
            if (item?.Info == null) continue;
            GetLegacyFootprint(item, out int width, out int height);
            if (!TryPlace(occupied, width, height, out _, out int y))
            {
                placedAll = false;
                break;
            }
            MarkPlacement(occupied, 0, y, width, height);
            maxRow = Math.Max(maxRow, y + height);
        }
        if (placedAll) break;

        int grown = rows * 2;
        if (grown > rows && grown <= 4096) { rows = grown; continue; }
        break;
    }
    return maxRow;
}
```
经过此修复，背包行数精准收敛到真实占用行数（例如 7 行），滚动条比例完全恢复正常。

---

## 五、F280 锁链滚动条、金色滑块与几何对齐

### 5.1 视觉结构与素材约定
- **底板凹槽 (Track Well)**：在 F250 背包主图上，右侧垂直凹槽坐标位于：
  - 左上角：$(X = 248, Y = 40)$
  - 尺寸：宽 16px，高 216px（Y 范围 40 到 256）
- **锁链贴图 (F280)**：
  - 贴图库：`LibraryFile.Interface`
  - 帧号：`Index = 280`
  - 尺寸：宽 16px，高 424px
  - 关键特征：锁链中央位置**已经内置烘焙（Baked）了一颗发光的金色圆珠**作为滑块（Thumb Dot）。
  - 圆珠中心坐标：`bakedDotY = 208` 像素。

### 5.2 视口裁剪与滑块移动公式
由于金珠是直接画在 424px 长的整条锁链贴图上的，为了实现“金珠随着滚动条上下滑动”的效果，原版 EI 的做法是：
**外层使用一个高 216px 的裁剪容器（Clip Viewport），将整条 424px 的锁链在裁剪容器内部上下平移**。

```
+------------------------------------+ Y=40 (窗口坐标)
| [裁剪容器 _legacyScrollClip]       |
| 尺寸: 16 x 216                     |
|                                    |
|   +----------------------------+   |
|   | 锁链贴图 (_legacyScrollTrack)|  | (在裁剪容器内平移)
|   | 尺寸: 16 x 424             |   |
|   |         ...                |   |
|   |      [ 金色圆珠 ]          |   | <--- 目标 thumbY 位置
|   |       bakedY=208           |   |
|   |         ...                |   |
|   +----------------------------+   |
+------------------------------------+ Y=256
```

#### 滑块几何数学公式：
设滚动条进度比例为 $t \in [0.0, 1.0]$：
$$t = \frac{\text{Value} - \text{MinValue}}{\text{MaxValue} - \text{MinValue} - \text{VisibleSize}}$$

两端预留缓冲距离 $pad = 10\text{ px}$，有效行程为：
$$travel = \text{ClipHeight} - 2 \times pad = 216 - 20 = 196\text{ px}$$

目标金珠在视口内的实际坐标 $ThumbY$ 为：
$$ThumbY = pad + t \times travel$$

为了让锁链贴图中的圆珠刚好对准 $ThumbY$，整条锁链在容器内的 $Y$ 偏移为：
$$\text{Track.Location.Y} = \text{round}(ThumbY - bakedDotY)$$

- **顶部极限 ($t = 0$)**：
  $$ThumbY = 10 \implies \text{Location.Y} = 10 - 208 = -198\text{ px}$$
- **底部极限 ($t = 1$)**：
  $$ThumbY = 206 \implies \text{Location.Y} = 206 - 208 = -2\text{ px}$$

### 5.3 全轨道拖拽驱动 (`LegacyGaugeDragSurface`)
由于 `_legacyScrollBar` 本身是无视觉的逻辑控件（`Visible=false`），为了支持鼠标在锁链槽上的任意点击与按住拖动：
- 创建覆盖整条黑槽（$X=248, Y=40, W=16, H=216$）的专有命中层 `LegacyGaugeDragSurface`。
- 在 `_GuiInput` 与 `_Process` 中捕获全局鼠标左键拖拽：
  ```csharp
  private void ApplyGaugeY(float y)
  {
      if (Target == null) return;
      int range = Target.MaxValue - Target.MinValue - Target.VisibleSize;
      if (range <= 0) return;
      const float pad = 10f;
      float travel = Mathf.Max(1f, Size.Y - pad * 2f);
      float t = Mathf.Clamp((y - pad) / travel, 0f, 1f);
      Target.Value = Target.MinValue + (int)Math.Round(t * range);
  }
  ```
- 用户无论点击槽的顶部、底部还是中途拖拽，金珠均丝滑跟随并实时刷新物品网格。

---

## 六、滚轮事件处理与逐行平滑滚动机制

### 6.1 滚轮死区 Bug 复盘
在早期的 GodotClient 中，用户在背包内滑动滚轮没有任何反应。经断点跟踪发现两大致命原因：
1. **默认 Change=10 导致数值钳位**：
   - `DXVScrollBar` 的基类默认步长为 `Change = 10`。
   - 当背包实际总行数为 7 行，可视行数为 6 行时，最大滚动范围 $Range = 7 - 6 = 1$。
   - 用户向前滚轮时，执行 `Value -= Change` 即 `0 - 10 = -10`。
   - 滚动条内部触发 `OnValueChanged`，由于 $-10 < \text{MinValue}(0)$，数值被自动钳位（Clamp）回 0。
   - 结果：数值从未发生实际改变，**上滚前 6~10 次完全死区**！
2. **逐行平滑滚动修复**：
   - 显式设置 `_legacyScrollBar.Change = 1`。
   - 每次滚轮只滚动 1 行网格，与原版传奇3逐行滚动体验完全对齐。

### 6.2 滚轮事件全树链条分发
Godot 控件树中，如果不作特殊处理，子控件接收到鼠标滚轮事件若未处理可能会中断传递。为了确保在背包任何位置滚动鼠标均能生效，构建了三重保障：

```
[鼠标滚轮输入]
   │
   ├─► 1. 悬停在某个物品格 (cell.MouseWheel)
   │     └─► 转发给 _legacyScrollBar.DoMouseWheel
   │
   ├─► 2. 悬停在网格空白处 (Grid.MouseWheel)
   │     └─► 转发给 _legacyScrollBar.DoMouseWheel
   │
   ├─► 3. 悬停在右侧锁链槽 (LegacyGaugeDragSurface.MouseWheel)
   │     └─► 转发给 _legacyScrollBar.DoMouseWheel
   │
   └─► 4. 悬停在窗口其它任意区域（负重条、金币区、边框）
         └─► 未消费冒泡至根窗口 (InventoryDialog.OnLegacyWindowWheel)
               └─► 集中兜底转发给 _legacyScrollBar.DoMouseWheel
```

每次网格因为行数变动调用 `CreateGrid()` 重建 `Cells` 时，都会通过 `BindLegacyInventoryScrollInputs()` 重新给所有新单元格挂载滚轮监听，杜绝了“滚动到底部后滚不回来”的事件失效隐患。

---

## 七、关键代码速查与维护要点

1. **步距常数**：
   - `DXItemCell.CellWidth = 36`
   - `DXItemCell.CellHeight = 36`
   - `InventoryDialog` 锁链槽：$X=248, Y=40, W=16, H=216$
2. **图库依赖**：
   - 网格内物品图标：`LibraryFile.StoreItem`（必须与渲染保持一致）
   - 背包主背景：`LibraryFile.Interface` Frame 250
   - 锁链与滑块：`LibraryFile.Interface` Frame 280
3. **禁止操作**：
   - 严禁在 `DXItemGrid` 中直接使用 `Cells[slot]` 访问单元格，必须通过 `ResolveSlotCell(slot)` 解析。
   - 严禁使用 `ItemGrid.Length` 作为行数，必须调用 `GetLegacyRequiredRows()`。
   - 严禁为 `_legacyScrollBar` 设置大于 1 的 `Change`。

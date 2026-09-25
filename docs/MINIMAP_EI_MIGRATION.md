# EI 小地图迁移报告 (MiniMap.Zl + System.db)

日期: 2026-08-11
任务: 把 EI 传奇3.0 客户端的小地图资源迁移到 Zircon Godot 客户端, 使游戏内
右上角小地图正确显示 EI 地图布局。

## 背景

- Zircon 客户端已把 544 张 EI 地图替换到位 (画面正确, 与 EI Map 目录 md5 一致),
  但小地图图库 `MiniMap.Zl` 仍是旧版 Zircon 资源 (537帧 v1 DXT1), 显示旧地图布局。
- 客户端加载机制 (未改任何源码):
  - `GodotClient/Controls/MiniMapDialog.cs`: `LibraryFile = LibraryFile.MiniMap`
    固定读 `Data/MiniMap.Zl`, `Image.Index = map.MiniMap` (来自 DB MapInfo.MiniMap)。
  - `LibraryCore/Libraries.cs`: `[LibraryFile.MiniMap] = @"Data\MiniMap.Zl"`。
  - `GodotClient/Controls/BigMapDialog.cs` 第56/133行: 大地图同样读
    `LibraryFile.MiniMap` + `map.MiniMap` → 替换 MiniMap.Zl 同时修好大小地图。

## 资源与转换

| 项 | 值 |
|---|---|
| EI 城镇大地图 | `Data/FMMap.wil`+`.wix`, 31 帧 (含 2 空白: f19/f29) |
| EI 洞穴小地图 | `Data/MMap.wil`+`.wix`, 255 帧 (含 101 空白) |
| 帧绑定表 | `Mir3-Research/Tools/mir3_client_simulator/data/map_bindings.json`, 182 条 |
| 工具 | `Tools/convert_wil_to_zl2.py` → `Tools/zl2writer.py` (PNG codec, raw deflate) |
| 合并脚本 | `Tools/convert_ei_minimap.py` |

### 合并布局 (MiniMap.Zl, 287 帧)

```
frame 0     = 空白占位   (对齐旧库: 帧0空, DB 索引 1-based)
frame 1-31  = FMMap f0-30   城镇 (0.map 比奇 f0 -> index 1)
frame 32-286= MMap  f0-254  洞穴 (MMap f1 -> index 33)
```

> 空白占位帧 0 是必须的: `MiniMapDialog.SetMap` 在 `Image.Index <= 0` 时把窗口
> 收缩成 32px 条 (无小地图)。比奇绑定 FMMap f0=0, 直接绑定会触发塌缩, 所以
> 整体 +1 偏移, 全部绑定帧从 1 起。

### 转换验证

- FMMap: `frames=31 payloads=29 blanks=2 errors=0 size=15,769,597`
- MMap:  `frames=255 payloads=154 blanks=101 errors=0 size=10,958,777`
- 合并:  `frames=287 payloads=183 blanks=104 size=26,728,312`
- 结构校验 (按 zl2writer/ZlImage.Read 布局): 元数据 287 条 present 183,
  index 183 条, 全部 PNG payload raw-deflate 可解压, 帧尺寸/offset 保留
  (f1=1200×800 比奇, f33=600×400 = MMap f1)。
- 可复现: `python3 Tools/convert_ei_minimap.py <EI Data> <out.Zl>` 输出与
  部署文件 md5 一致 (`ecdffb20...`)。
- 原 `MiniMap.Zl` (43,835,975 B) 备份至
  `/home/tetsuya/NAS/TMP/zircon-backup-20260811-095139/MiniMap-original.Zl`。

## DB 更新 (System.db, MirDB 格式)

用 C# 工具 (引用 LibraryCore, Session API 加载/保存) 遍历 MapInfo 更新:

- `FMMap.wil` 绑定: `MiniMap = frame + 1`
- `MMap.wil` 绑定: `MiniMap = frame + 32`
- 无绑定地图: `MiniMap = 0` (小地图收缩隐藏)

统计 (客户端 + 服务器两份):
- 地图总数 244, 绑定命中 38 (全部与 EI Map 目录文件 md5 一致, 无错绑),
  已更新 38, 归零 205/206, 保存后无残留错值。
- 关键绑定: `0` 比奇 f0 → 1; `2` 毒蛇山谷 f5 → 6; `D001` 半兽洞穴 f1 → 33;
  `D401` 废矿矿山入口 f11 → 43; `D1001` 赤月山谷1层 f101 → 133。
- 服务器副本 `Debug/ServerCore/Database/System.db` 同步, 两边 244 条值全同。
- DB 修改前已备份: `zircon-backup-20260811-095139/System-{client,server}-original.db`。
- Zircon 专属地图 (D201 废矿、D2301 戈鲁洞等, EI 绑定表无对应) 一律 MiniMap=0,
  小地图不显示 (按旧库也无对应帧, 行为一致)。

## 游戏内验证

- 启动本地服务器 (加载新 DB) + Godot 客户端 (`--screenshot-after-enter`)。
- 玩家位于 2.map (Banya Village = EI 毒蛇山谷): 右上角小地图窗口标题
  "Banya Village", 显示绿色林地 + 标记点, 与 EI FMMap f5 毒蛇山谷布局一致。
- 小地图渲染链路 (新 MiniMap.Zl → 客户端 ZlLibrary → 帧索引) 全程正确;
  未改任何客户端源码, 只替换数据 + DB。

## 文件清单

- 替换: `Debug/Client/Data/MiniMap.Zl` (287帧, EI FMMap+MMap 合并版)
- 更新: `Debug/Client/Data/System.db` + `Debug/ServerCore/Database/System.db`
  (MapInfo.MiniMap 38 条绑定修正, 206 条归零)
- 未动: `MiniMapIcon.Zl` (图标库, 与地图布局无关)
- 备份: `zircon-backup-20260811-095139/` 下 MiniMap-original.Zl 与两份 System.db

## 2026-08-11 沙巴克混合资源修正

`3.map` 当前明确保留 Zircon 原版地图资源，而不是 EI 地图。因此它不能沿用
EI 小地图迁移表中的 `FMMap f17 -> MiniMap.Zl frame 18`。旧 Zircon 的
`System.db` 记录为 `FileName=3, MiniMap=7`，对应旧 `MiniMap-original.Zl`
的第 7 帧（`800×600`, offset `(0,0)`）。

本次修正采用混合图库：保留 EI `MiniMap.Zl` 的全部 287 帧，仅用 Zircon 原版
第 7 帧替换当前图库第 7 帧，同时保留该帧的旧版尺寸和定位；客户端与服务器两份
`System.db` 均改为 `3 -> 7`。EI 第 18 帧仍为 `300×200`，用于其它 EI 地图，
未被删除或重绑。

验证记录：Godot `ZlReader` 实际读取混合文件时，frame 7 为 `800×600, offset=(0,0)`，
frame 18 为 `300×200, offset=(0,0)`；总帧数 287、有效 payload 183。地图几何
重构中已记录的 `3.map/Sabak.Zl` 索引偏移问题属于另一条独立问题，不能用小地图修正
代替，见 `Sabak_Map_Migration_Audit_2026-08-11.md`。

## 2026-09-26 修正：沙巴克覆盖了 frame 7，导致 4.map 显示成沙巴克

### 问题

2026-08-11 的「混合资源修正」把沙巴克小地图写进了 **frame 7**，但按 EI 布局
`frame 1-31 = FMMap f0-30`，**frame 7 = EI `FMMap` f6，是地图 `4` 的帧位**
（`4.map` 与 EI 同名图 md5 相同，见下表）。于是：

```
客户端 System.db:  FileName=3 (Sabuk Keep)  -> MiniMap 7
                  FileName=4 (Numa Village) -> MiniMap 7     ← 两个图共用一帧
MiniMap.Zl frame 7 = 沙巴克的 Zircon 原版小地图 (800×600)
```

结果 `4.map` 的小地图显示成沙巴克。

地图文件 md5 对照（Zircon `Debug/Client/Map` vs EI `mir2ei/Map`）：

| 地图 | 是否相同 | 含义 |
|---|---|---|
| `2.map` | 相同 | 对照组，按文件名绑定成立 |
| `3.map` | **不同** | 沙巴克是 Zircon 原版地图，确实需要 Zircon 小地图 |
| `4.map` | 相同 | 所以 `4.map` 就该用 EI `FMMap` f6 的小地图 |

### 修正做法

EI 布局的 287 帧全被 EI 帧位占满，Zircon 专属地图**没有任何可用帧位** —— 写进
哪个 EI 帧位都会覆盖另一张 EI 地图。所以改为把 Zircon 专属地图的小地图**追加在
EI 布局之后**（frame 287 起）：

```
frame 0-286  = 与 convert_ei_minimap.py 输出逐帧一致（EI 布局，未改动）
frame 287    = 沙巴克 Zircon 原版小地图 (800×600, offset (0,0))
```

工具：`Tools/apply_zircon_minimap_overrides.py`（在 EI 基准库上追加
`Tools/minimap_overrides/<地图文件名>.png`）。完整流程：

```bash
PYTHONPATH=<Mir3-Research>/Tools/common \
  python3 Tools/convert_ei_minimap.py <EI Data 目录> /tmp/minimap-base.zl
python3 Tools/apply_zircon_minimap_overrides.py \
  /tmp/minimap-base.zl Tools/minimap_overrides <输出 MiniMap.Zl>
# 脚本会打印追加得到的帧号，再写回两份 System.db：
#   DbMigrationTool --root <dir> set-minimap 3 287
```

> 顺序不能反：`convert_ei_minimap.py` 会重建全部 287 帧，必须在其之后叠加
> Zircon 追加帧，否则沙巴克的小地图会被 EI 原图覆盖。

### 变更结果

| 项 | 修正前 | 修正后 |
|---|---|---|
| `MiniMap.Zl` 帧数 | 287 | 288 |
| frame 7 | 沙巴克 (800×600) | EI `FMMap` f6 (1200×800) |
| frame 18 | 300×200（来源不明的手改值） | EI `FMMap` f17 (600×600)，回归 EI 布局 |
| frame 287 | — | 沙巴克 (800×600) |
| 客户端 DB `3` | MiniMap 7 | MiniMap 287 |
| 客户端 DB `4` | MiniMap 7（错） | MiniMap 7（正确，EI `FMMap` f6） |
| 服务器 DB `3` | MiniMap 7 | MiniMap 287 |

frame 18 的还原对画面无影响：客户端 DB 中没有任何地图引用 frame 18，
且服务端不向客户端发送 MiniMap（客户端用自己 `System.db` 的 `MapInfo.MiniMap`
渲染，见 `DatabaseLoader` → `Globals.MapInfoList`）。

`MiniMap.Zl` md5：`f83f6ec4…`（修正前）→ `ea09c837…`（修正后）。

### 验证

- 重建库与 EI 基准逐帧比对：前 287 帧**零差异**；frame 7/18/287 尺寸符合预期；
  沙巴克图与修正前 frame 7 逐像素一致。
- 客户端 DB 读回：`3 -> 287`、`4 -> 7`；服务器 DB 读回：`3 -> 287`。
- 游戏内：角色位于 `4.map`（Numa Village）时小地图显示正确（用户确认）。

### 遗留（本次未处理）

两份 `System.db` 的 `MapInfo.MiniMap` 存在既有多处不一致，例如服务器 DB
`FileName=4 -> 8`、`FileName=5 -> 9`，而客户端为 `4 -> 7`、`5 -> 8`；服务器 DB
还有 `D10032 -> 18`（洞穴指向城镇帧位）。服务端不发送 MiniMap，所以这些不影响
客户端画面，但双库同步问题独立存在，需要单独核对。

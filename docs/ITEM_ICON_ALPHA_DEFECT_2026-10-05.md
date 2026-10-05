# 物品图标 Image 层丢失 overlay 色 → 整块透明（2026-10-05 定位与修复）

> 结论一句话：`Data/*.Zl`（BC7 重编码图库）在转换时把 WIL 的 **opcode 0xC2
> 「overlay colour」** 像素**只写进了 Overlay 层、Image 层写成 alpha=0**；而原版
> 客户端画物品图标**只用 Image 层**，于是这些像素整块变透明——用户看到的
> 「衣服手臂/两侧下摆被截掉、只剩虚线」。涉及 `Inventory.Zl` / `Equip.Zl` 各 85 帧，
> 当前 `System.db` 里 60 件 Armour 的 `ItemInfo.Image` 命中。已修复并同步。

---

## 0. 资产权威位置（先读这一条）

**真正的游戏客户端资源在 `/home/tetsuya/mir2ei`**（`Zircon/Debug/Client` 是指向它的
软链）。客户端 `MirSkin.DataPath` 默认解析到 `/home/tetsuya/mir2ei/Data/`
（`ZIRCON_UI_DATA_PATH` → `MIR3_EI_ROOT/Data` → 默认值，见
`GodotClient/Controls/MirSkin.cs:ResolveDataPath`），所以：

| 路径 | 角色 | 本次是否改动 |
|---|---|---|
| `/home/tetsuya/mir2ei/Data/{Inventory,Equip}.Zl` | **权威运行资源（真客户端读这里）** | ✅ 已替换为修复件 |
| `/data/NAS/Mir2ei-godot/deploy/client/Debug/Client/Data/` | 部署镜像（其它机器/打包取这里） | ✅ 一起同步（原件保留 `.pre-overlay-hole-fix-20261005`） |
| `mir2ei/Data/Backup/pre-overlay-hole-fix-20261005-210215/` | 本次回滚点 | ✅ 新增 |
| `mir2ei_backup_20261003_7gb/`、`mir2ei.precleanup-backup-20260928/` | 历史快照备份 | ⛔ 不动（回滚参照） |
| `/data/NAS/Backups/Linux-asahi/Zircon/Debug/Client/Data/` | 旧快照（`System.db` 仍是 8-06 的 5.7MB 版） | ⛔ 不动（时间点备份，改一个文件反而会让它自相矛盾） |
| `/data/NAS/_IMPORT_DATA-2T_20260817/…/Mir3/Data/` | 2019 原版 Zircon 安装（考古对照） | ⛔ 不动 |

> 镜像纪律：以后再有 `Data/*.Zl` 修复，**必须同时改 mir2ei 与这个 deploy 镜像**，
> 否则从镜像重新部署会把缺陷带回来（本次已按此办理）。

---

## 1. 症状

同一件衣服（`Raiment Of High Priest (M)` / 大祭司法衣（男），`ItemInfo.Image=1003`）
两种渲染并存：

- **格子里的图标**（商店格/背包格，走 `Storeitem.Zl` 或 WIL 回退）**完整**；
- **跟随鼠标的图标**（原版拿起物品的 drag icon，legacy 模式走
  `LibraryFile.Inventory` → `Inventory.Zl`）**手臂、两侧下摆整块透明**，只留下
  原有的深色描边像素，看起来像「虚线」；用户原话：

  > 这种衣服、装备是有透明通道的，但是透明通道被渲染多了，导致衣服好多东西被
  > 截掉了，本来应该显示的现在也变成透了。

同一画面上「格子完整 / 跟随图标被抠」的差异，正是**两个不同图库命中不同数据**
造成的（`Storeitem.Zl` 没有这种洞，`Inventory.Zl` 有）。

---

## 2. 根因链（每一步都有可复现证据）

### 2.1 先确认不是渲染/解码问题

用**两个互相独立**的 BC7 解码器解同一段 payload：

```bash
# 生产路径（GodotClient ZlReader → BcnDecoder/BCnEncoder.NET）
godot-mono --path GodotClient res://Scenes/MapTestScene.tscn -- \
  --dump-zl-file=/home/tetsuya/mir2ei/Data/Inventory.Zl --dump-zl-index=1003 \
  --dump-zl-output=/tmp/zl1003.png
# 独立路径（Mir3-Research venv 的 texture2ddecoder / bc7dec）
python -c "..."   # 见 Tools/fix_zl_overlay_holes.py 的 layer()
```

两者 alpha=0 像素比例一致（0.4727 / 0.4752）⇒ **文件数据本身如此**，不是 BC7 解码、
不是 blend、不是 UI 缩放。

### 2.2 与「已知正确的同一幅画」对照

| 数据 | 不透明像素占比 | 说明 |
|---|---:|---|
| 源 WIL `LegacyEI/Data/inventory.wil` 第 1001 帧 | 74.5% | EI/mir3ei 客户端原画（完整） |
| 原版 Zircon `Inventory.Zl`（2019 老格式、Dxt1）第 1001 帧 | 74.5% | `_IMPORT_DATA-2T_20260817/…/Mir3/Data/Inventory.Zl` |
| **当前 `mir2ei/Data/Inventory.Zl`（ZL2/BC7）第 1002、1003 帧** | **52.7%** | 缺失 21.8 个百分点 |

### 2.3 缺失像素 = WIL opcode 0xC2 像素（决定性证据）

对当前 ZL 帧逐像素比对源 WIL 的 RLE 操作码图：

- 第 1002 帧「Image 透明但 Overlay 不透明」= 3048px；其中 **1417px** 落在源 WIL
  第 1001 帧的 0xC2 像素集（1421px）内 → 覆盖率 **100%**（差 4px 是帧号 1001↔1002
  的偏移）；
- 也就是说：**转换器把 0xC2 像素从 Image 层搬到了 Overlay 层**（Overlay 层的
  alpha 集合 = WIL 0xC2 集合，逐帧吻合）。

WIL 侧 opcode 语义（`LibraryEditor/WeMadeLibrary.cs:357` 与
`Mir3-Research/Tools/common/wilsdk.py` 一致）：

```
0xC0 skip / 0xC1,0xC3 solid colour / 0xC2 "Overlay Colour"（同色写入 colour+mask 两个平面）
```

原版 Zircon 的转换（2019 那批）把 0xC2 的颜色**也写进 Image 层**，与 WIL 一致；
当前这批（2026-08-06 的 BC7 重编码）只写进了 Overlay 层 → 回归。

### 2.4 为什么原版客户端会「缺」

- `Client/Controls/DXItemCell.cs`（原版）绘制物品图标只用 `ImageType.Image`；
  Godot 侧 `DXItemCell.DrawItemIcon → MirSkin.GetTexture(ItemLibraryFile, drawIndex)`
  同理（`GodotClient/Controls/DXItemCell.cs:306`）。
- `ImageType.Overlay` 在原版只用于**按装备颜色二次绘制**（染色区域）：
  `Client/Models/PlayerObject.cs:1139`、`Client/Scenes/Views/CharacterDialog.cs:2651` 等。
  因此 Overlay 层不能当「缺口的搭子」用——它是独立的一遍带色叠加。

### 2.5 范围扫描（全部 ZL2 图库逐帧）

用 `Tools/fix_zl_overlay_holes.py scan` 扫 `mir2ei/Data/*.Zl`：
**只有 `Inventory.Zl` 与 `Equip.Zl` 存在这种形态的洞，各 85 帧**
（帧号 940–1050、3320–3392，正好是全部「衣服/套装」图标）：

| 图库 | 有 Overlay 的帧 | 有洞的帧 | 洞像素 | 该库总像素占比 |
|---|---:|---:|---:|---:|
| `Inventory.Zl` | 85 | 85 | 147,556 | 17.9% |
| `Equip.Zl` | 85 | 85 | 201,250 | 13.3% |

`M-Hum.Zl`（世界身体）、`Ground.Zl`（地面掉落）、`Storeitem.Zl`（商店/格子）、
`Interface.Zl` 等**没有**这种洞，未被波及。

### 2.6 受影响的物品（当前库 60 件）

`ItemInfo.Image` 命中被修帧的 Armour（节选，完整可用下方脚本打印）：

```
1212 Raiment Of High Priest (M)   Image=1003    1213 Raiment Of High Priest (F)  Image=1013
1172 Light Armour (M)             Image=942     1173 Light Armour (F)            Image=952
1188 Robe Of Balance (M)          Image=1001    1189 Robe Of Balance (F)         Image=1011
1254 Armour of the Sun Keeper (M) Image=3320    1255 Armour of the Sun Keeper (F) Image=3330
1268 Robes of the Titan Slayer(M) Image=3351    1269 Robes of the Titan Slayer(F) Image=3341
…
```

---

## 3. ZL2 容器结构（修复涉及的部分）

```
[0..42]   header: "ZL2" + Version(i32) + ImageCount(i32) + AtlasCount(i32)
          + DefaultCompression(u8) + Flags(u8) + Reserved(i16)
          + MetaOffset(i64) + MetaSize(i32) + IndexOffset(i64) + IndexSize(i32)
[meta]    Version(i32)+Count(i32)+AtlasGroupImageCount(i32)+AtlasPageSize(i32)
          然后每帧：Present(u8) + 87B 记录
          记录含：Position(i32) W/H/OX/OY(i16×4) ShadowType(u8) Shadow w/h/ox/oy(i16×4)
                  OverlayW/H(i16×2) AtlasPage(i32) SrcRect(4×i16) VisBounds(4×i16)
                  ImageCodec/ShadowCodec/OverlayCodec(u8×3) RuntimePref(u8×3)
                  StoredImage/Bc7/Fallback(i32×3)
                  StoredShadow/ShadowBc7/ShadowFallback(i32×3)
                  StoredOverlay/OverlayBc7/OverlayFallback(i32×3)
[data]    每个 entry 一段（raw deflate）：[Image 段][Shadow 段][Overlay 段]
[index]   EntryCount(i32) + 每 23B：Type(u8) Id(i32) UncompressedSize(i32)
          CompressedSize(i32) Offset(i64) Compression(u8) Codec(u8)
```

修复只动 **Image 段** 与元数据里的 `ImageCodec / StoredImageDataSize / Bc7DataSize /
FallbackDataSize`；`Shadow/Overlay` 段、尺寸偏移、帧序、索引 Id 全部原样。

---

## 4. 修复方案

工具：`Tools/fix_zl_overlay_holes.py`

1. 逐帧解码 Image(BC7) 与 Overlay(BC7)；
2. 对 `Image.alpha==0 && Overlay.alpha>0` 的像素：**取 Overlay 的颜色**、alpha 置 255；
   其余像素保持不变；
3. 修复帧的 Image 段改用 **PNG（无损）载荷**（`ImageCodec=Png(4)`，
   `StoredImageDataSize=PNG 字节数`，Bc7/Fallback 清零）——不需要 BC7 编码器，
   客户端两侧（Godot `ZlReader` / `RenderingCore`）都已支持 Png codec；
4. 重建容器：header 原样（meta 不移动）、payload 按原顺序重排、索引重建。

### 为什么用 Overlay 补而不是「源画」

- 同一帧的 Overlay 层就是那批像素的副本：与源画逐像素相关系数 0.973/通道，
  是 **×0.84~0.98 的略暗副本**（逐帧倍率不同、逐像素 ±15 噪声）——形状 100% 正确；
- 精确源画只有 22/85 帧能定位到（EI/mir3ei 的 `inventory.wil`，其中 mir3ei 版
  相对 EI 版整体错行 2 行，需 roll 校正后才对得上）；3320–3392 的「经典套装」是
  另一套配色、不在本机可达素材里；
- 用 Overlay 补是**零外部依赖、可枚举验证**的；色调差 ≤16% 且只出现在被抠的区域
  （离线放大对照图 `docs/screenshots/overlay-hole-fix-2026-10-05/fill_method_comparison.png`：
  truth | broken | overlay 补 | 最优缩放补，
  肉眼与原画一致）。

### 校验工具：`Tools/verify_zl_repair.py`（独立实现，不复用修复代码路径的判定）

契约：

1. 结构：索引条目数量/顺序/Id/Type/Compression/Codec 全等；每个 payload 都能解压且
   长度 == `UncompressedSize`；
2. 元数据：帧数、Position/Width/Height/Offset/Overlay 尺寸/Shadow 尺寸全等；只有被修
   帧允许 `ImageCodec/StoredImageDataSize` 变化（BC7→PNG）；
3. 像素：未修帧逐字节相同；被修帧 Overlay 层必须相同、Image 层**只允许**原空洞像素
   变化、修后这些像素 alpha=255、且修后不得再存在「被 Overlay 覆盖的空洞」。

结果：

```
Inventory.Zl: repaired=85  unchanged=8068  entries=2365  PASS
Equip.Zl    : repaired=85  unchanged=8608  entries=798   PASS
```

（`unchanged` 含无 entry 的占位帧；两个库的全部 8153 / 8590 帧元数据都逐项比对过。）

---

## 5. 部署与验证记录（2026-10-05）

| 项 | 值 |
|---|---|
| 修复前 `Inventory.Zl` | md5 `876e7d0b9bb934113052e3dce662f635`（5,177,264B） |
| 修复后 `Inventory.Zl` | md5 `1a7c9ab925e23e3c6c5a0d48d4fc8a62`（6,098,483B） |
| 修复前 `Equip.Zl` | md5 `d32c1a449376d69648685f2f1950dc88`（3,203,199B） |
| 修复后 `Equip.Zl` | md5 `2585cb7af6713fa9ccd028e8da2253b6`（4,213,559B） |
| 回滚点 | `/home/tetsuya/mir2ei/Data/Backup/pre-overlay-hole-fix-20261005-210215/`（两份原件） |
| deploy 镜像 | `/data/NAS/Mir2ei-godot/deploy/client/Debug/Client/Data/`，已同步为同一 md5；原件保留 `*.pre-overlay-hole-fix-20261005` |

1. **生产解码复核**：修复件用 Godot `MapTestScene --dump-zl-file/…-index/…-output`
   读回，日志 `codec=Png`、帧 1003/940/3320 导出的 RGBA 手臂与侧摆已恢复
  （对照图 `docs/screenshots/overlay-hole-fix-2026-10-05/broken_vs_fixed_frames.png`）。
2. **实机复核**：本机 ServerCore + EI 复古 HUD 客户端登录 `TestHero`：
   - 日志 `[MirSkin] UI 库 Inventory 在 legacy 目录缺失，回退到 …/Data/: Inventory.Zl`
     → 确认读的是 `mir2ei/Data`（WIL 无该帧时才回退 ZL）；
   - `[MapView] 贴图诊断: missingLibraries=0, missingTextures=0, emptyImageEntries=0`
     → 修复件全部可正常加载；
   - 人物窗口纸娃娃 A/B（把旧 `Equip.Zl` 换回后重启对比）：渲染**逐像素相同**
     （当前穿着的那件衣服不在这 85 帧里），⇒ 重写容器**无回归**；
   - 拿起物品的 drag icon（`LibraryFile.Inventory`，即用户截图里那个）正常绘制。
3. **状态自检**（修复后应为 0）：

```bash
VENV=/home/tetsuya/development/Mir3-Research/Tools/dbeditor/venv/bin/python
$VENV Tools/fix_zl_overlay_holes.py scan /home/tetsuya/mir2ei/Data/Inventory.Zl
$VENV Tools/fix_zl_overlay_holes.py scan /home/tetsuya/mir2ei/Data/Equip.Zl
# → 0 frames with overlay-covered holes
```

### 证据图（`docs/screenshots/overlay-hole-fix-2026-10-05/`）

- `broken_vs_fixed_frames.png`：左=修复前 `Inventory.Zl` 第 1002 帧（即用户截图里
  被抠掉手臂/侧摆的那件衣服，洞 + 虚线残留），右=修复后；其后为大套装帧与衬衫帧。
- `fill_method_comparison.png`：`真源画 | 修复前 | Overlay 原色补 | 最优缩放补`
  四列对照（帧 1000/1001/1010/950/940/1020）——形状完全一致，颜色差肉眼不可辨。
- `equip_zl_ab_paperdoll.png`：实机纸娃娃 A/B（左=旧 `Equip.Zl`，右=修复件），
  逐像素相同（当前穿着不在这 85 帧内）⇒ 容器重写无回归。

客户端**无需重新构建**（纯数据），重启客户端即生效。

---

## 6. 回滚

```bash
BK=/home/tetsuya/mir2ei/Data/Backup/pre-overlay-hole-fix-20261005-210215
cp "$BK/Inventory.Zl" /home/tetsuya/mir2ei/Data/Inventory.Zl
cp "$BK/Equip.Zl"     /home/tetsuya/mir2ei/Data/Equip.Zl
# 镜像同步回退
D=/data/NAS/Mir2ei-godot/deploy/client/Debug/Client/Data
cp "$D/Inventory.Zl.pre-overlay-hole-fix-20261005" "$D/Inventory.Zl"
cp "$D/Equip.Zl.pre-overlay-hole-fix-20261005"     "$D/Equip.Zl"
```

---

## 7. 已知限制 / 后续可做

1. 补回的像素取自 Overlay（略暗副本，×0.84~0.98、±15 噪声），不是逐像素精确原画。
   若要精确：用已定位到的 22 帧源画替换（需在修复工具里加「源帧优先」分支，并在
   目标机放好 EI/mir3ei 的 `inventory.wil`）。
2. 这批 ZL 的**转换器不在本仓库**（2026-08-06 由外部流程产出）。要根治，应在该流程
   里把 0xC2 像素**同时写进 Image 层**（与 2019 版转换、与原版客户端语义一致），
   否则下次整体重转会再次产生同样的洞。
3. deploy 镜像的 `System.db` 仍是 8-06 的旧库（md5 `4c101304…`，与 `mir2ei` 的
   `827ddc87…` 不同）。本次未动（超出本缺陷范围），但部署前需要单独对齐。
4. `LegacyWilLibrary` 对 `LibraryFile.Inventory` 优先读
   `LegacyEI/Data/inventory.wil`（1440 帧、大量空帧），空帧/越界才回退 ZL——本次修的
   正是这条回退路径，与用户实际命中路径一致。

---

## 8. 复现与自检命令清单

```bash
VENV=/home/tetsuya/development/Mir3-Research/Tools/dbeditor/venv/bin/python

# 1) 扫描某个 ZL 的「Overlay 覆盖空洞」
$VENV Tools/fix_zl_overlay_holes.py scan <lib.Zl>

# 2) 生成修复件（不覆盖原文件）
$VENV Tools/fix_zl_overlay_holes.py fix <lib.Zl> <out.Zl>

# 3) 容器重排自检（不改像素，证明 writer 无损）
$VENV Tools/fix_zl_overlay_holes.py roundtrip <lib.Zl> <out.Zl>

# 4) 独立校验修复件
$VENV Tools/verify_zl_repair.py <original.Zl> <repaired.Zl>

# 5) 生产解码导出单帧（对照肉眼）
DISPLAY=:100 godot-mono --path GodotClient res://Scenes/MapTestScene.tscn -- \
  --dump-zl-file=<lib.Zl> --dump-zl-index=1003 --dump-zl-output=/tmp/frame.png

# 6) 把 System.db 导成 JSON（本仓库外的 SystemDbProbe：Mir3-Research/Tools/SystemDbProbe）
cd /home/tetsuya/development/Mir3-Research/Tools/SystemDbProbe/bin/Debug/net10.0
./SystemDbProbe --json /tmp/curdb /home/tetsuya/development/zircon

# 7) 打印「哪些物品的 Image 命中受影响帧」（对**修复前**的库扫描才有交集）
cd /home/tetsuya/development/zircon
LIB=<修复前的 Inventory.Zl，例如 Backup/pre-overlay-hole-fix-20261005-210215/Inventory.Zl>
$VENV - "$LIB" <<'PY'
import json,sys; sys.path.insert(0,'Tools')
import importlib.util
s=importlib.util.spec_from_file_location('fx','Tools/fix_zl_overlay_holes.py')
m=importlib.util.module_from_spec(s); s.loader.exec_module(m)
aff={i for i,_,_ in m.affected_frames(m.Zl2(sys.argv[1]))}
rows=json.load(open('/tmp/curdb/ItemInfo.json'))['rows']
hit=[(r['Index'],r['ItemName'],r['Image']) for r in rows if r.get('Image') in aff]
print('affected frames', len(aff), 'matched items', len(hit)); print(hit)
PY
# 实测：affected frames 85 / matched items 60（含 1212 Raiment Of High Priest (M)）。
# 对已修复的库扫描该集合为空（0 frames / 0 items）——这本身就是修复生效的自检。
```

依赖 `texture2ddecoder`（BC7 解码）与 Pillow，均在上述 venv 内。

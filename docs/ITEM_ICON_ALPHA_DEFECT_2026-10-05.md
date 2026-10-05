# 物品图标「透明通道被抠多」缺陷 — 定位与修复（2026-10-05）

## 症状（用户报告）

背包/商店里同一件衣服（大祭司法衣（男））出现两种渲染：格子里的图标完整，
**跟随鼠标的图标（或地面上的那个）手臂、两侧下摆整块变透明**，只剩几道
「虚线」残留。用户描述：「衣服、装备有透明通道，但透明通道被渲染多了，
本来就该显示的地方也变透了」。

## 根因（已用像素级证据闭环）

`GodotClient/Data/*.Zl` 里的 **Image 层被转换器抠掉了 WIL 的 “overlay colour”
像素（WIL RLE opcode 0xC2）**；这些像素只保留在同帧的 **Overlay 层**里。

原版客户端画物品图标只用 Image 层（`Client/Controls/DXItemCell.cs` →
`ImageType.Image`；Godot 侧 `DXItemCell.DrawItemIcon` → `MirSkin.GetTexture`），
所以 Image 层为透明的地方就**真的整块透明**。

证据链（`Data/Inventory.Zl` 第 1003 帧，大祭司法衣（男），ItemInfo.Image=1003）：

| 对照 | 不透明像素占比 |
|---|---:|
| 源 WIL `inventory.wil` 第 1001 帧（同一幅画，EI/mir3ei 客户端） | 74.5% |
| 原版 Zircon `Inventory.Zl`（2019 老格式、Dxt1）第 1001 帧 | 74.5% |
| **当前 ZL2/BC7 `Inventory.Zl` 第 1002/1003 帧** | **52.7%** |

- 丢失的像素集与 WIL 的 **0xC2 像素集完全重合**：第 1002 帧空洞 1417px，
  源帧 0xC2 像素 1421px，交集 1417/1421 = 100%（帧号差 1，故差 4px）。
- 空洞像素的 RGB 在 Image 层被编码成全 0；同一位置 Overlay 层 alpha=255，
  颜色 = 源画 × 0.84~0.98（逐帧不同、逐像素 ±15 噪声）——即 **Overlay 层存的是
  那批像素的（略暗）副本**。
- 用两个**互相独立**的 BC7 解码器（`texture2ddecoder`/bc7dec 与 Godot 侧
  `BcnDecoder`/BCnEncoder.NET）解同一份 payload 得到同一结果 ⇒ 不是客户端解码
  问题，是**文件数据本身**如此。
- 逐帧扫描全部 ZL2 图库：只有 `Inventory.Zl` 与 `Equip.Zl` 存在该形态的空洞，
  各 **85 帧**（范围 940–1050 与 3320–3392，即全部「衣服/套装」图标）。

`Data/*.Zl` 是「Zircon 从 WIL 转出的 BC7 重编码」（见
`Mir3-Research/docs/research/ei-ui-layout/GODOT_WINDOW_PARITY_MATRIX_2026-09-29.md:54`）；
2019 老版 ZL 的 Image 层把 0xC2 像素写成不透明（与 WIL 一致），说明这是**新转换
流程引入的回归**。注意 0xC2 在 Zircon 里另有用途：`Client/Scenes/Views/
CharacterDialog.cs` 等以 `ImageType.Overlay` + 装备颜色二次绘制=染色区域，
所以修复只补 Image 层、不动 Overlay 层。

## 影响面

- `mir2ei/Data/Inventory.Zl`（背包/商店/跟随图标）85 帧 147,556 px；
- `mir2ei/Data/Equip.Zl`（人物窗口纸娃娃/装备格）85 帧 201,250 px；
- 当前 `System.db` 中有 **60 件 Armour** 的 `ItemInfo.Image` 落在这些帧上
  （含 1212 `Raiment Of High Priest (M)` Image=1003、1172 `Light Armour (M)`
  Image=942、1254 `Armour of the Sun Keeper (M)` Image=3320 等，完整清单可由
  下方工具打印）。
- 角色在世界里的身体（`M-Hum.Zl` 等）与地面掉落图（`Ground.Zl`）、商店图标
  （`Storeitem.Zl`）**不含**该形态空洞，未被波及。

## 修复

`Tools/fix_zl_overlay_holes.py`：

1. 逐帧解码 Image(BC7) 与 Overlay(BC7)；
2. 对 `Image.alpha==0 && Overlay.alpha>0` 的像素，用 **Overlay 的颜色补回**并置
   alpha=255；
3. 修复帧的 Image 层改用 **PNG（无损）载荷**，Overlay/Shadow 载荷、metadata
   （尺寸、偏移、Overlay 大小、runtime preference）与帧序**原样保留**；
4. 重建 ZL2 容器（header/metadata/data/index 重排，索引条目顺序与 Id 不变）。

`Tools/verify_zl_repair.py` 独立校验：结构（条目数/顺序/Id/压缩方式/Codec、
UncompressedSize 与解压长度一致）、metadata（除被修帧的 ImageCodec/尺寸外全等）、
像素（未修帧逐字节相同；修帧只允许「原空洞」像素变化且修后 alpha=255；修后
不得再存在被 Overlay 覆盖的空洞）。

### 实测结果

```
Inventory.Zl: repaired=85 unchanged=8068 entries=2365  PASS
Equip.Zl    : repaired=85 unchanged=8608 entries=798   PASS
```

- 生产解码路径复核：Godot `MapTestScene --dump-zl-file/…-index/…-output` 读到
  `codec=Png`，帧 1003 / 940 / 3320 导出的 RGBA 里手臂、侧摆均已恢复；
- 实机复核：本机 ServerCore + EI 复古 HUD 客户端登录 TestHero 后
  `[MirSkin] UI 库 Inventory 在 legacy 目录缺失，回退到 …/Data/: Inventory.Zl`
  + `[MapView] 贴图诊断: missingLibraries=0, missingTextures=0`，人物窗口
  纸娃娃 A/B（换回旧 Equip.Zl）渲染**逐像素相同**，无回归；拖拽跟随图标正常。

### 备份与回滚

```
备份：/home/tetsuya/mir2ei/Data/Backup/pre-overlay-hole-fix-20261005-210215/
      （Inventory.Zl 876e7d0b…、Equip.Zl d32c1a44…）
回滚：把该目录两份文件覆盖回 /home/tetsuya/mir2ei/Data/
当前：Inventory.Zl 1a7c9ab9…、Equip.Zl 2585cb7a…
```

### 已知限制 / 后续可做

1. 补回的像素取自同帧 Overlay 层，它是源画的**略暗副本**（逐帧 0.84~0.98，
   逐像素 ±15 噪声）。视觉上与原画一致（见 `Tools` 对照图），但不是逐像素
   精确值。
2. 这批帧的**精确源画**：可达素材里只有 22/85 帧能在 EI/mir3ei 的
   `inventory.wil` 里按「同尺寸 + 已保留像素内容比对」定位到（其中 mir3ei 的
   `Inventory.wil` 解码相对 EI 版本整体错行 2 行，需 roll 校正）；其余（含
   3320–3392 的「经典衣服套装」，图来自 EI/Mud3 侧另一套配色）不在本机可达
   素材中。若要逐像素精确，可用已定位的源帧替换 Overlay 补值。
3. `/data/NAS/Mir2ei-godot/deploy/client/Debug/Client/Data/` 下仍是修复前的
   `Inventory.Zl`/`Equip.Zl`（md5 与备份相同）。若该目录会重新同步到
   `mir2ei/Data`，需要一并更新，否则缺陷会回归。
4. 客户端 `LegacyWilLibrary` 对 `LibraryFile.Inventory` 优先走
   `LegacyEI/Data/inventory.wil`（仅 1440 帧、大量空帧），索引越界或空帧时回退
   ZL；所以本次修的是 ZL 兜底路径，符合用户实际命中路径。

## 工具用法

```bash
VENV=/home/tetsuya/development/Mir3-Research/Tools/dbeditor/venv/bin/python
$VENV Tools/fix_zl_overlay_holes.py scan  <lib.Zl>
$VENV Tools/fix_zl_overlay_holes.py fix   <lib.Zl> <out.Zl>     # 写新文件，不覆盖
$VENV Tools/fix_zl_overlay_holes.py roundtrip <lib.Zl> <out.Zl> # 仅重排容器自检
$VENV Tools/verify_zl_repair.py <original.Zl> <repaired.Zl>
```

依赖 `texture2ddecoder`（BC7 解码）与 Pillow，均在上述 venv 内。

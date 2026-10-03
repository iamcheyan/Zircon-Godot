# 客户端资产归档与瘦身清单 (Archived Assets Manifest)

## 一、概述

为了让游戏客户端达到**经典纯净版**标准，并大幅缩减玩家下载安装包的体积，现已分两期将当前游戏世界（116 只经典/系统怪、326 件经典装备、59 门技能）**完全未使用的非经典与历史冗余资源（合计 5.78 GB）** 安全移出并存入独立归档目录。

- **原始客户端体积**：**11.0 GB**
- **第一期瘦身后**：**7.1 GB**（移出 53 个非经典怪物/特效包，净减 3.27 GB）
- **第二期瘦身后**：**4.6 GB**（移出 364 个历史冗余与拓展文件，净减 2.51 GB）
- **总瘦身体积**：**净减 5.78 GB**（降幅超过 **58%**！）
- **归档资源根目录**：`/home/tetsuya/mir2ei_archive/`
- **7.1GB 原始快照安全备份**：`/home/tetsuya/mir2ei_backup_20261003_7gb/`

---

## 二、一键还原与快速恢复

所有移出的资源文件均分门别类完整保存在归档目录中，未对文件内容做任何破坏。若未来需要制作新活动、开放新资料片或开发测试，可随时通过以下方式**秒级原样还原**：

### 方法 1：全量一键还原
在客户端根目录直接运行自动化还原脚本：
```bash
cd /home/tetsuya/mir2ei && ./restore_all_archived_assets.sh
```
该脚本会自动将 `mir2ei_archive/` 下的所有 `Data/`、`Map/`、`LegacyEI/Data/` 移回原位。

### 方法 2：按需分类/单文件拿回
- **按需拿回某张地图**：
  ```bash
  cp /home/tetsuya/mir2ei_archive/Map/D3005_JJ.map /home/tetsuya/mir2ei/Map/
  ```
- **按需拿回时装系统**：
  ```bash
  cp /home/tetsuya/mir2ei_archive/Data/*Costume* /home/tetsuya/mir2ei/Data/
  ```

---

## 三、第二期归档明细（共 364 个文件，2567.10 MB / 2.51 GB）

### 1. LegacyEI 历史全套冗余资源（172 个文件，1405.80 MB）
- **归档路径**：`/home/tetsuya/mir2ei_archive/LegacyEI/Data/`
- **移出原因**：Zircon 引擎渲染世界全部采用 `Data/*.Zl` 格式，`LegacyEI/Data/` 下的地图地砖（Tilesc.wil 106M、Tiles5c.wil 41M）、建筑物件（object1c.wil 122M、object2c.wil 81M、Furnituresc.wil、Animationsc.wil）、旧版怪物（Mon-*.wil 数百兆）等完全不会被任何渲染代码读取。代码中仅读取该目录下的 5 个 UI 文件（`Interface1c.wil`、`GameInter.wil`、`ProgUse.wil`、`inventory.wil`、`NPCface.wil`，合计仅 22.39 MB）。其余 172 个非 UI 历史旧包全部移出归档。

### 2. 怪物拓展超大单体包（3 个文件，374.28 MB）
- **归档路径**：`/home/tetsuya/mir2ei_archive/Data/`
- **移出原因**：当前纯净经典数据库共 116 只怪物，经算法审计全部映射在 `Mon-1` ~ `Mon-47` 的 26 个库内。以下 3 个超大包没有任何怪物引用：
  - `Mon-60.Zl` (296.07 MB)
  - `Mon-59.Zl` (51.21 MB)
  - `Mon-58.Zl` (27.00 MB)

### 3. 时装系统拓展包（6 个文件，201.07 MB）
- **归档路径**：`/home/tetsuya/mir2ei_archive/Data/`
- **移出原因**：经典传奇3中无商城时装系统，当前 326 件经典装备没有任何 Costume 属性：
  - `M-Costume.Zl` (51.42 MB)
  - `WM-Costume.Zl` (51.73 MB)
  - `M-CostumeEx1.Zl` (28.97 MB)
  - `WM-CostumeEx1.Zl` (26.79 MB)
  - `M-CostumeA.Zl` (20.82 MB)
  - `WM-CostumeA.Zl` (21.29 MB)

### 4. 高阶拓展衣服模型包（16 个文件，336.29 MB）
- **归档路径**：`/home/tetsuya/mir2ei_archive/Data/`
- **移出原因**：经典衣服（布衣、轻盔、战神等）全部位于 `M-Hum.Zl`、`WM-Hum.Zl` 及 `M-HumEx1.Zl` 内，后续私服拓展的高阶衣服模型当前无任何物品引用：
  - `M-HumEx2.Zl` (45.42 MB) / `WM-HumEx2.Zl` (41.99 MB)
  - `M-HumEx3.Zl` (39.84 MB) / `WM-HumEx3.Zl` (37.27 MB)
  - `M-HumEx4.Zl` (12.45 MB) / `WM-HumEx4.Zl` (11.32 MB)
  - `M-HumEx10.Zl` (25.54 MB) / `WM-HumEx10.Zl` (24.86 MB)
  - `M-HumEx11.Zl` (4.21 MB) / `WM-HumEx11.Zl` (4.05 MB)
  - `M-HumEx12.Zl` (16.06 MB) / `WM-HumEx12.Zl` (15.09 MB)
  - `M-HumEx13.Zl` (0.03 MB) / `WM-HumEx13.Zl` (0.03 MB)
  - `M-SHumEx1.Zl` (29.13 MB) / `WM-SHumEx1.Zl` (29.07 MB)

### 5. 数据库未登记死重地图（167 个文件，249.66 MB）
- **归档路径**：`/home/tetsuya/mir2ei_archive/Map/`
- **移出原因**：磁盘 `Map/` 目录原存有 794 张地图，而 `System.db` 登记的仅 627 张。这 167 张地图在数据库中没有任何 `MapInfo` 记录，客户端与服务端完全无法加载或进入，属于无用死重，已全部移入归档。

---

## 四、第一期归档回顾（共 53 个文件，3270 MB / 3.27 GB）

- **非经典怪物动作库（31 个文件，1354.75 MB）**：`Mon-18.Zl`、`Mon-21.Zl`、`Mon-27~33.Zl`、`Mon-35~46.Zl`、`Mon-48~57.Zl` 等。
- **非经典怪物施法特效包（18 个文件，1469.71 MB）**：`MonMagicEx.Zl`、`MonMagicEx3/5/9/10/12~19/21/22/24/25/27.Zl`。
- **非经典翅膀与全身光效包（4 个文件，425.00 MB）**：`EquipEffect-Full.Zl`、`EquipEffect-FullEx3.Zl` 等。

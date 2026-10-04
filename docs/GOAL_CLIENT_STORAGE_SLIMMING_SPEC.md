# 目标四：客户端存储深度精简与瘦身实施规范（Goal SOP）

> **文档性质**：独立智能体作业指引（SOP） & 验收标准  
> **适用模块**：客户端资源瘦身（`/home/tetsuya/mir2ei/` 及 `Debug/Client/Data/`）  
> **状态**：待执行

---

## 一、任务背景与核心目标

### 1. 现状痛点
- 当前客户端资源目录体积高达 **7.3 GB+**；
- 经典传奇 3（EI）当年客户端仅几百兆至 1~2GB，过大的包体不仅占用大量硬盘空间，且导致客户端首次加载内存偏高、资源检索缓慢；
- 体积庞大的主要根因是：上游 Zircon 项目累积了大量现代高清地图素材、私服自制怪物图库（如编号数百的 `Mon-*.Zl`）、私服坐骑、以及未启用的背景视频和未压缩音频。

### 2. 核心目标
1. **安全瘦身**：在**完整保留备份**的前提下，精准识别并剥离“从未在经典 1.45 中使用”的冗余现代图库；
2. **保持 100% 视觉与玩法完整**：确保 627 张地图、154 种经典怪物、399 件纯净物品、经典三职业角色动作不受任何影响；
3. **目标体积分级控制**：将客户端有效体积从 7GB+ 大幅精简至 2GB~3GB 区间。

---

## 二、权威参考数据源

- **客户端主路径**：`/home/tetsuya/mir2ei/`（软链 `Debug/Client/` 对应于此）
- **图库索引清单**：`LibraryCore/Libraries.cs`（定义了所有 LibraryFile 路径）
- **现役资源白名单**：
  - 地图引用的 Tiles/SmTiles/Objects 图库
  - 154 种经典怪物引用的 `Mon-*.Zl`（`MonsterLookup.cs` 映射表）
  - 角色装备与外形库（Hum, Weapon, Hair, Shield, Magic 等）
  - 经典 UI 库（Interface1c, GameInter, ProgUse 等）

---

## 三、实施铁律与红线

1. **🔴 铁律一：必须先做离线全量备份**：瘦身前必须将 `/home/tetsuya/mir2ei/` 完整复制或快照备份到独立安全目录（如 `/home/tetsuya/mir2ei_backup_full/`），绝不允许直接物理删除；
2. **🔴 铁律二：采用“隔离归档（Quarantine）”而非直接 rm**：初次瘦身应将冗余文件 `mv` 到隔离目录，待完整游戏回归测试通过后再做最终处理；
3. **🔴 铁律三：零破图零报错**：客户端进入所有经典主城（银杏、比奇、道馆、潘夜、沙巴克、失乐园）及主流地牢（僵尸洞、沃玛寺庙、祖玛寺庙、猪洞）不得出现红紫占位图或 Missing Library 报错。

---

## 四、执行步骤（SOP）

### Step 1：环境备份
```bash
# 执行全量完整备份
mkdir -p /home/tetsuya/mir2ei_backup_full
rsync -av /home/tetsuya/mir2ei/ /home/tetsuya/mir2ei_backup_full/
```

### Step 2：资源引用深度扫描与白名单生成
1. 编写分析工具（建议放置于 `Tools/AssetSlimmer/`）；
2. 扫描 `System.db`、`MonsterLookup.cs`、全部 `.map` 文件，统计所有被实际引用的 `.Zl`、`.wil` 文件；
3. 输出白名单清单（Active Assets）与黑名单清单（Unused Assets）。

### Step 3：隔离归档冗余素材
1. 创建隔离目录 `/home/tetsuya/mir2ei_quarantine/`；
2. 将黑名单中的多余 `Mon-*.Zl`（超出经典 154 怪物范围的私服模型）、无用现代高清地砖、未引用的 MP4/WMV 视频移入隔离目录；
3. 统计瘦身前后节省的磁盘空间。

### Step 4：全面游戏性实机回归测试
1. 启动 `ServerCore` 并启动 GodotClient；
2. 登录 `TestHero`；
3. 巡回测试各大核心区域：
   - `@move 0`（银杏山谷）
   - `@move 01`（比奇城）
   - `@move 02`（道馆）
   - `@move D202`（废矿）
   - `@move D1001`（沃玛寺庙）
   - `@move D1011`（祖玛寺庙）
   - `@move D1021`（潘夜石窟）
4. 刷出经典怪物并施放技能，确认画面完整、音效正常、无报错日志。

### Step 5：沉淀瘦身报告
1. 编写 `docs/CLIENT_STORAGE_SLIMMING_REPORT.md`，记录瘦身前体积、瘦身后体积、清理项分类统计；
2. 提交代码与文档。

---

## 五、验收标准（Checklist）

- [ ] 1. 完整备份目录存在且校验和完整；
- [ ] 2. 客户端资源体积显著下降（减少 2GB~4GB+）；
- [ ] 3. 登录并巡回各大主城及主流地牢，无贴图缺失、无红紫格子、无客户端控制台 Error；
- [ ] 4. 经典怪物、装备穿戴、技能施放特效完好无损；
- [ ] 5. 附带实机运行截图证据与瘦身报告，Git 规范提交推送至 `origin master`。

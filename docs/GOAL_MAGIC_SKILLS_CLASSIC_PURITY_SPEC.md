# 目标一：技能与魔法体系纯净清洗与经典对齐实施规范（Goal SOP）

> **文档性质**：独立智能体作业指引（SOP） & 验收标准  
> **适用模块**：技能系统（`MagicInfo` / `Magic`）  
> **状态**：待执行

---

## 一、任务背景与核心目标

### 1. 现状痛点
- 当前 Zircon 数据库 `System.db` 中包含 **174 个技能**；
- 17173 传奇 3 资料站经典纯净标准仅包含 **61 个技能**（战士 13 个、法师 26 个、道士 22 个）；
- 现状夹带了大量刺客技能（如暗影步、致残毒药等）、弓箭手技能以及私服自制的超纲技能。

### 2. 核心目标
1. **纯净清洗**：将 113 个非经典（刺客、弓手、私服变态技能）从现役技能表中清理或优雅停用；
2. **三职业 61 个技能精准对齐**：以 17173 资料站和原版 Mud3 为基准，锁死 61 个纯正经典技能；
3. **关键字段校准**：校对技能学习等级（如战士 7 级基本剑术、19 级攻杀、28 级刺杀、35 级烈火）、消耗 MP、修炼点数及元素属性（火/冰/雷/风/神圣/暗黑/幻影）；
4. **网站同步**：同步更新 `mir3-website/data/skills.json` 与资料站页面。

---

## 二、权威参考数据源

- **17173 技能基准清单**：`/home/tetsuya/development/mir3-website/data/skills.json`（61 条经典技能）
- **Mud3 原版技能定义**：`/home/tetsuya/development/Mir3-Research/reference/mir3-source/Mud3-Config/Envir/Magic.txt`
- **当前 Zircon 技能数据**：`System.db` 的 `MagicInfo` 表

---

## 三、实施铁律与红线

1. **🔴 铁律一：写库四处镜像必须绝对一致**：修改前必须停服，修改后覆盖 4 处 `System.db`，MD5 必须完全一致；
2. **🔴 铁律二：严禁破坏玩家现有技能关联**：处理多余技能时，需检查 `Users.db` 的 `UserMagic`，避免因直接硬删引发玩家账号反序列化失败；
3. **🔴 铁律三：元素属性必须准确**：法师四系（火、冰、雷、风）与道士三系（神圣、暗黑、灵魂）的 `Element` 枚举必须准确无误。

---

## 四、执行步骤（SOP）

### Step 1：环境准备与停服备份
1. 检查并终止后台 `ServerCore` 进程，确认 7000 端口释放；
2. 备份 4 处 `System.db` 与 `Users.db` 到带时间戳的备份目录。

### Step 2：比对与清洗工具开发
1. 开发工具（建议放置于 `Tools/MagicPurityFixer/`）；
2. 读取 17173 的 61 条经典技能作为白名单；
3. 扫描 `System.db` 中的 `MagicInfo`：
   - 属于白名单的 61 个技能：核验其 `Class`（Warrior/Wizard/Taoist）、`NeedLevel`（等级）、`MP`、`MagicType`、`Element` 并校准；
   - 超出白名单的技能：从激活列表中移除，并在 `UserMagic` 中清理关联记录。

### Step 3：写库与镜像同步
1. 工具保存修改至 `System.db`；
2. 同步覆盖 4 处路径，并用 `md5sum` 验证严格一致：
   - `Debug/ServerCore/Database/System.db`
   - `/home/tetsuya/mir2ei/Data/System.db`
   - `/home/tetsuya/mir2ei/Database/System.db`
   - `./System.db`

### Step 4：网站资料站同步
1. 运行 `mir3-website/tools/extract.py` 或更新 `data/skills.json`，确保网站展示的 61 个技能与游戏库完全一致；
2. 验证前端技能列表渲染正常。

### Step 5：游戏内实机运行验收
1. 启动 `ServerCore`（7000 端口就绪）；
2. 登录 `TestHero`（GM 账号），通过 `@giveSkills` 给全套技能；
3. 验证快捷键 `F11` 或打开技能窗口（SpellDialog）：技能列表规整，无刺客/弓手技能，图标与描述正常；
4. 截取游戏内技能界面截图，保存至 `docs/screenshots/magic_purified/`。

---

## 五、验收标准（Checklist）

- [ ] 1. 4 处 `System.db` MD5 严格对齐；
- [ ] 2. `MagicInfo` 中纯净保留且仅保留 61 个经典三职业技能；
- [ ] 3. 游戏内按 `F11` 查看技能面板，无任何刺客/私服多余技能；
- [ ] 4. 施放核心技能（烈火剑法、雷电术、火球术、施毒术等）动作、特效、扣蓝均正常；
- [ ] 5. 附带实机运行截图证据，Git 提交规范并推送到 `origin master`。

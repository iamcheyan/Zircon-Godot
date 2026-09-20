# AGENTS.md — Zircon 智能体协作指引

本文件供所有智能体（AI agent）在 Zircon 仓库工作时参考。

## 查找文档与资料

**重要：需要找文档、研究资料、历史结论、交接说明时，优先去以下目录：**

1. **`/home/tetsuya/development/Mir3-Research/docs/`** — 原版客户端逆向研究、资源解码、
   地图审计、UI 证据、渲染对比、问题排查等**所有研究文档**都集中在这里。
   包括但不限于：
   - `MAP_TILE_DESERTED_MINE_BUG.md` — 矿区/僵尸洞地面贴图问题完整分析（含终局裁决）
   - `GODOT_RENDERING_AUDIT.md`、`RENDER_PARITY_AUDIT_*.md` — 渲染对比审计
   - `AGENT_HANDOFF_PARITY_GOAL.md`、`OPERATION_PARITY_*.md` — 交接文档
   - `docs/database/`、`docs/notes/`、`docs/handoffs/`、`docs/research/` — 子目录分类
   - `Tools/` — 解码/审计工具（`Tools/common/wilsdk.py` 为 WIL/WIX 解码库，
     `Tools/maps/` 为地图工具）

2. **本仓库 `docs/`** — Zircon 自身项目文档（修复记录、迁移文档、部署说明）：
   - `MAP_FORMAT_COMPARISON.md` — .map 格式对比（Zircon .Zl vs NAS .wil）
   - `MAGIC_FULL_AUDIT.md`、`MAGIC_GROUND_EFFECT_FIXES.md` — 魔法特效审计
   - `REMOTE_SERVER_AND_CLIENT_SETUP.md` — 服务器部署说明
   - `Docs/`（大写）— 其它临时分析文档

3. **Mir3-Research 环境变量约定**（工具脚本依赖）：
   ```bash
   export MIR3_EI_ROOT=/home/tetsuya/mir3ei
   export MIR3_MUD3_ROOT=/home/tetsuya/NAS/TMP/Mud3
   export MIR3_ZIRCON_ROOT=/home/tetsuya/development/Zircon
   ```

## 客户端与一键启动

所有客户端运行资源统一存放于 `/home/tetsuya/mir3ei`（NVMe 本地高速存储，无网络延迟）：
- 启动游戏：`cd /home/tetsuya/mir3ei && ./login_game.sh`（或 `./start.sh`）
- 重启全套：`cd /home/tetsuya/mir3ei && ./login_game.sh all`
- 资源软链：`Zircon/Debug/Client` -> `/home/tetsuya/mir3ei`

## 测试账号

```bash
godot-mono --path /home/tetsuya/development/Zircon/GodotClient -- --server 127.0.0.1 --port 7000 --user test@test.com --pass test123 --char TestHero --window

# 连接 82 远程服务端时改为：
# godot-mono --path /home/tetsuya/development/Zircon/GodotClient -- --server 192.168.3.82 --port 7000 --user test@test.com --pass test123 --char TestHero --window
```

- 账号：`test@test.com`，密码：`test123`，角色：`TestHero`
- 用途：登录本地/远程测试服务器进行游戏内验证
- 构建客户端：`cd /home/tetsuya/development/Zircon && dotnet build GodotClient/ZirconClient.csproj`
  （注意必须在仓库根目录执行，`~` 下找不到项目文件）
- GM 权限：**该账号 `Admin = True`（永久 GM）**，可直接使用所有 `@` 管理命令；
  另外服务器支持 `MasterPassword` 机制（非邮箱格式登录名 + 主密码 → TempAdmin），
  详见 `ServerLibrary/Envir/SEnvir.cs` Login 逻辑

## GM 命令（管理员权限）

**用法**：游戏内聊天框直接输入，`@` 开头 + 空格分隔参数。
命令定义在 `ServerLibrary/Envir/Commands/Command/Admin/`，仅 `Account.Admin` 或
`TempAdmin` 可用（`AdminCommandHandler.IsAllowedByPlayer`）。

### 传送类

| 命令 | 用途 | 示例 |
|------|------|------|
| `@move 地图  x  y` | 传送到地图指定坐标（省略坐标=随机点） | `@move D201`、`@move D201 54 287` |
| `@goto 角色名` | 传送到某角色身边 | `@goto TestHero` |
| `@recall 角色名` | 把某角色拉到身边 | `@recall TestHero` |

### 角色/状态类

| 命令 | 用途 | 示例 |
|------|------|------|
| `@level 数字` | 设置等级 | `@level 50` |
| `@addstat 属性 值` | 加属性点 | `@addstat` |
| `@giveSkills` | 给技能 | `@giveSkills` |
| `@levelSkill` | 技能升级 | `@levelSkill` |
| `@levelWeapon` | 武器升级 | `@levelWeapon` |
| `@toggleGM` | 切换 GameMaster 模式（怪物不主动攻击） | `@toggleGM` |
| `@toggleSuperman` | 超人模式（无敌） | `@toggleSuperman` |
| `@toggleObserver` | 观察者模式 | `@toggleObserver` |
| `@resetDiscipline` | 重置修炼 | `@resetDiscipline` |
| `@removePKPoints` | 清除 PK 值 | `@removePKPoints` |

### 物品/生成类

| 命令 | 用途 | 示例 |
|------|------|------|
| `@make 物品名` | 刷物品到背包 | `@make 金创药` |
| `@giveHorse` | 给马 | `@giveHorse` |
| `@spawn 怪物 数量` | 刷怪 | `@spawn GhostSorcerer 5` |
| `@setCompanionLevel/Stat` | 宠物等级/属性 | `@setCompanionLevel` |
| `@setHermitStat` | 隐士属性 | `@setHermitStat` |

### 服务器管理类

| 命令 | 用途 | 示例 |
|------|------|------|
| `@kick 角色名` | 踢人下线 | `@kick TestHero` |
| `@ban 账号 天数 原因` | 封禁 | `@ban` |
| `@chatban` | 禁言 | `@chatban` |
| `@clearIPBlocks` | 清空 IP 封禁 | `@clearIPBlocks` |
| `@reboot` | 重启服务器 | `@reboot` |
| `@createGuild` | 创建公会 | `@createGuild` |
| `@giveGameGold` | 发游戏金币 | `@giveGameGold` |
| `@takeGameGold` | 扣游戏金币 | `@takeGameGold` |
| `@promoteFame` | 提升声望 | `@promoteFame` |

### 常用矿区传送（测试用）

```
@move D201    ← 废矿1层（僵尸洞，黑/熔岩地砖）
@move D202    ← 废矿2层
@move D203    ← 废矿3层
@move D101    ← 比奇矿洞1层
```

## 工作约定

- 推送远程是 `fork`（iamcheyan/Zircon），不是 `origin`（Suprcode/Zircon）
- 不要触碰原版 `Client/` 源码（除非通过 `NoColourKey` 机制等明确手段）
- 用 `dotnet build GodotClient/ZirconClient.csproj` 验证 Godot 端修复
- 用 `hub` 启动/停止服务（`zircon-dev`、`zircon-server`）
- commit 信息用中文，遵循仓库现有风格
- 切勿泄露个人信息或 API keys

## 验证深度约定（防"编译通过但逻辑全错"）

**教训记录**：
- 2026-08-11 `SelectScene.cs`：注释与 `if` 写在同一行，导致自动登录逻辑被整体注释掉——
  客户端停在选人界面，服务器看不到 StartGame。`dotnet build` 通过（注释后语法仍合法），
  只有跑完整登录流程才暴露。
- 2026-08-11 沙巴克移植：中层/前景索引 `-1/+1` 双重偏移，离线渲染验证用了同样错误的
  约定所以"验证通过"，实际游戏墙体缺口。工具逻辑错误必须用**独立于工具的正确约定**验证。

**强制规则**（任何涉及登录/进游戏/地图渲染的改动）：

1. **行为验证 ≥ 编译验证**：`dotnet build` 通过只证明语法正确，不证明逻辑正确。
   涉及以下场景必须跑真实流程验证：
   - 登录/进游戏逻辑 → 用测试账号完整登录到进入游戏（不能只看 build + 启动日志）
   - 地图/贴图渲染 → 游戏内实际查看（或独立离线渲染对照）
   - 数据转换/索引映射 → 用独立于转换工具的方法交叉验证
2. **注释不能吞代码**：写完含注释的代码，检查注释行是否把后续语句整行注释掉。
   可用 `grep -n "//.*if\|//.*{"` 快速扫描。
3. **数据约定先确认再写工具**：涉及 `.map` 帧索引、`+1/-1` 偏移、字节布局等格式约定，
   先读原版客户端/服务端源码确认约定（如 `MapControl.cs` 的 `+1` 存储），再写转换工具。
4. **验证工具不得与生产工具共用同一错误**：校验脚本若复用了生产工具的解析逻辑，
   错误会被"自洽"掩盖。校验必须用独立实现或对照真实游戏表现。

## 今日新工具（2026-08-14）

| 工具 | 端口 | 用途 |
|---|---|---|
| uieditor | :8820 | UI 所见即所得编辑器：`--ui-export` 导出控件树 → 浏览器拖拽改 → 保存 ui_overlay.json → 游戏内 F12 热重载生效 |
| webclient | :8822 | 静态世界测试台（不连服）：627 地图+连接切图+四职业 255 级 GM+全技能装备+NPC/怪物摆放+GM 面板。详见 Mir3-Research/Tools/webclient/README.md |
| zdocs 文档库 | — | docs/codebase/ 23 篇原版代码深度文档（战斗公式/怪物AI/协议/玩法/基础设施）——移植任何功能前先查这里 |

## 模型交接注意

- **无头验证配方**：Xvfb :100 + openbox + godot-mono（/tmp/godot-mono）+ scrot；用户参数在 `--` 之后；4K 缩放测试用 ZIRCON_UI_SCALE=2
- **构建**：`dotnet build GodotClient/ZirconClient.csproj`（仓库根目录执行；增量有缓存坑用 --no-incremental）
- **服务端口表**：7000 ServerCore / 8810 dbeditor / 8820 uieditor / 8822 webclient / 8800 dbviewer / 8899 mapviewer / 8765 wilviewer / 8830 yomu / 8831 fudoki / 80 svc-dashboard
- **写库纪律**：服务端运行中绝不写 System.db；双库（服务端+客户端）同步写；写前备份；round-trip 读回验证。工具模板见 Mir3-Research/AGENTS.md
- **goal 体系**：omp goal 跑在 tmux（一 goal 一会话）；goal_watchdog.sh GOALS 数组注册（id|jsonl|tmux会话|workdir|label）；终态自动回收记 ~/.omp/logs/goal-completed.log

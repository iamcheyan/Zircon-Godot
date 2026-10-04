# 测试角色异常排查、多路径数据库统一与未来数据架构建议

**记录时间**：2026-10-04  
**受影响角色**：`TestHero`（账号：`test@test.com`）  
**责任范围**：服务端数据库（`Users.db`）、启动脚本（`login_game.sh`）、运行时路径解析  

---

## 1. 现象与排查结论

### 1.1 用户反馈现象
用户登录测试账号 `test@test.com` 发现角色 `TestHero` 突变为 **70 级**，身上的全套装备与之前测试状态完全不一致（原本应为 **255 级** 且拥有全套高阶战士装备）。

### 1.2 根本原因定位
经只读反编译与全盘扫库分析，查明**并非数据丢失或被非法修改**，而是**系统中存在两套完全独立的 `Users.db` 数据库**，且启动脚本加载了非预期的旧目录：

| 属性 | 数据库 A（旧残留外部库） | 数据库 B（主开发/备份库） |
| :--- | :--- | :--- |
| **物理路径** | `/home/tetsuya/development/Debug/ServerCore/Database/Users.db` | `/home/tetsuya/development/zircon/Debug/ServerCore/Database/Users.db` |
| **角色名** | `TestHero` | `TestHero` |
| **角色职业** | **道士（Taoist）** | **战士（Warrior）** |
| **角色等级** | **70 级** | **255 级** |
| **金币数量** | 0 | 99,999,390 |
| **技能数量** | 174 个 | 153 个（战士全技能） |
| **物品/装备** | 10 件杂物（无战神套） | 52 件（手持裁决之杖、穿战神头盔、霸者之戒、平衡之袍、疾风靴等全套神装） |

---

## 2. 多路径混乱根源剖析

### 2.1 启动脚本路径优先级陷阱
查看 [`login_game.sh`](file:///home/tetsuya/development/zircon/login_game.sh#L99-L100)：
```bash
SERVER_DIR="$ROOT/../Debug/ServerCore"
[ -d "$SERVER_DIR" ] || SERVER_DIR="$ROOT/Debug/ServerCore"
```
- `$ROOT` 指向仓库根目录 `/home/tetsuya/development/zircon`。
- `$ROOT/..` 则是 `/home/tetsuya/development`。
- 历史遗留原因，`/home/tetsuya/development/Debug/ServerCore` 目录真实存在且未被删除。
- 脚本第 99 行优先判定 `$ROOT/../Debug/ServerCore`，导致服务端启动时的工作目录（CWD）被固定为仓库外部的 `Debug` 目录，实际加载了外部那套孤立的、存放 70 级道士的 `Users.db`。

### 2.2 数据库副本多头分散
当前系统中至少有 3 个位置依赖或读取 `Users.db`：
1. `/home/tetsuya/development/Debug/ServerCore/Database/Users.db`（外部运行态）
2. `/home/tetsuya/development/zircon/Debug/ServerCore/Database/Users.db`（仓库编译输出态）
3. `/home/tetsuya/mir2ei/Database/Users.db`（NVMe 资源存储区）

缺乏统一软链与单点真相源（SSOT），任何一个进程或工具单点修改其中一个副本，就会引发各处状态漂移。

---

## 3. 本次执行统一与修复动作

为彻底解决混乱，本次操作严格遵循写库纪律（**停服 -> 备份 -> 写入 -> 读回校验**）：

1. **优雅停服**：向正在运行的服务端发送 `SIGTERM`，确认进程完全释放端口 7000。
2. **备份旧数据**：
   - 将 70 级道士库备份至：`/home/tetsuya/development/Debug/ServerCore/Database/Users.db.bak-70taoist-20261004`。
3. **全域覆盖统一**：
   - 提取包含 **255 级、全技能、裁决战神套神装** 的纯净测试角色数据；
   - 统一写入上述 3 处核心位置：
     ```bash
     cp /home/tetsuya/development/zircon/Debug/ServerCore/Database/Users.db /home/tetsuya/development/Debug/ServerCore/Database/Users.db
     cp /home/tetsuya/development/zircon/Debug/ServerCore/Database/Users.db /home/tetsuya/mir2ei/Database/Users.db
     ```
4. **MD5 强一致性校验**：
   - 三处散列值完全一致：`a98a60f87bcbcc486cac29b6251645a1`（大小 388,704 字节）。
5. **只读反序列化验证（Round-trip）**：
   - 验证通过：全库账号收敛至纯净状态，唯一核心角色为战士 `TestHero`，等级 255 级，金币 99,999,390，全套装备 9 件穿戴在身，背包 43 件。
6. **重启验证**：
   - 服务端重新启动成功，正常监听 7000 端口，进入游戏即可看到 255 级战士。

---

## 4. 后续数据结构与目录重组建议

针对后续可能对数据目录进行重构/搬移的需求，建议实施以下三项架构原则：

### 建议 1：建立绝对的「单一真相源（Single Source of Truth, SSOT）」
目前 `System.db`（系统静态库）与 `Users.db`（动态用户库）在硬盘上有 4~5 个拷贝，手工复制极易出现疏漏或陈旧。  
**推荐方案**：
- 将物理数据库文件唯一固定存放在高速盘规范路径（如 `/home/tetsuya/mir2ei/Database/`）。
- 仓库内外的 `Debug/ServerCore/Database/*.db` 一律改为指向该规范路径的**软链接（Symbolic Links）**。
- 这样无论从哪个目录拉起服务端、无论哪个工具读写数据库，操作的都是同一个物理文件，彻底根除“两套库”现象。

### 建议 2：收敛启动脚本与编译输出路径
- 修改 [`login_game.sh`](file:///home/tetsuya/development/zircon/login_game.sh) 及相关自动化脚本，移除 `$ROOT/../Debug/ServerCore` 这一外部相对路径判定。
- 将编译输出统一锁定在 `$ROOT/Debug/ServerCore`（或直接使用系统环境变量 `MIR3_ZIRCON_ROOT`），避免任何幽灵外部目录分叉。

### 建议 3：测试账号防污染机制（Seed 模式）
- 针对技能测试、商城购买、怪物掉落等自动化破坏性测试（如清空背包/清空技能测试），避免直接修改生产级测试账号。
- 提供独立脚本（例如 `tools/reset_test_hero.sh`）或在代码中引入预设的 Seed 快照，测试完成后可一键无损复原为标准的 255 级满装状态。

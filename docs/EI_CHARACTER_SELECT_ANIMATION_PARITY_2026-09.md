# EI 角色选角动画与渲染 parity 复核文档
## 版本信息
- 时间: 2026-09-28
- 分支: goal/ei-character-select-animation-mini-20260928
- 模型: Seed-2.0-Mini

---

## 一、原版 EI EXE 身份与版本匹配
- 原版 EI EXE 路径：需外部提供副本（本机未找到对应路径）
- 当前复查待补充：需校验此 EXE 与研究记录目标构建的 SHA-256 匹配性（当前未执行哈希校验，证据级别待补）
- 资源根：EI复古UI原生Data目录（对应LegacyEI/目录）
- Interface1c.wil 状态: 目标 EI 客户端原生资源（非缓存/Modern Zl 帧，证据级别 pending）

---

## 二、选角 phase 动画调用链
### Phase 0（角色列表）
- 角色绘制路径: Round297 `0x4570D0` 为选角屏角色绘制函数，当前 Godot 实现为 `UpdateCaveSlots()`，匹配原版角色绘制路径
- 动画更新触发: Round298 `0x457790` 为动画列表更新触发，当前 Godot 中在 `_Process` 里处理动画刷新，逻辑对应
- 槽选择细节: Round297 `0x458150` 为选中状态渲染逻辑，当前 Godot 中通过 `SelectSkinCharacter(idx)` 触发刷新，对应原版槽选择逻辑

### Phase 2（动画角色列表）
- 动画处理: Round297 `0x457AB0` 为 phase 2 动画列表处理，对应 Godot 中 `UpdateCaveSlots()` 循环动画，逻辑匹配原版 phase 2 动画列表处理

---

## 三、创建/列表/详情媒体边界
- 创建阶段 `CreateChr.dat`: 建角过场动画，与选角角色列表动画独立（证据级别 closed，依据文档注释）
- 列表动画: 当前实现为 `Interface1c.wil` 中职业对应帧块循环，对应 phase 2，非建角阶段
- 选中角色详情: 待确认是否在选角屏实现，当前 Godot 实现未包含详情面板（证据级别 blocked）

---

## 四、原版槽角色动画/坐标证据（pending）
| 属性 | 原版证据来源 | 结论状态 | 当前实现对应 |
| --- | --- | --- | --- |
| 角色帧基址 | `login-charselect-flow-evidence.json` 6 个帧段 + Round298 `0x458EC0` 槽查找映射 | 候选（帧段基址匹配原版职业/性别映射） | `CharacterBaseFrame()` 返回 440/740/1040/1340/1640/1940 |
| 角色帧数量 | 审计文档 15-18 帧/职业 | 候选（帧数量匹配原版各职业段长度） | `CharacterFrameCount()` 返回 15-18 |
| 角色动画总时长 | Round298 动画循环反汇编 + DXAnimatedControl 实现 | 已验证（2400ms 为整轮总时长，符合原版循环逻辑） | `AnimationDelay=2400ms`（整轮总时长，依据 DXAnimatedControl 文档） |
| 角色帧高度 | Interface1c.wil 帧头 240-268px | 候选（使用原生尺寸，帧高匹配范围） | `MirSkin.GetSize()` 返回值 |
| 槽中心 X | Round297 `0x458BB0` 表单布局 + 背景构图 | 候选（推导值 350/490，待原版截图验证） | `Slot0CenterX=350`/`Slot1CenterX=490`（推导值） |
| 脚底 Y | Round297 背景构图 | 候选（推导值 440，待原版截图验证） | `SlotFeetY=440`（推导值） |
| 阴影帧偏移 | Round297 `[slot+0x3C]` 阴影帧偏移 | 匹配（baseFrame+20 对应原版 slot+0x3C） | `baseFrame+20` |

---

## 五、当前 Godot 代码实现
- 资源路径: `LibraryFile.Interface1c`（对应 LegacyEI/Data/Interface1c.wil）
- 帧基址: `CharacterBaseFrame()` 按职业/性别映射：0=440（战士男）、1=740（战士女）等
- 帧数量: `CharacterFrameCount()` 对应各职业帧数 15-18
- 动画周期: `AnimationDelay=2400ms`（`DXAnimatedControl` 实现为整轮总时长，每帧时长=2400/帧数）
- 角色尺寸: 按 `MirSkin.GetSize()` 原生尺寸，按 `Location=(centerX - size.X/2, SlotFeetY - size.Y)` 绘制
- 选中行为: 点击 `_slotHit0`/`_slotHit1` 触发 `SelectSkinCharacter(idx)`，切换角色列表选中项

---

## 六、差异与验证假设（pending）
| 差异假设 | 预期结果 | 最小验证方法 | 状态 |
| --- | --- | --- | --- |
| 帧基址/数量不匹配原版 | 动画循环与原版帧序列不同 | 对比 EI 帧段与当前实现帧切换 | pending |
| 坐标（中心/脚底）与原版不符 | 角色位置偏离原版构图 | 对比 Godot 与原版角色位置截图 | pending |
| 动画时长/帧速度与原版不符 | 角色动画速度异常 | 对比原版与 Godot 动画帧率 | pending |

---

## 七、交互与状态验证
- 角色槽点击选中: 当前实现可点击槽位切换，触发 `SelectSkinCharacter(idx)`，与原版槽选择逻辑（`0x458150`）对应，逻辑一致，已验证点击切换槽位状态
- 默认选中状态: 待确认原版初始选中槽位，当前 Godot 实现默认选中第一个槽位，需原版证据验证是否一致

---

## 八、文档修正记录
- 已完成 `screenshots/ei-legacy-character-selection-2026-09-28/README.md` 修正：将“人物选择：通过”改为“鼠标槽位交互观察到切换；视觉/动画 parity 未通过/未验证”，保留截图并说明非EI原版参照，已commit提交。

---

## 九、验收标准1-9当前状态（依据Goal要求逐项验证）
1. **逆向依据可追溯**：NOT TESTED。需完成原始bytes/callsite的完全闭合验证，当前仅匹配部分调用关系，尚未完全确认闭合。
2. **资源与版本正确**：BLOCKED。资源路径为EI复古UI原生Interface1c.wil，未找到本地原版EI EXE副本，无法校验SHA-256匹配性。
3. **视觉/动画对照**：BLOCKED。缺少同版原版EI运行的截图/录屏，无法对比角色bbox、中心、脚底位置及多时点帧变化，当前无有效原版视觉参照。
4. **交互**：BLOCKED。仅验证Godot中槽位点击可切换选中状态，不误进游戏，但未验证与原版选中状态逻辑是否一致，也未确认原版初始默认选中状态。
5. **实现质量**：PASS（build结果）/NOT TESTED（其余）。执行`dotnet build GodotClient/ZirconClient.csproj --no-restore --no-incremental`结果成功（0 errors，3个非关键CS警告）；未执行`git diff --check`及frame selection/scale/position回归验证。
6. **回归**：NOT TESTED。未验证Legacy EI入口是否正确进入Legacy UI、`--zircon-ui`参数是否有效切换，也未检查是否有未请求的角色创建/删除/协议流程变化，回归验证未完成。
7. **证据文档修正**：BLOCKED。需完成所有文档的错误状态修正，当前仅部分修正，且因缺少原版证据，无法完整完成所有文档验证。
8. **Git交付**：PASS。当前远端SHA与本地HEAD一致（3cfe8c59），已提交修正的文档并push到对应分支，未提交用户未跟踪图片/数据库文件。
9. **状态与保留**：BLOCKED。因缺少原版EXE副本、原版EI视觉对照证据及交互初始状态证据，无法完成剩余验证项，保留tmux、DIM会话及所有证据等待用户支持。

---

## 截图说明
- 既有截图`screenshots/ei-legacy-character-selection-2026-09-28/01-slot0-initial.png`/`02-slot1-selected.png`：仅证明Godot中槽位标签颜色切换，非本轮新截图，也非原版EI视觉参照证据。
- 未采集到真实Godot中间截图/多时点动画证据，所有视觉相关验证项均为NOT TESTED或BLOCKED状态。
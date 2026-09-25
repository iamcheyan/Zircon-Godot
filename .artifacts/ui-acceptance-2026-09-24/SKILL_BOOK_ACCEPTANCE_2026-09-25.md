# 技能书运行验收索引（2026-09-25）

## 运行环境

- 运行资源：`/home/tetsuya/mir2ei`，`MIR3_EI_ROOT=/home/tetsuya/mir2ei`。
- 客户端命令：`bash /home/tetsuya/mir2ei/login_game.sh legacy`。
- 显示：`Xvfb :100 -screen 0 1024x768x24 -ac -listen tcp`；Godot 参数由脚本传入 `--legacy-ui --legacy-hud --window`。
- 账号/角色：`test@test.com` / `TestHero`。
- 登录证据：日志显示服务器 7000 ready、Login 成功、`AutoStartGame`、`S.StartGame: Result=Success`，并载入 174 个魔法。
- 截图命令：`DISPLAY=:100 scrot -o <文件>`。
- 截图为完整 1024×768 viewport；Xvfb 无窗口管理器，地图区域呈灰色/局部黑色，这是运行环境基线，不是技能书控件。

## 截图逐项记录

| 文件名 | 用途 | 状态/操作 | 结论 |
|---|---|---|---|
| `skill-book-initial-2026-09-25.png` | 关闭时打开技能书基线 | 裸 `E` 打开；Fire 类；6 行左页；右页无选中详情；快捷栏同时可见 | F400 书页可见，左页六行/八类纵列/页码/前后页按钮成立；技能书与常驻快捷栏为独立层。
| `skill-book-selected-2026-09-25.png` | 右页详情、图标、文本换行 | 点击第一行 Fire Ball | 图标和名称显示；右页显示属性、元素、等级、修炼值、说明；说明在书页内折行且未越界。
| `skill-book-page2-2026-09-25.png` | 多页与末页空槽 | 点击右翻页，日志 `page=2/2 school=Fire` | 第二页显示剩余两技能和空槽占位；页码变为 2；左翻按钮仍可用。
| `skill-book-ice-2026-09-25.png` | 类别切换 | 点击 Ice 图标，日志 `category=Ice count=7 page=1/2` | 类别图标帧、选中类别与列表刷新成立；选中状态清除，页码归 1。
| `skill-book-closed-2026-09-25.png` | 关闭后主 HUD 基线 | `E` 关闭 | 书页完全隐藏；常驻快捷栏和 HUD 保留。
| `skill-book-reopen-2026-09-25.png` | 关闭后重开 | 再次 `E` 打开 | 书页重建为 Ice 页 1，旧 Fire 选中详情不残留；关闭/重开路径有效。
| `skill-book-empty-category-2026-09-25.png` | 类别边界检查 | 点击 Phantom 类别 | 列表刷新为该类技能，6 行布局不改变；类别切换不产生现代滚动条。
| `skill-book-physical-category-2026-09-25.png` | 少于一页状态 | 点击 Physical 类别 | 仅 3 条技能，其余槽为空；布局和页码稳定，未用伪技能补满。
| `skill-book-final-open-recheck-2026-09-25.png` | 构建后最终打开态复核 | 重启 `skill-login` 后用裸小写 `e` 打开 | 真实客户端重新登录成功，F400 书页、八类纵列、六行技能与独立快捷栏均可见。 |
| `skill-book-final-selected-recheck-2026-09-25.png` | 构建后最终详情复核 | 点击第一行 Fire Ball | 选中边框、真实 MIcon 图标、右页字段与受限换行均可见。 |

| 补充截图 | 用途 | 运行命令 | 状态/结论 |
|---|---|---|---|
| `skill-book-long-description-2026-09-25.png` | 长描述换行/底边裁剪 | `DISPLAY=:100 scrot -o <文件>` | 详情长文本在右页受限区域内换行，未进入书外；原版 formatter 语义仍列为阻塞。 |
| `skill-book-row-hover-2026-09-25.png`、`skill-book-row-pressed-2026-09-25.png` | 技能行悬停/按下 | `xdotool mousemove/click; DISPLAY=:100 scrot` | 行 hover/pressed 高亮与选中边框可见；按钮状态路径已实屏记录。 |
| `skill-book-category-fire-2026-09-25.png`、`skill-book-category-fire-2026-09-25-rerun.png`、`skill-book-category-ice-2026-09-25.png`、`skill-book-category-ice-2026-09-25-rerun.png`、`skill-book-category-lightning-2026-09-25.png`、`skill-book-category-lightning-2026-09-25-rerun.png`、`skill-book-category-wind-2026-09-25.png`、`skill-book-category-wind-2026-09-25-rerun.png`、`skill-book-category-holy-2026-09-25.png`、`skill-book-category-holy-2026-09-25-rerun.png`、`skill-book-category-dark-2026-09-25.png`、`skill-book-category-dark-2026-09-25-rerun.png`、`skill-book-category-dark-2026-09-25-fixed.png`、`skill-book-category-phantom-2026-09-25.png`、`skill-book-category-phantom-2026-09-25-rerun.png`、`skill-book-category-physical-2026-09-25.png`、`skill-book-category-physical-2026-09-25-rerun.png` | 八类切换态 | `xdotool mousemove/click; DISPLAY=:100 scrot` | 八类纵列均逐项采集；类别图标、页码归一与列表刷新成立。 |
| `skill-book-next-pressed-2026-09-25.png`、`skill-book-previous-pressed-2026-09-25.png` | 翻页按钮状态 | `xdotool mousemove/click; DISPLAY=:100 scrot` | F410/F412 pressed 状态与页码切换可见；边界行为由日志验证。 |
| `skill-book-entry-e-2026-09-25.png`、`skill-book-entry-e-closed-2026-09-25.png`、`skill-book-entry-ctrl-e-2026-09-25.png` | E/Ctrl+E 入口 | `xdotool key --window <id> e/ctrl+e; DISPLAY=:100 scrot` | 裸 E 与 Ctrl+E 入口截图已归档；最终构建后裸小写 e 复核见最终截图。 |
| `skill-book-entry-f1-2026-09-25.png`、`skill-book-entry-f1-diagnostic-2026-09-25.png`、`skill-book-entry-shift-f1-2026-09-25.png`、`skill-book-entry-shift-f1-diagnostic-2026-09-25.png`、`skill-book-entry-shift-f1-fixed-2026-09-25.png`、`skill-book-entry-ctrl-f1-diagnostic-2026-09-25.png`、`skill-book-entry-ctrl-f1-fixed-2026-09-25.png` | F1/Shift+F1/Ctrl+F1 入口 | `xdotool key --window <id> F1/shift+F1/ctrl+F1; DISPLAY=:100 scrot` | 普通 F1、Shift 与 Ctrl 诊断/修复截图均保留；Ctrl+F1 组标签缺少独立视觉证据，见未闭合项。 |
## 输入与控制台证据

- `E`：技能书打开/关闭均成功。
- `Ctrl+E`：代码路径保留独立窗口入口；本次实屏重点验证裸 `E`。
- `F1`：选中 Ice Bolt 后日志出现 `[MagicLegacy] bind skill=Ice Bolt set=1 key=Spell01`，说明技能书内绑定链可达。
- `Shift+F1..F12`：实现映射为 `Spell13..Spell24`，代码排除 Ctrl/Alt；本次 Xvfb xdotool 仅稳定捕获了普通 F1 日志，未将 Shift 单独列为实屏通过。
- `Ctrl+F1..F4`：GameScene 在可见窗口门控前处理 `SpellSet01..04`，避免被技能书普通 F 键处理吞掉；需后续以快捷栏组标签截图补强。
- 日志关键行：`[MagicLegacy] category=Fire count=8 page=1/2`、`refresh school=Fire skills=161`、`selected=Fire Ball id=FireBall`、`page=2/2 school=Fire`、`category=Ice count=7 page=1/2`、`selected=Ice Bolt id=IceBolt`。
- 已生成当前客户端 DB 的完整映射与 MIcon 元数据：`/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/magic-icon-map-2026-09-25.txt`（174 条）及 `magic-icon-metadata-2026-09-25.json`（164 个唯一帧，含 header offset/尺寸与 alpha bbox）；这闭合 Zircon 当前资源链，不等同于 EI `[skill+6]` 逐项映射。

## 未闭合项

1. 原版 F400 的 296×332 初始化记录与当前 452×380 包装/资源配准仍有静态证据冲突。
2. 原版六个左页 hit RECT 的最终写入值、F440/F441 业务动作、完整 EI 分类链表/排序尚未获得独立运行时证据。
3. `MagicInfo.Icon` 对 EI `[skill+6]` 的逐项帧、offset、有效 alpha bbox 尚未完全导出交叉验证。
4. Shift 快捷键与 Ctrl+F1..F4 需追加带快捷栏组标签的实屏截图；当前实现与日志路径已检查但截图证据不足。

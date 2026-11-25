# 掌机 NPC 全量审计阶段报告

日期：2026-11-25（暂停任务续跑）。范围限定 Godot 掌机客户端；没有部署至 TG5040，没有修改 System.db、正式存档、凭据或 PJOS。

## 数据基线与覆盖分母

- 隔离副本客户端与服务端 System.db SHA-256 相同：`511595c3f5709d21f6d771641ce3b5730a556da2e0b1cccfe05ca07b26dda865`。
- 当前 System.db：294 条 NPC，其中 230 条有入口页；4295 个 NPCPage，4284 个从入口结构可达；2033 页含文本，253 页属于服务类型。
- 离线 Godot 遍历记录 4284 个 NPC-页组合，生成 2031 张可见文本页截图。64 条 NPC 同时没有地图坐标和入口页。现有首点坐标中未发现重叠。4295 页的边索引包含 861 条条件失败跳转。离线索引未发现重复 inline ID、缺失按钮绑定或空条件目标。两个未从入口结构可达的文本页为 `36351` 和 `36354`。
- 暂停前既有隔离服记录覆盖 NPC `13–23`，共 11/294 个 NPC、65 个访问页、67 张截图（含第 24 个 NPC 的两张未完成记录），其中 9 个 NPC 共报告 12 条 `link_failed`。日志中的 132 条已记录边包含 54 次新页、46 次返回或环路、32 次客户端关闭。
- 逐 NPC、逐页机器索引及状态见 `.artifacts/handheld-ui-full-20261125/coverage.json`；离线完整 catalog 见 `.artifacts/handheld-ui-full-20261125/npc-catalog-final/catalog.json`。

## 实际结论

- **PASS（本地）**：NPC 文本页 640×360 Godot 离线绘制；姓名、正文、头像/缺省头像、选项焦点、长选项、正文滚动、页翻页、冒号价格文本和单次回应均有检查。截图来自真实 System.db 数据，但截图存在不等于逐图人工签收。
- **FIXED**：仅出售 BuySell 页会由 `ShowPage` 打开并置顶库存出售窗，导致掌机控制仍指向 NPC 对话但 service pane 无法接管。掌机模式现将出售库存窗视为服务数据源，不抢根服务的按键焦点；数量/确认框等更高层 prompt 仍优先。提交：`d28ddaa8`。
- **FIXED**：掌机正文曾显示 `{FCOLOR/10}...{FCOLOR/12}` 脚本标记。现在从原色表读取颜色，`12` 恢复默认字色，回应标签保持纯文本。
- **ISSUE（待联机复测）**：既有短实服扫到少量链接无页响应/无变化；商店 NPC 的出售回应在修复前不能选择。新的 service-focus 本地回归已通过，但尚未以新客户端重跑同 NPC 的隔离实服服务页。
- **BLOCKED（未完成）**：全量 NPC 的真实交谈、条件检查、服务操作效果、费用/物品差值、每种服务成功/失败/返回边仍需隔离 ServerCore 会话逐项运行。不得把目前 11 条 live 结果延续为全量结论。

## 验证命令与结果

- `dotnet build GodotClient/ZirconClient.csproj`：PASS，0 错误；日志 `.artifacts/handheld-ui-full-20261125/logs/build-final.log`。
- `ZIRCON_HANDHELD=1 godot-mono --headless --path GodotClient --script res://Tests/HandheldNpcInteractionChecks.cs -- --handheld`：PASS（最新单独运行）。覆盖 5 格距离 NPC 请求、含冒号选项、重复确认只发一包、无效页清旧回应、FCOLOR palette、页修订使旧确认失效、仅出售商店识别、B 关闭。夹具退出有 CanvasItem/ObjectDB leak 警告，不据此声称真实会话生命周期通过。
- `godot-mono --headless --path GodotClient --script res://Tests/HandheldServiceFocusChecks.cs -- --handheld`：PASS；同命令不带 `--handheld`：PASS。确认掌机 service root 不被 sale bag 抢焦点，真实确认框仍优先，PC 原生路径保持。
- 另通过：`HandheldInputChecks`、`HandheldBindingsChecks`、`HandheldActionIntegrationChecks`、`HandheldJournalExitChecks`、`HandheldPromptChecks`、`HandheldBagFootprintChecks`、`HandheldQuickLayoutChecks`、`HandheldHudChecks`、`HandheldMiniMapChecks`、`HandheldWorldMapChecks`。
- Xvfb/OpenGL 下通过并输出 640×360 图像：`HandheldNpcDialogueChecks`、`HandheldBagFootprintChecks`、`HandheldWorldMapChecks`。图像见 `.artifacts/handheld-ui-full-20261125/screens/`。
- `HandheldNpcCatalogChecks` 在 Xvfb/OpenGL 下完成离线完整渲染遍历：294 NPC、4295 页表、4284 结构可达页、2031 张页截图，选项焦点和待回应检查报告 0 个错误。日志 `.artifacts/handheld-ui-full-20261125/logs/catalog-final.log`。

## 截图索引

- `screens/npc-dialogue.png`：640×360 普通长文本/多选项对话。
- `screens/bag-footprint.png`：640×360 背包装备多格占用和详情。
- `screens/world-map.png`：640×360 Select 大地图与 HUD 信息。
- `npc-catalog-final/n14-p33906.png`：真实 catalog 中带 FCOLOR 的长文本页。
- `screens/sell-only-service.png`：仅出售服务簿。排队测试确认首次 A 核对，反复 A 仅排队一个 NPC 回复包。没有执行出售，也没有服务器回包。
- `.artifacts/handheld-npc-full-20261125/offline/n*-p*.png`：离线 NPC 页截图集合，2031 张。

## 未完成工作

1. 本轮按数据库及凭据限制，未启动读取保存账号或写入既有隔离存档的测试服。联机续跑从 NPC `24` 的未完成记录继续。对 `14–20`、`22–23` 的失败边做定向复测，跳过已记录完成的外部操作。
2. 对每种可达服务分别检查首次进入、方向/A、数量/确认取消、接受/拒绝、关闭再开、页面切换后旧回调；在隔离存档中核对货币、物品和锁状态差值。等待、受限、无结果分别记录。
3. 补齐条件失败、环路、自环、重复 option ID、SuccessPage/费用传送和所有服务代表截图的 live 证据，并对图片逐项人工检查。

PC NPC 原生窗口通过独立桌面焦点检查。设备部署状态为 `NOT_DEPLOYED`。
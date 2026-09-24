# Legacy HUD 运行验收记录（2026-09-24）

## 固定环境

- Zircon 分支：`ui/legacy-layout-lab`
- 运行资源：`/home/tetsuya/mir2ei`
- 数据根：`/home/tetsuya/mir2ei/Data`
- 显示：Xvfb `:100`，窗口 `1024×768`
- 服务端：本地 ServerCore `127.0.0.1:7000`
- 客户端参数：`--window --legacy-ui --legacy-hud --server 127.0.0.1 --port 7000 --user test@test.com --pass test123 --char TestHero`

## 代码验证

```text
dotnet build GodotClient/ZirconClient.csproj --no-incremental
通过：ZirconClient.dll 生成成功。
既有警告：TableSnapshotTool nullable 注释、GameScene 未使用局部变量。
```

本轮修改覆盖：

- `MainPanel` 将 legacy HUD 根控件固定为逻辑 `800×136`，避免本地 F50 帧头 `1024×68` 直接成为布局根尺寸；经验条仍为 F63、相对 `(61,121)`、339×11。
- `GameScene` 在 legacy 路径强制保留底部 `ChatLogPanel`；`MailButton` 改为打开/关闭 F350 `LegacyChatDialog`，不再切换底栏或现代通信窗。
- `InventoryDialog` 接入 F280（16×424）轨道、六行视口、滚轮/拖柄 Value 写回；legacy `DXItemGrid` 使用有证的 `Inventory.wil` 图库计算 first-fit footprint 并绘制物品，记录槽位与可视格索引分离；原始服务端列/行位置仍待协议字段。
- `CharacterDialog` 扩展态改为原版证据中的双列 15px 行距，覆盖第一列 13 个标签和第二列 11 个标签；未证服务器语义的“中毒恢复”显示 `—`。

## 实际运行证据

客户端日志确认：

- `MIR3_EI_ROOT`、`ZIRCON_UI_DATA_PATH`、`ZIRCON_LEGACY_UI_DATA_PATH` 均指向 `/home/tetsuya/mir2ei`。
- `GameInter` legacy WIL fallback 从 `/home/tetsuya/mir2ei/Data/GameInter.wil` 加载。
- 既有一次完整运行记录中，网络连接、版本校验、自动登录成功；服务端完成 `StartGame player.StartGame()`，客户端收到 `S.StartGame: Result=Success`、对象包和 `MapView` 首帧。
- 本轮在清理服务端会话后重跑自动登录，服务端仍完成 `StartGame player.StartGame()`，但客户端只持续收到 Ping，未收到 `S.StartGame`；因此没有把该次运行截图冒称为完整 HUD 验收，问题留在运行环境/启动流程阻塞项。

保存的截图：

- `.artifacts/ui-acceptance-2026-09-24/hud-baseline-1024x768.png`：登录/选角基线。
- `.artifacts/ui-acceptance-2026-09-24/after-enter-click.png`：选角点击后的重复场景状态。
- `.artifacts/ui-acceptance-2026-09-24/game-hud-1024x768.png`：启动流程截图。
- `.artifacts/ui-acceptance-2026-09-24/game-focused.png`：聚焦窗口截图。
- `.artifacts/ui-acceptance-2026-09-24/inventory-layout-after-footprint.png`：`LegacyHudLayoutLab --legacy-open=inventory` 离线背包布局截图；F250、6×6 可视区和 F280 锁链轨道可见。

1024×768 HUD 像素验收：阻塞。自动登录重跑未完成 `S.StartGame` 客户端分派，完整游戏 HUD、动态经验填充和普通聊天仍未取得新截图。
- F350 点击/R/关闭后二次打开、背包滚轮/拖柄边界、人物收起/展开属性截图：待完整游戏场景恢复后复测。
- 跳过：坐骑 S/Ctrl+S、第二玩家交易、会触发 Bad Request 的命令。

离线/静态补充：

- `LegacyHudLayoutLab` 使用本地资源运行，截图显示 F250 背景、六列六行格区、F280 垂直锁链轨道和底部 HUD 同屏。
- 代码构建后 `UITestScene --ui-audit --character-audit --storage-audit --chat-audit` 完成启动；`UICharacterAudit`、`UIStorageAudit`、`UIChatAudit`、`UIInventorySaleAudit` 等通过，既有 `UIHudAudit`/`UIItemGridAudit` 的现代基线失败未归因于本轮 legacy 改动。

详细静态差异矩阵：
`/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/HUD_ZIRCON_DIFF_MATRIX_2026-09-24.md`

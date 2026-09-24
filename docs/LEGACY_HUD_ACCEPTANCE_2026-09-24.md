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
- `InventoryDialog` 接入 F280（16×424）轨道、六行视口、滚轮/拖柄 Value 写回；记录数组按六列动态计算总行数。跨格 footprint 仍明确阻塞。
- `CharacterDialog` 扩展态改为原版证据中的双列 15px 行距，覆盖第一列 13 个标签和第二列 11 个标签；未证服务器语义的“中毒恢复”显示 `—`。

## 实际运行证据

客户端日志确认：

- `MIR3_EI_ROOT`、`ZIRCON_UI_DATA_PATH`、`ZIRCON_LEGACY_UI_DATA_PATH` 均指向 `/home/tetsuya/mir2ei`。
- `GameInter` legacy WIL fallback 从 `/home/tetsuya/mir2ei/Data/GameInter.wil` 加载。
- 网络连接、版本校验、自动登录成功。
- 服务端完成 `StartGame player.StartGame()`；客户端收到 `S.StartGame: Result=Success`、对象包和 `MapView` 首帧。

保存的截图（本地验收临时目录）：`/tmp/zircon-ui-acceptance-2026-09-24/`

- `hud-baseline-1024x768.png`：登录/选角基线。
- `after-enter-click.png`：选角点击后的重复场景状态。
- `game-hud-1024x768.png`：启动流程截图。
- `game-focused.png`：聚焦窗口截图。
- 1024×768 HUD 像素验收：阻塞。当前测试进程出现重复 Login/Select 流程，选择场景覆盖了截图；不将这些截图冒称为 HUD 完整验收。
- 经验条动态填充、普通聊天文本底栏、F350 点击/R/关闭后二次打开、背包滚轮/拖柄边界、人物收起/展开截图：待清理重复场景后复测。
- 跳过：坐骑 S/Ctrl+S、第二玩家交易、会触发 Bad Request 的命令。

详细静态差异矩阵：
`/home/tetsuya/development/Mir3-Research/docs/research/ei-ui-layout/HUD_ZIRCON_DIFF_MATRIX_2026-09-24.md`

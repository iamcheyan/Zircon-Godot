# EI 登录—选角流程实机验收记录

测试分支：`goal/legacy-ei-login-select-flow`
客户端提交：`d3e8d1fc`
测试服务：隔离副本 `127.0.0.1:7001`（不使用 7000 主开发服务）；测试数据仅写入 `/tmp/zircon-test-server` 副本。
运行：Godot Mono 4.6.3，游戏协议版本 `2026.09.26.1`。

## 结果

- 登录：通过。客户端收到连接确认、版本校验通过及登录成功；服务端返回 3 个角色。
- 新建角色：通过。通过 legacy 创建表单提交 `FlowLegacy0928`；客户端记录“建角色成功”，隔离服务器日志记录 `Character Created`。未删除任何角色。
- 角色列表：普通选择界面可正确列出 `TestHero`、`FlowTest0928`、`FlowLegacy0928`；手动选择新角色后可开始游戏。
- 进入游戏：通过。日志确认 `FlowLegacy0928` 进入地图 1（Bichon Town）；游戏内截图可见角色、地图及 HUD。

## 遗留问题 / 未通过项

1. **EI legacy 选角画面不通过视觉验收**：按钮图像/标签与语义不符、出现重复“开始游戏”按钮，角色信息和槽位显示不清。见 `01_legacy_select_layout_issue.png`。普通选择界面可用，但不代表 EI legacy 布局合格。
2. **CreateChr/StartGame 视频未验收**：本机运行资源缺少 `CreateChr.ogv` 与 `StartGame.ogv`；代码走缺文件回调，未能验证过场实际播放。
3. **legacy F602 开始确认未完成实测**：弹窗出现但内容/勾选控件不可辨，不能确认后续手动开始链路。普通选择界面手动开始已实测成功。
4. **自动指定角色存在索引错误**：`--char FlowLegacy0928` 日志先匹配到该名字，但随后 `AutoStartGame` 发出的索引对应到 `TestHero`；服务端实际进入了 `TestHero`。改用普通选择界面手动选中第三行后，`FlowLegacy0928` 才正确进入游戏。该问题未在本次任务中修改。
5. **删除流程部分验证**：普通选择界面的删除确认框显示了目标 `FlowLegacy0928`，我按取消关闭，服务端未收到删除请求，角色未删除。legacy EI 的 5 秒倒计时/确认路径因按钮错位未能验证。
6. 800×600 视口下，650 高的创建面板底部会被截；本次通过 1024×768 窗口及 `ZIRCON_UI_SCALE=1.0` 完成创建表单测试。

## 截图索引

- `01_legacy_select_layout_issue.png` — EI legacy 选角画面缺陷
- `02_legacy_creation_filled.png` — legacy 新建角色表单，含 `FlowLegacy0928`
- `03_character_created.png` — 提交后的 legacy 角色界面；成功结论以客户端/服务端日志为准
- `04_start_confirmation.png` — legacy F602 弹窗，确认控件不可辨
- `05_modern_character_selected.png` — 普通选角界面，第三行新角色已选中
- `06_new_character_in_game.png` — 成功进入实际游戏场景；具体角色由本记录中的对应客户端日志确认
- `07_delete_confirmation_safe.png` — 普通选择界面删除确认框；取消操作，未删除角色

截图不含密码或访问令牌。隔离服务器中保留测试角色 `FlowTest0928` 和 `FlowLegacy0928`；没有对 7000 服务或其数据库执行写入。

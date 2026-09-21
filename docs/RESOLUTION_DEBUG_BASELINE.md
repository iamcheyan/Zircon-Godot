# UI 校准阶段分辨率基准

更新时间：2026-09-21

## 当前约定

当前使用测试方案一：直接使用原版设计画布和窗口，UI 实机验证尺寸固定为：

```text
1024×768
```

固定逻辑位于：

- `GodotClient/Scripts/ClientSettings.cs`：`FixedDebugGameSize`
- `GodotClient/Controls/ConfigDialog.cs`：分辨率下拉暂时只显示 `1024 x 768`

逻辑设计画布和实际窗口都是 `1024×768`。即使用户配置文件 `user://Zircon.ini` 里残留其它值，启动时也会覆盖为 `1024×768` 窗口；启动参数 `--window=WxH` 在这个阶段同样不会改变校准基准。Godot 不启用 viewport stretch，由桌面/窗口管理器决定窗口在显示器上的外部缩放效果。

## 解除条件

完成以下工作后再解除固定窗口锁定：

1. 在 `1024×768` 下完成主界面、背包、角色、设置、菜单、快捷栏和小地图的布局校准。
2. 实机确认文字基线、贴图边缘、窗口边界和鼠标命中区域没有缩放偏差。
3. 恢复分辨率下拉的多档列表，并重新验证 16:9、16:10 和 5:4 窗口比例。

## 验证方式

启动后以实际窗口尺寸为准检查日志和设置页，不以桌面截图外框尺寸推断内部渲染分辨率。构建验证命令：

```bash
dotnet build GodotClient/ZirconClient.csproj --no-incremental
```

在本机 Hyprland 环境下，推荐通过 `/home/tetsuya/mir3ei/login_game.sh` 或
`start.sh` 启动。脚本会等待 `ZirconClient` 窗口创建完成，将它移动到第一个显示器，
设为浮动并调整为 `1024×768`；非 Hyprland 环境保持原来的前台启动流程。

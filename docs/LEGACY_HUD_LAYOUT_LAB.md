# 旧版 EI 主 HUD 测试布局

## 基准

测试场使用旧版客户端的 800×600 逻辑画布，不把旧版坐标重新解释成新版
1024×768 坐标。`GameInter[50]` 是 800×136 的底部主 HUD，固定在
`(0,465)`。

血蓝球和经验条使用旧版资源层：

- `GameInter[60]` 红色半球，屏幕矩形 `(61,496)-(104,566)`
- `GameInter[61]` 蓝色半球，屏幕矩形 `(105,496)-(147,566)`
- `GameInter[63]` 经验条，屏幕矩形 `(61,586)-(400,597)`

## 运行

旧版 WIL/WIX 资源复制到本机后，先把 `GameInter.wil` 转成测试用的
`GameInter.Zl`，放在 `/home/tetsuya/mir3ei/LegacyEI/Data/`，然后运行：

```bash
ZIRCON_UI_DATA_PATH=/home/tetsuya/mir3ei/LegacyEI/Data \
godot-mono --path GodotClient res://Scenes/LegacyHudLayoutLab.tscn -- --window
```

`ZIRCON_UI_DATA_PATH` 只影响本次 Godot 进程，不会改变正式客户端当前使用的
`/home/tetsuya/mir3ei/Data`。

## 验证记录

2026-09-22：`dotnet build GodotClient/ZirconClient.csproj --no-incremental`
通过；在 Xvfb 1024×768 窗口中实际启动测试场并截图确认旧版底图、红蓝球、
经验条和九个功能按钮均可绘制。

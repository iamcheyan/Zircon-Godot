# Godot 客户端世界与界面缩放

本文记录 Godot 客户端在窗口缩放和高 DPI 屏幕上的世界画面缩放设计、实现位置与验证边界。

## 问题表现

原先 HUD 按窗口视口尺寸计算 `UiScale`，地图、角色、怪物、天气和世界特效却使用固定 `WorldScale = 1`。窗口变大时界面控件变大，世界内容仍按原始逻辑像素绘制，于是人物相对窗口和 HUD 显得很小。

另一个容易混淆的问题是客户端运行在哪份源码上。开发机 `/home/tetsuya/development/zircon` 和 Mac `/Users/tetsuya/Development/Zircon` 是不同工作树。执行

```bash
/Users/tetsuya/mir2ei/LegacyEI/login_game.sh remote 192.168.3.82 legacy
```

时，`remote` 让远程机器负责构建/启动服务端；Godot 客户端仍在 Mac 本地工作树构建并运行。`legacy` 选择 EI 旧版 HUD 资源，不会切换到原版 EI 可执行程序。因此只改开发机工作树、未同步到 Mac，Mac 截图不会包含这些客户端改动。

## 缩放模型

逻辑基准仍为 `1024×768`，与 Godot 客户端已有的 HUD 布局保持一致。窗口视口大小为 `W×H` 时，世界基础倍率按较受限的一边计算并限制在 `1×` 到 `2×`：

```text
scale = clamp(min(W / 1024, H / 768), 1, 2)
```

例如视口为 `2028×1316` 时，计算结果约为 `1.71×`。更窄或更矮的窗口由受限方向决定倍率，以避免逻辑 UI 画布超出窗口；当前窗口尺寸设置还将窗口模式的最小尺寸限制在 `1024×768`。

`GameScene.WorldScale` 和 `GameScene.UiScale` 使用同一个屏幕基础倍率，均限制在 `1×` 到 `2×`。因此 1× 屏幕不额外缩放，2× 屏幕放大到约 2×；显示器的 Retina 2× 不再额外乘入，因为 Godot 的 viewport 已经反映了实际渲染尺寸。缩放在 `RefreshUiScale()` 中更新：

1. 更新 UI `CanvasLayer` 的变换。
2. 更新 `GameScene.WorldScale` 和 `GameScene.Scale`，使挂在游戏场景世界树下的地图、玩家、其他实体、粒子和世界效果共享世界倍率。
3. 更新独立 `CanvasLayer` 上的光照变换。光照层不继承普通场景树的 CanvasItem 变换，必须单独设置。
4. 更新 `MirSkin` 的 UI 资源缩放，使用 HUD 的 `UiScale`。
5. HUD 和世界层字体都使用同一套逻辑字号；HUD 挂在 HUD 缩放层，世界名称挂在世界节点，分别随各自画布倍率放大一次，不做反向补偿。

窗口尺寸变化时，`OnGameResized()` 和 `_Process()` 的视口尺寸检查会重新应用布局与缩放。独立登录/选人场景继续使用 `UiScaler`；本次世界缩放改动只覆盖游戏场景。

## 逻辑坐标与输入

地图瓦片坐标、对象位置和移动插值仍使用原有逻辑像素（地图格为 `48×32`），缩放只影响最终显示变换。地图视口计算先除以 `WorldScale`，让绘制范围使用逻辑坐标；`ScreenToCell()` 通过地图节点的全局变换逆矩阵把屏幕鼠标位置还原到地图局部坐标。

`MouseWalker` 将鼠标位置除以相同世界倍率后再计算移动方向。这样角色绘制、鼠标命中、地图格选择和按住鼠标移动共享同一坐标约定，避免放大后输入位置漂移。

## 修改文件

| 文件 | 作用 |
|---|---|
| `GodotClient/Scripts/GameScene.cs` | 以 `UiScale` 为统一倍率，缩放世界根节点和独立光照画布；在窗口变化时重新应用。 |
| `GodotClient/Scripts/MapView.cs` | 视口逻辑尺寸、格子到屏幕的位置和鼠标格子映射读取共享的 `WorldScale`。 |
| `GodotClient/Scripts/MapLightLayer.cs` | 光照层画布独立于世界场景树，逻辑绘制范围读取共享倍率。 |
| `GodotClient/Scripts/MapWeatherLayer.cs` | 天气粒子的逻辑视口读取共享倍率；粒子节点本身随世界根节点缩放。 |
| `GodotClient/Scripts/MouseWalker.cs` | 鼠标逻辑坐标按共享倍率换算。 |

## 构建与运行检查

本次开发机检查记录：

- `dotnet build GodotClient/ZirconClient.csproj --no-restore`：成功，0 错误、3 条警告（nullable 上下文和两个未使用局部变量）。
- `git diff --check`：通过。
- 曾在 Linux/Xvfb 启动客户端；TCP 连接和协议版本检查成功，但测试会话没有完成登录/进入游戏，因此这次启动没有验证游戏内的实际缩放。
- 用户提供的 Mac 截图来自 Mac 本地工作树尚未拉取本次客户端提交时的运行；该图证明当时运行画面人物仍小，但不能用于验证新提交。源码提交随后推送到 `origin/ui/legacy-layout-lab`，Mac 必须更新本地分支并重新构建后才能验证。

## DPI 边界与后续验收

倍率基于 Godot `Viewport` 实际报告的尺寸，代码没有另外读取或乘以显示器 DPI 倍率。因而窗口拉伸/视口尺寸变化会被覆盖；不同系统的 Retina/高 DPI 映射是否已体现在 Godot 视口尺寸中，需要在目标 Mac 上运行更新后的客户端确认。窗口标题的像素尺寸不能代替视口尺寸日志。

目标机器的验收应在同一地图、同一角色和同一窗口下，更新前后比较人物/怪物/地图元素与 HUD 的实际屏幕尺寸，并记录 Godot 视口尺寸、`UiScale`、`GameScene.Scale`。还需拖动窗口边缘改变尺寸，确认世界和 HUD 同步变化，地图点击、鼠标移动、光照和天气没有偏移或裁切问题。构建成功只能证明源码可编译，不能代替这项图形验收。

## 源码入口

- `GodotClient/Scripts/GameScene.cs`：`UiScale`、`WorldScale`、`RefreshUiScale()`、`OnGameResized()`。
- `GodotClient/Scripts/ClientSettings.cs`：`ApplyDisplaySettings()` 和窗口最小尺寸/初始尺寸处理。
- `GodotClient/Scripts/MapView.cs`：`CellToScreen()`、`ScreenToCell()`。
- `GodotClient/Scripts/MouseWalker.cs`：鼠标位置到逻辑世界坐标的换算。
- `docs/REMOTE_SERVER_AND_CLIENT_SETUP.md`：Mac 本机客户端和远程服务端的构建/启动边界。

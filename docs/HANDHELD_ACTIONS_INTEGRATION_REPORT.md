# TG5040 掌机输入与绑定界面整合报告

日期：2026-11-24  
状态：本地自动检查、ARM64 导出、TG5040 客户端部署和标准入口进图已验证。实体输入及服务器动作验收未完成，goal 保持开启。

## 实现

- `GodotClient/Input/HandheldActions/HandheldActions.cs` 提供两页×六个直接动作、六个固定触发槽、短按/长按/真实重叠组合、菜单/失焦释放门控及原子 JSON 保存。组合窗与长按阈值落在同一时刻时，实际到达的组合第二键优先，两个槽位的单键/长按均被压制；组合期间重按一侧也必须等到释放后重武装。
- `GameScene.HandheldActions.cs` 按服务器地址、端口和角色索引隔离配置；切换角色再切回会重新解析对应路径。动作在现有 `UseMagic`、`CanUseItem`、`DXItemCell.UseItem`、交互、目标和地图接口中执行。非法或损坏的旧配置不会被覆盖；编辑器保存失败保留最后生效绑定。
- `HandheldActionBindings` 接入手札「系统 → 操作」：第 1 页、第 2 页、固定快捷三标签；候选类别为技能、物品、功能、清空；方向键/左摇杆导航，L1/R1 切页或类别，A 确认，X 清空，B 取消；L3 打开长按阈值与组合窗口设置。目标规则显示为固定的方向夹角优先、再按距离，不提供未实现的自定义选项。
- F2 掌机 HUD 显示当前页六个真实绑定（十字方向、X、Y）和六个固定槽；空槽保持空白，不再从桌面技能栏生成五格预览。L2/R2 按住时显示tap、hold、组合动作和阈值进度。旧选择器不再创建或参与运行时输入路由。
- TG5040 fbdev 的 evdev 物理键映射沿用 `Tools/trimui-tg5040/godot-fbdev/fbdev/display_server_fbdev.cpp`：面键转为掌机语义按键，扳机轴转为 `Key1/Key2`，摇杆送入 Godot joy axis；本次没有修改或重新编译这个本机 C++ 后端。

## 自动验证

以下均在当前工作树执行：

- `dotnet run --project Tests/HandheldActionsChecks/HandheldActionsChecks.csproj --no-restore`：16 项通过。
- `dotnet build GodotClient/ZirconClient.csproj --no-restore`：通过。实际编译有 6 条既有警告，无错误。
- `ZIRCON_HANDHELD=1 MIR3_EI_ROOT="$HOME/mir2ei" godot-mono --headless --path GodotClient --script res://Tests/HandheldActionIntegrationChecks.cs`：26 项通过，含真实临时 JSON 保存、另一个角色不继承绑定、新 GameScene 重新加载绑定、失败保留和语义键事件拦截。没有登录服务端，也未发送施法或用药请求。
- 同环境下 `HandheldBindingsChecks`、`HandheldHudChecks`、`HandheldInputChecks`、`HandheldQuickLayoutChecks`、`HandheldMiniMapChecks`：均通过。
- `godot-mono --headless --path GodotClient --script res://Tests/RunningContinuityChecks.cs -- --offline-movement-test`：通过短走、持续跑和释放停止场景。
- 640×360 Xvfb / OpenGL：`HandheldBindingsChecks` 与 `HandheldHudChecks` 渲染实际 Control。候选测试从只读客户端技能定义构造离线角色，方向键跨过第十个候选进入下一屏；同时验证原配置损坏时保存失败、修复文件后可重试同一候选。HUD 截图使用无地图角色夹具；这些截图不是实体输入或实机字号证据。

## 渲染证据

绑定界面：[第 1 页](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-page-1.png)、[第 2 页](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-page-2.png)、[固定槽](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-fixed.png)、[技能候选首屏](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-picker.png)、[技能候选次屏](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-picker-page-2.png)、[功能候选](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-functions-category.png)、[清空候选](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-clear-category.png)、[保存失败保护](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-save-error.png)、[阈值设置](../Tools/trimui-tg5040/evidence/handheld-actions-ui-2026-11-24-verified/bindings-timing.png)。

F2 HUD：[第 1 页](../Tools/trimui-tg5040/evidence/handheld-actions-hud-2026-11-24/hud-page-1.png)、[第 2 页](../Tools/trimui-tg5040/evidence/handheld-actions-hud-2026-11-24/hud-page-2.png)、[L2 长按提示](../Tools/trimui-tg5040/evidence/handheld-actions-hud-2026-11-24/hud-l2-held.png)、[L2+R2 组合提示](../Tools/trimui-tg5040/evidence/handheld-actions-hud-2026-11-24/hud-chord-hint.png)。

## ARM64 导出

使用已有模板 `$HOME/development/godot-fbdev/src/bin/godot.linuxbsd.template_release.arm64.mono`，运行 `Tools/trimui-tg5040/godot-fbdev/rebuild-client.sh`。导出目录：

`.artifacts/handheld-actions-final-2026-11-24/client-fbdev.131B0b/`

- 导出包含 201 个文件。
- ARM64 可执行文件尾标记为 `GDPC`，配套程序集为 `data_ZirconClient_linuxbsd_arm64/ZirconClient.dll`。
- `Mir2eiClient` SHA-256：`e23ab122c048eba45e23108418a2306faec6dd628a8317321f700a431ba8ef37`
- 设备 `ZirconClient.dll` SHA-256：`de81c7fe4a15fa8062dd11b70c5eb0f95664c1b3043d775475c2ea987af43f8d`
- 本地 Debug 回归 DLL SHA-256：`cb2868292b2a4cac7f73748d9d294d2db613c9acb753922d249cf438f8dff66f`；补跑技能候选分页和保存重试的本地 DLL：`625541e85736ee5d592a2c04bb21bf22c4b9706efae52afd43e5086fb65f1da8`（只修改验证脚本，不重新部署）。
- 部署文件清单：`.artifacts/handheld-actions-final-2026-11-24/client-current-device.sha256`

## TG5040 部署与回退

- `adb -P 5038 devices -l` 找到设备；设备自报 TinaLinux / PJOS 1.5.3 / `aarch64` / `allwinner,a133`。现有标准安装位于 `/mnt/SDCARD/apps/Zircon`；没有重装启动器或替换服务端。
- 只暂存可执行文件和 .NET 目录（不复制 `.xdg`）；主机与 SD 卡分别以 `sha256sum -c` 验证全部 197 个文件，并检查 `GDPC`。对已核实路径和父进程的旧客户端发出 SIGTERM；`launch.sh` 记录停止旧 ServerCore，确认两个进程均退出后再备份和交换目录。
- 卡上回退目录：`/mnt/SDCARD/apps/Zircon/backups/handheld-actions-20261124T083317Z/` 保存上一次验证版；`handheld-actions-20261124T081726Z/` 保存本任务前的版本。均含旧 `GodotClient/`、启动脚本、配置、存档及暂存校验清单。两个库和 Server.ini 的备份均以 `cmp` 核对。启动后的 `System.db`、`Server.ini`、ServerCore 和 `launch.sh` 再次通过基线 SHA-256，`Users.db` 与部署前备份逐字节相同。未覆盖任何数据库。
- 调用设备现有 `System/compat/bin/pocketjs-native-launch zircon`，由 MainUI 执行标准 `launch.sh`。日志确认服务端监听、客户端以 fbdev 启动、自动登录并实际进图，点阵中文字体加载成功。`/proc/<pid>/exe`、`/proc/<pid>/maps` 指向标准目录，映射的 `ZirconClient.dll` 哈希与上方 ARM64 导出一致。
- [真机进图截图](../Tools/trimui-tg5040/evidence/tg5040-native-actions-world-2026-11-24.png)：读取 framebuffer 当前 `pan` 页（此帧 yoffset=0；同次采集亦观测到 720/1440），1280×720 BGRX、stride 5120；经 `adb pull` 后立即删除设备 raw。证实新动作栏、真实技能/物品图标、地图及圆形小地图上屏，不证明按键行为。
- 如需回退：先退出客户端并等原 `launch.sh` 停止 ServerCore。将活动 `GodotClient/` 移到新的保留目录，再把上述备份的 `GodotClient/` 移回原位；核对旧文件后用同一原生入口启动。只回退客户端，无须覆盖活动存档。没有执行实机回退演练。

## 尚未验证

没有获准的设备输入接口，未使用输入注入。以下需在当前 DLL 上用实体键验收：

1. 从封面启动；左摇杆短推走、持续跑、松开停，右摇杆选怪/NPC/掉落物，A 分别攻击、对话、拾取，B 清除目标；Start、Select、Menu、L3/R3 保持各自职责。
2. 从「手札 → 系统 → 操作 → 操作绑定」改一项技能和一项消耗品；取消一次候选替换，退出后用实体键翻页、施法和用药，以服务器响应和道具数量核实。实际角色的物品候选翻页及实体屏幕可读性仍未验证。
3. 分别验证肩键单按、扳机短按/长按与两种组合；在打开编辑器时持续按住一键，关闭后确认松开前不触发世界动作。
4. 退出、重新登录同一角色，核对绑定恢复；切换角色核对不继承。离线临时 JSON 测试已覆盖重新加载和角色隔离，但未验证真实登录过程。

设备运行包从当前工作树导出，包含当时既有的客户端 WIP；不能把工作树 DLL 哈希当成最小 Git 提交的可重建哈希。

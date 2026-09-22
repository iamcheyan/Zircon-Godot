# Legacy UI 双数据源编辑器

这是仓库内可复现的旧版 UI 参考编辑器，不修改旧版客户端源码。它同时读取：

- `GodotClient/UI/legacy_ui.json`：反编译得到的 800×600 旧版窗口基准；
- `GodotClient/UI/ui_tree.json`：当前 Godot 客户端导出的控件树。

浏览器左侧显示旧版窗口，右侧显示当前窗口。选择旧版窗口后可以把其换算后的坐标/尺寸复制为当前窗口的 `ui_overlay.json` 条目，供游戏 F12 热加载验证。旧版的 `candidate` 窗口只允许预览，不会直接写入 overlay。

启动：

```bash
cd /home/tetsuya/development/Zircon
python3 -m http.server 8840 --directory Tools/LegacyUiEditor
```

然后访问 `http://127.0.0.1:8840/`。编辑器通过相对路径读取 JSON，避免 `file://` 的浏览器限制；所有写入都由浏览器下载完成，不会悄悄覆盖游戏文件。

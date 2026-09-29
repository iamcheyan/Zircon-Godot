# EI 新建人物文案 / 音频修复运行取证 — 2026-09-29

对应文档：[`docs/EI_CHARACTER_CREATE_TEXT_AUDIO_FIX_2026-09-29.md`](../../docs/EI_CHARACTER_CREATE_TEXT_AUDIO_FIX_2026-09-29.md)。

## 环境

- 测试机 Xvfb `:101`（1024×768×24）+ openbox，Godot 4.6.3 mono（llvmpipe）。
- 客户端窗口 `--window=800x600`，复古画布为左上角 640×480
  （窗口内容左上 = 画布原点；`xdotool --window <wid> X Y` 的 (X,Y) 即画布坐标）。
- 截图均为 **1024×768 根窗口全屏**（含 WM 标题栏与桌面黑边），非裁剪 viewport。

## 截图

| 文件 | 阶段 | 内容 |
| --- | --- | --- |
| `01-create-warrior-male-explain.png` | phase 2 | 默认态：说明框「[ 男 战士 ]」+ 战士说明；男槽彩色（选中）、女槽灰阶 |
| `02-create-taoist-female-explain.png` | phase 2 | 点「道士」+ 点女槽：说明框「[ 女 道士 ]」+ 道士说明；女槽彩色、男槽灰阶；名字框已输入 |
| `03-select-info-box.png` | phase 0 | 点选角色后出现原版 (80,110) 详情框「角色名 TestHero / 等级 255 / 职业 道士」；无逐槽标签 |
| `04-delete-confirm-cmsg228.png` | phase 0 | 点「删除角色」→ CMsg 228 中文确认框（Yes 立即可用，无倒计时） |
| `05-two-character-limit.png` | phase 0 | 账号已有 2 角色时点「创建角色」→ 顶部显示「您可以为每个单独的帐号建立两个角色。」 |
| `06-overlay-before-fix.png` | phase 0 | A/B 对照（修复前，临时关闭 guard）：洞窟 (450,200) 处叠出 Zircon `帧+130` 的发光球+剑杂影 |
| `07-overlay-after-fix.png` | phase 0 | A/B 对照（修复后）：同位置只有岩壁 |

> `06`/`07` 是 (430,200)-(520,290) 区域 4× 放大的离线取景，用于证明 Zircon
> `帧+100/+130` 叠加层确实会在选角屏上画出与角色无关的杂影（即用户看到的
> 「Zircon 动画和闪现」）。

## 复现

```bash
# 离线取景（不连服）
godot-mono --path GodotClient -- --legacy-slot-preview --legacy-create --window=800x600

# 全流程（隔离服务端 /tmp/ei-flow-srv，端口 7001）
godot-mono --path GodotClient -- --server 127.0.0.1 --port 7001 \
  --user test@test.com --pass test123 --stay-select --window=800x600
```

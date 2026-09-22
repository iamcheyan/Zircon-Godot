# 旧版对话框迁移审计

> 事实来源仅限 Mir3 EI 3.0 反编译证据、原版 WIL 资源与
> `Tools/mir3_client_simulator`。Zircon 现有窗口参数不是旧版事实来源。

## 已确认窗口基线

| 窗口 | 原版矩形 | 背景资源 | 已确认内容 |
|---|---:|---|---|
| 人物状态 | 244×328 | GameInter F200 | 装备槽坐标来自 `equipment_slots.json`；F161/162 关闭，F171/172 动作 |
| 背包 | 284×324 | GameInter F250 | 6×6，起点 (25,41)，格距 36；F161/162 关闭；负重/总量文字 |
| 技能书 | 296×332 | GameInter F400 | 8 个系别页签；12 个技能格；旧版窗口 id14 |
| 聊天 | 572×388 | GameInter F350 | 原版聊天历史、频道、滚动和输入区 |
| 任务 | 340×440 | GameInter F700 | 原版任务列表/详情窗口 |
| 系统设置 | 248×264 | GameInter F750 | 音量滑块、开关和确认控件 |
| 组队 | 256×244 | GameInter F900 | 成员列表和组队状态 |

## 绘制规则

- 普通窗口与网页模拟器一致：完整背景帧绘制到窗口 `(0,0)`，并拉伸到证据矩形。
- `DXImageControl.FixedSize` 只固定控件边界，不会缩放贴图；旧版窗口背景必须同时启用 `StretchImage`。
- 交易窗口等有反编译证据的溢出绘制是例外，不能套普通窗口规则。
- 背景正确不代表迁移完成；控件树、格子、文字、快捷键和业务交互必须逐窗核对。

## 验收方式

独立测试场支持直接打开指定窗口，便于自动截图：

```bash
ZIRCON_UI_DATA_PATH=/home/tetsuya/mir3ei/LegacyEI/Data \
godot-mono --path GodotClient res://Scenes/LegacyHudLayoutLab.tscn -- \
  --window --legacy-open=magic
```

可选值：`character`、`inventory`、`magic`、`quest`、`chat`、`group`、`menu`、`belt`。

每个窗口必须完成三层验收：背景完整无裁切、控件坐标与数量一致、点击与快捷键行为一致。

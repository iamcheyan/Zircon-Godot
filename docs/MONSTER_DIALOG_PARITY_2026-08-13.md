# 怪物信息窗口（MonsterDialog）新旧对比与修复（2026-08-13）

## 1. 任务目标与执行摘要

目标：逐项比对旧版 `Client/Scenes/Views/MonsterDialog.cs`（953 行）与新版
`GodotClient/Controls/MonsterDialog.cs`（241 行）的怪物信息展示，补齐缺失的文本
与交互细节。

结论：新版**主体结构已完整移植**（等级/名字/血量/AC/MR/DC/8 抗性/攻击速度/移动
速度/可驯服/亡灵/展开面板），但缺失 3 处细节，本轮全部补齐。

## 2. 新旧逐项对照

| 项目 | 旧版 | 新版（修复前） | 修复 |
|---|---|---|---|
| 等级框 | LevelLabel（黑底金边 31×20） | ✅ | 不变 |
| 名字框 | NameLabel（140×20） | ✅ | 不变 |
| 血量数字 | HealthLabel `cur/max` | ✅ | 不变 |
| 血条纹理 | GameInter 5430 按百分比裁剪 | ✅ | 不变 |
| 攻击元素图标 | AttackIcon 1510-1517 + Hint | ✅（图标） | ✅ 补 Hint |
| 展开按钮 | ExpandButton 44/46 | ✅ | 不变 |
| AC / MR / DC | 文本行 | ✅ | 不变 |
| 8 项抗性 | Fire/Ice/Lightning/Wind/Holy/Dark/Phantom/Physical | ✅（文本） | ✅ |
| **抗性颜色** | **PopulateLabel：0 白 / 正 Lime / 负 IndianRed** | ❌ 全白 | ✅ `ColourResist` |
| 攻击速度图标 | ProgUse 590-596/630 + Hint | ✅（图标） | ✅ 补 Hint |
| 移动速度图标 | ProgUse 620-627 + Hint | ✅（图标） | ✅ 补 Hint |
| 可驯服图标 | 631/632 + Hint | ✅（图标） | ✅ 补 Hint |
| 亡灵图标 | 634/635 + Hint | ✅（图标） | ✅ 补 Hint |
| **成长图标** | **GrowthLevel > 0 显示 ProgUse 630 + Hint** | ❌ 硬编码隐藏 | ✅ `_monster.Stats[Stat.GrowthLevel]` |
| **图标悬停 Hint** | **DXImageControl.Hint（元素/抗性/速度/驯服/亡灵文字）** | ❌ 无 | ✅ TooltipText |

## 3. 修复内容

### 3.1 抗性颜色（对齐旧版 PopulateLabel）

旧版 `PopulateLabel`（MonsterDialog.cs:477-487）：

```csharp
label.Text = $"x{Math.Abs(stats[stat]):0}";
if (stats[stat] == 0)      label.ForeColour = Color.White;
else if (stats[stat] > 0)  label.ForeColour = Color.Lime;
else if (stats[stat] < 0)  label.ForeColour = Color.IndianRed;
```

新版 `ColourResist`：>0 → Lime、<0 → IndianRed 近似、0 → White。
（正数抗性绿、负数抗性红、零白，玩家可一眼看出怪物的克制关系。）

### 3.2 图标悬停 Hint

旧版每个图标带 `Hint`（火/冰/闪电/风/神圣/黑暗/幻影/物理、攻击速度、
移动速度、可驯服/不可驯服、亡灵/生者）。新版控件用 Godot `TooltipText`
承载，Refresh 时按当前状态写入。

新增 `_resistIcons[8]` 引用数组，`AddResistance` 构建时记录图标引用，
避免用子控件索引猜测。

### 3.3 成长图标

旧版 `RefreshStats`：`Monster.GrowthLevel > 0` 时显示 `ProgUse 630` 并给
Hint `MonsterDialogGrowthIconHint`（含成长等级）。新版此前硬编码
`_growthIcon.Visible = false`。

修复：读 `_monster.Stats?[Stat.GrowthLevel]`，>0 显示图标 + Tooltip
「成长等级 N」。数据来自 `S.ObjectStats`（GameScene.OnObjectStats 填充
`objectNode.Stats`），与旧版 `MonsterObject.Stats[Stat.GrowthLevel]` 同源。

### 3.4 悬停信息条可读性

旧版虽然将 `MonsterDialog` 放在逻辑画布顶部居中（`Location = (250, 50)`，
窗口尺寸 `186x54`），但窗口的 `Opacity = 0.3` 不会级联压暗子控件文字；各个
信息面板的半透明只影响背景。Godot 的 `Modulate` 会把父控件和文字一起变暗，
导致怪物信息条在游戏中几乎不可读。

新版保留原版的顶部居中定位和尺寸，将窗口透明度改为 1，改用面板背景颜色的
alpha 表达半透明，并给等级、名称、血量文字增加黑色描边。这样背景仍保持老游戏
的暗色效果，文字不会再被父控件透明度一起压暗。

## 4. 验证

- `dotnet build GodotClient/ZirconClient.csproj --no-incremental`：✅ 0 错误；
- `UITestScene --ui-audit --monster-audit`：✅ `UIMonsterAudit PASS`；
- `AuditLayout`（UITestScene 使用）：控件数 27 不变（引用数组不增控件），
  初始图标索引断言不受影响；
- 建议真机核对：悬停怪物展开面板，抗性正负颜色、图标悬停文字、
  成长怪物（如沃玛教主变体）显示成长图标。

## 5. 相关文件

| 文件 | 说明 |
|---|---|
| `GodotClient/Controls/MonsterDialog.cs` | 本轮修改（ColourResist/图标 Hint/成长图标） |

对照旧版：`Client/Scenes/Views/MonsterDialog.cs`（PopulateLabel/RefreshStats/
各图标 Hint）、`Client/Models/MonsterObject.cs`（GrowthLevel）。

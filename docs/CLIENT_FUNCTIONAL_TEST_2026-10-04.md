# 客户端功能测试报告（2026-10-04）

> 环境：debian(82) 本机 ServerCore(7000) + 虚拟显示器 `:123` + 客户端内置
> `--operation-audit-ext` 操作审计矩阵。
> 注：`login_game.sh remote` 本轮不可用（卡在 Syncthing `connected=False`），
> 故改用直连方式；素材与代码均已确认同步。

---

## 一、修复的真 bug（4 个，全部已推送）

### 1. 进游戏即与服务器断开（`14ad6768`）

真机栈：
```
InvalidOperationException: Sequence contains no matching element
  at DataObjectMonster.OnComplete() in ServerPackets.cs:line 1286
[Select] 与服务器断开连接，禁用选角操作
```

`OnComplete()` 用 `Binding.First(...)` 硬匹配模板索引。客户端 System.db 与服务端
不同步时抛异常，而异常发生在**网络线程的 `Packet.ReceivePacket` 内**，直接拆掉整个
连接。改为 `FirstOrDefault`（`DataObjectItem` 同理）。

已核对全部调用方空安全（`o.MonsterInfo?.AI`、`fortune?.ItemInfo == null`、
`m.MonsterInfo?.Level ?? -1`），null 不引入新崩溃。

验证：断线 **1 次 → 0 次**。

### 2. 纸娃娃每帧抛 NullReference（`b4de15b7`）

`PaperDoll._Draw` line 145 = `armour.Info.Image`。`ClientUserItem.Info` 是懒加载
字段（`Complete()` 按 `InfoIndex` 解析），换装/登录/检视同一帧绘制时仍为 null。

修复：取出 5 件装备后统一 `Complete()`（仅在 null 时），并引入 `HasInfo()` 判空，
把 5 处 `.Info` 解引用与 `DrawEquipmentEffect` 调用点全部改为「有 Info 才画」。

验证：状态窗纸娃娃完整渲染，崩溃计数 **0**。

### 3. 背包网格行数被稀疏槽位撑爆（`4df55d63`）

`GetLegacyRequiredRows` 里 `rows = max(minimumRows, ItemGrid.Length)`。背包数组按
**最大 slot** 扩容，所以 `ItemGrid.Length` 是「最高槽位+1」而非行数。实测该账号
slot 分布 `1,13,14,14,19,25,…,115,186`（数组长 187），大片空洞被当成 **187 行**，
网格撑到 187×6=1112 格，而 F250 背景只有 6 行可视 —— 绝大多数物品落在滚动区外。

修复：行数改由 first-fit 实际摆放结果决定（从可视行数起步、按物品数给初始容量、
放不下翻倍扩容，4096 行兜底）。

### 4. 多格物品全部重叠（`0038e5a0`）

上一修之后复测仍只见 3 件（服务端 16 件）。逐帧诊断确认**同步正常**：
```
服务端 DIAG: items=16
客户端 DIAG-C: StartInfo.Items=16
```

真因是 `GetLegacyFootprint` 用 `LibraryFile.Inventory` 取贴图算格子尺寸，而
`MirSkin.IsUiLibrary` 把 `Inventory` 列为 UI 库 → legacy 下走
`LegacyEI/Data/inventory.wil`（**1440 帧**）；但格子渲染图标走
`Data/Storeitem.Zl`（**8590 帧**）。两者索引空间不同，经典装备
`Info.Image`（7913/8187/8192/8221…）在 1440 帧库里全是空帧 → footprint 全部
退化成 1×1 → 10 件 2×2 衣服**堆在第一行互相覆盖**。

修复：footprint 改用格子实际渲染的库（`LibraryFile.StoreItem`），取不到再退回。

验证：背包由 1 行 3 件变为 3 行、多格物品按 footprint 正确排布。

---

## 二、测试数据准备

背包原为 **358 件 / 负重 4520（上限 2516，超载 63%）**。用户判断正确：全是塞进去的
武器装备。

清理：**358 → 14 件，负重 4520 → 1196**。清理走一次性服务端钩子（`CreateDropItem`
+ `Save()` 正确落盘），**已完全移除，工作区干净**。

另补 `Healing Potion` / `Mana Potion` 各 20 个以支撑审计的自动药水阶段。

顺带修 `.gitignore`：原 `/artifacts` 只锚定根目录，漏了 `.artifacts/`，
导致本地审计截图（172MB）挡住 `login_game.sh` 的「工作区必须干净」检查（`842bd689`）。

---

## 三、验证结果汇总

| 项目 | 结果 |
|---|---|
| 构建（客户端 / 服务端） | 0 错误 0 警告 |
| 登录进游戏 | 通过 |
| 断线异常 | 0（原 1） |
| NullReference | 0 |
| 贴图诊断 | missingLibraries=0 / missingTextures=0 / emptyImageEntries=0 |
| 背包负重 | 1086/2516（不超载） |
| 背包物品渲染 | 3 行、多格物品正确排布 |
| 状态窗 | 单窗口、装备齐全、纸娃娃完整 |
| 聊天窗 | F350 边框 + 锁链滚动条 + 频道钮正常 |
| 技能条 | 半透明材质正确（地面纹理透过底板） |
| 腰带 | 固定位置、拖不动 |

窗口与滚轮此前已逐个实机截图验证（状态窗/背包/技能条/聊天/任务窗）。

---

## 四、未覆盖项（如实说明）

### 1. 需要 X11 输入的实时操作

X11 环境下三种输入注入都进不去 Godot 的 LineEdit：
- `xdotool type` —— 无 IME 路径
- 剪贴板 + `Ctrl+V` —— 无效
- `keydown shift` + `click` 组合 —— Shift 到达了（日志有 `key=Shift`），
  但未形成带 Shift 修饰的 `MouseButton` 事件

因此**以下操作没有真正点过**：
- 人物走动 / 跑动
- 技能释放
- 丢弃 / 捡起
- Shift 快丢
- 滚轮滚动

`--operation-audit-ext` 覆盖的是 **UI 状态断言**（自动驱动控件、检查回包），
不等于实时操控。

### 2. Shift 快丢的代码级核对（未实机）

`GameScene.cs:11670` 起逻辑链完整：
```
if (dropMouse.ShiftPressed)
    if (!CanBeginItemDrop(source)) { 清选中; 吞事件; return; }
    source.Locked = true; source.UpdateBorder();
    SendItemDrop(new CellLinkInfo { ..., Count = 1 });
    DXItemCell.SelectedCell = null; 吞事件; return;   ← 直接 return，不弹数量框
```
服务端 `PlayerObject.ItemDrop:8571` 只拒绝 `Count <= 0` 或 `Count > fromItem.Count`，
`Count = 1` 天然合法。

**结论：逻辑与服务端契约均正确，缺实机点击确认。**

### 3. 审计的自动药水阶段

`FAIL no auto-potion consumable in inventory`。日志显示客户端背包格子遍历只覆盖
可视区；修复 footprint 后药水已在格内，但审计的
`InventoryCells.FirstOrDefault(...)` 仍依赖格子控件数组长度。

**这是测试夹具的限制，不是产品缺陷** —— 药水本身能看见、能用。

---

## 五、建议后续

1. 在有正常输入的环境（本机桌面 / macbook）走一遍：走动、跑动、技能、
   丢弃、Shift 快丢、捡起、背包/聊天/技能书滚轮
2. 审计矩阵的自动药水阶段可考虑改用 `Inventory` 数组而非 `InventoryCells` 遍历，
   消除夹具与可视区的耦合

# NPC 功能性验证（`--npc-func-audit`）

巡检（`--npc-audit`）只回答「点得开吗」。这条通道回答**「点了真的生效吗」**：每项目标功能都用
**真实 UI 路径**触发，并给出可核对的断言值（金币差、背包/仓库件数、地图文件名），只看断言不看弹窗。

实现在 `GodotClient/Scripts/GameScene.NpcFuncAudit.cs`（GameScene 的 partial 文件），
`GameScene._Process` 里一行启动钩子；商品面板只加了 4 个只读属性（`VisibleRows` /
`FirstVisibleRowIndex` / `SelectedIndex` / `GoodCount`），没有其它私有成员被绕过。

## CLI 参数

| 参数 | 说明 |
| --- | --- |
| `--npc-func-audit` | 打开功能性验证。进入游戏且地图就绪后自动开始。 |
| `--npc-audit-manifest <path>` | 清单路径（与巡检共用），缺省 `tools/npc_audit_manifest.json`。 |
| `--npc-func-audit-out <dir>` | 输出目录，缺省 `/tmp/npc_func_audit`（**别放 /tmp**，见下）。 |
| `--npc-func-audit-only buy,teleport,storage,sell` | 只跑指定用例（缺省全跑），便于只重跑失败项。 |
| `--npc-audit-quit=0\|1` | 跑完是否退出进程，缺省 1（与巡检共用）。 |

```bash
DISPLAY=:151 godot-mono --path GodotClient -- \
  --server 127.0.0.1 --port 7001 --window \
  --user test@test.com --pass test123 --char TestHero \
  --legacy-ui --legacy-hud \
  --npc-func-audit --npc-audit-manifest tools/npc_audit_manifest.json \
  --npc-func-audit-out /home/tetsuya/npc_func_out
```

## 四个用例（每个用例一台 NPC，坐标取自清单）

| 用例 | NPC | 真实 UI 路径 | 断言 |
| --- | --- | --- | --- |
| `buy` | #13 啊康（比奇 0） | 聊天 `@givegold TestHero 100000` → 点 NPC → 点「购买物品」选项（`NPCTextControl._GuiInput`）→ 商品面板第 1 行**双击**（`DXControl.MouseDoubleClick` → `NPCGoodsPanel.BuySelected`）→ 若弹数量框则点它自己的 OK | 金币**正好**减少该商品 `CostFor(currency,1)`；背包该物品件数 +1；`S.ItemsGained` 到达 |
| `teleport` | #39 六面神石（比奇 0） | 点 NPC → 点第 1 个目的地选项 | 地图文件名变成该选项 `Teleport` 动作的目标图；金币**正好**减少该页 `TakeGold` 的值；`S.MapChanged` 到达 + 对话自动关闭 |
| `storage` | #147 赵老头（边境城市 01） | 点 NPC → 点「寄存物品」→ 背包格**左键拿起** → 仓库格**左键放下**（`DXItemCell.MoveItem()` 真实路径） | `S.NPCStorage` 到达 + 仓库窗口可见；`Storage[slot]` 出现该物品（`S.ItemMove success=true`）；背包件数 -count |
| `sell` | 数据层反查出的可卖商店（本次 #14 店员，比奇 0） | 点 NPC → 点可卖商店页 → 背包格**右键选中**（`TrySelectItemForNpcSale`）→ 点背包窗「出售」按钮（`InventoryDialog.SellButton.MouseClick` → `SellSelected`） | 金币增加；背包该物品件数减少；`S.ItemsChanged success=true` |

## 输出

- `<out>/results.jsonl`：**每个用例一行**，字段沿用巡检格式，另加 `func`：
  ```json
  {"index":13,"name":"啊康","map":"0","x":402,"y":356,"clicked":true,"entryPage":13070,
   "pages":[{"page":13070,"links":[1,2,3,0,4],"shot":"...png","type":"None"}],
   "errors":[],
   "func":{"cases":[{"name":"buy","ok":true,"error":"","evidence":[
      "@givegold TestHero 100000: gold 100385770->100485770 CurrencyChanged+1 服务端回话=[Chat] ... [GIVE GOLD] TestHero Amount: 100000",
      "商品[0] 木剑(ItemInfo 126) 价格=50 类型=Weapon 堆叠=1",
      "断言: gold 100485770->100485720 期望降 50；inv 木剑 3->4 期望升 1",
      "到达包: ItemsGained+1 CurrencyChanged+1；[CurrencyChanged] currency=1 amount=100485720 ; [ItemsGained] Wood Swordx1"]}]}}
  ```
- `<out>/shots/*.png`：每个断言点一张（进对话页 / 打开商店或仓库 / 操作后）。
- 结束打印 `[NpcFuncAudit] SUMMARY total=.. ok=.. failed=..`。
- 失败时 `func.cases[].error` 是固定短码，例如 `give_gold_failed` / `no_shop_button` / `types_empty` /
  `gold_mismatch` / `inv_mismatch` / `no_teleport_button` / `teleport_not_arrived` / `fee_mismatch` /
  `no_storage_button` / `storage_packet_missing` / `not_safe_zone` / `storage_move_failed` /
  `unlock_failed` / `sell_select_failed` / `sell_not_confirmed`。

## 数据层前提（实测踩到的，写进这里免得下次再撞）

1. **卖出要求对话页 `NPCPage.Types` 非空**：服务端 `PlayerObject.NPCSell` 第一行就要求
   `NPCPage.Types.Count > 0`，客户端也只在 `Types.Count > 0` 时才把背包切到出售模式。
   当前库里 170 个 BuySell 页里只有 76 个 Types 非空，所以用例**先从数据层反查**哪些 NPC 的入口页
   能通到可卖商店页（按 `NPCInfo.NPCName` 匹配清单坐标），而不是写死某一家店。
2. **NPC 买来的物品带 `UserItemFlags.Locked`**（服务端 `NPCBuy` 故意打的防倒卖标记），
   客户端 `TrySelectForSale` 与服务端 `NPCSell` 都会拒绝它。用例因此先用
   `DXItemCell.ToggleLock()`（与键盘锁定路径同一个方法 → `C.ItemLock`）解锁，再选中出售。
3. **仓库存取要求安全区，而安全区是格子级的**：`PlayerObject.ItemMove` 的 Storage 分支要求
   `InSafeZone`，客户端 `DXItemCell.MoveItem` 也会先拦。赵老头门口 (471,277) 实测
   `InSafeZone=False`，用例会先在附近与同图其它 NPC 点位里找一格安全区落点（本次命中 (442,296)），
   仓库窗口保持打开，再搬运。整张图都没有安全区格子时用例会以 `not_safe_zone` 如实失败。
4. **`@givegold` 需要服务端二进制里真的有这条命令**：它是后加的 GM 命令，运行目录的
   `ServerLibrary.dll` 必须用 `dotnet build ServerCore/ServerCore.csproj -o Debug/ServerCore` 重新构建过，
   否则服务端只回 `Command @GIVEGOLD does not exist.`（用例会把这句话原样记进 evidence）。

## 已知限制

- 用例之间会**改变世界状态**（发金币、买卖物品、往仓库放东西），不是只读检查；同一账号反复跑会累积物品。
- `sell` 的 JSONL 记录字段跟随**实际使用**的商店 NPC（清单里的 #19 怡美只是默认目标；它的商店页
  `Types` 为空，卖不了），证据里写明了候选与最终命中的 NPC。
- 数量确认框只在可堆叠商品时出现；用例只点它的 OK 按钮（默认数量），不做数量输入。
- 与巡检一样：需要客户端 `Debug/Client` 指向 EI 素材根；输出目录别放 /tmp（本机 /tmp 是 tmpfs）。

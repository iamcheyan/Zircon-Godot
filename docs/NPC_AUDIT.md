# NPC 实机巡检（`--npc-audit`）

一条通道回答「230 个活动 NPC 里哪些真的能用」：把角色传送到每个 NPC 面前、以**真实点击路径**
唤起对话框、遍历对话框里每个可点链接、逐步截图存档，最后把结果写成 JSONL。

实现在 `GodotClient/Scripts/GameScene.NpcAudit.cs`（GameScene 的 partial 文件），
`GameScene._Process` 里只有一行启动钩子；对话框侧只加了 3 个只读属性
（`NPCDialog.CurrentPageIndex` / `CurrentPageDialogType` / `PageShowCount`）。

## CLI 参数

| 参数 | 说明 |
| --- | --- |
| `--npc-audit` | 打开巡检。进入游戏且地图就绪后自动开始，需要 Admin 账号（`@move` 是 GM 命令）。 |
| `--npc-audit-manifest <path>` | 清单路径，缺省 `tools/npc_audit_manifest.json`。相对路径会按「原样 → `res://` 项目目录 → 项目目录上一级」解析。 |
| `--npc-audit-out <dir>` | 输出目录，缺省 `/tmp/npc_audit`。 |
| `--npc-audit-only 13,14` | 只跑指定 index（缺省全量）。 |
| `--npc-audit-limit N` | 只跑清单前 N 条。 |
| `--npc-audit-quit=0\|1` | 跑完是否退出进程，缺省 1（便于脚本化）。 |

示例（仓库根目录执行；`Debug/Client` 需指向 EI 素材根，见下）：

```bash
DISPLAY=:151 godot-mono --path GodotClient -- \
  --server 127.0.0.1 --port 7000 --window \
  --user test@test.com --pass test123 --char TestHero \
  --legacy-ui --legacy-hud \
  --npc-audit --npc-audit-manifest tools/npc_audit_manifest.json \
  --npc-audit-out /tmp/npc_audit --npc-audit-only 13,90,39
```

## 跑全量的两个坑（实测）

1. **服务端包速率封禁**：`Server.ini` 的 `MaxPacket=50` 表示「一个处理周期内收到的包数上限」，
   超过会**断开连接并把 IP 封 5 分钟**（`SConnection.cs:193`）。巡检在高频点击时可能触发，
   表现为「客户端突然收不到包、后面全部 timeout」。跑全量前把 `MaxPacket` 调到 500
   （`Debug/ServerCore/Server.ini`，该目录 gitignore，仅本机生效），并注意巡检器已加断线守卫。
2. **客户端进程会中途死掉**：用 `tools/run_npc_audit_resumable.sh <outdir> [display] [rounds]`
   驱动——它每轮只跑「还没记录 / 上一轮有错」的 NPC，每轮独立目录（`roundN/`），
   跑完自动汇总。单轮全量约 50 分钟，崩了就再跑一轮。

## 清单格式

```json
{"count":230,"npcs":[
  {"index":13,"name":"啊康","mapFile":"0","mapDesc":"Bichon Town","x":402,"y":356,
   "category":"武器","mud3File":"02Weapon_Bichon1","services":["武器买卖","武器普通修理"],
   "entryPage":5}
]}
```

`mapFile` 是地图 `MapInfo.FileName`（服务端 `@move` 用同一个键），`x/y` 是 NPC 所在格。

## 输出

- `<out>/results.jsonl`：每个 NPC 一行，写完立即 flush（`AutoFlush`），跑的过程中就能 tail。
- `<out>/shots/<index>_<seq>_p<pageIndex>.png`：`seq` 是该 NPC 内已记录页的序号（防重名）。

一行记录：

```json
{"index":13,"name":"啊康","map":"0","x":402,"y":356,"clicked":true,"entryPage":5036,
 "pages":[{"page":5036,"links":[1,2,3,0,4],"shot":"/tmp/npc_audit/shots/13_0_p5036.png","type":"None"},
          {"page":5042,"links":[1,0],"shot":"/tmp/npc_audit/shots/13_4_p5042.png","type":"BuySell"}],
 "errors":[]}
```

- `clicked`：点击是否真的触发了 `C.NPCCall`。
- `entryPage`：**实测**入口页号（服务端第一次回的 `S.NPCResponse.Index`），等于 `pages[0].page`；
  与清单里的 `entryPage` 不一致时会打 `入口页差异: 清单=… 实测=…`（当前清单里的值是对话框重建前的旧页表，属预期）。
- `pages[].links`：该页可点链接的 id（`NPCTextControl.ButtonAreas` 去重后的顺序），`0` 是退出。
- `pages[].type`：该页 `NPCDialogType` 名。
- `errors`：固定短码 `npc_not_found` / `click_missed` / `no_response` / `timeout` / `link_failed`。

结束打印：

```
[NpcAudit] SUMMARY total=3 ok=3 no_response=0 not_found=0 click_missed=0 timeout=0
```

## 每个 NPC 的流程

1. **传送**：聊天发 `@move <mapFile> <x> <y-2>`，必要时退回 `y-3` / `y+2` / `x±2`；
   等待「地图文件名匹配 + 玩家与目标切比雪夫距离 ≤3」再宽限几秒等 NPC 进 `_objects`，总预算 25s（超时记 `timeout`）。
   全程状态轮询（`await ToSignal(GetTree(), ProcessFrame)`），没有固定 sleep。
2. **定位**：在 `_objects` 里找 `Type == ObjectRenderer.Kind.NPC` 且距离最近（≤6 格）的对象，
   同距离优先 `NPCInfo.NPCName` 或对象 `DisplayName` 与清单名字一致者；找不到记 `npc_not_found`。
3. **唤起对话**（与 `StartInteractionAudit` 同款路径）：
   `PickObjectAtCellForAudit(cell)` → `MouseObject = hit` → `_UnhandledInput(左键按下)` → `_UnhandledInput(左键抬起)`；
   点击前会等 `TrySendNpcCall` 的 1s 节流过去。未触发 `C.NPCCall` 记 `click_missed`。
4. **等响应**：`_npcDialog` 可见、`_npcObjectId` 是目标 NPC，且 `ShowPage` 次数超过点击前的计数（避免上一轮的页被当成新响应）；
   5s 未见新页记 `no_response`（数据层就没有入口页的 NPC 属预期观察结果）。
5. **截图**：`GetViewport().GetTexture().GetImage().SavePng(...)`，截前强制刷新玩家/物体屏幕坐标（同 HexaAudit）。
6. **遍历链接**：DFS，深度 ≤4、按页 `Index` 去重；点击走 `NPCTextControl._GuiInput(new InputEventMouseButton{Position=选项中心,…})`
   ——与鼠标点在选项上是同一个方法。探索兄弟分支前会关掉对话、重新点开 NPC 并**重放路径**回到当前页。
7. **关闭**：`CloseNPCDialog()`（等价于点 id=0 的退出链接，内部会发 `C.NPCClose`），再进下一个 NPC。

## 已知限制

- `entryPage` 记实测值；清单里的 `entryPage` 是对话框重建前的旧页表值，只作期望值参考。
- 点击链接后 5s 内没有新 `S.NPCResponse` 一律记 `link_failed`；打开非对话类子窗口（例如需要物品/货币的交互页）
  也可能落在这里，属于「该链接在本巡检的点击语义下没有产生新对话页」。
- 未做登录重试：若账号已被别的客户端占用，服务端回 `AlreadyLoggedIn`，客户端会停在登录屏（本次运行不产出结果）。
- 运行需要客户端的 `Debug/Client` 指向 EI 素材根（`Debug/` 已在 `.gitignore`，本地产物不入库），
  否则 map 加载抛异常、`_mapView.Map` 始终为 null，巡检钩子不会触发。

## 隔离服务端跑法（不与主服务端抢账号）

巡检要登录 Admin 账号；主服务端上该账号被别的客户端占用时，服务端回 `AlreadyLoggedIn`，
客户端会**静默**停在登录屏（只看到 `[Login] Login 包已入发送队列`，没有任何后续日志）。
需要并行跑时可以在 worktree 里起一份隔离服务端，端口/数据库都独立：

```bash
# 1. 构建到本 worktree 自己的运行目录（Debug/ 已 gitignore，不入库）
dotnet build ServerCore/ServerCore.csproj -o Debug/ServerCore

# 2. 运行目录：小文件直接拷，Map/Sound 软链到素材根（只读）
mkdir -p Debug/ServerCore/Database
cp -r $MAIN/Debug/ServerCore/{Config,Translations,chinese_alias.json} Debug/ServerCore/
cp $MIR3_EI_ROOT/Data/System.db Debug/ServerCore/Database/System.db   # 必须与客户端读的那份逐字节相同
cp $MAIN/Debug/ServerCore/Database/Users.db Debug/ServerCore/Database/Users.db
ln -s $MIR3_EI_ROOT/Map  Debug/ServerCore/Map
ln -s $MIR3_EI_ROOT/Sound Debug/ServerCore/Sound

# 3. Server.ini（UTF-16）里 Port=7001、UserCountPort=3001 —— 两个监听端口都要避开主服务端
# 4. 起服务端，然后客户端加 --port 7001
cd Debug/ServerCore && dotnet ServerCore.dll &
```


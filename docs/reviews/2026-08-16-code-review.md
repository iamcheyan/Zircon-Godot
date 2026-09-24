# Code Review — 2026-08-16（独立评审员报告）

- **范围**: `git log --since=2026-08-14` 至 90fd8e2 的 8 个提交（评审期间仓库追加 534b7cb「zreview 任务书」docs 提交，不改变评审范围）。
  - 8d1a6a3 光照特殊态（dead/abyss）· ceb903e 地图差异 docs · 0dc1321 E4/P3 特效表对齐 · 3a501d3 E5/A ClientData 落位 · c6bda50 E5/B1 快照工具 · 11fb0f2 E5/B2-B3 DataLayer · 3534c41 E5/B4 cutover · 90fd8e2 ClientData README。
- **方法**: 只读评审（`git show/log` + 隔离 worktree `/tmp/review-90fd8e2`，已清理）。diff 共 ~1100 行代码 + ~34.9k 行 JSON 数据，代码全量逐行过，JSON 用脚本统计与抽验。**行为验证（本人独立复跑）**：
  1. worktree `dotnet build --no-incremental` → 0 错误；
  2. headless `--table-snapshot` 导出 HEAD 运行时表 → 与 e5-proof `snapshot-before.json`（改造前硬编码表）**逐字节全等**（独立 Python 深比较）；
  3. `gen_cs_table.py --check`（文件层 148 技能/白名单 136/违规 0）与 `frameformulas.py --check` 全绿；
  4. headless `--magic-spot-audit` → `[MagicSpotAudit] PASS 5/5`（E4/P3 声明复现；输出中的 wav 缺失报错仅因 worktree 无未跟踪的 Debug 音频资源，与审计结果无关）。
  光照渲染审计（需 GPU 视口）未复跑，改为对照原版源码逐常量核验（见 Strengths #6）。

---

### Strengths

1. **等价性证明方法论扎实且可复现** — `TableSnapshotTool.cs:20-36,105-110,128-166` 用纯反射读取最终表状态，稳定序列化（键排序/枚举转字符串/TimeSpan→ms/Color 6 位小数），与 `DataLayer` 的解析逻辑**零共享**。before（C# 初始化器语义）vs after（JSON 解析器语义）全等因此是两条独立生产线收敛的证明，不构成「验证工具复用生产逻辑」的自洽掩盖。本人复算 e5-proof 三份快照两两 `==`，并重导 HEAD 快照确认仍全等。
2. **cutover 是真正的干净切换** — 四张表类保留 API 壳（`MagicEffectTable.cs:19-27,182-198`、`SoundCatalog.cs:14-19`、`MagicSoundCatalog.cs:29-79`、`MonsterSoundCatalog.cs:9-18`），全部消费者零改动（`GameScene.cs:3060,3217,3306`、`ObjectRenderer.cs:205-219`、`SoundPlayback.cs:16`）；编译 0 错 + 运行时快照全等 + 覆盖审计（`castConfigured=146 missingOriginalSpell=0`，e5-proof/audit-after-cutover.log:29）三重证据。未发现遗漏消费者。
3. **E4/P3 修正可溯源且已复现** — 0dc1321 的 5 处修正（AdamantineFireBall 1640/1800、ImprovedExplosiveTalisman Source 库、Summon 系 740x10@60ms、5 条补齐）均锚定 Mir3-Research 事实源；`OriginalSpellCases` 从原 case 全集重建（136+2 NoVisual）。`MapTestScene.cs:1707-1714` 抽测做表定义+运行时双层断言，期望三元组独立手抄自事实源而非读被测表——本人 headless 复跑 PASS 5/5。
4. **光照提交常量对照原版逐一核验通过** — `MapLightLayer.cs:19` DeadTint=(205,92,92) ≡ 原版 `Client/Scenes/Views/MapControl.cs:1624` IndianRed Clear；`AbyssGlowRadius()=256×(0.1+4×0.02)=46.08` ≡ 原版 `:1654` `BaseLightSize+4*LightScale`；死亡先于深渊、白天强制渲染 ≡ 原版 `OnClearTexture:1622-1626` 顺序与 `ShouldRenderLightLayer:1791-1792`；坐标约定 `Position+(24,0)` 与既有 `GetObjectLightSources`（`GameScene.cs:5072-5096`）一致。
5. **诚实的审计修正文化** — `MapTestScene.cs:74` 探针区从 (700,350) 移入视口并注释根因（逻辑 512x384 外采样被钳位）；8d1a6a3 commit message 完整披露 night 0.096 险败成因。这正是仓库「行为验证≥编译验证」铁律的正确执行方式。
6. **顺手修正生成器漂移** — 11fb0f2 再生成 `frame-formulas.json` 时删除 SDMob19/21/22/23 的 phantom hide/show 条目，与 `LibraryCore/FrameSet.cs:717-718,732-733,748-749,765-766` 中被注释的死条目对齐（旧 JSON 曾把注释当活数据供给 webport）。`frameformulas.py --check` 现绿。
7. **文档质量** — `ClientData/README.md` 数据流向/门禁命令/溯源/「单向镜像」定性准确；`_meta` provenance 与 e5-proof 存证目录（本人验证三快照全等）齐备。
8. **快照导出入口设计** — `MapTestScene.cs:203-215` 将导出放在地图加载 try/catch 之外、导出即 Quit，headless 友好且不被资源缺失阻断。

### Issues

#### Critical (Must Fix)

无。未发现数据丢失、消费者遗漏或可复现的功能回归；核心链路（编译→装载→快照全等→审计）均本人独立复验通过。

#### Important (Should Fix)

1. **容错承诺与实现不符：sounds/frame 路径坏条目会中断启动级联**
   - File:line: `GodotClient/Scripts/DataLayer.cs:387-427`（LoadSounds 全程无 per-entry try/catch，`:390,392-393,403,406,409-410,423-426` 裸 `Enum.Parse`）；`DataLayer.cs:99-123`（LoadFrameFormulas 的 `:109-112` `GetProperty`/`:119` `int.Parse` 同样无保护）；`DataLayer.cs:217-225`（FillWhitelist 裸 `Enum.Parse`）；对照承诺 `DataLayer.cs:26-27`「坏条目 GD.PrintErr 并跳过 (不崩客户端)」与 `ClientData/README.md:48-55`（明确引导用户手改 sounds.json，键拼错即触发）。
   - What: 任一 sounds.json 键名拼错/缺属性 → 异常逃出 `LoadAll` → `NetworkManager._Ready`（`NetworkManager.cs:22-24`）中止，**`DatabaseLoader.Load()` 被跳过**，客户端带半装载表+缺登录库继续跑。magic-effects 路径（`11fb0f2` 引入的 per-skill try/catch，`DataLayer.cs:178-187`）有保护，另两条 loader 没有。
   - Why it matters: README 把手改 sounds.json 列为标准工作流，一次拼写错误即产生难以定位的连锁半初始化；也直接违背代码自述与文档的容错契约。
   - How to fix: 给 LoadSounds/LoadFrameFormulas/FillWhitelist 加与 LoadMagicEffects 同款的 per-entry try/catch（PrintErr+skip+计数）；另在 `LoadAll` 或 `NetworkManager._Ready` 加顶层 try/catch 兜底，保证 `DatabaseLoader.Load()` 不被数据层异常阻断。
2. **导出包数据链路断言失实：`res://ClientData` 不存在， ClientData 不在导出内**
   - File:line: `GodotClient/Scripts/DataLayer.cs:52-54`（注释「res://ClientData (export presets 已 include)」）；`GodotClient/export_presets.cfg:9-10`（`export_filter="all_resources"` 仅打包 res:// 即 GodotClient/ 子树）；实际目录 `ClientData/` 位于仓库根（res:// 之外）；仓库内无任何部署脚本/文档提及拷贝 ClientData（`docs/REMOTE_SERVER_AND_CLIENT_SETUP.md` 无命中）。
   - What: 导出二进制（`deploy-out/Mir2eiClient`，内嵌 pck）在原位恰好靠第 3 级回退（exe 目录 `../ClientData`）命中 `zircon/ClientData` 而工作；一旦二进制被移动/远程部署（如 82 机器）而无同级 ClientData，magic-effects+三张音效表全空（FrameSet 因 `LibraryCore/FrameSet.cs` 仍硬编码而幸存），仅一条 PrintErr。
   - Why it matters: 注释给出了错误的正确性依据（「已 include」），未来读者会据此把目录保持在工作区根部并放心分发导出包；离机部署即静默失去全部技能特效与音效。
   - How to fix: 二选一并落实：① 把 `ClientData/` 迁入 `GodotClient/`（res:// 内，all_resources 自然打包，`ResolveClientDataDir` 第 2 级真正生效）；② 保留现布局但修正注释为「依赖 exe 同级 `../ClientData`」，并在部署脚本/文档中显式加入 ClientData 拷贝步骤。导出后做一次离机启动冒烟验证 `[DataLayer] OK`。

#### Minor (Nice to Have)

1. `DataLayer.cs:36-41,65-67` — 三文件全缺时仍 `Loaded=true`，之后重入直接 return，无重试也无对外可查询的失败态（只有 PrintErr）。建议暴露 `LoadErrors` 之类的状态供启动 UI/诊断使用。
2. `DataLayer.cs:281-284,354-361` — OffsetImpactDef 靠「是否含 offsetX/offsetY 键」猜类型，`ParseOffsetImpactList` 盲强转；additionalMapEffects 条目缺两键时以 InvalidCastException 丢弃整个技能（被 per-skill catch 吞成一行 PrintErr），排障信息不直观。建议 cast 失败时打印「缺 offsetX/offsetY」的针对性消息，或 JSON 侧用显式类型标记。
3. `DataLayer.cs:130-144` — `CamelToKey`/`TryParseAnim` 对空串/下划线开头名字会越界或错映；当前数据安全，属防御性短板，加一行 guard 即可。
4. `11fb0f2` commit message 未提 frame-formulas.json 的再生成与 SDMob19/21/22/23 phantom 条目删除——这是对 webport 消费者可见的行为修正，值得单独一条 bullet（提交内容本身正确）。
5. `MapLightLayer.cs:95-98` — `SetDayTime` 去掉了 `Math.Clamp`。行为中性（`AmbientFor:127-134` 内部仍钳制，`_dayTime` 无其他用途），但删钳制的原因未写入注释/commit message，未来易被「好心」恢复或误读为行为变更。
6. `GameScene.cs:8123-8134` — Abyss 环绕特效用固定 0.98s 间隔近似原版「每次光照层重建重画」；注释已自陈差异，属可接受近似，仅记录 parity 偏差备查。
7. `Tools/magiclab/gen_cs_table.py:189-236`（Mir3-Research 侧）— `check_runtime` 只对账 magicEffectTable 段；sounds/frameSet 的运行时↔JSON 全等只有 e5-proof 一次性存证，无常驻门禁。建议扩展该门禁覆盖五段（见 R3）。
8. `TableSnapshotTool.cs:134-135` — float 经 `Math.Round` 转 double 的中点舍入（banker's rounding）对自比较无碍，但若将来与异构工具比对需约定同款舍入。

### Recommendations

1. **R1（配 Important#1）**: 三条 loader 统一 per-entry 容错 + `LoadAll` 顶层兜底，并补一个坏 JSON 的启动冒烟用例（坏键 → PrintErr 但 `[DataLayer] sounds:` 计数正常、登录库照常加载）。
2. **R2（配 Important#2）**: 落实 ClientData 导出方案（迁入 res:// 或部署脚本+注释修正），并在导出包上做一次离机 `[DataLayer] OK` 验证。
3. **R3**: 把 sounds/frameSet 的运行时等价性纳入常驻 CI 门禁（扩展现有 `--table-snapshot` 比对到五段全量），不再依赖一次性 e5-proof 存证。
4. **R4（中期）**: 用四张表类上的显式 `LoadFrom(JsonElement)` API 取代对私有静态字段的反射写入（`DataLayer.cs:162-164,191-193,219-221,384-399,417-418`）——反射把私有字段名变成隐式契约，重命名只在运行时炸（虽有 `!` 早炸兜底）。
5. **R5**: 为 sounds.json 加 schema/枚举键 lint（提交期拦截拼错），把手改工作流的错误前移到 CI。

### Assessment

**Ready to merge?** With fixes

**Reasoning:** 核心交付（数据层 cutover + 特效表对齐）经本人独立复验为全等、可复现且消费者零遗漏，质量与验证纪律均属上乘；但两条 Important 属真实缺口——容错契约在 sounds/frame 路径未实现（手改工作流直接踩雷）、导出包 ClientData 链路的注释断言失实（离机部署静默失表）——修完即可合并。

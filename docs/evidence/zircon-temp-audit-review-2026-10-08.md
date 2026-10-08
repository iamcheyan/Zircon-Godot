---
title: Temporary Zircon audit assets — review record
description: Reviewed temporary audit resources; records selected evidence, provenance, and exclusions.
---

# Zircon 临时审计素材复核（2026-10-08）

## 收录结果

- **旧版 UI 资源帧**：已验证从 `LegacyEI/Data/Interface1c.wil` 的 F50，以及 `GameInter.wil` 的 F1000、F1002、F1003 精确解码出 PNG，归档在 [`legacy-ei-ui/resource-frames-2026-10-08/`](legacy-ei-ui/resource-frames-2026-10-08/README.md)。这些帧对应仓库现有旧版 UI 审计中的背景/商店状态资源。
- **六面神石过程截图和 NPC56 对齐试验图**：归档在 [`hexastones/process-review-2026-10-08/`](../screenshots/hexastones/process-review-2026-10-08/README.md)。它们是补充过程材料；最终验收仍以仓库已跟踪的 36 张点位实机截图及对应审计报告为准。

## 已复核但未重复收录

- `GameInter.wil` F1001 已有仓库证据图 `legacy-ei-ui/gameinter-frame-1001-wil-2026-09-24.png`；与临时提取版尺寸及可见像素逐点一致，不重复复制。
- 临时 Guard 修复截图中两张与 `docs/screenshots/guards/07_bichon_guards_after_coordinate_fix.png`、`08_mongchon_archer_after_coordinate_fix.png` 的 SHA-256 完全相同，不重复收录。
- 旧 `zircon-ui-acceptance-2026-09-24` 截图已有对应的 UI 审计/证据，不作为本次新素材重复提交。

## 未收录与原因

- `zircon-system-audit.json` 及 `zircon-system-audit-after.json` 是完整多表数据库 JSON 导出；它们体积大、包含远超此项审计所需的数据。仓库已有的六面神石审计报告记录了必要的数据库版本、哈希和 36 点验证结果，因此不把全库快照公开进 Git。
- `zircon-runtime-audit-*`、`zircon-game-audit.png` 是一般游戏画面/市场交互状态，没有显示本次目标对象的新增结论；黑屏的 `in_game_stone*.png` 无法作为视觉证据。
- `NPCface` 单帧、`npc_faces.json`、`npc56` 的动画帧和地图 tile 原始拆分图，与仍在工作区的 NPC 头像映射实验有关；因映射依据/生成步骤尚未成为已审定的最终结论，本次不将它们包装成已完成审计证据。只保留带有“探索性”标注的 NPC56 对齐比较图。

本次只新增已挑选的证据文件和说明；没有删除或修改 `/tmp` 中的任何原始材料，也没有暂存或提交其它未跟踪的代码、数据库备份、截图目录或工作文件。
# 服务端 NPC 生成实机证据 (2026-10-04)

## 服务器启动后各图 NPC 生成数 (来自 SEnvir FinaliseMapLoad 运行时统计)

```
[2026年10月4日星期日 04:04:27]: [NPC-DIAG] Bichon Town [0] regions=114 npcs=16
[2026年10月4日星期日 04:04:27]: [NPC-DIAG] Banya Village [2] regions=55 npcs=13
[2026年10月4日星期日 04:04:27]: [NPC-DIAG] Lost Paradise [1] regions=116 npcs=12
```

## 与写库后 System.db 的一致性

| 地图 | 服务端生成 NPC | System.db 中该图 NPC | 一致 |
|---|---|---|---|
| Bichon Town [0] | 16 | 16 | ✅ |
| Banya Village [2] | 13 | 13 | ✅ |
| Lost Paradise [1] | 12 | 12 | ✅ |

## 软停用验证

- System.db 中 76 个多余 NPC 的 Region = null, 服务端 SEnvir.cs:927 `if (info.Region == null) continue;` 静默跳过, 游戏内不生成。
- 启动日志中 `[NPC] Bad Map` / `Failed to spawn NPC` 计数 = 0。
- NPCInfo 294 / NPCPage 304 / NPCAction 288 / NPCCheck 430 行数迁移前后完全不变 (零物理删除)。

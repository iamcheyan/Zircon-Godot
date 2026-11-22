# 255 级测试角色无技能/物品：初始化恢复

## 现象与原因

2026-11-22 TrimUI TG5040 上进入的是 `TestHero`，等级 255、HP 13000、MP 1490，
但 `StartGame` 中技能数 0，背包物品也为 0。这不是选到了另一名低等级角色。

`ServerLibrary/Envir/DevSinglePlayer.cs` 原先先设置等级，再使用裸 `new UserMagic`
设置 `Info`。MirDB 对象没有所属集合，`OnChanged` 访问 `Collection.Session` 时
抛 `NullReferenceException`，剩余技能、物品和金币初始化中断。

下次登录又仅凭 `Level >= 255` 判断初始化已完成，永久跳过剩余项目。

## 修正

- 技能通过 `SEnvir.UserMagicList.CreateNewObject()` 创建，`Character` 关联自动
  加入角色列表，不再额外 `Add`。
- 等级、技能、物品分别检查/补缺；不再把等级当整套初始化的完成标志。
- 既有技能的快捷键和经验保留；已存在的运行时魔法不重复注册。
- 保留开发账号跨职业技能测试口径，但不启用已关闭的修炼系统。
- 物品定义已存在于角色背包、已穿戴或账号仓库时不重复发放；金币只补到下限，
  不把更高余额改低。
- 生效条件仍为显式 `--singleplayer-dev` 且账号为原测试账号；普通启动、
  非测试账号均不改变。

本修复不自动穿装备、不替换角色、不重建 Users.db，也不提升账号管理员权限。
全技能由测试模式赋予，并不意味着绕过其他玩法或装备使用规则。

## 验证

运行 [SinglePlayerDevChecks](../Tests/SinglePlayerDevChecks/README.md)：合成残缺角色、
重复登录、部分失败后补缺、正常账号隔离、新角色以及 MirDB 存盘重载。

设备存档副本在隔离目录中检查得到：255 级、0 技能、0 物品 → 61 技能、431 物品。
再次登录新增均为 0；重载和实际 StartInformation 投影数量一致。此数量来自当时
System.db，其他库可能不同，不应写死到实现或 UI。

## 安全应用

1. 正常结束客户端/服务端，确认进程停止。绝不直接改运行中的数据库。
2. 在卡上与主机各保存一份 Users.db、System.db、旧 ServerLibrary.dll 及哈希。
3. 使用与设备已有 LibraryCore.dll 兼容的修复 ServerLibrary.dll；不必替换客户端、
   System.db 或未完成的掌机 UI/按键。
4. 登录原 TestHero，让服务端在初始化路径补缺。
5. 核对服务端“补齐完成”日志与客户端 StartGame/等级/技能/物品日志，然后重新登录
   验证没有重复增加。保留备份，以便回滚。

只恢复旧 DLL 不会撤回已写入的技能/物品；如需完整回滚，必须在服务停止后连同
同一时点的 Users.db 一起还原。不要将数据库副本或含个人数据的备份提交到 Git。

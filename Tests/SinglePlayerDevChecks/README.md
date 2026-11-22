# SinglePlayerDevChecks

针对 `DevSinglePlayer` 的服务端回归检查，用真实 MirDB、`PlayerObject` 初始化、
技能运行时注册、物品发放及存盘/重载。无需 Godot、掌机、监听端口或账号登录。
所有测试数据库均在随机临时目录，退出时删除；不操作运行中的存档。

```sh
# 仓库根目录，需要 .NET 10
 dotnet run --project Tests/SinglePlayerDevChecks/SinglePlayerDevChecks.csproj -c Release
```

覆盖：

- 未启用 `--singleplayer-dev` 时不注入。
- 255 级但技能/物品为空的残缺角色可以补齐。
- 技能使用集合工厂，持久化身份、角色反向关联、运行时魔法均有效。
- 二次登录不重复增加技能/物品；既有快捷键、经验与超过测试下限的金币保留。
- 存盘重载后的角色和快捷键稳定；中途已发出部分内容时可以继续补缺。
- 非测试账号不受影响；新的低等级测试角色仍能完整初始化。
- 检查注入方法内部捕获的异常，不能把“没有抛出到调用方”当成功。

## 在已复制的数据库上验证

```sh
dotnet run --project Tests/SinglePlayerDevChecks/SinglePlayerDevChecks.csproj -c Release -- \
  --database-copy /absolute/path/to/offline-copy
```

目录必须含 `System.db` 和 `Users.db`。程序再次复制到随机临时目录后才打开；
传入的两个文件只读取，并用 SHA-256 校验未改动。要求副本内有测试账号的
`TestHero`。请先正常停止服务并备份，**不要将运行中数据库目录当输入**。

该模式还检查实际 `StartInformation` 是否含等级、技能和物品，避免只修了
数据库却没有出现在进入游戏包中。此检查不建立真实地图或网络连接；它不证明
设备 CLR、实际发包、客户端解析、画面或技能施放已经通过。

连接对象是测试用的断开网络构造替身，仅作为出包队列；业务、MirDB、序列化
投影与存盘代码仍来自正式项目。不要把替身用于正式服务。

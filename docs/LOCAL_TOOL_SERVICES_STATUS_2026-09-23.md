# 本机工具服务状态（2026-09-23）

本记录汇总 2026-09-23 对本机开发工具服务的检查结果，方便之后直接打开相关页面。

> 这是检查时点的运行快照，不代表服务会一直保持启动。网页服务通过本机 HTTP 探测，列出的页面返回 HTTP 200。

## 正在运行的网页服务

| 服务 | 地址 | 运行目录/数据位置 | 检查结果 |
|---|---|---|---|
| 素材帧预览器（wilviewer） | [http://localhost:8765](http://localhost:8765) | 服务脚本：`/home/tetsuya/development/Zircon/Tools/web/wilviewer.py`；素材根目录：`/home/tetsuya/mir3ei` | 监听中，HTTP 200 |
| 地图查看器（mapviewer） | [http://localhost:8899](http://localhost:8899) | 脚本：`/home/tetsuya/development/Mir3-Research/Tools/maps/mapviewer.py`；地图：`/home/tetsuya/mir3ei/Map`；数据：`/home/tetsuya/mir3ei/Data` | 监听中，HTTP 200 |
| 素材浏览（webres） | [http://localhost:8821](http://localhost:8821) | `/home/tetsuya/development/Mir3-Research/Tools/webres` | 监听中，HTTP 200 |
| UI 编辑器（uieditor） | [http://localhost:8820](http://localhost:8820) | `/home/tetsuya/development/Mir3-Research/Tools/uieditor` | 监听中，HTTP 200 |
| 静态地图测试台（webclient） | [http://localhost:8822](http://localhost:8822) | `/home/tetsuya/development/Mir3-Research/Tools/webclient` | 监听中，HTTP 200 |
| 网页客户端（webport） | [http://localhost:8823](http://localhost:8823) | `/home/tetsuya/development/Mir3-Research/Tools/webport` | 监听中，HTTP 200 |
| 数据库编辑器（dbeditor） | [http://localhost:8810](http://localhost:8810) | `/home/tetsuya/development/Mir3-Research/Tools/dbeditor` | 监听中，HTTP 200 |
| 开发 Portal | [http://localhost:8840](http://localhost:8840) | 服务脚本：`/home/tetsuya/development/Zircon/Tools/portal/portal.py` | 监听中，HTTP 200 |

## 其他监听服务

| 服务 | 地址/端口 | 检查结果 |
|---|---|---|
| Zircon 本地游戏服（ServerCore） | `127.0.0.1:7000` | TCP 监听中 |
| WebSocket 网关（wsgateway） | `localhost:7001` | TCP 监听中；HTTP 探测返回 426（符合 WebSocket 升级入口的表现） |

## 当时未启动

以下端口在检查时没有进程监听：

- `8800`（dbviewer）
- `8830`（yomu）
- `8831`（fudoki）

## 检查方法

使用 `ss -ltnp` 查看监听端口与进程，并通过 `curl` 对网页端口发起本机 HTTP 探测。游戏服和网关端口使用 TCP 监听状态判断。

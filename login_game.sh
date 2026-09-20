#!/usr/bin/env bash
# Zircon 游戏一键登录脚本
# 功能：1) 杀游戏进程 2) 构建 3) 启动服务器 4) 启动客户端登录
# 用法：
#   bash login_game.sh        # 默认：只杀客户端，服务器若在跑则直接连（不重启）
#   bash login_game.sh all    # 连服务器一起杀并重启（服务器代码有更新时用）
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="$ROOT/../Debug/ServerCore"
[ -d "$SERVER_DIR" ] || SERVER_DIR="$ROOT/Debug/ServerCore"
SERVER_LOG="/tmp/servercore_login.log"

# 端口配置：macOS ControlCenter 占 7000 时自动使用 7001
PORT=7000
if [ -f "$SERVER_DIR/Server.ini" ]; then
    INI_PORT=$(grep -E '^[[:space:]]*Port[[:space:]]*=' "$SERVER_DIR/Server.ini" | head -n1 | cut -d'=' -f2 | tr -d '\r\n[:space:]')
    if [ -n "$INI_PORT" ]; then PORT="$INI_PORT"; fi
elif [ "$(uname)" = "Darwin" ]; then
    PORT=7001
fi
KILL_ALL=0
if [ "${1:-}" = "all" ]; then
    KILL_ALL=1
fi

cd "$ROOT"

echo "══════════════════════════════════════"
echo "  Zircon 游戏一键登录"
if [ "$KILL_ALL" = "1" ]; then
    echo "  模式: all（杀服务器+客户端，重启服务器）"
else
    echo "  模式: 快速（只杀客户端，服务器在跑则直接连）"
fi
echo "══════════════════════════════════════"

# ---------- 1. 强制杀掉游戏相关进程 ----------
echo ""
echo "[1/4] 清理游戏进程..."

# 杀掉 Godot 客户端
CLIENT_PIDS=$(pgrep -f '[g]odot-mono.*ZirconClient' || true)
if [ -n "$CLIENT_PIDS" ]; then
    echo "  杀掉 Godot 客户端: $CLIENT_PIDS"
    kill -TERM $CLIENT_PIDS 2>/dev/null || true
else
    echo "  无客户端进程，跳过"
fi

# 杀掉服务器（仅 all 模式）
if [ "$KILL_ALL" = "1" ]; then
    SERVER_PIDS=$(pgrep -f '[d]otnet .*ServerCore(/|/ServerCore\.dll)|[d]otnet ServerCore\.dll' || true)
    if [ -n "$SERVER_PIDS" ]; then
        echo "  杀掉服务器: $SERVER_PIDS"
        kill -TERM $SERVER_PIDS 2>/dev/null || true
    else
        echo "  无服务器进程，跳过"
    fi
else
    if ss -H -ltn 2>/dev/null | awk '$4 ~ /:7000$/ { found=1 } END { exit(found ? 0 : 1) }'; then
        echo "  服务器已在运行 (端口 7000)，保留不重启"
    else
        echo "  服务器未运行，稍后由脚本启动"
    fi
fi

# 等待进程正常退出；只有残留时才强制结束
sleep 2

# 确认清理干净（all 模式含服务器）
if [ "$KILL_ALL" = "1" ]; then
    REMAIN=$(pgrep -f '[d]otnet .*ServerCore(/|/ServerCore\.dll)|[d]otnet ServerCore\.dll|[g]odot-mono.*ZirconClient' || true)
else
    REMAIN=$(pgrep -f '[g]odot-mono.*ZirconClient' || true)
fi
if [ -n "$REMAIN" ]; then
    echo "  ⚠️ 残留进程: $REMAIN，再杀一次"
    kill -KILL $REMAIN 2>/dev/null || true
    sleep 2
fi
echo "  ✓ 进程清理完成"

# ---------- 2. 构建服务端与客户端 ----------
echo ""
echo "[2/4] 构建服务端与客户端..."
SERVER_BUILD_LOG=/tmp/zircon_server_build.log
# 服务端的运行目录同时包含 Database/、Map/ 和 Server.ini；覆盖输出目录，
# 避免启动时 AppDomain 基目录指向另一个没有数据库的 Debug 目录。
if dotnet build ServerCore/ServerCore.csproj --no-restore -o "$SERVER_DIR" >"$SERVER_BUILD_LOG" 2>&1; then
    tail -3 "$SERVER_BUILD_LOG"
else
    cat "$SERVER_BUILD_LOG"
    echo "服务端构建失败，停止启动。"
    exit 1
fi

BUILD_LOG=/tmp/zircon_client_build.log
if dotnet build GodotClient/ZirconClient.csproj --no-restore >"$BUILD_LOG" 2>&1; then
    tail -3 "$BUILD_LOG"
else
    cat "$BUILD_LOG"
    echo "客户端构建失败，停止启动。"
    exit 1
fi

# ---------- 3. 启动服务器 ----------
echo ""
echo "[3/4] 启动服务器..."

# 默认模式: 服务器已在跑则跳过; all 模式: 总是重启
PORT_OPEN=0
if nc -z 127.0.0.1 "$PORT" 2>/dev/null; then
    PORT_OPEN=1
fi
if [ "$KILL_ALL" = "0" ] && [ "$PORT_OPEN" = "1" ]; then
    echo "  服务器已在运行 (端口 $PORT 监听中)，跳过启动"
else
    cd "$SERVER_DIR"
    setsid nohup dotnet ServerCore.dll > "$SERVER_LOG" 2>&1 < /dev/null &
    SERVER_PID=$!
    echo "  服务器 PID: $SERVER_PID"

    # 等待服务器就绪
    echo "  等待服务器就绪 (端口 $PORT)..."
    for i in $(seq 1 30); do
        if nc -z 127.0.0.1 "$PORT" 2>/dev/null; then
            echo "  ✓ 服务器已就绪 (端口 $PORT 监听中)"
            break
        fi
        sleep 1
        if [ "$i" -eq 30 ]; then
            echo "  ⚠️ 服务器 30 秒未就绪，查看日志:"
            tail -20 "$SERVER_LOG"
            exit 1
        fi
    done
fi

# ---------- 4. 启动客户端 ----------
echo ""
echo "[4/4] 启动客户端登录 (端口 $PORT)..."
godot-mono --path "$ROOT/GodotClient" -- --server 127.0.0.1 --port "$PORT" --user test@test.com --pass test123 --char TestHero --window
echo ""
echo "══════════════════════════════════════"
echo "  游戏已启动"
echo "══════════════════════════════════════"

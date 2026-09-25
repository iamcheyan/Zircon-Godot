#!/usr/bin/env bash
# Zircon 游戏一键登录脚本
# 功能：1) 杀游戏进程 2) 构建 3) 启动服务器 4) 启动客户端登录
# 用法：
#   bash login_game.sh        # 默认：只杀客户端，服务器若在跑则直接连（不重启）
#   bash login_game.sh all    # 连服务器一起杀并重启（服务器代码有更新时用）
#   bash login_game.sh legacy # 使用旧版 EI HUD 登录（不重启服务器）
#   bash login_game.sh all legacy # 重启服务器并使用旧版 EI HUD
#   bash login_game.sh remote 192.168.3.82 legacy # 用 Debian 工作树重启服务端，本机客户端连远程
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
LEGACY_HUD=0
REMOTE_SERVER_IP=""
REMOTE_SSH_TARGET="${ZIRCON_REMOTE_SSH_TARGET:-debian}"
REMOTE_TUNNEL_SOCKET=""
CLIENT_PORT=""
ARGS=("$@")
for ((i=0; i<${#ARGS[@]}; i++)); do
    case "${ARGS[$i]}" in
        all) KILL_ALL=1 ;;
        legacy) LEGACY_HUD=1 ;;
        remote)
            if [ $((i + 1)) -ge ${#ARGS[@]} ]; then
                echo "用法: bash login_game.sh remote <服务器IP> [legacy]" >&2
                exit 2
            fi
            REMOTE_SERVER_IP="${ARGS[$((i + 1))]}"
            ;;
    esac
done
if [ -n "$REMOTE_SERVER_IP" ]; then
    if [[ ! "$REMOTE_SERVER_IP" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || [ "$KILL_ALL" = "1" ]; then
        echo "remote 模式需要 IPv4 地址，且不能同时指定 all。" >&2
        exit 2
    fi
    IFS=. read -r octet1 octet2 octet3 octet4 <<< "$REMOTE_SERVER_IP"
    for octet in "$octet1" "$octet2" "$octet3" "$octet4"; do
        if [ "$octet" -gt 255 ]; then
            echo "无效的 IPv4 地址: $REMOTE_SERVER_IP" >&2
            exit 2
        fi
    done
    REMOTE_PORT=$(ssh -o BatchMode=yes "$REMOTE_SSH_TARGET" "cat /home/tetsuya/development/zircon/Debug/ServerCore/Server.ini" | iconv -f UTF-16 -t UTF-8 | awk -F= '/^[[:space:]]*Port[[:space:]]*=/ {gsub(/[[:space:]\\r]/, "", $2); print $2; exit}') || {
        echo "无法通过 SSH 读取远程 Server.ini。" >&2
        exit 1
    }
    if [[ ! "$REMOTE_PORT" =~ ^[0-9]+$ ]] || [ "$REMOTE_PORT" -lt 1 ] || [ "$REMOTE_PORT" -gt 65535 ]; then
        echo "远程 Server.ini 没有有效的 Port 配置。" >&2
        exit 1
    fi
    PORT="$REMOTE_PORT"
    CLIENT_PORT="$PORT"
fi

# 只清理由本次脚本启动的服务端；外部已运行的服务端不接管、不关闭。
SERVER_PID=""
SERVER_STARTED_BY_SCRIPT=0
cleanup_started_server() {
    if [ -n "$REMOTE_TUNNEL_SOCKET" ]; then
        ssh -S "$REMOTE_TUNNEL_SOCKET" -O exit "$REMOTE_SSH_TARGET" >/dev/null 2>&1 || true
        rm -f "$REMOTE_TUNNEL_SOCKET"
    fi
    if [ "$SERVER_STARTED_BY_SCRIPT" != "1" ] || [ -z "$SERVER_PID" ]; then
        return
    fi
    if kill -0 "$SERVER_PID" 2>/dev/null; then
        echo ""
        echo "  关闭本次启动的服务端 (PID $SERVER_PID)..."
        kill -TERM "$SERVER_PID" 2>/dev/null || true
        for _ in $(seq 1 10); do
            kill -0 "$SERVER_PID" 2>/dev/null || break
            sleep 1
        done
        if kill -0 "$SERVER_PID" 2>/dev/null; then
            echo "  服务端未正常退出，强制结束"
            kill -KILL "$SERVER_PID" 2>/dev/null || true
        fi
    fi
}
trap cleanup_started_server EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

cd "$ROOT"

echo "══════════════════════════════════════"
echo "  Zircon 游戏一键登录"
if [ -n "$REMOTE_SERVER_IP" ]; then
    echo "  模式: 远程开发服务器重启 + 本机客户端"
elif [ "$KILL_ALL" = "1" ]; then
    echo "  模式: all（杀服务器+客户端，重启服务器）"
else
    echo "  模式: 快速（只杀客户端，服务器在跑则直接连）"
fi
if [ "$LEGACY_HUD" = "1" ]; then
    echo "  HUD: 旧版 EI（--legacy-ui --legacy-hud）"
fi
if [ -n "$REMOTE_SERVER_IP" ]; then
    echo "  服务端: SSH $REMOTE_SSH_TARGET 工作树（$REMOTE_SERVER_IP:${PORT}，经本地隧道连接）"
fi
echo "══════════════════════════════════════"

# ---------- 1. 强制杀掉游戏相关进程 ----------
echo ""
echo "[1/4] 清理游戏进程..."

# 杀掉 Godot 客户端
CLIENT_PIDS=$(pgrep -f "[g]odot-mono.*--path $ROOT/GodotClient" || true)
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
    REMAIN=$(pgrep -f "[d]otnet .*ServerCore(/|/ServerCore\.dll)|[d]otnet ServerCore\.dll|[g]odot-mono.*--path $ROOT/GodotClient" || true)
else
    REMAIN=$(pgrep -f "[g]odot-mono.*--path $ROOT/GodotClient" || true)
fi
if [ -n "$REMAIN" ]; then
    echo "  ⚠️ 残留进程: ${REMAIN}，再杀一次"
    kill -KILL $REMAIN 2>/dev/null || true
    sleep 2
fi
echo "  ✓ 进程清理完成"

# ---------- 2. 构建服务端与客户端 ----------
echo ""
if [ -z "$REMOTE_SERVER_IP" ]; then
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
else
    echo "[2/4] 远程工作树负责构建服务端；本机只构建客户端..."
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

if [ -n "$REMOTE_SERVER_IP" ]; then
    ssh -o BatchMode=yes "$REMOTE_SSH_TARGET" bash -s -- "$PORT" <<'REMOTE_SCRIPT'
set -euo pipefail
PORT="$1"
REPO=/home/tetsuya/development/zircon
SERVER_DIR="$REPO/Debug/ServerCore"
BUILD_LOG=/tmp/zircon_remote_server_build.log
if [ ! -f "$REPO/ServerCore/ServerCore.csproj" ] || [ ! -d "$SERVER_DIR" ]; then
    echo "远程 Zircon 工作树或 Debug/ServerCore 不存在。" >&2
    exit 2
fi
cd "$REPO"
if dotnet build ServerCore/ServerCore.csproj --no-restore -o "$SERVER_DIR" >"$BUILD_LOG" 2>&1; then
    tail -3 "$BUILD_LOG"
else
    cat "$BUILD_LOG"
    exit 1
fi
# 只停止从远程 Zircon 工作树 Debug/ServerCore 启动的测试服务端。
SERVER_DIR_REAL=$(readlink -f "$SERVER_DIR")
for pid in $(pgrep -f '^dotnet ServerCore\.dll$' || true); do
    if [ "$(readlink -f "/proc/$pid/cwd" 2>/dev/null || true)" = "$SERVER_DIR_REAL" ]; then
        echo "停止远程工作树服务端 PID $pid"
        kill -TERM "$pid" 2>/dev/null || true
    fi
done
for _ in $(seq 1 10); do
    FOUND=0
    for pid in $(pgrep -f '^dotnet ServerCore\.dll$' || true); do
        if [ "$(readlink -f "/proc/$pid/cwd" 2>/dev/null || true)" = "$SERVER_DIR_REAL" ]; then FOUND=1; fi
    done
    [ "$FOUND" = "1" ] || break
    sleep 1
done
if pgrep -f '^dotnet ServerCore\.dll$' | while read -r pid; do
    [ "$(readlink -f "/proc/$pid/cwd" 2>/dev/null || true)" != "$SERVER_DIR_REAL" ] || exit 1
done; then
    echo "远程测试服务端未能退出，停止以避免启动重复实例。" >&2
    exit 1
fi
cd "$SERVER_DIR"
nohup dotnet ServerCore.dll >/tmp/servercore_login_remote.log 2>&1 </dev/null &
echo "远程服务端 PID $!"
for i in $(seq 1 30); do
    if nc -z 127.0.0.1 "$PORT" 2>/dev/null; then
        echo "远程服务端已就绪（端口 ${PORT}）"
        exit 0
    fi
    sleep 1
done
echo "远程服务端 30 秒内未就绪；日志：/tmp/servercore_login_remote.log" >&2
tail -30 /tmp/servercore_login_remote.log
exit 1
REMOTE_SCRIPT
    # 保持远程 Server.ini 的 loopback 绑定，通过 SSH 转发接入，不开放游戏端口。
    LOCAL_PORT="$PORT"
    while nc -z 127.0.0.1 "$LOCAL_PORT" 2>/dev/null; do
        LOCAL_PORT=$((LOCAL_PORT + 1))
    done
    if [ "$LOCAL_PORT" -gt 65535 ]; then
        echo "没有可用于 SSH 转发的本地 TCP 端口。" >&2
        exit 1
    fi
    REMOTE_TUNNEL_SOCKET="/tmp/zircon-remote-debug-$$.sock"
    ssh -f -N -M -S "$REMOTE_TUNNEL_SOCKET" \
        -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 \
        -L "127.0.0.1:${LOCAL_PORT}:127.0.0.1:${PORT}" "$REMOTE_SSH_TARGET"
    SERVER_HOST=127.0.0.1
    CLIENT_PORT="$LOCAL_PORT"
    echo "  SSH 转发已建立：127.0.0.1:$CLIENT_PORT → $REMOTE_SERVER_IP:$PORT"
else
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
        SERVER_STARTED_BY_SCRIPT=1
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

    SERVER_HOST=127.0.0.1
fi

# ---------- 4. 启动客户端 ----------
echo ""
if [ -z "$CLIENT_PORT" ]; then CLIENT_PORT="$PORT"; fi
echo "[4/4] 启动客户端登录 ($SERVER_HOST:$CLIENT_PORT)..."
CLIENT_ARGS=(--server "$SERVER_HOST" --port "$CLIENT_PORT" --user test@test.com --pass test123 --char TestHero --window)
if [ "$LEGACY_HUD" = "1" ]; then
    CLIENT_ARGS+=(--legacy-ui --legacy-hud)
fi
godot-mono --path "$ROOT/GodotClient" -- "${CLIENT_ARGS[@]}"
echo ""
echo "══════════════════════════════════════"
echo "  游戏已启动"
echo "══════════════════════════════════════"

#!/usr/bin/env bash
# Zircon 游戏一键登录脚本
# 功能：1) 杀游戏进程 2) 构建 3) 启动服务器 4) 启动客户端登录
# 用法：
#   ./login_game.sh              # 默认：启动 Legacy 界面，不自动登录
#   ./login_game.sh test         # Legacy 界面 + 自动登录测试账号
#   ./login_game.sh zircon       # 现代 Zircon 界面，不自动登录
#   ./login_game.sh test zircon  # 现代 Zircon 界面 + 自动登录测试账号
#   ./login_game.sh all test     # 重启服务器并自动登录测试账号
#   ./login_game.sh remote 192.168.3.82 test # 同步 D 机器代码，远程服务端 + 本机客户端
#
# 环境变量：
#   ZIRCON_EI_ROOT   EI 素材/运行目录；默认 $MIR3_EI_ROOT/LegacyEI 或 $HOME/mir2ei/LegacyEI
#   ZIRCON_TEST_USER / ZIRCON_TEST_PASS / ZIRCON_TEST_CHAR
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -f "$SCRIPT_DIR/GodotClient/ZirconClient.csproj" ]; then
    ROOT="$SCRIPT_DIR"
else
    ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
fi
if [ ! -f "$ROOT/GodotClient/ZirconClient.csproj" ]; then
    echo "无法定位 Zircon 仓库根目录（缺少 GodotClient/ZirconClient.csproj）。" >&2
    exit 1
fi
EI_BASE="${MIR3_EI_ROOT:-${HOME}/mir2ei}"
if [ ! -d "$EI_BASE" ] && [ -d "$HOME/mir2ei" ]; then
    EI_BASE="$HOME/mir2ei"
    export MIR3_EI_ROOT="$EI_BASE"
fi
EI_ROOT="${ZIRCON_EI_ROOT:-$EI_BASE/LegacyEI}"

export ZIRCON_UI_DATA_PATH="${ZIRCON_UI_DATA_PATH:-$ROOT/Debug/Client/Data}"
export ZIRCON_LEGACY_UI_DATA_PATH="${ZIRCON_LEGACY_UI_DATA_PATH:-$EI_ROOT/Data}"

# ---- 素材路径自愈（2026-10-04）----
# 背景：外部环境里残留的 ZIRCON_EI_ROOT / ZIRCON_LEGACY_UI_DATA_PATH 可能指向
# 仓库根（.../development/Zircon）而不是素材根（.../mir2ei/LegacyEI），拼出的
# LegacyEI/Data 不存在 → 客户端启动时静默跳过 4 个 .ogv 过场动画（日志只报
# 「缺少背景视频 …，请运行 convert_legacy_login_video.sh」，极具误导性，
# 实际文件好好的在 mir2ei 里）。
# 这里做「至少要有 GameInter.wil 才算有效素材目录」的判定，并给出正确路径。
REQUIRED_UI_FILE="GameInter.wil"
if [ ! -f "$ZIRCON_LEGACY_UI_DATA_PATH/$REQUIRED_UI_FILE" ]; then
    CANONICAL_LEGACY_DATA="$EI_BASE/LegacyEI/Data"
    if [ -f "$CANONICAL_LEGACY_DATA/$REQUIRED_UI_FILE" ]; then
        echo "素材路径无效：$ZIRCON_LEGACY_UI_DATA_PATH（缺 $REQUIRED_UI_FILE），已自动纠正为 $CANONICAL_LEGACY_DATA"
        ZIRCON_LEGACY_UI_DATA_PATH="$CANONICAL_LEGACY_DATA"
        export ZIRCON_LEGACY_UI_DATA_PATH
    else
        echo "找不到 EI 复古 UI 资源（需要 $REQUIRED_UI_FILE）：" >&2
        echo "  当前 ZIRCON_LEGACY_UI_DATA_PATH=$ZIRCON_LEGACY_UI_DATA_PATH" >&2
        echo "  期望路径=$CANONICAL_LEGACY_DATA" >&2
        echo "  若该目录也不存在，请先跑：/home/tetsuya/mir2ei/restore_all_archived_assets.sh" >&2
        exit 1
    fi
fi

# .ogv 过场动画是运行时必需资源（缺失时**不报错**、只是静默不播）。
# 启动前检查；缺失则**自动从归档补回**再继续 —— mir2ei 是 Syncthing 同步目录，
# 归档目录不在同步范围内，是这些文件的稳定来源；同步抖动/对端瘦身后
# 都可能让本地副本被删，自愈比只提示更可靠（2026-10-03 实际发生过）。
REQUIRED_VIDEOS="wemade.ogv ei_Login.ogv CreateChr.ogv StartGame.ogv"
MISSING_VIDEOS=""
for v in $REQUIRED_VIDEOS; do
    [ -f "$ZIRCON_LEGACY_UI_DATA_PATH/$v" ] || MISSING_VIDEOS="$MISSING_VIDEOS $v"
done
if [ -n "$MISSING_VIDEOS" ]; then
    RESTORE_SH="/home/tetsuya/mir2ei/restore_all_archived_assets.sh"
    if [ -x "$RESTORE_SH" ]; then
        echo "⚠ 缺少过场动画：$MISSING_VIDEOS —— 正在从归档自动补回..."
        if "$RESTORE_SH" >/dev/null 2>&1; then
            STILL_MISSING=""
            for v in $REQUIRED_VIDEOS; do
                [ -f "$ZIRCON_LEGACY_UI_DATA_PATH/$v" ] || STILL_MISSING="$STILL_MISSING $v"
            done
            if [ -z "$STILL_MISSING" ]; then
                echo "✓ 过场动画已补齐，继续启动。"
            else
                echo "✗ 补回后仍缺失：$STILL_MISSING（登录/建角/进游戏动画不会播）" >&2
            fi
        else
            echo "✗ 自动补回失败，请手动执行：$RESTORE_SH" >&2
        fi
    else
        echo "⚠ 缺少过场动画：$MISSING_VIDEOS"
        echo "   这些文件缺失时客户端**不会报错**，只是登录/建角/进游戏动画不播。"
        echo "   补回：$RESTORE_SH"
    fi
fi
for arg in "$@"; do
    if [[ "$arg" =~ ^([1-9][0-9]*)x$ ]]; then
        export ZIRCON_UI_SCALE="${BASH_REMATCH[1]}"
        break
    fi
done

# Local server runtime belongs to this checkout. A sibling ../Debug/ServerCore
# can be an old deployment with a different System.db, which accepts login but
# leaves the client/server map and character definitions out of sync.
SERVER_DIR="$ROOT/Debug/ServerCore"
[ -d "$SERVER_DIR" ] || SERVER_DIR="$ROOT/../Debug/ServerCore"
SERVER_LOG="/tmp/servercore_login.log"

# 端口配置：macOS ControlCenter 占 7000 时自动使用 7001
PORT=7000
if [ -f "$SERVER_DIR/Server.ini" ]; then
    if INI_CONTENT=$(iconv -f UTF-16 -t UTF-8 "$SERVER_DIR/Server.ini" 2>/dev/null); then
        :
    else
        INI_CONTENT=$(iconv -f UTF-8 -t UTF-8 "$SERVER_DIR/Server.ini")
    fi
    INI_PORT=$(printf '%s\n' "$INI_CONTENT" | awk -F= '/^[[:space:]]*Port[[:space:]]*=/ {gsub(/[[:space:]\r]/, "", $2); print $2; exit}')
    if [ -n "$INI_PORT" ]; then PORT="$INI_PORT"; fi
elif [ "$(uname)" = "Darwin" ]; then
    PORT=7001
fi
KILL_ALL=0
LEGACY_HUD=1
AUTO_LOGIN=0
REMOTE_SERVER_IP=""
REMOTE_SSH_TARGET="${ZIRCON_REMOTE_SSH_TARGET:-debian}"
REMOTE_REPO_PATH="${ZIRCON_REMOTE_REPO:-/home/tetsuya/development/zircon}"
REMOTE_BRANCH="${ZIRCON_REMOTE_BRANCH:-master}"
REMOTE_RESEARCH_PATH="${ZIRCON_REMOTE_RESEARCH_REPO:-/home/tetsuya/development/Mir3-Research}"
LOCAL_RESEARCH_PATH="${MIR3_RESEARCH_ROOT:-$ROOT/../Mir3-Research}"
REMOTE_RESEARCH_BRANCH="${ZIRCON_REMOTE_RESEARCH_BRANCH:-ei-ui-audit-2026-09-24}"
REMOTE_SYNC_DEVICE_ID="${ZIRCON_REMOTE_SYNCTHING_DEVICE_ID:-A43UXGE-Q2NW3JR-AWKZZNG-LAKZGYW-VGBLPA2-TYB6MIA-S5WHR75-VSKTKAK}"
SYNCTHING_CONFIG_DIR="${ZIRCON_SYNCTHING_CONFIG_DIR:-$HOME/.local/state/syncthing}"
REMOTE_TUNNEL_SOCKET=""
CLIENT_PORT="${ZIRCON_CLIENT_PORT:-}"
ARGS=("$@")
for ((i=0; i<${#ARGS[@]}; i++)); do
    case "${ARGS[$i]}" in
        all) KILL_ALL=1 ;;
        test) AUTO_LOGIN=1 ;;
        zircon) LEGACY_HUD=0 ;;
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
STAY_SELECT="${ZIRCON_STAY_SELECT:-0}"
if [[ "$STAY_SELECT" != "0" && "$STAY_SELECT" != "1" ]]; then
    echo "ZIRCON_STAY_SELECT 只能是 0 或 1。" >&2
    exit 2
fi
if [ -n "$CLIENT_PORT" ]; then
    if [[ ! "$CLIENT_PORT" =~ ^[0-9]+$ ]] || [ "$CLIENT_PORT" -lt 1 ] || [ "$CLIENT_PORT" -gt 65535 ]; then
        echo "ZIRCON_CLIENT_PORT 必须是 1 到 65535 之间的端口号。" >&2
        exit 2
    fi
    if [ "$KILL_ALL" = "1" ]; then
        echo "指定 ZIRCON_CLIENT_PORT 时不能使用 all。" >&2
        exit 2
    fi
fi
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

    # Pull committed source from D before building anything locally. Refuse to
    # touch a dirty checkout; clean divergent histories are merged so commits
    # made on either machine remain available. Conflicts stop startup safely.
    if [ -n "$(git -C "$ROOT" status --porcelain)" ]; then
        echo "本机代码仓库有未提交改动；为避免覆盖，停止同步和启动。" >&2
        exit 1
    fi
    REMOTE_GIT_URL="ssh://${REMOTE_SSH_TARGET}${REMOTE_REPO_PATH}/.git"
    echo "同步远程代码：${REMOTE_SSH_TARGET}:${REMOTE_REPO_PATH} (${REMOTE_BRANCH})..."
    if ! git -C "$ROOT" fetch "$REMOTE_GIT_URL" "$REMOTE_BRANCH"; then
        echo "无法从远程开发仓库获取代码。" >&2
        exit 1
    fi
    if git -C "$ROOT" merge-base --is-ancestor HEAD FETCH_HEAD; then
        if ! git -C "$ROOT" merge --ff-only FETCH_HEAD; then
            echo "远程代码无法安全快进到本地；请先处理本地改动或分支差异。" >&2
            exit 1
        fi
        echo "本机代码已快进到远程最新提交。"
    elif git -C "$ROOT" merge-base --is-ancestor FETCH_HEAD HEAD; then
        echo "本机提交已包含远程代码，无需更新。"
    else
        if ! git -C "$ROOT" merge --no-edit FETCH_HEAD; then
            git -C "$ROOT" merge --abort || true
            echo "本地与远程代码有冲突；已撤销合并，请人工处理后重试。" >&2
            exit 1
        fi
        echo "本机与 Debian 的提交已安全合并。"
    fi

    REMOTE_PORT=$(ssh -o BatchMode=yes "$REMOTE_SSH_TARGET" "cat '$REMOTE_REPO_PATH/Debug/ServerCore/Server.ini'" | iconv -f UTF-16 -t UTF-8 | awk -F= '/^[[:space:]]*Port[[:space:]]*=/ {gsub(/[[:space:]\\r]/, "", $2); print $2; exit}') || {
        echo "无法通过 SSH 读取远程 Server.ini。" >&2
        exit 1
    }
    if [[ ! "$REMOTE_PORT" =~ ^[0-9]+$ ]] || [ "$REMOTE_PORT" -lt 1 ] || [ "$REMOTE_PORT" -gt 65535 ]; then
        echo "远程 Server.ini 没有有效的 Port 配置。" >&2
        exit 1
    fi
    PORT="$REMOTE_PORT"
    CLIENT_PORT="$PORT"

    # remote 模式必须使用本机 Syncthing 镜像中的完整运行资源。
    # 环境中旧的 MIR3_EI_ROOT 可能只含 Data 子目录或是空目录。
    if [ ! -f "$EI_BASE/Data/System.db" ] || [ ! -d "$EI_BASE/Map" ] || [ ! -d "$EI_BASE/Sound" ]; then
        FALLBACK_EI_BASE="$HOME/mir2ei"
        if [ -f "$FALLBACK_EI_BASE/Data/System.db" ] && [ -d "$FALLBACK_EI_BASE/Map" ] && [ -d "$FALLBACK_EI_BASE/Sound" ]; then
            echo "资源根目录 $EI_BASE 不完整，改用本机镜像 $FALLBACK_EI_BASE。"
            EI_BASE="$FALLBACK_EI_BASE"
        else
            echo "本机 EI 资源不完整（需要 Data/System.db、Map/ 和 Sound/）：$EI_BASE" >&2
            exit 1
        fi
    fi
    export MIR3_EI_ROOT="$EI_BASE"
    if [ -z "${ZIRCON_EI_ROOT:-}" ]; then
        EI_ROOT="$EI_BASE/LegacyEI"
        export ZIRCON_LEGACY_UI_DATA_PATH="$EI_ROOT/Data"
    fi

    CLIENT_RESOURCE_LINK="$ROOT/Debug/Client"
    if [ -L "$CLIENT_RESOURCE_LINK" ]; then
        EXPECTED_CLIENT_ROOT=$(cd "$EI_BASE" && pwd -P)
        CURRENT_CLIENT_ROOT=$(cd "$CLIENT_RESOURCE_LINK" && pwd -P)
        if [ "$CURRENT_CLIENT_ROOT" != "$EXPECTED_CLIENT_ROOT" ]; then
            rm -f "$CLIENT_RESOURCE_LINK"
            ln -s "$EI_BASE" "$CLIENT_RESOURCE_LINK"
            echo "修正客户端资源链接：$CLIENT_RESOURCE_LINK -> $EI_BASE"
        fi
    fi
fi

sync_remote_research_and_assets() {
    if [ ! -d "$LOCAL_RESEARCH_PATH/.git" ]; then
        echo "本机 Mir3-Research 仓库不存在：$LOCAL_RESEARCH_PATH" >&2
        return 1
    fi
    if ! command -v rsync >/dev/null 2>&1; then
        echo "remote 模式同步研究资料和客户端资源需要 rsync。" >&2
        return 1
    fi

    echo "同步 Mir3-Research（包含 D 工作树中的未提交文档）..."
    RESEARCH_GIT_URL="ssh://${REMOTE_SSH_TARGET}${REMOTE_RESEARCH_PATH}/.git"
    if ! GIT_SSH_COMMAND='ssh -o BatchMode=yes' git -C "$LOCAL_RESEARCH_PATH" fetch "$RESEARCH_GIT_URL" "$REMOTE_RESEARCH_BRANCH"; then
        echo "无法获取 D 机器的 Mir3-Research 提交。" >&2
        return 1
    fi
    # 本地 Mir3-Research 是 D 的只读镜像；每次先对齐提交，再复制 D 的工作树状态。
    if ! git -C "$LOCAL_RESEARCH_PATH" reset --hard FETCH_HEAD; then
        echo "无法将本机 Mir3-Research 对齐到 D 机器。" >&2
        return 1
    fi
    if ! rsync -a --delete --info=stats2 -e 'ssh -o BatchMode=yes' \
        --exclude='/.git/' \
        --exclude='/Tools/dbeditor/venv/' \
        --exclude='/Tools/uieditor/venv/' \
        --exclude='/Mir3 Preview Version.rar' \
        "$REMOTE_SSH_TARGET:$REMOTE_RESEARCH_PATH/" "$LOCAL_RESEARCH_PATH/"; then
        echo "同步 Mir3-Research 工作树失败。" >&2
        return 1
    fi

    # Runtime game resources are in mir2ei-client. WebData is mirrored in the
    # background too, but its many files are not needed to launch the game.
    echo "确认 Syncthing 的游戏客户端资源已同步..."
    if ! python3 - "$SYNCTHING_CONFIG_DIR/config.xml" "$REMOTE_SYNC_DEVICE_ID" <<'PY'
import json
import sys
import time
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

config_path, peer_id = sys.argv[1:]
try:
    root = ET.parse(config_path).getroot()
    api_key = root.findtext("./gui/apikey")
    if not api_key:
        raise RuntimeError("Syncthing API key is missing")
except Exception as exc:
    print(f"Syncthing 配置不可用：{exc}", file=sys.stderr)
    sys.exit(1)

base = "http://127.0.0.1:8384"
headers = {"X-API-Key": api_key}
folders = ("mir2ei-client",)

def get(path):
    request = urllib.request.Request(base + path, headers=headers)
    with urllib.request.urlopen(request, timeout=10) as response:
        return json.load(response)

deadline = time.monotonic() + 3600
last_report = 0
while time.monotonic() < deadline:
    try:
        connected = get("/rest/system/connections")["connections"].get(peer_id, {}).get("connected", False)
        statuses = []
        for folder in folders:
            status = get("/rest/db/status?" + urllib.parse.urlencode({"folder": folder}))
            if status.get("errors", 0):
                raise RuntimeError(f"{folder} reports {status['errors']} errors")
            statuses.append((folder, status))
        pending_files = sum(s.get("needFiles", 0) + s.get("needDirs", 0) for _, s in statuses)
        pending_bytes = sum(s.get("needBytes", 0) for _, s in statuses)
        settled = all(
            s.get("state") == "idle"
            and s.get("needFiles", 0) == 0
            and s.get("needDirs", 0) == 0
            and s.get("needDeletes", 0) == 0
            and s.get("needBytes", 0) == 0
            for _, s in statuses
        )
        if connected and settled:
            print("Syncthing 已连接 D；mir2ei-client 游戏资源已同步。")
            sys.exit(0)
        now = time.monotonic()
        if now - last_report >= 30:
            state = ", ".join(f"{name}={s.get('state')}" for name, s in statuses)
            print(f"Syncthing 等待中：connected={connected}, files={pending_files}, bytes={pending_bytes}, {state}")
            last_report = now
    except Exception as exc:
        print(f"等待 Syncthing 同步时出错：{exc}", file=sys.stderr)
        sys.exit(1)
    time.sleep(3)

print("Syncthing 一小时内未完成游戏客户端资源同步，停止启动。", file=sys.stderr)
sys.exit(1)
PY
    then
        echo "Syncthing 资源未同步完成，停止启动。" >&2
        return 1
    fi
    echo "Mir3-Research 已同步；客户端资源由 Syncthing 持续镜像。"
}

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
    echo "  HUD: 复古 Legacy EI"
else
    echo "  HUD: 现代 Zircon"
fi
if [ "$AUTO_LOGIN" = "1" ]; then
    echo "  登录: 自动登录测试账号"
else
    echo "  登录: 手动登录"
fi
if [ -n "$REMOTE_SERVER_IP" ]; then
    echo "  服务端: $REMOTE_SERVER_IP (${REMOTE_SSH_TARGET} SSH；经本地隧道连接)"
fi
echo "══════════════════════════════════════"

# The client reads System.db from EI_BASE/Data while ServerCore reads
# Database/System.db from its working directory. Refuse to start against a
# mismatched database: login can succeed even though gameplay indices differ.
if [ -z "$REMOTE_SERVER_IP" ]; then
    SERVER_SYSTEM_DB="$SERVER_DIR/Database/System.db"
    CLIENT_SYSTEM_DB="$EI_BASE/Data/System.db"
    if [ ! -f "$SERVER_SYSTEM_DB" ] || [ ! -f "$CLIENT_SYSTEM_DB" ]; then
        echo "缺少客户端或服务端 System.db，停止启动：" >&2
        echo "  服务端：$SERVER_SYSTEM_DB" >&2
        echo "  客户端：$CLIENT_SYSTEM_DB" >&2
        exit 1
    fi
    if ! cmp -s "$SERVER_SYSTEM_DB" "$CLIENT_SYSTEM_DB"; then
        echo "客户端与服务端 System.db 不一致，停止启动：" >&2
        echo "  服务端：$SERVER_SYSTEM_DB" >&2
        echo "  客户端：$CLIENT_SYSTEM_DB" >&2
        exit 1
    fi
fi

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
    # macOS 没有 ss；用 nc -z 探测脚本实际使用的端口（macOS 上通常是 7001）。
    if nc -z 127.0.0.1 "$PORT" 2>/dev/null; then
        echo "  服务器已在运行 (端口 ${PORT})，保留不重启"
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

if [ -n "$REMOTE_SERVER_IP" ]; then
    echo ""
    echo "[1.5/4] 同步研究文档和客户端资源..."
    if ! sync_remote_research_and_assets; then
        echo "同步失败，停止启动以避免使用旧资源。" >&2
        exit 1
    fi
fi

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
    ssh -o BatchMode=yes "$REMOTE_SSH_TARGET" bash -s -- "$PORT" "$REMOTE_REPO_PATH" <<'REMOTE_SCRIPT'
set -euo pipefail
PORT="$1"
REPO="$2"
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
# 注意：这里判断的是「还有没有属于本工作树的服务端」。
# 原实现写成 `if pgrep | while ...; then 报错`，而 while 在**没有**匹配进程时
# 退出码为 0 —— 于是服务端**成功停掉反而报错退出**，真正没停掉时倒放行启动
# 第二个实例。改为显式统计。
STILL_RUNNING=0
for pid in $(pgrep -f '^dotnet ServerCore\.dll$' || true); do
    if [ "$(readlink -f "/proc/$pid/cwd" 2>/dev/null || true)" = "$SERVER_DIR_REAL" ]; then
        STILL_RUNNING=1
        break
    fi
done
if [ "$STILL_RUNNING" = "1" ]; then
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
    if [ -n "$CLIENT_PORT" ] && [ "$CLIENT_PORT" != "$PORT" ]; then
        if ! nc -z 127.0.0.1 "$CLIENT_PORT" 2>/dev/null; then
            echo "指定客户端端口 $CLIENT_PORT 没有服务监听；拒绝在配置端口 $PORT 启动另一服务端。" >&2
            exit 1
        fi
        echo "  使用已运行的指定客户端端口 $CLIENT_PORT；不启动配置端口 $PORT 的服务端"
    else
        PORT_OPEN=0
        if nc -z 127.0.0.1 "$PORT" 2>/dev/null; then
            PORT_OPEN=1
        fi
        if [ "$KILL_ALL" = "0" ] && [ "$PORT_OPEN" = "1" ]; then
            echo "  服务器已在运行 (端口 $PORT 监听中)，跳过启动"
        else
            cd "$SERVER_DIR"
            # macOS 没有 setsid；nohup + 后台即可，脚本退出时由 trap 负责清理。
            if command -v setsid >/dev/null 2>&1; then
                setsid nohup dotnet ServerCore.dll > "$SERVER_LOG" 2>&1 < /dev/null &
            else
                nohup dotnet ServerCore.dll > "$SERVER_LOG" 2>&1 < /dev/null &
            fi
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
    fi

    SERVER_HOST=127.0.0.1
fi

# ---------- 4. 启动客户端 ----------
echo ""
if [ -z "$CLIENT_PORT" ]; then CLIENT_PORT="$PORT"; fi
echo "[4/4] 启动客户端 ($SERVER_HOST:$CLIENT_PORT)..."
TEST_USER="${ZIRCON_TEST_USER:-test@test.com}"
TEST_PASS="${ZIRCON_TEST_PASS:-test123}"
TEST_CHAR="${ZIRCON_TEST_CHAR:-TestHero}"
CLIENT_ARGS=(--server "$SERVER_HOST" --port "$CLIENT_PORT" --window)
if [ "$AUTO_LOGIN" = "1" ]; then
    CLIENT_ARGS+=(--user "$TEST_USER" --pass "$TEST_PASS" --char "$TEST_CHAR")
    if [ "$STAY_SELECT" = "1" ]; then CLIENT_ARGS+=(--stay-select); fi
fi
if [ "$LEGACY_HUD" = "1" ]; then
    CLIENT_ARGS+=(--legacy-ui --legacy-hud)
else
    CLIENT_ARGS+=(--zircon-ui)
fi
godot-mono --path "$ROOT/GodotClient" -- "${CLIENT_ARGS[@]}"
echo ""
echo "══════════════════════════════════════"
echo "  游戏已启动"
echo "══════════════════════════════════════"

#!/usr/bin/env bash
# Godot 客户端 UI 交互审计 — 运行期操作/取证工具。
#
# 前提：Xvfb :100 + openbox 已起；Godot 客户端已连 127.0.0.1:7000 并进入游戏，
# 窗口为 800x600（legacy 逻辑画布 = 窗口像素，scale=1）。
#
# 用法：
#   ./harness.sh shot <name>            # 截图窗口 → .artifacts/.../shots/<name>.png
#   ./harness.sh click <lx> <ly> [btn]  # 逻辑坐标点击（默认左键）
#   ./harness.sh rclick <lx> <ly>       # 右键
#   ./harness.sh dclick <lx> <ly>       # 双击
#   ./harness.sh move <lx> <ly>         # 移动鼠标
#   ./harness.sh key <keysym>...        # 按键（如 q / Escape / alt+x）
#   ./harness.sh seq ...                # 依次执行：click 400 300 / key q / wait 1
#   ./harness.sh log [n]                # 客户端日志尾部
#
# 逻辑坐标 → 屏幕坐标：屏幕 = 窗口原点 + 逻辑坐标（scale=1）。
set -uo pipefail

export DISPLAY=:103
export WINW="${WINW:-800}"
export WINH="${WINH:-600}"
OUT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SHOT_DIR="$OUT_DIR/shots"
mkdir -p "$SHOT_DIR"

win_id() { xdotool search --name "ZirconClient" 2>/dev/null | head -1; }

win_origin() {
    local wid; wid="$(win_id)"
    xdotool getwindowgeometry "$wid" | sed -n 's/.*Position: \([0-9-]*\),\([0-9-]*\).*/\1 \2/p'
}

activate() {
    local wid; wid="$(win_id)"
    [ -n "$wid" ] || { echo "no client window" >&2; return 1; }
    xdotool windowactivate "$wid" 2>/dev/null || xdotool windowfocus "$wid"
    sleep 0.25
}

click() {
    activate
    local ox oy; read -r ox oy < <(win_origin)
    xdotool mousemove $((ox + $1)) $((oy + $2)) sleep 0.15 click "${3:-1}"
    sleep 0.35
}

dclick() {
    activate
    local ox oy; read -r ox oy < <(win_origin)
    xdotool mousemove $((ox + $1)) $((oy + $2)) sleep 0.15 click --repeat 2 --delay 120 1
    sleep 0.35
}

move() {
    activate
    local ox oy; read -r ox oy < <(win_origin)
    xdotool mousemove $((ox + $1)) $((oy + $2))
    sleep 0.3
}

key() {
    activate
    # 用 XTEST（不带 --window）注入：--window 走 XSendEvent，Godot 会忽略合成事件。
    xdotool key --clearmodifiers "$@"
    sleep 0.35
}

shot() {
    local name="$1"
    activate
    local wid ox oy
    wid="$(win_id)"
    read -r ox oy < <(win_origin)
    local tmp; tmp="$(mktemp /tmp/audit-shot-XXXXXX.png)"
    scrot -o "$tmp"
    python3 - "$tmp" "$SHOT_DIR/$name.png" "$ox" "$oy" "$WINW" "$WINH" <<'PY'
import sys
from PIL import Image
src, dst, ox, oy, w, h = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
Image.open(src).crop((ox, oy, ox + w, oy + h)).save(dst)
PY
    rm -f "$tmp"
    python3 - "$SHOT_DIR/$name.png" <<'PY'
import sys, os
from PIL import Image
p = sys.argv[1]
im = Image.open(p)
cols = im.convert('RGB').getcolors(maxcolors=1 << 20) or []
print(f"shot {os.path.basename(p)} {im.size[0]}x{im.size[1]} colors={len(cols)} bytes={os.path.getsize(p)}")
PY
}

# 客户端 stdout 直接读进程 fd（service 由 proc:// 持有）
log() { tail -"${1:-30}" "proc://zircon-godot-audit" 2>/dev/null || echo "(log unavailable)"; }

# seq: 用 / 分隔的步骤，每步是 "click X Y" / "key X" / "wait N" / "move X Y"
seq_run() {
    local spec="$1" IFS='/'
    local -a steps; read -r -a steps <<< "$spec"
    for s in "${steps[@]}"; do
        s="$(echo "$s" | sed 's/^ *//;s/ *$//')"
        [ -z "$s" ] && continue
        echo "+ $s"
        # shellcheck disable=SC2086
        "$@" >/dev/null 2>&1 || true
        case "$s" in
            wait*) sleep "$(echo "$s" | awk '{print $2}')" ;;
            click*)  click $(echo "$s" | cut -d' ' -f2-) ;;
            rclick*) click $(echo "$s" | cut -d' ' -f2-) 3 ;;
            dclick*) dclick $(echo "$s" | cut -d' ' -f2-) ;;
            move*)   move $(echo "$s" | cut -d' ' -f2-) ;;
            key*)    key "$(echo "$s" | cut -d' ' -f2-)" ;;
            shot*)   shot "$(echo "$s" | cut -d' ' -f2)" ;;
        esac
    done
}

case "${1:-}" in
    shot)   shift; shot "$@" ;;
    click)  shift; click "$@" ;;
    rclick) shift; click "$1" "$2" 3 ;;
    dclick) shift; dclick "$@" ;;
    move)   shift; move "$@" ;;
    key)    shift; key "$@" ;;
    log)    shift; log "$@" ;;
    seq)    shift; seq_run "$@" ;;
    *) sed -n '1,20p' "${BASH_SOURCE[0]}" ;;
esac

#!/usr/bin/env bash
# 刷怪实机核对：逐图 @move 加载 → 等刷怪 → @mobcensus 取服务端真值（写入 /tmp/mobcensus.txt）。
# 用法: bash tools/run_spawn_audit.sh <maplist.txt> [display] [wait_seconds]
#   maplist.txt 每行一个地图文件名（见 tools/mud3_spawn_plan.py 产出的 replacedMaps）
set -uo pipefail
LIST="${1:?maplist.txt}"
DISP="${2:-:150}"
WAIT="${3:-12}"
export DISPLAY="$DISP"

WIN=$(xdotool search --name ZirconClient | head -1)
if [ -z "$WIN" ]; then echo "找不到客户端窗口（先启动客户端并进游戏）" >&2; exit 1; fi
xdotool windowfocus "$WIN"
sleep 1

send() { # send <text>
    xdotool key Return; sleep 0.5
    xdotool type --delay 25 "$1"; sleep 0.5
    xdotool key Return
}

n=0
while read -r map; do
    [ -z "$map" ] && continue
    n=$((n+1))
    echo "[spawn-audit] ($n) map=$map"
    send "@move $map"
    sleep "$WAIT"
    # 统计两次：第一次可能只是把地图按需加载（刷怪循环还没跑到），第二次才是真值
    send "@mobcensus $map"
    sleep 8
    send "@mobcensus $map"
    sleep 1
done < "$LIST"
echo "[spawn-audit] 完成 $n 张图，结果见 /tmp/mobcensus.txt"

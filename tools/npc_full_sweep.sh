#!/usr/bin/env bash
# npc_full_sweep.sh —— 全量 NPC 实机巡检（分块、可续跑），Linux / macOS 通用
#
# 用途：把 tools/npc_audit_manifest.json 里的每个在用 NPC 都实机点开一遍，
#       逐页截图 + 记录断言结果，供人工复核与回归对比。
#
# 用法：
#   bash tools/npc_full_sweep.sh                 # 全量（230 个），输出到 .artifacts/npc-full-sweep-<日期>/
#   bash tools/npc_full_sweep.sh --ids 275,370   # 只跑指定 NPC
#   OUT=/path/to/dir bash tools/npc_full_sweep.sh
#   CHUNK_SIZE=10 PER_CHUNK_TIMEOUT=900 bash tools/npc_full_sweep.sh
#
# 前置条件：
#   - 服务端已在 $SERVER:$PORT 运行（默认 127.0.0.1:7001），且数据库已同步到目标版本；
#   - godot-mono 在 PATH 上；客户端可自动登录（参数见下方 CLIENT_ARGS）；
#   - 已构建客户端：dotnet build GodotClient/ZirconClient.csproj
#
# 输出结构（默认 OUT=.artifacts/npc-full-sweep-<日期>）：
#   chunk_NN/results.jsonl         每行一个 NPC 的断言结果（clicked/pages/errors）
#   chunk_NN/shots/*.png           每页一张截图（<npcIndex>_<pageIdx>_p<pageId>.png）
#   chunk_NN.log                   该块客户端完整日志
#   progress.txt                   进度与每块耗时（可续跑：已完成的块自动跳过）
#
# 退出码：0 = 全部块跑完；1 = 有块失败/超时（进度仍保留，可直接重跑续做）
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVER="${SERVER:-127.0.0.1}"
PORT="${PORT:-7001}"
USER_ACC="${USER_ACC:-test@test.com}"
PASS="${PASS:-test123}"
CHAR="${CHAR:-TestHero}"
CHUNK_SIZE="${CHUNK_SIZE:-15}"
PER_CHUNK_TIMEOUT="${PER_CHUNK_TIMEOUT:-1200}"
OUT="${OUT:-$ROOT/.artifacts/npc-full-sweep-$(date +%Y-%m-%d)}"
IDS=""
while [ $# -gt 0 ]; do
  case "$1" in
    --ids) IDS="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --chunk) CHUNK_SIZE="$2"; shift 2 ;;
    --timeout) PER_CHUNK_TIMEOUT="$2"; shift 2 ;;
    --help|-h) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "未知参数: $1"; exit 2 ;;
  esac
done

export ZIRCON_UI_DATA_PATH="$ROOT/Debug/Client/Data"
export ZIRCON_LEGACY_UI_DATA_PATH="${ZIRCON_LEGACY_UI_DATA_PATH:-$HOME/mir2ei/LegacyEI/Data}"

# 便携式超时：优先 timeout/gtimeout，都没有就用后台进程 + 看门狗
run_with_timeout() {
  local secs="$1"; shift
  if command -v timeout >/dev/null 2>&1; then timeout "$secs" "$@"; return $?; fi
  if command -v gtimeout >/dev/null 2>&1; then gtimeout "$secs" "$@"; return $?; fi
  local pid waited=0
  "$@" &
  pid=$!
  while kill -0 "$pid" 2>/dev/null; do
    sleep 5
    waited=$((waited + 5))
    if [ "$waited" -ge "$secs" ]; then
      kill -TERM "$pid" 2>/dev/null || true
      sleep 5
      kill -KILL "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
      return 124
    fi
  done
  wait "$pid"
}

mkdir -p "$OUT"
: > "$OUT/progress.txt"

# 取 NPC 清单（默认读 manifest；--ids 可覆盖）
ALL=()
if [ -n "$IDS" ]; then
  IFS=',' read -r -a ALL <<< "$IDS"
else
  while IFS= read -r line; do
    [ -n "$line" ] && ALL+=("$line")
  done < <(python3 -c "
import json, sys
d = json.load(open('$ROOT/tools/npc_audit_manifest.json'))
print('\n'.join(str(n['index']) for n in sorted(d['npcs'], key=lambda n: n['index'])))
")
fi

echo "[sweep] 共 ${#ALL[@]} 个 NPC，块大小 $CHUNK_SIZE，输出 $OUT" | tee -a "$OUT/progress.txt"
total=${#ALL[@]}
i=0
chunk=0
failed=0
while [ "$i" -lt "$total" ]; do
  ids=""
  j=0
  while [ "$j" -lt "$CHUNK_SIZE" ] && [ $((i + j)) -lt "$total" ]; do
    if [ -n "$ids" ]; then ids="$ids,${ALL[$((i + j))]}"; else ids="${ALL[$((i + j))]}"; fi
    j=$((j + 1))
  done
  tag="$(printf 'chunk_%02d' "$chunk")"
  dir="$OUT/$tag"
  if [ -f "$dir/results.jsonl" ]; then
    echo "[sweep] $tag 已完成，跳过" | tee -a "$OUT/progress.txt"
    i=$((i + CHUNK_SIZE)); chunk=$((chunk + 1)); continue
  fi
  rm -rf "$dir"; mkdir -p "$dir"
  start=$(date +%s)
  echo "[sweep] $tag 开始 ids=$ids $(date +%H:%M:%S)" | tee -a "$OUT/progress.txt"
  run_with_timeout "$PER_CHUNK_TIMEOUT" godot-mono --path "$ROOT/GodotClient" -- \
    --server "$SERVER" --port "$PORT" --window \
    --user "$USER_ACC" --pass "$PASS" --char "$CHAR" \
    --legacy-ui --legacy-hud \
    --npc-audit --npc-audit-only "$ids" --npc-audit-out "$dir" --npc-audit-quit \
    > "$OUT/$tag.log" 2>&1
  rc=$?
  dur=$(( $(date +%s) - start ))
  summary="$(grep -h "NpcAudit] SUMMARY" "$OUT/$tag.log" | tail -1)"
  echo "[sweep] $tag 结束 rc=$rc 用时=${dur}s ${summary:-（无 SUMMARY）}" | tee -a "$OUT/progress.txt"
  [ "$rc" -eq 0 ] && [ -f "$dir/results.jsonl" ] || failed=$((failed + 1))
  i=$((i + CHUNK_SIZE)); chunk=$((chunk + 1))
done

echo "[sweep] 全部完成 $(date +%H:%M:%S)，块数 ${chunk}，失败块 ${failed}" | tee -a "$OUT/progress.txt"
shots=$(find "$OUT" -name '*.png' | wc -l | tr -d ' ')
records=$(cat "$OUT"/chunk_*/results.jsonl 2>/dev/null | wc -l | tr -d ' ')
echo "[sweep] 截图 $shots 张，结果记录 $records 条，占用 $(du -sh "$OUT" | cut -f1)" | tee -a "$OUT/progress.txt"
[ "$failed" -eq 0 ]

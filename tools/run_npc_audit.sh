#!/usr/bin/env bash
# 全量 NPC 实机巡检驱动：
#   1) 检查 127.0.0.1:7000 服务端在跑
#   2) 在指定 X display 上启动客户端 --npc-audit（自动传送→点击→翻页→截图→JSONL）
#   3) 等客户端退出，把结果与截图收进 docs/screenshots/npc_audit/
#
# 用法: bash tools/run_npc_audit.sh [display=:150] [outdir=/tmp/npc_audit] [only=idx,idx]
set -uo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DISPLAY_ID="${1:-:150}"
OUT="${2:-/tmp/npc_audit}"
ONLY="${3:-}"
LOG="/tmp/npc_audit_client.log"
SHOTS="$REPO/docs/screenshots/npc_audit"

if ! ss -tln 2>/dev/null | grep -q ":7000"; then
    echo "服务端未在 127.0.0.1:7000 监听，先启动 ServerCore。" >&2
    exit 1
fi

mkdir -p "$OUT" "$SHOTS"
ARGS=(--server 127.0.0.1 --port 7000 --window --user test@test.com --pass test123 --char TestHero
      --legacy-ui --legacy-hud --npc-audit
      --npc-audit-manifest tools/npc_audit_manifest.json --npc-audit-out "$OUT")
if [ -n "$ONLY" ]; then ARGS+=(--npc-audit-only "$ONLY"); fi

echo "[*] 客户端启动（display=$DISPLAY_ID, out=$OUT）"
DISPLAY="$DISPLAY_ID" godot-mono --path "$REPO/GodotClient" -- "${ARGS[@]}" >"$LOG" 2>&1
echo "[*] 客户端退出，日志尾部："
tail -5 "$LOG"

if [ -f "$OUT/results.jsonl" ]; then
    cp "$OUT/results.jsonl" "$SHOTS/audit_results.jsonl"
    cp -f "$OUT"/shots/*.png "$SHOTS"/ 2>/dev/null || true
    echo "[*] 结果与截图已归档到 $SHOTS"
    echo "[*] 统计："
    python3 - "$SHOTS/audit_results.jsonl" <<'PY'
import json, sys, collections
rows = [json.loads(l) for l in open(sys.argv[1], encoding='utf-8') if l.strip()]
c = collections.Counter()
for r in rows:
    if r.get('errors'):
        c[r['errors'][0].split(':')[0]] += 1
    else:
        c['ok'] += 1
print(f"  共 {len(rows)} 个 NPC：", dict(c))
PY
else
    echo "[!] 未产出 results.jsonl，请检查 $LOG" >&2
    exit 1
fi

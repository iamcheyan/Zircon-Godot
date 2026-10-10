#!/usr/bin/env bash
# 可续跑的全量 NPC 巡检：客户端崩了/断线了就带着"还差哪些"重跑，直到跑完或达到最大轮数。
#
# 用法: bash tools/run_npc_audit_resumable.sh [outdir] [display] [max_rounds]
set -uo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-/home/tetsuya/npc_audit_final}"
DISP="${2:-:150}"
ROUNDS="${3:-8}"
MAN="$REPO/tools/npc_audit_manifest.json"

mkdir -p "$OUT"
if ! ss -tln 2>/dev/null | grep -q ":7000"; then
    echo "服务端未监听 7000，请先启动 ServerCore" >&2
    exit 1
fi

missing_list() {
    python3 - "$OUT" "$MAN" <<'PY'
import json, sys, os
import glob
rows = []
for p in sorted(glob.glob(os.path.join(sys.argv[1], "round*", "results.jsonl"))):
    rows += [json.loads(l) for l in open(p, encoding="utf-8") if l.strip()]
best = {}
for r in rows:
    prev = best.get(r["index"])
    if prev is None or len(r.get("errors") or []) <= len(prev.get("errors") or []):
        best[r["index"]] = r
man = json.load(open(sys.argv[2], encoding="utf-8"))["npcs"]
todo = [n["index"] for n in man
        if n["index"] not in best or best[n["index"]].get("errors")]
print(",".join(str(x) for x in todo))
PY
}

for round in $(seq 1 "$ROUNDS"); do
    ONLY="$(missing_list)"
    if [ -z "$ONLY" ]; then
        echo "[*] 全部 NPC 已巡检完成"
        break
    fi
    count=$(awk -F, '{print NF}' <<<"$ONLY")
    echo "[*] 第 $round 轮：还差 $count 个 -> 续跑"
    ROUND_OUT="$OUT/round$round"
    mkdir -p "$ROUND_OUT"
    DISPLAY="$DISP" timeout 5400 godot-mono --path "$REPO/GodotClient" -- \
        --server 127.0.0.1 --port 7000 --window \
        --user test@test.com --pass test123 --char TestHero \
        --legacy-ui --legacy-hud \
        --npc-audit --npc-audit-manifest "$MAN" --npc-audit-out "$ROUND_OUT" \
        --npc-audit-only "$ONLY" >"$ROUND_OUT/client.log" 2>&1 || true
    sleep 5
done

python3 "$REPO/tools/summarize_npc_audit.py" "$OUT"/round*/results.jsonl | head -30

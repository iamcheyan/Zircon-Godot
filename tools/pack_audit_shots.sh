#!/usr/bin/env bash
# pack_audit_shots.sh —— 把巡检/审计截图压成可入库的尺寸，Linux / macOS 通用
#
# 用途：实机截图默认是整屏 PNG（单张 2-3MB），直接把几千张进 git 会撑爆仓库。
#       本脚本把每张图等比缩放到指定最长边并转成 JPEG，保留目录结构与文件名，
#       用于把"证据"随代码入库；原图建议留在本地磁盘（体积原因）。
#
# 用法：
#   bash tools/pack_audit_shots.sh <src-dir> <dst-dir> [max-dim] [quality]
#   bash tools/pack_audit_shots.sh .artifacts/npc-full-sweep-2026-10-18/shots \
#                                 .artifacts/npc-full-sweep-2026-10-18/thumbs 640 80
#
# 说明：
#   - max-dim 是最长边（默认 640）；quality 仅对 JPEG 有效（默认 80）。
#   - macOS 用系统自带 sips；Linux 用 ImageMagick(convert/magick)。
#   - 已在 dst 中存在的同名文件会跳过（可反复重跑续做）。
#   - 结束后打印张数与总体积，便于判断是否适合入库。
set -u

SRC="${1:-}"; DST="${2:-}"; MAXDIM="${3:-640}"; QUALITY="${4:-80}"
if [ -z "${SRC}" ] || [ -z "${DST}" ]; then
  sed -n '2,20p' "$0"; exit 2
fi
[ -d "${SRC}" ] || { echo "[X] 源目录不存在: ${SRC}"; exit 1; }
mkdir -p "${DST}"

if command -v sips >/dev/null 2>&1; then
  CONV="sips"
elif command -v magick >/dev/null 2>&1; then
  CONV="magick"
elif command -v convert >/dev/null 2>&1; then
  CONV="convert"
else
  echo "[X] 找不到 sips / magick / convert，无法缩放"; exit 1
fi
echo "[pack] 转换器 ${CONV} / 最长边 ${MAXDIM} / 质量 ${QUALITY}"

n=0; skipped=0; failed=0
while IFS= read -r src; do
  rel="${src#"${SRC}"/}"
  out="${DST}/${rel%.*}.jpg"
  if [ -f "${out}" ]; then skipped=$((skipped + 1)); continue; fi
  mkdir -p "$(dirname "${out}")"
  case "${CONV}" in
    sips)    sips -s format jpeg -s formatOptions "${QUALITY}" -Z "${MAXDIM}" "${src}" --out "${out}" >/dev/null 2>&1 ;;
    magick)  magick "${src}" -resize "${MAXDIM}x${MAXDIM}>" -quality "${QUALITY}" "${out}" >/dev/null 2>&1 ;;
    convert) convert "${src}" -resize "${MAXDIM}x${MAXDIM}>" -quality "${QUALITY}" "${out}" >/dev/null 2>&1 ;;
  esac
  if [ -f "${out}" ]; then n=$((n + 1)); else failed=$((failed + 1)); echo "[!] 转换失败: ${src}"; fi
done < <(find "${SRC}" -type f \( -name '*.png' -o -name '*.jpg' -o -name '*.jpeg' \) | sort)

echo "[pack] 新转换 ${n} 张，跳过已存在 ${skipped} 张，失败 ${failed} 张"
python3 - "${DST}" <<'PY'
import os, sys
root = sys.argv[1]
tot = 0; cnt = 0
for dp, _, fns in os.walk(root):
    for fn in fns:
        p = os.path.join(dp, fn)
        if os.path.isfile(p):
            tot += os.path.getsize(p); cnt += 1
if cnt:
    print(f"[pack] 输出 {root}：{cnt} 张，共 {tot/1048576:.1f} MB，平均 {tot/cnt/1024:.0f} KB")
else:
    print(f"[pack] 输出 {root}：空")
PY

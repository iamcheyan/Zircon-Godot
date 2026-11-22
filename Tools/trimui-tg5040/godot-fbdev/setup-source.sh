#!/usr/bin/env bash
# Stage a writable Godot 4.6.3 source tree with the fbdev backend applied.
#
# Godot releases live on GitHub, which is not always reachable from this
# machine. The source tarball used here comes from the Nix store, which ships
# the same 4.6.3-stable tree the local editor was built from.
#
# Usage: setup-source.sh [godot-src-dir]
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
work=${GODOT_FBDEV_WORK:-$HOME/development/godot-fbdev}
src=${1:-}

if [ -z "$src" ]; then
	for candidate in /nix/store/*-source; do
		if [ -f "$candidate/version.py" ] && [ -d "$candidate/platform/linuxbsd" ] &&
			grep -q '^major = 4' "$candidate/version.py"; then
			src=$candidate
			break
		fi
	done
fi
[ -n "$src" ] || {
	printf 'no Godot source found; pass the source directory explicitly\n' >&2
	exit 1
}

mkdir -p "$work"
if [ ! -d "$work/src" ]; then
	cp -r "$src" "$work/src"
	chmod -R u+w "$work/src"
fi

printf 'Godot source: %s\n' "$work/src"
printf 'fbdev backend patch: run %s/apply-fbdev-backend.sh (idempotent)\n' "$here"

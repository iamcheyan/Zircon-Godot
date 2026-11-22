#!/usr/bin/env bash
# Install the freshly built fbdev engine template as a Godot export template.
#
# Godot looks for templates under
#   $XDG_DATA_HOME/godot/export_templates/<version>/linux_<kind>.<arch>
# so the binary is copied there as `linux_release.arm64` next to the official
# one (the original file is kept as `linux_release.arm64.official` on first run).
#
# Usage: install-template.sh [template-dir]
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
work=${GODOT_FBDEV_WORK:-$HOME/development/godot-fbdev}
templates=${1:-$HOME/development/Zircon/.artifacts/godot-data/godot/export_templates/4.6.3.stable.mono}

built=""
for candidate in \
	"$work/src/bin/godot.linuxbsd.template_release.arm64.mono" \
	"$work/src/bin/godot.linuxbsd.template_release.arm64"; do
	[ -f "$candidate" ] && built=$candidate && break
done
[ -n "$built" ] || {
	printf 'no fbdev engine binary found under %s/src/bin\n' "$work" >&2
	exit 1
}

file "$built" | grep -q 'ARM aarch64' || {
	printf '%s is not an aarch64 binary\n' "$built" >&2
	exit 1
}

mkdir -p "$templates"
target="$templates/linux_release.arm64"
if [ -f "$target" ] && [ ! -f "$target.official" ]; then
	cp -f "$target" "$target.official"
	printf 'kept the official template as %s.official\n' "$target"
fi
cp -f "$built" "$target"
chmod 755 "$target"

printf 'installed %s -> %s (%s bytes)\n' "$built" "$target" "$(stat -c%s "$target")"
printf 'sha256: '; sha256sum "$target" | cut -d' ' -f1

#!/usr/bin/env bash
# Rebuild and export the fbdev client. Does not mutate the handheld.
# The server, client and assets are deployed separately as a matching set.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
work=${GODOT_FBDEV_WORK:-$HOME/development/godot-fbdev}
artifacts=${ZIRCON_TG5040_ARTIFACTS:-$repo/.artifacts/trimui-tg5040}

if [[ -z ${GODOT_FBDEV_TEMPLATE:-} ]]; then
	"$here/apply-fbdev-backend.sh" "$work/src"
	"$here/build-godot-fbdev.sh"
fi

mkdir -p "$artifacts"
export_dir=$(mktemp -d "$artifacts/client-fbdev.XXXXXX")
xdg="$export_dir/.xdg"
version=$(godot-mono --version)
# Official editor version output appends the build flavor and commit hash.
version=${version%%.mono.*}.mono
templates="$xdg/godot/export_templates/$version"
if [[ -n ${GODOT_FBDEV_TEMPLATE:-} ]]; then
	file "$GODOT_FBDEV_TEMPLATE" | grep -q 'ARM aarch64'
	install -D -m 755 "$GODOT_FBDEV_TEMPLATE" "$templates/linux_release.arm64"
else
	"$here/install-template.sh" "$templates"
fi

XDG_DATA_HOME="$xdg" MIR3_EI_ROOT="${MIR3_EI_ROOT:-$HOME/mir2ei}" \
	godot-mono --headless --path "$repo/GodotClient" \
	--export-release Linux "$export_dir/Mir2eiClient"

# A template executable alone can report --version but is not a game export.
if [[ ! -x "$export_dir/Mir2eiClient" || $(tail -c 4 "$export_dir/Mir2eiClient") != GDPC ||
	! -s "$export_dir/data_ZirconClient_linuxbsd_arm64/ZirconClient.dll" ]]; then
	printf 'Incomplete ARM64 export in %s\n' "$export_dir" >&2
	exit 1
fi
printf 'Client export ready: %s\n' "$export_dir"

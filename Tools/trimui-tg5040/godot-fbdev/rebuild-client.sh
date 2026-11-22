#!/usr/bin/env bash
# Rebuild the fbdev engine template, export the client and push it to the card.
#
# Fast iteration helper: assumes setup-source.sh + apply-fbdev-backend.sh have
# already been run (see README). Only touched files are rebuilt by scons.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
work=${GODOT_FBDEV_WORK:-$HOME/development/godot-fbdev}
artifacts=${ZIRCON_TG5040_ARTIFACTS:-$repo/.artifacts/trimui-tg5040}
serial=${ADB_SERIAL:-}

"$here/apply-fbdev-backend.sh" "$work/src" >/dev/null
"$here/build-godot-fbdev.sh" 2>&1 | tail -n 3
"$here/install-template.sh" | tail -n 2

rm -rf "$artifacts/client-fbdev/data_ZirconClient_linuxbsd_arm64"
DOTNET_ROOT=${DOTNET_ROOT:-/nix/store/qw9r2wzaaj9mgz39zhim4lcyv3vs0bhq-dotnet-sdk-10.0.403/share/dotnet}
DOTNET_BIN=${DOTNET_BIN:-/nix/store/fmfkn7j5gnh6168n7v41wypwq0n35jns-dotnet-sdk-wrapped-10.0.403/bin}
PATH="$DOTNET_BIN:$PATH" DOTNET_ROOT="$DOTNET_ROOT" \
	XDG_DATA_HOME="$repo/.artifacts/godot-data" MIR3_EI_ROOT="${MIR3_EI_ROOT:-$HOME/mir2ei}" \
	godot-mono --headless --quiet --path "$repo/GodotClient" \
	--export-release Linux "$artifacts/client-fbdev/Mir2eiClient" 2>&1 |
	grep -v "uid\|_process_file_system\|at: " | tail -n 3 || true

remote=/mnt/SDCARD/apps/Zircon/GodotClient
adb_cmd() { if [ -n "$serial" ]; then adb -s "$serial" "$@"; else adb "$@"; fi; }
adb_cmd shell "killall -9 Mir2eiClient 2>/dev/null; killall -9 ServerCore 2>/dev/null; mkdir -p $remote"
adb_cmd push "$artifacts/client-fbdev/Mir2eiClient" "$remote/Mir2eiClient" >/dev/null
adb_cmd push "$artifacts/client-fbdev/data_ZirconClient_linuxbsd_arm64" "$remote/" >/dev/null
adb_cmd shell "chmod 755 $remote/Mir2eiClient; sync"
printf 'client pushed to %s\n' "$remote"

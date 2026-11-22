#!/bin/sh
# Deploy the Zircon handheld package to a TrimUI Smart Pro running PJOS.
#
# Two things are installed:
#   1. the game itself under /mnt/SDCARD/Apps/Zircon (client, ARM64 ServerCore,
#      resources, launcher script) - this directory is only ever written by this
#      package, never by the OS;
#   2. one PocketShell card for it: a patched boot launcher, the compat native
#      launch route, and the supervisor branch that hands the panel over.
#
# Card files that belong to the OS are backed up next to themselves first.
#
# Usage: Tools/trimui-tg5040/deploy.sh [--skip-package] [--no-restart]
set -eu

script_dir=$(CDPATH= cd "$(dirname "$0")" && pwd)
repo_root=$(CDPATH= cd "$script_dir/../.." && pwd)
artifacts=${ZIRCON_TG5040_ARTIFACTS:-$repo_root/.artifacts/trimui-tg5040}
package_root=${ZIRCON_TG5040_PACKAGE:-$artifacts/bundle}
launcher_package=${ZIRCON_TG5040_LAUNCHER:-$artifacts/boot-zircon.pocket}
game_root=/mnt/SDCARD/Apps/Zircon
launcher_target=/mnt/SDCARD/System/pocketjs/apps/boot.pocket
native_launch_target=/mnt/SDCARD/System/compat/bin/pocketjs-native-launch
supervisor_target=/mnt/SDCARD/trimui/app/MainUI
cover_target=/mnt/SDCARD/Apps/zircon.png
stamp=$(date +%Y%m%d-%H%M%S)

skip_package=0
restart=1
for arg in "$@"; do
    case "$arg" in
        --skip-package) skip_package=1 ;;
        --no-restart) restart=0 ;;
        *) printf 'unknown option: %s\n' "$arg" >&2; exit 2 ;;
    esac
done

adb_cmd() {
    if [ -n "${ADB_SERIAL:-}" ]; then
        adb -s "$ADB_SERIAL" "$@"
    else
        adb "$@"
    fi
}

shell() { adb_cmd shell "$1"; }

[ -f "$launcher_package" ] || {
    printf 'launcher package missing: %s\n' "$launcher_package" >&2
    printf 'build it with: bun Tools/trimui-tg5040/launcher/repack-launcher.ts --source <boot.pocket> --out %s\n' "$launcher_package" >&2
    exit 1
}
[ -x "$package_root/launch.sh" ] || { printf 'package root incomplete: %s\n' "$package_root" >&2; exit 1; }
[ -x "$package_root/GodotClient/Mir2eiClient" ] || { printf 'client missing in %s\n' "$package_root" >&2; exit 1; }
[ -x "$package_root/Debug/ServerCore/ServerCore" ] || { printf 'ServerCore missing in %s\n' "$package_root" >&2; exit 1; }

shell "mkdir -p $game_root /mnt/SDCARD/Apps /mnt/SDCARD/System/compat/bin"

if [ "$skip_package" = "0" ]; then
    printf '== game package -> %s\n' "$game_root"
    adb_cmd push "$package_root/Debug" "$game_root/"
    adb_cmd push "$package_root/GodotClient" "$game_root/"
    adb_cmd push "$package_root/ClientData" "$game_root/"
    adb_cmd push "$package_root/launch.sh" "$game_root/launch.sh"
    adb_cmd push "$package_root/README.md" "$game_root/README.md"
fi

printf '== launcher card assets\n'
shell "cp -f $launcher_target $launcher_target.bak-$stamp 2>/dev/null || :"
adb_cmd push "$launcher_package" "$launcher_target"
adb_cmd push "$script_dir/cover/zircon.png" "$cover_target"
adb_cmd push "$script_dir/device/pocketjs-native-launch" "$native_launch_target"
shell "cp -f $supervisor_target $supervisor_target.bak-$stamp 2>/dev/null || :"
adb_cmd push "$script_dir/device/MainUI" "$supervisor_target"

shell "chmod 755 $game_root/launch.sh $game_root/GodotClient/Mir2eiClient $game_root/Debug/ServerCore/ServerCore $native_launch_target $supervisor_target $launcher_target; sync"

printf '== installed\n'
shell "ls -l $launcher_target $cover_target $native_launch_target $supervisor_target | sed 's/^/   /'"

if [ "$restart" = "1" ]; then
    printf '== restarting the launcher supervisor\n'
    shell "killall -9 pocket-linux-kms 2>/dev/null; killall -9 Mir2eiClient 2>/dev/null; killall -9 ServerCore 2>/dev/null; sync"
    printf '   supervisor changes need a reboot: reboot the handheld to load %s\n' "$supervisor_target"
fi

printf '\nrollback: adb shell "cp -f %s.bak-%s %s; cp -f %s.bak-%s %s"\n' \
    "$launcher_target" "$stamp" "$launcher_target" "$supervisor_target" "$stamp" "$supervisor_target"

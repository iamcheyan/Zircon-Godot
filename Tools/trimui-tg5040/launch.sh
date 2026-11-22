#!/bin/sh
# Zircon handheld entry point (TrimUI Smart Pro / TG5040).
#
# The supervisor (trimui/app/MainUI) hands the panel to this script for the
# Zircon launcher card. It starts the self-contained ARM64 ServerCore shipped in
# this package and then the Godot client with the same auto-login arguments the
# desktop `login_game.sh test` path uses (test@test.com / test123 / TestHero,
# character created on first login).
#
# The server is started here rather than by the client so the package owns its
# own lifetime: if the client cannot draw yet the server still serves, and the
# supervisor always regains the panel when this script exits.
set -eu
export ZIRCON_FBDEV_RENDER_SCALE=${ZIRCON_FBDEV_RENDER_SCALE:-2}

app_root=$(CDPATH= cd "$(dirname "$0")" && pwd)
server_dir="$app_root/Debug/ServerCore"
client_root="$app_root/Debug/Client"
client_exe="$app_root/GodotClient/Mir2eiClient"
client_data="$app_root/ClientData"
log_file=${ZIRCON_LAUNCH_LOG:-/tmp/zircon-launch.log}
port=${ZIRCON_PORT:-7000}

say() {
    printf '[zircon] %s\n' "$1"
    printf '%s %s\n' "$(date '+%Y-%m-%dT%H:%M:%S' 2>/dev/null || printf now)" "$1" >> "$log_file" 2>/dev/null || :
}

afail() {
    say "FATAL: $1"
    printf 'Zircon: %s\n' "$1" >&2
    exit 1
}

[ -x "$client_exe" ] || afail "client binary missing: $client_exe"
[ -x "$server_dir/ServerCore" ] || afail "ServerCore apphost missing: $server_dir/ServerCore"
[ -f "$server_dir/Server.ini" ] || afail "server config missing: $server_dir/Server.ini"
[ -f "$client_root/Data/System.db" ] || afail "client System.db missing"
[ -d "$client_root/Map" ] || afail "client map directory missing"
[ -d "$client_root/Sound" ] || afail "client sound directory missing"
[ -d "$client_root/LegacyEI/Data" ] || afail "EI interface resources missing"
[ -f "$client_data/frame-formulas.json" ] || afail "ClientData/frame-formulas.json missing"

export ZIRCON_SERVER_DIR="$server_dir"
export ZIRCON_SERVER_EXE="$server_dir/ServerCore"
export MIR3_EI_ROOT="$client_root"
export ZIRCON_CLIENT_DATA="$client_data"
export ZIRCON_MAP_DATA="$client_root/Map"

# Tina Linux (glibc 2.33) ships no ICU. The client and the server both run the
# globalization-invariant runtime, which is all these string tables need.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# A writable HOME on the card gives Godot a persistent user:// (window/UI
# settings) and a place MirSkin looks for the CJK font (NotoSansCJK*) that the
# exported pck cannot carry because ClientData/Fonts is excluded from export.
export HOME=${ZIRCON_HOME:-$app_root/home}
mkdir -p "$HOME/.local/share/fonts" 2>/dev/null || :
# The card is FAT32, so symlinks are unavailable: copy once and reuse.
if [ -d "$app_root/Fonts" ]; then
    for font in "$app_root"/Fonts/NotoSansCJK*; do
        [ -f "$font" ] || continue
        target="$HOME/.local/share/fonts/${font##*/}"
        if [ ! -s "$target" ] || [ "$font" -nt "$target" ]; then
            cp -f "$font" "$target" 2>/dev/null || :
        fi
    done
fi

port_open() {
    if command -v nc >/dev/null 2>&1; then
        nc -z 127.0.0.1 "$port" >/dev/null 2>&1 && return 0
    fi
    # /proc/net/tcp: local port in hex, state 0A = LISTEN.
    hex=$(printf '%04X' "$port")
    awk -v hex="$hex" '$2 ~ (":" hex "$") && $4 == "0A" { found = 1 } END { exit !found }' /proc/net/tcp 2>/dev/null
}

server_pid=
start_server() {
    # Clean up any leftover processes before starting fresh
    killall -9 Mir2eiClient 2>/dev/null || :
    killall -9 ServerCore 2>/dev/null || :
    sleep 1

    say "starting ServerCore"
    (
        cd "$server_dir" || exit 1
        exec ./ServerCore --singleplayer-dev
    ) >> "$log_file" 2>&1 &
    server_pid=$!
    waited=0
    while [ "$waited" -lt 25 ]; do
        port_open && { say "server ready on 127.0.0.1:$port"; return 0; }
        if kill -0 "$server_pid" 2>/dev/null; then :; else
            say "server exited before listening"
            return 1
        fi
        sleep 1
        waited=$((waited + 1))
    done
    say "server did not listen within 25s"
    return 1
}

cleanup() {
    status=$?
    trap - EXIT INT TERM
    say "cleaning up ServerCore and Mir2eiClient"
    if [ -n "$server_pid" ] && kill -0 "$server_pid" 2>/dev/null; then
        say "stopping ServerCore pid=$server_pid"
        kill "$server_pid" 2>/dev/null || :
        wait "$server_pid" 2>/dev/null || :
    fi
    killall -9 ServerCore 2>/dev/null || :
    killall -9 Mir2eiClient 2>/dev/null || :
    exit "$status"
}
trap cleanup EXIT INT TERM

start_server || say "continuing without the bundled server (client will try port $port)"

mode=${ZIRCON_DISPLAY_MODE:-auto}
if [ "$mode" = "auto" ]; then
    if [ -n "${WAYLAND_DISPLAY:-}" ]; then
        mode=wayland
    elif [ -n "${DISPLAY:-}" ]; then
        mode=x11
    elif [ -w /dev/fb0 ]; then
        mode=fbdev
    else
        mode=headless
    fi
fi
say "display mode: $mode"

case "$mode" in
    headless)
        set -- --headless --audio-driver Dummy --log-file "${ZIRCON_CLIENT_LOG:-/tmp/zircon-client.log}"
        ;;
    fbdev)
        set -- --display-driver fbdev --rendering-driver opengl3_es             --audio-driver "${ZIRCON_AUDIO_DRIVER:-Dummy}" --max-fps "${ZIRCON_FBDEV_FPS:-30}"             --log-file "${ZIRCON_CLIENT_LOG:-/tmp/zircon-client.log}"
        ;;
    x11|wayland)
        set -- --display-driver "$mode" --rendering-method gl_compatibility --audio-driver ALSA --fullscreen
        ;;
    *)
        afail "unsupported ZIRCON_DISPLAY_MODE=$mode (use auto, fbdev, headless, x11 or wayland)"
        ;;
esac

say "starting client $client_exe"
"$client_exe" "$@" --     --server 127.0.0.1     --port "$port"     --auto-login     --user test@test.com     --pass test123     --char TestHero     --lang CHINESE
say "client exited with $?"

#!/usr/bin/env bash
# Build a Godot 4.6.3 engine template with the fbdev display backend.
#
# Why: upstream Godot's Linux template only ships X11 / Wayland / headless
# display drivers. The TrimUI Smart Pro (TinaLinux 4.9, no window system, only
# /dev/fb0 plus a vendor Imagination EGL) cannot run any of them, so the client
# fell back to headless and had no picture. This build adds `--display-driver
# fbdev`: an EGL offscreen context whose frames are written into /dev/fb0 and
# flipped with FBIOPAN_DISPLAY, plus direct evdev input.
#
# The backend lives in `src/platform/linuxbsd/fbdev/` of the working tree this
# script builds. `setup-source.sh` stages that tree from the Godot source tarball.
#
# Cross compilation uses Zig (`zig cc -target aarch64-linux-gnu.2.17`) so the
# result needs no more than glibc 2.17, which the device (glibc 2.33) satisfies.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
work=${GODOT_FBDEV_WORK:-$HOME/development/godot-fbdev}
jobs=${JOBS:-$(nproc)}

[ -d "$work/src" ] || {
	printf 'missing Godot work tree: %s\n' "$work" >&2
	printf 'run %s/setup-source.sh first\n' "$here" >&2
	exit 1
}

mkdir -p "$work/bin" "$work/.zig-cache"
write_wrapper() {
	local name=$1
	shift
	cat >"$work/bin/$name" <<EOF
#!/bin/sh
export ZIG_GLOBAL_CACHE_DIR=$work/.zig-cache
export ZIG_LOCAL_CACHE_DIR=$work/.zig-cache
exec $* "\$@"
EOF
	chmod 755 "$work/bin/$name"
}
write_wrapper cc zig cc -target aarch64-linux-gnu.2.17
write_wrapper cxx zig c++ -target aarch64-linux-gnu.2.17
write_wrapper ar zig ar
write_wrapper ranlib zig ranlib

cd "$work"
exec nix shell nixpkgs#scons nixpkgs#zig nixpkgs#dotnet-sdk_10 -c scons -C src \
	platform=linuxbsd arch=arm64 target=template_release \
	module_mono_enabled=yes \
	x11=no wayland=no pulseaudio=no dbus=no speechd=no alsa=yes fontconfig=no udev=no \
	vulkan=no opengl3=yes use_llvm=no \
	CC="$work/bin/cc" CXX="$work/bin/cxx" AR="$work/bin/ar" RANLIB="$work/bin/ranlib" \
	-j"$jobs"

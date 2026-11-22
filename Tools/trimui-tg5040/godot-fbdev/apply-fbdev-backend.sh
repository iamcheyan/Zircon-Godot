#!/usr/bin/env bash
# Apply the fbdev display backend to a Godot 4.6.3 source tree (idempotent).
#
# Copies platform/linuxbsd/fbdev/* from this directory and wires it into the
# linuxbsd build: SCsub, detect.py (the `fbdev` scons option) and
# os_linuxbsd.cpp (driver registration).
#
# Usage: apply-fbdev-backend.sh <godot-source-dir>
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
src=${1:-${GODOT_FBDEV_WORK:-$HOME/development/godot-fbdev}/src}

[ -d "$src/platform/linuxbsd" ] || {
	printf 'not a Godot source tree: %s\n' "$src" >&2
	exit 1
}

mkdir -p "$src/platform/linuxbsd/fbdev"
cp -f "$here"/fbdev/* "$src/platform/linuxbsd/fbdev/"

python3 - "$src" <<'PY'
import sys
from pathlib import Path

src = Path(sys.argv[1])


def patch(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text()
    if new in text:
        print(f"already applied: {label}")
        return
    if text.count(old) != 1:
        raise SystemExit(f"{label}: anchor appears {text.count(old)} times in {path}")
    path.write_text(text.replace(old, new, 1))
    print(f"applied: {label}")


patch(
    src / "platform/linuxbsd/SCsub",
    'if env["wayland"]:\n    common_linuxbsd += SConscript("wayland/SCsub")',
    'if env["wayland"]:\n    common_linuxbsd += SConscript("wayland/SCsub")\n\nif env["fbdev"]:\n    common_linuxbsd += SConscript("fbdev/SCsub")',
    "platform/linuxbsd/SCsub",
)

patch(
    src / "platform/linuxbsd/detect.py",
    '        BoolVariable("x11", "Enable X11 display", True),',
    '        BoolVariable("x11", "Enable X11 display", True),\n        BoolVariable("fbdev", "Enable the Linux framebuffer display server", True),',
    "detect.py option",
)

patch(
    src / "platform/linuxbsd/detect.py",
    '    if env["opengl3"]:\n        env.Append(CPPDEFINES=["GLES3_ENABLED"])',
    '    if env["opengl3"]:\n        env.Append(CPPDEFINES=["GLES3_ENABLED"])\n\n    if env["fbdev"]:\n        env.Append(CPPDEFINES=["FBDEV_ENABLED"])',
    "detect.py define",
)

patch(
    src / "platform/linuxbsd/os_linuxbsd.cpp",
    '#ifdef WAYLAND_ENABLED\n#include "wayland/detect_prime_egl.h"\n#include "wayland/display_server_wayland.h"\n#endif',
    '#ifdef WAYLAND_ENABLED\n#include "wayland/detect_prime_egl.h"\n#include "wayland/display_server_wayland.h"\n#endif\n\n#ifdef FBDEV_ENABLED\n#include "fbdev/display_server_fbdev.h"\n#endif',
    "os_linuxbsd.cpp include",
)

patch(
    src / "platform/linuxbsd/os_linuxbsd.cpp",
    '#ifdef WAYLAND_ENABLED\n\tDisplayServerWayland::register_wayland_driver();\n#endif',
    '#ifdef WAYLAND_ENABLED\n\tDisplayServerWayland::register_wayland_driver();\n#endif\n\n#ifdef FBDEV_ENABLED\n\tDisplayServerFbdev::register_fbdev_driver();\n#endif',
    "os_linuxbsd.cpp registration",
)
PY

printf 'fbdev backend applied to %s\n' "$src"

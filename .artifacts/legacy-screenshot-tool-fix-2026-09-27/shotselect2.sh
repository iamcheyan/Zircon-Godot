#!/bin/zsh
# 选角/登录场景截图（v2）：按 **PID** 匹配窗口，不用窗口标题。
# 用法: shotselect2.sh <outfile> <scene> [godot args...]
out=${1:-/tmp/shot.png}; scene=${2:-res://Scenes/SelectScene.tscn}; shift 2
export ZIRCON_UI_DATA_PATH=/Users/tetsuya/Development/Zircon/Debug/Client/Data
export ZIRCON_LEGACY_UI_DATA_PATH=/Users/tetsuya/mir2ei/LegacyEI/Data
export PATH="/Users/tetsuya/.local/bin:$PATH"
pkill -f "Godot --path" 2>/dev/null
sleep 2
godot-mono --path /Users/tetsuya/Development/Zircon/GodotClient "$scene" -- "$@" > /tmp/shot2.log 2>&1 &
GPID=$!
echo "godot pid=$GPID"
sleep 9
uv run --with pyobjc-framework-Quartz python -c "
import Quartz, sys, os
pid = $GPID
wins = Quartz.CGWindowListCopyWindowInfo(Quartz.kCGWindowListOptionAll, Quartz.kCGNullWindowID)
cands = []
for w in wins:
    if w.get('kCGWindowOwnerPID') != pid: continue
    b = w.get('kCGWindowBounds') or {}
    cands.append((w.get('kCGWindowNumber'), w.get('kCGWindowName'), b.get('Width'), b.get('Height'), w.get('kCGWindowLayer')))
print('PID', pid, '的窗口:', cands)
# 选 layer==0 且面积最大的那个（跳过辅助/无边框层）
best = None
for num, name, w, h, layer in cands:
    if layer != 0: continue
    if best is None or (w or 0)*(h or 0) > best[1]:
        best = (num, (w or 0)*(h or 0))
if best is None:
    print('NO_WINDOW_FOR_PID'); sys.exit(0)
wid = best[0]
img = Quartz.CGWindowListCreateImage(Quartz.CGRectNull, Quartz.kCGWindowListOptionIncludingWindow, wid, Quartz.kCGWindowImageBoundsIgnoreFraming)
p = '$out'
Quartz.CGImageDestinationCreateWithURL
import CoreFoundation
url = CoreFoundation.CFURLCreateFromFileSystemRepresentation(None, p.encode(), len(p), False)
dest = Quartz.CGImageDestinationCreateWithURL(url, 'public.png', 1, None)
Quartz.CGImageDestinationAddImage(dest, img, None)
ok = Quartz.CGImageDestinationFinalize(dest)
print('shot', ok, p, 'wid=', wid)
"

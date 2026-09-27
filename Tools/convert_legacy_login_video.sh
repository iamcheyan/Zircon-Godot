#!/usr/bin/env bash
# 把 EI 原版的 Indeo 5.0 视频转成 Godot 可播放的 Ogg Theora。
#
# 背景：原版登录流程由两个 AVI 驱动，Godot 的 VideoStreamPlayer **只支持
# Ogg Theora**（实测 mp4 会报 `No loader found for resource`）：
#
#   Data/wemade.dat   RIFF AVI 640x360 ~30fps 149 帧  (4.97s)  WeMade 开场 logo
#   Data/ei_Login.dat RIFF AVI 640x360 ~30fps 1629 帧 (54.35s) 登录页背景动画
#
# 两者视频编码都是 **Intel Indeo 5.0**，Godot 无法解码；本脚本用
# `ffmpeg2theora` 转封装为 .ogv（画面内容不变，仅换编码）。
#
# 依赖（macOS/brew）：brew install ffmpeg2theora
# 用法：bash Tools/convert_legacy_login_video.sh [EI数据目录]
#       默认 /Users/tetsuya/mir2ei/LegacyEI/Data
set -euo pipefail

DATA_DIR="${1:-/Users/tetsuya/mir2ei/LegacyEI/Data}"

if ! command -v ffmpeg2theora >/dev/null 2>&1; then
    echo "缺少 ffmpeg2theora，请先： brew install ffmpeg2theora" >&2
    exit 1
fi

for name in wemade ei_Login; do
    src="$DATA_DIR/$name.dat"
    dst="$DATA_DIR/$name.ogv"
    if [[ ! -f "$src" ]]; then
        echo "跳过（源不存在）: $src" >&2
        continue
    fi
    echo "转换 $src -> $dst"
    # --noaudio：原版这两个视频的 PCM 音轨在登录流程里未被播放，
    # 且 Ogg 容器混流会让 .ogv 体积翻倍；视频内容不受影响。
    ffmpeg2theora --noaudio -o "$dst" "$src" >/dev/null 2>&1
    ls -la "$dst"
done

echo "完成。客户端从 $DATA_DIR 读取；注意 Godot 只认 .ogv。"

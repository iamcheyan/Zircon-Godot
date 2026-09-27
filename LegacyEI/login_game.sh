#!/usr/bin/env bash
# Zircon 的 EI/Legacy 启动包装器。
# 脚本位于仓库内，但素材、数据库和运行工作目录仍使用外部 EI_ROOT。
#
# 用法:
#   ./LegacyEI/login_game.sh                  # Legacy 界面，不自动登录
#   ./LegacyEI/login_game.sh test              # Legacy 界面 + 自动登录
#   ./LegacyEI/login_game.sh zircon            # 现代 Zircon 界面
#   ./LegacyEI/login_game.sh test zircon       # 现代界面 + 自动登录
#   ./LegacyEI/login_game.sh test 2x           # Legacy 界面 + 自动登录 + 2 倍缩放
#
# 可配置环境变量:
#   ZIRCON_EI_ROOT       EI 素材/运行目录；未设置时使用 $MIR3_EI_ROOT/LegacyEI
#                        或 $HOME/mir2ei/LegacyEI
#   ZIRCON_TEST_USER     测试账号，默认 test@test.com
#   ZIRCON_TEST_PASS     测试密码，默认 test123
#   ZIRCON_TEST_CHAR     测试角色，默认 TestHero
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EI_BASE="${MIR3_EI_ROOT:-${HOME}/mir2ei}"
EI_ROOT="${ZIRCON_EI_ROOT:-$EI_BASE/LegacyEI}"

export ZIRCON_UI_DATA_PATH="${ZIRCON_UI_DATA_PATH:-$REPO/Debug/Client/Data}"
export ZIRCON_LEGACY_UI_DATA_PATH="${ZIRCON_LEGACY_UI_DATA_PATH:-$EI_ROOT/Data}"

for arg in "$@"; do
    if [[ "$arg" =~ ^([1-9][0-9]*)x$ ]]; then
        export ZIRCON_UI_SCALE="${BASH_REMATCH[1]}"
        break
    fi
done

# 保持 EI 目录作为运行工作目录；服务器/客户端路径仍由仓库启动器管理。
cd "$EI_ROOT"
exec "$REPO/login_game.sh" "$@"

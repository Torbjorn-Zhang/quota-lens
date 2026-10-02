#!/usr/bin/env bash
# Quota Lens one-step installer for macOS (Apple Silicon and Intel, macOS 11+).
#
#   curl -fsSL https://raw.githubusercontent.com/Torbjorn-Zhang/quota-lens/main/install-mac.sh | bash
#
# Installs QuotaLens.app into /Applications (or ~/Applications) and starts it. By default it
# downloads the prebuilt app from the latest GitHub release and checks its SHA-256; when that
# release has no macOS build, or with --source, it builds from source instead. Building needs no
# Git, Xcode or admin rights: the source comes as a tarball and the .NET 6 SDK is installed into
# ~/Library/Caches/QuotaLens. Run from a repository checkout, it builds that checkout.
#
# Options:
#   --release        only install a prebuilt release (fail if none has a macOS build)
#   --source         always build from source
#   --ref <name>     branch or tag to build when building from source (default: main)
#   --no-launch      install without starting the app
#   -h, --help       show this help

# Everything runs inside main(), so `curl … | bash` parses the whole script before executing any
# of it, and main's stdin is detached so no build step can swallow the rest of a piped script.
main() {
set -euo pipefail

repo="Torbjorn-Zhang/quota-lens"
mode="auto"
ref="main"
launch=1

say() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m错误：\033[0m%s\n' "$*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --release) mode="release" ;;
        --source) mode="source" ;;
        --ref) ref="${2:?--ref 需要分支或标签名}"; shift ;;
        --no-launch) launch=0 ;;
        -h|--help) sed -n '2,21p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) die "未知参数：$1（--help 查看用法）" ;;
    esac
    shift
done

[[ "$(uname -s)" == "Darwin" ]] || die "这个安装脚本只适用于 macOS。"
case "$(uname -m)" in
    arm64) rid="osx-arm64"; arch="arm64" ;;
    x86_64) rid="osx-x64"; arch="x64" ;;
    *) die "不支持的处理器架构：$(uname -m)" ;;
esac

work="$(mktemp -d "${TMPDIR:-/tmp}/quotalens-install.XXXXXX")"
trap 'rm -rf "$work"' EXIT
cache="$HOME/Library/Caches/QuotaLens"

# A checkout is detected only when the script runs from a file next to publish-mac.sh.
script_dir=""
if [[ -n "${BASH_SOURCE[0]:-}" && -f "${BASH_SOURCE[0]}" ]]; then
    script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    [[ -f "$script_dir/publish-mac.sh" && -d "$script_dir/src/QuotaLens.Mac" ]] || script_dir=""
fi

app_source=""

install_from_release() {
    say "查找最新发布版中的 macOS（$arch）安装包…"
    local json url sums_url file expected actual
    json="$(curl -fsSL -H 'Accept: application/vnd.github+json' "https://api.github.com/repos/$repo/releases/latest")" || return 1
    url="$(printf '%s' "$json" | grep -o '"browser_download_url": *"[^"]*"' | sed 's/.*"\(https[^"]*\)"$/\1/' \
        | grep -E "macos-$arch\.zip$" | head -1 || true)"
    sums_url="$(printf '%s' "$json" | grep -o '"browser_download_url": *"[^"]*SHA256SUMS\.txt"' | sed 's/.*"\(https[^"]*\)"$/\1/' | head -1 || true)"
    [[ -n "$url" ]] || { say "最新发布版里还没有 macOS 安装包。"; return 1; }

    file="$(basename "$url")"
    say "下载 $file"
    curl -fL --progress-bar "$url" -o "$work/$file"
    if [[ -n "$sums_url" ]]; then
        expected="$(curl -fsSL "$sums_url" | awk -v f="$file" '$2 == f { print $1 }')"
        actual="$(shasum -a 256 "$work/$file" | awk '{ print $1 }')"
        [[ -n "$expected" && "$expected" == "$actual" ]] || die "$file 的 SHA-256 校验失败，已停止安装。"
        say "SHA-256 校验通过"
    fi

    ditto -x -k "$work/$file" "$work/release"
    app_source="$work/release/QuotaLens.app"
    [[ -d "$app_source" ]] || die "安装包里没有 QuotaLens.app。"
}

ensure_dotnet() {
    if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^6\.'; then
        return
    fi
    local dir="${QUOTALENS_DOTNET_DIR:-$cache/dotnet}"
    if [[ ! -x "$dir/dotnet" ]] || ! "$dir/dotnet" --list-sdks 2>/dev/null | grep -q '^6\.'; then
        say "安装 .NET 6 SDK 到 $dir（只在本用户目录，约 200 MB，首次需要几分钟）"
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$work/dotnet-install.sh"
        bash "$work/dotnet-install.sh" --channel 6.0 --install-dir "$dir" --no-path >/dev/null
    fi
    export DOTNET_ROOT="$dir"
    export PATH="$dir:$PATH"
}

build_from_source() {
    local source_dir
    if [[ -n "$script_dir" ]]; then
        source_dir="$script_dir"
        say "从当前仓库构建：$source_dir"
    else
        say "下载源码（$ref）…"
        curl -fsSL "https://codeload.github.com/$repo/tar.gz/$ref" -o "$work/source.tgz" \
            || die "下载源码失败：分支或标签 $ref 不存在？"
        mkdir -p "$work/source"
        tar -xzf "$work/source.tgz" -C "$work/source" --strip-components 1
        source_dir="$work/source"
        [[ -f "$source_dir/publish-mac.sh" ]] || die "$ref 里还没有 macOS 版（缺少 publish-mac.sh），请用 --ref 指定包含 macOS 版的分支。"
    fi

    ensure_dotnet
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
    export NUGET_PACKAGES="${NUGET_PACKAGES:-$cache/nuget}"
    say "构建 QuotaLens.app（$rid）…"
    (cd "$source_dir" && bash ./publish-mac.sh "$rid")
    app_source="$source_dir/artifacts/mac/$rid/QuotaLens.app"
    [[ -d "$app_source" ]] || die "构建没有产出 QuotaLens.app。"
}

case "$mode" in
    release) install_from_release || die "没有可用的 macOS 发布版。可以加 --source 从源码构建。" ;;
    source) build_from_source ;;
    auto)
        if [[ -n "$script_dir" ]]; then
            build_from_source
        else
            install_from_release || build_from_source
        fi
        ;;
esac

if [[ -w /Applications ]]; then dest="/Applications"; else dest="$HOME/Applications"; mkdir -p "$dest"; fi
target="$dest/QuotaLens.app"

# Only the Quota Lens binary itself is stopped, matched by its path inside an app bundle.
running="$(pgrep -f 'QuotaLens\.app/Contents/MacOS/QuotaLens' || true)"
if [[ -n "$running" ]]; then
    say "退出正在运行的 Quota Lens（PID $(echo $running | tr '\n' ' ')）"
    kill $running 2>/dev/null || true
    for _ in $(seq 1 20); do
        pgrep -f 'QuotaLens\.app/Contents/MacOS/QuotaLens' >/dev/null || break
        sleep 0.5
    done
fi

say "安装到 $target"
rm -rf "$target"
ditto "$app_source" "$target"
xattr -dr com.apple.quarantine "$target" 2>/dev/null || true
if ! codesign --verify --deep --strict "$target" 2>/dev/null; then
    codesign --force --deep --sign - "$target" >/dev/null
fi

version="$(defaults read "$target/Contents/Info" CFBundleShortVersionString 2>/dev/null || echo '?')"
say "Quota Lens $version 已安装"

if [[ "$launch" == 1 ]]; then
    open "$target"
    cat <<EOF

已启动，菜单栏会出现 C◎ A◎ 图标，点一下打开额度面板。
• 首次读取 Claude 桌面版登录态时，macOS 会询问是否允许 Quota Lens 使用钥匙串里的“Claude Safe Storage”：
  输入开机密码并选“始终允许”。每次更新后会再问一次。
• Codex 显示“登录已过期”时，打开一次 Codex 让它刷新登录即可。
• 默认登录时自动启动，可在面板底部关闭。
EOF
fi
}

main "$@" </dev/null

#!/usr/bin/env bash
# Builds QuotaLens.app for macOS and zips it. Run on macOS (uses sips, iconutil, codesign, ditto).
#
#   ./publish-mac.sh                       # osx-arm64 and osx-x64
#   ./publish-mac.sh osx-arm64             # one architecture
#   ./publish-mac.sh --from <dir> osx-arm64  # bundle an existing `dotnet publish` output
#                                            # (for example one cross-published on Windows)
#
# Output: artifacts/mac/<rid>/QuotaLens.app and artifacts/QuotaLens-v<version>-macos-<arch>.zip
# The app is ad-hoc signed, which Apple Silicon requires to run it; it is not notarised.
set -euo pipefail
cd "$(dirname "$0")"

from=""
if [[ "${1:-}" == "--from" ]]; then
    from="$2"
    shift 2
fi
rids=("$@")
if [[ ${#rids[@]} -eq 0 ]]; then rids=(osx-arm64 osx-x64); fi

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
packaging=src/QuotaLens.Mac/Packaging

for rid in "${rids[@]}"; do
    out="artifacts/mac/$rid"
    app="$out/QuotaLens.app"
    rm -rf "$out"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

    if [[ -n "$from" ]]; then
        cp -R "$from"/. "$app/Contents/MacOS/"
    else
        dotnet publish src/QuotaLens.Mac/QuotaLens.Mac.csproj \
            -c Release -r "$rid" --self-contained true \
            -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
            -o "$app/Contents/MacOS"
    fi
    chmod +x "$app/Contents/MacOS/QuotaLens"

    sed "s/__VERSION__/$version/g" "$packaging/Info.plist" > "$app/Contents/Info.plist"

    iconset="$out/QuotaLens.iconset"
    mkdir -p "$iconset"
    for size in 16 32 128 256 512; do
        sips -z "$size" "$size" "$packaging/AppIcon.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
        double=$((size * 2))
        sips -z "$double" "$double" "$packaging/AppIcon.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
    done
    iconutil -c icns "$iconset" -o "$app/Contents/Resources/QuotaLens.icns"
    rm -rf "$iconset"

    codesign --force --deep --sign - "$app"
    codesign --verify --deep --strict "$app"

    arch="${rid#osx-}"
    zip="artifacts/QuotaLens-v$version-macos-$arch.zip"
    rm -f "$zip"
    ditto -c -k --keepParent "$app" "$zip"
    echo "Quota Lens $version ($rid): $app, $zip"
done

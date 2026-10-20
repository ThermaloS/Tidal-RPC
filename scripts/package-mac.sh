#!/bin/bash
# Builds TIDAL RPC for Apple Silicon and packages it as a drag-to-Applications disk image in dist/.
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/TidalRpc.Mac/TidalRpc.Mac.csproj)
APP="dist/staging/TIDAL RPC.app"
DMG="dist/TIDAL-RPC-$VERSION-mac-arm64.dmg"

dotnet publish src/TidalRpc.Mac -c Release -r osx-arm64 -o dist/macos >/dev/null
rm -rf dist/staging "$DMG"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp dist/macos/tidal-rpc "$APP/Contents/MacOS/tidal-rpc"
cp scripts/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
sed "s/__VERSION__/$VERSION/g" scripts/Info.plist.template > "$APP/Contents/Info.plist"
# Ad-hoc signature: enough for macOS to run it on this Mac. Downloads still need "Open Anyway" without a Developer ID.
codesign --force --deep -s - "$APP"
ln -s /Applications dist/staging/Applications
cp scripts/READ-ME-FIRST.txt dist/staging/

hdiutil create -quiet -volname "TIDAL RPC $VERSION" -srcfolder dist/staging -ov -format UDZO "$DMG"
echo "wrote $DMG"

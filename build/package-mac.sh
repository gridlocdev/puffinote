#!/usr/bin/env bash
#
# Build a macOS .app bundle for Cellestra.
#
# Publishes the Avalonia desktop app self-contained (no .NET install needed on the target),
# assembles it into Cellestra.app with the project Info.plist, and — when signing credentials
# are present in the environment — code-signs and notarizes it so it opens without Gatekeeper
# warnings. See ARCHITECTURE.md §14.
#
# Usage:
#   build/package-mac.sh                 # build artifacts/Cellestra.app for the host arch
#   RID=osx-x64 build/package-mac.sh     # cross-target Intel
#   CONFIG=Debug build/package-mac.sh    # debug build
#
# Optional signing / notarization (skipped when unset):
#   CODESIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
#   NOTARY_PROFILE="cellestra-notary"    # a profile stored via `xcrun notarytool store-credentials`
#
set -euo pipefail

APP_NAME="Cellestra"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Cellestra.Desktop/Cellestra.Desktop.csproj"
PLIST="$ROOT/src/Cellestra.Desktop/Info.plist"

CONFIG="${CONFIG:-Release}"
case "$(uname -m)" in
    arm64) HOST_RID="osx-arm64" ;;
    *)     HOST_RID="osx-x64" ;;
esac
RID="${RID:-$HOST_RID}"

OUT="${OUT:-$ROOT/artifacts}"
PUBLISH_DIR="$OUT/publish-$RID"
APP="$OUT/$APP_NAME.app"
EXE="$APP/Contents/MacOS/$APP_NAME"

echo "==> Publishing $APP_NAME ($CONFIG, $RID, self-contained)"
rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" \
    -c "$CONFIG" \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -o "$PUBLISH_DIR"

echo "==> Assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Everything the app loads (managed dlls + native dylibs) lives next to the executable.
cp -R "$PUBLISH_DIR/." "$APP/Contents/MacOS/"

cp "$PLIST" "$APP/Contents/Info.plist"

# Optional app icon: drop an .icns at build/Cellestra.icns to brand the bundle.
if [ -f "$ROOT/build/$APP_NAME.icns" ]; then
    cp "$ROOT/build/$APP_NAME.icns" "$APP/Contents/Resources/$APP_NAME.icns"
else
    echo "    (no build/$APP_NAME.icns — bundle will use the generic app icon)"
fi

chmod +x "$EXE"

# Validate the plist so a typo fails the build rather than a launch.
plutil -lint "$APP/Contents/Info.plist" >/dev/null

if [ -n "${CODESIGN_IDENTITY:-}" ]; then
    echo "==> Code-signing with: $CODESIGN_IDENTITY"
    # Sign nested code first, then the bundle, with the hardened runtime (required for notarization).
    find "$APP/Contents/MacOS" -type f \( -name "*.dylib" -o -name "*.so" \) -print0 \
        | xargs -0 -I{} codesign --force --timestamp --options runtime --sign "$CODESIGN_IDENTITY" {}
    codesign --force --timestamp --options runtime --sign "$CODESIGN_IDENTITY" "$EXE"
    codesign --force --timestamp --options runtime --sign "$CODESIGN_IDENTITY" "$APP"
    codesign --verify --deep --strict --verbose=2 "$APP"
else
    echo "==> Skipping code-signing (set CODESIGN_IDENTITY to enable)"
fi

if [ -n "${NOTARY_PROFILE:-}" ]; then
    echo "==> Notarizing via profile: $NOTARY_PROFILE"
    ZIP="$OUT/$APP_NAME.zip"
    ditto -c -k --keepParent "$APP" "$ZIP"
    xcrun notarytool submit "$ZIP" --keychain-profile "$NOTARY_PROFILE" --wait
    xcrun stapler staple "$APP"
    rm -f "$ZIP"
else
    echo "==> Skipping notarization (set NOTARY_PROFILE to enable)"
fi

echo "==> Done: $APP"

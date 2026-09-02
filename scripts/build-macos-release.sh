#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <version> [arm64|x64]" >&2
  exit 64
fi

version="$1"
architecture="${2:-arm64}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "Version must look like 0.1.0 or 0.1.0-beta.1." >&2
  exit 64
fi
if [[ "$architecture" != "arm64" && "$architecture" != "x64" ]]; then
  echo "Architecture must be arm64 or x64." >&2
  exit 64
fi
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "The macOS bundle must be built on macOS." >&2
  exit 69
fi

script_root="$(cd "$(dirname "$0")" && pwd)"
repository_root="$(cd "$script_root/.." && pwd)"
project_path="$repository_root/app/WallpaperWidget/WallpaperWidget.csproj"
platform_root="$repository_root/platform/macos"
release_root="$repository_root/artifacts/release/$version"
publish_path="$release_root/publish-macos-$architecture"
app_path="$release_root/Earth Wallpaper.app"
dmg_path="$release_root/EarthWallpaper-macOS-$architecture-$version.dmg"
contents_path="$app_path/Contents"
macos_path="$contents_path/MacOS"
resources_path="$contents_path/Resources"
frameworks_path="$contents_path/Frameworks"
entitlements_path="$platform_root/EarthWallpaper.entitlements"
dmg_stage_path="$release_root/dmg-stage-$architecture"

case "$release_root" in
  "$repository_root/artifacts/release/"*) ;;
  *) echo "Resolved release output escaped the artifacts directory." >&2; exit 70 ;;
esac

rm -rf "$publish_path" "$app_path" "$dmg_path" "$dmg_stage_path"
mkdir -p "$publish_path" "$macos_path" "$resources_path" "$frameworks_path"

echo "Publishing Earth Wallpaper $version (osx-$architecture)..."
dotnet publish "$project_path" \
  -c Release \
  -r "osx-$architecture" \
  --self-contained true \
  --output "$publish_path" \
  "-p:Version=$version" \
  "-p:IncludeNativeLibrariesForSelfExtract=false"

find "$publish_path" -type f -name '*.pdb' -delete
ditto "$publish_path" "$macos_path"

# macOS code signing expects data under Resources and native libraries under
# Frameworks. Relative links preserve the paths expected by the .NET host.
if [[ -d "$macos_path/Data" ]]; then
  ditto "$macos_path/Data" "$resources_path/Data"
  rm -rf "$macos_path/Data"
  ln -s ../Resources/Data "$macos_path/Data"
fi
for native_library in "$macos_path"/*.dylib; do
  [[ -e "$native_library" ]] || continue
  library_name="$(basename "$native_library")"
  mv "$native_library" "$frameworks_path/$library_name"
  ln -s "../Frameworks/$library_name" "$macos_path/$library_name"
done

echo "Compiling native macOS wallpaper helper..."
swiftc -O \
  "$platform_root/EarthWallpaperMacHelper.swift" \
  -framework AppKit \
  -o "$macos_path/EarthWallpaperMacHelper"

chmod +x "$macos_path/EarthWallpaper" "$macos_path/EarthWallpaperMacHelper"

short_version="${version%%-*}"
bundle_version="$short_version"
if [[ "$version" =~ -[^.]+\.([0-9]+)$ ]]; then
  IFS='.' read -r version_major version_minor _ <<< "$short_version"
  bundle_version="$version_major.$version_minor.${BASH_REMATCH[1]}"
fi
sed \
  -e "s/__SHORT_VERSION__/$short_version/g" \
  -e "s/__BUNDLE_VERSION__/$bundle_version/g" \
  "$platform_root/Info.plist" > "$contents_path/Info.plist"
plutil -lint "$contents_path/Info.plist"

"$macos_path/EarthWallpaper" --smoke-test

signing_identity="${MACOS_SIGNING_IDENTITY:--}"
echo "Signing application bundle with identity: $signing_identity"
if [[ "$signing_identity" == "-" ]]; then
  signing_timestamp=(--timestamp=none)
  signing_runtime=()
else
  signing_timestamp=(--timestamp)
  signing_runtime=(--options runtime)
fi
while IFS= read -r -d '' candidate; do
  if file "$candidate" | grep -q 'Mach-O'; then
    codesign --force "${signing_timestamp[@]}" "${signing_runtime[@]}" --sign "$signing_identity" "$candidate"
  fi
done < <(find "$app_path" -type f -print0)

codesign \
  --force \
  "${signing_timestamp[@]}" \
  "${signing_runtime[@]}" \
  --entitlements "$entitlements_path" \
  --sign "$signing_identity" \
  "$macos_path/EarthWallpaper"
codesign \
  --force \
  "${signing_timestamp[@]}" \
  "${signing_runtime[@]}" \
  --entitlements "$entitlements_path" \
  --sign "$signing_identity" \
  "$app_path"
codesign --verify --deep --strict --verbose=2 "$app_path"

echo "Running signed Avalonia UI smoke test..."
"$macos_path/EarthWallpaper" --ui-smoke-test

echo "Creating macOS disk image..."
mkdir -p "$dmg_stage_path"
ditto "$app_path" "$dmg_stage_path/Earth Wallpaper.app"
ln -s /Applications "$dmg_stage_path/Applications"
hdiutil create \
  -volname "Earth Wallpaper" \
  -srcfolder "$dmg_stage_path" \
  -format UDZO \
  -ov \
  "$dmg_path"

if [[ "$signing_identity" != "-" ]]; then
  codesign --force --timestamp --sign "$signing_identity" "$dmg_path"
fi

if [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" ]]; then
  if [[ "$signing_identity" == "-" ]]; then
    echo "Notarization credentials were supplied, but MACOS_SIGNING_IDENTITY is missing." >&2
    exit 78
  fi
  echo "Submitting DMG for Apple notarization..."
  xcrun notarytool submit "$dmg_path" \
    --apple-id "$APPLE_ID" \
    --team-id "$APPLE_TEAM_ID" \
    --password "$APPLE_APP_PASSWORD" \
    --wait
  xcrun stapler staple "$dmg_path"
  xcrun stapler validate "$dmg_path"
else
  echo "Created an ad-hoc signed beta. Gatekeeper will require Open Anyway."
fi

rm -rf "$dmg_stage_path"
echo "macOS release artifact: $dmg_path"

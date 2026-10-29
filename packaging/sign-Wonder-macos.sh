#!/bin/sh
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
set -eu
[ "$(uname -s)" = Darwin ] || { printf '%s\n' 'Run this local signing step on macOS.' >&2; exit 1; }
app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
app="$app_dir/Wonder Launcher.app"
chmod +x "$app/Contents/MacOS/Wonder.Desktop"
# Local ad hoc signature only. This is not Developer ID signing or Apple notarization.
/usr/bin/codesign --force --sign - --identifier org.wonderlauncher.desktop \
  --entitlements "$app_dir/Wonder.macOS.entitlements.plist" "$app"
/usr/bin/codesign --verify --strict --verbose=2 "$app"
printf '%s\n' 'Local trial signature applied. Developer ID signing and notarization are still pending.'

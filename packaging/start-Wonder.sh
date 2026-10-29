#!/bin/sh
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
set -eu
app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
if [ "$(uname -s)" != Linux ] || [ "$(uname -m)" != x86_64 ]; then
    printf '%s\n' 'This preview requires Linux x86_64 (glibc).' >&2
    exit 1
fi
if [ -z "${DISPLAY:-}" ]; then
    printf '%s\n' 'Start Wonder in an X11 desktop session, or enable XWayland in your Wayland session.' >&2
    exit 1
fi
if [ ! -w "$app_dir" ]; then
    printf '%s\n' 'Extract Wonder into a writable folder. WonderData is stored beside the application.' >&2
    exit 1
fi
if [ ! -x "$app_dir/Wonder.Desktop" ]; then
    printf '%s\n' 'Missing executable permission. Run: chmod +x Wonder.Desktop start-Wonder.sh' >&2
    exit 1
fi
if ! command -v secret-tool >/dev/null 2>&1; then
    printf '%s\n' 'Account saving requires secret-tool and an unlocked Secret Service keyring. See Guides/31-linux-trial.md.' >&2
fi
# Keep data private and never inherit a setting that disables single-instance locking.
umask 077
unset DOTNET_SYSTEM_IO_DISABLEFILELOCKING
exec "$app_dir/Wonder.Desktop" "$@"

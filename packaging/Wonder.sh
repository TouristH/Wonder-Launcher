#!/bin/sh
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
set -eu
app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
. "$app_dir/Wonder.Runtime.sh"
[ "$(uname -m)" = x86_64 ] || { printf '%s\n' 'This package requires Linux x64 (glibc).' >&2; exit 1; }
[ -x "$app_dir/Wonder.Desktop" ] || { printf '%s\n' 'Extract the complete lightweight tar.gz and preserve executable permissions.' >&2; exit 1; }
install="${XDG_DATA_HOME:-$HOME/.local/share}/Wonder/dotnet/x64/$wonder_runtime_version"
# Candidate paths are newline-delimited, never split on spaces or interpreted as shell code.
set --
set -f
old_ifs=$IFS
IFS='
'
for candidate in $(wonder_runtime_candidates); do set -- "$@" "$candidate"; done
IFS=$old_ifs
runtime=$(wonder_resolve_runtime "$install" "$@") || exit $?
export DOTNET_ROOT="$runtime" DOTNET_ROOT_X64="$runtime"
cd -- "$app_dir"
exec "$app_dir/Wonder.Desktop"

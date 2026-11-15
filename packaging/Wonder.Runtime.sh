#!/bin/sh
# SPDX-License-Identifier: GPL-3.0-only
# Additional terms: see ADDITIONAL_TERMS.md in the repository root.
# Sourceable functions allow validation using explicit synthetic paths, without changing the system.
wonder_runtime_version='10.0.15'
wonder_runtime_url='https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.15/dotnet-runtime-10.0.15-linux-x64.tar.gz'
wonder_runtime_sha512='a90177d75bc891f71aa814dde3bcf76a356f5a506824669f1891754457954a3d13b72f3ca04c98fa0fe588514a5a781f8a87b17930ee802105c14f9edb28318a'

wonder_has_runtime() {
    [ -x "$1/dotnet" ] || return 1
    # ELF64, little-endian, AMD64. Do not accidentally reuse an ARM or x86 host.
    [ "$(od -An -tx1 -N6 "$1/dotnet" 2>/dev/null | tr -d ' \n')" = '7f454c460201' ] || return 1
    [ "$(od -An -tx1 -j18 -N2 "$1/dotnet" 2>/dev/null | tr -d ' \n')" = '3e00' ] || return 1
    "$1/dotnet" --list-runtimes 2>/dev/null | grep -Eq '^Microsoft\.NETCore\.App 10\.0\.[0-9]+ \['
}

wonder_runtime_candidates() {
    for registration in /etc/dotnet/install_location_x64 /etc/dotnet/install_location; do
        if [ -r "$registration" ]; then head -n 1 "$registration"; fi
    done
    printf '%s\n' /usr/share/dotnet /usr/lib/dotnet /usr/local/share/dotnet
    [ -z "${DOTNET_ROOT_X64:-}" ] || printf '%s\n' "$DOTNET_ROOT_X64"
    [ -z "${DOTNET_ROOT:-}" ] || printf '%s\n' "$DOTNET_ROOT"
    host=$(command -v dotnet 2>/dev/null || true)
    if [ -n "$host" ]; then dirname "$(readlink -f "$host")"; fi
}

wonder_confirm_runtime() {
    message="Wonder needs Microsoft .NET $wonder_runtime_version x64. Download it from Microsoft, verify SHA-512, and install for this user?\n\nLocation: $1\n\nNo sudo or global PATH changes. This is separate from Minecraft/Java."
    if [ -t 0 ]; then
        printf '%b\nInstall now? [y/N] ' "$message" >&2
        IFS= read -r answer || return 1
        case "$answer" in y|Y|yes|YES|Yes) return 0 ;; *) return 1 ;; esac
    elif [ -n "${DISPLAY:-}" ] && command -v xmessage >/dev/null 2>&1; then
        xmessage -center -buttons 'Install:0,Cancel:1' -default Cancel "$(printf '%b' "$message")"
    else
        printf '%s\n' 'Runtime not installed. Run ./Wonder.sh from a terminal to review and approve installation.' >&2
        return 1
    fi
}

wonder_install_runtime() (
    install=$1
    parent=$(dirname "$install")
    mkdir -p "$parent" || exit 1
    stage=$(mktemp -d "$parent/.wonder-install-XXXXXX") || exit 1
    archive="$stage.tar.gz"
    trap 'rm -f -- "$archive"; rm -rf -- "$stage"' EXIT
    trap 'exit 1' HUP INT TERM
    for utility in curl tar sha512sum; do
        command -v "$utility" >/dev/null 2>&1 || { printf 'Required utility missing: %s\n' "$utility" >&2; exit 1; }
    done
    printf 'Downloading Microsoft .NET %s...\n' "$wonder_runtime_version" >&2
    curl --fail --show-error --location --proto '=https' --proto-redir '=https' --connect-timeout 20 --max-time 300 --output "$archive" "$wonder_runtime_url" || exit 1
    printf '%s  %s\n' "$wonder_runtime_sha512" "$archive" | sha512sum --check --status || {
        printf '%s\n' 'The Microsoft runtime SHA-512 check failed. Nothing was installed; run Wonder.sh again to retry.' >&2
        exit 1
    }
    printf '%s\n' 'SHA-512 verified. Installing the runtime for this user...' >&2
    tar -xzf "$archive" -C "$stage" || exit 1
    wonder_has_runtime "$stage" || {
        printf '%s\n' 'The runtime cannot run. Check the distribution .NET 10 native-library prerequisites in Guides/37-lightweight-distribution.md.' >&2
        exit 1
    }
    if [ -e "$install" ]; then
        wonder_has_runtime "$install" || { printf 'The runtime folder is incomplete. Rename it and retry: %s\n' "$install" >&2; exit 1; }
    else
        # -T avoids nesting this install inside a folder created by a concurrent first launch.
        mv -T -- "$stage" "$install" || wonder_has_runtime "$install" || exit 1
    fi
)

wonder_resolve_runtime() {
    install=$1
    shift
    for candidate do
        if wonder_has_runtime "$candidate"; then printf 'Using .NET 10: %s\n' "$candidate" >&2; printf '%s\n' "$candidate"; return 0; fi
    done
    if wonder_has_runtime "$install"; then printf 'Using .NET 10: %s\n' "$install" >&2; printf '%s\n' "$install"; return 0; fi
    wonder_confirm_runtime "$install" || { printf '%s\n' 'Installation cancelled. No application data was changed.' >&2; return 2; }
    wonder_install_runtime "$install" || return 1
    printf '%s\n' "$install"
}

#!/bin/bash
#
# Installs (or removes) the one sudo rule that lets tools/macos-verify.ps1 run the check mac.socket-other-user without a
# password (T-140, user decision 2026-11-02): the user who runs the script may start /usr/bin/nc as the user nobody,
# nothing else. nc as nobody has fewer rights than the user already has; the check uses it to connect to the instance
# socket of the test app as another UID, which the app must refuse (T-053).
#
#   sudo bash tools/macos-verify-sudoers.sh            install /etc/sudoers.d/layerpeek-verify for the user who ran sudo
#   sudo bash tools/macos-verify-sudoers.sh --remove   remove it again
#   bash tools/macos-verify-sudoers.sh --print [user]  only print the rule (no sudo needed)
#
# The rule is checked with visudo before it is installed, so a broken file never reaches /etc/sudoers.d.

set -euo pipefail

target=/etc/sudoers.d/layerpeek-verify

rule() {
    printf '# tools/macos-verify.ps1, check mac.socket-other-user (T-140): nc as nobody without a password, nothing else.\n'
    printf '%s ALL=(nobody) NOPASSWD: /usr/bin/nc\n' "$1"
}

case "${1:-}" in
    --print)
        rule "${2:-$(id -un)}"
        exit 0
        ;;
    --remove)
        [ "$(id -u)" -eq 0 ] || { echo "Run it with sudo." >&2; exit 2; }
        rm -f "$target"
        echo "Removed $target."
        exit 0
        ;;
    "")
        ;;
    *)
        echo "Unknown argument '$1' (use --print or --remove)." >&2
        exit 2
        ;;
esac

[ "$(id -u)" -eq 0 ] || { echo "Run it with sudo: sudo bash tools/macos-verify-sudoers.sh" >&2; exit 2; }
user="${SUDO_USER:-}"
if [ -z "$user" ] || [ "$user" = root ]; then
    echo "Run it with sudo from your own account (SUDO_USER is not set)." >&2
    exit 2
fi
case "$user" in
    *[!A-Za-z0-9._-]*) echo "Unexpected user name '$user'." >&2; exit 2 ;;
esac

staged="$(mktemp /tmp/layerpeek-verify-sudoers.XXXXXX)"
trap 'rm -f "$staged"' EXIT
rule "$user" > "$staged"
/usr/sbin/visudo -cq -f "$staged"
install -m 0440 -o root -g wheel "$staged" "$target"
echo "Installed $target:"
cat "$target"

#!/bin/bash
#
# Builds the native FFmpeg libraries of the camera for osx-arm64 (T-026) from the official FFmpeg source tarball:
# shared, LGPL version 3 or later (no --enable-gpl, no --enable-nonfree), software decoding only (no VideoToolbox),
# only what LayerPeek.Camera.FFmpeg uses (RTSP over TCP and raw H.264 files, the H.264 decoder, swscale to BGRA).
#
# Called by build/FFmpeg.targets on the first macOS build (the tarball is already downloaded and SHA256-verified there);
# it can also be run by hand with the same arguments. Needs only the Xcode Command Line Tools (clang, make, otool, lipo)
# and the tools of macOS; nothing is installed. The cache key in build/FFmpeg.targets includes this file, so any change
# here (flags, version) builds into a new cache folder.
#
#   build-ffmpeg.sh <tarball> <top folder inside the tarball> <work folder (created)> <output folder (must not exist)>
#
# Output: <output>/lib/libavformat.63.dylib, libavcodec.63.dylib, libavutil.61.dylib, libswscale.10.dylib (install names
# @loader_path/..., so they load from the folder they are in), <output>/LICENSE.txt (COPYING.LGPLv3 of the source) and
# <output>/BUILD.txt (version, source and the exact configure line).

set -euo pipefail

if [ "$#" -ne 4 ]; then
    echo "usage: $0 <tarball> <archive folder> <work folder> <output folder>" >&2
    exit 2
fi

TARBALL="$1"
ARCHIVE_FOLDER="$2"
WORK="$3"
OUT="$4"

# LSMinimumSystemVersion of build/macos/Info.plist (a unit test keeps both equal).
MIN_MACOS="14.0"

# The libraries the application loads (FFmpeg.AutoGen names avformat-63, avcodec-63, avutil-61, swscale-10).
LIBRARIES="libavformat.63.dylib libavcodec.63.dylib libavutil.61.dylib libswscale.10.dylib"

# The configure line (documented in tasks/done/T-026 and docs/prerequisites.md; keep them in sync).
CONFIGURE_FLAGS=(
    --enable-shared --disable-static
    --enable-version3
    --disable-programs --disable-doc --disable-debug
    --disable-autodetect
    --disable-everything
    --disable-avdevice --disable-avfilter --disable-swresample
    --enable-network
    --enable-protocol=tcp,file
    --enable-demuxer=rtsp,h264
    --enable-parser=h264
    --enable-decoder=h264
    --arch=aarch64
    --install-name-dir=@loader_path
    "--extra-cflags=-arch arm64 -mmacosx-version-min=$MIN_MACOS"
    "--extra-ldflags=-arch arm64 -mmacosx-version-min=$MIN_MACOS"
)

fail() {
    echo "build-ffmpeg.sh: $*" >&2
    exit 1
}

[ -f "$TARBALL" ] || fail "the source tarball $TARBALL does not exist."
[ ! -e "$OUT" ] || fail "the output folder $OUT already exists."
/usr/bin/xcrun --find clang > /dev/null 2>&1 || fail "clang was not found: install the Xcode Command Line Tools (xcode-select --install)."

mkdir -p "$WORK"
/usr/bin/tar -xf "$TARBALL" -C "$WORK"
SOURCE="$WORK/$ARCHIVE_FOLDER"
[ -x "$SOURCE/configure" ] || fail "the tarball has no $ARCHIVE_FOLDER/configure."
grep -q "^9\.0\." "$SOURCE/VERSION" || fail "the tarball is not FFmpeg 9.0.x (VERSION: $(cat "$SOURCE/VERSION" 2>/dev/null))."
PREFIX="$WORK/prefix"

# A clean environment: only the system tools and the Command Line Tools, so Homebrew (pkg-config, libraries, flags in
# CFLAGS/LDFLAGS/PKG_CONFIG_PATH) can never leak into the build.
CLEAN_ENV=(/usr/bin/env -i "HOME=$HOME" "TMPDIR=${TMPDIR:-/tmp}" "PATH=/usr/bin:/bin:/usr/sbin:/sbin" "MACOSX_DEPLOYMENT_TARGET=$MIN_MACOS")
if [ -n "${DEVELOPER_DIR:-}" ]; then
    CLEAN_ENV+=("DEVELOPER_DIR=$DEVELOPER_DIR")
fi

JOBS="$(/usr/sbin/sysctl -n hw.ncpu 2> /dev/null || echo 4)"

echo "Configuring FFmpeg $(cat "$SOURCE/VERSION") ..."
(cd "$SOURCE" && "${CLEAN_ENV[@]}" ./configure "--prefix=$PREFIX" "${CONFIGURE_FLAGS[@]}" > "$WORK/configure.log" 2>&1) \
    || { tail -n 30 "$WORK/configure.log" >&2; fail "configure failed (log: $WORK/configure.log)."; }
grep -q "^License: LGPL version 3 or later" "$WORK/configure.log" || fail "configure did not select the LGPL 3 license."

echo "Compiling FFmpeg ($JOBS jobs) ..."
(cd "$SOURCE" && "${CLEAN_ENV[@]}" make "-j$JOBS" > "$WORK/make.log" 2>&1 && "${CLEAN_ENV[@]}" make install > "$WORK/install.log" 2>&1) \
    || { tail -n 30 "$WORK/make.log" >&2; fail "make failed (log: $WORK/make.log)."; }

STAGE="$WORK/out"
mkdir -p "$STAGE/lib"
for library in $LIBRARIES; do
    # The real file under its install name (the versioned names are symbolic links).
    cp -L "$PREFIX/lib/$library" "$STAGE/lib/$library"
    archs="$(/usr/bin/lipo -archs "$STAGE/lib/$library")"
    [ "$archs" = "arm64" ] || fail "$library is built for '$archs', not arm64."
    # Nothing but the sibling libraries (@loader_path) and the system: never Homebrew (/opt/homebrew, /usr/local).
    foreign="$(/usr/bin/otool -L "$STAGE/lib/$library" | tail -n +2 | awk '{print $1}' \
        | grep -v -E '^(@loader_path/lib(avformat|avcodec|avutil|swscale)\.[0-9]+\.dylib|/usr/lib/|/System/Library/)' || true)"
    [ -z "$foreign" ] || fail "$library depends on $foreign."
done

cp "$SOURCE/COPYING.LGPLv3" "$STAGE/LICENSE.txt"
{
    echo "FFmpeg $(cat "$SOURCE/VERSION") (official source tarball $(basename "$TARBALL")), built by build/macos/build-ffmpeg.sh"
    echo "License: LGPL version 3 or later"
    echo "Toolchain: $("${CLEAN_ENV[@]}" clang --version | head -n 1)"
    echo "configure --prefix=<staging> ${CONFIGURE_FLAGS[*]}"
} > "$STAGE/BUILD.txt"

mv "$STAGE" "$OUT"
echo "FFmpeg libraries ready in $OUT"

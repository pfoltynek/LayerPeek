#!/bin/bash
#
# Builds the native FFmpeg libraries of the camera for Windows (T-202) from the official FFmpeg source tarball, the same
# one as macOS (build/macos/build-ffmpeg.sh): shared, LGPL version 3 or later (no --enable-gpl, no --enable-nonfree),
# only what LayerPeek.Camera.FFmpeg uses (RTSP over TCP, raw H.264 files and the app's own MP4 recordings, the H.264
# decoder, swscale to BGRA), the MP4 muxer for camera recording (remux, no encoder) and hardware decoding of H.264
# through D3D11VA (Direct3D 11 of Windows, user decision 2026-11-12). No external library.
#
# Toolchain: MSYS2 with MinGW-w64 (user decision 2026-11-12), packages make, diffutils, mingw-w64-x86_64-gcc,
# mingw-w64-x86_64-nasm (docs/prerequisites.md). Nothing is installed. build/FFmpeg.targets runs this script with the
# bash of MSYS2 on the first Windows build (the tarball is already downloaded and SHA256-verified there); it can also
# be run by hand with the same arguments from any MSYS2 shell. The cache key in build/FFmpeg.targets includes this file,
# so any change here (flags, version) builds into a new cache folder.
#
#   build-ffmpeg.sh <runtime identifier> <tarball> <top folder inside the tarball> <work folder (created)> <output folder (must not exist)>
#
# Runtime identifier: win-x64 (x86_64-w64-mingw32 of MSYS2 MINGW64). Only this one today; win-arm64 (llvm-mingw cross
# build) is added by T-225 as another case below. Paths may be Windows (D:\...) or MSYS2 (/d/...) paths.
#
# Output: <output>/bin/avformat-63.dll, avcodec-63.dll, avutil-61.dll, swscale-10.dll (they depend only on each other and
# on DLLs of Windows: the GCC runtime is linked statically and FFmpeg uses Windows threads, so no libgcc_s / libwinpthread
# DLL), <output>/LICENSE.txt (COPYING.LGPLv3 of the source) and <output>/BUILD.txt (version, source, toolchain and the
# exact configure line).

set -euo pipefail

# Only the tools of MSYS2 until the clean environment below (bash.exe started by MSBuild inherits the Windows PATH).
export PATH=/usr/bin

fail() {
    echo "build-ffmpeg.sh: $*" >&2
    exit 1
}

if [ "$#" -ne 5 ]; then
    echo "usage: $0 <runtime identifier> <tarball> <archive folder> <work folder> <output folder>" >&2
    exit 2
fi

RID="$1"
TARBALL="$(cygpath -u "$2")"
ARCHIVE_FOLDER="$3"
WORK="$(cygpath -u "$4")"
OUT="$(cygpath -u "$5")"

# The libraries the application loads (FFmpeg.AutoGen names avformat-63, avcodec-63, avutil-61, swscale-10).
LIBRARIES="avformat-63.dll avcodec-63.dll avutil-61.dll swscale-10.dll"

# Per runtime identifier: the MSYS2 environment of the toolchain, configure's architecture and the PE format the DLLs
# must have (objdump -f). T-225 adds win-arm64 here.
case "$RID" in
    win-x64)
        TOOLCHAIN=/mingw64
        MSYSTEM_NAME=MINGW64
        ARCH=x86_64
        PE_FORMAT=pei-x86-64
        MSYS2_PACKAGES="mingw-w64-x86_64-crt mingw-w64-x86_64-winpthreads mingw-w64-x86_64-binutils mingw-w64-x86_64-gcc"
        ;;
    *)
        fail "the runtime identifier '$RID' is not supported (only win-x64; win-arm64 follows in T-225)."
        ;;
esac

# The DLLs of Windows the libraries may import (objdump -p, "DLL Name"), compared case-insensitively. Anything else,
# e.g. libgcc_s_seh-1.dll, libwinpthread-1.dll or zlib1.dll of MSYS2, fails the build. msvcrt.dll is the C runtime of
# the MINGW64 environment (part of every Windows); d3d11.dll and dxgi.dll are loaded at run time (LoadLibrary), only
# when hardware decoding is used.
SYSTEM_DLLS="kernel32.dll msvcrt.dll user32.dll advapi32.dll bcrypt.dll ws2_32.dll ole32.dll shell32.dll"

# The configure line (documented in docs/prerequisites.md; keep it in sync). The same set as macOS:
# --enable-muxer=mp4 (T-147): recording remuxes the RTSP H.264 packets into fragmented MP4 (configure adds the mov
# muxer); the mov demuxer that reads the app's own MP4 recordings and timelapses (T-165) comes with the RTSP demuxer
# (rtsp_demuxer_select), exactly as on macOS. Windows only adds what the platform needs:
# --target-os=mingw32 --arch: the MinGW-w64 target. --enable-w32threads --disable-pthreads: threads of Windows itself, so
# nothing of winpthreads is linked (MSYS2's GCC uses the posix thread model). Sockets (Winsock, ws2_32.dll) come with
# --enable-network. --enable-d3d11va --enable-hwaccel=h264_d3d11va,h264_d3d11va2 (T-202, for T-206): needed explicitly
# because of --disable-autodetect and --disable-everything. Both hwaccels are D3D11VA: h264_d3d11va is the old API
# (the application hands FFmpeg its own ID3D11VideoContext, no device type), h264_d3d11va2 the one of the hwcontext
# d3d11va (AV_PIX_FMT_D3D11, av_hwdevice_ctx_create), which T-206 and FFmpegRuntime.HasHardwareDecoder use. D3D11VA uses
# only the headers of MinGW-w64 and DLLs of Windows (no SDK, no external library), neither GPL nor nonfree, the license
# stays LGPL 3. No DXVA2 or D3D12VA (user decision 2026-11-12).
# -static-libgcc: the GCC runtime (libgcc, GCC Runtime Library Exception) goes into the DLLs, no libgcc_s_seh-1.dll.
# -l:libwinpthread.a: even with Windows threads avutil needs clock_gettime and nanosleep, which MinGW-w64 implements in
# winpthreads (MIT and BSD-3-Clause); its static archive goes into the DLLs instead of libwinpthread-1.dll. The notices
# of the statically linked MinGW-w64 runtime and winpthreads are in the licences window (ThirdPartyLicenses, T-202).
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
    --enable-muxer=mp4
    --enable-parser=h264
    --enable-decoder=h264
    --enable-d3d11va --enable-hwaccel=h264_d3d11va,h264_d3d11va2
    --target-os=mingw32
    --arch=$ARCH
    --enable-w32threads --disable-pthreads
    --extra-ldflags=-static-libgcc
    --extra-libs=-l:libwinpthread.a
)

[ -f "$TARBALL" ] || fail "the source tarball $TARBALL does not exist."
[ ! -e "$OUT" ] || fail "the output folder $OUT already exists."
for tool in gcc nasm objdump strip; do
    [ -x "$TOOLCHAIN/bin/$tool.exe" ] || fail "$TOOLCHAIN/bin/$tool.exe was not found: install the MSYS2 packages make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm (docs/prerequisites.md)."
done
for tool in make cmp tar xz; do
    [ -x "/usr/bin/$tool.exe" ] || fail "/usr/bin/$tool.exe was not found: install the MSYS2 packages make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm (docs/prerequisites.md)."
done

mkdir -p "$WORK"

# The temporary folder of configure and the compiler: inside the work folder. configure of FFmpeg 9.0.2 does not quote
# it (tmpfile: exec > $tmp), so it must contain no space; %TEMP% may (a profile name with a space). A work folder with a
# space falls back to its short 8.3 form; without one the build stops here, before the long compile.
BUILD_TMP="$WORK/tmp"
mkdir -p "$BUILD_TMP"
case "$BUILD_TMP" in
    *" "*) short="$(cygpath -d "$BUILD_TMP")" && [ -n "$short" ] \
               || fail "cannot get the short 8.3 name of $BUILD_TMP. Set the property or environment variable LayerPeekFFmpegCacheRoot to a folder without spaces (docs/prerequisites.md)."
           BUILD_TMP="$(cygpath -u "$short")" ;;
esac
case "$BUILD_TMP" in
    *" "*) fail "the work folder $WORK contains a space and has no short 8.3 name; FFmpeg's configure cannot use it. Set the property or environment variable LayerPeekFFmpegCacheRoot to a folder without spaces (docs/prerequisites.md)." ;;
esac

tar -xf "$TARBALL" -C "$WORK"
SOURCE="$WORK/$ARCHIVE_FOLDER"
[ -f "$SOURCE/configure" ] || fail "the tarball has no $ARCHIVE_FOLDER/configure."
grep -q "^9\.0\." "$SOURCE/VERSION" || fail "the tarball is not FFmpeg 9.0.x (VERSION: $(cat "$SOURCE/VERSION" 2>/dev/null))."
PREFIX="$WORK/prefix"

# A clean environment: only the toolchain and the tools of MSYS2, so nothing of the machine (other compilers on PATH,
# CFLAGS / LDFLAGS / PKG_CONFIG_PATH, other MSYS2 environments) can leak into the build. SYSTEMROOT, WINDIR and the
# temporary folder (BUILD_TMP above) are needed by the Windows programs of the toolchain, COMSPEC by windres (it runs the
# preprocessor for the version resources of the DLLs through popen, i.e. cmd.exe).
CLEAN_ENV=(/usr/bin/env -i "HOME=$WORK" "TMPDIR=$BUILD_TMP" "TEMP=$BUILD_TMP" "TMP=$BUILD_TMP"
    "SYSTEMROOT=${SYSTEMROOT:-C:\\Windows}" "WINDIR=${WINDIR:-C:\\Windows}" "COMSPEC=${COMSPEC:-C:\\Windows\\system32\\cmd.exe}"
    "MSYSTEM=$MSYSTEM_NAME" "PATH=$TOOLCHAIN/bin:/usr/bin")

JOBS="$(nproc 2> /dev/null || echo 4)"

# The packages of the toolchain for BUILD.txt, read before the long compile: the MinGW-w64 runtime (crt, winpthreads) is
# linked into the DLLs and its version must match the licences window (ThirdPartyLicenses, "MinGW-w64 runtime";
# MinGWRuntimeVersionIsTheOneThatBuiltTheDlls).
PACKAGES="$(/usr/bin/pacman -Q $MSYS2_PACKAGES 2> /dev/null | tr '\n' ' ' | sed 's/ $//')"
[ -n "$PACKAGES" ] || fail "pacman -Q $MSYS2_PACKAGES returned nothing; check the MSYS2 installation (docs/prerequisites.md)."

echo "Configuring FFmpeg $(cat "$SOURCE/VERSION") for $RID ..."
(cd "$SOURCE" && "${CLEAN_ENV[@]}" ./configure "--prefix=$PREFIX" "${CONFIGURE_FLAGS[@]}" > "$WORK/configure.log" 2>&1) \
    || { tail -n 30 "$WORK/configure.log" >&2; fail "configure failed (log: $WORK/configure.log)."; }
grep -q "^License: LGPL version 3 or later" "$WORK/configure.log" || fail "configure did not select the LGPL 3 license."
grep -q "^threading support *w32threads" "$WORK/configure.log" || fail "configure did not select the threads of Windows (w32threads)."

echo "Compiling FFmpeg ($JOBS jobs) ..."
(cd "$SOURCE" && "${CLEAN_ENV[@]}" make "-j$JOBS" > "$WORK/make.log" 2>&1 && "${CLEAN_ENV[@]}" make install > "$WORK/install.log" 2>&1) \
    || { tail -n 30 "$WORK/make.log" >&2; fail "make failed (log: $WORK/make.log)."; }

STAGE="$WORK/out"
mkdir -p "$STAGE/bin"
for library in $LIBRARIES; do
    [ -f "$PREFIX/bin/$library" ] || fail "make install did not produce bin/$library."
    cp "$PREFIX/bin/$library" "$STAGE/bin/$library"
    # objdump runs in the folder with the bare file name: it prints paths in the ANSI code page of Windows, which sed
    # (UTF-8 locale of MSYS2) cannot match when the profile path has diacritics (an a-acute in C:\Users\Novak). LC_ALL=C
    # as well. This script stays ASCII (the release source archive tests compare it byte for byte).
    header="$(cd "$STAGE/bin" && "$TOOLCHAIN/bin/objdump.exe" -f "$library")" || fail "objdump -f $library failed."
    format="$(printf '%s\n' "$header" | LC_ALL=C sed -n 's/.*file format \([^ ]*\).*/\1/p' | head -n 1)"
    [ "$format" = "$PE_FORMAT" ] || fail "$library has the format '$format', not $PE_FORMAT."
    # Nothing but the sibling libraries and DLLs of Windows (like otool -L on macOS): never a DLL of MSYS2. An empty list
    # (objdump failed or changed its output) fails too: every DLL imports KERNEL32.dll.
    private="$(cd "$STAGE/bin" && "$TOOLCHAIN/bin/objdump.exe" -p "$library")" || fail "objdump -p $library failed."
    imports="$(printf '%s\n' "$private" | LC_ALL=C sed -n 's/^[[:space:]]*DLL Name: //p' | tr -d '\r')"
    printf '%s\n' "$imports" | grep -qix 'kernel32.dll' || fail "objdump -p lists no KERNEL32.dll import of $library (imports: '$imports')."
    foreign=""
    for imported in $imports; do
        lower="$(echo "$imported" | tr '[:upper:]' '[:lower:]')"
        case " $LIBRARIES $SYSTEM_DLLS " in
            *" $lower "*) ;;
            *) foreign="$foreign $imported" ;;
        esac
    done
    [ -z "$foreign" ] || fail "$library depends on$foreign."
done

cp "$SOURCE/COPYING.LGPLv3" "$STAGE/LICENSE.txt"
{
    echo "FFmpeg $(cat "$SOURCE/VERSION") (official source tarball $(basename "$TARBALL")), built by build/windows/build-ffmpeg.sh for $RID"
    echo "License: LGPL version 3 or later"
    echo "Toolchain: MSYS2 $MSYSTEM_NAME, $("${CLEAN_ENV[@]}" gcc --version | head -n 1), $("${CLEAN_ENV[@]}" nasm -v | head -n 1)"
    echo "Packages: $PACKAGES"
    echo "configure --prefix=<staging> ${CONFIGURE_FLAGS[*]}"
} > "$STAGE/BUILD.txt"

mv "$STAGE" "$OUT"
echo "FFmpeg libraries ready in $OUT"

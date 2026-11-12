---
id: T-202
title: Windows – vlastní minimální LGPL build FFmpeg ze zdrojů (místo BtbN), s D3D11VA
status: todo
owner: core-dev
priority: P1
platform: windows
depends_on: [T-199]
---

> **Rozhodnutí uživatele 2026-11-12 (T-199, otázka 2, varianta c):** na Windows se přestane používat build BtbN
> `win64-lgpl-shared` (staticky v něm je asi 50 externích knihoven) a FFmpeg se bude překládat ze zdrojů jako na macOS
> (T-026): jen to, co LayerPeek potřebuje, žádné externí knihovny.
>
> **Rozhodnutí uživatele 2026-11-12 (T-192):** hardwarové dekódování se bude implementovat na obou OS najednou, proto
> build Windows **musí obsahovat D3D11VA** (hwaccel `h264_d3d11va`, hwcontext `d3d11va`, bez externích knihoven).
>
> **Blokuje vydání 1.0.0** (T-198, oddíl 1.1, bloker B10).

## Cíl
Dnešní DLL BtbN obsahují kromě FFmpeg i asi 50 staticky přilinkovaných knihoven (`libdav1d`, `libaom`, `libvpx`,
`libass`, `gmp`, `libssh`, `vulkan`, `amf`, `libvpl`…, řádek configure v `avcodec-63.dll`, viz T-199 otázka 2). Jejich
zdroje vydání nepřikládá (bod 18 kontrolního seznamu https://ffmpeg.org/legal.html), DLL mají ~77 MB zipu a každá
knihovna rozšiřuje útočnou plochu dekódování dat z nedůvěryhodné sítě (T-050), i když ji aplikace nikdy nepoužije.
Vlastní minimální build:
- archiv zdroje u vydání obsahuje jen zdroj FFmpeg a skript buildu (žádné skripty BtbN ani odkazy na zdroje cizích knihoven),
- menší DLL (macOS dnes 4 knihovny ≈ 4,5 MB) a menší útočná plocha,
- stejná sada funkcí na obou OS (macOS i Windows ze stejného oficiálního tarballu),
- žádná závislost na tom, jestli BtbN připnutý build ještě drží (T-162).

## Rozsah
- **Skript buildu Windows** (např. `build/windows/build-ffmpeg.*`) podle `build/macos/build-ffmpeg.sh`: stejný oficiální
  tarball `ffmpeg-<verze>.tar.xz` jako macOS (`LayerPeekFFmpegMacSourceUrl` / `…Sha256` se stanou společnými pro oba OS,
  přejmenovat), sdílené knihovny, `--enable-version3`, bez `--enable-gpl` / `--enable-nonfree`, `--disable-autodetect`,
  `--disable-everything`, kontrola `License: LGPL version 3 or later` v `configure.log`, výstup `LICENSE.txt`
  (`COPYING.LGPLv3`) a `BUILD.txt` (verze, zdroj, toolchain, přesný řádek configure).
- **Sada funkcí stejná jako macOS, kde to jde:** `--disable-programs --disable-doc --disable-debug`,
  `--disable-avdevice --disable-avfilter --disable-swresample`, `--enable-network`, `--enable-protocol=tcp,file`,
  `--enable-demuxer=rtsp,h264` (demuxer `mov` pro čtení vlastních MP4 nahrávek / timelapse, T-165, musí být k dispozici
  stejně jako na macOS – ověřit, `FFmpegDecoder` má `format_whitelist` `h264,mov`), `--enable-muxer=mp4`,
  `--enable-parser=h264`, `--enable-decoder=h264`. Windows navíc jen to, co potřebuje platforma (vlákna, sockety Winsock).
- **D3D11VA (povinné, rozhodnutí 2026-11-12):** `--enable-d3d11va --enable-hwaccel=h264_d3d11va` (hwcontext `d3d11va`),
  jen systémové knihovny Windows (`d3d11.dll`, `dxgi.dll`), žádné externí knihovny ani SDK mimo toolchain. DXVA2 / D3D12VA
  jen po rozhodnutí uživatele (otázka 2). Samotné použití hardwarového dekódování v aplikaci je úkol implementace T-192,
  ne tento.
- **Knihovny:** `avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll`; `swresample-7.dll` odpadá
  (v BtbN je jen závislostí, nic ji nenačítá – stejně jako na macOS). Upravit `_LayerPeekFFmpegLibrary` v
  `build/FFmpeg.targets`, `FFmpegRuntime` a testy. Kontrola závislostí DLL (jako `otool -L` na macOS): jen sourozenecké
  knihovny FFmpeg a systémové DLL Windows; u MinGW žádné `libgcc` / `libwinpthread` DLL (staticky, nebo výslovně
  přibalené a uvedené v licencích).
- **Toolchain:** podle odpovědi na otázku 1 (MSYS2/MinGW, nebo MSVC). Bez instalace čehokoli při buildu: chybějící
  toolchain = srozumitelná chyba hned na začátku (jako hláška Xcode Command Line Tools na macOS) s návodem a únikem
  `-p:LayerPeekFFmpegEnabled=false`.
- **Cache jako na macOS:** `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\win-x64-<hash zdroje a skriptu>\`, první build přeloží,
  další jen zkontrolují cache, `tmp-…` s logy po chybě, úklid starých pracovních složek stejně jako macOS.
- **`build/FFmpeg.targets`:** odstranit `LayerPeekFFmpegUrl` / `…Sha256` / `…ArchiveFolder` (zip BtbN) a
  `LayerPeekFFmpegWindowsSource*` / `LayerPeekFFmpegWindowsScripts*` (T-199). `build/update-ffmpeg.ps1`: Windows bez
  BtbN (jeden tarball pro oba OS).
- **Balení zdroje u vydání (T-199):** `ffmpeg-source-<verze>-windows.tar.xz` jako macOS: oficiální tarball beze změny,
  skript buildu Windows, `README.txt` s řádkem configure z vydávané DLL. Funkce `Get-FFmpegSourcePins` /
  `Get-FFmpegSourceInputs` / `Get-FFmpegSourceReadme` ve `scripts/ReleaseTools/ReleaseTools.psm1` bez větve git/BtbN
  (`New-GitSourceArchive` a pin stromu se nejspíš úplně odstraní), Pester testy upravit. Název archivu zůstává (odkazuje
  ho okno O aplikaci, text vydání a README).
- **Aplikace:** `ThirdPartyLicenses.FFmpeg` (Windows odkazy na BtbN zip a FFmpeg-Builds pryč, zdroj = tarball
  ffmpeg.org), okno licencí třetích stran, testy `ThirdPartyLicensesTests`.
- **Dokumentace:** `docs/prerequisites.md` (nový toolchain pro první `dotnet build` na Windows, cache, chybové hlášky,
  „Nová verze FFmpeg“, nouzový postup T-162 odpadá), `docs/release.md` (zdroj Windows), `CLAUDE.md` (věta o toolchainu
  prvního buildu – jen přes hlavního agenta / uživatele), README EN + CZ („The first build on Windows downloads…“),
  `CHANGELOG.md`, T-125 oddíl 2ab (body o `git archive` / BtbN nahradit).
- **Ověření:** kamera se simulátorem (RTSP, nahrávání MP4, export timelapse do JPEG), E2E testy kamery, AOT publish,
  porovnání velikosti DLL a doby prvního buildu s BtbN.

## Akceptační kritéria
- [ ] První `dotnet build` na Windows přeloží FFmpeg z připnutého oficiálního tarballu (ověřený SHA-256, stejný jako
      macOS) do cache; druhý build nic nepřekládá; chybějící toolchain = srozumitelná chyba s návodem
- [ ] `configure.log` potvrdí LGPL 3 nebo novější; DLL závisí jen na sobě navzájem a na systémových DLL Windows (test /
      kontrola ve skriptu); žádná externí knihovna v řádku configure
- [ ] Build obsahuje D3D11VA: `--enable-d3d11va`, hwaccel `h264_d3d11va` a hwcontext `d3d11va` jsou v knihovnách
      (kontrola v testu, např. `av_hwdevice_find_type_by_name("d3d11va")` a seznam hwaccel dekodéru H.264), bez externích
      knihoven
- [ ] Kamera funguje beze změny (RTSP, nahrávání MP4, čtení MP4 / timelapse, snímky): `dotnet test` včetně testů FFmpeg
      a E2E kamery na Windows
- [ ] `release-build.ps1` přiloží `ffmpeg-source-<verze>-windows.tar.xz` jen se zdrojem FFmpeg a skriptem buildu
      (žádné skripty BtbN); Pester testy bez sítě upravené
- [ ] Velikost DLL a doba prvního buildu proti BtbN zapsané v poznámkách
- [ ] `ThirdPartyLicenses`, `docs/prerequisites.md`, `docs/release.md`, README EN + CZ, `CHANGELOG.md` aktualizované
- [ ] `dotnet publish src/LayerPeek.App -c Release -r win-x64` (Native AOT) projde, aplikace s kamerou běží
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Toolchain na Windows (rozhoduje uživatel, blokuje implementaci):** (a) MSYS2 + MinGW-w64 (`configure` FFmpeg je
   skript pro POSIX shell, MSYS2 je obvyklá cesta; nová vývojová závislost mimo Visual Studio; nutnost statického
   `libgcc` / `winpthreads`, licence runtime MinGW ověřit), nebo (b) MSVC (`--toolchain=msvc`, překladač už je potřeba pro
   AOT publish – „Desktop development with C++“, ale `configure` i tak potřebuje shell a `make` z MSYS2, a dnes
   `dotnet build` na Windows nativní toolchain nepotřebuje). Obojí mění `docs/prerequisites.md` a požadavky na první
   build na Windows (dnes stačí .NET SDK).
2. **DXVA2 / D3D12VA:** rozhodnuto je jen D3D11VA. Přidat i DXVA2 (starší systémy) nebo D3D12VA? Zatím ne.
3. **Build ve skriptu vydání, nebo i v každém vývojovém buildu?** Navrženo jako macOS (první `dotnet build`). Alternativa
   (předpřeložené DLL v cache nebo jako asset vlastního vydání) by měnila postup T-026 / T-199.

## Poznámky k implementaci
(vyplní agent po dokončení)

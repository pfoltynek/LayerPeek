---
id: T-202
title: Windows – vlastní minimální LGPL build FFmpeg ze zdrojů (místo BtbN), s D3D11VA
status: done
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
>
> **Rozhodnutí uživatele 2026-11-12 (Windows ARM64):** `win-arm64` se bude vydávat také; ARM64 build FFmpeg přidá
> **T-225** po tomto úkolu. Tady se ARM64 neimplementuje, jen se skript buildu, cache (`win-x64-<klíč>`) a výběr
> v `build/FFmpeg.targets` navrhnou tak, aby šla cílová architektura později přidat parametrem.

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
- **CI (T-218, doplněno architektem 2026-11-13):** pokud už bude workflow `.github/workflows/ci.yml` hotový, upravit
  klíč cache FFmpeg pro Windows (hash nového skriptu buildu + tarball) a instalaci toolchainu na runneru `windows-*`;
  pokud ne, T-218 to převezme podle tohoto úkolu.
  *Stav 2026-11-13 (T-218):* workflow existuje. Klíč cache FFmpeg pro Windows (job `Build and test (Windows)`, krok
  `Restore FFmpeg`) = obraz + RID + hash `build/FFmpeg.targets` a vzoru `build/windows/build-ffmpeg.*` (položka matice
  `ffmpeg-script`). Skript s tímto názvem se do klíče dostane sám; jiný název = upravit `ffmpeg-script`. T-202 doplní
  do jobu Windows instalaci toolchainu (krok před `Build (Release)`, akce připnutá na SHA s komentářem verze, test
  `CiWorkflowTests`) a v `docs/prerequisites.md`, oddíl „CI (GitHub Actions)“, větu o Windows zipu BtbN.
- **T-221 (2026-11-14):** s buildem BtbN dává rovina Y na Windows větší rozdíl proti BGRA než meze T-189 (1,72 / +1,44).
  Po přechodu na vlastní build změřit `GrayscaleSampleEquivalenceTests` znovu (zapsat do T-221).
- **T-221 se řeší v rámci tohoto úkolu (rozhodnutí uživatele 2026-11-15).** T-228 už zavedl meze podle architektury
  (arm64 1,5 / 1, x64 2,0 / 1,5) a změřil, že detekce změny podložky se nemění. Tady zbývá z T-221: změřit hodnoty
  s vlastním buildem, dopad na skóre detektorů na Windows, návrh meze x64 pro uživatele a pak odstranit přeskočení
  testu v CI na Windows (`ContinuousIntegration.IsWindowsRunner`) a ověřit zelený běh v CI. Kritéria T-221 se zaškrtnou
  v T-221 a T-221 se uzavře spolu s tímto úkolem.

## Akceptační kritéria
- [x] První `dotnet build` na Windows přeloží FFmpeg z připnutého oficiálního tarballu (ověřený SHA-256, stejný jako
      macOS) do cache; druhý build nic nepřekládá; chybějící toolchain = srozumitelná chyba s návodem (i s diakritikou
      a mezerou v cestě, opraveno po review B1 / B2, viz `## Review`)
- [x] `configure.log` potvrdí LGPL 3 nebo novější; DLL závisí jen na sobě navzájem a na systémových DLL Windows (test /
      kontrola ve skriptu); žádná externí knihovna v řádku configure
- [x] Build obsahuje D3D11VA: `--enable-d3d11va`, hwaccel `h264_d3d11va` a hwcontext `d3d11va` jsou v knihovnách
      (kontrola v testu, např. `av_hwdevice_find_type_by_name("d3d11va")` a seznam hwaccel dekodéru H.264), bez externích
      knihoven
- [x] Kamera funguje beze změny (RTSP, nahrávání MP4, čtení MP4 / timelapse, snímky): `dotnet test` včetně testů FFmpeg
      a E2E kamery na Windows – testy videa prošly; opakování E2E testů ukazatele / hlavního panelu na klidné ploše
      → přesunuto do T-125 (2ag)
- [x] `release-build.ps1` přiloží `ffmpeg-source-<verze>-windows.tar.xz` jen se zdrojem FFmpeg a skriptem buildu
      (žádné skripty BtbN); Pester testy bez sítě upravené – balení ověřené funkcemi `release-build.ps1` se skutečným
      tarballem a publish složkou; celý běh `release-build.ps1 -SkipFeedSignature` → přesunuto do T-125 (2ab)
- [x] Velikost DLL a doba prvního buildu proti BtbN zapsané v poznámkách
- [x] `ThirdPartyLicenses`, `docs/prerequisites.md`, `docs/release.md`, README EN + CZ, `CHANGELOG.md` aktualizované
- [x] `dotnet publish src/LayerPeek.App -c Release -r win-x64` (Native AOT) projde, aplikace s kamerou běží
- [x] Po přechodu na vlastní build znovu změřit `GrayscaleSampleEquivalenceTests` na x64 (průměrná odchylka a posun) a navrhnout
      limit x64 s rezervou ~15 % (rozhodnutí uživatele 2026-11-12, T-228; nový limit schvaluje uživatel); totéž uzavírá T-221
      – změřeno 1,72 / +1,44, mez x64 2,0 / 1,7 schválená uživatelem 2026-11-15 (otázka 4); T-221 čeká jen na zelený běh CI po pushi
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Rozhodnuto uživatelem 2026-11-12: 1 **MSYS2 + MinGW-w64** (MSYS2 nainstalovaný na vývojovém stroji Windows v `C:\msys64`
přes `winget install MSYS2.MSYS2`, balíčky `make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm mingw-w64-x86_64-pkgconf`;
gcc 16.2.0, nasm 3.02, make 4.4.1); 2 **jen D3D11VA** (bez DXVA2 a D3D12VA); 3 **při prvním `dotnet build`** jako na macOS.

1. **Toolchain na Windows (rozhoduje uživatel, blokuje implementaci):** (a) MSYS2 + MinGW-w64 (`configure` FFmpeg je
   skript pro POSIX shell, MSYS2 je obvyklá cesta; nová vývojová závislost mimo Visual Studio; nutnost statického
   `libgcc` / `winpthreads`, licence runtime MinGW ověřit), nebo (b) MSVC (`--toolchain=msvc`, překladač už je potřeba pro
   AOT publish – „Desktop development with C++“, ale `configure` i tak potřebuje shell a `make` z MSYS2, a dnes
   `dotnet build` na Windows nativní toolchain nepotřebuje). Obojí mění `docs/prerequisites.md` a požadavky na první
   build na Windows (dnes stačí .NET SDK).
2. **DXVA2 / D3D12VA:** rozhodnuto je jen D3D11VA. Přidat i DXVA2 (starší systémy) nebo D3D12VA? Zatím ne.
3. ~~**Build ve skriptu vydání, nebo i v každém vývojovém buildu?**~~ Rozhodnuto (první `dotnet build`).

Nové otázky z implementace (2026-11-15), všechny **rozhodnuté uživatelem 2026-11-15**:

4. ~~**Mez x64 v `GrayscaleSampleEquivalenceTests` (T-221):** s vlastním buildem změřeno 1,72 / +1,44 (stejně jako BtbN).
   Návrh s rezervou ~15 %: průměrný |rozdíl| **2,0** (beze změny), průměrný posun **1,7** (dnes 1,5, rezerva 4 %).
   Schválit? Dokud ne, mez zůstává 2,0 / 1,5 (test prochází).~~
   **Rozhodnuto uživatelem 2026-11-15:** mez x64 **2,0 / 1,7** schválená (arm64 beze změny 1,5 / 1); zapsáno v `Limits`
   včetně komentáře s naměřenými hodnotami a příčinou.
5. ~~**`h264_d3d11va2` navíc k `h264_d3d11va`:** hwcontext `d3d11va` (`av_hwdevice_ctx_create`, kterou T-206 použije) je ve
   FFmpeg 9.0.2 navázaný jen na `h264_d3d11va2`; samotné `h264_d3d11va` je starší API bez typu zařízení. Obojí je D3D11VA
   (žádné DXVA2 / D3D12VA), přidání považuji za součást rozhodnutí „D3D11VA“. Potvrdit, nebo chtít jen jedno z nich?~~
   **Rozhodnuto uživatelem 2026-11-15:** ponechat obojí (`h264_d3d11va` i `h264_d3d11va2`).
6. ~~**Licence runtime MinGW-w64 v okně licencí:** nová položka „MinGW-w64 runtime“ (verze 14.0.0, licence zapsaná jako
   `ZPL-2.1 AND MIT AND BSD-3-Clause`, texty `COPYING.MinGW-w64-runtime.txt` a `COPYING` winpthreads beze změny, popis
   „in the FFmpeg DLLs, Windows only“ v 8 jazycích). Ukazuje se i na macOS (jako ANGLE, které je jen pro Windows). Text
   obsahuje i další permisivní licence (ISC, BSD-2, gdtoa, Sun, musl MIT; LGPL 2.1+ jen hlavičky z Wine). Souhlasí výraz
   licence a umístění? Verze runtime se s aktualizací MSYS2 může posunout (zapisuje se do `BUILD.txt` jen gcc a nasm).~~
   **Rozhodnuto uživatelem 2026-11-15:** položka „MinGW-w64 runtime“ potvrzená tak, jak je, zobrazuje se i na macOS.
7. ~~**`CLAUDE.md`:** věta o toolchainu prvního buildu na Windows – návrh je v hlášení hlavnímu agentovi (uživatel schválí).~~
   **Rozhodnuto uživatelem 2026-11-15:** věta schválená, `CLAUDE.md` upraví hlavní agent.

## Poznámky k implementaci
Implementováno 2026-11-15 na Windows 11 x64 (MSYS2 v `C:\msys64`: gcc 16.2.0 Rev6, nasm 3.02, make 4.4.1, binutils 2.47,
mingw-w64-crt / winpthreads 14.0.0.r598.ge3d18511f-1). Přerušený běh z 2026-11-12 (jen analýza) je nahrazený touto
poznámkou.

**Build (`build/windows/build-ffmpeg.sh`, nový):** podle `build/macos/build-ffmpeg.sh`. Argumenty
`<RID> <tarball> <složka v tarballu> <pracovní složka> <výstup>`; RID `win-x64` vybere prostředí MSYS2 (`/mingw64`,
`MINGW64`), `--arch=x86_64` a očekávaný formát PE (`pei-x86-64`). Jiný RID skončí chybou s odkazem na T-225, který přidá
`win-arm64` jako další větev `case` (llvm-mingw). Skript převede cesty přes `cygpath` (MSBuild předává cesty Windows),
spustí `configure`/`make` v čistém prostředí (`env -i`, `PATH=/mingw64/bin:/usr/bin`, jen `SYSTEMROOT`, `WINDIR`, `COMSPEC`
a dočasná složka; `COMSPEC` potřebuje `windres` pro verzní prostředky DLL, bez něj `make` spadl), zkontroluje
`License: LGPL version 3 or later` a `threading support w32threads` v `configure.log`, u každé DLL formát (`objdump -f`)
a importy (`objdump -p`, „DLL Name“: jen sourozenecké knihovny FFmpeg a seznam DLL Windows ve skriptu) a zapíše
`LICENSE.txt` (`COPYING.LGPLv3`) a `BUILD.txt` (verze, tarball, toolchain, přesný řádek configure).

Řádek configure = macOS bez řádků platformy (test `FFmpegBuildsOfBothPlatformsHaveTheSameFeatures`) + Windows:
`--enable-d3d11va --enable-hwaccel=h264_d3d11va,h264_d3d11va2 --target-os=mingw32 --arch=x86_64 --enable-w32threads
--disable-pthreads --extra-ldflags=-static-libgcc --extra-libs=-l:libwinpthread.a`.
- **`h264_d3d11va2` navíc proti zadání:** v FFmpeg 9.0.2 je hwcontext `d3d11va` (`AV_PIX_FMT_D3D11`, typ zařízení
  `AV_HWDEVICE_TYPE_D3D11VA`, `av_hwdevice_ctx_create`) navázaný jen na hwaccel `h264_d3d11va2` (`libavcodec/hwconfig.h`,
  `HWACCEL_D3D11VA2`); `h264_d3d11va` je starší API bez typu zařízení (`AV_PIX_FMT_D3D11VA_VLD`, aplikace dodá vlastní
  `ID3D11VideoContext`). Jen s `h264_d3d11va` by `FFmpegRuntime.HasHardwareDecoder(D3D11VA)` vrátil `false` a T-206 by
  přes `hw_device_ctx` nemohl dekódovat. Obojí je D3D11VA (ne DXVA2 / D3D12VA), viz otázka 5 níže.
- **Vlákna:** `--enable-w32threads --disable-pthreads` (vlákna Windows). MSYS2 gcc má thread model posix; i tak avutil
  importoval `clock_gettime64` a `nanosleep64` z `libwinpthread-1.dll` (MinGW-w64 je implementuje ve winpthreads). Kontrola
  importů to chytila; řešení `-l:libwinpthread.a` (statický archiv). `-static-libgcc` drží libgcc v DLL.
- **Výsledné importy:** `avutil-61.dll` → `bcrypt.dll`, `KERNEL32.dll`, `msvcrt.dll`; `avcodec-63.dll` → `KERNEL32`,
  `msvcrt`, `avutil-61`; `avformat-63.dll` → `KERNEL32`, `msvcrt`, `WS2_32` (Winsock), `avcodec-63`, `avutil-61`;
  `swscale-10.dll` → `KERNEL32`, `msvcrt`, `avutil-61`. `d3d11.dll` / `dxgi.dll` FFmpeg načítá až za běhu (`LoadLibrary`).
  Žádná DLL MSYS2 (`libgcc_s_seh-1`, `libwinpthread-1`, `zlib1` …). Demuxer `mov` (čtení vlastních MP4 / timelapse,
  T-165) je v buildu díky závislostem demuxeru RTSP stejně jako na macOS (`CONFIG_MOV_DEMUXER=yes`), jediný dekodér je
  `h264`, jediný typ hw zařízení `d3d11va`, DXVA2 / D3D12VA / schannel / zlib vypnuté.

**Licence runtime MinGW-w64 (staticky v DLL):** libgcc = GPL 3 s GCC Runtime Library Exception (přeložený kód
„Eligible Compilation Process“, žádná povinnost uvádět text; GCC sám i FFmpeg jsou překládané z neupraveného GCC);
winpthreads = MIT, části BSD-3-Clause (Lockless Inc.); mingw-w64-crt (startovací kód a pomocné funkce, které se do každé
DLL MinGW linkují vždy) = ZPL 2.1 a další volné licence (BSD, ISC, MIT, Sun, gdtoa, LGPL 2.1+ jen u hlaviček z Wine);
mingw-w64 samo k binárkám přikládá `COPYING.MinGW-w64-runtime.txt` („Some parts of the runtime are under licenses which
require that the copyright and license notices are included when distributing the code in binary form“). Všechny jsou
kompatibilní s GPL-3.0 (ZPL 2.1 podle FSF kompatibilní, ostatní permisivní). Proto nová položka v okně licencí
„MinGW-w64 runtime“ (`ThirdPartyLicenses.All`, texty `Licenses/Texts/mingw-w64-runtime.txt` =
`/mingw64/share/licenses/crt/COPYING.MinGW-w64-runtime.txt` a `mingw-w64-winpthreads.txt` =
`/mingw64/share/licenses/libwinpthread/COPYING`, beze změny, jen LF), popis „(in the FFmpeg DLLs, Windows only)“ ve všech
8 jazycích (`LicensesComponentMinGW`). Platí i pro dřívější DLL BtbN (také MinGW), tam to nebylo uvedené. Žádný nový balíček
NuGet, žádná nová knihovna do aplikace (otázka 6 k potvrzení zápisu licence).

**`build/FFmpeg.targets`:** BtbN (`LayerPeekFFmpegUrl` / `Sha256` / `ArchiveFolder`, `LayerPeekFFmpegWindowsSource*`,
`LayerPeekFFmpegWindowsScripts*`, cíl stahování zipu) odstraněn; `LayerPeekFFmpegMacSource*` přejmenované na společné
`LayerPeekFFmpegSourceUrl` / `Sha256` / `Folder`. Nový cíl `_LayerPeekBuildFFmpegWindows` jako macOS: úklid starých `tmp-*`
(2 h), kontrola toolchainu **před stažením** (seznam souborů v `LayerPeekMsys2Root`, výchozí `C:\msys64`, i jako proměnná
prostředí; chybějící vypíše jmenovitě, s návodem `winget install MSYS2.MSYS2` + `pacman -S --needed …` a únikem
`-p:LayerPeekFFmpegEnabled=false`), stažení a SHA-256, `bash.exe --noprofile --norc build-ffmpeg.sh`, značka
`verified.sha256` naposled, přesun do cache jen přejmenováním (souběžné buildy jako dřív). Po chybě zůstane `tmp-…` s
`configure.log` / `make.log`. Cache `win-x64-<SHA-256 ze SHA tarballu a textu skriptu>` (RID z `_LayerPeekFFmpegRid`,
toolchain z `_LayerPeekFFmpegMsys2Toolchain`, balíčky v `_LayerPeekFFmpegMsys2Packages` – vše podle RID, T-225 přidá
`win-arm64`). Klíč macOS se nezměnil (stejné hodnoty), cache Macu zůstává platná. `swresample-7.dll` odpadl.

**Ověřeno:**
- Bez toolchainu (`-p:LayerPeekMsys2Root=C:\nonexistent-msys`, prázdná cache): chyba za ~1 s, nic se nestáhlo ani
  nevytvořilo.
- **Doba prvního buildu:** `dotnet build src/LayerPeek.Camera.FFmpeg` s prázdnou cache **3 min 36 s** (stažení 12 MB +
  rozbalení + configure + make na 16 vláknech; samotný skript 3 min 37 s ručně). Druhý `dotnet build` celého řešení 29 s,
  FFmpeg se nepřekládá (žádná zpráva „Building FFmpeg“). **BtbN:** první build = stažení zipu 76,6 MB (dnes 4,9 s na
  tomto připojení) + rozbalení, nic se nepřekládalo.
- **Velikost DLL:** vlastní build `avcodec-63.dll` 2 079 744 B, `avformat-63.dll` 1 119 232 B, `avutil-61.dll`
  1 126 912 B, `swscale-10.dll` 2 120 192 B, **celkem 6 446 080 B (6,4 MB)**. BtbN: 91 810 816 + 19 888 128 + 3 025 408 +
  2 343 936 + `swresample-7.dll` 747 008 = **117 815 296 B (117,8 MB)**, tj. 18× víc. Zip BtbN 76,6 MB, tarball 12,0 MB.
- **D3D11VA, LGPL, závislosti:** nové testy `FFmpegWindowsBuildTests` (Windows, `[FFmpegFact]`): čtyři knihovny vedle
  testů a proces nenačte nic jiného (žádný `swresample`; staré výstupní složky ho můžou mít – build nic nemaže, test proto
  kontroluje načtené moduly); importy každé DLL přečtené z PE (`PEReader`, tabulka importů) jen ze seznamu; architektura
  AMD64; `av_hwdevice_find_type_by_name("d3d11va")`, jediný typ zařízení `D3D11VA`, `HasHardwareDecoder(D3D11VA)`, hw
  konfigurace H.264 právě `D3D11` + `D3D11VA_VLD`, ne DXVA2 / D3D12VA / CUDA / QSV; `avcodec_configuration()` bez
  `--enable-lib`, `--enable-gpl`, `--enable-nonfree`; jediný dekodér `h264`. `ThirdPartyLicensesTests`: příznaky skriptu
  Windows, stejná sada funkcí obou OS, položka MinGW-w64.
- **Kamera:** `dotnet test` projektu `LayerPeek.Camera.FFmpeg.Tests` 212 testů, 193 prošlo, 19 přeskočených (VideoToolbox
  a macOS), 0 selhání: RTSP přes simulovanou kameru (`SimulatedCameraTests`, `FFmpegCameraFeedRtspTests`), nahrávání MP4
  (`FFmpegRecordingTests`), čtení MP4 / timelapse a export JPEG (`FFmpegTimelapseFromRecordingTests`,
  `FFmpegTimelapseJpegExportTests`), snímky, alarm přes simulátor (`BedChangeAlarmSimulatorTests`).
  E2E kamery (`tools/run-e2e.ps1 -Class '*Camera*' -NoSystemChanges -PublishAot`): na sdílené, souběžně používané ploše prošly testy videa: `CameraRecordingTests` (2), `CameraTimelapseTests` (2) a obojí znovu proti Native AOT exe (`-PublishAot`, zelené), `CameraWindowTests` (simulovaná kamera), `CameraMiniPreviewTests`, `CameraWidgetTests`, `CameraLayoutModeTests`, `CameraWindowAutoCloseTests`, `CameraTopmostTests`, `CameraMonitorTests` (při opakování 19 prošlo, 4 přeskočeny; první běh chyba 5 `QueryDisplayConfig` při zamčené ploše). Selhávaly jen testy ukazatele a hlavního panelu (`CameraPinTests` 3, `CameraMiniOutlineTests` 2, v `CameraTaskbarAndModeSwitchTests` pokaždé jiný 1): pod ukazatelem byl kurzor textového pole cizího okna (0x10005) a „someone used the mouse during the end-to-end run“ – prostředí, ne FFmpeg (tyto testy nedekódují, kamera v nich je záměrně nedostupná `rtsp://127.0.0.1:1`). Opakování na klidné ploše → T-125 2ag.
- **AOT:** `dotnet publish src/LayerPeek.App -c Release -r win-x64` prošel (1 min 16 s; zprávy ILC o
  `Avalonia.Win32 … CreateTargetForHwnd` nejsou varování a FFmpeg se netýkají). Publish obsahuje čtyři DLL FFmpeg +
  `FFmpeg-LICENSE.txt`, žádný `swresample`. Aplikace AOT s kamerou: průchod `-aot` E2E testů výše.
- **Zdroj u vydání:** `Get-FFmpegSourceInputs` + `New-FFmpegSourceArchive` (tytéž funkce, které volá
  `release-build.ps1`) se skutečným tarballem a skutečnou publish složkou AOT: `ffmpeg-source-1.0.0-windows.tar.xz`
  12 048 144 B za 5,3 s, obsah přesně `ffmpeg-9.0.2.tar.xz`, `build-ffmpeg.sh`, `README.txt` (řádek configure z
  `avcodec-63.dll`, verze `9.0.2` sedí). Celý `release-build.ps1 -SkipFeedSignature` zůstává v T-125 2ab (bod přepsaný).
- **`GrayscaleSampleEquivalenceTests` x64 (T-221):** s vlastním buildem průměrný |rozdíl| **1,72**, posun **+1,44**,
  max 24 – přesně jako BtbN. Detektory (bed-change, dočasný pokus): poplachy beze změny, max. rozdíl skóre ve výřezu
  Core One 0,0006 (podrobně v T-221). Test prochází. **Mez x64 s rezervou ~15 %:** |rozdíl| **2,0**
  (beze změny), posun **1,7** (dřív 1,5) – schváleno uživatelem 2026-11-15 (otázka 4), zapsáno v `Limits`. Přeskočení v CI na Windows odstraněno.

- **Celé řešení:** `dotnet build` 0 varování, 0 chyb; `dotnet test` **total 11354, failed 0, succeeded 10764,
  skipped 590** (5 min 52 s). Při prvním plném běhu selhal jen `StringsTests.FrenchPunctuationFollowsANonBreakingSpace`
  (nová francouzská poznámka měla před „:“ obyčejnou mezeru; opraveno na nezlomitelnou jako u poznámky macOS).

**Release tools / skripty:** `Get-FFmpegSourcePins` bez větve BtbN/git (obě OS: tarball + skript buildu OS, `Rid`),
`New-GitSourceArchive` odstraněný, `Save-FFmpegSourceInput` jen `download` (jiný druh = chyba), kontrola verze knihovny
přesně verze tarballu na obou OS, `Get-FFmpegSourceReadme` s větví Windows (MSYS2, statický runtime), `New-FFmpegSourceArchive`
přikládá skript buildu na obou OS. `build/update-ffmpeg.ps1` jen `-Version` (bez `-Platform`, `-BtbNTag`, `-BtbNAsset`),
zapisuje `LayerPeekFFmpegSource*` a `SourceTarballUri`, knihovny obou platforem musí sedět jedna k jedné. Pester:
`build/update-ffmpeg.Tests.ps1` přepsaný (38 testů), `ReleaseTools.Tests.ps1` oddíl „FFmpeg source of a release“ upravený
(21 testů, bez git repozitářů), bez sítě.

**CI (T-218):** v jobu Windows krok `Install MSYS2 with MinGW-w64 (FFmpeg build)` (`msys2/setup-msys2@ec48f7c5…` # v2.33.0,
SHA tagu ověřené přes `git ls-remote`, jen při `cache-hit != 'true'`, `update: false`, stejné balíčky) a krok
`Point the FFmpeg build to MSYS2` (`LayerPeekMsys2Root` z výstupu `msys2-location`). `ffmpeg-script` = přesně
`build/windows/build-ffmpeg.sh`. Test `CiWorkflowTests.TheWindowsJobInstallsTheFFmpegToolchainBeforeTheFirstBuild`.
`docs/prerequisites.md`, oddíl CI, doplněný. Akce `setup-msys2` (MIT, msys2/setup-msys2) se do aplikace nedostane.

**Aplikace:** `ThirdPartyLicenses.FFmpeg`: `SourceTarballUri` (dřív `MacOSBuildUri`, teď obě OS),
`WindowsBuildScriptsUri` / `MacOSBuildScriptsUri` na skript buildu podle tagu vydání (`BuildScriptUri(verze, macOS)`),
`WindowsBuildUri` (zip BtbN) a odkaz na FFmpeg-Builds pryč. Okno licencí na Windows: poznámka bez BtbN a swresample
(8 jazyků), odkazy „The FFmpeg source release used (ffmpeg.org)“ a „Build configuration and script (LayerPeek)“
(dřív jen macOS; klíče `…MacOS` sloučené do `LicensesFFmpegBuild` / `LicensesFFmpegBuildScripts`).

**Dokumentace:** `docs/prerequisites.md` (oddíl FFmpeg přepsaný pro obě platformy, MSYS2 v tabulce Windows i v úvodu,
chybová hláška, „Nová verze FFmpeg“ bez BtbN, nouzový postup T-162 „Když stažení selže (Windows)“ odstraněný, CI, časté
chyby), `docs/release.md` (zdroj Windows = tarball + skript), `docs/architecture.md`, `docs/hw-decoding.md` (D3D11VA2),
README EN + CZ (první build), `CHANGELOG.md` (Changed), T-125 bod 1 a oddíl 2ab. `CLAUDE.md` neměněn (návrh věty v
hlášení hlavnímu agentovi).

**Úklid:** dočasné soubory jen ve scratchpadu agenta; stará cache BtbN `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\e907b01f…`
a `00ab3bc8…` zůstává (build ji nemaže), nová `…\ffmpeg\win-x64-e5475b46…`, tarball zdroje pro vydání v
`…\LayerPeek.Build\ffmpeg-source\` zatím ne (pokus balení měl cache ve scratchpadu).

## Review
Review qa-reviewer 2026-11-15 (necommitnutý stav po 97a6695c). `dotnet build` 0 varování; `dotnet test`
`LayerPeek.Camera.FFmpeg.Tests` 212 (193 prošlo, 19 přeskočeno, 0 selhání), `LayerPeek.App.Tests` 4869 (4764 / 105 / 0),
`LayerPeek.Scripts.Tests` 20 (19 / 1 / 0). Klíč cache sedí se skriptem (build nic nepřekládal), importy DLL v cache
ověřené `objdump -p` (jen sourozenci, `KERNEL32`, `msvcrt`, `WS2_32`, `bcrypt`), SHA akce `setup-msys2` = tag v2.33.0
(`git ls-remote`), texty licencí MinGW-w64 shodné s `/mingw64/share/licenses`, klíč cache macOS beze změny.

**Blokující (úkol vrácen do `in-progress`):**
- **B1 – cesta s diakritikou (`build/windows/build-ffmpeg.sh:148`):** ověřeno skriptem na pracovní složce
  `…\scratchpad\Jan Novák\work` (TEMP bez mezer): configure, make i make install projdou (~4 min), pak skript skončí
  `avformat-63.dll has the format 'C:/…/Jan Nov�pei-x86-64', not pei-x86-64`. `objdump` (nativní program MinGW) vypíše
  cestu v kódové stránce ANSI (cp1250 `0xE1`), `sed` v `LANG=en_US.UTF-8` (výchozí MSYS2) neplatné UTF-8 nespáruje.
  Pracovní složka je vždy v `%LOCALAPPDATA%`, takže **každý uživatel Windows s diakritikou ve jméně profilu**
  (např. `C:\Users\Novák`) první build nedokončí. Oprava ověřená ručně: `LC_ALL=C sed …`, nebo `objdump` spouštět
  ve složce `$STAGE/bin` s holým názvem souboru (obojí dalo `pei-x86-64`). Totéž udělat i u `objdump -p` (ř. 152).
- **B2 – `TEMP` s mezerou (`build/windows/build-ffmpeg.sh:126-127`):** `TMPDIR` pro configure se bere z `TEMP`; configure
  FFmpeg 9.0.2 cestu nedává do uvozovek (`tmpfile()`: `exec > $tmp`, ř. 5123), s mezerou v cestě skončí
  `$TMPC: ambiguous redirect` … `C compiler test failed` (ověřeno). Uživatel s mezerou ve jméně profilu má `TEMP`
  s mezerou, pokud ho Windows nedá v krátkém tvaru 8.3 (není zaručené). Mezera v pracovní složce samotné nevadí
  (configure běží ve zdrojové složce, make install s mezerou v `--prefix` prošel). Návrh: pro `TMPDIR` vzít krátký
  tvar (`cygpath -d`) nebo podsložku bez mezer, a pokud mezera zůstane, skončit hned srozumitelnou chybou
  s `LayerPeekFFmpegCacheRoot` / `TEMP`; doplnit `docs/prerequisites.md` (časté chyby).
  B1 a B2 opravit jednou změnou skriptu (změna skriptu = nový klíč cache = jeden nový build ~4 min).

**Ne blokující:**
- **M1 (`build-ffmpeg.sh:152`):** kontrola importů projde naprázdno, když `objdump -p` selže nebo změní formát výstupu
  (`for … in $(…)` chybu substituce ignoruje i se `set -e`). Doplnit podmínku, že seznam není prázdný / obsahuje
  `KERNEL32.dll` (test `FFmpegWindowsBuildTests` to už dělá přes `Assert.NotEmpty`).
- **N1:** `BUILD.txt` neuvádí verze binutils, mingw-w64-crt a winpthreads, přitom položka licencí má pevnou verzi
  `14.0.0` (`ThirdPartyLicenses.cs:53`); po aktualizaci MSYS2 se rozejdou bez povšimnutí. Návrh: řádek
  `pacman -Q mingw-w64-x86_64-crt mingw-w64-x86_64-winpthreads mingw-w64-x86_64-binutils` do `BUILD.txt`.
- **N2:** Windows job v CI má `timeout: 60`; první běh bez cache = instalace MSYS2 + překlad FFmpeg na 4 jádrech
  (lokálně 3,5 min na 16 vláknech) – zatím neměřené, sledovat první běh po pushi.
- **N3 (opraveno reviewerem):** `build/update-ffmpeg.ps1:13` „until then the prebuilt BtbN release“ → „before it …“.
- **N4 (mimo rozsah, stávající text):** `docs/prerequisites.md` (úvod a časté chyby) dál tvrdí, že se `win-arm64`
  nepodporuje (T-130), proti rozhodnutí 2026-11-12 (T-224..T-226).

### Opravy po review (core-dev, 2026-11-15)
Změna `build/windows/build-ffmpeg.sh` (nový klíč cache `win-x64-495c9517…`; mezikrok `win-x64-ea07a8c0…` se liší jen komentářem a zůstal v cache):
- **B1:** `objdump -f` i `objdump -p` běží ve složce `$STAGE/bin` s holým názvem souboru (výstup neobsahuje cestu),
  `sed` navíc s `LC_ALL=C`.
- **B2:** `TMPDIR` / `TEMP` / `TMP` pro `configure` a překladač = `<pracovní složka>/tmp` (už ne `%TEMP%`). Má-li cesta
  mezeru, použije se krátký tvar 8.3 (`cygpath -u "$(cygpath -d …)"`); když ani ten mezeru nezbaví (svazek bez názvů 8.3),
  skript skončí **před rozbalením** chybou `the work folder … contains a space and has no short 8.3 name …
  LayerPeekFFmpegCacheRoot …` (nový řádek v „Častých chybách“ `docs/prerequisites.md`).
- **M1:** výstup `objdump` se ukládá do proměnné (chyba = `fail`), seznam importů musí obsahovat `KERNEL32.dll`, jinak
  `fail` (prázdný seznam už neprojde naprázdno).
- **N1:** `BUILD.txt` má řádek `Packages:` (`pacman -Q mingw-w64-x86_64-crt …-winpthreads …-binutils …-gcc`).
  `build/FFmpeg.targets` kopíruje `BUILD.txt` z cache jako `FFmpeg-BUILD.txt` **jen do výstupu buildu**
  (`CopyToPublishDirectory="Never"`, aplikace ho nevydává). Nový test
  `ThirdPartyLicensesTests.MinGWRuntimeVersionIsTheOneThatBuiltTheDlls` porovná verzi crt (`14.0.0` z
  `14.0.0.r598.ge3d18511f-1`) s položkou „MinGW-w64 runtime“ a že winpthreads má stejnou verzi; bez souboru (macOS,
  `LayerPeekFFmpegEnabled=false`) se přeskočí. Zvoleno, protože kontroluje DLL, které se opravdu sestavily (verze
  nainstalovaného MSYS2 se může lišit od verze, která překládala cache), a nezastaví build uživateli s novějším MSYS2.
  Postup aktualizace v `docs/prerequisites.md` („Aktualizace MSYS2“).
- **Skript jen ASCII:** komentář k B1 měl nejdřív „Novák“ a Pester `packs the Windows source…` (porovnává skript
  z archivu přes výstup `tar`) selhal na znaku `á`. Skript je teď čistě ASCII a hlídá to
  `ThirdPartyLicensesTests.FFmpegWindowsBuildIsLgplOnlyWithD3D11VAAsItsOnlyHardwareDecoder`.
- **N4:** tři věty o nepodporovaném `win-arm64` (úvod, `vpk`, časté chyby) minimálně přepsané na odkaz T-224 až T-226.
- **N2 (pro hlavního agenta):** CI Windows má `timeout: 60`. Dnes bez dat z runneru; lokálně 3,6–4,1 min na 16 vláknech,
  runner má 4 vCPU → odhad překladu ~10–15 min + instalace MSYS2 (~1–3 min) + build/testy/publish jako dosud. Sledovat
  první běh po pushi; nic neměněno.

Ověřeno:
- Skript ručně s pracovní složkou `…\scratchpad\Jan Novák\work` (diakritika i mezera) a `TEMP=…\temp with space`:
  prošel za 230 s, `BUILD.txt` s řádkem `Packages:`. Složka v `artifacts\t202 space test` na D: (svazek bez názvů 8.3):
  chyba B2 hned, vytvořená jen prázdná `tmp`. Pokusné složky smazané, existující cache nedotčená.
- Skutečná cache přes `dotnet build`: `win-x64-ea07a8c0…` za 4 min 6 s, po úpravě komentáře `win-x64-495c9517…` za
  **3 min 34 s** (celé řešení; stroj zatížený souběžnými agenty). DLL stejně velké jako dřív (celkem 6 446 080 B).
  `dotnet publish -c Release -r win-x64`: `FFmpeg-BUILD.txt` v publish složce není.
- `dotnet build` 0 varování; `dotnet test` celého řešení **total 11355, failed 0, succeeded 10765, skipped 590**
  (5 min 29 s). Mezi tím jednou selhal alokační test `SteadyStateGrayscaleFramesThroughTheHubDoNotAllocate` (při
  souběhu s `Scripts.Tests` jiného worktree; samostatně i v plném běhu prošel) a Pester `packs the Windows source…`
  kvůli `á` (opraveno, viz výše).

### Opakované review (qa-reviewer, 2026-11-15)
Schváleno. `dotnet build` 0 varování (cache `win-x64-495c9517…`, nic se nepřekládalo); `dotnet test`
`LayerPeek.Camera.FFmpeg.Tests` 212 (193 / 19 přeskočeno / 0 selhání), `LayerPeek.App.Tests` 4870 (4765 / 105 / 0;
`MinGWRuntimeVersionIsTheOneThatBuiltTheDlls` a test ASCII skriptu proběhly, nepřeskočené), `LayerPeek.Scripts.Tests`
20 (19 / 1 / 0). Ověřeno navíc: na D: (svazek bez názvů 8.3) vrátí `cygpath -d` dlouhou cestu s kódem 0, takže zabere
druhá kontrola se srozumitelnou chybou; `dotnet publish src/LayerPeek.App -c Release -r win-x64 -p:PublishAot=false`
do dočasné složky: `FFmpeg-BUILD.txt` ani `swresample` v publish složce nejsou. Reviewer zaškrtl kritérium 1 a vrátil
řádek v `tasks/README.md` (po opravách zůstaly neaktualizované).

Drobnosti (neblokují):
- Když `cygpath -d` skončí chybou (u právě vytvořené složky prakticky nenastane), skript skončí nesrozumitelným
  `cygpath: can't convert empty path` místo vlastní hlášky.
- Když `pacman -Q` selže, `BUILD.txt` má prázdný řádek `Packages:` a build projde; selže až (hlasitě) test
  `MinGWRuntimeVersionIsTheOneThatBuiltTheDlls`. Šlo by kontrolovat rovnou ve skriptu.

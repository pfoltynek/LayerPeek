---
id: T-225
title: Windows ARM64 – vlastní LGPL build FFmpeg pro `win-arm64` (křížový překlad z x64)
status: done
owner: core-dev
priority: P2
platform: windows
depends_on: [T-202, T-224]
---

> **Rozhodnutí uživatele 2026-11-12:** LayerPeek se vydává i pro `win-arm64` (série T-224 až T-227, ruší T-130).
> Kamera má na ARM64 fungovat stejně jako na `win-x64`.
>
> **Navazuje na T-202** (vlastní minimální LGPL build FFmpeg pro `win-x64` ze zdrojů místo BtbN; rozhodnutí uživatele
> 2026-11-12: MSYS2 + MinGW-w64, build při prvním `dotnet build`, D3D11VA). Tento úkol přidá druhou architekturu do
> stejného mechanismu. Implementuje se až po sloučení T-202; konkrétní názvy (skript, vlastnosti, cache) se převezmou
> z jeho výsledku.

## Cíl
`dotnet publish src/LayerPeek.App -c Release -r win-arm64` na Windows x64 obsahuje ARM64 knihovny FFmpeg
(`avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll`, podle výsledku T-202) a `FFmpeg-LICENSE.txt`,
přeložené ze stejného připnutého oficiálního tarballu a se stejnou sadou funkcí (včetně D3D11VA) jako `win-x64`.
Vývoj a testy na x64 se nemění.

## Rozsah
- **Toolchain pro aarch64-w64-mingw32 na hostiteli x64** – rozhodnuto (a) llvm-mingw ze shellu MSYS2 (otázka 1).
  Zjištěné možnosti (2026-11-12, pro historii):
  - **(a) llvm-mingw** (https://github.com/mstorsjo/llvm-mingw): hotové toolchainy pro Windows hostitele
    (`llvm-mingw-<verze>-ucrt-x86_64.zip`), které „can compile binaries for any of the 4 architectures“, tedy i ARM64.
    `configure` FFmpeg se spustí v shellu MSYS2 z T-202 s `--enable-cross-compile --arch=aarch64 --target-os=mingw32
    --cross-prefix=aarch64-w64-mingw32-`. Nová vývojová závislost (připnutá verze, SHA-256 zipu, cache jako FFmpeg).
    Licence: LLVM (Apache-2.0 s LLVM výjimkou), runtime mingw-w64 – ověřit v `LICENSE.txt` vydání a uvést, co se
    staticky přilinkuje (compiler-rt builtins, CRT start-up kód).
  - **(b) MSVC ARM64** (`--toolchain=msvc --arch=aarch64 --enable-cross-compile`, prostředí `vcvarsall x64_arm64`):
    komponentu MSVC ARM64 instaluje už T-224. `configure` a `make` stejně potřebují shell MSYS2; asembler NEON FFmpeg je
    v syntaxi GNU, s MSVC potřebuje `gas-preprocessor.pl` + `armasm64`, jinak `--disable-asm` (pomalejší dekódování
    H.264 na CPU). Jiný překladač než `win-x64` (MinGW, T-202) = dvě sady problémů.
  - **(c) Nativní build na ARM64** (MSYS2 prostředí CLANGARM64, https://www.msys2.org/docs/environments/): jeho balíčky
    jsou ARM64 binárky, na x64 nepoběží (dokumentace křížový překlad nezmiňuje). Šlo by jen na zařízení ARM64 nebo na
    běžci GitHub Actions `windows-11-arm` (zdarma pro veřejné repozitáře, ale CI zamítnuto v T-016) – nesedí
    s rozhodnutím „build při prvním `dotnet build`“.
  - **(d) Hotový build BtbN `winarm64-lgpl-shared`** – odporuje rozhodnutí T-199 / T-202 (vlastní minimální build,
    zdroj u vydání bez cizích knihoven). Uvedeno jen pro úplnost, nedoporučuje se.
- **Skript buildu** (z T-202, např. `build/windows/build-ffmpeg.*`): parametr cílové architektury; stejné příznaky
  `configure` jako `win-x64` (včetně `--enable-d3d11va --enable-hwaccel=h264_d3d11va`), jen architektura a toolchain se
  liší. Kontrola `License: LGPL version 3 or later` v `configure.log`, `BUILD.txt` s řádkem configure a toolchainem.
- **`build/FFmpeg.targets` – výběr podle RID:**
  - `RuntimeIdentifier` = `win-x64` nebo prázdný na x64 hostiteli → x64 (beze změny proti T-202);
  - `RuntimeIdentifier` = `win-arm64` → ARM64 (křížový překlad na x64 hostiteli);
  - prázdný RID na ARM64 hostiteli → FFmpeg vypnutý se srozumitelnou hláškou (otázka 3);
  - cache oddělená podle architektury (`win-x64-<klíč>` / `win-arm64-<klíč>`, klíč z hashe zdroje, skriptu a toolchainu);
  - chybějící toolchain ARM64 = srozumitelná chyba jen při požadavku na `win-arm64` (s návodem a únikem
    `-p:LayerPeekFFmpegEnabled=false`); ARM64 FFmpeg se překládá jen při požadavku na `win-arm64`, `dotnet build` /
    `dotnet test` na x64 toolchain ARM64 nepotřebují (otázka 2).
- **Zjištění z T-224 (2026-11-15, platí i po přepsání `build/FFmpeg.targets` v T-202):**
  - `LayerPeek.Camera.FFmpeg` je knihovna bez RID: SDK ji v buildu/publish aplikace sestaví **bez**
    `RuntimeIdentifier`, takže `build/FFmpeg.targets` (ať jakkoli vybírá platformu, cache nebo argument RID skriptu
    `build/windows/build-ffmpeg.*`) RID aplikace sám neuvidí. T-224 ho předává z `LayerPeek.App.csproj` jako
    `LayerPeekAppRuntimeIdentifier` (zatím jen pro `win-arm64`, kde `LayerPeek.Camera.FFmpeg.csproj` nastaví
    `LayerPeekFFmpegEnabled=false`). Výběr ARM64 FFmpeg má číst tuto vlastnost (podmínku v App csproj případně rozšířit
    na každý RID) a vypnutí v `LayerPeek.Camera.FFmpeg.csproj` nahradit. Vlastnost musí zůstat odříznutá od reference
    na `LayerPeek.Core` (`UndefineProperties` + `GlobalPropertiesToRemove`), jinak se Core sestaví dvakrát; hlídá to
    Pester test T-224 v `ReleaseTools.Tests.ps1`, upravit spolu s ním.
  - Na **hostiteli Windows ARM64** dá prostý `dotnet build` bez RID dál x64 FFmpeg (prázdný RID → Windows x64 větev
    v `build/FFmpeg.targets`); patří k odrážce „prázdný RID na ARM64 hostiteli“ výše (otázka 3), výběr má brát
    architekturu hostitele, ne jen RID.
- **Kontrola výsledku:** všechny DLL jsou ARM64 PE (`0xAA64`); závislosti jen sourozenecké DLL FFmpeg a systémové DLL
  Windows (žádné `libc++.dll`, `libunwind.dll`, `libwinpthread-1.dll`); D3D11VA přítomné (kontrola jako v T-202, ale
  bez načtení – na x64 ARM64 DLL načíst nejde, např. řetězce v `avcodec`/`avutil` nebo seznam z `configure`).
- **Testy:** `[FFmpegFact]` (`FFmpegTestEnvironment.CanRun`) spouštět i na Windows ARM64 procesu (na zařízení, T-227);
  na x64 test/kontrola obsahu publish `win-arm64` (DLL, licence, architektura). `SkipReason` a komentáře podle RID.
- **Licence a zdroj u vydání (T-199):** `ThirdPartyLicenses.FFmpeg` a okno licencí – oba buildy (stejný zdroj, dva
  řádky configure, toolchain ARM64 a jeho licence, pokud se z něj něco přilinkuje). Jeden archiv
  `ffmpeg-source-<verze>-windows.tar.xz` s oběma řádky configure (otázka 4).
- **Dokumentace:** `docs/prerequisites.md` (toolchain ARM64, cache, chyby), `docs/hw-decoding.md` (D3D11VA na ARM64 –
  ovladače GPU ARM64 zařízení, neověřeno do T-227), `docs/architecture.md` (FFmpeg pro dvě architektury Windows).

## Akceptační kritéria
- [x] `dotnet publish src/LayerPeek.App -c Release -r win-arm64` na Windows x64 přeloží FFmpeg pro ARM64 z připnutého
      tarballu do cache `win-arm64-…` (druhý publish nic nepřekládá) a publish obsahuje ARM64 DLL FFmpeg a
      `FFmpeg-LICENSE.txt`
- [x] `configure.log` potvrdí LGPL 3 nebo novější; řádek configure je stejný jako u `win-x64` až na architekturu a
      toolchain; obsahuje D3D11VA; žádná externí knihovna
- [x] DLL jsou ARM64 PE a závisí jen na sobě navzájem a na systémových DLL Windows (automatická kontrola)
- [x] `dotnet build`, `dotnet test` a publish `win-x64` na x64 bez toolchainu ARM64 fungují beze změny
- [x] Chybějící toolchain ARM64 při publish `win-arm64` = srozumitelná chyba s návodem
- [x] `ThirdPartyLicenses`, okno licencí a test pokrývají build `win-arm64`; jeden archiv zdroje u vydání s oběma
      řádky configure
- [x] `docs/prerequisites.md`, `docs/hw-decoding.md`, `docs/architecture.md` aktuální
- [x] Běh kamery na ARM64 (RTSP, nahrávání, timelapse, D3D11VA) → ověřuje T-227 → přesunuto do T-227
- [x] `dotnet build` a `dotnet test` bez chyb a varování (`dotnet build` celého řešení; `dotnet test` projektů
      `LayerPeek.Scripts.Tests`, `LayerPeek.Camera.FFmpeg.Tests` a licenčních testů `LayerPeek.App.Tests`, celé
      `dotnet test` s E2E spouští hlavní agent)

## Otevřené otázky
1. ~~**Toolchain pro ARM64:** (a) llvm-mingw v shellu MSYS2 z T-202, (b) MSVC ARM64, (c) nativní build na ARM64,
   (d) BtbN?~~ **Rozhodnuto uživatelem 2026-11-12:** **(a) llvm-mingw** spouštěný ze shellu MSYS2 z T-202 (připnutá
   verze a SHA-256 zipu, cache). `win-x64` zůstává na toolchainu z T-202.
2. ~~**Kdy se ARM64 FFmpeg překládá?**~~ **Rozhodnuto uživatelem 2026-11-12:** jen když se požaduje RID `win-arm64`
   (publish, `release-build.ps1`); běžný `dotnet build` / `dotnet test` na x64 toolchain ARM64 nepotřebuje.
3. ~~**Vývoj přímo na ARM64 stroji (build bez RID)?**~~ **Rozhodnuto uživatelem 2026-11-12:** zatím se nepodporuje; na
   ARM64 hostiteli bez RID se FFmpeg vypne se srozumitelnou hláškou.
4. ~~**Zdroj FFmpeg u vydání pro dvě architektury?**~~ **Rozhodnuto uživatelem 2026-11-12:** jeden archiv
   `ffmpeg-source-<verze>-windows.tar.xz` (stejný tarball, skript buildu, `README.txt` s oběma řádky configure a
   odkazem na toolchain ARM64).
5. ~~**D3D11VA na ARM64?**~~ **Rozhodnuto uživatelem 2026-11-12:** stejná sada funkcí jako `win-x64` včetně D3D11VA;
   ověření za běhu v T-227.
6. ~~**Samostatná položka „LLVM compiler-rt“ v okně licencí?** (staticky přilinkováno pět objektů builtins, Apache-2.0
   s LLVM Exceptions)~~ **Rozhodnuto uživatelem 2026-11-15:** ne, stejně jako runtime GCC u x64; zmínka v poznámkách a
   dokumentaci stačí.
7. ~~**Varianta llvm-mingw: ucrt, nebo msvcrt?**~~ **Rozhodnuto uživatelem 2026-11-15:** ponechat variantu ucrt
   (`llvm-mingw-<verze>-ucrt-x86_64.zip`, importy `api-ms-win-crt-*` místo `msvcrt.dll`).
8. ~~**Verze runtime MinGW-w64 v okně licencí** (review, nález 1: x64 podle balíčku MSYS2 `14.0.0`, ARM64 podle hlavičky
   `15.0.0`)?~~ **Rozhodnuto uživatelem 2026-11-15:** pro obě architektury z hlaviček toolchainu, který DLL sestavil
   (`_mingw_mac.h`), se stavem, když nejde o vydání: `15.0.0 (alpha)`. Jediný zdroj je řádek `MinGW-w64 runtime:`
   v `BUILD.txt`; `ThirdPartyLicenses` ukazuje totéž pravidlo pro x64 i ARM64.
## Poznámky k implementaci
Implementováno 2026-11-15 na Windows 11 x64 (16 vláken, MSYS2 v `C:\msys64`, make 4.4.1, diffutils 3.12).

**Toolchain (otázka 1):** llvm-mingw **20261104** (LLVM 23.1.5, runtime MinGW-w64 15.0.0), asset
`llvm-mingw-20261104-ucrt-x86_64.zip` z https://github.com/mstorsjo/llvm-mingw/releases (vydání 2026-11-04, ne
prerelease). SHA-256 `552191b5…1b40f` = `digest` assetu podle `gh api repos/mstorsjo/llvm-mingw/releases/tags/20261104`
a zároveň `sha256sum` staženého souboru (191 MB). Varianta ucrt podle zadání (zip hostitele x64); `msvcrt` varianta
existuje taky, nepoužitá. Pin: `LayerPeekLlvmMingwUrl` / `Sha256` / `Folder` v `build/FFmpeg.targets`.

**`build/FFmpeg.targets`:**
- Výběr podle RID: `LayerPeekAppRuntimeIdentifier` (z App), jinak vlastní `RuntimeIdentifier`, jinak hostitel
  (`RuntimeInformation.OSArchitecture`, přebitelné globální vlastností `_LayerPeekFFmpegHostArchitecture` kvůli testům).
  `win-x64`/`win-arm64` → ten build; bez RID na x64 → `win-x64`; bez RID na hostiteli ARM64 → FFmpeg vypnutý a cíl
  `LayerPeekFFmpegArm64HostNotice` vypíše zprávu (Message, ne varování – varování jsou chyby) s návodem `-r win-arm64`
  (otázka 3).
- Cache `win-arm64-<SHA256(SHA tarballu | skript | SHA llvm-mingw)>`, `win-x64` klíč beze změny vzorce. llvm-mingw se
  stáhne jen při `win-arm64` a chybějící cache FFmpeg (otázka 2): `DownloadFile` do pracovní složky `tmp-…`, `GetFileHash`
  (neshoda = smazat a chyba), `Unzip` bez `python/`, `i686-w64-mingw32/`, `armv7-w64-mingw32/` (sysroot x86_64 zůstává:
  build FFmpeg překládá a spouští generátory na stroji buildu, např. `libswscale/aarch64/ops_asmgen.exe`), zip se smaže,
  značka `verified.sha256`, přesun do `<cache root>\llvm-mingw-20261104-ucrt-x86_64\` jen přejmenováním (souběžné buildy
  stejně jako u cache FFmpeg). Rozbalený toolchain 569 MB. Nic se neinstaluje ani nepřidává do `PATH`.
- Kontrola MSYS2 před stažením podle RID: `win-arm64` potřebuje jen `bash`, `make`, `cmp`, `tar`, `xz` (balíčky
  `make diffutils`), vlastní chyba `MSYS2 is not installed in … The first win-arm64 build cross-compiles …` s únikem
  `dotnet publish -r win-arm64 -p:LayerPeekFFmpegEnabled=false`; chyba `win-x64` beze změny. Chyby stažení / SHA
  llvm-mingw mají vlastní návod.
- **Zjištění při ověření:** knihovna se při `-r win-arm64` a pak `-r win-x64` kopírovala do téže složky
  `src/LayerPeek.Camera.FFmpeg/bin/Release/net11.0/` a kvůli `PreserveNewest` v ní zůstaly novější ARM64 DLL (výstupy
  aplikace byly správné, ty kopírují z cache). Oprava: s `LayerPeekAppRuntimeIdentifier` má knihovna `OutputPath`
  `bin\<config>\app-<rid>\` (ověřeno: `app-win-arm64` ARM64, `app-win-x64` x64; build bez RID dál `bin\Debug\net11.0\`).

**Projekty:** `LayerPeek.App.csproj` předává `LayerPeekAppRuntimeIdentifier` pro **každý** RID (aby `-r win-x64` dal
x64 knihovny i na hostiteli ARM64), vypnutí `win-arm64` v `LayerPeek.Camera.FFmpeg.csproj` odstraněné, odříznutí od
reference na Core zůstalo. `LayerPeek.Camera.FFmpeg.Tests.csproj` totéž při RID (pro `dotnet test -r win-arm64` na
zařízení, T-227; ověřeno `dotnet build tests/LayerPeek.Camera.FFmpeg.Tests -r win-arm64`: čtyři DLL `0xAA64`).
`FFmpegTestEnvironment.CanRun`: na Windows ARM64 jako na macOS – testy běží, když jsou DLL vedle testů; `SkipReason` uvádí
`win-arm64`. Pester guard z T-224 upravený (RID pro `win-arm64`, `win-x64`, `osx-arm64`, testovací projekt, Core
odříznuté, `LayerPeekFFmpegEnabled=true` pro oba Windows RID). `LayerPeek.Core ->` jednou u obou publish i u `dotnet build`.

**`build/windows/build-ffmpeg.sh`:** nová větev `win-arm64` (šestý argument = složka llvm-mingw), jedno pole
`CONFIGURE_FLAGS` pro oba RID, `win-arm64` přidává jen `TOOLCHAIN_FLAGS` = `--enable-cross-compile
--cross-prefix=aarch64-w64-mingw32- --host-cc=x86_64-w64-mingw32-gcc` (`--arch=aarch64`). Kontroly: LGPL 3, w32threads,
nově i architektura v `configure.log` (`x86` / `aarch64`; první pokus porovnával `x86_64` a x64 build selhal – opraveno),
formát `coff-arm64` (`llvm-objdump -f`), importy (`llvm-objdump -p`), verze runtime MinGW-w64 z `_mingw_mac.h` proti
verze runtime MinGW-w64 z `_mingw_mac.h` (po review bez pinu ve skriptu, viz níže). `BUILD.txt` má nový řádek `MinGW-w64 runtime:` (oba RID).
Změna skriptu = nový klíč cache i pro `win-x64`: na každém stroji jednou nový x64 build (~4 min, zde 297 s s celým
`dotnet build`).

**Výsledek (ověřeno):**
- `dotnet publish src/LayerPeek.App -c Release -r win-arm64 -warnaserror` s prázdnou cache llvm-mingw i ARM64:
  **4 min 36 s celkem** (stažení 191 MB + rozbalení + FFmpeg + AOT; samotný ruční build skriptu 3 min 54 s), exit 0,
  0 varování. Druhý publish 2 s, nic nestahuje ani nepřekládá. Publish: `avcodec-63.dll` 1 582 592 B,
  `avformat-63.dll` 933 376 B, `avutil-61.dll` 741 376 B, `swscale-10.dll` 1 158 144 B (**celkem 4 415 488 B**, x64
  6,4 MB), `FFmpeg-LICENSE.txt`, bez `FFmpeg-BUILD.txt`. `Get-PublishArchitectureCheck -Runtime win-arm64`: 8 PE, vše
  ARM64 `0xAA64`, 0 problémů. `-r win-x64`: 8 PE, vše x64, 0 problémů, llvm-mingw se nepoužil.
- `configure.log`: `License: LGPL version 3 or later`, `ARCH aarch64 (generic)`, `NEON enabled yes`, `threading support
  w32threads`; `BUILD.txt` = řádek x64 + `--arch=aarch64` + tři příznaky toolchainu, D3D11VA, žádné `--enable-lib*`.
- Importy: `KERNEL32`, `bcrypt` (avutil), `WS2_32` (avformat), sourozenci a místo `msvcrt.dll` API sady UCRT
  `api-ms-win-crt-{convert,environment,filesystem,heap,locale,math,private,runtime,stdio,string,time,utility}-l1-1-0.dll`
  (součást Windows 10/11). Žádné `libunwind.dll`, `libc++.dll`, `libwinpthread-1.dll`.
- **Co se staticky přilinkuje** (link mapa všech čtyř DLL, `-Wl,-Map`, a přiřazení členů archivů přes `llvm-ar t`):
  mingw-w64-crt (`dllcrt2.o`, `libmingw32`, `libmingwex` vč. gdtoa, `libucrt_extra`, `crtbegin/crtend`), winpthreads
  (`cond`, `misc`, `mutex`, `rwlock`, `sched`, `spinlock`, `thread`, `time`) a z compiler-rt builtins jen
  `chkstk.S`, `divti3.c`, `udivmodti4.c`, `udivti3.c`, `umodti3.c`. **Z libunwind ani libc++ nic.**

**Licence:** llvm-mingw `LICENSE.TXT` = Apache-2.0 s LLVM Exceptions (+ starší NCSA); výjimka: části vložené do objektu
překladem lze šířit bez podmínek 4(a), 4(b), 4(d) – u builtins compiler-rt (hlavičky souborů „Apache-2.0 WITH
LLVM-exception“) tedy bez povinnosti uvádět licenci, obdoba libgcc u x64 (GCC Runtime Library Exception), proto
**bez nové položky** v okně licencí. `COPYING.MinGW-w64-runtime.txt` a `COPYING.winpthreads.txt` sysrootu
`aarch64-w64-mingw32` jsou s texty z T-202 shodné (bez rozdílu kromě konců řádků) – položka „MinGW-w64 runtime“ je
pokrývá; verze podle architektury procesu (`ThirdPartyLicenses.MinGWRuntimeVersion`; po review obě `15.0.0 (alpha)`, viz níže). Text
okna licencí FFmpeg pro Windows je neutrální vůči architektuře, nové řetězce UI nejsou. Toolchain (překladač) se do
aplikace nedostává – AOT kompatibilita irelevantní, žádný nový balíček NuGet. Apache-2.0 i licence runtime MinGW-w64 jsou
kompatibilní s GPL-3.0.

**Zdroj u vydání (otázka 4):** `Get-FFmpegSourcePins -Os windows` vrací `Rids` (`win-x64`, `win-arm64`) a `LlvmMingw`
(URL, SHA-256, složka; kontrola formátu a souladu URL se složkou). `New-FFmpegSourceArchive -PublishDirectories`
(RID → publish složka) ověří verzi knihovny každého RID a `README.txt` uvádí řádek configure každého vydávaného RID,
u `win-arm64` URL + SHA-256 zipu llvm-mingw a příkaz se šestým argumentem. `release-build.ps1`: krok `ffmpeg-source` běží
jednou za OS s posledním RID (kontext má `Runtimes` a `PublishRoot`). Do T-226 se vydává jen `win-x64`, archiv má tedy
jeden řádek.

**Testy:** nový Pester `build/FFmpeg.Tests.ps1` (24 testů; v `PesterScriptTests`): výběr buildu podle RID a hostitele,
vypnutí a zpráva na hostiteli ARM64, jiné RID, klíč cache, výstupní složka podle RID, pin llvm-mingw, chyby MSYS2 pro
oba RID bez stahování (prázdná cache zůstane prázdná), argumenty skriptu, sada DLL importů ARM64 a kontrola ARM64 cache
(PE `0xAA64`, tabulka importů, `BUILD.txt`, řádek configure a `h264_d3d11va2` v `avcodec-63.dll`, LGPL; bez cache se
přeskočí). `ReleaseTools.Tests.ps1`: guard T-224, piny, archiv s oběma řádky, odmítnutí cizího RID / jiné verze ARM64,
`release-build.ps1`. `ThirdPartyLicensesTests`: `FFmpegWindowsArm64BuildDiffersFromX64OnlyInArchitectureAndToolchain`,
`MinGWRuntimeVersionOfTheArm64BuildIsThatOfThePinnedLlvmMingw` (po review nahrazený), `MinGWRuntimeNoticesAreThoseOfThePinnedLlvmMingw`
(porovná texty s rozbaleným toolchainem v cache, jinak přeskočen). Dočasné soubory jen v
`LAYERPEEK_PESTER_TESTS_TEMP` / scratchpadu, `AfterAll` je maže; vyhodnocení MSBuild ukazují na vlastní prázdnou cache.

**Spuštěno:** `dotnet build` 0 varování, 0 chyb; `LayerPeek.Scripts.Tests` (všechny soubory Pester) 21 testů (20 prošlo, 1 přeskočen na vyžádání, 0 selhání; Pester `build/FFmpeg.Tests.ps1` 24/24);
`LayerPeek.Camera.FFmpeg.Tests` 212 (193 prošlo, 19 přeskočeno – VideoToolbox/macOS, 0 selhání);
`ThirdPartyLicensesTests` + `LicensesViewModelTests` 49/49. Celé `dotnet test` (s E2E) spouští hlavní agent.

**Dokumentace:** `docs/prerequisites.md` (tabulka FFmpeg se sloupcem `win-arm64`, oddíl „Windows ARM64 navíc“ s pinem,
cache, chybami, linkovaným runtime a postupem „Nová verze llvm-mingw“, oddíl Windows ARM64 z T-224 „S kamerou“, časté
chyby), `docs/release.md` (jeden archiv pro oba Windows RID), `docs/hw-decoding.md` (D3D11VA na ARM64 neověřeno → T-227),
`docs/architecture.md`. CHANGELOG beze změny (ARM64 zatím uživatel nedostane, přidá T-226). CI (T-218) beze změny:
staví jen `win-x64`, klíč cache CI bere skript, takže se jednou přestaví.

**Úklid:** pokusný build, stažený zip a tarball jen ve scratchpadu agenta. V `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\`
nově `llvm-mingw-20261104-ucrt-x86_64\`, `win-arm64-710f5cd3…`, `win-x64-a87b7b1d…` (starší `win-x64-*` z jiných větví
build nemaže); žádné `tmp-*`.

Uzavřeno 2026-11-15: běh kamery na ARM64 přesunut do T-227 (sběrný úkol Windows ARM64, rozhodnutí uživatele 2026-11-12).

**Opravy po review (2026-11-15):**
- Nález 1 (otázka 8): `build-ffmpeg.sh` bere verzi runtime MinGW-w64 pro oba RID z `_mingw_mac.h` a připojí stav, když
  není `stable` (`15.0.0 (alpha)`; MSYS2 crt `14.0.0.r598…` i llvm-mingw 20261104 mají v hlavičce 15.0.0 „alpha“); pin
  `MINGW_RUNTIME_VERSION` zrušený. `ThirdPartyLicenses.MinGWRuntimeVersionX64` / `MinGWRuntimeVersionArm64` =
  `15.0.0 (alpha)`. Kontrola proti řádku `MinGW-w64 runtime:` v `BUILD.txt`: C# test pro `FFmpeg-BUILD.txt` vedle testů
  (RID z prvního řádku, nález 3), Pester `build/FFmpeg.Tests.ps1` pro cache `win-arm64`.
- Nález 2: verze podle architektury jen na Windows (`OperatingSystem.IsWindows()`), jinde hodnota x64; test
  `MinGWRuntimeVersionShownIsThatOfTheRunningWindowsArchitecture`.
- Nález 4: komentář u `-static-libgcc` ve skriptu podle link mapy (z llvm-mingw jen pět objektů compiler-rt, nic
  z libunwind ani libc++).
- Nález 5: po `Unzip` se smažou prázdné vynechané složky; úplnost llvm-mingw se posuzuje i obsahem `verified.sha256`
  (musí být `LayerPeekLlvmMingwSha256`); chyba neshody SHA uvádí i přerušené stažení; v `docs/prerequisites.md`
  („Nová verze llvm-mingw“) ruční smazání starých složek llvm-mingw a cache. Pester kontroluje značku a chybějící složky.
- Nález 6 (konflikt `docs/architecture.md` s `develop`) se při sloučení vyřeší ručně (postup v hlášení hlavnímu agentovi).
- Změna skriptu = nové klíče cache, ověřeno znovu: `dotnet build` 246 s (nová x64 cache, `MinGW-w64 runtime: 15.0.0 (alpha)`), 0 varování; `dotnet publish -r win-arm64 -warnaserror` 233 s včetně nového stažení a rozbalení llvm-mingw (bez prázdných složek `python`, `i686-…`, `armv7-…`), 8 PE ARM64, 0 problémů, DLL stejných velikostí; `-r win-x64` 8 PE x64, 0 problémů; `LayerPeek.Core ->` jednou. Licenční testy 49/49, `LayerPeek.Camera.FFmpeg.Tests` 193/212 (19 přeskočeno), `LayerPeek.Scripts.Tests` 21 (20 prošlo, 1 přeskočen na vyžádání, 0 selhání). Zastaralé vlastní cache `win-arm64-710f5cd3…` a `win-x64-a87b7b1d…` smazané.
## Review
Review 2026-11-15 (qa-reviewer): `dotnet build` 0 varování; publish `win-arm64` i `win-x64` z cache (2 s), 8 PE ARM64
`0xAA64` / x64 `0x8664`, 0 problémů; `LayerPeek.Scripts.Tests` 20/21 (1 přeskočen), `LayerPeek.Camera.FFmpeg.Tests`
193/212 (19 přeskočeno), licenční testy 49/49.

Opraveno v review:
- `tests/LayerPeek.Camera.FFmpeg.Tests/FFmpegWindowsBuildTests.cs`: `AllowedImports` povolovalo jen `msvcrt.dll`, takže
  `TheLibrariesDependOnlyOnEachOtherAndOnWindows` by na zařízení ARM64 (T-227) selhal na importech
  `api-ms-win-crt-*-l1-1-0.dll`; seznam je teď podle architektury procesu (stejný jako `SYSTEM_DLLS` skriptu).

Zbývající nálezy (neblokující):
1. Verze runtime MinGW-w64 v okně licencí se určuje dvěma různými způsoby: x64 `14.0.0` z balíčku MSYS2
   (`14.0.0.r598…`), ARM64 `15.0.0` z `_mingw_mac.h`. Hlavičky obou toolchainů ale říkají `15.0.0` se stavem
   `"alpha"` (vývojové snímky po 14.0.0) a nový řádek `MinGW-w64 runtime: 15.0.0` v `BUILD.txt` x64 cache odporuje
   oknu licencí (`14.0.0`). Sjednotit pravidlo (např. obě podle hlavičky včetně stavu, nebo obě podle verze snímku).
2. `ThirdPartyLicenses.cs:209` volí verzi podle `ProcessArchitecture`, takže na macOS (Arm64) okno ukazuje u položky
   „Windows only“ `15.0.0` (dřív `14.0.0`); podmínit `OperatingSystem.IsWindows()`.
3. `ThirdPartyLicensesTests.MinGWRuntimeVersionIsTheOneThatBuiltTheDlls` by selhal, kdyby se ARM64 `FFmpeg-BUILD.txt` dostal vedle testů (např. `dotnet test -r win-arm64`) projektu
   `LayerPeek.App.Tests` (ARM64 `BUILD.txt` nemá balíček crt); porovnávat raději řádek `MinGW-w64 runtime:` podle RID.
4. Komentář `build/windows/build-ffmpeg.sh:124` uvádí, že se přilinkuje „static libunwind“, link mapa ale ukázala, že
   z libunwind nic – upravit kvůli licenčnímu dohledání.
5. Drobnosti: `Unzip` s `Exclude` nechá prázdné složky `python`, `i686-w64-mingw32`, `armv7-w64-mingw32`; značka
   `verified.sha256` llvm-mingw se kontroluje jen existencí, ne obsahem proti `LayerPeekLlvmMingwSha256`; stará vydání
   llvm-mingw (~570 MB) se po změně pinu nemažou (zmínit v „Nová verze llvm-mingw“).
6. Sloučení s `develop`: textový konflikt jen v `docs/architecture.md` (odstavec Camera.FFmpeg, T-206 ho také měnil);
   ostatní soubory (`FFmpegFactAttribute.cs`, `ReleaseTools.Tests.ps1`, `docs/hw-decoding.md`) se sloučí automaticky.

Opakovaná review 2026-11-15 (qa-reviewer): nálezy 1–5 vyřešené (verze runtime MinGW-w64 z `_mingw_mac.h` pro obě
architektury, `15.0.0 (alpha)`; jediný zdroj řádek `MinGW-w64 runtime:` v `BUILD.txt`; drift test podle RID z
`FFmpeg-BUILD.txt`; obsah `verified.sha256` llvm-mingw porovnaný s pinem; prázdné složky odstraněné; dokumentace).
V review jen opravené prázdné řádky v `ThirdPartyLicensesTests.cs`. `dotnet build` 0 varování, publish `win-arm64`
z cache 8 PE ARM64 bez problémů, licenční testy 49/49, `LayerPeek.Scripts.Tests` 20/21 (1 přeskočen). Schváleno.

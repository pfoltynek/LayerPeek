---
id: T-225
title: Windows ARM64 – vlastní LGPL build FFmpeg pro `win-arm64` (křížový překlad z x64)
status: todo
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
- [ ] `dotnet publish src/LayerPeek.App -c Release -r win-arm64` na Windows x64 přeloží FFmpeg pro ARM64 z připnutého
      tarballu do cache `win-arm64-…` (druhý publish nic nepřekládá) a publish obsahuje ARM64 DLL FFmpeg a
      `FFmpeg-LICENSE.txt`
- [ ] `configure.log` potvrdí LGPL 3 nebo novější; řádek configure je stejný jako u `win-x64` až na architekturu a
      toolchain; obsahuje D3D11VA; žádná externí knihovna
- [ ] DLL jsou ARM64 PE a závisí jen na sobě navzájem a na systémových DLL Windows (automatická kontrola)
- [ ] `dotnet build`, `dotnet test` a publish `win-x64` na x64 bez toolchainu ARM64 fungují beze změny
- [ ] Chybějící toolchain ARM64 při publish `win-arm64` = srozumitelná chyba s návodem
- [ ] `ThirdPartyLicenses`, okno licencí a test pokrývají build `win-arm64`; jeden archiv zdroje u vydání s oběma
      řádky configure
- [ ] `docs/prerequisites.md`, `docs/hw-decoding.md`, `docs/architecture.md` aktuální
- [ ] Běh kamery na ARM64 (RTSP, nahrávání, timelapse, D3D11VA) → ověřuje T-227
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

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

## Poznámky k implementaci
(vyplní agent po dokončení)

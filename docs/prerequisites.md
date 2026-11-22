# Prerekvizity pro vývoj a Native AOT

Aplikace se publikuje jako **Native AOT** – výsledkem je jeden nativní spustitelný soubor bez nutnosti instalovat .NET runtime.

> **Důležité:**
> - Pro běžný vývoj (`dotnet build`, `dotnet run`, `dotnet test`, ladění F5 ve VS Code) stačí .NET SDK. Nativní toolchain je potřeba **jen pro `dotnet publish`** s AOT. **Výjimka: první `dotnet build` přeloží knihovny FFmpeg ze zdrojů, takže potřebuje toolchain platformy: na Windows MSYS2 s MinGW-w64 (T-202, `winget install MSYS2.MSYS2` a balíčky `make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm`), na macOS Xcode Command Line Tools (T-026, `xcode-select --install`).** Bez něj build hned na začátku (nic nestahuje) skončí chybou s návodem (`MSYS2 with MinGW-w64 is not installed in C:\msys64 …` / `The Xcode Command Line Tools are not installed (or not selected). …`). Další buildy používají cache a toolchain už nepotřebují. Bez kamery jde vždy `dotnet build -p:LayerPeekFFmpegEnabled=false`.
> - AOT nejde kompilovat napříč OS: Windows binárky se sestavují na Windows, macOS binárky na macOS. Vydávané RID: `win-x64`, `win-arm64` a `osx-arm64` (rozhodnuto 2026-10-21, `win-arm64` rozhodnutím uživatele 2026-11-12): `win-arm64` se na Windows x64 křížově překládá a balí (T-224, T-225, T-226) a od prvního vydání po 1.0.0 se nahrává s `win-x64` (rozhodnutí uživatele 2026-11-16; ověření na zařízení ARM64 dodatečně v T-227).
> - CI (GitHub Actions, T-218) jen sestaví, otestuje a AOT publikuje na Windows i macOS (viz „CI (GitHub Actions)“). Nic nevydává: binárky pro vydání se dál sestavují lokálně skripty pro vydání (T-055, postup v [release.md](release.md)). Pro nahrání na GitHub je potřeba `gh` CLI (viz „Vydání“).

## Společné (obě platformy)

| Co | Poznámka |
|---|---|
| .NET SDK 11 | verze podle `global.json` (aktuálně `11.0.100`, GA), stáhnout z https://dotnet.microsoft.com/download/dotnet/11.0 |
| ASP.NET Core runtime 11 | instaluje se automaticky se SDK, nic navíc. Projekt PrusaLink používá sdílený framework `Microsoft.AspNetCore.App` (Kestrel pro lokální proxy webu, T-028), takže ho `dotnet run`/`dotnet test` potřebují. AOT binárka ho obsahuje a na cílovém počítači nic nevyžaduje. |
| Git | 2.38 nebo novější pro `scripts/release.ps1` (sloučení přes `git merge-tree --write-tree`) |
| *(volitelně)* **Microsoft Edge** | jen pro E2E testy webu přes proxy (`tests/LayerPeek.E2E.Tests`, T-030). Testy používají nainstalovaný Edge přes Playwright (`Channel = "msedge"`, headless) a nic nestahují. Bez Edge se testy prohlížeče přeskočí (`dotnet test` neselže). Ovladač Playwright (Node.js, asi 100 MB) přidá NuGet balíček `Microsoft.Playwright` do výstupu tohoto testovacího projektu, nic dalšího se instalovat nemusí. |
| *(volitelně)* **FFmpeg z příkazové řádky** (`ffmpeg` na `PATH`, na macOS i `/opt/homebrew/bin` nebo `/usr/local/bin`) a lokální vzorky v `~/LayerPeek-samples` (jinou složku nastaví `LAYERPEEK_SAMPLES`) | jen pro test detektoru „Změna na podložce“ na celé lokální sérii úspěšného tisku (`BedChangeLocalSamplesTests`, T-155): JPEG a MP4 dekóduje nástroj `ffmpeg`, ne testovací závislost. Bez vzorků nebo bez `ffmpeg` se test přeskočí. |
| *(volitelně, Windows)* **PowerShell 7** (`pwsh` na `PATH`) | pro E2E testy skutečné aplikace (`tools/run-e2e.ps1`, T-044). Testy, které mění nastavení Windows (T-065), si před prvním zásahem spustí hlídací proces `pwsh … run-e2e.ps1 -RestoreOnly -AfterProcess <PID>`. Bez `pwsh` na `PATH` se žádný zásah neudělá a test selže s popisem. Jednotkové testy výběru varování (`RunE2EScriptTests`, T-066) běží v každém `dotnet test` a bez `pwsh` se přeskočí. |
| *(volitelně, Windows)* **druhý monitor** | pro E2E zásahy T-066: vypnutí druhého monitoru a měřítko druhého monitoru. Bez druhého (neprimárního) monitoru se tyto testy přeskočí s důvodem. |
| *(volitelně, Windows)* **primární monitor se 150 %** | pro E2E testy ikon T-070 (hlavní panel, titulky, tray, tray menu při 100 a 150 %). Monitor musí měřítko 150 % nabízet (malé monitory ne), jinak se testy se 150 % přeskočí. Hlavní panel nesmí být automaticky skrytý. Ostrost ilustrace v tray menu při 150 % ověří test jen v relaci přihlášené při 150 % (systémové DPI se mění jen odhlášením). |
| VS Code + rozšíření | C# Dev Kit, Avalonia for VS Code, EditorConfig (nabídnou se automaticky) |

## Identita v gitu (e-mail autora commitů)

Repozitář je veřejný, takže jméno a e-mail autora každého commitu vidí kdokoli. Bez `user.email` si git adresu odvodí
ze jména počítače (`jmeno@pocitac.domena`) a prozradí tak jméno počítače a vnitřní doménu sítě (v historii 394 commitů,
T-198 oddíl 3.1). Rozhodnutí uživatele 2026-11-11 (T-198 otázka 1): historie zůstává, jak je, a **od teď** se
commituje s adresou GitHub noreply. Nastavuje se v **každém klonu** na **každém počítači** (Mac i Windows); platí pro
klon i všechny jeho worktree (sdílený `.git/config`):

```bash
git config user.email 12541813+pfoltynek@users.noreply.github.com
git config user.email      # kontrola
git log -1 --format='%an <%ae> | %cn <%ce>'   # po prvním commitu: autor i committer s adresou noreply
```

Pojistka pro nové klony (rozhodnutí uživatele 2026-11-13, T-198 otázka 12), jednou **globálně** na každém počítači:
git pak commit bez nastaveného `user.email` odmítne, místo aby adresu odvodil ze jména počítače.

```bash
git config --global user.name "Petr Foltýnek"   # s useConfigOnly se ani jméno už neodvozuje ze systému
git config --global user.useConfigOnly true
```

Nový klon (nebo nový počítač) nastavení nemá, protože `.git/config` se neklonuje. Ostatní přispěvatelé si adresu
volí sami (GitHub nabízí vlastní adresu noreply v *Settings → Emails*).

## Nativní knihovny FFmpeg (kamera)

Projekt `LayerPeek.Camera.FFmpeg` potřebuje nativní knihovny FFmpeg 9.0.2 (LGPL verze 3 nebo novější, sdílené knihovny). Knihovny nejsou v repozitáři. Obstará je build (`build/FFmpeg.targets`): na obou platformách stáhne stejný oficiální zdrojový tarball `ffmpeg-9.0.2.tar.xz` z ffmpeg.org (~12 MB), ověří ho podle připnutého SHA256 a přeloží ho skriptem buildu platformy jen s tím, co aplikace používá (žádné externí knihovny). Windows od T-202 (do té doby hotový build BtbN se ~50 přibalenými knihovnami), macOS od T-026.

| | Windows (`win-x64`, T-202) | Windows ARM64 (`win-arm64`, T-225) | macOS (`osx-arm64`, T-026) |
|---|---|---|---|
| Skript buildu | `build/windows/build-ffmpeg.sh` (MSYS2 + MinGW-w64) | týž skript, křížově na x64 s llvm-mingw ze shellu MSYS2 | `build/macos/build-ffmpeg.sh` (Xcode Command Line Tools) |
| Kdy | každý build bez RID na x64 a `-r win-x64` | jen `-r win-arm64` (publish, `release-build.ps1`) | každý build na Macu s Apple Silicon |
| Knihovny | `avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll` (celkem 6,4 MB) | stejné názvy, ARM64 (celkem 4,4 MB) | `libavformat.63.dylib`, `libavcodec.63.dylib`, `libavutil.61.dylib`, `libswscale.10.dylib` |
| Hardwarové dekódování | D3D11VA (`--enable-d3d11va`, hwaccel `h264_d3d11va` a `h264_d3d11va2`) | D3D11VA (stejně; běh ověří T-227) | VideoToolbox (`h264_videotoolbox`, T-203) |
| Cache | `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\win-x64-<klíč>\` | `…\ffmpeg\win-arm64-<klíč>\` + `…\ffmpeg\llvm-mingw-<verze>-ucrt-x86_64\` | `~/Library/Caches/LayerPeek.Build/ffmpeg/osx-arm64-<klíč>/` |
| První build | stáhne tarball a přeloží ho (na 16 vláknech asi 3,5 min) | stáhne llvm-mingw (~190 MB) a tarball, přeloží (první publish celkem 4 min 36 s) | stáhne tarball a přeloží ho (na Apple Silicon asi 30–60 s) |

Klíč cache = SHA256 ze SHA256 tarballu a obsahu skriptu buildu (u `win-arm64` i SHA256 zipu llvm-mingw), takže změna příznaků, verze nebo toolchainu sestaví novou cache. Staré složky cache (i `<sha256>` z doby BtbN) build nemaže, smazat je jde ručně.

Společné:

- **První build potřebuje internet a toolchain platformy.** Tarball se ověří podle připnutého SHA256 (`LayerPeekFFmpegSourceSha256`). Při neshodě build selže a stažený soubor se zahodí. Další buildy jen zkontrolují cache, fungují offline a toolchain už nepotřebují. Jinou složku cache nastavíš vlastností nebo proměnnou prostředí `LayerPeekFFmpegCacheRoot`. `LayerPeekFFmpegEnabled=false` krok vypne úplně (testy FFmpeg se pak přeskočí, kamera nefunguje).
- **Nic se neinstaluje.** Chybějící toolchain build zjistí hned na začátku (nic nestáhne) a skončí chybou s návodem.
- **Vydání (T-199)** přikládá zdroj FFmpeg (`ffmpeg-source-<verze>-<os>.tar.xz`, `docs/release.md`, „Zdrojový kód FFmpeg“): na obou OS stejný tarball beze změny, skript buildu dané platformy a `README.txt`. `scripts/release-build.ps1` bere tarball z vlastní cache vedle této (`~/Library/Caches/LayerPeek.Build/ffmpeg-source/`, `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg-source\`; potřebuje `tar` se zápisem xz). Stahuje jen při prvním vydání dané verze FFmpeg a nic v cache nemaže.
- Knihovny a `FFmpeg-LICENSE.txt` se kopírují do výstupu i do publish složky projektů, které na FFmpeg projekt odkazují. V `LayerPeek.app` jsou knihovny v `Contents/MacOS` (vedle exe, jako ostatní nativní knihovny) a licence v `Contents/Resources`.
- IDE (design-time build) nic nestahuje ani nepřekládá. Jiné RID (např. `osx-x64`, `win-x86`) a Intel Mac krok přeskočí. Na hostiteli **Windows ARM64 bez RID** build FFmpeg vypne a vypíše zprávu (ne varování) `no FFmpeg camera libraries. On a Windows ARM64 machine a build without a runtime identifier is not supported …` (rozhodnutí uživatele 2026-11-12); pro ARM64 aplikaci `-r win-arm64`.
- Sada funkcí je na obou platformách stejná (test `ThirdPartyLicensesTests.FFmpegBuildsOfBothPlatformsHaveTheSameFeatures`), liší se jen řádky platformy (architektura, toolchain, vlákna, hardwarový dekodér). `configure` přidá automaticky závislosti vybraných komponent (demuxery `asf`, `mov`, `mpegts`, `rm` a protokoly `rtp`, `udp`, `http` kvůli RTSP; muxer `mov`, parser `ac3` a bitstream filtry `aac_adtstoasc`, `vp9_superframe` kvůli muxeru `mp4`). Demuxer `mov` čte i vlastní nahrávky MP4 a timelapse (T-165). Aplikace ostatní nepovolí (`protocol_whitelist`, `format_whitelist`, `codec_whitelist`, T-014). Muxer `mp4` (T-147) slouží nahrávání kamery (T-144): pakety H.264 z RTSP se jen přebalí do fragmentovaného MP4, žádný enkodér se nepřidává. Převod Annex B na délkově prefixované NAL jednotky a SPS/PPS na `avcC` dělá muxer sám, bitstream filtry `h264_mp4toannexb` ani `extract_extradata` potřeba nejsou. Bez `--enable-gpl` a `--enable-nonfree`, licence podle `configure`: „LGPL version 3 or later“ (skript to v `configure.log` kontroluje).
- **Když build selže:** logy `configure.log` a `make.log` zůstanou ve složce `tmp-…` v cache (smaže je build po dvou hodinách). Když tarball z ffmpeg.org zmizí, změň v `build/FFmpeg.targets` `LayerPeekFFmpegSourceUrl`, `LayerPeekFFmpegSourceSha256` a `LayerPeekFFmpegSourceFolder` na jiný oficiální tarball 9.0.x (stejné verze knihoven). Postup je v „Nová verze FFmpeg“ níže.
- **Ověření tarballu je automatické:** každý první build ověří připnutý SHA256 a při neshodě skončí chybou. Uživatel ani vývojář nic ručně neověřuje. FFmpeg nezveřejňuje SHA256, jen podpisy GPG (`.asc`). Proto se připnutý hash při změně verze ověřuje skriptem `build/update-ffmpeg.ps1` proti nezávislému zdroji (vzorec Homebrew) a podpisem GPG, když je `gpg` k dispozici. Hash 9.0.2 (`8c38502…e002e`) souhlasí se vzorcem Homebrew (ověřeno 2026-10-29), podpis GPG ověřený nebyl (`gpg` na stroji není).

Windows navíc (T-202):

- **Potřeba je MSYS2 s MinGW-w64** (rozhodnutí uživatele 2026-11-12), už pro první `dotnet build`. Instalace (jednou):

  ```powershell
  winget install MSYS2.MSYS2
  C:\msys64\usr\bin\bash.exe -lc "pacman -S --needed --noconfirm make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm"
  ```

  Na vývojovém stroji ověřeno s gcc 16.2.0, nasm 3.02, make 4.4.1 a mingw-w64-crt 14.0.0 (2026-11-15). Výchozí umístění je `C:\msys64`; jiné nastaví vlastnost nebo proměnná prostředí `LayerPeekMsys2Root`. `pkgconf` potřeba není. Build před stažením zkontroluje soubory, které skript potřebuje (`usr\bin\bash.exe`, `make.exe`, `cmp.exe`, `tar.exe`, `xz.exe`, `mingw64\bin\gcc.exe`, `nasm.exe`, `objdump.exe`, `strip.exe`, `mingw64\lib\libwinpthread.a`). Bez nich skončí chybou:

  ```
  error : MSYS2 with MinGW-w64 is not installed in C:\msys64 (missing: usr\bin\bash.exe, …). The first build on Windows
  compiles the FFmpeg camera libraries from source and needs it. To fix it: Install MSYS2 (winget install MSYS2.MSYS2,
  or https://www.msys2.org/), then in an MSYS2 shell: pacman -S --needed make diffutils mingw-w64-x86_64-gcc
  mingw-w64-x86_64-nasm, and build again. … To build without the camera libraries instead:
  dotnet build -p:LayerPeekFFmpegEnabled=false …
  ```

  Visual Studio (MSVC) první build nepotřebuje, ten je dál jen pro AOT publish.
- **Cesty:** diakritika v cestě (profil `C:\Users\Novák`) nevadí: `objdump` se spouští s holým názvem souboru, protože cesty vypisuje v kódové stránce ANSI. Mezera ve jménu profilu taky ne: dočasná složka `configure` je podsložka pracovní složky, s mezerou v cestě se použije její krátký tvar 8.3 (`cygpath -d`). Když svazek krátké názvy nemá, build skončí hned (před překladem) chybou `… contains a space and has no short 8.3 name`; pak `LayerPeekFFmpegCacheRoot` na složku bez mezer.
- **Aktualizace MSYS2** (`pacman -Syu`) cache nezmění (klíč je tarball + skript). Nová verze runtime MinGW-w64 se do DLL dostane až s novým buildem (změna skriptu nebo verze FFmpeg). `BUILD.txt` v cache má řádek `Packages:` (`pacman -Q` crt, winpthreads, binutils, gcc) a řádek `MinGW-w64 runtime:` – verzi z hlaviček toolchainu (`_mingw_mac.h`) se stavem, když nejde o vydání, např. `15.0.0 (alpha)` (vývojový snímek po 14.0.0; stejné pravidlo pro `win-x64` i `win-arm64`, rozhodnutí uživatele 2026-11-15). Jeho kopie `FFmpeg-BUILD.txt` jde jen do výstupu buildu (ne do publish) a test `ThirdPartyLicensesTests.MinGWRuntimeVersionIsTheOneThatBuiltTheDlls` selže, když řádek neodpovídá verzi položky „MinGW-w64 runtime“ pro ten RID (`MinGWRuntimeVersionX64` / `MinGWRuntimeVersionArm64` v `ThirdPartyLicenses.cs`). Pak tam verzi a texty licencí (`/mingw64/share/licenses/crt`, `libwinpthread`) aktualizovat.
- MSBuild spustí skript přes `bash.exe` z MSYS2 (`--noprofile --norc`). Skript si nastaví čisté prostředí (`env -i`, `PATH=/mingw64/bin:/usr/bin`, `MSYSTEM=MINGW64`, jen `SYSTEMROOT`, `WINDIR`, `COMSPEC` a dočasná složka), takže se do buildu nedostane nic z `PATH` Windows (jiné překladače, `CFLAGS`, jiná prostředí MSYS2). `COMSPEC` potřebuje `windres` (verzní prostředky DLL).
- **Runtime MinGW-w64 je staticky:** `-static-libgcc` (libgcc, GPL 3 s GCC Runtime Library Exception, která u přeloženého kódu nic nevyžaduje) a `-l:libwinpthread.a` (winpthreads, MIT a části BSD-3-Clause; FFmpeg používá vlákna Windows `--enable-w32threads --disable-pthreads`, z winpthreads jen `clock_gettime` a `nanosleep`). Spolu s nimi je v DLL i startovací kód a pomocné funkce mingw-w64-crt (ZPL 2.1 a další volné licence). Jejich texty jsou v okně licencí (položka „MinGW-w64 runtime“, `Licenses/Texts/mingw-w64-*.txt`, převzaté z `/mingw64/share/licenses/crt` a `libwinpthread`).
- Výsledek se kontroluje: formát `pei-x86-64` (`objdump -f`) a importy (`objdump -p`, „DLL Name“) jen sousední knihovny FFmpeg a DLL Windows (`KERNEL32`, `msvcrt`, `bcrypt`, `WS2_32` a pár dalších povolených ve skriptu), nikdy `libgcc_s_seh-1.dll`, `libwinpthread-1.dll` ani jiná DLL z MSYS2. Totéž hlídá test `FFmpegWindowsBuildTests` (čte tabulku importů DLL vedle testů) a k tomu, že knihovny obsahují D3D11VA jako jediný hardwarový dekodér a jen dekodér `h264`. `d3d11.dll` a `dxgi.dll` FFmpeg načítá až za běhu (`LoadLibrary`), jen při hardwarovém dekódování.
- Přesný řádek `configure` (je i v `BUILD.txt` v cache):

  ```
  configure --enable-shared --disable-static --enable-version3 --disable-programs --disable-doc --disable-debug
    --disable-autodetect --disable-everything --disable-avdevice --disable-avfilter --disable-swresample
    --enable-network --enable-protocol=tcp,file --enable-demuxer=rtsp,h264 --enable-muxer=mp4 --enable-parser=h264
    --enable-decoder=h264 --enable-d3d11va --enable-hwaccel=h264_d3d11va,h264_d3d11va2 --target-os=mingw32
    --arch=x86_64 --enable-w32threads --disable-pthreads --extra-ldflags=-static-libgcc --extra-libs=-l:libwinpthread.a
  ```

- **D3D11VA:** `h264_d3d11va2` je hwaccel hwcontextu `d3d11va` (`AV_PIX_FMT_D3D11`, `av_hwdevice_ctx_create`), který použije T-206; `h264_d3d11va` je starší API (aplikace předá vlastní `ID3D11VideoContext`, bez typu zařízení). Obojí je D3D11VA, DXVA2 ani D3D12VA build neobsahuje (rozhodnutí uživatele 2026-11-12). Jen hlavičky MinGW-w64 a DLL Windows, žádné SDK ani externí knihovna.
- Architektura se vybírá identifikátorem RID (první argument skriptu, `_LayerPeekFFmpegRid` v `build/FFmpeg.targets`): `win-x64`, nebo `win-arm64` (viz „Windows ARM64 navíc“). `LayerPeek.Camera.FFmpeg` se sestavuje bez RID; RID aplikace mu předá `LayerPeek.App.csproj` (a `LayerPeek.Camera.FFmpeg.Tests.csproj`) jako `LayerPeekAppRuntimeIdentifier` pro každý RID, bez RID rozhoduje architektura hostitele. Výběr hlídá Pester `build/FFmpeg.Tests.ps1`.

Windows ARM64 navíc (T-225, rozhodnutí uživatele 2026-11-12):

- **Toolchain:** [llvm-mingw](https://github.com/mstorsjo/llvm-mingw) pro hostitele Windows x64 ve variantě ucrt, připnuté vydání `20261104` (LLVM 23.1.5, runtime MinGW-w64 15.0.0) v `build/FFmpeg.targets` (`LayerPeekLlvmMingwUrl`, `LayerPeekLlvmMingwSha256` = digest assetu z GitHubu, ověřený proti staženému souboru 2026-11-15, `LayerPeekLlvmMingwFolder`). První build `win-arm64` zip stáhne (~190 MB), ověří SHA256 (při neshodě ho zahodí a skončí chybou), rozbalí bez Pythonu (lldb) a sysrootů i686 a armv7 do `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\llvm-mingw-20261104-ucrt-x86_64\` (~570 MB, značka `verified.sha256`) a zip smaže. Sysroot x86_64 zůstává: build FFmpeg překládá a spouští pár generátorů na stroji buildu (`--host-cc=x86_64-w64-mingw32-gcc`). **Nic se neinstaluje**, nic nejde do `PATH`. Z MSYS2 stačí `bash`, `make`, `cmp`, `tar`, `xz` (balíčky `make diffutils`); bez nich build skončí před stažením chybou `MSYS2 is not installed in … The first win-arm64 build cross-compiles …` s návodem a únikem `dotnet publish -r win-arm64 -p:LayerPeekFFmpegEnabled=false`.
- **`dotnet build` / `dotnet test` / publish `win-x64` llvm-mingw nepotřebují** (otázka 2 v T-225).
- **Řádek configure** je stejný jako u `win-x64` (jedno pole `CONFIGURE_FLAGS`, `--arch=aarch64`) plus jen `--enable-cross-compile --cross-prefix=aarch64-w64-mingw32- --host-cc=x86_64-w64-mingw32-gcc`; hlídá test `ThirdPartyLicensesTests.FFmpegWindowsArm64BuildDiffersFromX64OnlyInArchitectureAndToolchain`. NEON zapnutý, licence LGPL 3, D3D11VA.
- **Kontrola výsledku** (skript): formát `coff-arm64` (`llvm-objdump -f`) a importy jen sourozenecké DLL a DLL Windows: `KERNEL32`, `bcrypt`, `WS2_32` a místo `msvcrt.dll` API sady Universal C Runtime `api-ms-win-crt-*-l1-1-0.dll` (součást Windows 10 a 11); nikdy `libunwind.dll`, `libc++.dll`, `libwinpthread-1.dll`. Totéž bez načtení (ARM64 DLL na x64 načíst nejde) kontroluje Pester `build/FFmpeg.Tests.ps1` nad cache (PE `0xAA64`, tabulka importů, `BUILD.txt`, řádek configure v `avcodec-63.dll`, řetězec `h264_d3d11va2`); bez cache `win-arm64` se ty testy přeskočí.
- **Staticky přilinkované:** runtime MinGW-w64 z llvm-mingw (crt s vazbou na UCRT, winpthreads; texty licencí shodné s x64, verze `15.0.0 (alpha)` z hlaviček llvm-mingw, `MinGWRuntimeVersionArm64`) a z LLVM jen pět objektů compiler-rt builtins (`chkstk`, `divti3`, `udivti3`, `umodti3`, `udivmodti4`; Apache-2.0 s LLVM Exceptions, výjimka u kódu vloženého překladem nevyžaduje uvedení licence, stejně jako libgcc u x64). libunwind ani libc++ se nepřilinkují (ověřeno link mapou všech čtyř DLL 2026-11-15).
- **Hostitel ARM64:** prostý `dotnet build` FFmpeg vypne (viz výše). `-r win-arm64` by na ARM64 stroji běžel přes emulaci x64 (llvm-mingw i MSYS2 jsou x64 binárky) – neověřeno. Testy FFmpeg na zařízení: `dotnet test tests/LayerPeek.Camera.FFmpeg.Tests -r win-arm64` (T-227).
- **Nová verze llvm-mingw:** v `build/FFmpeg.targets` změnit všechny tři hodnoty `LayerPeekLlvmMingw*` (SHA256 = `digest` assetu, `gh api repos/mstorsjo/llvm-mingw/releases/tags/<tag>`, a ověřit proti staženému souboru), po buildu porovnat `MinGW-w64 runtime:` v `BUILD.txt` cache `win-arm64-…`; jiná verze = Pester `build/FFmpeg.Tests.ps1` selže, pak upravit `ThirdPartyLicenses.MinGWRuntimeVersionArm64`. Složku starého vydání (`%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\llvm-mingw-<stará verze>-ucrt-x86_64\`, ~570 MB) ani starou cache `win-arm64-…` build nemaže – smazat ručně. Test `ThirdPartyLicensesTests.MinGWRuntimeNoticesAreThoseOfThePinnedLlvmMingw` porovná texty licencí s rozbaleným toolchainem.

macOS navíc:

- **Potřeba jsou Xcode Command Line Tools** (`clang`, `make`, `otool`, `lipo`), už pro první `dotnet build`. Build je kontroluje před stažením (`xcode-select -p`, `xcrun --find clang`, `xcrun --find make`; `xcode-select` na rozdíl od `xcrun` neotevře instalační dialog). Bez nich skončí chybou:

  ```
  error : The Xcode Command Line Tools are not installed (or not selected). The first build on macOS compiles the FFmpeg
  camera libraries from source and needs them (clang, make). To fix it: run xcode-select --install, wait for the
  installation to finish and build again (xcode-select -p must print a folder). To build without the camera libraries
  instead: dotnet build -p:LayerPeekFFmpegEnabled=false …
  ```

  Ověřeno simulací (`DEVELOPER_DIR=/nonexistent`, prázdná cache): build skončí za 2 s a nic nestáhne. Jinak stačí nástroje systému (`/usr/bin/tar` rozbalí `.tar.xz`). `pkg-config`, `nasm` ani Homebrew potřeba nejsou. Skript spouští `configure` a `make` s čistým prostředím (`PATH=/usr/bin:/bin:/usr/sbin:/sbin`, bez `CFLAGS`/`LDFLAGS`/`PKG_CONFIG_PATH`), takže se do buildu nedostane nic z Homebrew. Varování `pkg-config not found` v logu `configure` je v pořádku (žádné externí knihovny se nehledají).
- Výsledek se kontroluje: architektura `arm64` (`lipo`) a závislosti jen na sousedních knihovnách (`@loader_path/…`) a na systému (`/usr/lib`, `/System/Library`), nikdy `/opt/homebrew` ani `/usr/local` (`otool -L`). Knihovny mají install name `@loader_path/<název>`, takže se načtou ze složky, ve které jsou (výstup buildu, publish i bundle). Minimální macOS je `14.0` jako `LSMinimumSystemVersion` v `build/macos/Info.plist` (test hlídá shodu).
- Přesný řádek `configure` (je i v `BUILD.txt` v cache):

  ```
  configure --enable-shared --disable-static --enable-version3 --disable-programs --disable-doc --disable-debug
    --disable-autodetect --disable-everything --disable-avdevice --disable-avfilter --disable-swresample
    --enable-network --enable-protocol=tcp,file --enable-demuxer=rtsp,h264 --enable-muxer=mp4 --enable-parser=h264
    --enable-decoder=h264 --enable-videotoolbox --enable-hwaccel=h264_videotoolbox --arch=aarch64
    --install-name-dir=@loader_path
    --extra-cflags="-arch arm64 -mmacosx-version-min=14.0" --extra-ldflags="-arch arm64 -mmacosx-version-min=14.0"
  ```

  Muxer `mp4` dělá knihovny větší asi o 262 KiB (`libavformat` o 245 KiB, `libavcodec` o 16 KiB).
- **VideoToolbox (T-203):** `--enable-videotoolbox --enable-hwaccel=h264_videotoolbox` zapíná hardwarové dekódování H.264 okna kamery (rozhodnutí uživatele 2026-11-12, T-192; do 2026-11-12 jen software). Kvůli `--disable-autodetect` a `--disable-everything` musí být oba přepínače výslovně. Frameworky VideoToolbox, CoreMedia, CoreVideo, CoreFoundation a CoreServices jsou součástí macOS (`/System/Library/Frameworks`, kontrola závislostí je povoluje), nic dalšího se neinstaluje: Xcode Command Line Tools stačí. VideoToolbox není v seznamu GPL ani nonfree, licence zůstává LGPL 3. Knihovny jsou větší o 27 KB (`libavcodec` 1 624 952 B, `libavutil` 742 648 B). Klíč cache obsahuje obsah skriptu, takže se po změně přestaví sama (ověřeno 2026-11-12: nová složka `osx-arm64-c88d70…`, 39 s). Test `HardwareDecodingFallbackTests.TheMacOSBuildHasVideoToolboxForH264AndStaysLgpl` hlídá, že knihovny `h264_videotoolbox` obsahují (`av_hwdevice_iterate_types` + `avcodec_get_hw_config`).

### Nová verze FFmpeg

Obě platformy překládají stejný tarball (stejné verze knihoven `avformat-63`, `avcodec-63`, `avutil-61`, `swscale-10`; jiná hlavní verze znamená změnu názvů knihoven v `build/FFmpeg.targets`, v obou skriptech buildu a v `FFmpegRuntime`, i kontroly `^9\.0\.` ve skriptech buildu).

1. **Ověřit a zapsat připnuté hodnoty** skriptem pro správce `build/update-ffmpeg.ps1` (PowerShell 7, běží na macOS i Windows, i s `pwsh` nainstalovaným jako dotnet global tool; nic neinstaluje, jen čte přes HTTPS):

   ```bash
   # jen ověří a vypíše hodnoty
   pwsh build/update-ffmpeg.ps1 -Version 9.0.3
   # ověří a zapíše je
   pwsh build/update-ffmpeg.ps1 -Version 9.0.3 -Update
   ```

   Před stahováním skript zkontroluje verzi a to, že obě platformy v `build/FFmpeg.targets` mají stejné knihovny se stejnými hlavními verzemi (`avformat-63.dll` ↔ `libavformat.63.dylib` …, od T-202 jedna k jedné). Pak stáhne `https://ffmpeg.org/releases/ffmpeg-<verze>.tar.xz` do dočasné složky, spočítá SHA256 a porovná ho se vzorcem Homebrew (`https://formulae.brew.sh/api/formula/ffmpeg.json`, `urls.stable.checksum`, Homebrew potřeba není). Vzorec musí mít přesně stejnou URL tarballu: když je Homebrew na jiné verzi, skript skončí chybou a přepínač, který by to obešel, není (rozhodnuto 2026-10-29; zkusit znovu, až Homebrew verzi převezme). Když je na `PATH` `gpg`, ověří i podpis `<tarball>.asc` klíčem FFmpeg (`https://ffmpeg.org/ffmpeg-devel.asc`, otisk `FCF986EA15E6E293A5644F10B4322F04D67658D8` je ve skriptu) v dočasné domovské složce GnuPG, takže se osobní klíčenka nemění. Bez `gpg` vypíše `GPG signature: SKIPPED`. Hodnoty: `LayerPeekFFmpegSourceUrl`, `LayerPeekFFmpegSourceSha256`, `LayerPeekFFmpegSourceFolder` a `SourceTarballUri` (`ThirdPartyLicenses.cs`).

   Při jakékoli neshodě skončí kódem 1 a nezapíše nic. `-Update` zapíše hodnoty do `build/FFmpeg.targets` a `src/LayerPeek.App/Licenses/ThirdPartyLicenses.cs`. Bez `-Update` je jen vypíše; když se shodují s připnutými, řekne to. Dočasná složka se vždy smaže. Zdroj pro vydání (T-199) nic dalšího nepotřebuje: je to tentýž tarball.
2. U nové větve FFmpeg upravit kontrolu verze `^9\.0\.` v `build/macos/build-ffmpeg.sh` a `build/windows/build-ffmpeg.sh`; u každé nové verze verzi, kterou hlídá `ThirdPartyLicensesTests` (`ffmpeg-9.0.2.`).
3. Na obou platformách `dotnet build` (nová cache, klíč obsahuje SHA256) a `dotnet test`: testy FFmpeg ověří verze knihoven, licenci LGPL 3 a importy DLL, testy okna licencí shodu odkazů s `build/FFmpeg.targets`.
4. Aktualizovat verze v dokumentaci (tento soubor, `docs/architecture.md`) a `CHANGELOG.md`.

Testy skriptu (`build/update-ffmpeg.Tests.ps1`, Pester, běží v `dotnet test`) používají náhradní stahování, vzorec Homebrew a `gpg`, síť nepotřebují.

## Windows

| Co | Poznámka |
|---|---|
| **MSYS2 s MinGW-w64** (T-202) | **už pro první `dotnet build`**, který s ním přeloží knihovny FFmpeg (viz „Nativní knihovny FFmpeg“): `winget install MSYS2.MSYS2`, pak `C:\msys64\usr\bin\bash.exe -lc "pacman -S --needed --noconfirm make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm"`. Jiné umístění než `C:\msys64`: `LayerPeekMsys2Root`. Bez něj build skončí chybou `MSYS2 with MinGW-w64 is not installed in …` dřív, než cokoli stáhne |
| **Build Tools for Visual Studio 2026** (nebo Visual Studio 2022/2026) | bezplatné, plné IDE není nutné; jen pro AOT publish |
| Workload **Desktop development with C++** | obsahuje MSVC překladač/linker (komponenta `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`) a Windows SDK |
| Velopack **`vpk`** | jen pro `scripts/release-build.ps1` (instalátor Windows, od T-058 i `.app` a aktualizace pro macOS) a pro E2E test aktualizace `UpdateTests` (T-128, balí Debug sestavení; bez obnoveného `vpk` se přeskočí); lokální dotnet tool, `dotnet tool restore` ho stáhne sám (viz „Velopack `vpk`“ níže) |

Komponenta `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` se samostatně nestahuje, je součástí workloadu *Desktop development with C++* v instalátoru Visual Studia.

Ověřeno 2026-11-15 (T-232) s Build Tools for Visual Studio 2026 18.12.0: MSVC 14.52 (složka sady nástrojů `14.52.37025`, překladač 19.52.37029) a Windows 11 SDK 10.0.26100, které workload instaluje jako doporučené. Novější Windows 11 SDK 10.0.28000 je ve workloadu jen volitelné; Native AOT ho nepotřebuje (linker bere jen knihovny importu systému, které má i 10.0.26100), proto se neinstaluje. Minimální verze MSVC ani SDK se nepředepisuje.

### Varianta A – winget (doporučeno)

```powershell
winget install --id Microsoft.VisualStudio.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive --wait"
```

### Varianta B – ručně

1. Stáhni **Build Tools for Visual Studio** z https://visualstudio.microsoft.com/downloads/ (sekce *Tools for Visual Studio*).
2. Spusť instalátor a zaškrtni workload **Desktop development with C++** (vpravo musí zůstat zaškrtnuté *MSVC … x64/x86 build tools* a *Windows 11 SDK*).
3. Nainstaluj (cca 2–7 GB).

Pokud už máš Visual Studio, otevři *Visual Studio Installer → Upravit* a zaškrtni workload **Desktop development with C++**.

Ověření:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property displayName
dotnet publish src/LayerPeek.App -c Release -r win-x64
```

První příkaz musí vypsat název instalace, druhý vytvoří `LayerPeek.exe` v `src/LayerPeek.App/bin/Release/net11.0/win-x64/publish/`.

### Windows ARM64 (`win-arm64`, T-224)

Native AOT pro Windows ARM64 se křížově překládá na Windows x64 (Microsoft Learn, [Cross-compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile): „To target ARM64 make sure the Visual Studio component "VS 2022 C++ ARM64/ARM64EC build tools (Latest)" is installed“). Navíc k workloadu výše je potřeba komponenta **`Microsoft.VisualStudio.Component.VC.Tools.ARM64`** (v instalátoru *Jednotlivé komponenty → MSVC … ARM64/ARM64EC build tools (nejnovější)*). Pro `dotnet build`, `dotnet test` a publish `win-x64` potřeba není.

Instalace (PowerShell **jako správce**; instalátor Visual Studia bez zvýšených oprávnění nic nezmění, pokus v T-055 skončil kódem 5007; `setup.exe` se musí spustit z jiné složky, než ve které leží, proto `Push-Location`):

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
Push-Location $env:TEMP
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe" modify --installPath $installPath --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --passive --norestart
Pop-Location
```

Parametry podle Microsoft Learn ([Use command-line parameters to install Visual Studio](https://learn.microsoft.com/en-us/visualstudio/install/use-command-line-parameters-to-install-visual-studio)): `modify` mění existující instalaci, `--installPath` je u `modify` povinný, `--passive` ukáže průběh bez dotazů, `--norestart` odloží případný restart. Visual Studio musí být zavřené. Totéž ručně: *Visual Studio Installer → Upravit → Jednotlivé komponenty*, hledat „ARM64“.

Ověření:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property displayName
dotnet publish src/LayerPeek.App -c Release -r win-arm64
```

První příkaz musí vypsat název instalace (jinak publish skončí chybou `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.ARM64`), druhý vytvoří ARM64 `LayerPeek.exe` v `src/LayerPeek.App/bin/Release/net11.0/win-arm64/publish/` (bez varování trim/AOT; balíčky `runtime.win-arm64.Microsoft.DotNet.ILCompiler`, `Microsoft.NETCore.App.Runtime.win-arm64` a další stáhne NuGet sám, hostitelský překladač zůstává `runtime.win-x64.*`).

- **S kamerou (T-225):** publish obsahuje ARM64 DLL FFmpeg a `FFmpeg-LICENSE.txt`, přeložené křížově s llvm-mingw (viz „Nativní knihovny FFmpeg“, „Windows ARM64 navíc“). `src/LayerPeek.App/LayerPeek.App.csproj` předá `LayerPeek.Camera.FFmpeg` RID jako `LayerPeekAppRuntimeIdentifier` (knihovní projekt se sestavuje bez RID). Bez kamery: `-p:LayerPeekFFmpegEnabled=false`.
- **Kontrola architektury:** `Get-PublishArchitectureCheck` (`scripts/ReleaseTools/ReleaseTools.psm1`) ověří, že `LayerPeek.exe` a všechny DLL publish složky mají v hlavičce PE pole Machine podle RID (`win-arm64` = ARM64 `0xAA64`, `win-x64` = x64 `0x8664`). Volá ji `scripts/release-build.ps1` po každém publish Windows a `scripts/aot-smoke.ps1` před spuštěním. Ručně: `Import-Module ./scripts/ReleaseTools/ReleaseTools.psm1; (Get-PublishArchitectureCheck -Directory src/LayerPeek.App/bin/Release/net11.0/win-arm64/publish -Runtime win-arm64).Problems` (prázdný výstup = v pořádku).
- **CI (T-236):** job `Cross-publish (win-arm64)` v `.github/workflows/ci.yml` dělá totéž (publish + kontrola architektury, bez spuštění) při každém pushi a PR s kódem; viz „CI (GitHub Actions)“.
- **Spuštění:** Windows na x64 ARM64 binárky spustit neumí, ověření běhu na zařízení ARM64 je T-227. `scripts/release-build.ps1` (T-226) `win-arm64` sestaví a zabalí ve výchozím buildu, smoke test zapíše jako „not verified by running (this machine is windows-x64)“; `scripts/aot-smoke.ps1 -Runtime win-arm64` na x64 skončí hláškou „cannot run on this machine“ a nic nepublikuje ani nespustí. Build `win-arm64` tedy potřebuje komponentu MSVC ARM64 i llvm-mingw (viz výše); od rozhodnutí uživatele 2026-11-16 (první vydání po 1.0.0) je `win-arm64` součástí buildu pro nahrání , takže je stroj vydání na Windows potřebuje vždy.

### Velopack `vpk` (instalátor T-017, macOS T-058)

Instalátor pro Windows balí nástroj **`vpk`** (Velopack, licence MIT) jako balicí krok `scripts/release-build.ps1`. Je to lokální dotnet tool s připnutou verzí v `.config/dotnet-tools.json`. Globálně se nic neinstaluje:

```powershell
dotnet tool restore        # stáhne vpk podle manifestu (release-build.ps1 to dělá sám)
dotnet vpk -h              # ověření; spouští se z kořene repozitáře
```

- Verze `vpk` musí být stejná jako NuGet balíček `Velopack` v `Directory.Packages.props` (teď `1.2.161`). `release-build.ps1` to ověří před sestavením a Pester test hlídá oba soubory. Při povýšení změň obě verze najednou.
- `vpk` 1.2.161 je framework-dependent pro .NET 8, 9 a 10 (verze pro .NET 11 neexistuje). Na stroji jen s .NET 11 ho spustí `"rollForward": true` v manifestu. Když je nainstalovaný runtime .NET 8–10, použije se ten.
- Při prvním `dotnet tool restore` je potřeba internet (NuGet). `vpk` se spouští s `--skip-updates`, takže sám nic dalšího nestahuje.
- `vpk` na Windows balí oba Windows RID (`win-x64` a `win-arm64`, T-226; každý ve vlastním kanálu). Balíčky pro macOS umí vytvořit jen na Macu: od T-058 je `vpk` součástí kroku `app` na macOS (`release-build.ps1` spustí `dotnet tool restore` i tam; podpis `codesign` z Xcode Command Line Tools). Pester test na macOS spustí skutečné `vpk`, když je obnovené (jinak test přeskočí).

## macOS

| Co | Poznámka |
|---|---|
| **Xcode Command Line Tools** | `clang` a linker pro AOT: `xcode-select --install`. **Od T-026 je potřeba i pro první `dotnet build` na Macu**, který s nimi přeloží knihovny FFmpeg (viz „Nativní knihovny FFmpeg“). Bez nich build skončí chybou `The Xcode Command Line Tools are not installed (or not selected)` dřív, než cokoli stáhne |
| *(volitelně)* plné Xcode | není potřeba pro AOT ani pro ad-hoc podpis a balení (ověřeno v T-019 jen s Command Line Tools) |
| nástroje systému pro balení `.app` (T-019) | `codesign`, `sips`, `iconutil`, `hdiutil`, `ditto`, `/usr/bin/tar`, `lsappinfo`, `lsregister`, `defaults` jsou součástí macOS, nic se neinstaluje. `scripts/release-build.ps1` je volá absolutní cestou. |
| *(volitelně)* **PowerShell 7** (`pwsh` na `PATH`) | pro skripty vydání (T-055, T-019), ověřovací skripty `tools/macos-verify.ps1` / `tools/windows-verify.ps1` (T-105; na Macu si přeloží pomocný nástroj `tools/macos-verify-helper.swift` přes `swiftc` z Xcode Command Line Tools) a jejich Pester testy. Instalace přes Homebrew (`brew install --cask powershell`), nebo jako dotnet global tool (`dotnet tool install --global PowerShell`, složka `~/.dotnet/tools` musí být na `PATH`). Skripty i testy fungují s oběma způsoby instalace: podřízený `pwsh` spouštějí přes `Invoke-Pwsh` (u dotnet tool `dotnet …/pwsh.dll`). Pester 5 viz „Pester (testy skriptů)“. |
| *(volitelně)* **Microsoft Edge** (v `/Applications`) | jen pro E2E testy webu přes proxy (`tests/LayerPeek.E2E.Tests`, T-030), stejně jako na Windows; bez Edge se přeskočí. Edge si při prvním spuštění založí vlastní soubory v `~/Library` (známý pozůstatek), viz [architecture.md](architecture.md), „Izolace testů na macOS“. Ověřeno s Edge 156.0.4314.50 (T-021). |

**Verze SDK:** `global.json` připíná stabilní `11.0.100` (GA z 2026-11-10, T-193) s `"rollForward": "latestFeature"` a `"allowPrerelease": false`, takže se použije novější nainstalované stabilní SDK řady 11.0 (např. `11.0.101` nebo `11.0.200`), ale ne preview ani RC. Kdo má jen RC SDK (`11.0.100-rc.*`), musí doinstalovat GA.

**Symbolický odkaz v cestě k repozitáři.** Když je repozitář otevřený přes cestu se symbolickým odkazem (např. `~/Code` → `/Volumes/Disk/Code`), `$PWD` drží logickou cestu přes odkaz. dotnet CLI s ní skládá relativní cestu k projektu (`src/LayerPeek.App`), ale `ProjectReference` řeší přes fyzickou cestu. Obnova NuGet pak u `LayerPeek.App` zahodí všechny odkazy na projekty a build / `dotnet publish -r osx-arm64` selže s CS0246 (`ServiceProvider`, `IHttpClientFactory`). Smazání `bin`/`obj` nepomůže, absolutní fyzická cesta k projektu ano.

- **Skripty v `scripts/`** (`release-build.ps1`, `aot-smoke.ps1`, `release*.ps1`) si cestu k repozitáři samy převedou na fyzickou (`Resolve-PhysicalPath` v `ReleaseTools.psm1`) a dotnet dostane jen fyzické cesty. Fungují tedy odkudkoli, i přes symbolický odkaz.
- **Ručně psané příkazy** (`dotnet build`, `dotnet publish src/LayerPeek.App …`) a VS Code potřebují, aby `$PWD` byla fyzická cesta. Jednorázově to zajistí zsh na konci `~/.zshrc`:

  ```zsh
  # Resolve symlinks in the working directory (dotnet restore breaks on symlinked paths)
  setopt CHASE_LINKS
  [[ "$PWD" != "$(pwd -P)" ]] && cd -P .
  ```

  VS Code otevírej ve fyzické cestě (`File → Open Folder`), aby build v IDE nešel přes odkaz. Ověření:

  ```bash
  echo "$PWD"; pwd -P      # musí vypsat stejnou cestu
  ```

Ověření:

```bash
xcode-select -p          # musí vypsat cestu, např. /Library/Developer/CommandLineTools
clang --version
dotnet publish src/LayerPeek.App -c Release -r osx-arm64   # Apple Silicon
```

Podporuje se jen Apple Silicon (`osx-arm64`), Intel Macy (`osx-x64`) ani universal binary se nevydávají (rozhodnuto 2026-10-21). Vydání má jen ad-hoc podpis bez Apple Developer ID a notarizace (T-019).

## Vydání (GitHub CLI)

Pro nahrání vydání na GitHub (`scripts/release-upload.ps1`, `scripts/release-publish.ps1`, postup v [release.md](release.md)) je potřeba **GitHub CLI** `gh`, přihlášené k účtu s právem zápisu do repozitáře. Samotné sestavení (`scripts/release-build.ps1`) ho nepotřebuje. Skripty používají `gh release list --json` (kontrola dvou konceptů, T-200); ověřeno s `gh` 2.105.0, starší verzi aktualizuj (`winget upgrade GitHub.cli` / `brew upgrade gh`).

```powershell
winget install --id GitHub.cli     # Windows
```

```bash
brew install gh                    # macOS
```

Přihlášení (jednou na každém stroji, token uloží `gh` do systémového úložiště klíčů):

```bash
gh auth login        # GitHub.com, HTTPS, přihlášení přes prohlížeč
gh auth status       # ověření
```

Pro `.tar.gz` na macOS stačí systémový `tar`. Na Windows skripty používají systémový `C:\Windows\System32\tar.exe`, ne `tar` z Git for Windows.

### winget (jen Windows, T-059)

Odeslání vydání do winget (`scripts/release-winget.ps1`, volitelný krok po zveřejnění v `release-publish.ps1`, i spuštěném z `release.ps1`, postup v [release.md](release.md#publikace-do-winget-t-059)) potřebuje na Windows **wingetcreate** (licence MIT) a **winget** (App Installer, součást Windows 10/11). Na macOS se nespouští, skript to odmítne se zprávou.

```powershell
winget install --id Microsoft.WingetCreate
winget list --id Microsoft.WingetCreate      # kontrola instalace; wingetcreate samotný zatím nespouštět
pwsh -NoProfile -Command "Import-Module ./scripts/ReleaseTools/ReleaseTools.psm1; Disable-WingetCreateTelemetry"
```

**Telemetrie `wingetcreate` se vypíná** (rozhodnutí uživatele 2026-11-11): `"Telemetry": { "disable": true }` v jeho `settings.json`. První spuštění bez `settings.json` soubor zapíše se zapnutou telemetrií, proto se `wingetcreate` (ani `wingetcreate --version`) nespouští před jejím vypnutím. Skript vydání ji vypne sám před každým spuštěním `wingetcreate`; řádek výše to udělá hned po instalaci. Umístění souboru a podrobnosti: [release.md](release.md#telemetrie-wingetcreate).

`wingetcreate` dostane token GitHubu od skriptu (`gh auth token`, jen po dobu běhu), vlastní přihlášení (`wingetcreate token --store`) netřeba a nemá se používat (token by zůstal uložený). Token `gh` musí mít scope `public_repo` (klasický token nebo přihlášení OAuth); fine-grained token (`github_pat_…`, např. `GH_TOKEN` podle T-180) `wingetcreate` nepodporuje. Pro odeslání se proto použije **dočasné `gh auth login --web`** a po něm `gh auth logout` (rozhodnutí uživatele 2026-11-11, postup v [release.md](release.md#token)); skript nevhodný token odmítne a postup vypíše. Pro ruční ověření manifestu před prvním odesláním: `winget settings --enable LocalManifestFiles` (jednou, jako správce).

## Pester (testy skriptů)

Testy skriptů pro vydání (`scripts/ReleaseTools/ReleaseTools.Tests.ps1`, `ReleaseOrchestrator.Tests.ps1`, `ReleaseWinget.Tests.ps1`) potřebují **PowerShell 7.2+** a **Pester 5 nebo novější** (licence Apache-2.0). Windows PowerShell má předinstalovaný Pester 3.4, ten nestačí. Instalace pro aktuálního uživatele (bez práv správce):

```powershell
pwsh -NoProfile -Command "Install-Module Pester -Scope CurrentUser -MinimumVersion 5.0.0 -Force -SkipPublisherCheck"
```

`-SkipPublisherCheck` je potřeba jen na Windows, kde starý Pester 3.4 má jiný podpis. Na macOS stačí `pwsh -NoProfile -Command "Install-Module Pester -Scope CurrentUser -MinimumVersion 5.0.0 -Force"` (modul se uloží do `~/.local/share/powershell/Modules`). Ověření: `pwsh -NoProfile -Command "Get-Module -ListAvailable Pester"` musí ukázat verzi 5 nebo vyšší. Bez `pwsh` nebo Pesteru 5+ se test `ScriptPesterTestsPass` v `dotnet test` přeskočí, neselže.

## AOT smoke test

`dotnet build` a `dotnet test` běží přes JIT, takže neodhalí chyby, které se projeví až v nativní AOT binárce. Po změnách v buildu, balíčcích nebo XAML proto spusť:

```powershell
pwsh scripts/aot-smoke.ps1                         # RID podle aktuálního OS/architektury
pwsh scripts/aot-smoke.ps1 -Seconds 15             # delší běh (Windows jen win-x64, jiné RID Windows odmítne)
```

Skript publikuje aplikaci (Release) do dočasné složky mimo repozitář, spustí ji a ověří, že po `-Seconds` sekundách (výchozí 10) stále běží. S parametrem `-PublishDir <složka>` místo publikování ověří už hotovou publish složku a nechá ji beze změny (používá `scripts/release-build.ps1`). Pak proces ukončí a dočasnou složku smaže (i při chybě). Při neúspěchu vrací nenulový exit code a vypíše výstup aplikace. Vyžaduje PowerShell 7.2+ a nativní toolchain popsaný výše.

Release exe nemá testovací režim. Skript proto těsně před jeho spuštěním odsune složky dat, logů a mezipaměti aplikace (macOS `~/Library/Application Support/LayerPeek`, `~/Library/Logs/LayerPeek` a `~/Library/Caches/LayerPeek`, Windows `%APPDATA%\LayerPeek`, `%LOCALAPPDATA%\LayerPeek\logs` a `%LOCALAPPDATA%\LayerPeek\cache`, T-196; mezipaměti buildu FFmpeg `LayerPeek.Build` se nedotkne) vedle sebe jako `<složka>.aot-smoke-backup` a zapíše žurnál `<data>.aot-smoke-journal.json`. Na macOS stejně zálohuje i místa podle bundle identifikátoru (T-019): `~/Library/Saved Application State/<id>.savedState`, `~/Library/Caches/<id>`, `~/Library/HTTPStorages/<id>` a domény `defaults` `<id>` a `LayerPeek` (přes `defaults export/delete/import`, nikdy jako soubor, protože plist drží `cfprefsd`). Aplikace tak startuje s prázdným profilem. Po jejím ukončení skript smaže, co vytvořila, a zálohy vrátí (T-095). Když skript spadne nebo se přeruší, obnovu dokončí další běh, nebo ručně `pwsh scripts/aot-smoke.ps1 -RestoreOnly`.

Na macOS skript navíc ověří, že aplikace běží bez ikony v Docku (`lsappinfo`, typ `UIElement`). S `-PublishDir <LayerPeek.app>/Contents/MacOS` spustí exe uvnitř bundlu (tak to dělá `release-build.ps1` po podpisu) a registraci bundlu v LaunchServices, kterou spuštění vytvoří, po běhu zase odebere (`lsregister -u`, jen když bundle předtím registrovaný nebyl).

### Proč `UseOptimizedPublish=false`

V `src/LayerPeek.App/LayerPeek.App.csproj` je nastaveno `<UseOptimizedPublish>false</UseOptimizedPublish>`. .NET 11 SDK při AOT publishi standardně používá „optimalizovaný“ publish, který místo celého `Build` spouští jen `PrepareResources; Compile` – a `PrepareResources` tak běží před `ResolveReferences`. Avalonia 12 ale přidává své `!AvaloniaResources` až před `ResolveReferences`, takže se resource nevloží do assembly, XAML se nezkompiluje a publikovaná aplikace hned po startu spadne s `XamlLoadException: No precompiled XAML found`. Publish přitom projde bez varování. Nastavení vrací běžný plný build; odstranit ho lze až po opravě v Avalonii nebo SDK (ověřit smoke testem).

## CI (GitHub Actions)

Workflow `.github/workflows/ci.yml` (T-218) běží při pushi do `develop`, `release/**` a `hotfix/**`, v každém pull requestu do `develop` a `main` a ručně (`workflow_dispatch`). Na každém OS spustí totéž, co se má před sloučením spustit lokálně:

1. `dotnet build LayerPeek.slnx -c Release` a `-c Debug` (varování jsou chyby, jako lokálně);
2. `dotnet test` (Debug) pro všechny testovací projekty kromě `tests/LayerPeek.E2E.Tests` (na runneru Windows je Edge, testy prohlížeče by se tedy spustily). Seznam projektů se bere z `LayerPeek.slnx` při běhu (filtr řešení `ci-tests.slnf` v dočasné složce runneru), nový testovací projekt tedy CI spustí sám. Alokační testy (kolekce `AllocationSensitive`, trait `Category=AllocationSensitive`, `tests/Shared/AllocationProbe.cs`) tento krok vynechá (`--filter-not-trait`) a spustí je **samostatný krok** `Test the allocation-sensitive tests` nad stejnými projekty (`--filter-trait`; projekt bez nich hlásí „Zero tests ran“ a běh projde, běh bez jediného vybraného testu selže kódem 8; krok běží i po selhání hlavního kroku testů, job pak selže; T-220, rozhodnutí uživatele 2026-11-15): počítají alokace všech vláken procesu a ve společném běhu platforma testů ještě posílá tisíce dřívějších výsledků do `dotnet test`. Lokální `dotnet test` je spouští spolu s ostatními. Nový alokační test patří do kolekce `AllocationSensitive` (třída `*AllocationTests` mimo ni shodí `AllocationSensitiveTraitTests`);
3. `dotnet publish src/LayerPeek.App -c Release -r <rid>` (Native AOT, bez spuštění aplikace; `aot-smoke.ps1` se nespouští, mění profil a potřebuje displej);
4. na Windows navíc (T-236) křížový `dotnet publish src/LayerPeek.App -c Release -r win-arm64` na runneru x64 a kontrola architektury PE (`Get-PublishArchitectureCheck`: `LayerPeek.exe` a všechny DLL ARM64 `0xAA64`, v publish složce DLL FFmpeg a `FFmpeg-LICENSE.txt`). ARM64 aplikace se nespouští (runner x64 to neumí; běh na zařízení je T-227).

| Job | Runner | Co |
|---|---|---|
| `Changes` | `ubuntu-24.04` | u PR zjistí změněné soubory proti cílové větvi; push a ruční spuštění vždy plný běh |
| `Build and test (Windows)` | `windows-2025` (= obraz `windows-2025-vs2026`; VS 2026 s workloadem *Desktop development with C++*, Edge, PowerShell 7, Pester 5) | kroky 1–3, `win-x64` |
| `Build and test (macOS)` | `xcode-27` (macOS 27, Apple Silicon, zatím *preview* obraz GitHubu; Xcode Command Line Tools, PowerShell 7, Pester 5) | kroky 1–3, `osx-arm64` |
| `Cross-publish (win-arm64)` | `windows-2025` (navíc komponenta `Microsoft.VisualStudio.Component.VC.Tools.ARM64`, na obrazu předinstalovaná) | krok 4, souběžně s buildy, limit 20 min (studený běh ~7 min, teplý ~3 min) |
| `CI result` | `ubuntu-24.04` | jediná povinná kontrola: projde, když oba buildy i křížový publish prošly, nebo když nebyly potřeba |

- **Pull request jen s dokumentací / úkoly:** buildy se přeskočí a `CI result` hned projde (žádné `paths-ignore`, povinná kontrola tak nikdy nečeká). Za takovou změnu se počítá, když je **každý** změněný soubor v `tasks/`, v `docs/` nebo je to `*.md` v kořeni repozitáře. **Výjimky:** `manuals/**`, `docs/troubleshooting.md` a `CHANGELOG.md` čtou testy (`UserManualTests`, `NotificationAndUpdateInfrastructureTests`, `ReleaseOrchestrator.Tests.ps1`), proto se vždy buildí a testuje. **Kdo přidá test, který čte soubor v `docs/` nebo `*.md` v kořeni, doplní ho do výjimek v kroku `Classify the changed files`.** **Push** (do `develop`, `release/**`, `hotfix/**`) a ruční spuštění **vždy** buildí a testují, i když push mění jen dokumentaci: běh pushe může zastupovat dřívější pushe, jejichž běh se nespustil (viz souběh), a vlastní rozdíl pushe by jejich kód neukázal (2026-11-15: push jen s dokumentací zrušil běh pushe s kódem a prošel bez testů, T-218).
- **Bezpečnost:** `permissions: contents: read`, žádné `secrets`, `pull_request` (nikdy `pull_request_target`), `actions/checkout` s `persist-credentials: false`, každá akce připnutá na SHA commitu s komentářem verze. Hlídá to test `CiWorkflowTests` (v `dotnet test`). Aktualizace připnutých akcí navrhuje Dependabot (`.github/dependabot.yml`, týdně, nová verze až po 7 dnech, PR do `develop`). Obrazy runnerů (`runs-on`) Dependabot neaktualizuje: připnuté jsou kvůli předvídatelnosti, nový obraz se nastaví ručně, až GitHub ohlásí konec starého.
- **Souběh a časové limity:** `concurrency` po větvi / PR. **PR:** nový push do PR zruší rozběhnutý běh (`cancel-in-progress` jen pro `pull_request`; každý běh pokrývá celý PR). **Push:** rozběhnutý běh se vždy dokončí; GitHub drží ve skupině nejvýš jeden čekající běh a novější push ho nahradí, takže čekající běh vždy testuje špičku větve včetně commitů nahrazených běhů. Hlídá to test `CiWorkflowTests`. Každý job má `timeout-minutes`.
- **Cache:**
  - NuGet (`~/.nuget/packages`), klíč = OS + hash `Directory.Packages.props`, všech `*.csproj` a `global.json`. Obnova a uložení jsou oddělené kroky (`actions/cache/restore` + `/save`, uloží se hned po prvním buildu), takže cache vznikne i při červených testech.
  - FFmpeg: `LayerPeekFFmpegCacheRoot` ukazuje do dočasné složky runneru (`$RUNNER_TEMP/ffmpeg`), uloží se hned po prvním buildu (i když pak selžou testy). Klíč = obraz runneru + RID + hash `build/FFmpeg.targets` a skriptu buildu FFmpeg dané platformy (`build/macos/build-ffmpeg.sh`, `build/windows/build-ffmpeg.sh`). Stejné vstupy jako složka cache v `build/FFmpeg.targets`; obraz je v klíči proto, že se knihovny překládají jeho toolchainem. Bez `restore-keys`: jiný recept vždy staví načisto.
  - **Toolchain FFmpeg na Windows (T-202):** jen když se cache FFmpeg neobnoví, nainstaluje krok `Install MSYS2 with MinGW-w64 (FFmpeg build)` akcí `msys2/setup-msys2` (připnutá na SHA, v2.33.0; `msystem: MINGW64`, `update: false`) balíčky `make diffutils mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm` a další krok nastaví `LayerPeekMsys2Root` na její složku. Build sám nic neinstaluje. Shodu balíčků s `build/FFmpeg.targets` hlídá `CiWorkflowTests`. Verze balíčků MSYS2 nejsou připnuté (berou se aktuální z repozitáře MSYS2), cache je proto klíčovaná obrazem a receptem, ne verzí překladače.
- **Křížový publish `win-arm64` (T-236):** samostatný job, ne kroky jobu Windows: běží souběžně (celková doba CI neroste), padající test neskryje rozbitý ARM64 build ani naopak a jeho kořen cache FFmpeg obsahuje jen soubory ARM64 (cache `win-x64` se nezvětší). Pořadí: kontrola komponenty MSVC ARM64 (`Get-NativeToolchainProblems`, selže za pár sekund, kdyby ji nový obraz neměl) → obnova cache → `dotnet build src/LayerPeek.App -c Release -r win-arm64` (při minutí cache stáhne llvm-mingw a přeloží FFmpeg) → uložení cache FFmpeg a llvm-mingw → `dotnet publish … -r win-arm64 -o $RUNNER_TEMP/publish-win-arm64` → uložení NuGet → kontrola architektury. Cache:
  - FFmpeg `win-arm64`: cesta jen `$RUNNER_TEMP/ffmpeg/win-arm64-*`, klíč `ffmpeg-v1-windows-2025-win-arm64-<hash build/FFmpeg.targets a build/windows/build-ffmpeg.sh>` (FFmpeg.targets obsahuje pin zdrojového tarballu i llvm-mingw), bez `restore-keys`.
  - llvm-mingw (rozbalený ~570 MB): obnoví se **jen při minutí cache FFmpeg** (jinak ho build nepotřebuje), cesta `$RUNNER_TEMP/ffmpeg/<LayerPeekLlvmMingwFolder>`, klíč `llvm-mingw-v1-Windows-<LayerPeekLlvmMingwSha256>`; obě hodnoty čte krok `Read the llvm-mingw pin` z `build/FFmpeg.targets`. Nová verze llvm-mingw = nový klíč i nová cache FFmpeg.
  - MSYS2 jen při minutí cache FFmpeg, `msystem: MSYS`, balíčky `make diffutils` (překladač je llvm-mingw).
  - NuGet: vlastní předpona `nuget-win-arm64-Windows-…` (`restore-keys` nejdřív vlastní, pak `nuget-Windows-` jobu Windows), aby job Windows při minutí nedostal menší sadu balíčků tohoto jobu.
  - Teplý běh (všechny cache trefené) nic nestahuje ani nepřekládá: v logu buildu není „Downloading the ARM64 toolchain llvm-mingw“ ani „Building FFmpeg from source“ a kroky MSYS2 / llvm-mingw jsou přeskočené.
- **Pester:** krok `Check PowerShell and Pester` selže, když na runneru chybí Pester 5+, aby se `ScriptPesterTestsPass` nepřeskočil potichu.
- **Při selhání** se nahrají artefakty `test-results-<rid>` (TRX všech testovacích projektů, `--report-xunit-trx`) a `snapshot-failures-<rid>` (`*.actual.png` a `*.diff.png` neshodných snímků UI ze složky `LayerPeek.App.UiTests.<id>` v dočasné složce systému), uchování 3 dny. Při úspěchu žádné artefakty. Krok `Describe the runner` vypíše OS, počet CPU, obraz a na macOS `csrutil status` a `sw_vers` (testy na nich závisí).
- **Tagy:** checkout stahuje i tagy (`fetch-tags: true`, bez historie). Test skutečného `CHANGELOG.md` se podle tagu `v<verze>` přeskočí u už vydané verze; bez tagů by selhal.

**E2E a interaktivní testy v CI neběží.** Zapínají je jen tyto proměnné, workflow žádnou nenastavuje:

| Proměnná | Co zapne |
|---|---|
| `LAYERPEEK_E2E=1` | E2E testy skutečné aplikace (`LayerPeek.App.E2E.Tests`, jen Windows) a E2E testy macOS / Windows v `LayerPeek.App.Tests` (`Mac*E2ETests`, `ForegroundRaiserE2ETests`) |
| `LAYERPEEK_E2E_SYSTEM_CHANGES=1`, `LAYERPEEK_E2E_SYSTEM_CHANGE_KINDS` | E2E zásahy do nastavení Windows (jen s `tools/run-e2e.ps1`) |
| `LAYERPEEK_VERIFY_UNATTENDED=<id kontrol>` | skutečné kontroly `tools/*-verify.ps1` (`VerifyScriptTests.UnattendedChecksPassOnDemand`) |
| `LAYERPEEK_DETECTION_SWEEP=1` | dlouhé sweepy detektorů (`*SeedSweep*`, `BedChangeDarkSweep`) |
| `LAYERPEEK_README_SCREENSHOTS=1`, `LAYERPEEK_UPDATE_SNAPSHOTS=1` | přegenerování snímků README / referenčních snímků UI testů (zapisují do repozitáře) |
| `LAYERPEEK_SAMPLES` | složka lokálních vzorků kamery (bez ní a bez `~/LayerPeek-samples` se testy vzorků přeskočí) |
| `LAYERPEEK_CI=1` | **opak přepínače:** nastavuje ho jen workflow CI. Na runneru přeskočí testy se známou příčinou selhání, každý s důvodem a úkolem (`tests/Shared/ContinuousIntegration.cs`); teď žádné: alokační testy (T-220) i `TheLumaPlaneSamplesMatchTheBgraSamplesOfTheFixture` (T-202 / T-221) běží i v CI na Windows. Na počítači vývojáře se nenastavuje. |

**Snímky UI na macOS** (rozhodnutí uživatele 2026-11-14): Skia kreslí glyfy přes Core Graphics systému, takže jiná hlavní verze macOS dá jiné okrajové pixely písmen (rozvržení stejné; ověřeno na obrázcích rozdílů z runneru macOS 26). Sada `Snapshots/macOS/` proto v `rendered-on.txt` zapisuje hlavní verzi macOS, na které vznikla (`LAYERPEEK_UPDATE_SNAPSHOTS=1` ho přepíše). Na jiné hlavní verzi se porovnání pixelů **přeskočí s důvodem** (test skončí v místě snímku). CI proto běží na macOS 27 (`xcode-27`). Sada Windows soubor nemá a porovnává se vždy; dokud ji uživatel nepřegeneruje na Windows (T-125), snímky Nastavení a O aplikaci v CI na Windows selhávají (bez přeskočení, rozhodnutí 2026-11-14).

Testy prohlížeče (`tests/LayerPeek.E2E.Tests`) se v CI vynechávají celé (viz výše). Headless UI testy (`LayerPeek.App.UiTests`) běží. Testy Klíčenky se přeskočí s důvodem, když je výchozí klíčenka runneru zamčená.

### Povinná kontrola v rulesetu (provádí uživatel)

Workflow kontrolu jen poskytuje; ruleset se mění ručně (nebo agentem s výslovným souhlasem uživatele). Až první běh na GitHubu projde:

1. GitHub → repozitář → **Settings → Rules → Rulesets** → ruleset 2 („pull request required“, `develop` + `main`, T-180) → **Edit**.
2. Zaškrtnout **Require status checks to pass**, pak **Add checks**, do hledání napsat `CI result` a vybrat kontrolu se zdrojem **GitHub Actions** (nabídne se až po prvním běhu workflow).
3. Volitelně **Require branches to be up to date before merging** (pak se PR před sloučením znovu otestuje proti aktuálnímu cíli; stojí další běh). Doporučení: zatím nezapínat.
4. Ostatní pravidla (CodeQL, obcházení adminem) beze změny → **Save changes**.

Jen `CI result`, ne joby `Build and test (…)` a `Cross-publish (win-arm64)`: ty se u změn jen dokumentace přeskočí a jejich názvy se mohou změnit, výsledná kontrola zůstává stejná. CodeQL běží dál jako *default setup*; workflow žádný vlastní CodeQL nepřidává.

## Časté chyby

| Chyba | Řešení |
|---|---|
| `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64` | chybí workload *Desktop development with C++* (viz Windows) |
| `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.ARM64` (publish `-r win-arm64`) | chybí komponenta MSVC ARM64 (viz „Windows ARM64“) |
| `MSYS2 is not installed in …` (publish `-r win-arm64`) | build ARM64 FFmpeg potřebuje z MSYS2 `bash`, `make`, `cmp`, `tar`, `xz` (`pacman -S --needed make diffutils`); nebo `-p:LayerPeekFFmpegEnabled=false` (bez kamery) |
| `Downloading the ARM64 toolchain llvm-mingw from … failed` / `llvm-mingw SHA256 mismatch` | offline nebo vydání zmizelo / jiný soubor: připojení, případně jiné vydání llvm-mingw (viz „Windows ARM64 navíc“, „Nová verze llvm-mingw“); nebo `-p:LayerPeekFFmpegEnabled=false` |
| `no FFmpeg camera libraries. On a Windows ARM64 machine …` (zpráva) | build bez RID na ARM64 stroji se nepodporuje; `-r win-arm64` |
| `… is x64 (0x8664), expected ARM64 (0xAA64) for win-arm64` | publish složka obsahuje nativní soubor jiné architektury (balíček NuGet nebo FFmpeg bez knihoven pro RID, případně starý soubor z dřívějšího publish do stejné složky – složku smazat a publikovat znovu) |
| macOS: `CS0246` u `ServiceProvider` / `IHttpClientFactory` v `LayerPeek.App` (hlavně `dotnet publish -r osx-arm64`) | repozitář je otevřený přes symbolický odkaz, `$PWD` se liší od `pwd -P`; skripty v `scripts/` to řeší samy, pro ruční příkazy viz macOS (`setopt CHASE_LINKS`) |
| `xcrun: error: invalid active developer path` | chybí Xcode Command Line Tools: `xcode-select --install` |
| Publikovaná aplikace spadne s `XamlLoadException: No precompiled XAML found` | v App csproj chybí `UseOptimizedPublish=false` (viz „AOT smoke test“) |
| `FFmpeg source SHA256 mismatch` nebo chyba stahování při buildu | bez internetu není cache FFmpeg naplněná (viz „Nativní knihovny FFmpeg“); při neshodě hashe zkontroluj URL a hash v `build/FFmpeg.targets` |
| Windows: `build-ffmpeg.sh: the work folder … contains a space and has no short 8.3 name` | `configure` FFmpeg neumí dočasnou složku s mezerou; pracovní složka je v cache FFmpeg (`%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\`, profil s mezerou ve jménu na svazku bez krátkých názvů 8.3). Nastavit vlastnost nebo proměnnou prostředí `LayerPeekFFmpegCacheRoot` na složku bez mezer (např. `C:\LayerPeek.Build\ffmpeg`) a build zopakovat |
| Windows: `MSYS2 with MinGW-w64 is not installed in …` | první `dotnet build` překládá FFmpeg: nainstalovat MSYS2 a balíčky (viz Windows), build zopakovat; MSYS2 jinde: `LayerPeekMsys2Root`. Nebo `-p:LayerPeekFFmpegEnabled=false` (bez kamery) |
| macOS: `The Xcode Command Line Tools are not installed (or not selected)` | první `dotnet build` překládá FFmpeg: `xcode-select --install`, po instalaci build zopakovat (`xcode-select -p` musí vypsat složku). Nebo `-p:LayerPeekFFmpegEnabled=false` (bez kamery) |
| `Building FFmpeg from source failed` (Windows i macOS) | viz `configure.log` / `make.log` ve složce `tmp-…` z chybové hlášky |
| Varování `IL2xxx` / `IL3xxx` | kód nebo knihovna není kompatibilní s trimmingem/AOT – viz „Native AOT rules“ v `CLAUDE.md` |
| `aot-smoke.ps1`: `runtime 'win-arm64' cannot run on this machine (windows-x64)` | očekávané: Windows na x64 ARM64 binárky nespustí; smoke test `win-arm64` na zařízení ARM64 (T-227) |
| `release-publish.ps1`: `has no releases.win-arm64.json` | vydání Windows bez souborů `win-arm64` (build `-Runtime win-x64`); od 2026-11-16 se nahrávají oba RID: výchozí build `pwsh scripts/release-build.ps1` (viz `docs/release.md`, „Konfigurace RID“) |
| `release-upload.ps1`: `GitHub CLI (gh) is not installed` / `is not logged in` | viz „Vydání (GitHub CLI)“ |
| `release-build.ps1`: `Velopack … and vpk … differ` | verze NuGet balíčku `Velopack` a nástroje `vpk` se liší (viz „Velopack `vpk`“) |
| `dotnet vpk`: `You must install or update .NET to run this application` | chybí runtime .NET 8–10 a manifest nemá `"rollForward": true` (viz „Velopack `vpk`“) |

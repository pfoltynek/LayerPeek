# Prerekvizity pro vývoj a Native AOT

Aplikace se publikuje jako **Native AOT** – výsledkem je jeden nativní spustitelný soubor bez nutnosti instalovat .NET runtime.

> **Důležité:**
> - Pro běžný vývoj (`dotnet build`, `dotnet run`, `dotnet test`, ladění F5 ve VS Code) stačí .NET SDK. Nativní toolchain je potřeba **jen pro `dotnet publish`** s AOT. **Výjimka na macOS (T-026): první `dotnet build` přeloží knihovny FFmpeg ze zdrojů, takže potřebuje Xcode Command Line Tools** (`xcode-select --install`). Bez nich build hned na začátku (nic nestahuje) skončí chybou `The Xcode Command Line Tools are not installed (or not selected). …` s návodem. Další buildy používají cache a toolchain už nepotřebují.
> - AOT nejde kompilovat napříč OS: Windows binárky se sestavují na Windows, macOS binárky na macOS. Vydávané RID: `win-x64` a `osx-arm64` (rozhodnuto 2026-10-21; Windows ARM64 `win-arm64` se od 2026-11-01 nepodporuje, T-130).
> - CI (GitHub Actions) se nepoužívá (T-016 zamítnut 2026-11-01), AOT binárky se proto sestavují lokálně skripty pro vydání (T-055, postup v [release.md](release.md)). Pro nahrání na GitHub je potřeba `gh` CLI (viz „Vydání“).

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

Projekt `LayerPeek.Camera.FFmpeg` potřebuje nativní knihovny FFmpeg 9.0.2 (LGPL verze 3 nebo novější, sdílené knihovny). Knihovny nejsou v repozitáři. Obstará je build (`build/FFmpeg.targets`), na každé platformě jinak:

| | Windows (`win-x64`) | macOS (`osx-arm64`, T-026) |
|---|---|---|
| Zdroj | hotový LGPL shared build BtbN (`n9.0.2-39-g7adfbe40dc`, měsíční build `autobuild-2026-10-31-12-59`), ~77 MB z GitHubu | oficiální zdrojový tarball `ffmpeg-9.0.2.tar.xz` z ffmpeg.org (~12 MB), sestavený skriptem `build/macos/build-ffmpeg.sh` |
| Knihovny | `avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll`, `swresample-7.dll` | `libavformat.63.dylib`, `libavcodec.63.dylib`, `libavutil.61.dylib`, `libswscale.10.dylib` (swresample aplikace nenačítá) |
| Cache | `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\<sha256 archivu>\` | `~/Library/Caches/LayerPeek.Build/ffmpeg/osx-arm64-<klíč>/` (klíč = SHA256 ze SHA256 tarballu a obsahu skriptu buildu, takže změna příznaků nebo verze sestaví novou cache) |
| První build | stáhne archiv | stáhne tarball a přeloží ho (na Apple Silicon asi 30–60 s) |

Společné:

- **První build potřebuje internet.** Archiv / tarball se ověří podle připnutého SHA256. Při neshodě build selže a stažený soubor se zahodí. Další buildy jen zkontrolují cache a fungují offline. Jinou složku cache nastavíš vlastností nebo proměnnou prostředí `LayerPeekFFmpegCacheRoot`. `LayerPeekFFmpegEnabled=false` krok vypne úplně (testy FFmpeg se pak přeskočí).
- **Vydání (T-199)** přikládá zdroj FFmpeg (`ffmpeg-source-<verze>-<os>.tar.xz`, `docs/release.md`, „Zdrojový kód FFmpeg“). `scripts/release-build.ps1` ho bere z vlastní cache vedle této: `~/Library/Caches/LayerPeek.Build/ffmpeg-source/` (tarball z ffmpeg.org) a `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg-source\` (tary `git archive` commitu FFmpeg a skriptů BtbN; potřebuje `git` a `tar` se zápisem xz). Stahuje jen při prvním vydání dané verze FFmpeg a nic v cache nemaže.
- Knihovny a `FFmpeg-LICENSE.txt` se kopírují do výstupu i do publish složky projektů, které na FFmpeg projekt odkazují. V `LayerPeek.app` jsou knihovny v `Contents/MacOS` (vedle exe, jako ostatní nativní knihovny) a licence v `Contents/Resources`.
- IDE (design-time build) nic nestahuje ani nepřekládá. Jiné RID (např. `osx-x64`) a Intel Mac krok přeskočí.

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

  `configure` přidá automaticky závislosti vybraných komponent (demuxery `asf`, `mov`, `mpegts`, `rm` a protokoly `rtp`, `udp`, `http` kvůli RTSP; muxer `mov`, parser `ac3` a bitstream filtry `aac_adtstoasc`, `vp9_superframe` kvůli muxeru `mp4`). Aplikace je stejně nepovolí (`protocol_whitelist`, `format_whitelist`, `codec_whitelist`, T-014). Muxer `mp4` (T-147) slouží nahrávání kamery (T-144): pakety H.264 z RTSP se jen přebalí do fragmentovaného MP4, žádný enkodér se nepřidává. Převod Annex B na délkově prefixované NAL jednotky a SPS/PPS na `avcC` dělá muxer sám, bitstream filtry `h264_mp4toannexb` ani `extract_extradata` potřeba nejsou. Knihovny jsou díky tomu větší asi o 262 KiB (`libavformat` o 245 KiB, `libavcodec` o 16 KiB). Bez `--enable-gpl` a `--enable-nonfree`, licence podle `configure`: „LGPL version 3 or later“.
- **VideoToolbox (T-203):** `--enable-videotoolbox --enable-hwaccel=h264_videotoolbox` zapíná hardwarové dekódování H.264 okna kamery (rozhodnutí uživatele 2026-11-12, T-192; do 2026-11-12 jen software). Kvůli `--disable-autodetect` a `--disable-everything` musí být oba přepínače výslovně. Frameworky VideoToolbox, CoreMedia, CoreVideo, CoreFoundation a CoreServices jsou součástí macOS (`/System/Library/Frameworks`, kontrola závislostí je povoluje), nic dalšího se neinstaluje: Xcode Command Line Tools stačí. VideoToolbox není v seznamu GPL ani nonfree, licence zůstává LGPL 3. Knihovny jsou větší o 27 KB (`libavcodec` 1 624 952 B, `libavutil` 742 648 B). Klíč cache obsahuje obsah skriptu, takže se po změně přestaví sama (ověřeno 2026-11-12: nová složka `osx-arm64-c88d70…`, 39 s). Test `HardwareDecodingFallbackTests.TheMacOSBuildHasVideoToolboxForH264AndStaysLgpl` hlídá, že knihovny `h264_videotoolbox` obsahují (`av_hwdevice_iterate_types` + `avcodec_get_hw_config`).
- **Když build selže:** logy `configure.log` a `make.log` zůstanou ve složce `tmp-…` v cache (smaže je build po dvou hodinách). Když tarball z ffmpeg.org zmizí, změň v `build/FFmpeg.targets` `LayerPeekFFmpegMacSourceUrl`, `LayerPeekFFmpegMacSourceSha256` a `LayerPeekFFmpegMacSourceFolder` na jiný oficiální tarball 9.0.x (stejné verze knihoven). Postup je v „Nová verze FFmpeg“ níže.
- **Ověření tarballu je automatické:** každý první build ověří připnutý SHA256 (`LayerPeekFFmpegMacSourceSha256`) a při neshodě skončí chybou. Uživatel ani vývojář nic ručně neověřuje. FFmpeg nezveřejňuje SHA256, jen podpisy GPG (`.asc`). Proto se připnutý hash při změně verze ověřuje skriptem `build/update-ffmpeg.ps1` proti nezávislému zdroji (vzorec Homebrew) a podpisem GPG, když je `gpg` k dispozici. Hash 9.0.2 (`8c38502…e002e`) souhlasí se vzorcem Homebrew (ověřeno 2026-10-29), podpis GPG ověřený nebyl (`gpg` na stroji není).

### Nová verze FFmpeg

Obě platformy mají mít stejné vydání FFmpeg (stejné verze knihoven `avformat-63`, `avcodec-63`, `avutil-61`, `swscale-10`; jiná hlavní verze znamená změnu názvů knihoven v `build/FFmpeg.targets`, `build/macos/build-ffmpeg.sh` a `FFmpegRuntime`, i kontroly `^9\.0\.` ve skriptu buildu).

1. **Ověřit a zapsat připnuté hodnoty** skriptem pro správce `build/update-ffmpeg.ps1` (PowerShell 7, běží na macOS i Windows, i s `pwsh` nainstalovaným jako dotnet global tool; nic neinstaluje, jen čte přes HTTPS). Výchozí `-Platform all` ověří obě platformy najednou, `-Platform macos` / `-Platform windows` jen jednu:

   ```bash
   # jen ověří a vypíše hodnoty
   pwsh build/update-ffmpeg.ps1 -Version 9.0.3 -BtbNTag autobuild-2026-11-30-12-00 -BtbNAsset ffmpeg-n9.0.3-…-win64-lgpl-shared-9.0.zip
   # ověří a zapíše je
   pwsh build/update-ffmpeg.ps1 -Version 9.0.3 -BtbNTag autobuild-2026-11-30-12-00 -BtbNAsset ffmpeg-n9.0.3-…-win64-lgpl-shared-9.0.zip -Update
   # jen jedna platforma
   pwsh build/update-ffmpeg.ps1 -Platform macos -Version 9.0.3
   ```

   Tag a přesný název assetu jsou na stránce vydání https://github.com/BtbN/FFmpeg-Builds/releases (název obsahuje i počet commitů a hash větve, např. `n9.0.2-28-g669f5ab083`, proto se z verze odvodit nedá). **Připínat jen poslední build měsíce** (tag `autobuild-RRRR-MM-<poslední den>-…`): BtbN denní buildy asi po dvou týdnech maže a trvale drží jen poslední build každého měsíce, jinak čistý build na Windows později skončí chybou 404 (T-162). Skript jiný tag odmítne. Před stahováním skript zkontroluje vstupy (jen LGPL shared win64 build vydané větve, `ffmpeg-n<verze>-…-win64-lgpl-shared-<x.y>.zip`), to, že obě platformy zůstanou na stejné větvi FFmpeg (`x.y`; nová hodnota jedné platformy se porovná s novou nebo připnutou hodnotou druhé), a to, že knihovny obou platforem v `build/FFmpeg.targets` mají stejné hlavní verze (`avformat-63.dll` ↔ `libavformat.63.dylib` …).

   - **macOS** (`-Version`): stáhne `https://ffmpeg.org/releases/ffmpeg-<verze>.tar.xz` do dočasné složky, spočítá SHA256 a porovná ho se vzorcem Homebrew (`https://formulae.brew.sh/api/formula/ffmpeg.json`, `urls.stable.checksum`, Homebrew potřeba není). Vzorec musí mít přesně stejnou URL tarballu: když je Homebrew na jiné verzi, skript skončí chybou a přepínač, který by to obešel, není (rozhodnuto 2026-10-29; zkusit znovu, až Homebrew verzi převezme). Když je na `PATH` `gpg`, ověří i podpis `<tarball>.asc` klíčem FFmpeg (`https://ffmpeg.org/ffmpeg-devel.asc`, otisk `FCF986EA15E6E293A5644F10B4322F04D67658D8` je ve skriptu) v dočasné domovské složce GnuPG, takže se osobní klíčenka nemění. Bez `gpg` vypíše `GPG signature: SKIPPED`. Hodnoty: `LayerPeekFFmpegMacSourceUrl`, `LayerPeekFFmpegMacSourceSha256`, `LayerPeekFFmpegMacSourceFolder` a `MacOSBuildUri`.
   - **Windows** (`-BtbNTag`, `-BtbNAsset`): stáhne `checksums.sha256` vydání BtbN (`https://github.com/BtbN/FFmpeg-Builds/releases/download/<tag>/checksums.sha256`, formát `sha256sum`; BtbN ho přikládá k vydáním autobuild, ověřeno 2026-10-29 a 2026-11-04 na `autobuild-2026-10-31-12-59`). Když soubor chybí nebo asset neobsahuje, skript skončí chybou (nic nepřeskakuje). Pak stáhne zip (~80 MB), porovná jeho SHA256 s řádkem assetu a zkontroluje obsah bez rozbalení: jediná složka nejvyšší úrovně s názvem assetu bez `.zip` (`LayerPeekFFmpegArchiveFolder`), `LICENSE.txt` s textem LGPL 3 a `bin/<dll>` pro všechny knihovny Windows z `build/FFmpeg.targets` (`avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll`, `swresample-7.dll`). Když mají knihovny jinou hlavní verzi, chyba vypíše, které DLL v zipu jsou. Hodnoty: `LayerPeekFFmpegUrl`, `LayerPeekFFmpegSha256`, `LayerPeekFFmpegArchiveFolder` a `WindowsBuildUri`.

   Při jakékoli neshodě skončí kódem 1 a nezapíše nic (ani hodnoty platformy, která prošla). `-Update` zapíše hodnoty do `build/FFmpeg.targets` a `src/LayerPeek.App/Licenses/ThirdPartyLicenses.cs`. Bez `-Update` je jen vypíše; když se shodují s připnutými, řekne to. Dočasná složka se vždy smaže.
2. U nové větve FFmpeg upravit kontrolu verze `^9\.0\.` v `build/macos/build-ffmpeg.sh`; u každé nové verze verze, které hlídá `ThirdPartyLicensesTests` (`n9.0.2-`, `ffmpeg-9.0.2.`).
   **Zdroj pro vydání (T-199, `docs/release.md`, „Zdrojový kód FFmpeg“):** `update-ffmpeg.ps1` je zatím nezapisuje, ručně se v `build/FFmpeg.targets` doplní `LayerPeekFFmpegWindowsSourceCommit` (celý hash commitu z názvu assetu, `-g<zkrácený hash>`: `gh api repos/FFmpeg/FFmpeg/commits/<zkrácený hash> --jq .sha`), `LayerPeekFFmpegWindowsScriptsTag` (nový tag BtbN) a `LayerPeekFFmpegWindowsScriptsCommit` (`git ls-remote https://github.com/BtbN/FFmpeg-Builds.git refs/tags/<tag>`), stromy (`gh api repos/<repo>/commits/<commit> --jq .commit.tree.sha`) a SHA-256 tarů `git archive` (oba vypíše chyba prvního běhu `Get-FFmpegSourceInputs`, ten commit i strom ověří gitem sám; zkontrolovat a teprve pak připnout). macOS nic nového nepotřebuje (bere `LayerPeekFFmpegMac*`). Než se hodnoty doplní, selže test Pester `FFmpeg source of a release (T-199)` a `release-build.ps1` skončí chybou před buildem.
3. Na Macu `dotnet build` (nová cache, klíč obsahuje SHA256) a `dotnet test`: testy FFmpeg ověří verze knihoven a licenci LGPL 3, testy okna licencí shodu odkazů s `build/FFmpeg.targets`. Na Windows totéž.
4. Aktualizovat verze v dokumentaci (tento soubor, `docs/architecture.md`) a `CHANGELOG.md`.

Testy skriptu (`build/update-ffmpeg.Tests.ps1`, Pester, běží v `dotnet test`) používají náhradní stahování, vzorec Homebrew, soubor `checksums.sha256`, malý zip a `gpg`, síť nepotřebují.

### Když stažení selže (Windows)

Windows, když stažení selže (offline, nebo připnuté vydání z GitHubu zmizelo), build skončí chybou s návodem. Zrcadlo není. Možnosti:

1. V `build/FFmpeg.targets` změnit `LayerPeekFFmpegUrl`, `LayerPeekFFmpegSha256` a `LayerPeekFFmpegArchiveFolder` na jiný LGPL shared win64 build BtbN se stejnými verzemi knihoven (`avformat-63` …): ověří a zapíše je `pwsh build/update-ffmpeg.ps1 -Platform windows -BtbNTag <tag> -BtbNAsset <asset> -Update` (viz „Nová verze FFmpeg“).
2. Cache naplnit ručně: do `<cache>\<sha256>\bin\` dát `avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll` a `swresample-7.dll`, do `<cache>\<sha256>\` dát `LICENSE.txt` a soubor `verified.sha256`. Ten má obsahovat jeden řádek s hodnotou `LayerPeekFFmpegSha256` z `build/FFmpeg.targets` (SHA256 archivu, ze kterého DLL pocházejí), stejně jako cache vytvořená buildem. Build zatím kontroluje jen to, že soubor existuje. Za shodu DLL s připnutým buildem odpovídá ten, kdo cache naplnil. Případně nastavit `LayerPeekFFmpegCacheRoot` na složku s takovou cache.

## Windows

| Co | Poznámka |
|---|---|
| **Build Tools for Visual Studio 2026** (nebo Visual Studio 2022/2026) | bezplatné, plné IDE není nutné |
| Workload **Desktop development with C++** | obsahuje MSVC překladač/linker (komponenta `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`) a Windows SDK |
| Velopack **`vpk`** | jen pro `scripts/release-build.ps1` (instalátor Windows, od T-058 i `.app` a aktualizace pro macOS) a pro E2E test aktualizace `UpdateTests` (T-128, balí Debug sestavení; bez obnoveného `vpk` se přeskočí); lokální dotnet tool, `dotnet tool restore` ho stáhne sám (viz „Velopack `vpk`“ níže) |

Komponenta `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` se samostatně nestahuje, je součástí workloadu *Desktop development with C++* v instalátoru Visual Studia.

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

### Velopack `vpk` (instalátor T-017, macOS T-058)

Instalátor pro Windows balí nástroj **`vpk`** (Velopack, licence MIT) jako balicí krok `scripts/release-build.ps1`. Je to lokální dotnet tool s připnutou verzí v `.config/dotnet-tools.json`. Globálně se nic neinstaluje:

```powershell
dotnet tool restore        # stáhne vpk podle manifestu (release-build.ps1 to dělá sám)
dotnet vpk -h              # ověření; spouští se z kořene repozitáře
```

- Verze `vpk` musí být stejná jako NuGet balíček `Velopack` v `Directory.Packages.props` (teď `1.2.161`). `release-build.ps1` to ověří před sestavením a Pester test hlídá oba soubory. Při povýšení změň obě verze najednou.
- `vpk` 1.2.161 je framework-dependent pro .NET 8, 9 a 10 (verze pro .NET 11 neexistuje). Na stroji jen s .NET 11 ho spustí `"rollForward": true` v manifestu. Když je nainstalovaný runtime .NET 8–10, použije se ten.
- Při prvním `dotnet tool restore` je potřeba internet (NuGet). `vpk` se spouští s `--skip-updates`, takže sám nic dalšího nestahuje.
- `vpk` na Windows balí RID `win-x64` (jediný vydávaný RID pro Windows, Windows ARM64 se od T-130 nepodporuje). Balíčky pro macOS umí vytvořit jen na Macu: od T-058 je `vpk` součástí kroku `app` na macOS (`release-build.ps1` spustí `dotnet tool restore` i tam; podpis `codesign` z Xcode Command Line Tools). Pester test na macOS spustí skutečné `vpk`, když je obnovené (jinak test přeskočí).

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

Pro nahrání vydání na GitHub (`scripts/release-upload.ps1`, `scripts/release-publish.ps1`, postup v [release.md](release.md)) je potřeba **GitHub CLI** `gh`, přihlášené k účtu s právem zápisu do repozitáře. Samotné sestavení (`scripts/release-build.ps1`) ho nepotřebuje.

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

Odeslání vydání do winget (`scripts/release-winget.ps1`, volitelný krok `release-publish.ps1` a `release-upload.ps1 -AddToPublished`, postup v [release.md](release.md#publikace-do-winget-t-059)) potřebuje na Windows **wingetcreate** (licence MIT) a **winget** (App Installer, součást Windows 10/11). Na macOS se nespouští, skript to odmítne se zprávou.

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

## Časté chyby

| Chyba | Řešení |
|---|---|
| `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64` | chybí workload *Desktop development with C++* (viz Windows) |
| macOS: `CS0246` u `ServiceProvider` / `IHttpClientFactory` v `LayerPeek.App` (hlavně `dotnet publish -r osx-arm64`) | repozitář je otevřený přes symbolický odkaz, `$PWD` se liší od `pwd -P`; skripty v `scripts/` to řeší samy, pro ruční příkazy viz macOS (`setopt CHASE_LINKS`) |
| `xcrun: error: invalid active developer path` | chybí Xcode Command Line Tools: `xcode-select --install` |
| Publikovaná aplikace spadne s `XamlLoadException: No precompiled XAML found` | v App csproj chybí `UseOptimizedPublish=false` (viz „AOT smoke test“) |
| `FFmpeg archive SHA256 mismatch` / `FFmpeg source SHA256 mismatch` nebo chyba stahování při buildu | bez internetu není cache FFmpeg naplněná (viz „Nativní knihovny FFmpeg“); při neshodě hashe zkontroluj URL a hash v `build/FFmpeg.targets` |
| macOS: `The Xcode Command Line Tools are not installed (or not selected)` | první `dotnet build` překládá FFmpeg: `xcode-select --install`, po instalaci build zopakovat (`xcode-select -p` musí vypsat složku). Nebo `-p:LayerPeekFFmpegEnabled=false` (bez kamery) |
| macOS: `Building FFmpeg from source failed` | viz `configure.log` / `make.log` ve složce `tmp-…` z chybové hlášky |
| Varování `IL2xxx` / `IL3xxx` | kód nebo knihovna není kompatibilní s trimmingem/AOT – viz „Native AOT rules“ v `CLAUDE.md` |
| `release-build.ps1`: `Unknown runtime 'win-arm64'` / `aot-smoke.ps1`: `runtime 'win-arm64' is not supported` | Windows ARM64 se nepodporuje (T-130), Windows se sestavuje jen jako `win-x64` |
| `release-upload.ps1`: `GitHub CLI (gh) is not installed` / `is not logged in` | viz „Vydání (GitHub CLI)“ |
| `release-build.ps1`: `Velopack … and vpk … differ` | verze NuGet balíčku `Velopack` a nástroje `vpk` se liší (viz „Velopack `vpk`“) |
| `dotnet vpk`: `You must install or update .NET to run this application` | chybí runtime .NET 8–10 a manifest nemá `"rollForward": true` (viz „Velopack `vpk`“) |

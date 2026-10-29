# Prerekvizity pro vývoj a Native AOT

Aplikace se publikuje jako **Native AOT** – výsledkem je jeden nativní spustitelný soubor bez nutnosti instalovat .NET runtime.

> **Důležité:**
> - Pro běžný vývoj (`dotnet build`, `dotnet run`, `dotnet test`, ladění F5 ve VS Code) stačí .NET SDK. Nativní toolchain je potřeba **jen pro `dotnet publish`** s AOT. Výjimka na macOS (T-026): první build přeloží knihovny FFmpeg, takže potřebuje Xcode Command Line Tools.
> - AOT nejde kompilovat napříč OS: Windows binárky se sestavují na Windows, macOS binárky na macOS. Jinou architekturu v rámci stejného OS sestavit lze (např. `win-arm64` na x64 Windows, potřebuje ARM64 build tools). Vydávané RID: `win-x64`, `win-arm64` (až po T-057), `osx-arm64` (rozhodnuto 2026-10-21).
> - CI (GitHub Actions) je zatím odložené (T-016), AOT binárky se proto sestavují lokálně skripty pro vydání (T-055, postup v [release.md](release.md)). Pro nahrání na GitHub je potřeba `gh` CLI (viz „Vydání“).

## Společné (obě platformy)

| Co | Poznámka |
|---|---|
| .NET SDK 11 | verze podle `global.json` (aktuálně `11.0.100-rc.2`), stáhnout z https://dotnet.microsoft.com/download/dotnet/11.0 |
| ASP.NET Core runtime 11 | instaluje se automaticky se SDK, nic navíc. Projekt PrusaLink používá sdílený framework `Microsoft.AspNetCore.App` (Kestrel pro lokální proxy webu, T-028), takže ho `dotnet run`/`dotnet test` potřebují. AOT binárka ho obsahuje a na cílovém počítači nic nevyžaduje. |
| Git | 2.38 nebo novější pro `scripts/release.ps1` (sloučení přes `git merge-tree --write-tree`) |
| *(volitelně)* **Microsoft Edge** | jen pro E2E testy webu přes proxy (`tests/LayerPeek.E2E.Tests`, T-030). Testy používají nainstalovaný Edge přes Playwright (`Channel = "msedge"`, headless) a nic nestahují. Bez Edge se testy prohlížeče přeskočí (`dotnet test` neselže). Ovladač Playwright (Node.js, asi 100 MB) přidá NuGet balíček `Microsoft.Playwright` do výstupu tohoto testovacího projektu, nic dalšího se instalovat nemusí. |
| *(volitelně, Windows)* **PowerShell 7** (`pwsh` na `PATH`) | pro E2E testy skutečné aplikace (`tools/run-e2e.ps1`, T-044). Testy, které mění nastavení Windows (T-065), si před prvním zásahem spustí hlídací proces `pwsh … run-e2e.ps1 -RestoreOnly -AfterProcess <PID>`. Bez `pwsh` na `PATH` se žádný zásah neudělá a test selže s popisem. Jednotkové testy výběru varování (`RunE2EScriptTests`, T-066) běží v každém `dotnet test` a bez `pwsh` se přeskočí. |
| *(volitelně, Windows)* **druhý monitor** | pro E2E zásahy T-066: vypnutí druhého monitoru a měřítko druhého monitoru. Bez druhého (neprimárního) monitoru se tyto testy přeskočí s důvodem. |
| *(volitelně, Windows)* **primární monitor se 150 %** | pro E2E testy ikon T-070 (hlavní panel, titulky, tray, tray menu při 100 a 150 %). Monitor musí měřítko 150 % nabízet (malé monitory ne), jinak se testy se 150 % přeskočí. Hlavní panel nesmí být automaticky skrytý. Ostrost ilustrace v tray menu při 150 % ověří test jen v relaci přihlášené při 150 % (systémové DPI se mění jen odhlášením). |
| VS Code + rozšíření | C# Dev Kit, Avalonia for VS Code, EditorConfig (nabídnou se automaticky) |

## Nativní knihovny FFmpeg (kamera)

Projekt `LayerPeek.Camera.FFmpeg` potřebuje nativní knihovny FFmpeg 9.0.2 (LGPL verze 3 nebo novější, sdílené knihovny). Knihovny nejsou v repozitáři. Obstará je build (`build/FFmpeg.targets`), na každé platformě jinak:

| | Windows (`win-x64`) | macOS (`osx-arm64`, T-026) |
|---|---|---|
| Zdroj | hotový LGPL shared build BtbN (`n9.0.2-28-g669f5ab083`), ~77 MB z GitHubu | oficiální zdrojový tarball `ffmpeg-9.0.2.tar.xz` z ffmpeg.org (~12 MB), sestavený skriptem `build/macos/build-ffmpeg.sh` |
| Knihovny | `avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll`, `swresample-7.dll` | `libavformat.63.dylib`, `libavcodec.63.dylib`, `libavutil.61.dylib`, `libswscale.10.dylib` (swresample aplikace nenačítá) |
| Cache | `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\<sha256 archivu>\` | `~/Library/Caches/LayerPeek.Build/ffmpeg/osx-arm64-<klíč>/` (klíč = SHA256 ze SHA256 tarballu a obsahu skriptu buildu, takže změna příznaků nebo verze sestaví novou cache) |
| První build | stáhne archiv | stáhne tarball a přeloží ho (na Apple Silicon asi 30–60 s) |

Společné:

- **První build potřebuje internet.** Archiv / tarball se ověří podle připnutého SHA256. Při neshodě build selže a stažený soubor se zahodí. Další buildy jen zkontrolují cache a fungují offline. Jinou složku cache nastavíš vlastností nebo proměnnou prostředí `LayerPeekFFmpegCacheRoot`. `LayerPeekFFmpegEnabled=false` krok vypne úplně (testy FFmpeg se pak přeskočí).
- Knihovny a `FFmpeg-LICENSE.txt` se kopírují do výstupu i do publish složky projektů, které na FFmpeg projekt odkazují. V `LayerPeek.app` jsou knihovny v `Contents/MacOS` (vedle exe, jako ostatní nativní knihovny) a licence v `Contents/Resources`.
- IDE (design-time build) nic nestahuje ani nepřekládá. Jiné RID (`win-arm64`, `osx-x64`) a Intel Mac krok přeskočí.

macOS navíc:

- **Stačí Xcode Command Line Tools** (`clang`, `make`, `otool`, `lipo`) a nástroje systému (`/usr/bin/tar` rozbalí `.tar.xz`). `pkg-config`, `nasm` ani Homebrew potřeba nejsou. Skript spouští `configure` a `make` s čistým prostředím (`PATH=/usr/bin:/bin:/usr/sbin:/sbin`, bez `CFLAGS`/`LDFLAGS`/`PKG_CONFIG_PATH`), takže se do buildu nedostane nic z Homebrew. Varování `pkg-config not found` v logu `configure` je v pořádku (žádné externí knihovny se nehledají).
- Výsledek se kontroluje: architektura `arm64` (`lipo`) a závislosti jen na sousedních knihovnách (`@loader_path/…`) a na systému (`/usr/lib`, `/System/Library`), nikdy `/opt/homebrew` ani `/usr/local` (`otool -L`). Knihovny mají install name `@loader_path/<název>`, takže se načtou ze složky, ve které jsou (výstup buildu, publish i bundle). Minimální macOS je `14.0` jako `LSMinimumSystemVersion` v `build/macos/Info.plist` (test hlídá shodu).
- Přesný řádek `configure` (je i v `BUILD.txt` v cache):

  ```
  configure --enable-shared --disable-static --enable-version3 --disable-programs --disable-doc --disable-debug
    --disable-autodetect --disable-everything --disable-avdevice --disable-avfilter --disable-swresample
    --enable-network --enable-protocol=tcp,file --enable-demuxer=rtsp,h264 --enable-parser=h264 --enable-decoder=h264
    --arch=aarch64 --install-name-dir=@loader_path
    --extra-cflags="-arch arm64 -mmacosx-version-min=14.0" --extra-ldflags="-arch arm64 -mmacosx-version-min=14.0"
  ```

  `configure` přidá automaticky závislosti vybraných komponent (demuxery `asf`, `mov`, `mpegts`, `rm` a protokoly `rtp`, `udp`, `http` kvůli RTSP). Aplikace je stejně nepovolí (`protocol_whitelist`, `format_whitelist`, `codec_whitelist`, T-014). Bez `--enable-gpl` a `--enable-nonfree`, bez VideoToolbox (dekódování jen softwarově, rozhodnuto 2026-10-29), licence podle `configure`: „LGPL version 3 or later“.
- **Když build selže:** logy `configure.log` a `make.log` zůstanou ve složce `tmp-…` v cache (smaže je build po dvou hodinách). Když tarball z ffmpeg.org zmizí, změň v `build/FFmpeg.targets` `LayerPeekFFmpegMacSourceUrl`, `LayerPeekFFmpegMacSourceSha256` a `LayerPeekFFmpegMacSourceFolder` na jiný oficiální tarball 9.0.x (stejné verze knihoven). FFmpeg nezveřejňuje SHA256, jen podpisy GPG (`.asc`). Připnutý hash je spočítaný ze staženého tarballu 2026-10-29, podpis GPG ověřený nebyl (`gpg` na stroji není).

Windows, když stažení selže (offline, nebo připnuté vydání z GitHubu zmizelo), build skončí chybou s návodem. Zrcadlo není. Možnosti:

1. V `build/FFmpeg.targets` změnit `LayerPeekFFmpegUrl`, `LayerPeekFFmpegSha256` a `LayerPeekFFmpegArchiveFolder` na jiný LGPL shared win64 build BtbN se stejnými verzemi knihoven (`avformat-63` …).
2. Cache naplnit ručně: do `<cache>\<sha256>\bin\` dát `avformat-63.dll`, `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll` a `swresample-7.dll`, do `<cache>\<sha256>\` dát `LICENSE.txt` a soubor `verified.sha256`. Ten má obsahovat jeden řádek s hodnotou `LayerPeekFFmpegSha256` z `build/FFmpeg.targets` (SHA256 archivu, ze kterého DLL pocházejí), stejně jako cache vytvořená buildem. Build zatím kontroluje jen to, že soubor existuje. Za shodu DLL s připnutým buildem odpovídá ten, kdo cache naplnil. Případně nastavit `LayerPeekFFmpegCacheRoot` na složku s takovou cache.

## Windows

| Co | Poznámka |
|---|---|
| **Build Tools for Visual Studio 2026** (nebo Visual Studio 2022/2026) | bezplatné, plné IDE není nutné |
| Workload **Desktop development with C++** | obsahuje MSVC překladač/linker (komponenta `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`) a Windows SDK |
| *(volitelně)* komponenta **MSVC ARM64 build tools** (`Microsoft.VisualStudio.Component.VC.Tools.ARM64`) | jen pro publish na `win-arm64` (viz „ARM64 build tools“ níže) |
| Velopack **`vpk`** | jen pro instalátor v `scripts/release-build.ps1`; lokální dotnet tool, `dotnet tool restore` ho stáhne sám (viz „Velopack `vpk`“ níže) |

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

### Velopack `vpk` (instalátor, T-017)

Instalátor pro Windows balí nástroj **`vpk`** (Velopack, licence MIT) jako balicí krok `scripts/release-build.ps1`. Je to lokální dotnet tool s připnutou verzí v `.config/dotnet-tools.json`. Globálně se nic neinstaluje:

```powershell
dotnet tool restore        # stáhne vpk podle manifestu (release-build.ps1 to dělá sám)
dotnet vpk -h              # ověření; spouští se z kořene repozitáře
```

- Verze `vpk` musí být stejná jako NuGet balíček `Velopack` v `Directory.Packages.props` (teď `1.2.161`). `release-build.ps1` to ověří před sestavením a Pester test hlídá oba soubory. Při povýšení změň obě verze najednou.
- `vpk` 1.2.161 je framework-dependent pro .NET 8, 9 a 10 (verze pro .NET 11 neexistuje). Na stroji jen s .NET 11 ho spustí `"rollForward": true` v manifestu. Když je nainstalovaný runtime .NET 8–10, použije se ten.
- Při prvním `dotnet tool restore` je potřeba internet (NuGet). `vpk` se spouští s `--skip-updates`, takže sám nic dalšího nestahuje.
- `vpk` stačí na Windows pro RID `win-x64` i `win-arm64`. Balíčky pro macOS umí vytvořit jen na Macu (T-019).

### ARM64 build tools (jen pro `win-arm64`)

Publish `win-arm64` (i na x64 Windows) potřebuje navíc komponentu **MSVC ARM64 build tools** `Microsoft.VisualStudio.Component.VC.Tools.ARM64`. `win-arm64` se zatím nevydává (čeká na T-057), komponenta je potřeba jen pro `pwsh scripts/release-build.ps1 -Runtime win-arm64`.

Nová instalace Build Tools rovnou s ARM64:

```powershell
winget install --id Microsoft.VisualStudio.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --includeRecommended --passive --wait"
```

Doplnění do existující instalace (Build Tools nebo Visual Studio). **Spusť v PowerShellu jako správce**: s `--passive` instalátor bez zvýšených oprávnění hned skončí s kódem 5007 („Commands with --quiet or --passive should be run elevated“).

```powershell
$vs = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$path = & "$vs\vswhere.exe" -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
& "$vs\setup.exe" modify --installPath $path --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --passive --norestart
```

Nebo ručně: *Visual Studio Installer → Upravit → Jednotlivé komponenty →* „MSVC … ARM64/ARM64EC build tools (nejnovější)“.

Ověření (musí vypsat název instalace):

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property displayName
```

## macOS

| Co | Poznámka |
|---|---|
| **Xcode Command Line Tools** | `clang` a linker pro AOT: `xcode-select --install`. Od T-026 je potřeba i pro první `dotnet build` na Macu, který s nimi přeloží knihovny FFmpeg (viz „Nativní knihovny FFmpeg“) |
| *(volitelně)* plné Xcode | není potřeba pro AOT ani pro ad-hoc podpis a balení (ověřeno v T-019 jen s Command Line Tools) |
| nástroje systému pro balení `.app` (T-019) | `codesign`, `sips`, `iconutil`, `hdiutil`, `ditto`, `/usr/bin/tar`, `lsappinfo`, `lsregister`, `defaults` jsou součástí macOS, nic se neinstaluje. `scripts/release-build.ps1` je volá absolutní cestou. |
| *(volitelně)* **PowerShell 7** (`pwsh` na `PATH`) | pro skripty vydání (T-055, T-019) a jejich Pester testy. Instalace přes Homebrew (`brew install --cask powershell`), nebo jako dotnet global tool (`dotnet tool install --global PowerShell`, složka `~/.dotnet/tools` musí být na `PATH`). Skripty i testy fungují s oběma způsoby instalace: podřízený `pwsh` spouštějí přes `Invoke-Pwsh` (u dotnet tool `dotnet …/pwsh.dll`). Pester 5 viz „Pester (testy skriptů)“. |
| *(volitelně)* **Microsoft Edge** (v `/Applications`) | jen pro E2E testy webu přes proxy (`tests/LayerPeek.E2E.Tests`, T-030), stejně jako na Windows; bez Edge se přeskočí. Edge si při prvním spuštění založí vlastní soubory v `~/Library` (známý pozůstatek), viz [architecture.md](architecture.md), „Izolace testů na macOS“. Ověřeno s Edge 156.0.4314.50 (T-021). |

**Verze SDK:** `global.json` připíná `11.0.100-rc.2.26504.105` s `"rollForward": "latestFeature"` a `"allowPrerelease": true`, takže se použije i novější nainstalované SDK stejné řady, např. `11.0.100-rc.3` (ověřeno na Macu 2026-10-28).

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

## Pester (testy skriptů)

Testy skriptů pro vydání (`scripts/ReleaseTools/ReleaseTools.Tests.ps1`, `ReleaseOrchestrator.Tests.ps1`) potřebují **PowerShell 7.2+** a **Pester 5 nebo novější** (licence Apache-2.0). Windows PowerShell má předinstalovaný Pester 3.4, ten nestačí. Instalace pro aktuálního uživatele (bez práv správce):

```powershell
pwsh -NoProfile -Command "Install-Module Pester -Scope CurrentUser -MinimumVersion 5.0.0 -Force -SkipPublisherCheck"
```

`-SkipPublisherCheck` je potřeba jen na Windows, kde starý Pester 3.4 má jiný podpis. Na macOS stačí `pwsh -NoProfile -Command "Install-Module Pester -Scope CurrentUser -MinimumVersion 5.0.0 -Force"` (modul se uloží do `~/.local/share/powershell/Modules`). Ověření: `pwsh -NoProfile -Command "Get-Module -ListAvailable Pester"` musí ukázat verzi 5 nebo vyšší. Bez `pwsh` nebo Pesteru 5+ se test `ScriptPesterTestsPass` v `dotnet test` přeskočí, neselže.

## AOT smoke test

`dotnet build` a `dotnet test` běží přes JIT, takže neodhalí chyby, které se projeví až v nativní AOT binárce. Po změnách v buildu, balíčcích nebo XAML proto spusť:

```powershell
pwsh scripts/aot-smoke.ps1                         # RID podle aktuálního OS/architektury
pwsh scripts/aot-smoke.ps1 -Runtime win-arm64 -Seconds 15   # jen na ARM64 Windows
```

Skript publikuje aplikaci (Release) do dočasné složky mimo repozitář, spustí ji a ověří, že po `-Seconds` sekundách (výchozí 10) stále běží. S parametrem `-PublishDir <složka>` místo publikování ověří už hotovou publish složku a nechá ji beze změny (používá `scripts/release-build.ps1`). Pak proces ukončí a dočasnou složku smaže (i při chybě). Při neúspěchu vrací nenulový exit code a vypíše výstup aplikace. Vyžaduje PowerShell 7.2+ a nativní toolchain popsaný výše.

Release exe nemá testovací režim. Skript proto těsně před jeho spuštěním odsune složky dat a logů aplikace (macOS `~/Library/Application Support/LayerPeek` a `~/Library/Logs/LayerPeek`, Windows `%APPDATA%\LayerPeek` a `%LOCALAPPDATA%\LayerPeek\logs`) vedle sebe jako `<složka>.aot-smoke-backup` a zapíše žurnál `<data>.aot-smoke-journal.json`. Na macOS stejně zálohuje i místa podle bundle identifikátoru (T-019): `~/Library/Saved Application State/<id>.savedState`, `~/Library/Caches/<id>`, `~/Library/HTTPStorages/<id>` a domény `defaults` `<id>` a `LayerPeek` (přes `defaults export/delete/import`, nikdy jako soubor, protože plist drží `cfprefsd`). Aplikace tak startuje s prázdným profilem. Po jejím ukončení skript smaže, co vytvořila, a zálohy vrátí (T-095). Když skript spadne nebo se přeruší, obnovu dokončí další běh, nebo ručně `pwsh scripts/aot-smoke.ps1 -RestoreOnly`.

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
| macOS: `Building FFmpeg from source failed` | chybí Xcode Command Line Tools (`xcode-select --install`), nebo viz `configure.log` / `make.log` ve složce `tmp-…` z chybové hlášky |
| Varování `IL2xxx` / `IL3xxx` | kód nebo knihovna není kompatibilní s trimmingem/AOT – viz „Native AOT rules“ v `CLAUDE.md` |
| `release-build.ps1`: `The MSVC component Microsoft.VisualStudio.Component.VC.Tools.ARM64 … is not installed` | chybí ARM64 build tools (viz „ARM64 build tools“) |
| `release-upload.ps1`: `GitHub CLI (gh) is not installed` / `is not logged in` | viz „Vydání (GitHub CLI)“ |
| `release-build.ps1`: `Velopack … and vpk … differ` | verze NuGet balíčku `Velopack` a nástroje `vpk` se liší (viz „Velopack `vpk`“) |
| `dotnet vpk`: `You must install or update .NET to run this application` | chybí runtime .NET 8–10 a manifest nemá `"rollForward": true` (viz „Velopack `vpk`“) |

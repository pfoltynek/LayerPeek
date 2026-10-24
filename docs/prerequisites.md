# Prerekvizity pro vývoj a Native AOT

Aplikace se publikuje jako **Native AOT** – výsledkem je jeden nativní spustitelný soubor bez nutnosti instalovat .NET runtime.

> **Důležité:**
> - Pro běžný vývoj (`dotnet build`, `dotnet run`, `dotnet test`, ladění F5 ve VS Code) stačí .NET SDK. Nativní toolchain je potřeba **jen pro `dotnet publish`** s AOT.
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

Projekt `LayerPeek.Camera.FFmpeg` potřebuje nativní DLL FFmpeg (LGPL build BtbN, připnutá verze). DLL nejsou v repozitáři. Stáhne je build (`build/FFmpeg.targets`):

- **První build na Windows potřebuje internet** (~77 MB z GitHubu). Archiv se ověří podle SHA256. Při neshodě build selže.
- Cache je mimo repozitář: `%LOCALAPPDATA%\LayerPeek.Build\ffmpeg\<sha256>\`. Další buildy fungují offline. Jinou složku nastavíš proměnnou prostředí `LayerPeekFFmpegCacheRoot`.
- DLL a `FFmpeg-LICENSE.txt` se kopírují do výstupu i do publish složky projektů, které na FFmpeg projekt odkazují.
- IDE (design-time build) nic nestahuje. Na macOS se krok zatím přeskočí (úkol T-026). Testy FFmpeg se tam přeskakují.
- **Když stažení selže** (offline, nebo připnuté vydání z GitHubu zmizelo), build skončí chybou s návodem. Zrcadlo není. Možnosti:
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
| **Xcode Command Line Tools** | `clang` a linker pro AOT: `xcode-select --install` |
| *(volitelně)* plné Xcode | není potřeba pro AOT ani pro ad-hoc podpis (`codesign -s -` je v Command Line Tools) – úkol T-019 |

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

`-SkipPublisherCheck` je potřeba jen na Windows, kde starý Pester 3.4 má jiný podpis. Ověření: `pwsh -NoProfile -Command "Get-Module -ListAvailable Pester"` musí ukázat verzi 5 nebo vyšší. Bez `pwsh` nebo Pesteru 5+ se test `ScriptPesterTestsPass` v `dotnet test` přeskočí, neselže.

## AOT smoke test

`dotnet build` a `dotnet test` běží přes JIT, takže neodhalí chyby, které se projeví až v nativní AOT binárce. Po změnách v buildu, balíčcích nebo XAML proto spusť:

```powershell
pwsh scripts/aot-smoke.ps1                         # RID podle aktuálního OS/architektury
pwsh scripts/aot-smoke.ps1 -Runtime win-arm64 -Seconds 15   # jen na ARM64 Windows
```

Skript publikuje aplikaci (Release) do dočasné složky mimo repozitář, spustí ji a ověří, že po `-Seconds` sekundách (výchozí 10) stále běží. S parametrem `-PublishDir <složka>` místo publikování ověří už hotovou publish složku a nechá ji beze změny (používá `scripts/release-build.ps1`). Pak proces ukončí a dočasnou složku smaže (i při chybě). Při neúspěchu vrací nenulový exit code a vypíše výstup aplikace. Vyžaduje PowerShell 7.2+ a nativní toolchain popsaný výše.

### Proč `UseOptimizedPublish=false`

V `src/LayerPeek.App/LayerPeek.App.csproj` je nastaveno `<UseOptimizedPublish>false</UseOptimizedPublish>`. .NET 11 SDK při AOT publishi standardně používá „optimalizovaný“ publish, který místo celého `Build` spouští jen `PrepareResources; Compile` – a `PrepareResources` tak běží před `ResolveReferences`. Avalonia 12 ale přidává své `!AvaloniaResources` až před `ResolveReferences`, takže se resource nevloží do assembly, XAML se nezkompiluje a publikovaná aplikace hned po startu spadne s `XamlLoadException: No precompiled XAML found`. Publish přitom projde bez varování. Nastavení vrací běžný plný build; odstranit ho lze až po opravě v Avalonii nebo SDK (ověřit smoke testem).

## Časté chyby

| Chyba | Řešení |
|---|---|
| `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64` | chybí workload *Desktop development with C++* (viz Windows) |
| `xcrun: error: invalid active developer path` | chybí Xcode Command Line Tools: `xcode-select --install` |
| Publikovaná aplikace spadne s `XamlLoadException: No precompiled XAML found` | v App csproj chybí `UseOptimizedPublish=false` (viz „AOT smoke test“) |
| `FFmpeg archive SHA256 mismatch` nebo chyba stahování při buildu | bez internetu není cache FFmpeg naplněná (viz „Nativní knihovny FFmpeg“); při neshodě hashe zkontroluj URL a hash v `build/FFmpeg.targets` |
| Varování `IL2xxx` / `IL3xxx` | kód nebo knihovna není kompatibilní s trimmingem/AOT – viz „Native AOT rules“ v `CLAUDE.md` |
| `release-build.ps1`: `The MSVC component Microsoft.VisualStudio.Component.VC.Tools.ARM64 … is not installed` | chybí ARM64 build tools (viz „ARM64 build tools“) |
| `release-upload.ps1`: `GitHub CLI (gh) is not installed` / `is not logged in` | viz „Vydání (GitHub CLI)“ |
| `release-build.ps1`: `Velopack … and vpk … differ` | verze NuGet balíčku `Velopack` a nástroje `vpk` se liší (viz „Velopack `vpk`“) |
| `dotnet vpk`: `You must install or update .NET to run this application` | chybí runtime .NET 8–10 a manifest nemá `"rollForward": true` (viz „Velopack `vpk`“) |

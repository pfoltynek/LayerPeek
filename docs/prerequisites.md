# Prerekvizity pro vývoj a Native AOT

Aplikace se publikuje jako **Native AOT** – výsledkem je jeden nativní spustitelný soubor bez nutnosti instalovat .NET runtime.

> **Důležité:**
> - Pro běžný vývoj (`dotnet build`, `dotnet run`, `dotnet test`, ladění F5 ve VS Code) stačí .NET SDK. Nativní toolchain je potřeba **jen pro `dotnet publish`** s AOT.
> - AOT nejde kompilovat napříč OS: Windows binárky se sestavují na Windows, macOS binárky na macOS. Jinou architekturu v rámci stejného OS sestavit lze (např. `osx-x64` na Apple Silicon Macu).
> - CI (GitHub Actions) je zatím odložené (T-016), AOT binárky se proto sestavují lokálně.

## Společné (obě platformy)

| Co | Poznámka |
|---|---|
| .NET SDK 11 | verze podle `global.json` (aktuálně `11.0.100-rc.2`), stáhnout z https://dotnet.microsoft.com/download/dotnet/11.0 |
| Git | |
| VS Code + rozšíření | C# Dev Kit, Avalonia for VS Code, EditorConfig (nabídnou se automaticky) |

## Windows

| Co | Poznámka |
|---|---|
| **Build Tools for Visual Studio 2026** (nebo Visual Studio 2022/2026) | bezplatné, plné IDE není nutné |
| Workload **Desktop development with C++** | obsahuje MSVC překladač/linker (komponenta `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`) a Windows SDK |
| *(volitelně)* **C++ ARM64 build tools** | jen pro publish na `win-arm64` |

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
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64
```

První příkaz musí vypsat název instalace, druhý vytvoří `PrusaTrayDiagnostic.exe` v `src/PrusaTrayDiagnostic.App/bin/Release/net11.0/win-x64/publish/`.

## macOS

| Co | Poznámka |
|---|---|
| **Xcode Command Line Tools** | `clang` a linker pro AOT: `xcode-select --install` |
| *(volitelně)* plné Xcode | není potřeba pro AOT; hodí se pro podepisování a notarizaci (úkol T-017) |
| *(později)* Apple Developer ID | podpis a notarizace `.app` pro distribuci (úkol T-017) |

Ověření:

```bash
xcode-select -p          # musí vypsat cestu, např. /Library/Developer/CommandLineTools
clang --version
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r osx-arm64   # Apple Silicon
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r osx-x64     # Intel (lze i na Apple Silicon)
```

Universal binary (arm64 + x64) se spojí nástrojem `lipo` – řeší úkol T-017.

## AOT smoke test

`dotnet build` a `dotnet test` běží přes JIT, takže neodhalí chyby, které se projeví až v nativní AOT binárce. Po změnách v buildu, balíčcích nebo XAML proto spusť:

```powershell
pwsh scripts/aot-smoke.ps1                         # RID podle aktuálního OS/architektury
pwsh scripts/aot-smoke.ps1 -Runtime osx-x64 -Seconds 15
```

Skript publikuje aplikaci (Release) do dočasné složky mimo repozitář, spustí ji a ověří, že po `-Seconds` sekundách (výchozí 10) stále běží. Pak proces ukončí a dočasnou složku smaže (i při chybě). Při neúspěchu vrací nenulový exit code a vypíše výstup aplikace. Vyžaduje PowerShell 7 a nativní toolchain popsaný výše.

### Proč `UseOptimizedPublish=false`

V `src/PrusaTrayDiagnostic.App/PrusaTrayDiagnostic.App.csproj` je nastaveno `<UseOptimizedPublish>false</UseOptimizedPublish>`. .NET 11 SDK při AOT publishi standardně používá „optimalizovaný“ publish, který místo celého `Build` spouští jen `PrepareResources; Compile` – a `PrepareResources` tak běží před `ResolveReferences`. Avalonia 12 ale přidává své `!AvaloniaResources` až před `ResolveReferences`, takže se resource nevloží do assembly, XAML se nezkompiluje a publikovaná aplikace hned po startu spadne s `XamlLoadException: No precompiled XAML found`. Publish přitom projde bez varování. Nastavení vrací běžný plný build; odstranit ho lze až po opravě v Avalonii nebo SDK (ověřit smoke testem).

## Časté chyby

| Chyba | Řešení |
|---|---|
| `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64` | chybí workload *Desktop development with C++* (viz Windows) |
| `xcrun: error: invalid active developer path` | chybí Xcode Command Line Tools: `xcode-select --install` |
| Publikovaná aplikace spadne s `XamlLoadException: No precompiled XAML found` | v App csproj chybí `UseOptimizedPublish=false` (viz „AOT smoke test“) |
| Varování `IL2xxx` / `IL3xxx` | kód nebo knihovna není kompatibilní s trimmingem/AOT – viz „Native AOT rules“ v `CLAUDE.md` |

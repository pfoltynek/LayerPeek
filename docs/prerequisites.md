# Prerekvizity pro vývoj a Native AOT

Aplikace se publikuje jako **Native AOT** – výsledkem je jeden nativní spustitelný soubor bez nutnosti instalovat .NET runtime.

> **Důležité:**
> - Pro běžný vývoj (`dotnet build`, `dotnet run`, `dotnet test`, ladění F5 ve VS Code) stačí .NET SDK. Nativní toolchain je potřeba **jen pro `dotnet publish`** s AOT.
> - AOT nejde kompilovat napříč OS: Windows binárky se sestavují na Windows, macOS binárky na macOS. Jinou architekturu v rámci stejného OS sestavit lze (např. `osx-x64` na Apple Silicon Macu).
> - CI (GitHub Actions) sestavuje AOT pro obě platformy automaticky, takže lokálně AOT nutně potřeba není.

## Společné (obě platformy)

| Co | Poznámka |
|---|---|
| .NET SDK 11 | verze podle `global.json` (aktuálně `11.0.100-rc.2`), stáhnout z https://dotnet.microsoft.com/download/dotnet/11.0 |
| Git | |
| VS Code + rozšíření | C# Dev Kit, Avalonia for VS Code, EditorConfig (nabídnou se automaticky) |

## Windows

| Co | Poznámka |
|---|---|
| Visual Studio 2022 (17.x) nebo novější, **případně jen Build Tools for Visual Studio** | stačí bezplatné Build Tools, plné IDE není nutné |
| Workload **Desktop development with C++** | obsahuje MSVC překladač/linker (`Microsoft.VisualStudio.Component.VC.Tools.x86.x64`) a Windows SDK |
| *(volitelně)* **C++ ARM64 build tools** | jen pro publish na `win-arm64` |

Instalace Build Tools z příkazové řádky (příklad pro verzi 2022):

```powershell
winget install --id Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --passive --wait"
```

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

## Časté chyby

| Chyba | Řešení |
|---|---|
| `vswhere.exe failed to locate Visual Studio with Microsoft.VisualStudio.Component.VC.Tools.x86.x64` | chybí workload *Desktop development with C++* (viz Windows) |
| `xcrun: error: invalid active developer path` | chybí Xcode Command Line Tools: `xcode-select --install` |
| Varování `IL2xxx` / `IL3xxx` | kód nebo knihovna není kompatibilní s trimmingem/AOT – viz „Native AOT rules“ v `CLAUDE.md` |

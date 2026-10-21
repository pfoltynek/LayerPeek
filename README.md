# PrusaTrayDiagnostic

Multiplatformní (Windows + macOS) aplikace v systémové liště / menu baru pro monitorování tisku na 3D tiskárnách Prusa přes **PrusaLink** (lokální síť) a **Prusa Connect** (cloud).

Plánované funkce: stav a progress tisku v ikoně, systémové notifikace (dokončeno, chyba, pauza, filament), teploty a diagnostika, více tiskáren, snímek z kamery.

## Technologie

- .NET 11 (SDK v `global.json`), C#, publikace jako **Native AOT**
- Avalonia 12 – tray ikona a menu
- xUnit v3 + Microsoft.Testing.Platform

## Struktura repozitáře

| Cesta | Obsah |
|---|---|
| `src/PrusaTrayDiagnostic.Core` | doménový model, abstrakce, monitoring |
| `src/PrusaTrayDiagnostic.PrusaLink` | klient PrusaLink REST API |
| `src/PrusaTrayDiagnostic.PrusaConnect` | klient Prusa Connect |
| `src/PrusaTrayDiagnostic.App` | Avalonia tray aplikace |
| `tests/` | unit testy |
| `tools/PrusaSimulator` | vývojový nástroj: simulované tiskárny PrusaLink a RTSP kamera – viz [docs/simulator.md](docs/simulator.md) |
| `docs/` | architektura a poznámky k API |
| `tasks/` | backlog úkolů – viz [tasks/README.md](tasks/README.md) |
| `.claude/agents/` | Claude Code agenti (architect, core-dev, ui-dev, qa-reviewer) |
| `CLAUDE.md` | pravidla pro Clauda a agenty |

## Sestavení a spuštění

Prerekvizity (včetně nástrojů pro Native AOT na Windows a macOS): [docs/prerequisites.md](docs/prerequisites.md).

```bash
dotnet build
dotnet test
dotnet run --project src/PrusaTrayDiagnostic.App
dotnet run --project tools/PrusaSimulator         # simulované tiskárny a kamera (docs/simulator.md)

# Native AOT – jeden nativní soubor bez nutnosti instalovat .NET
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r win-x64     # na Windows
dotnet publish src/PrusaTrayDiagnostic.App -c Release -r osx-arm64   # na macOS
```

## Vývoj s agenty

V Claude Code např.:

- *„Použij agenta architect a rozpadni funkci X na úkoly.“*
- *„Implementuj T-001 agentem core-dev a pak ho zkontroluj agentem qa-reviewer.“*

Úkoly s otevřenými otázkami agenti neimplementují – nejdřív se zeptají.

## VS Code

Doporučená rozšíření se nabídnou při otevření složky (C# Dev Kit, Avalonia, EditorConfig).

- **F5** – sestaví a spustí tray aplikaci s debuggerem (konfigurace *PrusaTrayDiagnostic (tray app)*)
- **Ctrl+Shift+B** – build řešení
- *Terminal → Run Task… → test* – spustí testy, *watch app* – `dotnet watch` s hot reloadem

## Licence

Projekt je licencovaný pod [Apache License 2.0](LICENSE). Copyright © Petr Foltýnek.

Nejde o oficiální produkt společnosti Prusa Research.

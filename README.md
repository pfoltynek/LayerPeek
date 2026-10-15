# PrusaTrayDiagnostic

Multiplatformní (Windows + macOS) aplikace v systémové liště / menu baru pro monitorování tisku na 3D tiskárnách Prusa přes **PrusaLink** (lokální síť) a **Prusa Connect** (cloud).

Plánované funkce: stav a progress tisku v ikoně, systémové notifikace (dokončeno, chyba, pauza, filament), teploty a diagnostika, více tiskáren, snímek z kamery.

## Technologie

- .NET 11 (SDK v `global.json`), C#
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
| `docs/` | architektura a poznámky k API |
| `tasks/` | backlog úkolů – viz [tasks/README.md](tasks/README.md) |
| `.claude/agents/` | Claude Code agenti (architect, core-dev, ui-dev, qa-reviewer) |
| `CLAUDE.md` | pravidla pro Clauda a agenty |

## Sestavení a spuštění

```bash
dotnet build
dotnet test
dotnet run --project src/PrusaTrayDiagnostic.App
```

## Vývoj s agenty

V Claude Code např.:

- *„Použij agenta architect a rozpadni funkci X na úkoly.“*
- *„Implementuj T-001 agentem core-dev a pak ho zkontroluj agentem qa-reviewer.“*

Úkoly s otevřenými otázkami agenti neimplementují – nejdřív se zeptají.

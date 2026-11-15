---
id: T-232
title: Windows – aktualizace nativních závislostí a nástrojů (FFmpeg, MSVC/Windows SDK, WebView/Edge, runner CI)
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-202]
---

## Cíl
Zadání uživatele 2026-11-15: aktualizovat všechny knihovny na Macu i na Windows. Tento úkol pokrývá nativní závislosti
a nástroje, které používá jen Windows; spravované balíčky řeší T-230, macOS T-231.

## Rozsah
- **FFmpeg pro Windows:** po T-202 vlastní minimální build ze stejného zdroje jako macOS (T-231) – stejná verze na obou
  OS; do T-202 build BtbN (`build/FFmpeg.targets`, dnes `n9.0.2-39-g7adfbe40dc`, autobuild 2026-10-31) – nové
  autobuildy BtbN se už neaktualizují, nahradí je T-202.
- **Visual Studio / MSVC a Windows SDK** pro Native AOT: aktuální verze, `docs/prerequisites.md`.
- **Playwright a Edge** pro E2E testy (`LayerPeek.E2E.Tests`): po T-230 nainstalovat prohlížeče nové verze Playwright,
  ověřit E2E (`tools/run-e2e.ps1`).
- **Runner CI** (T-218): obraz Windows (`windows-2025`) – nový obraz ručně, až ho GitHub nabídne.
- Instalátor Velopack (`vpk`) – verze společně s T-230.

## Akceptační kritéria
- [ ] FFmpeg na Windows ve stejné verzi jako na macOS (po T-202); testy kamery bez chyb
- [ ] `dotnet build`, `dotnet test`, AOT publish win-x64 a `aot-smoke.ps1` bez chyb a varování
- [ ] E2E testy (`tools/run-e2e.ps1`) s aktuální verzí Playwright a Edge bez chyb
- [ ] `docs/prerequisites.md` odpovídá

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

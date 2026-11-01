---
id: T-124
title: Windows – ověření instalátoru (instalace, upozornění, autostart, odinstalace)
status: todo
owner: qa-reviewer
priority: P1
platform: windows
depends_on: [T-017, T-114]
---

## Cíl
Sběrný úkol jen pro skutečnou instalaci na Windows: instalátor Velopack, kliknutí na toast po instalaci, když aplikace
neběží, autostart po odhlášení a přihlášení, odinstalace a E2E test instalátoru. Vznikl rozdělením T-114 (rozhodnutí
uživatele 2026-11-01): body se přesunuly sem (v T-114 odškrtnuté s „→ přesunuto do T-124“), ostatní zbytek Windows je
v T-125. Protějšek pro Mac je T-126.

Postup: [docs/windows-manual-testing.md](../../docs/windows-manual-testing.md), oddíl „Skutečná instalace
(`win.install` → `win.toast-click` → `win.autostart` → `win.uninstall`)“, a skript `tools/windows-verify.ps1`.

## Rozsah
- **Předpoklad:** LayerPeek nainstalovaná uživatelem se musí odsunout (odinstalovat nebo jinak odložit) – kontroly se
  skutečnou instalací se jinak odmítnou spustit a `win.e2e-installer` se přeskočí (stav 2026-10-31 v T-114). LayerPeek
  nesmí běžet. Jak instalaci uživatele odložit a po ověření vrátit, rozhodne uživatel (otázka níže).
- Instalátor: `pwsh scripts/release-build.ps1` (`artifacts/release/<verze>/dist/LayerPeek-<verze>-win-x64-Setup.exe`).
- Pořadí: `pwsh tools/windows-verify.ps1 -Only win.e2e-installer`, pak
  `pwsh tools/windows-verify.ps1 -Only win.install,win.toast-click,win.autostart`, odhlášení a přihlášení,
  `pwsh tools/windows-verify.ps1 -Only win.autostart`, nakonec `pwsh tools/windows-verify.ps1 -Only win.uninstall`.
  Po přerušení `pwsh tools/windows-verify.ps1 -RestoreOnly`.
- Výsledky (verze Windows, datum, výňatek z `artifacts/verify/<čas>/report.md`) zapsat do `## Poznámky k implementaci`.
  Každý `FAIL` nebo nalezená chyba = nový úkol. Bod se odškrtne až po úspěšném ověření.
- Hesla ani API klíče nikdy nevkládat do chatu ani do úkolů.

## Akceptační kritéria

### Instalátor, upozornění a autostart (z T-114, oddíl 3)
- [ ] **T-017** (krit. „Odinstalace smaže registraci COM aktivátoru toastů … ; po instalaci kliknutí na toast …
      otevře web tiskárny, i když aplikace neběží“): `win.e2e-installer`, `win.install`, `win.toast-click`
      a `win.uninstall` = `PASS` / potvrzeno (`docs/windows-manual-testing.md`, „Skutečná instalace“).
- [ ] **T-017** (krit. „Autostart po přihlášení funguje na Windows“): `win.autostart` (zapnout v nainstalované aplikaci,
      odhlásit, přihlásit, `pwsh tools/windows-verify.ps1 -Only win.autostart`), potom `win.uninstall`.
- [ ] **T-128** (krit. „Běh `tools/run-e2e.ps1` … T-058 → s T-124“): bez nainstalované LayerPeek uživatele
      `pwsh tools/run-e2e.ps1 -Class '*UpdateTests'` – projdou i oba instalační případy
      `InstalledOlderVersionUpdatesFromALocalFeedAfterAClick(MenuItem|Banner)` (s instalací uživatele se přeskočí).

### Závěr
- [ ] Výsledky zapsané v poznámkách (verze Windows, datum, zpráva skriptu); každá chyba má nový úkol; ověřovací
      instalace odinstalovaná a profil vrácený (`win.uninstall`, případně `pwsh tools/windows-verify.ps1 -RestoreOnly`);
      instalace uživatele vrácená podle jeho volby.

## Otevřené otázky
- Jak se má odložit a po ověření vrátit LayerPeek nainstalovaná uživatelem (odinstalovat a po ověření znovu nainstalovat
  z vydání, nebo ověřovat na jiném účtu / počítači)? Rozhodne uživatel před začátkem.

## Poznámky k implementaci
(vyplní agent / uživatel po ověření)

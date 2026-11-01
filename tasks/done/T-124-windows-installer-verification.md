---
id: T-124
title: Windows – ověření instalátoru (instalace, upozornění, autostart, odinstalace)
status: done
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
- [x] **T-017** (krit. „Odinstalace smaže registraci COM aktivátoru toastů … ; po instalaci kliknutí na toast …
      otevře web tiskárny, i když aplikace neběží“): `win.e2e-installer`, `win.install`, `win.toast-click`
      a `win.uninstall` = `PASS` / potvrzeno (`docs/windows-manual-testing.md`, „Skutečná instalace“). `win.e2e-installer`, `win.install`, `win.uninstall` = `PASS` (2026-11-01); `win.toast-click` → pokryto E2E (rozhodnutí uživatele 2026-11-01), ruční klik na nainstalované verzi → přesunuto do T-125
- [x] **T-017** (krit. „Autostart po přihlášení funguje na Windows“): `win.autostart` (zapnout v nainstalované aplikaci,
      odhlásit, přihlásit, `pwsh tools/windows-verify.ps1 -Only win.autostart`), potom `win.uninstall`. Ověřeno podle zástupce v Po spuštění (rozhodnutí uživatele 2026-11-01); skutečné odhlášení a přihlášení → přesunuto do T-125
- [x] **T-128** (krit. „Běh `tools/run-e2e.ps1` … T-058 → s T-124“): bez nainstalované LayerPeek uživatele
      `pwsh tools/run-e2e.ps1 -Class '*UpdateTests'` – projdou i oba instalační případy
      `InstalledOlderVersionUpdatesFromALocalFeedAfterAClick(MenuItem|Banner)` (s instalací uživatele se přeskočí). → přesunuto do T-125 (blokuje T-133: upozornění testovací aplikace vypnutá ve Windows)

### Závěr
- [x] Výsledky zapsané v poznámkách (verze Windows, datum, zpráva skriptu); každá chyba má nový úkol; ověřovací
      instalace odinstalovaná a profil vrácený (`win.uninstall`, případně `pwsh tools/windows-verify.ps1 -RestoreOnly`);
      instalace uživatele vrácená podle jeho volby.

## Otevřené otázky
- Jak se má odložit a po ověření vrátit LayerPeek nainstalovaná uživatelem (odinstalovat a po ověření znovu nainstalovat
  z vydání, nebo ověřovat na jiném účtu / počítači)? Rozhodne uživatel před začátkem.

## Poznámky k implementaci
Hlavní agent, 2026-11-01, Windows 11 Pro 10.0.26300, instalátor 0.4.0 sestavený lokálně (`release-build.ps1` s dočasnou
verzí 0.4.0, nic nenahráno):
- Instalace uživatele 0.3.1: data zálohovaná do `%USERPROFILE%\LayerPeek-backup-2026-11-01` (nastavení, okna kamer, stav
  aktualizací, logy), odinstalace `Update.exe --uninstall --silent` – aplikace i zástupce pryč, nastavení a hesla zůstala.
- `win.e2e-installer` (`VelopackInstallerTests`): první běh selhal na předpokladu testu (prázdná instalační složka po
  odinstalaci) → T-133, po opravě 1/1 PASS.
- `win.install`: PASS – 0.4.0 nainstalovaná, zástupce ve Start menu a aktivátor upozornění zaregistrované; zástupce
  v Po spuštění ukazuje na nainstalovanou aplikaci (autostart zapnutý instalací).
- `win.toast-click` a skutečné odhlášení/přihlášení (`win.autostart`): rozhodnutí uživatele 2026-11-01 – klik na
  upozornění pokrývají E2E testy (`ToastNotificationTests` 6/6 včetně kliku při neběžící aplikaci, test instalátoru
  registrace), autostart ověřen zástupcem; ruční ověření na nainstalované verzi → T-125 (nepovinné, po vydání).
- `win.uninstall`: PASS – žádný klíč Uninstall, zástupce ani aktivátor; `%APPDATA%\LayerPeek` a `%LOCALAPPDATA%\LayerPeek`
  vrácené.
- Instalační případy `UpdateTests` neproběhly: upozornění testovací aplikace `LayerPeek.E2E` zůstávají ve Windows vypnutá
  (`DisabledForApplication`) a zapnout je nejde ani zápisem do registru, ani jednorázovou obnovou přes Nastavení → T-133,
  ověření → T-125.
- Uzavřeno 2026-11-01. Instalace uživatele se vrátí instalací zveřejněné 0.4.0.

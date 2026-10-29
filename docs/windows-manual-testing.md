# Ruční ověření na Windows (souhrnný postup)

Co na Windows zbývá ověřit pro rozpracované úkoly: „Windows beze změny“ po macOS úkolech (T-095, T-018, T-021, T-026,
T-053, T-097, T-100), instalátor Velopack s kliknutím na toast a autostart po skutečném přihlášení (T-017) a pravidlo
hlavního panelu z T-102 (až bude hotové). Vše provede skript `tools/windows-verify.ps1` (T-105); ručně zbývá jen to, na co
se zeptá.

> Stav: sepsáno 2026-10-29. Když úkol přidá nebo změní ruční krok na Windows, upraví se skript i tento soubor.

## Než začnete

- Windows 11 x64, přihlášený uživatel **bez** spuštění jako správce. .NET SDK z `global.json`, `pwsh` (i jako dotnet global
  tool), Pester 5 a pro AOT publish a instalátor C++ build tools (viz [prerequisites.md](prerequisites.md)).
- Instalátor pro `win.e2e-installer`, `win.install`: nejdřív `pwsh scripts/release-build.ps1`
  (`artifacts/release/<verze>/dist/LayerPeek-<verze>-win-x64-Setup.exe`). Bez něj jsou tyto kontroly `SKIP`.
- LayerPeek nesmí běžet a nesmí být nainstalovaný (kontroly se skutečnou instalací se jinak odmítnou spustit).
- Hesla ani API klíče nikdy nevkládejte do chatu ani do úkolů. Heslo simulátoru (`simulator`) tajné není.

## Spuštění

```powershell
pwsh tools/windows-verify.ps1 -ListChecks          # kontroly a kritéria, která uzavírají
pwsh tools/windows-verify.ps1                      # vše, interaktivně (ptá se před každým zásahem)
pwsh tools/windows-verify.ps1 -Unattended          # jen build, testy a AOT smoke; nic se neptá
pwsh tools/windows-verify.ps1 -Only win.install,win.toast-click,win.autostart
pwsh tools/windows-verify.ps1 -Only win.uninstall  # odinstaluje ověřovací instalaci a vrátí profil
pwsh tools/windows-verify.ps1 -RestoreOnly         # jen obnova profilu po přerušeném běhu
```

Výsledky `PASS` / `FAIL` / `SKIP` (s důvodem) / `MANUAL` (potvrzeno „y“), zpráva `report.md` a `report.json` s commitem,
verzí a důkazy v `artifacts/verify/<čas>/`. Kód 1 při jakémkoli `FAIL`.

| Kontrola | Druh | Uzavírá |
|---|---|---|
| `win.build` | automaticky | „Windows beze změny“: T-095, T-018 krit. 4, T-021 krit. 9, T-026 krit. 5, T-053 krit. 6, T-097 krit. 5, T-100 krit. 5 (build Debug i Release bez varování) |
| `win.test` | automaticky | totéž + T-018 krit. 3 (celý `dotnet test` bez selhání) |
| `win.aot-smoke` | automaticky | T-053 krit. 6, T-097 krit. 5 (AOT exe bez varování startuje; `scripts/aot-smoke.ps1` profil sám zálohuje) |
| `win.e2e` | oprávnění (plocha na několik minut) | T-018 krit. 3, T-100 krit. 5, T-097 krit. 5, T-026 krit. 5 (`tools/run-e2e.ps1 -NoSystemChanges`) |
| `win.e2e-installer` | oprávnění (tichá instalace a odinstalace) | T-017 krit. 6 (odinstalace smaže registraci COM aktivátoru toastů) |
| `win.install` | oprávnění (skutečná instalace) | podklad pro T-017 krit. 6 a 7 |
| `win.toast-click` | ručně | T-017 krit. 6 (klik na toast spustí neběžící aplikaci a otevře web tiskárny) |
| `win.autostart` | ručně, dva běhy | T-017 krit. 7 (autostart po odhlášení a přihlášení) |
| `win.uninstall` | oprávnění | T-017 krit. 6 (po odinstalaci není klíč Uninstall, zástupci ani `HKCU\Software\Classes\CLSID\{EC47413C-984D-447A-866A-5F780E408DE7}`) |
| `win.taskbar` | ručně | T-102 (jen když je T-102 v `tasks/done/`, jinak `SKIP`) |

## Skutečná instalace (`win.install` → `win.toast-click` → `win.autostart` → `win.uninstall`)

1. `win.install` odsune `%APPDATA%\LayerPeek` a celé `%LOCALAPPDATA%\LayerPeek` (logy vývojových sestavení) vedle sebe na
   `<složka>.aot-smoke-backup` (žurnál `%APPDATA%\LayerPeek.verify-install-journal.json`), uloží si zástupce LayerPeek
   v nabídce Start a ve složce Po spuštění a seznam cílů Správce pověření s „LayerPeek“, a nainstaluje
   `Setup.exe --silent`. Instalace zůstane i po skončení běhu.
2. `win.toast-click`: skript spustí nainstalovanou aplikaci (prázdný profil) a simulátor. V Nastavení přidáte tiskárnu
   simulátoru (adresu, jméno a heslo vypíše skript), skript ji pozastaví, aplikaci ukončí a vy kliknete na toast
   v centru oznámení (Win+N). Skript čeká na nový proces LayerPeek a zeptá se, jestli se otevřel web tiskárny.
3. `win.autostart`: v nainstalované aplikaci zapnout spouštění s Windows, Uložit, odhlásit a přihlásit, pak
   `pwsh tools/windows-verify.ps1 -Only win.autostart`. Skript ověří cíl zástupce ve složce Po spuštění a že LayerPeek běží
   od přihlášení (čas startu proti `explorer.exe`).
4. `win.uninstall` spustí `QuietUninstallString` z klíče Uninstall, ověří, že nic nezůstalo, smaže nové cíle Správce
   pověření (tiskárna simulátoru), vrátí zástupce a odsunuté složky. Po přerušení obnoví profil `-RestoreOnly`.

Známý pozůstatek (rozhodnuto v T-017): log Velopacku v `%LOCALAPPDATA%\velopack`.

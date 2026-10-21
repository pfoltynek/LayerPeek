---
id: T-017
title: Balení a distribuce, autostart – Windows
status: todo
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-008, T-055, T-056]
---

## Cíl
Instalovatelná aplikace pro Windows, spouštění po přihlášení. Balení a autostart pro macOS je samostatný úkol T-019.

Rozhodnutí uživatele (2026-10-21):
- **Instalátor:** preferovaný je **Velopack**, ale až po ověření ve spiku T-056. Pokud T-056 Velopack nedoporučí, rozhodne o náhradě uživatel (WiX chce nejdřív probrat). Do té doby se instalátor neimplementuje.
- **RID:** `win-x64` a `win-arm64`. `win-arm64` se vydává až po dokončení T-057 (FFmpeg pro ARM64), viz T-055.
- **Formáty:** instalátor a k tomu `.zip` (archiv vytváří skript z T-055).
- **Podpis:** žádný placený certifikát, binárky i instalátor zůstanou **nepodepsané**. Uživatelé uvidí varování SmartScreen („Systém Windows ochránil váš počítač“), které je potřeba popsat v dokumentaci vydání.
- **Sestavení a vydání:** lokálně skripty z T-055 (CI odloženo, T-016). Instalátor se připojí jako balicí krok do bodu rozšíření z T-055.

## Rozsah
- Instalátor (Velopack podle výsledku T-056) pro `win-x64` a `win-arm64`, instalace pro aktuálního uživatele bez admin práv
- Balicí krok instalátoru ve skriptech T-055 (výstup jde do kontrolních součtů a do GitHub vydání)
- Autostart po přihlášení (registry `Run` / složka Startup / zástupce Velopacku ve `Startup` – podle T-056 a odpovědi na otázku níže)
- Toasty (navazuje na T-010):
  - zástupce ve Start menu se stejným AppUserModelID `PrusaTrayDiagnostic` a názvem „Prusa Tray Diagnostic“ (obojí je finální). Zda ho vytvoří instalátor, nebo aplikace při prvním startu / v instalačním hooku (aplikace ho už dnes umí vytvořit, viz `AppShortcutRegistration` a `scripts/aot-smoke.ps1`), určí doporučení z T-056;
  - COM aktivátor (`ToastActivatorCLSID` v zástupci), aby kliknutí na toast fungovalo i když aplikace neběží (rozhodnutí uživatele: patří sem, ne do T-010);
  - odinstalace uklidí zástupce, klíč `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic` a historii toastů (podrobnosti v poznámkách T-010; u Velopacku přes hook `--veloapp-uninstall`).
- Instalační hooky musí proběhnout dřív než kontrola jediné instance (T-043) a bez UI.

## Akceptační kritéria
- [ ] Postup sestavení a vydání pro Windows zdokumentovaný v `docs/release.md` (z T-055), včetně varování SmartScreen u nepodepsané aplikace
- [ ] Instalátor `win-x64` nainstaluje aplikaci bez admin práv, zástupce má AUMID `PrusaTrayDiagnostic` a `ToastActivatorCLSID` (ověřeno)
- [ ] Instalátor `win-arm64` vznikne stejným skriptem, pokud je `win-arm64` zapnutý (vydává se až po T-057; spuštění na ARM64 zatím nejde ověřit, uživatel nemá zařízení)
- [ ] Odinstalace nezanechá zástupce, klíč notifikací ani toasty (ověřeno)
- [ ] Autostart po přihlášení funguje na Windows (ověřeno)
- [ ] Nový NuGet balíček (Velopack) je ověřeně AOT/trim kompatibilní (T-056), AOT publish bez varování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Formát pro Windows~~ → instalátor (preferovaný Velopack, čeká na T-056) + `.zip` (rozhodnuto 2026-10-21).
- ~~Podepisování kódu na Windows~~ → bez podpisu, SmartScreen varování se akceptuje (rozhodnuto 2026-10-21).
- ~~Odkud automatické aktualizace~~ → z veřejných GitHub Releases bez tokenu v aplikaci (repozitář bude později veřejný, rozhodnuto 2026-10-21). Implementace podle výsledku T-056. Platí i pro macOS (T-019).
- Má být autostart volitelný v nastavení (T-015), nebo ho řeší jen instalátor?
- Pokud T-056 Velopack nedoporučí: náhrada (WiX nebo jiný) – uživatel ji chce nejdřív probrat.

## Poznámky k implementaci

---
id: T-017
title: Balení a distribuce, autostart – Windows
status: todo
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-008]
---

## Cíl
Instalovatelná aplikace pro Windows, spouštění po přihlášení. Balení a autostart pro macOS je samostatný úkol T-019.

## Rozsah
- Windows: Native AOT publish (`win-x64`, případně `win-arm64`) + instalátor
- Autostart po přihlášení (registry `Run` / složka Startup)
- Toasty (navazuje na T-010):
  - instalátor vytvoří zástupce ve Start menu se stejným AppUserModelID `PrusaTrayDiagnostic` a názvem „Prusa Tray Diagnostic“ (obojí je finální);
  - COM aktivátor (`ToastActivatorCLSID` v zástupci), aby kliknutí na toast fungovalo i když aplikace neběží (rozhodnutí uživatele: patří sem, ne do T-010);
  - odinstalace uklidí zástupce, klíč `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic` a historii toastů (podrobnosti v poznámkách T-010).

## Akceptační kritéria
- [ ] Postup sestavení pro Windows zdokumentovaný v `docs/`
- [ ] Postup sestavení release lokálně na Windows (CI odloženo, viz T-016)
- [ ] Autostart po přihlášení funguje na Windows (ověřeno)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Formát pro Windows: MSIX, instalátor (Inno Setup/WiX), nebo jen zip? (AppUserModelID `PrusaTrayDiagnostic` je daný z T-010; MSIX by ho nahradil identitou balíčku.)
- Podepisování kódu na Windows (certifikát)?
- Automatické aktualizace (např. Velopack – ověřit kompatibilitu s Native AOT)? Rozhodnutí se týká i macOS (T-019).
- Má být autostart volitelný v nastavení (T-015), nebo ho řeší jen instalátor?

## Poznámky k implementaci

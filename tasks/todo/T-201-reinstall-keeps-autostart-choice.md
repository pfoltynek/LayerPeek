---
id: T-201
title: Přeinstalace (winget upgrade) nemá znovu zapnout spouštění po přihlášení
status: todo
owner: ui-dev
priority: P1
platform: windows
depends_on: [T-059]
---

## Cíl
Hook instalace Velopacku (`--veloapp-install`) dnes při **každé** instalaci zapne spouštění po přihlášení (výchozí zapnuto,
rozhodnutí uživatele 2026-10-26). Velopack ale stejný hook spustí i při **přeinstalaci přes existující instalaci** – a
přesně to dělá `winget upgrade` (manifest `UpgradeBehavior: install`, nový `Setup.exe --silent`, T-059). Uživatel, který si
autostart vypnul, ho tak po `winget upgrade` (i v rámci `winget upgrade --all`) má zase zapnutý. Týká se všech uživatelů
1.0.0, kteří instalují přes winget, proto P1. Aktualizace v aplikaci (Velopack `apply`) hook instalace nespouští a volbu
nemění.

Zjištěno v T-059 (ověřeno ve zdroji Velopacku 1.2.161, ne pokusem, `docs/release.md` „Velopack a `winget upgrade`“);
rozhodnutí uživatele 2026-11-11: opravit hook v samostatném úkolu.

## Současný stav (kód)
- `src/LayerPeek.App/Installer/VelopackStartup.cs` – `OnAfterInstallFastCallback` → `InstallerHooks.AfterInstall()`
  (jen Windows, produkční identita, `AppServices.CreateInstallerHooks`).
- `src/LayerPeek.App/Installer/InstallerHooks.cs` – `AfterInstall()` zaregistruje toasty a bezpodmínečně volá
  `autostart.SetEnabled(true)`; komentář tvrdí „Only a new install runs this hook, so an update keeps the user's
  choice“ – pro přeinstalaci to neplatí.
- Stav autostartu se nikde neukládá: je to jen existence zástupce `LayerPeek.lnk` ve složce Po spuštění
  (`src/LayerPeek.App/Platform/Windows/StartupShortcutAutostart.cs`, `IAutostart`). Vypnutí v nastavení zástupce smaže,
  takže hook při přeinstalaci nepozná „uživatel vypnul“ od „nová instalace“.
- Přeinstalace nahradí celou `%LOCALAPPDATA%\LayerPeek`; zachová se `%APPDATA%\LayerPeek` (nastavení,
  `settings.json`, `JsonSettingsStore` v Core) a zástupce ve složce Po spuštění (leží mimo instalaci). Odinstalace
  (`BeforeUninstall`) zástupce smaže, nastavení ponechá.

## Rozsah (navržené řešení, k potvrzení – viz otázky)
- **Varianta A (doporučená): uložit výslovnou volbu uživatele.** Nové pole v `AppSettings` (např. `bool? StartAtLogin`,
  `null` = uživatel nikdy nevolil), zapisované okno nastavení při uložení přepínače (Windows; na macOS se pole nepoužívá
  nebo se taky jen zapisuje). Hook `AfterInstall` načte nastavení (`JsonSettingsStore` jen pro čtení, bez migrací a
  bez zápisu, v časovém limitu fast callbacku) a autostart zapne jen když volba není `false`. Nečitelné nebo chybějící
  nastavení = zapnout (dnešní chování nové instalace). Výsledek: nová instalace zapne, přeinstalace i odinstalace +
  nová instalace respektují vypnutí.
- **Varianta B:** autostart zapnout jen když `%APPDATA%\LayerPeek\settings.json` ještě neexistuje (první instalace
  u účtu). Bez změny schématu, ale po odinstalaci a nové instalaci (nastavení zůstává) se autostart už nezapne, ani když
  ho uživatel měl zapnutý, a stejně tak u uživatele, který dřív používal `.zip`.
- Opravit komentáře v `InstallerHooks` a `docs/velopack.md` (bod 3, „běží jen při nové instalaci“), `docs/release.md`
  („Velopack a `winget upgrade`“: odrážka o autostartu) a podle výsledku T-059 kontrolu po instalaci.
- Kdyby oprava nestihla první vydání s winget (1.0.0), doplnit do kapitoly 12 všech 8 příruček (`manuals/*/manual.md`,
  odrážka „Windows, installed with winget“), že `winget upgrade` zapne autostart znovu.

## Akceptační kritéria
- [ ] Přeinstalace přes existující instalaci (`Setup.exe --silent`, `winget upgrade`) nezapne autostart, když ho
      uživatel vypnul; když ho měl zapnutý, zůstane zapnutý
- [ ] Nová instalace bez předchozí volby autostart zapne (beze změny, rozhodnutí 2026-10-26)
- [ ] Unit testy `InstallerHooksTests` (falešné `IAutostart` a úložiště nastavení): nová instalace, přeinstalace po
      vypnutí, přeinstalace po zapnutí, nečitelné nastavení, selhání autostartu jen do logu; testy okna nastavení, že
      přepínač volbu uloží (varianta A)
- [ ] Ruční ověření na Windows: nainstalovat, vypnout autostart, spustit tentýž / novější `Setup.exe --silent`
      (nebo `winget install --manifest` podruhé) → autostart zůstane vypnutý (lze přesunout do T-124)
- [ ] Dokumentace (`docs/velopack.md`, `docs/release.md`) odpovídá novému chování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Varianta A (uložená volba v nastavení) nebo B (zapnout jen bez existujícího `settings.json`)?** Doporučení: A.
2. Po **odinstalaci a nové instalaci** (nastavení zůstává): respektovat dřívější vypnutí (A), nebo autostart zapnout
   jako u úplně nové instalace?
3. Má se uložená volba exportovat v diagnostickém balíčku / exportu nastavení (`SettingsExport`) a platit i na macOS
   (tam je autostart ve výchozím stavu vypnutý a hook instalace neexistuje)?
4. **Závislost na T-059:** T-059 bude hotový až po prvním odeslání do winget po vydání 1.0.0, takže by se T-201
   podle pravidel nesměl začít dřív a oprava by do 1.0.0 nestihla. Oprava sama na T-059 technicky nezávisí (týká se
   i ruční přeinstalace přes `Setup.exe`). Zrušit závislost, aby šlo opravit před 1.0.0?

## Poznámky k implementaci
(vyplní agent po dokončení)

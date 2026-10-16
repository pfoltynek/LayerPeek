---
id: T-008
title: Tray menu
status: done
owner: ui-dev
priority: P1
platform: all
depends_on: [T-007]
---

## Cíl
Kontextové menu tray ikony.

## Rozsah
- Seznam tiskáren se stavem a progressem
- Otevřít webové rozhraní PrusaLink / Prusa Connect v prohlížeči
- Nastavení…, Diagnostika…, O aplikaci, Ukončit

## Akceptační kritéria
- [x] Menu se aktualizuje se změnou stavu (pokryto testy view modelu, presenteru a `TrayMenuTarget`; menu na Windows ručně ověřil uživatel)
- [x] Ukončit korektně zastaví monitorovací službu (test se skutečnou `MonitoringSession`; „Ukončit“ na Windows ručně ověřil uživatel)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Jazyk UI: čeština, angličtina, nebo lokalizace obou?~~ **Vyřešeno (2026-10-16):** UI je lokalizované CZ + EN přes .resx (infrastruktura vznikla v T-007), jazyk podle systému s fallbackem na angličtinu. Všechny nové texty T-008 jsou v `Strings.resx` i `Strings.cs.resx`.

Rozhodnutí uživatele (2026-10-16):
- ~~Nastavení… a Diagnostika… zakázané, nebo skryté?~~ → Viditelné, ale zakázané, dokud nevzniknou okna (T-015, T-011).
- ~~Otevření webu kliknutím na řádek tiskárny?~~ → Ano, kliknutí na řádek otevře web. Zbývající čas v menu není.
- ~~Prusa Connect: úvodní stránka, nebo přímý odkaz na tiskárnu?~~ → Úvodní stránka `https://connect.prusa3d.com/`.
- ~~Obsah okna O aplikaci?~~ → Doplněn odkaz na GitHub repozitář (otevírá se přes `IUrlLauncher`), upozornění, že nejde o oficiální produkt Prusa Research (CZ + EN), a licence Apache License 2.0. V kořeni repa je soubor `LICENSE` se standardním textem staženým z apache.org. Copyright: Petr Foltýnek.
- ~~Avalonia.Headless pro testy adaptéru?~~ → Neschváleno. Logika adaptéru je místo toho oddělená od Avalonie (viz poznámky).
- ~~Tiskárna s neplatnou adresou zakázaná (šedá)?~~ → Přijato: zobrazená, zakázaná, s tooltipem „Adresa tiskárny není platná“.

Otevřené otázky nejsou.

## Poznámky k implementaci
- **Struktura (`src/PrusaTrayDiagnostic.App/Tray/Menu`):**
  - `TrayMenuEntry` je neměnný záznam jedné položky (příkaz, text, povolení, tooltip, URL), porovnává se hodnotou. `TrayMenuCommand` je výčet akcí (`OpenWeb`, `Settings`, `Diagnostics`, `About`, `Quit`), `TrayMenuOptions` říká, která pomocná okna existují. Jsou to body, na které navážou T-011, T-015 a T-013 (podmenu tiskáren).
  - `PrinterMenuContent` obsahuje jen to, co položka tiskárny ukazuje (stav, celé % dolů). Pravidla jsou stejná jako u tooltipu: progress jen u tisku, pauzy a „vyžaduje zásah“ dosažitelné tiskárny.
  - `TrayMenuFormatter` skládá text „MK4: Tiskne – 42 %“, délku omezuje na 100 znaků (zkracuje název, nerozdělí surrogate pár). `NativeMenuText` zdvojuje `_`, protože Avalonia ho bere jako access key (Windows ho skryje a podtrhne, macOS ho odstraní).
  - `PrinterWebAddress`: PrusaLink používá stejná pravidla adresy jako API klient (`PrusaLinkAddress`), takže odmítne přihlašovací údaje v URL, query i jiné schéma než http/https. Prusa Connect otevírá úvodní stránku Connectu. `IsSafeToLaunch` pouští ven jen absolutní http(s) adresy bez přihlašovacích údajů.
  - `TrayMenuViewModel` (rozhraní `ITrayMenuSource`, `IMonitoringObserver`) odebírá `IPrinterMonitor.SnapshotUpdated`:
    - Pod zámkem porovná jen `PrinterMenuContent` dané tiskárny. Beze změny (časové razítko, telemetrie, zlomek procenta, zbývající čas, soubor) se nic dalšího neděje.
    - Změněnou tiskárnu označí a pošle na UI vlákno jedinou aktualizaci pro celou dávku. Ta naformátuje jen označené položky a v `ObservableCollection` nahradí jen ty, jejichž `TrayMenuEntry` se opravdu liší. Změna, která skončí tam, kde začala, nic nezmění.
    - Nová konfigurace se promítne s minimem změn (nahrazení, přidání a odebrání na konci).
    - Stavové zprávy „Spouštění…“, „Není nastavena žádná tiskárna“ a „Sledování se zastavilo kvůli chybě“ jsou zakázané položky. Po selhání monitoringu pozdní snapshoty nic nevrátí zpět.
    - Po `Dispose` se ignorují aktualizace i otevírání webu.
  - `TrayMenuPresenter` promítá kolekci na `ITrayMenuTarget` po jednotlivých položkách (aktualizace jedné tiskárny = jedna změna jedné položky) a směruje kliknutí: tiskárna → web, O aplikaci / Ukončit → `ITrayAppCommands`. Zakázané položky a kliknutí po `Dispose` ignoruje. Výjimku z příkazu zaloguje, aby nespadla aplikace.
  - Menu na platformě: `TrayMenuTarget` (bez Avalonie, testovaný) drží logiku položek. Položky vytváří jednou a mění na místě, vlastnost (`Header`, `IsEnabled`, `ToolTip`) zapíše jen při změně hodnoty. Každá položka si pamatuje aktuální `TrayMenuEntry`, takže kliknutí po změně adresy otevře novou adresu. Oddělovač za tiskárnami je vždy přítomný, takže index tiskárny = index v menu. NativeMenu se při pollingu nikdy znovu nesestavuje. Rozložení: tiskárny, oddělovač, Nastavení…, Diagnostika…, oddělovač, O aplikaci, Ukončit.
  - `AvaloniaNativeTrayMenu` (rozhraní `INativeTrayMenu` / `INativeTrayMenuItem`) je tenký adaptér nad `NativeMenu` z `TrayIcon`: jen vytvoření, vložení, odebrání položky a zápis vlastností. Avalonia.Headless se nepoužívá (neschváleno), proto adaptér samotný testy pokrytý není.
- **Otevření prohlížeče (`Platform/ProcessUrlLauncher`, rozhraní `IUrlLauncher`):** Windows `Process.Start` s `UseShellExecute`, macOS `open <url>`, ostatní `xdg-open <url>`. Na Unixu se adresa předává jako jediný argument bez shellu. Před spuštěním se znovu ověří schéma http/https. Spouští se mimo UI vlákno, selhání (např. žádný prohlížeč) se zaloguje a vrátí `false`, handle procesu se hned uvolní. Bez reflexe, kompatibilní s Native AOT.
- **Ukončení (`Tray/QuitCoordinator`):** běží jen jednou. Zavolá zastavení s limitem 3 s (rozhodnutí z T-007) a potom vždy ukončí aplikaci, i když zastavení selže. Navíc je tvrdý limit 3 s + 2 s na uvolnění služeb, aby ukončení nikdy nezamrzlo. `App` nejdřív zavře okno O aplikaci, `TrayHost.StopAsync` pak synchronně uvolní presenter menu i ikony (další kliknutí ani aktualizace už do tray nedojdou), zastaví monitoring a uvolní služby. Selhání zastavení se zaloguje jako chyba, překročení tvrdého limitu jako varování. Zápis do logu je chráněný, takže ani nefunkční logger neshodí `QuitAsync` (to se nikdy nevyhodí do `async void` handleru). Logger factory vlastní `TrayHost` (`LoggerFactory.Create`), ne DI kontejner. Do kontejneru se registruje jako instance, takže se s ním neuvolní a logy z ukončení se zapíšou i po uvolnění kontejneru. Selhání při uvolňování služeb zaloguje přímo `TrayHost`. Factory se uvolní až v `App.OnDesktopExit` (`ReleaseLogging`). Návazný požadavek je zapsaný v T-024.
- **`MonitoringSession`** teď dostává seznam `IMonitoringObserver` (ikona i menu), místo jednoho view modelu. Selhání jednoho pozorovatele při hlášení chyby zaloguje a ostatní stejně informuje.
- **Okno O aplikaci (`Windows/AboutWindow`, `AboutViewModel`):** název, verze (z `AssemblyInformationalVersion`), popis, upozornění „Nejde o oficiální produkt společnosti Prusa Research.“, „Licence: Apache License 2.0“, copyright z `AssemblyCopyrightAttribute` (generuje se z `Copyright` v `Directory.Build.props`, záloha „Copyright © Petr Foltýnek“) a odkaz „Zdrojový kód na GitHubu“. Odkaz otevírá `https://github.com/pfoltynek/PrusaTrayDiagnostic` přes `IUrlLauncher` (příkaz `[RelayCommand]`, ne `NavigateUri`). Kompilované bindingy přes `x:DataType`, jen jedna instance (opakované kliknutí okno aktivuje). Zavření okna aplikaci neukončí (`ShutdownMode.OnExplicitShutdown`). Během ukončování se okno neotevře.
- **Licence:** `LICENSE` v kořeni repa je standardní text Apache License 2.0 stažený z https://www.apache.org/licenses/LICENSE-2.0.txt (beze změn). `Directory.Build.props` má `Copyright` a `PackageLicenseExpression` (`Apache-2.0`). README má krátkou sekci Licence.
- **Sdílené texty:** `Tray/TrayText` (názvy stavů a zkracování textu) používá tooltip i menu; `TrayToolTipFormatter` se tím jen zjednodušil, chování je beze změny.
- **Platformy:**
  - Windows: menu vykresluje Avalonia (`TrayPopupRoot`), `_` je access key, tooltipy položek fungují.
  - macOS: nativní NSMenu přes Avalonia.Native, `open` pro prohlížeč. **Na macOS nic neověřeno** (ověří T-018).
- **Ověření na Windows 11:**
  - **Uživatel ručně ověřil:** menu, okno O aplikaci (odkaz na GitHub, zavření okna aplikaci neukončí) a „Ukončit“. Vše funguje.
  - `dotnet run` s dočasným `settings.json` (3 tiskárny: PrusaLink na nedosažitelné testovací adrese `192.0.2.10` s názvem `MK4_test`, PrusaLink s neplatnou adresou `ftp://printer`, Prusa Connect). Aplikace se spustila a běžela několik minut. Tooltip ikony přes UI Automation: „Prusa Tray Diagnostic MK4_test / Offline / Other printers: 2“. Po ověření byl proces ukončen a dočasná složka `%APPDATA%\PrusaTrayDiagnostic` smazána.
  - Původně (před ručním ověřením uživatelem): samotné menu se zobrazit nepodařilo. Automatizované pravé kliknutí na ikonu ve skrytých ikonách netrefilo (flyout se mezitím přesunul) a otevřelo kontextové menu v okně Microsoft Edge; zavřel jsem ho klávesou Esc. Automatizaci myši a klávesnice jsem potom ukončil a už se nepoužívá. Vzhled menu, escapování `_`, otevření prohlížeče, okno O aplikaci a kliknutí na „Ukončit“ bylo proto potřeba ručně zkontrolovat; to uživatel udělal.
  - Po obou kolech code review: `dotnet run` bez nastavení, bez jakéhokoli klikání. Aplikace se spustila a běžela cca 35 s (stejné PID, paměť stabilní ~98 MB), pak byl proces ukončen. Tooltip se přes UI Automation tentokrát přečíst nepodařilo, protože ikona je ve skrytých ikonách a flyout se bez kliknutí neotevře. Okno O aplikaci, odkaz na GitHub a menu ověřil ručně uživatel (viz výše).
- **Testy (`tests/PrusaTrayDiagnostic.App.Tests`):**
  - `Tray/Menu/TrayMenuViewModelTests`: úvodní stav, pevné položky CZ/EN, pořadí tiskáren, URL podle `ConnectionType`, neplatná adresa, aktualizace jen jedné položky, žádná aktualizace bez viditelné změny, sloučení dávky, změna tam a zpět, minimální změny při nové konfiguraci, selhání monitoringu, dlouhý název, otevření webu, chování po `Dispose`, souběžné snapshoty, zatímco UI vlákno průběžně aplikuje změny (žádná ztracená aktualizace, progress každé tiskárny jen roste).
  - `Tray/Menu/TrayMenuPresenterTests` (fake cíl): sestavení, aktualizace jen dané položky, směrování kliknutí, zakázané položky, chyba příkazu, odhlášení při `Dispose`.
  - `Tray/Menu/PrinterWebAddressTests`, `Tray/Menu/TrayMenuFormatterTests`, `Tray/TrayTextTests`, `Platform/ProcessUrlLauncherTests` (start info pro každý OS, odmítnutí `file:`/`ftp:`/přihlašovacích údajů, selhání startu, uvolnění handle; žádný proces se nespouští).
  - `Tray/QuitCoordinatorTests`: pořadí stop → shutdown, jen jednou, selhání (log Error s výjimkou) a zamrznutí zastavení (log Warning s limitem), ukončení se skutečnou `MonitoringSession` (zruší token monitoru), logger, který hází výjimky, ukončení nezastaví.
  - `Tray/MonitoringSessionTests`: všichni pozorovatelé dostanou tiskárny i chybu.
  - `Tray/Menu/TrayMenuTargetTests` (fake `INativeTrayMenu`): oddělovač za tiskárnami a index tiskárny = index v menu, kliknutí po aktualizaci použije novou položku (nová adresa), zápis jen změněných vlastností, escapování `_`, odebraná položka nehlásí kliknutí, opakované `SetFixedItems`.
  - `Windows/AboutViewModelTests`: texty CZ/EN (upozornění, licence), copyright z atributu sestavení i záloha (dynamické sestavení bez atributu), odkaz na GitHub přes `IUrlLauncher`, verze.
  - `Composition/AppServicesTests`: předaná logger factory se používá a uvolnění kontejneru ji neuvolní.
  - Testy nic nevytvářejí na disku ani nespouštějí procesy.

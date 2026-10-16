---
id: T-007
title: Tray ikona se stavem a progressem
status: done
owner: ui-dev
priority: P1
platform: all
depends_on: [T-006]
---

## Cíl
Ikona v liště (Windows) / menu baru (macOS) ukazuje stav tisku na první pohled.

Tento úkol pokrývá společnou část (Avalonia `TrayIcon`, mapování stavu na ikonu, tooltip) a barevnou sadu ikon pro Windows. Monochromatická „template“ ikona pro menu bar na macOS je samostatný úkol T-018.

## Rozsah
- Různé ikony/barvy podle `PrinterState` (idle, tiskne, pauza, hotovo, chyba, offline)
- Mapování stavu na ikonu oddělené od konkrétní sady ikon, aby šlo na macOS dodat jinou sadu (T-018)
- Tooltip: název tiskárny, stav, progress %, zbývající čas, název souboru
- Případně vykreslení progressu přímo do ikony

## Akceptační kritéria
- [x] Ikona se mění podle stavu z monitorovací služby (řetězec monitor → view model → ikona je pokrytý testy; naživo ověřen jen stav „bez tiskárny“, skutečná tiskárna nebyla k dispozici)
- [x] Mapování stavu na ikonu je pokryté testy
- [x] Ověřeno na Windows (oznamovací oblast; ikona je ve skrytých ikonách, viz poznámky)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
Všechny otázky jsou rozhodnuté uživatelem:
- ~~Vizuální styl ikon?~~ → Vlastní neutrální symbol, žádné logo ani ochranná známka Prusa. Barevná tečka podle stavu, při tisku a pauze kroužek s progressem kreslený za běhu. Barvy: nečinná šedá, tisk modrá + oblouk, pauza žlutá + oblouk, hotovo zelená, chyba červená, vyžaduje zásah oranžová, offline šedý přeškrtnutý prázdný kroužek.
- ~~Oddělení mapování od sady ikon?~~ → Rozhraní `ITrayIconSet`, aby T-018 mohl dodat template ikony pro macOS.
- ~~Jazyk UI?~~ → Čeština + angličtina přes `.resx` (silně typovaný přístup, kultura podle systému, záložní angličtina). Infrastruktura je zavedená v tomto úkolu.
- ~~Více tiskáren (T-013)?~~ → Jedna agregovaná ikona. Pořadí závažnosti: chyba > vyžaduje zásah > offline > pauza > tisk > hotovo > nečinná.
- ~~`ISecretStore` v composition rootu?~~ → Windows → `WindowsCredentialSecretStore`, ostatní OS → úložiště hlásící „nepodporováno“ (hesla nedostupná, aplikace běží dál).
- ~~Stavy mimo zadaný seznam?~~ → Návrh přijat:
  - `Busy` je modrá tečka bez oblouku, v závažnosti mezi „hotovo“ a „tisk“.
  - `Stopped` je šedá jako nečinná tiskárna, tooltip ukazuje „Zastaveno“.
  - `Unknown` má ikonu offline.
  - `Unauthorized` má oranžovou ikonu „vyžaduje zásah“.
- ~~Oblouk progressu i při pauze?~~ → Ano, žlutý kroužek s progressem.
- ~~Ikona bez dat / bez nastavené tiskárny?~~ → Zůstává ikona offline. Stavy rozliší tooltip („Spouštění…“, „Připojování…“, „Není nastavena žádná tiskárna“).
- ~~Vzhled ikony offline?~~ → Ponechat.
- ~~Texty CZ/EN a tooltip pro více tiskáren před T-013 („Další tiskárny: N“)?~~ → Přijato.
- ~~Logování?~~ → Mimo T-007. Logování do souboru s rotací bude samostatný úkol; do té doby je `AddLogging()` bez providera.
- ~~Pád Native AOT buildu při startu?~~ → Mimo T-007. Chyba existovala už před tímto úkolem a opraví se v samostatné větvi hned po T-007.

## Poznámky k implementaci
- **Struktura (`src/PrusaTrayDiagnostic.App`):**
  - `Tray/TrayStatus` je vizuální stav ikony. `TrayStatusRules` mapuje `PrinterState` → `TrayStatus`, určuje závažnost a vybírá primární tiskárnu: nejzávažnější, při shodě první v konfiguraci. T-013 na to může navázat.
  - `Tray/TrayIconKey` je klíč ikony. Progress se zaokrouhluje dolů na 5 % a používá se jen při tisku a pauze. Ikon je proto nejvýš 48 a dají se cachovat.
  - `Tray/ToolTipContent` obsahuje přesně ty hodnoty, které tooltip ukáže, už zaokrouhlené: stav, celé %, minuty, soubor, počet dalších tiskáren. Sestaví se levně, bez formátování a bez přístupu k resources.
  - `Tray/TrayPresentationKey` (ikona + druh zprávy + `ToolTipContent`) slouží k levnému porovnání při každém dotazu na tiskárnu.
  - `Tray/TrayToolTipFormatter` z `ToolTipContent` skládá text tooltipu: název, stav (– progress %), zbývající čas, soubor, „Další tiskárny: N“.
    - Délka je omezená na 127 znaků (limit `szTip` ve Windows). Nejdřív se zkracuje název souboru, rozdělené dvojice surrogate znaků se nevytvářejí.
    - Progress se zaokrouhluje dolů, takže nedokončený tisk nikdy neukáže 100 %.
    - Zbývající čas se zaokrouhluje nahoru na minuty. Když ho tiskárna nehlásí, použije se `PrintJob.EstimatedRemaining`.
    - Data úlohy se u nedostupné tiskárny nezobrazují.
  - `Tray/TrayStatusViewModel` (CommunityToolkit `ObservableObject`, rozhraní `ITrayStatusSource`) odebírá `IPrinterMonitor.SnapshotUpdated`.
    - Pod zámkem jen přepočítá `TrayPresentationKey`. Když se klíč nezměnil (např. změna jiné než primární tiskárny, časové razítko, telemetrie), nic dalšího se neděje.
    - Jinak naformátuje text mimo zámek a publikuje jen nejnovější verzi; zastaralý výsledek zahodí.
    - Na UI vlákno (`IUiDispatcher`) posílá jen změnu a dávku snapshotů sloučí do jedné UI aktualizace.
    - Ignoruje snapshoty tiskáren mimo konfiguraci. Selhání monitoringu zobrazí jako chybu.
  - `Tray/Icons`:
    - `ITrayIconSet` je oddělená sada ikon.
    - `ColorTrayIconPalette` popisuje vzhled (tvar, barvy, úhel oblouku) bez Avalonie.
    - `TrayIconRenderer` kreslí 32×32 px přes `RenderTargetBitmap` a `DrawingContext` (bez reflexe, AOT-safe). Stopa kroužku je neutrální šedá, takže pauza (žlutá) se při 16 px neplete s oranžovou „vyžaduje zásah“. Bitmapa se po vytvoření `WindowIcon` hned uvolní.
    - `TrayIconCache` vykreslí každý klíč jen jednou.
  - `Tray/TrayIconPresenter<TIcon>` přenáší `ITrayStatusSource` na `ITrayIconTarget<TIcon>`. V aplikaci je cílem `AvaloniaTrayIconTarget` nad Avalonia `TrayIcon`; generický typ umožňuje testy bez Avalonia platformy. Když vykreslení selže, zůstane předchozí ikona (na začátku statická `Assets/tray-idle.png` z `App.axaml`) a chyba se zaloguje.
  - `Tray/MonitoringSession`:
    - Na pozadí načte nastavení a spustí `IPrinterMonitor.RunAsync`.
    - Když nastavení není dostupné, použije výchozí. Pád monitoringu ukáže v tray.
    - `StopAsync(timeout)` čeká nejvýš zadaný čas (při „Ukončit“ 3 s). `RequestStop()` (ukončení ze strany OS) nečeká a nic neloguje; nulový timeout také nic neloguje.
    - `DisposeAsync` po už vyžádaném zastavení znovu nečeká, takže ukončení trvá nejvýš 3 s, ne 2× 3 s.
  - `Composition/AppServices` je composition root s explicitními factory registracemi (Native AOT). Objekty jen pro UI vlákno (ikony) v DI kontejneru nejsou, protože ten se může uvolňovat mimo UI vlákno.
  - `Composition/PrinterClientFactory` vrací pro PrusaLink `PrusaLinkClientFactory`, pro Prusa Connect hází `NotSupportedException` (T-005 je odložený).
  - `Composition/TrayHost` vlastní služby, session, sadu ikon a presenter. „Ukončit“ nejdřív synchronně na UI vlákně uvolní presenter a sadu ikon, pak zastaví monitoring a uvolní služby a nakonec ukončí aplikaci.
  - `Platform/UnsupportedSecretStore` hlásí každou operaci jako `SecretStoreException` („nepodporováno“).
- **Lokalizace:** `Resources/Strings.resx` (angličtina, neutrální jazyk `en`) a `Resources/Strings.cs.resx`.
  - Silně typovanou třídu `Strings` generuje MSBuild (`StronglyTypedLanguage`) do `obj/`, takže v repu není žádný designer soubor.
  - Třída je `public`, protože `{x:Static}` v kompilovaném XAML neumí interní členy. Kvůli testům typu `[Theory]` je veřejný i výčet `TrayStatus`.
  - Položka menu „Ukončit/Quit“ je přeložená. Kultura se řídí systémovým jazykem UI, pro jiné jazyky se použije angličtina.
- **Platformy:**
  - Windows: barevná sada ikon. Ověřeno.
  - macOS: zatím se použije stejná barevná sada a `UnsupportedSecretStore`. Template ikony doplní T-018, Keychain T-021. **Na macOS nic neověřeno.**
- **Ověření na Windows 11 (2560×1440):**
  - `dotnet run` bez nastavené tiskárny: aplikace se spustila a běžela ~5 min (stejné PID, paměť stabilní ~97 MB). Nevytvořila žádný soubor nastavení. Po opravách z code review spuštěno znovu se stejným výsledkem.
  - Windows 11 dává nové ikony do skryté oblasti (šipka ^). Po otevření přes UI Automation byla ikona vidět: šedý přeškrtnutý kroužek. Tooltip (UIA název) zněl „Prusa Tray Diagnostic / No printer configured“; systém má anglické UI.
  - Celá sada ikon (všechny stavy, progress tisku i pauzy) byla navíc vykreslená mimo repo a zkontrolovaná na světlém i tmavém pozadí při 16, 20 a 32 px.
  - Neověřeno: změna ikony naživo se skutečnou tiskárnou a kliknutí na „Ukončit“ v menu (ověřeno jen testy a ukončením procesu).
- **Testy (`tests/PrusaTrayDiagnostic.App.Tests`):**
  - `Tray/*`: mapování, závažnost, klíč ikony včetně pauzy, paleta, cache, tooltip CZ/EN, `TrayPresentationKey` (neviditelné změny klíč nemění), view model včetně souběhu.
  - Presenter s fake cílem: rozlišení podle jména vlastnosti včetně `null`/„“, zachování ikony při chybě vykreslení, odhlášení při `Dispose`.
  - Session: nečeká podruhé po vypršení timeoutu, `RequestStop` bez logu.
  - `Localization/StringsTests`: úplnost češtiny, shoda placeholderů, záložní angličtina.
  - `Platform/UnsupportedSecretStoreTests`.
  - `Composition/*`: registrace, výběr `ISecretStore` podle OS, směrování klientů.
  - Testy na disku ani v Credential Manageru nic nevytvářejí.

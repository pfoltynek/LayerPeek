---
id: T-009
title: Detekce událostí tisku
status: done
owner: core-dev
priority: P1
platform: all
depends_on: [T-006]
---

## Cíl
Z posloupnosti snapshotů odvodit události, na které se posílají notifikace.

## Rozsah
- Události: tisk zahájen, dokončen, pozastaven, obnoven, zrušen, chyba, vyžaduje pozornost (např. došel filament), tiskárna offline/online
- Deduplikace (stejná událost se neposílá opakovaně)
- Abstrakce `INotificationService` v Core (implementace v App – T-010 Windows, T-020 macOS)

## Akceptační kritéria
- [x] Tabulkové testy přechodů stavů
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Upozornit i na „zbývá X minut do konce“? Pokud ano, jaký práh?~~ **Vyřešeno (2026-10-16):** Ano – upozornění „zbývá X minut“, jedno na tisk, práh nastavitelný (výchozí 10 min, 0 = vypnuto). Implementováno jako `NotificationSettings.RemainingTimeMinutes`.

Rozhodnutí uživatele (2026-10-16), všechna implementovaná:
1. ~~Stav při spuštění aplikace?~~ → Chyba a „vyžaduje pozornost“, které trvají už při spuštění, se **hlásí**. Ostatní stavy (běžící tisk, offline…) při spuštění zůstávají tiché. Po uložení nastavení (restart monitoringu) se stejná trvající chyba znovu nehlásí: notifikátor si stav tiskáren, které zůstaly v konfiguraci se stejnou adresou a typem připojení, ponechá.
2. ~~Zpoždění offline?~~ → 1 minuta, pevně (nenastavitelné). Dvojice offline/online pro každý výpadek je v pořádku.
3. ~~Odmítnuté přihlašovací údaje?~~ → Ano, hlásí se jednou (nová událost `CredentialsRejected`, závažnost `ActionRequired`), znovu až poté, co tiskárna mezitím údaje přijala. Řídí se vypínačem `Attention` („tiskárna potřebuje zásah“). Hlásí se i při spuštění, stejně jako chyba a „vyžaduje pozornost“.
4. ~~Pravidla „zbývá X minut“?~~ → Přijata, jak jsou implementována (krátký tisk se nehlásí, během pauzy nic, při chybějícím odhadu tiskárny se použije lineární odhad).
5. ~~Zvuk?~~ → Jedno nastavení se třemi hodnotami `Off` / `ImportantOnly` / `All`, výchozí `ImportantOnly` (důležité = chyba, „vyžaduje pozornost“, odmítnuté údaje). Je v `NotificationSettings.Sound` včetně uložení; T-010 jen použije `PrintNotification.PlaySound`.

Otevřené otázky nejsou.

## Poznámky k implementaci
- **Model (Core):**
  - `Events/PrintEventKind`: `PrintStarted`, `PrintFinished`, `PrintPaused`, `PrintResumed`, `PrintStopped`, `Error`, `AttentionRequired`, `PrinterOffline`, `PrinterOnline`, `RemainingTime`, `CredentialsRejected`.
  - `Events/PrintEvent` (`readonly record struct`): druh, id tiskárny, čas snapshotu, název souboru, zbývající čas.
  - `Printers/PrintJob.Id` (nové, `long?`): PrusaLink plní z `job.id`. Podle id se rozliší dvě úlohy se stejným souborem. Když id chybí, porovnává se název souboru. Neznámá identita = stejná úloha, takže chybějící data nevyrobí falešný „nový tisk“.
- **Detektor (`Events/PrintEventDetector`):** čistý stavový automat bez I/O, stav každé tiskárny v malé třídě ve slovníku (id bez ohledu na velikost písmen).
  - První snapshot tiskárny je výchozí stav. Z něj se hlásí jen chyba, „vyžaduje pozornost“ a odmítnuté údaje. `Busy` nebo „vyžaduje pozornost“ s úlohou znamená rozběhnutý tisk, takže následný tisk téže úlohy není „zahájen“.
  - Přechody se porovnávají s posledním *dosažitelným* stavem, takže výpadek mezi nimi událost nezopakuje ani neskryje (např. tisk → offline → dokončeno = „dokončen“).
  - Tisk:
    - Zahájen při tisku/pauze bez běžící úlohy.
    - Když přijde jiná úloha (v jakémkoli stavu, i `Busy`/`Attention`/`Error`), stará úloha končí včetně stavu upozornění „zbývá X minut“. Následný tisk nové úlohy je proto „zahájen“, ne „obnoven“.
    - Obnoven jen z pauzy nebo z „vyžaduje pozornost“ během tisku (z `Error` ne).
    - `Busy` během úlohy nebo po chybě či „vyžaduje pozornost“ je průhledný: nepřepíše pauzu, čekání ani chybu. Pauza → busy → tisk je tedy „obnoven“, pauza → busy → pauza ani chyba → busy → chyba nic nezopakují.
    - Dokončen/zrušen při vstupu do stavu, nebo když se ve stejném stavu změní úloha (tisk proběhl celý mezi dvěma dotazy). `Idle` úlohu zapomene bez události.
  - Chyba a „vyžaduje pozornost“ se hlásí při vstupu do stavu.
  - `Unauthorized` → `CredentialsRejected` jednou. Znovu se hlásí až po dosažitelném snapshotu, offline mezi tím nic nenuluje. Nekončí výpadek („online“ se nepošle). `Unknown` a nedefinované hodnoty se přeskakují.
  - Offline se měří mezi časy snapshotů. Když čas skočí zpět, počítá se od dřívějšího času, aby se hlášení neodsunulo.
  - `UpdateOptions` a `RetainPrinters` umožní změnit práh a zapomenout odebrané tiskárny bez ztráty stavu ostatních.
  - Bez alokace v ustáleném stavu (ověřeno testem): pole událostí vzniká jen při události, žádné LINQ. Není thread-safe, volající serializuje.
- **Notifikace (`Notifications/*`):**
  - `INotificationService.Show(PrintNotification)` se volá z vláken na pozadí. Implementace musí být thread-safe a rychlá a musí snést volání během ukončování aplikace (zdokumentováno na rozhraní).
  - `PrintNotification` nese:
    - celou `PrinterDefinition` (název; adresa pro otevření webu po kliknutí v T-010),
    - druh, čas, soubor a zbývající čas,
    - `Severity` (`SeverityOf`: `ActionRequired` = chyba, pozornost, odmítnuté údaje; `Warning` = offline; ostatní `Information`),
    - `PlaySound` podle nastavení.
    - Texty se nelokalizují v Core.
  - `NullNotificationService` (Core, platformně neutrální) notifikace zahodí.
  - `PrintEventNotifier` odebírá `IPrinterMonitor.SnapshotUpdated`.
    - Detekce běží pod zámkem (monitor volá tiskárny souběžně, každou postupně), služba se volá mimo zámek.
    - Před každým `Show` se pod zámkem znovu ověří, že notifikátor není uvolněný. Zbylé okno souběhu s `Dispose` musí snést služba.
    - Výjimku služby zaloguje (Error) a pokračuje, monitoring ani další notifikace neovlivní.
    - `Configure` (každý běh) ponechá stav tiskáren, které zůstaly v konfiguraci, a nastaví nový práh. Odebrané tiskárny zapomene, takže po opětovném přidání začnou od výchozího stavu. Totéž platí pro tiskárnu, které se změnila adresa nebo typ připojení (může to být jiný stroj, jeho běžící tisk tedy není „zahájen“). Přejmenování stav zachová.
    - Snapshoty nenastavených tiskáren a snapshoty před `Configure` / po `Dispose` ignoruje.
- **Nastavení (`NotificationSettings`):**
  - `RemainingTimeMinutes` (výchozí 10, 0 = vypnuto, max. 1440 = den).
  - `Sound` (`NotificationSound.Off` / `ImportantOnly` / `All`, výchozí `ImportantOnly`) a `ShouldPlaySound(NotificationSeverity)`.
  - `IsEnabledFor(PrintEventKind)`: hlavní vypínač + vypínač události. `Offline` platí pro offline i online, `Attention` i pro odmítnuté údaje.
  - V `settings.json` jsou `notifications.remainingTimeMinutes` a `notifications.sound` (název hodnoty, např. `"ImportantOnly"`), přes zdrojově generovaný `SettingsJsonContext` (AOT).
  - Načítání je shovívavé:
    - Chybějící hodnota = výchozí.
    - Minuty: mimo rozsah se oříznou, desetinné číslo se zaokrouhlí, s varováním.
    - Zvuk: název bez ohledu na velikost písmen; neznámý název = výchozí s varováním. Číslo místo textu je chyba typu JSON, celý soubor se pak bere jako poškozený, stejně jako u ostatních textových hodnot.
- **App:**
  - `IMonitoringObserver.SetPrinters` je nahrazené `ApplySettings(AppSettings)`, aby pozorovatel dostal i nastavení notifikací. Ikona a menu ho implementují explicitně a volají své `SetPrinters`.
  - `Notifications/PrintNotificationObserver` předá tiskárny a nastavení notifikátoru. Selhání monitoringu nehlásí (ukazuje ho tray).
  - `Composition/AppServices` registruje `INotificationService` = `NullNotificationService` pro všechny OS (T-010/T-020 doplní výběr podle OS), `PrintEventNotifier` a pozorovatele v `MonitoringSession`. Řetězec monitor → detektor → služba tedy běží.
- **Testy:**
  - `Core.Tests/Events/PrintEventDetectorTests`: tabulkové `[Theory]` scénáře ve tvaru „Idle,Printing:a,…“:
    - výchozí stav a problémy při spuštění, zahájení, identita úlohy (včetně změny úlohy v `Busy`/`Attention`/`Error`), pauza/obnovení (včetně průhledného `Busy`), konec, chyba/pozornost,
    - Unknown, odmítnuté údaje,
    - offline/online se zpožděním,
    - zbývající čas (kolísání, práh 0, změna úlohy, pauza, výpadek, nová úloha po `Busy`/`Attention`).
    - Dále obsah událostí, nezávislost tiskáren, čas jdoucí zpět (offline se nahlásí jedno zpoždění po posunu), `UpdateOptions`, `RetainPrinters`, nulová alokace.
  - `Core.Tests/Notifications/PrintEventNotifierTests`:
    - filtrování podle nastavení, práh z nastavení, `PlaySound` podle nastavení zvuku, odmítnuté údaje pod vypínačem `Attention`,
    - házející služba (log + pokračování), ignorované snapshoty,
    - zachování stavu po `Configure` (trvající chyba se nezopakuje), nový výchozí stav pro znovu přidanou tiskárnu,
    - `Dispose` (i uprostřed snapshotu se dvěma událostmi), souběh 16 tiskáren, mapování závažnosti.
  - `Core.Tests/Settings/NotificationSettingsTests` a `SettingsMapperTests` (v paměti, bez disku; Core má kvůli tomu `InternalsVisibleTo` pro Core.Tests): práh, zvuk, `ShouldPlaySound`, vypínače událostí, uložení a shovívavé načtení.
  - PrusaLink: `PrintJob.Id` u tisku, další úlohy, dokončení a zrušení.
  - App:
    - registrace v `AppServicesTests`, `PrintNotificationObserverTests`, `MonitoringSessionTests` (pozorovatel dostane celé nastavení),
    - `TrayStatusViewModelTests` a `TrayMenuViewModelTests`: `IMonitoringObserver.ApplySettings` zobrazí tiskárny a zruší předchozí selhání.
  - Žádná síť, žádné soubory, žádné procesy.
- **Native AOT:** bez reflexe a bez nových balíčků. JSON přes existující source-generated kontext, DI explicitně přes factory.

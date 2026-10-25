---
id: T-080
title: Vychladnutí – volitelná notifikace „vychladlo, můžete vypnout“ a nastavení prahu
status: in-progress
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-076, T-079]
---

## Cíl
- Nový typ systémové notifikace „Tiskárna vychladla – můžete ji vypnout“. Je volitelný, přepínač je v nastavení upozornění.
- V nastavení tiskárny pole pro práh vychladnutí trysky (výchozí 50 °C, T-079) a akce „Použít práh u všech tiskáren“.

## Kontext
- Notifikace:
  - `Core/Events/PrintEventKind`,
  - `Core/Settings/NotificationSettings` (přepínače + `IsEnabledFor`),
  - `Core/Notifications/PrintEventNotifier` (volá `INotificationService`),
  - Windows toast: `App/Notifications/ToastContentBuilder` + `ToastNotificationService` (T-010),
  - zvuk podle `NotificationSettings.Sound` a `NotificationSeverity`,
  - kliknutí na toast otevře web tiskárny (`ToastActivationRouter`).
- Událost vychladnutí poskytuje `CooldownMonitor` z T-079. Přijde jednou na úlohu a nepřijde, když tisk skončil už pod prahem.
- Texty a umístění přepínače a pole prahu navrhuje T-076 (body 8 a 9).

## Rozhodnutí (uživatel, 2026-10-24)
- **Výchozí stav (Q1):** notifikace je ve výchozím stavu **vypnutá**.
- **Zvuk (Q2, Q4):** notifikace je **informační** (`NotificationSeverity` jiná než `ActionRequired`), takže ve výchozím nastavení zvuku „jen důležité“ nehraje. Při nastavení zvuku „všechny“ zazní jako ostatní informační notifikace (Q4).
- **macOS (Q3):** přijde automaticky s T-020 přes `INotificationService`, bez zvláštní práce.
- **Práh (T-079):** je **u každé tiskárny**, 30–100 °C, výchozí 50 °C, s akcí „Použít práh u všech tiskáren“.

## Rozhodnutí (uživatel, 2026-10-24, po dokončení T-079)
- **Rozhodnutí 12 – výsledek tisku v textu:** text notifikace uvádí, jak tisk skončil, např. „CoreOne: tisk dokončen, tiskárna vychladla“ nebo „CoreOne: tisk zastaven, tiskárna vychladla“. Výsledek nese `CooldownEvent.Outcome` (`CooldownPrintOutcome.Finished` / `Stopped`, doplněno v T-079, větev `feature/widget-core-followups`). Přesné znění ve všech jazycích navrhne ui-dev podle T-076.
- **Vykání:** texty vykají („Tiskárna vychladla – můžete ji vypnout“), stejně jako zbytek české UI.
- **Tiskárna vychladlá už při startu (rozhodnutí 11):** pro tiskárnu, která už při startu aplikace ukazuje skončený tisk, Core chladnutí nesleduje, takže notifikace nepřijde.

## Rozsah
### Core (core-dev)
- `PrintEventKind.CooledDown`: nová hodnota na konci výčtu, číselné hodnoty existujících se nemění.
- `NotificationSettings.CooledDown` (výchozí `false`) a `IsEnabledFor`.
- Uložení v `settings.json` (`notifications.cooledDown`). Výchozí hodnota se nezapisuje, starý soubor se načte s výchozí hodnotou.
- Napojení události z `CooldownMonitor` na `INotificationService`, buď přes `PrintEventNotifier`, nebo samostatný malý notifikátor. Logika filtrování a výběru zvuku (`ShouldPlaySound`) se **neduplikuje**. Závažnost je informační.

### App (ui-dev)
- Toast „vychladlo“ (`ToastContentBuilder`): titulek s názvem tiskárny podle stávajících pravidel, tělo z `Strings`, kliknutí jako u ostatních toastů.
- Okno nastavení:
  - přepínač „Tiskárna vychladla“ mezi ostatními upozorněními (výchozí vypnuto),
  - číselné pole prahu (°C) **u tiskárny** podle návrhu T-076, validace 30–100 se zprávou jako u ostatních číselných polí,
  - tlačítko „Použít u všech tiskáren“ (přenese práh do ostatních tiskáren v rozpracovaném nastavení; uloží se s ostatními změnami),
  - bez posuvníku ve všech 8 jazycích.
- Všechny nové texty ve všech 8 `Strings.*.resx`, přepnutí jazyka za běhu.
- Testovací režim (T-067): pokud je to levné, testovací příkaz pro vyvolání toastu „vychladlo“, aby ho mohl ověřit E2E (T-093).

## Akceptační kritéria
- [x] Core (testy v `LayerPeek.Core.Tests`):
  - nový druh události, přepínač (výchozí vypnuto), `IsEnabledFor`,
  - uložení, načtení a starý soubor,
  - notifikace jde jen při zapnutém hlavním přepínači i přepínači vychladnutí, jednou na úlohu,
  - tisk skončený pod prahem → žádná notifikace,
  - při nastavení zvuku „jen důležité“ bez zvuku,
  - při nastavení zvuku „všechny“ se zvukem, stejně jako ostatní informační notifikace.
- [x] App.Tests: obsah toastu (en, cs, fr) pro dokončený i zastavený tisk, zvuk podle nastavení
- [ ] Headless UI testy (`LayerPeek.App.UiTests`):
  - [x] přepínač „Tiskárna vychladla“: výchozí hodnota, uložení kliknutím, hlavní přepínač, pořadí Tab, `AutomationId`, `HelpText`, bez posuvníku ve všech 8 jazycích,
  - [ ] pole prahu: výchozí hodnoty, uložení, hodnoty mimo 30–100 – **čeká na otázku Q1** (logika je hotová a otestovaná v App.Tests, pole v okně zatím není),
  - [ ] „Použít u všech tiskáren“ v okně – **čeká na Q1** (logika hotová v App.Tests),
  - [ ] podzáložka Překryv s polem prahu bez posuvníku ve všech 8 jazycích – **čeká na Q1**.
- [x] Test přepnutí jazyka pro nové texty; `StringsTests` (nezlomitelná mezera před „°C“) projde
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – kam teď dát pole prahu (blokuje dokončení):** podle návrhu T-076 (bod 8) patří práh do pravého sloupce podzáložky Překryv. Návrh ale počítá s novou podobou podzáložky z T-092 (vlevo 5 widgetů, 232 DIP; vpravo 292 DIP). Dnes je na podzáložce ještě tabulka T-036 (9 řádků, roh, 2 sloupce). Ta zabírá skoro celou výšku a pravý sloupec má jen asi 120–160 DIP.
  - Měřeno headless testem `SettingsWindowLayoutTests` s polem prahu v pravém sloupci nad nápovědami T-036: obsah přeteče o 9 DIP (en, kamera vypnutá) až o 251 DIP (es). Přeteče ve všech 8 jazycích, takže by se ukázal posuvník.
  - Možnosti:
    - **A (doporučeno):** pole prahu se přidá až po T-092, buď jako dokončení T-080 hned po sloučení T-092, nebo přímo v T-092. Na podzáložce už pak bude místo podle návrhu. Pohled je hotový a otestovaný: `PrinterEditorViewModel.CooldownThresholdCelsius`, `CooldownThresholdError`, `ShowsCooldownThresholdHint`, `ApplyCooldownThresholdToAllCommand`, `ApplyToAllUnavailableReason`, `ShowsOverlayErrorMarker`, `OverlayTabName`. Zbývá jen XAML a headless testy. Do té doby se práh nedá v okně změnit, platí uložený (výchozí 50 °C).
    - **B:** přestavět podzáložku Překryv na dva sloupce už v T-080. To je část rozsahu T-092 a vznikne kolize s T-081, který na podzáložce pracuje souběžně.
    - **C:** dočasné jiné umístění nebo zhuštěná varianta, např. nápověda jen jako `HelpText` / tooltip. To je odchylka od schváleného návrhu.

## Poznámky k implementaci
- 2026-10-25, ui-dev: hotová Core část, notifikace, toast, přepínač v záložce Obecné a logika prahu v pohledu. **Zbývá jen pole prahu v okně (Q1)**, proto úkol zůstává v `in-progress`.
- **Core:**
  - `PrintEventKind.CooledDown` je na konci výčtu (12), ostatní hodnoty se nemění.
  - `NotificationSettings.CooledDown` (výchozí `false`), `IsEnabledFor` (hlavní přepínač + vlastní přepínač).
  - V `settings.json` je to `notifications.cooledDown`. Zapisuje se jen `true` (`WhenWritingNull`), starý soubor dá `false`. JSON jde přes stávající `SettingsJsonContext` (source generator).
  - Nový malý `Core/Notifications/CooldownNotifier`. Odebírá `CooldownMonitor.CooledDown`, filtruje přes `NotificationSettings.IsEnabledFor` a zvuk bere z `ShouldPlaySound(SeverityOf(CooledDown))`, takže se logika neduplikuje. Závažnost je `Information` (bez zvuku při „jen důležité“, se zvukem při „všechny“). Výjimky služby notifikací se jen zalogují. Logika „jednou na úlohu“, „konec nebyl vidět“ a „skončil pod prahem“ zůstává v `CooldownMonitor` (T-079), notifikátor ji nepřebírá.
  - `CooldownEvent` má navíc `NozzleCelsius` a `ThresholdCelsius` (teplota, která vychladnutí potvrdila, a práh) pro text toastu. `PrintNotification.Cooldown` je nese do služby notifikací.
- **App:**
  - `Notifications/CooldownNotificationObserver` je v `MonitoringSession` zaregistrovaný explicitně **před** `CooldownObserver`. Zvýšený práh může tiskárnu „vychladit“ hned při konfiguraci a notifikace musí mít už nové nastavení.
  - Toast (`ToastContentBuilder`):
    - titulek je název tiskárny,
    - 2. řádek je výsledek tisku a vychladnutí (rozhodnutí 12): cs „Tisk dokončen, tiskárna vychladla“ / „Tisk zastaven, tiskárna vychladla“, en „Print finished, printer cooled down“ / „Print stopped, printer cooled down“,
    - 3. řádek místo názvu souboru: cs „Můžete tiskárnu vypnout. Tryska 47 °C, práh 50 °C“, en „Safe to turn off. Nozzle 47 °C, threshold 50 °C“. Teplota je zaokrouhlená na celé stupně, před °C je nezlomitelná mezera.
    - Bez údajů o vychladnutí jen „Tiskárna vychladla“.
    - Kliknutí a zvuk jako u ostatních toastů. Klíče `NotificationCooledDownFinished` / `NotificationCooledDownStopped` doplňují klíče z návrhu T-076, protože rozhodnutí 12 přišlo po návrhu. `NotificationCooledDown` je text bez výsledku.
  - Přepínač „Tiskárna vychladla“ je v záložce Obecné, v levém sloupci za „Tisk zastaven (zrušen)“ (`SettingsNotifyCooledDown`, `HelpText` z `SettingsNotifyCooledDownHelp`). Záložka se vejde bez posuvníku ve všech 8 jazycích. Snímky `settings-general*.png` jsou přegenerované.
  - Práh v pohledu (`PrinterEditorViewModel.Cooldown.cs`):
    - celé číslo 30–100, jinak chyba „Zadejte celé číslo od 30 do 100.“ místo nápovědy,
    - neplatná hodnota blokuje uložení a okno přepne na podzáložku Překryv s „!“,
    - nikdy se neuloží,
    - práh nezávisí na kameře.
    - „Použít práh u všech tiskáren“ (`SettingsViewModel.ApplyCooldownThresholdToAll`) zkopíruje platný práh do ostatních tiskáren v rozpracovaném nastavení (i neuložených) a ukáže stavový text „Práh se zkopíroval do ostatních tiskáren. Projeví se po uložení.“. Uloží se s ostatními změnami. Při jediné tiskárně je akce zakázaná s důvodem „Je nastavená jen jedna tiskárna.“ (`SettingsOverlayApplyAllSingle`, klíč z T-092 – T-092 ho má použít a nepřidávat znovu).
  - Texty: 13 nových klíčů ve všech 8 `Strings.*.resx`. Notifikační jsou za `NotificationStatusChanged`, nastavení za `SettingsNotifyOffline`, mimo klíče podzáložky Překryv, aby nekolidovaly s T-081 / T-092. Ostatní jazyky (de, es, fr, it, pl, sk) navrhl ui-dev, vykání podle zvyklostí jazyka.
- **Testovací režim (T-067):** zvláštní příkaz pro toast se nepřidal. Toast „vychladlo“ jde v E2E (T-093) vyvolat simulátorem: dokončený tisk, chladnutí trysky τ = 60 s, práh např. 100 °C.
- **Testy:**
  - Core: `Notifications/CooldownNotifierTests`, `Settings/SettingsMapperCooledDownNotificationTests`, rozšířené `NotificationSettingsTests`.
  - App: `Notifications/CooldownNotificationObserverTests`, `Notifications/ToastContentBuilderCooledDownTests`, `Settings/PrinterCooldownThresholdSettingsTests` (rozšířené), `AppServicesTests.CooldownNotificationIsConfiguredBeforeTheCooldownMonitor`.
  - UI: `Settings/SettingsWindowCooledDownNotificationUiTests`.
  - Žádné soubory na disku.
- **Závislosti:** žádné nové balíčky; vše AOT kompatibilní (bez reflexe, JSON přes source generator).
- **Platformy:** ověřeno na Windows (build, testy, start aplikace v testovacím režimu – běží). macOS notifikace přijde s T-020 přes `INotificationService`, na Macu neověřeno.

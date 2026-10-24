---
id: T-080
title: Vychladnutí – volitelná notifikace „vychladlo, můžeš vypnout“ a nastavení prahu
status: todo
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-076, T-079]
---

## Cíl
- Nový typ systémové notifikace „Tiskárna vychladla – můžeš ji vypnout“. Je volitelný, přepínač je v nastavení upozornění.
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
- [ ] Core (testy v `LayerPeek.Core.Tests`):
  - nový druh události, přepínač (výchozí vypnuto), `IsEnabledFor`,
  - uložení, načtení a starý soubor,
  - notifikace jde jen při zapnutém hlavním přepínači i přepínači vychladnutí, jednou na úlohu,
  - tisk skončený pod prahem → žádná notifikace,
  - při nastavení zvuku „jen důležité“ bez zvuku,
  - při nastavení zvuku „všechny“ se zvukem, stejně jako ostatní informační notifikace.
- [ ] App.Tests: obsah toastu (en, cs, fr), zvuk podle nastavení
- [ ] Headless UI testy (`LayerPeek.App.UiTests`):
  - přepínač a pole prahu: výchozí hodnoty, uložení, hodnoty mimo 30–100,
  - „Použít u všech tiskáren“,
  - bez posuvníku ve všech 8 jazycích,
  - pořadí Tab, `AutomationId`.
- [ ] Test přepnutí jazyka pro nové texty; `StringsTests` (nezlomitelná mezera před „°C“) projde
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Žádné.

## Poznámky k implementaci
(vyplní agent po dokončení)

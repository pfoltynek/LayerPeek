---
id: T-047
title: Nastavení „Podrobné logování (Debug)“
status: done
owner: core-dev + ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-024, T-040]
---

## Cíl
Uživatel může v okně nastavení zapnout podrobné logování (`Debug`), třeba když ho o to požádá vývojář při řešení chyby. Přepnutí platí hned, bez restartu. Ve výchozím stavu je volba vypnutá a loguje se od úrovně `Information` (T-024).

## Rozsah
- **Core (core-dev):** nová vlastnost v `AppSettings`, např. `bool DebugLogging` (výchozí `false`). Přidat ji do `Equals` / `GetHashCode`, do `SettingsDocument` / `SettingsMapper` a do source-generated JSON kontextu (`settings.json`). Starý `settings.json` bez této hodnoty se načte s `false`.
- **Napojení (core-dev / ui-dev):** po načtení nastavení při startu a po každém uložení nastavení se nastaví přepínač minimální úrovně provideru z T-024 (`Debug`, nebo `Information`). Do té doby, a vždy u druhého spuštění, platí `Information`. Testovací režim T-044 zůstává na `Debug` bez ohledu na volbu. Změnu úrovně zalogovat (úroveň `Information`).
- **UI (ui-designer + ui-dev):** zaškrtávací políčko „Podrobné logování (Debug)“ v okně nastavení. Umístění (záložka, skupina, případný vysvětlující text, např. „Zapněte jen na žádost při řešení chyby, log poroste rychleji.“) určí ui-designer. Okno nastavení musí zůstat bez scrollbaru ve všech 8 jazycích a hlavních stavech (T-033, T-040).
- **Texty UI:** do `Strings.resx` a všech přeložených `Strings.*.resx` (cs, de, es, fr, it, pl, sk) podle T-029.
- Bez nového NuGet balíčku. Kompilované bindingy (AOT).

## Akceptační kritéria
- [x] Volba se ukládá do `settings.json` a načítá se (testy mapperu včetně starého souboru bez této hodnoty)
- [x] Zapnutí nebo vypnutí se projeví v logu hned po uložení nastavení, bez restartu (test přepínače a napojení)
- [x] Políčko je v okně nastavení podle návrhu ui-designera; okno bez scrollbaru ve všech jazycích (headless UI testy T-031 doplněné o nový prvek)
- [x] Texty ve všech jazycích
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Přepínač (2026-10-21):** zaškrtávací políčko v nastavení, změna za běhu, výchozí vypnuto (`Information`), uloženo v `settings.json`.
- **Umístění (2026-10-22, návrh ui-designera, schválil uživatel):** poslední řádek záložky Obecné přes oba sloupce: políčko vlevo, nápověda vpravo na stejném řádku, bez nadpisu sekce (na nadpis není místo bez scrollbaru).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Core:** `AppSettings.DebugLogging` (výchozí `false`, v `Equals` / `GetHashCode`), v `settings.json` jako `"debugLogging"`; soubor bez této hodnoty (nebo s `null`) se načte s `false`.
- **Napojení:** `Logging/DebugLoggingObserver` je `IMonitoringObserver` hned za `UiLanguageObserver`. Načtené nastavení (`ApplySettings`) i uložení, které mění jen tuto volbu (`UpdateSettings`, monitoring se nerestartuje), přepne `LogLevelSwitch` procesu z T-024 (`TrayHost` ho předá přes `AppServices.Create(logLevel:)`) na `Debug` / `Information` a změnu zaloguje na úrovni `Information`. Testovací režim zůstává na `Debug`. Druhé spuštění kontejner nevytváří, takže zůstává na `Information`.
- **UI:** `SettingsDebugLogging` (CheckBox, `HelpText` = nápověda) a `SettingsDebugLoggingHint` v řádku 4 mřížky záložky Obecné; pořadí Tab: … `SettingsRemainingTime` → `SettingsDebugLogging` → Uložit. Rezerva výšky je malá (asi 5 DIP ve španělštině s chybami): nápověda smí mít nejvýš 2 řádky (test ve všech jazycích), každá další volba na záložce Obecné si vynutí přestavbu.
- **Texty:** `SettingsDebugLogging`, `SettingsDebugLoggingHint` ve všech 8 jazycích (polsky „logi“, ne „logowanie“, které v aplikaci znamená přihlášení).
- **Testy:** mapper (round trip, starý soubor), `AppSettings`, pozorovatel, view model, kontejner (pozorovatel je v seznamu session), `MonitoringSession` (změna jen této volby bez restartu přepne úroveň), headless UI (pořadí Tab, nápověda ≤ 2 řádky, bez scrollbaru ve všech jazycích a stavech, nové snímky `settings-general*.png`), App E2E zelené.
- Žádný nový NuGet balíček; AOT publish win-x64 bez varování (2026-10-22).

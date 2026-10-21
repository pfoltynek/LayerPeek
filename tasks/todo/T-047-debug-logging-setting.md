---
id: T-047
title: Nastavení „Podrobné logování (Debug)“
status: todo
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
- [ ] Volba se ukládá do `settings.json` a načítá se (testy mapperu včetně starého souboru bez této hodnoty)
- [ ] Zapnutí nebo vypnutí se projeví v logu hned po uložení nastavení, bez restartu (test přepínače a napojení)
- [ ] Políčko je v okně nastavení podle návrhu ui-designera; okno bez scrollbaru ve všech jazycích (headless UI testy T-031 doplněné o nový prvek)
- [ ] Texty ve všech jazycích
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Přepínač (2026-10-21):** zaškrtávací políčko v nastavení, změna za běhu, výchozí vypnuto (`Information`), uloženo v `settings.json`.

## Otevřené otázky
- (žádné; umístění v okně určí ui-designer)

## Poznámky k implementaci

---
id: T-066
title: E2E – zásahy do zobrazení: měřítko monitoru a dočasné vypnutí monitoru
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065]
---

## Cíl
Dva zásahy do systému pro E2E testy (infrastruktura T-065), bez kterých nejde automaticky ověřit chování oken a ikon při 125/150 % (T-025, T-035, T-039, T-041) a při odpojeném monitoru (T-035):
1. **měřítko** (100/125/150 %) vybraného monitoru za běhu, bez odhlášení;
2. **dočasné vypnutí** dalšího (ne primárního) monitoru, jako by se odpojil.

Úkol dodá jen zásahy a jejich samotesty. Testy aplikace jsou v T-070 a T-071.

## Rozhodnuto
- **Okna jiných aplikací při vypnutí monitoru (uživatel 2026-10-22, dřív Q1):** spolehnout se na to, že Windows okna po návratu monitoru vrátí (nastavení „Pamatovat umístění oken podle připojení monitoru“). Test cizí okna sám nepřesouvá. Samotest ale ověří, že se okna vrátila.
- **Nedokumentované API měřítka (uživatel 2026-10-22, dřív Q2):** přijato (`DisplayConfigSetDeviceInfo` s typy −3/−4). Když se změna neprojeví, test se **přeskočí** s důvodem, neselže.

## Rozsah

### Měřítko (`DisplayScaleChange`)
- Čtení a nastavení přes `DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo` s **nedokumentovanými** typy paketů −3 (čtení: aktuální, doporučené, min. a max. krok) a −4 (nastavení relativního kroku k doporučenému měřítku), pro zdroj (`sourceId` + `adapterId`) monitoru z `QueryDisplayConfig`. Vzor: https://github.com/lihas/windows-DPI-scaling-sample (Unlicense; jen jako zdroj informací, kód se nekopíruje, žádný balíček). Interop přes `[LibraryImport]` v testovacím projektu.
- Kroky měřítka: 100, 125, 150, 175, 200 … %. Test požádá o absolutní měřítko, zásah ho převede na krok. Měřítko mimo rozsah monitoru → zásah ohlásí „nedostupné“ a test se **přeskočí** s důvodem (např. malý monitor nedovolí 150 %).
- **Ověření, že změna proběhla:** po nastavení do 10 s `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` daného monitoru vrátí očekávané DPI (např. 144 pro 150 %). Pokud ne (nedokumentované API přestalo fungovat), vrátí se původní hodnota a test se přeskočí s důvodem „změna měřítka za běhu není na této verzi Windows k dispozici“. Číslo sestavení Windows se zapíše do výsledků.
- **Návrat:** původní krok. Windows hodnotu ukládají do `HKCU\Control Panel\Desktop\PerMonitorSettings\<id monitoru>\DpiValue`. Pokud hodnota (nebo klíč) před změnou neexistovala, po návratu se smaže, aby registr zůstal přesně jako před během. Původní stav v záznamu T-065 obsahuje krok i stav registru.
- **Systémové DPI se nemění:** `GetDpiForSystem` zůstává hodnota z přihlášení. Zásah to zapíše do výsledků (`systemDpi`, `monitorDpi`), aby testy T-070 věděly, zda mohou ověřovat věci závislé na systémovém DPI (viz T-065, Zůstává ručně 1).
- Okna uživatele se při změně měřítka přeskládají (jako při ruční změně). Po návratu měřítka se vrátí, protože Windows je přepočítají zpět. Zapsat do varování T-065 (už tam je).

### Vypnutí monitoru (`MonitorDisconnectChange`)
- `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` uloží současné cesty a režimy. Nová konfigurace bez cesty vybraného **neprimárního** monitoru (`SetDisplayConfig` s `SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES`, **bez** `SDC_SAVE_TO_DATABASE`, takže po restartu počítače Windows použijí uloženou konfiguraci s oběma monitory). Primární monitor se nikdy nevypíná.
- Ověření: do 10 s `EnumDisplayMonitors` monitor nevrací.
- **Návrat:** `SetDisplayConfig` s uloženými cestami a režimy. Když selže (např. po restartu se změnilo LUID adaptéru), použije se `SDC_APPLY | SDC_TOPOLOGY_EXTEND`, případně `SDC_USE_DATABASE_CURRENT`. Ověří se, že počet monitorů, jejich rozlišení, poloha a primární monitor odpovídají stavu před změnou.
- Záznam T-065 obsahuje uložené struktury (binárně, base64) a čitelný popis (názvy monitorů, rozlišení, poloha) pro ruční návrat.
- Bez druhého monitoru se testy přeskočí s důvodem.
- **Okna jiných aplikací:** Windows je při vypnutí monitoru přesunou na zbývající monitor. Po návratu je vrátí podle nastavení „Pamatovat umístění oken podle připojení monitoru“ (rozhodnutí 2026-10-22). Zásah si před vypnutím zapíše viditelná okna ostatních procesů na tomto monitoru (HWND, proces, obdélník, stav) jen pro kontrolu, sám je nepřesouvá. Po návratu monitoru ověří, že jsou zpět na původním monitoru. Pokud ne, zapíše do výsledků seznam (název procesu a okna) a nahlásí to jako chybu běhu.

### Samotesty
- Měřítko: 100 → 125 → 150 → původní na primárním i na druhém monitoru (podle dostupnosti); po každém kroku `GetDpiForMonitor`; po návratu registr `PerMonitorSettings` stejný jako před testem (porovnání exportu klíče).
- Monitor: vypnout → ověřit → vrátit → ověřit stejnou topologii a že viditelná okna ostatních aplikací jsou zpět na původním monitoru (obdélník v toleranci).
- Zabitý podřízený proces uprostřed obou zásahů → návrat úklidem i hlídacím procesem (T-065).
- Oba zásahy mají trait `SystemChanges` a bez potvrzeného varování se přeskočí.

## Akceptační kritéria
- [ ] `DisplayScaleChange`: nastavení 100/125/150 % jednoho monitoru za běhu, ověření přes `GetDpiForMonitor`, návrat včetně stavu `PerMonitorSettings`; nedostupná změna → přeskočeno s důvodem (samotest)
- [ ] `MonitorDisconnectChange`: vypnutí neprimárního monitoru bez zápisu do databáze konfigurací, návrat a ověření topologie; záloha `SDC_TOPOLOGY_EXTEND`; okna ostatních aplikací jsou po návratu zpět na původním monitoru, test je nepřesouvá (samotest)
- [ ] Návrat po zabitém procesu pro oba zásahy (samotest s T-065)
- [ ] Výsledky běhu obsahují sestavení Windows, systémové DPI a DPI monitorů před a po
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; žádný nový NuGet balíček

## Otevřené otázky
- (žádné – Q1 a Q2 rozhodnuty 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
(vyplní agent po dokončení)

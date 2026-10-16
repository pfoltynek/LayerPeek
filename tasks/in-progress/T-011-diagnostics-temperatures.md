---
id: T-011
title: Teploty a diagnostika
status: in-progress
owner: ui-dev
priority: P2
platform: all
depends_on: [T-006]
---

## Cíl
Malé okno / popup s diagnostikou otevírané z tray menu.

## Rozsah
- Teplota trysky a podložky (aktuální/cílová), ventilátory, rychlost, osa Z
- Chybové stavy a poslední zprávy tiskárny
- Informace o tiskárně a firmwaru (`/api/v1/info`, `/api/version`)

## Akceptační kritéria
- [ ] Okno se otevře z menu, zavření neukončí aplikaci – směrování položky menu, jediná instance okna a uvolnění při zavření/ukončení jsou pokryté testy; **ruční ověření kliknutím v menu čeká na uživatele** (automatizované klikání na plochu se nepoužívá)
- [x] Hodnoty se živě aktualizují (testy view modelu a přípravy dat grafu; naživo se skutečnou tiskárnou zatím neověřeno)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Má okno obsahovat graf teplot (navazuje na T-012) a jakou knihovnu grafů použít?~~ **Vyřešeno (2026-10-16):** Ano, graf teplot (tryska, podložka) jako vlastní lehký Avalonia control nad historií z T-012, bez knihovny grafů. T-012 se implementuje před T-011. **Implementováno** (viz poznámky).

Nové otázky (implementace zvolila variantu v závorce, změna je snadná):
1. Časová okna grafu: stačí 30 min / 2 h / 24 h? Výchozí okno? Pamatovat si volbu mezi otevřeními? (Tyto tři, výchozí 2 h, volba se nepamatuje.)
2. Cílová teplota 0 = topení vypnuto: v tabulce „Vypnuto“, v grafu se čárkovaná cílová čára nekreslí. Je to tak v pořádku? (Ano.)
3. Řádek „Podrobnosti“ ukazuje `StatusMessage` klienta, což jsou technické anglické texty (např. „The printer is not reachable.“). Lokalizovat je, nebo je skrýt? Firmware Buddy v API neposílá důvod chyby ani „vyžaduje zásah“ (viz `docs/api/prusalink.md`), takže skutečné hlášky tiskárny zobrazit nejde. (Zobrazují se, jen když existují, anglicky.)
4. Model tiskárny: firmware hlásí jen kód „typ.verze.podverze“ (např. `7.0.0`). Mapovat kódy na názvy (Core One, MK4…)? Tabulku kódů nemám ověřenou. (Kód se ukazuje tak, jak je, s popiskem „Kód modelu“.)
5. Vzhled: barvy grafu (tryska oranžová, podložka modrá; pro tmavé téma světlejší odstíny), rozložení okna (výchozí 560 × 760, měnitelná velikost), popisky sekcí. Schválit? (Viz popis v poznámkách.)
6. Hodnoty v grafu po najetí myší (tooltip) nebo zoom nejsou. Chcete je? (Ne.)
7. Informace o tiskárně se načtou jednou při otevření okna / přepnutí tiskárny (2 požadavky) ; po neúspěchu se zkusí znovu nejvýš jednou při dalším dosažitelném snapshotu a další pokus je možný až po výpadku tiskárny (nebo po změně adresy / přepnutí tiskárny). Stačí to? (Ano.)
8. Názvy v okně: „Ventilátor chlazení tisku“ (print fan), „Kód modelu“, „Název hostitele“. Upravit? (Takto.)

## Poznámky k implementaci
- **Okno (`App/Windows/DiagnosticsWindow.axaml`):** kompilované bindingy (`x:DataType`, i v `DataTemplate`), texty přes `{x:Static res:Strings…}`. Obsah shora:
  - výběr tiskárny (`ComboBox`, jen když je tiskáren víc; jinak název tiskárny), upozornění „Spouštění…“ / „Není nastavena žádná tiskárna“ / „Sledování se zastavilo kvůli chybě“,
  - stav, podrobnosti (jen když klient poslal zprávu), poslední aktualizace (místní čas se sekundami, u starší než dnešní i datum),
  - teploty trysky a podložky (aktuální tučně s jedním desetinným místem, cílová v celých stupních, 0 = „Vypnuto“), ventilátory (ot./min), rychlost a průtok (%), osa Z (mm, 2 desetinná místa),
  - graf historie teplot s výběrem okna a legendou,
  - informace o tiskárně: firmware, kód modelu, název hostitele, průměr trysky, MMU.
  - Neznámá hodnota (null, NaN, ±∞) je vždy „–“, nikdy 0 ani „NaN“. Čísla podle kultury (`215,3 °C`, `7 200 ot./min`, `95 %` s nezlomitelnou mezerou v češtině; `215.3 °C`, `7,200 RPM`, `95%` v angličtině). Barvy jsou v `ThemeDictionaries` okna (světlé/tmavé téma, `DynamicResource`).
- **View model (`App/Diagnostics/DiagnosticsViewModel`):**
  - Jedna instance na otevřené okno, při zavření se uvolní: odhlásí se od `IPrinterMonitor` a `MonitoredPrinters` a zruší rozpracovaný dotaz na informace. Zavřené okno nic nedělá.
  - Snapshoty jiných tiskáren se zahodí hned (bez zámku). U vybrané se pod zámkem porovná jen `DiagnosticsContent` (hodnoty zaokrouhlené přesně jako na displeji, čas na celé sekundy). Beze změny se na UI vlákno nic neposílá; dávka změn = jedna UI aktualizace, která vyvolá `PropertyChanged` jen u textů, které se opravdu změnily.
  - Konfigurace (přidání, odebrání, přejmenování tiskárny) a selhání monitoringu přicházejí stejně přes `App/Tray/MonitoredPrinters` (`IMonitoringObserver` v `MonitoringSession`). Odebraná vybraná tiskárna → vybere se první zbývající; žádná → upozornění. Přejmenování výběr zachová; změna adresy/typu připojení znovu načte informace.
  - Po přepnutí tiskárny se hned zobrazí její poslední snapshot z monitoru (`TryGetSnapshot`). Výběr, který `ComboBox` na chvíli vynuluje (při výměně položek), se ignoruje.
  - Informace o tiskárně (`IPrinterInfoSource` → `PrinterInfoSource`): PrusaLink přes existující `PrusaLinkClient.GetInfoAsync` (`/api/version` + `/api/v1/info`, už v T-003, doplněné testy), Prusa Connect „není k dispozici“. Starý výsledek po přepnutí tiskárny se zahodí. Při neúspěchu „Nelze načíst: <stav>“ a jeden nový pokus při dalším dosažitelném snapshotu – i když selhání přišlo, když už tiskárna odpovídala (dotaz běžel při startu, přechodná chyba, změna adresy). Když selže i ten, další dotazy dosažitelné tiskárny už nic nežádají; nový pokus povolí až snapshot nedostupné tiskárny, nová adresa nebo jiná tiskárna. Nikdy tedy nejde o požadavek při každém dotazu (review).
- **Graf (`App/Diagnostics/Chart`):**
  - `TemperatureChartData` (bez Avalonie, testovaný): zkopíruje rozsah z `TelemetryHistory.CopySamples` do jednoho bufferu o velikosti `GetCapacity` (nová alokace jen při změně kapacity, při „žádné tiskárně“ se uvolní) a v jednom průchodu ho zredukuje na nejvýš jeden sloupec na pixel a řadu (první, min, max, poslední hodnota – špička se neztratí). Čára se přeruší na `NaN` a na `StartsSegment`; cílová teplota 0 se bere jako chybějící. Kopíruje jen při změně verze, tiskárny nebo rozsahu, při změně šířky jen znovu decimuje. Ustálený stav nealokuje (test). Měření v harnessu: 86 401 vzorků (24 h po 1 s) → 640 sloupců ≈ 3 ms (kopie + decimace).
  - `ChartScale`: osa teplot od 0 po „hezký“ násobek kroku (1/2/2,5/5 × 10ⁿ, nejméně 50 °C, místo nad maximem), časový krok 1 min … 1 den podle šířky, značky (`TimeTicks`) se počítají v místním čase pro každou značku zvlášť (`TimeZoneInfo`), takže zůstávají kulaté i přes změnu letního/zimního času; neexistující hodina (jaro) značku nemá, opakovaná (podzim) má jednu.
  - `TemperatureChart` (vlastní `Control`, `Render` + `StreamGeometry`): tryska a podložka plnou čarou, cílové teploty tenkou čárkovanou. Odebírá `TelemetryHistory.Changed` jen v době, kdy je v okně (připojení k vizuálnímu stromu), změny jiných tiskáren ignoruje a dávku změn sloučí do jednoho požadavku na překreslení (`Dispatcher.UIThread.Post`, priorita Background). Co se staví kdy: vodorovná mřížka a popisky °C jen při změně rozsahu osy nebo velikosti; s každou změnou dat (dotazem) jen geometrie řad a pozice časových značek (svislé čáry se kreslí přímo `DrawLine`). Popisky (`FormattedText`) drží `ChartLabelCache` podle hodnoty / času značky (omezená velikost), takže nový popisek vznikne jen pro novou značku; při změně štětce (téma), kultury nebo časového pásma se zahodí. Na jeden dotaz tedy alokuje jen nové `StreamGeometry` řad (až 4) a případně jeden popisek. Pera vznikají jen při změně štětce. Při pouhém překreslení `Render` jen znovu vykreslí uložené objekty. „Teď“ se posune jen s novými daty, takže změna velikosti neposouvá osu. Bez dat text „V tomto období nejsou žádné teploty“.
- **Menu a okna:** položka „Diagnostika…“ je povolená (`TrayMenuOptions.Default`), `TrayMenuPresenter` ji směruje na `ITrayAppCommands.ShowDiagnostics`. `App/Windows/AuxiliaryWindowSlot` drží jednu instanci okna (další klik ho aktivuje), při zavření uvolní view model právě jednou a při ukončení aplikace okno zavře a další otevření odmítne. Používá ho i okno O aplikaci. `AvaloniaAuxiliaryWindow` je tenký adaptér nad `Window`. Při „Ukončit“ se nejdřív zavře diagnostika (odhlásí se) a okno O aplikaci, pak se zastaví monitoring.
- **Lokalizace:** všechny nové texty v `Strings.resx` i `Strings.cs.resx` (test úplnosti a placeholderů prochází).
- **Native AOT:** bez reflexe, bez nových balíčků; kompilované bindingy všude, DI explicitně (`AppServices.CreateDiagnosticsViewModel`). `scripts/aot-smoke.ps1` prošel (aplikace v AOT běží 10 s). Samotné okno diagnostiky se v AOT buildu neotevíralo (vyžaduje kliknutí v menu).
- **Testy (nové):** `Diagnostics/TemperatureChartDataTests` (16: prázdná/neznámá tiskárna, jeden vzorek, min/max/první/poslední v každém sloupci proti hrubé síle, celý den 1 s vzorků, přerušení na NaN, offline a `StartsSegment`, spojení přes prázdné sloupce, cílová 0, ořez rozsahu, pravý okraj, kdy `Update` hlásí změnu, přepnutí tiskárny, velikost a znovupoužití bufferu při změně kapacity, růst bufferů sloupců, nulová alokace), `ChartScaleTests` (včetně značek přes začátek i konec letního času s vlastní `TimeZoneInfo` s pravidly, bez závislosti na systémových zónách), `ChartLabelCacheTests`, `DiagnosticsFormatterTests` (kultura cs/en, NaN/∞/null, časové pásmo, datum u jiného dne), `DiagnosticsViewModelTests` (37: upozornění, výběr, okamžité zobrazení, živá aktualizace, slučování dávky, žádná UI práce bez viditelné změny, jen změněné vlastnosti, cizí tiskárny, offline, částečná telemetrie, přepínání, odebrání/přejmenování/změna adresy, selhání monitoringu, odhlášení a ignorování pozdní práce po `Dispose`, informace o tiskárně včetně Prusa Connect, selhání a opakování (po výpadku, selhání při běžícím startu, přechodná chyba, změna adresy, žádné opakování při každém dotazu), zastaralý výsledek, výjimka zdroje, časová okna), `PrinterInfoSourceTests` (falešný `HttpMessageHandler`), `Windows/AuxiliaryWindowSlotTests`, `Tray/MonitoredPrintersTests`, směrování a povolení položky v testech menu, registrace v `AppServicesTests`, PrusaLink: kód modelu, `/api/v1/info` bez odpovědi, neplatná adresa bez síťového přístupu. Žádný disk, síť ani procesy; `FakePrinterMonitor` si nově pamatuje poslední snapshot (`TryGetSnapshot`).
- **Ověření na Windows 11:**
  - `dotnet build` 0 varování, `dotnet test` 999 testů (998 prošlo, 1 přeskočený existující). Po review znovu vykresleno v harnessu, vzhled beze změny.
  - Okno bylo vykresleno mimo repozitář (dočasný harness, `RenderTargetBitmap`, okno mimo obrazovku, bez jakéhokoli vstupu) s 24 h simulovanými daty: světlé i tmavé téma, okna 30 min / 2 h / 24 h, tiskárna bez dat, čeština i angličtina. Zkontrolováno: přerušení čar při výpadku tiskárny i při „uspaném počítači“, krátká špička zachovaná v 24h pohledu, čárkované cílové čáry jen při ohřevu, popisky os, text bez dat, „Nelze načíst: Nedostupná“.
  - `dotnet run`: aplikace se spustila a běžela 30 s (stejné PID, ~97 MB), pak byla ukončena; nevytvořila složku nastavení. Okno se z menu neotevíralo (bez syntetického klikání) – **otevření z menu, opakované kliknutí (aktivace), zavření bez ukončení aplikace a „Ukončit“ s otevřeným oknem musí ručně ověřit uživatel**, stejně jako živé hodnoty se skutečnou tiskárnou.
- **Platformy:** kód okna i grafu je společný (Avalonia). **Na macOS nic neověřeno** (vykreslení grafu, `ComboBox`, chování okna u aplikace bez Docku – T-018/T-019).

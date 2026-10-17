---
id: T-012
title: Historie telemetrie
status: done
owner: core-dev
priority: P3
platform: all
depends_on: [T-006]
---

## Cíl
Uchovávat historii teplot a stavů pro grafy a diagnostiku.

## Rozsah
- Ring buffer v paměti pro každou tiskárnu
- ~~Volitelně perzistence~~ (rozhodnuto: bez perzistence)

## Akceptační kritéria
- [x] Testy bufferu (kapacita, pořadí, souběžný přístup)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Jak dlouhou historii držet? Ukládat na disk (např. SQLite), nebo jen v paměti?~~ **Vyřešeno (2026-10-16):** Jen v paměti, 24 h historie na tiskárnu (ring buffer), bez perzistence na disk. Implementováno.

Další otázky (obě rozhodnuty uživatelem podle implementace):
1. **Vyřešeno (2026-10-17, potvrzeno uživatelem):** Když se tiskárně v nastavení změní adresa nebo typ připojení, má se její historie zahodit? (Ano – stejně jako stav notifikací v T-009: může jít o jiný stroj a graf by míchal dvě tiskárny. Přejmenování historii zachová.)
2. **Vyřešeno (2026-10-17, potvrzeno uživatelem):** Má graf (T-011) přerušit čáru, když delší dobu nepřišel žádný vzorek (např. uspaný počítač)? A od jaké pauzy? (Ano, od 3 intervalů pollingu, ale nejméně od 30 s, aby pomalá odpověď tiskárny čáru nerozdělila.)

## Poznámky k implementaci
- **Model (`Core/History/TelemetrySample`):** `readonly record struct` o velikosti 48 B bez referencí (ověřeno testem), takže historie tiskárny je jedno pole bez objektů na haldě.
  - Čas jako UTC ticky (`long`), `State` jako bajt (nedefinovaná hodnota = `Unknown`), příznaky `HasTelemetry` a `StartsSegment`.
  - Teploty trysky a podložky (aktuální i cílová), ventilátory a osa Z jako `float`. Rychlost, průtok a průběh tisku jako `Half` – celá procenta do 2048 jsou přesná, průběh má rozlišení aspoň 0,0625 procentního bodu. Navenek jsou všechny hodnoty `float`.
  - Chybějící hodnota je `NaN` (i průběh bez úlohy – není to 0). Hodnota, kterou typ úložiště neumí uložit (z `float`/`Half` by bylo ±∞, např. rychlost nad 65 504 %), nebo nekonečný vstup se také uloží jako `NaN`.
- **Výpadky (mezery v grafu):**
  - Snapshot bez telemetrie (offline, `Unknown`, `Unauthorized`…) se uloží jako vzorek se stavem, `HasTelemetry = false` a všemi hodnotami `NaN`. Graf na `NaN` čáru přeruší.
  - `StartsSegment` má první vzorek tiskárny a vzorek, který přišel po pauze delší než `GapThreshold` = 3 intervaly pollingu, nejméně 30 s (např. uspaný počítač). Graf ho s předchozím nespojí.
- **Kapacita a paměť:**
  - Kapacita = ⌈24 h / interval⌉ + 1. Interval kratší než 1 s se počítá jako 1 s (monitor technicky dovolí 1 ms), takže paměť je shora omezená.
  - **Nejhorší případ na tiskárnu:** interval 1 s → 86 401 vzorků × 48 B = 4 147 248 B ≈ **4,0 MiB** (+ ~100 B režie). Výchozí 5 s → 17 281 vzorků ≈ 0,8 MiB, 5 min → 289 vzorků ≈ 14 KB.
  - Pole roste zdvojováním od 256 vzorků (12 KB) až do kapacity, takže krátce sledovaná tiskárna nezabere celé 4 MiB. Při posledním zvětšení existuje krátce staré i nové pole (nejhůř ~3 + 4 MiB). Pole nad ~85 KB jsou na LOH.
  - Retence je časová: vzorky starší než 24 h před nejnovějším vzorkem tiskárny se zahodí, i když buffer není plný (např. po změně intervalu nebo po výpadku). Vzorek přesně 24 h starý zůstává.
  - Když vzorky chodí rychleji než interval (restart monitoringu po uložení nastavení dotazuje hned), plný buffer přepisuje nejstarší, pokrytí je pak o pár vzorků kratší než 24 h.
- **Čas jdoucí zpět:** vzorky s časem ≥ času nového vzorku se zahodí a nový se připojí. Pořadí tak zůstává ostře rostoucí (binární vyhledávání), data před skokem zůstanou. Stejný čas = nahrazení posledního vzorku.
- **Změny nastavení (`Configure(printers, interval)`, volá se před každým během):**
  - Tiskárny, které zůstaly (stejné id bez ohledu na velikost písmen, stejná adresa a typ připojení), si historii nechají. Při změně intervalu se buffer přealokuje s nejnovějšími vzorky (zmenšení zahodí nejstarší).
  - Odebrané tiskárny a tiskárny s jinou adresou/typem připojení historii ztratí, znovu přidaná tiskárna začíná prázdná. Stará historie se pod svým zámkem označí jako vyřazená dřív, než nová dostane verzi. Zápis z vlákna, které ještě drží starý slovník, se pak zahodí a verze, kterou vidí čtenář, nikdy neklesne.
  - Změna kapacity (změna intervalu) zvýší verzi a vyvolá `Changed`, i když se žádný vzorek nezahodil. Graf, který si buffer dimenzuje podle `GetCapacity`, se tak o zvětšení dozví.
  - Snapshoty nenastavených tiskáren a snapshoty před prvním `Configure` se ignorují.
  - Neplatný seznam (null položka) vyhodí výjimku a předchozí konfiguraci nechá beze změny.
- **Vlákna a API pro graf (T-011):**
  - Zápis (`Record`) volá monitor, pro každou tiskárnu postupně. Čtení může běžet souběžně z libovolného vlákna (UI).
  - Každá tiskárna má vlastní krátký zámek (`System.Threading.Lock`). Zápis je O(1), čtení drží zámek jen po dobu binárního hledání a kopie požadovaného rozsahu (`memcpy`, max. ~4 MB). Slovník tiskáren je copy-on-write, vyhledání je bez zámku.
  - `CountSamples(id, from, to)` a `CopySamples(id, from, to, Span<TelemetrySample>, out version)`: rozsah včetně obou mezí, nejstarší první. Při malém cílovém bufferu se zkopírují nejnovější vzorky rozsahu. `GetCapacity(id)` vrátí velikost, se kterou může graf alokovat jeden znovupoužitelný buffer.
  - `GetVersion(id)` roste s každou změnou (globální čítač, takže znovu přidaná tiskárna nikdy nedostane starou verzi). Graf překreslí jen při změně verze.
  - Událost `Changed(printerId)` přichází mimo zámek z vlákna, které změnu udělalo (monitor, nebo volající `Configure`). UI si ji musí přepnout na svoje vlákno.
  - Každý handler `Changed` běží ve vlastním try/catch: výjimka se zaloguje (Error, volitelný `ILogger<TelemetryHistory>`, v App zaregistrovaný) a ostatní handlery běží dál. Vadný graf tak nemůže shodit `Record` ani `Configure`, a přes `ApplySettings` ani monitorovací session. Handlery jsou v copy-on-write poli, takže vyvolání nealokuje (na rozdíl od `GetInvocationList`).
  - Bez LINQ v horkých cestách. `Record` v ustáleném stavu a čtení nealokují (ověřeno testy přes `GC.GetAllocatedBytesForCurrentThread`).
- **App:** `App/History/TelemetryHistoryObserver` (`IMonitoringObserver`, `IDisposable`):
  - Odebírá `IPrinterMonitor.SnapshotUpdated` → `Record`. `ApplySettings` → `Configure(settings.Printers, settings.PollingInterval)`.
  - Selhání monitoringu historii nemaže.
  - `Composition/AppServices` registruje `TelemetryHistory` a pozorovatele explicitně (factory, AOT) a přidává ho do `MonitoringSession`.
- **Testy:**
  - `Core.Tests/History/TelemetryHistoryTests` (84 případů): mapování hodnot, NaN (včetně hodnot mimo rozsah `float`/`Half`), mezery (offline/unknown/unauthorized), velikost struktury, výpočet kapacity, přetečení a mnohonásobný wrap-around, líný růst paměti, časová retence, změna intervalu (zmenšení, zvětšení, zabalený buffer), přidání/odebrání/přejmenování/změna adresy, dotazy na rozsah (prázdný, částečný, celý, hranice, obrácený, malý cílový buffer), segmenty a práh mezery, čas jdoucí zpět, verze a událost `Changed` (včetně změny kapacity, házejícího a odebraného handleru), nulová alokace, validace.
  - Souběh: 2 zapisovatelé × 20 000 vzorků a 3 čtenáři kontrolují, že žádný vzorek není roztržený, pořadí je souvislé a verze neklesá. Ověřeno mutací: bez zámku při čtení test spolehlivě selže.
  - Zátěžový test: k zápisu a čtení přidává třetí úlohu, která neustále volá `Configure` (interval 1 s ↔ 5 s a výměna tiskárny xl změnou adresy). Úlohy startují společně (`Barrier`). Ověřeno mutací: změna kapacity mimo zámek tiskárny test spolehlivě shodí.
  - Pomocník `ReadWhileAsync` při chybě, timeoutu i zrušení testu všechny úlohy zastaví a počká na ně, takže po testu nic neběží.
  - `App.Tests/History/TelemetryHistoryObserverTests` a registrace v `AppServicesTests`.
  - Vše v paměti, bez disku, sítě a procesů.
- **Native AOT:** bez reflexe a bez nových balíčků. `Enum.IsDefined<T>` je generický (AOT-safe) a DI je explicitní.

# Nastavení GC a Native AOT – A/B měření (T-191)

Měření z 2026-11-11 na Macu mini M2 Pro (10 jader, 16 GB, macOS 27.2.1, .NET SDK 11.0.100, `osx-arm64`). Cílem bylo
zjistit, jestli tray aplikaci, která většinu času jen čeká, pomůže vypnutý souběžný GC nebo optimalizace AOT na velikost.
Shipped build se **neměnil**. Případná změna buildu vznikne jako samostatný úkol až po souhlasu uživatele.

## Varianty

Každá varianta je publikovaná zvlášť příkazem `dotnet publish src/LayerPeek.App -c Release -r osx-arm64 -o <složka mimo repozitář>`
s vlastnostmi MSBuild na příkazové řádce. Že se vlastnosti opravdu uplatnily, ověřuje soubor `LayerPeek.ilc.rsp`
(`--runtimeknob:System.GC.Concurrent=false` a `--Os`).

| Varianta | Vlastnosti | Velikost `LayerPeek` (exe) | `__TEXT` | Publikovaná složka bez dSYM |
|---|---|---|---|---|
| A – výchozí | – | 35 664 856 B (34,0 MiB) | 30,8 MiB | 57,6 MiB |
| B – bez souběžného GC | `ConcurrentGarbageCollection=false` | 35 664 856 B (stejná velikost, liší se jen přepínač) | 30,8 MiB | 57,6 MiB |
| C – velikost | `OptimizationPreference=Size` | 35 001 160 B (33,4 MiB, −648 KiB, −1,9 %) | 30,2 MiB | 57,0 MiB |
| D – obojí | obě vlastnosti | 35 001 160 B | 30,2 MiB | 57,0 MiB |

Zbytek složky tvoří nativní knihovny (Skia 14,5 MiB, HarfBuzz, AvaloniaNative, FFmpeg) a `.pdb`. Ty se mezi variantami nemění.

## Postup

- **Simulátor** (`tools/PrusaSimulator`, Release, jen `127.0.0.1`) běží se 3 tiskárnami (Core One, MK4S, XL, scénář
  `printing`) a kamerou 1280 × 720 při 25 snímcích/s. Pro každý běh se spouští znovu, takže každý běh vidí stejný průběh
  tisku (ohřev 90 s, pak tisk).
- **Nastavení**: 3 tiskárny PrusaLink, interval 5 s, kamera zapnutá u všech tří (sdílená adresa). Notifikace a kontrola
  aktualizací jsou vypnuté. Detekce, nahrávání a dashcam zůstaly na výchozí hodnotě (vypnuto). Aplikace startuje
  s `--layerpeek-smoke-skip-notification-center`.
- **Heslo bez Klíčenky**: Release exe čte heslo z Klíčenky a položku, kterou vytvořil nástroj `security`, mu macOS
  nevydá bez dialogu (seznam oddílů). Při zamčené obrazovce aplikace na tom dialogu visí. Aplikace se proto připojuje
  na loopback proxy (`127.0.0.1:18080–18082`), která k požadavkům přidává `X-Api-Key`. Simulátor přihlášení dál
  vyžaduje. Aplikace tak nemá heslo, Klíčenka uživatele zůstala netknutá a ve srovnání s Digestem odpadá jen jedna výzva
  `401` za 5 minut na tiskárnu.
- **Okno kamery bez testovacích háčků**: Release nemá testovací příkazy. Okno proto otevře automatika T-151:
  `camera-windows.json` obsahuje `"autoClosed": ["t191-p1"]` a nastavení tiskárny 1 obsahuje `camera.window.startMode`
  `Normal` (okno 960 × 540) nebo `Mini` (320 × 180). Aplikace okno otevře 10 s po startu, jakmile je tiskárna online
  (v logu „Opening the camera window of printer t191-p1 again“). V každém běhu s kamerou bylo ověřené spojení RTSP na
  `:8554`.
- **Scénáře**:
  - jen sledování: 30 min od startu, vyhodnocení od 120 s do 1800 s;
  - okno kamery 720p: 10 min od otevření okna, vyhodnocení od otevření + 60 s;
  - malý náhled: totéž jako okno kamery.
- **Pořadí**: 2 opakování každé kombinace. První kolo běží v pořadí A, B, C, D, druhé v obráceném pořadí, aby se
  vyrušil drift. Celkem 24 běhů a asi 7 h. Na stroji jinak nic neběželo. Mac držel vzhůru `caffeinate -d -i -u`.
- **Metriky** přes `proc_pid_rusage` (v4) a `proc_pidinfo(PROC_PIDTASKINFO)` každých 5 s:
  - `phys_footprint`: stejné číslo jako ukazuje `footprint`, ověřeno;
  - rezidentní paměť (RSS, obsahuje i sdílené stránky frameworků);
  - CPU = přírůstek uživatelského a systémového času dělený uplynulým časem (100 % = jedno jádro);
  - počet vláken;
  - probuzení za sekundu (`pkg_idle_wkups` + `interrupt_wkups`).

  Na konci každého běhu proběhl ještě `footprint -p` („Untagged“ zhruba odpovídá spravované haldě). **Start** je čas
  od spuštění procesu po řádek logu „The detected model … of printer 't191-p1' was saved.“, tj. po první úspěšnou
  odpověď tiskárny. V tu chvíli už běží UI i ikona v menu baru (načtení nastavení a vytvoření traye jsou před
  monitorováním).
- **Ochrana profilu uživatele**: Release exe nemá izolovaný režim (`LAYERPEEK_TEST_DATA_DIR` a
  `LAYERPEEK_INSTANCE_SUFFIX` existují jen v Debug sestavení). Každý běh proto odsunul profil žurnálovanými funkcemi
  `Backup-SmokeProfile` / `Restore-SmokeProfile` z `scripts/ReleaseTools`. Jsou to stejné funkce jako v
  `scripts/aot-smoke.ps1`: složky dat, logů, Saved Application State, Caches/HTTPStorages bundlu a domény `defaults`.
  Navíc se hlídala složka `~/Library/Caches/LayerPeek`, kterou smoke profil nepokrývá (běhy ji nevytvořily). Po každém
  běhu se porovnal otisk profilu (SHA-256 souborů, export domén, položky Klíčenky se službou `LayerPeek`) s otiskem
  před měřením. Výsledek byl ve všech 24 bězích shodný a shodný byl i na konci.

## Výsledky – průměr 2 opakování (v závorce jednotlivé běhy)

| Scénář | Varianta | CPU % jádra | footprint Ø MB | footprint max MB | RSS Ø MB | vlákna Ø | probuzení/s | CPU startu s |
|---|---|---|---|---|---|---|---|---|
| sledování 3 tiskáren (30 min) | A – výchozí | 0,08 (0,08 / 0,08) | 49,8 (49,8 / 49,7) | 50,8 | 141,7 | 17,1 | 1,7 | 0,20 |
| | B – bez souběžného GC | 0,08 (0,08 / 0,08) | 49,7 (49,6 / 49,7) | 50,7 | 141,8 | 17,1 | 1,7 | 0,20* |
| | C – velikost | 0,08 (0,08 / 0,08) | 49,9 (50,0 / 49,8) | 50,9 | 141,3 | 17,1 | 1,7 | 0,20* |
| | D – obojí | 0,08 (0,07 / 0,08) | 49,8 (49,7 / 49,8) | 50,8 | 141,3 | 16,1 (15,0 / 17,1) | 1,7 | 0,20* |
| okno kamery 720p (10 min) | A – výchozí | 20,74 (20,80 / 20,68) | 138,9 (140,4 / 137,4) | 141,5 | 195,0 | 19,2 | 63,7 | 0,20 |
| | B – bez souběžného GC | 20,56 (20,50 / 20,63) | 147,6 (151,4 / 143,8) | 151,9 | 198,3 | 19,3 | 63,7 | 0,19 |
| | C – velikost | 20,64 (20,54 / 20,75) | 141,5 (145,9 / 137,0) | 143,7 | 193,1 | 20,1 | 63,7 | 0,20 |
| | D – obojí | 20,44 (20,68 / 20,20) | 140,9 (140,6 / 141,2) | 143,3 | 196,8 | 20,1 | 63,7 | 0,20 |
| malý náhled (10 min) | A – výchozí | 18,24 (18,01 / 18,46) | 89,7 (89,8 / 89,6) | 89,8 | 180,1 | 19,0 | 64,3 | 0,20 |
| | B – bez souběžného GC | 18,11 (18,10 / 18,12) | 89,4 (89,4 / 89,4) | 89,5 | 179,8 | 19,0 | 64,3 | 0,20 |
| | C – velikost | 18,11 (18,17 / 18,05) | 89,7 (89,6 / 89,8) | 89,8 | 179,3 | 19,1 | 63,8 | 0,19 |
| | D – obojí | 18,20 (18,17 / 18,23) | 89,7 (89,7 / 89,6) | 89,8 | 179,1 | 19,1 | 63,7 | 0,20 |

\* Hodnota z druhého opakování. V prvním kole šlo o první spuštění nové binárky (0,38–0,40 s CPU, viz start).

„CPU startu“ je čas CPU spotřebovaný do 10 s po startu. „Untagged“ z `footprint` na konci běhu: sledování 12 MB u všech
variant, malý náhled 18 MB u všech, okno kamery 23–29 MB bez vazby na variantu.

### Start (od spuštění procesu po první odpověď tiskárny, 6 běhů na variantu)

| Varianta | medián | min | max |
|---|---|---|---|
| A – výchozí | 162 ms | 156 ms | 172 ms |
| B – bez souběžného GC | 157 ms | 141 ms | 1518 ms* |
| C – velikost | 158 ms | 146 ms | 1329 ms* |
| D – obojí | 167 ms | 155 ms | 1387 ms* |

\* Maximum je vždy první spuštění nové binárky (běhy 02–04). macOS ji při prvním startu kontroluje (ad-hoc podpis,
syspolicyd). Variantu A spustily už zkušební běhy před měřením. S variantou to nesouvisí.

## Vyhodnocení

- **Sledování (hlavní případ tray aplikace)**: všechny varianty jsou stejné v rámci přesnosti měření. CPU je 0,08 %
  jádra (asi 1,4 s CPU za 30 min), footprint 49,7–49,9 MB, 1,7 probuzení/s. Vypnutý souběžný GC **neušetřil vlákno**:
  B má stejný počet vláken jako A (17–18). NativeAOT zřejmě vlákno pro GC na pozadí vytváří až při prvním gen2 GC na
  pozadí a malá halda této aplikace se k němu za 30 min nedostala. Nižší počet vláken v jednom běhu D (15–16) se ve
  druhém opakování nezopakoval (17–18), jde o vlákna thread poolu.
- **Okno kamery a malý náhled**: rozdíly CPU mezi variantami (±0,3 procentního bodu) jsou menší než rozptyl mezi
  opakováními. Footprint okna kamery kolísá mezi běhy o ±7 MB bez vazby na variantu (B 151 a 144 MB, A 140 a 137 MB).
  U malého náhledu jsou všechny varianty na 89,4–89,8 MB.
- **`OptimizationPreference=Size`** zmenší exe o 648 KiB (1,9 %), měřitelně nezpomalí ani nezrychlí. Na velikosti
  balíčku se to skoro neprojeví (Skia a FFmpeg zůstávají).
- **Co doopravdy stojí prostředky**: zobrazení kamery, ne GC. Otevřené okno 720p stojí ~20,6 % jádra a +90 MB footprintu
  (z toho asi 58 MB grafika: IOSurface, CG Image, IOAccelerator), malý náhled ~18 % jádra a +40 MB. Probuzení
  stoupnou z 1,7/s na ~64/s. Malý náhled stojí skoro tolik CPU jako velké okno, protože se dekóduje celý stream 720p při
  25 snímcích/s. Na to míří T-185 (omezení snímkové frekvence malého náhledu) a T-192 (hardwarové dekódování).

## Doporučení

**Build neměnit**: ponechat výchozí souběžný GC a výchozí `OptimizationPreference`.

- `ConcurrentGarbageCollection=false` nepřinesl měřitelný rozdíl v paměti, CPU, vláknech ani startu. Vypnutí přitom
  bere aplikaci GC na pozadí, který by mohl pomoct, kdyby halda někdy narostla (dlouhé nahrávání, detekce).
- `OptimizationPreference=Size` ušetří 1,9 % exe a výkonově je neutrální. Přínos je tak malý, že změnu nedoporučuji.
  Pokud by uživatel o každý kilobajt stál, je to jediná varianta bez měřitelné nevýhody. Rozhodnutí je na uživateli
  a změna by šla do samostatného úkolu (podle T-191).
- Úspory hledat v cestě kamery (T-185, T-192), ne v nastavení GC.

## Omezení měření

- **Obrazovka byla po celou dobu zamčená** (`CGSSessionScreenIsLocked`, displej zapnutý). Okna se kreslila (IOSurface
  ve footprintu, CPU okolo 20 %), ale WindowServer může zamčenou plochu skládat jinak než odemčenou. Absolutní čísla
  okna kamery proto mohou být při odemčené obrazovce jiná. Srovnání variant to neovlivňuje, protože všechny běžely za
  stejných podmínek.
- Měřilo se jen na macOS (Apple Silicon). Na Windows (x64, jiný GC backend pro regiony, jiný renderer) může vyjít
  jinak. Tam měření zopakovat, jen pokud by se o změně buildu uvažovalo.
- Autentizace přes `X-Api-Key` (proxy) místo Digestu, viz Postup. Rozdíl je jedna výzva za 5 minut na tiskárnu.
- **Pauzy GC se neměřily**: Native AOT build má `EventSource` vypnutý (`System.Diagnostics.Tracing.EventSource.IsSupported=false`),
  takže `dotnet-counters` ani `dotnet-trace` nefungují a měřit by to šlo jen s upraveným buildem. Nepřímý ukazatel je
  CPU (žádný rozdíl) a „Untagged“ z `footprint` (stejné u všech variant).
- 2 opakování na kombinaci. Rozptyl je uvedený v závorkách, rozdíly mezi variantami jsou menší než on.
- Skripty měření (driver, vzorkování přes libproc, proxy, analýza) v repozitáři nejsou. Jsou to jednorázové nástroje
  ve scratchpadu agenta, postup výše stačí k zopakování.

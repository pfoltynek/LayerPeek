---
id: T-145
title: Nahrávání kamery – služba v Core (ruční a automatické nahrávání při tisku, složka, limit, rotace)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-144, T-009]
---

## Cíl
Logika, **kdy** a **kam** se nahrává, bez UI a testovatelná s falešnými hodinami a falešným rekordérem (nápad 1, fáze 1). Staví na zápisu z T-144 a na sdíleném spojení z T-143, takže nahrávání během tisku běží **i bez otevřeného okna kamery**.

## Rozsah
- **`Core/Cameras/Recording`, služba (pracovně `CameraRecordingService`), po vzoru `CooldownMonitor` / `PrintEventNotifier`:**
  - odebírá `IPrinterMonitor.SnapshotUpdated`, `Configure(printers, settings)` při každém běhu monitoringu (stav tiskáren se stejným strojem zachová),
  - **ruční nahrávání:** `Start(printerId)` / `Stop(printerId)` / stav nahrávání tiskárny + událost `Changed(printerId)` (pro tray menu a okno kamery v T-146),
  - **automatické nahrávání během tisku:** začne při přechodu do `Printing`, skončí po `Finished` / `Stopped` / `Error` (podle otázky 2 i po `Offline`), chování při `Paused` / `Attention` podle otázky 3. Využít `Core/Events/PrintEventDetector` nebo stejná pravidla.
  - souběh ručního a automatického nahrávání (otázka 4),
  - název souboru: tiskárna + čas začátku + případně název tisku (`Core` už umí hezčí název tisku z T-078); jen bezpečné znaky pro souborové systémy Windows i macOS, bez kolizí,
  - **složka** (otázka 5) – při startu nahrávání se ověří, že existuje a jde do ní zapisovat; jinak stav chyby s důvodem, ne výjimka,
  - **limit velikosti a rotace:** po dokončení nahrávky (a při startu služby) se smažou nejstarší nahrávky LayerPeeku, dokud celkem nejsou pod limitem; mažou se **jen soubory, které vytvořil LayerPeek** (podle vzoru názvu ve vlastní složce), nikdy cizí soubory,
  - ochrana disku: při volném místě pod mezí (otázka 6) se nahrávání nezačne / ukončí s chybou,
  - nahrávka, která se nedala dokončit (chyba, pád), se při dalším startu označí / opraví podle kontejneru z T-144.
- **Nastavení** v `settings.json` (`AppSettings` / `PrinterDefinition` podle otázky 7), čtené volně jako ostatní sekce (chybná hodnota = výchozí hodnota s varováním, výchozí hodnoty se nezapisují). `System.Text.Json` jen přes source generator.
- Volitelně událost „nahrávání skončilo chybou“ pro notifikaci (otázka 8).
- Diagnostický balíček (T-048) **nesmí** obsahovat nahrávky.

## Akceptační kritéria
- [ ] Automatické nahrávání začne a skončí podle pravidel z rozhodnutí (testy se sekvencí snapshotů a falešnými hodinami pro všechny přechody včetně `Paused`, `Error`, `Offline` a restartu monitoringu).
- [ ] Ruční start / stop a souběh s automatickým nahráváním podle rozhodnutí (test).
- [ ] Rotace smaže nejstarší nahrávky LayerPeeku nad limitem a nikdy nesmaže cizí soubor ve stejné složce (test v dočasné složce).
- [ ] Nedostupná složka, plný disk (simulovaný) a nedostupný muxer (T-144) skončí stavem chyby s důvodem, bez pádu (test).
- [ ] Nastavení se načte, uloží a s chybnými hodnotami spadne na výchozí (testy s fixture `settings.json`).
- [ ] Testy po sobě uklidí všechny soubory i po pádu.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Režimy spouštění:** jen ručně, jen automaticky během tisku, nebo obojí s volbou u každé tiskárny? Výchozí hodnota (doporučení architekta k rozhodnutí: automatické nahrávání **vypnuté** ve výchozím stavu kvůli místu na disku a soukromí – rozhodne uživatel)?
2. **Konec automatické nahrávky při `Offline`:** ukončit hned, po toleranci (jako 1 min u vychladnutí, T-079), nebo nahrávat dál, dokud běží stream kamery (kamera má vlastní napájení / síť)?
3. **Pauza tisku (`Paused`, `Attention`):** nahrávat dál, pozastavit, nebo ukončit?
4. **Ruční nahrávání během automatického:** jedna nahrávka (ruční start jen „připne“ běžící), nebo dvě samostatné? Může ruční „Stop“ ukončit automatickou nahrávku?
5. **Složka:** výchozí umístění (např. Videa/LayerPeek na Windows `%USERPROFILE%\Videos\LayerPeek`, na macOS `~/Movies/LayerPeek`) a podsložka pro každou tiskárnu ano / ne? Volitelná vlastní složka?
6. **Výchozí limit velikosti a rotace:** limit celkové velikosti (GB), počtu nahrávek, nebo stáří (dní)? Kolik? Mez volného místa na disku? Podklad: datový tok změřený v T-144.
7. **Nastavení globálně, nebo u každé tiskárny?** (složka a limit spíš globálně, zapnutí automatického nahrávání spíš u tiskárny – rozhodne uživatel.)
8. **Notifikace** o začátku / konci / chybě nahrávání: žádná, jen při chybě, nebo volitelně vše?

### Rozhodnutí uživatele 2026-11-03
1. **Ruční i automatické** nahrávání; automatické nahrávání během tisku je ve výchozím stavu **vypnuté**. Tlačítko nahrávání má být i v okně kamery (T-146). Soubory na jednom místě, rozlišené podle tiskárny.
2. Konec tisku: nahrávat ještě **1 min** po konci tisku; `Offline` ukončí nahrávku po toleranci **1 min** (jako T-079).
3. `Paused` / `Attention`: **nahrávat dál**.
4. Ruční nahrávání během automatického: **jedna nahrávka** (ruční Start nic nepřidá); ruční Stop ukončí i automatickou a ta se do konce tisku znovu nespustí.
5. Složka: Windows `%USERPROFILE%\Videos\LayerPeek`, macOS `~/Movies/LayerPeek`, **podsložka pro každou tiskárnu**, volitelná vlastní složka v nastavení.
6. Limit: **50 GB celkem** (mazat nejstarší nahrávky), mez volného místa **5 GB** (pod ní se nahrávání zastaví s chybou); limit jde změnit v nastavení.
7. Nastavení **smíšeně**: složka a limit globálně, „Automaticky nahrávat tisk“ u každé tiskárny.
8. Notifikace **jen při chybě** nahrávání.

## Poznámky k implementaci
(vyplní agent po dokončení)

---
id: T-146
title: Nahrávání kamery – UI (tray menu, okno kamery, nastavení) a E2E
status: todo
owner: ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-145, T-033, T-092]
---

## Cíl
Uživatelské rozhraní nahrávání z fáze 1 (nápad 1): ruční spuštění a zastavení, indikace probíhající nahrávky, nastavení automatického nahrávání, složky a limitu, otevření složky s nahrávkami. Logika je v T-145.

## Rozsah
- **ui-designer – návrh (design spec v `docs/design/`, jako u T-076):**
  - kde je ruční „Nahrávat / Zastavit nahrávání“ (podmenu tiskárny v tray menu, kontextové menu okna kamery, tlačítko v okně kamery – otázka 1),
  - indikace probíhající nahrávky (tray ikona / tooltip, okno kamery, malý náhled – otázka 2),
  - umístění nastavení (záložka tiskárny Kamera / nová záložka „Nahrávání“ / obecné nastavení – otázka 3), bez posuvníku v 780 × 532 (T-094) a s dlouhými překlady,
  - stav „nahrávání není k dispozici“ (např. macOS před T-147) a chybové stavy z T-145 (složka, disk).
- **ui-dev – implementace:**
  - položky menu podle návrhu, s klávesovými zkratkami z `.resx`; text se mění podle stavu (jako „Zobrazit / Skrýt kameru“, T-042),
  - nastavení podle návrhu, výběr složky přes systémový dialog, „Otevřít složku s nahrávkami“,
  - všechny nové texty ve všech jazycích (`Strings.*.resx`, T-029),
  - compiled bindings, žádná reflexe (Native AOT).
- **Notifikace chyby nahrávání** (rozhodnutí 8 v T-145): `CameraRecordingService.Failed` (`CameraRecordingError`) přes `INotificationService`, texty podle `CameraRecordingProblem` ve všech jazycích; Core text nelokalizuje (API viz poznámky T-145).
- **Testy:** headless UI testy (T-031) pro nastavení a menu; E2E na Windows (UI Automation, T-044) proti simulátoru: ruční nahrávka z menu vytvoří soubor, automatická nahrávka během scénáře tisku simulátoru bez otevřeného okna kamery vytvoří soubor, indikace se zobrazí a zmizí.

## Akceptační kritéria
- [ ] Design spec schválený uživatelem (odpovědi na otázky 1–4 zapsané v úkolu).
- [ ] Ruční nahrávání jde spustit a zastavit z míst podle návrhu; stav je vidět podle návrhu.
- [ ] Automatické nahrávání během tisku běží bez otevřeného okna kamery (E2E se simulátorem).
- [ ] Nastavení se uloží a po restartu aplikace platí; chybové stavy (nedostupná složka) mají srozumitelnou hlášku.
- [ ] Na platformě bez muxeru (macOS před T-147) jsou volby zakázané s vysvětlením, aplikace nespadne.
- [ ] Všechny nové texty ve všech jazycích; okno nastavení bez posuvníku i s nejdelším překladem.
- [ ] E2E testy po sobě smažou nahrávky i po pádu (vlastní dočasná složka nahrávek).
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; Native AOT publish (Debug podle T-073) bez nových varování.

## Otevřené otázky
1. **Kde ovládat ruční nahrávání:** tray menu tiskárny, okno kamery (tlačítko / kontextové menu), obojí?
2. **Indikace nahrávání:** červená tečka / „REC“ v okně kamery a malém náhledu? Změna tray ikony nebo jen tooltip? Má být indikace vidět i ve snímku obrazovky okna (soukromí při sdílení obrazovky)?
3. **Umístění nastavení:** u tiskárny na záložce Kamera, nová podzáložka, nebo globální sekce na záložce Obecné (souvisí s otázkou 7 v T-145)?
4. **Otevření posledního záznamu** (přehrání) z menu / z notifikace – chce to uživatel ve fázi 1?

### Rozhodnutí uživatele 2026-11-03
1. Ruční Start/Stop v **tray menu tiskárny i tlačítkem v okně kamery**.
2. Indikace: červená tečka **REC** v okně kamery a malém náhledu, v tooltipu tray ikony „Nahrává se“; tray ikona se nemění.
3. Nastavení: složka a limit v nové sekci **Nahrávání na záložce Obecné**, „Automaticky nahrávat tisk“ u tiskárny na **záložce Kamera**.
4. Ve fázi 1 **ano**: „Otevřít složku s nahrávkami“ a „Přehrát poslední nahrávku“ (otevře se ve výchozí aplikaci systému).
5. Notifikace jen při chybě nahrávání (T-145, otázka 8).

## Poznámky k implementaci
(vyplní agent po dokončení)

---
id: T-072
title: E2E – jump list oken na hlavním panelu a druhé spuštění přes shell (popředí, blikání)
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-044]
---

## Cíl
Ověřit automaticky ruční kontroly T-043: jump list pomocných oken nabízí jen „Zavřít okno“ a druhé spuštění přes shell (dvojklik, zástupce) přenese naposledy použité okno do popředí, jinak otevře Nastavení. Když Windows popředí nepovolí, okno blikne. T-044 už ověřuje vlastnosti oken (PreventPinning + AUMID) a druhé spuštění přes `Process.Start`. Tento úkol přidá skutečný jump list a spuštění přes shell. Nastavení systému se nemění, takže úkol nepotřebuje varování T-065.

## Pokrytí
| Úkol | Kontrola | Test (návrh názvu) |
|---|---|---|
| T-043 | jump list okna kamery, nastavení, diagnostiky, O aplikaci: jen „Zavřít okno“, název „Prusa Tray Diagnostic“ | `JumpListOffersOnlyCloseWindow` |
| T-043 | „Zavřít okno“ z jump listu okno zavře | součást `JumpListOffersOnlyCloseWindow` |
| T-043 | druhé spuštění z jump listu | jump list nemá položku pro spuštění aplikace ani připnutí; test ověří, že tam není |
| T-043 | druhé spuštění dvojklikem / zástupcem s právem popředí → okno v popředí | `ShellStartWithForegroundRightsActivatesTheRecentWindow` |
| T-043 | druhé spuštění bez práva popředí → okno blikne | `ShellStartWithoutForegroundRightsFlashesTheRecentWindow` |
| T-043 | bez otevřeného okna → Nastavení | `ShellStartWithoutWindowsOpensTheSettings` |
| T-043 | skutečný klik v nabídce Start | **zůstává doporučená** (T-065: proces z Průzkumníka nemá přepínače testovacího režimu) |

## Rozsah
- **Jump list:** pro každé pomocné okno (kamera, nastavení, diagnostika, O aplikaci) najít jeho tlačítko na hlavním panelu přes UIA (prvek hlavního panelu Windows 11 s názvem okna / „Prusa Tray Diagnostic“). Testovací AUMID `PrusaTrayDiagnostic.E2E` okna seskupí do jednoho tlačítka – ověřit, že tlačítko testovací instance je oddělené od případné instance uživatele. Pravý klik skutečnou myší (jako tray v T-044), nabídka se přečte přes UIA:
  - přesně jedna položka akce (zavření okna / oken), žádná položka spuštění aplikace, žádné „Připnout na hlavní panel“, žádné úlohy ani nedávné položky;
  - porovnávat se nemá podle textu v jazyce Windows. Text „Zavřít okno“ se jen zapíše do výsledků, kontroluje se počet a druh položek (případně lokalizovaný text ze zdrojů shellu, pokud jde spolehlivě získat);
  - spuštění položky okno zavře;
  - Esc zavře nabídku; snímek nabídky.
- **Spuštění přes shell:** zástupce `.lnk` na testovaný exe v testovací složce (ne v nabídce Start), spuštěný přes `ShellExecuteEx` z testovacího procesu. Proces zdědí proměnné testovacího režimu a přípony instance. Je to stejná cesta jako dvojklik na zástupce, jen bez Průzkumníka.
  - **s právem popředí:** testovací proces nejdřív získá popředí (vlastní pomocné okno aktivované kliknutím skutečnou myší). Po druhém spuštění je naposledy použité okno aplikace v popředí (`GetForegroundWindow`) do 5 s;
  - **bez práva popředí:** popředí drží jiné okno (pomocné okno testu, aktivní a zamčené pro popředí bez vstupu uživatele). Aplikace nesmí převzít popředí. Test zaregistruje `RegisterShellHookWindow` a čeká na `HSHELL_FLASH` (0x8006) s HWND naposledy použitého okna. Pokud Windows okno přesto aktivují (pravidla popředí se liší podle sestavení), zapsat výsledek a ověřit aspoň, že okno je obnovené a nahoře;
  - **bez oken:** otevře se Nastavení;
  - po každém spuštění: druhý proces skončí s kódem 0 do 5 s, další ikona v tray nevznikne (počet ikon `[E2E]` přes `NotificationArea`).
- Úklid: zástupce jen v rezervované testovací složce; pomocná okna se zavřou v `finally`; žádný záznam v Nedávných (spouštět s `SEE_MASK_FLAG_NO_UI` a bez přidání do nedávných – ověřit, případně zapsat jako stopu shellu podobně jako MRU v T-064).

## Akceptační kritéria
- [ ] Jump list každého pomocného okna: jen zavření okna, bez spuštění a připnutí; položka okno zavře (test, snímky)
- [ ] Druhé spuštění přes shell s právem popředí aktivuje naposledy použité okno; bez práva okno blikne (`HSHELL_FLASH`); bez oken otevře Nastavení; žádný další proces ani tray ikona (testy)
- [ ] Testy po sobě nic nenechají (zástupce, pomocná okna, procesy)
- [ ] Kritéria T-043 „Pomocná okna mají PreventPinning + AUMID; jump list jen ‚Zavřít okno‘“ a „Ruční ověření…“ zaškrtnutá podle výsledků (klik v nabídce Start zůstává doporučená kontrola)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

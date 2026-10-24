---
id: T-072
title: E2E – jump list oken na hlavním panelu a druhé spuštění přes shell (popředí, blikání)
status: done
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
- [x] Jump list každého pomocného okna: jen zavření okna, bez spuštění a připnutí; položka okno zavře (test, snímky)
- [x] Druhé spuštění přes shell s právem popředí aktivuje naposledy použité okno; bez práva okno blikne (`HSHELL_FLASH`); bez oken otevře Nastavení; žádný další proces ani tray ikona (testy)
- [x] Testy po sobě nic nenechají (zástupce, pomocná okna, procesy)
- [x] Kritéria T-043 „Pomocná okna mají PreventPinning + AUMID; jump list jen ‚Zavřít okno‘“ a „Ruční ověření…“ zaškrtnutá podle výsledků (klik v nabídce Start zůstává doporučená kontrola)
- [x] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (žádné)

## Poznámky k implementaci

**Testy**
- `TaskbarJumpListTests.JumpListOffersOnlyCloseWindow`: postupně Nastavení, Diagnostika, O aplikaci a kamera (simulátor), vždy jen jedno okno. Tlačítko testovací aplikace na hlavním panelu se najde přes UIA podle automation id `Appid: PrusaTrayDiagnostic.E2E` (tlačítko uživatelovy instance má `Appid: PrusaTrayDiagnostic`, test na něj nesahá a ověří, že jeho přítomnost se nezměnila). Název tlačítka začíná „Prusa Tray Diagnostic“. Pravý klik skutečnou myší (ukazatel skočí na tlačítko, pohyb myši uživatelem se ohlásí jako taková chyba). Jump list je okno `Windows.UI.Core.CoreWindow` procesu ShellExperienceHost v popředí; kontroluje se, že má **přesně jednu položku** (list item) s automation id `Close` (seznam `SystemItemList`) – žádné spuštění aplikace, připnutí, úlohy ani nedávné položky. Text se jen zapíše do výsledků (`Close window`, název okna „Jump List for PrusaTrayDiagnostic“). Esc jump list zavře a okno zůstane; spuštění položky `Close` okno zavře a tlačítko zmizí. Snímky tlačítka (okraj 4 px) a seznamů jump listu (přesně jejich obdélník, po doběhnutí animace) jako výřez obrazovky – bez sousedních tlačítek a oken uživatele; zpráva `jump-list-report.txt`.
- `ShellStartTests` (zástupce `.lnk` přes `WScript.Shell` ve složce běhu aplikace, spuštěný `ShellExecuteEx` s `SEE_MASK_FLAG_NO_UI | NOCLOSEPROCESS | NOASYNC` na STA vlákně):
  - `ShellStartWithoutWindowsOpensTheSettings`: otevře se Nastavení.
  - `ShellStartWithForegroundRightsActivatesTheRecentWindow`: O aplikaci a Nastavení minimalizované; testovací proces získá popředí kliknutím skutečnou myší do vlastního pomocného okna (`ForegroundHolder`); po druhém spuštění jsou Nastavení obnovená a v popředí (`GetForegroundWindow`) do 5 s, O aplikaci zůstane minimalizované.
  - `ShellStartWithoutForegroundRightsFlashesTheRecentWindow`: okna se otevřou s popředím (test předá právo `AllowSetForegroundWindow`), aby žádné neblikalo už od otevření; pak pomocné okno testu získá popředí a zamkne ho (`LockSetForegroundWindow`), 1 s klidu bez `HSHELL_FLASH` pro Nastavení, druhé spuštění → `HSHELL_FLASH` (0x8006) s HWND Nastavení (přes `RegisterShellHookWindow`) a popředí zůstane pomocnému oknu. Výsledek na Windows 11 26300: blikne ~0,4 s po spuštění, aktivace neproběhla. Kdyby Windows okno přesto aktivovaly, test to zapíše a ověří aspoň obnovení a popředí.
  - po každém druhém spuštění: konec s kódem 0 do 5 s, jediný označený proces aplikace, záznam „Another instance is running“ s PID druhého spuštění v logu, počet ikon `[E2E]` v tray se nezvýšil, v Nedávných (`%APPDATA%\Microsoft\Windows\Recent`) nevznikla položka se jménem zástupce (ověřeno – `ShellExecuteEx` zástupce do Nedávných nezapisuje).
- **Bezpečnost proměnných:** proces spuštěný shellem dědí prostředí testovacího procesu; proměnné testovacího režimu (`…_TEST_DATA_DIR`, `…_INSTANCE_SUFFIX`, navíc `…_INSTANCE_FORWARD_ONLY=1`, přepínače T-067 odstraněné) se nastaví jen na dobu volání a pak vrátí. Než se takto spustí aplikace, test stejnou cestou spustí zástupce na `cmd.exe` (skrytě), který skončí 0 jen když vidí příponu instance – jinak by aplikace běžela v produkčním režimu nad daty uživatele. `FORWARD_ONLY` zaručí, že druhé spuštění se nikdy nestane druhou aplikací (kód 3).
- **Úklid:** zástupci jen ve složce běhu (`%TEMP%\PrusaTrayDiagnostic.App.E2E.Tests.<id>\run-…\shortcut`, maže se s ní; sonda `environment-probe.lnk` se maže hned), pomocné okno se zavře v `using` (a odemkne popředí), druhé spuštění se při chybě ukončí (`TerminateProcess` přes handle), jump list se po chybě zavře Esc. Po bězích v nabídce Start jen produkční zástupce, žádný proces aplikace, nic v Nedávných.
- Odolnost: Esc na jump list se opakuje (hned po otevření shell klávesy občas nebere); počítání ikon v tray zkusí znovu, když se přetečení napoprvé neotevře (jednou viděno v plném běhu; zapíše se jen text, ne snímek plochy).

**Ověření (Windows 11 26300, 2026-10-22):** `dotnet build` 0 varování; `dotnet test` 4674 testů, 0 selhání (42 přeskočených – E2E bez `PTD_E2E` a dřívější); `tools/run-e2e.ps1` zelené: 40 testů (1 přeskočený – skutečná kamera nedostupná). Jeden další plný běh selhal jen na kontrole „ukazatel myši opustil pomocné okno“ (uživatel během běhu použil myš); opakování třídy zelené.

**Zůstává ručně (doporučeno):** skutečný klik v nabídce Start a dvojklik v Průzkumníku – proces spuštěný Průzkumníkem nemá přepínače testovacího režimu a běžel by nad daty uživatele. Rozložení „nikdy neseskupovat“ hlavního panelu se netestovalo (test počítá s výchozím seskupováním, kde jedno okno = jedno tlačítko s `Appid:`).

## Review
**2026-10-22, qa-reviewer** – nálezy opravené v rámci review (žádný blokující nezůstal):
1. **Bezpečnost – proměnné prostředí po timeoutu (`ShellStart.StartAsync`):** po vypršení 30 s čekání se proměnné testovacího režimu vracely, zatímco `ShellExecuteEx` mohl proces ještě vytvořit – ten by běžel v produkčním režimu nad daty uživatele. Nastavení, volání shellu i vrácení proměnných teď proběhne celé na vlákně volajícím shell a další spuštění začne až potom; proces vytvořený po timeoutu se hned ukončí.
2. **Bezpečnost – klik skutečnou myší bez kontroly cíle:** pravý klik na tlačítko hlavního panelu šel na střed obdélníku z UIA i u prázdného/zastaralého obdélníku (přetečení panelu, automaticky skrytý panel, zakrytí oknem) – mohl trefit tlačítko nebo okno uživatele. Před klikem se ověří, že bod leží na `Shell_TrayWnd` a UIA tam najde tlačítko `Appid: PrusaTrayDiagnostic.E2E` (`TaskbarJumpList.EnsureClickHitsTestButton`); stejně u pomocného okna testu (`ForegroundHolder.ActivateWithMouseAsync`: `WindowFromPoint` musí vrátit jeho okno).
3. **Soukromí – snímky:** snímek tlačítka měl okraj 96 px (sousední tlačítka uživatele), snímek jump listu sjednocení s tlačítkem + 16 px (okna a tlačítka uživatele). Teď okraj 4 px a přesně obdélník seznamů jump listu, pořízený až po animaci (dřív zachycoval rozjetou nabídku).
4. **Soukromí – názvy oken ve zprávách:** zpráva o zprávách shellu a chyba „popředí je …“ vypisovaly titulky libovolných oken (i uživatele). Titulek se teď vypisuje jen u oken aplikace a testu; chyba popředí popisuje okno v okamžiku timeoutu (dřív v okamžiku volání).
5. **Esc naslepo:** opakované Esc v `JumpList.DismissAsync` a zavírání přetečení tray v `ShellStartTests` posílaly Esc oknu v popředí, i když nabídka mezitím zmizela (mohlo jít oknu uživatele). Esc se posílá jen, když je jump list v popředí / přetečení zobrazené. Jump list se navíc rozpozná i podle procesu `ShellExperienceHost` (ne nabídka Start apod.).

Ověření po opravách: `dotnet build` 0 varování; `dotnet test` 4674 testů, 0 selhání (41 přeskočených); `tools/run-e2e.ps1 -Class '*ShellStartTests'` (3/3), `'*TaskbarJumpListTests'` (1/1) a `'*SingleInstanceTests'` (1/1) zelené; po bězích žádný proces aplikace, nic v Nedávných, složky běhu smazané.

Zbytkové riziko (nezměněno, přijatelné): sonda prostředí ověřuje dědění proměnných na `cmd.exe`, ne na samotném exe – rozdíl by vznikl jen při zvláštním spouštění exe shellem (kompatibilitní shim, elevace), které Debug build nemá.

---
id: T-049
title: Podpora – kopírování informací do schránky
status: done
owner: ui-dev
priority: P3
platform: all
depends_on: [T-048]
---

## Cíl
Akce **„Kopírovat info do schránky“** vloží do schránky krátký text pro hlášení chyby (např. do GitHub issue nebo e-mailu): verzi aplikace, OS, stavy tiskáren a posledních N řádků logu. Doplňuje diagnostický balíček z T-048 tam, kde ZIP poslat nejde.

## Rozsah
- Umístění a vzhled podle návrhu ui-designera z T-048.
- Obsah: výstup `SupportInfo` z T-048, vždy anglicky, bez lokalizace (verze, OS, .NET, AOT, jazyk, FFmpeg, tiskárny a jejich stavy; redakce stejná jako v T-048) a pod ním **posledních 200 řádků** logu. Když je aktuální soubor po rotaci krátký, doplnit řádky z předchozího. Před čtením vyprázdnit frontu logu (`Flush`, T-024). Soubory číst se sdíleným přístupem. Celý text omezit na **přibližně 64 KB**: při překročení se vynechají nejstarší řádky logu, informace o prostředí zůstanou celé.
- Schránka přes Avalonii (`TopLevel.Clipboard.SetTextAsync`, kompatibilní s AOT). Po zkopírování krátká vizuální odezva v okně (např. text tlačítka „Zkopírováno“ na pár sekund), podle návrhu ui-designera.
- Čtení logu a sestavení textu mimo UI vlákno; na UI vlákně jen zápis do schránky.
- **Texty UI:** do `Strings.resx` a všech přeložených `Strings.*.resx` podle T-029.
- **Dokumentace:** doplnit `docs/troubleshooting.md` (z T-048) o kopírování do schránky.
- Bez nového NuGet balíčku.

## Akceptační kritéria
- [x] Akce vloží do schránky anglický text s informacemi o prostředí, stavy tiskáren a posledních 200 řádků logu, celkem nejvýš přibližně 64 KB
- [x] Test: text neobsahuje žádný tajný údaj z falešného `ISecretStore` ani přihlašovací údaje z adresy kamery
- [x] Testy čtení posledních 200 řádků a limitu 64 KB (dlouhé řádky, krátký aktuální soubor po rotaci, soubor zamčený zapisovatelem, prázdná nebo chybějící složka); testy po sobě nic nenechají
- [x] Headless UI test akce (falešná schránka) a okno bez scrollbaru ve všech jazycích
- [x] `docs/troubleshooting.md` doplněn
- [x] Texty ve všech jazycích
- [x] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Umístění a odezva (2026-10-22, návrh ui-designera z T-048, schválil uživatel):** tlačítko v okně O aplikaci mezi „Uložit diagnostický balíček…“ a „Otevřít složku s logy“; po zkopírování na 2 s „✓ Zkopírováno“ místo textu, šířka se nemění.
- **Obsah (2026-10-21):** verze, OS, stavy tiskáren, posledních N řádků logu, jako text.
- **Jazyk (2026-10-21):** vždy anglicky, bez lokalizace.
- **Rozsah logu (2026-10-21):** posledních 200 řádků, celý text nejvýš přibližně 64 KB.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Kód:** `App/Support`:
  - `LogTail`: posledních 200 řádků. Čte aktuální soubor, soubor právě přesouvaný rotací (`PrusaTrayDiagnostic.rotating.log`) a pak starší soubory, z každého jen posledních 64 KB, se sdílením `ReadWrite | Delete`. Neúplný první řádek rozsahu se zahodí; jeden bajt před rozsahem rozhodne, zda rozsah začíná celým řádkem. Za souborem delším než rozsah se už nečte, aby v textu nebyla mezera.
  - `SupportText`: `SupportInfo` celý, pod ním řádky logu, nejvýš 64 KB v UTF-8. Nejstarší řádky se vynechají a text uvede, kolik jich chybí.
  - `SupportActions.CopyInfoAsync`: text se sestaví mimo UI vlákno (po `Flush` logu) a na UI vlákně se jen zapíše do schránky (`ClipboardExtensions.SetTextAsync`). Nikdy nehází: schránku drženou jiným programem (na Windows timeout) zaloguje a okno ukáže chybu.
  - `AboutViewModel.CopyInfoCommand`: „Zkopírováno“ běží mimo příkaz, takže tlačítko zůstává aktivní a další klik odezvu restartuje (čítač generací).
- **Přístupnost:** oba vnitřní texty tlačítka jsou pro čtečky skryté (`AccessibilityView=Raw`). Název tlačítka (`CopyButtonName`) se přepne na „✓ Zkopírováno“ a změnu ohlásí (`LiveSetting=Polite`). Ověření s Předčítáním / VoiceOverem ručně zatím neproběhlo.
- **Testy:**
  - `LogTailTests`: 200 řádků, krátký soubor po rotaci, soubor přesouvaný rotací, dlouhé řádky, hranice 64 KB, rozsah začínající celým řádkem, soubor otevřený zapisovatelem, záznam bez konce řádku, prázdná / chybějící složka.
  - `SupportText` (limit 64 KB).
  - `SupportBundleTests.ClipboardTextHoldsNoSecret`: falešný `ISecretStore`, kamera s `user:pass@` a tokenem, log vytvořený během testu.
  - `AboutSupportTests`: zkopírováno na 2 s s `FakeTimeProvider`, opakovaný klik, chyba schránky.
  - Headless `AboutWindowLayoutTests`: skutečné tlačítko a schránka okna; stavy „Zkopírováno“ a „kopírování selhalo“ ve všech 8 jazycích; šířka tlačítka beze změny; snímek `about-support-copied.png`.
- Žádný nový NuGet balíček; AOT publish win-x64 bez varování (2026-10-22). App E2E zelené; `TrayMenuTests` jednou selhal souběžně s buildem reviewera a samostatně prošel.

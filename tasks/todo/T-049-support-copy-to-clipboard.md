---
id: T-049
title: Podpora – kopírování informací do schránky
status: todo
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
- [ ] Akce vloží do schránky anglický text s informacemi o prostředí, stavy tiskáren a posledních 200 řádků logu, celkem nejvýš přibližně 64 KB
- [ ] Test: text neobsahuje žádný tajný údaj z falešného `ISecretStore` ani přihlašovací údaje z adresy kamery
- [ ] Testy čtení posledních 200 řádků a limitu 64 KB (dlouhé řádky, krátký aktuální soubor po rotaci, soubor zamčený zapisovatelem, prázdná nebo chybějící složka); testy po sobě nic nenechají
- [ ] Headless UI test akce (falešná schránka) a okno bez scrollbaru ve všech jazycích
- [ ] `docs/troubleshooting.md` doplněn
- [ ] Texty ve všech jazycích
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Obsah (2026-10-21):** verze, OS, stavy tiskáren, posledních N řádků logu, jako text.
- **Jazyk (2026-10-21):** vždy anglicky, bez lokalizace.
- **Rozsah logu (2026-10-21):** posledních 200 řádků, celý text nejvýš přibližně 64 KB.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci

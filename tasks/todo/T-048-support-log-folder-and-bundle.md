---
id: T-048
title: Podpora – otevření složky s logy a uložení diagnostického balíčku (ZIP)
status: todo
owner: core-dev + ui-designer + ui-dev
priority: P2
platform: all
depends_on: [T-024, T-041]
---

## Cíl
Uživatel při hlášení chyby snadno předá logy a základní informace o prostředí. Úkol přidává dvě akce: **„Otevřít složku s logy“** (otevře složku v Průzkumníku / Finderu) a **„Uložit diagnostický balíček“** (ZIP s logy, verzí aplikace, informacemi o OS a nastavením bez tajných údajů; uživatel zvolí, kam ho uložit). Třetí akci „Kopírovat info do schránky“ implementuje T-049. Její umístění ale navrhne ui-designer už tady, aby všechny tři akce tvořily jeden celek.

Součástí je i uživatelský návod „jak získat logy při hlášení chyby“.

## Rozsah
- **Návrh umístění (ui-designer):** kam patří všechny tři podpůrné akce (okno O aplikaci, okno Diagnostika, nebo obojí), jejich pořadí, texty a vzhled (tlačítka / odkazy). Okno Diagnostika má pevnou velikost bez scrollbaru (T-041) a to musí platit ve všech 8 jazycích. Výstup: návrh v Poznámkách k implementaci a layout-only XAML.
- **Informace o prostředí (core-dev):** sestavovač textu `SupportInfo`. Návrh umístění v App, např. `App/Support/`; Core ho nepotřebuje. Obsah: název a verze aplikace (stejný zdroj jako okno O aplikaci), OS a verze (`RuntimeInformation.OSDescription`, architektura), verze .NET, příznak Native AOT, jazyk UI, dostupnost a verze FFmpeg (`FFmpegCameraFeedFactory.LibraryVersion`, chyba načtení jen jako typ), počet tiskáren a stav každé z nich (typ připojení, stav monitoringu, poslední chyba bez tajných údajů). U tiskáren se uvádí název, adresa (hostname / IP) a uživatelské jméno PrusaLink; adresy kamer bez přihlašovacích údajů **a bez dotazové části**. Text je vždy anglicky, bez lokalizace. `SupportInfo` použije i T-049.
- **Otevřít složku s logy:** složku z T-024 vytvořit, pokud neexistuje, a otevřít ji ve správci souborů. Windows: `explorer.exe` se složkou. macOS: `/usr/bin/open` s absolutní cestou (viz T-052/L4). Dnešní `IUrlLauncher` přijímá jen `http(s)` adresy. Doplnit samostatnou metodu, nebo malé rozhraní pro otevření složky, s testovatelnou tvorbou `ProcessStartInfo` jako u `ProcessUrlLauncher.CreateStartInfo`. Selhání zalogovat, aplikace nespadne.
- **Uložit diagnostický balíček:**
  - Výběr cíle přes `StorageProvider.SaveFilePickerAsync`. Výchozí název např. `PrusaTrayDiagnostic-diagnostika-20261021-140312.zip`, filtr `*.zip`.
  - Před sestavením vyprázdnit frontu logu (`Flush` z T-024). Logy číst se sdíleným přístupem (`FileShare.ReadWrite | FileShare.Delete`), protože do nich aplikace dál zapisuje.
  - Obsah: všechny soubory z `logs`, `info.txt` (výstup `SupportInfo`), `settings.json` **bez tajných údajů**. Nastavení se nekopíruje jako surový soubor: sestaví se znovu z načteného `AppSettings` přes existující mapper a source-generated JSON kontext a projde redakcí. Adresy kamer bez přihlašovacích údajů (`UriRedaction`) a bez dotazové části (`?…`, může nést token). Adresy tiskáren, názvy tiskáren a uživatelské jméno PrusaLink zůstávají. Logy se kopírují beze změny, bez anonymizace. Do balíčku nikdy nejde obsah `ISecretStore`, `camera-windows.json` ani `printer-images/`.
  - ZIP přes `System.IO.Compression.ZipArchive` (součást BCL, kompatibilní s AOT). Zápis mimo UI vlákno. Při chybě se nedokončený soubor smaže a uživatel dostane srozumitelnou hlášku v okně.
- **Texty UI:** do `Strings.resx` a všech přeložených `Strings.*.resx` podle T-029.
- **Uživatelská dokumentace:** nový `docs/troubleshooting.md` (česky): kde jsou logy (Windows `%LOCALAPPDATA%\PrusaTrayDiagnostic\logs`, macOS `~/Library/Logs/PrusaTrayDiagnostic`, z T-024), jak zapnout podrobné logování (T-047), jak uložit diagnostický balíček a co obsahuje (a co ne). Odkaz z `README.md`. T-049 návod doplní o kopírování do schránky.
- Bez nového NuGet balíčku. Kompilované bindingy (AOT).

## Akceptační kritéria
- [ ] Návrh ui-designera pro všechny tři akce (vč. umístění akce z T-049) zapsaný v Poznámkách k implementaci; okna bez scrollbaru ve všech 8 jazycích (headless UI testy)
- [ ] „Otevřít složku s logy“ otevře složku (i když ještě neexistovala); testy tvorby `ProcessStartInfo` pro Windows i macOS; ručně ověřeno na Windows
- [ ] „Uložit diagnostický balíček“ vytvoří ZIP ve zvoleném místě s logy, `info.txt` a redigovaným `settings.json`; zrušený výběr nic nevytvoří
- [ ] Test: balíček sestavený s falešným `ISecretStore` obsahujícím známá hesla, API klíče a přihlašovací údaje kamer a s kamerou s `user:pass@` v adrese neobsahuje v žádném souboru (včetně logů vytvořených během testu) žádný z těchto řetězců
- [ ] Testy `SupportInfo` (anglický text nezávislý na jazyku UI) a redakce nastavení: adresy kamer bez přihlašovacích údajů a dotazové části, adresy tiskáren, názvy a uživatelské jméno PrusaLink zachované; testy po sobě nic nenechají
- [ ] `docs/troubleshooting.md` existuje a je odkázaný z `README.md`
- [ ] Texty ve všech jazycích
- [ ] Žádný nový NuGet balíček; `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Rozhodnuto
- **Podpůrné akce (2026-10-21):** otevřít složku s logy, uložit diagnostický ZIP (umístění volí uživatel přes `StorageProvider`), kopírovat info do schránky (T-049); umístění v oknech O aplikaci a/nebo Diagnostika určí ui-designer.
- **Údaje tiskáren v balíčku a v `info.txt` (2026-10-21):** adresy tiskáren (hostname / IP), názvy tiskáren a uživatelské jméno PrusaLink se ponechávají. U adres kamer se odstraní přihlašovací údaje i dotazová část. Logy se neanonymizují.
- **Jazyk `info.txt` a textu pro podporu (2026-10-21):** vždy anglicky, bez lokalizace.

## Otevřené otázky
- (žádné; umístění akcí v oknech určí ui-designer)

## Poznámky k implementaci

---
id: T-116
title: macOS – obrázky tiskáren v menu v menu baru
status: done
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-018, T-039, T-042]
---

## Cíl
Obrázky tiskáren z T-039 se v menu LayerPeeku zobrazí i na macOS (nativní `NSMenu` v menu baru), stejně jako na
Windows. Vzniklo rozhodnutím uživatele k otevřené otázce T-018 „Ikony tiskáren v menu na macOS (T-039)“ (2026-10-30:
samostatný úkol).

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- Zapnout `TrayMenuOptions.PrinterIcons` i na macOS: dnes `ForCurrentPlatform` nastavuje
  `PrinterIcons = OperatingSystem.IsWindows()` (`src/LayerPeek.App/Tray/Menu/TrayMenuEntry.cs`, rozhodnutí
  2026-10-19 „macOS až po ověření v T-018“); `TrayHost` pak vytvoří `PrinterMenuIcons` podle měřítka systému.
- Zkontrolovat vykreslení v nativním `NSMenu` (Avalonia `NativeMenuItem.Icon`): velikost obrázku v bodech, ostrost
  na displeji @1x i @2x (Retina), světlý i tmavý režim menu a zvýrazněná položka. Obrázky tiskáren jsou barevné, ne
  template – macOS je nesmí přebarvit; ověřit, že jsou čitelné na světlém i tmavém pozadí menu.
- Pokud se velikost nebo měřítko na macOS liší od Windows (např. `SystemScaling()` na macOS), upravit výpočet
  velikosti jen pro macOS a izolovat ho jako dosud (bez reflexe, bez nového balíčku).
- Windows chování beze změny.

## Akceptační kritéria
- [x] `TrayMenuOptions.ForCurrentPlatform.PrinterIcons` je `true` na Windows i macOS; test
      `TrayMenuIconTests` upravený (dnes očekává `OperatingSystem.IsWindows()`)
- [x] Velikost obrázku pro `NSMenu` (body a pixely @1x / @2x) je ověřená testem (unit nebo headless UI test)
- [x] Ruční kontrola na Macu: obrázky tiskáren v menu v menu baru jsou ostré na displeji @1x i @2x, čitelné ve
      světlém i tmavém režimu i u zvýrazněné položky, nerozbijí zarovnání položek (podmenu tiskárny podle T-042)
      → přesunuto do T-115
- [x] Windows chování z T-039 / T-042 zůstává beze změny
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **Jak Avalonia 12.1.4 velikost obrázku v `NSMenu` určuje** (ověřeno ve zdroji balíčku Avalonia.Native 12.1.4 v NuGet
  cache, disassembler `otool` / IL řízené části):
  - Řízená část (`__MicroComIAvnMenuItemProxy.UpdateIcon`) bitmapu `NativeMenuItem.Icon` jen uloží jako PNG
    (`IBitmap.Save` s `PngBitmapEncoderOptions.Default`) a předá bajty do `IAvnMenuItem.SetIcon`; velikost nemění.
  - Nativní část (`AvnAppMenuItem::SetIcon` v `libAvaloniaNative.dylib`, arm64 i x86_64 stejně): `NSImage
    initWithData:` z PNG, výška = `floor([[NSFont menuFontOfSize:0] pointSize] × 1.333333)`, šířka = `floor(šířka ×
    výška / původní výška)`, `[image setSize:]` v **bodech** a `setImage:` na `NSMenuItem`. Obrázek se tedy vždy
    roztáhne / zmenší na pevnou velikost v bodech bez ohledu na počet pixelů a není template (barvy zůstanou, AppKit ho
    nepřebarví). Má jedinou reprezentaci – na každém displeji se kreslí z téhož PNG.
  - Výchozí písmo menu má 14 bodů (ověřeno na macOS 27.2: `NSFont.menuFont(ofSize: 0).pointSize` = 14), obrázek je
    tedy **18 × 18 bodů**: 18 px na displeji @1x, 36 px na Retině @2x. Velikost v bodech určuje Avalonia, ne
    LayerPeek – rozhodnutí o velikosti nebylo potřeba.
- **Výpočet velikosti** je nově v `PrinterMenuIconSize` (`src/LayerPeek.App/Tray/Menu/PrinterMenuIconSize.cs`):
  `Windows(scaling)` = 16 DIP jako dosud (16 / 20 / 24 / 32 px), `MacOS(písmo menu, nejvyšší měřítko displejů)` =
  `floor(písmo × 1.333333)` bodů. Pixely vybírá stejné pravidlo jako jinde (`PrinterIllustrations.SelectPixelSize`:
  nejmenší předrenderovaná velikost s aspoň tolika pixely, obrázek se nikdy nezvětšuje): @1x 18 → **20 px**
  (zjednodušená kresba), @2x 36 → **48 px** (podrobná kresba), AppKit je zmenší na 18 / 36 px. Vlastní obrázek
  tiskárny se ořízne do čtverce téže velikosti. `PrinterMenuIcons` dostane `PrinterMenuIconSize` (konstruktor
  s měřítkem zůstal pro Windows).
- **Měřítko na macOS:** jedno PNG pro všechny displeje, proto se bitmapa dělá pro nejvyšší `backingScaleFactor`
  ze `[NSScreen screens]` (na @1x monitoru vedle Retiny ji AppKit zmenší). Bez displeje (T-099, start se zhasnutým
  displejem) nebo bez AppKitu platí 2.0, bez písma 14 bodů. Čte `MacMenuMetrics`
  (`src/LayerPeek.App/Platform/MacOS/MacMenuMetrics.cs`) přes `ObjCRuntime` – přibyly dva tvary `objc_msgSend`
  (`SendWithCGFloat` pro `menuFontOfSize:`, `SendCGFloat` pro `pointSize` / `backingScaleFactor`; na arm64 i x86_64 vrací
  `CGFloat` obyčejný `objc_msgSend`). Velikost se spočítá jednou při vytvoření tray menu (`TrayHost.MenuIconSize()`),
  stejně jako na Windows systémové DPI; připojení jiného monitoru za běhu ji nezmění.
- `TrayMenuOptions.ForCurrentPlatform.PrinterIcons` = Windows nebo macOS. Windows: beze změny (16 DIP, systémové
  měřítko, stejné bitmapy; testy T-039 / T-042 beze změny prochází).
- **Testy:** `PrinterMenuIconSizeTests` (Windows 16 DIP beze změny, body z písma 13–16 pt, @1x / @2x / @3x → 20 / 48 /
  56 px, náhradní hodnoty, kontrola verze Avalonia 12.1.4 a koeficientu – při aktualizaci Avalonie znovu ověřit
  `AvnAppMenuItem::SetIcon`), `MacMenuMetricsTests` (čtení bez pádu; v testovacím procesu AppKit obvykle není načtený),
  `TrayMenuIconUiTests.PrinterItemsGetAnIconForTheMacMenu` (skutečné `NativeMenuItem.Icon` s velikostí pro macOS
  @1x / @2x, ilustrace i vlastní obrázek, podmenu bez obrázku), `TrayMenuIconTests` upravený. Snímky UI se nezměnily
  (menu v menu baru snímky nemá); Windows snímky k přegenerování v T-114 nejsou.
- **Ruční kontrola** (ostrost @1x / @2x, světlý / tmavý režim, zvýrazněná položka, zarovnání) je v T-115 (otázka
  přidaná do kontroly `mac.menu` v `tools/macos-verify.ps1` a krok v `docs/macos-manual-testing.md`, sezení 2.3).
  Aplikace se v agentovi nespouštěla (pravidlo sandboxu: žádné spuštění mimo testovací bundle) – vykreslení v menu
  baru zatím nikdo neviděl. Kdyby zmenšení 48 → 36 px (nebo 20 → 18 px na @1x) bylo vidět rozmazané, řešením jsou
  předrenderované velikosti 18 / 36 px (rozhodnutí ui-designer / uživatel, nový úkol).
- Žádná nová závislost; vše přes `[LibraryImport]` do `libobjc` (Native AOT bez varování, bez reflexe).
- Uzavřeno 2026-10-30: zbývající ruční ověření přesunuto do T-115 (rozhodnutí uživatele).

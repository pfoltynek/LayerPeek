---
id: T-116
title: macOS – obrázky tiskáren v menu v menu baru
status: todo
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
- [ ] `TrayMenuOptions.ForCurrentPlatform.PrinterIcons` je `true` na Windows i macOS; test
      `TrayMenuIconTests` upravený (dnes očekává `OperatingSystem.IsWindows()`)
- [ ] Velikost obrázku pro `NSMenu` (body a pixely @1x / @2x) je ověřená testem (unit nebo headless UI test)
- [ ] Ruční kontrola na Macu: obrázky tiskáren v menu v menu baru jsou ostré na displeji @1x i @2x, čitelné ve
      světlém i tmavém režimu i u zvýrazněné položky, nerozbijí zarovnání položek (podmenu tiskárny podle T-042)
- [ ] Windows chování z T-039 / T-042 zůstává beze změny
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

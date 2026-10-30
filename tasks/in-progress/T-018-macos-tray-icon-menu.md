---
id: T-018
title: macOS – ikona a menu v menu baru
status: in-progress
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-007, T-008, T-095]
---

## Cíl
Ikona stavu (T-007) a tray menu (T-008) fungují a jsou čitelné v menu baru macOS.

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

## Rozsah
- Monochromatická „template“ sada ikon pro menu bar (macOS ji sám přebarví pro světlý/tmavý režim), napojená na mapování stavu z T-007
- Výběr sady ikon podle OS (Windows barevná sada z T-007, macOS template sada)
- Ověření tooltipu a menu z T-008 v menu baru

## Akceptační kritéria
- [x] Na macOS je ikona čitelná ve světlém i tmavém režimu → přesunuto do T-115
- [x] Ikona se mění podle stavu z monitorovací služby i na macOS (automaticky: headless test `MenuBarTrayIconUiTests` na macOS; vzhled v reálném menu baru viz kritérium 1)
- [x] Menu z T-008 se v menu baru zobrazí, aktualizuje a „Ukončit“ korektně ukončí aplikaci → přesunuto do T-115
- [x] Windows chování z T-007 zůstává beze změny → přesunuto do T-114
- [x] `dotnet build` a `dotnet test` bez chyb a varování (na macOS i Windows) – macOS ověřeno 2026-10-29 (poznámky: build 0 varování, `dotnet test` 7099 testů bez selhání), zbývá Windows → přesunuto do T-114

## Návrh UI
Úplná specifikace: [`docs/design/macos-tray-icons.md`](../../docs/design/macos-tray-icons.md); SVG zdroje:
`src/LayerPeek.App/Assets/AppIcon/tray/macos/menubar-*.svg` (`ui-designer`, 2026-10-29).

- Plátno 18 × 18 pt (Avalonia nastaví výšku `floor(14 pt × 4/3)` = 18 pt), dodat jednu bitmapu 36 × 36 px; motiv 16 pt
  vysoký, hrany na celých pt; jen černá + alfa (1,0 / 0,35 / 0,2); `MacOSProperties.SetIsTemplateIcon(trayIcon, true)`.
- Základní tvar = design B „Layers“ (hotend + 3 vrstvy), progress plní vrstvy po celých pt (48 pt = 100 %).
- Symbol v rohu (box 8 × 7 pt vpravo nahoře):

```
Idle        Busy        Printing p% Paused p%   Finished    Attention   Error       Offline
 ▀█▀         ▀█▀ •••     ▀█▀ ▶       ▀█▀ ❚❚      ▀█▀ ✓       ▀█▀ !       ▀█▀ ×        ╲ (lomítko přes
 ░░░░░░░     ░░░░░░░     ░░░░░░░     ░░░░░░░     ███████     ░░░░░░░     ███████      ╲ ztlumený
 ░░░░░░░     ░░░░░░░     ██░░░░░     ██░░░░░     ███████     ░░░░░░░     ███████       ╲ motiv)
 ░░░░░░░     ░░░░░░░     ███████     ███████     ███████     ░░░░░░░     ███████
```
  (█ = alfa 1,0, ░ = track 0,35)
- Vykreslení za běhu kódem (Avalonia `DrawingContext` → `RenderTargetBitmap` → `WindowIcon`), stejně jako
  `ColorTrayIconSet`; žádná SVG knihovna ani nový balíček.

## Otevřené otázky
- ~~Progress jako text vedle ikony~~ → ne, jen ikona; průběh je v tooltipu a v menu jako na Windows (rozhodnuto 2026-10-29).
- ~~Rozlišení stavů v monochromatické ikoně~~ → stejný základní tvar ikony, stav označí malý symbol v rohu (např. tečka = tiskne, vykřičník = chyba / pozornost, prázdné = nečinná, přeškrtnutí = odpojeno); template ikona, kterou macOS přebarví pro světlý i tmavý režim. Návrh symbolů připraví `ui-designer` (rozhodnuto 2026-10-29).
- ~~**Symbol pro tisk:** (A) ▶ „play“ / (B) plná tečka ●~~ → **A** ▶ (rozhodnuto 2026-10-29).
- ~~**Chyba vs. pozornost:** (A) chyba ×, pozornost ! / (B) obě !~~ → **A** chyba ×, pozornost ! (rozhodnuto 2026-10-29).
- ~~**Vrstvy u chyby:** (A) plné / (B) prázdné~~ → **A** plné vrstvy (rozhodnuto 2026-10-29).
- ~~**Přeškrtnutí u offline:** (A) lomítko přes celou ztlumenou ikonu / (B) malé − v rohu~~ → **A** diagonální lomítko
  přes celou ztlumenou ikonu (rozhodnuto 2026-10-29).
- ~~**Ostrost na displejích @1x:** (A) jedna 36px bitmapa / (B) vícerozlišovací obrázek~~ → **A** jedna 36px bitmapa
  (rozhodnuto 2026-10-29); vícerozlišovací obrázek (18 + 36 px) jen jako záloha, pokud se ikona na skutečném Macu
  s displejem @1x ukáže rozmazaná.
- **Ikony tiskáren v menu na macOS (T-039):** `TrayMenuOptions.PrinterIcons` je zatím jen pro Windows s poznámkou
  „macOS (nativní NSMenu) až po ověření v T-018“ (rozhodnutí 2026-10-19). Rozsah T-018 to nezmiňuje, proto jsem to
  neměnil. Mají se obrázky tiskáren v NSMenu zapnout (a ověřit) v rámci T-018, nebo v samostatném úkolu?

## Poznámky k implementaci
- 2026-10-30: zbývající ruční ověření přesunuto do T-114 a T-115 (rozhodnutí uživatele). Úkol zůstává rozpracovaný kvůli nezodpovězeným otevřeným otázkám (viz Otevřené otázky).

**Stav závislostí:** T-095 je zatím `in-progress` – čeká už jen na ověření na Windows; uživatel požádal pokračovat
s T-018 i tak (2026-10-29).

**Implementace (macOS, 2026-10-29):**
- `Tray/Icons/MenuBarIconAppearance.cs` – mapování `TrayIconKey` → vzhled (alfa motivu, alfa tracku, vyplnění,
  symbol `MenuBarSymbol`); stejné stavy a kroky progressu jako Windows (T-007).
- `Tray/Icons/MenuBarIconRenderer.cs` – kreslení kódem podle `docs/design/macos-tray-icons.md`: Avalonia
  `DrawingContext` do `RenderTargetBitmap` 36 × 36 px (mřížka 18 pt × 2 přes `PushTransform`), jen černá + alfa
  1,0 / 0,35 / 0,2; vrstvy po celých pt (`floor(48 × progress)`, spodní první), track jen pro nevyplněnou část. Offline:
  motiv kreslen s ořezem `PushGeometryClip` (plocha minus pás 3,6 pt kolem lomítka, `GetWidenedGeometry`), pak
  lomítko 1,5 pt. Bez SVG knihovny, bez reflexe, bez nového balíčku.
- `Tray/Icons/TemplateTrayIconSet.cs` – `ITrayIconSet` s cache podle klíče (`TrayIconCache`), `IsTemplate = true`.
- `ITrayIconSet.IsTemplate` (nové), `ColorTrayIconSet` vrací `false` a vystavuje `MultiSize` (jen kvůli testu výběru;
  kreslení beze změny).
- `Tray/Icons/TrayIconSets.cs` – výběr podle OS v composition rootu: macOS → `TemplateTrayIconSet`, Windows →
  `ColorTrayIconSet(multiSize: true)` jako dosud, jiný OS → barevná 32px bitmapa. `Prepare` volá
  `MacOSProperties.SetIsTemplateIcon(trayIcon, true)` jen pro template sadu; na Windows se na `TrayIcon` nic nenastavuje.
  Avalonia (`trayicon.mm`) uplatní příznak na aktuální i každý další obrázek, pořadí vůči nastavení ikony je tedy jedno.
- `Composition/TrayHost.cs` – používá `TrayIconSets.ForCurrentPlatform()` + `Prepare`.
- Pozn.: statická záložní ikona z `App.axaml` (barevná ikona aplikace) se na macOS po nastavení příznaku zobrazí jako
  silueta; uplatní se jen do prvního vykreslení stavu nebo když vykreslení selže.

**AOT a licence:** žádný nový NuGet balíček; použitá API (`RenderTargetBitmap`, `StreamGeometry`, `CombinedGeometry`,
`MacOSProperties`) jsou součást Avalonia 12.1.4 (MIT), bez reflexe. `dotnet publish src/LayerPeek.App -c Release -r osx-arm64`
(do scratchpadu): úspěch, 0 varování MSBuild (jen známé hlášky nativního linkeru / dsymutil jako v T-095). Exe nespuštěno.

**Testy (na macOS 27.2, arm64):**
- `tests/LayerPeek.App.Tests/Tray/MenuBarIconAppearanceTests.cs` (28) – symbol pro každý stav, unikátní symboly, alfy,
  vyplnění po celých pt (shodné s SVG 10/35/70/95 % a pauza 50 %), každý 5% krok ≥ 2 pt.
- `tests/LayerPeek.App.Tests/Tray/TrayIconSetsTests.cs` (4) – Windows = barevná multi-size sada, macOS = template sada.
- `tests/LayerPeek.App.UiTests/Tray/MenuBarIconRendererTests.cs` (32) – snapshoty 16 stavů/kroků v
  `Snapshots/macOS/MenuBarIcons/` (jen na macOS, jinde přeskočeno – sada se používá jen na macOS), černá + alfa,
  ostrý motiv mimo symbol, mezery kolem symbolu, vrstvy po celých pt, offline ztlumený s vyříznutou mezerou, každé dva
  stavy se liší, cache podle klíče.
- `tests/LayerPeek.App.UiTests/Tray/MenuBarTrayIconUiTests.cs` (2) – zapojení jako v `TrayHost` (sada → `Prepare` →
  `TrayIconPresenter` → `AvaloniaTrayIconTarget` → `TrayIcon`): příznak template, ikona a tooltip se mění podle snímků
  monitoru (tisk 42 % → klíč 40 %, chyba); barevná sada příznak nenastaví.
- Menu T-008 (položky, aktualizace, tooltip tiskárny, „Ukončit“ volá příkaz) pokrývají existující headless testy
  `TrayMenuTests` a další `TrayMenu*UiTests`, které na macOS procházejí (T-095); `QuitCoordinatorTests` pokrývá ukončení.
- Vygenerované PNG jsem porovnal s náhledy návrháře (`glyphs/*@2x.png`, 36 px): 12 stavů pixelově shodných (rozdíl
  alfy 0), offline max. rozdíl alfy 11/255 na hraně ořezu (vyhlazení), vizuálně shodné.
- `dotnet build` 0 varování, 0 chyb; celý `dotnet test` 7099 testů, 0 neúspěšných (223 přeskočených – ty existující,
  bez Edge/FFmpeg/Windows). Kontrola profilu (`tools/profile-snapshot.ps1 -Save/-Compare`): beze změny.

**Neověřeno (vyžaduje člověka na skutečném Macu / Windows):**
- Debug aplikaci v testovacím režimu se ze sandboxu agenta nepodařilo spustit: `Avalonia.Native` padá už při
  inicializaci platformy (`was not able to start the RenderTimer`, kód -6661) – před jakýmkoli kódem T-018, zřejmě
  bez přístupu k okennímu serveru. Snímek menu baru navíc nebyl možný (chybí oprávnění Nahrávání obrazovky). Data jen
  ve scratchpadu, profil beze změny.
- Kroky pro ruční ověření na Macu (kritéria 1 a 3; souhrnně v [`docs/macos-manual-testing.md`](../../docs/macos-manual-testing.md); nejdřív skriptem `tools/macos-verify.ps1` – kontroly `mac.icons`, `mac.menu`, `mac.menubar`, `mac.build`, `mac.test`, T-105, sezení 2):
  1. `LAYERPEEK_TEST_DATA_DIR=<prázdná složka> LAYERPEEK_INSTANCE_SUFFIX=t018 dotnet run --project src/LayerPeek.App`
     (volitelně simulátor `tools/PrusaSimulator`, `docs/simulator.md`, a tiskárna přidaná v Nastavení).
  2. Ve světlém i tmavém režimu (Nastavení systému → Vzhled) zkontrolovat ikonu v menu baru: bez tiskárny lomítko přes
     ztlumenou ikonu; se simulátorem nečinná / ▶ s plněním vrstev / ❚❚ / ✓ / ! / ×. Ikona je bílá na tmavém a černá na
     světlém menu baru a obrácená při otevřeném menu.
  3. Na displeji @1x (externí monitor bez Retiny) zkontrolovat ostrost; při rozmazání přejít na zálohu (vícerozlišovací
     obrázek).
  4. Najetí myší: tooltip (stav tiskárny); kliknutí: menu z T-008 (tiskárny, Diagnostika…, Nastavení…, O aplikaci,
     Ukončit), položky se aktualizují při změně stavu; „Ukončit“ aplikaci ukončí a ikona zmizí.
- Windows (kritéria 4 a 5): spustit `dotnet build` a `dotnet test` na Windows; Windows snapshoty
  (`Snapshots/TrayIcons/`) se nezměnily, kód barevné sady kreslí beze změny, `MenuBarIconRendererTests.IconMatchesReference`
  se tam přeskočí.


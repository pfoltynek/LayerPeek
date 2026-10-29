# Ikony stavu v menu baru macOS (T-018)

Návrh monochromatické sady „template“ ikon pro menu bar macOS. Doplňuje barevnou sadu pro Windows
(`ColorTrayIconSet`, design B „Layers“ z T-025, mapování stavů z T-007). Žádná nová logika stavů: stejné
`TrayIconKey` (stav + progress zaokrouhlený dolů na 5 %), jen jiný vzhled.

Rozhodnutí uživatele (2026-10-29), ze kterých návrh vychází:

- v menu baru je **jen ikona**, žádný text; progress zůstává v tooltipu a v menu,
- stavy se liší **stejným základním tvarem + malým symbolem v rohu**,
- je to **template image**: jen černá + alfa, macOS ji sám přebarví pro světlý/tmavý menu bar i pro zvýrazněný stav.

## Rozměry a pixelová mřížka

| Parametr | Hodnota | Zdroj |
|---|---|---|
| Výška menu baru | 24 pt (u MacBooků s výřezem víc) | Apple HIG, *The menu bar → Menu bar extras*: „The menu bar's height is 24 pt.“ |
| Výška status itemu | 22 pt (`NSStatusBar.systemStatusBar.thickness`, změřeno na macOS 27.2) | AppKit; Bjango: pracovní plocha extras je pevně 22 pt |
| Výška obrázku, kterou nastaví Avalonia | `floor(menuFont × 1.333333)` = `floor(14 × 1.333)` = **18 pt** (šířka dle poměru stran) | `Avalonia.Native` `trayicon.mm`, `SetIcon`; `NSFont.menuFontOfSize(0)` = 14 pt (změřeno) |
| **Plátno ikony** | **18 × 18 pt**, dodáno jako **jedna bitmapa 36 × 36 px** (@2x) | viz níže |
| Optická výška motivu | 16 pt (řádky 1–17), 1 pt volno nahoře a dole | Bjango: 16 pt odpovídá vizuální váze systémových položek |
| Barvy | jen černá `#000` s alfou 1,0 / 0,35 / 0,2 | HIG: „black and clear colors“; Bjango: 35 % = Apple „disabled“ |

**Proč jedna 36px bitmapa:** Avalonia na macOS předává ikonu jako jeden obrázek (PNG z `WindowIcon`) a sama mu
nastaví velikost 18 pt. 36 px tak na Retina displeji (@2x) sedí 1 : 1. Na obrazovce @1x macOS bitmapu zmenší 2 : 1;
protože všechny hrany motivu leží na celých pt (= sudých pixelech @2x), zmenšení je bezeztrátové a ostré. Jen
diagonály (✓, ×, ▶, lomítko) a kulaté tečky jsou vyhlazené – to je u systémových symbolů normální.

Pokud by se v budoucnu velikost písma menu změnila (jiná verze macOS), Avalonia ikonu přeškáluje na novou výšku;
návrh proto drží celý motiv v plátně 18 × 18 a nespoléhá na přesný počet pixelů nad rámec mřížky 1 pt.

## Základní tvar (stejný jako na Windows, design B „Layers“)

Mřížka 18 × 18 pt, souřadnice v pt (x, y, šířka, výška):

```
     0 1 2 3 4 5 6 7 8 9 10  12  14  16 17
  0  . . . . . . . . . . . . . . . . . .
  1  . . # # # # . . . [ badge 8 × 7 pt  ]     žebro chladiče   (2, 1, 4, 1)
  2  . . . . . . . . . [ x 9–17, y 1–8   ]
  3  . # # # # # # . . [                 ]     topný blok       (1, 3, 6, 3)
  4  . # # # # # # . . [                 ]
  5  . # # # # # # . . [                 ]
  6  . . . # # . . . . [                 ]     tryska (zúžená)  (3, 6)–(5, 6)–(4.5, 7)–(3.5, 7)
  7  . . . . . . . . . . . . . . . . . .
  8  . . . . . . . . . . . . . . . . . .       (1 pt mezera badge ↔ vrstvy)
  9  . = = = = = = = = = = = = = = = = .       horní vrstva     (1,  9, 16, 2)
 11  . . . . . . . . . . . . . . . . . .
 12  . = = = = = = = = = = = = = = = = .       prostřední       (1, 12, 16, 2)
 14  . . . . . . . . . . . . . . . . . .
 15  . = = = = = = = = = = = = = = = = .       spodní (plní se první) (1, 15, 16, 2)
 17  . . . . . . . . . . . . . . . . . .
```

- Hotend: alfa 1,0 (u offline 0,35).
- Vrstvy: nevyplněná část („track“) alfa **0,35**, vyplněná část alfa **1,0**. Barvu (modrá/žlutá/zelená/červená)
  na Windows tu nahrazuje rozdíl alfy 1,0 vs 0,35 – ten macOS zachová ve světlém, tmavém i zvýrazněném stavu.
- Badge: box x 9–17, y 1–8 (8 × 7 pt), od hotendu 2 pt, od horní vrstvy 1 pt. Symbol je **samostatný tvar bez
  kotouče** (na Windows je v barevném kotouči); v monochromu by kotouč s vyříznutým symbolem byl při 1x nečitelný.
- Překrývání průhledných tvarů je zakázané (track se kreslí jen pro nevyplněnou část), jinak by se alfy sčítaly.

## Progress

Stejný princip jako na Windows: tři vrstvy se plní zleva doprava, spodní první. Celkem 3 × 16 = **48 pt**; vyplněno
`floor(48 × progress)` **celých pt** (ne pixelů @2x), aby obraz zůstal ostrý i při 1x. Krok klíče 5 % = 2,4 pt, takže
každý krok je vidět (změna ≥ 2 pt). 100 % (plné vrstvy) se při tisku nikdy neukáže (progress se zaokrouhluje dolů).

Proč ne oblouk/kruh: kruhový progress by vyžadoval opustit základní tvar (rozhodnutí uživatele: stejný tvar pro
všechny stavy) a při 16 pt by oblouk o tloušťce ~1,5 pt měl při 5% krocích jen ~0,8 pt přírůstky – nečitelné.
Vrstvy dávají 48 pt dlouhou „lištu“, tedy nejlepší rozlišení, a jsou shodné s Windows.

## Stavy → glyf

Seznam stavů ověřen v `TrayStatus` (8 hodnot) a `TrayIconKey` (progress jen u Printing a Paused, krok 5 %).

| `TrayStatus` | Vrstvy | Symbol v rohu (badge) | Geometrie symbolu (pt) | Windows (pro srovnání) |
|---|---|---|---|---|
| Idle | prázdné (track 0,35) | žádný | – | oranžový hotend, šedé vrstvy, bez badge |
| Busy | prázdné | **⋯** tři tečky | kruhy r 1,2 se středy (10, 5), (13, 5), (16, 5) | modrý badge ⋯ |
| Printing *p* % | vyplněno *p* % | **▶** „play“ | trojúhelník (10.5, 1)–(16.5, 4.5)–(10.5, 8) | modrý hotend + modré vrstvy, bez badge |
| Paused *p* % | vyplněno *p* % | **❚❚** | obdélníky (10, 1, 2, 7) a (14, 1, 2, 7) | žluté vrstvy + žlutý badge ❚❚ |
| Finished | plné | **✓** | lomená čára (9.9, 4.6)–(12.2, 6.9)–(16.2, 2.1), tah 1,6, kulaté konce | zelené vrstvy + ✓ |
| Attention | prázdné | **!** | obdélník (12, 1, 2, 4.5) + tečka (12, 6, 2, 2), poloměr rohů 0,4 | jantarový badge ! |
| Error | plné | **×** | úsečky (10.3, 1.8)–(15.7, 7.2) a (10.3, 7.2)–(15.7, 1.8), tah 1,7, kulaté konce | červené vrstvy + × |
| Offline | prázdné, celý motiv ztlumený (hotend 0,35, track 0,2) | **přeškrtnutí** celé ikony | úsečka (1.5, 1)–(16.5, 17), tah 1,5; pod ní vyříznutá mezera tahem 3,6 | šedý motiv + badge − |

Odůvodnění voleb:

- **▶ / ❚❚** – dvojice „přehrávání / pauza“ je univerzálně srozumitelná a obě ikony se liší jen symbolem, což
  odpovídá významu (stejný tisk, jen pozastavený). Uživatel uvedl jako příklad tečku; ta se ale plete s ⋯ (Busy) a
  nemá vztah k pauze – alternativa je v otevřených otázkách a v náhledech (`alt-printing-dot-*`).
- **Printing 0 %** se od Idle liší jen symbolem ▶ (na Windows to řeší modrý hotend; barva tu není k dispozici).
- **Error ×, Attention !** – uživatel uvedl „vykřičník = chyba / pozornost“; aby se obě odlišily (požadavek T-018),
  dostala chyba × jako na Windows a k tomu plné vrstvy (stejně jako na Windows), takže je vizuálně „nejtěžší“.
- **Offline** – přeškrtnutí přes celý motiv odpovídá idiomu macOS (`wifi.slash`, `bell.slash` v SF Symbols);
  ztlumení na 0,35 je hodnota, kterou Apple používá pro neaktivní prvky. Badge se u offline nekreslí (lomítko jím je).
- Žádná informace jen barvou/alfou: každý stav kromě Idle má vlastní tvar symbolu; plné vs. prázdné vrstvy jsou
  doplňková informace.

Zvýrazněný stav (otevřené menu): macOS kreslí pod ikonu zaoblený podklad a glyf obarví sám; náhledy ho simulují
(druhý řádek každé lišty). Alfy 0,35 / 0,2 zůstávají čitelné i na něm.

## Odlišení od Windows sady

| | Windows (`ColorTrayIconSet`) | macOS (template) |
|---|---|---|
| Barvy | oranžový hotend, stavové barvy T-007 | jen černá + alfa, barvu určuje systém |
| Velikosti | `.ico` 16/20/24/32 px, ručně hintované | jedna bitmapa 36 px = 18 pt @2x |
| Badge | barevný kotouč s obrysem a symbolem | samotný symbol bez kotouče, větší (8 × 7 pt) |
| Printing | modrý hotend, bez badge | ▶ |
| Offline | šedý motiv + − | ztlumený motiv + lomítko |
| Progress | 3 vrstvy, celé pixely | 3 vrstvy, celé pt |

## Zdroje a vykreslení za běhu

- SVG zdroje: `src/LayerPeek.App/Assets/AppIcon/tray/macos/menubar-*.svg` (18 × 18, viewBox v pt), reprezentativní
  stavy včetně kroků progressu 0/10/35/70/95 a pauzy 0/50 – stejné jako referenční SVG Windows sady. Slouží jako
  designová reference; SVG jsou z `AvaloniaResource` vyloučená (`Exclude="Assets\**\*.svg"`) a za běhu se nenačítají.
- **Doporučené vykreslení (pro `ui-dev`)** – stejný přístup jako `ColorTrayIconSet`/`TrayIconRenderer`: geometrii
  z tabulky výše nakreslit kódem přes Avalonia `DrawingContext` do `RenderTargetBitmap` 36 × 36 px (vše × 2), černou
  s danou alfou, cachovat přes `TrayIconCache` podle `TrayIconKey` a vrátit `new WindowIcon(bitmap)`. Vyříznutí pod
  lomítkem: nakreslit motiv do vlastní vrstvy a lomítko s mezerou řešit buď ořezem (`PushGeometryClip` s geometrií
  „plocha mínus pás kolem lomítka“), nebo vynecháním dotčených částí – výsledek musí odpovídat `menubar-offline.svg`.
  Žádná SVG knihovna, žádný nový NuGet balíček, AOT bez reflexe.
- Na `TrayIcon` nastavit `MacOSProperties.SetIsTemplateIcon(trayIcon, true)` (Avalonia 12.1.4, `Avalonia.Controls`;
  v `Avalonia.Native` implementováno přes `ITrayIconWithIsTemplateImpl`). Bez toho macOS ikonu nepřebarví a na tmavém
  menu baru bude černá.
- Na Windows se nic nemění (dál `ColorTrayIconSet(multiSize: true)`).

## Ověření a co ověřeno není

- Náhledy všech stavů při @1x (zmenšení 2 : 1 z @2x, jako to udělá macOS) a @2x na světlém i tmavém menu baru,
  normální i zvýrazněný stav, byly vykresleny (SkiaSharp, mimo repozitář) a vizuálně zkontrolovány.
- **Neověřeno:** skutečné zobrazení v menu baru (Avalonia + AppKit), chování na displeji @1x, MacBook s výřezem
  (vyšší menu bar), nastavení „Zvýšit kontrast“ / „Omezit průhlednost“.
- Implementace (T-018, `MenuBarIconRenderer`): bitmapy 36 × 36 px jsou ve snapshot testech
  (`tests/LayerPeek.App.UiTests/Snapshots/macOS/MenuBarIcons/`) a pixelově odpovídají náhledům návrhu (offline jen
  vyhlazení hrany ořezu, max. 11/255 alfy). Co z `WindowIcon` dostane `NSImage` v reálném menu baru, zbývá ověřit na Macu.

Odkazy: [Apple HIG – The menu bar](https://developer.apple.com/design/human-interface-guidelines/the-menu-bar),
[Bjango – Designing macOS menu bar extras](https://bjango.com/articles/designingmenubarextras/),
[Avalonia `trayicon.mm`](https://github.com/AvaloniaUI/Avalonia/blob/master/native/Avalonia.Native/src/OSX/trayicon.mm).

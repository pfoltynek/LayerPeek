---
id: T-100
title: macOS – klávesové zkratky podle zvyklostí Applu
status: in-progress
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-018, T-038]
---

## Cíl
Při testování na Macu (2026-10-29) nefungovala v okně kamery F11 pro celou obrazovku: horní řada kláves posílá na Macu mediální funkce (F11 jen s `fn`) a F11 má macOS ve výchozím nastavení pro „Zobrazit plochu“, takže se k aplikaci nedostane. Uživatel rozhodl projít všechny zkratky a na macOS je upravit podle zvyklostí Applu. Windows beze změny.

## Rozsah (schválený návrh, rozhodnuto 2026-10-29)
- **Okno kamery:** ⌃⌘F přepíná celou obrazovku (standardní zkratka macOS), F11 zůstává jako záloha, Esc zpět jako dosud. Všechny texty, které zkratku uvádějí (tooltip tlačítka celé obrazovky, popis obrazu pro čtečky obrazovky, nápovědy), ukazují na macOS „⌃⌘F“, na Windows dál „F11“. Ve všech 8 jazycích.
- **Nastavení:** ⌘⇧] / ⌘⇧[ přepíná hlavní záložky (⌃Tab / ⌃⇧Tab zůstávají), ⌘⌥→ / ⌘⌥← přepíná záložky tiskáren (klávesnice Macu nemají PageUp/PageDown).
- **Všechna pomocná okna** (Nastavení, Diagnostika, O aplikaci, Licence, okno kamery): ⌘W zavře okno (v Nastavení se chová jako Zrušit, včetně případného dotazu na neuložené změny), ⌘. = Zrušit.
- **Kontextové menu kamery:** na macOS jen ⌃+klik a pravé tlačítko; zmínky o Shift+F10 / klávese Menu se na macOS nezobrazují.
- Rozvržení kamery (mezerník, Enter/F2, šipky, Home/End, Tab, Esc), Enter = výchozí tlačítko a Esc = Zrušit beze změny.
- Menu v menu baru: přístupové klávesy (podtržená písmena) na macOS neřešit, systémové menu je nepoužívá.

## Akceptační kritéria
- [x] ⌃⌘F přepíná celou obrazovku v okně kamery na macOS, F11 a Esc fungují dál (test)
- [x] Zkratky Nastavení a ⌘W / ⌘. fungují na macOS podle rozsahu (testy)
- [x] Texty se zkratkami ukazují na macOS zkratky macOS ve všech 8 jazycích, na Windows beze změny (testy, UI testy bez ořezání)
- [ ] Ověřeno uživatelem na Macu (včetně Logitech klávesnice)
- [ ] Windows beze změny
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Jiné rozložení klávesnice (⌘⇧] / ⌘⇧[).** Avalonia na macOS odvozuje logickou klávesu ze znaku aktuálního
   rozložení (s ⇧ a ⌥, bez ⌘ a ⌃); na českém rozložení jsou vpravo od P „ú“ a „)“ a „]“ se píše s ⌥. Implementace proto
   bere zkratku podle logické klávesy závorky **nebo** podle fyzické klávesy na americké pozici ] / [. Apple ale v
   macOS 12+ zkratky menu pro některá rozložení sám přemapovává, takže „správná“ klávesa na české klávesnici se může
   lišit. Neověřeno – prosím vyzkoušet (docs/macos-manual-testing.md, krok 6.3) a rozhodnout, zda stačí fyzická pozice.
2. **Dostanou se ⌃⌘F, ⌘W a ⌘. vůbec k aplikaci?** Headless testy posílají klávesy přímo do Avalonie. Na skutečném Macu
   jdou zkratky s ⌘ přes `performKeyEquivalent` a systém (nebo výchozí menu aplikace Avalonie) by je mohl zachytit dřív
   (⌃⌘F / fn+F = „Celá obrazovka“, ⌘. = `cancelOperation:`). Neověřeno na Macu.
3. **⌘. při otevřeném rozbalovacím seznamu v Nastavení** zavře celé okno jako Zrušit (Esc v té chvíli zavře jen
   seznam). Je to v pořádku, nebo má ⌘. nejdřív zavřít jen seznam?

## Poznámky k implementaci
- **Sdílený pomocník** `src/LayerPeek.App/Windows/MacShortcuts.cs`: rozpoznání ⌘W, ⌘., ⌃⌘F, ⌘⇧] / ⌘⇧[ (logická
  klávesa závorky nebo fyzická klávesa `BracketRight` / `BracketLeft`), ⌘⌥→ / ⌘⌥← a `HandleCloseKeys` (tunelující
  handler ⌘W / ⌘. pro okno). ⌘ = `KeyModifiers.Meta`, ⌃ = `Control`; modifikátory se porovnávají přesně (⌘F ani ⌃F
  nic nedělá, ⌘] bez ⇧ také ne). Bez nových balíčků, bez reflexe (AOT beze změny).
- **Přepínač platformy (seam):** každé okno má `internal bool UsesMacShortcuts` (výchozí `OperatingSystem.IsMacOS()`,
  testy ho nastaví) – Nastavení, Diagnostika, O aplikaci, Licence; okno kamery bere `CameraViewModel.UsesMacShortcuts`,
  který zároveň vybírá texty (`FullScreenToolTip`, `ImageHint`), stejně jako `SettingsViewModel.UsesMacTerms` (T-019).
  Na Windows se nic nemění: zkratky macOS jsou vypnuté, texty dál „F11“.
- **Okno kamery:** ⌃⌘F = `ToggleFullScreenCommand` (stejně jako F11, které zůstává v `KeyBindings`); ⌘. = Esc (v režimu
  rozvržení `HandleEscape` – popover, tažení, zvednutá karta, zahození změn; jinak `EscapeCommand`); ⌘W = `Close()` jako
  zavírací tlačítko (pamatuje polohu stejně). Tooltip a přístupný název tlačítka celé obrazovky a popis obrazu pro
  čtečky obrazovky jsou na macOS nové řetězce `CameraFullScreenMac` / `CameraImageHintMac` („⌃⌘F“ místo „F11“) ve všech
  8 jazycích; symboly ⌃⌘ jsou v každém jazyce stejné, Esc zůstává ve tvaru dosavadních textů.
- **Nastavení:** ⌘⇧] / ⌘⇧[ = hlavní záložky, ⌘⌥→ / ⌘⌥← = záložky tiskárny (i s fokusem v textovém poli, tunelování),
  ⌃Tab / ⌃PageDown zůstávají. ⌘W i ⌘. spustí `CancelCommand` – totéž co Zrušit / Esc (Zrušit dnes nemá dotaz na
  neuložené změny, úpravy se zahodí); během ukládání nedělají nic, stejně jako zakázané Zrušit.
- **Diagnostika, O aplikaci, Licence:** ⌘W i ⌘. = Zavřít (jako tlačítko s `IsCancel`). Licence zavřou jen sebe.
- **Kontextové menu kamery:** žádný text v UI Shift+F10 / klávesu Menu nezmiňuje (jen komentář v XAML, upraven);
  chování otevírání menu se nemění.
- **Testy (headless, macOS varianta vynucená přes seam na každém OS):** `CameraWindowMacShortcutsUiTests` (⌃⌘F, F11, Esc,
  ⌘. v celé obrazovce / náhledu / režimu rozvržení, ⌘W ve všech režimech, Windows varianta, texty a tooltip bez ořezání
  na jednom řádku v 8 jazycích), `SettingsWindowMacShortcutsUiTests` (záložky US i „české“ rozložení – fyzická klávesa
  bez logické závorky, ⌘W / ⌘. = Zrušit bez uložení, během ukládání nic, Windows varianta),
  `AuxiliaryWindowMacShortcutsUiTests` (O aplikaci, Licence, Diagnostika, výchozí hodnota = OS). Stávající testy
  textů „F11“ vynucují Windows variantu (`CameraHarness(macShortcuts: false)`), aby platily na obou OS.
- **Ověřeno (macOS, 2026-10-29, headless):** `dotnet build` a `dotnet build -c Release` 0 varování, `dotnet test`
  7573 testů, 0 selhání (180 přeskočeno), profil uživatele beze změny (`tools/profile-snapshot.ps1`). **Neověřeno:**
  skutečné klávesy na Macu (vestavěná a Logitech klávesnice, české rozložení) – kritérium 4, otázky 1 a 2; běh testů
  na Windows (kritérium 5 je zajištěné testy s vynucenou Windows variantou, ale ne během na Windows).
- Ruční kroky: `docs/macos-manual-testing.md`, sezení 6, krok 3; nejdřív skriptem `tools/macos-verify.ps1` – kontroly `mac.keys` (skutečné události klávesnice, potřebuje Zpřístupnění pro Terminál) a `mac.keyboard-physical` (vestavěná a Logitech klávesnice, české rozložení), T-105. CHANGELOG [0.3.0] doplněn. `README.md` zmiňuje F11
  (popis Windows verze) a zůstal beze změny.

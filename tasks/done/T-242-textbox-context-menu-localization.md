---
id: T-242
title: Kontextové menu textových polí (Cut / Copy / Paste) je anglicky i v jiném jazyce aplikace
status: done
owner: ui-dev
priority: P3
platform: all
depends_on: [T-029]
---

## Cíl
Kontextové menu textového pole (pravé tlačítko / ⌃-klik do pole „Název“ v Nastavení › Tiskárny) ukazuje položky
**„Cut“, „Copy“, „Paste“ anglicky**, i když je LayerPeek v jiném jazyce. Je to výchozí kontextové menu `TextBox`
Avalonie (aplikace vlastní nemá); všechny ostatní texty UI jsou lokalizované (`Strings.*.resx`, T-029). Systémové
panely AppKitu (výběr obrázku tiskárny) jsou naopak v jazyce systému.

## Zjištění (2026-11-15, Mac mini Mac14,12, macOS 27.2.1, Avalonia 12.1.6)
- Opakovaný běh `mac.appkit-language` na větvi `feature/macos-verify-first-real-run` (systém cs-CZ, LayerPeek de):
  „image panel (Avalonia Application): buttons 'Otevřít', 'Zrušit' = cs (2); text field context menu: 'Cut', 'Copy',
  'Paste' = en (3)“ (`evidence/mac.appkit-language/finding.txt`, `text-menu.json`). Zjištění k T-097 otázce 5 je
  zapsané v T-127.
- V běhu `-Preset release` `artifacts/verify/20261115-155430/` kontrola omylem přečetla místní nabídku prohlížeče
  souborů otevřeného panelu (helper panel na macOS 27 nepoznal; opraveno na téže větvi).
- Windows pravděpodobně stejně (stejné výchozí menu Avalonie) – ověřit.

## Rozsah
- Vlastní `ContextFlyout` / `ContextMenu` pro textová pole (styl pro `TextBox` v App) s lokalizovanými položkami
  Vyjmout / Kopírovat / Vložit (a Vybrat vše?), příkazy `TextBox` (`Cut`, `Copy`, `Paste`), stav `CanCut` / `CanCopy` /
  `CanPaste`; pole hesla bez Vyjmout / Kopírovat.
- Nové texty do všech jazyků; UI test (headless) s menu.

## Akceptační kritéria
- [x] Kontextové menu textových polí je v jazyce aplikace (všech 8 jazyků), pole hesla nenabízí kopírování. (část Windows → přesunuto do T-125)
- [x] `mac.appkit-language` ukáže jazyk aplikace u kontextového menu. → přesunuto do T-127 (oddíl 1u; běh 2026-11-15 `SKIP` kvůli zamčené obrazovce)
- [x] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Lokalizovat?~~ **Rozhodnutí uživatele 2026-11-15: ano**, vlastní lokalizované menu místo výchozího Avalonie.
2. ~~Které položky?~~ **Rozhodnutí uživatele 2026-11-15: Vyjmout, Kopírovat, Vložit, Vybrat vše** (bez Zpět/Znovu); pole hesla bez Vyjmout a Kopírovat.

## Poznámky k implementaci
- **Kde:** styl `TextBox` v `src/LayerPeek.App/App.axaml` (jedno místo pro všechna pole aplikace včetně vnitřních polí
  `NumericUpDown`) nastaví `ContextFlyout` na vlastní `MenuFlyout`; jedna instance sdílená všemi poli, stejně jako ji
  sdílí Fluent. Položky Vyjmout / Kopírovat / Vložit / Vybrat vše (bez Zpět/Znovu, rozhodnutí uživatele): texty přes
  `LocalizedStrings` (přepnutí jazyka za běhu se projeví při dalším otevření), `Command` = metody `TextBox` (`Cut`,
  `Copy`, `Paste`, `SelectAll`) přes kompilovaný binding `$parent[TextBox]`, `IsEnabled` = `CanCut` / `CanCopy` /
  `CanPaste`, Vybrat vše jen s textem v poli (`StringConverters.IsNotNullOrEmpty`). Zkratky zobrazené v menu jsou ty
  platformy (`TextBox.CutGesture`…, pro Vybrat vše `Windows/TextBoxContextMenu.SelectAllGesture` z
  `PlatformHotkeyConfiguration.SelectAll`); klávesy samotné dál obsluhuje `TextBox` (beze změny). Žádná reflexe, jen
  kompilované bindingy (Native AOT), žádný nový balíček.
- **Pole hesla:** Vyjmout a Kopírovat jsou skryté, když `PasswordChar` není `\0` (`ObjectConverters.Equal` s
  `Char.MinValue`); zůstává Vložit a Vybrat vše. Avalonia u hesla `CanCut` / `CanCopy` stejně vrací `false`.
- **Texty** (`TextBoxCut`, `TextBoxCopy`, `TextBoxPaste`, `TextBoxPasteMac`, `TextBoxSelectAll`, všech 8 jazyků; „_“ =
  přístupová klávesa jako v ostatních kontextových menu, T-121, na macOS se nezobrazuje). Podle Edit menu Windows a
  macOS; liší se jen „Vložit“ v němčině (Windows „Einfügen“, Apple „Einsetzen“) a slovenštině (Windows „Prilepiť“,
  Apple „Vložiť“), proto `TextBoxPasteMac` vybíraný přes `OnPlatform` (v ostatních jazycích stejný text):
  en Cu_t / _Copy / _Paste / Select _All; cs Vyj_mout / _Kopírovat / V_ložit / Vybrat _vše; sk Vys_trihnúť /
  _Kopírovať / _Prilepiť (Mac V_ložiť) / Vybrať _všetko; de _Ausschneiden / _Kopieren / E_infügen (Mac _Einsetzen) /
  Alles aus_wählen; es Cor_tar / _Copiar / _Pegar / _Seleccionar todo; fr Co_uper / Co_pier / C_oller /
  _Tout sélectionner; it _Taglia / _Copia / _Incolla / _Seleziona tutto; pl Wy_tnij / _Kopiuj / _Wklej /
  _Zaznacz wszystko.
- **Testy:** `tests/LayerPeek.App.Tests/Windows/TextBoxContextMenuTextTests.cs` (jedinečné přístupové klávesy
  v každém jazyce s oběma variantami Vložit, překlad, varianty Vložit), `tests/LayerPeek.App.UiTests/Windows/
  TextBoxContextMenuUiTests.cs` (headless: 4 položky v jazyce aplikace a po přepnutí, stav a účinek kliknutí na položky
  včetně schránky, pole jen pro čtení, pole hesla, klávesy Ctrl+A / Ctrl+C a zkratky v menu, pole Název a Heslo
  v Nastavení). Kontrola mutací: bez skrytí u hesla a se špatným příkazem testy selžou. Snímky UI se nemění (menu
  žádný snímek nezachycuje).
- **`mac.appkit-language`:** kontextové menu textového pole už není jen zjištění – nový `Get-VerifyTextMenuProblem`
  (`tools/VerifyTools.psm1`, Pester v `tools/VerifyTools.Tests.ps1`) a kontrola hlásí FAIL, když položky nejsou všechny
  v jazyce LayerPeeku; `Get-VerifyTitleLanguages` ignoruje „_“. Dialog obrázku zůstává zjištěním.
- **Ověřeno 2026-11-15 na Macu** (Mac mini, macOS 27.2.1): `dotnet build` 0 varování, `dotnet test` 0 selhání;
  aplikace v izolovaném testovacím režimu nastartovala a běžela (displej spal, bez chyb v logu).
  `mac.appkit-language` z Terminálu: **SKIP – obrazovka zamčená** (`artifacts/verify/20261115-201553/`), proto
  kritérium 2 zůstává otevřené. **Neověřeno:** Windows (bod v T-125) a živé menu na macOS (AX titulky, zda obsahují „_“
  – pomocná funkce to snese).
- Příručky toto menu nepopisují – beze změny. Doplněn `docs/architecture.md` (bod 10) a `docs/macos-manual-testing.md`
  (sezení 4, bod 5).

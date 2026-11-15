---
id: T-242
title: Kontextové menu textových polí (Cut / Copy / Paste) je anglicky i v jiném jazyce aplikace
status: todo
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
- [ ] Kontextové menu textových polí je v jazyce aplikace (všech 8 jazyků), pole hesla nenabízí kopírování.
- [ ] `mac.appkit-language` ukáže jazyk aplikace u kontextového menu.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~Lokalizovat?~~ **Rozhodnutí uživatele 2026-11-15: ano**, vlastní lokalizované menu místo výchozího Avalonie.
2. ~~Které položky?~~ **Rozhodnutí uživatele 2026-11-15: Vyjmout, Kopírovat, Vložit, Vybrat vše** (bez Zpět/Znovu); pole hesla bez Vyjmout a Kopírovat.

## Poznámky k implementaci
(vyplní agent po dokončení)

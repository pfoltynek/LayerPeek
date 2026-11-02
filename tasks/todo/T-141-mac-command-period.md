---
id: T-141
title: macOS – ⌘. (Zrušit) se k oknům aplikace nedostane
status: todo
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-100]
---

## Cíl
Nalezeno 2026-11-02 kontrolou `mac.keys` (T-140), odpověď na otázku 2 z T-100: ⌘. na Macu nefunguje v žádném okně –
neopustí celou obrazovku okna kamery, nezavře Nastavení (Zrušit) ani Diagnostiku. Sonda v čistém AppKitu ukázala proč:
AppKit pošle ⌘. jako `performKeyEquivalent:` a potom jako akci `cancelOperation:` prvnímu respondentovi, ne jako
`keyDown:`. Avalonia.Native `cancelOperation:` nepřekládá na událost klávesnice, takže `MacShortcuts.IsCancel`
(`Key.OemPeriod` + ⌘) nikdy nic nedostane. ⌘W a Esc chodí jako `keyDown:` a fungují.

## Rozsah
- Aby ⌘. dělalo totéž co Esc ve všech oknech s `MacShortcuts.HandleCloseKeys` (Nastavení, Diagnostika, O aplikaci,
  Licence, okno kamery) – způsob podle otázky níže.
- Test (UI test tam, kde to jde, a `mac.keys` = `PASS`).

## Akceptační kritéria
- [ ] ⌘. v okně kamery opustí celou obrazovku / malý náhled, v Nastavení je Zrušit, v Diagnostice zavře okno
- [ ] S otevřeným rozbalovacím seznamem ⌘. zavře jen seznam (T-100 otázka 3)
- [ ] `mac.keys` = `PASS`

## Otevřené otázky
1. Jak ⌘. zachytit: (a) doplnit nativní metodu `cancelOperation:` do třídy pohledu Avalonie přes runtime Objective-C
   (`class_addMethod`, AOT bezpečné, jako dnešní `ObjCRuntime`) a předat ji aktivnímu oknu jako ⌘.; (b) položka
   s klávesovou zkratkou ⌘. v nativním menu aplikace (mění menu v menu baru); (c) oprava v Avalonii (upstream,
   čekání na vydání).

## Poznámky k implementaci
(vyplní agent)

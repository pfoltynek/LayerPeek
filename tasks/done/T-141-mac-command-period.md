---
id: T-141
title: macOS – ⌘. (Zrušit) se k oknům aplikace nedostane
status: done
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
- [x] ⌘. v okně kamery opustí celou obrazovku / malý náhled, v Nastavení je Zrušit, v Diagnostice zavře okno
- [x] S otevřeným rozbalovacím seznamem ⌘. zavře jen seznam (T-100 otázka 3)
- [x] `mac.keys` = `PASS`

## Otevřené otázky
1. ~~Jak ⌘. zachytit~~ – **rozhodnuto 2026-11-02 (uživatel): (a) nativní `cancelOperation:`.** Původní znění: Jak ⌘. zachytit: (a) doplnit nativní metodu `cancelOperation:` do třídy pohledu Avalonie přes runtime Objective-C
   (`class_addMethod`, AOT bezpečné, jako dnešní `ObjCRuntime`) a předat ji aktivnímu oknu jako ⌘.; (b) položka
   s klávesovou zkratkou ⌘. v nativním menu aplikace (mění menu v menu baru); (c) oprava v Avalonii (upstream,
   čekání na vydání).

## Poznámky k implementaci
- `Platform/MacOS/MacCommandPeriod.cs`: při startu (`App.StartHost`, jen macOS) přidá `class_addMethod` třídě `AvnView`
  metodu `cancelOperation:` (`UnmanagedCallersOnly`, AOT bezpečné, nový `ObjCRuntime.AddMethod`). Ta vezme
  `[NSApp currentEvent]` a když je to stisk klávesy s ⌘, pošle ho vlastnímu `keyDown:` pohledu; Avalonia pak vyvolá
  `KeyDown` (`Key.OemPeriod` + ⌘) a stávající `MacShortcuts.HandleCloseKeys` / T-100 otázka 3 fungují beze změny.
  Ochrana proti opětovnému vstupu (textový vstup může `cancelOperation:` poslat znovu). Když by Avalonia metodu jednou
  měla sama, `class_addMethod` nic nezmění (v logu „cancelOperation: not added“).
- Ověřeno 2026-11-02 (macOS 27.2): `mac.keys -Unattended -Allow mac.keys` (s nástroji T-140) = `PASS` – ⌘. opustí celou
  obrazovku okna kamery, zavře Nastavení jako Zrušit beze změny, s otevřeným seznamem `PrinterConnectionType` zavře
  nejdřív jen seznam; v logu „Cmd-. reaches the windows as a key (cancelOperation: of AvnView).“ Před opravou stejná
  kontrola `FAIL` (⌘. nic neudělalo).
- Testy `MacCommandPeriodTests` (výběr události, instalace bez Avalonie nic nezmění); `dotnet build` 0 upozornění,
  `dotnet test` 8491 testů, 0 selhalo.

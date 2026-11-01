---
id: T-137
title: macOS – zapnutí „vždy navrchu“ deaktivuje aplikaci a okno ztratí fokus
status: done
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-102, T-117, T-122]
---

## Cíl
Nalezeno kontrolou `mac.camera-pin` (T-136, 2026-11-01, macOS 27.2). Když se v okně kamery zapne „vždy navrchu“ (klik
na špendlík, mezerník nebo Enter na špendlíku, AXPress), přejde aplikace podle T-102 na doplňkovou
(`NSApplicationActivationPolicyAccessory`, typ LaunchServices `UIElement`) a **macOS ji deaktivuje**: vpředu je Finder,
okno zůstane navrchu, ale fokus klávesnice zmizí (Accessibility: žádný prvek s fokusem). Uživatel klávesnice pak Enterem
volbu nevrátí (porušuje T-122 „mezerník a Enter přepínají a fokus zůstane na špendlíku“). Stejně v normálním okně
i v malém náhledu.

`MacAppPresence.SetVisible` už s `keepActive` aplikaci znovu aktivuje, ale jen když `isActive` hned po
`setActivationPolicy:` vrátí `false`; deaktivace zřejmě přijde až později (asynchronně), takže se neprojeví.

## Rozsah
- Po přechodu na Accessory s otevřeným oknem, které bylo klíčové, aplikaci a okno udržet aktivní / klíčové (např.
  aktivace až po deaktivaci – `NSApplicationDidResignActiveNotification` krátce po změně politiky – nebo odložená
  aktivace). Řešení navrhne ui-dev; nesmí krást popředí, když aplikace aktivní nebyla.
- Test: `mac.camera-pin` (T-136) projde bodem „after space / return the pin lost the keyboard focus“; jednotkové testy
  podle návrhu.

## Akceptační kritéria
- [x] Po zapnutí „vždy navrchu“ (myš, mezerník, Enter) zůstane LayerPeek vpředu a fokus na špendlíku
- [x] Když aplikace aktivní nebyla (AXPress z VoiceOveru / jiná aplikace vpředu), popředí si nevezme
- [x] `mac.camera-pin` bez této chyby; `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- Příčina: AppKit deaktivuje aplikaci, která se stala doplňkovou, až chvíli po `setActivationPolicy:`; kontrola
  `isActive` hned po volání v `MacAppPresence.SetVisible` (s `keepActive`) to nezachytila.
- Oprava `MacAppPresence`: po přechodu na Accessory u aplikace, která byla aktivní a má dál otevřené okno, se 1 s
  (20 × 50 ms, `DispatcherTimer`) hlídá `isActive`; při první ztrátě aktivace se aplikace jednou znovu aktivuje
  (`MacAppActivation.Activate`). Další změna politiky hlídání zruší. Aplikace, která aktivní nebyla, se neaktivuje.
- Ověřeno 2026-11-01 na macOS 27.2 kontrolou `mac.camera-pin` (T-136): před opravou „after space / return (pin on) the
  pin lost the keyboard focus (front app: Finder)“ v normálním okně i malém náhledu; po opravě 3 běhy bez této chyby
  (2 před T-138, 1 úplný PASS s T-138). Krok „AXPress s Finderem vpředu“: Finder zůstane vpředu.
- Jednotkový test chybí záměrně: chování je jen v AppKitu (headless testy nemají `NSApp`); pokrývá ho `mac.camera-pin`.
- `dotnet build` 0 upozornění, `dotnet test` 8481 testů, 0 selhalo.

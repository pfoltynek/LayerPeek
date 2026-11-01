---
id: T-137
title: macOS – zapnutí „vždy navrchu“ deaktivuje aplikaci a okno ztratí fokus
status: todo
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
- [ ] Po zapnutí „vždy navrchu“ (myš, mezerník, Enter) zůstane LayerPeek vpředu a fokus na špendlíku
- [ ] Když aplikace aktivní nebyla (AXPress z VoiceOveru / jiná aplikace vpředu), popředí si nevezme
- [ ] `mac.camera-pin` bez této chyby; `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

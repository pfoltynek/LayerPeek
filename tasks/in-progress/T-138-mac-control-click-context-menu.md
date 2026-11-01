---
id: T-138
title: macOS – ⌃-klik v okně kamery neotevře kontextové menu
status: in-progress
owner: ui-dev
priority: P2
platform: macos
depends_on: [T-117]
---

## Cíl
Nalezeno kontrolou `mac.camera-pin` (T-136, 2026-11-01, macOS 27.2). Kontextové menu okna kamery se otevře pravým
tlačítkem, ale ⌃-klikem (levé tlačítko se stisknutým Control, běžné na Macu s jedním tlačítkem / trackpadem) ne –
v normálním okně ani v malém náhledu. T-117 požaduje obojí. Avalonia ⌃-klik na kontextové menu sama nepřevádí; stejný
syntetický ⌃-klik ve Finderu menu otevře.

## Rozsah
- Na macOS ⌃ + levé tlačítko v okně kamery otevře kontextové menu jako pravé tlačítko (a nezačne tažení okna, dvojklik
  ani jinou akci levého tlačítka). Zvážit i ostatní kontextová menu aplikace (stejné řešení všude, kde je ContextMenu).
- Headless UI test (⌃ + levé tlačítko otevře menu) a `mac.camera-pin` (T-136).

## Akceptační kritéria
- [ ] ⌃-klik otevře kontextové menu okna kamery v normálním okně i malém náhledu (macOS), na Windows beze změny
- [ ] UI test; `mac.camera-pin` bez této chyby; `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

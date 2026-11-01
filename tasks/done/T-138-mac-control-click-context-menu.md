---
id: T-138
title: macOS – ⌃-klik v okně kamery neotevře kontextové menu
status: done
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
- [x] ⌃-klik otevře kontextové menu okna kamery v normálním okně i malém náhledu (macOS), na Windows beze změny
- [x] UI test; `mac.camera-pin` bez této chyby; `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
- `MacShortcuts.HandleControlClick`: ⌃ + levé tlačítko (jen ⌃, tunelováním, takže levé tlačítko nic jiného neudělá –
  tažení náhledu, dvojklik, tlačítka) vyvolá `ContextRequested` jako pravé tlačítko; zapnuté v okně kamery podle
  `UsesMacShortcuts` (macOS, v testech vynucené). Jiná vlastní kontextová menu aplikace nemá (textová pole mají menu
  Avalonie – mimo rozsah).
- UI testy `CameraWindowMacShortcutsUiTests`: ⌃-klik otevře menu v normálním okně i malém náhledu, puštění tlačítka nad
  menu nespustí položku a nezmění režim ani „vždy navrchu“; bez macOS zkratek (Windows) se menu neotevře. Bez opravy test
  selže.
- Ověřeno 2026-11-01 na macOS 27.2: `mac.camera-pin` (T-136) = PASS – ⌃-klik otevře menu se zaškrtnutou položkou
  „Always on top“ v normálním okně i malém náhledu (skutečně stisknutý Control).
- `dotnet build` 0 upozornění, `dotnet test` 8483 testů, 0 selhalo.

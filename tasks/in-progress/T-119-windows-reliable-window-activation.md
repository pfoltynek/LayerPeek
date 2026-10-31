---
id: T-119
title: Windows – spolehlivé vytažení otevřeného okna do popředí
status: in-progress
owner: ui-dev
priority: P2
platform: windows
depends_on: [T-102, T-117]
---

## Cíl
Položka tray menu „Přenést kameru do popředí“ (T-102) na Windows okno kamery nevytáhne dopředu. Uživatel 2026-10-31:
vypnul špendlík (T-117), okno kamery zapadlo za jiné okno, zvolil „Přenést kameru do popředí“ a nic se nestalo.

Analýza (2026-10-31): cesta v kódu je správná (`CameraWindows.BringToFront` → `AuxiliaryWindowSlot` →
`AvaloniaAuxiliaryWindow.Activate` → `Window.Activate()`), ale Avalonia 12.1.4 na Windows aktivuje okno jen voláním
`SetForegroundWindow` (`Avalonia.Win32.WindowImpl.Activate`, ověřeno dekompilací). Windows ho kvůli zámku popředí
(foreground lock) často odmítne a okno zůstane za ostatními (nanejvýš zabliká tlačítko na hlavním panelu). Headless
testy kontrolují jen to, že se `Activate` zavolalo, chování Windows nesimulují.

Stejnou cestou se aktivují i už otevřená okna Nastavení, Diagnostiky a O aplikaci (další volba z menu) a okna
v `AuxiliaryWindowTracker.BringToFront` (T-043).

## Rozsah (rozhodnuto 2026-10-31)
1. **Položka zůstává** na obou OS (uživatel: „Ponechat a opravit“). Je jedinou cestou zpět na macOS (bez ikony v Docku)
   a na Windows u minimalizovaného okna se špendlíkem (bez tlačítka na hlavním panelu, T-102).
2. **Spolehlivé vytažení dopředu na Windows** pro všechna pomocná okna (kamera, Nastavení, Diagnostika, O aplikaci
   a jejich vlastněná okna), jedno společné řešení. Uživatel: „Ano, všechna okna“.
3. Řešení přes `[LibraryImport]` do `user32` (bez nového balíčku, AOT v pořádku), izolované v kódu pro Windows;
   macOS beze změny (tam dál `bringAppToFront`, T-019).
4. Nesmí měnit `Window.Topmost` ani dočasně: podle něj se řídí tlačítko na hlavním panelu a Spaces (T-102,
   `AppPresence`), blikalo by. Okno se špendlíkem po vytažení zůstane vždy nahoře, okno bez špendlíku nesmí zůstat
   vždy nahoře.
5. Minimalizované okno se obnoví jako dosud (celá obrazovka zpět do celé obrazovky).

## Akceptační kritéria
- [ ] Test, který chybu zopakuje před opravou (Windows E2E/integrační: okno kamery překryté oknem jiného procesu,
      volba z tray menu, kamera je pak v popředí / nad oknem jiného procesu)
- [ ] Kamera bez špendlíku i se špendlíkem, normální i minimalizovaná, malý náhled: po volbě vepředu a aktivní
- [ ] Nastavení, Diagnostika, O aplikaci: druhá volba z menu vytáhne otevřené okno dopředu
- [ ] Okno bez špendlíku po vytažení není vždy nahoře; `Topmost` se během vytažení nemění
- [ ] Odmítne-li Windows aktivaci i tak, okno se aspoň přesune v pořadí nad ostatní okna
- [ ] macOS beze změny (unit testy cesty pro jiné OS)
- [ ] Žádné nové balíčky, žádné potlačené trim/AOT varování
- [ ] `dotnet build` a `dotnet test` bez chyb a varování
- [ ] Ruční ověření ve skutečné aplikaci (Windows) → T-114

## Otevřené otázky
(žádné)

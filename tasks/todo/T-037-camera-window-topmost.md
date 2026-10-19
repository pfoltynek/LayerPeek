---
id: T-037
title: Okno kamery vždy navrchu (volba v nastavení)
status: todo
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-033]
---

## Cíl
U každé tiskárny jde nastavit, jestli normální okno kamery a malý náhled zůstávají vždy nad ostatními okny.

## Současný stav (2026-10-19, T-014)
- `CameraWindow.ApplyMode`: `Topmost = true` jen v režimu malý náhled (`Mini`); normální okno a celá obrazovka mají `Topmost = false`. Nastavit to nejde.
- Headless UI test ověřuje `Topmost` u malého náhledu. Skutečný z-order nad jinými aplikacemi je jen ruční ověření.

## Rozhodnuto
- **Volby (2026-10-19):** dvě volby – **normální okno** (výchozí vypnuto) a **malý náhled** (výchozí zapnuto). Celá obrazovka není nikdy `Topmost`.
- **Tlačítko „připnout“ v okně kamery (2026-10-19):** ne.
- **Kdy se projeví (2026-10-19):** v otevřených oknech hned po uložení nastavení.
- **UI (2026-10-19):** volby jsou v nastavení součástí jedné sekce **„Okno kamery“** společně s T-035. Úkoly zůstávají oddělené.

## Rozsah
- Core (core-dev): dvě volby v nastavení kamery tiskárny (např. `CameraWindowDefaults.NormalTopmost` = `false`, `MiniTopmost` = `true`, nebo samostatně v `CameraSettings` – podle stavu po T-035), nepovinná pole v `printers[*].camera.window`, chybějící hodnota = výchozí. Testy mapování a starého souboru, scrubber sekci nemění.
- App (ui-dev): `CameraWindow` nastavuje `Topmost` podle režimu a nastavení (normální / malý náhled; celá obrazovka vždy `false`); změna po uložení nastavení se projeví v otevřeném okně. Dvě zaškrtávací pole v sekci Okno kamery (rozložení podle schváleného návrhu UI z T-033, bez posuvníku).
- Texty ve všech 8 jazycích.
- Pozn.: T-035 a T-036 mění stejné soubory (`CameraSettings`, podzáložka Kamera, `CameraWindow.ApplyMode`); doporučené pořadí je T-035 → T-037.

## Akceptační kritéria
- [ ] Core: volby uložené a načtené, výchozí normální okno vypnuto, malý náhled zapnuto; starý soubor; testy
- [ ] App.Tests: view model nastavení (načtení, změna, uložení), `CameraViewModel` vystavuje požadovaný `Topmost` pro každý režim a kombinaci voleb; celá obrazovka vždy `false`
- [ ] Headless UI testy: `Window.Topmost` odpovídá nastavení ve všech režimech, i po přepnutí režimu tam a zpět a po uložení změněného nastavení za běhu; snímek reference podzáložky Kamera aktualizovaný; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [ ] Ručně na Windows: okno zůstane nad jinou aplikací, po vypnutí volby ne (zapsat do poznámek)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

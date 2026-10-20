---
id: T-037
title: Okno kamery vždy navrchu (volba v nastavení)
status: in-progress
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
- [x] Core: volby uložené a načtené, výchozí normální okno vypnuto, malý náhled zapnuto; starý soubor; testy
- [x] App.Tests: view model nastavení (načtení, změna, uložení), `CameraViewModel` vystavuje požadovaný `Topmost` pro každý režim a kombinaci voleb; celá obrazovka vždy `false`
- [x] Headless UI testy: `Window.Topmost` odpovídá nastavení ve všech režimech, i po přepnutí režimu tam a zpět a po uložení změněného nastavení za běhu; snímek reference podzáložky Kamera aktualizovaný; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [ ] Ručně na Windows: okno zůstane nad jinou aplikací, po vypnutí volby ne (zapsat do poznámek)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – popisek tlačítka malého náhledu:** tlačítko v okně kamery má tip „Malý náhled vždy navrchu“ (en „Mini preview, always on top“, klíč `CameraMiniPreview`). Když uživatel volbu „Vždy navrchu“ u malého náhledu vypne, tip neodpovídá. Možnosti: (a) tip podle volby – „Malý náhled“ / „Malý náhled vždy navrchu“, (b) vždy jen „Malý náhled“, (c) nechat beze změny. Zatím beze změny (texty UI jsou rozhodnutí uživatele).

## Poznámky k implementaci

### Core (2026-10-20)
- `CameraWindowDefaults.TopmostNormal` (výchozí `false`) a `TopmostMini` (výchozí `true`) – `init` vlastnosti, součást hodnotové rovnosti. `IsTopmost(CameraWindowMode)`: normální okno / malý náhled podle voleb, celá obrazovka vždy `false`.
- `RequiresForgettingRememberedState` volby nezapočítává (T-035 Q3) – test `TopmostChangesKeepRememberedState`.
- Uložení: `printers[*].camera.window.topmostNormal` / `topmostMini` (bool). Jako u T-035 se sekce `window` nezapisuje, dokud jsou všechny hodnoty výchozí; jakmile se zapisuje, obsahuje všechny hodnoty včetně obou voleb. Chybějící hodnota (soubory z T-035 a starší) = výchozí; špatný JSON typ (např. `"yes"`) = poškozený soubor jako u `rememberLastPlacement`. Formát dál `version: 1`, scrubber beze změny.

### App (2026-10-20)
- `CameraViewModel.IsTopmost` = `Defaults.IsTopmost(Mode)`; mění se s režimem (oznámení po `Mode`) a po uložení nastavení (`ApplyPrinters` oznámí změnu, jen když se výsledná hodnota pro aktuální režim změnila).
- `CameraWindow`: `Topmost` se nastaví před zobrazením podle počátečního režimu (celá obrazovka `false`), při přepnutí režimu podle volby nového režimu a při změně `IsTopmost` po uložení se změní **jen** `Topmost` – pozice, velikost, rámeček ani režim se nemění (na Windows Avalonia volá `SetWindowPos` s `SWP_NOMOVE | SWP_NOSIZE`).
- Okno nastavení, podzáložka Okno kamery: záhlaví „Vždy navrchu“ ve vyhrazeném sloupci 88 DIP (SemiBold, zalomení na max. 2 řádky, na střed) a dvě zaškrtávací pole v řádcích Normální okno / Malý náhled (`AutomationId` `PrinterCameraWindowNormalTopmost`, `PrinterCameraWindowMiniTopmost`, záhlaví `PrinterCameraWindowTopmostHeader`). `AutomationProperties.Name` „{řádek} – Vždy navrchu“, při změně jazyka se obnoví. Pořadí Tab: velikost → umístění → vždy navrchu v každém řádku. U vypnuté kamery zašedlá (celá sekce `IsEnabled=CameraEnabled`), hodnoty se zachovají a uloží.
- Nový text `SettingsCameraWindowTopmost` v 8 jazycích: Always on top / Vždy navrchu / Vždy navrchu (sk) / Immer im Vordergrund / Siempre encima / Toujours au premier plan / Sempre in primo piano / Zawsze na wierzchu.
- Nové balíčky žádné.

### Testy
- Core.Tests: `CameraWindowDefaultsTests` (výchozí hodnoty, `IsTopmost` pro všechny kombinace, rovnost, nemazání stavu), `SettingsMapperCameraWindowTests` (round-trip, zápis jen změněné volby, chybějící hodnoty = výchozí, soubor z T-035 bez voleb, špatný typ, přesný tvar JSON).
- App.Tests: `CameraViewModelWindowTests` (+7: `IsTopmost` ve všech režimech a kombinacích i tam a zpět, celá obrazovka nikdy, pořadí oznámení, uložená volba se projeví v otevřeném okně bez změny režimu a pozic, volba jiného režimu bez oznámení, stav se po změně volby dál zapisuje), `CameraWindowSettingsTests` (načtení, výchozí hodnoty, názvy pro čtečky vč. češtiny, uložení, volby samy nemažou stav, vypnutá kamera, `IsDirty`).
- UiTests: nový `Cameras/CameraWindowTopmostUiTests` (14: `Window.Topmost` ve všech režimech pro 4 kombinace voleb včetně přepínání tam a zpět a Esc, počáteční režimy, výchozí chování, změna po uložení u normálního okna i náhledu bez posunu a změny velikosti, celá obrazovka zůstane `false` a po Esc platí nová volba, změna jen u okna dané tiskárny); `SettingsWindowCameraWindowUiTests` (hodnoty, výchozí hodnoty, zašedlá pole, klik + uložení bez smazání stavu, pořadí Tab, změna jazyka); `SettingsWindowLayoutTests` – nový test, že záhlaví „Vždy navrchu“ má ve všech 8 jazycích nejvýš 2 řádky, není oříznuté a žádné slovo není širší než 88 DIP; test „bez posuvníku“ prošel ve všech 8 jazycích.
- Snímky přegenerované a zkontrolované: `settings-camera-window`, `settings-camera-window-errors-fr` („Toujours au / premier plan“), `settings-camera-window-off-cs` (zašedlá pole).
- `dotnet build` 0 varování; `dotnet test` 2× po sobě 3154 testů (3153 prošlo, 1 dřívější přeskočený). `dotnet publish -c Release -r win-x64` (AOT) bez varování; AOT exe se spustí a běží (25 s, pak ukončeno). Okno nastavení ani okno kamery jsem v běžící aplikaci ručně neotevíral.

### Zbývá ručně (uživatel)
- Windows: normální okno se zapnutou volbou zůstane nad jinou aplikací, po vypnutí volby a uložení ne (v otevřeném okně bez zavření); malý náhled obdobně; celá obrazovka nikdy navrchu. Skutečný z-order headless testy neověří.
- macOS (T-018): `Topmost` okna kamery – neověřeno.

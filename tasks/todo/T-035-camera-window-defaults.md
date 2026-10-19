---
id: T-035
title: Výchozí velikost, monitor a umístění okna kamery (nastavení u tiskárny)
status: todo
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: [T-033]
---

## Cíl
Na podzáložce Kamera jde u každé tiskárny nastavit, jak se okno kamery otevře: počáteční režim, velikost a roh normálního okna i malého náhledu a monitor. Hodí se hlavně při více monitorech, kdy má kamera „své“ místo.

## Současný stav (2026-10-19, T-014)
- `CameraWindow` (App): normální okno výchozí 960×540, `WindowStartupLocation=CenterScreen` (hlavní obrazovka), `MinWidth=160`, `MinHeight=90`. Malý náhled poprvé 320×180 na pozici normálního okna (levý horní roh), bez rámečku, `Topmost`. Celá obrazovka = `WindowState.FullScreen` na monitoru, kde okno právě je.
- Režimy `CameraWindowMode { Normal, FullScreen, Mini }`; přepínání tlačítky, F11 / Esc / dvojklikem.
- Pamatovaný stav: `JsonCameraWindowStateStore` → `camera-windows.json` vedle `settings.json` (verze 1, max. 64 tiskáren, podle printer id): `mode`, `normal {x, y, width, height}`, `mini {…}`. Pozice v pixelech obrazovky, velikost v DIP. Ukládá se při změně režimu a zavření okna (ne na celé obrazovce).
- Při otevření se použije pamatovaná pozice normálního okna a pamatovaný režim. `CameraWindowPlacementRules.IsReachable` ověří, že horní okraj je na některé pracovní ploše; jinak se okno přesune doprostřed hlavní obrazovky.
- Monitor se nikde neukládá (jen souřadnice). Avalonia 12 nabízí `Screens.All`, `Screens.Primary`, `Screen.DisplayName` („device name“ – na Windows nejspíš `\\.\DISPLAY1`, neověřeno), `Bounds`, `WorkingArea`, `Scaling`, `IsPrimary`.
- Nastavení kamery v Core: `CameraSettings` (`Enabled`, `StreamUri`, …) → `settings.json`, `printers[*].camera {enabled, streamUrl}`, formát `version: 1`.

## Rozhodnuto
- **Výchozí hodnoty vs. pamatovaný stav (2026-10-19):** přepínač u tiskárny **„Pamatovat poslední pozici a velikost“** (výchozí zapnuto). Zapnuto: výchozí hodnoty se použijí, jen když pro tiskárnu není nic zapamatováno (dnešní chování). Vypnuto: okno se otevře vždy podle výchozích hodnot. **Uložení změněných výchozích hodnot v nastavení smaže zapamatovaný stav dané tiskárny** v `camera-windows.json`.
- **Rozsah (2026-10-19):** zvlášť velikost a roh pro **normální okno** a pro **malý náhled**; **jeden společný monitor** pro všechny režimy (i pro celou obrazovku).
- **Identifikace monitoru (2026-10-19):** název monitoru (`DisplayName`) + rozlišení. Když zvolený monitor chybí, použije se hlavní monitor; volba se zachová a v nastavení se zobrazí jako „nepřipojený“.
- **Umístění (2026-10-19):** 4 rohy + střed, okraj 16 DIP od kraje pracovní plochy. Celá obrazovka je počáteční režim, ne umístění.
- **Velikost (2026-10-19):** předvolby 640×360, 960×540, 1280×720, 1920×1080 + vlastní šířka × výška (DIP), bez vazby na poměr stran.
- **Počáteční režim (2026-10-19):** Jako minule (výchozí) / Normální / Malý náhled / Celá obrazovka.
- **Uložení (2026-10-19):** `settings.json`, `printers[*].camera.window`, nepovinná pole, formát zůstává `version: 1`. Pamatovaný stav zůstává v `camera-windows.json`.
- **Kdy se projeví (2026-10-19):** až při dalším otevření okna, otevřené okno se nepřesouvá.
- **Vypnutá kamera (2026-10-19):** hodnoty se zachovají, pole jsou zašedlá, dokud kamera není zapnutá.
- **UI (2026-10-19):** volby z T-035 a T-037 jsou v nastavení jedna sekce **„Okno kamery“**.

## Rozsah

### Core (core-dev)
- Model výchozího okna kamery jako součást `CameraSettings` (např. `CameraWindowDefaults`): `RememberLastPlacement` (výchozí `true`), `StartMode` (`Last` / `Normal` / `Mini` / `FullScreen`), `Monitor` (název + rozlišení, nebo hlavní), `Normal` a `Mini` (velikost + `Corner` z `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight`, `Center`). Výchozí hodnoty = dnešní chování (normální 960×540 uprostřed, náhled 320×180, hlavní monitor).
- Validace velikosti (rozsah např. 160×90 až 16384, sladit s `CameraWindowPlacement.MaxSize` a `MinWidth`/`MinHeight` okna).
- Uložení jako nepovinná sekce `printers[*].camera.window`; chybějící sekce = výchozí hodnoty. Neplatné hodnoty ze souboru → výchozí hodnoty s varováním (stejně jako ostatní nastavení). `CameraCredentialScrubber` sekci nemění.
- Testy v Core.Tests: mapování, výchozí hodnoty, neplatné hodnoty, starý soubor bez sekce (fixture z T-014), zpětný zápis, scrubber.

### App (ui-dev)
- Čistá, testovatelná logika výpočtu umístění (např. `CameraWindowDefaultPlacement.Compute(defaults, mode, screens)` nad seznamem `(DisplayName, Bounds, WorkingArea, Scaling, IsPrimary)`): výběr monitoru (název + rozlišení, záloha hlavní), roh / střed v pracovní ploše s okrajem 16 DIP, přepočet DIP → px podle `Scaling` daného monitoru, ořez velikosti na pracovní plochu, celá obrazovka na zvoleném monitoru.
- `CameraWindow`/`CameraViewModel`: při otevření podle `RememberLastPlacement` použije pamatovaný stav, nebo výchozí hodnoty; počáteční režim `Last` = pamatovaný režim. Přepnutí do malého náhledu bez pamatované pozice použije výchozí roh náhledu.
- `ICameraWindowStateStore`: metoda pro smazání stavu tiskárny; volá se po uložení nastavení se změněnými výchozími hodnotami.
- Seznam monitorů přes abstrakci (např. `IScreenCatalog` nad `Screens`), aby šlo v testech podvrhnout víc monitorů (headless má jeden). Na začátku ověřit, co `Screen.DisplayName` vrací na Windows; když to není čitelný název, popisek v nastavení bude „Monitor n (1920×1080)“ a pro identifikaci se stejně uloží `DisplayName` + rozlišení. Nepřipojený zvolený monitor se v seznamu zobrazí s označením „nepřipojený“.
- Sekce **Okno kamery** na podzáložce Kamera (společně s T-037): Pamatovat poslední pozici a velikost, počáteční režim, monitor, pro normální okno a malý náhled velikost (předvolba / vlastní) a umístění. Pole zašedlá u vypnuté kamery. Rozložení podle schváleného návrhu UI z T-033, bez posuvníku.
- Texty ve všech 8 jazycích.

## Akceptační kritéria
- [ ] Core: model, validace a uložení `camera.window`; starý soubor bez sekce = výchozí hodnoty; neplatné hodnoty → výchozí + varování; testy v Core.Tests
- [ ] `RemoveCameraCredentialsAsync` nechá sekci `window` beze změny (test)
- [ ] Výpočet umístění (App.Tests, jen data): 1 / 2 / 3 monitory (vedle sebe, nad sebou, se zápornými souřadnicemi), `Scaling` 100/125/150/200 %, 4 rohy + střed s okrajem 16 DIP, velikost větší než pracovní plocha (ořez), chybějící monitor → hlavní, monitor se stejným názvem a jiným rozlišením, celá obrazovka na zvoleném monitoru
- [ ] Pamatování (App.Tests s `MemoryCameraWindowStateStore`): zapnuto + nic zapamatováno → výchozí; zapnuto + zapamatováno → pamatovaný stav; vypnuto → vždy výchozí; počáteční režim Jako minule / Normální / Malý náhled / Celá obrazovka; uložení změněných výchozích hodnot smaže stav jen dané tiskárny, nezměněné ho nemaže
- [ ] View model nastavení: načtení, předvolby i vlastní velikost, validace, monitor „nepřipojený“, zašedlá pole u vypnuté kamery, přenos do `PrinterDefinition` (App.Tests)
- [ ] Otevřené okno se po uložení nastavení nepřesune (test)
- [ ] Headless UI testy: okno kamery se otevře v nastavené velikosti, režimu a rohu headless obrazovky (normální, malý náhled, celá obrazovka); sekce Okno kamery v nastavení (výběr hodnot, uložení); snímek reference podzáložky Kamera aktualizovaný; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [ ] Ručně na Windows se 2 monitory: otevření na zvoleném monitoru a v rohu, celá obrazovka na zvoleném monitoru, odpojený monitor → hlavní (zapsat do poznámek)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish `win-x64` bez varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

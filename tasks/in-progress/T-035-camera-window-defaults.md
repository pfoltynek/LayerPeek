---
id: T-035
title: Výchozí velikost, monitor a umístění okna kamery (nastavení u tiskárny)
status: in-progress
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
- [x] Core: model, validace a uložení `camera.window`; starý soubor bez sekce = výchozí hodnoty; neplatné hodnoty → výchozí + varování; testy v Core.Tests
- [x] `RemoveCameraCredentialsAsync` nechá sekci `window` beze změny (test)
- [ ] Výpočet umístění (App.Tests, jen data): 1 / 2 / 3 monitory (vedle sebe, nad sebou, se zápornými souřadnicemi), `Scaling` 100/125/150/200 %, 4 rohy + střed s okrajem 16 DIP, velikost větší než pracovní plocha (ořez), chybějící monitor → hlavní, monitor se stejným názvem a jiným rozlišením, celá obrazovka na zvoleném monitoru
- [ ] Pamatování (App.Tests s `MemoryCameraWindowStateStore`): zapnuto + nic zapamatováno → výchozí; zapnuto + zapamatováno → pamatovaný stav; vypnuto → vždy výchozí; počáteční režim Jako minule / Normální / Malý náhled / Celá obrazovka; uložení změněných výchozích hodnot smaže stav jen dané tiskárny, nezměněné ho nemaže
- [ ] View model nastavení: načtení, předvolby i vlastní velikost, validace, monitor „nepřipojený“, zašedlá pole u vypnuté kamery, přenos do `PrinterDefinition` (App.Tests)
- [ ] Otevřené okno se po uložení nastavení nepřesune (test)
- [ ] Headless UI testy: okno kamery se otevře v nastavené velikosti, režimu a rohu headless obrazovky (normální, malý náhled, celá obrazovka); sekce Okno kamery v nastavení (výběr hodnot, uložení); snímek reference podzáložky Kamera aktualizovaný; test „bez posuvníku“ z T-033 projde ve všech 8 jazycích
- [ ] Ručně na Windows se 2 monitory: otevření na zvoleném monitoru a v rohu, celá obrazovka na zvoleném monitoru, odpojený monitor → hlavní (zapsat do poznámek)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish `win-x64` bez varování

## Otevřené otázky
- **Q1 – výchozí roh malého náhledu (core-dev, 2026-10-19):** zadání ho neuvádí (dnes se náhled poprvé otevře na pozici normálního okna). Použito **vpravo dole** podle drátěného modelu T-033 (`CameraWindowDefaults.DefaultMini`). Potvrdit, nebo zvolit jiný roh.
- **Q2 – „Jako minule“ při vypnutém přepínači (core-dev):** přepínač se týká pozice a velikosti, proto `Jako minule` i při vypnutém přepínači použije zapamatovaný režim (jinak Normální). Alternativa: při vypnutém přepínači vždy Normální. Potvrdit.
- **Q3 – co přesně maže zapamatovaný stav (core-dev):** maže změna počátečního režimu, monitoru, velikosti nebo umístění (normální okno i náhled); samotná změna přepínače „Pamatovat…“ stav nemaže a volby „Vždy navrchu“ z T-037 ho mazat nemají. Potvrdit.
- **Q4 – shoda monitoru (core-dev):** přesně podle rozhodnutí – název (rozlišuje velikost písmen) **a** rozlišení, jinak hlavní monitor. Důsledek: otočený monitor (prohozená šířka/výška) nebo změna rozlišení = „nepřipojený“ → hlavní. Alternativa: když se rozlišení liší, vzít monitor se stejným názvem. Ponechat přesnou shodu?
- **Q5 – monitor při přepnutí režimu za běhu (pro ui-dev):** rozhodnutí určuje monitor pro otevření okna. Při přepnutí do malého náhledu bez zapamatované pozice / do celé obrazovky (F11) za běhu – zvolený monitor, nebo monitor, kde okno právě je (dnešní chování celé obrazovky)? Core nabízí obojí (`Compute` × `Place`).
- **Q6 – neplatná velikost u vypnuté kamery (pro ui-dev):** podle T-033 Q9 se nevaliduje, ale model neplatnou velikost uložit neumí. Návrh: uloží se poslední platná (uložená) hodnota. Potvrdit.

## Poznámky k implementaci

### Core (core-dev, 2026-10-19)
- **Model** (`Core/Cameras`): `CameraWindowDefaults` (`RememberLastPlacement` = `true`, `StartMode` = `Last`, `Monitor` = `null` = hlavní, `Normal` = 960×540 `Center`, `Mini` = 320×180 `BottomRight` – viz Q1), `CameraWindowLayout` (velikost + roh), `CameraWindowSize` (celé DIP, šířka 160–16384, výška 90–16384 = `MinWidth`/`MinHeight` okna kamery a `CameraWindowPlacement.MaxSize`; `Presets` 640×360 … 1920×1080), `CameraMonitorId` (název + rozlišení v px; název 1–256 znaků bez řídicích znaků, rozlišení 1–65535), výčty `CameraWindowStartMode`, `CameraWindowCorner`. Neměnné záznamy s hodnotovou rovností, neplatné hodnoty konstruktor/`init` odmítne.
- `CameraSettings.Window` (výchozí `CameraWindowDefaults.Default`) je součástí rovnosti i `ToString` (adresa dál skrytá). Model je u `CameraSettings`, ne přímo u `PrinterDefinition`: patří ke kameře a zachová se i u vypnuté kamery a neplatné adresy.
- `CameraWindowMode` přesunutý z App do `Core/Cameras` (stejné hodnoty, jen jmenný prostor; v App doplněné `using`), protože ho používá výpočet umístění a pravidlo otevření.
- **Uložení**: `printers[*].camera.window` (`rememberLastPlacement`, `startMode`, `monitor {displayName, width, height}`, `normal`/`mini {width, height, corner}`), formát dál `version: 1`. Sekce se nezapisuje, dokud jsou všechny hodnoty výchozí (soubory zůstanou jako dřív a budou sledovat případnou změnu výchozích hodnot); `monitor` se nezapisuje pro hlavní monitor. Načtení je benevolentní: chybějící = výchozí; neznámý režim / roh, neplatná velikost (mimo rozsah, desetinná, NaN/∞, jen jeden rozměr) nebo neplatný monitor → výchozí hodnota té části + jedno varování, bez opakování hodnot ze souboru. Hodnota špatného JSON typu (např. `"window": 5`) dál znamená poškozený soubor jako u ostatních nastavení.
- `CameraCredentialScrubber` beze změny – vnořené objekty v `camera` nechává byte po bytu; nový test to ověřuje pro sekci `window` (i s „podezřelými“ `streamUrl`/`enabled` uvnitř).
- **Umístění** (`CameraWindowDefaultPlacement`, čisté funkce nad `CameraScreen {DisplayName, Bounds, WorkingArea, Scaling, IsPrimary}` v px): `SelectScreen` (přesná shoda názvu a rozlišení, jinak hlavní / první obrazovka, příznak `PreferredMonitorMissing`), `Compute(defaults, mode, screens, frameWidth, frameHeight)` (normální okno / náhled v rohu s okrajem 16 DIP převedeným podle `Scaling`, ořez na pracovní plochu, celá obrazovka = celý zvolený monitor) a `Place(layout, screen, …)` pro konkrétní obrazovku. Rámeček okna (titulek) se zadává v DIP a počítá se do vnějšího obdélníku. Umístění je v Core (ne v App, jak uvádělo zadání), protože nepotřebuje žádné typy Avalonia; testy jsou v Core.Tests (1–4 monitory, záporné souřadnice, 100/125/150/200 %, všechny rohy a střed, ořez, chybějící monitor, stejný název s jiným rozlišením, celá obrazovka).
- **Pamatovaný stav** (`CameraWindowOpenPolicy.Decide` → `CameraWindowOpenPlan`): pamatovaná pozice normálního okna a náhledu zvlášť jen při zapnutém přepínači a existujícím stavu; režim = pevný `StartMode`, nebo u `Last` pamatovaný režim (jinak Normální) – viz Q2. `CameraWindowDefaults.RequiresForgettingRememberedState(uložené, nové)` řekne App, kdy po uložení smazat stav tiskárny v `camera-windows.json` (viz Q3); samotné mazání je na App.
- T-037 (vždy navrchu): do modelu zatím nepřidáno – T-037 nechává umístění voleb „podle stavu po T-035“. Doporučení: `CameraWindowDefaults.TopmostNormal`/`TopmostMini` v téže sekci `window`, nezahrnout je do `RequiresForgettingRememberedState`.
- Nový balíček žádný. `dotnet build` 0 varování, `dotnet test` 2933 testů (2932 prošlo, 1 dřívější přeskočený).

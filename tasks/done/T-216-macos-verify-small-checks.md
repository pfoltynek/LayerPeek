---
id: T-216
title: macOS ověření – drobné kontroly (autoclose, špendlík a editor výřezu přes AX, jazyk AppKitu, falešné `open`, HW dekódování, české rozložení)
status: done
owner: qa-reviewer
priority: P3
platform: macos
depends_on: [T-209, T-213]
---

> **Rozhodnuto uživatelem 2026-11-13:** kontrola popisků a stavů přes Accessibility nahrazuje poslech VoiceOveru
> (T-122, T-117, T-154, T-161).

## Cíl
Sada menších kontrol, které uzavřou jednotlivé body T-127. Každá je samostatná kontrola v presetu `release` (T-208).

## Rozsah
- **`mac.camera-autoclose`** (Auto, potřebuje odemčenou obrazovku, jinak `SKIP`) – **T-151** fokus: spustí
  `LAYERPEEK_E2E=1 dotnet test --project tests/LayerPeek.App.Tests -- --filter-class "*MacCameraWindowAutoCloseE2ETests"`.
  `PASS` jen tehdy, když test opravdu proběhl. Výsledek „vynecháno … the screen is locked“ = `SKIP`. Použije
  `Invoke-DotnetTest`.
- **`mac.camera-pin-ax`** (Permission: Zpřístupnění) – **T-122** a VoiceOver část **T-117**: v normálním okně i v malém
  náhledu má špendlík v AX popisek „Vždy navrchu: zapnuto / vypnuto“ podle stavu a AXPress ho přepne. Na celé
  obrazovce špendlík v AX není. Otevřené Nastavení ukáže stav. Rozšíření `mac.camera-pin` (T-115) o čtení popisku,
  ne nová infrastruktura.
- **`mac.detection-region`** (Permission) – **T-154** editor výřezu:
  - testovací bundle se simulátorem a kamerou;
  - „Načíst snímek“ načte snímek simulátoru (AX: obrázek přítomen);
  - tažení myší (`helper` drag: nový příkaz `drag <x1> <y1> <x2> <y2>`), šipky, ⌘+šipky o 10 % změní hodnoty;
  - AX popisek „Výřez podložky: … % zleva …“ odpovídá hodnotám v `settings.json` po Uložit;
  - „Výchozí pro Core One“ nastaví výchozí hodnoty z kódu.

  Snímek ze skutečné Buddy3D a to, že výchozí výřez sedí na přední část podložky, zůstávají ruční (skutečná kamera).
- **`mac.appkit-language`** (Permission) – **T-097 otázka 5:** testovací bundle v jazyce LayerPeeku jiném, než je jazyk
  systému (např. LayerPeek `de`, systém podle stroje). Zapsat jazyk titulků tlačítek panelu výběru obrázku tiskárny
  (`helper panel` z T-213) a položek kontextového menu textového pole (`ctrlclick` do pole + AX menu). Kontrola je
  vždy `PASS` se zjištěním ve zprávě (rozhodovací bod, ne chyba). Zjištění se zapíše do T-097.
- **`mac.open-fake`** (Auto) – **T-053** (L4):
  - aplikace dostane v `PATH` falešné `open`, které při spuštění zapíše značku do dočasné složky;
  - AXPress na odkaz v O aplikaci a „Otevřít PrusaLink“ v menu (simulátor). Falešné `open` se nespustí (značka
    chybí), otevře se výchozí prohlížeč (nový proces / okno prohlížeče, pak ho zavřít). Pokud by to měnilo stav
    prohlížeče uživatele, viz otázka 1;
  - licence v O aplikaci se otevřou (AX okno).

  Přihlášení do webu PrusaLink přes proxy ověří E2E testy T-028 / T-107 (Edge). Tady jen zapsat, že web simulátoru
  odpověděl přes proxy (HTTP 200 z adresy, kterou aplikace otevřela).
- **`mac.hw-decoding-switch`** (Permission) – **T-205 (4) a (5):**
  - (4) při otevřeném okně kamery přepnout „Hardwarové dekódování“ Automaticky ↔ Vypnuto a Uložit. Do ~2 s
    (klíčový snímek) obraz běží dál (`regiondiff`, je-li Záznam obrazovky, jinak AX bez „Bez signálu“), v logu řádek
    „Camera video decoding: …“ s novou cestou;
  - (5) dva simulátory (`--port 18080 --camera-port 8554` a `--port 18081 --camera-port 8555`, klip
    `coreone-20261103-1905-60s.mp4 --fps 25`), dvě tiskárny, dvě okna. Obě okna mají obraz a běží právě jeden
    `VTDecoderXPCService` (`pgrep -x`).

  Release AOT bundle se tu nepoužívá, stačí testovací bundle (Debug). Rozdíl Debug / Release viz otázka 2.
- **`mac.keys-czech-layout`** (Permission) – **T-100 otázka 1:** helper přepne vstupní zdroj na české rozložení (nový
  příkaz `inputsource <id>` přes Text Input Sources API, předchozí zdroj vždy obnovit, i po chybě). Kontrola pošle
  ⌘⇧] / ⌘⇧[ fyzickými kódy kláves (vpravo od P) a ověří přepnutí hlavní záložky Nastavení (AX vybraná záložka).
  Zároveň ověří, že se nestalo nic jiného: žádné nové okno, žádný změněný text v poli. Fyzická vestavěná a Logitech
  klávesnice (`mac.keyboard-physical`) zůstávají ruční.

## Akceptační kritéria
- [x] `mac.camera-autoclose` = `PASS` s odemčenou obrazovkou (uzavře v T-127 bod **T-151** „Automaticky otevřené okno
      nebere fokus“) → přesunuto do T-127 (oddíl 1q)
- [x] `mac.camera-pin-ax` = `PASS` (uzavře v T-127 bod **T-122** a VoiceOver část bodu **T-117**) → přesunuto do T-127
      (oddíl 1q)
- [x] `mac.detection-region` = `PASS` (uzavře v T-127 část editoru výřezu bodu **T-154** kromě skutečné Buddy3D)
      → přesunuto do T-127 (oddíl 1q)
- [x] `mac.appkit-language` zapsané (uzavře v T-127 bod **T-097** otázka 5; zjištění i v T-097) → přesunuto do T-127
      (oddíl 1q)
- [x] `mac.open-fake` = `PASS` (uzavře v T-127 bod **T-053** kromě ručního přihlášení přes proxy, pokud ho E2E
      nepokrývají) → přesunuto do T-127 (oddíl 1q)
- [x] `mac.hw-decoding-switch` = `PASS` (uzavře v T-127 body **T-205** (4) a (5)) → přesunuto do T-127 (oddíl 1q)
- [x] `mac.keys-czech-layout` = `PASS` nebo zjištění s novým úkolem (uzavře v T-127 bod **T-100** otázka 1 pro
      softwarové rozložení; fyzické klávesnice zůstávají) → přesunuto do T-127 (oddíl 1q)
- [x] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
1. `mac.open-fake`: smí kontrola otevřít odkaz ve výchozím prohlížeči uživatele (nová karta / okno, historie
   prohlížeče)? Druhá možnost: ověřit jen, že se falešné `open` nespustilo a že aplikace zalogovala otevření adresy,
   bez skutečného prohlížeče.
2. `mac.hw-decoding-switch`: stačí testovací bundle (Debug JIT), nebo má (4) / (5) běžet na Release AOT bundlu
   z `release-build.ps1`? Release nemá testovací režim, kontrola by musela dočasně odsunout profil jako
   `aot-smoke.ps1`.

Rozhodnuto uživatelem 2026-11-13: 1 **smí** otevřít odkaz ve výchozím prohlížeči; 2 **stačí testovací Debug bundle** (Release AOT změřil T-205).

## Poznámky k implementaci
Implementováno 2026-11-15 na Macu (qa-reviewer, worktree `LayerPeek-t216`), postupem T-210–T-215. Terminál bez
Zpřístupnění, obrazovka během běhů zamčená – skutečné běhy sedmi kontrol jsou bod 1q v T-127.

- **Aplikace beze změny** (`src/` nedotčeno, žádný nový testovací přepínač, bez nových balíčků). Odkazy v O aplikaci
  nemají AutomationId: kontrola je najde v AX stromu podle textu a klikne na ně.
- **Helper** (`tools/macos-verify-helper.swift`, nově `import Carbon`, překlad bez varování):
  - `inputsource` – JSON `current` a `enabled` (zapnutá rozložení);
  - `inputsource <id> <s>` – vybere zdroj (nezapnutý zapne a potom zase vypne), vypíše řádek JSON (`previous`, `selected`,
    `enabledHere`) a drží ho do SIGTERM / SIGINT / SIGHUP nebo do uplynutí <s>; pak vždy vybere předchozí zdroj a vypíše
    `restored`, `current`. Exit 1 neznámý zdroj, 2 nejde vybrat (předchozí se vybere zpět). Kontrola ho ukončí SIGTERM
    ve `finally`; kdyby skript spadl, vrátí rozložení sám nejpozději po 5 min;
  - `apps <bundle id>` – pid běžících aplikací (NSWorkspace), `terminate <pid>` – ukončení jako „Ukončit“
    (NSRunningApplication.terminate, bez oprávnění Apple Events); pro prohlížeč, který spustily odkazy.
  - `drag` (T-215) se použil beze změny.
- **Kontroly** (`tools/macos-verify.ps1`, preset `release` za `mac.camera-next-attempt`, teď 42 kontrol; `-SelfTest` 52):
  - `mac.camera-autoclose` (Auto, `Session`): `Invoke-DotnetTest` s `LAYERPEEK_E2E=1` a `--filter-class
    "*MacCameraWindowAutoCloseE2ETests"`; `PASS` jen při proběhnutém testu, „… the screen is locked“ = `SKIP`, selhání
    se „No active display“ (uspané displeje, aplikace čeká na displej) = `SKIP` s radou `caffeinate -d -u`.
  - `mac.camera-pin-ax` (Auto, `Session` + `Accessibility`): česky (`Get-AppTexts 'cs'` = `Strings.cs.resx` přes
    angličtinu), normální okno i malý náhled: AX jméno `CameraTopmostButton` = `CameraTopmostOn` / `Off` podle hodnoty,
    dvakrát AXPress; otevřené Nastavení › Tiskárny › Okno kamery: `PrinterCameraWindow{Normal,Mini}Topmost` ukazuje stav
    a řídí se dalším AXPress; celá obrazovka (AXPress `CameraFullScreenButton`) bez špendlíku v AX, zpět tlačítkem režimu.
    Rozšiřuje `mac.camera-pin` (ten zůstává beze změny), žádná nová infrastruktura.
  - `mac.detection-region` (Permission): tiskárna s obrázkem Core One (`New-TestData -PrinterOptions`) a detekcí;
    výchozí výřez = `DetectionRegion.CoreOneBed` (přečteno ze zdrojáku), „Načíst snímek“ (zmizí `PrinterDetectionNoFrame`,
    žádné `…LoadFrameFailed`), `drag` z 5 / 5 % do 45 / 40 % snímku (obdélník obrázku jako `DetectionRegionGeometry.ImageRect`
    pro 16:9, ±2 %), fokus v editoru, 9 kláves (šipky 1 %, ⌘ 10 %, Shift velikost; každá přesně), Uložit →
    `printers[0].detection.region` = jméno, po novém otevření stejné; „Default for Core One“ + Uložit = přesně hodnoty kódu.
  - `mac.appkit-language` (Permission): LayerPeek `de` (na německém systému `fr`, `Get-VerifyOtherLanguage`), systém
    z `AppleLanguages`; panel přes `PrinterImageButton` › `PrinterImageCustom` (helper `panel`, `panelcancel`), kontextové
    menu `PrinterName` (⌃-klik, jinak pravé tlačítko, položky `AXMenuItem`); jazyk titulků `Get-VerifyTitleLanguages`.
    `PASS` se zjištěním (do zprávy a `finding.txt`), `FAIL` jen když nejde přečíst nic.
  - `mac.open-fake` (Auto, `Session` + `Accessibility`): falešné `open` (jen `: > značka`, nikdy argumenty s tokenem)
    první v `PATH` aplikace (ověřeno `command -v open`), podrobné logování; odkaz na GitHub → výchozí prohlížeč
    (`Get-VerifyDefaultBrowser` z LaunchServices, `apps`) vpředu; licence → okno `LicensesTitle`; „Open PrusaLink“
    (`menupress`) → `Web proxy: GET … answered 200` (`Get-VerifyProxyAnswers`); značka chybí. Úklid: nová okna prohlížeče
    `axwin … close`, prohlížeč, který neběžel, `terminate`; panel v už otevřeném okně zůstane (poznámka ve zprávě).
  - `mac.hw-decoding-switch` (Permission, volitelně Nahrávání obrazovky): (4) Off a Automatic v Nastavení › Kamera
    (výběr v seznamu jako `mac.timelapse-layers`), Uložit, `cameraHardwareDecoding` v `settings.json`, nový řádek
    `Get-VerifyDecodingPaths` (`software (…)` / `videotoolbox`) do 10 s se změřeným časem, obraz běží (`regiondiff` dvou
    snímků, bez Nahrávání obrazovky AX: `CameraOverlay` bez „No signal“ / „Connecting…“ do 5 s). (5) druhý simulátor
    (`Get-Simulator -Instance`, `-Fps 25`, klip Buddy3D; volné porty místo 18080 / 18081, aby nekolidovaly), druhá
    tiskárna (`Add-TestPrinter`), dvě okna, dvakrát `videotoolbox`, RTSP spojení obou kamer, právě jeden nový
    `VTDecoderXPCService` proti stavu před startem (`pgrep -x`). Testovací Debug bundle (rozhodnutí 2026-11-13).
  - `mac.keys-czech-layout` (Permission): `inputsource com.apple.keylayout.Czech 300` na pozadí, fokus v `PrinterName`,
    ⌘⇧] a ⌘⇧[ (helper `key`, fyzické kódy 30 / 33) → `Get-VerifyTabMoveProblem` tam a zpět, počet oken a text pole beze
    změny; ve `finally` SIGTERM a kontrola, že je vybrané předchozí rozložení. `FAIL` = nový úkol (T-100 otázka 1).
  - Společné: `New-TestData -Language / -SettingsOptions / -PrinterOptions`, `Get-Simulator -Fps / -Instance`,
    `Add-TestPrinter`, `Get-AppTexts`. Čisté funkce ve `VerifyTools.psm1` (oddíl T-216): `ConvertFrom-VerifyFormattedText`,
    `Get-VerifyRegionProblem`, `ConvertTo-VerifyRegionPercent`, `Get-VerifyDecodingPaths`, `Get-VerifyTitleLanguages`,
    `Get-VerifyOtherLanguage`, `Get-VerifyDefaultBrowser`, `Get-VerifyProxyAnswers`, `Get-VerifyTabMoveProblem`; Pester blok
    „Small checks“ (9 testů včetně presetu). Dokumentace: tabulka kontrol, Zpřístupnění, vzorky a pozůstatky
    v `docs/macos-manual-testing.md`; T-127 oddíl 1q a tabulka automatizace.
- **Ověřeno skutečně (bez Zpřístupnění):** `mac.camera-autoclose` pod `caffeinate -d -u -i` celý proběhl
  (`artifacts/verify/20261115-124609/`): E2E test zavřel a znovu otevřel okno, fokus „vynecháno … the screen is locked
  (the login window is in front)“ → `SKIP`. Bez `caffeinate` (spící displeje) test selhal na „No active display“ – proto
  mapování na `SKIP`. Sonda mimo skript (Debug aplikace v testovacím režimu, dva simulátory s klipem Buddy3D, 25 fps,
  dvě okna přes testovací příkaz): log dvakrát „Camera video decoding: videotoolbox.“, `VTDecoderXPCService` před 1 (jiná
  aplikace), potom 2 = právě jeden nový; `Get-VerifyDecodingPaths` čte skutečné řádky. Helper `inputsource` (vybrání
  už vybraného českého rozložení, SIGTERM i uplynutí času → `restored`), `apps`, `terminate` (neběžící pid), neznámý zdroj
  exit 1. Výchozí prohlížeč tohoto Macu podle LaunchServices: `com.apple.safari` (běží – v reálném běhu se odkazy
  otevřou v něm).
- **Neověřeno proti skutečnému systému** (bez Zpřístupnění a se zamčenou obrazovkou nešlo): AX jména špendlíku
  v češtině a checkboxů v Nastavení, jméno editoru výřezu (AXTitle, jinak AXDescription), `drag` v editoru a klávesy,
  výběr v seznamu „Hardware decoding“, panel a kontextové menu textového pole v AX, klik na `HyperlinkButton` v O aplikaci,
  okna prohlížeče v AX, ⌘⇧] s fokusem v textovém poli s českým rozložením.
- **Ověřeno 2026-11-15:** `pwsh tools/macos-verify.ps1 -Unattended -Only mac.camera-autoclose,mac.camera-pin-ax,
  mac.detection-region,mac.appkit-language,mac.open-fake,mac.hw-decoding-switch,mac.keys-czech-layout,mac.profile-unchanged
  -Allow …`, běh `artifacts/verify/20261115-124253/`: sedm `SKIP` (předběžné podmínky Session – zamčená obrazovka – a
  u šesti Accessibility), `PASS mac.profile-unchanged`. `-SelfTest` PASSED (52 kontrol), `-ListChecks -Preset release`
  42 kontrol, čtyři nové Permission mezi povolenými předem. Žádná Release aplikace se nespouštěla, profil uživatele beze
  změny, vstupní zdroj beze změny (`com.apple.keylayout.Czech`). Nic na GitHub.
- **Build a testy 2026-11-15** (na `cf9e4201` = aktuální lokální `develop` + tato změna; `origin/develop` je o 7 commitů
  napřed – T-224, T-220, T-186 z Windows – a do této větve sloučený není): `dotnet build` Debug i `-c Release` 0 varování,
  0 chyb. Plný `dotnet test`: 10 848 testů, 10 577 prošlo, 271 přeskočeno, 0 selhalo. Pester `tools/VerifyTools.Tests.ps1`
  102 prošlo / 1 přeskočen (Windows) / 0 selhalo. AOT publish nespuštěn: `src/` beze změny.
- Uzavřeno 2026-11-15: zbývající ruční ověření (běhy sedmi kontrol se Zpřístupněním a odemčenou obrazovkou, zápis
  zjištění `mac.appkit-language` do T-097 a výsledku `mac.hw-decoding-switch` do T-205) přesunuto do T-127, oddíl 1q
  (podle zadání úkolu, postup T-210–T-215)

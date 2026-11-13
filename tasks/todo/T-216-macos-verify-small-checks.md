---
id: T-216
title: macOS ověření – drobné kontroly (autoclose, špendlík a editor výřezu přes AX, jazyk AppKitu, falešné `open`, HW dekódování, české rozložení)
status: todo
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
- [ ] `mac.camera-autoclose` = `PASS` s odemčenou obrazovkou (uzavře v T-127 bod **T-151** „Automaticky otevřené okno
      nebere fokus“)
- [ ] `mac.camera-pin-ax` = `PASS` (uzavře v T-127 bod **T-122** a VoiceOver část bodu **T-117**)
- [ ] `mac.detection-region` = `PASS` (uzavře v T-127 část editoru výřezu bodu **T-154** kromě skutečné Buddy3D)
- [ ] `mac.appkit-language` zapsané (uzavře v T-127 bod **T-097** otázka 5; zjištění i v T-097)
- [ ] `mac.open-fake` = `PASS` (uzavře v T-127 bod **T-053** kromě ručního přihlášení přes proxy, pokud ho E2E
      nepokrývají)
- [ ] `mac.hw-decoding-switch` = `PASS` (uzavře v T-127 body **T-205** (4) a (5))
- [ ] `mac.keys-czech-layout` = `PASS` nebo zjištění s novým úkolem (uzavře v T-127 bod **T-100** otázka 1 pro
      softwarové rozložení; fyzické klávesnice zůstávají)
- [ ] Pester / `-SelfTest` a `dotnet build` / `dotnet test` bez chyb a varování

## Otevřené otázky
1. `mac.open-fake`: smí kontrola otevřít odkaz ve výchozím prohlížeči uživatele (nová karta / okno, historie
   prohlížeče)? Druhá možnost: ověřit jen, že se falešné `open` nespustilo a že aplikace zalogovala otevření adresy,
   bez skutečného prohlížeče.
2. `mac.hw-decoding-switch`: stačí testovací bundle (Debug JIT), nebo má (4) / (5) běžet na Release AOT bundlu
   z `release-build.ps1`? Release nemá testovací režim, kontrola by musela dočasně odsunout profil jako
   `aot-smoke.ps1`.

Rozhodnuto uživatelem 2026-11-13: 1 **smí** otevřít odkaz ve výchozím prohlížeči; 2 **stačí testovací Debug bundle** (Release AOT změřil T-205).

## Poznámky k implementaci
(vyplní agent po dokončení)

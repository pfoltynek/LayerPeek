---
id: T-203
title: Kamera – hardwarové dekódování H.264, sdílený základ a VideoToolbox (macOS)
status: todo
owner: core-dev
priority: P2
platform: all
depends_on: [T-192]
---

## Cíl
Průzkum T-192 ([docs/hw-decoding.md](../../docs/hw-decoding.md)) ukázal, že softwarové dekódování H.264 je i po T-184,
T-185 a T-195 50–69 % instrukcí aplikace s otevřenou kamerou. VideoToolbox s `av_hwframe_map` je sníží o 45–63 %.
Uživatel 2026-11-12 rozhodl (T-192, otázky 2–5): implementovat, **nejdřív macOS**, software jako automatický návrat
a pro samostatné klíčové snímky detekce, volba „Hardwarové dekódování“ Automaticky / Vypnuto (výchozí Automaticky),
Windows (D3D11VA) souběžně v T-206, na Windows ale výchozí software, dokud ho T-125 nezměří.

Tento úkol staví sdílený základ v `Camera.FFmpeg` a model nastavení v Core a zapíná VideoToolbox na macOS. UI volby je
T-204, ověření a měření na Macu T-205, Windows T-206. **Doporučené pořadí: implementovat na Macu** (priorita uživatele
a jen tam dnes existuje hardwarová cesta, kterou testy skutečně projdou). Na Windows se dá úkol udělat také, testy VT
se tam přeskočí a ověří je T-205.

Priorita při implementaci: minimum prostředků (žádné kopie snímků navíc, žádné alokace v cestě snímku, hardwarová
relace jen při plném dekódování), výkon, čitelný a robustní kód.

## Rozsah
- **Core – model nastavení:** `AppSettings.CameraHardwareDecoding` s neutrálním výčtem v Core (např.
  `Core/Cameras/CameraHardwareDecoding { Automatic, Off }`), výchozí `Automatic`, jedna hodnota pro všechny kamery.
  Uložení v `settings.json` (`SettingsDocument` / `SettingsMapper`, neznámá hodnota → výchozí, chybějící → výchozí),
  `SettingsExport` ho do diagnostického balíčku zahrne. Core nezná FFmpeg ani typ zařízení, jen „automaticky / vypnuto“.
- **Předání do dekodéru:** `FFmpegCameraFeedFactory` dostane aktuální režim bez reflexe a bez zámků v cestě snímku
  (např. vlastnost s `volatile` polem, kterou nastaví pozorovatel nastavení v App po načtení a každém uložení, stejně
  jako `DebugLoggingObserver`). Každý dekodér si režim přečte jednou při otevření plného dekódování. Chování po
  změně volby za běhu (hned / při dalším připojení) je otázka 2 v T-204; tento úkol jen zajistí, že nové otevření
  dekodéru použije aktuální hodnotu.
- **Pravidlo podle OS (jedno místo v `Camera.FFmpeg`, např. `HardwareDecoderSelection`):** `Automatic` → macOS
  `AV_HWDEVICE_TYPE_VIDEOTOOLBOX`, Windows **žádné zařízení (software)**, dokud uživatel po měření v T-125 nerozhodne
  jinak (T-206 doplní D3D11VA jen za vývojový přepínač). `Off` → vždy software. Jiné typy (NVDEC, QSV, AMF, Vulkan)
  se nepoužívají.
- **Vývojový přepínač (jen Debug, testovací režim T-044):** proměnná prostředí `LAYERPEEK_TEST_HW_DECODER` = `off` |
  `videotoolbox` | `d3d11va` přebije pravidlo pro měření (T-205, T-125). V Release se nečte (podmíněný překlad jako
  ostatní `LAYERPEEK_TEST_*`). Neznámá hodnota → zápis do logu a software.
- **`FFmpegDecoder` – hardwarová cesta jen pro plné dekódování** (okno kamery, malý náhled, celoobrazovka):
  - `av_hwdevice_ctx_create` (jednou za otevření plného dekódování; `AVBufferRef` se uvolní přes `av_buffer_unref`
    při zastavení plného dekódování i v `Dispose`), `AVCodecContext.hw_device_ctx`, `get_format` jako ukazatel na
    `[UnmanagedCallersOnly]` funkci (jako dnešní callback přerušení): vybere hardwarový formát, jen když ho nabídka
    obsahuje, jinak první softwarový formát (FFmpeg při selhání inicializace hwaccel volá `get_format` znovu bez něj).
  - **Převod jen splatných snímků (T-185):** nepřevedený snímek se nestahuje ani nemapuje. Splatný snímek se namapuje
    `av_hwframe_map` (`AV_HWFRAME_MAP_READ`, bez kopie) do opakovaně použitého `AVFrame`; když mapování selže,
    jednou `av_hwframe_transfer_data` (a dál pro toto otevření rovnou přenos, bez opakovaného pokusu na každém snímku).
    NV12 jde beze změny přes `BgraConverter` / `GrayscaleConverter` (T-184 / T-195 / T-189 platí). Žádné `vtbgra`
    (převod barev ve VideoToolboxu vychází hůř, T-192).
  - **Samostatné klíčové snímky detekce (T-153 / T-189, flush → IDR → drain → flush), `CameraSnapshot` a „Otestovat
    kameru“ zůstávají softwarové.** Hardwarová relace (a s ní `VTDecoderXPCService`, +17–36 MB) existuje jen při plném
    dekódování. Jestli to znamená dva kontexty kodeku (SW pro klíčové snímky, HW pro plné dekódování) nebo znovuotevření
    kontextu při přepnutí, rozhodne implementace podle paměti a jednoduchosti. Zdůvodnit v poznámkách.
  - **Návrat k softwaru:** selže-li vytvoření zařízení, `get_format` hardware nenabídne nebo dekódování v hardwaru
    vrátí chybu, dekodér se pro toto spojení znovu otevře softwarově od dalšího klíčového snímku (bez odpojení RTSP,
    bez stavu chyby v okně kamery). Proti přepínání dokola: po selhání hardwaru jede spojení do konce softwarově, další
    připojení to zkusí znovu, po 2 selháních za sebou zůstane tiskárna softwarová do konce běhu procesu (nebo do změny
    volby). Konkrétní pravidlo zapsat do poznámek a pokrýt testem.
  - **Log:** při otevření plného dekódování jeden řádek `Information` (cesta `videotoolbox` / `software`, důvod
    návratu k softwaru, bez adresy kamery), při návratu k softwaru `Warning` s kódem chyby. Žádné logování na snímek.
- **macOS build FFmpeg:** `build/macos/build-ffmpeg.sh` dostane `--enable-videotoolbox --enable-hwaccel=h264_videotoolbox`
  (nutné kvůli `--disable-autodetect` / `--disable-everything`), komentář v hlavičce skriptu („software decoding only“)
  opravit. Kontrola licence (LGPL 3) a cizích závislostí (`/System/Library/` povolené) zůstává. Klíč cache se změní sám
  (obsah skriptu). Test / kontrola v `build/update-ffmpeg.Tests.ps1` nebo v testech FFmpeg, že knihovny macOS obsahují
  hwaccel `h264_videotoolbox` (např. `av_hwdevice_iterate_types` vrátí VideoToolbox). `docs/prerequisites.md`
  (Xcode Command Line Tools stačí, frameworky VideoToolbox / CoreMedia / CoreVideo jsou součástí macOS).
- **Windows build se v tomto úkolu nemění** (D3D11VA přinese T-202 / T-206).
- **Dokumentace:** `docs/architecture.md` (oddíl „Hardwarové dekódování kamery“ – skutečný stav místo plánu),
  `docs/hw-decoding.md` (stav implementace).

## Akceptační kritéria
- [ ] `AppSettings.CameraHardwareDecoding` (výchozí `Automatic`) se ukládá a načítá, chybějící i neznámá hodnota dá
      výchozí; testy v `LayerPeek.Core.Tests` (mapper, export).
- [ ] Pravidlo podle OS je na jednom místě a má jednotkové testy: `Off` → software všude, `Automatic` → VideoToolbox
      na macOS a software na Windows; přepínač `LAYERPEEK_TEST_HW_DECODER` funguje jen v Debug.
- [ ] Na macOS s VideoToolboxem: okno kamery a malý náhled ukazují obraz z hardwarové cesty (log `videotoolbox`),
      snímky se mapují bez kopie a jen splatné snímky se mapují (test s počítadlem mapování a omezenou frekvencí, T-185).
- [ ] Test s vynuceným softwarem (`Off`) i deterministický test návratu k softwaru na obou OS: požadované zařízení,
      které build nemá (např. typ, který není zkompilovaný, nebo selhání `av_hwdevice_ctx_create` přes testovací šev),
      → spojení dekóduje softwarově bez chyby a bez odpojení, zapíše se jeden `Warning`.
- [ ] Test pravidla proti přepínání dokola (selhání za běhu → zbytek spojení software; 2 selhání → software do konce
      běhu / do změny volby).
- [ ] Klíčové snímky detekce, `CameraSnapshot` a „Otestovat kameru“ jdou softwarem i při `Automatic` (test: bez okna
      kamery se hardwarové zařízení nevytvoří).
- [ ] Testy hardwarové cesty mají vlastní atribut (např. `[HardwareDecodingFact]`), který se bez dostupného zařízení
      přeskočí s jasným důvodem; stávající `[FFmpegFact]` testy projdou s `Off` i `Automatic`.
- [ ] Žádné alokace navíc v cestě snímku (rozšířit `FFmpegCameraFeedAllocationTests` o hardwarovou cestu, kde je
      dostupná) a hardwarové prostředky se uvolní při zastavení plného dekódování, při `Dispose` i po chybě (test).
- [ ] macOS knihovny FFmpeg mají `h264_videotoolbox`, `avcodec_license()` dál „LGPL version 3 or later“ (test), build
      cache se po změně skriptu přestaví sama.
- [ ] Rychlá kontrola na Macu (podrobné měření je T-205): simulátor 720p, normální okno, instrukce procesu
      + `VTDecoderXPCService` podle postupu T-192 klesnou proti `Off` aspoň o 40 %; výsledek v poznámkách.
- [ ] `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` (Native AOT) bez varování trim / AOT.
- [ ] `docs/architecture.md`, `docs/hw-decoding.md` a `docs/prerequisites.md` odpovídají stavu.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k AOT a licencím
- Jen funkce FFmpeg, které vazby FFmpeg.AutoGen 9.0.1.1 už mají (`av_hwdevice_ctx_create`, `hw_device_ctx`,
  `av_hwframe_map`, `av_hwframe_transfer_data`, `av_buffer_unref`) a callback `[UnmanagedCallersOnly]`, žádná reflexe.
  Sonda v T-192 se přeložila bez varování.
- Žádný nový NuGet balíček. FFmpeg zůstává LGPL 3; VideoToolbox je systémový framework (není v seznamu GPL ani nonfree).

## Otevřené otázky
- (žádné blokující) Rozhodnutí uživatele 2026-11-12 jsou v T-192 (otázky 2–5). Zobrazení volby na Windows a chování
  po změně volby za běhu jsou otázky v T-204; tento úkol na nich nezávisí.

## Poznámky k implementaci
(vyplní agent po dokončení)

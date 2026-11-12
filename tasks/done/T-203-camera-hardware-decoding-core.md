---
id: T-203
title: Kamera – hardwarové dekódování H.264, sdílený základ a VideoToolbox (macOS)
status: done
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
- [x] `AppSettings.CameraHardwareDecoding` (výchozí `Automatic`) se ukládá a načítá, chybějící i neznámá hodnota dá
      výchozí; testy v `LayerPeek.Core.Tests` (mapper, export).
- [x] Pravidlo podle OS je na jednom místě a má jednotkové testy: `Off` → software všude, `Automatic` → VideoToolbox
      na macOS a software na Windows; přepínač `LAYERPEEK_TEST_HW_DECODER` funguje jen v Debug.
- [x] Na macOS s VideoToolboxem: okno kamery a malý náhled ukazují obraz z hardwarové cesty (log `videotoolbox`),
      snímky se mapují bez kopie a jen splatné snímky se mapují (test s počítadlem mapování a omezenou frekvencí, T-185).
- [x] Test s vynuceným softwarem (`Off`) i deterministický test návratu k softwaru na obou OS: požadované zařízení,
      které build nemá (např. typ, který není zkompilovaný, nebo selhání `av_hwdevice_ctx_create` přes testovací šev),
      → spojení dekóduje softwarově bez chyby a bez odpojení, zapíše se jeden `Warning`.
- [x] Test pravidla proti přepínání dokola (selhání za běhu → zbytek spojení software; 2 selhání → software do konce
      běhu / do změny volby).
- [x] Klíčové snímky detekce, `CameraSnapshot` a „Otestovat kameru“ jdou softwarem i při `Automatic` (test: bez okna
      kamery se hardwarové zařízení nevytvoří).
- [x] Testy hardwarové cesty mají vlastní atribut (např. `[HardwareDecodingFact]`), který se bez dostupného zařízení
      přeskočí s jasným důvodem; stávající `[FFmpegFact]` testy projdou s `Off` i `Automatic`.
- [x] Žádné alokace navíc v cestě snímku (rozšířit `FFmpegCameraFeedAllocationTests` o hardwarovou cestu, kde je
      dostupná) a hardwarové prostředky se uvolní při zastavení plného dekódování, při `Dispose` i po chybě (test).
- [x] macOS knihovny FFmpeg mají `h264_videotoolbox`, `avcodec_license()` dál „LGPL version 3 or later“ (test), build
      cache se po změně skriptu přestaví sama.
- [x] Rychlá kontrola na Macu (podrobné měření je T-205): simulátor 720p, normální okno, instrukce procesu
      + `VTDecoderXPCService` podle postupu T-192 klesnou proti `Off` aspoň o 40 %; výsledek v poznámkách.
- [x] `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` (Native AOT) bez varování trim / AOT.
- [x] `docs/architecture.md`, `docs/hw-decoding.md` a `docs/prerequisites.md` odpovídají stavu.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k AOT a licencím
- Jen funkce FFmpeg, které vazby FFmpeg.AutoGen 9.0.1.1 už mají (`av_hwdevice_ctx_create`, `hw_device_ctx`,
  `av_hwframe_map`, `av_hwframe_transfer_data`, `av_buffer_unref`) a callback `[UnmanagedCallersOnly]`, žádná reflexe.
  Sonda v T-192 se přeložila bez varování.
- Žádný nový NuGet balíček. FFmpeg zůstává LGPL 3; VideoToolbox je systémový framework (není v seznamu GPL ani nonfree).

## Otevřené otázky
- (žádné blokující) Rozhodnutí uživatele 2026-11-12 jsou v T-192 (otázky 2–5). Zobrazení volby na Windows a chování
  po změně volby za běhu jsou otázky v T-204; tento úkol na nich nezávisí.

## Poznámky k implementaci
Hotovo 2026-11-12 na Macu (Mac mini M2 Pro, macOS 27.2, .NET SDK 11.0.100).

**Co vzniklo**
- **Core:** `Core/Cameras/CameraHardwareDecoding { Automatic, Off }`, `AppSettings.CameraHardwareDecoding` (výchozí
  `Automatic`, součást rovnosti a hashe, nedefinovaná hodnota → výjimka). V `settings.json`
  `"cameraHardwareDecoding": "Off"`; výchozí se nezapisuje (jako `miniPreviewFrameRate`), chybějící → `Automatic`, neznámý
  název → `Automatic` s varováním, jména bez ohledu na velikost písmen, žádná čísla (jako `notifications.sound`).
  `SettingsExport` ho nese stejně jako soubor (`Off` je v balíčku vidět, chybějící znamená `Automatic`).
- **App:** `CameraHardwareDecodingObserver` (pozorovatel `MonitoringSession` před `CameraStreamHubObserver`) nastaví
  `FFmpegCameraFeedFactory.HardwareDecoding` po načtení i po každém uložení; jiný dekodér (testy) nechá být. Továrna se
  vytváří s loggerem. Vývojový přepínač `LAYERPEEK_TEST_HW_DECODER` čte `AppEnvironment` (jen `#if DEBUG`, jen v testovacím
  režimu, mimo něj se hlásí v `IgnoredTestSwitches` jako ostatní), hodnotu předá `UseTestHardwareDecoder` (existuje jen
  v Debug). Na rozdíl od ostatních přepínačů neznámá hodnota start neodmítne: dekodér ji zapíše jako `Warning` a použije
  software (zadání). Přepínač přebije volbu i pravidlo OS.
- **Camera.FFmpeg:**
  - `HardwareDecoderSelection` – jediné pravidlo podle OS (`Off` → software, `Automatic` → macOS VideoToolbox, jinde
    software), parsování přepínače, názvy cest do logu.
  - `HardwareDecodingPolicy` (jedna na továrnu) – aktuální volba jako neměnný `HardwareDecodingChoice` (zařízení, důvod
    softwaru, generace) v `volatile` poli; zápis pod zámkem jen při změně nastavení, dekodér čte bez zámku.
  - `HardwareDecodingHistory` – pravidlo proti přepínání dokola, jedna na kameru (klíč = adresa bez přihlašovacích údajů
    a fragmentu).
  - `FFmpegDecoder` – hardwarová cesta (viz níže), `HardwareDecodingLog` – řádky logu (`[LoggerMessage]`, text chyby
    a název cesty se formátují až při zápisu).
  - `FFmpegRuntime.HasHardwareDecoder` – kontrola buildu (`av_hwdevice_iterate_types` + `avcodec_get_hw_config`).

**Rozhodnutí implementace (zdůvodnění)**
- **Jeden kontext kodeku, ne dva.** Hardwarový kontext (s ním `AVBufferRef` zařízení, které kontext vlastní a
  `avcodec_free_context` ho uvolní přes `av_buffer_unref`) se otevře pro plné dekódování a zavře, když plné dekódování
  skončí (`StopDecoding`, tj. po lhůtě hubu), po chybě a v `Dispose`. Samostatné klíčové snímky detekce pak dostanou nový
  softwarový kontext. Dva souběžné kontexty by držely softwarový dekodér (a jeho pool snímků) i při otevřeném okně
  zbytečně; znovuotevření nic nestojí, protože po zastavení plného dekódování se stejně čeká na klíčový snímek.
- **Kdy hardware:** jen pro čtenáře s `CameraStreamDemand.DisplaySize` (okno kamery a malý náhled, jediný takový čtenář od
  T-184), takže snímek (`CameraSnapshot`), test kamery (neuložené adresy přes továrnu i uložené přes hub), klíčové snímky
  a šedotónové vzorky detekce jsou softwarové bez nového API v Core. Rozhoduje se při startu plného dekódování a na každém
  klíčovém snímku plného dekódování (jedno `volatile` čtení, čtení velikosti a krátký zámek historie jednou za GOP, nic na
  snímek). Přepnutí na klíčovém snímku neztratí žádný snímek. Tím je hotový i mechanismus z T-204 (otázka 2, „hned po
  Uložit“): uložená změna platí od dalšího klíčového snímku běžících oken, bez odpojení RTSP; T-204 už jen přidá UI.
  Okno, které dostane velikost až po rozvržení (T-184: do té doby plné rozlišení), začne softwarově a na prvním klíčovém
  snímku po rozvržení přejde na hardware. Běžící hardwarová relace zůstává i bez čtenáře s velikostí až do konce plného
  dekódování (během lhůty 10 s po zavření okna se tak nepřepíná dvakrát).
- **`get_format`: výchozí funkce FFmpeg místo vlastního callbacku `[UnmanagedCallersOnly]`.** `avcodec_default_get_format`
  dělá přesně to, co zadání chce (s připojeným zařízením vybere jeho formát, jen když ho nabídka obsahuje, jinak první
  softwarový; při selhání inicializace hwaccel ji FFmpeg volá znovu bez hardwarového formátu, `libavcodec/decode.c`).
  Vlastní callback by byl kód navíc se stejným chováním. Že FFmpeg hardware nenastavil, pozná dekodér podle formátu
  prvního snímku: snímek je dobrý (software), hlásí se jako selhání a na dalším klíčovém snímku se dekodér otevře čistě
  softwarově.
- **Převod jen splatných snímků:** v `Convert` se nejdřív rozhodne, co je splatné, a teprve pak se hardwarový snímek
  `av_hwframe_map` (`AV_HWFRAME_MAP_READ`) namapuje do opakovaně použitého `AVFrame` a hned po převodu uvolní
  (`av_frame_unref`, odemkne `CVPixelBuffer`). Selže-li mapování, jeden řádek `Information` a do konce relace
  `av_hwframe_transfer_data` (+ `av_frame_copy_props`, přenos barevné vlastnosti nenese). Selže-li i kopie → návrat
  k softwaru. Žádné `vtbgra`.
- **Návrat k softwaru:** zařízení není v buildu (`avcodec_get_hw_config` ho nezná), `av_hwdevice_ctx_create` selže,
  FFmpeg zvolí softwarový formát, nebo `avcodec_send_packet` / `avcodec_receive_frame` / kopie vrátí v hardwaru jinou chybu
  než `EAGAIN` / `EOF` / `INVALIDDATA` (poškozený snímek se přeskakuje jako v softwaru) → jeden `Warning` s kódem a textem
  chyby. Při chybě dekódování se kontext i zařízení uvolní hned a další klíčový snímek otevře softwarový dekodér (snímky do
  něj se ztratí jako po každé chybě dekódování). Spojení se neodpojí, okno nehlásí chybu.
- **Pravidlo proti přepínání dokola (`HardwareDecodingHistory`, `MaxConsecutiveFailures = 2`):** selhání hardwaru →
  spojení jede do konce softwarově; další spojení stejné kamery to zkusí znovu; po 2 selhaných hardwarových relacích za
  sebou zůstane kamera softwarová do konce běhu procesu. Hardwarová relace, která dekódovala aspoň jeden snímek a skončila
  bez chyby, počet vynuluje. Změna volby (i přepínače) začne novou generaci: selhání starší generace se nepočítají a i
  běžící spojení po selhání to od dalšího klíčového snímku zkusí znovu.
- **Log:** při startu i přepnutí plného dekódování jeden řádek `Information` „Camera video decoding: videotoolbox.“ nebo
  „Camera video decoding: software (turned off in the settings | not used on this OS yet | no camera window | hardware
  decoding failed on this connection | hardware decoding failed on several connections in a row | the hardware decoder is
  not available | test switch).“; při návratu k softwaru `Warning` „Hardware video decoding (videotoolbox) failed while
  decoding: … (code …); … (1 of 2 failures in a row …)“. Nikdy na snímek, nikdy s adresou.
- **Testovací šev:** `HardwareDecodingPolicy.Faults` (interní, v aplikaci `null`): selhání vytvoření zařízení, mapování
  a dekódování po N hardwarových snímcích; v cestě snímku jedna kontrola `null` jen u hardwarových snímků.
  `FFmpegCameraFeedFactory.DefaultHardwareDecoding` (interní) umožní spustit celou sadu FFmpeg s `Off`
  (`LAYERPEEK_TEST_CAMERA_HARDWARE_DECODING=off`, modulový inicializátor testů).

**Build macOS:** `--enable-videotoolbox --enable-hwaccel=h264_videotoolbox`, hlavička skriptu opravená. Cache se po změně
skriptu přestavěla sama do nové složky (`osx-arm64-c88d70…`, build 39 s), configure dál „LGPL version 3 or later“,
`otool -L` jen `@loader_path`, `/usr/lib` a `/System/Library/Frameworks/{VideoToolbox,CoreFoundation,CoreMedia,CoreVideo,
CoreServices}`; `libavcodec` 1 624 952 B, `libavutil` 742 648 B (+27 KB jako v T-192). Test `ThirdPartyLicensesTests`
(dřív „bez hardwarového dekódování“) teď hlídá LGPL a VideoToolbox jako jediný hwaccel. Windows build beze změny.

**AOT a licence:** žádný nový balíček, žádná reflexe, jen funkce FFmpeg.AutoGen 9.0.1.1 (`av_hwdevice_ctx_create`,
`avcodec_get_hw_config`, `av_hwdevice_iterate_types`, `av_hwframe_map`, `av_hwframe_transfer_data`, `av_frame_copy_props`,
`av_buffer_unref` přes `avcodec_free_context`). `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` prošel bez
varování trim / AOT (zbylé řádky jsou hlášky nativního `dsymutil` / `xcodebuild` mimo kód, stejné jako dřív).
VideoToolbox je systémový framework, licence FFmpeg zůstává LGPL 3.

**Testy (nové 60 = Core 14, FFmpeg 42, App 4):** `CameraHardwareDecodingSettingsTests` (Core: výchozí, rovnost, zápis/čtení, chybějící,
neznámá, export), `HardwareDecodingRuleTests` (pravidlo OS, přepínač, generace, historie), `HardwareDecodingFallbackTests`
(oba OS s FFmpeg: typ, který build nemá → software + 1 `Warning`; selhání vytvoření zařízení → totéž; `Off` → žádné
zařízení; bez okna kamery – `CameraSnapshot`, klíčové snímky, šedotón – žádné zařízení; klíč historie bez přihlašovacích
údajů; build macOS má `h264_videotoolbox` a LGPL; přepínač jen v Debug), `HardwareDecodingVideoToolboxTests`
(`[HardwareDecodingFact]`, přeskočí se bez VideoToolboxu: okno dekóduje v hardwaru a mapuje každý převedený snímek, barvy
jako software (průměrná odchylka ≤ 2 / 255), mapují se jen splatné snímky, kopie po selhání mapování, selhání za běhu →
software od dalšího klíčového snímku a uvolněné zařízení, 2 selhání za sebou → software do změny volby, úspěch mezi
selháními počítání nuluje, uvolnění zařízení při zastavení plného dekódování a v `Dispose`, změna volby od dalšího
klíčového snímku bez ztráty snímku, okno s velikostí až po rozvržení), `FFmpegCameraFeedAllocationTests`
(+ `SteadyStateHardwareDisplayFramesDoNotAllocate`: 0 B), App: `CameraHardwareDecodingObserverTests`,
`TestModeSwitchesTests.TheHardwareDecoderSwitchIsOnlyForTheTestMode`. Stávajících 151 testů FFmpeg prošlo s `Automatic`
(zobrazovací testy tedy na Macu přes VideoToolbox) i s `Off`; celá sada FFmpeg 193/193 v obou režimech.

**Rychlá kontrola na Macu (podrobné měření je T-205):** postup T-192 – simulátor (`idle`, `--fps 25`, porty 18492 / 18566)
s klipem 1280 × 720 z `coreone-20261103-1905-60s.mp4` (libx264 Constrained Baseline, GOP 50, 25 fps, 1,1 Mb/s, plný
rozsah BT.709), aplikace Debug v izolovaném testovacím režimu (dočasná složka, přípona instance, `test-secrets.txt`),
normální okno 640 × 360 DIP otevřené příkazem `camera:sim`, volba z `settings.json` (`Off` / bez hodnoty = `Automatic`),
15 s zahřátí, 30 s měření `proc_pid_rusage` (`ri_instructions`), `VTDecoderXPCService` započtený (stejný odpovědný proces
a spuštěný po aplikaci), 2 kola se střídaným pořadím, zátěž 1,5–2,2 na 10 jader. Log: `videotoolbox` /
`software (turned off in the settings)`.

| Volba | instrukce aplikace G/s | `VTDecoderXPCService` G/s | celkem G/s | CPU aplikace + pomocný | energie CPU |
|---|---|---|---|---|---|
| Off (kolo 1 / 2) | 0,375 / 0,372 | – | **0,375 / 0,372** | 20,1 / 20,2 % | 47 / 46 mW |
| Automatic (kolo 1 / 2) | 0,154 / 0,154 | 0,038 / 0,038 | **0,192 / 0,192** | 15,3 + 4,8 % | 28 / 28 mW |

Instrukce procesu + pomocného procesu klesly o **48,6 %** (požadavek ≥ 40 %), odhad energie CPU o 40 %; doba CPU zůstala
stejná (pomocný proces čeká na hardware), stejně jako v T-192.

**Mimo rozsah / navazuje:** UI volby (T-204; mechanismus uplatnění po uložení už je hotový), měření a ověření na Macu včetně
AOT buildu, kvality obrazu, zpoždění, spánku a dvou kamer (T-205), D3D11VA na Windows (T-206: na Windows `Automatic` =
software, `LAYERPEEK_TEST_HW_DECODER=d3d11va` už teď projde stejnou obecnou cestou – zařízení, `get_format`, mapování /
kopie, návrat –, ale bez ověření a bez ošetření ztráty zařízení D3D11; testy `HardwareDecodingFallbackTests` na Windows
používají D3D11VA jen pro šev selhání vytvoření zařízení a pro „bez okna žádné zařízení“). `CHANGELOG.md` doplní T-204
s UI volby.

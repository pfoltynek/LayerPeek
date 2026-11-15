---
id: T-206
title: Windows – hardwarové dekódování D3D11VA (výchozí software do měření v T-125)
status: done
owner: core-dev
priority: P2
platform: windows
depends_on: [T-202, T-203, T-204]
---

## Cíl
Rozhodnutí uživatele 2026-11-12 (T-192, otázka 4): Windows v témže záměru jako macOS – D3D11VA nad sdíleným základem
z T-203, ale **výchozí zůstává software, dokud ho T-125 nezměří**. Úspora na Windows je neověřená: na rozdíl od sdílené
paměti Apple Silicon může stažení snímku z GPU (staging textura, u dedikované GPU přes PCIe) úsporu sníst
([docs/hw-decoding.md](../../docs/hw-decoding.md), „Rizika a vazby“). Závisí na **T-202** (vlastní minimální LGPL build
FFmpeg pro Windows místo BtbN, s D3D11VA).

Priorita: minimum prostředků, výkon, čitelný a robustní kód; stejná cesta jako macOS, jen jiný typ zařízení.

## Rozsah
- **Build (T-202):** ověřit, že knihovny z T-202 obsahují hwaccel `h264_d3d11va` (případně `h264_d3d11va2`) a
  `hwcontext_d3d11va`, licence dál LGPL 3; jinak doplnit přepínače do skriptu T-202 (`--enable-d3d11va
  --enable-hwaccel=h264_d3d11va`) a test příznaků jako u macOS (T-203). DXVA2, D3D12VA, NVDEC, QSV, AMF ani Vulkan
  se nepoužívají.
- **Pravidlo podle OS (T-203):** D3D11VA se na Windows zapne jen vývojovým přepínačem
  `LAYERPEEK_TEST_HW_DECODER=d3d11va` (Debug, testovací režim). `Automatic` na Windows zůstává software. Přepnutí na
  D3D11VA v „Automaticky“ je jen změna pravidla v kódu po rozhodnutí uživatele (nový úkol po T-125), uložené nastavení
  se nemigruje.
- **`FFmpegDecoder` s `AV_HWDEVICE_TYPE_D3D11VA`:** výchozí adaptér (`av_hwdevice_ctx_create` bez názvu zařízení),
  `get_format` → `AV_PIX_FMT_D3D11`. Splatné snímky (T-185) do NV12: `av_hwframe_map` jen když jsou textury mapovatelné,
  jinak `av_hwframe_transfer_data`; co je rychlejší na iGPU a dedikované GPU, změřit a zvolit (bez přepínání na snímek).
- **Ztráta zařízení a prostředí bez GPU:** spánek / probuzení, `Win+Ctrl+Shift+B` (restart ovladače grafiky),
  aktualizace ovladače, přepnutí GPU, vzdálená plocha, VM bez GPU (Microsoft Basic Render Driver) → chyba dekódování
  → návrat k softwaru podle pravidla T-203, okno kamery nezůstane černé ani v chybě.
- **Bezpečnost:** platí rozhodnutí T-192 otázka 5 (ovladač GPU / DXVA rozšiřuje útočnou plochu, pojistka „Vypnuto“).
- **Měření pro T-125:** po dokončení přidat do `tasks/todo/T-125-*.md` nový oddíl („z T-206“) s body níže a stejný
  postup zapsat do `docs/hw-decoding.md` (oddíl „Windows – měření pro přepnutí výchozího stavu“, připravený architektem).
  - Metriky: **cykly procesu** (`QueryProcessCycleTime`, obdoba instrukcí na macOS; uživatelská část ovladače GPU běží
    v procesu aplikace), CPU % procesu, využití enginu „Video Decode“ GPU (čítač `\GPU Engine(*engtype_VideoDecode)\
    Utilization Percentage`), paměť: private bytes a working set procesu + `\GPU Process Memory(*)\Dedicated Usage`
    a `Shared Usage`, dekódované / převedené snímky/s.
  - Scénáře jako T-192 / T-205: bez okna, normální okno 720p a 1080p (100 % DPI, obraz v plném rozlišení), malý náhled
    720p a 1080p (10 fps); simulátor, 15 s zahřátí, 30 s měření, 2 kola se střídaným pořadím, D3D11VA
    (`LAYERPEEK_TEST_HW_DECODER=d3d11va`) proti `Off`; **integrovaná GPU (Intel / AMD) i dedikovaná GPU** (NVIDIA /
    AMD), pokud jsou k dispozici; `map` proti `transfer`.
  - Robustnost: spánek / probuzení, `Win+Ctrl+Shift+B`, vzdálená plocha, VM bez GPU – vždy obraz (software).
  - **Návrh pro přepnutí výchozího stavu** (rozhoduje uživatel): cykly procesu klesnou aspoň o 30 % v normálním okně
    i malém náhledu na každé změřené GPU, doba CPU nevzroste, paměť navíc ≤ 50 MB na stream, žádný trvalý návrat
    k softwaru ani chyba v robustních scénářích. Splněno → uživatel rozhodne o přepnutí (nový úkol: pravidlo
    + příručky + volba na Windows podle T-204 otázky 1); nesplněno → software zůstává.
- Testy hardwarové cesty s `[HardwareDecodingFact]` (T-203) se na Windows bez GPU / bez D3D11VA přeskočí.

## Akceptační kritéria
- [x] Knihovny Windows obsahují `h264_d3d11va`, licence LGPL 3 (test), bez dalších hwaccel.
- [x] S `LAYERPEEK_TEST_HW_DECODER=d3d11va` (Debug) okno kamery a malý náhled dekódují v D3D11VA (log `d3d11va`),
      převádí se jen splatné snímky; `Automatic` i `Off` na Windows dekódují softwarově (test pravidla).
- [x] Ztráta zařízení (test se simulovanou chybou dekódování + ruční `Win+Ctrl+Shift+B`) a prostředí bez D3D11VA →
      software bez chyby v okně kamery. Simulovaná chyba dekódování a kopie automaticky; ruční `Win+Ctrl+Shift+B`, spánek,
      vzdálená plocha a VM bez GPU → přesunuto do T-125
- [x] Testy `[HardwareDecodingFact]` projdou na Windows s GPU, bez GPU se přeskočí; `[FFmpegFact]` testy projdou.
      Na tomto PC s GPU prošly; přeskočení bez GPU (VM) → přesunuto do T-125
- [x] Rychlé orientační měření na vývojovém PC (jedna GPU, normální okno 720p, cykly a CPU proti `Off`) v poznámkách.
- [x] Do T-125 přibyl oddíl s měřením podle „Měření pro T-125“ výše; `docs/hw-decoding.md` a `docs/architecture.md`
      odpovídají stavu.
- [x] `dotnet publish src/LayerPeek.App -c Release -r win-x64` (Native AOT) bez varování trim / AOT.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k AOT a licencím
- Stejné vazby FFmpeg.AutoGen a callback jako T-203; žádný Direct3D interop v C# (zařízení vytváří FFmpeg), žádný
  nový balíček, žádné COM. Knihovny z T-202 zůstávají LGPL 3; D3D11 je systémová součást Windows.

## Otevřené otázky
- (žádné blokující) Přepnutí výchozího stavu na Windows rozhodne uživatel po měření v T-125. Orientační měření
  na vývojovém PC (RTX 4060) vyšlo pro D3D11VA hůř než software (viz níže), tedy zatím pro software.
- (rozhodnuto) Drahá je kopie snímku z GPU do procesoru (FFmpeg 9.0 textury D3D11 namapovat
  neumí). Kopii by odstranil jen vlastní D3D11 interop (sdílená textura NV12 přímo do vykreslení okna kamery), což
  T-206 výslovně vylučuje a znamenalo by nový návrh rendereru i interopu (`[GeneratedComInterface]`). Má se to vůbec
  zkoumat, nebo Windows zůstane u softwaru, pokud T-125 nenajde počítač, kde se D3D11VA vyplatí? Rozhoduje uživatel.
  **Rozhodnutí uživatele 2026-11-15 (předal hlavní agent):** na Windows zůstává softwarové dekódování; renderer se
  sdílenou texturou se nedělá. Výchozí stav se přepne jen tehdy, když T-125 (2ah) najde počítač (integrovaná GPU,
  1080p), kde se D3D11VA vyplatí. Otázka 1 (výchozí stav po měření v T-125) zůstává otevřená.

## Poznámky k implementaci
Hotovo 2026-11-15 na Windows 11 Pro 10.0.26300 (core-dev; AMD Ryzen 7 3800X, NVIDIA GeForce RTX 4060, ovladač
32.0.16.1742).

**Build (T-202):** beze změny – knihovny už mají `h264_d3d11va` a `h264_d3d11va2` a hwcontext `d3d11va`, LGPL 3; hlídá
to `FFmpegWindowsBuildTests.TheWindowsBuildHasD3D11VAForH264AsItsOnlyHardwareDecoderAndStaysLgpl` (prošel).

**Co se změnilo (Camera.FFmpeg)**
- Pravidlo OS beze změny: `Automatic` i `Off` na Windows = software, D3D11VA jen přes `LAYERPEEK_TEST_HW_DECODER=d3d11va`
  (Debug, testovací režim; zapojení v App je z T-203). Obecná cesta T-203 (zařízení na výchozím adaptéru, výchozí
  `get_format` → `AV_PIX_FMT_D3D11`, převod jen splatných snímků, návrat k softwaru, historie selhání) fungovala na
  D3D11VA bez úprav.
- **Mapování vs. kopie:** FFmpeg 9.0 (`libavutil/hwcontext_d3d11va.c`) nemá `map_from`, `av_hwframe_map` snímku D3D11
  vždy vrátí ENOSYS (ověřeno ve zdrojích i testem na skutečném zařízení). Měřit „map proti transfer“ proto nejde;
  nové `HardwareDecoderSelection.MapsFrames(device)` (VideoToolbox ano, D3D11VA ne) a relace D3D11VA kopíruje rovnou,
  bez zbytečného pokusu o mapování a bez zavádějícího řádku „Mapping … failed“ v každé relaci.
- **Kopie bez nativní alokace na snímek:** `av_hwframe_transfer_data` do prázdného snímku alokuje každý snímek znovu
  (u 720p 1,4 MB × 25/s). `FFmpegDecoder.EnsureCopyBuffer` kopíruje do jednoho `AVFrame` relace (`sw_format` kontextu
  snímků, NV12), nový jen při změně velikosti, uvolní se s relací (`CloseDecoder`). Barevný rozsah, matice, primárky,
  přenosová funkce a umístění chroma se přenesou ručně (`av_frame_copy_props` by na opakovaně použitý snímek přidávalo
  vedlejší data). Opravuje i „nice-to-have“ z review T-203 (záložní kopie VideoToolboxu).
- **Ztráta zařízení:** podle zdrojů FFmpeg se odebrané zařízení D3D11 projeví hlavně selháním `Map` staging textury při
  kopii (`AVERROR_UNKNOWN`); chyby `end_frame` hwaccel dekodér H.264 jen zapíše. Obojí vede na návrat k softwaru T-203.
  Nový šev testů `HardwareDecodingFaults.FailCopyAfterFrames`.
- **Šev testů `HardwareDecodingPolicy.AutomaticDevice`** (interní): co znamená „Automaticky“ (výchozí = pravidlo OS).
  Testy ho nastaví na zařízení tohoto OS, takže stejné testy volby (Automatic / Off / změna na klíčovém snímku) běží
  na Macu s VideoToolboxem a na Windows s D3D11VA, jako by pravidlo přepnulo. Konstruktor s `isMacOS` zůstal.
- Diagnostika pro testy: `FFmpegCameraFeed.CopyBuffersAllocated`.

**Testy (Camera.FFmpeg.Tests)**
- `HardwareDecodingTestEnvironment` / `[HardwareDecodingFact]` jsou teď pro zařízení tohoto OS: macOS VideoToolbox
  (beze změny), Windows D3D11VA – sonda vytvoří zařízení a čistým FFmpeg (nezávisle na testovaném dekodéru) dekóduje
  první klíčový snímek fixture; bez snímku `AV_PIX_FMT_D3D11` (VM, CI s Basic Render Driver) se testy přeskočí.
- `HardwareDecodingVideoToolboxTests` → `HardwareDecodingDeviceTests` (`git mv`): stejné testy pro obě zařízení (log
  `{DeviceName}`, počty mapovaných nebo kopírovaných snímků), nový
  `ADeviceLostWhileCopyingContinuesInSoftwareFromTheNextKeyFrame`, `ACopyBufferIsAllocatedAgainOnlyWhenThePictureSizeChanges`
  (fixture 1280 × 720 + klíčové snímky I_PCM 320 × 180 s vlastním SPS: buffer kopií se nahradí jednou; s vypnutou
  kontrolou velikosti selže) a kontrola jednoho bufferu kopií v
  `FramesThatCannotBeMappedAreCopiedForTheRestOfTheSession`. `ParameterSetsOnceTests` a alokační test hardwarových
  snímků (`SteadyStateHardwareDisplayFramesDoNotAllocate`, 0 B) běží na Windows s D3D11VA.
- Nový `HardwareDecodingD3D11VATests`: `OnWindowsAutomaticAndOffDecodeInSoftware` (továrna aplikace bez švu),
  `TheTestSwitchDecodesTheCameraWindowAndTheMiniPreviewInD3D11VA` (Debug; okno kopíruje každý snímek, malý náhled jen
  splatné, log `d3d11va`), `D3D11VAFramesAreCopiedIntoOneBufferPerSessionAndNeverMapped`, `FFmpegCannotMapD3D11Frames`
  (selže, až FFmpeg mapovat začne → změřit), `D3D11VAFramesLookLikeSoftwareFramesInTheMiniPreviewSize` (odchylka ≤ 2/255).
- `HardwareDecodingRuleTests`: `OnlyVideoToolboxFramesAreMappedD3D11VAFramesAreCopied`,
  `TheDeviceOfAutomaticCanBeChangedWithANewGeneration`, `TheDefaultDeviceOfAutomaticIsTheRuleOfTheOs`.
- Kontrola mutací: s `MapsFrames` = vždy `true` selže 9 testů, s opravou projdou.
- Na Macu jsem testy nespustil; úpravy testů VideoToolboxu jsou mechanické (název zařízení, součet mapovaných
  a kopírovaných snímků), ověří je příští běh `dotnet test` na Macu.

**Orientační měření (podrobně `docs/hw-decoding.md`, „Výsledky implementace (T-206)“)**
Dočasný měřicí program mimo repozitář nad veřejným API továrny (Debug, `UseTestHardwareDecoder("d3d11va")` proti
`Off`), simulátor `idle --fps 25` s klipem Buddy3D 1280 × 720 přes RTSP, bez okna aplikace (plochu používali jiní
agenti; vykreslení je v obou variantách stejné). 15 s zahřátí, 30 s měření, 2 kola se střídaným pořadím; počítač
souběžně zatěžovali jiní agenti.

| Scénář | Off: Gcyklů/s · CPU | D3D11VA: Gcyklů/s · CPU | paměť private (Off → D3D11VA) |
|---|---|---|---|
| normální okno 720p (plné rozlišení) | 0,205 / 0,205 · 5,2 / 5,4 % | 0,545 / 0,531 · 12,0 / 13,7 % | 30 → 131 MB |
| malý náhled 320 × 180 (~8 fps) | 0,198 / 0,183 · 4,1 / 4,0 % | 0,214 / 0,216 · 4,4 / 4,9 % | 27 → 134 MB |

GPU „Video Decode“ 3 %, GPU paměť 39 MB dedikovaná + 11–17 MB sdílená. Doba na snímek (soubor co nejrychleji): okno
Off 1,2 ms, D3D11VA 2,5 ms; náhled 1,1 / 1,4 ms. **Na tomto PC D3D11VA mez nesplňuje** (cykly +160 % v okně, +8–18 %
v náhledu, paměť +~100 MB): drahá je kopie z dedikované GPU, ne dekódování. Integrovaná GPU a slabší procesor → T-125.

**Ostatní:** `dotnet build` 0 varování; `dotnet test --project tests/LayerPeek.Camera.FFmpeg.Tests` 221 testů, 219
prošlo, 2 přeskočené (test macOS a test lokálního klipu, který na tomto PC chybí), i s `LAYERPEEK_TEST_CAMERA_HARDWARE_DECODING=off`. Core, App ani UI se
neměnily (jejich testy ani celé řešení jsem nespouštěl – celé řešení spouští hlavní agent; E2E ne, plochu měl jiný agent).
`dotnet publish src/LayerPeek.App -c Release -r win-x64` bez varování trim / AOT. Žádný nový balíček, žádná reflexe,
žádný Direct3D / COM interop v C# (jen funkce FFmpeg.AutoGen: `av_frame_get_buffer`, `av_hwframe_transfer_data`,
`av_hwframe_map`); licence beze změny (LGPL 3, D3D11 je součást Windows). Bez změny viditelné uživateli (Windows dál
software), proto bez změn příruček, `CHANGELOG.md` a textů UI.
Uzavřeno 2026-11-15: ruční `Win+Ctrl+Shift+B`, spánek, vzdálená plocha, VM bez GPU a plné měření (iGPU i dedikovaná
GPU, 1080p, okno aplikace) přesunuto do T-125, oddíl 2ah (pravidlo sběrných ověřovacích úkolů).

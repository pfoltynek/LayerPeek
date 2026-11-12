---
id: T-206
title: Windows – hardwarové dekódování D3D11VA (výchozí software do měření v T-125)
status: todo
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
- [ ] Knihovny Windows obsahují `h264_d3d11va`, licence LGPL 3 (test), bez dalších hwaccel.
- [ ] S `LAYERPEEK_TEST_HW_DECODER=d3d11va` (Debug) okno kamery a malý náhled dekódují v D3D11VA (log `d3d11va`),
      převádí se jen splatné snímky; `Automatic` i `Off` na Windows dekódují softwarově (test pravidla).
- [ ] Ztráta zařízení (test se simulovanou chybou dekódování + ruční `Win+Ctrl+Shift+B`) a prostředí bez D3D11VA →
      software bez chyby v okně kamery.
- [ ] Testy `[HardwareDecodingFact]` projdou na Windows s GPU, bez GPU se přeskočí; `[FFmpegFact]` testy projdou.
- [ ] Rychlé orientační měření na vývojovém PC (jedna GPU, normální okno 720p, cykly a CPU proti `Off`) v poznámkách.
- [ ] Do T-125 přibyl oddíl s měřením podle „Měření pro T-125“ výše; `docs/hw-decoding.md` a `docs/architecture.md`
      odpovídají stavu.
- [ ] `dotnet publish src/LayerPeek.App -c Release -r win-x64` (Native AOT) bez varování trim / AOT.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k AOT a licencím
- Stejné vazby FFmpeg.AutoGen a callback jako T-203; žádný Direct3D interop v C# (zařízení vytváří FFmpeg), žádný
  nový balíček, žádné COM. Knihovny z T-202 zůstávají LGPL 3; D3D11 je systémová součást Windows.

## Otevřené otázky
- (žádné blokující) Přepnutí výchozího stavu na Windows rozhodne uživatel po měření v T-125.

## Poznámky k implementaci
(vyplní agent po dokončení)

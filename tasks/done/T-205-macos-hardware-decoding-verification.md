---
id: T-205
title: macOS – hardwarové dekódování VideoToolbox, ověření a měření v aplikaci
status: done
owner: core-dev
priority: P2
platform: macos
depends_on: [T-203, T-204]
---

## Cíl
Ověřit skutečnou implementaci VideoToolboxu z T-203 / T-204 na Macu stejnou metodikou jako průzkum T-192
([docs/hw-decoding.md](../../docs/hw-decoding.md), „Postup měření“) a potvrdit, že výchozí „Automaticky“ na macOS
(rozhodnutí uživatele 2026-11-12) šetří prostředky bez ztráty kvality a stability. T-192 měřil necommitnutou sondu,
tady se měří produktový kód a Native AOT.

## Rozsah
- **Měření A/B** `Automatic` (VideoToolbox) proti `Off` (software) v izolovaném testovacím režimu (`LAYERPEEK_TEST_DATA_DIR`
  v dočasné složce, přípona instance, skutečný profil uživatele netknutý), simulátor (`tools/PrusaSimulator`, scénář
  `idle`, `--fps 25`) s klipy 720p a 1080p z T-192 (`~/LayerPeek-samples`, ne v repozitáři). Scénáře: bez okna kamery,
  normální okno 720p a 1080p v plném rozlišení, malý náhled 720p a 1080p (výchozí 10 fps). 15 s zahřátí, 30 s měření,
  2 kola se střídaným pořadím, zátěž stroje před během ≤ 6.
- **Veličiny:** instrukce procesu za sekundu (`ri_instructions`, hlavní metrika) **včetně** `VTDecoderXPCService`
  přiřazeného přes `responsibility_get_pid_responsible_for_pid` (bez procesu tapety), doplňkově CPU % (aplikace
  + pomocný proces, podíl P-jader), odhad energie CPU (`ri_energy_nj`), paměť (footprint aplikace, footprint pomocného
  procesu, součet), dekódované a převedené snímky/s.
- **Native AOT:** měření i kontroly na `dotnet publish -c Release -r osx-arm64` (aplikace z `.app` bundle, ad-hoc
  podpis s hardened runtime T-108 – ověřit, že VideoToolbox pod hardened runtime funguje bez nového entitlementu).
- **Kvalita obrazu:** snímek z VT a ze softwaru ze stejného místa klipu (NV12 → BGRA vs. YUV 4:2:0 → BGRA, plný
  rozsah, BT.709): průměrná absolutní odchylka kanálů ≤ 2 úrovně, bez posunu barev (kontrola okem + číslo).
- **Zpoždění:** čas od přijetí paketu po převedený snímek (VT dekóduje synchronně); zhoršení proti softwaru o víc než
  jeden snímek (40 ms) zapsat a hlásit uživateli.
- **Robustnost:** uspání a probuzení Macu s otevřeným oknem (`kVTInvalidSessionErr` → FFmpeg relaci obnoví, případně
  návrat k softwaru podle T-203, okno nesmí zůstat černé), zamčená obrazovka, dvě kamery současně (paměť roste
  s počtem relací), přepnutí volby za běhu podle T-204, detekce chyb tisku bez okna (žádná relace VT, žádný
  `VTDecoderXPCService`).
- Výsledky do `docs/hw-decoding.md` (nový oddíl „Výsledky implementace (T-205)“) a krátce do `docs/architecture.md`.

## Akceptační kritéria
- [x] Tabulka A/B (instrukce/s, CPU %, energie, paměť, fps) pro všech 5 scénářů v `docs/hw-decoding.md`, AOT build.
- [x] Instrukce aplikace + pomocného procesu s VT jsou proti `Off` nižší aspoň o 40 % v normálním okně 720p i 1080p
      a v malém náhledu (T-192: −48 až −63 %); doba CPU (aplikace + pomocný proces) není vyšší než u softwaru.
- [x] Paměť navíc (aplikace + pomocný proces) je nejvýš 40 MB na stream (T-192: +10–25 MB, pomocný proces 17–36 MB);
      po zavření okna kamery `VTDecoderXPCService` skončí nebo jeho paměť klesne (relace se uvolní).
- [x] Bez okna kamery (jen sledování a detekce) se relace VT nevytvoří a instrukce jsou stejné jako u `Off` (±5 %).
- [x] Kvalita obrazu VT vs. software v mezích výše; dekódováno 25,0 snímků/s, malý náhled převádí jen splatné snímky.
- [x] Uspání / probuzení, zamčení, dvě kamery a přepnutí volby za běhu bez černého okna a bez trvalé chyby
      (výsledky v poznámkách; ruční části, které agent neudělá, → T-127). → přesunuto do T-127
- [x] Pokud některá mez neplatí: nic se nemění, výsledek a návrh (např. výchozí „Vypnuto“ na macOS) jdou do
      `## Otevřené otázky` pro uživatele. (Všechny meze platí, výchozí „Automaticky“ zůstává.)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k AOT a licencím
- Bez nového balíčku a bez změny produktového kódu (jen měření). Pomocné měřicí nástroje (C, Swift, skripty) zůstávají
  ve scratchpadu nebo mimo repozitář; do repozitáře jen dokumentace.

## Otevřené otázky
Neblokují uzavření úkolu (všechny meze platí):

1. **Kamery bez opakovaných SPS / PPS (návrh nového úkolu, rozhodne uživatel).** Dekodér od T-203 při přepnutí volby,
   při návratu k softwaru a po skončení hardwarové relace otevírá nový kontext kodeku na klíčovém snímku. Ten dostane
   SPS a PPS jen z `sprop-parameter-sets` v SDP (extradata) nebo z proudu před IDR snímkem. Buddy3D posílá obojí, takže
   se jí to netýká. Kamera, která je pošle jen jednou na začátku spojení a nemá je v SDP (tak se choval simulátor
   s klipem MP4, opraveno v tomto úkolu), by po přepnutí ukazovala stojící obraz až do nového spojení. Pojistka by byla
   malá: dekodér si pamatuje poslední SPS / PPS z paketů a dá je novému kontextu jako extradata. Založit úkol
   (P3), nebo nechat, dokud se taková kamera neobjeví? Doporučení: P3 úkol, levné a bez vlivu na Buddy3D.

## Poznámky k implementaci
Hotovo 2026-11-12 na Macu (core-dev; Mac mini M2 Pro, 10 jader, macOS 27.2, .NET SDK 11.0.100). Celé tabulky
a postup: [docs/hw-decoding.md](../../docs/hw-decoding.md), oddíl „Výsledky implementace (T-205)“; krátce
v `docs/architecture.md` (oddíl „Hardwarové dekódování kamery“).

**Jak se měřilo**
- `dotnet publish src/LayerPeek.App -c Release -r osx-arm64` bez varování trim / AOT (zbylé řádky jsou hlášky nativního
  `dsymutil` / modulové cache clangu, jako v T-203 / T-204). `LayerPeek.app` funkcemi vydání (`New-MacAppBundle`,
  `Invoke-MacAdHocSign`), ad-hoc podpis s hardened runtime (`flags=0x10002(adhoc,runtime)`) a jen entitlementem
  `disable-library-validation`; `Get-MacSignatureProblems` + `Get-MacHardenedRuntimeProblems` bez nálezu.
  **VideoToolbox pod hardened runtime funguje bez nového entitlementu.**
- Release nemá testovací režim (`LAYERPEEK_TEST_DATA_DIR` a příkazy instance jsou jen v Debug), zadání chtělo obojí.
  Použil jsem postup T-191: profil aplikace odsunutý žurnálovanými `Backup-SmokeProfile` / `Restore-SmokeProfile`
  (jako `scripts/aot-smoke.ps1`), proxy na `127.0.0.1` s `X-Api-Key` místo hesla z Klíčenky, okno kamery otevírá
  automatika T-151 (`camera-windows.json` s `autoClosed`). Po všech bězích byl profil uživatele stejný jako předtím
  (soubory, velikosti, časy změny, doména `defaults`), registrace bundlu v LaunchServices zrušená.
- Simulátor (`idle`, `--fps 25`) s klipy 720p / 1080p z T-192, 15 s zahřátí, 30 s měření, 2 kola se střídaným
  pořadím, zátěž před během 1,1–3,1. `ri_instructions` aplikace + `VTDecoderXPCService` (stejný odpovědný proces,
  spuštěný po aplikaci). Obrazovka zamčená, displej probuzený.
- Kvalita, zpoždění, snímky/s a přepnutí za běhu: pomocný program ve scratchpadu nad veřejným API
  `FFmpegCameraFeedFactory` (produktový `FFmpegDecoder`, poptávka jako okno kamery), že dekóduje v hardwaru, potvrdil
  jeho `VTDecoderXPCService`. Měřicí nástroje zůstaly ve scratchpadu, do repozitáře jen dokumentace a oprava simulátoru.

**Výsledky (Release AOT, kolo 1 / kolo 2)**

| Scénář | instrukce Off G/s | instrukce Automatic G/s (aplikace + VT) | úspora | CPU Off → Automatic (aplikace + VT) | paměť navíc |
|---|---|---|---|---|---|
| bez okna kamery | 0,0010 / 0,0011 | 0,0011 / 0,0010 | −0,9 % (šum, viz níže) | 0,1 → 0,1 % | 0 MB, žádný pomocný proces |
| normální okno 720p | 0,370 / 0,371 | 0,182 / 0,182 (0,144 + 0,038) | **−50,9 %** | 18,4 → 18,0 % | **+12,7 MB** |
| malý náhled 720p | 0,320 / 0,322 | 0,134 / 0,134 (0,096 + 0,038) | **−58,3 %** | 12,5 → 12,0 % | **+12,7 MB** |
| normální okno 1080p | 0,982 / 0,996 | 0,487 / 0,498 (0,451 + 0,042) | **−50,2 %** | 36,9 → 31,7 % | **+21,7 MB** |
| malý náhled 1080p | 0,741 / 0,741 | 0,244 / 0,244 (0,202 + 0,042) | **−67,1 %** | 22,8 → 14,1 % | **+21,3 MB** |

Odhad energie CPU (aplikace + pomocný proces) −32 % / −37 % (720p okno / náhled) a −45 % / −59 % (1080p).

| Kontrola | Výsledek | Mez | |
|---|---|---|---|
| úspora instrukcí okno / malý náhled | −50 až −51 % / −58 a −67 % | ≥ 40 % | splněno |
| doba CPU aplikace + pomocný proces | nikde vyšší (−0,4 až −8,7 b. jádra) | není vyšší | splněno |
| paměť navíc na stream | +12,7 MB (720p), +21,5 MB (1080p); dvě kamery +30,4 MB za oba | ≤ 40 MB | splněno |
| po zavření okna | pomocný proces 17,4 → 3,3 MB po lhůtě hubu (10 s), znovuotevření zase VT | skončí nebo paměť klesne | splněno |
| detekce bez okna (`printing`, vzorek 2 s) | 15,46 → 15,55 M instr./s (+0,6 %), 0 procesů `VTDecoderXPCService` | ±5 %, žádná relace | splněno |
| jen sledování bez okna | žádná relace, žádný pomocný proces; instrukce 0,5–1 M/s, rozdíl −0,9 % / +14,8 % / −22 % podle série | ±5 % | viz níže |
| kvalita (snímky 0, 37, 149, 500; plné rozlišení i 640 × 360) | průměrná i největší odchylka 0 (bit po bitu stejné), okem bez posunu barev | ≤ 2 úrovně | splněno |
| dekódováno / převedeno | 25,00 / 25,00 (okno), 25,0 / jen splatné (náhled) | 25,0 | splněno |
| zpoždění paket → snímek (medián) | 720p +1,1 až +1,4 ms, 1080p −1,1 až −1,4 ms, žádný snímek ve frontě | zhoršení < 40 ms | splněno |
| přepnutí volby za běhu | 3 × 10 s, 250–253 snímků v každé fázi, největší mezera 63 ms (po opravě simulátoru) | bez černého okna | splněno |

- **Jen sledování (±5 %):** při sledování bez okna se aplikace ke kameře vůbec nepřipojí, cesta je pro obě volby
  stejná. Instrukce jsou tak malé (0,5–1 M/s), že se mezi procesy téže volby liší až o ±30 % a znaménko rozdílu se mezi
  sériemi mění (30 s: −0,9 %, 120 s × 6 párů: +14,8 %, 300 s × 2 páry: −22 %). Ustálená hladina (medián 10s úseků) je
  stejná: Off 0,49, Automatic 0,49 / 0,55 M/s. Kritérium jsem vyhodnotil podle detekce bez okna (+0,6 %) a podle toho,
  že relace ani pomocný proces nevznikají; mez ±5 % u samotného sledování měřením rozlišit nejde.
- **Dvě kamery** (720p + 1080p, dvě okna): −55,4 % instrukcí, jeden `VTDecoderXPCService` pro obě relace, jeho paměť
  roste s počtem relací (46,9 MB ≈ 17,4 + 32,8 − 3,3 MB základu).
- **Zamčená obrazovka:** všechna měření při zamčené obrazovce, VT dekóduje 25 snímků/s.

**Nalezená a opravená chyba (simulátor):** přepnutí volby za běhu v pomocném programu zastavilo obraz na 14 a 24 s.
Příčina: `tools/PrusaSimulator` posílal u klipu MP4 (SPS a PPS jen v `avcC`) parametry jen před prvním IDR snímkem
smyčky, i když `docs/simulator.md` slibuje „jako Buddy3D před každým IDR snímkem“. Dekodér, který přepnutí (T-203)
otevře na dalším klíčovém snímku, je pak neměl až do konce smyčky. `H264Clip.Parse` teď dá SPS a PPS před každý IDR
snímek (poslední předchozí v pořadí dekódování, PPS za své SPS). Testy: `RtspCameraTests.EveryIdrFrameGetsTheParameterSetsWhenTheClipHasThemOnlyOnce`,
`RtspCameraTests.AnIdrFrameGetsTheLatestParameterSetsBeforeIt` a `HardwareDecodingVideoToolboxTests.AChangedSettingKeepsTheSimulatedCameraRunningWhenItsClipHasTheParameterSetsOnlyOnce`
(simulátor s takovým klipem, přepnutí Automaticky → Vypnuto; bez opravy 50 ze 71 snímků, s opravou 71). Produktový kód
se neměnil; skutečná Buddy3D posílá SPS a PPS před každým IDR (fixture `buddy3d.h264`) i v SDP. Zbývající riziko pro
jiné kamery je otevřená otázka 1. `docs/simulator.md` doplněný.

**Přesunuto do T-127 (oddíl 1h):** uspání a probuzení Macu s otevřeným oknem (`kVTInvalidSessionErr`), zamčení
a odemčení s oknem na obrazovce, pohled na obraz okem v okně a malém náhledu (Automaticky vs. Vypnuto), přepnutí volby
v okně nastavení při otevřeném okně a dvě okna kamery na obrazovce. Agent je udělat nemůže (vzdálené sezení, zamčená
obrazovka, Mac by se sám neprobudil); automaticky je pokrývají měření výše a testy T-203 (návrat k softwaru po chybě
dekódování, přepnutí od dalšího klíčového snímku).

**Ověření:** `dotnet build` 0 varování, 0 chyb. `dotnet test` celé řešení: 10 516 testů, 10 249 prošlo, 267 přeskočeno (testy jiného OS, čtení obrazovky E2E, generátory snímků README), 0 chyb (Camera.FFmpeg 196 s testy VideoToolbox, PrusaSimulator 153). Nový balíček žádný, produktový kód beze změny.
Uzavřeno 2026-11-12: zbývající ruční ověření přesunuto do T-127 (rozhodnutí uživatele 2026-10-30, sběrné ověřovací úkoly).

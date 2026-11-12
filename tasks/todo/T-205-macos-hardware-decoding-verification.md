---
id: T-205
title: macOS – hardwarové dekódování VideoToolbox, ověření a měření v aplikaci
status: todo
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
- [ ] Tabulka A/B (instrukce/s, CPU %, energie, paměť, fps) pro všech 5 scénářů v `docs/hw-decoding.md`, AOT build.
- [ ] Instrukce aplikace + pomocného procesu s VT jsou proti `Off` nižší aspoň o 40 % v normálním okně 720p i 1080p
      a v malém náhledu (T-192: −48 až −63 %); doba CPU (aplikace + pomocný proces) není vyšší než u softwaru.
- [ ] Paměť navíc (aplikace + pomocný proces) je nejvýš 40 MB na stream (T-192: +10–25 MB, pomocný proces 17–36 MB);
      po zavření okna kamery `VTDecoderXPCService` skončí nebo jeho paměť klesne (relace se uvolní).
- [ ] Bez okna kamery (jen sledování a detekce) se relace VT nevytvoří a instrukce jsou stejné jako u `Off` (±5 %).
- [ ] Kvalita obrazu VT vs. software v mezích výše; dekódováno 25,0 snímků/s, malý náhled převádí jen splatné snímky.
- [ ] Uspání / probuzení, zamčení, dvě kamery a přepnutí volby za běhu bez černého okna a bez trvalé chyby
      (výsledky v poznámkách; ruční části, které agent neudělá, → T-127).
- [ ] Pokud některá mez neplatí: nic se nemění, výsledek a návrh (např. výchozí „Vypnuto“ na macOS) jdou do
      `## Otevřené otázky` pro uživatele.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k AOT a licencím
- Bez nového balíčku a bez změny produktového kódu (jen měření). Pomocné měřicí nástroje (C, Swift, skripty) zůstávají
  ve scratchpadu nebo mimo repozitář; do repozitáře jen dokumentace.

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

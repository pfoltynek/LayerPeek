---
id: T-192
title: Hardwarové dekódování H.264 kamery – průzkum (spike)
status: done
owner: architect
priority: P3
platform: all
depends_on: [T-184, T-185]
---

## Cíl
Výkonnostní analýza (2026-11-06): kamera se dekóduje jen softwarově. Hardwarové dekódování (D3D11VA / DXVA2
na Windows, VideoToolbox na macOS) by mohlo snížit zátěž CPU. Zkomplikuje ale build LGPL FFmpeg (T-026, T-162)
a kopírování snímků z GPU zpět do paměti může úsporu sníst. Nejdřív je potřeba změřit, jestli se to po T-184 a T-185
vůbec vyplatí.

## Rozsah
- Změřit CPU softwarového dekódování po T-184 a T-185 (720p a 1080p, simulátor): je dekódování ještě podstatná část zátěže?
- Zjistit, co by vyžadoval build FFmpeg (konfigurace, licence LGPL, velikost knihoven).
- Doporučení „vyplatí se / nevyplatí se“ s čísly. Žádná implementace.

## Akceptační kritéria
- [x] Měření a rozbor dopadu na build jsou zapsané v `docs/` ([docs/hw-decoding.md](../../docs/hw-decoding.md)).
- [x] Doporučení je předložené uživateli (závěrečná zpráva a otázky 2–5 níže). Případná implementace vznikne jako nový
      úkol po jeho rozhodnutí.
- [x] `dotnet build` a `dotnet test` bez chyb a varování (jen dokumentace)

## Otevřené otázky
1. Chcete hardwarové dekódování vůbec zkoumat, nebo úkol zamítnout? (Blokuje implementaci.)

   Rozhodnuto uživatelem 2026-11-11: **ano, zkoumat, nejdřív měřit.** Zjistit, jak velký podíl zátěže okna kamery je
   dekódování H.264 a jestli hardwarové dekódování (VideoToolbox na macOS, D3D11VA / DXVA2 na Windows, Windows jen
   rozborem) ušetří CPU a energii i s kopií snímků z paměti GPU. Žádná změna produktového kódu.

Otázky z průzkumu (architekt, 2026-11-11). Dokončení průzkumu neblokují, rozhodují o navazující implementaci:

2. **Implementovat hardwarové dekódování?** Doporučení: ano, nejdřív macOS (VideoToolbox s `av_hwframe_map`, software
   jako fallback a pro samostatné klíčové snímky detekce). Instrukce aplikace s kamerou klesnou o 45–63 %, odhad energie
   CPU o 20–50 %, doba CPU jen o 0–35 % a paměť naroste o 10–25 MB (systémový proces `VTDecoderXPCService`). Pokud ano,
   architekt založí implementační úkol(y) podle `docs/hw-decoding.md` (oddíl „Odhad pracnosti“).
3. **Výchozí stav a nastavení:** zapnuto automaticky bez volby, zapnuto s volbou v Nastavení > Kamera (např.
   „Hardwarové dekódování“: Automaticky / Vypnuto), nebo vypnuto s volbou? Volba znamená texty v 8 jazycích a manuály.
4. **Windows:** implementovat D3D11VA hned ve stejném úkolu s výchozím softwarem, dokud ho T-125 nezměří, nebo
   Windows odložit a udělat zatím jen macOS? Build BtbN už D3D11VA obsahuje, úspora na Windows je neověřená.
5. **Bezpečnost a paměť:** souhlasíte, že data řezů H.264 z kamery (nedůvěryhodná síť) zpracuje navíc systémový
   hardwarový dekodér a jeho ovladač (rozšíření útočné plochy z T-050, chráněno aktualizacemi OS), a s +10–25 MB paměti
   na stream?

## Poznámky k implementaci

### Výsledek (architekt, 2026-11-11, Mac mini M2 Pro, macOS 27.2)
Celá zpráva: [docs/hw-decoding.md](../../docs/hw-decoding.md), odkaz z `docs/architecture.md` (oddíl „Hardwarové
dekódování kamery“). Produktový kód ani build se neměnily. Benchmark v C, sonda v `FFmpegDecoder` (v odděleném
worktree, smazaném po měření) a knihovny FFmpeg s VideoToolboxem byly jen ve scratchpadu.

- Metrika jsou instrukce procesu (`ri_instructions`) **včetně** systémového procesu `VTDecoderXPCService`, který
  VideoToolbox používá. Pomocný proces se přiřazuje přes `responsibility_get_pid_responsible_for_pid`, aby se nezapočítal
  proces animované tapety. CPU % a energie (`ri_energy_nj`) jsou doplňkové: `powermetrics` vyžaduje `sudo` a IOReport
  bez práv roota vrací nuly.
- Podíl softwarového dekódování na instrukcích aplikace: normální okno 720p 53–58 %, 1080p 50–52 %, malý náhled
  67 % / 69 %.
- Aplikace s VideoToolboxem (`av_hwframe_map`, G instr./s, SW → VT): okno 720p 0,41–0,45 → 0,21–0,22 (−49 %), malý
  náhled 720p 0,35 → 0,18 (−50 %), okno 1080p 1,04–1,08 → 0,55–0,56 (−48 %), malý náhled 1080p 0,78 → 0,29 (−63 %).
  Kopie z GPU (`av_hwframe_transfer_data`) stojí 0,7 / 2,2 M instr. na snímek, mapování skoro nic (sdílená paměť).
  VideoToolbox rovnou do BGRA vychází hůř než NV12 + swscale.
- Doba CPU (aplikace + pomocný proces) klesne jen o 0–35 %, odhad energie CPU o 20–50 %. Paměť je o 10–25 MB vyšší
  (pomocný proces).
- Build macOS: `--enable-videotoolbox --enable-hwaccel=h264_videotoolbox`, LGPL 3 zůstává, +27 KB, systémové frameworky.
  Windows BtbN už obsahuje D3D11VA / DXVA2 / D3D12VA (statická kontrola DLL). AOT: stávající vazby FFmpeg.AutoGen
  a callback `[UnmanagedCallersOnly]`, žádný nový balíček.
- `dotnet build` 0 varování, `dotnet test` 10 347 testů: 10 080 prošlo, 267 přeskočeno, 0 chyb (jen dokumentace).
- Doporučení: vyplatí se, nejdřív macOS, Windows po změření v T-125. Implementační úkoly zatím nevznikly (otázky 2–5).
- Pokud se bude implementovat Windows, má T-125 dostat měření D3D11VA (instrukce nebo CPU, iGPU i dedikovaná GPU).
  Tento bod nevznikl, protože implementace zatím není rozhodnutá.

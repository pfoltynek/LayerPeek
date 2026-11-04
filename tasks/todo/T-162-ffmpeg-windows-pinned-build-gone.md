---
id: T-162
title: FFmpeg pro Windows – připnutý build BtbN už neexistuje (404)
status: todo
owner: core-dev
priority: P1
platform: windows
depends_on: []
---

> **Vedlejší nález spiku T-149 (2026-11-03).**

## Cíl
`build/FFmpeg.targets` stahuje na Windows build BtbN
`https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-17-20-51/ffmpeg-n9.0.2-28-g669f5ab083-win64-lgpl-shared-9.0.zip`.
2026-11-03 vrací tato adresa **404** (`Not Found`) a GitHub API pro tag `autobuild-2026-10-17-20-51` také 404.
Počítač s prázdnou cache (nový klon, jiný vývojář, čistý stroj pro vydání) proto build na Windows nepřeloží.
Dnešní vývojářský počítač s naplněnou cache to nepozná.

Zjištěno (GitHub API `releases?per_page=100`, 2026-11-03): BtbN drží denní buildy asi dva týdny (nejstarší denní
`autobuild-2026-10-21-13-06`) a dlouhodobě jen poslední build každého měsíce (`autobuild-2026-10-31-12-59`,
`-09-30-…`, `-08-31-…`, … až `autobuild-2024-12-31-13-02`).

## Rozsah
- Připnout build `autobuild-2026-10-31-12-59` (rozhodnutí uživatele 2026-11-04, viz otázka 1) přes `build/update-ffmpeg.ps1`
  (ověří `checksums.sha256`, složku v archivu, licenci LGPL) a aktualizovat `build/FFmpeg.targets`.
- Pravidlo pro budoucí aktualizace zapsat do dokumentace skriptu / `docs/prerequisites.md` (např. „připínat jen
  měsíční build“) a případně ho vynutit v `update-ffmpeg.ps1` (+ `update-ffmpeg.Tests.ps1`).
- Ověřit na Windows s prázdnou cache (`LayerPeekFFmpegCacheRoot` na prázdnou složku): `dotnet build`, `dotnet test`
  (FFmpeg testy, MP4 muxer T-144), AOT publish `win-x64`.

## Akceptační kritéria
- [ ] Build na Windows s prázdnou cache stáhne a ověří FFmpeg a projde.
- [ ] Testy `update-ffmpeg.Tests.ps1` prochází, pravidlo pro výběr buildu je zapsané.
- [ ] Seznam licencí třetích stran (`ThirdPartyLicenses`) odpovídá nové verzi.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
1. ~~**Který build připnout?**~~ *Rozhodnutí uživatele 2026-11-04: varianta a) – trvalý měsíční build
   `autobuild-2026-10-31-12-59`.* Původní návrh architekta: poslední měsíční build větve 9.0, dnes
   `autobuild-2026-10-31-12-59` / `ffmpeg-n9.0.2-39-g7adfbe40dc-win64-lgpl-shared-9.0.zip`
   (SHA-256 `e907b01fa4783913151237e37df2a10fa97d99a1598a021bf59d4ae62ffed4c0` podle `checksums.sha256` releasu,
   ověřeno stažením 2026-11-03). Alternativa: zrcadlit zip na vlastní release v repozitáři LayerPeek na GitHubu
   (nezávislé na úklidu BtbN, ale vlastní hosting binárek a povinnosti LGPL ke zdrojům).
2. Má se stejné riziko řešit i pro macOS? Tam se stahuje oficiální tarball `ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz`
   (dostupný, SHA-256 ověřen 2026-11-03), riziko je malé.

## Poznámky k implementaci
(vyplní agent po dokončení)

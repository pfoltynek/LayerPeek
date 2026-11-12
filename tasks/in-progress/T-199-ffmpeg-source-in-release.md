---
id: T-199
title: Vydání – zdrojový kód FFmpeg přiložený k vydání a věta o FFmpeg v okně O aplikaci (LGPL)
status: in-progress
owner: qa-reviewer
priority: P1
platform: all
depends_on: [T-198]
---

> **Rozhodnutí uživatele 2026-11-11 (T-198, otázka 9):** ke každému vydání LayerPeeku se na GitHub přiloží přesně
> použitý zdrojový kód FFmpeg (archiv přesné verze včetně voleb configure / skriptů buildu) a okno O aplikaci dostane
> větu doporučenou na https://ffmpeg.org/legal.html (kontrolní seznam, bod 10) ve všech 8 jazycích.

## Cíl
Splnit body kontrolního seznamu LGPL na https://ffmpeg.org/legal.html, které dnes chybí: zdroj FFmpeg hostovaný u binárky
(body 3–8) a zmínka v okně O aplikaci (bod 10). Dnes aplikace jen odkazuje na cizí servery (BtbN zip, tarball ffmpeg.org)
a build BtbN, na který se odkazuje, už jednou zmizel (T-162).

## Rozsah
- Zjistit, z jakého přesného zdroje je FFmpeg každého OS:
  - macOS: oficiální tarball `ffmpeg-9.0.2.tar.xz` (připnutý SHA-256 v `build/FFmpeg.targets`), přeložený
    `build/macos/build-ffmpeg.sh`.
  - Windows: build BtbN `ffmpeg-n9.0.2-39-g7adfbe40dc-win64-lgpl-shared-9.0` = commit FFmpeg z větve `release/9.0`
    (bez úprav) přeložený skripty BtbN FFmpeg-Builds v commitu tagu `autobuild-2026-10-31-12-59`.
- Krok balení `ffmpeg-source` v `scripts/release-build.ps1`: `ffmpeg-source-<verze>-<os>.tar.xz` se zdrojem, skriptem
  buildu / skripty BtbN, volbami configure (přečtenými z vydávané knihovny) a README; hashe proti připnutým hodnotám;
  soubor jde do `SHA256SUMS-<os>.txt`, `build-info.json` a nahrání jako ostatní soubory. Stažené vstupy v cache
  `LayerPeek.Build/ffmpeg-source` (nikdy se nemaže).
- Okno O aplikaci: věta o FFmpeg a odkaz na archiv zdroje u vydání, ve všech `Strings.*.resx`, kontrola rozložení.
- `docs/release.md`, `docs/prerequisites.md`, testy Pester (bez sítě) a xUnit.

## Akceptační kritéria
- [x] Zdroj každého OS určený a připnutý (commit, strom gitu, SHA-256) v `build/FFmpeg.targets`; kontrola, že připnutý
      zdroj Windows patří k připnutému zipu BtbN (commit z názvu složky, tag z adresy)
- [x] `release-build.ps1` vytvoří `ffmpeg-source-<verze>-<os>.tar.xz` (ověřené vstupy, verze vydávané knihovny odpovídá
      zdroji), je v `SHA256SUMS-<os>.txt` a `build-info.json`; `-SignOnly` (T-197) a nahrání fungují beze změny
      (běh na Windows → přesunuto do T-125)
- [x] Stažené vstupy v cache `LayerPeek.Build/ffmpeg-source`, druhý běh nic nestahuje, poškozený soubor se nahradí
- [x] Testy Pester bez sítě (falešné stahování, lokální repozitáře gitu)
- [x] Okno O aplikaci: věta o FFmpeg a odkaz na archiv zdroje ve všech 8 jazycích, rozložení ve všech jazycích
      (`AboutWindowLayoutTests`), snímky (sada macOS; sada Windows → přesunuto do T-125)
- [x] `docs/release.md` a `docs/prerequisites.md` aktualizované; příručky (popisují O aplikaci jen kvůli podpoře,
      licence ne) zkontrolované
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Znění věty v okně O aplikaci.** Bod 10 na https://ffmpeg.org/legal.html (ověřeno 2026-11-11) zní přesně:
   *Mention "This software uses libraries from the FFmpeg project under the LGPLv2.1" in your program "about box".*
   Oba naše buildy jsou ale `--enable-version3` (LGPL 3 nebo novější: `build-ffmpeg.sh`, BtbN `defaults-lgpl.sh`,
   `LICENSE.txt` = `COPYING.LGPLv3`), takže „LGPLv2.1“ by nebylo pravda. Implementováno: „This software uses libraries
   from the FFmpeg project under the LGPLv3 or later.“, přeložené do 8 jazyků. Souhlasí uživatel s (a) verzí licence
   místo šablony a (b) překladem (místo anglické věty doslova ve všech jazycích)?
2. **Windows: externí knihovny v DLL BtbN (bod 18 kontrolního seznamu).** Varianta `win64-lgpl-shared` staticky
   přikládá do DLL asi 50 knihoven (řádek configure z `avcodec-63.dll`: mimo jiné `libdav1d`, `libaom`, `libvpx`,
   `libopus`, `libass`, `libfribidi`, `gmp`, `libbluray`, `libmp3lame`, `libssh`, `libtwolame`, `libzvbi`, `libsoxr`,
   `librsvg`, `libplacebo`, `libgme`, `libaribb24`, `iconv`, `vulkan`, `amf`, `libvpl`…), z nichž některé jsou LGPL.
   Archiv obsahuje jen zdroj FFmpeg a skripty BtbN (`scripts.d/` uvádí repozitář a commit každé knihovny, jejich zdroj
   v archivu není, `README.txt` to říká). Stačí to, nebo (b) přikládat i zdroje všech závislostí (stovky MB, skript,
   který stáhne každý repozitář/commit z `scripts.d`), nebo (c) na Windows přejít na vlastní minimální LGPL build jako
   na macOS (jen RTSP/H.264/MP4, žádné externí knihovny; nový úkol, toolchain na Windows)? Licenční / balicí rozhodnutí.
3. **Bod 9 kontrolního seznamu (stránky se stažením):** věta „This software uses code of FFmpeg licensed under the
   LGPL… and its source can be downloaded here“ na každé stránce s odkazem ke stažení (text vydání na GitHubu, README,
   winget). Rozhodnutí 2026-11-11 jmenuje jen okno O aplikaci, proto neimplementováno; README dostal jen věcnou větu,
   že vydání zdroj obsahuje. Doplnit větu do textu vydání (`release-upload.ps1`, sekce CHANGELOG) a README?
4. **Rozložení okna O aplikaci (ui-designer).** Věta s odkazem se do 508 DIP (T-033) v původním rozložení nevešla (561 DIP,
   rezerva byla 24 DIP). Řešení: blok licence (licence, copyright, věta o FFmpeg drobným písmem 12 px, průhlednost 0,75)
   s mezerou 2 DIP a tři odkazy v `WrapPanel` (dva v řádku, třetí pod nimi). Výsledek 506 / 508 DIP ve všech jazycích
   (Inter, macOS), rezerva jen 2 DIP. Schválit, nebo navrhnout jiné rozložení (např. širší okno, tlačítka podpory
   vedle sebe)?

## Poznámky k implementaci
Implementace 2026-11-11 (qa-reviewer, Mac, větev `feature/T-199-ffmpeg-source-in-release`):
- **Zjištěné zdroje.** macOS: `ffmpeg-9.0.2.tar.xz` z ffmpeg.org (SHA-256 `8c385028…e002e`, už připnutý z T-026),
  `build-ffmpeg.sh`. Windows: název assetu BtbN `n9.0.2-39-g7adfbe40dc` = `git describe` commitu FFmpeg
  `7adfbe40dcde598f93a6963d2f7b22cd75202ccc` (strom `effafc94…9474`, větev `release/9.0`, GitHub API); BtbN
  `build.sh` klonuje větev bez úprav (záplaty v `patches/` jen pro `aom` a `aribb24`), `addins/9.0.sh` = `release/9.0`,
  `variants/defaults-lgpl.sh` = `--enable-version3`. Tag `autobuild-2026-10-31-12-59` ukazuje na commit FFmpeg-Builds
  `5031c6f1082414e53688d39e521849905071070d` (strom `322b4b72…6b2c`, `git ls-remote` i API). Ověřeno i v knihovnách:
  `avcodec-63.dll` zipu BtbN (SHA-256 `e907b01f…ed4c0` sedí) obsahuje `FFmpeg version n9.0.2-39-g7adfbe40dc-20261031`,
  `libavcodec.63.dylib` z cache Macu `FFmpeg version 9.0.2`; obě knihovny mají v sobě celý řádek configure.
- **Jeden archiv, nebo dva:** dva. Zdroje se liší (oficiální tarball 9.0.2 proti commitu `n9.0.2-39` větve), společný
  archiv by u jednoho OS neodpovídal binárkám (bod 4 kontrolního seznamu).
- **Formát:** `ffmpeg-source-<verze>-<os>.tar.xz` s jednou složkou nejvyšší úrovně. macOS: oficiální tarball beze změny
  (kdokoli ho ověří podpisem `.asc` z ffmpeg.org), `build-ffmpeg.sh`, `README.txt`. Windows: `git archive` commitu
  FFmpeg a commitu skriptů BtbN (oficiální tarball commitu větve neexistuje; obsah ověřitelný přes `git` podle commitu
  a stromu), `README.txt`. Vnitřní tary se nerozbalují (žádné dlouhé cesty na Windows), komprese jen jednou (xz).
  Zip BtbN se nepřikládá (binárky jsou v aplikaci, README uvádí URL a SHA-256).
- **Připnuté hodnoty:** nové vlastnosti `LayerPeekFFmpegWindowsSource*` / `LayerPeekFFmpegWindowsScripts*` v
  `build/FFmpeg.targets` (build je nepoužívá). SHA-256 tarů `git archive` (`bb3aaf57…ae28`, `61c8b7b2…7c23`) spočítané
  na Macu (git 2.54.0), dvakrát se stejným výsledkem; `-c core.autocrlf=false -c core.eol=lf`, test Pester to ověří
  s globálním `core.autocrlf=true` (mutace bez přepínače test shodí).
- **`ReleaseTools.psm1`:** `Get-FFmpegSourceFileName`, `Get-FFmpegSourceCacheDirectory`, `Get-FFmpegSourcePins`
  (čistá, kontrola souladu s pinem BtbN), `Invoke-FFmpegSourceDownload` (jediné stahování, v testech nahrazené),
  `New-GitSourceArchive`, `Save-FFmpegSourceInput` (cache, `tmp-…` v cache, přesun až po ověření, cache se nemaže),
  `Get-FFmpegSourceInputs`, `Get-FFmpegLibraryFacts` (verze a configure z bajtů knihovny, prefix → `<prefix>`, aby
  v README nebyla cesta `/Users/…` stroje buildu), `Get-FFmpegLibraryVersionProblems`, `Get-FFmpegSourceReadme`,
  `Get-FFmpegPublishedLibrary`, `New-FFmpegSourceArchive` (výpis archivu porovnaný s očekávaným).
- **`release-build.ps1`:** `Get-FFmpegSourceInputs` hned po kontrole klíče feedu (před dlouhým buildem, chyba skončí
  hned) a krok balení `ffmpeg-source` (oba OS). Soubor jde do `build-info.json` (i záznam `pending` T-197, takže
  `-SignOnly` ho porovná jako ostatní), `SHA256SUMS-<os>.txt` a do nahrání; `release-upload.ps1` seznam povolených
  názvů nemá (nahraje `build-info.json`), `Test-ReleaseMarkerFileName` ho nebere jako značku OS, takže jde v první dávce;
  `release-publish.ps1` ho ověří proti součtům. Sandbox testy `release-build.ps1` (bez `build/FFmpeg.targets`) dál
  skončí před čímkoli ze sítě.
- **Ověřeno ručně na Macu (síť, scratchpad cache):** obě OS (Windows zdroj gitem stažený i na Macu): stažení / `fetch`,
  ověření SHA-256, druhý běh jen z cache, archivy 12,0 MB (macOS) a 11,8 MB (Windows, xz z ~100 MB asi 20 s), kontrola
  verze s reálnými knihovnami (`libavcodec.63.dylib`, `avcodec-63.dll` z BtbN). Celý `release-build.ps1` (s klíčem) se
  nespouštěl.
- **Aplikace:** `AboutViewModel.FFmpegNotice`, `FFmpegSourceText`, `OpenFFmpegSourceCommand` →
  `ThirdPartyLicenses.FFmpeg.SourceArchiveUri(verze, macOS)` (`…/releases/download/v<verze>/ffmpeg-source-<verze>-<os>.tar.xz`);
  řetězce `AboutFFmpegNotice`, `AboutFFmpegSource` ve všech 8 `Strings.*.resx`. Okno: viz otázka 4. Pořadí tabulátoru
  Repozitář → Licence → Zdroj FFmpeg → podpora (test upravený o nový odkaz). Snímky `about-support*.png` sady macOS
  přegenerované; sada Windows v T-125 (2ab).
- **Testy:** Pester `FFmpeg source of a release (T-199)` (26 testů: piny, soulad s BtbN, fakta knihovny, cache se
  stahováním nahrazeným mockem, git z lokálních repozitářů, archiv obou OS, `release-build.ps1`), xUnit
  `AboutViewModelTests` (texty, věta ve všech jazycích s „FFmpeg“ a „LGPLv3“, odkaz Windows/macOS),
  `ThirdPartyLicensesTests` (adresa archivu, název shodný s `Get-FFmpegSourceFileName`). `ReleaseTools.Tests.ps1`
  319 / 0 / 3 skip, `update-ffmpeg.Tests.ps1` 75 / 0.
- **Build a testy (Mac, 2026-11-11):** `dotnet build` 0 varování, 0 chyb; `dotnet test` 10 441 celkem, 0 selhalo,
  267 přeskočeno (App.UiTests 1379, App.Tests 4651, Core 3329, Scripts 13 …).
- **Nové závislosti:** žádné (PowerShell, `git`, systémový `tar`).
- **Dokumentace:** `docs/release.md` (oddíl „Zdrojový kód FFmpeg (T-199)“, výstupy, kroky balení), `docs/prerequisites.md`
  (cache, ruční připnutí zdroje při nové verzi), `build/update-ffmpeg.ps1` (nápověda), `CHANGELOG.md`, README EN a CZ
  (věcná věta v oddílu komponent třetích stran). Příručky O aplikaci popisují jen kvůli podpoře, licence ne: beze změny.
- **Zbývá na Windows → T-125 (2ab):** snímky sady Windows, `dotnet test` se Segoe UI (rezerva výšky), `tar.exe`
  zapíše xz, Git for Windows zapíše `git archive` se stejným SHA-256, zkušební `release-build.ps1 -SkipFeedSignature`.
- **Návrh na později (ne rozhodnutí):** `build/update-ffmpeg.ps1 -Platform windows` by mohl zdroj připnout sám (commit
  z API GitHubu, tag přes `git ls-remote`, SHA-256 `git archive`); dnes ručně podle `docs/prerequisites.md`.

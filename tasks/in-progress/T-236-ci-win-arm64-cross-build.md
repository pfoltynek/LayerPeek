---
id: T-236
title: CI – křížový build win-arm64 (Native AOT publish + kontrola architektury, bez spuštění)
status: in-progress
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-225, T-226]
---

## Cíl
Rozhodnutí uživatele 2026-11-15 (otázka z revize T-226): CI (T-218) dnes na Windows staví a publikuje jen `win-x64`.
Křížový build `win-arm64` (T-224, T-225) se ověřuje jen lokálně a při vydání, takže rozbití (toolchain, llvm-mingw,
FFmpeg pro ARM64, nativní balíčky Avalonie/Skia) by se ukázalo až při vydání.

## Rozsah
- Job (nebo krok jobu Windows) v `.github/workflows/ci.yml`: `dotnet publish src/LayerPeek.App -c Release -r win-arm64`
  na runneru x64 a kontrola architektury PE (`Get-PublishArchitectureCheck`, T-224); aplikace se nespouští.
- Toolchain na runneru: komponenta MSVC ARM64 (je na obrazu `windows-2025`? ověřit, jinak instalace), MSYS2 z T-202
  a llvm-mingw (připnutý zip z T-225) – cache llvm-mingw a FFmpeg `win-arm64-<klíč>` přes `actions/cache` (připnuté na
  SHA s komentářem verze), aby se při zásahu cache nic nestahovalo ani nepřekládalo.
- Doba běhu: změřit studený a teplý běh; job nesmí prodloužit CI nad limit jobu (dnes 60 min).
- `CiWorkflowTests` a `docs/prerequisites.md` (oddíl „CI (GitHub Actions)“) aktualizovat.

## Akceptační kritéria
- [ ] CI na Windows publikuje `win-arm64` a kontrola architektury projde (všechny PE `0xAA64`)
  – lokální simulace kroků jobu prošla (8 PE, vše ARM64); čeká na první skutečný běh na GitHubu (spustí uživatel)
- [ ] Cache llvm-mingw a FFmpeg ARM64 funguje (teplý běh nic nestahuje ani nepřekládá); doby běhu zapsané
  – lokálně ověřeno (studený / teplý běh níže); trefu cache a doby na runneru zapsat po prvním a druhém běhu na GitHubu
- [x] `CiWorkflowTests` pokrývají nový krok; `docs/prerequisites.md` odpovídá
- [ ] `dotnet build` a `dotnet test` bez chyb a varování
  – `dotnet build` 0 varování, `LayerPeek.Scripts.Tests` zelené; celé `dotnet test` proběhne v CI spolu s kritérii výše

## Otevřené otázky
- (žádné blokující) Limit jobu `timeout-minutes: 30` je odhad (~1,5× odhadu studeného běhu); po změření na runneru
  případně upravit stejně jako u T-218 (otázka 9 tam).

## Poznámky k implementaci
Stav 2026-11-15 (qa-reviewer, Windows x64, worktree `LP-T-236`): workflow, testy a dokumentace hotové a lokálně ověřené.
Úkol zůstává v `in-progress/`: zelený běh, trefu cache a doby na GitHubu ověří uživatel / hlavní agent po pushi.

**Komponenta MSVC ARM64 na runneru:** `Microsoft.VisualStudio.Component.VC.Tools.ARM64` **je na obrazu `windows-2025`
předinstalovaná** (README obrazu `actions/runner-images`, `images/windows/Windows2025-Readme.md`, obraz 20261111.331.1,
tabulka komponent Visual Studia: `VC.Tools.ARM64` 17.14.36510.44, vedle `VC.Tools.x86.x64`). Žádná instalace
instalátorem VS, žádný kompromis k rozhodnutí. Pro případ, že ji budoucí obraz vypustí, job hned na začátku spustí
`Get-NativeToolchainProblems -Runtimes win-arm64 -HostOs windows` (vswhere, stejná kontrola jako `release-build.ps1`) a
selže za pár sekund místo až po překladu FFmpeg.

**Rozložení: samostatný job `cross-arm64` („Cross-publish (win-arm64)“), ne kroky jobu Windows.** Důvody:
- běží souběžně s `Build and test (…)` – celková doba CI se neprodlouží (job Windows dnes ~15 min);
- padající test v jobu Windows by kroky ARM64 přeskočil (a naopak) – takhle je výsledek ARM64 vidět vždy;
- kořen cache FFmpeg jobu obsahuje jen soubory ARM64; ve společném jobu by uložení cache `win-x64` (celá složka
  `$RUNNER_TEMP/ffmpeg`) zabalilo i ARM64 FFmpeg a llvm-mingw (~570 MB);
- vlastní `timeout-minutes: 30`; cena: navíc checkout, `setup-dotnet` a obnova NuGet (~1–2 min minut runneru,
  veřejný repozitář zdarma).
- `CI result` čeká na `[changes, build, cross-arm64]` a selže, když job s kódem neprošel; u PR jen s dokumentací se job
  přeskočí jako buildy (`if: needs.changes.outputs.code == 'true'`) a `CI result` projde. Vyhodnocení teď nahlásí obě
  chyby najednou (build i ARM64).

**Kroky jobu:** checkout (`persist-credentials: false`) → `setup-dotnet` → `LayerPeekFFmpegCacheRoot` a
`ARM64_PUBLISH_DIR` do `$RUNNER_TEMP` → kontrola MSVC ARM64 → `Read the llvm-mingw pin` (regex z `build/FFmpeg.targets`:
`LayerPeekLlvmMingwSha256`, `LayerPeekLlvmMingwFolder`) → obnova NuGet / FFmpeg / (jen při minutí FFmpeg) llvm-mingw →
MSYS2 jen při minutí FFmpeg (`msystem: MSYS`, `update: false`, `make diffutils` = `_LayerPeekFFmpegMsys2Packages` pro
`win-arm64`) → `dotnet build src/LayerPeek.App -c Release -r win-arm64` → uložení FFmpeg a llvm-mingw (hned po buildu,
selhání linku AOT je nezahodí) → `dotnet publish src/LayerPeek.App -c Release -r win-arm64 -o $env:ARM64_PUBLISH_DIR` →
uložení NuGet (až po publish, může dotáhnout balíčky) → `Get-PublishArchitectureCheck -Runtime win-arm64` + kontrola,
že v publish jsou `avformat/avcodec/avutil/swscale-*.dll` a `FFmpeg-LICENSE.txt` (vypnutý FFmpeg by kontrolou PE prošel).

**Cache:**
- FFmpeg ARM64: cesta jen `${{ runner.temp }}/ffmpeg/win-arm64-*`, klíč
  `ffmpeg-v1-windows-2025-win-arm64-<hashFiles(build/FFmpeg.targets, build/windows/build-ffmpeg.sh)>` – tytéž vstupy
  jako název složky cache (pin tarballu, skript, pin llvm-mingw), bez `restore-keys`.
- llvm-mingw: obnoví se jen při minutí FFmpeg (build ho jinak nepotřebuje – stahuje se a kontroluje jen v cíli
  `_LayerPeekBuildFFmpegWindows`), klíč `llvm-mingw-v1-<OS>-<SHA-256 pinu>`, takže změna skriptu FFmpeg llvm-mingw
  znovu nestahuje; uloží se jen když se obnova spustila a minula.
- NuGet: předpona `nuget-win-arm64-<OS>-` (`restore-keys`: vlastní, pak `nuget-<OS>-` jobu Windows). Se společnou
  předponou by `restore-keys` jobu Windows mohl obnovit menší sadu balíčků tohoto jobu.

**Akce:** žádná nová; znovu použité pinové SHA ověřené `git ls-remote` (2026-11-15, lehké tagy):
`actions/checkout` v7.0.1 `3d3c42e5…`, `actions/setup-dotnet` v6.1.0 `0f4a354d…`, `actions/cache` v6.1.0 `55cc8345…`,
`msys2/setup-msys2` v2.33.0 `ec48f7c5…` (vstup `msystem: MSYS` ověřen v `action.yml` na připnutém SHA).

**Testy (`tests/LayerPeek.Scripts.Tests/CiWorkflowTests.cs`, napřed červené, pak zelené):**
`TheArm64JobCrossPublishesAndChecksTheArchitecture` (runner, podmínka, pořadí kontrola MSVC → build → publish → kontrola
architektury, `throw` při problému, kontrola knihoven FFmpeg, nic se nespouští), `TheArm64JobCachesFFmpegAndLlvmMingw`
(cesty a klíče, podmínky obnovy a uložení, pin a balíčky MSYS2 proti `build/FFmpeg.targets`, uložení mezi buildem a
publish, předpona NuGet), `TheResultCheckIncludesTheArm64Job`; `EveryJobHasATimeout` čeká 4 joby. Ověřeno mutací
(6 mutací, každou test shodí): llvm-mingw bez podmínky, jiné balíčky MSYS2, cache FFmpeg přes celý kořen, `CI result`
bez ARM64, kontrola bez `throw`, uložení FFmpeg až po publish.

**Validace YAML:** `actionlint` 1.7.7 z oficiálního obrazu Dockeru `rhysd/actionlint:1.7.7` (stažen lokálně do Dockeru,
nic se neinstalovalo globálně): jediné hlášení je existující štítek `xcode-27` (actionlint 1.7.7 ho nezná; ne z tohoto
úkolu), k novému jobu nic.

**Lokální simulace kroků jobu** (pwsh, čerstvý klon `adbc3a86`, prázdná cache v dočasné složce, teplá NuGet cache,
16 jader; runner má 4):
- studený běh: kontrola MSVC i pin < 1 s; `build -r win-arm64` **193 s** (stažení llvm-mingw 191 MB + rozbalení +
  stažení tarballu + křížový překlad FFmpeg), `publish` **48 s**, 0 varování; kontrola architektury: 8 PE
  (`LayerPeek.exe`, `avcodec-63`, `avformat-63`, `avutil-61`, `swscale-10`, `av_libglesv2`, `libHarfBuzzSharp`,
  `libSkiaSharp`), vše ARM64 `0xAA64`.
- teplý běh (nový klon bez `bin/obj`, v kořeni cache jen složka `win-arm64-*` jako po obnově cache, bez llvm-mingw):
  `build` **10 s**, `publish` **46 s**, žádné „Downloading“ ani „Building FFmpeg from source“, llvm-mingw se nestáhl.

**Očekávané doby na runneru (odhad, změřit):** studený běh ~15–20 min (setup ~1 min, NuGet ~1–2 min, MSYS2 ~1–2 min,
build s llvm-mingw a FFmpeg na 4 jádrech ~8–12 min, uložení cache ~1 min, AOT publish ~1,5–2 min; publish `win-x64`
na runneru trval 75 s); teplý běh ~4–5 min (obnova NuGet ~1 min jako u jobu Windows, build ~0,5 min, publish
~1,5–2 min). Job běží souběžně s jobem Windows (~15 min), celková doba CI se tedy nemění.

**Zbývá po pushi (uživatel spustí ručně):** zelený `Cross-publish (win-arm64)` s výpisem „Architecture: 8 PE file(s),
all ARM64 (0xAA64)“; druhý běh: `Restore FFmpeg (win-arm64)` trefa, `Restore llvm-mingw` a MSYS2 přeskočené, v logu
buildu žádné „Downloading the ARM64 toolchain llvm-mingw“ / „Building FFmpeg from source“; doby obou běhů sem; podle
nich případně upravit `timeout-minutes`.

**Po prvních bězích na GitHubu (kroky z review 2026-11-15):**
- `timeout-minutes: 30` jobu `cross-arm64` upravit podle změřeného studeného běhu (~1,5× jeho doby).
- Zkontrolovat využití cache repozitáře (Actions → Caches; kvóta 10 GB, při překročení GitHub maže nejstarší
  položky): FFmpeg `win-x64` a `win-arm64`, llvm-mingw, dvě cache NuGet (job Windows a `nuget-win-arm64-…`) a cache
  macOS (NuGet, FFmpeg). Velikosti sem zapsat.

**Úpravy po review (2026-11-15):** `CI result` u neúspěšného jobu ARM64 nastaví `failed=1` jako u buildu a skončí až
na konci (nahlásí obě chyby); `Save NuGet packages` jobu ARM64 má
`if: ${{ !cancelled() && steps.build.outcome == 'success' && steps.nuget.outputs.cache-hit != 'true' }}`, takže se
uloží i při selhání publish (opakovaný běh balíčky znovu nestahuje), ale jen po úspěšném buildu (obnova balíčků
dokončená). `TheResultCheckIncludesTheArm64Job` a `TheArm64JobCachesFFmpegAndLlvmMingw` to hlídají.

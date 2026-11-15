---
id: T-232
title: Windows – aktualizace nativních závislostí a nástrojů (FFmpeg, MSVC/Windows SDK, WebView/Edge, runner CI)
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-202]
---

## Cíl
Zadání uživatele 2026-11-15: aktualizovat všechny knihovny na Macu i na Windows. Tento úkol pokrývá nativní závislosti
a nástroje, které používá jen Windows; spravované balíčky řeší T-230, macOS T-231.

## Rozsah
- **FFmpeg pro Windows:** po T-202 vlastní minimální build ze stejného zdroje jako macOS (T-231) – stejná verze na obou
  OS; do T-202 build BtbN (`build/FFmpeg.targets`, dnes `n9.0.2-39-g7adfbe40dc`, autobuild 2026-10-31) – nové
  autobuildy BtbN se už neaktualizují, nahradí je T-202.
- **Visual Studio / MSVC a Windows SDK** pro Native AOT: aktuální verze, `docs/prerequisites.md`.
- **Playwright a Edge** pro E2E testy (`LayerPeek.E2E.Tests`): po T-230 nainstalovat prohlížeče nové verze Playwright,
  ověřit E2E (`tools/run-e2e.ps1`).
- **Runner CI** (T-218): obraz Windows (`windows-2025`) – nový obraz ručně, až ho GitHub nabídne.
- Instalátor Velopack (`vpk`) – verze společně s T-230.

## Akceptační kritéria
- [x] FFmpeg na Windows ve stejné verzi jako na macOS (po T-202); testy kamery bez chyb
- [x] `dotnet build`, `dotnet test`, AOT publish win-x64 a `aot-smoke.ps1` bez chyb a varování (`aot-smoke.ps1`
      → přesunuto do T-125, oddíl 2aj)
- [x] E2E testy (`tools/run-e2e.ps1`) s aktuální verzí Playwright a Edge bez chyb (testy prohlížeče
      `LayerPeek.E2E.Tests` prošly; plný `run-e2e.ps1` → přesunuto do T-125, oddíl 2aj)
- [x] `docs/prerequisites.md` odpovídá

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
Ověřeno 2026-11-15 na Windows 11 Pro 10.0.26300 x64 (relace přes vzdálenou plochu), worktree z `develop` 8f946b53.

- **Visual Studio / MSVC** (hlavní agent se souhlasem uživatele): Build Tools for Visual Studio 2026 18.11.3 → **18.12.0**,
  MSVC 14.51.36231 → **14.52** (složka sady nástrojů `14.52.37025`, `cl.exe` 19.52.37029, `link.exe` 14.52.37029);
  komponenta `Microsoft.VisualStudio.Component.VC.Tools.ARM64` zůstala (`vswhere -requires …ARM64` ji vypíše).
- **Windows SDK – nic se neinstaluje:** nainstalované je Windows 11 SDK **10.0.26100** (balíček `Win11SDK_10.0.26100`
  10.0.26100.18 = verze v katalogu 18.12.0, tedy nejnovější servis). Katalog 18.12.0 nabízí i 10.0.28000 (balíček
  10.0.28000.6), ale workload *Desktop development with C++* (`Microsoft.VisualStudio.Workload.VCTools`) má jako
  doporučené 26100, 28000 jen jako volitelné. Native AOT nepředepisuje verzi SDK (ILCompiler najde MSVC přes `vswhere` /
  `vcvarsall`, linkuje jen knihovny importu systému) a publish s 26100 prochází; i obraz CI má 26100. Instalace by
  potřebovala UAC a nic by nepřinesla, proto beze změny.
- **FFmpeg beze změny:** `build/FFmpeg.targets` = oficiální `ffmpeg-9.0.2.tar.xz` pro obě OS (T-202); T-231 na Macu pin
  neměnil (9.0.2 je stále nejnovější vydání). llvm-mingw `20261104` (T-225) je nejnovější vydání na GitHubu, MSYS2
  (gcc 16.2.0) z T-202. Cache se nepřestavovala.
- **Playwright / Edge:** `Microsoft.Playwright` 1.64.1 (T-230, nejnovější stabilní na NuGet; ovladač
  `.playwright/package` 1.64.1 ve výstupu testů). Testy používají nainstalovaný Edge (`Channel = "msedge"`), prohlížeče
  Playwright se **neinstalují** (`docs/prerequisites.md`). Edge na Windows **157.0.4341.46** (Mac v T-230 měl
  157.0.4341.60; Edge se aktualizuje sám).
- **Velopack:** balíček `Velopack` i nástroj `vpk` 1.2.161 (nejnovější stabilní na NuGet, jako v T-230);
  `Get-VelopackVersionProblems` bez problémů, `dotnet tool restore` + `dotnet vpk -h` = „Velopack CLI 1.2.161“.
- **Runner CI:** `windows-2025` je podle README `actions/runner-images` (2026-11-15) totéž co `windows-2025-vs2026`
  a `windows-latest`; obraz 20261103.298.1 (VS Enterprise 2026 18.11, Windows 11 SDK 26100, komponenta ARM64, Edge 156).
  Novější označení obrazu Windows neexistuje (jinak jen `windows-2022` a `windows-11-arm`), `ci.yml` beze změny.
  `docs/prerequisites.md`, tabulka CI, doplněná o shodu označení.
- **Ověření:** `dotnet build` 0 varování, 0 chyb; `dotnet test` **11 468 testů, 0 selhání, 10 886 prošlo, 582
  přeskočeno** (6 min 32 s), z toho `LayerPeek.Camera.FFmpeg.Tests` 220 / 2 přeskočené a `LayerPeek.E2E.Tests`
  (Playwright + Edge, bez myši) 21 prošlo / 1 přeskočený (jen macOS), včetně `ProxyOriginGuardTests`.
  `dotnet publish src/LayerPeek.App -c Release -r win-x64` (58 s) i `-r win-arm64` (49 s, křížově) bez varování
  AOT/trim (jen známé zprávy ILC `Avalonia.Win32 … CreateTargetForHwnd`); `Get-PublishArchitectureCheck`: win-x64 x64
  `0x8664`, win-arm64 ARM64 `0xAA64`, po 8 souborech, 0 problémů; DLL FFmpeg i `FFmpeg-LICENSE.txt` v publish.
- **Přesunuto do T-125 (oddíl 2aj):** `aot-smoke.ps1 -Runtime win-x64` – běžela nainstalovaná LayerPeek uživatele
  (`%LOCALAPPDATA%\LayerPeek\current`), skript se s běžící instancí odmítne spustit a ukončit ji jsem nechtěl; plný
  `tools/run-e2e.ps1` – přes RDP nejsou testy s myší spolehlivé (T-169). V T-125 2ae odškrtnut bod T-230 (build, test,
  publish na Windows).
- `docs/prerequisites.md`: ověřené verze MSVC / SDK u Windows, poznámka k SDK 10.0.28000, označení obrazu CI.
- Žádná nová závislost, žádný NuGet balíček.

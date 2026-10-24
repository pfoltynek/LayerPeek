---
id: T-063
title: win-arm64 – ověření sestavení a balení včetně Velopack (MSVC ARM64)
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-055, T-056]
---

## Cíl
Soustředit na jedno místo všechna neověřená místa kolem `win-arm64`, která zůstala v T-055 (skripty pro vydání) a T-056 (spike Velopack). Obě úlohy byly na Windows x64 ověřené, ale `win-arm64` nešlo ověřit: na stroji chybí komponenta **MSVC ARM64 build tools** a uživatel nemá zařízení s Windows na ARM64 (stav 2026-10-21). Přesunuto na žádost uživatele (2026-10-21: „arm64 věci do samostatného úkolu“), aby T-055 a T-056 na ARM64 nečekaly.

Úkol je jen ověřovací: kód skriptů ani aplikace se nemění, pokud ověření nenajde chybu (pak opravu popsat a případně založit samostatný úkol).

## Rozsah
1. **MSVC ARM64 build tools (převzato z T-055):** komponentu `Microsoft.VisualStudio.Component.VC.Tools.ARM64` musí doinstalovat uživatel v PowerShellu **jako správce**. Pokus agenta v T-055 (`setup.exe modify … --passive --norestart`) skončil kódem 5007, protože proces neběžel se zvýšenými oprávněními. Příkaz a ověření přes `vswhere` jsou v `docs/prerequisites.md`. Instalaci uživatel schválil (2026-10-21).
2. **Sestavení a balení skriptem z T-055 (převzato z T-055, kritérium `win-arm64`):** `pwsh scripts/release-build.ps1 -Runtime win-x64,win-arm64` sestaví a zabalí `win-arm64` stejně jako `win-x64` (publish složka, `LayerPeek-<verze>-win-arm64.zip`, řádek v `SHA256SUMS-windows.txt`), jen lokálně, **bez nahrání**. Ověřit, že:
   - AOT publish `win-arm64` je bez trim/AOT varování z kódu projektu a exe je ARM64 PE (pole Machine `0xAA64`);
   - výchozí běh skriptu (bez `-Runtime`) `win-arm64` nesestaví a `release-upload.ps1 -DryRun` výstup se `win-arm64` odmítne („Build output contains win-arm64, which must not be uploaded“), viz `docs/release.md`, „Povolení `win-arm64` (po T-057)“;
   - smoke test `win-arm64` se na x64 stroji vypíše jako „neověřeno spuštěním“.
   Dokud není hotový T-057, balíček neobsahuje DLL FFmpeg; to je očekávané a zapíše se to do poznámek.
3. **Velopack pro `win-arm64` (převzato z T-056, bod 8):** T-056 ověřil jen názvy souborů kanálu `win-arm64` (s x64 binárkami) a to, že `Setup.exe` / `Update.exe` jsou nativní ARM64 (`docs/velopack.md`, bod 8). Neověřené zůstalo:
   - `vpk pack --channel win-arm64 -r win-arm64` nad skutečnou AOT publish složkou `win-arm64` (z bodu 2), parametry podle `docs/velopack.md`, bod 12; zapsat velikost a seznam souborů;
   - soubory `win-x64` a `win-arm64` vedle sebe ve stejné výstupní složce (žádná kolize názvů).
   Pokus probíhá stejně jako v T-056 mimo repozitář (dočasná kopie v `%TEMP%`), pokud mezitím T-017 nepřidal balicí krok Velopack do skriptů. Pak se použije ten.
4. **Ruční ověření na zařízení ARM64** (spuštění exe, instalace a odinstalace `Setup.exe` `win-arm64`) se zde nedělá, je jen v T-057 (rozhodnutí uživatele 2026-10-21). Tento úkol se uzavře po bodech 1–3.
5. Výsledky zapsat do `docs/velopack.md` (bod 8) a `docs/release.md` („Povolení `win-arm64` (po T-057)“), u T-055 a T-056 jen odkaz sem.

Vztah k dalším úkolům:
- **T-057** (FFmpeg pro `win-arm64`): nezávislý. Body 1–3 nepotřebují FFmpeg. Ruční ověření na zařízení ARM64 (exe, instalátor, kamera) je jen v T-057. Vydávání `win-arm64` (zapnutí `Default` / `Uploadable` v `$script:ReleaseRuntimes`) zůstává podmíněné T-057, viz T-055.
- **T-017** (Velopack ve skriptech): kritérium „instalátor `win-arm64` vznikne stejným skriptem“ patří dál T-017. Tento úkol ověřuje jen to, co nešlo ověřit ve spiku a ve skriptech T-055.

## Akceptační kritéria
- [ ] Komponenta MSVC ARM64 je nainstalovaná a `vswhere` ji najde (příkaz z `docs/prerequisites.md`)
- [ ] `pwsh scripts/release-build.ps1 -Runtime win-x64,win-arm64` sestaví a zabalí `win-arm64` (zip, součet v `SHA256SUMS-windows.txt` odpovídá `Get-FileHash`), AOT publish bez trim/AOT varování z kódu projektu, exe je ARM64 PE
- [ ] Výchozí běh skriptu `win-arm64` nesestaví a `release-upload.ps1 -DryRun` výstup se `win-arm64` odmítne (ověřeno, na GitHubu nic nezměněno)
- [ ] `vpk pack` nad AOT publish `win-arm64` vytvoří balíček kanálu `win-arm64` bez kolize se soubory `win-x64` (výsledek v `docs/velopack.md`, bod 8)
- [ ] Pokusné instalace a dočasné složky jsou po ověření uklizené (stejně jako v T-056, „Pokusy a úklid“)
- [ ] `docs/velopack.md`, `docs/release.md` a `docs/prerequisites.md` aktuální
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Ruční ověření na zařízení ARM64~~ → jen v T-057, tento úkol se uzavře po bodech 1–3 (rozhodnuto 2026-10-21).

## Poznámky k implementaci
(vyplní agent po dokončení)

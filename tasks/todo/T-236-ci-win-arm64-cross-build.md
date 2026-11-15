---
id: T-236
title: CI – křížový build win-arm64 (Native AOT publish + kontrola architektury, bez spuštění)
status: todo
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
- [ ] Cache llvm-mingw a FFmpeg ARM64 funguje (teplý běh nic nestahuje ani nepřekládá); doby běhu zapsané
- [ ] `CiWorkflowTests` pokrývají nový krok; `docs/prerequisites.md` odpovídá
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

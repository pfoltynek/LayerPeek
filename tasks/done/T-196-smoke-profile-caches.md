---
id: T-196
title: Smoke test – zálohovat a obnovovat i mezipaměť ~/Library/Caches/LayerPeek
status: done
owner: qa-reviewer
priority: P3
platform: all
depends_on: [T-191]
---

## Cíl
Zjištění z T-191 (rozhodnutí uživatele 2026-11-11): smoke profil (`Get-SmokeProfile`, `Backup-SmokeProfile` /
`Restore-SmokeProfile`, používá `scripts/aot-smoke.ps1`) nepokrývá mezipaměť `~/Library/Caches/LayerPeek`. Smoke běh
s prázdným profilem ji nevytváří, ale ruční nebo měřicí běh Release exe ji vytvořit může; skutečný profil uživatele
pak po běhu není přesně původní.

## Rozsah
- Doplnit mezipaměť aplikace do smoke profilu na macOS a odpovídající umístění na Windows (ověřit, kam aplikace
  na Windows ukládá mezipaměť; pokud nikam, zapsat do poznámek).
- `~/Library/Caches/LayerPeek.Build` (mezipaměť buildu FFmpeg) se nesmí odsouvat ani mazat.
- Záloha a obnova i tehdy, když složka před během neexistuje (po běhu ji smazat) i když existuje (obnovit přesně).

## Akceptační kritéria
- [x] `Backup-SmokeProfile` / `Restore-SmokeProfile` zahrnují mezipaměť aplikace; `LayerPeek.Build` zůstává netknutá (Pester testy).
- [x] Oba případy (složka existovala / neexistovala) mají Pester test.
- [x] Windows část ověřená nebo přesunutá do T-125. → přesunuto do T-125
- [x] `dotnet build` a `dotnet test` (včetně Pester) bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci
- Mezipaměť aplikace určuje `AppDataPaths.GetDefaultCacheDirectory()` (`src/LayerPeek.Core/Settings/AppDataPaths.cs`):
  macOS `~/Library/Caches/LayerPeek`, Windows `%LOCALAPPDATA%\LayerPeek\cache` (podsložky `notification-images`
  a `last-frames`, `AppServices`). Na Windows tedy mezipaměť existuje; `Get-SmokeProfile` ji přidává jako podsložku,
  ne celé `%LOCALAPPDATA%\LayerPeek` (tam může být nainstalovaná aplikace, Velopack).
- `Get-SmokeProfile` (`scripts/ReleaseTools/ReleaseTools.psm1`) má složku mezipaměti jako poslední položku `Folders`
  (zástupný profil `<root>/caches/LayerPeek`), takže indexy dosavadních složek i místo žurnálu (`Folders[0]`) zůstávají.
  Záloha a obnova jde stejnou žurnálovanou cestou (přejmenování vedle sebe, po běhu smazání nebo vrácení). Starý žurnál
  bez této položky se pořád přečte (obnoví své složky; mezipaměť pak jen neodsunul).
- `LayerPeek.Build` je sourozenec, ne položka profilu; nic ho nepřesouvá. Zástupný profil v testech teď obsahuje
  `caches/LayerPeek.Build/...`, takže ho kontrolují všechny testy smoke profilu (`Get-TreeState` před a po).
- Pester: dva nové testy (mezipaměť existovala → po běhu přesně původní; neexistovala → po běhu smazaná), oba hlídají
  i `LayerPeek.Build`; test názvů složek rozšířen o macOS i Windows cestu. Upravena nápověda a hláška `aot-smoke.ps1`
  (test `-RestoreOnly` hlídá nový text), `docs/prerequisites.md`, `docs/architecture.md`, `docs/aot-gc-settings.md`.
- Windows: cesta je ověřená jen Pester testem, který běží pouze na Windows → bod 2u v T-125.
- Známé omezení (beze změny, platí i pro logy): když `%LOCALAPPDATA%\LayerPeek` před během neexistovalo, po obnově
  na Windows zůstane prázdná nadřazená složka. Na macOS se to netýká (`~/Library/Caches` existuje vždy).
- Ověřeno na macOS 2026-11-11: `dotnet build` 0 varování, `dotnet test` 9882 prošlo, 0 chyb, 267 přeskočeno (včetně Pester).

---
id: T-196
title: Smoke test – zálohovat a obnovovat i mezipaměť ~/Library/Caches/LayerPeek
status: todo
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
- [ ] `Backup-SmokeProfile` / `Restore-SmokeProfile` zahrnují mezipaměť aplikace; `LayerPeek.Build` zůstává netknutá (Pester testy).
- [ ] Oba případy (složka existovala / neexistovala) mají Pester test.
- [ ] Windows část ověřená nebo přesunutá do T-125.
- [ ] `dotnet build` a `dotnet test` (včetně Pester) bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

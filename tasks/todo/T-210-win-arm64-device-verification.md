---
id: T-210
title: Windows ARM64 – ověření na zařízení ARM64 (aplikace, kamera, instalátor, aktualizace)
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-209]
---

> **Rozhodnutí uživatele 2026-11-12:** LayerPeek se vydává i pro `win-arm64` (série T-207 až T-210, ruší T-130).
> Na Windows x64 nejde ARM64 binárky spustit (Windows na x64 emulaci ARM64 nemá), proto vše, co vyžaduje běh, je
> soustředěné sem. Uživatel zatím zařízení s Windows na ARM64 nemá (stav 2026-11-12, stejně jako 2026-10-21 v T-057).

## Cíl
Ověřit na skutečném Windows ARM64 (nebo náhradě podle otázky 1), že AOT build `win-arm64` z T-209 funguje stejně jako
`win-x64`, a podle výsledku povolit nahrávání `win-arm64` (`Uploadable = $true`, viz T-209 otázka 1).

## Rozsah
Na ARM64 zařízení, s balíčkem z `release-build.ps1` (T-209) a se simulátorem (`tools/PrusaSimulator`, běží na x64
stroji v síti nebo na zařízení):
1. **AOT smoke:** `pwsh scripts/aot-smoke.ps1 -PublishDir <publish win-arm64>` projde (na ARM64 hostiteli se spustí).
2. **Aplikace:** tray ikona, menu, okno nastavení, diagnostika, okno licencí (FFmpeg ARM64, T-208), jazyk, jump list,
   jediná instance, upozornění (toast, AUMID `LayerPeek`), Správce pověření, proxy webu PrusaLink.
3. **Kamera (T-208):** testy `[FFmpegFact]` projdou nativně na ARM64; živý stream ze simulátoru (případně Buddy3D),
   nahrávání MP4, timelapse a export JPEG; hardwarové dekódování D3D11VA (T-206) – funguje, nebo spadne zpět na
   software (zapsat GPU a ovladač).
4. **Instalátor a aktualizace (Velopack):** `LayerPeek-X.Y.Z-win-arm64-Setup.exe` nainstaluje bez práv správce,
   zástupce ve Start menu s AUMID, autostart, odinstalace beze zbytku; aktualizace N → N+1 z kanálu `win-arm64`
   (lokální feed nebo zkušební vydání podle `docs/release.md`); instalace ARM64 přes existující x64 instalaci podle
   T-209 otázky 2.
5. **winget** (až bude `arm64` v manifestu, T-059 / T-209): `winget install --manifest` na ARM64 vybere uzel `arm64`.
6. **E2E na zařízení (volitelné):** `tools/run-e2e.ps1` a `tools/windows-verify.ps1` s RID hostitele (T-209 bod 7);
   výsledek zapsat.
7. Výsledek zapsat do poznámek; nalezené chyby = nové úkoly. Po úspěchu přepnout `win-arm64` na `Uploadable = $true`
   (malá změna v `ReleaseTools.psm1` + test), upravit README / příručky, pokud T-209 použil označení „neověřeno“.

## Akceptační kritéria
- [ ] AOT smoke test `win-arm64` prošel na ARM64 (zařízení, Windows verze a CPU v poznámkách)
- [ ] Aplikace a upozornění fungují (body 2)
- [ ] Testy FFmpeg prošly nativně na ARM64 a živý stream, nahrávání a timelapse fungují; stav D3D11VA zapsaný
- [ ] `Setup.exe` `win-arm64` nainstaluje, aktualizuje a odinstaluje aplikaci (bod 4)
- [ ] `win-arm64` povolený k nahrání (`Uploadable`), dokumentace podle výsledku
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Na čem ověřovat (blokuje celý úkol):** uživatel ARM64 zařízení nemá. Možnosti:
   (a) vlastní / zapůjčené zařízení se Snapdragon X apod. (plné ověření včetně GPU a D3D11VA);
   (b) placený cloudový Windows 11 ARM64 stroj (např. Azure ARM64 VM) – odporuje zásadě vydání za 0 Kč, bez skutečné GPU;
   (c) běžec GitHub Actions `windows-11-arm`, zdarma pro veřejné repozitáře
       (https://github.blog/changelog/2025-04-14-windows-arm64-hosted-runners-now-available-in-public-preview/,
       https://blogs.windows.com/windowsdeveloper/2025/04/14/github-actions-now-supports-windows-on-arm-runners-for-all-public-repos/)
       – jen ručně spouštěný workflow pro AOT smoke a testy (bez interaktivního UI a bez GPU); CI bylo ale zamítnuto
       (T-016, 2026-11-01), takže by šlo o nové rozhodnutí;
   (d) emulace celého Windows 11 ARM64 v QEMU na x64 – velmi pomalé, bez GPU, licence Windows; nedoporučuje se.
   **Doporučení:** (a) pro plné ověření; pokud zařízení nebude brzy, (c) jako minimální automatická kontrola, že AOT
   exe na ARM64 vůbec startuje a testy FFmpeg projdou – vyžaduje výjimku z T-016.
2. **Do té doby:** zůstává úkol v `todo/` (žádné přesouvání do T-125, protože T-125 se dělá na x64 stroji), nebo se
   z něj stane sběrný úkol „Windows ARM64“, kam budou další úkoly přesouvat body vyžadující ARM64? **Doporučení:**
   sběrný úkol pro ARM64 (stejný formát jako T-125), doplnit do pravidel v `tasks/README.md`.

## Poznámky k implementaci
(vyplní agent po dokončení)

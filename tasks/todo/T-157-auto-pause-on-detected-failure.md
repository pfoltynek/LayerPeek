---
id: T-157
title: Automatické pozastavení tisku po detekci chyby (volitelné, PrusaLink)
status: todo
owner: core-dev + ui-dev
priority: P3
platform: all
depends_on: [T-154, T-155]
---

## Cíl
Ve fázi 1 detekce chyb jen upozorňuje (T-153 / T-154). Tento úkol přidá **volitelné** (opt-in) automatické pozastavení tisku přes PrusaLink, když detektor ohlásí chybu. Jde o **první zápisovou operaci** LayerPeeku vůči tiskárně (klient PrusaLink dnes jen čte), proto je oddělená a ve výchozím stavu vypnutá.

## Výchozí stav (ověřeno 2026-11-03)
- Specifikace PrusaLink (`spec/openapi.yaml` v https://github.com/prusa3d/Prusa-Link-Web/blob/master/spec/openapi.yaml) obsahuje `PUT /api/v1/job/{id}/pause` (odpovědi 204, 401, 404, 409), `PUT /api/v1/job/{id}/resume` a `DELETE /api/v1/job/{id}` (zastavení). Kdy firmware vrací 409, specifikace neuvádí – ověřit ve firmwaru Buddy (`lib/WUI/link_content/`, https://github.com/prusa3d/Prusa-Firmware-Buddy) a na skutečné tiskárně / v simulátoru.
- Id úlohy už klient zná (`PrintJob.Id` z `GET /api/v1/job`).

## Rozsah
- **core-dev:** zápisové volání v `LayerPeek.PrusaLink` (`PauseJobAsync(jobId)`) za novou abstrakcí v Core (např. `IPrinterJobControl`, aby Core neznal HTTP), Digest autentizace jako u čtení, srozumitelné chyby (409 = úloha neběží / jiný stav). Dokumentace v `docs/api/prusalink.md`.
  - Pozastavuje se jen úloha, u které detektor poplach vyhlásil (stejné `job.id`), jen ve stavu `Printing`, nejvýš jednou na poplach.
  - Simulátor (`tools/PrusaSimulator`) podporuje `PUT /api/v1/job/{id}/pause` (test bez skutečné tiskárny).
- **ui-dev:** volba v nastavení detektorů (T-154), výchozí vypnuto, s jasným varováním o falešných poplaších; notifikace „tisk pozastaven kvůli …“ s možností pokračovat (otázka 3).
- Lokální proxy webu PrusaLink (T-051, T-107) se nemění: zápis jde přímo z klienta aplikace, ne přes proxy prohlížeče.

## Akceptační kritéria
- [ ] Poplach se zapnutou volbou pozastaví tisk v simulátoru (test); s vypnutou volbou ne (test).
- [ ] Pozastaví se jen úloha s id z poplachu; jiná / skončená úloha se nepozastaví (test).
- [ ] Chyby PrusaLink (401, 404, 409, timeout) skončí srozumitelnou notifikací, bez opakování donekonečna (test).
- [ ] Ověřeno na skutečné tiskárně uživatele (→ případně přesunout do T-125 / T-127).
- [ ] `docs/api/prusalink.md` popisuje zápisové volání s odkazy na zdroje.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
1. **Chce uživatel zápisové operace vůči tiskárně vůbec?** (Dosud LayerPeek jen čte; zápis mění bezpečnostní profil – heslo PrusaLink pak umožňuje i ovládání.)
2. **Pozastavit, nebo zastavit** tisk? (Návrh: jen pozastavit, aby šel tisk po kontrole obnovit.)
3. **Pokračování z notifikace:** má notifikace nabídnout „Pokračovat v tisku“ (`resume`), nebo jen otevřít web PrusaLink / okno kamery?
4. **Prodleva před pozastavením:** pozastavit hned, nebo po N sekundách s možností zrušit z notifikace?

## Poznámky k implementaci
(vyplní agent po dokončení)

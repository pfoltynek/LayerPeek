---
id: T-250
title: Znovu posoudit napojení na Prusa Connect (cloud i Prusa Connect Local) – průzkum a návrh
status: todo
owner: architect
priority: P2
platform: all
depends_on: [T-170]
---

## Cíl
Podnět uživatele 2026-11-22: znovu zvážit podporu Prusa Connect jako dalšího zdroje dat vedle PrusaLink, a to dvěma
cestami:
1. **Prusa Connect (cloud)**: nově zveřejněná dokumentace API
   - Connect mobile API: https://connect-mobile-api.prusa3d.com/api/docs#/
   - Camera service: https://camera-service.prusa3d.com/docs/
2. **Prusa Connect Local**: lokálně nasazený server Prusa Connect. Jeho REST API má dokumentaci na `/docs` a OpenAPI
   specifikaci na `/openapi.json` běžícího serveru. Aktuální verze je beta a API se může měnit. Podle uživatele by
   lokální nasazení mělo už být k dispozici.

Podpora Connectu skončila v T-170 (2026-11-05), protože podle T-004 neexistovalo veřejné API (`docs/api/prusa-connect.md`).
Tento úkol posoudí, jestli se to změnilo. Je to **průzkum a návrh, ne implementace**. Výstupem je podklad pro rozhodnutí
uživatele a případné navazující implementační úkoly.

## Rozsah
- Cloud: projít obě dokumentace. Zjistit, jestli jde o API určené i pro aplikace třetích stran (podmínky použití,
  stabilita, verzování). Dále způsob ověření (OAuth / token / přihlášení účtem Prusa, obnova tokenu, kde token uložit
  podle pravidel pro tajné údaje). Dostupná data proti tomu, co dnes dává PrusaLink (stav, průběh, teploty, úloha,
  ovládání pause/resume/stop, soubory). Dále kamera (jen snímky, nebo i stream?), limity a frekvence dotazování
  a případná push/websocket upozornění.
- Prusa Connect Local: zjistit, co to je a jak se nasazuje (Docker? požadavky, licence), jestli je dostupná instalace
  a jak ji spustit pro vývoj a testy. Stáhnout `/openapi.json` z běžícího serveru a uložit ho jako fixture/podklad.
  Porovnat s cloudovým API (stejné API, jiná base URL?) a vysvětlit, co znamená beta pro údržbu.
- Architektura: jak by Connect zapadl do `IPrinterClient` / `ConnectionType` (T-170 nechal `ConnectionType` pro budoucí
  zdroj). Nový projekt `LayerPeek.PrusaConnect`? Dopad na nastavení (přidání tiskárny z účtu), na kameru, detekci
  a timelapse. Native AOT (generování klienta z OpenAPI jen ve formě kompatibilní s AOT, bez reflexe) a licence
  případných knihoven (GPL-3.0 kompatibilní).
- Testovatelnost: fixtures z dokumentace / OpenAPI, simulátor (`tools/PrusaSimulator`) – rozšířit o Connect?
  Bez skutečné sítě v testech.
- Výstup: aktualizovat `docs/api/prusa-connect.md` (nový stav, odkazy, zjištění) a navrhnout rozdělení na
  implementační úkoly (do `## Otevřené otázky`, ne vytvářet bez souhlasu uživatele).

## Akceptační kritéria
- [ ] Zjištění o cloudovém API (oprávnění k použití, ověření, data, kamera, limity) zapsaná v `docs/api/prusa-connect.md`
- [ ] Zjištění o Prusa Connect Local (nasazení, dostupnost, OpenAPI spec uložená, rozdíly proti cloudu, beta) zapsaná
- [ ] Návrh zapojení do architektury vč. AOT, licencí a ukládání tajných údajů
- [ ] Seznam otázek k rozhodnutí uživatele a návrh navazujících úkolů

## Otevřené otázky
- Má podpora zahrnovat obě cesty (cloud i Local), nebo jen jednu z nich? Rozhodne uživatel po průzkumu.
- Kde je dostupné lokální nasazení Prusa Connect Local (adresa / instalační balík) pro průzkum? Doplní uživatel.

## Poznámky k implementaci
(vyplní agent po dokončení)

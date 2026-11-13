---
id: T-207
title: Kamera – dekodér si pamatuje poslední SPS/PPS pro nově otevřený kontext kodeku
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: [T-205]
---

## Cíl
Navazuje na T-205 (otázka 1, rozhodnutí uživatele 2026-11-13). Dekodér od T-203 otevírá nový kontext kodeku na klíčovém
snímku při přepnutí volby „Hardwarové dekódování“, při návratu k softwaru a po skončení hardwarové relace. Nový kontext
dostane SPS a PPS jen z `sprop-parameter-sets` v SDP (extradata) nebo z proudu před IDR snímkem. Kamera, která je pošle
jen jednou na začátku spojení a nemá je v SDP, by po přepnutí ukazovala stojící obraz až do nového spojení. Buddy3D
posílá obojí, takže se jí to netýká – jde o pojistku pro jiné kamery.

## Rozsah
- `FFmpegDecoder` si pamatuje poslední SPS a PPS viděné v paketech (Annex B, NAL typ 7 a 8) a předá je novému kontextu
  kodeku jako extradata, pokud je SDP neobsahuje.
- Výkon: kontrola NAL typu jen u klíčových snímků / paketů s parametry, bez alokací na každý snímek (uložené pole se
  přepíše jen při změně obsahu); žádná změna u Buddy3D.
- Bezpečnost: limit velikosti uložených parametrů (data z nedůvěryhodné sítě, T-050).

## Akceptační kritéria
- [ ] Proud s SPS/PPS jen na začátku spojení a bez `sprop-parameter-sets`: po přepnutí volby i po návratu k softwaru
      obraz pokračuje od dalšího klíčového snímku (test se simulátorem nebo syntetickým proudem; ověřit, že bez opravy
      selže).
- [ ] Buddy3D / simulátor s SPS/PPS před každým IDR beze změny (stávající testy); test alokací ve snímkové cestě 0 B.
- [ ] Limit velikosti uložených parametrů s testem.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(zatím žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

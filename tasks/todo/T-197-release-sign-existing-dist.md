---
id: T-197
title: Vydání – jen podepsat hotový `dist/` (opakování podpisu feedu bez nového buildu)
status: todo
owner: qa-reviewer
priority: P3
platform: all
depends_on: [T-181]
---

## Cíl
Rozhodnutí uživatele 2026-11-11 (otázka 26 v T-181): když podpis feedu na konci `release-build.ps1` selže (médium
odpojené a `abort`, neinteraktivní relace, 3× špatné heslo, jiný klíč na cestě), balíčky v `dist/` zůstanou bez podpisu,
`build-info.json` nevznikne a celý build (~20 min: build, testy, AOT publish, smoke test, balení) se musí spustit znovu.
Samostatný režim „jen podepsat hotový `dist/`“ doplní podpis k už sestaveným souborům, **aniž by oslabil záruky
T-181**: podepsat jde jen `dist/`, který vznikl z čistého checkoutu otagovaného commitu a od buildu se nezměnil.

## Rozsah
- Nový režim podpisu (např. přepínač `release-build.ps1 -SignOnly` nebo samostatný skript `scripts/release-sign.ps1`;
  tvar navrhnout a potvrdit s uživatelem) pro `artifacts/release/<version>/dist/` této verze a tohoto OS.
- **Původ `dist/` (povinné):** režim podepíše jen `dist/`, který:
  - vznikl z **čistého checkoutu otagovaného commitu vydání** (`vX.Y.Z`, `<Version>` z `Directory.Build.props`) – stejná
    kontrola jako u `release-upload.ps1` (commit, čistý pracovní strom); aktuální checkout musí být tentýž commit, čistý;
  - je **beze změny od buildu**: každý soubor v `dist/` (a žádný navíc ani chybějící) odpovídá záznamu, který build zapsal
    po zabalení a před podpisem (seznam souborů se SHA-256 a velikostí, commit, verze, RID; např. dočasný
    `build-info.json` se stavem podpisu `pending`, nebo `SHA256SUMS-<os>.txt` bez `.sig` plus záznam commitu). Kde a v jakém
    tvaru build tento záznam zapíše, navrhnout; záznam musí vzniknout ve stejném běhu buildu jako balíčky.
  - Při jakékoli neshodě (jiný commit, nečistý strom, jiný / chybějící / přebývající soubor, jiný hash, záznam chybí nebo
    je z jiné verze) režim nic nepodepíše a skončí chybou s jasnou hláškou; build se pak musí spustit znovu.
- **Stejné kontroly klíče jako `release-build.ps1`** (T-181, `docs/release.md`, „Podpis feedu aktualizací“): klíč
  z `-FeedSigningKey` / `LAYERPEEK_FEED_SIGNING_KEY`, jen šifrovaný PEM PKCS#8 (nešifrovaný odmítnout),
  `UpdateFeedKeys.Production` není prázdný, heslo jen z výzvy nebo `SecureString` (3 pokusy z výzvy, rozhodnutí 2026-11-11,
  otázka 25 v T-181), veřejný klíč v seznamu, podpis `releases.<RID>.json.sig` každého feedu, ověření zpětným čtením proti
  seznamu, okamžité zahození klíče a hesla. Sdílet kód s `Invoke-FeedSigning` / `Confirm-FeedSigningKey` (žádná druhá
  implementace). Soukromý klíč ani heslo se nikdy neobjeví ve výpisu, logu ani v `artifacts/`.
- Po podpisu vznikne `SHA256SUMS-<os>.txt` (s `.sig`) a `build-info.json` se stavem `signed` stejně jako po úspěšném
  buildu, takže `release-upload.ps1` a `release-publish.ps1` se nemění. Podepsaný `dist/` se znovu nepodepisuje
  (odmítnout, nebo jen ověřit – navrhnout).
- Dokumentace: `docs/release.md` (nový krok v tabulce „Co odmítne při podpisu na konci“ a v řešení problémů: „podpis
  selhal → jen podepsat“).
- Pester testy v dočasném repozitáři (jako testy `release-build.ps1` v T-181): úspěšný podpis hotového `dist/`; odmítnutí
  změněného, přidaného a chybějícího souboru, jiného commitu, nečistého stromu, chybějícího záznamu buildu, už
  podepsaného `dist/`, nedůvěryhodného / nešifrovaného klíče, prázdného seznamu a špatného hesla; žádné tajemství ve
  výpisech ani souborech; výsledek přijme `release-upload.ps1 -DryRun`.

## Akceptační kritéria
- [ ] Režim podepíše jen `dist/` z čistého checkoutu otagovaného commitu, beze změny od buildu (ověřeno proti záznamu
      buildu se SHA-256); jinak nic nepodepíše a skončí chybou (Pester testy)
- [ ] Stejné kontroly klíče a hesla jako `release-build.ps1`, sdílený kód; žádné tajemství ve výpisu ani souborech (Pester)
- [ ] `release-upload.ps1` přijme výsledek beze změny (`-DryRun`, falešné `gh`)
- [ ] `docs/release.md` popisuje, kdy a jak režim použít a co odmítne
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Tvar režimu (přepínač `release-build.ps1` vs. samostatný skript) a záznam buildu před podpisem – navrhnout a potvrdit
  s uživatelem před implementací.

## Poznámky k implementaci
(vyplní agent po dokončení)

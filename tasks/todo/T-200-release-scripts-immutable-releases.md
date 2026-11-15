---
id: T-200
title: Skripty vydání pro neměnná vydání (odmítnout `-AddToPublished` a `-AllowIncomplete`, výpis bez doplnění druhého OS)
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: [T-180]
---

## Cíl
Od 1.0.0 jsou v repozitáři zapnutá **neměnná vydání** (immutable releases, T-180): soubory zveřejněného vydání už
nejde změnit ani doplnit. Vydání se proto zveřejňuje jen se soubory obou OS v konceptu (`docs/release.md`, „Vydání od
1.0.0: oba OS do konceptu, pak zveřejnit“). Tok T-113 „jeden OS zveřejní s `-AllowIncomplete`, druhý doplní přes
`-AddToPublished`“ s neměnnými vydáními nefunguje (doplnění by GitHub odmítl až uprostřed nahrávání), skripty ho ale
dál nabízejí. Tento úkol je upraví tak, aby ho odmítly a nenabízely.

Část T-180 pro `qa-reviewer` (T-180, Rozsah: „skripty vydání musí `-AddToPublished` odmítnout (nebo ho odstranit)
a zveřejnění neúplného vydání zakázat“), oddělená do samostatného úkolu.

**Kdy:** rozhodnutí uživatele 2026-11-11 (T-198 otázka 4): **až po vydání 1.0.0**. 1.0.0 se vydá s dnešními skripty
podle postupu „oba OS do konceptu“, přepínače se jen nepoužijí. Úkol se neimplementuje před zveřejněním 1.0.0, i když
by T-180 byl hotový dřív.

## Rozsah
- `scripts/release-upload.ps1`: `-AddToPublished` odmítnout (nebo odstranit, otázka 1) se zprávou, která odkáže na
  postup „oba OS do konceptu“. Hláška u už zveřejněného vydání (`Release vX.Y.Z is already published …`) přestane
  radit `-AddToPublished`. Závěrečná věta po nahrání do konceptu (`… or publish with -AllowIncomplete and add …`) jen
  „nahraj druhý OS do konceptu, pak zveřejni“.
- `scripts/release-publish.ps1`: `-AllowIncomplete` odmítnout (nebo odstranit, otázka 1). Kontrola úplnosti (obě
  `SHA256SUMS-<os>.txt`, feed a `.sig` každého RID) zůstává beze změny.
- `scripts/release.ps1` / `ReleaseTools.psm1`: výpis dalších kroků na konci (dnes `release-publish.ps1
  -AllowIncomplete` a `release-upload.ps1 -AddToPublished`, ř. ~3210–3246) a plán při běhu na druhém OS po zveřejnění
  (`It has no SHA256SUMS-<os>.txt yet …`) jen s tokem „druhý OS do konceptu, pak zveřejnit“. Zveřejněné vydání bez
  souborů tohoto OS = jasná chyba (nejde doplnit, verze propadá, vydat další).
- Winget (`ReleaseTools.psm1`, `Get-WingetReleaseProblems`, `Get-WingetOffer` a hlášky „Add the Windows files
  first … -AddToPublished“, ř. ~3872 a ~4047): bez rady `-AddToPublished`. Nabídka winget po `release-upload.ps1 -AddToPublished` odpadá s ním.
- Mrtvý kód podle odpovědi na otázku 1 (`Get-AddToPublishedProblems`, `Get-PublishProblems -Published`, pořadí
  nahrání do zveřejněného vydání) odstranit, nebo ponechat jen pro odmítnutí.
- Pester testy (`ReleaseTools.Tests.ps1`, `ReleaseOrchestrator.Tests.ps1`, `ReleaseWinget.Tests.ps1`): testy
  doplnění druhého OS (T-113) nahradit testy odmítnutí; výpis `release.ps1` bez obou přepínačů.
- `docs/release.md`: oddíl „Vydání na dvou počítačích (OS 1 zveřejní, OS 2 doplní, T-113)“ odstranit nebo zkrátit na
  historickou poznámku, tabulky „Co skripty odmítnou“ a „Opakování po chybě“, úvod, „Zkušební vydání“ (zkouška
  doplnění druhého OS), „Publikace do winget“ (zmínky o `-AddToPublished`) a poznámka v „Vydání od 1.0.0“ (výpis
  `release.ps1` už nic z toho nenabízí). `docs/prerequisites.md` („winget“ zmiňuje `-AddToPublished`).
- Sběrné úkoly: bod T-125 oddíl 4 (T-055, doplnění Windows `-AddToPublished`) a T-127 oddíl 3 (T-055, doplnění
  druhého OS) uzavřít jako zrušené („→ zrušeno (T-200) …“, stejně jako zrušené body T-130).

## Akceptační kritéria
- [ ] `release-upload.ps1 -AddToPublished` a `release-publish.ps1 -AllowIncomplete` skončí s kódem 1 a zprávou
      s postupem „oba OS do konceptu“ (nebo přepínače neexistují, podle otázky 1); nic se nenahraje ani nezveřejní
      (Pester test proti falešnému `gh`)
- [ ] Žádný výpis skriptů (`release.ps1` na konci a v plánu, `release-upload.ps1`, `release-publish.ps1`, winget)
      nenabízí `-AllowIncomplete` ani `-AddToPublished` (Pester test)
- [ ] Běžný tok beze změny: první OS do konceptu, druhý OS do konceptu, `release-publish.ps1` zveřejní jen úplné
      vydání (stávající testy procházejí)
- [ ] `docs/release.md` a `docs/prerequisites.md` bez toku T-113 jako možnosti; T-125 / T-127 body doplnění druhého
      OS uzavřené jako zrušené
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (Pester testy běží v `LayerPeek.Scripts.Tests`)

## Otevřené otázky
1. **Odmítnout, nebo odstranit?** Přepínače `-AddToPublished` / `-AllowIncomplete` (a) ponechat a vždy odmítnout se
   zprávou (starý příkaz z historie terminálu dostane vysvětlení), nebo (b) odstranit i s kódem (PowerShell pak
   vypíše jen „A parameter cannot be found…“)? T-180 připouští obojí. Rozhodne uživatel.
2. **Zkušební vydání (`-Repo`):** platí odmítnutí i pro testovací repozitář, kde neměnná vydání zapnutá být nemusí?
   Návrh: ano, jeden tok pro všechny repozitáře. Rozhodne uživatel.
3. **Zjišťovat stav neměnných vydání z GitHubu?** Skripty by mohly číst nastavení repozitáře (API pro immutable
   releases) a odmítat jen tehdy, když je zapnuté. Přidá síťový dotaz a závislost na API, které je potřeba ověřit
   z primárního zdroje (https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/immutable-releases).
   Návrh: nezjišťovat, odmítat vždy. Rozhodne uživatel.

Rozhodnuto uživatelem 2026-11-12:
1. **Ponechat a odmítnout:** přepínače zůstanou, skript skončí s kódem 1 a zprávou s postupem „oba OS do konceptu, pak
   zveřejnit“; mrtvý kód doplňování do zveřejněného vydání se odstraní.
2. **Odmítnutí platí všude**, i pro zkušební vydání do testovacího repozitáře (`-Repo`) – jeden tok pro všechny.
3. **Stav neměnných vydání se z GitHubu nezjišťuje**, odmítá se vždy (žádný síťový dotaz ani závislost na API).

## Poznámky k implementaci
(vyplní agent po dokončení)

Založeno 2026-11-11 (architekt, T-198 otázka 4): rozhodnutí uživatele „neměnná vydání zapnout před 1.0.0, změny skriptů
z T-180 až po 1.0.0“.

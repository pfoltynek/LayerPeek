---
id: T-200
title: Skripty vydání pro neměnná vydání (odmítnout `-AddToPublished` a `-AllowIncomplete`, výpis bez doplnění druhého OS)
status: done
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
- [x] `release-upload.ps1 -AddToPublished` a `release-publish.ps1 -AllowIncomplete` skončí s kódem 1 a zprávou
      s postupem „oba OS do konceptu“ (nebo přepínače neexistují, podle otázky 1); nic se nenahraje ani nezveřejní
      (Pester test proti falešnému `gh`)
- [x] Žádný výpis skriptů (`release.ps1` na konci a v plánu, `release-upload.ps1`, `release-publish.ps1`, winget)
      nenabízí `-AllowIncomplete` ani `-AddToPublished` (Pester test)
- [x] Běžný tok beze změny: první OS do konceptu, druhý OS do konceptu, `release-publish.ps1` zveřejní jen úplné
      vydání (stávající testy procházejí)
- [x] `docs/release.md` a `docs/prerequisites.md` bez toku T-113 jako možnosti; T-125 / T-127 body doplnění druhého
      OS uzavřené jako zrušené
- [x] `release.ps1 -Version X.Y.Z` na druhém počítači (tag už na originu) stáhne jen tag, Gitflow přeskočí, nahraje
      do konceptu a nabídne zveřejnění; na prvním počítači skončí výpisem dalšího kroku pro druhý OS (Pester, dva klony,
      obě pořadí OS)
- [x] Koncept vzniká hned po pushi tagu; víc vydání pro jeden tag odmítnou `release.ps1`, upload i publish (Pester)
- [x] Zveřejněné vydání bez souborů tohoto OS: `release.ps1` skončí s kódem 1 a návodem „verze propadá, vydej další“
- [x] `dotnet build` a `dotnet test` bez chyb a varování (Pester testy běží v `LayerPeek.Scripts.Tests`)

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

Rozhodnuto uživatelem 2026-11-22 (rozšíření rozsahu: vydání ze dvou počítačů, kterýkoli může být první, robustní,
plně návodné i pro agenty, testované):
4. **Stejný příkaz na obou počítačích:** `pwsh scripts/release.ps1 -Version X.Y.Z` spouští Windows i Mac, role se pozná
   ze stavu (tag na originu, obsah konceptu). Druhý počítač si v ostrém běhu stáhne **jen tag `vX.Y.Z`** (nikdy
   nepřepíše existující lokální tag), Gitflow kroky hotové na originu přeskočí, sestaví build tagu a nahraje ho do
   konceptu. Mění rozhodnutí 2026-10-24 (ostrý běh nestahuje tagy) jen pro tento jeden tag.
5. **Zveřejnění nabídne `release.ps1`:** běh, po kterém má koncept soubory obou OS, spustí `release-publish.ps1 -DryRun`
   a zeptá se na zveřejnění (potvrzení napsáním verze, neměnné vydání); na Windows pak nabídne winget. Odmítnutí =
   vypíše přesný příkaz na později. Mění rozhodnutí 2026-10-24 („release.ps1 nikdy nezveřejňuje“).

## Rozsah – rozšíření 2026-11-22 (otázky 4 a 5)
- `release.ps1` / `Get-ReleaseFlowPlan`: Gitflow kroky hotové na originu (tag na originu, `origin/main` ho obsahuje,
  `origin/develop` obsahuje commit vydání) jsou hotové i na druhém počítači (bez posunu lokálního `main` / `develop`).
- Ostrý běh stáhne tag `vX.Y.Z` z originu, když tam je a lokálně chybí (`git fetch --no-tags <remote>
  refs/tags/vX.Y.Z:refs/tags/vX.Y.Z`, bez `+`).
- Nový krok **koncept hned po pushi tagu** (prázdný koncept s textem z `CHANGELOG.md` tagu, ještě před buildem), aby
  druhý počítač, spuštěný souběžně, koncept vždy našel a nevznikly dva koncepty. Kontrola duplicit: víc vydání pro
  jeden tag (`gh release list`) odmítne `release.ps1`, `release-upload.ps1` i `release-publish.ps1` s návodem.
- Nový krok **zveřejnění** (potvrzení napsáním verze), jen když má koncept soubory obou OS; jinak výpis „DALŠÍ KROK na
  počítači s <druhý OS>“ s přesnými příkazy (klíč, `caffeinate` na Macu).
- Plán ukazuje stav konceptu po OS (soubory Windows / macOS nahrané / chybí). Zveřejněné vydání bez souborů tohoto OS
  = chyba (neměnné, verze propadá, vydat další verzi).
- Pester test celého toku na dvou klonech („Windows“ a „Mac“) proti falešnému GitHubu, v obou pořadích OS (testovací
  přepínač OS jen s `LAYERPEEK_PESTER_TESTS_TEMP`, jako `LAYERPEEK_RELEASE_TEST_SKIP_TOOLCHAIN`).

## Poznámky k implementaci
- **Tok na dvou počítačích:** `Get-ReleaseFlowPlan` má 10 kroků (nově 4 „koncept“ hned po pushi tagu a 10
  „zveřejnění“). Gitflow hotový na `origin` je hotový i na druhém počítači (lokální `main`/`develop` se nemění).
  Ostrý běh stáhne jen chybějící tag `vX.Y.Z` (bez `+`). Zveřejnění čeká (`[wait]`), dokud koncept nemá
  `SHA256SUMS-<os>.txt` obou OS (`Get-ReleaseOsStatus`); pak `release-publish.ps1 -DryRun`, napsání verze,
  `release-publish.ps1`.
- **Bezpečnost souběhu (z revize qa-reviewer):** druhý počítač nikdy neslučuje `develop` za první. Dokud první
  nepushne `develop`, plán odmítne s návodem „dokonči běh na druhém počítači“, a hláška „druhý počítač může začít“ se
  vypíše až po pushi `develop`. Dva koncepty jednoho tagu (`gh release list`, `Get-DuplicateReleaseProblems`) odmítnou
  `release.ps1`, upload i publish; kontrolu dělá i `New-ReleaseDraft` hned po vytvoření. Souběžný start obou počítačů
  (konflikt tagu) má v hlášce postup úklidu.
- **Robustnost:** koncept, který už má soubory tohoto OS, se znovu nestaví ani nenahrává (poznámka v plánu), takže
  zveřejnit jde na kterémkoli počítači i bez místního buildu. Každý konec běhu (úspěch, čekání, odmítnutí, problém
  v plánu, chyba) vypíše řádek `NEXT STEP:` s přesným příkazem a počítačem (`Get-ReleaseNextSteps`,
  `Get-ReleaseRunCommand`, na Macu s `caffeinate -d -i -u`). Hlášky jsou ASCII (výstup podřízeného `pwsh` se
  zachytává).
- **Odmítnuté přepínače:** `Get-RetiredSwitchProblem` odmítne `-AddToPublished` a `-AllowIncomplete` před jakoukoli
  kontrolou a bez volání `gh`. `Get-AddToPublishedProblems` a `Get-PublishProblems -Published` jsou odstraněné, winget
  hlášky jsou bez `-AddToPublished`.
- **Testy:** `ReleaseOrchestrator.Tests.ps1`: falešný `gh` umí `release list` (a duplicitní koncept). Nový celý tok
  na dvou klonech jednoho `origin` s přepínačem `LAYERPEEK_RELEASE_TEST_HOST_OS` (`Get-HostOs`, platí jen pro modul
  uvnitř `LAYERPEEK_PESTER_TESTS_TEMP`, `Test-ReleaseTestPath`). Pokrývá obě pořadí OS, zveřejnění druhým i prvním
  počítačem, druhý počítač nahrávající dřív, čekání na `develop` prvního a dva koncepty. Čisté testy jsou
  v `ReleaseTools.Tests.ps1`. Pester: orchestrátor a ReleaseTools 476/0, winget prošel.
- **Dokumentace:** `docs/release.md` má nový oddíl „Vydání ze dvou počítačů (Windows a Mac, T-200)“ s tabulkou rolí,
  postupem na obou OS, tabulkou bloků `NEXT STEP` a řešením problémů. Tok T-113 je zkrácený na historickou poznámku.
  Upravené jsou i `prerequisites.md` (`gh release list --json`, ověřeno s gh 2.105.0), `macos-manual-testing.md`,
  README EN/CZ a CHANGELOG `[Unreleased]`. Body T-125 a T-127 jsou označené jako zrušené (T-200).
- Ostré ověření na skutečném GitHubu proběhne při příštím vydání (1.2.0 nebo jiné) na obou počítačích.

Založeno 2026-11-11 (architekt, T-198 otázka 4): rozhodnutí uživatele „neměnná vydání zapnout před 1.0.0, změny skriptů
z T-180 až po 1.0.0“.

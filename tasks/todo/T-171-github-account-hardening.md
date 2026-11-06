---
id: T-171
title: Zabezpečení účtu a repozitáře na GitHubu (2FA klíčem, pravidla tagů a větví, krátkodobý token pro vydání)
status: todo
owner: user
priority: P1
platform: all
depends_on: []
---

## Cíl
Bezpečnostní revize (2026-11-06, problém A): aktualizace přes Velopack ověřují jen hashe balíčků proti feedu
`releases.<RID>.json`, který leží ve stejném vydání na GitHubu. Kdo převezme účet GitHub nebo uniklý token `gh`, může
vydat škodlivou aktualizaci, kterou nainstalované aplikace přijmou. Tento úkol snižuje pravděpodobnost převzetí účtu a
omezuje, co jde s uniklým tokenem udělat. Podpis feedu (ochrana i při převzetí účtu) řeší T-172.

Úkol dělá **uživatel ručně** v nastavení GitHubu (`owner: user`, schváleno 2026-11-06). Agent připraví jen úpravu
`docs/release.md` (viz Rozsah) a případnou úpravu kontrol v `scripts/` – ta část je pro `qa-reviewer`.

## Rozsah

### Účet (uživatel)
- 2FA: přidat passkey / hardwarový klíč FIDO2 (např. YubiKey), ideálně dva (záložní uložený jinde).
- Odebrat SMS jako způsob 2FA (zůstane klíč, případně TOTP aplikace).
- Stáhnout nové záchranné kódy (recovery codes) a uložit je offline (papír / šifrované médium mimo počítač), staré
  zneplatnit.
- Zkontrolovat a odebrat nepoužívané: OAuth aplikace, GitHub Apps, SSH klíče, klasické PAT, aktivní relace.

### Repozitář `pfoltynek/LayerPeek` (uživatel)
- **Ruleset pro tagy `v*`:** zákaz smazání, zákaz přepsání (non-fast-forward / update), vytvoření jen pro vlastníka
  (bypass list = jen vlastník repozitáře, nebo pravidlo „Restrict creations“ s výjimkou pro vlastníka).
- **Ruleset pro `main`:** zákaz force-push a smazání. (Zda i povinný PR / zákaz přímého pushe – viz Otevřené otázky;
  `release.ps1` dnes pushuje sloučení do `main` přímo.)
- **Immutable releases** zapnout v nastavení repozitáře – **pozor na konflikt s T-113**, viz Otevřené otázky.

### Token pro vydání (uživatel + úprava dokumentace)
- Místo trvalého `gh auth login` jemně omezený (fine-grained) PAT:
  - platnost 1–7 dní,
  - jen repozitář `pfoltynek/LayerPeek`,
  - oprávnění `Contents: Read and write` (a `Metadata: Read`, které GitHub přidá povinně), nic dalšího.
- Token se předá jen po dobu běhu skriptů vydání přes `$env:GH_TOKEN` (např. `$env:GH_TOKEN = Read-Host -MaskInput`),
  po vydání `Remove-Item Env:GH_TOKEN`, `gh auth logout` (pokud existuje uložené přihlášení) a token na GitHubu smazat
  nebo nechat vypršet.
- **Git push** (`release.ps1` kroky 3 a 5 pushují `main`, tag a `develop` přes `git`, ne přes `gh`) používá vlastní
  přihlašovací údaje (Git Credential Manager / SSH klíč), které `GH_TOKEN` nepokrývá – i ty musí být pokryté, viz
  Otevřené otázky.
- **Úprava `docs/release.md` je součástí tohoto úkolu** (ne T-172): nový oddíl „Token pro vydání“ s postupem výše,
  úprava řádků o `gh auth status` (kontrola musí projít i s `GH_TOKEN` bez uloženého přihlášení) a odkaz na T-171 v
  „Rozhodnutí“. Ověřit, že `release.ps1`, `release-upload.ps1` a `release-publish.ps1` s tokenem jen v `GH_TOKEN`
  fungují (včetně `-DryRun`); pokud ne, upravit kontrolu `gh auth status` a doplnit Pester test.

## Akceptační kritéria
- [ ] Účet: hardwarový klíč / passkey jako 2FA, SMS odebrané, nové záchranné kódy uložené offline (potvrdí uživatel)
- [ ] Nepoužívané OAuth aplikace, GitHub Apps, SSH klíče a klasické PAT odebrané (potvrdí uživatel)
- [ ] Ruleset tagů `v*` aktivní (stav *Active*, ne *Evaluate*): smazání a přepsání tagu odmítne i vlastník bez
      bypassu; vytvoření tagu `v*` jiným účtem odmítnuto (ověřit aspoň náhledem pravidel; zkušební tag ve zkušebním
      repozitáři podle „Zkušební vydání“ v `docs/release.md`)
- [ ] Ruleset `main` aktivní: `git push --force` a smazání `main` odmítnuté
- [ ] Immutable releases: zapnuto, nebo vědomě odloženo podle odpovědi na otázku 1 (zapsat do Poznámek)
- [ ] Trvalé přihlášení `gh` odstraněné (`gh auth status` bez `GH_TOKEN` hlásí nepřihlášený stav)
- [ ] `docs/release.md` popisuje vytvoření fine-grained PAT (repozitář, oprávnění, platnost), předání přes
      `$env:GH_TOKEN` jen během vydání, úklid po vydání a přihlášení pro `git push` podle odpovědi na otázku 3
- [ ] `release.ps1 -DryRun`, `release-upload.ps1 -DryRun` a `release-publish.ps1 -DryRun` projdou s tokenem jen v
      `GH_TOKEN` (ověří uživatel při příštím vydání; případná úprava skriptů má Pester test)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (jen pokud se mění `scripts/`)

## Otevřené otázky
1. **Immutable releases vs. T-113 (`-AddToPublished`).** Po zveřejnění immutable vydání GitHub nedovolí přidat,
   nahradit ani smazat soubory (HTTP 422 „Cannot upload assets to an immutable release“). Tok „jeden OS zveřejní
   s `-AllowIncomplete`, druhý OS později doplní soubory do zveřejněného vydání“ (T-113, `docs/release.md`, „Vydání na
   dvou počítačích“) by přestal fungovat. Varianty: (a) immutable zapnout a vždy zveřejňovat až s oběma OS v konceptu
   (`-AddToPublished` zrušit nebo zakázat), (b) immutable nezapínat a spolehnout se na ruleset tagů + podpis feedu
   (T-172), (c) jiné (např. samostatná vydání pro každý OS). Rozhodne uživatel.
2. **`main`: povinný PR / zákaz přímého pushe?** `release.ps1` pushuje sloučení přímo; povinný PR by vyžadoval změnu
   toku vydání nebo bypass pro vlastníka. Stačí zákaz force-push a smazání?
3. **Přihlášení pro `git push`.** Fine-grained PAT v `GH_TOKEN` pokrývá jen `gh`. Pro `git push` varianty: (a) stejný
   PAT dočasně přes `gh auth setup-git` / credential helper a po vydání vymazat z Git Credential Manageru, (b) SSH klíč
   na hardwarovém klíči (`ed25519-sk`) nebo s heslem, (c) ponechat dnešní stav GCM. Rozhodne uživatel.
4. **Vytváření tagů `v*`:** jen vlastník přes bypass list, nebo úplný zákaz vytváření kromě bypassu – stejné v praxi,
   jen potvrdit, že jiní spolupracovníci (dnes žádní) tagy vytvářet nesmí.

## Poznámky k implementaci
(vyplní agent po dokončení)

Zdroje (ověřit aktuální stav při implementaci):
- Immutable releases: https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/immutable-releases
- Správa vydání (immutable: soubory po zveřejnění nejde měnit, koncept ano): https://docs.github.com/en/enterprise-server@3.21/repositories/releasing-projects-on-github/managing-releases-in-a-repository
- Rulesets: https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets
- Fine-grained PAT: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
- `gh` a `GH_TOKEN`: https://cli.github.com/manual/gh_help_environment

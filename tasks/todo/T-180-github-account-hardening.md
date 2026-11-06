---
id: T-180
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
omezuje, co jde s uniklým tokenem udělat. Podpis feedu (ochrana i při převzetí účtu) řeší T-181.

Úkol dělá **uživatel ručně** v nastavení GitHubu (`owner: user`, schváleno 2026-11-06). Agent připraví jen úpravu
`docs/release.md` (viz Rozsah) a případnou úpravu kontrol v `scripts/` – ta část je pro `qa-reviewer`.

## Rozsah

### Účet (uživatel)
- 2FA: přidat passkey / hardwarový klíč FIDO2 (např. YubiKey), ideálně dva (záložní uložený jinde).
- Odebrat SMS jako způsob 2FA (zůstane klíč, případně TOTP aplikace).
- Stáhnout nové záchranné kódy (recovery codes) a uložit je offline (papír / šifrované médium mimo počítač), staré
  zneplatnit.
- Zkontrolovat a odebrat nepoužívané: OAuth aplikace, GitHub Apps, SSH klíče, klasické PAT, aktivní relace.

### Repozitář `pfoltynek/LayerPeek` – **rulesety už nastavené (2026-11-06, jiná session na žádost uživatele)**
Aktivní rulesety (zkontrolovat v Settings → Rules a zapsat do Poznámek):
1. „Branches: no force-push or deletion“ – `develop` + `main`: zákaz smazání a non-fast-forward, **bez bypassu** (ani
   vlastník).
2. „Branches: pull request required“ – `develop` + `main`: PR s 1 schválením, schválení posledního pushe, vyřešená
   vlákna, jen merge commit, brána CodeQL code scanning; **bypass = admin repozitáře (vždy)**, takže přímé pushe
   `release.ps1` a lokální `git merge --no-ff` + push fungují dál.
3. „Tags v*: no deletion or update“ – zákaz smazání, non-fast-forward a update, **bez bypassu**.
4. „Tags v*: creation by owner only“ – vytvoření tagu `v*`, bypass = admin repozitáře.

Zapnuto také: secret scanning + push protection, Dependabot alerts + security updates (bez `dependabot.yml`), private
vulnerability reporting, CodeQL default setup, Discussions; wiki vypnutá; squash/rebase merge vypnuté; mazání větve po
sloučení zapnuté.
- **Immutable releases** zapnout (rozhodnuto 2026-11-06, varianta a): vydání se zveřejňuje až s oběma OS v konceptu.
  Proto se ruší tok T-113 „jeden OS zveřejní s `-AllowIncomplete`, druhý později přidá soubory přes `-AddToPublished`“:
  skripty vydání (`release-upload.ps1` / `release-publish.ps1`) musí `-AddToPublished` odmítnout (nebo ho odstranit) a
  zveřejnění neúplného vydání zakázat; upravit `docs/release.md` („Vydání na dvou počítačích“) a Pester testy. Tato
  část je pro `qa-reviewer`.

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
- **Úprava `docs/release.md` je součástí tohoto úkolu** (ne T-181): nový oddíl „Token pro vydání“ s postupem výše,
  úprava řádků o `gh auth status` (kontrola musí projít i s `GH_TOKEN` bez uloženého přihlášení) a odkaz na T-180 v
  „Rozhodnutí“. Ověřit, že `release.ps1`, `release-upload.ps1` a `release-publish.ps1` s tokenem jen v `GH_TOKEN`
  fungují (včetně `-DryRun`); pokud ne, upravit kontrolu `gh auth status` a doplnit Pester test.

## Akceptační kritéria
- [ ] Účet: hardwarový klíč / passkey jako 2FA, SMS odebrané, nové záchranné kódy uložené offline (potvrdí uživatel)
- [ ] Nepoužívané OAuth aplikace, GitHub Apps, SSH klíče a klasické PAT odebrané (potvrdí uživatel)
- [ ] Rulesety 1–4 (viz Rozsah) zkontrolované ve stavu *Active* (ne *Evaluate*), stav zapsaný do Poznámek
- [ ] Ruleset PR má výjimku pro admina, přímý push vlastníka do `main` i `develop` funguje (rozhodnuto 2026-11-06, bod 2)
- [ ] Immutable releases zapnuté; `-AddToPublished` a zveřejnění neúplného vydání skripty odmítnou (Pester test),
      `docs/release.md` popisuje zveřejnění až s oběma OS v konceptu
- [ ] Trvalé přihlášení `gh` odstraněné (`gh auth status` bez `GH_TOKEN` hlásí nepřihlášený stav)
- [ ] `docs/release.md` popisuje vytvoření fine-grained PAT (repozitář, oprávnění, platnost), předání přes
      `$env:GH_TOKEN` jen během vydání, úklid po vydání a přihlášení pro `git push` podle odpovědi na otázku 3
- [ ] `release.ps1 -DryRun`, `release-upload.ps1 -DryRun` a `release-publish.ps1 -DryRun` projdou s tokenem jen v
      `GH_TOKEN` (ověří uživatel při příštím vydání; případná úprava skriptů má Pester test)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (jen pokud se mění `scripts/`)

## Otevřené otázky
1. ~~Immutable releases vs. T-113~~ – **rozhodnuto 2026-11-06: (a)** zapnout, zveřejňovat až s oběma OS v konceptu,
   `-AddToPublished` zrušit (viz Rozsah).
2. ~~`main`: povinný PR~~ – **rozhodnuto 2026-11-06:** povinný PR pro `develop` i `main`, **výjimka pro admina
   (vlastníka) zůstává** – vlastník smí pushovat do `main` i `develop` přímo (`release.ps1`, lokální `git merge
   --no-ff` + push); PR platí jen pro jiné účty. Strojový účet ani druhý schvalovatel se nezavádí (uživatel nechce).
   Vědomě přijaté: proti ukradenému tokenu vlastníka PR nechrání – to řeší T-181 a krátkodobý token.
3. **Přihlášení pro `git push`.** Fine-grained PAT v `GH_TOKEN` pokrývá jen `gh`. Pro `git push` varianty: (a) stejný
   PAT dočasně přes `gh auth setup-git` / credential helper a po vydání vymazat z Git Credential Manageru, (b) SSH klíč
   na hardwarovém klíči (`ed25519-sk`) nebo s heslem, (c) ponechat dnešní stav GCM. Rozhodne uživatel.
4. ~~Vytváření tagů `v*`~~ – **rozhodnuto 2026-11-06:** jen vlastník (bypass list), nikdo jiný; nastaveno (ruleset 4).

## Poznámky k implementaci
(vyplní agent po dokončení)

Zdroje (ověřit aktuální stav při implementaci):
- Immutable releases: https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/immutable-releases
- Správa vydání (immutable: soubory po zveřejnění nejde měnit, koncept ano): https://docs.github.com/en/enterprise-server@3.21/repositories/releasing-projects-on-github/managing-releases-in-a-repository
- Rulesets: https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets
- Fine-grained PAT: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens
- `gh` a `GH_TOKEN`: https://cli.github.com/manual/gh_help_environment

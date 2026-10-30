---
id: T-016
title: CI – GitHub Actions (Windows + macOS)
status: todo
owner: qa-reviewer
priority: P3
platform: all
depends_on: []
---

## Cíl
Automatický build a testy při každém push a PR. 

> **Odloženo** (2026-10-15, znovu potvrzeno 2026-10-17): uživatel zatím GitHub Actions nechce. Neimplementovat a při výběru dalšího úkolu přeskočit, dokud to uživatel výslovně nepotvrdí.

> **Náklady (zjištění 2026-10-21):** repozitář je soukromý. GitHub Free dává pro soukromé repozitáře 2 000 minut hostovaných runnerů měsíčně a minuta na macOS runneru se počítá 10× (Windows 2×). Pravidelné běhy s AOT publish na Windows i macOS by limit rychle vyčerpaly. Proto se vydání zatím sestavují lokálně skripty z T-055 a nahrávají do GitHub Releases (soubory vydání jsou zdarma). Při obnovení úkolu ověřit aktuální limity a ceny: https://docs.github.com/en/billing/managing-billing-for-your-products/managing-billing-for-github-actions/about-billing-for-github-actions

## Rozsah
- Matrix `windows-latest`, `macos-latest`
- SDK podle `global.json`
- Native AOT publish pro `win-x64`, `win-arm64` a `osx-arm64` (cílové RID rozhodnuté 2026-10-21) + upload artefaktů. Použít skripty z T-055, neduplikovat jejich logiku ve workflow.
- Cache NuGet, publikace výsledků testů

## Akceptační kritéria
- [ ] Workflow prochází na obou OS
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozbor (příprava 2026-10-29)

Připravil architekt jako podklad pro rozhodnutí uživatele. **Úkol zůstává odložený**, nic z toho není rozhodnuté.

### Aktuální ceny a limity (ověřeno 2026-10-29)

Zdroje: [GitHub's plans](https://docs.github.com/en/get-started/learning-about-github/githubs-plans), [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [Actions runner pricing](https://docs.github.com/en/billing/reference/actions-runner-pricing). Stránky datum poslední změny neuvádějí, obsah byl přečtený 2026-10-29.

- **GitHub Free (osobní účet), soukromé repozitáře:** v ceně je **12 USD výpočetního času měsíčně**. Od roku 2026 je zahrnutá částka „expressed as a monetary amount, not a fixed number of minutes“. Dřívější údaj v banneru nahoře (2 000 minut, násobky ×2 / ×10) tak už neplatí ve stejném tvaru. Bez platební metody se Actions po vyčerpání zastaví, nic se nedoplácí.
- **Ceny za minutu** (každá úloha se zaokrouhluje nahoru na celé minuty): Linux 2 jádra 0,006 USD, Windows 2 jádra 0,010 USD, macOS (M1, 3 jádra) 0,062 USD. **12 USD tedy odpovídá zhruba 2 000 min na Linuxu, 1 200 min na Windows, nebo jen asi 190 min na macOS.**
- **Úložiště:** 500 MB pro artefakty (společně s GitHub Packages), 10 GB cache na repozitář.
- **Veřejné repozitáře:** standardní hostované runnery zdarma a nečerpají zahrnutou částku.
- **Self-hosted runnery:** dokumentace je uvádí jako zdarma. Poplatek 0,002 USD/min, ohlášený v prosinci 2025 od 1. 3. 2026, GitHub odložil na neurčito ([The Register, 2025-12-17](https://www.theregister.com/2025/12/17/github_charge_dev_own_hardware/), [Techzine](https://www.techzine.eu/news/devops/137396/github-bends-to-criticism-and-delays-paid-self-hosting-of-runners/), [GitHub Resources: 2026 pricing changes](https://resources.github.com/actions/2026-pricing-changes-for-github-actions/)). Novější zprávu jsme nenašli, při obnovení úkolu ověřit znovu.

### Odhad minut na jeden běh

**Odhady z lokálních běhů, ne měření na GitHubu:** celý `dotnet test` trvá asi 2 min na Macu s M-series, `release-build.ps1` 6–8 min. Hostované runnery (Windows 2 jádra, macOS M1 3 jádra) jsou pomalejší a navíc instalují .NET 11 rc (`global.json`, `actions/setup-dotnet`), obnovují NuGet a stahují nebo kompilují FFmpeg (macOS asi 0,5 min lokálně, cache mezi běhy). Proto počítáme zhruba s 1,5–2× delší dobou:

| Běh | Windows (min) | macOS (min) | Cena Windows | Cena macOS |
|---|---|---|---|---|
| build + test (push / PR) | 8–12 | 7–10 | 0,08–0,12 USD | 0,43–0,62 USD |
| vydání: build + test + AOT publish + instalátor Velopack / `.app` + `.dmg` (`release-build.ps1`) | 12–18 | 10–15 | 0,12–0,18 USD | 0,62–0,93 USD |

Rizika odhadu: na `windows-latest` je Edge, takže by se mohly spustit E2E testy (Playwright, T-030), které se bez Edge přeskakují. Testy s druhem zásahu do systému (Installer, Startup) jdou jen přes `tools/run-e2e.ps1`, a ty se v CI spouštět nemají. Délku je potřeba po prvním běhu změřit.

Kolik běhů: do `develop` přibylo za posledních 30 dní asi 170 sloučení (`git log --first-parent`). Běhy ale nevznikají z commitů, vznikají z pushů. Uživatel pushuje ručně a agenti nepushují. **Předpoklad: 1–2 pushe `develop` denně, tedy 30–60 běhů měsíčně**, plus 2–4 vydání měsíčně. `concurrency` s `cancel-in-progress` zruší starší běh téže větve.

### Porovnání variant

| Varianta | Měsíčně (odhad) | Plus | Minus |
|---|---|---|---|
| **(a) Jen Windows runner pro build + test** při push do `develop`/`main` a u PR, macOS dál lokálně (`release-build.ps1` na Macu) | 30–60 běhů × ~10 min = **300–600 min Windows ≈ 3–6 USD** (25–50 % z 12 USD) | Vejde se do bezplatné částky i s rezervou. Hlídá build, testy a AOT analyzátory na hlavní platformě. Bez placení a bez vlastního HW. | Na macOS nic (regrese pro macOS chytí až lokální běh na Macu). AOT publish a instalátor se v CI neověřují. |
| **(b) Jen `workflow_dispatch`** (ruční spuštění) pro sestavení vydání na obou OS | 2–4 vydání × (15 Win + 13 macOS) ≈ 30–60 min Win + 26–52 min macOS **≈ 2–4 USD** | Platí se jen za vydání. Sestavení z čistého stroje. | Neověřuje průběžně. Duplikuje dnešní lokální `release.ps1`. Nahrání do konceptu by potřebovalo token v secrets, tedy jiný bezpečnostní model než dnes. Pozor na limit 500 MB artefaktů (jedno vydání má přes 200 MB). |
| **(c) Self-hosted runner na Windows PC uživatele** | 0 USD (poplatek odložen) | Zdarma, rychlý (lokální cache, MSVC už nainstalované), umí i E2E s Edge. | Workflow běží s právy uživatele na hlavním PC: přístup ke Správci pověření, `gh` tokenu, SSH klíčům a síti s tiskárnami. GitHub varuje, že runner může být „persistently compromised by untrusted code in a workflow“, a doporučuje opatrnost i u soukromých repozitářů ([Secure use reference](https://docs.github.com/en/actions/reference/security/secure-use)). U soukromého repozitáře bez dalších spolupracovníků je riziko hlavně v závislostech (NuGet, akce třetích stran). **Po zveřejnění repozitáře je nepoužitelný** („should almost never be used for public repositories“). PC musí být zapnuté. Kolize s vývojem na stejném stroji (zámky worktree T-032, testy zasahující do systému). Zmírnění: samostatný účet Windows nebo VM, jen `workflow_dispatch`, žádné běhy z PR. |
| **(d) Zveřejnit repozitář** | 0 USD, Windows **i macOS** bez limitu minut | Plná matice včetně AOT publish na obou OS zdarma. Zároveň to odblokuje kontrolu aktualizací bez tokenu (T-058, T-017: soukromý repozitář vrací 404). Zveřejnění je stejně v plánu (rozhodnutí 2026-10-21). | **Rozhodnutí uživatele s důsledky:** kód a celá historie gitu jsou veřejné, takže je předem potřeba projít historii kvůli tajným údajům a osobním datům. Veřejné issues a PR, workflow z forků potřebují schválení. Self-hosted runner (c) pak nejde. Vydání `0.x` uvidí kdokoli (souvisí s T-058 Q7). |
| **(e) Nechat odložené** | 0 | Žádná práce ani riziko. Vydání fungují lokálně (T-055). | Žádná automatická kontrola. Regrese se chytí až při lokálním běhu. |

**Doporučení: dál (e), dokud uživatel nerozhodne o zveřejnění repozitáře. Pak (d) s plnou maticí Windows + macOS zdarma.** Pokud uživatel chce CI ještě před zveřejněním, pak **(a)**: jen Windows, push do `develop`/`main` a PR, `concurrency` a `paths-ignore` pro `docs/**` a `tasks/**`, odhad **300–600 min Windows měsíčně (asi 3–6 USD z 12 USD)**. Varianty (b) a (c) nedoporučuju: (b) duplikuje lokální vydání a potřebuje token v secrets, (c) přidává bezpečnostní riziko na hlavním PC a po zveřejnění se musí zrušit.

## Otevřené otázky
**Rozhodnuto 2026-10-30 (uživatel): zatím neimplementovat.** Úkol zůstává odložený; otázky níže se otevřou, až se k CI vrátíme. Repozitář je už veřejný (minuty GitHub Actions pro veřejné repozitáře jsou zdarma).

- Chceš i `ubuntu-latest` (rychlejší a levnější běh testů Core)?
  - a) **Ne, jen Windows (doporučeno).** Linux není cílová platforma. Windows minuty se do 12 USD vejdou (varianta a) a Windows ověří i App, AOT analyzátory a kód pro Windows. Minus: asi 1,7× dražší minuta než Linux.
  - b) `ubuntu-latest` jako rychlá první brána pro Core, PrusaLink, PrusaConnect a Camera.FFmpeg (bez FFmpeg, `build/FFmpeg.targets` na Linuxu nic nedělá) a Windows až potom nebo jen u PR. Plus: levnější (0,006 USD/min) a obvykle rychlejší. Minus: třetí OS k údržbě (citlivost na velikost písmen v cestách, chybějící nativní knihovny). Neověřeno, jestli se App a její testy na Linuxu vůbec sestaví.
  - c) Jen `ubuntu-latest` místo Windows. Plus: nejlevnější. Minus: neověřuje ani jednu cílovou platformu, nedoporučuju.
- **(nová, 2026-10-29)** Kterou variantu zvolit (a–e z tabulky výše), a jestli rozhodnutí počká na zveřejnění repozitáře (souvisí s T-058)?
- **(nová, 2026-10-29)** Pokud (a): spouštět i u každého pushe feature větví, nebo jen `develop`/`main` a PR? (Feature větve: víc běhů, odhad by se zhruba zdvojnásobil.)

## Poznámky k implementaci

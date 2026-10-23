---
id: T-075
title: Podpora projektu (GitHub Sponsors, Ko-fi) a financování podpisových certifikátů
status: todo
owner: architect
priority: P3
platform: all
depends_on: [T-074]
---

## Cíl
Umožnit uživatelům přispět na podpisové certifikáty, aby šla vydávat podepsaná sestavení a instalace přes Homebrew.

> **ODLOŽENO – neimplementovat**, dokud uživatel nezaloží účty a nepošle odkazy (rozhodnutí 2026-10-22: zatím jen jako plán v Roadmap v README).

## Kontext (průzkum 2026-10-22)
- **winget** podpis nepotřebuje (T-059), jen veřejný repozitář → nezávisí na penězích.
- **Homebrew:** od 1. 9. 2026 oficiální `homebrew/cask` vypíná casky, které neprojdou Gatekeeperem → nutný Apple Developer Program (99 USD/rok, T-060).
- **Windows:** Azure Artifact Signing není pro fyzické osoby v EU dostupný (jen USA/Kanada). Možnosti: SignPath Foundation (zdarma pro OSS, vyžaduje reputaci projektu, vydavatel „SignPath Foundation“), Certum Open Source / cloud Code Signing (orientačně od ~49 €/rok, ověřit aktuální cenu).
- Realistický cíl sbírky: ~99 USD + ~50 € ročně.
- Platformy: GitHub Sponsors (Stripe, IBAN, doklad, W-8BEN, dárce potřebuje účet GitHub, 0 % poplatek u jednotlivců) a Ko-fi (PayPal/Stripe, dárce bez registrace, 0 % platformě + poplatek procesoru). Buy Me a Coffee ne (5 %).

## Rozsah
- `.github/FUNDING.yml` s odkazy na GitHub Sponsors a Ko-fi.
- Sekce „Support the project“ / „Podpora projektu“ v `README.md` a `README.cz.md` (cíl: certifikáty, průběh sbírky).
- Po vybrání peněz: aktualizovat T-060 (Apple) a případně nový úkol na podpis pro Windows.

## Akceptační kritéria
- [ ] Uživatel založil účty a dodal odkazy.
- [ ] `FUNDING.yml` a sekce v obou README, tlačítko „Sponsor“ se na GitHubu zobrazuje.

## Otevřené otázky
- Daňové zacházení s příjmy ze sponzorství – ověří uživatel (účetní / finanční úřad).

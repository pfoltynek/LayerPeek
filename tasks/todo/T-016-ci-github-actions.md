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

## Otevřené otázky
- Chceš i `ubuntu-latest` (rychlejší a levnější běh testů Core)?

## Poznámky k implementaci

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

## Rozsah
- Matrix `windows-latest`, `macos-latest`
- SDK podle `global.json`
- Native AOT publish pro `win-x64` a `osx-arm64` + upload artefaktů
- Cache NuGet, publikace výsledků testů

## Akceptační kritéria
- [ ] Workflow prochází na obou OS
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Chceš i `ubuntu-latest` (rychlejší a levnější běh testů Core)?

## Poznámky k implementaci

---
id: T-016
title: CI – GitHub Actions (Windows + macOS)
status: todo
owner: qa-reviewer
priority: P1
depends_on: []
---

## Cíl
Automatický build a testy při každém push a PR. Základní workflow `.github/workflows/build.yml` už existuje – ověřit, že prochází, a doplnit.

## Rozsah
- Matrix `windows-latest`, `macos-latest`
- SDK podle `global.json`
- Native AOT publish pro `win-x64` a `osx-arm64` + upload artefaktů (základ už je ve workflow)
- Cache NuGet, publikace výsledků testů

## Akceptační kritéria
- [ ] Workflow prochází na obou OS
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Chceš i `ubuntu-latest` (rychlejší a levnější běh testů Core)?

## Poznámky k implementaci

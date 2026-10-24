---
id: T-074
title: Přejmenování aplikace a repozitáře na LayerPeek
status: todo
owner: architect
priority: P1
platform: all
depends_on: [T-065]
---

## Cíl
Před prvním veřejným vydáním přejmenovat aplikaci z „LayerPeek“ na **LayerPeek**, aby název nezačínal ochrannou známkou Prusa a nepůsobil jako oficiální produkt Prusa Research. Vztah k tiskárnám vyjadřuje doplněk „for Prusa printers“ (popis kompatibility).

Rozhodnutí uživatele (2026-10-22):
- Název **LayerPeek** (volný na GitHubu, ve wingetu i na webu, ověřeno 2026-10-22). Podnázev v README a v okně O aplikaci: „for Prusa printers“ / „pro tiskárny Prusa“.
- Přejmenovat **úplně vše**: produkt, exe, složky dat, AUMID, cíle ve Správci pověření, projekty, namespaces, `.slnx`, testy, nástroje, skripty a dokumentaci.
- Přejmenovat i **repozitář na GitHubu** na `pfoltynek/LayerPeek` (provede hlavní agent přes `gh repo rename`, GitHub staré URL přesměruje).
- **Barva ikony zůstává** (`#FD5000`), ikona se nemění.

## Předpoklady
- T-065 je mergnutý do `develop` a **neběží žádná jiná feature větev / worktree** (přejmenování sahá na ~800 souborů, souběžná práce by skončila konflikty). Před začátkem ověřit `git worktree list` a domluvit se s ostatními sessions.
- Větev `feature/readme-pitch` je mergnutá (nové README, CHANGELOG, `.github/`).

## Rozsah
- **Projekty a cesty:** `src/LayerPeek.*` → `src/LayerPeek.*`, `tests/LayerPeek.*` → `tests/LayerPeek.*`, `LayerPeek.slnx` → `LayerPeek.slnx`, přes `git mv` (zachovat historii). `PrusaSimulator` zůstává (simuluje tiskárnu Prusa, nejde o náš produkt).
- **Kód:** namespaces, `RootNamespace`, `AssemblyName` (exe `LayerPeek.exe`), `InternalsVisibleTo`, odkazy v `Directory.Build.props`, `Directory.Packages.props`, `build/FFmpeg.targets`, `.vscode/`.
- **Identita za běhu:** AUMID (`ProductionAppUserModelId`), zástupce v nabídce Start, název mutexu / pipe jediné instance, prefix cílů ve Správci pověření, složky `%APPDATA%\LayerPeek` a `%LOCALAPPDATA%\LayerPeek\logs`, názvy logů a diagnostického balíčku, User-Agent (pokud obsahuje název), na macOS bundle id a cesty (jen pokud už existují).
- **UI texty:** název aplikace ve všech `Strings.*.resx` (tooltip, O aplikaci, notifikace, titulky oken), odkaz na repozitář v `AboutViewModel`.
- **Skripty a nástroje:** `scripts/*.ps1` (názvy archivů `LayerPeek-<verze>-<RID>.zip`, cesty k projektu), Pester testy, `tools/*.ps1`, E2E testy (název procesu, cesty).
- **Dokumentace:** `README.md`, `README.cz.md`, `CHANGELOG.md`, `CLAUDE.md`, `.claude/agents/*.md`, `docs/**`, `.github/**`, odkazy `github.com/pfoltynek/LayerPeek` → `github.com/pfoltynek/LayerPeek`. Hotové úkoly v `tasks/done/` se **nepřepisují** (historie), aktivní úkoly v `todo/` a `in-progress/` ano.
- **Repozitář:** `gh repo rename LayerPeek`, `git remote set-url origin https://github.com/pfoltynek/LayerPeek.git` ve všech worktree. Volitelně přejmenovat lokální složku `D:\00-Source\LayerPeek` (rozhodne uživatel).
- Upozornění na nezávislost na Prusa Research v README a v okně O aplikaci zůstává.

## Akceptační kritéria
- [ ] `git grep -i "LayerPeek\|LayerPeek"` najde výskyty jen v `tasks/done/`, v `CHANGELOG.md` (poznámka o přejmenování) a v případném kódu pro úklid starých dat.
- [ ] Exe se jmenuje `LayerPeek.exe`, vlastnosti exe a okno O aplikaci ukazují LayerPeek a verzi z `Directory.Build.props`.
- [ ] Nastavení, logy a hesla se ukládají pod novými názvy (ověřeno testem a E2E testem).
- [ ] Notifikace fungují s novým AUMID (E2E nebo ruční ověření; starý zástupce v nabídce Start se odstraní nebo je popsané, jak ho odstranit).
- [ ] Release skripty vytvoří `LayerPeek-<verze>-win-x64.zip`, Pester testy procházejí.
- [ ] AOT publish a smoke test procházejí.
- [ ] Repozitář je přejmenovaný, `origin` ve všech worktree ukazuje na nové URL, odkazy v README fungují.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- **Migrace starých dat:** aplikace zatím nebyla vydaná, takže migrace pro uživatele není potřeba. Mají se na vývojovém stroji převést stávající nastavení a hesla (jednorázový skript), nebo stačí tiskárny zadat znovu? Návrh: bez migrace v aplikaci, jednorázový ruční skript nebo nové zadání.
- **Lokální složka repozitáře:** přejmenovat `D:\00-Source\LayerPeek` na `D:\00-Source\LayerPeek`? Ovlivní worktree a cesty v paměti agentů.

---
id: T-247
title: CI – omezit počet souběžně běžících testovacích projektů (přetížený 4jádrový runner)
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: [T-245, T-246]
---

## Cíl
Rozhodnutí uživatele 2026-11-15: testovací krok CI spouští všechny testovací projekty najednou a runner se 4 CPU je
přetížený. Z toho plynuly nestabilní testy citlivé na čas (T-220 alokace, T-245 UI test přes 30 s, T-246 Pester přes
5 min – podproces `pwsh` startuje pod zátěží 5 s místo 0,4 s). Změřit, zda omezení souběhu
(`dotnet test --max-parallel-test-modules N`) zlepší stabilitu, a podle délky kroku rozhodnout.

## Rozsah
- Několik ručních běhů CI (`ci/<sha>`, `workflow_dispatch`) s dnešním nastavením a s N = 2 a 3 na Windows (případně
  i macOS): délka testovacího kroku, nejpomalejší testy (TRX), selhání.
- Doporučení s čísly; změna `ci.yml`, `CiWorkflowTests`, `docs/prerequisites.md` (oddíl CI) jen podle výsledku.
- Lokální `dotnet test` se nemění.

## Akceptační kritéria
- [ ] Měření zapsaná (délka kroku, stabilita) pro dnešní stav a N = 2 / 3
- [ ] Rozhodnutí uživatele podle měření zapsané a provedené
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; CI zelené

## Otevřené otázky
- Jaké N, pokud se omezení ukáže jako přínosné (rozhodne uživatel podle měření)?

## Poznámky k implementaci
(vyplní agent po dokončení)

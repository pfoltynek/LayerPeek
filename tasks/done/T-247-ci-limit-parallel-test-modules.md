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
- [x] ~~Měření zapsaná (délka kroku, stabilita) pro dnešní stav a N = 2 / 3~~ – zrušeno (rozhodnutí uživatele 2026-11-15)
- [x] Rozhodnutí uživatele zapsané: úkol zrušen, CI beze změny
- [x] ~~`dotnet build` a `dotnet test` bez chyb a varování; CI zelené~~ – bez změny kódu

## Otevřené otázky
- ~~Jaké N, pokud se omezení ukáže jako přínosné (rozhodne uživatel podle měření)?~~ **Rozhodnuto uživatelem 2026-11-15:**
  úkol **zrušen** – CI zůstává bez omezení souběhu; testy citlivé na zátěž se řeší jednotlivě (T-220, T-245, T-246).

## Poznámky k implementaci
**Zrušeno 2026-11-15 (rozhodnutí uživatele).** Rozpracované měření: běh s `--max-parallel-test-modules 3`
(43766166248) – hlavní testy Windows 7 min 31 s, macOS 12 min 35 s, zelené; alokační krok na Windows selhal
(`CameraDashcamAllocationTests.SteadyStateBufferingDoesNotAllocate`, 4120 B, sleduje se samostatně). Běhy pro dnešní
stav a N = 2 byly omylem smazány při úklidu Actions a nové zrušeny; dočasné větve `ci/t247-*` smazány, `ci.yml` beze změny.

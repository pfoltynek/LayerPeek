---
id: T-008
title: Tray menu
status: todo
owner: ui-dev
priority: P1
platform: all
depends_on: [T-007]
---

## Cíl
Kontextové menu tray ikony.

## Rozsah
- Seznam tiskáren se stavem a progressem
- Otevřít webové rozhraní PrusaLink / Prusa Connect v prohlížeči
- Nastavení…, Diagnostika…, O aplikaci, Ukončit

## Akceptační kritéria
- [ ] Menu se aktualizuje se změnou stavu
- [ ] Ukončit korektně zastaví monitorovací službu
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- Jazyk UI: čeština, angličtina, nebo lokalizace obou?

## Poznámky k implementaci

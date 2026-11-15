---
id: T-244
title: Nestabilní test InstanceTransportTests – kontrola peera nestihne proběhnout pod zátěží (4 jádra)
status: todo
owner: qa-reviewer
priority: P3
platform: all
depends_on: []
---

## Cíl
Při práci na T-220 (2026-11-15, Windows, CI-like běh `dotnet test` omezený na 4 jádra) jednou selhal test
v `tests/LayerPeek.App.Tests/SingleInstance/InstanceTransportTests.cs` (kolem ř. 528):
`Assert.True(refusing.PeerChecks > 0)`. Druhá instance vzdá přeposlání příkazu po `forwardTimeout` 1 s dřív, než
zatížená primární instance spojení přijme a zkontroluje peera, takže `PeerChecks` zůstane 0. Jde o závod v testu
(chování aplikace je správné: příkaz cizího uživatele nedorazí).

## Rozsah
- Test upravit tak, aby nezávisel na tom, že primární instance stihne spojení přijmout do 1 s (např. počkat na
  `PeerChecks > 0` / záznam v logu s časovým limitem `TestTimeouts`, nebo delší `forwardTimeout` jen pro tuto kontrolu),
  a přitom dál ověřoval, že příkaz nedorazí a v logu je „another user“.
- Projít sousední testy téže třídy se stejným vzorem (`forwardTimeout` 1 s + tvrzení o straně primární instance).
- Reprodukce: `dotnet test` omezený na 4 jádra se souběžnou zátěží (postup v poznámkách T-220).

## Akceptační kritéria
- [ ] Příčina zapsaná, test opravený bez oslabení kontroly
- [ ] Třída `InstanceTransportTests` prochází 10× za sebou pod zátěží (4 jádra + souběžné běhy)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

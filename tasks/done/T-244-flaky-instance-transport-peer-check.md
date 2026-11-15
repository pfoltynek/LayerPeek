---
id: T-244
title: Nestabilní test InstanceTransportTests – kontrola peera nestihne proběhnout pod zátěží (4 jádra)
status: done
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
- [x] Příčina zapsaná, test opravený bez oslabení kontroly
- [x] Třída `InstanceTransportTests` prochází 10× za sebou pod zátěží (4 jádra + souběžné běhy)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
**Příčina:** v `ConnectionOfAnotherUserIsRefusedUnread` se druhá instance (forward budget 1 s) na Unix socketu připojí hned
(spojení čeká ve frontě `listen`), ale primární instance ho přijme, zkontroluje peera a zapíše „another user“ až ve své
smyčce `AcceptAsync` na pozadí. Test po `Start` druhé instance (vždy `NotForwarded`, buď odmítnutím, nebo vypršením
1 s) okamžitě tvrdil `PeerChecks > 0` a záznam v logu – pod zátěží smyčka primární instance za 1 s nedoběhla.
Chování aplikace je správné (příkaz nedorazí, spojení ve frontě se odmítne později); produkční kód beze změny.

**Oprava** (`tests/LayerPeek.App.Tests/SingleInstance/InstanceTransportTests.cs`):
- Test čeká na záznam „another user (uid …)“ v logu primární instance s limitem `TestTimeouts.Wait` (nový pomocník
  `WaitUntilAsync`, stejný vzor jako v `SettingsEditorStoreTests`), pak dál tvrdí `PeerChecks > 0` a že příkaz
  nedorazí (200 ms). Žádná kontrola nezmizela; záznam v logu vzniká až po kontrole peera, takže čekání na něj pokrývá obojí.
- Sousední test se stejným vzorem: `NamedPipeInstanceTransportTests.SecondStartGivesUpWhenTheRunningInstanceNeverReads`
  (Windows) tvrdil `connected.IsCompletedSuccessfully` hned po vzdání druhé instance (2 s) – dokončení
  `WaitForConnectionAsync` na straně serveru může pod zátěží přijít později. Nyní `await connected.WaitAsync(TestTimeouts.Wait)`
  (selže stejně, pokud se spojení nenaváže nebo skončí chybou). Ověřeno jen překladem na Macu (test se tu přeskakuje).
- Ostatní testy třídy tvrdí na straně primární instance jen přes `received.NextAsync(WaitTimeout)` (10 s), tedy už s limitem.

**Ověření (Mac, 10 jader):**
- Původní kód se na Macu pod zátěží nereprodukoval (10× s 30 procesy `yes`, 5× navíc pod `taskpolicy -b`).
  Deterministicky: dočasně zpomalená kontrola peera (`Thread.Sleep(1500)` ve `Peer`, delší než 1 s budget) – původní
  test selže v obou variantách (`Assert.Contains() Failure`), opravený projde. Dočasná úprava vrácena.
- Zátěž: třída (`-maxThreads 4`) spuštěná pod `taskpolicy -b` (jen úsporná jádra, nízká priorita) + 30 procesů `yes`
  + 2 souběžné smyčky běhů `LayerPeek.Core.Tests`: **10/10 průchodů** (47 testů, 11 přeskočených Windows; 31–99 s na běh).
  Všechny zátěžové procesy ukončeny (ověřeno `pgrep`).
- `dotnet build` 0 varování, 0 chyb; `dotnet test` 11012 celkem, 10725 prošlo, 287 přeskočeno, 0 selhalo.

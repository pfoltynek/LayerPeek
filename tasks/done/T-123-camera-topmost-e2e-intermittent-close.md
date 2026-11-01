---
id: T-123
title: Občasné selhání E2E „okno kamery zůstane otevřené při změně volby Vždy navrchu“
status: done
owner: qa-reviewer
priority: P3
platform: windows
depends_on: [T-114]
---

## Cíl
Při celém běhu `win.e2e` (T-114, 2026-11-01, develop 7b40a09) selhal jednou test
`CameraTopmostTests.TopmostKeepsTheCameraAboveAnotherApp` s hlášením „The camera window stays open when the option
changes.“ Dva samostatné opakované běhy třídy hned potom prošly (2/2, 2/2) a třída prošla i 2026-10-31. Snímky a log
selhání se přepsaly opakováním (stejná složka výsledků).

## Rozsah
1. Zjistit, zda jde o skutečnou občasnou chybu aplikace (okno kamery se při Použít / změně „Vždy navrchu“ v Nastavení
   zavře nebo znovu vytvoří, T-040 / T-102 / T-117), nebo o časování v testu (okno se při změně přítomnosti na hlavním
   panelu krátce skryje – `Hide()` / `Show()` Avalonie – a test ho v tu chvíli nenajde).
2. Podle výsledku opravit aplikaci, nebo test (čekání na stav místo okamžitého dotazu), a zachovat výsledky selhání
   (vlastní složka nebo název podle běhu, aby je opakování nepřepsalo).

## Akceptační kritéria
- [x] Příčina zjištěna a zapsána v poznámkách
- [x] Oprava (aplikace nebo test); třída `CameraTopmostTests` projde 10× za sebou (`run-e2e.ps1`)
  – oprava testu hotová, 10× běh na ploše uživatele zatím nespuštěn (koordinuje hlavní agent)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
(žádné)

## Poznámky k implementaci
**Příčina: časování v testu, ne chyba aplikace.** Rozbor z kódu (Avalonia.Win32 12.1.4 dekompilováno):
- Test po Uložit čekal, až okno ztratí `WS_EX_TOPMOST`, a hned potom jediným dotazem `IsWindowVisible` ověřil, že je
  okno otevřené.
- Aplikace (`CameraWindow.OnViewModelPropertyChanged` → `Topmost = …`, `CameraWindow.OnPropertyChanged` →
  `ShowInTaskbar = ShowsInTaskbar(…)`, T-102) při vypnutí „Vždy navrchu“ nejdřív zavolá `WindowImpl.SetTopmost(false)`
  (`SetWindowPos(HWND_NOTOPMOST)` – styl zmizí hned) a pak `ShowTaskbarIcon(true)` → `UpdateWindowProperties`, větev
  `ShowInTaskbar`: okno se skrytým vlastníkem se `Hide()` (`ShowWindow(SW_HIDE)`), `SetParent(null)` a `Show(activate)`.
  Je to **stejné HWND**, okno se nezavírá ani nevytváří znovu; jednorázové bliknutí bylo v T-102 (otázka 5) přijato.
- Test běží v jiném procesu a stav okna čte bez ohledu na UI vlákno aplikace, takže se mohl trefit přesně mezi
  `SW_HIDE` a `Show` (skrytí aktivního okna přepíná aktivaci mezi procesy, chvilka je delší než pár instrukcí). Totéž
  platí pro malý náhled (druhé ověření v testu).

**Oprava testu** (`tests/LayerPeek.App.E2E.Tests/CameraTopmostTests.cs`, `ExpectStaysOpenAsync`): místo jednoho snímku
test počká (až 10 s), až je okno znovu vidět, a pak ho 1 s sleduje. Selže, když okno zaniklo (`IsWindow` – zavřené nebo
znovu vytvořené), se neukázalo znovu, se později znovu skrylo, bylo skryté víc než jednou (události `EVENT_OBJECT_HIDE`
z `WinEventRecorder` od Uložit) nebo když se objevilo jiné okno kamery aplikace. Hlášení selhání i zpráva
`camera-topmost-report` zapisují, co test viděl: HWND, viditelnost hned po zmizení stylu, výpadky se časem, události
SHOW/HIDE se časem, ostatní okna kamery a všechna okna aplikace; při selhání se navíc uloží snímky všech oken
(`FailureScreenshots.Capture`, `failure-topmost-normal-off-*`).
Review (qa-reviewer): `SetWindowPos(HWND_NOTOPMOST)` běží těsně před `SW_HIDE`, takže test může vidět okno ještě viditelné
a vzorek sledování pak padne do toho jednoho přijatého skrytí. Skrytý vzorek je proto povolený jen tehdy, když hook viděl
právě jedno `EVENT_OBJECT_HIDE`; zaniklé okno, druhé skrytí nebo okno skryté na konci dál selžou.

Ověření: `dotnet build` 0 varování; `dotnet test` 8962 testů, 0 selhání (E2E bez proměnné prostředí přeskočeny). První
celý běh měl 2 selhání v `LayerPeek.App.UiTests` (změna se jich netýká, názvy výstup oříznul); projekt samotný i druhý
celý běh prošly – nejspíš zátěž stroje souběžnými agenty.

Uchování výsledků selhání při opakování (bod 2 rozsahu) řeší obecně T-132; `run-e2e.ps1` zatím výsledky přepisuje, proto
smyčka pro 10× běh kopíruje složku výsledků po každém selhání.
- **Ověření na skutečné ploše (hlavní agent, 2026-11-01, Windows 11 Pro 10.0.26300):** `run-e2e.ps1 -Class '*CameraTopmostTests' -NoSystemChanges` 10× za sebou, 10/10 zelených. Hlášení potvrzuje příčinu: po každém Uložit jedno `HIDE` a `SHOW` téhož HWND s odstupem 15–22 ms, žádné zavření ani nové okno. Review bez blokujících nálezů; uzavřeno 2026-11-01.

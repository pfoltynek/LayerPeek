---
id: T-131
title: Občasné selhání StatusWidgetTests (Release, statická inicializace)
status: done
owner: qa-reviewer
priority: P3
platform: all
depends_on: []
---

## Cíl
Při `pwsh scripts/release-build.ps1` (Release `dotnet test`, 2026-11-01, T-130) jednou selhal
`LayerPeek.App.Tests.Cameras.Overlay.Widgets.StatusWidgetTests.StatusIsRegisteredWithItsNamePriorityAndSizes` na
`Assert.Same(StatusWidgetTemplate.Card, definition.CardTemplate)` (`tests/LayerPeek.App.Tests/Cameras/Overlay/Widgets/StatusWidgetTests.cs:91`).
Tři samostatné opakované běhy v Release prošly. Pravděpodobně souběh statické inicializace (šablona karty / registr
widgetů) mezi paralelně běžícími testy. Release build se kvůli tomu zastavil, takže chyba vadí i vydání.

## Rozsah
1. Zjistit příčinu (statická pole, `Lazy`, pořadí inicializace, sdílený registr widgetů mezi testy) a zda může nastat
   i v aplikaci (dva různé objekty šablony za běhu), nebo jen v testech.
2. Opravit produkt, nebo test; ověřit opakovaným během (např. 50× třídy v Release).

## Akceptační kritéria
- [x] Příčina zapsaná v poznámkách
- [x] Oprava; `StatusWidgetTests` a sousední testy widgetů projdou opakovaně v Debug i Release
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Poznámky k implementaci
- **Příčina:** `StatusWidgetTemplate.Card` byl `field ??= (IDataTemplate)new StatusWidgetTemplate()[Key]!` – neatomické
  „zkontroluj a nastav“. Když se první použití sejde ve dvou vláknech (paralelní testovací třídy volající
  `OverlayWidgets.CreateRegistry()`), obě uvidí `null`, obě načtou vlastní slovník a každé vrátí *svou* šablonu; v poli
  zůstane jen ta poslední. Test pak dostal z registru šablonu A, ale `StatusWidgetTemplate.Card` už vracel B →
  `Assert.Same` selhal. Ostatní widgety (`Times`, `Temperatures`, `PrintName`, `Cooldown`) mají `static … { get; } =`
  (inicializátor typu, CLR ho spustí právě jednou), takže problém neměly.
- **V aplikaci:** registr se vytváří jednou při skládání služeb (`AppServices`, singleton) v jednom vlákně, takže dvě
  instance šablony za běhu prakticky nevzniknou; i kdyby, byly by funkčně shodné (jen dvojí načtení XAML). Šlo tedy hlavně
  o testy, oprava je ale v produktu.
- **Oprava:** `Card => LazyInitializer.EnsureInitialized(ref field, …)` – atomické publikování (`Interlocked.CompareExchange`),
  všichni volající dostanou tutéž instanci. Přetížení s továrnou nepoužívá reflexi (AOT v pořádku, build bez varování).
  Stejný vzor měl `AppIcon.Window` – opraven stejně (neúspěšné načtení se neukládá a zkusí se znovu, jako dosud).
- **Důkaz:** nový `StatusWidgetTemplateRaceTests` (vlastní kolekce s `DisableParallelization`, běží sám po paralelních
  testech; testovou reflexí vynuluje skryté pole, 20 kol × 8 vláken přes Barrier, původní šablonu vrátí). Před opravou
  selhal 10/10 běhů v Release, po opravě 0/20.
- **Opakované běhy po opravě:** `--filter-namespace "LayerPeek.App.Tests.Cameras.Overlay*"` (418 testů, všechny widgety)
  Release 50× bez chyby, Debug 10× bez chyby; celé `LayerPeek.App.Tests` v Release 4122 testů (83 přeskočeno) zeleně;
  celé `dotnet test` (Debug) 8963 testů, 0 selhání, 277 přeskočeno; `dotnet build` 0 varování.
- Původní selhání bylo vzácné (závisí na tom, které dvě třídy se sejdou na prvním použití), proto 50 zelených běhů
  testů widgetů samo o sobě nic nedokazuje – průkazný je deterministicky selhávající zátěžový test před opravou.

## Otevřené otázky
(žádné)
- Review 2026-11-01 bez blokujících nálezů (doplněn záchyt výjimek ve vláknech zátěžového testu); uzavřeno 2026-11-01.

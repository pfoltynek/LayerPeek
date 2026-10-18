---
id: T-031
title: Headless UI testy Avalonie (okno nastavení, tray menu)
status: todo
owner: qa-reviewer
priority: P2
platform: all
depends_on: [T-015, T-028]
---

## Cíl
Automaticky testovat skutečné Avalonia UI (XAML, kompilované bindingy, ovládací prvky) bez zobrazení oken. Dnešní testy v `PrusaTrayDiagnostic.App.Tests` pokrývají view modely a prezentéry přes fakes, ale ne XAML okna nastavení ani napojení `NativeMenu`/`TrayIcon`.

## Rozsah
- Rozhodnuto uživatelem 2026-10-18: headless testy Avalonie (Avalonia.Headless) pro okno nastavení a tray menu.
- Testovací infrastruktura podle rozhodnutí Q1. Headless aplikace s `UseHeadless(...)` a stejným tématem jako aplikace (Fluent). Okna se v testu vytvoří přes stejný kód jako v aplikaci (`SettingsWindow` + `SettingsViewModel` s fake `ISettingsEditorStore`, `ISecretStore`, `ISettingsApplier`).
- **Okno nastavení (T-015, T-027, T-029):**
  - přidání a odebrání tiskárny;
  - vyplnění adresy, uživatelského jména a hesla psaním do `TextBox` (`KeyTextInput`);
  - heslo je maskované;
  - validační chyby se zobrazí u pole;
  - Uložit zavolá store se správnými hodnotami;
  - Zrušit nic neuloží;
  - přepnutí jazyka přepne texty v otevřeném okně;
  - zavření okna uvolní view model.
- **Tray menu (T-008, T-013, T-028):**
  - `AvaloniaNativeTrayMenu` nad skutečným `NativeMenu`: struktura položek pro jednu a pro víc tiskáren (podmenu);
  - texty po změně stavu a jazyka;
  - vyvolání `Command` položky „Otevřít PrusaLink v prohlížeči“ zavolá `PrinterWebOpener` s fake `IPrinterWebGateway`/`IUrlLauncher` (T-028);
  - `TrayIcon` lze v headless režimu vytvořit a přiřadit mu menu (ověřeno pokusem). Nativní vykreslení ikony a menu headless netestuje, to zůstává ručním ověřením.
- Kompilované bindingy zůstávají zapnuté (`AvaloniaUseCompiledBindingsByDefault`), testy nesmí potřebovat `x:CompileBindings="False"`.
- Testy po sobě uklidí okna, view modely a dočasné soubory, i při pádu. Nic se nezapisuje do skutečného úložiště tajných údajů ani do skutečného `settings.json`.

## Akceptační kritéria
- [ ] Infrastruktura podle Q1 funguje s `dotnet test` (MTP), bez balíčků VSTest.
- [ ] Testy okna nastavení podle Rozsahu (minimálně: přidání tiskárny, vyplnění polí psaním, validace, uložení, zrušení, přepnutí jazyka).
- [ ] Testy tray menu podle Rozsahu (struktura pro 1 a N tiskáren, změna textů, příkaz „Otevřít v prohlížeči“).
- [ ] Testy jsou deterministické (bez `Task.Delay` na čekání UI, jen `Dispatcher.UIThread.RunJobs()` / fake čas) a dají se spouštět paralelně s ostatními testovacími projekty.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Q1 – Balíček a integrace s xUnit v3.** Ověřeno 2026-10-18 pokusnými projekty ve scratchpadu (Avalonia 12.1.4, SDK `11.0.100-rc.2.26504.105`):
  - `Avalonia.Headless.XUnit` **12.1.4** (nejnovější, `[AvaloniaFact]`, `[AvaloniaTestApplication]`) je sestavený proti `xunit.v3.extensibility.core` **3.2.2**. S `xunit.v3` **4.0.2**, kterou repozitář používá, padá už při objevování testů na `MissingMethodException` (`TestIntrospectionHelper.GetTestCaseDetails`). S `xunit.v3` 3.2.2 oba pokusné testy prošly (psaní do `TextBox`, vytvoření `TrayIcon` + `NativeMenu`). Dokumentace: https://docs.avaloniaui.net/docs/testing/headless-xunit. Avalonia 12 přešla na xUnit v3: https://v12.docs.avaloniaui.net/docs/avalonia12-breaking-changes.
  - `Avalonia.Headless` **12.1.4** bez integrace s xUnit: vlastní `HeadlessUnitTestSession.StartNew(typeof(TestApp))` a `session.Dispatch(...)` z běžného `[Fact]` funguje s `xunit.v3` 4.0.2. V pokusu ale `session.Dispose()` zavolané z testovacího vlákna **visel**. Bez `Dispose` (jedna relace na celé sestavení, ukončí se s procesem) test prošel.
  - Možnosti:
    - (a) `Avalonia.Headless.XUnit` 12.1.4 a pro celé řešení snížit `xunit.v3` na 3.2.2;
    - (b) `Avalonia.Headless.XUnit` v samostatném testovacím projektu s jinou verzí `xunit.v3`. Centrální správa balíčků to umožní jen přes `VersionOverride`, což porušuje pravidlo „verze jen v `Directory.Packages.props`“;
    - (c) `Avalonia.Headless` 12.1.4 s vlastní fixture (jedna relace na sestavení, přes `IAssemblyFixture` xUnit v3), bez snižování xUnit. Visící `Dispose` je nutné vyřešit nebo obejít;
    - (d) počkat na `Avalonia.Headless.XUnit` s podporou xUnit v3 4.x.
  - Oba balíčky jsou jen testovací, takže je požadavek na AOT netýká. Licence MIT.
  - **Doporučení architekta:** (c). Zachová xUnit 4.0.2 a pravidla centrální správy balíčků. Před implementací ověřit příčinu visícího `Dispose` (např. volat ho přes `Dispatch`, nebo nechat relaci žít do konce procesu).
  - **Rozhodnuto 2026-10-18:** (c) samotný **`Avalonia.Headless`** s vlastní `HeadlessUnitTestSession`, **jedna relace na testovací sestavení**, bez snižování `xunit.v3`. Před implementací nejdřív zjistit příčinu visícího `Dispose`.
- **Q2 – Kam testy umístit.** Do stávajícího `PrusaTrayDiagnostic.App.Tests` (sdílí fakes, `MenuFakes.cs`), nebo do nového `PrusaTrayDiagnostic.App.UiTests` (izolace headless platformy od ostatních testů)?
  - **Doporučení architekta:** nový projekt `tests/PrusaTrayDiagnostic.App.UiTests`. Sdílené fakes přesunout do `tests/Shared`.
  - **Rozhodnuto 2026-10-18:** nový projekt **`tests/PrusaTrayDiagnostic.App.UiTests`**.

## Poznámky k implementaci
(vyplní agent po dokončení)

---
id: T-234
title: Pole hesla – UI Automation hlásí IsPassword = False a délku hesla (Avalonia 12.1.6)
status: done
owner: ui-dev
priority: P3
platform: windows
depends_on: [T-054]
---

## Cíl
Měření v T-054 (2026-11-15, Avalonia 12.1.6, Windows 11, Debug i Native AOT) ukázalo, že pole hesla PrusaLink
v okně nastavení (`TextBox` s `PasswordChar="●"`, `PrinterPassword`) **heslo samotné přes UI Automation nevydá**
(`ValuePattern.Value` i `LegacyIAccessible.Value` = maska `●`, `Name` / `HelpText` = popisek a nápověda, `TextPattern`
prvek nenabízí, v surovém stromu okna heslo nemá žádný prvek). Zbyly ale dva nálezy:

1. **`IsPassword` = `False`.** Win32 vrstva automatizace Avalonie (`Avalonia.Win32.Automation.AutomationNode.GetPropertyValue`)
   vlastnost `UIA_IsPasswordPropertyId` vůbec neobsluhuje a `AutomationPeer` pro ni nemá API, takže ji nejde nastavit ani
   vlastním peerem. Klienti UIA (Předčítání, NVDA, JAWS) se podle `IsPassword` rozhodují, zda při psaní předčítají znaky;
   pole se jim jeví jako běžné textové pole. Možný důsledek: Předčítání nahlas přečte psané znaky hesla. **Neověřeno**
   (vyžaduje zapnuté Předčítání).
2. **Délka hesla.** `ValuePattern.Value` vrací masku o délce hesla (např. 23 × `●`), takže každý proces stejného
   uživatele zjistí délku hesla. Na obrazovce je stejná informace vidět; nativní pole hesla Windows (`ES_PASSWORD`)
   a WPF `PasswordBox` hodnotu přes `ValuePattern` nevydávají vůbec.

Kopírování a vyjmutí z pole hesla Avalonia zakazuje (`TextBox.IsPasswordBox` → `CanCopy`/`CanCut` = false), schránkou
heslo neunikne.

## Rozsah (podle rozhodnutí uživatele 2026-11-15)
- `IsPassword`: oprava jen v Avalonii (vlastnost v `AutomationPeer`, např. `IsPasswordCore`, a její mapování
  v `AutomationNode` Windows a v macOS vrstvě). V tomto úkolu se připraví **koncept upstream issue**
  (`docs/upstream/avalonia-ispassword-issue.md`, anglicky); odešle ho uživatel ze svého účtu. V aplikaci se nic nemění.
- Délka hesla v masce `ValuePattern.Value`: **přijatá** (odpovídá tomu, co je vidět na obrazovce), vlastní peer se nedělá.
- Ruční kontrola s Předčítáním se nedělá.
- macOS: kontrola stejných vlastností přes Accessibility jako bod **1t v T-127**.
- Navazující krok (mimo tento úkol, po opravě v Avalonii): zpřísnit `PasswordAutomationTests` (T-054) tak, aby
  vyžadoval `IsPassword` = `True`; v testu je `TODO(T-234)` s odkazem na koncept issue.

## Akceptační kritéria
- [x] Rozhodnutí uživatele k otevřeným otázkám zapsané níže
- [x] ~~`IsPassword` = `True` u `PrinterPassword` (po opravě v Avalonii) a test `PasswordAutomationTests` to vyžaduje~~
      → splnitelné až po opravě v Avalonii (rozhodnuto uživatelem 2026-11-15); nahrazeno: koncept upstream issue
      `docs/upstream/avalonia-ispassword-issue.md` připravený a v `PasswordAutomationTests.RecordIsPassword` je
      `TODO(T-234)` – po aktualizaci Avalonie s opravou vyžadovat `True` (navazující krok, viz Rozsah)
- [x] ~~(pokud se schválí) `ValuePattern.Value` pole hesla nevydává délku hesla; test to ověřuje~~ → neschváleno, délka
      hesla v masce přijatá (rozhodnuto uživatelem 2026-11-15)
- [x] Kontrola macOS zapsaná do T-127 (bod 1t) → přesunuto do T-127
- [x] `dotnet build` a `dotnet test` bez chyb a varování; E2E `pwsh tools/run-e2e.ps1 -Class '*PasswordAutomationTests'
      -NoSystemChanges` na Windows PASS (2026-11-15, hlavní agent; test jen přes UIA, bez myši, takže běží i přes RDP)

## Otevřené otázky
- Nahlásit chybějící `IsPassword` upstream do Avalonie (issue, případně PR)? Kdo a kdy?
  - **Rozhodnuto uživatelem 2026-11-15:** agent připraví text issue (anglicky, `docs/upstream/avalonia-ispassword-issue.md`),
    uživatel ho odešle ze svého účtu. Agent nic neodesílá.
  - **Změna rozhodnutí uživatele 2026-11-15 (večer):** issue se **neodesílá, upstream hlášení zamítnuto**. Koncept
    `docs/upstream/avalonia-ispassword-issue.md` byl smazán (zůstává v historii gitu, commit T-234). Zjištění zůstávají
    zapsaná v Poznámkách; `IsPassword` = `False` se přijímá, dokud ho Avalonia sama neopraví.
- Skrývat délku hesla vlastním peerem (prázdná / pevná hodnota `ValuePattern.Value`), nebo ji přijmout jako
  rovnocennou tomu, co je vidět na obrazovce?
  - **Rozhodnuto uživatelem 2026-11-15:** přijmout (je to totéž, co je vidět na obrazovce); vlastní peer se nedělá.
- Ověřit ručně s Předčítáním, zda při psaní do pole hesla předčítá znaky (zapnutí Předčítání je zásah do systému)?
  - **Rozhodnuto uživatelem 2026-11-15:** ne, ruční kontrola s Předčítáním se nedělá.
- macOS: ověřit stejné vlastnosti přes AX v T-127, nebo samostatným úkolem?
  - **Rozhodnuto uživatelem 2026-11-15:** v T-127 (nový oddíl 1t: role `AXSecureTextField`, `AXValue` maska, heslo
    nikde ve stromu AX).

## Poznámky k implementaci
- Příčina ověřená dekompilací balíčků Avalonia 12.1.6 (ilspycmd, 2026-11-15):
  - `Avalonia.Win32.Automation.AutomationNode.GetPropertyValue` (sestavení `Avalonia.Win32.Automation.dll`) nemá větev
    pro `UiaPropertyId.IsPassword` (hodnota výčtu existuje), větev `default` vrací `null`.
  - `AutomationPeer` (`Avalonia.Controls.dll`) nemá žádné `IsPassword` / `IsPasswordCore`, vlastním peerem to opravit
    nejde. `TextBoxAutomationPeer.Value` masku dělá přes `GetDisplayedText(Text, PasswordChar, RevealPassword)`
    (při `RevealPassword = true` vydá čitelný text, jako na obrazovce; aplikace odhalení hesla nemá).
  - macOS: `libAvaloniaNative.dylib` 12.1.6 neobsahuje odkaz na `NSAccessibilitySecureTextFieldSubrole`, pole se tam
    tedy nejspíš jako `AXSecureTextField` nehlásí – neověřeno za běhu, ověří T-127 bod 1t.
- Koncept issue: `docs/upstream/avalonia-ispassword-issue.md` (název, popis, minimální repro, očekávané / skutečné
  chování, analýza, návrh opravy: `AutomationPeer.IsPassword()` / `IsPasswordCore()`, přepsání v `TextBoxAutomationPeer`
  podle `PasswordChar`, větev `UiaPropertyId.IsPassword` v `AutomationNode.GetPropertyValue`, podrole
  `AXSecureTextField` v Avalonia.Native). Hledání duplicit na webu (jen čtení) žádné odpovídající issue nenašlo; před
  odesláním ho uživatel zopakuje přímo na GitHubu. Po odeslání sem a do konceptu připsat odkaz.
- `tests/LayerPeek.App.E2E.Tests/PasswordAutomationTests.cs`: komentář u `RecordIsPassword` s `TODO(T-234)` – po
  aktualizaci na verzi Avalonie s opravou přidat `IsPassword != True` mezi selhání.
- Ověřeno jen na Windows (build, testy, kompilace E2E projektu); na macOS nic.

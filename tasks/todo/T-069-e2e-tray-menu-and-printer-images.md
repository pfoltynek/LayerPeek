---
id: T-069
title: E2E – tray menu (klávesnice, tooltip, Přidat tiskárnu, Diagnostika při startu, Předčítání) a obrázky tiskáren
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065, T-067]
---

## Cíl
Ověřit automaticky ruční kontroly T-042 (body 1–7) a T-039 při 100 % na skutečném tray menu a v oknech testovací instance. Měřítko 150 % pokrývá T-070. Staví na `TrayMenuTests` a `NotificationArea` z T-044/T-062. Zásah T-065 „testovací tray ikona na hlavním panelu“ se použije, aby ikona nebyla jen v okně skrytých ikon.

## Pokrytí
| Úkol | Kontrola | Test (návrh názvu) |
|---|---|---|
| T-042 bod 1 | vzhled podmenu u 1 i 3 tiskáren, ikony jen u tiskáren | `PrinterRowsHaveIllustrationsAndActionsHaveNone` |
| T-042 bod 2 | podtržení a funkce zkratek (klávesnice, Alt, písmeno) | `MenuWorksFromTheKeyboardWithAccessKeys` |
| T-042 bod 3 | tooltip s celým názvem u zkráceného řádku | `TruncatedPrinterRowShowsTheFullNameInATooltip` |
| T-042 bod 4 | „Přidat tiskárnu…“ s prázdným nastavením | `AddPrinterOpensSettingsWithANewPrinter` |
| T-042 bod 5 | Předčítání u tiskárny s `_` v názvu | `PrinterRowWithUnderscoreHasAPlainAutomationName` |
| T-042 bod 6 | „Diagnostika…“ zakázaná při „Spouštění…“; přepnutí nezavře otevřené menu | `DiagnosticsTurnsOnWhileTheMenuStaysOpen` |
| T-042 bod 7 | „Zobrazit kameru“ / „Skrýt kameru“, zavření křížkem | rozšíření `RealTrayMenuShowsThePrinterSubmenuAndTogglesTheCamera` |
| T-039 | ilustrace v tray menu při 100 % | `PrinterRowsHaveIllustrationsAndActionsHaveNone` |
| T-039 | tlačítko obrázku: nabídka, model, vlastní otočená fotka, Uložit / Zrušit | `CustomImageFromAPhoneIsShownUpright` |
| T-039 | Diagnostika: obrázek v záhlaví | `DiagnosticsHeaderShowsThePrinterImage` |
| T-039 | po uložení se obrázek v menu změní bez „Připojování…“ | `SavedImageChangesTheMenuWithoutReconnecting` |
| – | „Otevřít PrusaLink“ v podmenu (UIA ji nehlásí, T-044) | `OpenPrusaLinkFromTheKeyboardOpensTheProxy` (klávesa zkratky, adresa z `opened-urls.txt` T-067, GET přes proxy) |

## Rozhodnuto
- **U+2060 v názvu pro čtečky (uživatel 2026-10-22, dřív Q1):** UIA `Name` řádku tiskárny se porovná s viditelným textem po odstranění U+2060. Nezměněný `Name` se zapíše do výstupu testu a do výsledků, aby bylo vidět, co Předčítání dostává. Výskyt U+2060 sám o sobě není chyba.

## Rozsah
- **Ikony v menu (pixelová kontrola):** snímek otevřeného menu. Ve sloupci ikon řádku tiskárny musí být obrázek, který po složení na změřenou barvu pozadí menu odpovídá ilustraci modelu simulátoru (Core One, MK4S, XL, MINI z `Assets`) ve velikosti `16 × systémové škálování` px. Tolerance odchylky se zapíše do poznámek. Akce a pevné položky mají sloupec ikon prázdný (jen barva pozadí). Ilustrace se čte ze zdrojových assetů repozitáře, ne z aplikace.
- **Klávesnice:** Win+B (fokus do oznamovací oblasti), šipkami na ikonu testovací instance (podle `[E2E]` v názvu), Shift+F10 / klávesa nabídky otevře menu. Šipka vpravo otevře podmenu tiskárny. Písmeno zkratky spustí položku (např. „S“ = Settings… otevře nastavení; v podmenu „C“ = Show camera). `AccessKey` všech pevných položek přes UIA odpovídá `.resx`. **Podtržení:** snímek menu otevřeného klávesnicí (zobrazené klávesové nápovědy) a myší. Rozdíl obrazu leží jen v dolní části řádků s klávesovou zkratkou (vodorovná čára pod písmenem). Heuristika: když se ukáže jako nestabilní, zůstane jen snímek pro člověka a zapíše se do poznámek.
- **Tooltip:** tiskárna s názvem delším než 32 znaků. Skutečná myš najede na řádek, do 3 s se objeví okno tooltipu s celým názvem (UIA `ToolTip`). Pokud ho Avalonia u nativního menu nezobrazí (T-042 to připouští), test to zapíše jako nález a zeptá se uživatele (neopravuje sám).
- **Přidat tiskárnu:** start s prázdným `printers`. Menu ukáže zakázaný text a „Add printer…“. Klik otevře nastavení na záložce Tiskárny s jednou novou tiskárnou. Druhý klik nepřidá další (stejné okno).
- **Předčítání (rozhodnutí 2026-10-22):** tiskárna `MK4_dílna`. UIA `Name` řádku se porovná **po odstranění U+2060** s viditelným textem (`MK4_dílna` + stav), bez zdvojeného `_` a bez jiných neviditelných znaků. Nezměněný `Name` (s kódovými body neviditelných znaků, např. `⁠`) se zapíše do výstupu testu a do složky výsledků. Pokud U+2060 v `Name` je, test neselže. Kontrolují se i `Name` a `AccessKey` položek podmenu.
- **Diagnostika při startu:** `PRUSATRAYDIAGNOSTIC_TEST_START_DELAY_MS` (T-067) asi 15 s. Test otevře menu během „Spouštění…“, „Diagnostics…“ je zakázaná. Menu zůstane otevřené, a když se monitorování spustí, položka se povolí. Ověří se, že je to stejné okno menu (HWND) a stejný prvek UIA (`RuntimeId`), menu se nezavřelo ani nepřestavělo. Pak ji jde spustit a otevře Diagnostiku.
- **Kamera v podmenu:** k existujícímu testu doplnit, že položka nemá stav zaškrtnutí (`TogglePattern` chybí / `ToggleState` není `On`) a že po zavření okna kamery křížkem (`WindowPattern.Close`) menu znovu ukazuje „Show camera“.
- **Vlastní obrázek z telefonu:** test vygeneruje JPEG (např. 600×400, čtyři barevné kvadranty) s EXIF `Orientation = 6` (otočit o 90°) do testovací datové složky. V nastavení: tlačítko obrázku → nabídka (seznam modelů + „Vlastní obrázek…“), výběr modelu změní náhled. „Vlastní obrázek…“ otevře skutečný dialog Otevřít ve složce testu (T-067), soubor se vybere v seznamu, Otevřít. Náhled ukazuje kvadranty v pořadí po otočení (pixelová kontrola). Zrušit = beze změny, Uložit = obrázek v `printer-images/` testovací složky. Nic nezůstane mimo testovací složku. Dialog si může zapamatovat poslední složku (MRU, stejně jako v T-064) – zapsat.
- **Diagnostika:** záhlaví (`DiagnosticsHeader`) obsahuje obrázek tiskárny: ilustrace modelu, po uložení vlastní obrázek (pixelová kontrola).
- **Po uložení:** tray menu před a po uložení vlastního obrázku: ikona řádku se změnila (porovnání snímků) a text řádku nikdy neobsahuje „Connecting…“ (UIA `Name` čtený opakovaně během 10 s po uložení).
- Snímky každého kroku do složky výsledků.

## Akceptační kritéria
- [ ] Testy z tabulky Pokrytí procházejí 3× po sobě (stabilita jako T-062)
- [ ] Pixelová kontrola ikon v menu: ilustrace u tiskáren, nic u akcí; tolerance zapsaná
- [ ] Klávesnice: otevření menu bez myši, zkratky fungují, `AccessKey` odpovídá `.resx`
- [ ] Předčítání: `Name` po odstranění U+2060 = viditelný text; nezměněný `Name` (kódové body neviditelných znaků) zapsaný do výstupu testu a do výsledků
- [ ] „Diagnostika…“ se povolí při otevřeném menu bez zavření a přestavění menu
- [ ] Vlastní obrázek s EXIF orientací se zobrazí správně; nic mimo testovací složku
- [ ] Ruční kontroly T-042 (body 1–7) a T-039 (100 %, nabídka, Diagnostika, po uložení) zaškrtnuté v příslušných úkolech podle výsledků
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (žádné – Q1 rozhodnuta 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
(vyplní agent po dokončení)

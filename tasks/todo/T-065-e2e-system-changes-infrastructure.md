---
id: T-065
title: E2E – infrastruktura zásahů do systému (varování, návrat i po pádu) a přehled pokrytí ručních kontrol
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-044, T-064]
---

## Cíl
Devět úkolů v `tasks/in-progress/` (T-010, T-025, T-035, T-037, T-039, T-041, T-042, T-043, T-052) drží otevřené jen ruční kontroly na Windows. Uživatel chce před první předběžnou verzí 0.1.0 tyto kontroly co nejvíc nahradit E2E testy skutečné aplikace (`tests/PrusaTrayDiagnostic.App.E2E.Tests`, T-044). Část kontrol jde ověřit jen tehdy, když test dočasně změní nastavení Windows (motiv, Nerušit, měřítko, monitory, tray). Tento úkol dodá **společnou infrastrukturu pro takové zásahy**: varování s odpočtem, pojistku v testech, záznam původního stavu a návrat, a to i po neúspěšném testu, pádu nebo zabitém běhu. Dodá také první jednoduché zásahy (motiv, viditelnost tray ikony, Nerušit). Ostatní úkoly řady (T-066 až T-073) na něm staví.

Tento úkol obsahuje i **přehled pokrytí** (níže): ke každé otevřené ruční kontrole devíti úkolů uvádí, který nový úkol ji pokryje, nebo proč zůstává ručně.

## Rozhodnuto
- **Rozsah automatizace (uživatel 2026-10-22):** co nejvíc. Testy smí měnit nastavení systému (světlý/tmavý motiv hlavního panelu i aplikací, centrum oznámení / Nerušit, měřítko 100/125/150 %, odpojení monitoru), pokud to jde automatizovat. Co automatizovat nejde, zůstane jako **doporučená ruční kontrola**, která neblokuje uzavření úkolů. Seznam s důvody je níže.
- **Návrat do původního stavu (uživatel 2026-10-22):** každá změna, kterou test udělá (i nastavení systému), se **vždy** vrátí do původního stavu, i po neúspěšném ověření, pádu nebo zabitém běhu. Testy nic nezanechají: soubory, pověření, toasty v historii centra oznámení, zástupce, hodnoty registru, motiv ani konfiguraci monitorů.
- **Varování (uživatel 2026-10-22):** před prvním testem, který mění nastavení systému, se ukáže viditelné varování, **co se automaticky změní**. Běh začne po 5s odpočtu. Během odpočtu jde běh přerušit Ctrl+C.
- **Výchozí chování skriptu (uživatel 2026-10-22, dřív Q1):** testy se zásahy do systému běží v `tools/run-e2e.ps1` ve výchozím stavu (s varováním a 5s odpočtem). Přepínač `-NoSystemChanges` je přeskočí.
- **Jazyk varování (uživatel 2026-10-22, dřív Q2):** anglicky, stejně jako ostatní výstup skriptů v `tools/`.
- **Předčítání (uživatel 2026-10-22):** kontrola vlastnosti UI Automation `Name` (a dalších, ze kterých Předčítání čte) se počítá jako automatizace. Skutečný hlas Předčítání se automaticky neověřuje (viz „Zůstává ručně“).

## Rozsah

### 1. Záznam zásahů a návrat (`SystemChangeJournal`)
- Každý zásah (motiv, Nerušit, měřítko, monitor, propagace tray ikony, zástupce a registr toastů z T-068) je samostatný typ se třemi operacemi: **přečíst současný stav**, **použít testovací stav** a **vrátit původní stav**. Zásah také ověří, že návrat proběhl (přečtený stav = původní).
- **Před** změnou se původní stav zapíše do trvalého záznamu („čeká na návrat“) v počítačově společné složce, např. `%LOCALAPPDATA%\PrusaTrayDiagnostic.E2E.Restore\<typ>.json`. Stav systému je společný pro všechny worktree a běhy už serializuje zámek plochy `PrusaTrayDiagnostic.App.E2E.Desktop` z T-062. Záznam se smaže až po ověřeném návratu. Prázdná složka se smaže.
- Pořadí je důležité: **nejdřív se uklidí staré záznamy, teprve pak se čte původní stav.** Jinak by se po pádu jako „původní“ zapsal stav nastavený testem.
- Návrat probíhá v několika vrstvách:
  1. `finally` / `DisposeAsync` zásahu v testu, i po neúspěšném ověření;
  2. úklid při startu běhu: `E2EFixture.InitializeAsync` vrátí všechny čekající záznamy (pod zámkem plochy) dřív, než cokoli spustí. Totéž udělá `tools/run-e2e.ps1` před varováním a vypíše, co vracel;
  3. `finally` skriptu `tools/run-e2e.ps1`: PowerShell ho provede i po Ctrl+C. Zavolá úklid stejně jako `-RestoreOnly`;
  4. **hlídací proces:** před prvním zásahem běhu fixture spustí oddělený proces (např. `pwsh -File tools/run-e2e.ps1 -RestoreOnly -AfterProcess <PID testů>`). Ten počká na konec procesu testů, ať skončí normálně, nebo byl zabit, a provede úklid. Chrání i tehdy, když někdo zabije zároveň skript i testy. Když nic nečeká, nic nedělá;
  5. `tools/run-e2e.ps1 -RestoreOnly`: ruční úklid bez spuštění testů (např. po restartu počítače uprostřed běhu).
- Když návrat selže, záznam zůstane, test i běh skončí chybou a skript vypíše, co se nepodařilo vrátit a jak to vrátit ručně (odkaz na `docs/troubleshooting.md`).
- Úklid jde spustit samostatně. Úklid bez čekajících záznamů nic nemění. Úklid nepotřebuje sestavenou aplikaci, jen testovací projekt.

### 2. Varování a pojistka (`tools/run-e2e.ps1` + testy)
- Skript po sestavení a **před spuštěním testů** vypíše výrazné varování (barevně, **anglicky**, rozhodnutí 2026-10-22). Obsahuje:
  - seznam zásahů, které vybrané testy mohou udělat: světlý/tmavý motiv hlavního panelu a aplikací; vypnutí Nerušit; měřítko primárního i dalšího monitoru (100/125/150 %); dočasné vypnutí druhého monitoru; testovací toasty v centru oznámení a dočasný zástupce „Prusa Tray Diagnostic E2E“ v nabídce Start (Windows si může trvale pamatovat aplikaci `PrusaTrayDiagnostic.E2E` v databázi oznámení, přijato 2026-10-22, T-068); zobrazení testovací tray ikony přímo na hlavním panelu; dočasné položky `PrusaTrayDiagnostic.E2E/…` ve Správci pověření;
  - upozornění, že během běhu se nemá používat myš ani klávesnice a okna se budou přesouvat a měnit velikost;
  - jak běh vrátit, kdyby se něco pokazilo: `tools/run-e2e.ps1 -RestoreOnly`, ručně Win+P → Rozšířit, Nastavení → Zobrazení → Měřítko, Přizpůsobení → Barvy.
- Pak následuje **odpočet 5 s** (5, 4, 3, 2, 1) s textem „Ctrl+C = přerušit“. Teprve po odpočtu skript nastaví proměnnou `PTD_E2E_SYSTEM_CHANGES=1` a spustí testy. Proměnnou v `finally` odstraní.
- **Pojistka v testech:** každý test, který mění systém, má trait `SystemChanges` a jako první volá `SystemChanges.Require()`. Bez `PTD_E2E_SYSTEM_CHANGES=1` (např. spuštění z IDE jen s `PTD_E2E=1`) se test **přeskočí** s důvodem „spusť přes tools/run-e2e.ps1, který ukáže varování“. Zásah nejde vytvořit jinak než přes `SystemChanges` (konstruktor zásahů je interní a kontroluje stejnou podmínku), takže ho žádný test neudělá bez varování.
- Ve výchozím stavu testy se zásahy běží (rozhodnutí 2026-10-22). Přepínač `-NoSystemChanges`: bez varování a bez odpočtu, testy se zásahy se přeskočí.
- Varování se ukáže jen tehdy, když vybrané testy (`-Class`, `-Method`) obsahují test s traitem `SystemChanges`. Seznam testů jde zjistit předem přes `--list-tests` s filtrem traitu. Když to nepůjde spolehlivě, ukáže se vždy (zapsat do poznámek).
- Druhý průchod proti Native AOT (T-073) už varování neopakuje. Varování na začátku pokrývá celý běh skriptu a tento rozsah uvádí.

### 3. První zásahy (ostatní dodají T-066 a T-068)
- **Motiv** (`ThemeChange`): `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`, hodnoty `SystemUsesLightTheme` (hlavní panel, Start, oznamovací oblast) a `AppsUseLightTheme` (aplikace), plus rozeslání `WM_SETTINGCHANGE` s `"ImmersiveColorSet"`, aby se Průzkumník a aplikace přepnuly bez odhlášení. Původní stav zahrnuje i „hodnota chybí“. `ColorPrevalence` (barva zvýraznění na panelu) se nemění, jen se zapíše do výsledků, protože ovlivňuje barvu panelu. Ověření: po přepnutí se barva pozadí hlavního panelu u oznamovací oblasti (snímek) změní na světlou/tmavou do 5 s. **Pozn.:** hodnoty registru nejsou dokumentované API, ale stejné hodnoty zapisuje aplikace Nastavení. Pokud se Průzkumník nepřepne, test zásahu selže s popisem.
- **Testovací tray ikona na hlavním panelu** (`TrayIconPromotionChange`): `HKCU\Control Panel\NotifyIconSettings\<id>`, hodnota `IsPromoted = 1` jen u položky testovaného exe. Stejný mechanismus jako T-061, který je v testovacím režimu vypnutý. Původní stav každé dotčené položky (včetně „hodnota chybí“) se vrátí. **Položky, které Průzkumník během běhu vytvořil pro testovaný exe a předtím neexistovaly, se po běhu smažou**, pokud to Průzkumník dovolí. Pokud je přepíše zpět, zapsat do poznámek a zeptat se uživatele. Debug exe je i exe vývojářova `dotnet run`, proto se nemažou položky, které existovaly před během. Díky propagaci jde ikona fotit přímo na hlavním panelu, nejen v okně skrytých ikon.
- **Nerušit** (`DoNotDisturbChange`): zjištění a vypnutí režimu Nerušit (Windows 11) a jeho vrácení. Veřejné API pro zápis neexistuje (viz Výzkum), proto se ovládá přes UI Automation přepínače „Nerušit“ v centru oznámení (Win+N). Záloha: stránka `ms-settings:notifications`, ale jen když aplikace Nastavení neběží, jinak by test přepsal uživateli otevřenou stránku. Stav se přečte z přepínače. Ověří se, že se po vrácení přepínač shoduje s původním stavem. Automatická pravidla Nerušit (celá obrazovka, duplikace displeje, hry) se nemění. Test toastů (T-068) jen ověří, že během něj Nerušit vypnuté zůstalo.

### 4. Samotesty infrastruktury (běží jen v E2E běhu, se zásahy jen s potvrzeným varováním)
- Každý zásah: použít → ověřit → vrátit → ověřit původní stav.
- Simulovaný pád: podřízený proces (testovací exe s filtrem jedné testovací metody) zapíše záznam, udělá zásah a je zabit (`Kill`). Úklid v rodičovském procesu (vrstva 2) a hlídací proces (vrstva 4) vrátí původní stav. Ověří se obě vrstvy.
- Záznam s „původním“ stavem z předchozího pádu: nový zásah nejdřív vrátí starý záznam a teprve pak čte původní stav.
- Pojistka: bez `PTD_E2E_SYSTEM_CHANGES` se test se zásahem přeskočí a nic nezmění.
- Skript: varování a odpočet, Ctrl+C během odpočtu nespustí testy a nic nezmění, `-RestoreOnly`, `-NoSystemChanges`. Ověřit ručně jednou při implementaci (skript) a zapsat do poznámek. Ctrl+C se automatizovat nedá spolehlivě.

### 5. Dokumentace
- `docs/troubleshooting.md`: oddíl „Přerušený E2E běh vrátil nastavení jen zčásti“: `-RestoreOnly`, ruční kroky pro každý zásah, kde leží záznamy.
- Nápověda skriptu (`.DESCRIPTION`, `.PARAMETER`) a `docs/prerequisites.md`, pokud se změní požadavky (např. 2 monitory pro část testů).

## Výzkum proveditelnosti (2026-10-22, architect)
| Zásah | Jak | Zdroj / stav | Co nejde |
|---|---|---|---|
| Motiv hlavního panelu a aplikací | registr `Personalize` + `WM_SETTINGCHANGE "ImmersiveColorSet"` | nedokumentované hodnoty, které zapisuje Nastavení; běžně používané | – |
| Nerušit (Windows 11) | UI Automation přepínače v centru oznámení / v Nastavení | veřejné API pro zápis není; `SHQueryUserNotificationState` (https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryusernotificationstate) vrací jen obecný stav „neobtěžovat“ a nemusí přesně odpovídat přepínači Nerušit – ověřit při implementaci | stav se čte z přepínače, ne z API |
| Měřítko jednoho monitoru za běhu | `DisplayConfigGetDeviceInfo` / `DisplayConfigSetDeviceInfo` s **nedokumentovanými** typy paketů −3 / −4 (relativně k doporučenému měřítku) – ukázka https://github.com/lihas/windows-DPI-scaling-sample (Unlicense); dokumentace funkce: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfigsetdeviceinfo | nedokumentované, může se změnit s aktualizací Windows; hodnota se ukládá do `HKCU\Control Panel\Desktop\PerMonitorSettings` (přežije restart → nutný návrat) | **systémové DPI** (`GetDpiForSystem`) je DPI primárního monitoru při přihlášení a do odhlášení se nemění (https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows). Co aplikace odvozuje od systémového DPI (bitmapy ikon v tray menu, `SystemDpiNative`, T-039), jde při jiném měřítku ověřit jen po odhlášení |
| Odpojení monitoru | `QueryDisplayConfig` + `SetDisplayConfig` s vlastní sadou cest bez druhého monitoru, bez `SDC_SAVE_TO_DATABASE` (po restartu Windows použijí uloženou konfiguraci s oběma monitory) – https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig | dokumentované | fyzické vytažení kabelu (jiný průběh událostí ovladače) – výsledek pro aplikaci je stejný (monitor zmizí ze `Screens`) |
| Blikání tlačítka na panelu | `RegisterShellHookWindow` + zpráva `HSHELL_FLASH` – https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registershellhookwindow | dokumentované | – |
| Historie toastů | `ToastNotificationHistory.Clear(appId)` – https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.toastnotificationhistory.clear | dokumentované (detail v T-068) | záznam aplikace v databázi oznámení Windows možná nejde odstranit; přijato a zdokumentuje se (T-068, rozhodnutí 2026-10-22) |
| Předčítání | UI Automation `Name`, `AccessKey`, `HelpText`, `LiveSetting` | rozhodnutí uživatele 2026-10-22 | API pro to, co Předčítání skutečně řekne, neexistuje |

## Přehled pokrytí ručních kontrol (všech devět úkolů)
| Úkol | Otevřená ruční kontrola | Pokryje | Jak / proč ne |
|---|---|---|---|
| T-010 | Kliknutí na toast (banner) otevře web tiskárny | T-068 | skutečný toast testovací instance, klik přes UIA, zachycená adresa proxy + GET přes proxy |
| T-010 | Kliknutí na toast v centru oznámení, dokud aplikace běží | T-068 | Win+N, UIA |
| T-010 | (volitelně) toast v novém jazyce bez restartu | T-068 | text toastu přes UIA |
| T-010 | (volitelně) zvuk jen u chyby a „vyžaduje pozornost“ | **zůstává doporučená** | slyšitelnost zvuku nejde spolehlivě ověřit (ztlumený výstup, jiné zvuky); XML s `audio` ověřují unit testy |
| T-025 | exe v Průzkumníku | T-070 | ikona přes shell (`SHGetFileInfo` / `IShellItemImageFactory`) = co ukazuje Průzkumník, porovnání s `app-icon.ico` |
| T-025 | hlavní panel, titulky oken | T-070 | snímek tlačítka na panelu (obdélník z UIA) a titulku, porovnání s položkou `.ico` dané velikosti |
| T-025 | tray na světlém i tmavém panelu, 100 % a 150 % | T-070 | T-065 motiv + T-066 měřítko + propagace ikony; kontrast a velikost ikony ve snímku |
| T-025 | toast s novou ikonou | T-068 | snímek toastu, porovnání ikony aplikace |
| T-025 | stará ikona v cache ikon Windows po aktualizaci | **zůstává doporučená** (T-017) | týká se aktualizace instalované verze, ne této verze |
| T-035 | okno na zvoleném monitoru a v rohu (normální, náhled) | T-071 | 5 umístění × 2 režimy na druhém monitoru, otevřené znovu (ne jen „Použít“) |
| T-035 | celá obrazovka na zvoleném monitoru | T-071 | počáteční režim Celá obrazovka + druhý monitor |
| T-035 | odpojený monitor → hlavní, v nastavení „nepřipojený“ | T-071 | (a) neexistující monitor v `settings.json` bez zásahu, (b) skutečné vypnutí monitoru (T-066) |
| T-035 | skutečné názvy monitorů v nastavení | T-071 | porovnání s názvy z `QueryDisplayConfig` zjištěnými testem |
| T-035 | okno při otevření neposkočí (125/150 %) | T-071 | WinEvent hook: žádná změna polohy po prvním zobrazení, při 100/125/150 % |
| T-035 | 2 stejné monitory: dvě položky, otevře se na zvoleném | T-071 | jen na počítači se dvěma stejnými monitory (tento počítač: 2× DELL U2719D), jinak přeskočeno |
| T-035 | mezera přesně 16 DIP i při 125/150 % | T-071 | `DWMWA_EXTENDED_FRAME_BOUNDS` vs. pracovní plocha při 125/150 % (T-066) |
| T-035 | názvy monitorů v AOT binárce | T-073 | stejný test proti Native AOT |
| T-037 | okno zůstane nad jinou aplikací, po vypnutí volby ne (normální, náhled) | T-071 | pomocné okno jiného procesu přes okno kamery, `WindowFromPoint` + pořadí z-order, změna volby a Uložit v otevřeném okně |
| T-037 | celá obrazovka nikdy navrchu | T-071 | totéž v režimu celé obrazovky |
| T-039 | ilustrace v tray menu při 100 % | T-069 | snímek menu, porovnání s ilustrací modelu 16 px |
| T-039 | ilustrace v tray menu při 150 % | T-070 | jen ostrost, když je systémové DPI relace 150 % (viz Zůstává ručně); jinak přítomnost a velikost |
| T-039 | tlačítko obrázku: nabídka, model, vlastní obrázek z telefonu (otočený), Uložit/Zrušit | T-069 | skutečný dialog Otevřít (start v testovací složce, T-067), vygenerovaný JPEG s EXIF orientací |
| T-039 | Diagnostika: obrázek v záhlaví | T-069 | snímek záhlaví, porovnání s ilustrací |
| T-039 | po uložení se obrázek v menu změní bez „Připojování…“ | T-069 | tray menu před a po uložení |
| T-041 | ruční ověření na Windows (Segoe UI, DPI 125/150 %, tmavý motiv) | T-071 | bez posuvníku v 8 jazycích při 125/150 %; tmavý motiv (T-065); Esc a Tab skutečnou klávesnicí |
| T-041 | celkový dojem | **zůstává doporučená** | estetický úsudek |
| T-042 | 1 vzhled podmenu, ikony jen u tiskáren | T-069 | snímek + pixelová kontrola sloupce ikon |
| T-042 | 2 podtržení a funkce zkratek (klávesnicí) | T-069 | otevření menu klávesnicí (Win+B, šipky, Shift+F10), písmeno zkratky, `AccessKey` přes UIA; podtržení porovnáním snímků |
| T-042 | 3 tooltip s celým názvem | T-069 | najetí skutečnou myší, okno tooltipu přes UIA |
| T-042 | 4 „Přidat tiskárnu…“ s prázdným nastavením | T-069 | skutečné tray menu |
| T-042 | 5 Předčítání u tiskárny s `_` v názvu | T-069 | UIA `Name` položky (rozhodnutí uživatele 2026-10-22) |
| T-042 | 6 „Diagnostika…“ zakázaná při „Spouštění…“, změna stavu nezavře otevřené menu | T-069 | Debug-only zpoždění startu (T-067), menu otevřené během přepnutí |
| T-042 | 7 položka kamery bez zaškrtnutí, Zobrazit/Skrýt, zavření křížkem | T-069 | doplnění k existujícímu `TrayMenuTests` |
| T-043 | jump list okna kamery / nastavení / diagnostiky / O aplikaci jen „Zavřít okno“ | T-072 | pravý klik na tlačítko na panelu, UIA nabídky |
| T-043 | druhé spuštění z jump listu | T-072 | jump list nemá položku spuštění (PreventPinning) – test ověří, že tam není |
| T-043 | druhé spuštění dvojklikem / ze Startu (přenese okno, jinak Nastavení; blikání) | T-072 | `ShellExecute` zástupce `.lnk` v testovací složce s právem popředí i bez něj; blikání přes `HSHELL_FLASH` |
| T-043 | skutečný klik v nabídce Start | **zůstává doporučená** | proces spuštěný Průzkumníkem dědí jeho prostředí bez přepínačů testovacího režimu, takže by běžel v produkčním režimu nad daty uživatele; izolovat to nejde |
| T-052 | celá E2E sada v Debug i v AOT: tray, okna, toasty, Správce pověření, kamera (FFmpeg) | T-073 | druhý průchod proti Native AOT exe konfigurace Debug + nový test Správce pověření |
| T-052 | web přes proxy | ověřeno ručně 2026-10-21 (Debug i AOT); navíc T-068/T-069 | GET přes proxy po kliknutí na toast a „Otevřít PrusaLink“ |

## Zůstává ručně (doporučené kontroly, neblokují uzavření úkolů)
1. **Tray menu při 150 % po novém přihlášení** (T-039, T-025): bitmapy ikon menu se vybírají podle systémového DPI, které se mění jen odhlášením. Test T-070 to ověří automaticky, když relace začala při 150 %. Ručně stačí odhlásit se, přihlásit při 150 % a spustit `tools/run-e2e.ps1` s filtrem na testy T-070.
2. **Skutečný klik v nabídce Start** (T-043): viz tabulka, nejde izolovat od dat uživatele.
3. **Hlas Předčítání** (T-042 bod 5, T-049): API pro skutečný výstup neexistuje, ověřuje se UIA `Name`.
4. **Slyšitelný zvuk toastu** (T-010, volitelné).
5. **Celkový dojem** oken a menu (T-041, T-044).
6. **Fyzické odpojení kabelu monitoru** (T-035): automaticky se monitor vypne přes `SetDisplayConfig`, pro aplikaci se stejným výsledkem.
7. **Krátké proklikání Release Native AOT** (T-052): testovací režim existuje jen v Debug. Kritérium AOT uzavře Native AOT v konfiguraci Debug (rozhodnutí uživatele 2026-10-22, T-073).
8. macOS části (T-018, T-019, T-020, T-038, T-053) – mimo tuto řadu.

## Akceptační kritéria
- [ ] `SystemChangeJournal`: záznam před změnou, návrat v `finally`, úklid při startu fixture i skriptu, hlídací proces, `-RestoreOnly`; staré záznamy se vrátí dřív, než se čte nový původní stav (testy)
- [ ] Simulovaný pád (zabitý podřízený proces se zásahem) vrátí původní stav úklidem i hlídacím procesem (test)
- [ ] `tools/run-e2e.ps1`: testy se zásahy běží ve výchozím stavu; anglické varování se seznamem zásahů a návodem k ručnímu návratu, odpočet 5 s s možností Ctrl+C, `PTD_E2E_SYSTEM_CHANGES` jen po odpočtu, `-NoSystemChanges` (bez varování, testy se zásahy přeskočené), `-RestoreOnly`, úklid v `finally` (ověřeno ručně, zapsáno do poznámek)
- [ ] Pojistka: test se zásahem bez potvrzeného varování se přeskočí a nic nezmění (test); zásah nejde vytvořit mimo `SystemChanges`
- [ ] Zásahy motiv, propagace tray ikony a Nerušit s návratem a ověřením (samotesty); původní hodnoty včetně „chybí“
- [ ] Po úspěšném i neúspěšném běhu jsou motiv, Nerušit, `NotifyIconSettings` a složka záznamů jako před během (test na konci běhu porovná snímek stavu ze začátku)
- [ ] `docs/troubleshooting.md` a nápověda skriptu aktualizované
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené; nový NuGet balíček žádný (případně licence + poznámka, že E2E projekt se nepublikuje, AOT se ho netýká)

## Otevřené otázky
- (žádné – Q1 a Q2 rozhodnuty 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
(vyplní agent po dokončení)

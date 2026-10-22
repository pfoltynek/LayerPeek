---
id: T-068
title: E2E – toasty: banner, klik otevře web přes proxy, centrum oznámení, jazyk, ikona
status: todo
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-065, T-067]
---

## Cíl
Ověřit automaticky poslední ruční kontrolu T-010 (kliknutí na toast otevře web tiskárny) a toast s ikonou aplikace z T-025. Testuje se na skutečném exe v testovacím režimu se zapnutými toasty (T-067, AUMID `PrusaTrayDiagnostic.E2E`). Úkol zahrnuje i úklid toastů po pádu.

## Pokrytí
| Úkol | Kontrola | Test |
|---|---|---|
| T-010 | kliknutí na banner otevře web tiskárny | `ToastBannerClickOpensThePrinterWebThroughTheProxy` |
| T-010 | kliknutí na toast v centru oznámení, dokud aplikace běží | `ToastInTheNotificationCenterOpensThePrinterWeb` |
| T-010 | (volitelně) toast v novém jazyce bez restartu | `ToastFollowsTheLanguageSavedAtRuntime` |
| T-025 | toast s novou ikonou | součást testu banneru (snímek a porovnání ikony) |
| T-010 | (volitelně) zvuk | **zůstává doporučená** (T-065) |

## Rozhodnuto
- **Stopy v databázi oznámení (uživatel 2026-10-22, dřív Q1):** pokud Windows po úklidu dál pamatují aplikaci `PrusaTrayDiagnostic.E2E` v databázi oznámení (`%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db`) nebo v seznamu Nastavení → Oznámení, je to přijatelné, podobně jako záznamy v protokolu událostí z T-046. Stopu je potřeba zdokumentovat: v poznámkách tohoto úkolu, v nápovědě `tools/run-e2e.ps1` a ve varování T-065.
- **Přístup k historii toastů při úklidu (uživatel 2026-10-22, dřív Q2):** Debug-only režim aplikace `--ptd-e2e-clear-toasts` (T-067), který použije vlastní interop aplikace. Cílová platforma testovacího projektu se nemění (žádné `net11.0-windows10.0.x`).

## Rozsah
- **Předpoklady (zásahy T-065, trait `SystemChanges`):** Nerušit vypnuté (`DoNotDisturbChange`). Oznámení systému povolená (`HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications`, `ToastEnabled`) a pro testovací AUMID povolená (`HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\PrusaTrayDiagnostic.E2E`, `Enabled`). Pokud je uživatel vypnul, test je dočasně zapne jako zásah s návratem. Pokud je vypnula zásada skupiny, test se přeskočí s důvodem.
- **Vyvolání události:** simulátor (T-045) se scénářem `printing`, za běhu přepnutý na `paused` / `error` přes `POST /_simulator/scenario/…`. Aplikace pošle toast s názvem tiskárny.
- **Banner:** najít okno toastu přes UIA od kořene plochy (Windows 11: okno nového oznámení v procesu `ShellExperienceHost`; hledat podle názvu aplikace „Prusa Tray Diagnostic E2E“ ze zástupce a podle názvu tiskárny v textu, ne podle lokalizovaného názvu okna). Ověřit titulek = název tiskárny a text podle stavu. Snímek. **Ikona:** výřez ikony aplikace v hlavičce toastu porovnat s položkou `app-icon.ico` odpovídající velikosti (tolerance, pozadí toastu). Klik: `Invoke` přes UIA, a když nejde, skutečná myš.
- **Ověření webu:** v `opened-urls.txt` (T-067) se objeví adresa proxy tiskárny (`http://127.0.0.1:<port>/<token>/…`). Test ji otevře přes `HttpClient` a dostane stránku simulátoru („PrusaLink simulator – …“, `StatusPage`) bez dotazu na heslo. Tím se ověří i proxy (T-028, T-052) proti tomuto buildu.
- **Centrum oznámení:** nechat banner vypršet nebo ho zavřít. Otevřít centrum oznámení (Win+N) a najít skupinu testovací aplikace. Klik na toast → nová adresa v `opened-urls.txt`. Centrum zavřít (Esc).
- **Jazyk:** v nastavení (okno přes testovací příkaz `settings`) přepnout jazyk na Deutsch, Uložit, vyvolat další událost → text toastu je německy (porovnání s `Strings.de.resx` přes známý klíč).
- **Úklid (vše i po pádu, záznamy T-065):**
  - historie toastů testovacího AUMID: během testu ji uklidí aplikace při `quit`. Po pádu (úklid T-065) se spustí testovaný exe v režimu `--ptd-e2e-clear-toasts` (T-067). Ten uklidí historii i testovacího zástupce, test zkontroluje kód 0 a že zástupce zmizel;
  - zástupce `Prusa Tray Diagnostic E2E.lnk` v nabídce Start;
  - klíč `…\Notifications\Settings\PrusaTrayDiagnostic.E2E`, který Windows vytvoří po prvním toastu (smazat, pokud před během neexistoval);
  - případné další stopy (např. seznam aplikací v Nastavení → Oznámení, databáze `wpndatabase.db`) zjistit při implementaci (snímek registru a seznamu aplikací před a po). Co nejde odstranit, se přijímá (rozhodnutí 2026-10-22). Musí to být zdokumentované: co přesně zůstává a kde.
  - Ověřit, že produkční zástupce `Prusa Tray Diagnostic.lnk` a klíč `PrusaTrayDiagnostic` (pokud existují) jsou po běhu byte po bytu stejné.

## Akceptační kritéria
- [ ] Banner toastu testovací instance se zobrazí; klik otevře adresu proxy tiskárny a GET přes proxy vrátí stránku simulátoru (test)
- [ ] Toast v centru oznámení: klik, dokud aplikace běží, otevře web (test)
- [ ] Toast po změně jazyka za běhu je v novém jazyce (test)
- [ ] Ikona aplikace v toastu odpovídá `app-icon.ico` (snímek + porovnání)
- [ ] Nerušit, povolení oznámení, historie toastů, testovací zástupce a klíč registru jsou po běhu i po zabitém běhu jako před ním; produkční zástupce a klíč beze změny (test + samotest pádu s úklidem přes `--ptd-e2e-clear-toasts`); cílová platforma testovacího projektu beze změny
- [ ] Stopy, které nejdou odstranit (záznam `PrusaTrayDiagnostic.E2E` v databázi oznámení / v Nastavení → Oznámení), zjištěné a zdokumentované v poznámkách, v nápovědě `tools/run-e2e.ps1` a ve varování T-065
- [ ] Testy se zásahy mají trait `SystemChanges` a bez varování se přeskočí
- [ ] Kritérium T-010 „Kliknutí na notifikaci otevře detail / web tiskárny“ zaškrtnuté a T-010 přesunutý do `done/` (pokud nic dalšího nebrání)
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; `tools/run-e2e.ps1` zelené

## Otevřené otázky
- (žádné – Q1 a Q2 rozhodnuty 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
(vyplní agent po dokončení)

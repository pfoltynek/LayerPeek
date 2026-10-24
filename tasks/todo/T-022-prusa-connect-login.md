---
id: T-022
title: Prusa Connect – přihlášení k účtu (UI)
status: todo
owner: ui-dev
priority: P3
platform: all
depends_on: [T-005, T-015]
---

## Cíl
Uživatel se z okna nastavení přihlásí ke svému Prusa Account a vybere tiskárny z Connectu, které chce sledovat. Postup přihlášení je popsaný v [docs/api/prusa-connect.md](../../docs/api/prusa-connect.md).

> **Blokováno:** způsob přihlášení závisí na odpovědích uživatele (níže a v T-005). Kritéria platí pro OAuth 2.0 authorization code + PKCE. Konkrétní zachycení kódu (WebView / ruční vložení) doplní architekt po rozhodnutí.

## Rozsah
- V okně nastavení (T-015) akce „Přihlásit k Prusa Connect“ a „Odhlásit“.
- Spuštění autorizace na `https://account.prusa3d.com/o/authorize/` s PKCE a zachycení autorizačního kódu zvoleným způsobem. Výměnu kódu za tokeny dělá kód z T-005.
- Po přihlášení seznam tiskáren účtu (z T-005) s výběrem, které přidat jako `PrinterDefinition` (`ConnectionType.PrusaConnect`, `Address` = UUID).
- Odhlášení smaže refresh token z `ISecretStore`.

## Akceptační kritéria
- [ ] Přihlášení na Windows dokončí výměnu kódu a uloží refresh token přes `ISecretStore` (ověřeno ručně s reálným účtem, popsáno v poznámkách).
- [ ] Zrušení přihlášení nebo chyba (zamítnutí, timeout, neplatný kód) zobrazí srozumitelnou hlášku a nic neuloží.
- [ ] Odhlášení smaže uložený token a tiskárny Connectu přejdou do stavu `Unauthorized`.
- [ ] ViewModel přihlášení je testovatelný bez sítě a bez UI. Testy pokrývají úspěch, zrušení a chybu.
- [ ] Žádný token se nezobrazuje v UI ani nezapisuje do logu.
- [ ] Compiled bindings, bez reflexe. Případný nový balíček (např. WebView) má ověřenou AOT/trim kompatibilitu na Windows i macOS a je uvedený v poznámkách.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- **Odloženo rozhodnutím uživatele (2026-10-15):** Prusa Connect nemá oficiální API pro čtení stavu. Nedokumentované API se zatím nepoužije a úkol se neimplementuje. Případně se požádá Prusa o oficiální přístup. Otázky níže se otevřou, až se úkol obnoví.
- **Blokující:** Jak zachytit autorizační kód?
  - a) vestavěné WebView v pomocném okně, které zachytí přesměrování na `https://connect.prusa3d.com/login/auth-callback?code=…`. Vyžaduje výběr a ověření WebView komponenty pro Avalonia 12 + Native AOT na Windows i macOS.
  - b) systémový prohlížeč + uživatel ručně vloží výslednou URL nebo kód;
  - c) e-mail + heslo (+ TOTP) scrapingem přihlašovacího formuláře (architekt nedoporučuje);
  - d) ruční vložení tokenu (jen pro pokročilé).
- Platí rozhodnutí o `client_id` a o použití nedokumentovaného API z T-005.
- Mají se tiskárny Connectu přidávat výběrem ze seznamu účtu, nebo i ručním zadáním UUID?
- Texty UI: jazyk zatím nerozhodnut (viz CLAUDE.md).

## Poznámky k implementaci

---
id: T-005
title: Prusa Connect klient
status: todo
owner: core-dev
priority: P3
platform: all
depends_on: [T-001, T-002, T-004]
---

## Cíl
Implementovat `IPrinterClient` nad cloudovým Prusa Connect podle [docs/api/prusa-connect.md](../../docs/api/prusa-connect.md). Klient jen čte data, příkazy neposílá.

> **Blokováno:** Prusa Connect nemá oficiální API pro uživatelské aplikace (T-004). Implementovat až poté, co uživatel odpoví na otázky níže. Kritéria jsou napsaná pro doporučenou variantu A (webové API `https://connect.prusa3d.com/app/...`). Při jiné odpovědi je architekt upraví.

## Rozsah
- Projekt `PrusaTrayDiagnostic.PrusaConnect`: HTTP klient, DTO, `JsonSerializerContext` a mapování na doménu.
- Správa tokenů Prusa Account:
  - obnova access tokenu přes `grant_type=refresh_token` na `https://account.prusa3d.com/o/token/`;
  - uložení rotovaného refresh tokenu;
  - access token jen v paměti.
- Výměna autorizačního kódu za tokeny (PKCE S256): funkce v Core/PrusaConnect bez UI. Samotné UI přihlášení je T-022.
- Rozšíření `SecretKeys` o klíč pro účet Connect.
- Konfigurace: `PrinterDefinition.Address` = UUID tiskárny v Connectu.
- Jeden sdílený dotaz na seznam tiskáren účtu za cyklus pollingu, ne dotaz na každou tiskárnu zvlášť.
- Mimo rozsah: kamera (T-014), příkazy tiskárně, UI přihlášení (T-022).

## Akceptační kritéria
- [ ] `IPrinterClient.GetSnapshotAsync` pro tiskárnu s `ConnectionType.PrusaConnect` vrací `PrinterSnapshot` se stavem, úlohou (`FileName`, `ProgressPercent`, `Elapsed`, `Remaining`) a telemetrií (tryska, podložka, `AxisZ`, `SpeedPercent`, `FlowPercent`) podle mapovací tabulky v `docs/api/prusa-connect.md`.
- [ ] Všechny stavy Connectu (`READY`, `IDLE`, `BUSY`, `MANIPULATING`, `PRINTING`, `PAUSED`, `FINISHED`, `STOPPED`, `ATTENTION`, `ERROR`, `OFFLINE`, `UNKNOWN` a neznámá hodnota) mají test mapování.
- [ ] Tiskárna, která v seznamu účtu chybí, vrací snapshot se srozumitelnou `StatusMessage` a nevyhodí výjimku.
- [ ] Více `IPrinterClient` instancí téhož účtu sdílí jeden dotaz `GET /app/printers` za cyklus (test počítá požadavky na falešném handleru).
- [ ] Access token se obnoví před vypršením (podle `exp` z JWT) i po odpovědi `401`. Následuje nejvýš jeden opakovaný pokus. Když obnova selže, výsledek je `PrinterState.Unauthorized`. Testy pokrývají všechny tři případy.
- [ ] Nový refresh token z odpovědi se uloží do `ISecretStore` dřív, než se použije nový access token. Test ověřuje pořadí a i to, že po rotaci se starý refresh token už nepoužije.
- [ ] Access token se nikdy neukládá do `ISecretStore` ani do nastavení a žádný token se neobjeví v logu, výjimce ani `StatusMessage` (test).
- [ ] Překročení limitu velikosti tajného údaje (`ArgumentException` z `ISecretStore.SetAsync`, Windows 2560 B) se ohlásí srozumitelnou chybou, token se neořezává (test s falešným `ISecretStore`).
- [ ] Odpověď `429` respektuje `Retry-After`. Chyby `5xx` a síťové chyby vedou k backoffu a snapshotu `Offline`/`Unknown` se `StatusMessage` (sjednoceno s T-003). Pokrývají to testy.
- [ ] Výměna autorizačního kódu (PKCE S256: `code_verifier` → `code_challenge`) má test s pevně daným verifierem a očekávaným challenge.
- [ ] JSON jen přes zdrojově generovaný `JsonSerializerContext`. JWT payload (`exp`) se čte bez knihovny třetí strany. Žádný nový NuGet balíček (případný nový balíček musí mít ověřenou AOT/trim kompatibilitu a být uvedený v poznámkách).
- [ ] `HttpClient` přes `IHttpClientFactory`. Base URL Connectu a Prusa Account jsou konfigurovatelné kvůli testům.
- [ ] Testy s JSON fixtures v `tests/PrusaTrayDiagnostic.PrusaConnect.Tests/Fixtures/*.json`, bez reálné sítě. Fixtures pocházejí ze skutečných odpovědí účtu uživatele a jsou anonymizované (UUID, sériová čísla, IP, MAC, jména, tokeny). Dokud je uživatel nedodá, vycházejí z komunitní specifikace a v poznámkách je to výslovně uvedeno.
- [ ] `docs/api/prusa-connect.md` aktualizováno podle skutečně ověřených odpovědí.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování.

## Otevřené otázky
- **Odloženo rozhodnutím uživatele (2026-10-15):** Prusa Connect nemá oficiální API pro čtení stavu. Nedokumentované API se zatím nepoužije a úkol se neimplementuje. Případně se požádá Prusa o oficiální přístup. Otázky níže se otevřou, až se úkol obnoví.
- **Blokující:** Je přijatelné použít nedokumentované API Connectu? Možnosti:
  - a) webové API `/app/...` (doporučeno technicky);
  - b) mobilní gateway `connect-mobile-api.prusa3d.com`, až po ověření;
  - c) ne, T-005 odložit;
  - d) nejdřív požádat Prusu o oficiální přístup.
  Podrobně v `docs/api/prusa-connect.md`.
- **Blokující:** Jaký OAuth `client_id`?
  - a) `client_id` webu Connectu (jako komunitní SDK);
  - b) čekat na oficiální `client_id` od Prusy.
  `client_id` PrusaSliceru je nepoužitelný.
- Interval pollingu pro Connect: použít globální interval (výchozí 5 s), nebo vlastní minimum pro cloud (podmínky zakazují přetěžování služby)? Jakou hodnotu?
- Stav `READY` (připravena pro tisk z fronty): mapovat na `Idle`, nebo přidat samostatný doménový stav?
- Jeden účet Connect, nebo víc účtů / týmů?
- Dodá uživatel anonymizované skutečné odpovědi (DevTools prohlížeče na `connect.prusa3d.com`: `GET /app/printers`, `GET /app/printers/{uuid}`, odpověď `/o/token/` bez hodnot tokenů) a délky access a refresh tokenu v bajtech? Bez nich jsou fixtures jen odhad.
- Windows Credential Manager má limit 2560 B na jeden tajný údaj (UTF-8). Doporučení: ukládat jen refresh token pod klíčem pro účet. Skutečnou délku tokenů je potřeba změřit. macOS Keychain je T-021.

## Poznámky k implementaci

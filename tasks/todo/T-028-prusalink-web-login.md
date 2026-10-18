---
id: T-028
title: Otevření webu PrusaLink s přihlášením (odloženo)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-027]
---

## Cíl
Při otevření webu tiskárny z tray menu se nemusí znovu zadávat jméno a heslo. Aplikace je předá prohlížeči.

## Rozsah
- ~~Rozhodnuto 2026-10-18: údaje se předají v URL ve tvaru `http://jmeno:heslo@tiskarna/`.~~ **Nefunguje, viz Otevřené otázky.** Jméno i heslo se procentově zakódují (RFC 3986 userinfo), aby speciální znaky (`@`, `:`, `/`, `#`, `%`, mezera, diakritika…) adresu nerozbily.
- Platí jen pro tiskárny PrusaLink, které mají v `ISecretStore` uložené heslo. Bez hesla, při chybě úložiště tajných údajů nebo u Prusa Connect se otevře holá adresa jako dosud.
- Heslo se načte až v okamžiku kliknutí (ne předem do paměti menu) a nikdy se neloguje. Ani adresa s údaji se nikdy neloguje (logovat jen adresu bez userinfo).
- `PrinterWebAddress.IsSafeToLaunch` dnes adresy s userinfo odmítá. Kontrola bezpečnosti (jen http/https, neprázdný host) musí zůstat. Úprava nesmí dovolit, aby userinfo pocházelo z adresy tiskárny zadané uživatelem. Userinfo smí přidat jen aplikace z uložených údajů.
- `ProcessUrlLauncher`: na macOS/Linux dál jeden argument bez shellu. Na Windows ShellExecute.
- Známá omezení zapsat do `docs/` (česky): Edge/Chrome údaje z URL přijmou, Firefox se zeptá na potvrzení, Safari může varovat. Heslo je vidět v příkazové řádce procesu prohlížeče a může zůstat v historii prohlížeče.

## Akceptační kritéria
- [ ] Testy: sestavení URL se zakódováním speciálních znaků ve jméně i hesle. Bez hesla → holá adresa. Chyba `ISecretStore` → holá adresa a nic nespadne. Prusa Connect → beze změny.
- [ ] Test, že se do logu nedostane heslo ani adresa s userinfo.
- [ ] Adresa tiskárny zadaná s userinfo je dál odmítnutá (stávající validace v nastavení i v `IsSafeToLaunch` pro adresy od uživatele).
- [ ] Ověřeno na Windows (ručně, uživatel): v Edge se web PrusaLink otevře přihlášený bez dialogu.
- [ ] Omezení zdokumentovaná v `docs/`.
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Odloženo 2026-10-18 (uživatel si ověří funkční řešení).** Varianta s údaji v URL byla implementována (větev `feature/T-028-prusalink-web-login`, nesloučená) a v Edge **nefunguje**:
  - Úvodní stránka a skript webu PrusaLink se vracejí bez přihlášení (`200`). Digest výzvu vrací až `fetch("/api/v1/info")` volaný JavaScriptem aplikace (`WWW-Authenticate: Digest realm="Printer API"`).
  - Údaje z URL proto prohlížeč při navigaci nepoužije (nedostane `401`). Chromium navíc odmítne `fetch` relativní adresy na stránce, jejíž adresa obsahuje userinfo („Request cannot be constructed from a URL that includes credentials“). Skript spadne a stránka je prázdná.
  - Možné varianty k ověření: lokální proxy na `127.0.0.1` s náhodným portem a tokenem v cestě, která doplní Digest (web používá relativní cesty odvozené z `window.location.pathname`, takže to půjde). Nebo heslo do schránky s automatickým smazáním. Nebo nechat zapamatování hesla na prohlížeči.

## Poznámky k implementaci
(vyplní agent po dokončení)

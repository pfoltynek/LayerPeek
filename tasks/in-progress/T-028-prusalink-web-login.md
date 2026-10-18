---
id: T-028
title: Otevření webu PrusaLink s přihlášením
status: in-progress
owner: ui-dev
priority: P2
platform: all
depends_on: [T-027]
---

## Cíl
Při otevření webu tiskárny z tray menu se nemusí znovu zadávat jméno a heslo. Aplikace je předá prohlížeči.

## Rozsah
- Rozhodnuto 2026-10-18: údaje se předají v URL ve tvaru `http://jmeno:heslo@tiskarna/`. Jméno i heslo se procentově zakódují (RFC 3986 userinfo), aby speciální znaky (`@`, `:`, `/`, `#`, `%`, mezera, diakritika…) adresu nerozbily.
- Platí jen pro tiskárny PrusaLink, které mají v `ISecretStore` uložené heslo. Bez hesla, při chybě úložiště tajných údajů nebo u Prusa Connect se otevře holá adresa jako dosud.
- Heslo se načte až v okamžiku kliknutí (ne předem do paměti menu) a nikdy se neloguje. Ani adresa s údaji se nikdy neloguje (logovat jen adresu bez userinfo).
- `PrinterWebAddress.IsSafeToLaunch` dnes adresy s userinfo odmítá. Kontrola bezpečnosti (jen http/https, neprázdný host) musí zůstat. Úprava nesmí dovolit, aby userinfo pocházelo z adresy tiskárny zadané uživatelem. Userinfo smí přidat jen aplikace z uložených údajů.
- `ProcessUrlLauncher`: na macOS/Linux dál jeden argument bez shellu. Na Windows ShellExecute.
- Známá omezení zapsat do `docs/` (česky): Edge/Chrome údaje z URL přijmou, Firefox se zeptá na potvrzení, Safari může varovat. Heslo je vidět v příkazové řádce procesu prohlížeče a může zůstat v historii prohlížeče.

## Akceptační kritéria
- [x] Testy: sestavení URL se zakódováním speciálních znaků ve jméně i hesle. Bez hesla → holá adresa. Chyba `ISecretStore` → holá adresa a nic nespadne. Prusa Connect → beze změny.
- [x] Test, že se do logu nedostane heslo ani adresa s userinfo.
- [x] Adresa tiskárny zadaná s userinfo je dál odmítnutá (stávající validace v nastavení i v `IsSafeToLaunch` pro adresy od uživatele).
- [ ] Ověřeno na Windows (ručně, uživatel): v Edge se web PrusaLink otevře přihlášený bez dialogu.
- [x] Omezení zdokumentovaná v `docs/`.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
- **API:** `IUrlLauncher.OpenAsync(Uri)` zůstává pro adresy bez údajů a adresu s userinfo dál odmítá. Nová metoda `IUrlLauncher.OpenWithCredentialsAsync(Uri baseUri, string userName, string password, CancellationToken)` nejdřív ověří základní adresu přes `PrinterWebAddress.IsSafeToLaunch` (http/https, neprázdný host, žádné vlastní userinfo). Teprve pak přidá userinfo z předaných údajů. Userinfo tedy nikdy nepochází z adresy zadané uživatelem.
- **Sestavení adresy:** `PrinterWebAddress.WithCredentials` kóduje jméno i heslo přes `Uri.EscapeDataString` a vkládá je hned za `scheme://`. Vrací `string`: ověřeno, že `Uri.ToString()` vrací zakódovanou diakritiku a mezery zpět na surové znaky (`AbsoluteUri` je nechává zakódované). Testy ověřují `@ : / # % ?`, mezeru, diakritiku, emoji, `%2F` a zpětné dekódování se zachovaným hostem, portem a cestou. `IsSafeToLaunch` odmítá i prázdné userinfo (`http://@host/`). `Uri.UserInfo` je u něj prázdné, ale `@` v adrese zůstává a vzniklo by `user:heslo@@host` (doplněno při revizi).
- **Čtení hesla:** nová třída `Tray/Menu/PrinterWebOpener` čte heslo z `ISecretStore` až při kliknutí. Bez hesla, při jakékoli chybě úložiště, u Prusa Connect nebo u neznámé tiskárny otevře holou adresu. `TrayMenuViewModel` jí předá definici tiskárny jen tehdy, když položka menu ukazuje aktuální adresu své tiskárny. Stará položka po změně adresy tak heslo nikam nepošle. Čtení z úložiště běží přes `Task.Run` mimo vlákno UI, protože `CredRead` nebo Keychain může blokovat. Stejně to dělá `SettingsEditorStore` (doplněno při revizi). Při `Dispose` view modelu se rozpracované otevření zruší a prohlížeč se nespustí.
- **Logy:** zjištěno, že `Process.Start` vkládá do textu `Win32Exception` celé `FileName`, tedy i adresu s heslem. Selhání spuštění s údaji se proto loguje jen jako host, typ výjimky a kód chyby, bez objektu výjimky. Test zachytává logy `PrinterWebOpener` i `ProcessUrlLauncher` (úspěch, selhání spuštění, chyba úložiště) a ověřuje, že v nich není heslo v čisté ani zakódované podobě ani `maker:`/`@host`.
- **Platformy:** Windows ShellExecute (`UseShellExecute = true`, adresa ve `FileName`), macOS/Linux `open`/`xdg-open` s adresou jako jediným prvkem `ArgumentList`, bez shellu. Testy pokrývají obě větve. Na macOS se reálně nic neověřovalo. Navíc tam do T-021 běží `UnsupportedSecretStore`, takže se na macOS otevře vždy holá adresa.
- **Ověřeno lokálně (Windows 11):** `dotnet build` 0 varování, `dotnet test` 1639 úspěšných (po revizi), 0 selhání, 1 přeskočený (stávající). Aplikace se spustila přes `dotnet run` a 25 s běžela bez pádu. Otevření webu v Edge ručně neověřeno, to je kritérium pro uživatele.
- **Dokumentace:** `docs/api/prusalink.md`, oddíl „Otevření webu s přihlášením (T-028)“, včetně známých omezení.
- Žádný nový NuGet balíček. Kód je bez reflexe (AOT).

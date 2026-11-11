---
id: T-182
title: Varování u adresy http:// mimo místní síť (heslo PrusaLink přes nedůvěryhodnou síť)
status: done
owner: core-dev + ui-dev
priority: P2
platform: all
depends_on: []
---

## Cíl
Bezpečnostní revize (2026-11-06, problém B, schváleno uživatelem): PrusaLink používá HTTP Digest s MD5 podle RFC 2069
(bez `cnonce` a `qop`, omezení firmwaru; `src/LayerPeek.PrusaLink/Api/DigestAuthenticator.cs`). Kdo odposlechne
jedinou výměnu (LAN, Wi-Fi), může heslo offline hádat hrubou silou. V místní síti je to přijatelné riziko (zmírnění
v T-183), ale adresa `http://` mimo místní síť posílá odpověď Digest přes cizí sítě. Nastavení tiskárny proto u takové
adresy zobrazí lokalizované varování.

## Rozsah

### Klasifikace adresy (`core-dev`)
- Čistá funkce bez sítě, např. `PrinterAddressExposure.Classify(Uri)` v Core (`System.Net.IPAddress` je v pořádku,
  nejde o HTTP klienta). „Bez varování“ pro:
  - schéma `https` (jakýkoli host),
  - loopback (`127.0.0.0/8`, `::1`, `localhost`),
  - RFC 1918 (`10/8`, `172.16/12`, `192.168/16`),
  - link-local IPv4 `169.254/16`, IPv6 `fe80::/10`,
  - IPv6 ULA `fc00::/7`,
  - IPv4 mapované do IPv6 se vyhodnotí jako IPv4,
  - jména mDNS `*.local`.
- Varování pro `http` se vším ostatním (veřejné IP, plně kvalifikovaná jména mimo `.local`), s výjimkami podle otázek
  1 a 4.
- V App už existuje podobná `LocalNetworkAddress` (T-097, oprávnění „Místní síť“ na macOS), ale s jinými pravidly
  (loopback = ne-místní, jednoslovná jména = místní, multicast). Sdílet logiku jen tehdy, když to nezmění chování T-097;
  jinak samostatná funkce (rozhodne implementace, zapsat do Poznámek).

### UI (`ui-dev`)
- `src/LayerPeek.App/Settings/SettingsViewModel.cs` (podzáložka Tiskárna): pod polem adresy text varování, když
  klasifikace vrátí varování; aktualizuje se při psaní adresy (stejně jako dnešní validace).
- Text: heslo (odpověď přihlášení) jde přes síť, které aplikace nemůže věřit; doporučení `https` přes reverzní proxy
  nebo VPN, odkaz na `docs/` z T-183 jen podle otázky 3. Nový text ve **všech** `Strings.*.resx` (en, cs, de, es, fr,
  it, pl, sk).
- Vzhled podle T-112 (zvýraznění pole a přístupný stav), ale jako varování, ne chyba, pokud otázka 2 nedopadne jinak;
  přístupné pro čtečky obrazovky (`AutomationProperties`).
- Varování neblokuje uložení, pokud otázka 2 nedopadne jinak.

## Akceptační kritéria
- [x] Unit testy klasifikace (Core): `http://192.168.1.5`, `http://10.0.0.1`, `http://172.31.255.255`,
      `http://169.254.1.1`, `http://[fe80::1]`, `http://[fd12::1]`, `http://[::ffff:192.168.1.5]`,
      `http://127.0.0.1:18080`, `http://localhost`, `http://prusa-mk4.local` → bez varování;
      `http://172.32.0.1`, `http://8.8.8.8`, `http://[2001:db8::1]`, `http://printer.example.com` → varování;
      `https://8.8.8.8`, `https://printer.example.com` → bez varování; jednoslovné jméno a CGNAT `100.64/10` podle
      odpovědí na otázky 1 a 4
- [x] Klasifikace nedělá žádný DNS dotaz ani síťový přístup (pokud otázka 1 nedopadne jinak)
- [x] Okno nastavení zobrazí varování u adresy s varováním a skryje ho po opravě adresy (headless UI test
      `LayerPeek.App.UiTests`)
- [x] Text varování ve všech `Strings.*.resx` (`Strings.resx` + cs, de, es, fr, it, pl, sk), přeložený, ne anglický
      zástupný text
- [x] Referenční snímky UI nastavení (Windows) aktualizované, pokud se změnily; macOS snímky → přesunout do T-127
      (nezměnily se: žádný snímek neukazuje adresu mimo místní síť, `SettingsWindowSnapshotTests` na macOS PASS;
      kontrola na Windows → přesunuto do T-125, oddíl 2z)
- [x] Pokud `docs/prusalink-security.md` (T-183) už existuje, zmiňuje varování; jinak to T-183 doplní (podle rozhodnutí
      2026-11-11 je dokumentace kapitolou 14 „Zabezpečení přístupu k PrusaLinku“ všech příruček z T-183; úvod kapitoly
      teď varování zmiňuje s odkazem na 3.2, oddíly 3.2 a 3.3 varování popisují a odkazují na kapitolu 14; odkaz
      „Více informací“ v nastavení otevře kapitolu v jazyce aplikace)
- [x] `docs/architecture.md`: kde leží klasifikace
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
**Rozhodnutí uživatele 2026-11-11** (otázky 1–5):
1. **(a) jen podle textu, bez DNS:** jednoslovná jména (bez tečky, i `localhost`) a přípony `.local`, `.lan`, `.home.arpa`
   jsou místní, ostatní jména dostanou varování. IP literály podle rozsahů v Rozsahu.
2. **Informativní:** uložení zůstává povolené, žádný potvrzovací dialog.
3. **Jen v nastavení tiskárny**, s **odkazem na bezpečnostní dokumentaci T-183** (GitHub). Upřesnění 2026-11-11:
   dokumentace bude nová kapitola „Zabezpečení přístupu k PrusaLinku“ ve všech 8 `manuals/<jazyk>/manual.md` (píše ji
   T-183), odkaz vede na kapitolu v jazyce aplikace, mapování jazyk → kapitola na jednom místě.
4. **CGNAT `100.64.0.0/10` dostane varování** (nebere se jako místní).
5. **Stejné varování i na podzáložce Kamera** u adresy RTSP, v tomto úkolu, stejná klasifikace.

**Vyřešeno 2026-11-11:**
6. **Kotvy kapitoly T-183.** T-183 sloučený do `develop` (`84027efd`): kapitola 14 ve všech příručkách, kotvy doplněné
   v `UserManual.SecurityChapterAnchors`, odkaz „Více informací“ se zobrazuje; `UserManualTests` ověřuje, že každá
   kotva je slug nadpisu `##` příručky svého jazyka podle pravidel GitHubu.

**Nové otázky pro uživatele (implementace se jich nedotýká, platí výchozí „varování“):**
7. **Další místní přípony?** Podle rozhodnutí 1 jsou místní jen `.local`, `.lan`, `.home.arpa`. Varování dnes dostanou
   např. `printer.fritz.box` (routery AVM), `.internal` (ICANN vyhradila 2024 pro soukromé sítě), `.localdomain`,
   `.home`, `.corp` a podřízená jména `*.localhost` (RFC 6761: vždy loopback). Přidat některé?
8. **Zastaralé IPv6 site-local `fec0::/10`** (RFC 3879) dnes dostane varování (není v seznamu rozsahů); T-097 ho bere
   jako místní. Ponechat varování?
9. **Text varování.** Návrh (en): „Unencrypted connection outside the local network: the PrusaLink password could be
   intercepted. Use https:// (reverse proxy) or a VPN.“ / kamera: „… the camera image could be intercepted. Use a VPN.“
   (cs: „Nešifrované spojení mimo místní síť: heslo PrusaLink může někdo zachytit. Použijte https:// (reverzní proxy)
   nebo VPN.“). Text musí zůstat nejvýš na dva řádky vedle odkazu (jinak se podzáložka Kamera v němčině nevejde, rezerva
   5 DIP). Schválit, nebo upravit?
10. **(z T-183, otázka 2; jen zapsáno, nerozhodnuto)** Chybová hláška „Otestovat spojení“ při selhání HTTPS
    (`SettingsTestSecureConnection`, en: „The secure (https) connection failed. Try the address with http:// or check the
    printer's certificate.“) radí zkusit `http://`. Za HTTPS reverzní proxy by to šifrování vypnulo (a adresa mimo místní
    síť pak dostane varování z tohoto úkolu). Má hláška místo toho radit zkontrolovat certifikát proxy / důvěru v CA
    (kapitola 14 příručky), nebo zůstat?

Původní otázky:
1. **Jména přeložená přes DNS** (např. `prusa-mk4` bez tečky nebo `printer.home.arpa`, `printer.lan`, `printer.fritz.box`):
   (a) nepřekládat a rozhodnout jen podle textu (jednoslovná jména a vybrané místní přípony jako `.lan`, `.home.arpa`
   brát jako místní, ostatní jako varování), (b) přeložit přes DNS a rozhodnout podle výsledné IP (síťový přístup
   v nastavení, zpoždění, výsledek se může měnit), (c) varovat u všech jmen kromě `.local`. Rozhodne uživatel.
2. **Blokující, nebo informativní?** Doporučení architekta: informativní (uložení povoleno), protože jde o vědomou
   volbu uživatele (např. VPN). Varianta: potvrzovací dialog při uložení.
3. **Kde se varování zobrazí:** jen v nastavení tiskárny, nebo i v okně Diagnostika / v tooltipu tray menu? A má text
   odkazovat na dokumentaci T-183 (odkaz na GitHub)?
4. **Další rozsahy bez varování?** CGNAT `100.64.0.0/10` (Tailscale a jiné VPN) – šifrovaný tunel, ale také rozsah
   poskytovatelů; přidat, nebo varovat?
5. **Kamera:** adresa RTSP kamery může obsahovat jméno a heslo (`rtsp://user:pass@…`, RTSP bez šifrování). Má se
   stejné varování ukázat i na podzáložce Kamera, nebo je to samostatný úkol?

## Poznámky k implementaci
- **Core:** `src/LayerPeek.Core/Network/AddressExposure.cs` – `AddressExposure` (`Encrypted`, `LocalNetwork`,
  `UntrustedNetwork`) a `AddressExposureClassifier.Classify(Uri)` (+ `IsLocal(IPAddress)`, `IsLocalName(string)`). Jen
  `Uri` a `IPAddress`, bez DNS a sítě; IPv4 bajty do `stackalloc`, žádné alokace kromě `DnsSafeHost`. Šifrované je jen
  schéma `https` (RTSP v aplikaci nikdy šifrované není, `rtsps` aplikace nepřijímá). Testy
  `tests/LayerPeek.Core.Tests/Network/AddressExposureClassifierTests.cs` (všechny adresy z kritérií, CGNAT, jednoslovná
  jména, `.lan`, `.home.arpa`, zóna IPv6, mapované IPv4, nepřeložitelná jména).
- **Se `LocalNetworkAddress` (T-097) se logika nesdílí:** pravidla se liší (loopback, multicast/broadcast, `.lan`,
  `.home.arpa`), sdílení by změnilo chování T-097.
- **App:** `PrinterEditorViewModel.Exposure.cs` – `AddressWarning` / `CameraUrlWarning`, `Has…Warning`,
  `ShowsSecurityHelp`, `OpenSecurityHelpCommand`. Klasifikuje se `Uri`, kterou při každé změně pole už vytvořila
  validace (`PrusaLinkAddress.TryCreateBaseUri`, `CameraSettings.TryParseStreamUri`), takže psaní nic navíc neparsuje
  a debounce není potřeba. Pořadí: chyba > varování > nápověda (varování nahrazuje nápovědu, jako chyba v T-033);
  `AutomationProperties.HelpText` pole = text varování; pole má třídu `warning` (2 DIP rámeček v `SettingsWarningBrush`,
  stejné barvy jako `ExperimentalBrush`: `#8A4B00` / tmavý motiv `#FFC56B`), text varování stejnou barvou, 12 px.
  Varování kamery se ukazuje i při vypnuté kameře (hodnotí se adresa, stejně jako nápověda). Adresa kamery nesmí
  obsahovat jméno a heslo (chyba z T-033), varování proto mluví jen o obrazu kamery.
- **Odkaz „Více informací“:** `HyperlinkButton` vpravo od textu varování (12 px, výška 32 DIP podle T-111).
  `SettingsViewModel.Security.cs`: `UrlLauncher` (v `AppServices` produkční `IUrlLauncher`) a `SecurityHelpUri`
  (výchozí `UserManual.SecurityChapterUriFor(jazyk textů)`). Jediné mapování jazyk → kotva je
  `UserManual.SecurityChapterAnchors` (kapitola 14 „Zabezpečení přístupu k PrusaLinku“ z T-183, base adresa jako
  `UserManual.UriFor`, kotva percent-encoded v UTF-8, např. `#14-zabezpe%C4%8Den%C3%AD-…`; jazyk mimo překlady → en).
  `UserManualTests.TheSecurityChapterIsAHeadingOfTheManualOfItsLanguage` čte `manuals/<jazyk>/manual.md` a porovná kotvu
  se slugy nadpisů `##` (malá písmena, mezera → `-`, interpunkce kromě `-` a `_` pryč, písmena mimo ASCII zůstávají),
  takže přejmenovaný nebo přečíslovaný nadpis test shodí. Harness UI testů má pevnou testovací adresu odkazu.
- **Rozložení (měřeno layout testy, macOS, Inter):** sloupec formuláře 548 DIP, nápověda adresy 1 řádek, nápověda
  kamery 2 řádky. Nové stavy v `SettingsWindowLayoutTests.States()`: „address warning + all other errors + refused
  test“, „address warning + name error + longest failed connection test“, „camera warning + credentials removed +
  longest failed camera test“, „camera warning + successful camera test + preview“. Nejmenší zbývající výška ve všech
  stavech: podzáložka Tiskárna 11 DIP (es, fr, it), Kamera 5 DIP (de); bez posuvníku ve všech 8 jazycích. Texty
  varování proto musí zůstat nejvýš na dva řádky.
- **Texty:** `SettingsAddressUntrustedNetwork`, `SettingsCameraUrlUntrustedNetwork`, `SettingsSecurityMoreInfo`,
  `SettingsSecurityMoreInfoHint` ve všech 8 `Strings.*.resx` (otázka 9: znění ke schválení).
- **Příručky:** všech 8 `manuals/<jazyk>/manual.md`, oddíly 3.2 (varování u adresy tiskárny a odkaz na kapitolu 14)
  a 3.3 (kamera, stejný odkaz). Po T-183 jsou kapitoly přečíslované (14 zabezpečení, 15 řešení problémů,
  16 odinstalace); úpravy tohoto úkolu odkazují jen na kapitolu 14.
  Snímky v `docs/images/` se nemění (adresy ve scénáři jsou `*.local`, varování se nezobrazí), proto se negenerovaly.
- **Testy:** `SettingsAddressWarningTests` (App.Tests, view model), `SettingsWindowUntrustedNetworkUiTests` (headless:
  psaní adresy ukáže / skryje varování, rámeček a barva ve světlém i tmavém motivu, nápověda pro čtečku, uložení
  povoleno, odkaz otevře kapitolu, jazyk), `UserManualTests` (kapitola v manuálu svého jazyka).
- **Ověřeno na macOS** (Darwin 27.2) po sloučení `develop` s T-183: `dotnet build` 0 varování, `dotnet test`
  10 426 testů, 0 chyb (267 přeskočených, běžné volitelné testy). Aplikace (`dotnet run`, testovací režim s vlastní
  složkou dat) se spustila a běžela 45 s bez chyby, ale Mac neměl aktivní displej (sezení přes SSH), takže se ikona
  v liště nespustila; okno nastavení je ověřené jen headless testy. Windows (Segoe UI, Narrator) → T-125 oddíl 2z
  (oddíl 2y patří T-183).
- Uzavřeno 2026-11-11: otázky 7–10 jsou otázky na znění a rozsah, neblokují; implementováno výchozí chování podle
  rozhodnutí 1–5 (přípony a rozsahy mimo seznam dostanou varování, text podle návrhu). Případná změna = nový úkol.
- AOT: žádný nový balíček, žádná reflexe, kompilované bindingy.

---
id: T-097
title: macOS – oprávnění „Místní síť“ (NSLocalNetworkUsageDescription)
status: in-progress
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-019, T-026]
---

## Cíl
Od macOS 15 potřebuje aplikace pro každé odchozí spojení na adresu v místní síti oprávnění „Místní síť“ (Apple TN3179). Týká se to PrusaLink i RTSP kamery, tedy celé aplikace. Bez popisu v `Info.plist` ukáže macOS obecný dialog. Zjištěno při T-026 (2026-10-29).

## Rozsah
- Klíč `NSLocalNetworkUsageDescription` v `build/macos/Info.plist` (anglický základ) a přeložené texty ve všech 8 jazycích aplikace přes `<jazyk>.lproj/InfoPlist.strings` v `Contents/Resources` (rozhodnuto 2026-10-29: přeložený).
- Návrh textu z T-026: „LayerPeek connects to your Prusa printers and their cameras on your local network to show the print status and the camera stream.“ Texty projde ui-designer.
- Sestavení `.lproj` v kroku `app` (`scripts/release-build.ps1`, `New-MacAppBundle`), podpis bundlu po přidání souborů, `codesign --verify --deep --strict`.
- Chování při odepření oprávnění: srozumitelný stav tiskárny / hláška místo obecné chyby spojení (ověřit, jakou chybu macOS vrací), záznam do logu. Případně odkaz do Nastavení systému → Soukromí a zabezpečení → Místní síť.
- Ověřit, zda macOS s ad-hoc podpisem po aktualizaci aplikace žádá o oprávnění znovu.
- Testy na loopbacku (simulátor) se oprávnění netýkají; procesy spuštěné z Terminálu jsou výjimkou, proto se dialog při vývoji neukáže.

## Akceptační kritéria
- [x] `Info.plist` obsahuje `NSLocalNetworkUsageDescription`, `.app` obsahuje `InfoPlist.strings` pro všech 8 jazyků (test / Pester)
- [x] Podepsaný bundle projde `codesign --verify --deep --strict`
- [ ] Dialog „Místní síť“ ukáže text v jazyce systému (ověřeno uživatelem na Macu)
- [ ] Odepřené oprávnění nezpůsobí pád a je srozumitelně ohlášeno
- [ ] Windows beze změny
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Návrh textu (ui-designer 2026-10-29)
Text se zobrazí pod nadpisem systémového dialogu (anglicky „"LayerPeek" would like to find and connect to devices on your local network.“, přesné znění v ostatních jazycích neověřeno), proto o povolení nežádá, jen vysvětluje proč. Oslovení podle zbytku aplikace (de/es/fr vykání, it tykání; pl bez zájmena), termín pro kameru jako v aplikaci (it „telecamera“). Název oprávnění v Nastavení systému → Soukromí a zabezpečení (ověřeno v lokalizaci macOS 27.2, `SecurityPrivacyExtension.appex`): Local Network / Místní síť / Lokálna sieť / Lokales Netzwerk / Red local / Réseau local / Rete locale / Sieć lokalna; panel: Privacy & Security / Soukromí a zabezpečení / Súkromie a bezpečnosť / Datenschutz & Sicherheit / Privacidad y seguridad / Confidentialité et sécurité / Privacy e sicurezza / Prywatność i ochrona.

| Jazyk (`.lproj`) | `NSLocalNetworkUsageDescription` |
|---|---|
| en (základ v `Info.plist`) | LayerPeek connects to your Prusa printers and their cameras on your local network to show the print status and the camera view. |
| cs | LayerPeek se připojuje k vašim tiskárnám Prusa a jejich kamerám v místní síti, aby mohl zobrazit stav tisku a obraz z kamery. |
| sk | LayerPeek sa pripája k vašim tlačiarňam Prusa a ich kamerám v lokálnej sieti, aby mohol zobraziť stav tlače a obraz z kamery. |
| de | LayerPeek verbindet sich mit Ihren Prusa-Druckern und deren Kameras im lokalen Netzwerk, um den Druckstatus und das Kamerabild anzuzeigen. |
| es | LayerPeek se conecta a sus impresoras Prusa y a sus cámaras en la red local para mostrar el estado de la impresión y la imagen de la cámara. |
| fr | LayerPeek se connecte à vos imprimantes Prusa et à leurs caméras sur le réseau local pour afficher l'état de l'impression et l'image de la caméra. |
| it | LayerPeek si collega alle tue stampanti Prusa e alle loro telecamere sulla rete locale per mostrare lo stato della stampa e l'immagine della telecamera. |
| pl | LayerPeek łączy się z drukarkami Prusa i ich kamerami w sieci lokalnej, aby wyświetlać stan drukowania i obraz z kamery. |

Poznámky k implementaci textu: `InfoPlist.strings` v UTF-8 (nebo UTF-16), klíč a hodnota v uvozovkách, apostrof (fr, it) neescapovat, `"` uvnitř hodnoty escapovat. Text není v `Strings.*.resx` (nečte ho aplikace, ale macOS), proto ho nehlídá kontrola úplnosti překladů – přidat test/Pester, že všech 8 souborů existuje a obsahuje klíč. „Prusa“ je jen popisné označení tiskáren (jako „PrusaLink“ v aplikaci), ne logo ani ochranná známka v grafice.

## Otevřené otázky
- ~~Konečné znění textu ve všech jazycích (revize ui-designerem).~~ → navržené znění v 8 jazycích viz „Návrh textu“ výše (revize ui-designer 2026-10-29). Proti návrhu z T-026 jen „camera stream“ → „camera view“ (srozumitelnější pro běžného uživatele, ve všech jazycích „obraz z kamery“). Implementace v tomto úkolu. Texty schválil uživatel (2026-10-29).
1. **Texty hlášky v aplikaci `LocalNetworkAccessMayBeOff` (8 jazyků) – revize.** Nový řetězec v `Strings.*.resx`, přeložil ui-dev s ověřenými názvy panelu a oprávnění z „Návrhu textu“; ui-designer ani uživatel ho zatím neviděli. en: „No connection. Local Network access for LayerPeek may be turned off in System Settings > Privacy & Security > Local Network.“, cs: „Bez spojení. LayerPeek možná nemá povolený přístup k místní síti v Nastavení systému > Soukromí a zabezpečení > Místní síť.“ (ostatní jazyky v resx). Je znění a délka v pořádku (okno kamery, malý náhled, výsledek testu v nastavení)?
2. **Kde hlášku ukazovat.** Teď: detail stavu v okně Diagnostika, výsledek „Otestovat připojení“ a testu kamery v nastavení, stavový řádek okna kamery; log. Menu v liště a tooltip ukazují dál jen stav „Offline“. Má ji mít i menu / tooltip, případně jednorázové upozornění?
3. **Tlačítko „Otevřít Nastavení systému“ (panel Místní síť).** Neimplementováno: adresa panelu (`x-apple.systempreferences:com.apple.preference.security?Privacy_LocalNetwork`) není ověřená na macOS 15–27 a otevírat Nastavení systému jsem tu nesměl. Přidat podle vzoru T-020/T-019 (kam: Diagnostika, okno kamery, výsledek testu)?
4. **Které adresy se počítají za místní síť.** Hláška jen u IPv4 10/8, 172.16/12, 192.168/16, 169.254/16, IPv6 link-local / ULA / site-local, multicast, broadcast a názvů `.local` nebo jednoslovných (`prusa-mk4`). U ostatních názvů (`printer.example.com`) a veřejných IP zůstane „Tiskárna není dostupná“, i když by se v LAN mohly přeložit na místní adresu. Stačí to?
5. **Lokalizace systémových prvků AppKitu.** S `.lproj` složkami a `CFBundleLocalizations` macOS považuje aplikaci za lokalizovanou: systémové prvky (např. standardní dialogy pro výběr souboru, kontextové menu textových polí, pokud je Avalonia bere z AppKitu) budou v jazyce systému místo dosavadní angličtiny, i když je v LayerPeeku nastavený jiný jazyk. Nesměl jsem aplikaci spustit s okny, takže neověřeno. Je to v pořádku?

## Poznámky k implementaci

### Implementace (ui-dev) – 2026-10-29

**Text dialogu (`NSLocalNetworkUsageDescription`).**
- `build/macos/Info.plist`: klíč `NSLocalNetworkUsageDescription` s anglickým textem z „Návrhu textu“ (doslova) a `CFBundleLocalizations` se seznamem jazyků aplikace (`en, cs, sk, de, es, fr, it, pl` = `AppLanguages.Supported`). `CFBundleDevelopmentRegion` = `en` už byl.
- Jediný zdroj překladů: `build/macos/Localizations/<jazyk>.lproj/InfoPlist.strings`, jeden soubor na jazyk aplikace včetně `en` (stejný text jako `Info.plist`). Formát: klasický `.strings` (`"klíč" = "hodnota";`, komentáře `/* */`), **UTF-8 bez BOM** (`plutil -lint` ho přijímá; CFBundle čte UTF-8 i UTF-16, Xcode by soubory jen převedl, nic dalšího nedělá). Apostrof (fr, it) neescapovaný. Názvy `.lproj` = kódy ISO 639-1 (macOS je přijímá stejně jako starší `English.lproj`).
- `New-MacAppBundle` má nový nepovinný parametr `-LocalizationDirectory`: soubory zkopíruje do `Contents/Resources/<jazyk>.lproj/` (bajtově shodné), odmítne kolizi se souborem z publish složky. `Get-MacLocalizationFiles` pustí jen `<jazyk>.lproj/InfoPlist.strings` (cokoli jiného ve složce = chyba).
- Kontroly v kroku `app` (`release-build.ps1`): **před sestavením** `Get-MacLocalizationProblems` (každý jazyk z `AppLanguages.cs` má soubor, v něm neprázdný `NSLocalNetworkUsageDescription` a nic jiného, anglický = `Info.plist`, žádné `.lproj` navíc, `CFBundleLocalizations` = jazyky aplikace; jazyky čte `Get-AppLanguageCodes` přímo z `src/LayerPeek.Core/Settings/AppLanguages.cs`, `.strings` parsuje `ConvertFrom-StringsText`), **po sestavení a před podpisem** `Get-MacPlistLintProblems` (`plutil -lint` na `Info.plist` a každý `.strings` v bundlu). Podpis zevnitř ven se neměnil: `.lproj` jsou zapečetěné prostředky bundlu (změna souboru podpis rozbije, Pester to ověřuje).
- Testy: `AppBundleInfoPlistTests` (C#, všechny OS): klíč v `Info.plist`, `CFBundleLocalizations` = `AppLanguages.Supported`, pro každý jazyk soubor v UTF-8 bez BOM, právě jeden klíč, neprázdný, `en` shodný s `Info.plist`, ostatní přeložené, žádná složka navíc. Pester (`ReleaseTools.Tests.ps1`, „macOS packaging“): kopie `.lproj` do bundlu, kontroly problémů (chybějící / prázdný / navíc / jiný anglický text / chybějící klíč v plistu / jiné `CFBundleLocalizations`), parser `.strings`, pořadí kroků ve skriptu a na macOS `plutil -lint`, ad-hoc podpis a `codesign --verify --deep --strict` bundlu s lokalizacemi.

**Odepřené oprávnění.**
- Zjištěno (Apple TN3179 + Apple Developer Forums, 2026-10-29): kontrola je hluboko v síťovém stacku a platí pro BSD sockety i vše nad nimi. Dokud oprávnění není povolené (dotaz čeká nebo bylo zamítnuto), `connect()` hned selže s **EHOSTUNREACH** („No route to host“). Network.framework hlásí čekání s `unsatisfiedReason == .localNetworkDenied`, Bonjour `kDNSServiceErr_PolicyDenied` (-65570); to LayerPeek nepoužívá. .NET to hlásí jako `HttpRequestException` s vnitřní `SocketException` `SocketError.HostUnreachable`, FFmpeg jako `AVERROR(EHOSTUNREACH)` (= -65 na macOS). Stejnou chybu dává i skutečně nedostupná adresa, a macOS neumí stav oprávnění přečíst (žádné API), proto hláška říká „možná“.
- Core (neutrální, bez macOS): `PrinterProblem.HostUnreachable` (nová hodnota na konci výčtu), `CameraFeedException.IsHostUnreachable` a `CameraStatus.IsHostUnreachable` (předává `ReconnectingCameraFeed`). PrusaLink: `PrusaLinkFailure.HostUnreachable`, když je v řetězu vnitřních výjimek `SocketException` s `SocketError.HostUnreachable` (stav Offline, technická zpráva beze změny). FFmpeg: `FFmpegDecoder.IsHostUnreachable` podle errno běžícího OS (macOS 65, Linux 113, Windows 110 nebo 10065).
- App: rozhraní `Platform/ILocalNetworkPrivacy` (`MayBeDenied(host, subject)`); macOS 15+ → `Platform/MacOS/MacLocalNetworkPrivacy` (bez nativních volání), jinak (Windows, macOS 14) `NoLocalNetworkPrivacy` – volba v `AppServices` (`OperatingSystem.IsMacOSVersionAtLeast(15)`). Místní adresa podle `Platform/LocalNetworkAddress` (viz otázka 4; loopback nikdy, takže simulátor a testy se netýkají).
- Kde se ukáže `Strings.LocalNetworkAccessMayBeOff` (8 jazyků, otázky 1–2): okno Diagnostika (detail stavu tiskárny), „Otestovat připojení“ (`ConnectionTestOutcome.LocalNetworkAccessMayBeOff`), test kamery (`CameraTestOutcome.LocalNetworkAccessMayBeOff`) a stavový řádek okna kamery (místo technického „No route to host“; po přepnutí jazyka se přeloží znovu). Jinde „no route to host“ dál zobrazí dosavadní texty („Tiskárna není dostupná“, důvod z FFmpeg).
- Log: `MacLocalNetworkPrivacy` zapíše varování „The connection to printer 'mk4' in the local network failed with "no route to host": Local Network access for LayerPeek may be off (System Settings > Privacy & Security > Local Network).“ jednou za předmět (tiskárna / kamera tiskárny / testovaná kamera) a běh, bez adresy. `LocalNetworkPrivacyObserver` (pozorovatel monitoringu) ho zapíše i bez otevřeného okna.
- Testy (falešné chyby, žádná síť): PrusaLink (snímek i `GetInfoAsync` s `SocketError.HostUnreachable`, jiné chyby ne), FFmpeg (čísla errno, odmítnuté spojení na loopbacku není HostUnreachable), Core (`CameraStatus.IsHostUnreachable`), App (`LocalNetworkAddress`, `MacLocalNetworkPrivacy` včetně logu jednou a souběhu, pozorovatel, oba testery, Diagnostika, okno kamery včetně přepnutí jazyka, texty výsledku testu, rozvržení okna nastavení s nejdelším textem).
- Tlačítko do Nastavení systému neimplementováno (otázka 3).

**Windows.** Bez oprávnění: `NoLocalNetworkPrivacy`, `HostUnreachable` má stejné texty jako `NotReachable` (test `EveryPrinterProblemHasATextInEveryLanguage` + `NoRouteToAPrinterIsNotReachableWhereThereIsNoLocalNetworkPermission`). Jediný viditelný rozdíl: `SupportInfo` (kopírování do schránky / diagnostický balíček) vypíše u takové tiskárny „problem HostUnreachable“ místo „problem NotReachable“. Na Windows jsem nespouštěl (proto kritérium „Windows beze změny“ zatím neodškrtnuté).

**Ověřeno na tomto Macu (macOS 27.2, Apple Silicon), 2026-10-29.**
- `dotnet build`: 0 varování, 0 chyb. `dotnet test` (Debug): **7387 testů, 0 selhání, 177 přeskočeno**. Pester `ReleaseTools.Tests.ps1`: 188 prošlo, 0 selhalo, 1 přeskočen (běží i v `dotnet test` přes `LayerPeek.Scripts.Tests`).
- `caffeinate -d -u -i pwsh scripts/release-build.ps1`: build Release 0 varování, testy Release 7240 / 0 selhání / 139 přeskočeno, AOT publish, smoke test publish složky i `LayerPeek.app` prošel (UIElement, běží 10 s), `codesign --verify --deep --strict`: valid, `.dmg` + `.tar.gz`. Upload by odmítl špinavý strom (očekávané).
- Hotový bundle: 8× `Contents/Resources/<jazyk>.lproj/InfoPlist.strings`, `plutil -lint` OK pro `Info.plist` i všechny `.strings`, `CFBundleLocalizations` = `en, cs, sk, de, es, fr, it, pl`. `NSBundle` (Foundation, jen čtení souborů přes JXA, aplikace se nespouštěla) vidí všechny lokalizace, pro preferenci `cs-CZ` vybere `cs` a texty cs/de/pl/fr přečte správně. Samotný dialog jsem neviděl (spuštění z Terminálu se neptá, TCC se nemění).
- `tools/profile-snapshot.ps1` před a po testech i release buildu: „No change in the watched places of the user profile.“ (Login Items „not checked“ jako dřív).

**Kroky pro uživatele (nešlo ověřit, zákaz změn TCC a instalace):**
1. Podepsaný `artifacts/release/<verze>/app/osx-arm64/LayerPeek.app` zkopírovat do Aplikace, spustit z Finderu (ne z Terminálu: procesy z Terminálu mají přístup automaticky) a přidat / mít tiskárnu v LAN. Dialog „Místní síť“ má ukázat text v jazyce systému (zkusit aspoň cs a en: Nastavení systému → Obecné → Jazyk a oblast, případně jen pro LayerPeek v Aplikace → LayerPeek).
2. Zvolit „Nepovolovat“: aplikace nespadne, Diagnostika / Otestovat připojení / okno kamery mají ukázat hlášku o místní síti, log řádek „Local Network access for LayerPeek may be off“. Tím se ověří i předpoklad, že .NET a FFmpeg dostanou EHOSTUNREACH (ne jinou chybu nebo timeout).
3. Povolit v Nastavení systému → Soukromí a zabezpečení → Místní síť: spojení se obnoví.
4. Po aktualizaci (nový ad-hoc podepsaný build přes stávající) zjistit, zda se macOS ptá znovu (TN3179: ad-hoc podpis identitu spolehlivě nesleduje; hlavní exe má vlastní `LC_UUID`).

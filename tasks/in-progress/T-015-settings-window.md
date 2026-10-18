---
id: T-015
title: Okno nastavení
status: in-progress
owner: ui-dev
priority: P1
platform: all
depends_on: [T-002, T-003]
---

## Cíl
Uživatel přidá/upraví/odebere tiskárnu bez ruční editace souborů.

## Rozsah
- Formulář: název, typ (PrusaLink/Connect), adresa, přihlašovací údaje
- Tlačítko „Otestovat spojení“
- Obecná nastavení (interval, notifikace)
- Registrace `ISecretStore` v composition rootu (App) s výběrem platformní implementace: na Windows `WindowsCredentialSecretStore` (T-002), macOS větev (`Keychain`) doplní T-021. Pokud službu potřebuje zaregistrovat už dřívější úkol (např. při napojení PrusaLink klientů na monitoring), registrace se přesune tam a tento bod se jen ověří.

## Akceptační kritéria
- [x] Validace vstupů, test spojení ukáže srozumitelnou chybu (testy view modelu a testeru; vzhled zkontrolován ve vykreslených obrázcích)
- [x] Tajné údaje se ukládají přes `ISecretStore` (nikdy v `settings.json`; pořadí zápisu a návrat při chybě pokryté testy)
- [x] Composition root vybírá implementaci `ISecretStore` podle OS (explicitní registrace, bez skenování – Native AOT) – ověřeno: registraci zavedl T-007 v `Composition/AppServices`, test `SecretStoreMatchesOperatingSystem`
- [ ] Ověřeno na Windows – **čeká na ruční ověření uživatelem** (okno se otevírá kliknutím v menu; automatizované klikání se nepoužívá), viz „Co ověřit“ v poznámkách
- [x] `dotnet build` a `dotnet test` bez chyb a varování (0 varování, 1510 testů, 1 přeskočený existující)

## Otevřené otázky
- ~~Automatické vyhledání tiskáren v síti (mDNS/Zeroconf) – chceme?~~
- ~~Co má aplikace dělat na OS, kde zatím není implementace `ISecretStore`?~~

**Rozhodnutí (2026-10-16):**
- Automatické vyhledání tiskáren (mDNS) zatím ne, případně samostatný budoucí úkol.
- OS bez implementace `ISecretStore` (macOS před T-021): ukládání hesel v UI zakázané s vysvětlením, aplikace běží dál (`UnsupportedSecretStore` z T-007). **Implementováno.**

**Rozhodnutí uživatele (2026-10-17):** všech 16 níže uvedených otázek je přijato tak, jak jsou implementované (varianta v závorce). Otevřené otázky nejsou.
1. ~~**Unikátní názvy tiskáren:**~~ vyžadovat? (Ano – stejný název u dvou tiskáren je chyba, porovnává se bez mezer na okrajích a bez ohledu na velikost písmen; menu a notifikace by jinak nešly rozlišit.)
2. ~~**Maximální délka názvu:**~~ 100 znaků? (Ano – delší by menu stejně zkrátilo.)
3. ~~**Prusa Connect ve výběru typu připojení:**~~ zobrazit zakázaný, nebo skrýt? (Zobrazený jako „Prusa Connect (zatím není k dispozici)“, nejde vybrat. Existující Connect tiskárna z ručně upraveného souboru se zobrazí a dá se upravit název a adresa; heslo ani test spojení pro ni nejsou.)
4. ~~**Neuložené změny při zavření okna**~~ (křížek, Esc, Ukončit z menu): zeptat se? (Ne – zahodí se bez dotazu, stejně jako Zrušit. Nejjednodušší bezpečné chování: nikdy se neuloží nic, co uživatel nepotvrdil.)
5. ~~**Uložit zavře okno?**~~ (Ano, při úspěchu; žádné samostatné „Použít“. Bez změn Uložit jen zavře okno. Když se nepodaří smazat heslo odebrané tiskárny, okno zůstane otevřené s upozorněním.)
6. ~~**Odebrání tiskárny bez potvrzení?**~~ (Ano – nic se neděje, dokud uživatel neuloží; Zrušit vše vrátí.)
7. ~~**Heslo u nové PrusaLink tiskárny povinné?**~~ (Ne – dá se uložit i bez hesla; test spojení i tray pak hlásí chybějící heslo.)
8. ~~**Smazání uloženého hesla:**~~ prázdné pole znamená „ponechat“, takže heslo nejde jen smazat (PrusaLink heslo vždy vyžaduje). Chcete tlačítko „Smazat heslo“? (Ne.)
9. ~~**Pořadí tiskáren:**~~ nová tiskárna se přidá na konec, přesouvat nejde. Pořadí rozhoduje při shodě závažnosti v ikoně (T-007/T-013). Chcete „Posunout nahoru/dolů“? (Ne.)
10. ~~**Zobrazení hesla**~~ (tlačítko „ukázat“) – chcete? (Ne, pole je vždy skryté.)
11. ~~**Časový limit testu spojení:**~~ 10 s celkem (každý požadavek max. 3 s jako v monitoringu). (Takto.)
12. ~~**Interval dotazování:**~~ povolená desetinná čísla (např. 2,5 s), rozsah 1–300 s podle T-002. (Takto.)
13. ~~**Poškozený / nečitelný soubor nastavení:**~~ okno ukáže výchozí nastavení s upozorněním, že uložení soubor přepíše (podle rozhodnutí T-002 se soubor nezálohuje). Při opravených hodnotách (varování při načtení) upozornění, že se uloží opravené nastavení. (Takto.)
14. ~~**Vzhled okna:**~~ 780 × 680 px, měnitelná velikost, dvě záložky „Tiskárny“ (seznam vlevo, formulář vpravo) a „Obecné“ (interval, upozornění, zbývající minuty, zvuk); přepínače událostí jsou zašedlé při vypnutém hlavním vypínači. Texty viz `Strings.resx` / `Strings.cs.resx` (prefix `Settings`). Schválit? (Viz vykreslené obrázky v poznámkách.)
15. ~~**Id nové tiskárny:**~~ náhodný GUID (32 hex znaků, např. `3f2a…`), v Credential Manageru tedy `PrusaTrayDiagnostic/printer/<guid>`. Čitelnější varianta by byla id odvozené jednou z názvu (např. `mk4-2`), ale i to by se po přejmenování lišilo od názvu. (GUID.)
16. ~~**Změna v projektu PrusaLink (oblast core-dev):**~~ kvůli srozumitelné chybě testu (hlavně TLS) přibyl výčet `PrusaLinkFailure` v `PrusaLinkInfoResult.Failure` a samostatná hláška pro selhání https. Stav tiskárny v monitoringu je beze změny (offline), jen `StatusMessage` nově říká „secure (https) connection … failed“ místo „not reachable“. Ponechat? (Ano.)

## Poznámky k implementaci
- **Okno (`App/Windows/SettingsWindow.axaml`):** položka menu „Nastavení…“ je povolená (`TrayMenuOptions.Default`), `TrayMenuPresenter` ji směruje na nové `ITrayAppCommands.ShowSettings`. Jediná instance přes `AuxiliaryWindowSlot` (další klik okno aktivuje), zavření aplikaci neukončí, při „Ukončit“ se okno zavře jako první (neuložené změny se zahodí, rozběhnutý test se zruší). Kompilované bindingy všude (`x:DataType` i v šablonách a ve stylu položek ComboBoxu), texty přes `{x:Static res:Strings…}`, barvy chyb a úspěchu v `ThemeDictionaries` (světlé i tmavé téma).
  - Záložka **Tiskárny:** seznam (neplatná tiskárna má červený „!“), Přidat / Odebrat, formulář: název, připojení, adresa (s nápovědou), heslo (skryté znaky, zástupný text „Beze změny“ / „Nenastaveno“, nápověda pod polem), Otestovat spojení / Zrušit test s indikátorem průběhu a výsledkem (zeleně / červeně).
  - Záložka **Obecné:** interval dotazování (s), hlavní vypínač upozornění, vypínače událostí (zahájen, dokončen, pozastaven, obnoven, zastaven, chyba, vyžaduje zásah vč. odmítnutého hesla, offline/online), „zbývá X minut“ (0–1440, 0 = vypnuto), zvuk (Vypnutý / Jen důležitá / Všechna).
  - Dole stav posledního uložení, Uložit (výchozí tlačítko, Enter) a Zrušit (Esc).
- **View model (`App/Settings/SettingsViewModel`, `PrinterEditorViewModel`):** bez Avalonie, testovaný. Vzniká při otevření okna, data načte až `LoadAsync` (soubor i úložiště hesel mimo UI vlákno), při zavření se uvolní: zruší načítání i test, zapomene napsaná hesla. Zavřené okno nic nestojí.
  - **Validace (inline, CZ + EN):** název povinný, max. 100 znaků, unikátní; adresa přesně podle `PrusaLinkAddress` (tytéž pravidla jako klient a menu – jen http/https, bez jména/hesla, dotazu a fragmentu); heslo podle `PasswordPolicy` (striktní UTF-8, max. 2560 B na Windows – stejně jako `WindowsCredentialSecretStore`). U nové tiskárny se chyby ukážou až po první úpravě nebo při pokusu o uložení. Uložení s chybou vybere první neplatnou tiskárnu (nebo přepne na záložku Obecné) a nic neuloží.
  - **Id:** nová tiskárna dostane při přidání náhodný GUID („N“); nikdy se nepoužije id načtené ani dříve vygenerované tiskárny (bez ohledu na velikost písmen), takže heslo odebrané tiskárny nemůže smazat heslo nové. Přejmenování id nemění – heslo, historie (T-012) i stav notifikací (T-009) zůstanou.
  - **Dirty tracking:** `IsDirty` = sestavené `AppSettings` se liší od uložených (hodnotové porovnání, názvy oříznuté) nebo je napsané nové heslo. Změna tam a zpět není změna.
  - **Hesla:** prázdné pole = ponechat uložené. Bez úložiště hesel (macOS před T-021) je pole zakázané s vysvětlením a hesla se nikdy neposílají k uložení.
- **Ukládání (`App/Settings/SettingsEditorStore`, pořadí a obnova):**
  1. Nová hesla: pro každé se nejdřív přečte předchozí hodnota (jen v paměti pro návrat), pak se zapíše. Když nejde přečíst předchozí hodnotu, nezapíše se nic.
  2. `settings.json` atomicky přes `ISettingsStore`.
  3. Když 1 nebo 2 selže, zapsaná hesla se vrátí (předchozí hodnota, nebo smazání), od posledního. Nastavení tak nikdy neodkazuje na tiskárnu bez jejího nového hesla a nové heslo nezůstane jako sirotek. Selhání návratu se hlásí („heslo se mohlo změnit“).
  4. Nastavení se použije (`ISettingsApplier.ApplyAsync` → restart monitoringu) a uložení počká (nejvýš 5 s), až skončí předchozí běh monitoru – nic už nedotazuje odebranou tiskárnu.
  5. Až potom se smažou hesla odebraných tiskáren (už je nic nepoužívá). Když smazání selže, nastavení je uložené a okno zobrazí upozornění; sirotek v Credential Manageru je neškodný (nic na něj neodkazuje).
  Ukládání nejde zrušit (případné přerušení by stejně potřebovalo návrat) a běží celé mimo UI vlákno. Hesla se nikdy nelogují (logy obsahují jen id tiskárny).
- **Použití nastavení za běhu (`Tray/MonitoringSession.ApplyAsync`):** zruší aktuální běh monitoru (`CancelAsync`, mimo UI vlákno), počká na jeho konec a spustí nový s novým nastavením; pozorovatelé (ikona, menu, notifikace, historie, diagnostika) dostanou nastavení přes `ApplySettings`. Rychle po sobě použitá nastavení se sloučí do posledního. Nastavení použité před dokončením načítání vyhraje nad souborem. Po selhání monitoringu uložení monitoring znovu spustí (a zruší chybu v tray). Po požadavku na ukončení se ignoruje. Notifikátor (T-009) a historie (T-012) si u tiskáren se stejným id, adresou a typem připojení ponechají stav.
- **Test spojení (`App/Settings/PrusaLinkConnectionTester`):** skutečný `PrusaLinkClient.GetInfoAsync` (`/api/version` + `/api/v1/info`) se sdílenou HTTP konfigurací „PrusaLink“ (3 s na požadavek), celkový limit 10 s, zrušitelný. Napsané heslo dostane klient jen v paměti (dočasné read-only `ISecretStore`), do úložiště se nezapisuje; prázdné pole u uložené tiskárny použije uložené heslo, nová tiskárna bez hesla se testuje bez hesla. Neplatná adresa se ohlásí bez síťového přístupu. Výsledky: funguje (kód modelu a firmware), nedostupná, nestihla odpovědět, selhalo https (TLS), odmítnuté heslo, odepřený přístup, chybí heslo, uložené heslo nejde přečíst, nepodporovaný způsob přihlášení, „neodpovídá jako tiskárna s PrusaLink“, Prusa Connect nepodporován, zrušeno. Dvojklik spustí test jen jednou (tlačítko je během testu zakázané), úprava adresy/hesla/typu běžící test zruší a starý výsledek smaže; odebrání tiskárny a zavření okna test zruší.
- **PrusaLink:** `PrusaLinkFailure` (`PrusaLinkInfoResult.Failure`) rozlišuje důvod chyby (viz otázka 16); `DigestAuthenticator` předává důvod (chybí heslo, nepodporované přihlášení). TLS chyba (`HttpRequestError.SecureConnectionError`) má vlastní hlášku.
- **Composition root (`Composition/AppServices`):** explicitně registrované `PasswordPolicy` (podle OS), `ISettingsApplier` (= `MonitoringSession`), `ISettingsEditorStore`, `IConnectionTester`; `CreateSettingsViewModel` pro každé okno. `TrayHost.CreateSettingsViewModel`, `App.ShowSettings`.
- **Lokalizace:** 74 nových textů `Settings*` v `Strings.resx` i `Strings.cs.resx` (test úplnosti a placeholderů prochází).
- **Native AOT:** bez reflexe a bez nových balíčků v aplikaci. `ObservableObject` / `RelayCommand` / `AsyncRelayCommand` (už používané). `scripts/aot-smoke.ps1` prošel (AOT aplikace běžela 10 s; hlášky ILC o Avalonia DComposition jsou starší a nesouvisí). Samotné okno nastavení se v AOT buildu neotevíralo (vyžaduje kliknutí v menu).
- **Testy (nové/změněné):**
  - `App.Tests/Settings/SettingsViewModelTests` (83 případů): načtení, upozornění na poškozený/nečitelný/opravený soubor, zavření během načítání, zástupný text hesla, přidání (nové id, skryté chyby), GUID, id se neopakuje (ani odebrané), přejmenování zachová id, odebrání (výběr souseda), validace názvu/adresy/hesla/intervalu/minut, čeština, uložení všech hodnot vč. zvuku a vypínačů, prázdné heslo = ponechat, změněné heslo, uložení s chybou (výběr tiskárny / záložky), bez změn jen zavře, selhání uložení (3 varianty), nesmazané heslo odebrané tiskárny, jen jedno uložení najednou, Zrušit, zapomenutí hesel při zavření, dirty tracking, macOS bez úložiště, Prusa Connect, test spojení (úspěch, pomlčky, neuložené hodnoty, nová tiskárna bez hesla, všechny chyby, neplatná adresa bez testu, dvojklik, zrušení, úprava během testu, smazání starého výsledku, zavření okna, odebrání tiskárny, neočekávaná výjimka).
  - `App.Tests/Settings/SettingsEditorStoreTests` (22): načtení vč. stavu hesel, selhání úložiště, macOS, pořadí operací (heslo → nastavení → použití → mazání), heslo není v logu, selhání zápisu hesla / druhého hesla / nastavení / návratu / čtení předchozího hesla, mazání hesel odebraných tiskáren jen po úspěchu, nesmazatelné heslo = jen upozornění, přejmenování nemění hesla, id bez ohledu na velikost písmen, nepodporované úložiště se nikdy nevolá, odmítnutí hesel bez úložiště/pro neznámé id, házející `ISettingsApplier`, nedostupné úložiště nastavení.
  - `App.Tests/Settings/PrusaLinkConnectionTesterTests` (13, falešný digest server, bez sítě, `FakeTimeProvider` pro limit): úspěch, napsané vs. uložené heslo, špatné/chybějící/nečitelné heslo, neplatná adresa, Prusa Connect, nedostupná, TLS, „není tiskárna“, časový limit, zrušení volajícím, heslo není v logu.
  - `App.Tests/Settings/PasswordPolicyTests`, `PrusaLink.Tests/PrusaLinkInfoFailureTests` (14), `Tray/MonitoringSessionTests` (+6: restart, sloučení, nastavení před načtením, restart po selhání, po zastavení ignorováno, zastavení během restartu), menu (povolená položka, směrování, zakázané volby), `Composition/AppServicesTests` (registrace).
  - App.Tests nově odkazuje na `Microsoft.Extensions.TimeProvider.Testing` (už schválený v `Directory.Packages.props`, používají ho PrusaLink testy).
  - Testy nesahají na Credential Manager, disk, síť ani procesy (falešná úložiště v paměti).
- **Opravy po review (2026-10-17), vždy nejdřív test:**
  1. *Rozběhnuté uložení:* křížek (nebo Alt+F4) během ukládání okno nezavře a Zrušit je během ukládání zakázané, takže výsledek (hlavně chyba) zůstane vidět (`SettingsViewModel.BlocksClosing`, `SettingsWindow.OnClosing` – blokuje jen zavření uživatelem, ne zavření aplikací). Při „Ukončit“ `TrayHost.StopAsync` nejdřív počká na rozběhnuté uložení (`ISettingsEditorStore.WaitForPendingSaveAsync`, nejvýš 1 s), aby přerušené uložení nenechalo heslo bez nastavení; celkový limit ukončení se tím nemění.
  2. *Falešné upozornění „odmítnuté heslo“ u odebrané tiskárny:* `MonitoringSession.ApplyAsync` vrací úlohu, která skončí, až doběhne předchozí běh monitoru (nic už nedotazuje se starým nastavením); hesla odebraných tiskáren se mažou až potom (nejvýš 5 s čekání, pak se smažou stejně s varováním v logu). Navíc `PrinterMonitor` nepublikuje snapshot, který doběhl až po požadavku na zastavení běhu (Core, `PrinterPollLoop`).
  3. *Jediný výběr podle OS:* `PasswordPolicy.For(ISecretStore)` odvozuje pravidla z úložiště zaregistrovaného v composition rootu (`UnsupportedSecretStore` → hesla nejdou uložit, `WindowsCredentialSecretStore` → 2560 B, jiné úložiště → `NotSupportedException`, aby T-021 nemohl Keychain zapomenout). Test ověřuje registrovanou službu.
  4. *Návrat i při neočekávané výjimce:* kroky 1–2 ukládání spustí návrat hesel při jakékoli výjimce (nejen dokumentované); ani mazání a návrat nevyhodí výjimku ven.
  5. *Bez restartu, když se nemění tiskárny ani interval:* když zůstanou tiskárny (id, název, adresa, typ, pořadí) i interval dotazování, monitor běží dál a pozorovatelé dostanou jen `IMonitoringObserver.UpdateSettings` (výchozí implementace nic nedělá, `PrintNotificationObserver` předá nové nastavení upozornění notifikátoru, který si stav tiskáren ponechá). Ikona ani menu tedy po změně upozornění neblikají na „Připojování…“.
  6. *Přepnutí na Prusa Connect* zapomene napsané heslo (pole je pak zakázané).
  - Nové testy: Core `PrinterMonitorTests.SnapshotFinishedAfterStopIsNotPublished`; App: `MonitoringSessionTests` (`ApplyCompletesOnlyWhenTheOldRunEnded`, `ApplyNeverFaultsAndIsDoneWhenNothingRuns`, `NotificationOnlyChangeKeepsTheMonitorRunning`), `SettingsEditorStoreTests` (čekání na starý běh, zaseknutý běh, neočekávaná výjimka s návratem, čekání na uložení při ukončení; pořadí operací nově včetně konce starého běhu), `PrintNotificationObserverTests.UpdatedSettingsWithoutRestartReachTheNotifier`, `SettingsViewModelTests` (blokované zavření a Zrušit během ukládání, zapomenutí hesla při přepnutí na Connect), `PasswordPolicyTests` (odvození z úložiště), `AppServicesTests` (pravidla z registrované služby).
  - `scripts/aot-smoke.ps1` po opravách znovu prošel.
- **Opravy po 2. kole review (2026-10-17), vždy nejdřív test:**
  1. *Regrese z bodu 5:* uložení, které mění jen heslo (nastavení beze změny), šlo cestou „bez restartu“. Běžící klient si heslo přečte až po dalším odmítnutí a odmítnutá tiskárna je na backoffu, takže by tray zůstal červený i minuty. Nově `ISettingsApplier.ApplyAsync(settings, credentialsChanged)`; úložiště posílá `true`, když se ukládá nějaké heslo, a monitoring se pak restartuje vždy (tiskárna se dotáže hned). Odebrání tiskárny mění nastavení, takže restartuje i bez toho. Testy `SettingsEditorStoreTests.ChangedPasswordForcesAMonitoringRestart`, `MonitoringSessionTests.ChangedPasswordRestartsEvenWithUnchangedSettings`.
  2. *Souběh předání nastavení:* předání nastavení pozorovatelům na začátku běhu a změna jen upozornění jsou serializované vlastním zámkem (`_delivery`, bere se před `_gate`). Začátek běhu předá nejnovější nastavení běhu (i když je mezitím změnila úprava upozornění), úprava upozornění se přeskočí, když ji už nahradilo novější nastavení. Pozorovatelé tak vždy skončí s posledním nastavením. Test `NotificationUpdateDuringRunStartIsNotOverwrittenByOlderSettings` (pozorovatel zablokovaný v prvním `ApplySettings`; ověřeno 5 opakovanými běhy). `ApplyAsync` tedy může krátce počkat, než pozorovatelé převezmou nastavení (jsou rychlé).
- **Ověření na Windows 11:**
  - `dotnet build` 0 varování, `dotnet test` 1494 (1493 prošlo, 1 přeskočený existující); po opravách z review 1510 (1509 prošlo, 1 přeskočený existující).
  - Okno bylo vykresleno mimo repozitář (dočasný harness, okno mimo obrazovku, `RenderTargetBitmap`, bez jakéhokoli vstupu) s falešnými daty: CZ i EN, světlé i tmavé téma – tiskárny s úspěšným testem, chyby validace (duplicitní název, „!“ v seznamu, hláška dole), neúspěšný test, Prusa Connect tiskárna, macOS bez úložiště hesel + poškozený soubor, prázdný seznam, záložka Obecné (zapnutá i vypnutá upozornění, chybějící interval). Vše čitelné, nic se nepřekrývá. Při původní výšce 620 px byl „Zvuk“ až po posunutí, proto výška 680 px.
  - `dotnet run` (bez nastavení): aplikace se spustila a běžela 30 s (stejné PID, ~98 MB), pak byla ukončena; nevytvořila složku `%APPDATA%\PrusaTrayDiagnostic`.
  - **Co ověřit ručně (uživatel):** Nastavení… z menu otevře okno, druhý klik ho jen aktivuje, zavření ani Zrušit aplikaci neukončí; přidání tiskárny se skutečnou adresou a heslem, „Otestovat spojení“ (OK, špatné heslo, vypnutá tiskárna, Zrušit test); Uložit → tiskárna se objeví v tray menu/ikoně bez restartu aplikace; v Credential Manageru (Obecné pověření) je `PrusaTrayDiagnostic/printer/<id>`; úprava bez hesla ponechá heslo; odebrání tiskárny smaže její pověření; přejmenování zachová graf v diagnostice; „Ukončit“ s otevřeným oknem.
- **Platformy:** okno je společné (Avalonia). macOS: pole hesla zakázané s vysvětlením (`PasswordPolicy.Unsupported`), dokud T-021 nedodá Keychain. **Na macOS nic neověřeno.**

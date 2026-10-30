---
id: T-101
title: Zkušební upozornění a nový dotaz na povolení notifikací
status: in-progress
owner: ui-dev + ui-designer
priority: P2
platform: all
depends_on: [T-010, T-020]
---

## Cíl
Při testování na Macu (2026-10-29) byla upozornění v macOS vypnutá a uživatel neměl jak si o povolení znovu říct ani si upozornění vyzkoušet. macOS se na povolení ptá jen jednou (při první žádosti) a po zamítnutí jde povolit jen v Nastavení systému. Uživatel rozhodl přidat do Nastavení tlačítko pro zkušební upozornění, na Windows i macOS (rozhodnuto 2026-10-29).

## Rozsah
- Tlačítko „Odeslat zkušební upozornění“ (pracovní název, texty navrhne ui-designer) v sekci upozornění na záložce Obecné v Nastavení, na obou platformách.
- Windows: pošle zkušební toast stejnou cestou jako skutečná upozornění (T-010), se zvukem podle nastavení `notifications.sound`.
- macOS: stav oprávnění `notDetermined` → vyvolá systémový dotaz na povolení a po povolení pošle zkušební upozornění; `denied` → dotaz macOS znovu neukáže, proto srozumitelná zpráva a nabídka otevřít Nastavení systému → Oznámení (navázat na varování z T-020); `authorized` / `provisional` → pošle zkušební upozornění. Ověřit chování podle dokumentace Applu (`requestAuthorization`, `getNotificationSettings`) a zapsat zdroje.
- Výsledek akce srozumitelně v okně (odesláno / vypnuto v systému / chyba), bez blokování UI.
- Kliknutí na zkušební upozornění neotevírá web tiskárny (není to událost tiskárny); ověřit, co je konzistentní (např. otevřít Nastavení, nebo nic).
- Izolace testů: testy jen s falešnou platformou notifikací; testovací režim jen pod testovacím identifikátorem (`.E2E`), nikdy pod produkčním; doručená zkušební upozornění v testovacím režimu se uklidí jako ostatní.
- ui-designer: umístění tlačítka, texty v 8 jazycích, konzistence s varováním T-020 a s tlačítky T-019/T-097 („Otevřít Nastavení systému“), rozvržení na Windows i macOS.

## Akceptační kritéria
- [x] Tlačítko na Windows pošle zkušební toast (test s falešnou platformou)
- [x] Na macOS vyvolá dotaz na povolení (`notDetermined`), při zamítnutí nabídne Nastavení systému, při povolení pošle upozornění (testy s falešnou platformou)
- [ ] Texty v 8 jazycích, UI testy bez ořezání na obou platformách, revize ui-designerem (texty ve všech 8 jazycích a UI testy na macOS hotové; chybí běh UI testů a přegenerování snímků na Windows a revize ui-designerem)
- [ ] Ověřeno uživatelem na Macu i Windows
- [ ] `dotnet build` a `dotnet test` bez chyb a varování (na macOS ano, viz poznámky; na Windows neověřeno)

## Návrh UI

Autor: ui-designer, 2026-10-29. Vychází ze snímků `tests/LayerPeek.App.UiTests/Snapshots/settings-general.png` a `macOS/settings-general.png` (okno 780 × 532 DIP, obsah záložky Obecné dnes končí na y ≈ 450, patička s Uložit/Zrušit na y ≈ 485), z XAML `SettingsWindow.axaml` (řádek nadpisu „Upozornění“, T-020) a z testu `GeneralTabWithNotificationsTurnedOffFitsInEveryLanguage`.

### Umístění: jeden „slot“ v řádku nadpisu Upozornění (obě platformy)

Tlačítko patří do **řádku nadpisu sekce Upozornění, vpravo** – přesně do buňky, kde dnes na macOS (T-020) vyskakuje „Otevřít nastavení oznámení“. Prostřední sloupec řádku (dnes varování T-020) slouží zároveň jako **řádek výsledku** zkušebního upozornění.

Pravidlo „jedno tlačítko ve slotu“ (žádná dvě tlačítka vedle sebe):
- systém upozornění LayerPeeku **povoluje / ještě se neptal** → ve slotu je **„Poslat zkušební upozornění“**, vlevo od něj případně výsledek posledního pokusu;
- systém je **vypnul** (macOS `denied`; Windows viz otázka 1) → ve slotu je stávající **„Otevřít nastavení oznámení“** a vlevo varování T-020; tlačítko testu je skryté (test by stejně nic neukázal a dva soupeřící povely by mátly). Po návratu ze Systémových nastavení okno stav znovu načte (`RefreshNotificationPermissionAsync` při aktivaci) a slot se vrátí na test.

Proč tady a ne jinde (měřeno na snímku):
- pravý sloupec pod „Zbývající minuty“: tlačítko + výsledek přidají ≥ 44 DIP výšky → řádek „Podrobné logování“ vyjede do patičky, záložka by při 780 × 532 rolovala (porušení T-033);
- vedle „Zvuk“: sloupec je plný (popisek + rozbalovací seznam ~300 DIP);
- patička vlevo: patří stavu celého okna (uložení, chyby validace), ne jedné akci sekce;
- řádek nadpisu: tlačítko tam už na macOS je a test T-020 dokazuje, že se vejde ve všech 8 jazycích bez rolování (řádek povyroste o ~9 DIP, to je ověřený stav). Akce je u nadpisu sekce, které se týká – vzor „nadpis skupiny + akce vpravo“ používá Windows Settings (Fluent, *SettingsExpander/SettingsCard* s akcí vpravo) i macOS (tlačítko vpravo v hlavičce skupiny, např. Notifications → „Show Previews“).

Tlačítko je **mimo** `IsEnabled="{Binding NotificationsEnabled}"` – viz otázka 3.

### Wireframy (780 × 532 DIP, záložka Obecné, jen řádek nadpisu se mění)

Výchozí stav (Windows i macOS `notDetermined` / `authorized`):
```
| Upozornění                                          [ Poslat zkušební upozornění ] |
| [x] Zobrazovat upozornění                  Zvuk  [ Jen důležitá upozornění    v ] |
|     [x] Tisk zahájen                       [x] Chyba tiskárny                       |
```

macOS, čeká se na odpověď na systémový dotaz (tlačítko zakázané, text neutrální):
```
| Upozornění  Čekám na vaši odpověď: macOS se ptá,    [ Poslat zkušební upozornění ] |
|             jestli smí LayerPeek posílat upozornění.  (zakázané)                    |
```

Odesláno (neutrální barva textu – doručení neumíme ověřit, proto ne zelená):
```
| Upozornění  Zkušební upozornění odesláno. Pokud ho  [ Poslat zkušební upozornění ] |
|             nevidíte, zkontrolujte ve Windows režim Nerušit.                        |
```

Vypnuto v systému (macOS `denied`; Windows `DisabledForApplication` / `DisabledForUser` při variantě A otázky 1) – stávající stav T-020:
```
| Upozornění  Upozornění jsou v macOS vypnutá.        [ Otevřít nastavení oznámení ] |
```

Windows, vypnuto zásadou organizace (`DisabledByGroupPolicy`) – uživatel to nezmění, žádné tlačítko do Nastavení; test zůstává:
```
| Upozornění  Upozornění vypnula zásada vaší          [ Poslat zkušební upozornění ] |
|             organizace.                                                             |
```

Chyba (výjimka, vypršení čekání, neznámý stav) – červený text (třída `error`), tlačítko zase povolené:
```
| Upozornění  Zkušební upozornění se nepodařilo       [ Poslat zkušební upozornění ] |
|             odeslat.                                                                |
```

### Rozměry a styl
- Tlačítko: stávající styl tlačítek okna (MinHeight 36 DIP, výchozí Fluent padding), `Margin="0,12,0,0"`, `VerticalAlignment="Center"` – totožně s tlačítkem T-020, aby se slot při přepnutí nehýbal. Není primární (není `accent`); primární akcí okna zůstává Uložit.
- Výsledek: `TextBlock` ve sloupci 1 řádku nadpisu, `Margin="16,16,12,4"`, `VerticalAlignment="Center"`, `TextWrapping="Wrap"`, `FontSize="12"`; barvy: neutrální (čekání, odesláno), `SettingsErrorBrush` (vypnuto, zásada, chyba). Jedna sada pravidel s varováním T-020 – v jednu chvíli je viditelný **buď** výsledek, **nebo** varování (varování má přednost; když se objeví, výsledek se smaže).
- Limit: výsledek i varování max. **2 řádky** a šířka ≥ 160 DIP při 780 × 532 (stejná kontrola jako v testu T-020). Dostupná šířka textu ≈ 748 − nadpis (95–140) − 28 − tlačítko (≈ 175–250) ≈ 330–380 DIP, tj. ~55 znaků na řádek při 12 px → texty výsledků drženy pod ~100 znaků v nejdelším jazyce (viz tabulka; nejdelší FR/DE ≈ 97 znaků).
- Nejširší tlačítko: FR „Envoyer une notification de test“ (32 znaků, odhad ≈ 250 DIP), DE „Testbenachrichtigung senden“ (27), PL „Wyślij powiadomienie testowe“ (28).

### Chování tlačítka
1. Klik → tlačítko zakázané (`CanExecute` false), předchozí výsledek zmizí. UI se neblokuje (async, `LifetimeToken`); zavření okna čekání tiše zruší.
2. **Windows:** přečte `ToastNotifier.Setting`:
   - `Enabled` → pošle toast stejnou cestou jako T-010 → text „Odesláno… Nerušit“. `Enabled` neznamená, že se banner ukáže: při zapnutém *Nerušit* / Focus Windows toast jen uloží do centra oznámení a hlásí stále `Enabled` (proto poctivá formulace „Pokud ho nevidíte, zkontrolujte…“).
   - `DisabledForApplication` / `DisabledForUser` → toast se neposílá (Windows by ho tiše zahodil); varianta A otázky 1: zobrazí varování „Upozornění jsou ve Windows vypnutá.“ + tlačítko „Otevřít nastavení oznámení“ (`ms-settings:notifications`); varianta B: jen text s cestou.
   - `DisabledByGroupPolicy` → text o zásadě, bez tlačítka.
   - `DisabledByManifest` (nemá nastat u nebalené aplikace), výjimka → „…nepodařilo odeslat.“, podrobnosti do logu.
3. **macOS:** `getNotificationSettings`:
   - `notDetermined` → text „Čekám na vaši odpověď…“ → `requestAuthorization` (systém ukáže dotaz) → povoleno: pošle upozornění + „Odesláno… Soustředění“; zamítnuto: okno znovu načte stav → objeví se varování T-020 + „Otevřít nastavení oznámení“ (to je výsledek, žádný další text);
   - `denied` → tlačítko by nemělo být vidět (slot drží T-020); kdyby se stav změnil mezi aktivací a klikem, jen se obnoví varování;
   - `authorized` / `provisional` / `ephemeral` → pošle → „Odesláno… Soustředění“. (`provisional` doručí tiše jen do Centra oznámení; LayerPeek o něj nežádá, takže prakticky nenastane.)
   - vypršení `AuthorizationTimeout` (10 min) nebo chyba → „…nepodařilo odeslat.“
4. Opakované kliknutí nahradí předchozí zkušební upozornění (pevný tag/identifikátor), nehromadí je v centru oznámení.
5. Výsledek zůstává do dalšího kliknutí nebo zavření okna; při změně jazyka se přeloží (ukládat druh výsledku, ne hotový řetězec; `ILanguageAware`).
6. Obsah upozornění: titulek `NotificationTestTitle`, text `NotificationTestBody`, bez třetího řádku. Zvuk a kliknutí viz otázky 2 a 4.

### Přístupnost
- Tlačítko: `AutomationProperties.AutomationId="SettingsTestNotification"`, jméno = obsah, `ToolTip.Tip` i `AutomationProperties.HelpText` = `SettingsTestNotificationHint`.
- Výsledek: `AutomationId="SettingsTestNotificationResult"`, `AutomationProperties.LiveSetting="Polite"` (vzor `AboutWindow.axaml`), aby čtečka oznámila výsledek; stav nikdy jen barvou – text vždy říká, co se stalo. Doporučuji doplnit `LiveSetting="Polite"` i varování T-020 (`SettingsNotificationsTurnedOff`), protože po zamítnutí na macOS je výsledkem právě ono.
- Fokus: když se tlačítko testu skryje a mělo fokus (zamítnuto → slot přepne na T-020), přesunout fokus na `SettingsOpenNotificationSettings`, ne na začátek okna.
- Pořadí tabulátoru: Interval → Jazyk → slot v řádku nadpisu → Zobrazovat upozornění → … (pořadí shora dolů, beze změny).
- Kontrast: neutrální text = výchozí barva popředí, chybový = `SettingsErrorBrush` (již používaný, AA ve světlém i tmavém motivu).

### Rizika jazyků
- Řádek nadpisu je nejužší místo: FR tlačítko (~250 DIP) + DE/FR dvouřádkový výsledek. Pokud by FR/DE přesto přetekly na 3 řádky, zkrátit text (ne zmenšovat písmo).
- macOS německy: systém říká „Mitteilungen“ (proto v textu čekání a ve stávajícím „Mitteilungen öffnen“), v UI aplikace „Benachrichtigungen“. Windows německy „Benachrichtigungen“.
- Názvy režimů musí odpovídat lokalizaci OS: Windows 11 *Nerušit / Nerušiť / Nicht stören / No molestar / Ne pas déranger / Non disturbare / Nie przeszkadzać* (ověřeno, otázka 5); macOS *Soustředění / Môj čas / Fokus / Modos de concentración / Concentration / Full immersion / Tryb skupienia* (ověřeno a opraveno podle lokalizačních tabulek macOS, otázka 5).

### Testy (pro ui-dev)
- Rozšířit/doplnit headless test: tlačítko viditelné v 8 jazycích při 780 × 532 a 1366 × 705, bez rolování a ořezu; pro každý výsledek (nejdelší řetězec) max. 2 řádky a šířka ≥ 160 DIP; přepnutí slotu (test ↔ T-020) a přesun fokusu.
- Snímky: přegenerovat `settings-general*.png` (obě sady) + nový snímek se stavem „odesláno“ v DE nebo FR.

### Texty (finální, do všech `Strings.*.resx` doplní ui-dev)

| Klíč | en | cs | sk | de | es | fr | it | pl |
|---|---|---|---|---|---|---|---|---|
| `SettingsTestNotification` (tlačítko) | Send test notification | Poslat zkušební upozornění | Poslať skúšobné upozornenie | Testbenachrichtigung senden | Enviar notificación de prueba | Envoyer une notification de test | Invia notifica di prova | Wyślij powiadomienie testowe |
| `SettingsTestNotificationHint` (tooltip, HelpText) | Shows a sample notification to check that LayerPeek's notifications appear on this computer. | Zobrazí ukázkové upozornění, abyste ověřili, že se upozornění LayerPeeku na tomto počítači zobrazují. | Zobrazí ukážkové upozornenie, aby ste overili, že sa upozornenia LayerPeeku na tomto počítači zobrazujú. | Zeigt eine Beispielbenachrichtigung, damit Sie prüfen können, ob die Benachrichtigungen von LayerPeek auf diesem Computer erscheinen. | Muestra una notificación de ejemplo para comprobar que las notificaciones de LayerPeek aparecen en este equipo. | Affiche une notification d'exemple pour vérifier que les notifications de LayerPeek s'affichent sur cet ordinateur. | Mostra una notifica di esempio per verificare che le notifiche di LayerPeek compaiano su questo computer. | Pokazuje przykładowe powiadomienie, aby sprawdzić, czy powiadomienia LayerPeek pojawiają się na tym komputerze. |
| `SettingsTestNotificationAsking` (macOS, neutrální) | Waiting for your answer: macOS asks whether LayerPeek may send notifications. | Čekám na vaši odpověď: macOS se ptá, jestli smí LayerPeek posílat upozornění. | Čaká sa na vašu odpoveď: macOS sa pýta, či môže LayerPeek posielať upozornenia. | Warten auf Ihre Antwort: macOS fragt, ob LayerPeek Mitteilungen senden darf. | Esperando su respuesta: macOS pregunta si LayerPeek puede enviar notificaciones. | En attente de votre réponse : macOS demande si LayerPeek peut envoyer des notifications. | In attesa della tua risposta: macOS chiede se LayerPeek può inviare notifiche. | Oczekiwanie na odpowiedź: macOS pyta, czy LayerPeek może wysyłać powiadomienia. |
| `SettingsTestNotificationSentWindows` (neutrální) | Test notification sent. If you don't see it, check Do not disturb in Windows. | Zkušební upozornění odesláno. Pokud ho nevidíte, zkontrolujte ve Windows režim Nerušit. | Skúšobné upozornenie bolo odoslané. Ak ho nevidíte, skontrolujte vo Windows režim Nerušiť. | Testbenachrichtigung gesendet. Falls sie nicht erscheint, prüfen Sie „Nicht stören“ in Windows. | Notificación de prueba enviada. Si no la ve, revise «No molestar» en Windows. | Notification de test envoyée. Si elle n'apparaît pas, vérifiez « Ne pas déranger » dans Windows. | Notifica di prova inviata. Se non la vedi, controlla «Non disturbare» in Windows. | Wysłano powiadomienie testowe. Jeśli go nie widać, sprawdź tryb „Nie przeszkadzać” w Windows. |
| `SettingsTestNotificationSentMac` (neutrální; sk/es/pl opraveno podle lokalizace macOS, otázka 5) | Test notification sent. If you don't see it, check Focus in macOS. | Zkušební upozornění odesláno. Pokud ho nevidíte, zkontrolujte v macOS režim Soustředění. | Skúšobné upozornenie bolo odoslané. Ak ho nevidíte, skontrolujte v macOS režim Môj čas. | Testbenachrichtigung gesendet. Falls sie nicht erscheint, prüfen Sie „Fokus“ in macOS. | Notificación de prueba enviada. Si no la ve, revise «Modos de concentración» en macOS. | Notification de test envoyée. Si elle n'apparaît pas, vérifiez « Concentration » dans macOS. | Notifica di prova inviata. Se non la vedi, controlla «Full immersion» in macOS. | Wysłano powiadomienie testowe. Jeśli go nie widać, sprawdź „Tryb skupienia” w macOS. |
| `SettingsTestNotificationBlockedByPolicy` (Windows, chyba) | Notifications are turned off by your organization's policy. | Upozornění vypnula zásada vaší organizace. | Upozornenia vypla politika vašej organizácie. | Benachrichtigungen wurden durch eine Richtlinie Ihrer Organisation deaktiviert. | Las notificaciones están desactivadas por una directiva de su organización. | Les notifications sont désactivées par une stratégie de votre organisation. | Le notifiche sono disattivate da un criterio della tua organizzazione. | Powiadomienia zostały wyłączone przez zasady Twojej organizacji. |
| `SettingsTestNotificationFailed` (chyba) | The test notification could not be sent. | Zkušební upozornění se nepodařilo odeslat. | Skúšobné upozornenie sa nepodarilo odoslať. | Die Testbenachrichtigung konnte nicht gesendet werden. | No se pudo enviar la notificación de prueba. | La notification de test n'a pas pu être envoyée. | Impossibile inviare la notifica di prova. | Nie udało się wysłać powiadomienia testowego. |
| `NotificationTestTitle` (titulek upozornění) | Test notification | Zkušební upozornění | Skúšobné upozornenie | Testbenachrichtigung | Notificación de prueba | Notification de test | Notifica di prova | Powiadomienie testowe |
| `NotificationTestBody` (text upozornění) | Notifications from LayerPeek work. Printer events will look like this. | Upozornění z LayerPeeku fungují. Takto budou vypadat události tiskáren. | Upozornenia z LayerPeeku fungujú. Takto budú vyzerať udalosti tlačiarní. | Benachrichtigungen von LayerPeek funktionieren. So sehen Druckerereignisse aus. | Las notificaciones de LayerPeek funcionan. Así se verán los eventos de las impresoras. | Les notifications de LayerPeek fonctionnent. Les événements des imprimantes s'afficheront ainsi. | Le notifiche di LayerPeek funzionano. Gli eventi delle stampanti appariranno così. | Powiadomienia LayerPeek działają. Tak będą wyglądać zdarzenia drukarek. |

Jen při variantě A otázky 1 (Windows varování + tlačítko jako T-020; stávající macOS řetězce zůstávají beze změny):

| Klíč | en | cs | sk | de | es | fr | it | pl |
|---|---|---|---|---|---|---|---|---|
| `SettingsNotificationsTurnedOffWindows` (varování, chyba) | Notifications are turned off in Windows. | Upozornění jsou ve Windows vypnutá. | Upozornenia sú vo Windows vypnuté. | Benachrichtigungen sind in Windows ausgeschaltet. | Las notificaciones están desactivadas en Windows. | Les notifications sont désactivées dans Windows. | Le notifiche sono disattivate in Windows. | Powiadomienia są wyłączone w Windows. |
| `SettingsOpenNotificationSettingsWindows` (tlačítko) | Open notification settings | Otevřít nastavení oznámení | Otvoriť nastavenia oznámení | Benachrichtigungseinstellungen öffnen | Abrir configuración de notificaciones | Ouvrir les paramètres de notification | Apri impostazioni notifiche | Otwórz ustawienia powiadomień |
| `SettingsOpenNotificationSettingsHintWindows` (tooltip, HelpText) | LayerPeek's notifications are turned off in Settings > System > Notifications. Turn them on there. | Upozornění LayerPeeku jsou vypnutá v Nastavení > Systém > Oznámení. Zapněte je tam. | Upozornenia LayerPeeku sú vypnuté v Nastaveniach > Systém > Oznámenia. Zapnite ich tam. | Die Benachrichtigungen von LayerPeek sind unter „Einstellungen > System > Benachrichtigungen“ ausgeschaltet. Aktivieren Sie sie dort. | Las notificaciones de LayerPeek están desactivadas en Configuración > Sistema > Notificaciones. Actívelas allí. | Les notifications de LayerPeek sont désactivées dans Paramètres > Système > Notifications. Activez-les à cet endroit. | Le notifiche di LayerPeek sono disattivate in Impostazioni > Sistema > Notifiche. Attivale lì. | Powiadomienia LayerPeek są wyłączone w Ustawieniach > System > Powiadomienia. Włącz je tam. |

Jen při variantě B otázky 1 (Windows bez tlačítka, jen text výsledku):

| Klíč | en | cs | sk | de | es | fr | it | pl |
|---|---|---|---|---|---|---|---|---|
| `SettingsTestNotificationTurnedOffWindows` (chyba) | Notifications are turned off. Turn them on in Settings > System > Notifications. | Upozornění jsou vypnutá. Zapněte je v Nastavení > Systém > Oznámení. | Upozornenia sú vypnuté. Zapnite ich v Nastaveniach > Systém > Oznámenia. | Benachrichtigungen sind aus. Aktivieren Sie sie unter Einstellungen > System > Benachrichtigungen. | Las notificaciones están desactivadas. Actívelas en Configuración > Sistema > Notificaciones. | Les notifications sont désactivées. Activez-les dans Paramètres > Système > Notifications. | Le notifiche sono disattivate. Attivale in Impostazioni > Sistema > Notifiche. | Powiadomienia są wyłączone. Włącz je w Ustawieniach > System > Powiadomienia. |

### Zdroje (ověřeno 2026-10-29)
- Apple, `requestAuthorization(options:completionHandler:)`: „The first time your app calls the method, the system prompts the person… Subsequent calls to this method don’t prompt the person again.“; `granted` je `false` i při zamítnutí – stav číst přes `getNotificationSettings`. https://developer.apple.com/documentation/usernotifications/unusernotificationcenter/requestauthorization(options:completionhandler:)
- Apple, *Asking permission to use notifications* (žádat v kontextu, ne při startu; provisional = tiché doručení jen do historie Centra oznámení). https://developer.apple.com/documentation/usernotifications/asking-permission-to-use-notifications
- Apple, `UNAuthorizationStatus` (notDetermined, denied, authorized, provisional, ephemeral) a `UNNotificationSettings.alertSetting` („Authorization does not guarantee that alerts always appear on the user’s screen“). https://developer.apple.com/documentation/usernotifications/unauthorizationstatus , https://developer.apple.com/documentation/usernotifications/unnotificationsettings/alertsetting
- Apple HIG, *Managing notifications* (oprávnění lze změnit jen v nastavení systému; Focus/plánované doručení mohou upozornění zdržet). https://developer.apple.com/design/human-interface-guidelines/managing-notifications
- Microsoft, `ToastNotifier.Setting` („whether there is an app, user, or system block that prevents the display of a toast“; priorita Manifest > GroupPolicy > ForUser > ForApplication) a `NotificationSetting`. https://learn.microsoft.com/uwp/api/windows.ui.notifications.toastnotifier.setting , https://learn.microsoft.com/uwp/api/windows.ui.notifications.notificationsetting
- Microsoft, *Notifications and Do Not Disturb in Windows* (při Nerušit jen bannery vybraných aplikací, ostatní čekají v centru oznámení). https://support.microsoft.com/windows/8942c744-6198-fe56-4639-34320cf9444e
- Microsoft, *Notifications design basics* („If the user clicks on your notification… your app should launch in the notification's context“). https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-ux-guidance
- Ani Apple HIG, ani Windows guidelines nemají samostatný vzor „poslat zkušební upozornění“; návrh se opírá o obecná pravidla výše a o vzor „Otestovat spojení / Otestovat kameru“ v aplikaci (tlačítko + výsledek vedle, text místo barvy).

## Otevřené otázky
1. ~~**Windows: vypnutá upozornění – jen text, nebo stejné varování + tlačítko jako na macOS?**~~ (rozhodnuto 2026-10-29) Varianta A: rozšířit T-020 na Windows – `ISystemNotificationSettings` pro Windows nad `ToastNotifier.Setting`; `DisabledForApplication` / `DisabledForUser` → varování „Upozornění jsou ve Windows vypnutá.“ + „Otevřít nastavení oznámení“ (`ms-settings:notifications`), `DisabledByGroupPolicy` → text o zásadě bez tlačítka; kontroluje se při otevření okna a při jeho aktivaci jako na macOS; windowsové texty varianty A.
2. ~~**Zvuk zkušebního upozornění.**~~ (rozhodnuto 2026-10-29) Varianta A: zvuk vždy, když volba Zvuk ≠ „Vypnuto“, podle hodnoty **aktuálně vybrané v okně** (i neuložené).
3. ~~**Je test dostupný, když je „Zobrazovat upozornění“ odškrtnuté?**~~ (rozhodnuto 2026-10-29) Varianta A: ano, tlačítko je mimo zakázanou oblast.
4. ~~**Co udělá kliknutí na zkušební upozornění?**~~ (rozhodnuto 2026-10-29) Varianta A: přenese do popředí (nebo otevře) okno Nastavení na záložce Obecné; nový druh launch argumentu ověřený jako `ToastLaunchArgument.IsWellFormed` (T-068), stejnou cestou aktivace (příkaz instance / `ToastActivationRouter`) na obou OS; během čekání na displej (T-099, macOS) se podrží jako aktivace a vyřídí po připojení UI.
5. ~~**Ověřit na lokalizovaných systémech** názvy „Nerušit“ (Windows 11) a „Soustředění/Focus“ (macOS) v 8 jazycích.~~ (rozhodnuto 2026-10-29) Ověřit (macOS z lokalizačních tabulek systému, Windows ze stránek podpory Microsoftu), texty opravit a zapsat zdroje – provedeno, viz Poznámky k implementaci.

6. **Výška tlačítek v okně Nastavení (revize ui-designer 2026-10-30, mimo rozsah T-101):** tlačítka okna mají výchozí
   Fluent výšku (headless macOS ≈ 28 DIP), tj. pod cílem 32 DIP pro cíl klepnutí. (A, doporučeno) samostatný úkol: styl
   `Button` s `MinHeight="32"` v `SettingsWindow.axaml` + kontrola výšek všech záložek (T-033) a přegenerování snímků /
   (B) ponechat výchozí Fluent výšku (na Windows se Segoe UI je blízko 32 DIP).

## Poznámky k implementaci
Stav 2026-10-29 (ui-dev na macOS 27.2, Apple Silicon): implementováno, úkol zůstává `in-progress` kvůli ověření uživatelem na Macu i Windows, běhu testů a snímků na Windows a revizi ui-designerem.

**Rozhraní a platformy**
- `Platform/ISystemNotificationSettings.cs`: rozhraní místo `AreTurnedOffAsync` nově `ReadStateAsync` (stav `SystemNotificationState`: Unknown / NotAsked / Allowed / TurnedOff / BlockedByPolicy), `RequestPermissionAsync`, `SendTestAsync(playSound)` (výsledek `TestNotificationOutcome`: Sent / TurnedOff / BlockedByPolicy / Failed) a `Open()`. Nic z toho nevyhazuje; zrušení (zavření okna) = Unknown / false / Failed.
- macOS – `MacNotificationSettings` (T-020 rozšířeno): stav z `getNotificationSettings` (`notDetermined` → NotAsked, `denied` → TurnedOff, `authorized`/`provisional`/`ephemeral` → Allowed), dotaz přes `requestAuthorization` (macOS ukáže dotaz jen poprvé, pak hned odpoví volbou uživatele; čekání max. 10 min = `AuthorizationTimeout` centra), zkušební upozornění přes stejný `IUserNotificationCenter.Post` jako upozornění tiskáren (před odesláním znovu ověří oprávnění, nikdy se neptá). Vše mimo UI vlákno (`Task.Run`).
- Windows – nový `Platform/ToastNotificationSettings` nad `ToastNotificationService` (T-010): čtení `ToastNotifier.Setting` i zkušební toast běží jako požadavek na vlákně toastů (stejná WinRT session, stejný MTA, stejná registrace AppUserModelID/zástupce), požadavky mají přednost před čekajícími upozorněními, max. 8 čekajících; při nedostupných toastech / po `Dispose` / zrušení okamžitě Unknown / Failed. Mapování: `Enabled` → Allowed, `DisabledForApplication`/`DisabledForUser` → TurnedOff, `DisabledByGroupPolicy` → BlockedByPolicy, `DisabledByManifest` a neznámé → Unknown. Když se nastavení nedá přečíst, toast se přesto zkusí zobrazit. Windows se na nic neptá (`RequestPermissionAsync` = true). „Otevřít nastavení oznámení“ → `WindowsSystemSettings.Open` (shell execute jen adres `ms-settings:`) na pozadí (`Task.Run`), v testovacím režimu jen zápis do logu (`AppServices.OpenSystemSettings`, nově i pro Windows).
- Pevný tag / identifikátor `TestNotification.Identifier` = `layerpeek-test-notification`: macOS nahradí požadavek se stejným identifikátorem; Windows nahradí toast se stejným tagem – tag přes `IToastNotification2.put_Tag` (nové rozhraní `[GeneratedComInterface]` v `WinRtToastInterop.cs`, IID `9DFB9FD1-143A-490E-90BF-B9FBA7132DE7`, AOT bez reflexe; `IToastSession.Show(xml, tag)`). Upozornění tiskáren tag nemají (beze změny).
- Texty upozornění `NotificationTestTitle` / `NotificationTestBody` v jazyce aplikace v okamžiku odeslání (`UiLanguage.CurrentTextCulture`), na Windows `ToastContentBuilder.BuildTest` (stejná šablona `ToastGeneric`, `activationType="foreground"`, bez zvuku `<audio silent="true"/>`).
- Žádný nový balíček NuGet; žádná reflexe, žádné potlačení varování trim/AOT.

**Okno Nastavení (MVVM)**
- `Settings/SettingsViewModel.Notifications.cs` (nový partial): `ShowsNotificationsTurnedOff`, `ShowsNotificationsBlockedByPolicy`, `ShowsNotificationWarning`, `NotificationWarningText` (macOS / Windows / zásada), `OpenNotificationSettingsText`/`Hint` (macOS / Windows), `ShowsTestNotification`, `SendTestNotificationCommand` (async, během běhu zakázaný), `TestNotificationResult` (ukládá se druh výsledku, ne řetězec → přeloží se při `RefreshTexts`), `ShowsTestNotificationResult`, `TestNotificationResultIsError`. Varování má přednost: když se objeví, výsledek se smaže. Čtení stavu má verzi, takže pomalé čtení předběhnuté testem se zahodí.
- Tok kliknutí: stav → `NotAsked` → text „Čekám na vaši odpověď…“ → dotaz → povoleno: odeslat; zamítnuto: znovu načíst stav → varování T-020 + tlačítko (bez dalšího textu); jiný výsledek dotazu → „…nepodařilo odeslat.“. `TurnedOff` / `BlockedByPolicy` → jen varování. `Unknown` → rozhodne platforma. Zvuk = `SelectedSound.Value != Off` (hodnota v okně, i neuložená – otázka 2).
- Bez `ISystemNotificationSettings` (vývojové sestavení na macOS bez bundlu, testovací režim bez upozornění, jiné OS) se tlačítko testu nezobrazuje – není čím poslat.
- XAML (`SettingsWindow.axaml`): jeden slot v řádku nadpisu „Upozornění“ podle návrhu – tlačítko testu (`SettingsTestNotification`, tooltip i HelpText = `SettingsTestNotificationHint`) nebo „Otevřít nastavení oznámení“ (`SettingsOpenNotificationSettings`), vlevo varování (`SettingsNotificationsTurnedOff`, nově `LiveSetting="Polite"`) nebo výsledek (`SettingsTestNotificationResult`, `LiveSetting="Polite"`, neutrální / třída `error`). Tlačítko je mimo oblast zakázanou „Zobrazovat upozornění“ (otázka 3).
- Fokus (`SettingsWindow.axaml.cs`): když se slot přepne a fokus byl na skrytém tlačítku, přesune se na zobrazené tlačítko slotu (oba směry).

**Kliknutí na zkušební upozornění (otázka 4)**
- `ToastLaunchArgument.Settings` = `settings=general`; `IsWellFormed` přijme kromě `printer=…` jen přesně tento text (žádná varianta, žádná velikost písmen), takže projde stávajícími kontrolami T-068 (`ToastActivationInbox`, `InstanceCommand.TryForToast`, protokol `toast:settings=general`).
- `ToastActivationRouter.ActivateAsync` ho neotevírá jako web, ale zavolá opener okna Nastavení, který připojí UI (`App.StartHost` → `TrayHost/ServiceHost.AttachSettingsOpener`, přes `AvaloniaUiDispatcher` → `SettingsWindowCommand.Show(..., generalTab: true)` → `SettingsViewModel.ShowGeneralTab()`; otevřené okno si edity ponechá). Duplicitní hlášení téhož kliknutí (2 s) se ignoruje, po ukončení aplikace se nic neotevře.
- macOS během čekání na displej (T-099): `WaitingInstanceCommands` takové kliknutí ponechá pro UI (jako „activate“), fronta příkazů ho doručí po připojení UI. Router navíc drží jedno kliknutí, které by přišlo před připojením openeru (např. přímé kliknutí na Windows), a vyřídí ho při `AttachSettingsOpener`.

**Izolace testů**
- Testy používají jen falešné platformy: `FakeSystemNotificationSettings` (nový, sdílený s UI testy), `FakeToastPlatform`/`FakeToastSession` (rozšířeno o `Setting` a tagy), `FakeUserNotificationCenter`. Žádné skutečné upozornění, žádný dotaz na oprávnění, žádné otevření Nastavení.
- Testovací režim: na macOS jen centrum testovacího bundlu `…LayerPeek.E2E` (beze změny), jeho úklid (`TestNotificationCleanup`, `RemoveAll`) odstraní i zkušební upozornění (nový test); na Windows úklid T-067 (`ToastNotificationHistory.ClearWithId` testovacího AUMID) maže všechny toasty testovací aplikace včetně otagovaných; otevírání Nastavení / Nastavení systému je v testovacím režimu jen záznam do logu.

**Ověření lokalizovaných názvů režimů (otázka 5)**
- macOS „Focus“ – přečteno jen pro čtení z lokalizačních tabulek tohoto Macu (macOS 27.2): `/System/Library/ExtensionKit/Extensions/FocusSettingsExtension.appex/Contents/Resources/InfoPlist.loctable` (`CFBundleDisplayName`, název panelu v Nastavení systému) a `…/Localizable.loctable` (klíč `FOCUS`), shodně `/System/Library/CoreServices/ControlCenter.app/Contents/Resources/Focus.loctable` (Ovládací centrum, es tam zkráceně „Modos“). Výsledek: en Focus, cs Soustředění, **sk Môj čas**, de Fokus, **es Modos de concentración**, fr Concentration, it Full immersion, **pl Tryb skupienia**. Opraveno proti návrhu: sk (bylo Sústredenie), es (bylo Concentración), pl (bylo Skupienie).
- Windows 11 „Do not disturb“ – lokalizované stránky podpory Microsoftu „Notifications and Do Not Disturb in Windows“: https://support.microsoft.com/cs-cz/windows/8942c744-6198-fe56-4639-34320cf9444e (režim Nerušit), https://support.microsoft.com/sk-sk/windows/8942c744-6198-fe56-4639-34320cf9444e (Nerušiť), https://support.microsoft.com/de-de/windows/8942c744-6198-fe56-4639-34320cf9444e (Nicht stören), https://support.microsoft.com/es-es/windows/8942c744-6198-fe56-4639-34320cf9444e (No molestar), https://support.microsoft.com/fr-fr/windows/8942c744-6198-fe56-4639-34320cf9444e (Ne pas déranger), https://support.microsoft.com/it-it/windows/8942c744-6198-fe56-4639-34320cf9444e (Non disturbare), https://support.microsoft.com/pl-pl/windows/8942c744-6198-fe56-4639-34320cf9444e (Nie przeszkadzać). Všechny texty návrhu sedí, beze změny.
- Francouzský text „En attente de votre réponse : …“ má před dvojtečkou nezlomitelnou mezeru (pravidlo `StringsTests`).

**Testy**
- Nové / rozšířené: `SettingsNotificationPermissionTests` (stavy, dotaz, zamítnutí, zvuk podle okna, test při vypnutých upozorněních, výsledky a jejich překlad, zrušení zavřením okna, předběhnuté čtení), `MacNotificationSettingsTests`, `ToastTestNotificationTests` (čtení nastavení na vlákně toastů, mapování, tag, XML, zvuk, nedostupné toasty, dispose/zrušení, `ToastNotificationSettings`), `ToastLaunchArgumentTests`, `InstanceProtocolTests`, `WaitingInstanceCommandsTests`, `ToastActivationRouterTests` (otevření okna, podržení před UI, duplicita, po ukončení nic, chyba openeru), `TestNotificationCleanupTests`, `AppServicesTests` (Windows: `ToastNotificationSettings`, jen na Windows).
- UI (headless): `GeneralTabWithTheTestNotificationFitsInEveryLanguage` (8 jazyků × výsledky čekání / odesláno macOS / chyba / odesláno Windows / zásada / vypnuto ve Windows, 780 × 532 i 1366 × 705, bez rolování a ořezu, ≤ 2 řádky, ≥ 160 DIP, tlačítko v okně), `TheTestNotificationIsAccessible`, `TheFocusFollowsTheSlotWhenMacOSDenies` (ověřeno, že bez přesunu fokusu selže), `AClickOnTheTestNotificationShowsTheGeneralTab`; stávající T-020 test běží beze změny chování. Harness okna nastavení má nově výchozí `FakeSystemNotificationSettings` (tlačítko testu je vidět ve všech snímcích záložky Obecné).
- Snímky: přegenerovaná macOS sada `Snapshots/macOS/settings-general.png`, `settings-general-errors.png` a nový `settings-general-test-sent-fr.png` (francouzsky, výsledek „odesláno“ s termíny OS dané sady). **Windowsová sada (`Snapshots/settings-general.png`, `settings-general-errors.png`) se na Macu přegenerovat nedá a nový `Snapshots/settings-general-test-sent-fr.png` chybí – na Windows tyto 3 snímkové testy selžou, dokud je uživatel nepřegeneruje (`LAYERPEEK_UPDATE_SNAPSHOTS=1 dotnet test --project tests/LayerPeek.App.UiTests`) a nezkontroluje.**
- Výsledky na macOS: `dotnet build` a `dotnet build -c Release` 0 varování, 0 chyb; `dotnet test` celkem 7661, úspěšné 7480, přeskočené 181 (Windows-only a testy na vyžádání), neúspěšné 0. `tools/profile-snapshot.ps1 -Save` / `-Compare` kolem práce: beze změny (Login Items „not checked“ bez autorizace).

**Neověřeno (nutné ověřit uživatelem)**
- Nic z Windows se na Macu nespustilo: WinRT `IToastNotification2.put_Tag` (nahrazení toastu), `ToastNotifier.Setting` v Nastavení → Systém → Oznámení / zásadě, `ms-settings:notifications`, klik na toast (`Activated` i COM aktivátor), Windows UI testy a snímky, E2E testy na Windows.
- Na macOS se aplikace podle pokynů nespouštěla: skutečné zkušební upozornění, systémový dotaz (`notDetermined`), nahrazení v Centru oznámení a klik na upozornění ověří uživatel (postup v `docs/macos-manual-testing.md`, sezení 5 krok 4).

### Revize ui-designer (2026-10-30)
Podklady: snímky macOS `settings-general`, `settings-general-errors`, `settings-general-test-sent-fr` (zvětšené výřezy,
měřeno v DIP při 100 %), XAML `Windows/SettingsWindow.axaml` (řádek nadpisu „Upozornění“, `TestNotificationButton`,
`OpenNotificationSettingsButton`, `SettingsTestNotificationResult`), `Strings.*.resx`. Aplikace se nespouštěla.

**Verdikt: OK s drobnými nedostatky** – implementace odpovídá návrhu (jeden slot vpravo v řádku nadpisu, výsledek vlevo
od tlačítka, neutrální barva u „odesláno“, `error` u chyb, `LiveSetting="Polite"` u výsledku i varování T-020, tooltip
= `HelpText`, přesun fokusu při přepnutí slotu, tlačítko mimo oblast zakázanou „Zobrazovat upozornění“). Nejdelší
výsledek (fr „Notification de test envoyée … « Concentration » dans macOS.“) má 2 řádky, tlačítko fr „Envoyer une
notification de test“ se vejde, záložka bez posuvníku; v chybovém stavu (`settings-general-errors`) se nic neposune.

Nálezy:
1. **Střední – tlačítko testu „lepí“ na rozbalovací seznam Zvuk.** V en je mezi spodní hranou tlačítka (y ≈ 183)
   a horní hranou seznamu Zvuk (y ≈ 186) jen ~3 DIP (fr se dvouřádkovým výsledkem ~7 DIP), pod mřížkou 4/8 DIP.
   Tlačítko pak opticky patří k řádku „Zvuk“, ne k nadpisu sekce. Příčina: `Margin="0,12,0,0"` posouvá tlačítko dolů
   a řádek nadpisu nemá spodní okraj. Návrh (jen XAML): oběma tlačítkům slotu `Margin="0,8,0,4"` – výška řádku zůstane
   stejná (8 + ~28 + 4 = 40 DIP jako dnes 12 + ~28), mezera k seznamu ~7 DIP, střed tlačítka se posune o 2 DIP nahoru
   k nadpisu; `GeneralTabWithTheTestNotificationFitsInEveryLanguage` a test T-020 by měly projít beze změny,
   přegenerovat `settings-general*`.
2. **Nízká – výška tlačítka ~28 DIP (< 32 DIP cíle).** Okno nemá styl `Button` s `MinHeight` (návrh omylem uváděl
   „stávající MinHeight 36“); tlačítka mají výchozí Fluent výšku, v headless macOS ≈ 28 DIP, na Windows se Segoe UI
   ≈ 30–32. Týká se všech tlačítek okna (Obnovit výchozí, Testovat připojení…), ne jen T-101. Návrh: samostatný úkol
   „`Button` `MinHeight` 32 v okně Nastavení“ s kontrolou výšek všech záložek (T-033) – viz Otevřené otázky Q6.
3. **Nízká – chybí snímky dalších stavů slotu.** Stav „vypnuto v systému“ (varování + „Otevřít nastavení oznámení“),
   „čekám na odpověď“ (tlačítko zakázané) a tmavý motiv výsledku jsou ověřené jen testy stromu. Návrh: doplnit
   `settings-general-turned-off-de` (nejdelší varování + tlačítko) a `settings-general-test-sent-dark`.
4. **Informace – neověřeno:** Windows (Segoe UI, Windows sada snímků, skutečný toast, `ms-settings:notifications`),
   skutečný systémový dotaz macOS a čtení výsledku VoiceOverem / Narratorem.

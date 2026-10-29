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
- [ ] Tlačítko na Windows pošle zkušební toast (test s falešnou platformou)
- [ ] Na macOS vyvolá dotaz na povolení (`notDetermined`), při zamítnutí nabídne Nastavení systému, při povolení pošle upozornění (testy s falešnou platformou)
- [ ] Texty v 8 jazycích, UI testy bez ořezání na obou platformách, revize ui-designerem
- [ ] Ověřeno uživatelem na Macu i Windows
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

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
- Názvy režimů musí odpovídat lokalizaci OS: Windows 11 *Nerušit / Nerušiť / Nicht stören / No molestar / Ne pas déranger / Non disturbare / Nie przeszkadzać*; macOS *Soustředění / Sústredenie / Fokus / Concentración / Concentration / Full immersion / Skupienie* – před vydáním ověřit na lokalizovaném systému (neověřeno).

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
| `SettingsTestNotificationSentMac` (neutrální) | Test notification sent. If you don't see it, check Focus in macOS. | Zkušební upozornění odesláno. Pokud ho nevidíte, zkontrolujte v macOS režim Soustředění. | Skúšobné upozornenie bolo odoslané. Ak ho nevidíte, skontrolujte v macOS režim Sústredenie. | Testbenachrichtigung gesendet. Falls sie nicht erscheint, prüfen Sie „Fokus“ in macOS. | Notificación de prueba enviada. Si no la ve, revise «Concentración» en macOS. | Notification de test envoyée. Si elle n'apparaît pas, vérifiez « Concentration » dans macOS. | Notifica di prova inviata. Se non la vedi, controlla «Full immersion» in macOS. | Wysłano powiadomienie testowe. Jeśli go nie widać, sprawdź „Skupienie” w macOS. |
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
1. **Windows: vypnutá upozornění – jen text, nebo stejné varování + tlačítko jako na macOS?**
   - A) Rozšířit T-020 na Windows: `ISystemNotificationSettings` pro Windows (`ToastNotifier.Setting` ≠ `Enabled` → varování „Upozornění jsou ve Windows vypnutá.“ + „Otevřít nastavení oznámení“ → `ms-settings:notifications`), zobrazené už při otevření okna, nejen po kliknutí na test. Slot se chová stejně na obou OS, texty jsou krátké. **Doporučeno.**
   - B) Jen výsledek testu s cestou „Nastavení > Systém > Oznámení“, bez tlačítka a bez proaktivního varování (méně kódu, ale uživatel se to dozví až po kliknutí a text je na hraně 2 řádků).
2. **Zvuk zkušebního upozornění.**
   - A) Hraje vždy, když volba Zvuk ≠ „Vypnuto“ (test se chová jako důležité upozornění; uživatel slyší, jak zní). **Doporučeno.**
   - B) Přesně jako informační upozornění (při výchozím „Jen důležitá upozornění“ bude tiché – poctivé, ale vypadá to, že zvuk nefunguje).
   - C) Vždy se zvukem bez ohledu na nastavení.
   - Podotázka: použít hodnotu **aktuálně vybranou v okně** (i neuloženou – „co vidím, to zkouším“, **doporučeno**), nebo uloženou?
3. **Je test dostupný, když je „Zobrazovat upozornění“ odškrtnuté?**
   - A) Ano, test zkouší systém, ne nastavení událostí (tlačítko je mimo zakázanou oblast). **Doporučeno.**
   - B) Ne, tlačítko se zakáže spolu s ostatními volbami.
4. **Co udělá kliknutí na zkušební upozornění?**
   - A) Otevře / přenese do popředí okno Nastavení na záložce Obecné (kontext, odkud test přišel – doporučení Microsoftu „launch in the notification's context“). Vyžaduje nový druh launch argumentu (ověřený jako `ToastLaunchArgument.IsWellFormed`, T-068). **Doporučeno.**
   - B) Nic (upozornění jen zmizí) – nejjednodušší, bez nového argumentu.
5. **Ověřit na lokalizovaných systémech** názvy „Nerušit“ (Windows 11) a „Soustředění/Focus“ (macOS) v 8 jazycích – mám je z obecné znalosti lokalizace OS, ne ověřené na systému. Případně formulace bez názvu režimu („…zkontrolujte nastavení oznámení v systému“)?

## Poznámky k implementaci
(vyplní agent po dokončení)

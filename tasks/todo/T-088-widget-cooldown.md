---
id: T-088
title: Widget Vychladnutí („Chladne…“ / „Vychladlo – můžete vypnout“)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-079, T-081, T-092]
---

## Cíl
Widget **Vychladnutí**, který se objeví po dokončení nebo zastavení tisku (ne po chybě): stav **„Chladne…“** (teplota trysky a podložky, průběh k prahu, odhad času) a **„Vychladlo – můžete vypnout“**. Data z `CooldownMonitor` (T-079); rozhoduje jen tryska, podložka se jen zobrazuje.

## Rozsah
- View model + šablona, jedna registrace v `OverlayWidgetRegistry`; odběr `CooldownMonitor.Changed` jen pro tiskárnu okna a jen při připojení okna (odhlášení při zavření).
- Velikosti S/M/L a zvýraznění podle T-076; odhad času jen když ho Core vrátí (jinak bez odhadu, ne „0 min“); práh v textu podle nastavení tiskárny (T-079, T-080); zvýraznění = aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (akcentová barva).
- Widget je bez stavu chladnutí skrytý; zmizí při začátku dalšího tisku, při přechodu tiskárny do offline a při znovu zapnutém topení trysky (T-079); tisk skončený pod prahem ukáže rovnou „Vychladlo“.
- Nové texty ve všech 8 jazycích (s nezlomitelnou mezerou před „°C“).
- **Panel nastavení na záložce Widgety** (rozhodnutí 2026-10-25, smlouva T-092, Návrh UI 6 v T-092): registrace widgetu Vychladnutí dostane `DescriptionKey` (`CameraOverlayWidgetCooldownDescription`), `IconKey` (ikona sněhové vločky z T-076 oddílu 6) a `Settings` = továrnu panelu Vychladnutí, kterou připraví T-092 (práh trysky, „Použít práh u všech tiskáren“, stav upozornění „Tiskárna vychladla“ s odkazem na záložku Obecné). Panel, jeho view model, šablonu a texty dodává T-092; tento úkol je jen zapojí do registrace.

## Akceptační kritéria
- [ ] App.Tests: oba stavy, zmizení při dalším tisku / offline / ohřevu, tisk skončený pod prahem, chybějící podložka, bez odhadu, neznámý konec tisku (`PrintEndedAt` `null` → bez řádku časů), změna prahu za běhu, levné porovnání, přepnutí jazyka, odhlášení
- [ ] Headless UI testy: widget ve všech velikostech v obou stavech, nejdelší jazyk; přechod „Chladne…“ → „Vychladlo“ s falešnými hodinami a snapshoty; snímky reference
- [ ] Registrace podle smlouvy T-092: test registru (popis a ikona Vychladnutí, panel nastavení deklarovaný) a headless UI test, že na záložce Widgety se po výběru Vychladnutí ukáže panel s polem prahu (`WidgetCooldownThreshold`) se skutečnou registrací
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Začátek po dokončeném i zastaveném tisku, ne po chybě; konec při dalším tisku, offline nebo znovu zapnutém topení trysky; tisk skončený pod prahem = rovnou „Vychladlo“ (bez notifikace); práh u tiskárny 30–100 °C, výchozí 50 °C (T-079).
- Zvýraznění: aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (návrh architekta, designer potvrdí v T-076).

## Rozhodnutí (uživatel, 2026-10-24, po dokončení T-079)
- **Rozhodnutí 10 – konec tisku nebyl vidět:** když `CooldownState.PrintEndedAt` je `null` (tisk skončil během výpadku spojení), widget ukáže jen **„Vychladlo – můžete vypnout“** s teplotami, **bez** řádku „tisk dokončen v … · vychladlo za …“. Ve stavu „Chladne…“ se řádek s časem konce tisku také vynechá.
- **Rozhodnutí 11 – tiskárna vychladlá už při startu:** tiskárna, která už při startu aplikace (nebo při přidání tiskárny) ukazuje skončený tisk, widget **nemá**. Core (T-079, větev `feature/widget-core-followups`) pro ni chladnutí nesleduje, fáze zůstane `None`; widget nic zvláštního řešit nemusí.
- **Vykání:** texty widgetu vykají („Vychladlo – můžete vypnout“), stejně jako zbytek české UI.

## Rozhodnutí (uživatel, 2026-10-25)
- Nastavení widgetu (práh trysky, „Použít práh u všech tiskáren“, stav upozornění) je v panelu Vychladnutí na nové záložce **Widgety** (T-092, otázka Q6 jeho návrhu). Tento úkol zaregistruje panel Vychladnutí podle smlouvy, kterou zavádí T-092 (`DescriptionKey`, `IconKey`, `IOverlayWidgetSettingsFactory`). Proto nově závisí na T-092 (T-092 naopak na tomto úkolu nezávisí).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

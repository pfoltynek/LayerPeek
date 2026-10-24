---
id: T-088
title: Widget Vychladnutí („Chladne…“ / „Vychladlo – můžeš vypnout“)
status: todo
owner: ui-dev
priority: P2
platform: all
depends_on: [T-079, T-081]
---

## Cíl
Widget **Vychladnutí**, který se objeví po dokončení nebo zastavení tisku (ne po chybě): stav **„Chladne…“** (teplota trysky a podložky, průběh k prahu, odhad času) a **„Vychladlo – můžeš vypnout“**. Data z `CooldownMonitor` (T-079); rozhoduje jen tryska, podložka se jen zobrazuje.

## Rozsah
- View model + šablona, jedna registrace v `OverlayWidgetRegistry`; odběr `CooldownMonitor.Changed` jen pro tiskárnu okna a jen při připojení okna (odhlášení při zavření).
- Velikosti S/M/L a zvýraznění podle T-076; odhad času jen když ho Core vrátí (jinak bez odhadu, ne „0 min“); práh v textu podle nastavení tiskárny (T-079, T-080); zvýraznění = aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (akcentová barva).
- Widget je bez stavu chladnutí skrytý; zmizí při začátku dalšího tisku, při přechodu tiskárny do offline a při znovu zapnutém topení trysky (T-079); tisk skončený pod prahem ukáže rovnou „Vychladlo“.
- Nové texty ve všech 8 jazycích (s nezlomitelnou mezerou před „°C“).

## Akceptační kritéria
- [ ] App.Tests: oba stavy, zmizení při dalším tisku / offline / ohřevu, tisk skončený pod prahem, chybějící podložka, bez odhadu, změna prahu za běhu, levné porovnání, přepnutí jazyka, odhlášení
- [ ] Headless UI testy: widget ve všech velikostech v obou stavech, nejdelší jazyk; přechod „Chladne…“ → „Vychladlo“ s falešnými hodinami a snapshoty; snímky reference
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Rozhodnutí (uživatel, 2026-10-24)
- Začátek po dokončeném i zastaveném tisku, ne po chybě; konec při dalším tisku, offline nebo znovu zapnutém topení trysky; tisk skončený pod prahem = rovnou „Vychladlo“ (bez notifikace); práh u tiskárny 30–100 °C, výchozí 50 °C (T-079).
- Zvýraznění: aktuální teplota trysky, ve stavu „Vychladlo“ text stavu (návrh architekta, designer potvrdí v T-076).

## Otevřené otázky
- (žádné)

## Poznámky k implementaci
(vyplní agent po dokončení)

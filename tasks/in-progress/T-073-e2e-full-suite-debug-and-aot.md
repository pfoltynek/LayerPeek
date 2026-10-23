---
id: T-073
title: E2E – celá sada proti Debug i Native AOT (Debug) a test Správce pověření (uzavření T-052)
status: in-progress
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-067, T-068, T-069, T-070, T-071, T-072]
---

## Cíl
Uzavřít poslední kritérium T-052: tray, okna, toasty, Správce pověření a kamera (FFmpeg) fungují v Debug buildu i v Native AOT, a E2E testy procházejí. Hledání nativních knihoven je omezené (`SetDefaultDllDirectories`), takže je potřeba ověřit, že se nic nerozbilo ani v AOT. Dnes `tools/run-e2e.ps1 -PublishAot` pouští proti Native AOT exe jen testy logu a pádů (T-064, T-046). Tento úkol pustí proti němu **celou sadu** a přidá E2E test skutečného Správce pověření.

## Pokrytí
| Úkol | Kontrola | Jak |
|---|---|---|
| T-052 | tray v Debug i AOT | `TrayMenuTests` + T-069 v obou průchodech |
| T-052 | okna v Debug i AOT | `SettingsAndDiagnosticsTests`, `CameraWindowTests`, T-071 v obou průchodech |
| T-052 | toasty v Debug i AOT | T-068 v obou průchodech |
| T-052 | Správce pověření v Debug i AOT | nový `CredentialManagerTests` |
| T-052 | kamera (FFmpeg) v Debug i AOT | `CameraWindowTests` se simulovanou kamerou (živý obraz se mění) v obou průchodech; FFmpeg DLL ze složky publish |
| T-052 | web přes proxy | ručně ověřeno 2026-10-21 (Debug i AOT); navíc GET přes proxy v T-068/T-069 v obou průchodech |
| T-052 | E2E testy T-044 a T-030 procházejí | celý `tools/run-e2e.ps1 -PublishAot` + `dotnet test` (T-030 s Edge) |
| T-035 | názvy monitorů (`MonitorNameNative`) v AOT | `SettingsListsTheRealMonitorNames` v průchodu AOT |

## Rozhodnuto
- **Native AOT v konfiguraci Debug stačí (uživatel 2026-10-22, dřív Q1):** AOT část kritéria T-052 splní E2E sada proti Native AOT exe konfigurace Debug, stejně jako u T-064. Krátké ruční proklikání Release AOT exe je jen doporučená kontrola, která uzavření neblokuje.

## Rozsah
- **`tools/run-e2e.ps1 -PublishAot`:** po zeleném průchodu proti Debug exe spustit **celou vybranou sadu** (respektuje `-Class` / `-Method`) proti `bin/Debug/net11.0/win-x64/publish/PrusaTrayDiagnostic.exe`, s vlastní složkou výsledků (`…Results.<id>-aot`). Testy logu a pádů v něm zůstanou. Testy, které proti AOT nedávají smysl (např. kontrola Release DLL), se v AOT průchodu přeskočí s důvodem (`E2EFixture.IsNativeAot`). Varování T-065 se ukáže jednou na začátku a platí pro oba průchody. Souhrn na konci uvede oba průchody a počty prošlých, přeskočených a neúspěšných testů.
- **Porty:** simulátory běží na portech přidělených systémem (`0`), jako dnes, takže obsazený port 8080 (např. Aspire) nevadí. Pevné porty 18080/18554 z poznámky T-052 platí jen pro ruční spuštění simulátoru.
- **`CredentialManagerTests`** (T-067 `PRUSATRAYDIAGNOSTIC_TEST_CREDENTIAL_PREFIX`, předpona `PrusaTrayDiagnostic.E2E/<id worktree>/<běh>`):
  - start bez hesla, v nastavení zadat heslo simulátoru, Uložit → v Credential Manageru existuje `…/printer/<id>` (ověří test přes `CredRead`) a monitorování se přihlásí (stav ≠ odmítnuté přihlášení);
  - ukončit a spustit znovu se stejnou předponou → přihlásí se bez zadání hesla (heslo se načetlo z úložiště);
  - odebrat tiskárnu a Uložit → položka zmizí;
  - heslo se neobjeví v logu ani v `settings.json` (kontrola souborů testovací složky);
  - **úklid:** `CredEnumerate("PrusaTrayDiagnostic.E2E/*")` a smazání na konci testu, při startu a konci běhu (pod zámkem plochy). Ostatní položky uživatele se nečtou ani nemění (filtr jen na tuto předponu).
- **Release Native AOT:** dál jen kontrola, že neobsahuje testovací přepínače (T-044, T-067). Spuštění Release exe by sáhlo na data uživatele, proto se testy proti němu nespouští (rozhodnutí T-064 pro log a 2026-10-22 pro T-052). Krátké ruční proklikání Release AOT exe (tray, okno kamery se simulátorem, jeden toast) je jen **doporučená** kontrola a uzavření T-052 neblokuje.

## Akceptační kritéria
- [ ] `tools/run-e2e.ps1 -PublishAot` spustí celou sadu proti Debug exe i proti Native AOT exe konfigurace Debug; oba průchody zelené, souhrn obou
- [ ] `CredentialManagerTests` v obou průchodech: uložení, načtení po restartu, smazání; po běhu žádná položka `PrusaTrayDiagnostic.E2E/…`
- [ ] Kamera se simulátorem (FFmpeg) ukazuje živý obraz v obou průchodech
- [ ] `dotnet test` (včetně T-030) zelené
- [ ] Kritérium T-052 „Ruční ověření na Windows v AOT publish i v Debug…“ zaškrtnuté podle výsledků obou průchodů (AOT část splňuje Native AOT v konfiguraci Debug, rozhodnutí 2026-10-22); v poznámkách T-052 zapsané proklikání Release AOT exe jako doporučená kontrola; T-052 přesunutý do `done/`
- [ ] Ostatní úkoly řady (T-010, T-025, T-035, T-037, T-039, T-041, T-042, T-043) zkontrolované: kritéria zaškrtnutá podle výsledků obou průchodů, zbývající doporučené kontroly zapsané v jejich poznámkách, úkoly přesunuté do `done/`
- [ ] `dotnet build` a `dotnet test` bez chyb a varování; AOT publish win-x64 bez varování

## Otevřené otázky
- (žádné – Q1 rozhodnuta 2026-10-22, viz Rozhodnuto)

## Poznámky k implementaci
(vyplní agent po dokončení)

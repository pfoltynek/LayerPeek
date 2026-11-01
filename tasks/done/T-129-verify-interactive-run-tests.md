---
id: T-129
title: windows-verify – testy interaktivního běhu
status: done
owner: qa-reviewer
priority: P2
platform: windows
depends_on: [T-105, T-125]
---

## Cíl
Uživatel 2026-11-01: „jsi schopen na T-125 napsat testy, ať to nemusím ručně testovat?“ – rozhodnuto automatizovat
bod T-105 (celý interaktivní běh `tools/windows-verify.ps1`) z T-125.

Původně patřil do úkolu i bod T-017 (instalátor `win-arm64`, kontrola `win.arm64-build`). Ten byl rozhodnutím
uživatele 2026-11-01 vypuštěn: podpora `win-arm64` se ruší (samostatný úkol); rozpracovaná kontrola byla vrácena.

## Rozsah
1. **T-105 – interaktivní běh:** Pester testy průchodu `tools/windows-verify.ps1` v interaktivním režimu se simulovanými
   odpověďmi (y / n / přeskočit u kontrol druhu Permission a Manual), přerušení uprostřed a dokončení obnovy
   `-RestoreOnly`, správný souhrn a report; bez skutečných změn systému (podvržené akce kontrol).
2. Dokumentace (`docs/windows-manual-testing.md`) a T-125: bod T-105 odkazuje na nové testy.

## Akceptační kritéria
- [x] Pester testy interaktivního běhu a `-RestoreOnly`, spouštěné i z `dotnet test` (`LayerPeek.Scripts.Tests`)
- [x] V T-125 u bodu T-105 odkaz na testy
- [x] `dotnet build` a `dotnet test` bez chyb a varování (2026-11-01: build 0 varování; test 8947 celkem, 0 selhání, 274 přeskočeno)

## Otevřené otázky
- ~~(review 2026-11-01, nice-to-have) Zpevnit seam `LAYERPEEK_VERIFY_STANDINS`?~~ Vyřešeno (rozhodnutí uživatele
  2026-11-01): obojí – soubor se načte jen z dočasné složky testů (`LAYERPEEK_PESTER_TESTS_TEMP` nebo
  `[IO.Path]::GetTempPath()`), jinak chyba a kód 2 bez jediné kontroly; použití podvržených kontrol je v `report.md`
  i `report.json`.

## Poznámky k implementaci
- **Testy:** nový soubor `tools/windows-verify.Tests.ps1` (Pester 5+), zařazený do `PesterScriptTests`
  (`LayerPeek.Scripts.Tests`). Spouští skutečný hlavní běh `tools/windows-verify.ps1` v podřízeném `pwsh`; odpovědi jdou
  na standardní vstup, takže se testuje i skutečný `Read-Host` výchozího promptu. Pokryto:
  - interaktivní běh se šesti podvrženými kontrolami (Auto, Permission y, Permission n → `SKIP` „declined by the user“,
    podvržená instalace y, Manual n → `FAIL`, podvržená odinstalace y): spuštěné jen povolené akce, souhrn v konzoli,
    `report.md` (režim, výsledek, řádky tabulky, odkaz na důkaz) a `report.json` (režim, výběr, stavy), otázka s odpovědí
    v `steps.txt`, profil po běhu stejný jako před ním;
  - Manual s `Y` → `MANUAL`, kód 0; `-Unattended` se nic neptá a spustí jen Auto;
  - instalace ponechaná mezi běhy (hláška „The verification install stays …“) a odinstalace dalším během (hláška
    „… removed and your profile restored“);
  - přerušení uprostřed běhu (tvrdé `Stop-Process` vlastního procesu: žádný `finally`, úklid ani report), pak
    `-RestoreOnly` vrátí profil do původního stavu (složky, zástupci, nový cíl Správce pověření smazán) a druhé
    `-RestoreOnly` hlásí „Nothing to restore.“;
  - jeden test běží na každém OS (`-ListChecks` s podvrženými kontrolami a bez nich), ostatní jen na Windows.
- **Seam ve skriptu (beze změny chování pro uživatele):** když je nastavená proměnná `LAYERPEEK_VERIFY_STANDINS`, skript
  před hlavní částí načte (dot-source) uvedený soubor a v konzoli to ohlásí řádkem `STAND-INS:`. Test tím podvrhne
  kontroly a funkce sahající na profil (`Get-InstallPaths`, `Get-CredentialTargets`, `Remove-CredentialTarget`) do
  složky sandboxu v dočasné složce testu. Pro to se mazání pověření vyčlenilo do `Remove-CredentialTarget` a záloha
  před instalací (`win.install`) do `Backup-Install`; podvržená instalace i obnova tak používají skutečnou logiku
  skriptu (`Backup-Install`, `Restore-Install`, `Backup-SmokeProfile` / `Restore-SmokeProfile`). Mutační kontrola:
  vypnutí obnovy zástupců v `Restore-Install` shodí 3 testy.
- **Zpevnění seamu (rozhodnutí uživatele 2026-11-01):** `Resolve-VerifyStandInsPath` (`tools/VerifyTools.psm1`) vrátí
  plnou cestu souboru jen uvnitř `LAYERPEEK_PESTER_TESTS_TEMP` nebo `[IO.Path]::GetTempPath()` (relativní cesta vůči
  umístění PowerShellu, `..` se normalizuje, sourozenecká složka se stejným začátkem názvu neprojde). Jinak skript
  vypíše chybu a skončí s kódem 2 ještě před `-ListChecks` i před výběrem kontrol. `Get-VerifyRunInfo -StandIns` přidá
  do `report.json` pole `Run.StandIns` a do `report.md` řádek „**Stand-ins:** …“; bez proměnné se zpráva nemění (pole
  ani řádek nevzniknou). Testy: odmítnutí cesty v repozitáři a cesty ven přes `..` (kód 2, nic nespuštěno, žádná
  složka běhu), pole a řádek ve zprávě testovacího běhu, jejich absence ve zprávě běžného běhu, pravidla cest.
- Nic se nezapisuje mimo dočasnou složku testu (`LAYERPEEK_PESTER_TESTS_TEMP`), `AfterAll` ji smaže.
- Žádný nový balíček.
- Review 2026-11-01 bez blokujících nálezů, zpřísnění háčku podle rozhodnutí uživatele; uzavřeno 2026-11-01.

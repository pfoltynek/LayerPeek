---
id: T-032
title: Zámky a úklid testů zvlášť pro každý worktree
status: done
owner: qa-reviewer
priority: P3
platform: all
depends_on: [T-030, T-031]
---

## Cíl
Testy spuštěné současně ve dvou git worktree se dnes navzájem blokují. `TestRunLock` i vyhrazené složky v `%TEMP%` mají jména společná pro celý počítač, takže druhý běh čeká až 5 minut a pak selže. Zjištěno při T-031: Core.Tests neproběhly, protože čekaly na zámek běhu z jiného worktree. Běhy z různých worktree se mají navzájem neblokovat a nesmí si mazat soubory.

## Rozsah
- `tests/Shared/TestRunLock.cs` a všechna jeho použití (Core.Tests `TestDirectories`, E2E `EdgeFixture`, UiTests `HeadlessSession` / `SnapshotAssert`): název zámku i vyhrazené složky v `%TEMP%` ponesou krátký stabilní hash cesty worktree (kořene repozitáře), např. `PrusaTrayDiagnostic.Core.Tests.<hash8>`.
- Úklid pozůstatků po pádu odstraní vlastní složky worktree. Kvůli pravidlu „testy po sobě nic nenechají“ odstraní i osiřelé složky worktree, které už neexistují: do každé složky se zapíše značka s cestou worktree a úklid smaže složky, jejichž worktree zmizel. Složky jiného existujícího worktree nikdy nemaže.
- E2E: úklid procesů smí ukončit jen procesy s profilem `--user-data-dir` pod vlastní složkou worktree.
- **Rozhodnuto 2026-10-18:** testy Windows Credential Manageru (`CredentialCleanupFixture`) zůstávají **globální**, tedy globální zámek i globální úklid testovacího prefixu. Jsou rychlé a sdílejí jedno systémové úložiště.
- Srozumitelná chybová hláška, když se zámek nepodaří získat (který zámek, jak dlouho se čekalo).

## Akceptační kritéria
- [x] Dva souběžné běhy `dotnet test` ze dvou různých worktree se neblokují a oba projdou (ověřeno ručně nebo testem se dvěma procesy).
- [x] Souběžné běhy ve stejném worktree se dál serializují.
- [x] Úklid po pádu odstraní vlastní pozůstatky i osiřelé složky smazaných worktree a nikdy nesáhne na složky jiného existujícího worktree (test).
- [x] Credential Manager testy beze změny chování (globální zámek a úklid).
- [x] Po běhu nic nezůstane v `%TEMP%`.
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- **Rozhodnuto 2026-10-18 (koordinátor):** oprava nestability `TelemetryHistoryTests` zůstává v T-032 (nalezena při jeho ověřování).
- **Rozhodnuto 2026-10-18 (koordinátor):** E2E smí ukončit procesy prohlížeče pod osiřelými a starými složkami, jen pod zámkem dané složky a nikdy pro jiný existující worktree — přijato.
- **Rozhodnuto 2026-10-18 (koordinátor):** mezera „složka bez značky“ se odstraní: vyhrazená složka vzniká atomicky se značkou (`<název>.<id>.tmp-<guid>` se značkou uvnitř, pak přejmenování na konečné jméno); úklid maže i zastaralé sourozence `*.tmp-*`, jejichž zámek je volný. Implementováno s testem (review).
- **Rozhodnuto 2026-10-18 (koordinátor):** nové umístění obrázků selhání UiTests (`%TEMP%\PrusaTrayDiagnostic.App.UiTests.<id>`) přijato.

## Poznámky k implementaci
- **Identita worktree:** `tests/Directory.Build.props` (nový, importuje kořenový) zapíše do každého testovacího assembly `AssemblyMetadata("TestWorktreeRoot")` = kořen repozitáře, ze kterého se testy sestavily. `tests/Shared/TestWorktree.cs`: `Root` (normalizovaná absolutní cesta bez koncového oddělovače), `Id` = prvních 4 bajty SHA-256 jako 8 malých hex znaků; na Windows se před hashem převádí na velká písmena (cesty nerozlišují velikost), jinde ne.
- **Vyhrazená složka:** `ReservedTempDirectory` = `%TEMP%\<název>.<id>` a zámek `%TEMP%\<název>.<id>.lock` (stávající `TestRunLock`, `DeleteOnClose`). Po úklidu se složka vytvoří atomicky se značkou `.worktree` (cesta worktree): nejdřív `<název>.<id>.tmp-<guid>` se značkou, pak přejmenování (`CreateAsync`).
- **Úklid po pádu (`SweepAsync`, pod vlastním zámkem):** smaže vlastní složku; projde složky `<název>.<8 hex>` jiných id a smaže jen tu, jejíž zámek se dá okamžitě získat (žádný běh), značka existuje, patří k id ve jméně a cesta worktree už neexistuje. Složky existujícího worktree, běžících běhů, bez značky nebo s cizím jménem nechá být. Starou složku bez hashe (`PrusaTrayDiagnostic.Core.Tests`, `PrusaTrayDiagnostic.E2E.Tests`, `PrusaTrayDiagnostic.App.UiTests.Snapshots`) smaže jen při okamžitě získaném starém zámku `<název>.lock`, tedy když neběží žádný běh se starým kódem (např. hlavní repozitář na starší větvi).
- **Uživatelé:** Core.Tests `TestDirectories` (+ fixture), E2E `EdgeFixture` (před smazáním libovolné složky ukončí procesy s `--user-data-dir` pod právě mazanou složkou; jen vlastní, osiřelé nebo staré, vždy pod jejich zámkem, nikdy jiného existujícího worktree), UiTests `HeadlessSession` / `SnapshotAssert` (obrázky selhání jsou nyní přímo v `%TEMP%\PrusaTrayDiagnostic.App.UiTests.<id>`, složka zůstane jen se snímky selhání). Fixture bez získaného zámku při ukončení nic nemaže.
- **Credential Manager beze změny:** `CredentialCleanupFixture` dál volá globální `TestRunLock.AcquireAsync("PrusaTrayDiagnostic.App.Tests", …)` (stejný soubor `%TEMP%\PrusaTrayDiagnostic.App.Tests.lock`) a globální úklid prefixu i `PrusaTrayDiagnostic.App.Tests.Shortcuts`.
- **Hláška při vypršení zámku:** `TimeoutException` s cestou zámku, dobou čekání v sekundách a radou (jiný běh téhož assembly ve stejném worktree, IDE nebo `dotnet test`).
- **Testy (Core.Tests/TestInfrastructure):** `TestWorktreeTests` (stabilita, normalizace, velikost písmen jen na Windows, různé worktree = různé id, metadata = kořen se `PrusaTrayDiagnostic.slnx`, značka vlastní složky) a `ReservedTempDirectoryTests` (značka, vlastní pozůstatky, osiřelá složka smazaného worktree + volání `beforeDelete`, existující worktree nedotčen, běžící běh nedotčen, bez značky / cizí jména nedotčena, stará složka se starým zámkem volným vs. drženým, serializace stejného worktree i při jiném zápisu cesty, různé worktree se neblokují, uvolněný zámek nezanechá soubor). Vše v sandboxu pod vyhrazenou složkou Core.Tests.
- **Oprava souvisejícího závodu:** `TelemetryHistoryTests.ReadWhileAsync` spouštěl čtenáře přes `Task.Run(..., stop.Token)`; při zátěži (dva souběžné běhy) skončili zapisovatelé dřív, než se čtenář naplánoval, a test padal na `TaskCanceledException`. Čtenáři se nyní spouštějí s `CancellationToken.None` (každý čte aspoň jednou a pak se zastaví tokenem) — test se neoslabil.
- **Ověření:** `dotnet build` 0 varování; `dotnet test` celé řešení 2112 testů (1 přeskočen – nenahrané fixtures) opakovaně zeleně. Ruční ověření s dočasným worktree `D:\00-Source\PTD-T032-probe` (detached, bez větve): při drženém zámku Core.Tests tohoto worktree prošel běh z probe za 2 s, běh tohoto worktree čekal do uvolnění zámku a pak prošel; 3× souběžně celé `dotnet test` v obou worktree, oba vždy zeleně za ~13 s. Po odstranění probe worktree jsem podstrčil jeho „pozůstatky po pádu“ (Core, E2E, UiTests se značkou) a starou složku `PrusaTrayDiagnostic.Core.Tests`; další běh je všechny smazal. `%TEMP%` je po bězích prázdný (žádné `PrusaTrayDiagnostic*`).

## Review
Review 2026-10-18 (qa-reviewer). Zaměření: bezpečnost úklidu, parsování značky, kolize hashe, normalizace cest, souběh se starým kódem, MSBuild.

**Ověřeno bez nálezu:**
- Úklid maže každou složku jen při drženém zámku (vlastní zámek po celou dobu; cizí/starý zámek přes `using` přes celé `await DeleteAsync`), takže mezi kontrolou „zámek volný“ a smazáním nemůže začít jiný běh (TOCTOU). Cizí zámky se jen zkoušejí (`TryAcquire`), nikdy se na ně nečeká → dva souběžně uklízející běhy nemohou uváznout; o osiřelou složku „vyhraje“ jen jeden.
- `Directory.Delete(recursive)` v .NET nenásleduje junction ani symlink (odstraní jen odkaz) — nově ověřeno testem s junction uvnitř osiřelé složky.
- Kolize 8 hex (2^-32): dva worktree se stejným id sdílí složku i zámek, běhy se jen serializují a nikdy se nepřekrývají; bezpečné (doplněno do dokumentace `SweepAsync`).
- Normalizace: velikost písmen jen na Windows, koncové oddělovače, `..`, `/` vs `\`; koncové tečky ořízne `GetFullPath`. `subst`/junction/UNC zápis téhož worktree dá jiné id → běhy se neserializují, ale používají oddělené složky; cizí složku úklid nesmaže (existuje / drží zámek). Bezpečné.
- Starý kód v hlavním repozitáři: maže jen přesné cesty (`…Core.Tests`, `…App.UiTests.Snapshots`, E2E marker `…E2E.Tests\` s oddělovačem) → hashované složky ani nové prohlížeče nezasáhne; nový kód maže starou složku jen při volném starém zámku.
- `tests/Directory.Build.props` importuje kořenový právě jednou (ověřeno `dotnet msbuild -pp`), metadata `TestWorktreeRoot` jsou ve všech 6 testovacích assembly.
- Credential Manager testy (`CredentialCleanupFixture`) beze změny (globální zámek i úklid).
- Oprava `TelemetryHistoryTests` test neoslabuje (čtenář čte vždy aspoň jednou, zastaví se tokenem).

**Nálezy a opravy (opraveno v review):**
1. (střední, rozhodnutí koordinátora 3) `tests/Shared/TestWorktree.cs` `Create()` vytvářel složku a značku zvlášť → po pádu mezi nimi zůstala složka bez značky, kterou nikdo nesmaže. Nahrazeno `CreateAsync()`: složka `<název>.<id>.tmp-<guid>` se značkou, pak `Directory.Move` (s opakováním); při chybě se staging smaže. `SweepAsync` maže zastaralé staging složky (přesný tvar `.tmp-` + 32 malých hex), jejichž zámek je volný (vlastní pod vlastním zámkem), bez volání `beforeDelete` (staging nikdy nemá procesy). Upraveni volající (Core `TestDirectories`, E2E `EdgeFixture`, UiTests `SnapshotAssert`). Testy: `SweepRemovesStagingFoldersWhoseLockIsFree`, rozšířený `CreateWritesTheMarkerWithTheWorktreePath`.
2. (nízká) Robustnost značky: obří soubor se četl celý; cesta s neplatnými znaky (např. `\0`) by v `IdOf` vyhodila `ArgumentException` a shodila úklid celého assembly. Nyní limit 32 KiB, odmítnutí `\0`, `TryIdOf`. Test `SweepKeepsAFolderWhoseMarkerIsOversizedOrNotAPath` (obří, `\0`, relativní cesta, značka jako složka).
3. (nízká, čitelnost) `TestRunLock` — výpočet doby čekání vytažen z dlouhého interpolovaného řetězce; text hlášky beze změny.
4. Test `SweepDeletesAJunctionInsideAFolderWithoutFollowingIt` (jen Windows) — junction ven z TEMP se při úklidu nenásleduje.

**Poznámky (bez změny, k zvážení):**
- `Directory.Exists(worktree)` vrací `false` i pro dočasně nedostupný worktree (odpojená síťová/`subst` jednotka) → jeho *pozůstatky po pádu* (zámek volný) se smažou. Jde jen o odpadky po pádu, ne o běžící běh; přijatelné.
- Hláška `TimeoutException` neříká, zda jde o zámek worktree, nebo globální (Credential); cesta zámku to ale prozradí (obsahuje id).

**Výsledky:** `dotnet build` 0 varování / 0 chyb; `dotnet test` celé řešení 2× po sobě 2115 testů (1 přeskočen – nenahrané fixtures), 0 selhání. Souběžný běh s dočasným detached worktree `D:\00-Source\PTD-T032-review` (se zkopírovanými změnami): oba zeleně. Pak záměrně shozený E2E běh v probe worktree (zůstala složka + 4 procesy Edge), podstrčená osiřelá Core složka a staging složka: běh tohoto worktree, dokud probe existoval, jeho složky ani procesy nechal být (staging s volným zámkem smazal); po `git worktree remove --force` další běh smazal vše a ukončil procesy. `%TEMP%` bez `PrusaTrayDiagnostic*`. Nálezy nezůstávají.

### Review – druhé kolo (2026-10-18, kontrola vlastních oprav z prvního kola)
- `CreateAsync`: pokud konečná složka už existuje (pod zámkem po úspěšném úklidu nemůže, jen při ruční chybě), `Directory.Move` selže, opakování je omezené (5 pokusů, cca 1 s) a skončí `InvalidOperationException`; staging se smaže v `finally` při každé chybě (zrušení se tu nepoužívá, pád procesu uklidí příští úklid). Nový test `CreateOverAnExistingFolderFailsAndLeavesNoStagingFolder`.
- Úklid staging složek: přesná shoda jména (`<název>.<8 hex>.tmp-<32 malých hex>`), mazání pod drženým zámkem; staging rozpracovaného vytváření jiného běhu se nesmaže, protože ten běh drží svůj zámek po celou dobu úklidu i vytváření (Core fixture, E2E, UiTests). Bez nálezu.
- Limity značky / NUL / značka jako složka: bez nálezu.
- Test s junction: jen Windows (`Assert.Skip` jinde), `mklink /J` nepotřebuje admina, junction i cílová složka leží v sandboxu, takže je uklidí `Dispose` i při selhání testu; doplněno čtení stdout `mklink`.
- **Nález (střední, opraveno):** `Directory.Delete(recursive)` mazal značku `.worktree` mezi prvními (řazení podle jména); když mazání selhalo uprostřed (otevřený soubor), zůstala složka bez značky — přesně mezera z rozhodnutí 3. `DeleteAsync` nyní maže značku až nakonec (`DeleteMarkerLast`). Test `DeletionThatFailsHalfWayKeepsTheMarker` (jen Windows; se starým kódem by selhal).
- Výsledky: `dotnet build` 0 varování / 0 chyb; `dotnet test` celé řešení 2× 2117 testů (1 přeskočen), 0 selhání; `%TEMP%` bez `PrusaTrayDiagnostic*`. Nálezy nezůstávají.

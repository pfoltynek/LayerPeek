---
id: T-228
title: Windows – 47 selhávajících testů na develop (snapshoty UI, proxy, porty E2E, FFmpeg, Pester)
status: done
owner: qa-reviewer
priority: P1
platform: windows
depends_on: []
---

## Cíl
Při práci na T-201 (2026-11-12, Windows 11 Pro 10.0.26300) skončil plný `dotnet test` se 47 selháními, která selhávají
už na `develop` `2ce7be01` bez změn T-201 (ověřeno v dočasném worktree). `develop` má být na Windows zelený; úkol má
přednost před T-202 (rozhodnutí uživatele 2026-11-12).

## Rozsah
Selhání (10 996 testů, 47 FAIL):
- `LayerPeek.App.UiTests`: `SettingsWindowSnapshotTests` (40×) a `AboutWindowLayoutTests` (3×) – snapshoty Windows
  nejspíš neodpovídají posledním změnám (např. snímky obrazovky přegenerované na macOS, změny rozvržení).
- `LayerPeek.PrusaLink.Tests`: `WebProxySecurityTests.ListenerIsNotReachableOnOtherAddresses` (síťová rozhraní).
- `LayerPeek.E2E.Tests`: `BrowserLifecycleTests.DisposedRigReleasesThePrinterAndProxyPorts` (porty).
- `LayerPeek.Camera.FFmpeg.Tests`: `GrayscaleSampleEquivalenceTests` (rozdíl 1,72, limit 1,5).
- `LayerPeek.Scripts.Tests`: Pester běh `scripts/ReleaseTools/ReleaseTools.Tests.ps1`.

Postup: u každého určit příčinu (chyba aplikace / zastaralý snapshot / závislost na prostředí stroje / nestabilní test)
a opravit ji. Snapshoty přegenerovat jen tehdy, když je změna vzhledu záměrná (dohledat commit); nečekaná změna vzhledu =
chyba. Změnu tolerance nebo přeskakování testu nejdřív odůvodnit, při nejistotě otázka uživateli.

## Akceptační kritéria
- [x] Příčina každé skupiny selhání zjištěná a zapsaná v poznámkách (s commitem, který ji způsobil, pokud jde dohledat)
- [x] Plný `dotnet test` na Windows bez selhání (2× za sebou)
- [x] `dotnet build` bez chyb a varování

## Otevřené otázky
1. ~~**`GrayscaleSampleEquivalenceTests.TheLumaPlaneSamplesMatchTheBgraSamplesOfTheFixture` na Windows x64** – meze
   (průměrný |rozdíl| ≤ 1,5, průměrný posun ≤ 1) jsou z jediného měření na arm64 (1,31 / +0,87, T-189), na x64 vychází
   1,72 / +1,44 (aritmetika swscale podle CPU). Zvýšit meze pro obě platformy (a), meze podle architektury (b), nebo
   jiný postup (c)?~~
   **Rozhodnuto uživatelem 2026-11-12:** varianta b) – meze podle `RuntimeInformation.ProcessArchitecture`: arm64 beze
   změny (≤ 1,5 / ≤ 1), x64 průměrný |rozdíl| ≤ 2,0, posun ≤ 1,5; jiná architektura test jasně shodí (neměřené meze).

## Poznámky k implementaci
**`SettingsWindowSnapshotTests` (40×) – zastaralé snímky, záměrná změna vzhledu.** `9e48dd59` (T-185, Mac 2026-11-11)
přidal šestou hlavní záložku „Kamera“ a zkrátil záhlaví (fr „Démarrage, MàJ“, it „Riprese“, …); sada Windows byla
přesunutá do T-125 (2w). Rozdílové obrázky ukazují jen řádek záložek. Přegenerováno `LAYERPEEK_UPDATE_SNAPSHOTS=1`,
zkontrolováno (fr, it, de, tmavý motiv), položka v T-125 odškrtnutá.

**`AboutWindowLayoutTests` (3×) – zastaralé snímky, záměrná změna vzhledu.** `400417a8` (T-199) přidal větu o FFmpeg
a odkaz „FFmpeg source code of this version“; okno je 448 × 506 místo 484 (v mezi 508 DIP). Přegenerováno
a zkontrolováno (en, de s chybou), položka v T-125 (2ab) odškrtnutá.

**`WebProxySecurityTests.ListenerIsNotReachableOnOtherAddresses` a `BrowserLifecycleTests.DisposedRigReleasesThe
PrinterAndProxyPorts` – závislost na prostředí (chyba OS), ne chyba aplikace.** Windows 11 build 26300 tohoto počítače
(poslední kumulativní aktualizace KB5127936 z 2026-11-11) vrací v tabulce TCP (`GetTcpTable` / `GetExtendedTcpTable`)
nesmyslné stavy řádků: většina naslouchajících soketů má stav 0, některá spojení a TIME_WAIT stav LISTEN. Proto
`IPGlobalProperties.GetActiveTcpListeners()`, `netstat -an` i `Get-NetTCPConnection` naslouchající soket proxy nevidí
(ověřeno samostatným programem s `TcpListener` na 127.0.0.1). Testy proxy s `Assert.False(IsListening(...))`
(`WebProxyLifecycleTests`) přitom procházely jen náhodou. Oprava v testovací infrastruktuře: nový sdílený
`tests/Shared/Network/TcpListenerTable.cs` – na Windows čte `GetExtendedTcpTable` (IPv4 i IPv6, `[LibraryImport]`) a za
naslouchající bere řádky bez vzdáleného konce (0.0.0.0:0 / [::]:0) bez ohledu na pole stavu; jinde
`GetActiveTcpListeners` beze změny. Používá ho `ProxyTestContext.Listeners` a `BrowserLifecycleTests.IsListening`
(oba projekty mají `AllowUnsafeBlocks` jen kvůli interopu). Nové testy `TcpListenerTableTests` (loopback IPv4, IPv6,
zavřený listener se spojením v TIME_WAIT na stejném portu se nevypíše). Aserce testů beze změny.

**Pester `ReleaseTools.Tests.ps1` („archives exactly the pinned commit … without line ending conversion“) – chyba testu
specifická pro Windows.** `400417a8` (T-199, Mac) kontroloval CR v `tar -xOf … | Out-String`; PowerShell výstup
nativního programu rozdělí na řádky a `Out-String` je na Windows spojí s CRLF, takže CR přidal sám test (archiv
byl správný, SHA-256 seděl). Test teď soubor rozbalí do dočasné složky testu a porovná obsah přesně
(`"#!/bin/sh`necho configure`n"` bez CR). MSYS2 v `C:\msys64` ani ARM64 Build Tools s tím nesouvisí (tar je vždy
`System32\tar.exe`).

**`GrayscaleSampleEquivalenceTests` – meze změřené jen na arm64** (`e4b37a11`, T-189; na Windows selhával od vzniku,
ověřeno v dočasném worktree na tomto commitu). swscale počítá převod YUV → BGRA staré cesty (i zmenšení) jiným SIMD kódem
podle CPU (NEON / SSE-AVX): fixture `buddy3d.h264` dává na arm64 1,31 / +0,87, na x64 1,72 / +1,44 se stejným FFmpeg
9.0.2; ani s `SWS_ACCURATE_RND | SWS_BITEXACT` (dočasný pokus) to není pod 1,5 (1,54 / +1,20). Detekce tím dotčená
není: bed-change na fixture (dočasný pokus) dává v obou cestách stejné poplachy ve stejný čas, max. rozdíl skóre ve
výřezu Core One 0,0006 (tolerance 0,0008). Oprava podle rozhodnutí uživatele (otázka 1, varianta b): meze podle
architektury v `Limits` s komentářem naměřených hodnot a příčiny; stejná mez průměrného |rozdílu| platí i pro test
lokálního klipu (na Windows se bez klipu přeskočí). Jiná architektura než arm64 / x64 test shodí s výzvou meze změřit.

Ověření: `dotnet build` 0 varování; plný `dotnet test` 2× za sebou: 11 056 testů, 10 672 prošlo, 384 přeskočeno,
0 selhání. V T-125 odškrtnuté snímky T-185 / T-199 a automatické položky `dotnet test` T-185, T-195, T-199.

Bez nových balíčků. Dočasné pokusy (příznaky swscale, skóre detektoru na fixture) byly vrácené.

---
id: T-053
title: macOS – zabezpečení socketu jediné instance (L2) a ověření `/usr/bin/open` (L4)
status: in-progress
owner: ui-dev
priority: P3
platform: macos
depends_on: [T-043, T-052, T-095]
---

## Cíl
Opravit nález L2 z bezpečnostní revize (2026-10-21, závažnost **nízká**) a na Macu ověřit opravu L4 z T-052.

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu. Neblokuje žádný Windows úkol.

L2:
- `src/LayerPeek.App/SingleInstance/UnixSocketInstanceTransport.cs:84-104`: server Unix socketu nekontroluje, kdo se připojil (žádné `getpeereid` / `LOCAL_PEERCRED`). Na Windows to řeší `PipeOptions.CurrentUserOnly`. Na macOS spoléhá jen na práva složky.
- `src/LayerPeek.App/SingleInstance/InstanceTransports.cs:95-107` (`UnixPaths`): když je cesta k socketu delší než 103 bajtů, socket se přesune do `Path.GetTempPath()`. Na macOS je `$TMPDIR` obvykle soukromý pro uživatele (`/var/folders/...`), ale když je nastavený na sdílené `/tmp`, může jiný uživatel cestu předem obsadit. Následek je DoS: aplikace nepoběží jako jediná instance, nebo neuslyší příkazy.

## Rozsah
- **Kontrola protistrany:** po `Accept` zjistit UID připojeného procesu, např. `getpeereid(fd, out uid, out gid)` z `libc` přes `[LibraryImport]`, nebo `LOCAL_PEERCRED` přes `Socket.GetRawSocketOption`. Spojení od jiného UID než `geteuid()` zavřít bez čtení a zalogovat (bez obsahu). Linux, který aplikace nepodporuje, ale kód pro něj existuje: `SO_PEERCRED`, případně jen zdokumentovat.
- **Soukromá složka pro socket:** záložní socket nedávat přímo do `$TMPDIR`, ale do vlastní podsložky (např. `LayerPeek-<uid>`) vytvořené s právy `0700` (`Directory.CreateDirectory(path, UnixFileMode.UserRead | UserWrite | UserExecute)`). Když složka už existuje, ověřit, že patří aktuálnímu uživateli, má práva `0700` a není to symbolický odkaz. Jinak ochranu jediné instance vypnout, zalogovat a aplikace poběží (stejná politika jako `InstanceLockUnavailableException` z T-043). Totéž ověření pro datovou složku, pokud je socket v ní.
- **L4:** na Macu ověřit, že odkazy z menu a okna O aplikaci a otevření webu PrusaLink přes proxy fungují s `/usr/bin/open` (T-052).
- Bez nového NuGet balíčku, jen `[LibraryImport]` (AOT).
- **Izolace testů:** testy socketu, práv `0700`, cizího vlastníka a symbolického odkazu běží v soukromé podsložce pod `$TMPDIR` (ne v repozitáři: svazek s repozitářem může být připojený s `noowners`, vlastnictví se tam nevynucuje, viz T-095) a po sobě ji smažou.

## Akceptační kritéria
- [ ] Spojení od jiného UID se odmítne (test s injektovanou kontrolou UID; na Macu skutečný test, pokud jde bez druhého uživatele, jinak ručně)
  - automatická část hotová (injektovaný test + skutečný test se stejným UID); ruční test s druhým uživatelem **neověřen**, viz poznámky
- [x] Záložní socket je v podsložce `0700` aktuálního uživatele; cizí, symbolickým odkazem nahrazená nebo příliš otevřená složka vypne ochranu bez pádu (testy)
- [x] Druhé spuštění na Macu dál předá příkaz běžící instanci (krátká i dlouhá cesta k datové složce)
- [ ] Ručně na Macu: `/usr/bin/open` otevírá odkazy a web PrusaLink přes proxy (L4)
- [x] Testy po sobě nic nenechají (sockety, složky, zámky); testy práv a vlastnictví běží v soukromé podsložce `$TMPDIR`, ne v repozitáři
- [ ] Lokální Native AOT publish `osx-arm64` bez varování; Windows build a testy beze změny chování
  - AOT `osx-arm64` hotovo (0 varování); Windows build a testy **neověřeny** (vyvíjeno jen na Macu)
- [x] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Pravidlo pro datovou složku~~ → (a) ponechat: skutečná složka, vlastník = aktuální uživatel, nikdo jiný nesmí zapisovat (`0755` projde); soukromá složka v `$TMPDIR` přesně `0700`. Aplikace práva uživatelských složek nemění (rozhodnuto 2026-10-29).
- ~~umask `002`~~ → ochrana se vypne a zaloguje, aplikace běží; složku neopravuje (rozhodnuto 2026-10-29).

## Poznámky k implementaci
- **Závislost T-095:** T-095 je zatím v `in-progress` jen kvůli čekajícímu ověření na Windows. Uživatel výslovně požádal začít T-053 hned.
- **Kontrola protistrany (L2):** `UnixSocketInstanceTransport.Listener.AcceptAsync` po `Accept` zjistí UID klienta. Když se liší od `geteuid()` nebo ho OS nevrátí, spojení zavře bez čtení a vyhodí `InstancePeerRefusedException`. `InstanceCommandServer` to zaloguje jako varování („Refused a connection of another user (uid N)…“, bez obsahu a bez výjimky) a hned přijímá další spojení, bez pauzy 500 ms jako u jiných chyb `Accept`.
- **Volba API:** `getpeereid` z libc přes `[LibraryImport]` (`Platform/MacOS/MacUnixNative.cs`), ne `LOCAL_PEERCRED` přes `Socket.GetRawSocketOption`. Je to dokumentovaná funkce s pevnou signaturou (macOS ji sám staví na `LOCAL_PEERCRED`), takže v kódu není natvrdo rozložení `struct xucred`. Knihovna se načítá absolutní cestou `/usr/lib/libSystem.B.dylib`, ne holým jménem `libc`, které .NET nejdřív hledá vedle aplikace. Vlastníka a typ složky zjišťuje `lstat` (.NET veřejné API pro vlastníka souboru nemá): na arm64 symbol `lstat`, na x64 `lstat$INODE64`, z `struct stat` se čte jen `st_mode` (offset 4) a `st_uid` (offset 16), buffer má rezervu 256 B. Na x64 **neověřeno**, není k dispozici Intel Mac.
- **Seam pro testy:** `IUnixCredentials` (`SingleInstance/UnixCredentials.cs`): `EffectiveUserId`, `TryGetPeerUserId`, `GetEntry` (lstat). Produkčně `MacUnixCredentials.Current`, jen na macOS. Bez credentials (Windows, kde se Unix socket používá jen v testech, a nepodporovaný Linux) se nic nového nekontroluje, chování T-043 se nemění. Linux je jen zdokumentovaný: protějšky by byly `SO_PEERCRED` a vlastní rozložení `struct stat`.
- **Soukromá složka:** `InstanceTransports.UnixPaths(data, temp, uid, suffix)` vrací `UnixInstancePaths`. Když je cesta k socketu v datové složce delší než 103 bajtů, socket jde do `$TMPDIR/LayerPeek-<uid>/instance<suffix>.sock` a složka se vytvoří jako `0700` (`Directory.CreateDirectory(path, UnixFileMode)`). Bez `uid` (Linux) zůstává původní `$TMPDIR/LayerPeek-instance<suffix>.sock`.
- **Ověření složky:** `TryAcquireLock` (ještě před zámkem) a znovu `Listen` vytvoří chybějící složku socketu a ověří ji (`SocketDirectoryProblem`): musí existovat, nesmí to být symbolický odkaz ani soubor, musí patřit `geteuid()`, soukromá složka musí mít přesně `0700` a datová složka nesmí být zapisovatelná pro skupinu a ostatní (viz otázky). Jinak se vyhodí `InstanceLockUnavailableException` a guard se zachová jako v T-043: zaloguje chybu, vrátí `Primary`, aplikace běží bez ochrany a nic se nezapíše přes symbolický odkaz (ani zámek). Selhání v `Listen` skončí v existujícím `LogListenFailed`. Ověřuje se i u druhého spuštění, takže nepošle příkaz do cizí složky.
- **Testy (macOS, vše pod `$TMPDIR`):** `UnixSocketInstanceTransportTests` běží na macOS se skutečnými credentials. Nové testy pokrývají: soukromou složku `0700` s předáním příkazu; cizího vlastníka (injektované jiné UID); symbolický odkaz (cíl zůstane prázdný); příliš otevřená práva (`0755`/`0701` u soukromé složky, `0775` u datové); soubor místo složky; odmítnutí spojení jiného UID i neznámého UID (injektované, běží i na Windows); skutečný `getpeereid` se stejným UID na obou koncích; skutečný `lstat` (složka, soubor, odkaz, chybějící cesta, `/usr` patří rootu); čistou funkci `SocketDirectoryProblem`. Server test: odmítnutí se zaloguje bez obsahu a server pokračuje.
- **Dvouprocesový test na Macu:** `SecondProcessTests.SecondStartOfTheMacAppForwardsActivateAndExitsWithZero(short/long)` spustí skutečný Debug apphost `LayerPeek` v testovacím režimu (`LAYERPEEK_TEST_DATA_DIR`, přípona jména, forward-only). U dlouhé datové složky dostane jako `TMPDIR` krátkou rezervovanou složku, takže soukromá složka `LayerPeek-<uid>` vzniká v ní. Test ověří `0700`, předání `activate`, kód 0, log v testovací složce a nedotčený produkční log.
- **Úklid:** cesty pod `TestDirectories.Root` jsou na macOS na socket příliš dlouhé, proto `TestDirectories` dostal druhou rezervaci s krátkým jménem `lpt.<id>` (`ShortReserved`, stejný zámek, úklid po pádu i smazání jako hlavní). Fixture bere zámky vždy ve stejném pořadí. Po celém `dotnet test` v `$TMPDIR` nezůstalo nic s `lpt` ani `LayerPeek`.
- **Ověřeno na macOS 27.2 (Apple Silicon):** `dotnet build` 0 varování, 0 chyb. Celý `dotnet test`: 7033 testů, 6810 úspěšných, 223 přeskočených, 0 neúspěšných (`LayerPeek.App.Tests` +3309/?157). `dotnet publish -c Release -r osx-arm64` 0 varování MSBuild/IL (dsymutil/xcodebuild hlásí jen šum nativní sady nástrojů z Command Line Tools, nejde o varování sestavení). Výsledné exe se nespouštělo. `tools/profile-snapshot.ps1 -Compare`: beze změny (Login Items „not checked“).
- **Neověřeno – ruční kroky pro uživatele** (souhrnně v [`docs/macos-manual-testing.md`](../../docs/macos-manual-testing.md), sezení 10 a krok 2.6):
  1. *Jiné UID (L2):* na Macu s druhým uživatelským účtem `other` spusťte Debug aplikaci v testovacím režimu, např. `LAYERPEEK_TEST_DATA_DIR=/tmp/lp-t053 LAYERPEEK_INSTANCE_SUFFIX=t053 dotnet run --project src/LayerPeek.App`. Složka `/tmp` je sdílená, aby k ní měl přístup i druhý účet; cesta k socketu je krátká, socket je tedy v datové složce. Udělejte `chmod 755 /tmp/lp-t053` a `chmod 777 /tmp/lp-t053/instance-t053.sock`, aby se druhý uživatel vůbec dostal ke `connect`. Pak jako `other`: `sudo -u other /usr/bin/nc -U /tmp/lp-t053/instance-t053.sock` a napište `activate`. Očekávání: spojení se hned zavře a v `/tmp/lp-t053/logs/LayerPeek.log` je „Refused a connection of another user (uid …)“. Žádné okno se neaktivuje. Nakonec ukončete aplikaci a `rm -rf /tmp/lp-t053`.
  2. *L4 `/usr/bin/open`:* spusťte simulátor (`dotnet run --project tools/PrusaSimulator`) a aplikaci v testovacím režimu jako výše, se složkou v `$TMPDIR`. Kontrola, že se nehledá v `PATH`: spusťte aplikaci s `PATH="$TMPDIR/fakebin:$PATH"`, kde `$TMPDIR/fakebin/open` je spustitelný skript `#!/bin/sh` + `touch "$TMPDIR/fakebin/HIT"`. V nastavení přidejte tiskárnu simulátoru. Ověřte, že (a) „Otevřít web“ z menu tiskárny otevře výchozí prohlížeč na `http://127.0.0.1:<port>/…` proxy a web PrusaLink je přihlášený, (b) odkazy v okně O aplikaci (web projektu, licence) se otevřou v prohlížeči, (c) soubor `HIT` nevznikl a log nemá „Opening … failed“. Nakonec `rm -rf "$TMPDIR/fakebin"` a testovací složku.
  3. *Windows:* `dotnet build` a `dotnet test` na Windows (Windows cesta se nemění, credentials jsou `null`; testy `UnixSocketInstanceTransportTests` pro macOS se tam přeskočí, injektované testy odmítnutí poběží).

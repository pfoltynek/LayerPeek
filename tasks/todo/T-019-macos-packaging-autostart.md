---
id: T-019
title: macOS – .app bundle, distribuce a autostart
status: todo
owner: ui-dev
priority: P1
platform: macos
depends_on: [T-008, T-015, T-055]
---

## Cíl
Spustitelná a distribuovatelná `.app` pro macOS bez ikony v Docku, spouštění po přihlášení.

Priorita P1, protože `.app` bundle s bundle identifikátorem je předpokladem pro macOS notifikace (T-020).

> **Pouze na macOS:** úkol implementuje a ověřuje uživatel později na Macu (Native AOT pro macOS lze sestavit jen na macOS, vyžaduje Xcode Command Line Tools – viz `docs/prerequisites.md`). Neblokuje žádný Windows úkol.

Rozhodnutí uživatele (2026-10-21):
- **RID:** jen `osx-arm64` (Apple Silicon). Žádný `osx-x64`, žádný universal binary (`lipo`).
- **Formáty:** přednostně instalátor, tedy **`.dmg`**, a k tomu archiv **`.tar.gz`**.
- **Podpis:** bez Apple Developer ID a bez notarizace, jen **ad-hoc podpis** (`codesign -s -`).
- **Sestavení a vydání:** lokálně na Macu skripty z T-055 (CI odloženo, T-016). Běh na Macu přidá soubory `osx-arm64` do stejného GitHub vydání, které vytvořil běh na Windows.

## Rozsah
- Native AOT publish pro `osx-arm64`
- `.app` bundle s `Info.plist` (bundle identifikátor, `LSUIElement` – bez ikony v Docku), ad-hoc podpis celého bundlu včetně nativních knihoven (FFmpeg z T-026, až bude)
- Balicí kroky do bodu rozšíření skriptů T-055: `.app` → `.dmg` a `.tar.gz` s `.app` (místo dočasného `.tar.gz` publish složky z T-055)
- Pomocná okna otevřená z menu se u aplikace s `LSUIElement` musí dostat do popředí
- Autostart po přihlášení (Login Items)
- Dokumentace pro uživatele: jak spustit aplikaci staženou z internetu s ad-hoc podpisem (Gatekeeper; na macOS 15 a novějším přes Nastavení systému → Soukromí a zabezpečení → „Přesto otevřít“, případně odstranění atributu `com.apple.quarantine`)

## Rizika ad-hoc podpisu
- **Gatekeeper:** stažená aplikace s ad-hoc podpisem (bez notarizace) se po prvním spuštění zablokuje a uživatel ji musí povolit ručně.
- **T-020 (notifikace):** `UNUserNotificationCenter` vyžaduje bundle a podpis. S ad-hoc podpisem nemusí notifikace fungovat nebo může macOS žádat o povolení znovu po každém sestavení. Ověřit v T-020.
- **T-021 (Keychain):** ad-hoc podpis se mění s každým sestavením, takže macOS může po každé aktualizaci znovu žádat o povolení přístupu k položkám Keychainu. Ověřit v T-021.

## Akceptační kritéria
- [ ] Postup sestavení `.app`, `.dmg` a `.tar.gz` lokálně na macOS zdokumentovaný v `docs/release.md` (z T-055)
- [ ] Skript T-055 na Macu vytvoří `.dmg`, `.tar.gz` a `SHA256SUMS-macos.txt` a přidá je do existujícího GitHub vydání pro tag (ověřeno)
- [ ] `codesign --verify --deep --strict` na `.app` projde (ad-hoc podpis)
- [ ] Aplikace běží bez ikony v Docku, ikona je jen v menu baru
- [ ] Okno Nastavení (T-015) otevřené z menu se zobrazí v popředí a dostane fokus
- [ ] Autostart po přihlášení funguje na macOS (ověřeno)
- [ ] Spuštění stažené `.dmg` na Macu s Gatekeeperem ověřené a postup popsaný v dokumentaci
- [ ] `docs/prerequisites.md` aktuální, pokud přibyly nástroje
- [ ] `dotnet build` a `dotnet test` bez chyb a varování

## Otevřené otázky
- ~~Podepisování a notarizace~~ → jen ad-hoc podpis (rozhodnuto 2026-10-21). Rizika pro T-020 a T-021 viz výše.
- ~~Podporovat i Intel Macy (`osx-x64`)~~ → ne, jen Apple Silicon `osx-arm64` (rozhodnuto 2026-10-21).
- ~~Formát distribuce~~ → `.dmg` + `.tar.gz` (rozhodnuto 2026-10-21).
- Nástroj pro `.dmg`: `hdiutil` (součást macOS), nebo jiný nástroj? Velopack na macOS vytváří `.pkg`, ne `.dmg` (viz T-056) – pokud by se Velopack používal i pro aktualizace na macOS, je `.pkg` místo `.dmg` přijatelný?
- Automatické aktualizace – viz rozhodnutí v T-017 a výsledek T-056.

## Poznámky k implementaci

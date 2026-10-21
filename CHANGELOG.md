# Changelog

Všechny podstatné změny aplikace PrusaTrayDiagnostic. Formát podle [Keep a Changelog 1.1.0](https://keepachangelog.com/cs/1.1.0/), verze podle [Semantic Versioning](https://semver.org/lang/cs/).

Text sekce verze se při vydání použije jako poznámky k vydání na GitHubu (`scripts/release-upload.ps1`, viz [docs/release.md](docs/release.md)). Pravidla:

- Nové změny se píší do sekce `## [Unreleased]`.
- Při vydání se sekce přejmenuje na `## [X.Y.Z] - RRRR-MM-DD` (skutečné datum vydání) a nad ni se přidá nová prázdná `## [Unreleased]`.
- Skript vydání odmítne, když sekce verze chybí, nemá platné datum nebo je prázdná.
- Podsekce: `### Přidáno`, `### Změněno`, `### Opraveno`, `### Odebráno`, `### Zabezpečení`.

## [Unreleased]

## [0.1.0] - RRRR-MM-DD

<!-- Před vydáním 0.1.0 doplnit skutečné datum vydání a zkontrolovat text. -->

### Přidáno

- První vydání: aplikace v oznamovací oblasti (Windows) pro sledování tiskáren Prusa přes PrusaLink.
- Stav a průběh tisku v ikoně, menu ikony, systémové notifikace o událostech tisku.
- Okno diagnostiky s teplotami a historií telemetrie, podpora více tiskáren.
- Okno nastavení (tajné údaje ve Windows Credential Manager), náhled a okno kamery.

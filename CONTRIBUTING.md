# Contributing

Thanks for your interest in Prusa Tray Diagnostic! Bug reports, ideas, translations and pull requests are all welcome.

## Bugs and ideas

Open an [issue](https://github.com/pfoltynek/PrusaTrayDiagnostic/issues) using one of the templates. For bugs, the
short report or the diagnostic bundle from the **About** window helps a lot. Security issues go through
[SECURITY.md](SECURITY.md), not public issues.

**Tested a printer model that isn't marked as tested in the README?** Please tell us in an issue, even when everything
works.

## Translations

UI strings are in `src/PrusaTrayDiagnostic.App/Resources/Strings.*.resx` (English is `Strings.resx`). Every string
must exist in all languages. Fixes of existing translations and new languages are very welcome.

## Pull requests

1. Fork the repository and create a branch from `develop` (the project uses Gitflow; `main` holds releases only).
2. Make your change, with tests. See [Building from source](README.md#building-from-source).
3. `dotnet build` and `dotnet test` must pass with **zero warnings**, because warnings are treated as errors.
4. Add a line to `CHANGELOG.md` under `[Unreleased]` for user-visible changes.
5. Open the PR against `develop`.

## Coding rules

- **Native AOT:** no unconstrained reflection, `System.Text.Json` only through source-generated contexts, Avalonia
  compiled bindings only. Every new NuGet package must be trim/AOT compatible and have a GPL-3.0-compatible license
  (MIT, BSD, Apache-2.0, LGPL…).
- Async all the way with `CancellationToken`, no `.Result` / `.Wait()`.
- Secrets never in logs or in plain-text settings.
- Tests use recorded fixtures or loopback-only servers (the [simulator](docs/simulator.md)), never the real network.
- Code, comments and commit messages are in English. Task and design notes in `tasks/` and `docs/` are in Czech.

The full set of rules is in [CLAUDE.md](CLAUDE.md).

## License

There is no CLA. By submitting a contribution you agree that it is licensed under the project's license
(GPL-3.0-only).

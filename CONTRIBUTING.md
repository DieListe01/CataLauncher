# Contributing

Vielen Dank fuer deinen Beitrag zu `CatanLauncher`.

## Branching

- Nutze kurze, beschreibende Branch-Namen, zum Beispiel:
  - `feature/github-update-check`
  - `fix/release-installer-path`

## Commit Messages

Bitte verwende moeglichst das Conventional-Commit-Format:

`<type>(optional-scope): <kurze beschreibung>`

Beispiele:

- `feat(launcher): add github release update check`
- `fix(update): handle missing release asset`
- `docs(ci): explain release tag conventions`

Erlaubte `type` Werte (werden im Changelog gruppiert):

- `feat`
- `fix`
- `refactor`
- `perf`
- `build`
- `ci`
- `docs`
- `test`
- `chore`

## Pull Requests

- Halte PRs moeglichst klein und thematisch fokussiert.
- Beschreibe kurz:
  - was geaendert wurde
  - warum es geaendert wurde
  - wie getestet wurde
- Verlinke passende Issues, falls vorhanden.

## Release Tags

Die Workflows reagieren auf folgende Tags:

- Stable Release: `v1.2.3`
- Beta Release: `v1.3.0-beta.1`
- RC Release: `v1.3.0-rc.1`

Hinweise:

- Stable-Tags duerfen kein `-suffix` enthalten.
- Beta/RC-Tags muessen ein `-suffix` enthalten.

## Local Checks

Vor einem Push bitte mindestens:

1. `dotnet build Catan.slnx`


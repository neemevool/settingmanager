\## Commands



\## Build and test: always via the wrapper

\- Build: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build`

\- Warnings (when you need them): `pwsh -NoProfile -File ./eng/verify.ps1 -Mode warnings \[-Target <project>]` (non-incremental, slower)

\- Tests: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "<filter>"`

\- Gate (must pass before finishing): `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate`

\- Never call `dotnet build` / `dotnet test` directly (a hook blocks it).



## sm module scripts

Any change under `VTMF.Settings.Core/Sql/settingsmanagement/` requires bumping `version` in `module.json` and updating `sha256` (the `RecordedHash_MatchesScripts` test prints the new value). Scripts must stay idempotent; no `BEGIN TRAN` in scripts.

\## Commands



\## Build and test: always via the wrapper

\- Build: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build`

\- Warnings (when you need them): `pwsh -NoProfile -File ./eng/verify.ps1 -Mode warnings \[-Target <project>]` (non-incremental, slower)

\- Tests: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "<filter>"`

\- Gate (must pass before finishing): `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate`

\- Never call `dotnet build` / `dotnet test` directly (a hook blocks it).



## sm module scripts

Any change under `VTMF.Settings.Core/Sql/settingsmanagement/` requires bumping `version` in `module.json` and updating `sha256` (both, on every script change; the failing `RecordedHash_MatchesScripts` test shows the new hash as the **Expected** value, split into two 32-char halves to defeat xunit truncation; concatenate them). Scripts must stay idempotent; no `BEGIN TRAN` in scripts.

When the skills under `plugins/vtmf-settings/` change, bump `version` in `plugins/vtmf-settings/.claude-plugin/plugin.json` (installed copies are pinned to it).

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`VTMF.SQLInstaller` is a .NET console app (single project, no test project) that runs inside a Linux Docker
container to install/upgrade a customer's MS SQL Server database: tables, stored procedures, app settings,
admin logins, and service accounts. It is **idempotent and version-aware** — it is re-run on every deploy and
only applies items whose version is newer than what is already installed. The container's **exit code is the
contract**: `0` = success, `1` = any failure (set in `InstallationWorker`).

The git repo root is the parent `sql-installer/` directory (contains `docker-compose.yaml`, `.github/`, `nupkg/`,
and sibling services `cdoc-encryptor/`, `key-receiver/`, `sql-nginx/`). This project lives in `VTMF.SQLInstaller/`.

## Build, run, package

```bash
dotnet build                       # WarningsAsErrors is ON (level 8); build fails on any warning except IL2104/IL3053
dotnet run                         # needs env vars set (see below) + a reachable SQL Server
dotnet pack -c Release -p:PackageVersion=1.0.12 -o ../nupkg

# Docker (multi-stage, AOT — needs clang/zlib in build stage). Final image bundles cdoc-encryptor.jar, config/, scripts/.
docker build --progress=plain --build-arg FINAL_BASE_IMAGE=eclipse-temurin:21-jre-jammy -t vtmf/sqlinstaller .

# Local dev stack (mssql + installer), from repo root:
docker compose --env-file VTMF.SQLInstaller/.env up -d
```

There is **no test suite**. Verify changes by building and running against the docker-compose SQL Server.

### Two build-time constraints that will bite you
1. **Native AOT** (`PublishAot=true`, `linux-x64`, self-contained). No runtime reflection. All JSON (de)serialization
   goes through the source-generated `Services/SerializationContext.cs` — **any new type sent to/from the key-receiver
   must be added there with `[JsonSerializable]`** or it will fail at runtime. XML parsing uses `System.Xml.Linq` (fine).
2. **`TreatWarningsAsErrors=true`**. Keep the build clean; only the AOT-trim warnings `IL2104;IL3053` are exempted.

### In-progress .NET 10 upgrade
The csproj targets `net9.0` and the Dockerfile uses the `dotnet/sdk:9.0` images, but the local SDK is 10.0.x, some
package refs are already `10.0.0`, and `obj/` contains `net10.0` artifacts (last commit: "Commit pending changes before
.NET 10 upgrade"). Treat the target framework / package versions as in flux and confirm before assuming 9.0.

## Architecture (the big picture)

Generic Host with one `BackgroundService`. Flow: `Program.cs` → `InstallationWorker` → `InstallationRunnerFactory` →
`InstallationOrchestrator` → an `IInstaller` per item.

- **`Program.cs`** — builds `InstallConfig` from environment variables, parses/validates the connection string
  (overriding creds with `MSSQL_USER`/`MSSQL_PASSWORD` if both set), tests the *server* connection, then runs the host.
- **`Workers/InstallationWorker.cs`** — the only hosted service; calls the runner, sets `Environment.ExitCode`, stops the app.
- **Two-tier DI (important).** Root provider holds run-independent singletons (`InstallConfig`, `KeyReceiverService`,
  `DatabaseConnectionTester`, `InstallationRunnerFactory`). `InstallationRunnerFactory.RunInstallationAsync` builds a
  **fresh child `ServiceProvider` per run** that registers everything config-dependent (`DatabaseService`, all installers,
  `InstallationOrchestrator`, parsers, `InstallerFactory`). Register new per-run services in that child collection, not in `Program.cs`.
- **`Services/InstallationOrchestrator.cs`** — the heart. `RunAsync`: can-connect → ensure DB exists (create via
  `DatabaseCreator` if missing) → parse `install.xml` → `InitializeVersionCache` → iterate sets in order → for each item,
  `IInstallerFactory.GetInstaller(item.Type).InstallAsync(...)`. **Any item failure throws and aborts the entire run.**
- **Installer strategy pattern.** `Installers/InstallerFactory.cs` maps the item `type` string → an `IInstaller`:
  `database` → `DatabaseInstaller`, `file` → `FileInstaller`, `settings` → `SettingsInstaller`, `admin` → `AdminInstaller`,
  `serviceaccount` → `ServiceAccountInstaller`, `service` → `ServiceInstaller` (**stub: throws NotImplementedException**).
- **`Services/DatabaseService.cs`** — thin raw ADO.NET wrapper over `Microsoft.Data.SqlClient` (no ORM/Dapper despite what
  the README says). Connects to `master` for server checks, DB-existence checks, and DB/login creation. Honors dry-run.
- **`Services/KeyReceiverService.cs`** — posts install logs (fire-and-forget via `Task.Run`) and encrypted SQL secrets to an
  external mgmt API (`KEYRECEIVER_URL`, header `X-Apikey`). `LoggingKeyReceiverService` is a no-op alternative impl.

## How install items work

`config/install.xml` drives everything: `<install>` → ordered `<set name= optional=>` → ordered `<item name check isfolder type source>`
with optional `<spec name= value>` children (used by `serviceaccount` for role grants). Parsed by `Services/XmlInstallParser.cs`.

- **Optional sets** (`optional="true"`) run only if their name is in the `SETS` env var (comma-separated).
- **Idempotency / version check** is driven by the item `check` attribute (`Services/DatabaseService.CheckInstallAsync`):
  - `check="file"` → presence of a marker file in `{CONFIG_DIR}/installhistory/` (name = relative path with `/`→`_`).
  - `check="database"` → stored proc `dbo.SetupCheckInstall @itemname` returns the installed version; the item installs only
    if the script version is greater. On success, `dbo.SetupRegisterInstallation` records it.
- **Where the script version comes from (README is WRONG here).** `DatabaseInstaller.InstallScriptFileAsync` reads the version
  from the **version cache** (`InitializeVersionCache`), keyed by source path — loaded from `versions.csv` (`path,version`) in
  `SQLSCRIPTS_DIR` by default, or from TFS if `TFS_ACCESS_TOKEN` is set. The `-- Version: N` header (parsed by
  `SqlScriptParser.ExtractVersion`) is **not** used in the current install path. If a path isn't in the cache, version is `0`.
- **Folder items** (`isfolder="true"`) install every **`*.txt`** file in the folder in alphabetical order (note: `.txt`, not `.sql`).
- **GO batching.** `SqlScriptParser.SplitIntoBatches` splits a script on standalone `GO` lines; each batch executes separately.
- **Path resolution** (`Installers/FileUtils.cs`): TFS-style `$/...` source → strip `$/`, then strip `SQLSCRIPTS_PREFIX`
  (default `Sawmill/database5`), then combine with `SQLSCRIPTS_DIR` (default `/data`).
- **`admin` / `serviceaccount`** shell out to `/scripts/encryptpassword.sh` (wraps `cdoc-encryptor.jar`, hence the Java base
  image) to encrypt generated passwords against the user's Estonian ID-code certificate, then send the encrypted secret to the
  key-receiver. They create SQL logins/users directly via `master`/`msdb`/`tempdb`.

## Configuration (environment variables — `Program.cs` is the source of truth; README is partially stale)

| Variable | Purpose | Default |
|---|---|---|
| `MSSQL_CONNECTION_STRING` | **Required.** Target server/db connection string | — |
| `MSSQL_USER` / `MSSQL_PASSWORD` | Override connstring creds (both must be set) | `sa` / — |
| `SQLSCRIPTS_DIR` | SQL scripts + `versions.csv` location | `/data` |
| `CONFIG_DIR` | Holds `install.xml` and `installhistory/` markers | `/config` |
| `INSTALL_XML_PATH` | Path to install.xml | `{CONFIG_DIR}/install.xml` |
| `SQLSCRIPTS_PREFIX` | Path prefix stripped from `$/...` sources | `Sawmill/database5` |
| `INSTALL_MODE` | Anything other than `install` ⇒ **dry-run** (no DB writes) | `install` |
| `SETS` | Comma-separated names of optional sets to enable | — |
| `CLIENTNAME`, `ADMINS` | Client id; admins as `idcode-username,idcode-username` | `notconfigured`, `` |
| `KEYRECEIVER_URL`, `KEYRECEIVER_TOKEN` | Mgmt API for logs/secrets | `https://mgmt.vtmf.ee/keyreceiver` |
| `TFS_ACCESS_TOKEN` | Enables loading the version cache from TFS instead of CSV | — |

Note: `docker-compose.yaml` also passes `GITHUB_SQLSCRIPTS_*` — those are consumed by `scripts/downloadsqlscripts.sh`
(clones the SQL scripts repo into `SQLSCRIPTS_DIR` at container start), not by the .NET app.

## Stored procedures the installer depends on

The target database must provide these (created by the bootstrap `init` scripts): `dbo.SetupCheckInstall @itemname`,
`dbo.SetupRegisterInstallation @itemname,@itemtype,@version,@source,@target,@setname`, and `dbo.SaveSetting @settingnode,@workercode`
(used by `SettingsInstaller`).

## CodeGraph

`.codegraph/` exists in this project. Per the global workflow, do **not** call `codegraph_explore`/`codegraph_context` from the
main session — spawn an Explore agent for "how does X work" questions; use the lightweight `codegraph_search`/`callers`/`callees`/
`impact`/`node` tools directly only for targeted lookups before editing.

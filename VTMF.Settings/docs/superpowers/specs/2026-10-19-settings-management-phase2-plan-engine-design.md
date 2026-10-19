# Settings management — Phase 2: plan engine, sessions and the settings skill

**Date:** 2026-10-19 · **Status:** Approved
**Parent design:** `2026-10-16-settings-management-tools-design.md` (§4 row model, §5.2 plan engine, §6 Core, §7 MCP).
This document refines and partly overrides the parent for phase 2. Where they differ, this document wins.

## Intent

Consultants change customer settings **directly in production** (a test database costs the customer money, and
customers do not want to test). They work by trial and error: find settings, change some, look at the result,
change again. Today this happens through the UI with no preview, no atomic apply and no reliable undo.

Phase 2 makes every change through Claude Code safe and reversible, without the consultant having to know the
word "plan". The consultant asks to see settings, then asks to change them; Claude shows what will change, applies
after confirmation, and can undo any step or a whole working session.

Phase 2 also lays the ground for phase 2.1 (features): every change is recorded in a named **session**, and the
net change of a session is the raw material for packaging a reusable feature.

## Who

- **Consultants:** apply settings for customers. Know roughly how features work, but often forget a value or
  don't know how to choose between options. Do several trial-and-error rounds. Do not know about plans and will not
  ask for one.
- **The developer (user):** writes the SPA and backend code that reads the settings; knows what each setting means.

## Goals

1. `sm` plan engine in every client DB: create plan from a desired state, apply atomically, detect stale plans,
   roll back a plan, roll back a session, migrate legacy env rows (parent §5.2).
2. `sm.session`: named working sessions; every plan belongs to one; the net change of a session can be read.
3. `CreateTable` rollback = back up the table in the existing UI backup format, then drop it.
4. Core: `SmClient` wrappers for the new procedures; `SmOrchestrator.CompareClients` and `ClonePlan`.
5. MCP tools for sessions, plans, apply, rollback, clone and compare.
6. A new skill **`manage-settings`** in the `vtmf-settings` plugin that turns "show me… / change…" conversations
   into session + plan + preview + apply + undo, without the user asking for a plan.
7. Root `README.md`: how to update the plugin, skills and MCP image; how to change settings with Claude (sample
   conversation, sample prompts).

## Non-goals

- Feature model, setting knowledge base, create/apply/document-feature skills → phase 2.1 (separate design).
- UI changes. The Blazor UI keeps writing directly; its changes are not plans (stale detection protects plans
  from them).
- Test-then-promote workflow. (Clone between clients exists, but is not a staging pipeline.)
- Approval workflow, authentication.
- Global `settings` table, worker-layer rows (parent §4.7 `WORKER_LAYER`).
- Replacing `settingssession` / `settingslog`; they stay for the UI.

## Anti-goals (failure even if it "works")

- A consultant has to understand plans, checksums or layers to change a setting.
- Anything is written to a settings table without the user having seen the change list in the conversation.
- An undo silently overwrites someone else's later change, or reverts only part of a change.
- A rolled-back `CreateTable` loses data (no backup).
- The skill falls back to direct SQL when a tool fails.

## Decisions made by the user

| # | Decision |
|---|---|
| U1 | Rollback of a `CreateTable` item drops the table after creating a backup, reusing the Settingmanager backup format |
| U2 | Phase 2 = plan engine + settings skill + README; features move to phase 2.1 on the new `sm.feature` model (no extension of `settingsfeature*`) |
| U3 | Consultants work directly on production; no test-client step |
| U4 | Sessions capture the net change; trial-and-error reverts drop out |
| U5 | Before the first change Claude asks for a session name in plain words; all plans in the conversation are tagged with it |
| U6 | Preview as a short table (insert/update/delete, old → new, warnings); apply only after confirmation; reload SPA reminder |
| U7 | "Undo that" = roll back last plan; "undo everything" = roll back the session |
| U9 | Undo is all-or-nothing. If any row touched by the undo has changed since, the rollback plan is `Stale` and nothing is applied — partial reverts could combine with the later change in unforeseen ways. Claude then shows the conflicting rows (current vs. expected) and builds a normal merge plan with the consultant |
| U10 | Two `Setting` rows with the same (table, workercode, NAME) are an invalid state. `SettingGroup` rows may share a NAME (e.g. `Detail`); their VALUE distinguishes them (parent §4.4) |
| U8 | At the end of a session with changes, Claude offers to package it as a feature (the packaging itself is phase 2.1). *Builder's call:* in phase 2 nothing is stored for this; the skill mentions the session name/id in its reply, and phase 2.1 finds sessions via `list_sessions` |

## Design

### 1. Module scripts (client DB)

New scripts in `VTMF.Settings.Core/Sql/settingsmanagement/`, following the existing `###-type-name.txt`
convention. `module.json` version 4 → 5, `sha256` updated. Tables are created with `IF OBJECT_ID(...) IS NULL`;
procedures with `CREATE OR ALTER`. SQL Server 2019 floor (compat 150): `OPENJSON`/`JSON_VALUE` allowed, no `json`
type.

The "no `BEGIN TRAN` in scripts" rule (the installer wraps all scripts in one transaction) applies to top-level
script statements. Procedure **bodies** (`sm.ApplyPlan`) do use transactions; the CLAUDE.md wording is clarified
accordingly.

### 2. Tables

`sm.session`

| Column | Notes |
|---|---|
| `id` int identity PK | |
| `name` nvarchar(200) | plain-words name given by the user |
| `process` nvarchar(100) | main process; plans may still touch others (informational) |
| `createdby` nvarchar(128) | `ORIGINAL_LOGIN()` |
| `created`, `lastactivity` datetime2(0) | UTC |
| `status` | `Open` \| `Closed` |

`sm.plan` — as parent §5.2, plus:

- `sessionid` int NULL FK → `sm.session` (NULL only for plans created outside a session, e.g. by tests).
- `rollbackof` int NULL — plan id this plan reverses (`source = rollback:{planid}`) or NULL.
- `source` also allows `rollback-session:{sessionid}`.
- `createdby` / `appliedby` use `ORIGINAL_LOGIN()` (consistent with `settingslog`).

`sm.planitem` — as parent §5.2. `action` values: `CreateTable`, `Insert`, `Update`, `Delete`, `BackupTable`,
`DropTable`. Add `backuptable` nvarchar(128) NULL (name produced by `BackupTable` at apply time).

`sm.planwarning` — as parent.

### 3. Procedures

All procedures validate names with `sm.IsSafeName`, use `THROW` with stable numbers in 51000–51999, and return
result sets that `SmClient` maps to DTOs.

**Sessions**

- `sm.StartSession @name, @process` → new `Open` session id. No reuse by name.
- `sm.GetSessions @process = NULL, @top = 20` → newest first, with plan counts and applied counts.
- `sm.GetSessionChanges @sessionid` → **net change**: for each row identity (§4.4 of parent, per physical table and
  workercode) touched by an **applied** plan in the session (rollback plans included), the before-image of the
  first applied item and the after-image of the last applied item. Rows where first-before equals last-after are
  omitted. Tables created in the session and still existing are reported as `CreateTable`.
- `sm.CloseSession @sessionid` → `Closed`. Closed sessions can still be read and rolled back, but `CreatePlan` into
  them is rejected.

**Plans** (parent §5.2, with these refinements)

- `sm.CreatePlan @desired, @description, @source, @options, @sessionid` → as parent. Updates
  `sm.session.lastactivity`.
- `sm.ApplyPlan @planid, @checksum` → as parent: single `SERIALIZABLE` transaction, checksum + status check, every
  before-image verified (else whole plan `Stale`, nothing changed), items applied in `seq` order, `Applied`; any
  error → rollback, `Failed` with message. Additional per-action preconditions:
  - `CreateTable`: table does not exist.
  - `BackupTable`: table exists.
  - `DropTable`: table exists and is empty at that point in the sequence.
- `sm.CreateRollbackPlan @planid` → inverse plan from before-images (`rollbackof = @planid`, same session). Only for
  `Applied` plans. The original is marked `RolledBack` when the rollback plan is applied. Inverse of `CreateTable`
  is the sequence **`BackupTable` → `Delete` of every row the plan inserted into it → `DropTable`**. If other rows
  exist in the table at apply time, `DropTable` fails its precondition and the plan is `Stale`.
- `sm.CreateSessionRollbackPlan @sessionid` → one plan restoring the session's net change (inverse of
  `GetSessionChanges`): before-images = the net after-images, after-images = the net before-images. Applied plans in
  the session that it covers are marked `RolledBack` when it is applied. *(Builder's call recorded here: one plan,
  not a chain of per-plan rollbacks, so the user sees one preview.)*
- **Inverses are total, so a rollback can itself be rolled back ("redo").** Inverse of `DropTable` = `CreateTable`;
  inverse of `Delete` = `Insert` with the before-image; inverse of `BackupTable` = no item (the backup table stays).
  Rolling back a `CreateTable` rollback therefore recreates the table and its rows from the plan's own
  before-images; the `bck_*` table is an extra safety copy, not the restore path. *(Builder's call.)*
- `sm.CreateLegacyEnvMigrationPlan @process` → as parent. If the overlay already holds the same row with a
  different value, no items are generated for that row; warning `LEGACY_CONFLICT` lists it and the legacy row stays.
  *(Builder's call: never silently change today's effective value.)*
- `sm.GetPlans @process, @sessionid, @top`, `sm.GetPlan @planid` → as parent; `GetPlan` returns plan, items,
  warnings.

**Duplicate guardrail (U10).** New guardrail `DUPLICATE_ROW` (error): `CreatePlan` (including rollback, clone and
migration plans) refuses when a row it would touch has more than one physical match under its identity — two
`Setting` rows with the same (table, workercode, NAME), or two `SettingGroup` rows with the same identity per parent
§4.4. The message names the table and rows and tells the user the data must be cleaned up first. Duplicates in
rows the plan does not touch are ignored. *(Builder's call: same rule for duplicate `SettingGroup` identities.)*
`ApplyPlan` re-checks this inside its transaction (a duplicate inserted after preview → `Stale`).

**Backup format (U1).** `BackupTable` creates `bck_{table}_{yyyyMMdd_HHmmss}` with `SELECT * INTO` and adds the
marker row `NAME='Backup.Description'`, `VALUE='Rollback of plan {id}: {description}'`,
`settingtype='BackupDescription'`, `workercode='SYSTEM'`, `ordernr=100000` — the same format as
`SettingsService.BackupSettings`, so the UI backup page lists it. (The UI has no working restore today:
`RestoreBackup` is not wired to a page and `GetBackupInfo` orders by a non-existent column. Fixing that is out of
scope; the restore path for engine changes is the rollback-of-rollback described below.) Seconds are added to the timestamp
(the UI format uses minutes) to avoid collisions; the UI's `LIKE 'bck_{process}_%'` listing still matches.

**Create table template.** `CreateTable` uses the same columns as `SettingsService.EnsureSettingTable`: `NAME`
nvarchar(200) NOT NULL, `VALUE` nvarchar(400) NOT NULL, `settingtype` nvarchar(20) NOT NULL, `LISTVALUE`
nvarchar(1600) NULL, `workercode` nvarchar(100) NOT NULL, `ordernr` int NOT NULL.

### 4. Core

- `SmClient`: one async method per new procedure; DTOs `SmSession`, `SmSessionChange`, `SmPlan`, `SmPlanItem`,
  `SmPlanWarning`, `SmDesiredState`, `SmPlanOptions`. Desired state serialized with `System.Text.Json`.
- `SmOrchestrator`:
  - `CompareClients(source, target, process, scope)` → reads both via `sm.GetSettings`, returns the diff without
    storing anything.
  - `ClonePlan(source, target, process, scope, sessionid, options)` → replace-mode desired state from source,
    `CreatePlan` on target with `source = clone:{source}`.
  - *Builder's call — default scope* (when `scope` is omitted, for both compare and clone): every frontend view
    table of the process that exists on the source, `base` layer plus every env/lang overlay present on the source,
    and `ServerProcessSettings` (`base`). `ServerSetting` rows are skipped as in parent §4.7. Overlays and tables
    missing on the target follow the normal guardrails: `TABLE_MISSING` unless `options.createTable=true`, which
    `clone_plan` passes through. Target tables that don't exist on the source are outside the scope and untouched.
    The skill always runs `compare_clients` and shows the differences before `clone_plan`.
- Both clients must have the module installed at the tool's version (existing module gate).

### 5. MCP tools

All take `client`; all return the existing `{ ok, data, error }` envelope; all go through the module gate.

| Tool | Writes settings? | Notes |
|---|---|---|
| `start_session(client, name, process)` | no | |
| `list_sessions(client, process?)` | no | |
| `session_changes(client, sessionId)` | no | net change |
| `close_session(client, sessionId)` | no | |
| `create_plan(client, sessionId, desired, description, options?)` | no | stores plan, returns items + warnings + checksum |
| `get_plan(client, planId)`, `list_plans(client, process?, sessionId?)` | no | |
| `apply_plan(client, planId, checksum)` | **yes** | the only settings-mutating tool; `Destructive` annotation |
| `rollback_plan(client, planId)` | no | creates the inverse plan; apply via `apply_plan` |
| `rollback_session(client, sessionId)` | no | creates the session rollback plan |
| `compare_clients(source, target, process, scope?)` | no | |
| `clone_plan(source, target, process, sessionId, scope?, options?)` | no | plan stored on `target` |
| `legacy_env_migration_plan(client, process, sessionId)` | no | |

Tool descriptions state: show the plan items to the user before `apply_plan`; never apply without the user's
confirmation in the conversation; reload the SPA after apply; `ServerSetting` values are never available.

Plan results returned to the model contain a compact item list (`action`, table, layer, name, old → new value,
ordernr) suitable for rendering as a table; physical details (workercode) are included but secondary.

### 6. Plugin skill `manage-settings`

New `plugins/vtmf-settings/skills/manage-settings/SKILL.md`; plugin version 1.0.1 → 1.1.0. `sm-module` stays as is
and is referenced from `manage-settings`.

**Trigger (description):** use when the user wants to find, inspect, compare, copy or change VTMF settings of a
client or process, or undo a settings change — even if they don't mention plans.

**Behaviour the skill teaches:**

1. **Orient.** Resolve client (`list_clients`), run `sm-module`, resolve process (`list_processes`) and tables
   (`list_tables`). Ask instead of guessing when client or process is ambiguous.
2. **Read and explain.** `get_settings` / `get_effective_settings` / `settings_usage`. Explain in customer terms
   what the settings do, as far as known; say so when the meaning is unknown rather than inventing it.
3. **Start a session before the first change.** Ask "What are you setting up?" and call `start_session` with the
   answer. Reuse the session for all further changes in the conversation; if the user switches to a clearly
   different task, ask whether to start a new one.
4. **Translate the request into a desired state.** Choose `merge` for "change/add/remove these", `replace` only
   when the user wants a table/layer to become exactly a given content. Pick the layer: `base` unless the user
   talks about one environment (`env:{E}`) or language (`lang:{L}`); never worker codes. Use `after` anchors for new
   rows next to related ones. Ask before using `createTable` or `targetAlias`.
5. **Preview.** Call `create_plan` and show the items as a short table: action, table, name, old → new, plus
   warnings in plain words. "No changes" → say so. Errors → explain the guardrail and suggest the fix (e.g. env
   layer instead of worker row).
6. **Apply only after confirmation** in the conversation ("apply", "yes", …). Then `apply_plan` with the returned
   checksum. Report the result and remind: reload the SPA to see the change.
7. **Stale.** Explain that the settings changed since the preview (someone else, or the UI), re-read, rebuild,
   re-preview.
8. **Undo.** "Undo that" → `rollback_plan` on the last applied plan, preview, confirm, apply. "Undo everything" →
   `rollback_session`, preview, confirm, apply. Mention when a rollback drops a table (backup name is reported).
   If the undo is `Stale` (U9): never undo partially. Show which rows changed since (current value vs. the value
   the undo expected), explain that a partial undo could clash with the later change, and work out with the user
   what the rows should be now; then build a normal `merge` plan and preview it.
   `DUPLICATE_ROW`: explain that the table holds an invalid duplicate that must be cleaned up first; do not work
   around it.
9. **Copy from another client.** `compare_clients` to show differences; `clone_plan` to copy; same preview/confirm.
10. **End of session.** When the user is done and the session has changes, show `session_changes` as a summary,
    `close_session`, and ask whether these changes form something reusable (a feature). In phase 2 just note the
    answer and session id in the reply (packaging comes in phase 2.1).
11. **Never** use direct SQL, show `ServerSetting` values, or apply without confirmation.
12. Reply in the user's language (consultants may write Estonian); setting names and values stay verbatim.

The skill includes a short worked example (find barcode settings → change `Function.Barcode.Notfound.Action` →
preview → apply → undo).

### 7. README.md (root)

- **Updating** (extend §4): what to update when — MCP image tag in `compose.yaml` (tools and `sm` module) and the
  plugin (`/plugin marketplace update vtmf`, `/reload-plugins`, skills). Both should be updated together; the
  `sm-module` skill handles `module_outdated` / `module_newer_than_tool`. How to check the installed plugin version.
- **Tools list** updated with the phase 2 tools.
- **New section "Changing settings with Claude"**: you don't ask for a plan — you ask to see and change settings.
  Short sample conversation (find → name the session → preview table → "yes" → applied → reload SPA → "undo that").
  What the preview means, what "stale" means, how undo works, where the backup is when a table is dropped,
  that every change is recorded with your SQL login.
- **Sample prompts** table extended: change a value, add a column row to a view, copy a process from client A to B,
  compare two clients, undo the last change, undo today's session, show what I changed in this session.

### 8. Phase 2.1 hand-off

`sm.session` + `GetSessionChanges` is the input for feature packaging in phase 2.1. Phase 2.1 (separate design)
covers: `sm.feature` model in the metabase with parameters and descriptions, setting knowledge base (meaning of
each setting and value in customer terms, decision guides per group), skills `create-feature`, `apply-feature`,
`document-settings`, and conversion of the existing column features.

### 9. Tests

Integration tests (xUnit + Testcontainers, SQL Server 2019) extending the phase 1 fixture:

- Diff semantics: merge/replace, identity rules, ordering and renumbering, every guardrail (parent §10).
- Apply: success, checksum mismatch, wrong status, stale before-image, failure mid-plan rolls back everything.
- Rollback after a later change to a touched row → `Stale`, nothing applied (U9). `DUPLICATE_ROW` for duplicate
  `Setting` rows; `SettingGroup` rows sharing a NAME with different VALUEs are handled normally (U10).
- Rollback of a rollback restores a dropped table with its rows.
- `CreateTable` → rollback: backup table exists in UI format with marker row, table dropped; extra row → `Stale`.
- Sessions: net change drops reverted values, keeps final values, handles multiple plans per row; session rollback
  restores the start state; closed session rejects `CreatePlan`.
- Clone and compare across two databases in the same container.
- MCP tool-level tests for every new tool, including the envelope and module gate.
- Skill: no automated test; manual checklist in the plan (sample conversations from README produce the expected
  tool sequence).

## Left to the builder

- Exact error numbers, result-set shapes and DTO property names.
- Canonical JSON for the checksum (must be deterministic and documented in the script).
- Whether net-change computation lives in SQL or in `SmClient` (SQL preferred so `CreateSessionRollbackPlan` can
  reuse it).
- Wording of tool descriptions and the skill, within §5–§6.

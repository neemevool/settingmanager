# Settings management — Phase 3: features

**Date:** 2026-10-20 · **Status:** Approved
**Parent design:** `2026-10-16-settings-management-tools-design.md` (§4 row model, §8 features).
**Builds on:** `2026-10-19-settings-management-phase2-plan-engine-design.md` (sessions, plans, `manage-settings`).
This document replaces parent §8 for phase 3. Where they differ, this document wins.

## Intent

A **feature** is a named, reusable piece of behaviour that is switched on by settings: "Allows adding a barcode to
an article on the product card (sparelocateitems)", "Adds a column to the document list". The code behind it
already exists (a feature is always implemented in code first); the feature records which settings make it work,
which choices the consultant has, and what must be done by hand afterwards.

Today features are built by hand: the developer starts a `settingssession`, changes settings, reads `settingslog`
and picks the rows that belong together. Names are poor ("NeemeFeature", "DocumentTableRow") and descriptions talk
about settings instead of what the user gains, so consultants can't find or trust them. Consultants then apply them
by remembering "it works on customer Y" and copying.

Phase 3 makes features first-class: stored once in the shared metabase so every customer can use them, created
with a skill that insists on a clear user-facing description, and applied with a skill that interviews the
consultant in their own terms and goes through the phase 2 plan engine (preview, confirm, apply, undo).

## Who

- **Feature author** — the developer, or a consultant who knows the code behaviour. Describes, collects the rows,
  marks the choices, writes the manual steps.
- **Consultant** — applies features for customers, directly in production (phase 2 U3). Thinks in terms of the
  customer's request ("could the article get a barcode if it has none?") or of another customer ("like on customer
  Y in sparelocateitems"), never in terms of how a feature is stored.

## Goals

1. Feature tables and procedures in schema `sm` of the **metabase** (`DefaultMetaBase`), as a separately versioned
   and installed SQL module.
2. Feature model: description, name, status, rows with placeholders, typed parameters, post-apply steps.
3. Core: feature store client, feature expansion into a phase 2 desired state, backend-usage check.
4. MCP tools to find, read, save, finish and apply features, and to list features applied on a client.
5. Plugin skill **`create-feature`**: describe → name → collect rows → parameters → post-apply steps → finish.
   Startable on its own or from a finished `manage-settings` session.
6. Plugin skill **`apply-feature`**: interview → find → ask parameters → preview → apply → post-apply steps.
7. `manage-settings` end-of-session step hands over to `create-feature`. README updated.
8. Parent design updated: §8 points here, phase 4 gains the migration of the old feature tables, phasing table
   updated.

## Non-goals

- **Feature sets** (several features applied together, replacing `settingsfeaturegroup`) → a separate later stage.
- **Setting knowledge base / wiki** (meaning of every setting) → future, separate task.
- **Old feature tables** (`settingsfeature*`, `settingsfeaturegroup`): untouched in phase 3; the Settingmanager UI
  keeps using them. Their migration and the new feature pages are phase 4.
- UI changes of any kind.
- Versioning of features.
- Formal plan items for non-settings tables (`dcquerysettings` etc.).
- Authentication / approval workflow.

## Anti-goals (failure even if it "works")

- A consultant has to know a feature's stored name, tables or placeholders to find and apply it.
- A draft (unfinished) feature is offered to a consultant or applied.
- Anything is written to a settings table without a preview the user confirmed (phase 2 rule holds).
- Claude executes post-apply SQL itself. It prepares SQL; the consultant runs it.
- The model grows conditions, branches or per-option row groups. **Design principle (user):** don't formalize the
  complexity into code. Anything beyond placeholders goes into post-apply guidance or a separate complementary
  feature described in free form.
- A feature's description says what settings it changes instead of what it enables for the user.

## Decisions made by the user

| # | Decision |
|---|---|
| F1 | Features live in the metabase (`DefaultMetaBase`), schema `sm`, so every customer can use them |
| F2 | Creating features is a separate skill (`create-feature`); it can start from a session or on its own |
| F3 | Old `settingsfeature*` tables stay for the UI; their migration moves to phase 4 |
| F4 | Creation starts with the description (what the feature enables for the user), then the name; the skill validates the description and helps with the name |
| F5 | A feature is stored in the metabase from the first step as a **Draft** and updated step by step (not only in the chat); drafts can be listed; "show the current state" works anytime |
| F6 | Rows can **set** (add or change) or **remove** settings in any managed table (`ServerProcessSettings`, process settings tables, layout tables, overlays) |
| F7 | Non-settings work (e.g. `dcquerysettings` routes for Directo hooks, scanner profile setup) is **post-apply guidance**: free text, optionally with SQL. Claude may generate/adapt the SQL; the consultant applies it (Settingmanager module install or own SQL tool). No formal plan items for such tables |
| F8 | Only **Finished** features are usable |
| F9 | Consultants ask either in the customer's words or by reference ("like on customer Y"); `apply-feature` interviews them |
| F10 | A feature is not tied to a process. Rows store the table kind (`DocumentLayout`, `ProcessSettings`, `ServerProcessSettings`, …); the process is chosen at apply time. Frontend rows work on any process. Only `ServerProcessSettings` rows need code support in the target process |
| F11 | No versioning: features are fixed in place |
| F12 | `{{placeholder}}` with arbitrary names in setting names and values; one answer fills every occurrence (e.g. `Column` group row = `{{name}}`, then `Column.{{name}}.Caption`, …). A chosen value can drive dependent setting names (`Function.Barcode.Notfound.Action = {{notfoundAction}}`, `Function.{{notfoundAction}}.Notfound.ItemType = Option`, `Function.Barcode.Notfound.{{notfoundAction}}.Procedure = …`) |
| F13 | Parameters are typed like today's feature management: Text, Number, Boolean (`True`/`False` as text), List (values with descriptions) |
| F14 | Feature sets are a separate stage |
| F15 | Setting knowledge base is out of scope |
| F16 | No trial application of drafts. The author tests the behaviour by changing settings through `manage-settings` (usually the session the feature is built from); only Finished features can ever be planned |

## Design

### 1. Metabase module

New script set `VTMF.Settings.Core/Sql/settingsmetabase/` with its own `module.json` (`version`, `sha256`, same hash
guard and `RecordedHash_MatchesScripts`-style test as the client module), same `###-type-name.txt` convention,
idempotent, SQL Server 2019 floor, no top-level `BEGIN TRAN`. Installed by the existing `SmInstaller` mechanism
(transaction + app lock) into `DefaultMetaBase`.

**Coexistence:** the metabase module and the client module must be installable into the same database (tests use
one container; a small installation may share a DB). They share schema `sm` but must not share object names; the
metabase module records its version separately from `sm.moduleinfo` (e.g. its own `sm.featuremoduleinfo`, or a
module-name key — builder's call).

Connection: `SqlConnectionProvider.GetDefaultMetaBaseConnection()`. Per-client `MetaBaseName` is ignored for
features (*my call*: one shared feature store). Missing `DefaultMetaBase` configuration → error
`metabase_not_configured`.

Module gate: every feature tool checks the metabase module state like the client gate (`not_installed`,
`outdated`, `newer_than_tool`, `up_to_date`), with error codes `feature_module_not_installed`,
`feature_module_outdated`, `feature_module_newer_than_tool`.

### 2. Feature model (tables)

`sm.feature`

| Column | Notes |
|---|---|
| `id` int identity PK | stable reference |
| `name` nvarchar(100) | unique (case-insensitive); may change while editing |
| `description` nvarchar(max) | user-facing: what the feature enables |
| `status` | `Draft` \| `Finished` |
| `builtonprocess` nvarchar(100) NULL | process it was built/tested on — informational only |
| `createdby`, `created`, `modifiedby`, `modified` | `ORIGINAL_LOGIN()`, UTC |
| `revision` rowversion | optimistic concurrency for saves |

`sm.featurerow`

| Column | Notes |
|---|---|
| `featureid`, `seq` | `seq` = order of application |
| `tablekind` nvarchar(100) | view suffix (`DocumentItemsListLayout`, `ProcessSettings`, …) or `ServerProcessSettings`; no placeholders |
| `layer` nvarchar(100) | `base` \| `env:{E}` \| `lang:{L}`; placeholders allowed (`env:{{env}}`) |
| `name`, `value` | placeholders allowed; `value` NULL for remove rows |
| `settingtype` | `Setting` \| `SettingGroup` (`ServerSetting` refused) |
| `listvalue` | nullable; placeholders allowed |
| `op` | `set` \| `remove` |
| `after` | optional anchor (`NAME` or `NAME=VALUE`); placeholders allowed |
| `note` | optional free text for the author/consultant |

Feature rows have no `ordernr`; position follows the phase 2 merge rules (`after` anchor, else longest common
prefix, else table end). *(My call: ordernr is customer-specific; anchors carry over between processes.)*

`sm.featureparam`

| Column | Notes |
|---|---|
| `featureid`, `name` | `name` = placeholder word, matched case-insensitively |
| `description` | the question asked to the consultant, in plain words |
| `type` | `Text` \| `Number` \| `Boolean` \| `List` |
| `defaultvalue` | optional |
| `seq` | question order |

`sm.featureparamvalue` (for `List`): `featureid`, `paramname`, `value`, `description`, `seq`.

`sm.featurestep` (post-apply steps): `featureid`, `seq`, `text` (guidance, required), `sql` (nvarchar(max) NULL).
Placeholders allowed in both.

**Placeholders.** Syntax `{{word}}`, word = `[A-Za-z][A-Za-z0-9_]*`, case-insensitive. Built-ins, always available
and never asked: `{{processname}}` (target process) and `{{clientname}}` (target client) — same names as today's
`SettingFeatureValue.Configure`. Every other placeholder must have a `featureparam` row.

**Validation.**
- Save (any status): structure valid, `tablekind`/`layer` syntax, `op`/`settingtype` values, param types, List
  params have ≥1 value, no `ServerSetting` rows, name unique.
- Finish (and every save of a Finished feature): name and description non-empty; at least one row or step; every
  non-built-in placeholder in rows/steps has a described param; every param has a description; List param values
  have descriptions; defaults valid for their type. Params not used by any row/step → warning (not refused).
- A save that would make a Finished feature invalid is refused; to make bigger changes the author reopens it to
  Draft (*my call*).

### 3. Metabase procedures

- `sm.GetFeatureModuleInfo`.
- `sm.FindFeatures @text = NULL, @status = 'Finished'` → compact summaries (id, name, description, status,
  builtonprocess, table kinds used, param count, modified). `@text` matches name, description, `builtonprocess`
  and row names (`LIKE`). Default returns only Finished. No process filter: features are not tied to a process
  (F10), and the feature count is small enough for the skill to match descriptions itself (*my call*).
- `sm.GetFeature @id | @name` → feature, rows, params, param values, steps, revision.
- `sm.SaveFeature @json, @revision = NULL` → insert (no id) or full replace of an existing feature's content
  (whole-document save, simpler than per-row edits). Revision mismatch → error `feature_changed` (someone else
  saved in between; re-read and re-apply the edit). Returns id, new revision, validation warnings.
- `sm.SetFeatureStatus @id, @status, @revision` → `Finished` (runs finish validation) or back to `Draft`.
- `sm.DeleteFeature @id, @revision` → **Drafts only**; finished features must be reopened first (*my call*).

Errors via `THROW` in a range distinct from the client module (*builder's call*, e.g. 52000–52999).

### 4. Client module additions

`module.json` version bump (6), `sha256` updated.

- `sm.CreatePlan` accepts `source = feature:{id}:{name}`.
- `sm.GetAppliedFeatures @process = NULL` → per feature id and process: last applied date, by whom, plan id, from
  `sm.plan` rows with `status = 'Applied'` and `source LIKE 'feature:%'` (rolled-back plans excluded).

### 5. Core

- `SmFeatureClient` (metabase): one async method per metabase procedure; DTOs `SmFeature`, `SmFeatureRow`,
  `SmFeatureParam`, `SmFeatureParamValue`, `SmFeatureStep`, `SmFeatureSummary`; metabase module gate.
- `SmOrchestrator.FeaturePlan(client, featureId, process, sessionId, params, options)`:
  1. Read the feature; refuse unless `Finished`.
  2. Resolve parameters: given values, else defaults. A param without a default must be answered (*my call*: no
     separate `required` flag); missing → error `feature_param_missing` listing them. An empty Text answer is
     valid;
     validate types (Number parses; Boolean accepts true/false case-insensitively and writes `True`/`False`; List
     value must be one of the listed values) → `feature_param_invalid`.
  3. Substitute placeholders in rows; build a **merge** desired state for `process` (table kinds → tables; remove
     rows → `op: "remove"`).
  4. `sm.CreatePlan` on the client with `source = feature:{id}:{name}`, the session, and options
     (`createTable`, `targetAlias` passed through; the skill asks before using them).
  5. **Backend usage check (F10):** for each `ServerProcessSettings` set-row, run the existing settings-usage scan
     (`sm.GetSettingsUsage` + C# scanner) for the target process; a setting no procedure reads → warning
     `SETTING_NOT_READ` ("the code of {process} may not support this"). If the scan reports `unparsed` calls, the
     warning says the result is uncertain. Returned with the plan result (*my call*: not stored in
     `sm.planwarning`).
  6. Return the plan result plus the post-apply steps with placeholders substituted.
- Apply stays `apply_plan` (phase 2) — the only mutating tool for settings.

Re-applying a feature after it was fixed is a normal merge: only differences appear in the plan. Rows that were
removed from the feature are not removed from customers unless the feature has an explicit remove row.

### 6. MCP tools

All return the `{ ok, data, error }` envelope. Feature-store tools go through the metabase gate; tools that touch
a client also through the client gate.

| Tool | Writes | Notes |
|---|---|---|
| `feature_module_status()` / `install_feature_module()` | metabase DDL | like `module_status` / `install_module`; install is `Destructive` |
| `find_features(text?, status?)` | no | default Finished only; `status: "Draft"` lists drafts |
| `get_feature(id \| name)` | no | full feature incl. revision |
| `save_feature(feature, revision?)` | metabase | create or full replace; returns id, revision, warnings |
| `set_feature_status(id, status, revision)` | metabase | finish / reopen |
| `delete_feature(id, revision)` | metabase | drafts only |
| `feature_plan(client, featureId, process, sessionId, params, options?)` | no | plan + `SETTING_NOT_READ` warnings + resolved post-apply steps |
| `list_applied_features(client, process?)` | no | joins `GetAppliedFeatures` with feature names/descriptions |

Tool descriptions state: only Finished features can be planned; show the plan before `apply_plan`; post-apply SQL
is shown to the user to run, never executed by the assistant; descriptions must say what the feature enables for
the user.

### 7. Skill `create-feature`

New `plugins/vtmf-settings/skills/create-feature/SKILL.md`.

**Trigger:** the user wants to define, record, package, edit, finish or list (unfinished) features, or says that
what they just changed should be reusable.

**Behaviour:**

1. **Start.** Either from scratch, or from a session (`session_changes` on the client the session ran on), or from
   an existing client's process ("take the barcode settings from customer Y": `get_settings`, user picks rows), or
   to continue a draft (`find_features(status: "Draft")`).
2. **Describe first.** Ask what the feature lets the end user do. Check the description: user-facing, says what
   becomes possible and where (process / screen), short, no setting names as the main content. Offer a rewrite
   when it fails. Bad: "feature adds settings Action.Barcode to sparelocateitems". Good: "Allows adding a barcode to
   an article on the product card (sparelocateitems)".
3. **Name.** Propose 2–3 names that say what the feature does; avoid person names and storage terms. The user
   picks or edits. `save_feature` immediately as **Draft** (with `builtonprocess` when known).
4. **Rows, iteratively.** The user names settings to add, change or remove per table; from a session, the net
   changes are proposed as rows (after-image → set, deleted row → remove, `CreateTable` → note that the table is
   created at apply time with the user's consent) and the user trims them. Translate physical tables to table
   kinds and layers. Save after every change. "Show the current state" → `get_feature`, rendered as a short table.
5. **Parameters.** Ask which values vary per customer; replace them with `{{word}}` across all rows that share
   them (F12); for each, a question, type, default, and for List the values with descriptions.
6. **Post-apply steps.** Ask about work outside settings tables (Directo hooks in `dcquerysettings`, scanner
   profiles, other records) and important guidance; help write idempotent SQL with `IF NOT EXISTS` guards
   (pattern of the existing `065-script-PostSetBarcodeQuery.txt`).
7. **Finish.** `set_feature_status(Finished)`; explain any validation refusal in plain words and fix it.
8. **Complexity rule.** If something needs conditions or branching, don't model it: write guidance, or suggest a
   separate complementary feature.
9. Reply in the user's language; names/values verbatim. Never edit settings tables from this skill. Drafts are
   never applied (F16): the author tests the behaviour by changing settings with `manage-settings`, then records
   the result here (typically from that session).

### 8. Skill `apply-feature`

New `plugins/vtmf-settings/skills/apply-feature/SKILL.md`.

**Trigger:** the user wants some behaviour on a customer process ("could it be possible…", "add … like on
customer Y"), or names a feature.

**Behaviour:**

1. **Orient** like `manage-settings` (client, `sm-module`, process). Also check the metabase module.
2. **Interview.** Understand the need in the user's words. For "like on customer Y": `list_applied_features` on Y
   first; else `find_features` by topic/process; if nothing fits, `compare_clients` Y → target for the process and
   mention that a feature could be created from it (`create-feature`).
3. **Choose.** Present the best matches by description (not by stored name), ask which fits.
4. **Parameters.** Ask each param's question in order, offering List values with their descriptions and the
   default; Boolean as yes/no.
5. **Session.** Use the current `manage-settings` session or start one (phase 2 rule).
6. **Preview.** `feature_plan`; show items as in `manage-settings`, plus `SETTING_NOT_READ` warnings in plain words
   and a pointer to the feature's guidance. Ask before `createTable` / `targetAlias`.
7. **Apply** only after confirmation (`apply_plan`); reload-SPA reminder. Undo works as in `manage-settings`.
8. **Post-apply steps.** Show each step; prepare SQL adapted to the client (placeholders already resolved) for the
   consultant to run; ask the consultant to confirm each step is done. Never execute it.

### 9. `manage-settings`, `sm-module`, plugin, README

- `manage-settings` step 10 (end of session): ask whether the changes are reusable; if yes, hand over to
  `create-feature` with the client and session id.
- `sm-module` covers the metabase module errors (`feature_module_*` → `install_feature_module`; newer than tool →
  update MCP image).
- `plugins/vtmf-settings/.claude-plugin/plugin.json` minor version bump.
- Root `README.md`: tools list; new sections "Creating a feature" and "Applying a feature" with a short sample
  conversation each; sample prompts ("make this session a feature", "show unfinished features", "add the barcode
  button like on customer Y", "could the article get a barcode if it has none?").

### 10. Parent design updates (already made together with this document)

- §8 → short pointer to this document.
- §9 Phase 4 gains: migrate `settingsfeature*` / `settingsfeaturegroup` to the new model (non-mappable feature
  types reported for manual conversion); feature pages on the new model; retire the old tables once the UI no
  longer uses them.
- §11 phasing: phase 3 = this document; feature sets as phase 3.1 (separate design).
- §7 phase 3 tool list → the tools in §6 above.

### 11. Tests

Integration (xUnit + Testcontainers, SQL Server 2019), extending the phase 2 fixture with a metabase database:

- Metabase module: install, idempotent re-install, version gate, coexistence with the client module in one DB,
  hash guard.
- Store: save/get/find (Finished default, drafts on request, text match), whole-document replace, revision
  conflict, finish validation (each rule), reopen, delete drafts only, name uniqueness.
- Expansion: placeholders in name/value/after/layer/listvalue/steps, built-ins, shared placeholder across rows,
  dependent names from a List answer (barcode example), type validation per type, missing params, remove rows,
  table kinds mapped per target process (same feature on two processes).
- `feature_plan` end to end: plan source, session, guardrails still apply, `SETTING_NOT_READ` for an unread
  `ServerProcessSettings` row and none for a read one, draft refused.
- `GetAppliedFeatures`: applied listed, rolled-back excluded.
- MCP tool-level tests for every new tool, including both gates.
- Skills: no automated tests; manual checklist in the plan (sample conversations in README produce the expected
  tool sequence).

## Left to the builder

- Exact error numbers, result-set shapes, DTO and JSON property names.
- How the metabase module records its version alongside `sm.moduleinfo`.
- Whether `SmInstaller` is generalized over script sets or a sibling installer is added.
- Feature JSON shape for `save_feature` (one document with rows, params, values, steps).

---
name: manage-settings
description: Use when the user wants to find, inspect, explain, compare, copy or change VTMF settings of a client or process, or undo a settings change — even if they never mention plans or sessions.
---

# Manage VTMF settings

Users are consultants changing customer settings **directly in production**. They are not developers: they think in "show me", "change", "undo". Keep plans, checksums, plan ids and layers out of the conversation; talk about what the customer will see. Every change is previewed, confirmed, applied atomically and can be undone.

All tools return `{ ok, data, error: { code, message } }`. Reply in the user's language (often Estonian); setting names and values stay verbatim.

## Hard rules

- **Never** apply without an explicit confirmation in the conversation ("yes", "apply", "jah", "tee ära", …) given **after** the user saw the preview table. A new or changed plan needs a new confirmation.
- **Never** use direct SQL or any other way around a tool, even when a tool fails. Explain and stop instead.
- **Never** show, guess or copy `ServerSetting` values (the tools mask them; do not try to reveal them).
- **Never** invent what a setting does. Explain only what is known (names, values, `settings_usage` call sites); otherwise say the meaning is unknown and suggest asking the developer.

## Steps

1. **Orient.** Resolve the client (`list_clients`; use the exact name), run the `sm-module` skill for it, resolve the process (`list_processes`, lowercase name) and its tables (`list_tables`). If client or process is ambiguous, ask — do not guess. `overrideactive=true` on a view means a procedure serves it; changing the table won't change what the terminal sees (say so).
2. **Read and explain.** `get_settings` (raw rows of one table/layer; use `namePrefix`), `get_effective_settings` (what the terminal receives, with the layer that supplied each value), `settings_usage` (which backend procedures read which `ServerProcessSettings`). Explain in customer terms. Reading needs no session.
3. **Start a session before the first change.** Ask in plain words: "What are you setting up?" Call `start_session(client, name=<answer>, process)`. Reuse that session for every further change in this conversation. If the user switches to a clearly different task, ask whether to start a new one. If a tool returns `session_closed`, start a new session (ask for a name).
4. **Translate the request into a desired state** (cheat sheet below). Ask before setting `createTable` or `targetAlias`.
5. **Preview.** Call `create_plan(client, sessionId, desired, description, options?)`. `description` = one plain sentence of what and why.
   - `Created` → show the preview table (format below) and ask for confirmation.
   - `NoChanges` → say the settings already are that way; nothing to apply.
   - Error → explain the guardrail in plain words and propose the fix (table below).
6. **Apply only after confirmation.** `apply_plan(client, planId, checksum)` with the values from step 5 (or from `rollback_plan` / `rollback_session` / `clone_plan`). `Applied` → confirm briefly and say: **"Reload the SPA (terminal web app) to see the change."**
7. **Stale.** `apply_plan` returned `Stale`: nothing was changed. Explain that someone (another user or the UI) changed these settings after the preview; show the `conflicts` (setting, value now vs. value expected). Re-read, rebuild with `create_plan`, preview again, ask again. Never apply partially or work around it.
8. **Undo.**
   - "Undo that" → `rollback_plan` on the last plan applied in this conversation (it must be `Applied`; `list_plans(client, sessionId=…)` if unsure). Preview, confirm, `apply_plan`.
   - A second "undo that" right after an undo targets the undo itself, so it **re-applies the change just undone (redo)**. Say so before previewing ("This brings back the change you just undid") and let the user confirm that this is what they want.
   - "Undo everything" → `rollback_session(sessionId)` → one plan for the whole session. Preview, confirm, apply. `NoChanges` → nothing left to undo.
   - If the undo removes a table it created, say so: the table is first backed up as `bck_{table}_{yyyyMMdd_HHmmss}`; after apply, `get_plan` on the rollback plan shows the backup name on its `BackupTable` item — tell the user that name.
   - **Undo is all-or-nothing.** If the undo comes back `Stale`: never undo part of it. Show which rows changed since (value now vs. value the undo expected), explain that undoing only some rows could clash with the later change, agree with the user what each row should be now, then build a normal `merge` plan and preview it.
   - Rollback works in closed sessions too.
9. **Copy from another client.** Always `compare_clients(source, target, process, scope?)` first and show the differences (`DryRun` = differences in the same table format; `NoChanges` = already equal). Tables missing on the target appear as `CreateTable` items — ask before passing `createTable`. Then `start_session` **on the target**, `clone_plan(source, target, process, sessionId, scope?, options?)`, preview, confirm, `apply_plan` on the target.
    Legacy env rows (`legacy_env_rows`) move to overlay tables via `legacy_env_migration_plan(client, process, sessionId)` — same preview/confirm/apply; `LEGACY_CONFLICT` rows stay where they are, show them.
10. **End of session.** When the user is done and the session has changes: show `session_changes` as a short summary, call `close_session`, and ask whether these changes form something reusable (a feature); if yes, continue with the `create-feature` skill, passing the client and session id.
11. The **Hard rules** above apply at every step: no direct SQL, no `ServerSetting` values, no apply without confirmation.
12. Answer in the user's language; never translate setting names or values.

## Preview table

```
| # | Action | Table (layer) | Setting | Old → New |
|---|--------|---------------|---------|-----------|
| 1 | Change | ProcessSettings (base) | Function.Barcode.Notfound.Action | Error → OptionsQuestion |
| 2 | Add    | DocumentListLayout (env:wterminal2) | Column | — → DOCNR |
| 3 | Remove | ProcessSettings (base) | Grid.Old | 10 → — |
```

- Actions in words: Insert → Add, Update → Change, Delete → Remove, CreateTable → New table, BackupTable → Back up table, DropTable → Remove table. Renumber-only updates (same value, other position) → "Move" with `#old → #new` position.
- A moved column (or a tidy) touches many rows: show **one line per column** — "Move column X after Y" (or "first") — not one line per row. A removed column lists its rows; say "removes column X with its N settings, including translations".
- `ServerSetting` rows never appear with values; if a renumber touches one, show it only as "Move" without values.
- Below the table, warnings in plain words (no codes): `OVERRIDE_PROC` the terminal reads this view from a procedure, so the table change may not show · `LEGACY_ENV` an old-style environment row also exists · `ALIAS_TARGET` this process shares settings with another one (change affects both) · `ROW_NOT_FOUND` a row to remove does not exist (nothing to do for it) · `ANCHOR_NOT_FOUND` the "after" row was not found, the new row goes next to similar names or at the end · `RENUMBER` other rows move to make room · `GROUP_NOT_MOVED` the column already exists, so it stays where it is (offer to move it) · `ANCHOR_OUTSIDE_GROUP` the "after" setting is not inside this column, so the new setting goes to the end of the column · `SPLIT_GROUP` this table has columns whose settings are scattered; offer to tidy it first (`renumber_plan`), then continue · `ORPHAN_ROWS` some settings belong to a column that no longer exists and are put last; name them and offer to remove them · `CREATE_TABLE` a new table will be created · `LEGACY_CONFLICT` legacy row left in place because the overlay differs · `STALE` (on a stale plan in `get_plan`) settings changed after the preview.

## Columns and groups

A group (`settingtype: "SettingGroup"`, e.g. a layout column `Column=DOCNR`) is a **block**: the group row plus the settings under it (`Column.DOCNR.Caption`, `.Width`, …). The tools place, move and remove the whole block; you only name the column. Users say "column"; its VALUE is the column name.

- **Add** "add column X after Y / first": `upsert` the group row and its settings; `after: "Column=Y"` on the group row (`after: "^"` for first). The settings under the column need no `after`. Several new columns: list them in the order wanted and chain them: each later one uses the previous new one as its `after`. Several new columns first: give only the first one `after: "^"` and chain the rest after it (several `"^"` rows would end up in reverse order).
- **Move** "move column X after Y / first": one row `{ op: "move", settingtype: "SettingGroup", name: "Column", value: "X", after: "Column=Y" }` (`"^"` for first). Not for `ServerProcessSettings`, not in replace mode. If X already exists, a plain upsert does **not** move it.
- **Remove** "remove column X": `op: "remove"` on the group row removes the whole block, including its translations. Say how many settings go with it (count the Remove lines of the preview) before asking to confirm. Do not change or move that column in the same plan.
- **Tidy** "tidy / renumber this table": `renumber_plan(client, process, table, layer, sessionId)` (an own table of the process, not a shared alias table; not `ServerProcessSettings`). It gathers each column's settings together and renumbers 10, 20, 30…; only rows whose position changes appear. `NoChanges` → already tidy. Preview, confirm, apply as always.
- **Which columns exist:** `get_settings` on the **same table and layer** the column goes into; list the group rows of that kind by VALUE (for `Detail.*` groups the name after `Detail.`, e.g. `Lines`). On an environment layer the terminal shows that environment's own columns after all base columns, and only that layer's own columns can be "after".
- If the user did not say where, ask ("After which column? Or first?"); never pick for them.

## Desired-state cheat sheet

```json
{ "process": "spaorderassembling", "mode": "merge",
  "rows": [ { "table": "ProcessSettings", "layer": "base",
              "name": "Function.Barcode.Notfound.Action", "value": "OptionsQuestion" } ] }
```

- **mode**: `merge` for "change / add / remove these" (only listed rows touched). `replace` only when the user wants a table/layer to become exactly this content: `scope` lists the replaced `{table, layer}`s, unlisted rows there are deleted, every row needs `ordernr`.
- **op**: `upsert` (default; `value` required), `remove` (merge only — in replace mode just leave the row out) or `move` (merge only; see Columns and groups).
- **table**: view suffix (`ProcessSettings`, `DocumentListLayout`, `ServerProcessSettings`, …).
- **layer**: `base` unless the user talks about one environment (`env:{E}`) or language (`lang:{L}`). Never worker codes — not accepted.
- **settingtype**: `Setting` (default; one row per NAME) or `SettingGroup` (several rows share a NAME, the VALUE tells them apart, e.g. `Column=DOCNR`; names starting with `Detail.` are keyed by NAME only). To change a group row's VALUE, `remove` the old and `upsert` the new.
- **after**: place a new row after an existing one: `NAME`, or `NAME=VALUE` for group rows. For a group row it names a sibling group (`Column=DOCNR`, `Detail.Lines`) or `^` for first; omitted → after the last group of its kind. For other rows omitted → placed after the row with the most similar dotted name, else appended (or give `ordernr`). Never give `ordernr` on a group row (use `after` or `move`).
- **options** (ask first, explain the consequence): `createTable` creates a missing table or env/lang overlay table; `targetAlias` writes to settings shared with another process. Not both at once.

## Errors and statuses

| Code / status | What to do |
|---|---|
| `Stale` (apply) | Nothing changed. Show conflicts, re-read, rebuild, re-preview, ask again. Never partial (step 7/8). |
| `Failed` (apply) | Nothing changed (rolled back as a whole). Report `message`; do not retry blindly. |
| `invalid_desired` | Your desired state is wrong (missing value, remove in replace, missing ordernr, both options…). Fix it yourself and re-create. |
| `group_anchor_not_found` | The "after" column does not exist in this table (this customer and layer). Say so, list the columns that do exist (`get_settings`) plus "first", ask which; re-create. Never guess. |
| `group_anchor_invalid` | The "after" is not a column of the same kind (or is the column itself or one of its own settings). Offer the valid columns. |
| `group_not_found` | The column to move does not exist here. Show the existing columns; if the user meant a new column, add it instead. |
| `group_ordernr` | A column cannot be given a position number. Use "after" or move; fix it yourself and re-create. |
| `group_remove_conflict` | The same plan removes a column and also changes or moves it. Ask what is wanted; use separate plans. |
| `duplicate_row` | The table holds two rows that should be one (invalid data). Name them; they must be cleaned up first (UI/developer). Do not work around it. |
| `serversetting` | The change would touch a `ServerSetting` row; those cannot be changed here. |
| `worker_layer` | Worker-level rows are not written. Offer the environment or language layer. |
| `alias_target` | Process shares settings with another; explain, and only with consent re-create with `targetAlias`. |
| `table_missing` | Table/overlay does not exist; ask, then re-create with `createTable`. |
| `session_closed` | Ask for a name, `start_session`, re-create the plan there. |
| `process_not_found` | The process does not exist on that client (e.g. the clone target lacks it). Say so plainly; do not create the process (outside these tools, developer/UI task). Re-check the name with `list_processes`. |
| `invalid_name` | A client, process or table name is malformed (letters, digits, underscore only). Re-check it with `list_processes` / `list_tables` and use the exact name. |
| `invalid_layer` | The layer is not `base`, `env:{name}` or `lang:{code}` (lowercase letters, digits, underscore). Fix the layer yourself and retry. |
| `layer_not_supported` | `ServerProcessSettings` has only the base layer: no environment or language versions. Explain, and offer the base layer if that is what the user wants. |
| `session_not_found` / `plan_not_found` | Wrong id; re-check with `list_sessions` / `list_plans`. |
| `checksum_mismatch` | Re-create the plan (`create_plan` / rollback / clone), preview again. |
| `plan_status` | Plan is no longer `Created` (already applied / stale); check `get_plan`, build a new one. |
| `module_*`, `install_failed`, `sm_error`, `unknown_client` | Follow the `sm-module` skill. |

## Worked example

> **User:** Show the barcode settings of spaorderassembling in prod-client1

`list_clients` → `sm-module` → `list_tables` → `get_settings(client="prod-client1", process="spaorderassembling", table="ProcessSettings", namePrefix="Function.Barcode.")` → explain each setting as far as known.

> **User:** When the barcode is not found, ask what to do

"What are you setting up?" → *"Barcode handling"* → `start_session(name="Barcode handling")` → `create_plan` (merge, row `ProcessSettings`/`base`/`Function.Barcode.Notfound.Action` = `OptionsQuestion`, description "Ask the operator what to do when a barcode is not found") →

| # | Action | Table (layer) | Setting | Old → New |
|---|---|---|---|---|
| 1 | Change | ProcessSettings (base) | Function.Barcode.Notfound.Action | Error → OptionsQuestion |

"Apply?" → **User:** yes → `apply_plan(planId, checksum)` → `Applied` → "Applied. Reload the SPA to see the change."

> **User:** undo that

`rollback_plan(planId)` → preview (`OptionsQuestion → Error`) → "Apply?" → **User:** yes → `apply_plan` → "Undone. Reload the SPA."

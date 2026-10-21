---
name: create-feature
description: Use when the user wants to define, record, package, edit, finish or list (unfinished) VTMF features, or says that settings they just changed should be reusable.
---

# Create a VTMF feature

A **feature** is a named, reusable piece of behaviour that settings switch on ("Allows adding a barcode to an article on the product card (sparelocateitems)"). The code behind it already exists; the feature records which settings make it work, which choices the consultant has when applying it, and what must be done by hand afterwards. Features live in the shared metabase, so every customer can use them. Consultants find them by their **description**, so the description matters more than anything else.

The author is a developer or a consultant who knows the code behaviour. All tools return `{ ok, data, error: { code, message } }`. Reply in the user's language (often Estonian); setting names and values stay verbatim.

## Hard rules

- **Never** edit settings tables from this skill. No `create_plan`, `feature_plan` or `apply_plan` here. To try the behaviour, the author changes settings with the `manage-settings` skill (usually in the session the feature is built from) and records the result here.
- **Drafts are never applied** — only Finished features can be planned, and only by `apply-feature`.
- **Never** execute step SQL. Steps are guidance; their SQL is shown to the consultant who runs it.
- **Never** copy `ServerSetting` rows into a feature (they are refused) and never show their values.
- **Save to the metabase immediately and after every change** — the chat is not the storage.

## Steps

0. **Orient.** `feature_module_status`. `not_installed` / `outdated` → `install_feature_module`, report the version, continue. `newer_than_tool` or `metabase_not_configured` → follow the `sm-module` skill and stop. If you will read a client (session or another customer's settings), run `sm-module` for that client too.
1. **Start.** Find out where the feature comes from:
   - **from a session** ("make this session a feature"): `session_changes(client, sessionId)` on the client the session ran on (`list_sessions` if the id is unknown; `manage-settings` passes client and session id when it hands over);
   - **from another customer's process** ("take the barcode settings from customer Y"): `get_settings` on that client/process/table, the user picks the rows;
   - **from scratch**: the user names the settings;
   - **continue a draft** ("show unfinished features"): `find_features(status: "Draft")`, list them by description, the user picks one, `get_feature(id)`.
2. **Describe first** (before the name). Ask: "What does this let the end user do, and where?" Check the answer:
   - user-facing: says what becomes possible and where (process / screen);
   - short (one sentence, two at most);
   - no setting names as the main content.

   If it fails, offer a rewrite and let the user accept or edit it.
   - Bad: "feature adds settings Action.Barcode to sparelocateitems"
   - Good: "Allows adding a barcode to an article on the product card (sparelocateitems)"
3. **Name.** Propose 2–3 names that say what the feature does (e.g. "Add barcode to article on product card"). No person names ("NeemeFeature"), no storage terms ("DocumentTableRow"). The user picks or edits. Then **immediately** `save_feature(feature)` without `id` and without `revision` — it is stored as **Draft**. Include `builtOnProcess` when known (informational only). Keep the returned `id` and `revision`.
4. **Rows, iteratively.** The user names settings to add, change or remove per table; or, from a session, propose the net changes as rows and let the user trim them:
   - item with an after-image (Insert / Update) → row `op: "set"` with the new `value`;
   - deleted row (Delete) → row `op: "remove"` (`value` omitted);
   - `CreateTable` → no row; add a row `note` (or tell the author) that the table is created at apply time only with the consultant's consent;
   - `ServerSetting` rows → leave out and say why.

   Translate physical tables to table kinds and layers: `table` = view suffix without the process name (`spaorderassemblingDocumentLayout` → `DocumentLayout`, `ProcessSettings`, `ServerProcessSettings`, …); `layer` = `base`, `env:{E}` or `lang:{L}`. A feature is not tied to a process: frontend rows work on any process; only `ServerProcessSettings` rows need support in the target process code. Use `settingtype: "SettingGroup"` for group rows (e.g. `Column`), `after` (`NAME` or `NAME=VALUE`) when the position matters; rows have no `ordernr`.

   **Save after every change.** `save_feature` replaces the whole feature: always send the complete document (every row, parameter and step) with `id` and the **last** `revision` you got. "Show the current state" → `get_feature(id)`, rendered as a short table:

   ```
   | # | Op | Table (layer) | Setting | Value |
   |---|----|---------------|---------|-------|
   | 1 | set | ProcessSettings (base) | Function.Barcode.Notfound.Action | {{notfoundAction}} |
   ```
5. **Parameters.** Ask which values vary per customer. Replace each with a placeholder `{{word}}` (letters, digits, underscore; starts with a letter) in **every** row and step that shares it — one answer fills every occurrence. Placeholders work in `name`, `value`, `layer` (`env:{{env}}`), `listvalue`, `after` and in steps; not in `table`. Built-ins, never asked: `{{processname}}` (target process), `{{clientname}}` (target client).

   Examples:
   - Column: group row `Column` = `{{name}}`, then `Column.{{name}}.Caption`, `Column.{{name}}.Width`, …
   - Barcode: `Function.Barcode.Notfound.Action` = `{{notfoundAction}}`, `Function.{{notfoundAction}}.Notfound.ItemType` = `Option`, `Function.Barcode.Notfound.{{notfoundAction}}.Procedure` = … — the chosen value drives the dependent setting names.

   For each parameter: a question in plain words (`description`), a `type`, an optional `default`, and for List the values with descriptions:
   - `Text` (an empty answer is valid), `Number`;
   - `Boolean` — stored as the text `True` / `False`; the consultant answers yes/no;
   - `List` — at least one value; each `{ value, description }`.

   A parameter without a default must be answered at apply time. Save. `PARAM_UNUSED` in the warnings → a parameter no row or step uses: remove it or use it.
6. **Post-apply steps.** Ask about work outside settings tables (Directo hooks in `dcquerysettings`, scanner profiles, other records) and important guidance for the consultant. Each step: `text` (required guidance) and optional `sql`. Help write SQL that is **idempotent**, guarded with `IF NOT EXISTS` (pattern of the existing `065-script-PostSetBarcodeQuery.txt`), e.g.:

   ```sql
   IF NOT EXISTS (SELECT 1 FROM dcquerysettings WHERE <key columns> = '{{processname}}' ...)
       INSERT INTO dcquerysettings (...) VALUES (...);
   ```

   Placeholders are allowed in steps and are filled at apply time. Save.
7. **Finish.** When the author is satisfied: `set_feature_status(id, status: "Finished", revision)`. On `feature_invalid`, explain the refusal in plain words (missing description, a placeholder without a described parameter, a parameter or List value without a description, an invalid default, no rows and no steps) and fix it with the author, save, and finish again. Only now can consultants find and apply it.
8. **Complexity rule.** If something needs conditions, branching or per-option row groups, **don't model it**. Write it as step guidance, or suggest a separate complementary feature described in free form.
9. **Editing a Finished feature.** Small fixes: `save_feature` with the full document; a save that would make it invalid is refused (`feature_invalid`). Bigger changes: reopen with `set_feature_status(id, status: "Draft", revision)`, edit, finish again. No versioning: the feature is changed in place; customers get the fix by applying it again (only differences appear in the plan). Rows removed from the feature are not removed from customers unless the feature has an explicit remove row.
10. **Delete** only drafts and only after the user asks: `delete_feature(id, revision)`. A Finished feature must be reopened first. Settings already applied are not touched.

## Errors

| Code | What to do |
|---|---|
| `feature_changed` | Someone else saved in between. `get_feature(id)`, reapply your edit on the fresh document, save with the new revision. Never overwrite blindly. |
| `feature_invalid` | Explain the validation message in plain words, fix with the author, save again. |
| `feature_name_taken` | Another feature has that name (case-insensitive). Propose another name. |
| `feature_status` | The operation needs another status (e.g. delete needs Draft). Explain; reopen only with the author's consent. |
| `feature_not_found` | Wrong id or name; `find_features(status: "all")`. |
| `metabase_not_configured`, `feature_module_*` | Follow the `sm-module` skill. |
| `session_not_found`, `module_*`, `unknown_client` | As in `manage-settings` / `sm-module`. |

## Worked example

> **User:** make this session a feature

`session_changes(client="dev-client1", sessionId=42)` → "What does this let the end user do, and where?" → **User:** *the barcode thing on the product card* → "Suggested description: *Allows adding a barcode to an article on the product card (sparelocateitems)*. OK?" → **User:** yes → "Names: *Add barcode to article*, *Article barcode on product card*, *Barcode for articles without one*" → **User:** the second → `save_feature({ name, description, builtOnProcess: "sparelocateitems", rows: [], params: [], steps: [] })` → id 7, revision → propose the session's net changes as rows → user trims → `save_feature` (full document, last revision) → "Which values differ per customer?" → `{{notfoundAction}}` List param → save → "Anything outside settings tables?" → step with `IF NOT EXISTS` SQL → save → `set_feature_status(7, "Finished", revision)` → "Finished. Consultants can now apply it with apply-feature."

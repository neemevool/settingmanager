---
name: apply-feature
description: Use when the user wants some behaviour on a customer process ('could it be possible…', 'add … like on customer Y'), or names a VTMF feature to apply.
---

# Apply a VTMF feature

Users are consultants applying features for customers **directly in production**. They ask in the customer's words ("could the article get a barcode if it has none?") or by reference ("add the barcode button like on customer Y in sparelocateitems"). They never need to know a feature's stored name, tables or placeholders. Talk about what the customer will get; keep feature ids, plan ids, checksums and placeholders out of the conversation.

A feature is applied through the same plan engine as `manage-settings`: preview, confirm, apply, undo. All tools return `{ ok, data, error: { code, message } }`. Reply in the user's language (often Estonian); setting names and values stay verbatim.

## Hard rules

- **Never** apply without an explicit confirmation in the conversation given **after** the user saw the preview table. A new or changed plan needs a new confirmation.
- **Only Finished features** are offered or planned. Never offer a draft (`find_features` returns Finished by default — do not pass `status: "Draft"` or `"all"` here).
- **Never** execute post-apply SQL. Show it; the consultant runs it (Settingmanager module install or their own SQL tool).
- **Never** use direct SQL or any other way around a tool. Never show `ServerSetting` values.
- **Never** invent what a feature does beyond its description and guidance.

## Steps

1. **Orient** like `manage-settings`: resolve the client (`list_clients`), run the `sm-module` skill for it, resolve the process (`list_processes`, lowercase name). Also check the feature module: `feature_module_status` (`not_installed` / `outdated` → `install_feature_module`; `newer_than_tool` or `metabase_not_configured` → `sm-module`, stop). If client or process is ambiguous, ask.
2. **Interview.** Understand the need in the user's words; ask short questions until you know what the customer should be able to do and on which process.
   - **"Like on customer Y"**: first `list_applied_features(client=Y, process?)` — the features applied there, with descriptions. Pick the one(s) matching the request.
   - Otherwise `find_features(text?)` by topic or process word (or without text; the catalog is small) and match the **descriptions** yourself.
   - **Nothing fits**: if a reference customer is known, `compare_clients(Y, target, process)` and show the differences (copy them the `manage-settings` way if the user wants), and mention that a reusable feature could be created from it with the `create-feature` skill. Without a reference, say no feature exists for this yet.
3. **Choose.** Present the best matches **by description** (not by stored name), at most a few, and ask which fits. Only after the choice: `get_feature(id)` to read its parameters, steps and rows. If the feature sets `ServerProcessSettings`, mention that the target process code must support it (the preview checks this).
4. **Parameters.** Ask each parameter's question (`description`) in order, one or a few at a time:
   - `List`: offer the values with their descriptions, mark the default;
   - `Boolean`: ask yes/no; pass `True` / `False`;
   - `Text` / `Number`: offer the default if there is one.

   A parameter without a default must be answered. Never guess an answer. `{{processname}}` and `{{clientname}}` are filled automatically — never ask for them. Answers can end up inside setting names (e.g. a column name); the preview shows the resulting names — check them with the user.
5. **Session.** Use the current `manage-settings` session in this conversation, or ask "What are you setting up?" and `start_session(client, name, process)` (phase 2 rule). `session_closed` → start a new one.
6. **Preview.** `feature_plan(client, featureId, process, sessionId, parameters, options?)`.
   - `data.plan.status` `Created` → show `data.plan.items` in the `manage-settings` preview table format, the plan warnings in plain words, and `data.warnings`:
     - `SETTING_NOT_READ` → "No procedure of {process} reads `{setting}`; the code of this process may not support this feature" (if the message says the scan was uncertain, say so). Point to the feature's guidance; let the consultant decide.
   - Briefly list the post-apply steps (`data.steps`) that will follow, so the consultant knows the whole job before confirming.
   - `NoChanges` → the customer already has it this way; nothing to apply (still offer the post-apply steps if they may be missing).
   - A missing table (`table_missing`) or alias process (`alias_target`): explain and **ask** before re-running `feature_plan` with `options: { createTable: true }` or `{ targetAlias: true }` (not both).
7. **Apply only after confirmation.** `apply_plan(client, data.plan.planId, data.plan.checksum)`. `Applied` → confirm and say: **"Reload the SPA (terminal web app) to see the change."** `Stale` / `Failed` and undo ("undo that" → `rollback_plan`, …) work exactly as in `manage-settings`.
8. **Post-apply steps.** After the apply, go through `data.steps` **one by one**: show the guidance; if the step has SQL, show it in a code block, already adapted to this client and process (placeholders are resolved), and say the consultant runs it. Ask the consultant to confirm each step is done before the next. **Never execute the SQL** — not via a tool, not in any other way.

## Errors

| Code | What to do |
|---|---|
| `feature_param_missing` | Ask the listed questions, then call `feature_plan` again. |
| `feature_param_invalid` | The answer does not fit the type (not a number, not yes/no, not one of the list values). Ask again, offering the valid values. |
| `feature_not_finished` | The feature is a draft. Do not apply it; tell the user it is unfinished (its author can finish it with `create-feature`). |
| `feature_not_found` | The feature no longer exists; search again with `find_features`. |
| `table_missing`, `alias_target`, `session_closed`, `serversetting`, `invalid_desired`, `Stale`, … | As in `manage-settings`. |
| `metabase_not_configured`, `feature_module_*`, `module_*` | Follow the `sm-module` skill. |

## Worked example

> **User:** add the barcode button like on customer Y in sparelocateitems for prod-client1

`list_clients` → `sm-module` (Y and prod-client1) → `feature_module_status` → `list_applied_features(client="Y", process="sparelocateitems")` → "On Y this is applied: *Allows adding a barcode to an article on the product card (sparelocateitems)*. Is this it?" → **User:** yes → `get_feature` → "What should happen when the scanned barcode is not found? 1) *Ask the operator* (default) 2) *Show an error*" → **User:** ask → "What are you setting up?" → `start_session` → `feature_plan(...)` → preview table + warnings + "afterwards one manual step: a Directo hook" → "Apply?" → **User:** yes → `apply_plan` → "Applied. Reload the SPA." → "Step 1: add the Directo hook. Run this SQL in prod-client1:" *(SQL block)* "Tell me when it is done."

# Settings management — SettingGroup blocks in the plan engine

**Date:** 2026-10-24 · **Status:** For review
**Builds on:** `2026-10-19-settings-management-phase2-plan-engine-design.md` (plan engine, `sm.PlanOrder`),
`2026-10-20-settings-management-phase3-features-design.md` (features, `create-feature` / `apply-feature`).
**Prior art:** `AddDocumentListLayoutColumn.txt` (a customer procedure that adds a layout column after a named column;
works for that case).

## Intent

Layout tables describe columns, details and similar things as **groups**: a `SettingGroup` row plus the rows under its
name, for example

```
Column          = DOCNR     (SettingGroup)
Column.DOCNR.Caption = Document no.
Column.DOCNR.Width   = 120
```

The order of the groups is what the user sees: the terminal orders columns by the `SettingGroup` rows, and the
Settingmanager UI orders them by where the children sit (the tree node `Column.DOCNR` is created at the first child
row, `SettingsManager.razor.cs` `BuildSettingsTree`). So a group and its children must stay together as one **block**,
and the position of a block is a real choice the consultant makes ("after which column?").

Today the plan engine has no notion of groups. It places every new row on its own (anchor, longest name prefix, table
end), so a new group can end up split (children before the group row, or a new row landing between another group's row
and its children), `remove` of a group row leaves its children behind, and nothing can move a group. The feature skills
therefore cannot record "ask the consultant where the new column goes, then put it there": `create-feature` tells the
author it cannot store that reliably.

This design makes groups first-class in the plan engine: adding, moving and removing work on whole blocks, a group's
position is asked as "after which column (or first)", and a renumber plan tidies a table. The feature model gains a
**Position** parameter so features that add groups ask exactly that question.

## Who

- **Consultant** — adds, moves and removes columns/details for a customer with `manage-settings` or `apply-feature`.
  Thinks "put the barcode column after the document number" or "first", never in ordernr.
- **Feature author** — records a feature that adds one or more groups; must not have to think about placement
  mechanics.

## Goals

1. Plan engine treats a `SettingGroup` row and the rows under its path as one block for add, move and remove.
2. Group placement: "after group X" (after X's whole block) or "first" (before the first group of the same kind).
3. A named anchor group that does not exist refuses the plan.
4. `move` operation for groups (whole block).
5. `remove` of a group row removes the whole block.
6. Renumber plan for one table: blocks contiguous, steps of 10, orphan children last.
7. Warning when a table touched by a plan already contains split groups.
8. Feature model: parameter type `Position`; features that add groups ask the consultant the position.
9. Skills `manage-settings`, `create-feature`, `apply-feature` (and README) know these rules.

## Non-goals

- Settingmanager UI code (Blazor, `SettingsService`): unchanged. Its limited use cases work well enough (user).
- Order of plain settings (not in a group): it does not matter (user). The engine keeps it stable but makes no promises.
- Order of children inside a block: it does not matter (user). Kept as it is.
- Moving plain settings.
- `ServerProcessSettings` ordering (stays unordered, new rows get ordernr 0).
- Cleaning up duplicate identities (still refused with `DUPLICATE_ROW` where renumbering is needed).

## Anti-goals (failure even if it "works")

- A plan places any row between a group row and its children when the row does not belong to that group.
- A group's children are left behind when the group is moved or removed.
- A consultant has to give an ordernr, a table position number or a setting name to place a column.
- A plan silently puts a group somewhere else because the named column does not exist on this customer.
- A feature that adds a group is applied without the consultant having chosen its position.

## Decisions made by the user

| # | Decision |
|---|---|
| G1 | Design the plan engine first; the UI stays as it is |
| G2 | A group = the `SettingGroup` row + the rows under its name prefix; it is always handled as one block |
| G3 | The consultant is asked only "after which column (or Detail, or other group) should it go"; placement is done by the tool |
| G4 | "First" is possible |
| G5 | If the named column does not exist on the customer, the plan is refused |
| G6 | Moving a group is needed, and moves the whole block |
| G7 | Removing a group removes the whole block |
| G8 | A renumber tool for a whole table; orphan children go last, after all settings |
| G9 | Order of children inside a block and of plain settings does not matter |
| G10 | Whole blocks, not only group rows, are moved (so terminal and Settingmanager UI show the same order) |
| G11 | The anchor must exist in the same table (layer): a column in `env:X` "after DOCNR" where DOCNR is only in base is refused |
| G12 | Removing a group also removes its child rows in the lang tables |
| G13 | Groups never nest |
| G14 | The terminal orders rows with equal ordernr by NAME |

## Design

### 1. Terms

All per settings table (physicaltable, workercode), using the identities and paths `sm.PlanOrder` already uses.
Every layer is its own table: base, each `env:{E}` and each `lang:{L}` table is ordered on its own, and anchors are
resolved only within the table of the row being placed (G11). The terminal shows an env table's new groups after all
base groups and ignores group rows in lang tables (`sm.GetEffectiveSettings`); the engine does not try to change that.

- **Group row**: a row with `settingtype = 'SettingGroup'`.
- **Group path**: `NAME.VALUE` (`Column.DOCNR`); for name-keyed groups (NAME starts with `Detail.`) just `NAME`
  (`Detail.Lines`). Same rule as `idkey` / `path` today (a group named exactly `Detail` is keyed by NAME+VALUE).
- **Children**: rows whose path starts with group path + `.`. Groups never nest (G13): a child is never a group row.
  *(Builder's note: if real data has a group row under another group's path, treat it as a child of the outer block,
  not as a nested group.)*
- **Block**: the group row plus its children.
- **Sort key**: (ordernr, NAME, VALUE) — matches the terminal, which orders equal ordernr by NAME (G14). Sibling
  group rows that share NAME and ordernr (`Column=A` and `Column=B` both at 0) have no defined terminal order: the
  engine never creates such ties (placed and moved rows always get an ordernr strictly between their neighbours,
  renumbering when there is no room), and the renumber plan removes existing ones.
- **Family** of a group: its group path without the last segment (`Column` for `Column.DOCNR`, `Detail` for
  `Detail.Lines`). Groups of the same family are siblings: "after which column" picks among them.
- **Block position**: the block sits at its group row (that is what the terminal orders by). Block end = the last row
  of the block by sort key (ordernr, NAME, VALUE).
- **Split block**: a block whose rows are not contiguous by sort key (a child sorts before the group row, or a row that
  is not in the block sorts between the group row and the block end).
- **Orphan child**: a row that is not in any block but whose path starts with `F.` where `F` is a family that has at
  least one group in the table (`Column.X.Caption` without a `Column = X` row). *(Builder's note: this may also catch a
  plain setting such as `Column.DefaultWidth`; harmless, since order of plain settings does not matter.)*

### 2. Anchors

The desired-row field `after` keeps its syntax (`NAME`, or `NAME=VALUE` for group rows) and gains one reserved value:

- `after: "^"` — **first**: before the first block of the placed group's family. *(My call: `^` cannot be a setting
  name, so it cannot clash with a real column.)*

Meaning of `after` by row kind:

| Row being placed | `after` names a group of the same family | `after` = `^` | `after` not found | `after` omitted |
|---|---|---|---|---|
| New group row, or `move` | after that group's **block end** | before the first block of the family | **plan refused** | after the last block of the family; no family yet → today's rule (longest prefix, else end) |
| New child of a group | honoured only if the anchor is inside the same block; else warning `ANCHOR_OUTSIDE_GROUP` and end of block | refused (invalid desired) | today's warning `ANCHOR_NOT_FOUND`, end of block | end of its block |
| New plain row (not in a group) | after that group's block end | refused (invalid desired) | today's warning `ANCHOR_NOT_FOUND` + fallback | today's rule |

A group row's `after` that names a group of another family, or a row that is not a group row, refuses the plan
(`GROUP_ANCHOR_INVALID`). A `move` whose anchor is the moved group itself or inside its block is refused too.
"Not found" means not in the same table: an env-layer column anchored on a column that exists only in base is refused
(G11).

**Existing groups.** An upsert of a group row that already exists does not place it: its `after` is ignored (not even
checked), the block stays where it is, and a warning `GROUP_NOT_MOVED` says so ("already exists; use move to change
its position"). This is the normal case when a feature is re-applied. *(My call.)* An explicit `ordernr` on a group
row in merge mode is refused (`GROUP_ORDERNR`, "use after or move"), because it would move the row without its block.
*(My call.)* Replace mode (clone/compare) keeps explicit ordernr for every row as today.

**One pass in desired order.** Inserts, moves and removes are resolved in desired order against the table as it will
be after the rows processed so far: an anchor may name a group added or moved earlier in the same plan. A plan that
removes a group and also upserts or moves it or one of its children is refused (`GROUP_REMOVE_CONFLICT`). *(My call.)*

Refusals use the guardrail mechanism (severity Error in `#warnings`, thrown, plan not stored), with new codes
`GROUP_ANCHOR_NOT_FOUND` and `GROUP_ANCHOR_INVALID` and new error numbers in the client module range (builder's call).
The message names the missing anchor and the table so the skill can re-ask in plain words.

**Never inside a foreign block.** Whatever rule picks a position (anchor on a non-group row, longest prefix; an
explicit `ordernr` on a plain row in merge mode is the only exception, kept as an expert override), a row is never
placed inside a block it does not belong to; such a position moves to after the end of that block.

### 3. Adding

- A new group's block is placed as a unit: the group row at the position from §2, its new children right behind it,
  **regardless of the order of the rows in the desired state** (children before the group row in `rows` are fine).
- Several new groups in one plan are placed in desired order; an anchor may name a group added earlier in the same
  plan (`Column=LOT` after `Column=BARCODE`, both new).
- New children of an existing group go to the end of that group's block (or after an in-block anchor).
- Ordernr: use the gap behind the position when it fits the whole block (rows spread evenly in the gap); otherwise the
  table is renumbered in steps of 10 with the existing `RENUMBER` warning. Same as today, but the gap must fit the
  block, not one row.

### 4. Moving

New desired-row op **`move`** (merge mode only):

```json
{ "table": "DocumentItemsListLayout", "layer": "base", "op": "move",
  "name": "Column", "value": "BARCODE", "settingtype": "SettingGroup", "after": "Column=DOCNR" }
```

- Only group rows (`settingtype: "SettingGroup"`); `after` required (a group of the same family, or `^`).
- The group must exist (else plan refused, `GROUP_NOT_FOUND`).
- The whole block moves to the position from §2; the internal order of the block is kept.
- Result: ordernr-only `Update` items for the block rows (and for other rows only if the table had to be renumbered).
  Undo works through the existing rollback (Update items carry `oldordernr`).
- A row that is moved and also changed in the same plan yields **one** Update item (value and ordernr).
- Moving to where the block already is → no items (`NoChanges` if nothing else).
- Not allowed in `ServerProcessSettings` (no ordering there) and not in replace mode.

### 5. Removing

- `op: "remove"` on a group row creates `Delete` items for **every row of the block**.
- **Lang tables (G12):** removing a group from the base or an env table also deletes the rows under its group path
  (`Column.DOCNR.*`) in every existing lang table of the same view — unless, after the plan, the same group still
  exists in another non-lang layer of that view (base or another env table), whose captions they also serve.
  *(The exception is my call.)* The plan's scope therefore includes the view's lang tables when a group is removed
  (`sm.PlanResolve` / `sm.PlanLoadCurrent`); rollback restores them like any Delete item.
- Removing a group from a lang table (rare; the terminal ignores group rows there) removes only that table's block.
- The preview lists each deleted row (existing preview; skills say "removes column X with its N settings, including
  its translations").
- Remove of a child row alone is unchanged (only that row).
- Replace mode is unchanged (it lists exactly the rows to keep). *(My call.)*

### 6. Renumber plan

New procedure `sm.CreateRenumberPlan @process, @table, @layer, @description, @sessionid = NULL, @dryrun = 0` and MCP
tool **`renumber_plan(client, process, table, layer, sessionId)`**. *(My call: a separate entry point rather than a
desired-state mode; it has no desired rows.)* Source `renumber`.

Final order of the table:

1. Walk the rows in current order (ordernr, NAME, VALUE).
2. A group row emits its whole block there: the group row, then its children in their current order. Children
   already emitted are skipped later. A child that sorted *before* its group row moves to its block (the group row
   decides, G2). Sibling group rows tied on ordernr and NAME are taken in VALUE order (the only stable choice; their
   terminal order was undefined anyway).
3. Plain rows stay in place relative to each other and to the blocks.
4. Orphan children go last, after all other rows, in their current order (G8).
5. Ordernr 10, 20, 30…

Only rows whose ordernr changes get an `Update` item; an already tidy table gives `NoChanges`. Duplicate identities
in the table refuse the plan (`DUPLICATE_ROW`, as today when renumbering). Refused for `ServerProcessSettings`.
Preview, apply, undo as for every plan. A warning `ORPHAN_ROWS` lists the orphan children moved to the end (they
probably belong to a removed column; the consultant may want to remove them).

### 7. Split-group warning

`sm.CreatePlan` warns `SPLIT_GROUP` for every touched table (one with Insert, Update with ordernr, Delete, or move
items) that already contains split blocks or sibling group rows tied on ordernr, naming up to a few groups and
suggesting `renumber_plan`. Not an error: the
plan still places rows by the rules above (block end = last row of the block, even if split).

### 8. Client module

All changes in `VTMF.Settings.Core/Sql/settingsmanagement/` (mainly `sm.PlanOrder`, `sm.PlanDiff`, `sm.CreatePlan`
validation, guardrails, new `sm.CreateRenumberPlan`). `module.json` version bump and new `sha256` (CLAUDE.md rule).
Idempotent scripts, no top-level `BEGIN TRAN`, SQL Server 2019 floor. Core: `SmPlanClient` (or equivalent) gets
`CreateRenumberPlanAsync`; error code mapping for the new error numbers.

### 9. Feature model: Position parameter

Metabase module (`VTMF.Settings.Core/Sql/settingsmetabase/`, version bump + `sha256`):

- `sm.featureparam.type` adds **`Position`**. *(My call: an explicit type, so the skills know to offer columns and
  "first", and finish validation can check it.)*
- A Position param may be used **only as the whole `after` of a `set` row with `settingtype = 'SettingGroup'`**
  (`after: "{{position}}"`). Anywhere else → save refused.
- **Finish validation:** every `set` group row must have an `after` that is either a Position param, or `NAME=VALUE`
  / `NAME` naming another group row of the same feature (to chain several new columns: second column after the first).
  Otherwise finish is refused with a plain message ("ask the consultant where the column goes").
- A Position param has no default and no list values; its description is the question ("After which column should the
  barcode column be shown?").

Expansion (`SmFeatureExpander`):

- Answer `first` (case-insensitive) → `after: "^"`.
- Any other answer is the sibling group's key: for a group row `Column` (keyed by VALUE) → `after: "Column={answer}"`;
  for a name-keyed group row `Detail.{{name}}` → `after: "Detail.{answer}"` (family + `.` + answer). The consultant
  answers just the column name (`DOCNR`), never `Column=DOCNR`.
- Empty answer → `feature_param_missing`. Existence is checked by the plan (§2: refused if not found).
- If the group already exists on the target, the answer has no effect (`GROUP_NOT_MOVED`, §2).

Features stored before this change: `feature_plan` checks the feature against the current finish rules and refuses a
Finished feature whose group `set` rows have no Position or same-feature anchor (`feature_needs_position`: "reopen it
with create-feature and add the position question"). No migration script. *(My call: phase 3 is new, few features
exist.)*

### 10. MCP tools

- `create_plan`: description updated — groups are blocks; `after` for group rows names a sibling group or `^` (first);
  `op: "move"` for groups; `remove` of a group removes its block.
- `renumber_plan` (new, no writes, returns a plan like `create_plan`).
- `feature_plan`: Position params resolved as §9; group anchor errors returned with their codes.
- `save_feature` / `set_feature_status` descriptions mention the Position type and the finish rule.

### 11. Skills (plugin version bump)

**`manage-settings`**
- Rules section: group = block; "add column X after Y / first", "move column X after Y / first", "remove column X"
  (whole block; say how many settings go with it), "tidy / renumber this table" → `renumber_plan`.
- To offer columns, read **the same layer's** table with `get_settings` and list the group rows of the family (by
  their VALUE, or name suffix for `Detail.*`). For an env layer, say that the terminal shows env columns after all base
  columns, and that only the env table's own columns can be anchors.
- Plain-word warnings: `GROUP_ANCHOR_NOT_FOUND` "column X does not exist on this customer — pick another or first" ·
  `GROUP_ANCHOR_INVALID` · `GROUP_NOT_FOUND` · `ANCHOR_OUTSIDE_GROUP` · `SPLIT_GROUP` "this table has columns whose
  settings are scattered; tidy it first?" · `ORPHAN_ROWS`.
- Preview: ordernr-only updates of a move shown as "Move column X after Y" (one line per block, not per row).

**`create-feature`**
- When a feature adds a group (`set` row with `settingtype: "SettingGroup"`), automatically add a Position param on
  that row's `after` and propose its question ("After which column should … be shown? (or first)"). Tell the author
  the tool places the whole column; children need no `after`.
- From a session: an inserted group row becomes a set row with a Position param; ordernr-only updates (moves) are not
  feature rows (features have no ordernr) — mention them to the author.
- Remove the old "cannot reliably store placement" behaviour.

**`apply-feature`**
- For a Position param: read the target table (`get_settings`, the table kind of the group row, on the chosen process),
  offer its existing groups of that family plus "first", ask in the param's words.
- On `GROUP_ANCHOR_NOT_FOUND`: explain and re-ask (G5); never pick a position yourself.

**README**: sample prompts "add the barcode column after the document number", "move the date column first", "remove
the old price column", "tidy the layout table".

### 12. Tests

Integration (existing Testcontainers fixture, SQL Server 2019):

- Add: new group + children in desired order and in reversed order → contiguous block after the anchor block end;
  `^` → before the first block of the family; omitted `after` → after the last block of the family; anchor missing →
  refused `GROUP_ANCHOR_NOT_FOUND`; anchor of another family / non-group → `GROUP_ANCHOR_INVALID`; two new groups
  chained in one plan; anchor = a group row whose children follow (the old split case) → after the children.
- Children: new child without `after` → end of its block; child with in-block anchor; child with foreign anchor →
  `ANCHOR_OUTSIDE_GROUP`, end of block.
- Plain row with an anchor or longest prefix pointing into a block → after the block.
- Gap too small for a block → renumber with `RENUMBER`; gap large enough → no renumber.
- Name-keyed groups (`Detail.*`) for add, move, remove.
- Layers: env-layer column anchored on a base-only column → refused; remove of a base group deletes its children in
  lang tables; kept when the same group remains in an env table; rollback restores the lang rows.
- Existing group upserted with `after` → no move, `GROUP_NOT_MOVED`; explicit ordernr on a group row in merge →
  `GROUP_ORDERNR`; remove + upsert of a child of the same group → `GROUP_REMOVE_CONFLICT`; insert anchored on a group
  moved earlier in the same plan.
- Ties: new group next to sibling group rows tied on ordernr → renumber, distinct ordernr; renumber plan breaks ties.
- Move: after a sibling, first, to its own position (no items), anchor inside own block (refused), missing group,
  move + value change of a child → one Update item, ServerProcessSettings refused; apply then rollback restores order.
- Remove group → Delete items for the whole block; rollback restores it.
- Renumber: split blocks joined at the group row position, child before its group row moved, plain rows stable,
  orphans last + `ORPHAN_ROWS`, tidy table → `NoChanges`, duplicates → `DUPLICATE_ROW`, apply + rollback.
- `SPLIT_GROUP` warning on a touched split table; none on a tidy one.
- Hash guard tests for both modules (version bumps).
- Features: Position type save/finish rules (used elsewhere → refused; group set row without position → finish
  refused; chained group anchor accepted), expansion (`first`, value-keyed, name-keyed), `feature_plan` end to end
  with a missing column → `GROUP_ANCHOR_NOT_FOUND`, old Finished feature without position → `feature_needs_position`.
- MCP tool-level tests for `renumber_plan` and the `move` op through `create_plan`.
- Skills: manual checklist (sample conversations produce the expected tool sequence).

## Left to the builder

- Restructuring `sm.PlanOrder` (e.g. place group rows first, then children; or compute a final order and assign
  ordernr) as long as the outcomes above hold and the existing ordering tests keep passing (adjust only those whose
  expectation contradicts the block rules, and say which).
- How rows are spread inside a gap; error numbers; result-set shapes; how the preview groups move items (skill side).
- Whether Position validation lives in SQL, C#, or both (today's feature validation split applies).

# SettingGroup Blocks in the Plan Engine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The plan engine adds, moves and removes a `SettingGroup` row together with its children as one block, places
groups "after column X" or first, refuses missing anchors, offers a renumber plan, and features ask the consultant the
position through a new `Position` parameter type.

**Architecture:** All ordering stays in T-SQL inside the `sm` client module: `sm.PlanOrder` is rewritten into a
block-aware single pass over the desired rows (inserts and moves), `sm.PlanDiff` expands group removes into whole
blocks (including lang tables), and a new `sm.CreateRenumberPlan` stores a renumber plan through the existing
`sm.PlanStore`. Core/MCP get thin wrappers; the feature model gains the `Position` type (metabase module, validator,
expander, planner); the skills are updated last.

**Tech Stack:** SQL Server 2019+ T-SQL (module scripts), C# / .NET 10, Dapper, ModelContextProtocol C# SDK, xUnit +
Testcontainers.

**Spec:** `VTMF.Settings/docs/superpowers/specs/2026-10-24-settings-management-setting-groups-design.md` (read it
first; section numbers below refer to it).

## Global Constraints

- Build and tests only via the wrapper: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build|test|gate [-Filter "<filter>"]`. Never `dotnet build` / `dotnet test`.
- Client module `VTMF.Settings.Core/Sql/settingsmanagement/module.json`: `version` 6 → **7** (once, Task 1); `sha256` updated in **every** task that changes a script there (the failing `RecordedHash_MatchesScripts` test shows the new hash as **Expected**, split in two 32-char halves — concatenate).
- Metabase module `VTMF.Settings.Core/Sql/settingsmetabase/module.json`: `version` 1 → **2** (Task 7) and `sha256` (test `RecordedHash_MatchesScripts_Metabase`).
- Plugin `plugins/vtmf-settings/.claude-plugin/plugin.json`: `version` 1.2.0 → **1.3.0** (Task 8).
- Scripts idempotent, no top-level `BEGIN TRAN`, SQL Server 2019 floor, every temp-table string column `COLLATE DATABASE_DEFAULT`.
- Identity / path rule unchanged: group path = `NAME.VALUE`, or `NAME` when NAME starts with `Detail.`; family = group path without its last segment. Groups never nest.
- Sort key everywhere: `(ordernr, NAME, VALUE)` (terminal orders equal ordernr by NAME).
- New error numbers (client module) and codes: 51017 `group_anchor_not_found` (`GROUP_ANCHOR_NOT_FOUND`), 51018 `group_anchor_invalid` (`GROUP_ANCHOR_INVALID`), 51019 `group_not_found` (`GROUP_NOT_FOUND`), 51020 `group_ordernr` (`GROUP_ORDERNR`), 51021 `group_remove_conflict` (`GROUP_REMOVE_CONFLICT`). C#-only code: `feature_needs_position`.
- New warning codes: `GROUP_NOT_MOVED`, `ANCHOR_OUTSIDE_GROUP`, `SPLIT_GROUP`, `ORPHAN_ROWS`. Existing `RENUMBER`, `ANCHOR_NOT_FOUND` unchanged in meaning.
- Reserved anchor `^` = first in the placed group's family. Only valid in `after` of a group row (upsert or move).
- Placement into a gap (A = ordernr of the row before, N = ordernr of the row after, k = rows to place): no N → `A + 10·i`; `N − A ≥ k + 1` → `A + (N − A)·i / (k + 1)` (integer division), i = 1..k; otherwise renumber the table (existing spread-and-compress path, `RENUMBER` warning). For k = 1 this is today's midpoint rule. "Before the first row" uses A = 0.
- Errors are raised through the guardrail mechanism (severity `Error` row in `#warnings`, first error's number thrown with all messages), never a ServerSetting value in a message.

## Review Focus

1. Consultant types the column in another case (`docnr`) → anchor still found (database collation, case-insensitive). Test in Task 1.
2. Legacy table where every row has ordernr 0 → adding a group renumbers and gives distinct ordernr. Test in Task 1.
3. Position answer with surrounding spaces (`" DOCNR "`) → trimmed before building the anchor. Test in Task 7.
4. Removing a group when a lang table exists but has none of its rows, or no lang table exists → plan works, no error. Test in Task 3.
5. Re-applying a feature whose column already exists on the customer → `GROUP_NOT_MOVED`, no items for the group row, no refusal even if the Position answer names a missing column. Test in Task 7.

---

### Task 1: Block-aware placement of new rows

**Files:**
- Modify: `VTMF.Settings.Core/Sql/settingsmanagement/126-procedure-PlanOrder.txt` (rewrite; update its header comment)
- Modify: `VTMF.Settings.Core/Sql/settingsmanagement/122-procedure-PlanDiff.txt` (call `sm.PlanOrder` unconditionally; it returns early itself)
- Modify: `VTMF.Settings.Core/Sql/settingsmanagement/129-procedure-CreatePlan.txt` (validation of `^`, header comment for `after`)
- Modify: `VTMF.Settings.Core/Sql/settingsmanagement/123-procedure-PlanGuardrails.txt` (map new codes to numbers in the throw `CASE`, or move that mapping into a shared spot used by `sm.PlanOrder`)
- Modify: `VTMF.Settings.Core/Sql/settingsmanagement/module.json` (version 7, sha256)
- Modify: `VTMF.Settings.Core/Sm/SmErrorCodes.cs`
- Create: `VTMF.Settings.Tests/Sm/PlanGroupPlacementTests.cs`
- Modify: `VTMF.Settings.Tests/Sm/SmErrorCodesTests.cs`
- Modify (only if an expectation contradicts §2/§3; list each change in the commit message): `VTMF.Settings.Tests/Sm/PlanOrderingTests.cs`

**Interfaces:**
- Produces: `SmErrorCodes.GroupAnchorNotFound = "group_anchor_not_found"`, `GroupAnchorInvalid = "group_anchor_invalid"`, `GroupNotFound = "group_not_found"`, `GroupOrderNr = "group_ordernr"`, `GroupRemoveConflict = "group_remove_conflict"`, mapped from 51017–51021 in `FromSqlErrorNumber`.
- Produces: `sm.PlanOrder` works on a per-(physicaltable, workercode) working set `#ord` with a computed/maintained **block key** (group path of the row's own group, or of the group the row is a child of in the after-plan state; NULL for plain rows) and processes "rows to place" in one pass. Task 2 adds moves to the same pass; Task 4 reads the same working set for `SPLIT_GROUP`.

Test setup: `PlanSandbox.CreateProcessAsync` table `{p}DocumentListLayout` = `Column=DOCNR` 10, `Column.DOCNR.Caption` 20, `Column=DATE` 30, `Column.DATE.Caption` 40, `Grid.PageSize` 50, `Grid.Secret` (ServerSetting) 60. Helpers as in `PlanOrderingTests` (`PlanAsync` dry run, `Row(...)`); add `Group(string value, string? after = null, int? orderNr = null)` → `Name = "Column", SettingType = "SettingGroup"`.

- [ ] **Step 1: Write the failing tests** in `PlanGroupPlacementTests`:

```csharp
[Fact] NewGroup_AfterGroup_PlacedAfterWholeBlock()
// Group("BARCODE", after: "Column=DOCNR"), Row("Column.BARCODE.Caption","Barcode")
// → Column/BARCODE ordernr 23, Column.BARCODE.Caption 26; no RENUMBER
[Fact] NewGroup_ChildrenListedFirst_SameResult()        // same two rows reversed → 23 / 26
[Fact] NewGroup_First_BeforeFirstBlockOfFamily()        // after "^" → 3 / 6
[Fact] NewGroup_NoAfter_AfterLastBlockOfFamily()        // no after → 43 / 46
[Fact] NewGroup_AnchorMissing_Refused()
// after "Column=NOPE" → SmException Code group_anchor_not_found; Message contains "NOPE" and p + "DocumentListLayout"
[Fact] NewGroup_AnchorNotAGroup_Refused()               // after "Grid.PageSize" → group_anchor_invalid
[Fact] NewGroup_AnchorDifferentCase_Found()             // after "Column=docnr" → 23 (Review Focus 1)
[Fact] TwoNewGroups_Chained()                           // A after Column=DOCNR → 25; B after Column=A → 27
[Fact] PlainRow_AnchorOnGroupRow_PlacedAfterBlock()     // Row("Zzz.X","1", after: "Column=DOCNR") → 25 (was 15)
[Fact] Child_ForeignAnchor_WarnsAndGoesToOwnBlockEnd()  // Row("Column.DATE.Width","1", after:"Column.DOCNR.Caption") → 45 + ANCHOR_OUTSIDE_GROUP
[Fact] Child_InBlockAnchor_Honoured()                   // Row("Column.DATE.Width","1", after:"Column=DATE") → 35
[Fact] Caret_OnPlainRow_InvalidDesired()                // Row("Zzz.X","1", after:"^") → invalid_desired
[Fact] ExistingGroup_WithAfter_NotMoved()
// Group("DATE", after:"Column=NOPE") → no exception, Items empty, warning GROUP_NOT_MOVED mentioning "DATE"
[Fact] GroupRow_ExplicitOrderNr_Refused()               // Group("DATE", orderNr: 5) → group_ordernr
[Fact] NewGroup_GapTooSmall_Renumbers()
// UPDATE Column.DOCNR.Caption ordernr 28; Group("BARCODE", after:"Column=DOCNR") + caption
// → RENUMBER; BARCODE 30, Column.BARCODE.Caption 40, Column/DATE update 30→50
[Fact] NewGroup_NextToTiedRows_DistinctOrderNr()
// UPDATE Column.DATE.Caption ordernr 50 (tie with Grid.PageSize); Group("X") no after
// → RENUMBER; X.ordernr > final Column.DATE.Caption and < final Grid.PageSize; all final ordernr distinct
[Fact] NewGroup_AllOrderNrZero_Renumbers()
// UPDATE every row ordernr 0; Group("BARCODE", after:"Column=DOCNR") → RENUMBER, all final ordernr distinct,
// order DOCNR, DOCNR.Caption, BARCODE, DATE... by the sort key (Review Focus 2)
[Fact] NameKeyedGroup_AfterGroup_PlacedAfterBlock()
// add rows Detail.Lines (SettingGroup,"x") 70, Detail.Lines.Caption 80; plan new Detail.Notes (SettingGroup,"y")
// after "Detail.Lines" + Detail.Notes.Caption → 90 / 100
```

Also in `SmErrorCodesTests`: 51017–51021 map to the five new codes.

- [ ] **Step 2: Run to verify they fail**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~PlanGroupPlacementTests|FullyQualifiedName~SmErrorCodesTests"`
Expected: FAIL (placements differ, no refusals, unknown codes).

- [ ] **Step 3: Implement**

`sm.CreatePlan` validation (51006 `invalid_desired`): `after = '^'` only on SettingGroup rows. `sm.PlanDiff`: an upsert of an existing group row carrying `ordernr` → Error `GROUP_ORDERNR`; an upsert of an existing group row carrying `after` → warning `GROUP_NOT_MOVED` (after not resolved). Error rows are thrown via the guardrail pattern with the numbers above.

`sm.PlanOrder` (rewrite), per (physicaltable, workercode):
1. Load `#ord` as today (current rows minus deletes, Inserts with ordernr, Inserts to place).
2. Order of placement: rows to place in `rowno` order, except that a child whose group row is itself a new row of this plan is placed right after that group row (group row first, then its children in rowno order).
3. Position for a **new group row**: `after` = `^` → before the first row of the earliest block of its family; `after` given → it must resolve (NAME or NAME=VALUE, last match by sort key) to a group row of the **same family** in the same table, else Error `GROUP_ANCHOR_NOT_FOUND` / `GROUP_ANCHOR_INVALID`; position = after that block's end; omitted → after the end of the last block of the family, else today's longest-prefix / append rule. The group row and its new children are placed together as one k-row unit in that gap (Global Constraints formula).
4. Position for a **new child** of an existing block: end of its block, or right after an in-block anchor; an anchor outside its block → warning `ANCHOR_OUTSIDE_GROUP`, end of block.
5. Position for a **new plain row**: today's rules; if the chosen spot is inside a block (between its group row and its end) the spot moves to after that block's end. An explicit `ordernr` on a plain row is left as is.
6. Renumbering, duplicate check and item updates as today.

- [ ] **Step 4: Run tests until they pass**, plus the whole ordering suite

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~PlanGroupPlacementTests|FullyQualifiedName~PlanOrderingTests|FullyQualifiedName~SmErrorCodesTests|FullyQualifiedName~SmScriptSetTests"`
Expected: PASS after `module.json` version 7 and the new `sha256`.

- [ ] **Step 5: Commit**

```bash
git add VTMF.Settings.Core VTMF.Settings.Tests
git commit -m "feat(sm): block-aware placement of SettingGroup rows (module v7)"
```

---

### Task 2: `move` operation for groups

**Files:**
- Modify: `129-procedure-CreatePlan.txt` (accept op `move`), `122-procedure-PlanDiff.txt`, `126-procedure-PlanOrder.txt`, `module.json` (sha256) — all under `VTMF.Settings.Core/Sql/settingsmanagement/`
- Create: `VTMF.Settings.Tests/Sm/PlanGroupMoveTests.cs`

**Interfaces:**
- Consumes: Task 1's single-pass placement and block key.
- Produces: desired row `op = "move"` (string, no C# model change): requires `settingtype = "SettingGroup"`, `after` (group of the same family or `^`), merge mode, not `ServerProcessSettings`; `value` required for value-keyed groups.

- [ ] **Step 1: Write the failing tests** (same sandbox; helper `Move(string value, string after)`):

```csharp
[Fact] Move_First()                     // Move("DATE","^") → Updates Column/DATE 30→3, Column.DATE.Caption 40→6, nothing else
[Fact] Move_AfterSibling()              // Move("DOCNR","Column=DATE") → 10→43, 20→46
[Fact] Move_ToOwnPosition_NoItems()     // Move("DATE","Column=DOCNR") → Items empty
[Fact] Move_AnchorInOwnBlock_Refused()  // Move("DATE","Column.DATE.Caption") → group_anchor_invalid
[Fact] Move_MissingGroup_Refused()      // Move("NOPE","^") → group_not_found
[Fact] Move_MissingAnchor_Refused()     // Move("DATE","Column=NOPE") → group_anchor_not_found
[Fact] Move_PlusChildValueChange_OneItem()
// Move("DATE","^") + Row("Column.DATE.Caption","Date2") → single Update for the caption: Value "Date2", OrderNr 6
[Fact] Move_ThenInsertAnchoredOnMovedGroup()   // Move("DATE","^") then Group("X", after:"Column=DATE") → X ordernr 8
[Fact] Move_NonGroupRow_InvalidDesired()       // op move on Grid.PageSize → invalid_desired
[Fact] Move_ReplaceMode_InvalidDesired()
[Fact] Move_ServerProcessSettings_InvalidDesired()
[Fact] Move_ApplyThenRollback_RestoresOrder()  // not dry run: apply, then CreateRollbackPlanAsync + apply → original ordernr
```

- [ ] **Step 2: Run to verify they fail**

Run: `pwsh -NoProfile -File ./eng/verify.ps1 -Mode test -Filter "FullyQualifiedName~PlanGroupMoveTests"`
Expected: FAIL (`unknown op`).

- [ ] **Step 3: Implement** — `sm.CreatePlan` validation for `move` (51006 messages naming the row); `sm.PlanDiff` creates no item for move rows; `sm.PlanOrder` treats a move as "take the block out, place it as a k-row unit" in the same rowno-ordered pass as inserts (anchor rules of Task 1 step 3; anchor in own block → `GROUP_ANCHOR_INVALID`; missing group → `GROUP_NOT_FOUND`), emitting ordernr-only Update items for the block rows, or setting `ordernr` on an existing Update item of the same identity. A block already at the target position produces nothing.

- [ ] **Step 4: Run tests until they pass** (also `PlanGroupPlacementTests`, `PlanOrderingTests`, `RollbackPlanTests`, `SmScriptSetTests` after the new sha256)

- [ ] **Step 5: Commit** — `feat(sm): move op moves a SettingGroup with its children`

---

### Task 3: Remove a group as a whole block (incl. lang tables)

**Files:**
- Modify: `120-procedure-PlanResolve.txt` (add lang scopes), `121-procedure-PlanLoadCurrent.txt` (if needed for the added scopes), `122-procedure-PlanDiff.txt`, `module.json` (sha256)
- Modify: `VTMF.Settings.Tests/Sm/PlanSandbox.cs` — add `CreateLayerTableAsync(SqlServerFixture f, string client, string physicalTable, string workercode, params (string Name, string Value, string SettingType, int OrderNr)[] rows)`
- Create: `VTMF.Settings.Tests/Sm/PlanGroupRemoveTests.cs`

**Interfaces:**
- Produces: for every `remove` of a group row in a base or env layer, `#scope` also holds every existing lang layer of the same view (from `sm.ProcessTables`), so `#current` has their rows; Delete items carry the lang `layer`.

- [ ] **Step 1: Write the failing tests**:

```csharp
[Fact] RemoveGroup_DeletesWholeBlock()
// remove Column=DOCNR → exactly two Delete items: Column/DOCNR and Column.DOCNR.Caption
[Fact] RemoveGroup_AlsoDeletesLangChildren()
// lang table {p}DocumentListLayouten (ALL): Column.DOCNR.Caption "Doc no" 10, Column.DATE.Caption "Date" 20
// remove base Column=DOCNR → Delete of the en Column.DOCNR.Caption (layer "lang:en"); en Column.DATE.Caption untouched
[Fact] RemoveGroup_KeepsLangChildren_WhenGroupRemainsInEnv()
// env table {p}DocumentListLayoutwt2 (workercode wt2) with Column=DOCNR → en row NOT deleted
[Fact] RemoveGroup_NoLangTable_Works()                  // Review Focus 4
[Fact] RemoveGroup_LangTableWithoutItsRows_Works()      // en table only has Column.DATE.Caption → only base Deletes
[Fact] RemoveGroup_FromLangTable_OnlyThatBlock()        // remove group row stored in lang table → only that table
[Fact] RemoveGroup_PlusChildUpsert_Refused()            // remove Column=DOCNR + Row("Column.DOCNR.Width","1") → group_remove_conflict
[Fact] RemoveGroup_PlusMove_Refused()                   // remove + Move same group → group_remove_conflict
[Fact] RemoveChild_OnlyThatRow()                        // remove Column.DOCNR.Caption → one Delete
[Fact] RemoveGroup_ApplyThenRollback_RestoresBlockAndLang()
```

- [ ] **Step 2: Run to verify they fail** — `-Filter "FullyQualifiedName~PlanGroupRemoveTests"`; Expected: FAIL.

- [ ] **Step 3: Implement** — `sm.PlanResolve` adds the lang scopes; `sm.PlanDiff` expands a group remove into Deletes for every row of its block in its own table and, for base/env removes, every row under the group path in the view's lang tables unless the group still exists after the plan in another base/env table of that view; `GROUP_REMOVE_CONFLICT` Error when any desired upsert or move targets the removed group or a row of its block. Replace mode unchanged.

- [ ] **Step 4: Run tests until they pass** (plus Tasks 1–2 suites, `CreatePlanTests`, `SmScriptSetTests`)

- [ ] **Step 5: Commit** — `feat(sm): removing a SettingGroup removes its block and lang children`

---

### Task 4: `SPLIT_GROUP` warning

**Files:**
- Modify: `126-procedure-PlanOrder.txt` or `122-procedure-PlanDiff.txt` (builder's call; must run whenever a table has Insert, ordernr Update, Delete or move items), `module.json` (sha256)
- Modify: `VTMF.Settings.Tests/Sm/PlanGroupPlacementTests.cs`

- [ ] **Step 1: Write the failing tests**:

```csharp
[Fact] SplitTable_Touched_WarnsSplitGroup()
// UPDATE Column.DOCNR.Caption ordernr 45; plan Row("Zzz.X","1") → one SPLIT_GROUP warning; message contains "DOCNR"
// and "renumber"
[Fact] TiedSiblingGroups_Touched_WarnsSplitGroup()      // UPDATE Column/DATE ordernr 10 (tie with DOCNR) → SPLIT_GROUP
[Fact] TidyTable_NoSplitGroup()                         // plan Row("Zzz.X","1") on the seed → no SPLIT_GROUP
```

- [ ] **Step 2: Run to verify they fail.** **Step 3: Implement** (one warning per table, names up to 5 groups by group path). **Step 4: Run until pass** (+ sha256). **Step 5: Commit** — `feat(sm): warn about split SettingGroups`

---

### Task 5: Renumber plan

**Files:**
- Create: `VTMF.Settings.Core/Sql/settingsmanagement/152-procedure-CreateRenumberPlan.txt`
- Modify: `module.json` (sha256)
- Modify: `VTMF.Settings.Core/Sm/SmClient.cs`
- Create: `VTMF.Settings.Tests/Sm/RenumberPlanTests.cs`

**Interfaces:**
- Produces: `sm.CreateRenumberPlan @process nvarchar(100), @table nvarchar(100), @layer nvarchar(110), @description nvarchar(400), @sessionid int = NULL, @dryrun bit = 0` → same three result sets as `sm.CreatePlan`; source `renumber`.
- Produces: `Task<SmPlanResult> SmClient.CreateRenumberPlanAsync(string client, string process, string table, string layer, string description, int? sessionId, bool dryRun = false, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing tests** (dry run unless stated):

```csharp
[Fact] TidyTable_NoChanges()                     // seed → Status "NoChanges" (or DryRun with no items), Items empty
[Fact] SplitBlock_Joined()                       // Column.DOCNR.Caption 45 → one Update 45→20
[Fact] ChildBeforeGroup_MovedToBlock()           // Column.DATE.Caption 5 → one Update 5→40
[Fact] Orphans_GoLast_AndWarn()
// INSERT Column.OLD.Caption ordernr 15 → its Update →70; ORPHAN_ROWS warning contains "Column.OLD.Caption"
[Fact] TiedSiblingGroups_OrderedByValue()
// Column/DATE ordernr 10 → final DATE 10, DATE.Caption 20, DOCNR 30, DOCNR.Caption 40, Grid.PageSize 50, Grid.Secret 60
[Fact] ServerSettingRow_ValuesNeverReturned()    // Grid.Secret item (if any) has null Value/OldValue
[Fact] Duplicates_Refused()                      // duplicate Grid.Secret row → duplicate_row
[Fact] ServerProcessSettings_Refused()           // table ServerProcessSettings → invalid_desired
[Fact] Apply_ThenRollback_RestoresOrder()        // not dry run; source "renumber" in GetPlanAsync
```

- [ ] **Step 2: Run to verify they fail** — `-Filter "FullyQualifiedName~RenumberPlanTests"`; Expected: compile error (method missing).

- [ ] **Step 3: Implement** — resolve the table like `sm.PlanResolve` does for one (table, layer) (reuse it with a one-row `#desired`/`#scope`, or `sm.ResolveLayer` + `sm.ProcessTables`); load `#current` via `sm.PlanLoadCurrent`; compute the final order per spec §6 (walk by sort key; group row emits its block; siblings tied on ordernr and NAME by VALUE; orphans last); ordernr 10, 20, …; Update items only for changed rows (ServerSetting rows as in `sm.PlanOrder`); `ORPHAN_ROWS` warning; `sm.PlanStore`. `SmClient.CreateRenumberPlanAsync` calls it with `QueryMultipleAsync(..., ReadPlanResultAsync, ...)`.

- [ ] **Step 4: Run tests until they pass** (+ sha256)

- [ ] **Step 5: Commit** — `feat(sm): renumber plan keeps SettingGroup blocks together`

---

### Task 6: MCP tools

**Files:**
- Modify: `VTMF.Settings.Mcp/Tools/SettingsPlanTools.cs`
- Modify: `VTMF.Settings.Tests/Mcp/SettingsPlanToolsTests.cs`

**Interfaces:**
- Consumes: `SmClient.CreateRenumberPlanAsync` (Task 5).
- Produces: tool `renumber_plan` → `Task<ToolResult<SmPlanResult>> RenumberPlan(string client, int sessionId, string process, string table, string layer, string? description, CancellationToken cancellationToken)` (`ReadOnly = false, Destructive = false`, like `create_plan`; default description `"Renumber {table} ({layer})"`).

- [ ] **Step 1: Write the failing tests**:

```csharp
[Fact] RenumberPlan_SplitTable_ViaTools()        // Column.DOCNR.Caption 45 → Ok, Status Created, one Update OrderNr 20
[Fact] CreatePlan_MoveGroup_ViaTools()           // op move Column=DATE after "^" → Ok, two Updates
[Fact] CreatePlan_MissingGroupAnchor_CodedError()// new group after Column=NOPE → Ok false, Error.Code "group_anchor_not_found"
```

- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the tool and update the `create_plan` description: groups (a SettingGroup row plus the rows under `NAME.VALUE.` / `Detail.X.`) are placed, moved and removed as one block; for a group row `after` names a sibling group (`Column=DOCNR`, `Detail.Lines`) or `^` for first, and a missing anchor refuses the plan; `op` adds `move` (group rows only, `after` required); `remove` of a group removes its block and its rows in lang tables; existing group rows are not moved by upsert (use move); no `ordernr` on group rows in merge mode. `renumber_plan` description: what it does (§6), that it returns a plan to preview and apply like `create_plan`.
- [ ] **Step 4: Run tests until they pass** — `-Filter "FullyQualifiedName~SettingsPlanToolsTests"`
- [ ] **Step 5: Commit** — `feat(mcp): renumber_plan tool, group rules in create_plan`

---

### Task 7: Feature `Position` parameter

**Files:**
- Modify: `VTMF.Settings.Core/Sql/settingsmetabase/010-script-featuretables.txt` (idempotent: if `CK_sm_featureparam_type` does not allow `Position`, drop and re-add it with `N'Text', N'Number', N'Boolean', N'List', N'Position'`), `VTMF.Settings.Core/Sql/settingsmetabase/module.json` (version 2, sha256)
- Modify: `VTMF.Settings.Core/Sm/Features/SmFeatureValidator.cs`, `SmFeatureExpander.cs`, `SmFeaturePlanner.cs`
- Modify: `VTMF.Settings.Core/Sm/SmErrorCodes.cs` (`FeatureNeedsPosition = "feature_needs_position"`)
- Modify: `VTMF.Settings.Mcp/Tools/SettingsFeatureTools.cs` (descriptions of `save_feature`, `set_feature_status`, `feature_plan`)
- Test: `VTMF.Settings.Tests/Features/SmFeatureValidatorTests.cs`, `VTMF.Settings.Tests/Features/SmFeatureExpanderTests.cs`, `VTMF.Settings.Tests/Sm/FeaturePlannerTests.cs`, `VTMF.Settings.Tests/Sm/FeatureStoreTests.cs`

**Interfaces:**
- Consumes: plan-engine anchors and codes (Tasks 1–3).
- Produces: `SmFeatureValidator` public API unchanged; new finish rule reused by `SmFeaturePlanner` (expose `internal static IReadOnlyList<string> SmFeatureValidator.PositionErrors(SmFeature feature)` or equivalent) to throw `feature_needs_position`.

- [ ] **Step 1: Write the failing tests**:

Validator:
```csharp
[Fact] Save_PositionParam_UsedOutsideGroupAfter_IsError()   // {{pos}} in a Setting row value → error mentions "pos"
[Fact] Save_PositionParam_WithDefaultOrValues_IsError()
[Fact] Finish_GroupSetRowWithoutAfter_IsError()             // error text contains "position"
[Fact] Finish_GroupSetRowWithPositionParam_IsValid()        // after "{{pos}}", pos type Position
[Fact] Finish_GroupChainedToSameFeatureGroup_IsValid()      // second group row after "Column={{name}}" (first row is Column={{name}})
[Fact] Finish_GroupRemoveRow_NeedsNoPosition()
```
Expander:
```csharp
[Fact] Expand_PositionFirst_IsCaret()          // answer "First" → After "^"
[Fact] Expand_PositionValueKeyed()             // row Column={{name}}, answer "DOCNR" → After "Column=DOCNR"
[Fact] Expand_PositionNameKeyed()              // row Detail.{{name}}, answer "Lines" → After "Detail.Lines"
[Fact] Expand_PositionTrimmed()                // answer " DOCNR " → "Column=DOCNR" (Review Focus 3)
[Fact] Expand_PositionEmpty_IsMissing()        // "" → feature_param_missing
```
Integration:
```csharp
[Fact] SaveFeature_PositionParam_RoundTrips()                     // FeatureStoreTests: save + get keeps Type "Position"
[Fact] FeaturePlan_Position_PlacesColumnBlock()                   // answer DOCNR → group 23, caption 26
[Fact] FeaturePlan_PositionMissingColumn_Refused()                // answer NOPE → group_anchor_not_found
[Fact] FeaturePlan_Reapplied_GroupNotMoved()                      // column already exists, answer NOPE → no throw, GROUP_NOT_MOVED (Review Focus 5)
[Fact] FeaturePlan_OldFinishedFeatureWithoutPosition_Refused()    // feature saved as Draft, status set to Finished via SQL → feature_needs_position
```

- [ ] **Step 2: Run to verify they fail** — `-Filter "FullyQualifiedName~SmFeatureValidatorTests|FullyQualifiedName~SmFeatureExpanderTests|FullyQualifiedName~FeaturePlannerTests|FullyQualifiedName~FeatureStoreTests"`

- [ ] **Step 3: Implement**
  - Validator: `ParamTypes` adds `Position`; save rules: Position param only as the whole `after` (`{{p}}`) of `set` + `SettingGroup` rows, no default, no values; finish rule: each `set` group row has `after` that is a Position param or `NAME=VALUE`/`NAME` matching another group row of the same feature (compare unsubstituted text), message "Row N adds a group; ask the consultant where it goes (add a Position parameter as its after)".
  - Expander: Position answer trimmed; empty → missing; `first` (case-insensitive) → `^`; otherwise for a row whose substituted name starts with `Detail.` → family (name without last segment) + `.` + answer, else substituted name + `=` + answer. Only `after` values that are exactly one Position placeholder are translated.
  - Planner: before expansion, throw `SmException(SmErrorCodes.FeatureNeedsPosition, "Feature '{name}' adds a group without asking where it goes; reopen it with create-feature and add the position question.")` when the finish position rule fails.
  - Metabase constraint change + version 2 + sha256.
  - Tool descriptions: Position type (the question is "after which column … (or first)"; answer is the column name or `first`), the finish rule, `feature_needs_position`.

- [ ] **Step 4: Run tests until they pass** (+ `SmScriptSetTests`, `SmMetabaseModuleTests`, `SettingsFeatureToolsTests`)

- [ ] **Step 5: Commit** — `feat(features): Position parameter for SettingGroup placement (metabase v2)`

---

### Task 8: Skills, README, plugin version

**Files:**
- Modify: `plugins/vtmf-settings/skills/manage-settings/SKILL.md`, `plugins/vtmf-settings/skills/create-feature/SKILL.md`, `plugins/vtmf-settings/skills/apply-feature/SKILL.md`
- Modify: `plugins/vtmf-settings/.claude-plugin/plugin.json` (1.3.0)
- Modify: `README.md` (root)

- [ ] **Step 1: `manage-settings`** — per spec §11: group = block; add after X / first; move (`op: "move"`); remove whole block incl. translations (say how many rows); "tidy / renumber this table" → `renumber_plan`; offer columns from the **same layer** via `get_settings` (group rows of the family), env-layer note; preview shows a move as one line "Move column X after Y"; plain-word texts for `GROUP_ANCHOR_NOT_FOUND`, `GROUP_ANCHOR_INVALID`, `GROUP_NOT_FOUND`, `GROUP_ORDERNR`, `GROUP_REMOVE_CONFLICT`, `GROUP_NOT_MOVED`, `ANCHOR_OUTSIDE_GROUP`, `SPLIT_GROUP` (offer to tidy first), `ORPHAN_ROWS`. Update the `after` rule line (`manage-settings/SKILL.md:71`).
- [ ] **Step 2: `create-feature`** — when a `set` row has `settingtype: "SettingGroup"`, add a Position param as its `after` automatically and propose the question; children need no `after`; several new groups: chain or ask each; from a session, inserted group rows get a Position param and ordernr-only moves are mentioned, not recorded; remove any wording that says placement cannot be stored.
- [ ] **Step 3: `apply-feature`** — Position param: `get_settings` on the target process, the group row's table kind and layer; offer existing groups of the family plus "first"; answer with the column name or `first`; on `group_anchor_not_found` explain and re-ask; on `feature_needs_position` say the feature must be fixed with `create-feature`; `GROUP_NOT_MOVED` → "the column already exists; it stays where it is (I can move it with manage-settings)".
- [ ] **Step 4: README** — `renumber_plan` in the tools list; sample prompts "add the barcode column after the document number", "move the date column first", "remove the old price column", "tidy the layout table".
- [ ] **Step 5: Verify** — `pwsh -NoProfile -File ./eng/verify.ps1 -Mode build` passes; manual checklist: each sample prompt maps to the expected tool sequence in the skill text.
- [ ] **Step 6: Commit** — `docs(skills): SettingGroup placement, move, remove, renumber (plugin 1.3.0)`

---

### Task 9: Gate

- [ ] **Step 1:** `pwsh -NoProfile -File ./eng/verify.ps1 -Mode gate` → passes.
- [ ] **Step 2:** If any existing test expectation was changed in Task 1–3, confirm each is listed in a commit message with the spec rule that changed it.
- [ ] **Step 3:** Commit any fixes — `chore: gate fixes`.

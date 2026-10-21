## 2026-10-17 — Settings management phase 1.1: sm module install/upgrade via MCP

> We finished the phase 1 from @docs\superpowers\plans\2026-10-16-settings-management-phase1.md
> Before going to phase 2, we need to add the phase 1.1 which would install the settingsmanagement schema when using the MCP server. Currently the even the settingmanager UI (VTMF.Settings) cannot install it because the default @..\VTMF.SQLInstaller\config\install.xml packaged to Docker image does not contain these. We need to create a mechanism to install or update the schema using either the same MCP or some other way. Likely the first install would be simple, but we need also to update it. Brainstorm it

Spec: `docs/superpowers/specs/2026-10-17-settings-management-phase1-1-module-install-design.md`; plan: `docs/superpowers/plans/2026-10-17-settings-management-phase1-1-module-install.md`.

> rewrite the @README.md and split it to 2 files : README.md to the root of the repo containing only instructions how to run it, step by step: 1) create docker compose stack (important - check the correct latest image tags) 2) set up environment file 3) run the docker compose stack from commandline with the environment file parameter 4) how to use optional MCP server - install plugin 5) sample prompts how the new skill and mcp would be activated and how to check it works
> Move the development part (build) to the VTMF.Settings\README.md

## 2026-10-19 — Settings management phase 2: plan engine, sessions, manage-settings skill

> We worked on the spec @VTMF.Settings/docs/superpowers/specs/2026-10-16-settings-management-tools-design.md
> Do I understand correctly that the 5.2 plan engine works with the items described in desired state 4.5?

> You mean when the new setting table like spaorderassemblingDocumentNewLayout is needed, it is not a plan item but a createTable parameter?

> The rollback of the CreateTable plan should be drop the table with creating the backup. Creating the backups is the feature Settingmanager has currently and similar can be used. Lets brainstorm the phase 2 and create a plan for the phase 2, plan engine. The results should also contain besides updated MCP server:
> 1. Updated skill. We need to brainstorm this. The updated skill should contain of course the knowledge how the settings should be managed. But the skill should also contain the knowledge how to understand how to create the plan to apply the feature which the user may describe vaguely or ambigously. Creating and applying the plan may be building the needed changed in settings one by one, where user names the settings to be changed or discovering the feature from the metabase which already describes how to apply some feature. So we may likely need a phase 2.1, preparation. And likely the separate skill to define and store features. The features may be created interactively, by the user pointing the settings which work together and which values implement some feature. For example the feature is how the barcode is handled. Or the feature may be configuring a new column in the UI. I have created features so far manually using the tables settingssession and settingslog which are created by Settingmanager in every customer table. The settingssession allows to start the setting management session giving it some name like "configuring the new column in document view" and then settingslog can associate changed settings with the session. I can then inspect the log and pick settings which make up a new feature.
> 2. Instructions in @README.md how to update the skill (if not already there)
> 3. Instructions in @README.md how to create the plan (with the skill). The user usually do not know that there must be a plan and does not start the work with the prompt "I need a plan for .." The user uses a prompt to find settings and then wants to modify settings.

> Yes, approved

> Yes, reviewed the plan. Run subagent-driven

Spec: `docs/superpowers/specs/2026-10-19-settings-management-phase2-plan-engine-design.md`; plan: `docs/superpowers/plans/2026-10-19-settings-management-phase2-plan-engine.md`.

## 2026-10-20 — Settings management phase 3: features

> We finished the work on phase 2 from 2026-10-16-settings-management-tools-design.md. The next is the features, phase 3. It is partially described. Please check first the work we did recently about the features. I remember we described the feature management and feature management skill

> I create a new branch phase3-features. Lets brainstorm the phase3. In addition to what you listed: 1. Features should live in the metabase, because then these would be available to be used on any customer 2. The feature creation should be as agreed a separate skill. It should guide creation of a feature from the session but also the feature creation can be initiated separately. 3. The current settingfeatures will stay for now for the Settingmanager UI, but we will move these in the Phase 4 (update the phase 4 too)

> Feature creation process: the feature is first implemented in code; then describe the feature without naming it (what it enables for the user, e.g. "Allows adding the barcode to the article on the product card (sparelocateitems)"), store it immediately in the metabase as not finished and keep unfinished features visible; name the settings to add/change/remove iteratively with described value options; manual steps / non-settings records as guidance; the apply skill interviews the consultant, asks option values, creates the plan and guides post-apply actions.

> The feature becomes usable only when marked finished. (Example of records in another table: dcquerysettings / Directo hooks for the relocateitems button.)

> No extra step for settings-like tables. It makes things very fragile. There should be just a guidance what to do as post-apply ... Claude Code could just generate the SQL and the consultant may apply it.

> Consultants refer to a customer where the feature works ("add the similar button ... as it is on customer Y in process sparelocateitems"); customers ask "could it be possible to add the barcode to the article which yet does not have it".

> Features apply to any similar table (spaorderassemblingDocumentLayout → spaincomeorderDocumentLayout); only ServerProcessSettings need support in the process code.

> No versioning. If something was wrong or needs update, then the feature must be updated.

> One choice can set several dependent settings (Function.Barcode.Notfound.Action OptionsQuestion / Function.OptionsQuestion.Notfound.ItemType Option / Function.Barcode.Notfound.OptionsQuestion.Procedure RowSelectionOptionsQuestion) — use placeholders; complex cases are described in free form, not formalized in code.

> Values are often booleans (True/False as text), types are supported; placeholders like {{name}} can be used in setting names and values and are prompted from the consultant (e.g. DocumentLayout Column SettingGroup with {{name}}).

> Lets have feature sets as separate stage

> Testing drafts: (b). The author tests through manage-settings

> Reviewed the design document, proceed

> I reviewed the plan, run it subagent-driven

Spec: `docs/superpowers/specs/2026-10-20-settings-management-phase3-features-design.md`; plan: `docs/superpowers/plans/2026-10-20-settings-management-phase3-features.md`.

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

## 2026-10-17 — Settings management phase 1.1: sm module install/upgrade via MCP

> We finished the phase 1 from @docs\superpowers\plans\2026-10-16-settings-management-phase1.md
> Before going to phase 2, we need to add the phase 1.1 which would install the settingsmanagement schema when using the MCP server. Currently the even the settingmanager UI (VTMF.Settings) cannot install it because the default @..\VTMF.SQLInstaller\config\install.xml packaged to Docker image does not contain these. We need to create a mechanism to install or update the schema using either the same MCP or some other way. Likely the first install would be simple, but we need also to update it. Brainstorm it

Spec: `docs/superpowers/specs/2026-10-17-settings-management-phase1-1-module-install-design.md`; plan: `docs/superpowers/plans/2026-10-17-settings-management-phase1-1-module-install.md`.

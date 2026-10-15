# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Tasks

Before starting work on the task check for ambiguities and interview me!
After completing a task, write the task content as-is to the end of TASKHISTORY.md file 

## Project Overview

VTMF.Settings is a centralized settings management Blazor Server application built on ASP.NET Core 9.0. It manages Name-Value pair settings stored in MS SQL Server databases across multiple clients.

## Build & Run Commands

```bash
# Build
dotnet build VTMF.Settings.csproj

# Run (development, HTTP on port 5045)
dotnet run --launch-profile http

# Run (development, HTTPS on port 7188)
dotnet run --launch-profile https

# Docker build & run
docker build --tag vtmf/settingsmanager:v1.0 .
docker compose up -d
```

No test project exists in the solution.

## Architecture

- **Blazor Server** with Interactive Server rendering (`@rendermode InteractiveServer`)
- **Dapper** for data access (raw SQL, no Entity Framework)
- **Multi-client support**: each client has its own SQL Server connection string configured via `SqlConnectionParameters` (environment variables or appsettings)
- **Separate metadata database**: usually shared across clients, stores feature/process definitions

### Key Layers

- `Components/Pages/` — Blazor page components (routing entry points)
- `Components/SettingsManagement/` — Reusable sub-components with `.razor.cs` code-behind
- `Services/` — Business logic and data access, registered as singletons via `AddSettingsServices()` in `ServiceCollectionExtensions.cs`
- `Entities/` — Data models mapped by Dapper
- `Models/` — UI/display models (e.g., `SettingTreeNode` for hierarchical tree rendering)

### Domain Model

- **Setting**: Name-Value pairs with hierarchical dot-separated naming (e.g., `Email.SMTP.Server`). Dot separator creates tree structure in UI.
- **SettingFeature**: Groups multiple settings into a single feature that can be applied atomically.
- **SettingFeatureValue**: Maps setting names to values within a feature.
- **SettingFeatureGroup**: Groups multiple features for batch application.
- **ProcessType**: Defines processes where settings/features are applied.

## Code Conventions

- C# 12+ with nullable reference types enabled
- Primary constructors for service classes (e.g., `public class SettingsService(SqlConnectionProvider connectionProvider)`)
- Raw string literals for multi-line SQL queries
- Interface + implementation pattern: `IXxxService` / `XxxService`
- UI components: BlazorBootstrap library with Bootstrap 5.3 styling and Bootstrap Icons/Unicons

## Configuration

Database connections are configured via environment variables following the pattern:
```
SQLCONNECTIONPARAMETERS__0__CLIENTNAME=ClientName
SQLCONNECTIONPARAMETERS__0__SERVER=Server=tcp:...
SQLCONNECTIONPARAMETERS__0__USERID=user
SQLCONNECTIONPARAMETERS__0__PASSWORD=pass
```

Development uses User Secrets. See `settings.env` for the environment variable template.

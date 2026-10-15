# Summary

This is a Settings management Blazor Server application. Settings are Name-Value pairs stored in MS SQL database tables.
Settings are used by other application to configure their behavior. The application can manage settings for multiple Clients. 
There are connection strings defined for each Client. There is a separate metadata database. Each Client can have its own metadata database defined, but usually there is just one. 

## Entities

- Setting - Name-Value pairs. Naming convention is hierarchical using dot (.) separator. Example: "Email.SMTP.Server". Settings with same prefix are grouped together and dot separator creates a tree structure.
- Client - Represents a client application. Each Client can have its own connection string and metadata database
- SettingFeature - to handle group of settings as one feature. Example: "Email" feature can have multiple settings like "Email.SMTP.Server", "Email.SMTP.Port", "Email.SMTP.Username", etc. The purpose is possibility to apply multiple setting values at once.
- SettingFeatureValue - lists setting names and values which are added/updated when a SettingFeature is applied.
- SettingFeatureGroup - to group multiple SettingFeatures together. Example: "Default Email Settings" group can have multiple features like "Email", "EmailTemplates", etc. The purpose is possibility to apply multiple features at once.
- ProcessType - the process where settings are applied. Example: "UserRegistration", "PasswordReset", etc.

## UI Design requirements

- UI components: BlazorBootstrap 
- Design: Bootstrap 5.3
- Icons: Bootstrap Icons and Unicons

# SqlInstaller to install MS SQL Server tables and stored procedures to the customer database

## Overview

The purpose of this container is to install the required tables and stored procedures to the customer database. 
The installation scripts are located in the `/data` directory in the container (but configurable with the SQLSCRIPTS_DIR environment variable). 
The installation order is defined by the install.xml file in the `/data` directory.
The installer uses Dapper to interact with the database.

## Architecture

The application follows a clean architecture with the following components:

### Core Components
- **Program.cs** - Entry point, configures services and orchestrates execution
- **InstallationOrchestrator** - Main orchestrator that coordinates the installation process
- **DatabaseInstaller** - Handles database script installations
- **FileInstaller** - Handles file-based installations
- **DatabaseService** - Wrapper for Dapper database operations
- **SqlScriptParser** - Parses SQL scripts (version extraction, GO batch splitting)
- **XmlInstallParser** - Parses install.xml configuration

### Data Models
- **InstallConfig** - Configuration from environment variables
- **Install** - Root installation configuration
- **InstallSet** - A set of installation items
- **InstallItem** - Individual installation item
- **InstalledItem** - Record of installed item

## Configuration

The application is configured via environment variables:

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `SQLSCRIPTS_DIR` | Directory containing SQL scripts and install.xml | `/data` | No |
| `ConnectionString` | SQL Server connection string | - | Yes |

### Example Configuration (.env file)
```bash
SQLSCRIPTS_DIR=/data
ConnectionString=Server=localhost;Database=YourDatabase;User Id=sa;Password=YourPassword;TrustServerCertificate=True
```

## Process of installation

### Creation of customer database

The customer database must be created before installing scripts. 

1. InstallationOrchestrator checks if the database exists using DatabaseInstaller.DatabaseIsCreated method.
1. If the database does not exist, the DatabaseCreator class is used to create the database. 

### Installing scripts to customer database

Scripts can be installed to the customer database multiple times, because scripts can be versioned. Every time the installer is run, it checks if new versions of the scripts are available and installs them if needed.

1. The console application `VTMF.SQLInstaller` is started in the container.
1. The application is configured by binding the InstallConfig class to configuration. Configuration is read from environment variables.
1. The application connects to the customer database using the provided connection string from InstallConfig.ConnectionString.
1. The application reads the install.xml file and parses it to Install class to determine the installation order. The install.xml structure is:
   - file root element is `install`. 
   - there can be multiple `set` elements inside the `install` element. `set` elements are mapped to InstallSet records and added to Install.Sets collection.
   - each `set` element can have multiple `item` elements. `item` elements are mapped to InstallItem records and added to InstallSet.Items collection.
1. The Install.Sets collection is iterated in order. For each InstallSet, its InstallSet.Items collection is iterated in order. The installation behavior is determined by the InstallItem.Type property:
- If InstallItem.Type is `database`, then the DatabaseInstaller is used to check and install items. IMPORTANT! The sql script may contain multiple SQL blocks separated by `GO` statements. Each block is executed separately.
   - If InstallItem.Type is `file` or `settings`, the FileInstaller is used to check and install items.
1. The DatabaseInstaller behavior is determined by the InstallItem.IsFolder property and InstallItem.Check property:
   - If InstallItem.IsFolder is `true`, the InstallItem.Source is a directory containing multiple SQL scripts. Each script is checked and installed in alphabetical order.
   - If InstallItem.IsFolder is `false` the InstallItem.Source is a single SQL script. The script is checked and installed.
   - If InstallItem.Check is `file` then the existence of the file in `/data/installhistory`, same name as installable file is checked. If the file exists, the item is considered installed.
   - If InstallItem.Check is `database` then the SQL query using sql procedure `SetupCheckInstall` is executed to check if the item is installed. The procedure requires additionally the version parameter. The version parameter is read from the script file from the first line. There must be a string `-- Version: <version>` in the first line.
1. If the InstallItem.Check is `database` and item installation is successful, the installation history is created using procedure `SetupRegisterInstallation` 

### SQL Script Requirements

#### Version Header (for database check)
SQL scripts using `check="database"` must include a version header on the first line:
```sql
-- Version: 1
```

#### GO Statements
Scripts can contain multiple batches separated by `GO` statements. Each batch is executed separately:
```sql
-- Version: 1
CREATE TABLE MyTable (ID int)
GO

INSERT INTO MyTable VALUES (1)
GO
```

#### Path Mapping
TFS-style paths (`$/...`) are automatically converted to filesystem paths by removing the `$/` prefix:
- `$/Sawmill/database5/init` ? `/data/Sawmill/database5/init`

Optionally, path prefixes which should be removed can be configured in the InstallConfig class property SqlScriptsDir


### Source code of the SetupCheckInstall and SetupRegisterInstallation procedures

```sql
CREATE procedure [dbo].[SetupCheckInstall]
	@itemname nvarchar(200)
AS 
BEGIN
	select [version] from dbo.install where itemname = @itemname
END
```
```sql
CREATE procedure [dbo].[SetupRegisterInstallation]
	@itemname nvarchar(200),
	@itemtype nvarchar(20),
	@version int,
	@source nvarchar(1000) = null,
	@target nvarchar(1000) = null,
	@setname nvarchar(50) = null
AS 
BEGIN
	if not exists(select * from dbo.install where itemname=@itemname)
	begin
		insert into dbo.install (itemname,itemtype,[source],[target],setname,version)
		values (@itemname,@itemtype,@source,@target,@setname,@version)
	end
	else
	begin
		update dbo.install set version=@version,source=@source,setname=@setname,installtime=getdate() where itemname=@itemname
	end
END
```

## Sample Usage

### Sample install.xml
```xml
<install>
  <set name="initialization">
    <item name="init" check="file" isfolder="true" type="database" source="sample-scripts"/>
  </set>
  
  <set name="core">
    <item name="users" check="database" isfolder="true" type="database" source="$/database/users"/>
    <item name="config" check="database" isfolder="false" type="database" source="$/database/config.sql"/>
  </set>
</install>
```

### Sample SQL Script
```sql
-- Version: 1
-- Create users table

CREATE TABLE [dbo].[Users](
    [ID] [int] IDENTITY(1,1) NOT NULL,
    [Username] nvarchar(100) NOT NULL,
    [Email] nvarchar(200) NULL,
PRIMARY KEY ([ID])
)
GO

CREATE INDEX IX_Users_Username ON [dbo].[Users](Username)
GO
```

## Building the images (for debugging)

1. If there is no cdoc-encryptor.jar then get the cdoc-encryptor.jar from the cdoc-encryptor container. The cdoc-encryptor container has to be built in the cdoc-encryptor directory first. To copy the jar to the host system (output directory must exist!):
```bash
docker build --progress=plain -t cdoc-encryptor .
docker run -it -d -v ./output:/output --rm --entrypoint sh cdoc-encryptor -c "cp /app/cdoc-encryptor.jar /output && sleep 5"
docker rm cdoc-encryptor
```


2. Copy the cdoc-encryptor.jar from the output directory to the sqlinstaller/cdoc-encryptor directory. Build the sqlinstaller image:
```bash
docker build --progress=plain --build-arg FINAL_BASE_IMAGE=eclipse-temurin:21-jre-jammy -t vtmf/sqlinstaller .
```

### Local development with Docker Compose
There is a docker-compose file in the repository root which can be used for local development.
The command assumes services are already built, there is no build step in the docker-compose.yaml.
```bash
docker compose --env-file VTMF.SQLInstaller/.env up -d
```
Then exec into the sqlinstaller container:
```bash
docker exec -it sqlinstaller bash
```

## Building the images (for production)

Build the final image with Native AOT compilation:
```bash
docker build --progress=plain --build-arg FINAL_BASE_IMAGE=eclipse-temurin:21-jre-jammy -t vtmf/sqlinstaller .
```
Build and push to Docker Hub:
```bash
docker build --progress=plain --build-arg FINAL_BASE_IMAGE=eclipse-temurin:21-jre-jammy -t vtmf/sqlinstaller:v2 --attest type=sbom,generator=docker/scout-sbom-indexer:latest --pull --push .
```

## Building nupkg package
To build the nupkg package, use the following command in the project root directory:
```bash
dotnet pack -c Release -p:PackageVersion=1.0.14 -o ../nupkg
```
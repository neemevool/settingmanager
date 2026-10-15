using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using VTMF.SQLInstaller.Services;

namespace VTMF.SQLInstaller.Installers;

/// <summary>
/// Creates the database if it does not already exist.
/// </summary>
public class DatabaseCreator
{
    private readonly DatabaseService _databaseService;
    private readonly ILogger<DatabaseCreator> _logger;

    public DatabaseCreator(DatabaseService databaseService, ILogger<DatabaseCreator> logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new database with the specified name.
    /// </summary>
    /// <param name="databaseName">The name of the database to create</param>
    /// <returns>True if database was created successfully, false if it already exists</returns>
    public async Task<bool> CreateDatabaseAsync(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name cannot be null or empty", nameof(databaseName));
        }

        _logger.LogInformation("Attempting to create database: {DatabaseName}", databaseName);

        try
        {
            // Get the creation script template with the database name
            var createScript = GenerateDatabaseCreationScript(databaseName);
            foreach (var (sql, dbName) in createScript)
            {
                _logger.LogDebug("Executing script for database '{DatabaseName}': {Sql}", dbName, sql);
                // Execute each part of the script in the appropriate database context
                await _databaseService.ExecuteDatabaseCreationScriptAsync(sql, dbName);
                
            }
            
            
            _logger.LogInformation("Database '{DatabaseName}' created successfully", databaseName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create database '{DatabaseName}'", databaseName);
            throw;
        }
    }

    /// <summary>
    /// Generates the database creation script with the specified database name.
    /// This script is based on the template in deployment\tfs\sql\002-createdatabase.sql
    /// </summary>
    private static IEnumerable<(string sql,string databaseName)> GenerateDatabaseCreationScript(string databaseName)
    {
        var escapedDatabaseName = DatabaseUtils.EscapeSqlString(databaseName);
        yield return ($"""
        CREATE DATABASE [{escapedDatabaseName}] CONTAINMENT = NONE ON PRIMARY 
            ( NAME = N'{escapedDatabaseName}', FILENAME = N'/var/opt/mssql/data/{escapedDatabaseName}.mdf' , SIZE = 1024000KB , FILEGROWTH = 65536KB )
            LOG ON 
            ( NAME = N'{escapedDatabaseName}_log', FILENAME = N'/var/opt/mssql/data/{escapedDatabaseName}_log.ldf' , SIZE = 204800KB , FILEGROWTH = 65536KB )
        """, "master");

        yield return ($"""
            ALTER DATABASE [{escapedDatabaseName}] SET COMPATIBILITY_LEVEL = 150
            ALTER DATABASE [{escapedDatabaseName}] SET ANSI_NULL_DEFAULT OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET ANSI_NULLS OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET ANSI_PADDING OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET ANSI_WARNINGS OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET ARITHABORT OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET AUTO_CLOSE OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET AUTO_SHRINK OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET AUTO_CREATE_STATISTICS ON(INCREMENTAL = OFF)
            ALTER DATABASE [{escapedDatabaseName}] SET AUTO_UPDATE_STATISTICS ON 
            ALTER DATABASE [{escapedDatabaseName}] SET CURSOR_CLOSE_ON_COMMIT OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET CURSOR_DEFAULT  GLOBAL 
            ALTER DATABASE [{escapedDatabaseName}] SET CONCAT_NULL_YIELDS_NULL OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET NUMERIC_ROUNDABORT OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET QUOTED_IDENTIFIER OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET RECURSIVE_TRIGGERS OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET DISABLE_BROKER 
            ALTER DATABASE [{escapedDatabaseName}] SET AUTO_UPDATE_STATISTICS_ASYNC OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET DATE_CORRELATION_OPTIMIZATION OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET PARAMETERIZATION SIMPLE 
            ALTER DATABASE [{escapedDatabaseName}] SET READ_COMMITTED_SNAPSHOT OFF 
            ALTER DATABASE [{escapedDatabaseName}] SET READ_WRITE 
            ALTER DATABASE [{escapedDatabaseName}] SET RECOVERY SIMPLE 
            ALTER DATABASE [{escapedDatabaseName}] SET MULTI_USER 
            ALTER DATABASE [{escapedDatabaseName}] SET PAGE_VERIFY CHECKSUM  
            ALTER DATABASE [{escapedDatabaseName}] SET TARGET_RECOVERY_TIME = 60 SECONDS 
            ALTER DATABASE [{escapedDatabaseName}] SET DELAYED_DURABILITY = DISABLED
            ALTER DATABASE [{escapedDatabaseName}] SET READ_COMMITTED_SNAPSHOT ON
            """, "master");

        yield return ($"""
            ALTER DATABASE SCOPED CONFIGURATION SET LEGACY_CARDINALITY_ESTIMATION = Off;
            ALTER DATABASE SCOPED CONFIGURATION FOR SECONDARY SET LEGACY_CARDINALITY_ESTIMATION = Primary;
            ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 0;
            ALTER DATABASE SCOPED CONFIGURATION FOR SECONDARY SET MAXDOP = PRIMARY;
            ALTER DATABASE SCOPED CONFIGURATION SET PARAMETER_SNIFFING = On;
            ALTER DATABASE SCOPED CONFIGURATION FOR SECONDARY SET PARAMETER_SNIFFING = Primary;
            ALTER DATABASE SCOPED CONFIGURATION SET QUERY_OPTIMIZER_HOTFIXES = Off;
            ALTER DATABASE SCOPED CONFIGURATION FOR SECONDARY SET QUERY_OPTIMIZER_HOTFIXES = Primary;
            """, escapedDatabaseName);

        yield return ($"""
            IF DATABASE_PRINCIPAL_ID('worker') IS NULL
            begin
            create role [worker]
            end
            IF DATABASE_PRINCIPAL_ID('printservice') IS NULL
            begin
            create role [printservice]
            end
            IF DATABASE_PRINCIPAL_ID('emailservice') IS NULL
            begin
            create role [emailservice]
            end
            """, escapedDatabaseName);


    }

    
}

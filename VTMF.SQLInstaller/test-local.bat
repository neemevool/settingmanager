@echo off
REM Local test script for SQL Installer (Windows)

echo === VTMF SQL Installer - Local Test ===
echo.

REM Check if .env file exists
if not exist .env (
    echo ? .env file not found!
    echo Please create .env file from .env.example and configure your settings.
    exit /b 1
)

echo ? .env file found

REM Load environment variables from .env
for /f "usebackq tokens=1,* delims==" %%a in (".env") do (
    set "%%a=%%b"
)

echo ? Environment variables loaded

REM Check if ConnectionString is set
if "%ConnectionString%"=="" (
    echo ? ConnectionString not set in .env file!
    exit /b 1
)

echo ? ConnectionString configured

REM Set default SQLSCRIPTS_DIR if not set
if "%SQLSCRIPTS_DIR%"=="" (
    set "SQLSCRIPTS_DIR=./config"
    echo ? Using default SQLSCRIPTS_DIR: ./config
) else (
    echo ? SQLSCRIPTS_DIR: %SQLSCRIPTS_DIR%
)

REM Check if install.xml exists
set "INSTALL_XML=%SQLSCRIPTS_DIR%/install.xml"
if not exist "%INSTALL_XML%" (
    REM Try sample-install.xml
    set "SAMPLE_INSTALL=%SQLSCRIPTS_DIR%/sample-install.xml"
    if exist "%SAMPLE_INSTALL%" (
        echo ? install.xml not found, using sample-install.xml
        copy "%SAMPLE_INSTALL%" "%INSTALL_XML%" >nul
    ) else (
        echo ? install.xml not found in %SQLSCRIPTS_DIR%!
        exit /b 1
    )
)

echo ? install.xml found
echo.
echo === Starting SQL Installer ===
echo.

REM Build
dotnet build
if errorlevel 1 (
    echo ? Build failed!
    exit /b 1
)

REM Run
dotnet run

if errorlevel 1 (
    echo.
echo ? SQL Installer failed!
    exit /b 1
) else (
    echo.
    echo ? SQL Installer completed successfully!
    exit /b 0
)

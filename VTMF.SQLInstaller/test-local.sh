#!/bin/bash
# Local test script for SQL Installer

echo "=== VTMF SQL Installer - Local Test ==="
echo ""

# Check if .env file exists
if [ ! -f .env ]; then
    echo "? .env file not found!"
    echo "Please create .env file from .env.example and configure your settings."
    exit 1
fi

# Load environment variables
set -a
source .env
set +a

echo "? Environment variables loaded"

# Check if ConnectionString is set
if [ -z "$ConnectionString" ]; then
    echo "? ConnectionString not set in .env file!"
  exit 1
fi

echo "? ConnectionString configured"

# Set default SQLSCRIPTS_DIR if not set
if [ -z "$SQLSCRIPTS_DIR" ]; then
    export SQLSCRIPTS_DIR="./config"
    echo "? Using default SQLSCRIPTS_DIR: ./config"
else
 echo "? SQLSCRIPTS_DIR: $SQLSCRIPTS_DIR"
fi

# Check if install.xml exists
INSTALL_XML="$SQLSCRIPTS_DIR/install.xml"
if [ ! -f "$INSTALL_XML" ]; then
    # Try sample-install.xml
    SAMPLE_INSTALL="$SQLSCRIPTS_DIR/sample-install.xml"
    if [ -f "$SAMPLE_INSTALL" ]; then
  echo "? install.xml not found, using sample-install.xml"
     cp "$SAMPLE_INSTALL" "$INSTALL_XML"
    else
        echo "? install.xml not found in $SQLSCRIPTS_DIR!"
        exit 1
    fi
fi

echo "? install.xml found"
echo ""
echo "=== Starting SQL Installer ==="
echo ""

# Build and run
dotnet build
if [ $? -ne 0 ]; then
 echo "? Build failed!"
    exit 1
fi

dotnet run

EXIT_CODE=$?

echo ""
if [ $EXIT_CODE -eq 0 ]; then
    echo "? SQL Installer completed successfully!"
else
    echo "? SQL Installer failed with exit code $EXIT_CODE"
fi

exit $EXIT_CODE

#!/bin/bash

# Script to clone SQL scripts repository from GitHub
# Uses environment variables:
#   GITHUB_SQLSCRIPTS_REPO - Repository URL
#   GITHUB_SQLSCRIPTS_TOKEN - Personal access token (optional)
#   SQLSCRIPTS_DIR - Target directory for cloning

set -e

# Define default values
DEFAULT_REPO_URL="https://github.com/neemevool/vtmfdcbase"
DEFAULT_SQLSCRIPTS_DIR="/data"

REPO_URL="${GITHUB_SQLSCRIPTS_REPO:-$DEFAULT_REPO_URL}"
SQLSCRIPTS_DIR="${SQLSCRIPTS_DIR:-$DEFAULT_SQLSCRIPTS_DIR}"

TOKEN="$GITHUB_SQLSCRIPTS_TOKEN"

# Prepare the clone URL based on whether token is provided
if [ -n "$TOKEN" ] && [ "$TOKEN" != "" ]; then
    # Extract repository path from URL (handles both https://github.com/user/repo and git@github.com:user/repo formats)
    if [[ "$REPO_URL" =~ https://github.com/ ]]; then
        # For HTTPS URLs, insert token: https://token@github.com/user/repo
        CLONE_URL="${REPO_URL//https:\/\/github.com/https://${TOKEN}@github.com}"
    elif [[ "$REPO_URL" =~ git@github.com: ]]; then
        # For SSH URLs, fall back to HTTPS with token
        REPO_PATH="${REPO_URL#git@github.com:}"
        REPO_PATH="${REPO_PATH%.git}"
        CLONE_URL="https://${TOKEN}@github.com/${REPO_PATH}"
    else
        # If URL format is unexpected, use it as-is
        CLONE_URL="$REPO_URL"
    fi
    
    echo "Cloning repository with authentication..."
else
    # No token provided, clone without authentication
    CLONE_URL="$REPO_URL"
    echo "Cloning repository without authentication..."
fi

# Remove existing directory if it exists
if [ -d "$SQLSCRIPTS_DIR" ]; then
    echo "Removing existing contents of '$SQLSCRIPTS_DIR'..."
    rm -rf "$SQLSCRIPTS_DIR"/*
    rm -rf "$SQLSCRIPTS_DIR"/.git*
fi

# Clone the repository with depth=1 (single branch, minimal history)
git clone --depth 1 "$CLONE_URL" "$SQLSCRIPTS_DIR"

# Get the repository name from the URL for logging
REPO_NAME=$(basename "$REPO_URL" .git)

echo "Repository '$REPO_NAME' cloned successfully to '$SQLSCRIPTS_DIR'"
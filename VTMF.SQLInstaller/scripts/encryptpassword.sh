#!/bin/bash
set -euo pipefail

# Script to download certificate, create admin credentials file, and encrypt it
# Usage: ./create-admin.sh --idcode <IDCODE> --password <PASSWORD> --username <USERNAME> [--silent]

# Silent mode flag
SILENT=false

# Function to log messages (respects silent mode)
log() {
    if [[ "$SILENT" != true ]]; then
        echo "$@"
    fi
}

# Function to log errors (always shown, even in silent mode)
log_error() {
    echo "$@" >&2
}

# Function to display usage
usage() {
    cat << EOF
Usage: $0 --idcode <IDCODE> --password <PASSWORD> --username <USERNAME> [--silent]

Arguments:
    --idcode <IDCODE>       Estonian personal identification code (11 digits)
    --password <PASSWORD>   Admin password (can contain special characters)
    --username <USERNAME>   Admin username
    --silent                Suppress informational messages (errors still shown)

Example:
    $0 --idcode 37301234567 --password 'P@ss!w0rd$123' --username admin_user
    $0 --idcode 37301234567 --password 'P@ss!w0rd$123' --username admin_user --silent

EOF
    exit 1
}

# Parse command-line arguments
IDCODE=""
PASSWORD=""
USERNAME=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --idcode)
            IDCODE="$2"
            shift 2
            ;;
        --password)
            PASSWORD="$2"
            shift 2
            ;;
        --username)
            USERNAME="$2"
            shift 2
            ;;
        --silent)
            SILENT=true
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            log_error "Error: Unknown argument: $1"
            usage
            ;;
    esac
done

# Validate required arguments
if [[ -z "$IDCODE" ]] || [[ -z "$PASSWORD" ]] || [[ -z "$USERNAME" ]]; then
    log_error "Error: All required arguments (--idcode, --password, --username) must be provided"
    usage
fi

# Validate IDCODE format (11 digits)
if ! [[ "$IDCODE" =~ ^[0-9]{11}$ ]]; then
    log_error "Error: IDCODE must be exactly 11 digits"
    exit 1
fi

# Define file paths
CERT_FILE="/config/installhistory/admin_${IDCODE}.pem"
PLAIN_FILE="/config/installhistory/admin_${IDCODE}_${USERNAME}.txt"
ENCRYPTED_FILE="/config/installhistory/admin_${IDCODE}_${USERNAME}.cdoc"

# Ensure directory exists
mkdir -p /config/installhistory

log "===================================="
log "Creating admin credentials"
log "===================================="
log "ID Code:  $IDCODE"
log "Username: $USERNAME"
log "===================================="
log ""

# Step 1: Download certificate from LDAP
log "Step 1: Downloading certificate for ID code $IDCODE..."
if [[ "$SILENT" == true ]]; then
    # Redirect stdout to /dev/null in silent mode, keep stderr
    if java -jar /app/cdoc-encryptor.jar --download-cert "$IDCODE" "$CERT_FILE" >/dev/null 2>&1; then
        : # Success, no output
    else
        log_error "Error: Failed to download certificate"
        exit 1
    fi
else
    if java -jar /app/cdoc-encryptor.jar --download-cert "$IDCODE" "$CERT_FILE"; then
        log "✓ Certificate downloaded successfully: $CERT_FILE"
    else
        log_error "Error: Failed to download certificate"
        exit 1
    fi
fi
log ""

# Step 2: Create plain text credentials file
log "Step 2: Creating credentials file..."
{
    printf '%s\n' "$USERNAME"
    printf '%s\n' "$PASSWORD"
} > "$PLAIN_FILE"

log "✓ Credentials file created: $PLAIN_FILE"
log ""

# Step 3: Encrypt the credentials file
log "Step 3: Encrypting credentials file..."
if [[ "$SILENT" == true ]]; then
    # Redirect stdout to /dev/null in silent mode, keep stderr
    if java -jar /app/cdoc-encryptor.jar "$PLAIN_FILE" "$CERT_FILE" "$ENCRYPTED_FILE" >/dev/null 2>&1; then
        : # Success, no output
    else
        log_error "Error: Failed to encrypt file"
        exit 1
    fi
else
    if java -jar /app/cdoc-encryptor.jar "$PLAIN_FILE" "$CERT_FILE" "$ENCRYPTED_FILE"; then
        log "✓ File encrypted successfully: $ENCRYPTED_FILE"
    else
        log_error "Error: Failed to encrypt file"
        exit 1
    fi
fi
log ""

# Step 4: Clean up plain text file (security best practice)
log "Step 4: Cleaning up plain text credentials..."
if shred -vfz -n 3 "$PLAIN_FILE" 2>/dev/null && rm -f "$PLAIN_FILE"; then
    log "✓ Plain text file securely deleted"
else
    log_error "Warning: Could not securely delete plain text file"
fi
log ""

log "===================================="
log "✓ Admin credentials created successfully!"
log "===================================="
log "Certificate:     $CERT_FILE"
log "Encrypted file:  $ENCRYPTED_FILE"
log "===================================="

# In silent mode, output only the essential result (machine-readable)
if [[ "$SILENT" == true ]]; then
    echo "$ENCRYPTED_FILE"
fi
#!/bin/bash
set -e

###############################################################################
# Acetone V2 Tray Application Installer for Linux
###############################################################################

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m'

# Default values
INSTALL_DIR="/opt/acetone"
REMOVE_FROM_AUTOSTART=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --install-dir)
            INSTALL_DIR="$2"
            shift 2
            ;;
        --remove)
            REMOVE_FROM_AUTOSTART=true
            shift
            ;;
        --help)
            echo "Acetone V2 Tray Application Installer"
            echo ""
            echo "Usage: ./install-tray.sh [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --install-dir DIR    Installation directory (default: /opt/acetone)"
            echo "  --remove             Remove from autostart"
            echo "  --help               Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

DESKTOP_FILE="$HOME/.config/autostart/acetone-tray.desktop"

if $REMOVE_FROM_AUTOSTART; then
    echo -e "${YELLOW}Removing Acetone Tray from autostart...${NC}"
    if [ -f "$DESKTOP_FILE" ]; then
        rm -f "$DESKTOP_FILE"
        echo -e "${GREEN}✓ Removed from autostart${NC}"
    else
        echo -e "${GRAY}Not found in autostart${NC}"
    fi
    exit 0
fi

TRAY_EXE="$INSTALL_DIR/Acetone.TrayApp"

if [ ! -f "$TRAY_EXE" ]; then
    echo "Error: Tray application not found: $TRAY_EXE"
    exit 1
fi

echo -e "${YELLOW}Adding Acetone Tray to autostart...${NC}"

# Create autostart directory if it doesn't exist
mkdir -p "$HOME/.config/autostart"

# Create desktop entry
cat > "$DESKTOP_FILE" << EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=Acetone V2 Proxy Manager
Comment=System tray manager for Acetone V2 Proxy
Exec=$TRAY_EXE
Icon=$INSTALL_DIR/acetone-icon.png
Terminal=false
Categories=Network;System;
StartupNotify=false
X-GNOME-Autostart-enabled=true
EOF

chmod +x "$DESKTOP_FILE"

echo -e "${GREEN}✓ Added to autostart: $DESKTOP_FILE${NC}"
echo ""
echo -e "${CYAN}The tray application will start automatically on next login.${NC}"
echo -e "${CYAN}To start it now, run: $TRAY_EXE${NC}"

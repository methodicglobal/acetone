#!/bin/bash
set -e

###############################################################################
# Acetone V2 Proxy Installer for Linux
###############################################################################

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

# Default values
INSTALL_DIR="/opt/acetone"
CREATE_SERVICE=false
SERVICE_NAME="acetone-v2-proxy"
SERVICE_USER="acetone"
PORT=8080
METRICS_PORT=9090
START_SERVICE=false

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --install-dir)
            INSTALL_DIR="$2"
            shift 2
            ;;
        --create-service)
            CREATE_SERVICE=true
            shift
            ;;
        --service-name)
            SERVICE_NAME="$2"
            shift 2
            ;;
        --user)
            SERVICE_USER="$2"
            shift 2
            ;;
        --port)
            PORT="$2"
            shift 2
            ;;
        --metrics-port)
            METRICS_PORT="$2"
            shift 2
            ;;
        --start-service)
            START_SERVICE=true
            shift
            ;;
        --help)
            echo "Acetone V2 Proxy Installer for Linux"
            echo ""
            echo "Usage: sudo ./install.sh [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --install-dir DIR      Installation directory (default: /opt/acetone)"
            echo "  --create-service       Create systemd service"
            echo "  --service-name NAME    Service name (default: acetone-v2-proxy)"
            echo "  --user USER            User to run service as (default: acetone)"
            echo "  --port PORT            HTTP port (default: 8080)"
            echo "  --metrics-port PORT    Prometheus metrics port (default: 9090)"
            echo "  --start-service        Start service after installation"
            echo "  --help                 Show this help message"
            echo ""
            echo "Examples:"
            echo "  sudo ./install.sh"
            echo "  sudo ./install.sh --create-service --start-service"
            echo "  sudo ./install.sh --install-dir /usr/local/acetone --port 80"
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            echo "Use --help for usage information"
            exit 1
            ;;
    esac
done

# Check if running as root
if [[ $EUID -ne 0 ]]; then
   echo -e "${RED}This script must be run as root (use sudo)${NC}"
   exit 1
fi

echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
echo -e "${CYAN}  Acetone V2 Proxy - Linux Installer${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════${NC}"
echo ""

###############################################################################
# Function: Check .NET Runtime
###############################################################################
check_dotnet_runtime() {
    echo -e "${YELLOW}[1/7] Checking .NET Runtime...${NC}"

    if ! command -v dotnet &> /dev/null; then
        echo -e "${RED}  ✗ .NET is not installed${NC}"
        echo ""
        echo -e "${YELLOW}Please install .NET 10 Runtime:${NC}"
        echo ""

        # Detect distribution
        if [ -f /etc/os-release ]; then
            . /etc/os-release
            case $ID in
                ubuntu|debian)
                    echo "  wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb"
                    echo "  sudo dpkg -i packages-microsoft-prod.deb"
                    echo "  sudo apt update"
                    echo "  sudo apt install -y aspnetcore-runtime-10.0"
                    ;;
                rhel|centos|fedora)
                    echo "  sudo dnf install aspnetcore-runtime-10.0"
                    ;;
                *)
                    echo "  Visit: https://dotnet.microsoft.com/download/dotnet/10.0"
                    ;;
            esac
        else
            echo "  Visit: https://dotnet.microsoft.com/download/dotnet/10.0"
        fi
        echo ""

        read -p "Continue anyway? (y/N) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            exit 1
        fi
        return
    fi

    # Check for ASP.NET Core 10 runtime
    if ! dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 10\."; then
        echo -e "${YELLOW}  ⚠ ASP.NET Core 10 Runtime not found${NC}"
        echo -e "${GRAY}Found runtimes:${NC}"
        dotnet --list-runtimes | sed 's/^/  /' | while read line; do
            echo -e "${GRAY}$line${NC}"
        done
        echo ""

        read -p "Continue anyway? (y/N) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            exit 1
        fi
    else
        echo -e "${GREEN}  ✓ .NET 10 ASP.NET Core Runtime found${NC}"
    fi
}

###############################################################################
# Function: Create service user
###############################################################################
create_service_user() {
    if $CREATE_SERVICE; then
        echo -e "${YELLOW}[2/7] Creating service user...${NC}"

        if id "$SERVICE_USER" &>/dev/null; then
            echo -e "${GRAY}  ℹ User '$SERVICE_USER' already exists${NC}"
        else
            useradd --system --no-create-home --shell /sbin/nologin "$SERVICE_USER"
            echo -e "${GREEN}  ✓ Created user: $SERVICE_USER${NC}"
        fi
    else
        echo -e "${GRAY}[2/7] Skipping service user creation${NC}"
    fi
}

###############################################################################
# Function: Install files
###############################################################################
install_files() {
    echo -e "${YELLOW}[3/7] Installing files to $INSTALL_DIR...${NC}"

    # Get script directory
    SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

    # Create installation directory
    mkdir -p "$INSTALL_DIR"
    echo -e "${GREEN}  ✓ Created directory: $INSTALL_DIR${NC}"

    # Copy all files except install scripts
    find "$SCRIPT_DIR" -maxdepth 1 -type f ! -name 'install.sh' ! -name 'install.ps1' -exec cp {} "$INSTALL_DIR/" \;

    # Copy README and install scripts
    [ -f "$SCRIPT_DIR/README.md" ] && cp "$SCRIPT_DIR/README.md" "$INSTALL_DIR/"
    cp "$SCRIPT_DIR/install.sh" "$INSTALL_DIR/"
    [ -f "$SCRIPT_DIR/install.ps1" ] && cp "$SCRIPT_DIR/install.ps1" "$INSTALL_DIR/"

    echo -e "${GREEN}  ✓ Files copied${NC}"

    # Set executable permissions
    chmod +x "$INSTALL_DIR/Acetone.V2.Proxy"
    [ -f "$INSTALL_DIR/install.sh" ] && chmod +x "$INSTALL_DIR/install.sh"
    echo -e "${GREEN}  ✓ Set executable permissions${NC}"

    # Set ownership
    if $CREATE_SERVICE; then
        chown -R "$SERVICE_USER:$SERVICE_USER" "$INSTALL_DIR"
        echo -e "${GREEN}  ✓ Set ownership to $SERVICE_USER${NC}"
    fi
}

###############################################################################
# Function: Create configuration
###############################################################################
create_configuration() {
    echo -e "${YELLOW}[4/7] Configuring application...${NC}"

    CONFIG_FILE="$INSTALL_DIR/appsettings.Production.json"

    cat > "$CONFIG_FILE" << EOF
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Yarp": "Information"
    }
  },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:$PORT"
}
EOF

    if $CREATE_SERVICE; then
        chown "$SERVICE_USER:$SERVICE_USER" "$CONFIG_FILE"
    fi

    echo -e "${GREEN}  ✓ Created configuration: $CONFIG_FILE${NC}"
    echo -e "${GRAY}  ℹ HTTP Port: $PORT${NC}"
    echo -e "${GRAY}  ℹ Metrics Port: $METRICS_PORT${NC}"
}

###############################################################################
# Function: Configure firewall
###############################################################################
configure_firewall() {
    echo -e "${YELLOW}[5/7] Configuring firewall...${NC}"

    # Check if UFW is installed and enabled
    if command -v ufw &> /dev/null && ufw status | grep -q "Status: active"; then
        ufw allow $PORT/tcp comment "Acetone V2 Proxy HTTP" &>/dev/null || true
        echo -e "${GREEN}  ✓ Allowed port $PORT (UFW)${NC}"

        # For metrics, allow only from local network (more secure)
        echo -e "${GRAY}  ℹ Metrics port $METRICS_PORT not auto-configured (configure manually for security)${NC}"
    elif command -v firewall-cmd &> /dev/null; then
        # Firewalld (RHEL/CentOS/Fedora)
        firewall-cmd --permanent --add-port=$PORT/tcp &>/dev/null || true
        firewall-cmd --reload &>/dev/null || true
        echo -e "${GREEN}  ✓ Allowed port $PORT (firewalld)${NC}"
    else
        echo -e "${GRAY}  ℹ No supported firewall detected, skipping firewall configuration${NC}"
    fi
}

###############################################################################
# Function: Create systemd service
###############################################################################
create_systemd_service() {
    if $CREATE_SERVICE; then
        echo -e "${YELLOW}[6/7] Creating systemd service...${NC}"

        SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"

        cat > "$SERVICE_FILE" << EOF
[Unit]
Description=Acetone V2 Reverse Proxy
Documentation=https://github.com/methodicglobal/acetone
After=network.target

[Service]
Type=notify
User=$SERVICE_USER
Group=$SERVICE_USER
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/Acetone.V2.Proxy
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=$SERVICE_NAME

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$INSTALL_DIR

# Environment
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=ASPNETCORE_URLS=http://0.0.0.0:$PORT

[Install]
WantedBy=multi-user.target
EOF

        systemctl daemon-reload
        echo -e "${GREEN}  ✓ Created systemd service: $SERVICE_NAME${NC}"

        systemctl enable "$SERVICE_NAME"
        echo -e "${GREEN}  ✓ Service enabled (will start on boot)${NC}"
    else
        echo -e "${GRAY}[6/7] Skipping service creation (use --create-service to enable)${NC}"
    fi
}

###############################################################################
# Function: Start service
###############################################################################
start_service() {
    if $CREATE_SERVICE && $START_SERVICE; then
        echo -e "${YELLOW}[7/7] Starting service...${NC}"

        systemctl start "$SERVICE_NAME"
        sleep 2

        if systemctl is-active --quiet "$SERVICE_NAME"; then
            echo -e "${GREEN}  ✓ Service started successfully${NC}"
        else
            echo -e "${YELLOW}  ⚠ Service may not have started correctly${NC}"
            echo -e "${GRAY}  Check status with: systemctl status $SERVICE_NAME${NC}"
        fi
    elif $CREATE_SERVICE; then
        echo -e "${GRAY}[7/7] Service created but not started (use --start-service to auto-start)${NC}"
    else
        echo -e "${GRAY}[7/7] Skipping service start${NC}"
    fi
}

###############################################################################
# Main installation process
###############################################################################

# Run installation steps
check_dotnet_runtime
create_service_user
install_files
create_configuration
configure_firewall
create_systemd_service
start_service

# Success message
echo ""
echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}  ✓ Installation completed successfully!${NC}"
echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
echo ""
echo -e "${CYAN}Installation Location:${NC} $INSTALL_DIR"
echo -e "${CYAN}HTTP Port:${NC} $PORT"
echo -e "${CYAN}Metrics Port:${NC} $METRICS_PORT"

if $CREATE_SERVICE; then
    echo -e "${CYAN}Service Name:${NC} $SERVICE_NAME"
    echo -e "${CYAN}Service User:${NC} $SERVICE_USER"
    echo ""
    echo -e "${YELLOW}Service Management Commands:${NC}"
    echo -e "${GRAY}  Start:   sudo systemctl start $SERVICE_NAME${NC}"
    echo -e "${GRAY}  Stop:    sudo systemctl stop $SERVICE_NAME${NC}"
    echo -e "${GRAY}  Restart: sudo systemctl restart $SERVICE_NAME${NC}"
    echo -e "${GRAY}  Status:  sudo systemctl status $SERVICE_NAME${NC}"
    echo -e "${GRAY}  Logs:    sudo journalctl -u $SERVICE_NAME -f${NC}"
else
    echo ""
    echo -e "${YELLOW}To run manually:${NC}"
    echo -e "${GRAY}  cd $INSTALL_DIR${NC}"
    echo -e "${GRAY}  ./Acetone.V2.Proxy${NC}"
    echo ""
    echo -e "${YELLOW}To create a service later, run:${NC}"
    echo -e "${GRAY}  sudo ./install.sh --create-service --start-service${NC}"
fi

echo ""
echo -e "${YELLOW}Health Check:${NC} http://localhost:$PORT/health"
echo -e "${YELLOW}Metrics:${NC} http://localhost:$METRICS_PORT/metrics"
echo ""
echo -e "${CYAN}Documentation:${NC} See README.md in $INSTALL_DIR"
echo ""

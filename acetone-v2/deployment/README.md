# Acetone V2 Proxy - Installation Guide

Welcome to Acetone V2, a high-performance reverse proxy built on YARP and .NET 10.

## Quick Start

### Windows
```powershell
# Run the installer (requires Administrator privileges)
.\install.ps1

# Or run directly
.\Acetone.V2.Proxy.exe
```

### Linux
```bash
# Run the installer (requires sudo)
sudo ./install.sh

# Or run directly
./Acetone.V2.Proxy
```

## System Requirements

- **.NET 10 Runtime** (ASP.NET Core)
  - Windows: Download from https://dotnet.microsoft.com/download/dotnet/10.0
  - Linux: Follow your distribution's package manager instructions

### Supported Platforms
- **Windows**: Windows Server 2019+, Windows 10/11 (x64)
- **Linux**: Ubuntu 20.04+, Debian 11+, RHEL 8+, other modern distributions (x64)

## Installation Methods

### Method 1: Automated Installation (Recommended)

#### Windows (PowerShell - Run as Administrator)
```powershell
.\install.ps1 -InstallLocation "C:\Program Files\Acetone" -CreateService
```

**Options:**
- `-InstallLocation`: Installation directory (default: `C:\Program Files\Acetone`)
- `-CreateService`: Register as Windows Service (requires admin)
- `-ServiceName`: Service name (default: `AcetoneV2Proxy`)
- `-Port`: HTTP port (default: 8080)
- `-MetricsPort`: Prometheus metrics port (default: 9090)

#### Linux (Bash - Run as root/sudo)
```bash
sudo ./install.sh --install-dir /opt/acetone --create-service
```

**Options:**
- `--install-dir`: Installation directory (default: `/opt/acetone`)
- `--create-service`: Register as systemd service
- `--service-name`: Service name (default: `acetone-v2-proxy`)
- `--port`: HTTP port (default: 8080)
- `--metrics-port`: Prometheus metrics port (default: 9090)
- `--user`: User to run as (default: `acetone`)

### Method 2: Manual Installation

#### Windows

1. **Check .NET Runtime**
   ```powershell
   dotnet --list-runtimes | Select-String "Microsoft.AspNetCore.App 10."
   ```

2. **Extract Files**
   ```powershell
   Expand-Archive acetone-v2-win-x64.zip -DestinationPath "C:\Program Files\Acetone"
   cd "C:\Program Files\Acetone"
   ```

3. **Configure**
   - Edit `appsettings.json` to customize settings
   - See Configuration section below

4. **Run**
   ```powershell
   .\Acetone.V2.Proxy.exe
   ```

#### Linux

1. **Check .NET Runtime**
   ```bash
   dotnet --list-runtimes | grep "Microsoft.AspNetCore.App 10."
   ```

   If not installed:
   ```bash
   # Ubuntu/Debian
   wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb
   sudo dpkg -i packages-microsoft-prod.deb
   sudo apt update
   sudo apt install -y aspnetcore-runtime-10.0

   # RHEL/CentOS/Fedora
   sudo dnf install aspnetcore-runtime-10.0
   ```

2. **Extract Files**
   ```bash
   sudo mkdir -p /opt/acetone
   sudo tar -xzf acetone-v2-linux-x64.tar.gz -C /opt/acetone
   cd /opt/acetone
   ```

3. **Set Permissions**
   ```bash
   sudo chmod +x Acetone.V2.Proxy
   sudo chown -R root:root /opt/acetone
   ```

4. **Configure**
   - Edit `appsettings.json` to customize settings
   - See Configuration section below

5. **Run**
   ```bash
   ./Acetone.V2.Proxy
   ```

## Configuration

### Application Settings

Create or edit `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:8080",
  "ReverseProxy": {
    "Routes": {
      "route1": {
        "ClusterId": "cluster1",
        "Match": {
          "Path": "{**catch-all}"
        }
      }
    },
    "Clusters": {
      "cluster1": {
        "Destinations": {
          "destination1": {
            "Address": "https://example.com"
          }
        }
      }
    }
  }
}
```

### Environment Variables

You can override settings using environment variables:

```bash
# Windows
$env:ASPNETCORE_URLS = "http://0.0.0.0:8080"
$env:ASPNETCORE_ENVIRONMENT = "Production"

# Linux
export ASPNETCORE_URLS="http://0.0.0.0:8080"
export ASPNETCORE_ENVIRONMENT="Production"
```

## Running as a Service

### Windows Service

**Using the installer:**
```powershell
.\install.ps1 -CreateService
```

**Manual setup:**
```powershell
# Using NSSM (Non-Sucking Service Manager) - recommended
# Download from https://nssm.cc/download
nssm install AcetoneV2Proxy "C:\Program Files\Acetone\Acetone.V2.Proxy.exe"
nssm set AcetoneV2Proxy AppDirectory "C:\Program Files\Acetone"
nssm set AcetoneV2Proxy Description "Acetone V2 Reverse Proxy"
nssm start AcetoneV2Proxy
```

**Service Management:**
```powershell
# Start service
Start-Service AcetoneV2Proxy

# Stop service
Stop-Service AcetoneV2Proxy

# Restart service
Restart-Service AcetoneV2Proxy

# View logs
Get-EventLog -LogName Application -Source AcetoneV2Proxy -Newest 50
```

### Linux systemd Service

**Using the installer:**
```bash
sudo ./install.sh --create-service
```

**Manual setup:**
Create `/etc/systemd/system/acetone-v2-proxy.service`:

```ini
[Unit]
Description=Acetone V2 Reverse Proxy
After=network.target

[Service]
Type=notify
User=acetone
Group=acetone
WorkingDirectory=/opt/acetone
ExecStart=/opt/acetone/Acetone.V2.Proxy
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=acetone-v2-proxy
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

**Service Management:**
```bash
# Reload systemd
sudo systemctl daemon-reload

# Enable service (start on boot)
sudo systemctl enable acetone-v2-proxy

# Start service
sudo systemctl start acetone-v2-proxy

# Stop service
sudo systemctl stop acetone-v2-proxy

# Restart service
sudo systemctl restart acetone-v2-proxy

# View status
sudo systemctl status acetone-v2-proxy

# View logs
sudo journalctl -u acetone-v2-proxy -f
```

## Monitoring

### Health Checks

```bash
# Check if the service is running
curl http://localhost:8080/health

# Check Prometheus metrics
curl http://localhost:9090/metrics
```

### Prometheus Metrics

Acetone V2 exposes metrics at `http://localhost:9090/metrics` by default.

Add to your Prometheus configuration:
```yaml
scrape_configs:
  - job_name: 'acetone-v2'
    static_configs:
      - targets: ['localhost:9090']
```

## Firewall Configuration

### Windows Firewall
```powershell
# Allow HTTP traffic (port 8080)
New-NetFirewallRule -DisplayName "Acetone V2 HTTP" -Direction Inbound -LocalPort 8080 -Protocol TCP -Action Allow

# Allow Metrics traffic (port 9090)
New-NetFirewallRule -DisplayName "Acetone V2 Metrics" -Direction Inbound -LocalPort 9090 -Protocol TCP -Action Allow
```

### Linux Firewall (UFW)
```bash
# Allow HTTP traffic
sudo ufw allow 8080/tcp

# Allow Metrics traffic (only from monitoring server)
sudo ufw allow from 10.0.0.0/8 to any port 9090 proto tcp
```

## Uninstallation

### Windows
```powershell
# Stop and remove service
Stop-Service AcetoneV2Proxy
sc.exe delete AcetoneV2Proxy

# Remove files
Remove-Item -Recurse -Force "C:\Program Files\Acetone"
```

### Linux
```bash
# Stop and disable service
sudo systemctl stop acetone-v2-proxy
sudo systemctl disable acetone-v2-proxy
sudo rm /etc/systemd/system/acetone-v2-proxy.service
sudo systemctl daemon-reload

# Remove files
sudo rm -rf /opt/acetone
sudo userdel acetone
```

## Troubleshooting

### Port Already in Use
```bash
# Windows
netstat -ano | findstr :8080
# Kill the process using the PID shown

# Linux
sudo lsof -i :8080
# Kill the process using the PID shown
```

### Permission Denied
```bash
# Linux - ensure executable permissions
chmod +x Acetone.V2.Proxy

# Linux - run as root or with sudo
sudo ./Acetone.V2.Proxy
```

### .NET Runtime Not Found
```bash
# Check installed runtimes
dotnet --list-runtimes

# Install ASP.NET Core Runtime 10.0
# See: https://dotnet.microsoft.com/download/dotnet/10.0
```

### Service Won't Start
```bash
# Windows - check Event Viewer
eventvwr.msc

# Linux - check journalctl
sudo journalctl -u acetone-v2-proxy -n 50 --no-pager
```

## Support & Documentation

- **GitHub Repository**: https://github.com/methodicglobal/acetone
- **Issues**: https://github.com/methodicglobal/acetone/issues
- **YARP Documentation**: https://microsoft.github.io/reverse-proxy/

## Version Information

Check `VERSION.txt` in this package for build details including:
- Version number
- Build timestamp
- Git commit hash
- Platform

## License

See `LICENSE` file for license information.

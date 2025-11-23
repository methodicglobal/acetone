using System.Diagnostics;
using System.ServiceProcess;

namespace Acetone.V2.TrayApp.Windows;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly string _serviceName = "AcetoneV2Proxy";
    private readonly string _installPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Acetone"
    );

    public TrayApplicationContext()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // TODO: Use custom icon
            Text = "Acetone V2 Proxy",
            Visible = true
        };

        _trayIcon.DoubleClick += OnTrayIconDoubleClick;

        UpdateContextMenu();

        // Update status every 5 seconds
        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (s, e) => UpdateContextMenu();
        timer.Start();
    }

    private void UpdateContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        var status = GetServiceStatus();

        // Title
        var titleItem = new ToolStripLabel($"Acetone V2 Proxy - {status}")
        {
            Font = new Font(contextMenu.Font, FontStyle.Bold)
        };
        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        // Service controls
        if (status == "Running")
        {
            contextMenu.Items.Add("Stop Service", null, OnStopService);
            contextMenu.Items.Add("Restart Service", null, OnRestartService);
        }
        else if (status == "Stopped")
        {
            contextMenu.Items.Add("Start Service", null, OnStartService);
        }
        else
        {
            var notInstalledItem = new ToolStripMenuItem("Service Not Installed")
            {
                Enabled = false
            };
            contextMenu.Items.Add(notInstalledItem);
        }

        contextMenu.Items.Add(new ToolStripSeparator());

        // Configuration & Monitoring
        contextMenu.Items.Add("Open Configuration", null, OnOpenConfiguration);
        contextMenu.Items.Add("View Logs", null, OnViewLogs);
        contextMenu.Items.Add("Health Check", null, OnHealthCheck);
        contextMenu.Items.Add("Metrics Dashboard", null, OnMetricsDashboard);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Information
        contextMenu.Items.Add("About", null, OnAbout);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Exit
        contextMenu.Items.Add("Exit", null, OnExit);

        _trayIcon.ContextMenuStrip = contextMenu;

        // Update icon based on status
        _trayIcon.Icon = status == "Running"
            ? SystemIcons.Information  // Green-ish
            : SystemIcons.Warning;      // Yellow-ish

        _trayIcon.Text = $"Acetone V2 Proxy - {status}";
    }

    private string GetServiceStatus()
    {
        try
        {
            using var service = new ServiceController(_serviceName);
            return service.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "Not Installed";
        }
        catch (Exception)
        {
            return "Error";
        }
    }

    private void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        var status = GetServiceStatus();
        var message = $"Acetone V2 Proxy\n\n" +
                     $"Status: {status}\n" +
                     $"Service Name: {_serviceName}\n" +
                     $"Installation: {_installPath}";

        MessageBox.Show(message, "Acetone V2 Proxy", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnStartService(object? sender, EventArgs e)
    {
        ExecuteServiceCommand(() =>
        {
            using var service = new ServiceController(_serviceName);
            if (service.Status == ServiceControllerStatus.Stopped)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                MessageBox.Show("Service started successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }, "start");
    }

    private void OnStopService(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to stop the Acetone V2 Proxy service?",
            "Confirm Stop",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );

        if (result == DialogResult.Yes)
        {
            ExecuteServiceCommand(() =>
            {
                using var service = new ServiceController(_serviceName);
                if (service.Status == ServiceControllerStatus.Running)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    MessageBox.Show("Service stopped successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }, "stop");
        }
    }

    private void OnRestartService(object? sender, EventArgs e)
    {
        ExecuteServiceCommand(() =>
        {
            using var service = new ServiceController(_serviceName);
            if (service.Status == ServiceControllerStatus.Running)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            MessageBox.Show("Service restarted successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }, "restart");
    }

    private void ExecuteServiceCommand(Action action, string commandName)
    {
        try
        {
            action();
            UpdateContextMenu();
        }
        catch (InvalidOperationException)
        {
            MessageBox.Show(
                $"Service '{_serviceName}' is not installed.\n\nPlease run the installer first.",
                "Service Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                $"Access denied. Administrator privileges required to {commandName} the service.\n\n" +
                "Please restart this application as Administrator.",
                "Permission Denied",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to {commandName} service:\n\n{ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void OnOpenConfiguration(object? sender, EventArgs e)
    {
        var configPath = Path.Combine(_installPath, "appsettings.json");

        if (File.Exists(configPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = configPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open configuration:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            MessageBox.Show($"Configuration file not found:\n{configPath}", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnViewLogs(object? sender, EventArgs e)
    {
        try
        {
            // Open Event Viewer filtered to this service
            Process.Start(new ProcessStartInfo
            {
                FileName = "eventvwr.msc",
                Arguments = "/c:Application",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open Event Viewer:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnHealthCheck(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://localhost:8080/health",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open health check:\n\n{ex.Message}\n\nMake sure the service is running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnMetricsDashboard(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://localhost:9090/metrics",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open metrics:\n\n{ex.Message}\n\nMake sure the service is running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        var versionFile = Path.Combine(_installPath, "VERSION.txt");
        var version = "Unknown";

        if (File.Exists(versionFile))
        {
            try
            {
                version = File.ReadAllText(versionFile);
            }
            catch { }
        }

        var message = $"Acetone V2 Proxy - System Tray Manager\n\n" +
                     $"{version}\n\n" +
                     $"A high-performance reverse proxy built on YARP and .NET 10\n\n" +
                     $"GitHub: https://github.com/methodicglobal/acetone";

        MessageBox.Show(message, "About Acetone V2 Proxy", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

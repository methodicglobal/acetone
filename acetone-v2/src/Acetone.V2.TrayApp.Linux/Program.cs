using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Diagnostics;

namespace Acetone.V2.TrayApp.Linux;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

public class App : Application
{
    public override void Initialize()
    {
        // No XAML needed for tray-only app
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var trayIcon = new TrayIcon
            {
                Icon = new WindowIcon("/usr/share/pixmaps/acetone-icon.png"), // Fallback icon
                ToolTipText = "Acetone V2 Proxy"
            };

            var menu = new NativeMenu();

            var statusItem = new NativeMenuItem
            {
                Header = "Acetone V2 Proxy - Checking...",
                IsEnabled = false
            };
            menu.Items.Add(statusItem);
            menu.Items.Add(new NativeMenuItemSeparator());

            var startItem = new NativeMenuItem { Header = "Start Service" };
            startItem.Click += (s, e) => ExecuteServiceCommand("start");
            menu.Items.Add(startItem);

            var stopItem = new NativeMenuItem { Header = "Stop Service" };
            stopItem.Click += (s, e) => ExecuteServiceCommand("stop");
            menu.Items.Add(stopItem);

            var restartItem = new NativeMenuItem { Header = "Restart Service" };
            restartItem.Click += (s, e) => ExecuteServiceCommand("restart");
            menu.Items.Add(restartItem);

            menu.Items.Add(new NativeMenuItemSeparator());

            var configItem = new NativeMenuItem { Header = "Edit Configuration" };
            configItem.Click += (s, e) => OpenConfiguration();
            menu.Items.Add(configItem);

            var validateConfigItem = new NativeMenuItem { Header = "Validate Configuration" };
            validateConfigItem.Click += (s, e) => ValidateConfiguration();
            menu.Items.Add(validateConfigItem);

            var logsItem = new NativeMenuItem { Header = "View Logs" };
            logsItem.Click += (s, e) => ViewLogs();
            menu.Items.Add(logsItem);

            var healthItem = new NativeMenuItem { Header = "Health Check" };
            healthItem.Click += (s, e) => OpenUrl("http://localhost:8080/health");
            menu.Items.Add(healthItem);

            var metricsItem = new NativeMenuItem { Header = "Metrics Dashboard" };
            metricsItem.Click += (s, e) => OpenUrl("http://localhost:9090/metrics");
            menu.Items.Add(metricsItem);

            menu.Items.Add(new NativeMenuItemSeparator());

            var aboutItem = new NativeMenuItem { Header = "About" };
            aboutItem.Click += (s, e) => ShowAbout();
            menu.Items.Add(aboutItem);

            menu.Items.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem { Header = "Exit" };
            exitItem.Click += (s, e) =>
            {
                trayIcon.Dispose();
                desktop.Shutdown();
            };
            menu.Items.Add(exitItem);

            trayIcon.Menu = menu;
            trayIcon.IsVisible = true;

            // Update status periodically
            var timer = new System.Timers.Timer(5000);
            timer.Elapsed += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var status = GetServiceStatus();
                    statusItem.Header = $"Acetone V2 Proxy - {status}";
                    trayIcon.ToolTipText = $"Acetone V2 Proxy - {status}";

                    // Enable/disable menu items based on status
                    startItem.IsEnabled = status == "inactive" || status == "failed";
                    stopItem.IsEnabled = status == "active";
                    restartItem.IsEnabled = status == "active";
                });
            };
            timer.Start();

            // Initial status check
            var initialStatus = GetServiceStatus();
            statusItem.Header = $"Acetone V2 Proxy - {initialStatus}";
            trayIcon.ToolTipText = $"Acetone V2 Proxy - {initialStatus}";
        }

        base.OnFrameworkInitializationCompleted();
    }

    private string GetServiceStatus()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "systemctl",
                    Arguments = "is-active acetone-v2-proxy",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output; // active, inactive, failed, etc.
        }
        catch
        {
            return "unknown";
        }
    }

    private void ExecuteServiceCommand(string command)
    {
        try
        {
            var action = command switch
            {
                "start" => "start",
                "stop" => "stop",
                "restart" => "restart",
                _ => throw new ArgumentException("Invalid command")
            };

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pkexec",
                    Arguments = $"systemctl {action} acetone-v2-proxy",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                ShowNotification($"Service {command}ed successfully", "Acetone V2 Proxy");
            }
            else
            {
                ShowNotification($"Failed to {command} service", "Error", true);
            }
        }
        catch (Exception ex)
        {
            ShowNotification($"Error: {ex.Message}", "Error", true);
        }
    }

    private void OpenConfiguration()
    {
        try
        {
            var configPath = "/opt/acetone/appsettings.json";

            if (!File.Exists(configPath))
            {
                ShowNotification($"Configuration file not found: {configPath}", "Error", true);
                return;
            }

            // Try to find default text editor with GUI
            var guiEditors = new[] { "gedit", "kate", "mousepad", "geany", "pluma" };

            foreach (var editor in guiEditors)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "which",
                            Arguments = editor,
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = editor,
                            Arguments = configPath,
                            UseShellExecute = false
                        });

                        ShowNotification(
                            "Configuration file opened in editor.\nValidate your changes before restarting the service.",
                            "Configuration"
                        );
                        return;
                    }
                }
                catch
                {
                    continue;
                }
            }

            // Fallback to xdg-open
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = configPath,
                    UseShellExecute = false
                });

                ShowNotification(
                    "Configuration file opened.\nValidate your changes before restarting the service.",
                    "Configuration"
                );
            }
            catch
            {
                ShowNotification(
                    $"Could not find a text editor. Please edit manually:\n{configPath}",
                    "No Editor Found",
                    true
                );
            }
        }
        catch (Exception ex)
        {
            ShowNotification($"Error opening configuration: {ex.Message}", "Error", true);
        }
    }

    private void ValidateConfiguration()
    {
        try
        {
            var configPath = "/opt/acetone/appsettings.json";

            if (!File.Exists(configPath))
            {
                ShowNotification($"Configuration file not found: {configPath}", "Error", true);
                return;
            }

            // Read and parse JSON
            var json = File.ReadAllText(configPath);
            var errors = new List<string>();

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Basic validation
                if (root.TryGetProperty("Urls", out var urls))
                {
                    var urlString = urls.GetString();
                    if (string.IsNullOrWhiteSpace(urlString))
                    {
                        errors.Add("Urls cannot be empty");
                    }
                    else
                    {
                        foreach (var url in urlString.Split(';'))
                        {
                            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out _))
                            {
                                errors.Add($"Invalid URL: {url}");
                            }
                        }
                    }
                }

                // Validate ReverseProxy if present
                if (root.TryGetProperty("ReverseProxy", out var proxy))
                {
                    if (proxy.TryGetProperty("Routes", out var routes))
                    {
                        foreach (var route in routes.EnumerateObject())
                        {
                            if (route.Value.TryGetProperty("ClusterId", out var clusterId))
                            {
                                var clusterIdStr = clusterId.GetString();
                                if (string.IsNullOrWhiteSpace(clusterIdStr))
                                {
                                    errors.Add($"Route '{route.Name}': ClusterId cannot be empty");
                                }
                            }
                        }
                    }

                    if (proxy.TryGetProperty("Clusters", out var clusters))
                    {
                        foreach (var cluster in clusters.EnumerateObject())
                        {
                            if (cluster.Value.TryGetProperty("Destinations", out var destinations))
                            {
                                var destCount = 0;
                                foreach (var dest in destinations.EnumerateObject())
                                {
                                    destCount++;
                                    if (dest.Value.TryGetProperty("Address", out var address))
                                    {
                                        var addressStr = address.GetString();
                                        if (string.IsNullOrWhiteSpace(addressStr))
                                        {
                                            errors.Add($"Cluster '{cluster.Name}': Destination '{dest.Name}' address cannot be empty");
                                        }
                                        else if (!Uri.TryCreate(addressStr, UriKind.Absolute, out _))
                                        {
                                            errors.Add($"Cluster '{cluster.Name}': Destination '{dest.Name}' has invalid address");
                                        }
                                    }
                                }

                                if (destCount == 0)
                                {
                                    errors.Add($"Cluster '{cluster.Name}': Must have at least one destination");
                                }
                            }
                        }
                    }
                }

                if (errors.Count == 0)
                {
                    ShowNotification(
                        "Configuration is valid! ✓\nNo errors found.",
                        "Validation Successful"
                    );
                }
                else
                {
                    var errorMessage = $"Found {errors.Count} error(s):\\n" + string.Join("\\n", errors.Take(5));
                    if (errors.Count > 5)
                    {
                        errorMessage += $"\\n...and {errors.Count - 5} more";
                    }
                    ShowNotification(errorMessage, "Validation Errors", true);
                }
            }
            catch (System.Text.Json.JsonException jsonEx)
            {
                ShowNotification($"Invalid JSON format: {jsonEx.Message}", "JSON Error", true);
            }
        }
        catch (Exception ex)
        {
            ShowNotification($"Validation error: {ex.Message}", "Error", true);
        }
    }

    private void ViewLogs()
    {
        try
        {
            // Try to open in terminal with journalctl
            var terminals = new[]
            {
                ("gnome-terminal", "-- journalctl -u acetone-v2-proxy -f"),
                ("konsole", "-e journalctl -u acetone-v2-proxy -f"),
                ("xterm", "-e journalctl -u acetone-v2-proxy -f")
            };

            foreach (var (terminal, args) in terminals)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = terminal,
                        Arguments = args,
                        UseShellExecute = false
                    });
                    return;
                }
                catch
                {
                    continue;
                }
            }

            ShowNotification("No terminal found. Use: journalctl -u acetone-v2-proxy -f", "Info");
        }
        catch (Exception ex)
        {
            ShowNotification($"Error: {ex.Message}", "Error", true);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            ShowNotification($"Error opening URL: {ex.Message}", "Error", true);
        }
    }

    private void ShowAbout()
    {
        var versionFile = "/opt/acetone/VERSION.txt";
        var version = "Unknown";

        if (File.Exists(versionFile))
        {
            try
            {
                version = File.ReadAllText(versionFile);
            }
            catch { }
        }

        var message = $"Acetone V2 Proxy - System Tray Manager\\n\\n{version}\\n\\nA high-performance reverse proxy built on YARP and .NET 10";
        ShowNotification(message, "About Acetone V2 Proxy");
    }

    private void ShowNotification(string message, string title, bool isError = false)
    {
        try
        {
            var urgency = isError ? "critical" : "normal";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    Arguments = $"-u {urgency} \"{title}\" \"{message}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
        }
        catch
        {
            // Fallback: just print to console
            Console.WriteLine($"{title}: {message}");
        }
    }
}

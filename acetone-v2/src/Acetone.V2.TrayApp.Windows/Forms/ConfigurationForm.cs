using System.ComponentModel;
using Acetone.V2.TrayApp.Windows.Configuration;

namespace Acetone.V2.TrayApp.Windows.Forms;

public class ConfigurationForm : Form
{
    private readonly string _configPath;
    private readonly SecureConfigurationManager _configManager;
    private AcetoneConfiguration _configuration;

    private TabControl _tabControl;
    private Button _saveButton;
    private Button _cancelButton;
    private Button _validateButton;
    private Button _restartServiceButton;

    // General tab controls
    private TextBox _urlsTextBox;
    private TextBox _allowedHostsTextBox;

    // Logging tab controls
    private DataGridView _loggingGrid;

    // Service Fabric tab controls
    private TextBox _sfEndpointTextBox;
    private TextBox _sfServerThumbprintTextBox;
    private TextBox _sfClientThumbprintTextBox;
    private ComboBox _sfStoreLocationCombo;
    private TextBox _sfStoreNameTextBox;
    private ComboBox _sfProtectionLevelCombo;

    // Reverse Proxy tab controls
    private ListBox _routesListBox;
    private ListBox _clustersListBox;
    private PropertyGrid _routePropertyGrid;
    private PropertyGrid _clusterPropertyGrid;

    // Rate Limiting tab controls
    private CheckBox _rateLimitingEnabledCheckBox;
    private NumericUpDown _permitLimitNumeric;
    private TextBox _windowTextBox;
    private NumericUpDown _queueLimitNumeric;

    public ConfigurationForm(string configPath)
    {
        _configPath = configPath;
        _configManager = new SecureConfigurationManager(configPath);

        try
        {
            _configuration = _configManager.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load configuration:\n\n{ex.Message}\n\nA default configuration will be used.",
                "Configuration Load Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            _configuration = new AcetoneConfiguration();
        }

        InitializeComponent();
        LoadConfiguration();
    }

    private void InitializeComponent()
    {
        Text = "Acetone V2 Proxy - Configuration";
        Size = new Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Tab Control
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(10)
        };

        // Create tabs
        _tabControl.TabPages.Add(CreateGeneralTab());
        _tabControl.TabPages.Add(CreateLoggingTab());
        _tabControl.TabPages.Add(CreateServiceFabricTab());
        _tabControl.TabPages.Add(CreateReverseProxyTab());
        _tabControl.TabPages.Add(CreateRateLimitingTab());

        // Bottom panel with buttons
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            Padding = new Padding(10)
        };

        _validateButton = new Button
        {
            Text = "Validate",
            Size = new Size(100, 35),
            Location = new Point(10, 12)
        };
        _validateButton.Click += ValidateButton_Click;

        _saveButton = new Button
        {
            Text = "Save && Apply",
            Size = new Size(120, 35),
            Location = new Point(bottomPanel.Width - 360, 12),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _saveButton.Click += SaveButton_Click;

        _restartServiceButton = new Button
        {
            Text = "Restart Service",
            Size = new Size(120, 35),
            Location = new Point(bottomPanel.Width - 230, 12),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _restartServiceButton.Click += RestartServiceButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Size = new Size(100, 35),
            Location = new Point(bottomPanel.Width - 110, 12),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _cancelButton.Click += (s, e) => Close();

        bottomPanel.Controls.AddRange(new Control[] { _validateButton, _saveButton, _restartServiceButton, _cancelButton });

        Controls.Add(_tabControl);
        Controls.Add(bottomPanel);
    }

    private TabPage CreateGeneralTab()
    {
        var tab = new TabPage("General");
        var y = 20;

        // URLs
        AddLabel(tab, "Listen URLs:", 20, y);
        _urlsTextBox = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(640, 25),
            Text = _configuration.Urls
        };
        AddTooltip(_urlsTextBox, "Semicolon-separated list of URLs to listen on (e.g., http://0.0.0.0:8080;https://0.0.0.0:8443)");
        tab.Controls.Add(_urlsTextBox);
        y += 40;

        // Allowed Hosts
        AddLabel(tab, "Allowed Hosts:", 20, y);
        _allowedHostsTextBox = new TextBox
        {
            Location = new Point(200, y),
            Size = new Size(640, 25),
            Text = _configuration.AllowedHosts
        };
        AddTooltip(_allowedHostsTextBox, "Semicolon-separated list of allowed host names, or * for all");
        tab.Controls.Add(_allowedHostsTextBox);
        y += 40;

        return tab;
    }

    private TabPage CreateLoggingTab()
    {
        var tab = new TabPage("Logging");

        _loggingGrid = new DataGridView
        {
            Location = new Point(20, 20),
            Size = new Size(820, 500),
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _loggingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Category",
            HeaderText = "Logger Category",
            Width = 400
        });

        var levelColumn = new DataGridViewComboBoxColumn
        {
            Name = "Level",
            HeaderText = "Log Level",
            Width = 150,
            DataSource = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" }
        };
        _loggingGrid.Columns.Add(levelColumn);

        tab.Controls.Add(_loggingGrid);

        var infoLabel = new Label
        {
            Text = "Common categories: Default, Microsoft.AspNetCore, Yarp, System",
            Location = new Point(20, 530),
            Size = new Size(820, 20),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(infoLabel);

        return tab;
    }

    private TabPage CreateServiceFabricTab()
    {
        var tab = new TabPage("Service Fabric");
        var y = 20;

        // Connection Endpoint
        AddLabel(tab, "Connection Endpoint:", 20, y);
        _sfEndpointTextBox = new TextBox
        {
            Location = new Point(220, y),
            Size = new Size(400, 25)
        };
        AddTooltip(_sfEndpointTextBox, "Service Fabric cluster endpoint (e.g., localhost:19000)");
        tab.Controls.Add(_sfEndpointTextBox);
        y += 40;

        // Server Cert Thumbprint
        AddLabel(tab, "Server Cert Thumbprint:", 20, y);
        _sfServerThumbprintTextBox = new TextBox
        {
            Location = new Point(220, y),
            Size = new Size(600, 25)
        };
        AddTooltip(_sfServerThumbprintTextBox, "SHA-1 or SHA-256 thumbprint of the cluster certificate");
        tab.Controls.Add(_sfServerThumbprintTextBox);
        y += 40;

        // Client Cert Thumbprint
        AddLabel(tab, "Client Cert Thumbprint:", 20, y);
        _sfClientThumbprintTextBox = new TextBox
        {
            Location = new Point(220, y),
            Size = new Size(600, 25)
        };
        AddTooltip(_sfClientThumbprintTextBox, "SHA-1 or SHA-256 thumbprint of the client certificate");
        tab.Controls.Add(_sfClientThumbprintTextBox);
        y += 40;

        // Store Location
        AddLabel(tab, "Certificate Store Location:", 20, y);
        _sfStoreLocationCombo = new ComboBox
        {
            Location = new Point(220, y),
            Size = new Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _sfStoreLocationCombo.Items.AddRange(new[] { "CurrentUser", "LocalMachine" });
        tab.Controls.Add(_sfStoreLocationCombo);
        y += 40;

        // Store Name
        AddLabel(tab, "Certificate Store Name:", 20, y);
        _sfStoreNameTextBox = new TextBox
        {
            Location = new Point(220, y),
            Size = new Size(200, 25)
        };
        AddTooltip(_sfStoreNameTextBox, "Certificate store name (e.g., My, Root, TrustedPeople)");
        tab.Controls.Add(_sfStoreNameTextBox);
        y += 40;

        // Protection Level
        AddLabel(tab, "Protection Level:", 20, y);
        _sfProtectionLevelCombo = new ComboBox
        {
            Location = new Point(220, y),
            Size = new Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _sfProtectionLevelCombo.Items.AddRange(new[] { "None", "Sign", "EncryptAndSign" });
        tab.Controls.Add(_sfProtectionLevelCombo);
        y += 40;

        var infoLabel = new Label
        {
            Text = "Leave certificate fields empty if not using certificate-based authentication",
            Location = new Point(20, y),
            Size = new Size(820, 40),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(infoLabel);

        return tab;
    }

    private TabPage CreateReverseProxyTab()
    {
        var tab = new TabPage("Reverse Proxy");

        var infoLabel = new Label
        {
            Text = "Advanced proxy configuration. Edit appsettings.json directly for complex routing scenarios.",
            Location = new Point(20, 20),
            Size = new Size(820, 40),
            ForeColor = Color.DarkOrange,
            Font = new Font(Font, FontStyle.Bold)
        };
        tab.Controls.Add(infoLabel);

        // Split container for routes and clusters
        var splitContainer = new SplitContainer
        {
            Location = new Point(20, 70),
            Size = new Size(820, 480),
            Orientation = Orientation.Horizontal
        };

        // Routes panel
        var routesLabel = new Label
        {
            Text = "Routes:",
            Location = new Point(5, 5),
            Size = new Size(100, 20)
        };
        splitContainer.Panel1.Controls.Add(routesLabel);

        _routesListBox = new ListBox
        {
            Location = new Point(5, 30),
            Size = new Size(200, splitContainer.Panel1.Height - 35)
        };
        splitContainer.Panel1.Controls.Add(_routesListBox);

        var routesInfo = new Label
        {
            Text = "Route configuration requires manual JSON editing.\nUse the main configuration file for complex routing rules.",
            Location = new Point(215, 30),
            Size = new Size(595, 150),
            ForeColor = Color.Gray
        };
        splitContainer.Panel1.Controls.Add(routesInfo);

        // Clusters panel
        var clustersLabel = new Label
        {
            Text = "Clusters:",
            Location = new Point(5, 5),
            Size = new Size(100, 20)
        };
        splitContainer.Panel2.Controls.Add(clustersLabel);

        _clustersListBox = new ListBox
        {
            Location = new Point(5, 30),
            Size = new Size(200, splitContainer.Panel2.Height - 35)
        };
        splitContainer.Panel2.Controls.Add(_clustersListBox);

        var clustersInfo = new Label
        {
            Text = "Cluster and destination configuration requires manual JSON editing.\nDefine backend servers, health checks, and load balancing in the main config.",
            Location = new Point(215, 30),
            Size = new Size(595, 150),
            ForeColor = Color.Gray
        };
        splitContainer.Panel2.Controls.Add(clustersInfo);

        tab.Controls.Add(splitContainer);

        return tab;
    }

    private TabPage CreateRateLimitingTab()
    {
        var tab = new TabPage("Rate Limiting");
        var y = 20;

        // Enabled checkbox
        _rateLimitingEnabledCheckBox = new CheckBox
        {
            Text = "Enable Global Rate Limiting",
            Location = new Point(20, y),
            Size = new Size(300, 25)
        };
        _rateLimitingEnabledCheckBox.CheckedChanged += (s, e) =>
        {
            _permitLimitNumeric.Enabled = _rateLimitingEnabledCheckBox.Checked;
            _windowTextBox.Enabled = _rateLimitingEnabledCheckBox.Checked;
            _queueLimitNumeric.Enabled = _rateLimitingEnabledCheckBox.Checked;
        };
        tab.Controls.Add(_rateLimitingEnabledCheckBox);
        y += 40;

        // Permit Limit
        AddLabel(tab, "Permit Limit:", 40, y);
        _permitLimitNumeric = new NumericUpDown
        {
            Location = new Point(220, y),
            Size = new Size(150, 25),
            Minimum = 1,
            Maximum = 1000000,
            Value = 100
        };
        AddTooltip(_permitLimitNumeric, "Maximum number of requests allowed within the time window");
        tab.Controls.Add(_permitLimitNumeric);
        y += 40;

        // Time Window
        AddLabel(tab, "Time Window:", 40, y);
        _windowTextBox = new TextBox
        {
            Location = new Point(220, y),
            Size = new Size(150, 25),
            Text = "00:01:00"
        };
        AddTooltip(_windowTextBox, "Time window for rate limiting (format: HH:MM:SS or days.HH:MM:SS)");
        tab.Controls.Add(_windowTextBox);

        var windowExampleLabel = new Label
        {
            Text = "Example: 00:01:00 (1 minute), 01:00:00 (1 hour)",
            Location = new Point(380, y),
            Size = new Size(400, 25),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(windowExampleLabel);
        y += 40;

        // Queue Limit
        AddLabel(tab, "Queue Limit:", 40, y);
        _queueLimitNumeric = new NumericUpDown
        {
            Location = new Point(220, y),
            Size = new Size(150, 25),
            Minimum = 0,
            Maximum = 1000000,
            Value = 0
        };
        AddTooltip(_queueLimitNumeric, "Maximum number of queued requests (0 to reject immediately)");
        tab.Controls.Add(_queueLimitNumeric);

        return tab;
    }

    private void AddLabel(Control parent, string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            Size = new Size(170, 20),
            TextAlign = ContentAlignment.MiddleRight
        };
        parent.Controls.Add(label);
    }

    private void AddTooltip(Control control, string text)
    {
        var tooltip = new ToolTip();
        tooltip.SetToolTip(control, text);
    }

    private void LoadConfiguration()
    {
        // General
        _urlsTextBox.Text = _configuration.Urls;
        _allowedHostsTextBox.Text = _configuration.AllowedHosts;

        // Logging
        _loggingGrid.Rows.Clear();
        if (_configuration.Logging?.LogLevel != null)
        {
            foreach (var (category, level) in _configuration.Logging.LogLevel)
            {
                _loggingGrid.Rows.Add(category, level);
            }
        }

        // Service Fabric
        if (_configuration.ServiceFabric != null)
        {
            _sfEndpointTextBox.Text = _configuration.ServiceFabric.ConnectionEndpoint;
            _sfServerThumbprintTextBox.Text = _configuration.ServiceFabric.ServerCertThumbprint ?? "";
            _sfClientThumbprintTextBox.Text = _configuration.ServiceFabric.ClientCertThumbprint ?? "";
            _sfStoreLocationCombo.SelectedItem = _configuration.ServiceFabric.StoreLocation;
            _sfStoreNameTextBox.Text = _configuration.ServiceFabric.StoreName;
            _sfProtectionLevelCombo.SelectedItem = _configuration.ServiceFabric.ProtectionLevel;
        }
        else
        {
            _sfStoreLocationCombo.SelectedIndex = 0;
            _sfProtectionLevelCombo.SelectedIndex = 2; // EncryptAndSign
        }

        // Reverse Proxy
        if (_configuration.ReverseProxy != null)
        {
            _routesListBox.Items.Clear();
            foreach (var routeId in _configuration.ReverseProxy.Routes.Keys)
            {
                _routesListBox.Items.Add(routeId);
            }

            _clustersListBox.Items.Clear();
            foreach (var clusterId in _configuration.ReverseProxy.Clusters.Keys)
            {
                _clustersListBox.Items.Add(clusterId);
            }
        }

        // Rate Limiting
        if (_configuration.RateLimiting?.GlobalLimiter != null)
        {
            _rateLimitingEnabledCheckBox.Checked = true;
            _permitLimitNumeric.Value = _configuration.RateLimiting.GlobalLimiter.PermitLimit;
            _windowTextBox.Text = _configuration.RateLimiting.GlobalLimiter.Window;
            _queueLimitNumeric.Value = _configuration.RateLimiting.GlobalLimiter.QueueLimit;
        }
        else
        {
            _rateLimitingEnabledCheckBox.Checked = false;
            _permitLimitNumeric.Enabled = false;
            _windowTextBox.Enabled = false;
            _queueLimitNumeric.Enabled = false;
        }
    }

    private void SaveConfiguration()
    {
        // General
        _configuration.Urls = _urlsTextBox.Text.Trim();
        _configuration.AllowedHosts = _allowedHostsTextBox.Text.Trim();

        // Logging
        _configuration.Logging.LogLevel.Clear();
        foreach (DataGridViewRow row in _loggingGrid.Rows)
        {
            if (row.Cells[0].Value != null && row.Cells[1].Value != null)
            {
                var category = row.Cells[0].Value.ToString();
                var level = row.Cells[1].Value.ToString();
                if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(level))
                {
                    _configuration.Logging.LogLevel[category] = level;
                }
            }
        }

        // Service Fabric
        if (!string.IsNullOrWhiteSpace(_sfEndpointTextBox.Text))
        {
            _configuration.ServiceFabric ??= new ServiceFabricConfiguration();
            _configuration.ServiceFabric.ConnectionEndpoint = _sfEndpointTextBox.Text.Trim();
            _configuration.ServiceFabric.ServerCertThumbprint = string.IsNullOrWhiteSpace(_sfServerThumbprintTextBox.Text) ? null : _sfServerThumbprintTextBox.Text.Trim();
            _configuration.ServiceFabric.ClientCertThumbprint = string.IsNullOrWhiteSpace(_sfClientThumbprintTextBox.Text) ? null : _sfClientThumbprintTextBox.Text.Trim();
            _configuration.ServiceFabric.StoreLocation = _sfStoreLocationCombo.SelectedItem?.ToString() ?? "CurrentUser";
            _configuration.ServiceFabric.StoreName = _sfStoreNameTextBox.Text.Trim();
            _configuration.ServiceFabric.ProtectionLevel = _sfProtectionLevelCombo.SelectedItem?.ToString() ?? "EncryptAndSign";
        }

        // Rate Limiting
        if (_rateLimitingEnabledCheckBox.Checked)
        {
            _configuration.RateLimiting ??= new RateLimitingConfiguration();
            _configuration.RateLimiting.GlobalLimiter = new GlobalRateLimiterConfiguration
            {
                PermitLimit = (int)_permitLimitNumeric.Value,
                Window = _windowTextBox.Text.Trim(),
                QueueLimit = (int)_queueLimitNumeric.Value
            };
        }
        else
        {
            _configuration.RateLimiting = null;
        }
    }

    private void ValidateButton_Click(object? sender, EventArgs e)
    {
        try
        {
            SaveConfiguration();
            var errors = _configManager.Validate(_configuration);

            if (errors.Count == 0)
            {
                MessageBox.Show(
                    "Configuration is valid! ✓\n\nNo errors found.",
                    "Validation Successful",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            else
            {
                MessageBox.Show(
                    $"Validation found {errors.Count} error(s):\n\n{string.Join("\n", errors)}",
                    "Validation Errors",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Validation failed:\n\n{ex.Message}",
                "Validation Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        try
        {
            SaveConfiguration();
            _configManager.Save(_configuration);

            MessageBox.Show(
                "Configuration saved successfully!\n\nRestart the service to apply changes.",
                "Save Successful",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ValidationException vex)
        {
            MessageBox.Show(
                $"Configuration validation failed:\n\n{vex.Message}",
                "Validation Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save configuration:\n\n{ex.Message}",
                "Save Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void RestartServiceButton_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Do you want to save the configuration and restart the service now?",
            "Restart Service",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question
        );

        if (result == DialogResult.Yes)
        {
            try
            {
                SaveConfiguration();
                _configManager.Save(_configuration);

                // Restart service
                using var service = new System.ServiceProcess.ServiceController("AcetoneV2Proxy");
                if (service.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    service.Stop();
                    service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
                service.Start();
                service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

                MessageBox.Show(
                    "Configuration saved and service restarted successfully!",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to restart service:\n\n{ex.Message}\n\nYou may need to restart manually with administrator privileges.",
                    "Restart Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}

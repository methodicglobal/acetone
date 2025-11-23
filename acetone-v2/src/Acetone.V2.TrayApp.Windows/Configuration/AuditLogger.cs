using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acetone.V2.TrayApp.Windows.Configuration;

/// <summary>
/// Provides append-only audit logging for configuration changes.
/// Logs include timestamp, user, field changes, and tamper detection.
/// </summary>
public class AuditLogger
{
    private readonly string _auditLogPath;
    private readonly object _logLock = new object();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false, // Compact single-line format for log entries
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AuditLogger(string auditLogPath)
    {
        _auditLogPath = auditLogPath;

        // Ensure audit log directory exists
        var directory = Path.GetDirectoryName(_auditLogPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Logs a configuration change with full audit details.
    /// </summary>
    public void LogConfigurationChange(
        AcetoneConfiguration oldConfig,
        AcetoneConfiguration newConfig,
        bool success,
        string? errorMessage = null)
    {
        var changes = DetectChanges(oldConfig, newConfig);

        var auditEntry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = "ConfigurationChange",
            User = Environment.UserName,
            MachineName = Environment.MachineName,
            Success = success,
            ErrorMessage = errorMessage,
            ChangeCount = changes.Count,
            Changes = changes
        };

        WriteAuditEntry(auditEntry);
    }

    /// <summary>
    /// Logs a validation failure.
    /// </summary>
    public void LogValidationFailure(List<string> validationErrors)
    {
        var auditEntry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = "ValidationFailure",
            User = Environment.UserName,
            MachineName = Environment.MachineName,
            Success = false,
            ErrorMessage = string.Join("; ", validationErrors),
            ChangeCount = 0,
            Changes = new List<ConfigurationChange>()
        };

        WriteAuditEntry(auditEntry);
    }

    /// <summary>
    /// Logs a configuration load event.
    /// </summary>
    public void LogConfigurationLoad(bool success, string? errorMessage = null)
    {
        var auditEntry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = "ConfigurationLoad",
            User = Environment.UserName,
            MachineName = Environment.MachineName,
            Success = success,
            ErrorMessage = errorMessage,
            ChangeCount = 0,
            Changes = new List<ConfigurationChange>()
        };

        WriteAuditEntry(auditEntry);
    }

    /// <summary>
    /// Writes an audit entry to the log file with tamper detection hash.
    /// </summary>
    private void WriteAuditEntry(AuditLogEntry entry)
    {
        lock (_logLock)
        {
            try
            {
                // Calculate hash of previous log entry for chain integrity
                entry.PreviousEntryHash = GetLastEntryHash();

                // Serialize entry
                var entryJson = JsonSerializer.Serialize(entry, _jsonOptions);

                // Calculate hash of this entry for next entry's validation
                entry.EntryHash = CalculateHash(entryJson);

                // Re-serialize with hash included
                entryJson = JsonSerializer.Serialize(entry, _jsonOptions);

                // Append to log file (create if doesn't exist)
                File.AppendAllText(_auditLogPath, entryJson + Environment.NewLine);

                // Check if rotation is needed (e.g., > 10 MB)
                RotateIfNeeded();
            }
            catch (Exception ex)
            {
                // Audit logging failure should not break the application
                // Log to Windows Event Log or console as fallback
                Console.Error.WriteLine($"Audit logging failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Detects changes between old and new configuration.
    /// </summary>
    private List<ConfigurationChange> DetectChanges(
        AcetoneConfiguration oldConfig,
        AcetoneConfiguration newConfig)
    {
        var changes = new List<ConfigurationChange>();

        // Compare URLs
        if (oldConfig.Urls != newConfig.Urls)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "Urls",
                OldValue = MaskIfSensitive("Urls", oldConfig.Urls),
                NewValue = MaskIfSensitive("Urls", newConfig.Urls)
            });
        }

        // Compare AllowedHosts
        if (oldConfig.AllowedHosts != newConfig.AllowedHosts)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "AllowedHosts",
                OldValue = oldConfig.AllowedHosts,
                NewValue = newConfig.AllowedHosts
            });
        }

        // Compare Service Fabric config
        if (oldConfig.ServiceFabric != null || newConfig.ServiceFabric != null)
        {
            CompareServiceFabricConfig(
                oldConfig.ServiceFabric,
                newConfig.ServiceFabric,
                changes);
        }

        // Compare Reverse Proxy routes and clusters
        if (oldConfig.ReverseProxy != null || newConfig.ReverseProxy != null)
        {
            CompareReverseProxyConfig(
                oldConfig.ReverseProxy,
                newConfig.ReverseProxy,
                changes);
        }

        // Compare Rate Limiting
        if (oldConfig.RateLimiting != null || newConfig.RateLimiting != null)
        {
            CompareRateLimitingConfig(
                oldConfig.RateLimiting,
                newConfig.RateLimiting,
                changes);
        }

        return changes;
    }

    private void CompareServiceFabricConfig(
        ServiceFabricConfiguration? oldSf,
        ServiceFabricConfiguration? newSf,
        List<ConfigurationChange> changes)
    {
        if (oldSf == null && newSf != null)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric",
                OldValue = null,
                NewValue = "[Service Fabric configuration added]"
            });
            return;
        }

        if (oldSf != null && newSf == null)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric",
                OldValue = "[Service Fabric configuration existed]",
                NewValue = null
            });
            return;
        }

        if (oldSf == null || newSf == null) return;

        // Compare individual fields (mask sensitive ones)
        if (oldSf.ConnectionEndpoint != newSf.ConnectionEndpoint)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric.ConnectionEndpoint",
                OldValue = "***MASKED***",
                NewValue = "***CHANGED***"
            });
        }

        if (oldSf.ServerCertThumbprint != newSf.ServerCertThumbprint)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric.ServerCertThumbprint",
                OldValue = "***MASKED***",
                NewValue = "***CHANGED***"
            });
        }

        if (oldSf.ClientCertThumbprint != newSf.ClientCertThumbprint)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric.ClientCertThumbprint",
                OldValue = "***MASKED***",
                NewValue = "***CHANGED***"
            });
        }

        if (oldSf.ProtectionLevel != newSf.ProtectionLevel)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric.ProtectionLevel",
                OldValue = oldSf.ProtectionLevel,
                NewValue = newSf.ProtectionLevel
            });
        }
    }

    private void CompareReverseProxyConfig(
        ReverseProxyConfiguration? oldProxy,
        ReverseProxyConfiguration? newProxy,
        List<ConfigurationChange> changes)
    {
        if (oldProxy == null && newProxy != null)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ReverseProxy",
                OldValue = null,
                NewValue = $"[Added {newProxy.Routes.Count} routes, {newProxy.Clusters.Count} clusters]"
            });
            return;
        }

        if (oldProxy != null && newProxy == null)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ReverseProxy",
                OldValue = $"[Had {oldProxy.Routes.Count} routes, {oldProxy.Clusters.Count} clusters]",
                NewValue = null
            });
            return;
        }

        if (oldProxy == null || newProxy == null) return;

        // Detect route changes
        var oldRouteIds = oldProxy.Routes.Keys.ToHashSet();
        var newRouteIds = newProxy.Routes.Keys.ToHashSet();

        foreach (var routeId in newRouteIds.Except(oldRouteIds))
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = $"ReverseProxy.Routes.{routeId}",
                OldValue = null,
                NewValue = "[Route added]"
            });
        }

        foreach (var routeId in oldRouteIds.Except(newRouteIds))
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = $"ReverseProxy.Routes.{routeId}",
                OldValue = "[Route existed]",
                NewValue = null
            });
        }

        // Detect cluster changes
        var oldClusterIds = oldProxy.Clusters.Keys.ToHashSet();
        var newClusterIds = newProxy.Clusters.Keys.ToHashSet();

        foreach (var clusterId in newClusterIds.Except(oldClusterIds))
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = $"ReverseProxy.Clusters.{clusterId}",
                OldValue = null,
                NewValue = "[Cluster added]"
            });
        }

        foreach (var clusterId in oldClusterIds.Except(newClusterIds))
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = $"ReverseProxy.Clusters.{clusterId}",
                OldValue = "[Cluster existed]",
                NewValue = null
            });
        }
    }

    private void CompareRateLimitingConfig(
        RateLimitingConfiguration? oldRl,
        RateLimitingConfiguration? newRl,
        List<ConfigurationChange> changes)
    {
        if (oldRl?.GlobalLimiter != null && newRl?.GlobalLimiter != null)
        {
            if (oldRl.GlobalLimiter.PermitLimit != newRl.GlobalLimiter.PermitLimit)
            {
                changes.Add(new ConfigurationChange
                {
                    FieldPath = "RateLimiting.GlobalLimiter.PermitLimit",
                    OldValue = oldRl.GlobalLimiter.PermitLimit.ToString(),
                    NewValue = newRl.GlobalLimiter.PermitLimit.ToString()
                });
            }

            if (oldRl.GlobalLimiter.Window != newRl.GlobalLimiter.Window)
            {
                changes.Add(new ConfigurationChange
                {
                    FieldPath = "RateLimiting.GlobalLimiter.Window",
                    OldValue = oldRl.GlobalLimiter.Window,
                    NewValue = newRl.GlobalLimiter.Window
                });
            }
        }
    }

    private string? MaskIfSensitive(string fieldName, string? value)
    {
        // Don't log sensitive values in audit
        var sensitiveFields = new[] { "ConnectionEndpoint", "Thumbprint", "Password", "Secret", "Key" };
        if (sensitiveFields.Any(s => fieldName.Contains(s, StringComparison.OrdinalIgnoreCase)))
        {
            return value == null ? null : "***MASKED***";
        }
        return value;
    }

    private string? GetLastEntryHash()
    {
        if (!File.Exists(_auditLogPath))
            return null;

        try
        {
            var lines = File.ReadAllLines(_auditLogPath);
            if (lines.Length == 0)
                return null;

            var lastLine = lines[^1];
            var lastEntry = JsonSerializer.Deserialize<AuditLogEntry>(lastLine);
            return lastEntry?.EntryHash;
        }
        catch
        {
            return null;
        }
    }

    private string CalculateHash(string content)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes);
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_auditLogPath))
            return;

        var fileInfo = new FileInfo(_auditLogPath);
        const long maxSizeBytes = 10 * 1024 * 1024; // 10 MB

        if (fileInfo.Length > maxSizeBytes)
        {
            var rotatedPath = $"{_auditLogPath}.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(_auditLogPath, rotatedPath);
        }
    }
}

/// <summary>
/// Represents a single audit log entry.
/// </summary>
public class AuditLogEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("changeCount")]
    public int ChangeCount { get; set; }

    [JsonPropertyName("changes")]
    public List<ConfigurationChange> Changes { get; set; } = new();

    [JsonPropertyName("entryHash")]
    public string? EntryHash { get; set; }

    [JsonPropertyName("previousEntryHash")]
    public string? PreviousEntryHash { get; set; }
}

/// <summary>
/// Represents a single configuration change.
/// </summary>
public class ConfigurationChange
{
    [JsonPropertyName("fieldPath")]
    public string FieldPath { get; set; } = string.Empty;

    [JsonPropertyName("oldValue")]
    public string? OldValue { get; set; }

    [JsonPropertyName("newValue")]
    public string? NewValue { get; set; }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acetone.V2.TrayApp.Linux.Configuration;

/// <summary>
/// Provides append-only audit logging for configuration changes on Linux.
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
                Console.Error.WriteLine($"Audit logging failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Detects changes between old and new configuration (basic version for Linux).
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
                OldValue = oldConfig.Urls,
                NewValue = newConfig.Urls
            });
        }

        // Compare Service Fabric config
        var oldSf = oldConfig.ServiceFabric;
        var newSf = newConfig.ServiceFabric;

        if (oldSf == null && newSf != null)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric",
                OldValue = null,
                NewValue = "[Service Fabric configuration added]"
            });
        }
        else if (oldSf != null && newSf == null)
        {
            changes.Add(new ConfigurationChange
            {
                FieldPath = "ServiceFabric",
                OldValue = "[Service Fabric configuration existed]",
                NewValue = null
            });
        }
        else if (oldSf != null && newSf != null)
        {
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
        }

        return changes;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Acetone.V2.TrayApp.Linux.Configuration;

/// <summary>
/// Enhanced configuration manager with encryption support for sensitive data on Linux.
/// Uses AES encryption with a machine-specific key derived from /etc/machine-id.
/// </summary>
public class SecureConfigurationManager
{
    private readonly string _configPath;
    private const string EncryptedPrefix = "ENC:";
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("Acetone.V2.SecureConfig.Linux");

    // Cache for machine key to avoid reading file multiple times
    private static byte[]? _cachedMachineKey;
    private static readonly object _keyLock = new object();

    public SecureConfigurationManager(string configPath)
    {
        _configPath = configPath;
    }

    /// <summary>
    /// Loads configuration and decrypts sensitive fields marked with [Sensitive] attribute.
    /// </summary>
    public AcetoneConfiguration Load()
    {
        if (!File.Exists(_configPath))
        {
            throw new FileNotFoundException($"Configuration file not found: {_configPath}");
        }

        var json = File.ReadAllText(_configPath);
        var config = JsonSerializer.Deserialize<AcetoneConfiguration>(json)
                    ?? new AcetoneConfiguration();

        DecryptSensitiveFields(config);
        return config;
    }

    /// <summary>
    /// Encrypts sensitive fields and saves configuration.
    /// </summary>
    public void Save(AcetoneConfiguration configuration)
    {
        // Validate first
        var errors = Validate(configuration);
        if (errors.Count > 0)
        {
            throw new ValidationException($"Configuration validation failed:\n{string.Join("\n", errors)}");
        }

        // Clone the configuration to avoid modifying the original
        var json = JsonSerializer.Serialize(configuration);
        var configToSave = JsonSerializer.Deserialize<AcetoneConfiguration>(json)
                          ?? throw new InvalidOperationException("Failed to clone configuration");

        EncryptSensitiveFields(configToSave);

        // Backup existing configuration
        if (File.Exists(_configPath))
        {
            var backupPath = $"{_configPath}.backup.{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_configPath, backupPath, true);
        }

        // Save with indented formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        var outputJson = JsonSerializer.Serialize(configToSave, options);
        File.WriteAllText(_configPath, outputJson);
    }

    /// <summary>
    /// Validates configuration (basic validation, can be extended).
    /// </summary>
    public List<string> Validate(AcetoneConfiguration configuration)
    {
        var errors = new List<string>();

        // Basic URL validation
        if (string.IsNullOrWhiteSpace(configuration.Urls))
        {
            errors.Add("Urls cannot be empty");
        }

        return errors;
    }

    /// <summary>
    /// Encrypts all properties marked with [Sensitive] attribute.
    /// </summary>
    private void EncryptSensitiveFields(object obj)
    {
        if (obj == null) return;

        var type = obj.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Check if property is marked as sensitive
            if (property.GetCustomAttribute<SensitiveAttribute>() != null)
            {
                if (property.PropertyType == typeof(string))
                {
                    var value = property.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(value) && !value.StartsWith(EncryptedPrefix))
                    {
                        var encrypted = EncryptString(value);
                        property.SetValue(obj, encrypted);
                    }
                }
            }
            // Recursively process nested objects
            else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
            {
                var nestedObj = property.GetValue(obj);
                if (nestedObj != null)
                {
                    // Handle dictionaries
                    if (nestedObj is System.Collections.IDictionary)
                    {
                        var dict = nestedObj as System.Collections.IDictionary;
                        if (dict != null)
                        {
                            foreach (var key in dict.Keys.Cast<object>().ToList())
                            {
                                var dictValue = dict[key];
                                if (dictValue != null && dictValue.GetType().IsClass && dictValue.GetType() != typeof(string))
                                {
                                    EncryptSensitiveFields(dictValue);
                                }
                            }
                        }
                    }
                    // Handle lists
                    else if (nestedObj is System.Collections.IEnumerable enumerable
                            && nestedObj.GetType().IsGenericType)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item != null && item.GetType().IsClass && item.GetType() != typeof(string))
                            {
                                EncryptSensitiveFields(item);
                            }
                        }
                    }
                    else
                    {
                        EncryptSensitiveFields(nestedObj);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Decrypts all properties marked with [Sensitive] attribute.
    /// </summary>
    private void DecryptSensitiveFields(object obj)
    {
        if (obj == null) return;

        var type = obj.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Check if property is marked as sensitive
            if (property.GetCustomAttribute<SensitiveAttribute>() != null)
            {
                if (property.PropertyType == typeof(string))
                {
                    var value = property.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix))
                    {
                        try
                        {
                            var decrypted = DecryptString(value);
                            property.SetValue(obj, decrypted);
                        }
                        catch (CryptographicException ex)
                        {
                            throw new InvalidOperationException(
                                $"Failed to decrypt sensitive field '{property.Name}'. " +
                                "Configuration may have been encrypted on a different machine.", ex);
                        }
                    }
                }
            }
            // Recursively process nested objects
            else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
            {
                var nestedObj = property.GetValue(obj);
                if (nestedObj != null)
                {
                    // Handle dictionaries
                    if (nestedObj is System.Collections.IDictionary)
                    {
                        var dict = nestedObj as System.Collections.IDictionary;
                        if (dict != null)
                        {
                            foreach (var key in dict.Keys.Cast<object>().ToList())
                            {
                                var dictValue = dict[key];
                                if (dictValue != null && dictValue.GetType().IsClass && dictValue.GetType() != typeof(string))
                                {
                                    DecryptSensitiveFields(dictValue);
                                }
                            }
                        }
                    }
                    // Handle lists
                    else if (nestedObj is System.Collections.IEnumerable enumerable
                            && nestedObj.GetType().IsGenericType)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item != null && item.GetType().IsClass && item.GetType() != typeof(string))
                            {
                                DecryptSensitiveFields(item);
                            }
                        }
                    }
                    else
                    {
                        DecryptSensitiveFields(nestedObj);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets or generates a machine-specific encryption key.
    /// Uses /etc/machine-id for Linux systems, falls back to /var/lib/dbus/machine-id.
    /// </summary>
    private byte[] GetMachineKey()
    {
        lock (_keyLock)
        {
            if (_cachedMachineKey != null)
                return _cachedMachineKey;

            string? machineId = null;

            // Try /etc/machine-id first (systemd standard)
            if (File.Exists("/etc/machine-id"))
            {
                machineId = File.ReadAllText("/etc/machine-id").Trim();
            }
            // Fallback to /var/lib/dbus/machine-id
            else if (File.Exists("/var/lib/dbus/machine-id"))
            {
                machineId = File.ReadAllText("/var/lib/dbus/machine-id").Trim();
            }

            if (string.IsNullOrWhiteSpace(machineId))
            {
                throw new InvalidOperationException(
                    "Cannot derive encryption key: machine-id not found. " +
                    "This is required for encrypting sensitive configuration data.");
            }

            // Derive a proper encryption key using PBKDF2
            using var pbkdf2 = new Rfc2898DeriveBytes(
                machineId,
                Salt,
                iterations: 10000,
                HashAlgorithmName.SHA256);

            _cachedMachineKey = pbkdf2.GetBytes(32); // 256-bit key for AES-256
            return _cachedMachineKey;
        }
    }

    /// <summary>
    /// Encrypts a string using AES-256-GCM.
    /// </summary>
    private string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        try
        {
            var key = GetMachineKey();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);

            using var aes = new AesGcm(key);

            // Generate random nonce (12 bytes for GCM)
            var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            RandomNumberGenerator.Fill(nonce);

            // Prepare output buffer
            var ciphertext = new byte[plainBytes.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];

            // Encrypt
            aes.Encrypt(nonce, plainBytes, ciphertext, tag);

            // Combine nonce + tag + ciphertext
            var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

            return EncryptedPrefix + Convert.ToBase64String(combined);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to encrypt sensitive data", ex);
        }
    }

    /// <summary>
    /// Decrypts a string using AES-256-GCM.
    /// </summary>
    private string DecryptString(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText) || !encryptedText.StartsWith(EncryptedPrefix))
            return encryptedText;

        try
        {
            var key = GetMachineKey();
            var base64Text = encryptedText.Substring(EncryptedPrefix.Length);
            var combined = Convert.FromBase64String(base64Text);

            // Extract nonce, tag, and ciphertext
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var ciphertext = new byte[combined.Length - nonceSize - tagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(combined, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(combined, nonceSize + tagSize, ciphertext, 0, ciphertext.Length);

            using var aes = new AesGcm(key);
            var plaintext = new byte[ciphertext.Length];

            // Decrypt
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to decrypt sensitive data", ex);
        }
    }
}

public class AcetoneConfiguration
{
    public string Urls { get; set; } = "http://0.0.0.0:8080";
    public ServiceFabricConfiguration? ServiceFabric { get; set; }
}

public class ServiceFabricConfiguration
{
    [Sensitive]
    public string ConnectionEndpoint { get; set; } = "localhost:19000";

    [Sensitive]
    public string? ServerCertThumbprint { get; set; }

    [Sensitive]
    public string? ClientCertThumbprint { get; set; }
}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}

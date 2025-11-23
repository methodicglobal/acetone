using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acetone.V2.TrayApp.Windows.Configuration;

/// <summary>
/// Enhanced configuration manager with encryption support for sensitive data.
/// Uses Windows Data Protection API (DPAPI) to encrypt sensitive configuration values.
/// </summary>
public class SecureConfigurationManager : ConfigurationManager
{
    private const string EncryptedPrefix = "ENC:";
    private static readonly byte[] AdditionalEntropy = Encoding.UTF8.GetBytes("Acetone.V2.SecureConfig");

    public SecureConfigurationManager(string configPath) : base(configPath)
    {
    }

    /// <summary>
    /// Loads configuration and decrypts sensitive fields marked with [Sensitive] attribute.
    /// </summary>
    public new AcetoneConfiguration Load()
    {
        var config = base.Load();
        DecryptSensitiveFields(config);
        return config;
    }

    /// <summary>
    /// Encrypts sensitive fields and saves configuration.
    /// </summary>
    public new void Save(AcetoneConfiguration configuration)
    {
        // Clone the configuration to avoid modifying the original
        var json = JsonSerializer.Serialize(configuration);
        var configToSave = JsonSerializer.Deserialize<AcetoneConfiguration>(json)
                          ?? throw new InvalidOperationException("Failed to clone configuration");

        EncryptSensitiveFields(configToSave);

        // Use base save which includes validation
        base.Save(configToSave);
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
                        // For dictionaries, we need to encrypt values if they're complex objects
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
                                "Configuration may have been encrypted on a different machine or user account.", ex);
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
    /// Encrypts a string using Windows Data Protection API (DPAPI).
    /// Data is protected for the current user on the current machine.
    /// </summary>
    private string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectedData.Protect(
                plainBytes,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);

            return EncryptedPrefix + Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to encrypt sensitive data", ex);
        }
    }

    /// <summary>
    /// Decrypts a string using Windows Data Protection API (DPAPI).
    /// </summary>
    private string DecryptString(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText) || !encryptedText.StartsWith(EncryptedPrefix))
            return encryptedText;

        try
        {
            var base64Text = encryptedText.Substring(EncryptedPrefix.Length);
            var encryptedBytes = Convert.FromBase64String(base64Text);
            var decryptedBytes = ProtectedData.Unprotect(
                encryptedBytes,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to decrypt sensitive data", ex);
        }
    }
}

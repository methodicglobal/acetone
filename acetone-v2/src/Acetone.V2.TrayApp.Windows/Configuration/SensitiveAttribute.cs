using System;

namespace Acetone.V2.TrayApp.Windows.Configuration;

/// <summary>
/// Marks a configuration property as containing sensitive data that should be encrypted at rest.
/// Used for credentials, certificates, connection strings, and other secrets.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class SensitiveAttribute : Attribute
{
}

# Acetone V2 Security Features

## Configuration Encryption

Acetone V2 automatically encrypts sensitive configuration data at rest to protect credentials, certificates, and connection strings from unauthorized access.

### What Gets Encrypted

The following sensitive fields are automatically encrypted when saved:

- **Service Fabric Configuration**
  - Connection endpoints
  - Server certificate thumbprints
  - Client certificate thumbprints

### How It Works

#### Windows
- Uses **Windows Data Protection API (DPAPI)**
- Data is protected for the current user account on the current machine
- Encrypted data is bound to the Windows user profile
- Configuration cannot be decrypted if copied to another machine or user account

#### Linux
- Uses **AES-256-GCM encryption**
- Encryption key is derived from `/etc/machine-id` using PBKDF2 (10,000 iterations)
- Provides authenticated encryption with additional data protection
- Configuration cannot be decrypted if copied to another machine

### Encrypted Data Format

Encrypted values are stored with the prefix `ENC:` followed by Base64-encoded ciphertext:

```json
{
  "ServiceFabric": {
    "ConnectionEndpoint": "ENC:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA...",
    "ServerCertThumbprint": "ENC:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA..."
  }
}
```

### Automatic Encryption

When you save configuration through the tray application's configuration editor:

1. **Validation** - Configuration is validated for correctness
2. **Backup** - Existing configuration is backed up with timestamp
3. **Encryption** - Sensitive fields are automatically encrypted
4. **Save** - Encrypted configuration is written to disk

### Automatic Decryption

When the tray application loads configuration:

1. **Load** - Configuration JSON is read from disk
2. **Decryption** - Fields marked with `ENC:` prefix are automatically decrypted
3. **In-Memory** - Decrypted values are kept in memory for use
4. **Display** - Decrypted values are shown in the configuration editor

### Important Security Notes

#### Machine Binding
⚠️ **Encrypted configuration is bound to the machine (and user on Windows) where it was created.**

- Copying `appsettings.json` to another machine will fail to decrypt
- On Windows, copying to another user account will fail to decrypt
- This is intentional for security

#### Backup Files
🔒 **Automatic backups contain encrypted data**

- Backups are created in format: `appsettings.json.backup.yyyyMMddHHmmss`
- Backup files also contain encrypted sensitive data
- Keep backups secure as they preserve configuration history

#### Migration Between Machines

To migrate configuration to a new machine:

1. **Option A: Edit and re-save**
   - Copy the configuration file to the new machine
   - Decryption will fail on first load
   - Manually edit the file to remove `ENC:` prefixes
   - Enter values as plain text
   - Save through the tray app - will re-encrypt for new machine

2. **Option B: Use unencrypted template**
   - Use `appsettings.example.json` as starting point
   - Fill in your values as plain text
   - Save through the tray app - will encrypt automatically

### Fintech Compliance

This encryption approach satisfies common fintech requirements:

✅ **Data at Rest Protection** - Configuration files on disk are encrypted
✅ **Credential Protection** - Certificates and connection strings encrypted
✅ **Tamper Detection** - GCM mode (Linux) provides authentication
✅ **Audit Trail** - Configuration changes create timestamped backups
✅ **Defense in Depth** - Even with file access, credentials stay protected

### Limitations

⚠️ **This encryption protects against:**
- Unauthorized file access
- Configuration files committed to source control
- Accidental exposure via backups

⚠️ **This encryption does NOT protect against:**
- Malware running as the same user (can decrypt in memory)
- Attackers with user account access on the same machine
- Memory dumps while the tray application is running
- Privileged attackers (root/Administrator)

### Best Practices

1. **File Permissions**
   - Ensure `appsettings.json` has restricted permissions
   - Windows: Only current user should have read/write access
   - Linux: Set permissions to `600` or `640`

2. **Backup Security**
   - Keep backup files in the same secure location
   - Consider cleaning old backups periodically
   - Never commit backups to source control

3. **Development vs Production**
   - Use different certificates for development and production
   - Never reuse production credentials in development
   - Consider using Azure Key Vault or HashiCorp Vault for production

4. **Certificate Management**
   - Regularly rotate certificates before expiration
   - Update thumbprints in configuration when rotating
   - Test certificate changes in non-production first

5. **Audit Configuration Changes**
   - Review backup files to track configuration history
   - Implement additional audit logging for production (see AUDIT_LOG.md)
   - Monitor for unexpected configuration changes

## Additional Security Features

### Validation Before Save

All configuration is validated before being saved:

- URL format validation
- Certificate thumbprint format validation
- Route and cluster relationship validation
- Health check parameter validation
- Rate limiting parameter validation

Invalid configuration is rejected before encryption occurs.

### Automatic Backup

Every configuration save creates a timestamped backup:

```
/opt/acetone/appsettings.json.backup.20251123143022
```

This provides rollback capability and change history.

## Troubleshooting

### "Failed to decrypt sensitive field"

**Cause**: Configuration was encrypted on a different machine or user account.

**Solution**:
1. Remove the encrypted configuration
2. Use `appsettings.example.json` as template
3. Enter values as plain text
4. Save through tray app to re-encrypt for current environment

### "Cannot derive encryption key: machine-id not found" (Linux)

**Cause**: System is missing `/etc/machine-id`.

**Solution**:
1. Check if systemd is installed: `systemctl --version`
2. Regenerate machine-id: `sudo systemd-machine-id-setup`
3. Verify file exists: `cat /etc/machine-id`

### Configuration not encrypting

**Cause**: Manually editing `appsettings.json` directly.

**Solution**:
- Use the tray application's configuration editor
- Encryption only occurs when saving through the tray app
- Manual edits bypass encryption

## Further Reading

- **Audit Logging**: See `AUDIT_LOG.md` for configuration change auditing
- **Validation**: See `deployment/README.md` for validation details
- **Installation**: See `deployment/README.md` for setup instructions

# Acetone V2 Audit Logging

## Overview

Acetone V2 automatically logs all configuration changes to an append-only audit log for compliance, troubleshooting, and security monitoring. This feature is essential for fintech deployments requiring change tracking and accountability.

## Audit Log Location

### Windows
```
C:\ProgramData\Acetone\acetone-audit.log
```

### Linux
```
/opt/acetone/acetone-audit.log
```

## What Gets Logged

The audit system captures three types of events:

### 1. Configuration Changes
Logged whenever configuration is successfully saved or fails to save:

- **Timestamp** (UTC)
- **User** who made the change (Windows username / Linux username)
- **Machine name** where change was made
- **Success/failure** status
- **Field-level changes** showing what was modified
- **Change count**
- **Error message** (if failed)

### 2. Validation Failures
Logged when configuration fails validation before saving:

- **Timestamp** (UTC)
- **User** who attempted the change
- **Machine name**
- **Validation errors** encountered

### 3. Configuration Loads
Logged when configuration is loaded:

- **Timestamp** (UTC)
- **User** loading the configuration
- **Machine name**
- **Success/failure** status
- **Error message** (if failed)

## Log Entry Format

Audit entries are written in JSON format (one entry per line) for easy parsing and analysis:

```json
{
  "timestamp": "2025-11-23T14:30:22.1234567Z",
  "eventType": "ConfigurationChange",
  "user": "jdoe",
  "machineName": "WORKSTATION01",
  "success": true,
  "errorMessage": null,
  "changeCount": 2,
  "changes": [
    {
      "fieldPath": "ServiceFabric.ProtectionLevel",
      "oldValue": "Sign",
      "newValue": "EncryptAndSign"
    },
    {
      "fieldPath": "ReverseProxy.Routes.api-route",
      "oldValue": null,
      "newValue": "[Route added]"
    }
  ],
  "entryHash": "A1B2C3D4E5F6...",
  "previousEntryHash": "F6E5D4C3B2A1..."
}
```

## Sensitive Data Protection

⚠️ **Sensitive values are NEVER logged in plain text**

The audit system automatically masks sensitive fields:

- Certificate thumbprints: `***MASKED***` → `***CHANGED***`
- Connection endpoints: `***MASKED***` → `***CHANGED***`
- API keys and secrets: `***MASKED***` → `***CHANGED***`

This ensures the audit log itself doesn't become a security vulnerability.

## Tamper Detection

Each audit entry includes two hash values for chain integrity:

1. **entryHash** - SHA-256 hash of the current entry
2. **previousEntryHash** - SHA-256 hash of the previous entry

This creates a blockchain-like chain where:
- Tampering with any entry breaks the hash chain
- Missing entries are detectable
- Insertion of forged entries is detectable

### Verifying Audit Log Integrity

To verify the audit log hasn't been tampered with:

1. Read the log file line by line
2. For each entry, verify `previousEntryHash` matches the `entryHash` of the previous entry
3. Recalculate `entryHash` for each entry and verify it matches

Example Python verification script:

```python
import json
import hashlib

def verify_audit_log(log_path):
    with open(log_path, 'r') as f:
        lines = f.readlines()

    previous_hash = None
    for i, line in enumerate(lines):
        entry = json.loads(line.strip())

        # Check hash chain
        if entry['previousEntryHash'] != previous_hash:
            print(f"❌ Chain broken at line {i+1}")
            return False

        # Verify entry hash (simplified - actual implementation more complex)
        # In production, recalculate the hash and compare
        previous_hash = entry['entryHash']

    print("✅ Audit log integrity verified")
    return True
```

## Log Rotation

To prevent unbounded growth, the audit log automatically rotates when it exceeds **10 MB**:

1. Current log is renamed with timestamp: `acetone-audit.log.20251123143022`
2. New log file is created
3. Old rotated logs are preserved

**Important**: Rotated logs should be:
- Archived to secure storage
- Kept for compliance period (often 7 years for fintech)
- Never deleted without proper authorization

## Compliance Use Cases

### Regulatory Compliance

Audit logs satisfy common fintech regulations:

✅ **SOC 2** - Change management and access tracking
✅ **PCI DSS** - Requirement 10 (Track and monitor all access)
✅ **GDPR** - Article 30 (Records of processing activities)
✅ **SOX** - Change control documentation
✅ **HIPAA** - § 164.312(b) Audit controls

### Incident Investigation

When investigating issues:

1. **Identify when** configuration changed:
   ```bash
   grep "ConfigurationChange" acetone-audit.log | jq '.timestamp'
   ```

2. **Find who** made changes:
   ```bash
   grep "success\":true" acetone-audit.log | jq '{user, machine: .machineName, when: .timestamp}'
   ```

3. **See what** was changed:
   ```bash
   grep "ServiceFabric" acetone-audit.log | jq '.changes'
   ```

### Change Rollback

To rollback a configuration change:

1. Review audit log to find the change:
   ```bash
   cat acetone-audit.log | jq 'select(.timestamp > "2025-11-23T12:00:00Z")'
   ```

2. Note the field changes and old values

3. Use timestamped backup files:
   ```
   appsettings.json.backup.20251123120000
   ```

4. Restore from backup:
   ```bash
   # Windows
   copy C:\ProgramData\Acetone\appsettings.json.backup.20251123120000 C:\ProgramData\Acetone\appsettings.json

   # Linux
   sudo cp /opt/acetone/appsettings.json.backup.20251123120000 /opt/acetone/appsettings.json
   ```

5. Restart the Acetone proxy service

## Analysis and Monitoring

### Query Examples

**Find all failed configuration changes:**
```bash
cat acetone-audit.log | jq 'select(.success == false)'
```

**Count changes by user:**
```bash
cat acetone-audit.log | jq -r '.user' | sort | uniq -c
```

**Find configuration changes in last 24 hours:**
```bash
cat acetone-audit.log | jq 'select(.timestamp > "'$(date -u -d '24 hours ago' +%Y-%m-%dT%H:%M:%SZ)'")'
```

**Extract all Service Fabric certificate changes:**
```bash
cat acetone-audit.log | jq '.changes[] | select(.fieldPath | contains("Thumbprint"))'
```

### Alerting

Set up monitoring alerts for:

1. **Failed configuration saves**
   - Could indicate attack or misconfiguration

2. **Changes during off-hours**
   - Unusual activity pattern

3. **Sensitive field changes**
   - Certificate rotations, endpoint changes

4. **Multiple validation failures**
   - Could indicate brute-force config attempts

Example systemd timer for monitoring (Linux):

```ini
# /etc/systemd/system/acetone-audit-monitor.service
[Unit]
Description=Monitor Acetone audit log for security events

[Service]
Type=oneshot
ExecStart=/usr/local/bin/check-acetone-audit.sh

# /etc/systemd/system/acetone-audit-monitor.timer
[Unit]
Description=Run Acetone audit monitoring hourly

[Timer]
OnCalendar=hourly
Persistent=true

[Install]
WantedBy=timers.target
```

## Log Forwarding

For centralized logging, forward audit logs to a SIEM:

### Splunk
```
[monitor:///opt/acetone/acetone-audit.log]
sourcetype = acetone:audit:json
index = security
```

### ELK Stack
```yaml
filebeat.inputs:
- type: log
  enabled: true
  paths:
    - /opt/acetone/acetone-audit.log
  json.keys_under_root: true
  json.add_error_key: true
  fields:
    application: acetone-v2
    log_type: audit
```

### Azure Sentinel
Use the Log Analytics agent to ingest audit logs as custom logs.

## Retention and Archival

### Recommended Retention Policies

- **Active logs**: Keep last 90 days on disk
- **Archived logs**: Retain for 7 years (fintech standard)
- **Backup frequency**: Daily incremental, weekly full

### Archive Script Example

```bash
#!/bin/bash
# archive-audit-logs.sh

AUDIT_DIR="/opt/acetone"
ARCHIVE_DIR="/backup/acetone/audit"
RETENTION_DAYS=90

# Find rotated logs older than retention period
find "$AUDIT_DIR" -name "acetone-audit.log.*" -mtime +$RETENTION_DAYS -exec bash -c '
    for log in "$@"; do
        # Compress and move to archive
        gzip -c "$log" > "$ARCHIVE_DIR/$(basename $log).gz"
        rm "$log"
        echo "Archived: $(basename $log)"
    done
' bash {} +
```

## Security Best Practices

1. **Protect audit logs** with restrictive permissions:
   ```bash
   # Linux
   sudo chmod 640 /opt/acetone/acetone-audit.log
   sudo chown acetone:acetone /opt/acetone/acetone-audit.log

   # Windows - use GUI or icacls
   icacls "C:\ProgramData\Acetone\acetone-audit.log" /grant Administrators:F /grant SYSTEM:F
   ```

2. **Monitor for gaps** in audit logs (missing timestamps)

3. **Alert on hash chain breaks** (tamper detection)

4. **Separate log storage** from application servers when possible

5. **Implement log backup** independent of application backups

6. **Regular integrity checks** using hash chain verification

7. **Audit the auditors** - log access to audit logs themselves

## Troubleshooting

### "Audit logging failed" in console

**Cause**: Audit logger can't write to log file.

**Solution**:
1. Check directory permissions
2. Verify disk space available
3. Check for SELinux/AppArmor restrictions (Linux)

### Audit log growing too large

**Cause**: Rotation threshold not met, or excessive configuration changes.

**Solution**:
1. Trigger manual rotation if needed
2. Investigate why so many changes are occurring
3. Consider lowering rotation threshold

### Missing audit entries

**Cause**: Application crash before audit write completed.

**Solution**:
- Audit logging is synchronous and should be durable
- Check for hardware issues or power loss
- Verify hash chain to detect gaps

## Further Reading

- **Encryption**: See `SECURITY.md` for configuration encryption details
- **Validation**: See `deployment/README.md` for validation rules
- **Compliance**: Consult your organization's compliance team for specific retention requirements

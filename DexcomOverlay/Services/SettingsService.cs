using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using DexcomOverlay.Models;

namespace DexcomOverlay.Services;

public static class SettingsService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DexcomOverlay");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Application-specific entropy — raises the bar for same-user attackers
    private static readonly byte[] Entropy =
        "DexcomOverlay.v1.CredentialProtection"u8.ToArray();

    private static readonly HashSet<string> ValidRegions = new(StringComparer.OrdinalIgnoreCase)
        { "us", "ous", "jp" };

    public static AppSettings Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Decrypt credentials after loading
            settings.Username = Unprotect(settings.Username);
            settings.Password = Unprotect(settings.Password);

            // Sanitize loaded settings
            Sanitize(settings);

            return settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DexcomOverlay] Failed to load config: {ex.GetType().Name}: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigDir);

        // Back up existing config before overwriting — prevents permanent data loss
        BackupConfig();

        // Sanitize before saving
        Sanitize(settings);

        // Clone so we don't mutate the live settings object
        var toSave = new AppSettings
        {
            Username = Protect(settings.Username),
            Password = Protect(settings.Password),
            Region = settings.Region,
            WindowX = settings.WindowX,
            WindowY = settings.WindowY,
            RefreshIntervalSeconds = settings.RefreshIntervalSeconds,
            FontSize = settings.FontSize,
            Opacity = settings.Opacity,
            ShowTrendArrow = settings.ShowTrendArrow,
            ShowMmol = settings.ShowMmol,
            EnablePredictiveAlerts = settings.EnablePredictiveAlerts,
            AlertCooldownMinutes = settings.AlertCooldownMinutes,
            EnableNoDataAlert = settings.EnableNoDataAlert,
            NoDataAlertMinutes = settings.NoDataAlertMinutes,
            Thresholds = settings.Thresholds,
            Suppression = settings.Suppression,
        };

        var json = JsonSerializer.Serialize(toSave, JsonOptions);
        File.WriteAllText(ConfigPath, json);

        // Restrict config file ACL to current user only
        RestrictFileAccess(ConfigPath);
    }

    // ── Validation ─────────────────────────────────────────────

    /// <summary>
    /// Clamps and validates all settings to safe ranges,
    /// preventing tampered config files from causing harm.
    /// </summary>
    internal static void Sanitize(AppSettings s)
    {
        // Region must be a known value
        if (!ValidRegions.Contains(s.Region))
            s.Region = "us";

        // Refresh interval: 30s–600s (10 min)
        s.RefreshIntervalSeconds = Math.Clamp(s.RefreshIntervalSeconds, 30, 600);

        // Font size: 12–120
        s.FontSize = Math.Clamp(s.FontSize, 12, 120);

        // Opacity: 10%–100%
        s.Opacity = Math.Clamp(s.Opacity, 0.1, 1.0);

        // Alert cooldown: 5–60 min
        s.AlertCooldownMinutes = Math.Clamp(s.AlertCooldownMinutes, 5, 60);

        // No-data alert: 5–120 min
        s.NoDataAlertMinutes = Math.Clamp(s.NoDataAlertMinutes, 5, 120);

        // Ensure Suppression hierarchy is never null (guards against JSON deserialization gaps)
        s.Suppression ??= new AlertSuppressionSettings();
        s.Suppression.Global ??= new SuppressionRule();
        s.Suppression.PerType ??= new Dictionary<string, SuppressionRule>();

        // Threshold sanity: must be in physiologically plausible order
        // and within 20–500 mg/dL range
        var t = s.Thresholds;
        t.UrgentLow = Math.Clamp(t.UrgentLow, 20, 100);
        t.Low = Math.Clamp(t.Low, t.UrgentLow + 1, 150);
        t.High = Math.Clamp(t.High, t.Low + 1, 400);
        t.UrgentHigh = Math.Clamp(t.UrgentHigh, t.High + 1, 500);
    }

    // ── Config backup ───────────────────────────────────────────

    private static readonly string BackupPath = Path.Combine(ConfigDir, "config.json.bak");

    /// <summary>
    /// Copies the current config to a .bak file before overwriting.
    /// Only keeps one backup to avoid clutter.
    /// </summary>
    private static void BackupConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
                File.Copy(ConfigPath, BackupPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DexcomOverlay] Config backup failed: {ex.Message}");
        }
    }

    // ── File ACL restriction ───────────────────────────────────

    /// <summary>
    /// Restricts file read/write to the current Windows user only.
    /// Removes inherited permissions from Users, Everyone, etc.
    /// </summary>
    private static void RestrictFileAccess(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();

            // Remove all inherited rules
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
                security.RemoveAccessRule(rule);

            // Grant full control to current user only
            var currentUser = WindowsIdentity.GetCurrent().User!;
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DexcomOverlay] Failed to set config ACL: {ex.Message}");
        }
    }

    // ── DPAPI helpers ──────────────────────────────────────────

    /// <summary>
    /// Encrypts a string using Windows DPAPI (CurrentUser scope)
    /// with application-specific entropy.
    /// Returns a Base64 string. Empty/null input passes through unchanged.
    /// </summary>
    private static string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText ?? "";
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        try
        {
            var cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipherBytes);
        }
        finally
        {
            // Zero out plain bytes from memory
            Array.Clear(plainBytes);
        }
    }

    /// <summary>
    /// Decrypts a DPAPI-protected Base64 string back to plain text.
    /// Handles three cases:
    ///   1. Valid DPAPI ciphertext (with entropy) → decrypts normally
    ///   2. Old DPAPI ciphertext (without entropy) → falls back to no-entropy decrypt
    ///   3. Legacy plain-text value → returns as-is for auto-migration
    /// </summary>
    private static string Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText ?? "";

        byte[]? cipherBytes = null;
        try
        {
            cipherBytes = Convert.FromBase64String(cipherText);
        }
        catch (FormatException)
        {
            // Not Base64 → legacy plain-text config. Will be encrypted on next Save().
            Debug.WriteLine("[DexcomOverlay] Config contains plain-text credentials; will encrypt on next save.");
            return cipherText;
        }

        // DPAPI ciphertext is typically much longer than plain text.
        // A short Base64 string (< 32 bytes decoded) that happens to be valid Base64
        // is almost certainly legacy plain text (e.g. a username like "bradleyklein").
        if (cipherBytes.Length < 32)
        {
            Debug.WriteLine("[DexcomOverlay] Short Base64 value detected — treating as legacy plain text.");
            return cipherText;
        }

        // Try with entropy first (current format)
        try
        {
            var plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            var result = Encoding.UTF8.GetString(plainBytes);
            Array.Clear(plainBytes);
            return result;
        }
        catch (CryptographicException)
        {
            // May be old format without entropy — try without
        }

        // Try without entropy (previous format before entropy was added)
        try
        {
            var plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
            var result = Encoding.UTF8.GetString(plainBytes);
            Array.Clear(plainBytes);
            Debug.WriteLine("[DexcomOverlay] Migrated credential from no-entropy DPAPI; will re-encrypt on next save.");
            return result;
        }
        catch (CryptographicException)
        {
            // Couldn't decrypt — likely legacy plain text that happened to be valid Base64.
            // Return the original string rather than losing the credential.
            Debug.WriteLine("[DexcomOverlay] DPAPI decryption failed — returning value as plain text to avoid credential loss.");
            return cipherText;
        }
    }
}

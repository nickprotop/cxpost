using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CXPost.Services;

/// <summary>
/// Cross-platform credential storage:
/// - Linux: secret-tool (libsecret), falls back to encrypted file
/// - macOS: security (Keychain)
/// - Windows: AES-256 encrypted file (machine-scoped key)
///
/// When the primary OS keyring is unavailable, credentials are stored
/// as base64-encoded encrypted files in the data directory.
/// </summary>
public class CredentialService : ICredentialService
{
    private const string ServiceName = "cxpost";
    private readonly string _fallbackDir;
    private readonly Action<string>? _log;
    private bool? _secretToolAvailable;

    public CredentialService() : this(GetDefaultFallbackDir(), null) { }

    public CredentialService(string fallbackDir, Action<string>? log = null)
    {
        _fallbackDir = fallbackDir;
        _log = log;
    }

    public string? GetPassword(string accountId)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (IsSecretToolAvailable())
                {
                    var result = RunProcess("secret-tool", $"lookup service {ServiceName} account {accountId}");
                    if (result != null) return result;
                    Log($"secret-tool lookup returned no result for account '{accountId}', trying file fallback");
                }
                else
                {
                    Log("secret-tool is not installed or not in PATH; using file-based credential fallback");
                }

                return GetPasswordFromFile(accountId);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return RunProcess("security", $"find-generic-password -s {ServiceName} -a {accountId} -w");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetPasswordFromFile(accountId);
        }
        catch (Exception ex)
        {
            Log($"GetPassword failed for account '{accountId}': {ex.Message}");
        }

        return null;
    }

    public void StorePassword(string accountId, string password)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (IsSecretToolAvailable())
                {
                    if (RunProcessWithInput("secret-tool", $"store --label=CXPost service {ServiceName} account {accountId}", password))
                    {
                        Log($"Password stored via secret-tool for account '{accountId}'");
                        return;
                    }
                    Log("secret-tool store failed (keyring service may not be running); falling back to encrypted file");
                }
                else
                {
                    Log("secret-tool unavailable; storing password in encrypted file fallback");
                }

                StorePasswordToFile(accountId, password);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                RunProcessSafe("security", ["add-generic-password", "-U", "-s", ServiceName, "-a", accountId, "-w", password]);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                StorePasswordToFile(accountId, password);
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"StorePassword failed for account '{accountId}': {ex.Message}");

            // Last-resort fallback on Linux/Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    StorePasswordToFile(accountId, password);
                }
                catch (Exception ex2)
                {
                    Log($"File fallback also failed for account '{accountId}': {ex2.Message}");
                }
            }
        }
    }

    public void DeletePassword(string accountId)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (IsSecretToolAvailable())
                    RunProcess("secret-tool", $"clear service {ServiceName} account {accountId}");

                DeletePasswordFile(accountId);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                RunProcess("security", $"delete-generic-password -s {ServiceName} -a {accountId}");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                DeletePasswordFile(accountId);
        }
        catch (Exception ex)
        {
            Log($"DeletePassword failed for account '{accountId}': {ex.Message}");
        }
    }

    // ── File-based encrypted storage (Linux fallback, Windows primary) ────────

    private string GetCredentialFilePath(string accountId)
    {
        var safeId = Convert.ToHexString(Encoding.UTF8.GetBytes(accountId));
        return Path.Combine(_fallbackDir, "credentials", $"{safeId}.enc");
    }

    private void StorePasswordToFile(string accountId, string password)
    {
        var filePath = GetCredentialFilePath(accountId);
        var dir = Path.GetDirectoryName(filePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Encrypt with AES using a machine-scoped key derived from hostname + username
        var key = DeriveKey();
        var plainBytes = Encoding.UTF8.GetBytes(password);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Store as: IV (16 bytes) + encrypted data, base64-encoded
        var combined = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, combined, aes.IV.Length, encrypted.Length);

        File.WriteAllText(filePath, Convert.ToBase64String(combined));

        // Restrict file permissions on Linux
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try { File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* best effort */ }
        }

        Log($"Password stored in encrypted file for account '{accountId}' (WARNING: less secure than OS keyring)");
    }

    private string? GetPasswordFromFile(string accountId)
    {
        var filePath = GetCredentialFilePath(accountId);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var base64 = File.ReadAllText(filePath).Trim();
            var combined = Convert.FromBase64String(base64);

            if (combined.Length < 17) // At least 16 IV + 1 data byte
                return null;

            var key = DeriveKey();
            var iv = new byte[16];
            var encrypted = new byte[combined.Length - 16];
            Buffer.BlockCopy(combined, 0, iv, 0, 16);
            Buffer.BlockCopy(combined, 16, encrypted, 0, encrypted.Length);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            Log($"Failed to read credential file for account '{accountId}': {ex.Message}");
            return null;
        }
    }

    private void DeletePasswordFile(string accountId)
    {
        var filePath = GetCredentialFilePath(accountId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Log($"Deleted credential file for account '{accountId}'");
        }
    }

    /// <summary>
    /// Derives a 256-bit key from machine-scoped data. Not a substitute for
    /// a proper keyring, but ensures the file isn't plain text.
    /// </summary>
    private static byte[] DeriveKey()
    {
        var machineId = $"{Environment.MachineName}:{Environment.UserName}:cxpost-credential-fallback";
        return SHA256.HashData(Encoding.UTF8.GetBytes(machineId));
    }

    // ── secret-tool availability check ───────────────────────────────────────

    private bool IsSecretToolAvailable()
    {
        if (_secretToolAvailable.HasValue)
            return _secretToolAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo("which", "secret-tool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                _secretToolAvailable = false;
                return false;
            }
            process.WaitForExit(3000);
            _secretToolAvailable = process.ExitCode == 0;
        }
        catch
        {
            _secretToolAvailable = false;
        }

        return _secretToolAvailable.Value;
    }

    // ── Process helpers ──────────────────────────────────────────────────────

    private static string? RunProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit(5000);
            var output = outputTask.Result.Trim();
            var error = errorTask.Result.Trim();

            if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] {fileName} stderr: {error}");

            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] Failed to run {fileName}: {ex.Message}");
            return null;
        }
    }

    private static void RunProcessSafe(string fileName, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] Failed to run {fileName}: {ex.Message}");
        }
    }

    private static bool RunProcessWithInput(string fileName, string arguments, string input)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            process.StandardInput.Write(input);
            process.StandardInput.Close();
            var error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit(5000);

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrEmpty(error))
                    System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] {fileName} stderr: {error}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] Failed to run {fileName}: {ex.Message}");
            return false;
        }
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
        System.Diagnostics.Trace.WriteLine($"[CXPost.CredentialService] {message}");
    }

    private static string GetDefaultFallbackDir()
    {
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(xdg ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "cxpost");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CXPost");
    }
}

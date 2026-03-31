using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CXPost.Services;

/// <summary>
/// Cross-platform credential storage using OS CLI tools:
/// - Linux: secret-tool (libsecret)
/// - macOS: security (Keychain)
/// - Windows: cmdkey / credential manager via PowerShell
/// </summary>
public class CredentialService : ICredentialService
{
    private const string ServiceName = "cxpost";

    public string? GetPassword(string accountId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RunProcess("secret-tool", $"lookup service {ServiceName} account {accountId}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RunProcess("security", $"find-generic-password -s {ServiceName} -a {accountId} -w");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RunProcess("powershell", $"-Command \"(Get-StoredCredential -Target '{ServiceName}:{accountId}').GetNetworkCredential().Password\"");

        return null;
    }

    public void StorePassword(string accountId, string password)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            RunProcessWithInput("secret-tool", $"store --label=CXPost service {ServiceName} account {accountId}", password);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            RunProcess("security", $"add-generic-password -U -s {ServiceName} -a {accountId} -w {password}");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            RunProcess("cmdkey", $"/generic:{ServiceName}:{accountId} /user:{accountId} /pass:{password}");
    }

    public void DeletePassword(string accountId)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            RunProcess("secret-tool", $"clear service {ServiceName} account {accountId}");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            RunProcess("security", $"delete-generic-password -s {ServiceName} -a {accountId}");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            RunProcess("cmdkey", $"/delete:{ServiceName}:{accountId}");
    }

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
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RunProcessWithInput(string fileName, string arguments, string input)
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
            if (process == null) return;
            process.StandardInput.Write(input);
            process.StandardInput.Close();
            process.WaitForExit(5000);
        }
        catch
        {
            // Silently fail — credential storage is best-effort
        }
    }
}

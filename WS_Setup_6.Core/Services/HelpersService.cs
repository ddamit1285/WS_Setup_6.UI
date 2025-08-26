using MahApps.Metro.Controls.Dialogs;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using WS_Setup_6.Core.Interfaces;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public class HelpersService(IOnboardService onboardService) : IHelpersService
    {
        private readonly IOnboardService _onboardSvc = onboardService;

        // Validates the installer path, checking if it's local or a UNC share.
        /// <summary>
        /// Ensures the given path is a non-empty, .msi file that exists locally.
        /// </summary>
        /// <param name="rawPath">User-entered path to the installer.</param>
        /// <param name="log">Logger action for detailed messages.</param>
        /// <param name="setStatus">Status-bar updater for user feedback.</param>
        /// <returns>The validated file path, or null if invalid.</returns>
        public Task<string?> ValidateInstallerPathAsync(
            string rawPath,
            Action<string> log,
            Action<string> setStatus)
        {
            // Trim whitespace, guard null
            var path = rawPath?.Trim() ?? "";
            log($"Validating installer path: {path}");

            // 1) Nothing entered → skip
            if (string.IsNullOrEmpty(path))
            {
                setStatus("No path entered, skipping agent install.");
                return Task.FromResult<string?>(null);
            }

            // 2) Must be an .msi
            if (!path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                setStatus("Installer must be a .msi file.");
                log($"Rejected non-MSI extension: {path}");
                return Task.FromResult<string?>(null);
            }

            // 3) Check existence on local disk
            if (File.Exists(path))
            {
                setStatus("Local installer found.");
                return Task.FromResult<string?>(path);
            }

            // 4) Not found → error
            setStatus("Installer file not found.");
            log($"File not found: {path}");
            return Task.FromResult<string?>(null);
        }

        // Domain join logic:
        public async Task<bool> TryJoinDomainAsync(
            string domainName,
            Func<string, Task<NetworkCredential?>> promptForCredentials,
            Action<string> log,
            Action<string> setStatus,
            int maxRetries = 3)
        {
            if (string.IsNullOrWhiteSpace(domainName))
            {
                log("No domain specified; skipping domain join.");
                return true;
            }

            log($"Starting domain join for: {domainName}");
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                setStatus($"Domain join (attempt {attempt}/{maxRetries})");
                var creds = await promptForCredentials(domainName);
                if (creds == null)
                {
                    log("Domain join canceled by user.");
                    return true;
                }

                try
                {
                    // **ACTUAL DOMAIN JOIN CALL**
                    await _onboardSvc.RunDomainJoinAsync(domainName, creds);
                    log($"Joined domain on attempt {attempt}");
                    return true;
                }
                catch (Exception ex)
                {
                    log($"Domain join failed: {ex.Message}");
                    if (attempt == maxRetries)
                    {
                        setStatus("Domain join failed permanently.");
                        return false;
                    }

                    // continue loop for retry…
                }
            }

            return false; // should never reach here
        }

        // Deletes the file at the given path if it exists, logging any errors.
        public void TryDelete(string path, Action<string> log)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    log($"Deleted file: {path}");
                }
            }
            catch (Exception ex)
            {
                log($"Could not delete file {path}: {ex.Message}");
            }
        }

        // Automatically finds the NinjaOne Agent installer on the user's Desktop.
        public string? FindAgentInstallerOnDesktop()
        {
            try
            {
                var desktop = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);

                return Directory
                    .EnumerateFiles(desktop, "*.msi", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f =>
                        Path.GetFileName(f)
                            .StartsWith("NinjaOne-Agent",
                                StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        // Shared Build silent uninstall command logic for OEM removal.
        // Build the silent uninstall command based on the uninstall string
        public string BuildSilentCommand(string uninstallString)
        {
            if (string.IsNullOrWhiteSpace(uninstallString))
                return string.Empty;

            // 1) Split into exe path + existing args
            //    Handles both: "C:\Foo\bar.exe" /uninstall /someflag
            //    and:  msiexec.exe /x {GUID} /someflag
            var firstSpace = uninstallString.IndexOf(' ');
            string exePath;
            string args;
            if (firstSpace < 0)
            {
                exePath = uninstallString.Trim('"');
                args = string.Empty;
            }
            else
            {
                exePath = uninstallString.Substring(0, firstSpace).Trim('"');
                args = uninstallString.Substring(firstSpace + 1);
            }

            // 2) Lowercase for comparisons
            var exeName = Path.GetFileName(exePath).ToLowerInvariant();
            var sb = new StringBuilder();

            // 3) Inject silent flags by type
            if (exeName == "msiexec.exe" || exeName.EndsWith(".msi"))
            {
                // MSI based – use /qn (no UI), /norestart
                // If the original command used /x or /i, keep it
                if (!args.Contains("/qn")) sb.Append("/qn ");
                if (!args.Contains("/norestart")) sb.Append("/norestart ");
            }
            else
            {
                // EXE-based – try common silent switches
                // Inno Setup: /VERYSILENT /SUPPRESSMSGBOXES
                if (!args.Contains("/silent", StringComparison.OrdinalIgnoreCase) &&
                    !args.Contains("/verysilent", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("/VERYSILENT /SUPPRESSMSGBOXES ");
                }

                // NSIS or InstallShield often support /S or -s
                if (!args.Contains("/S ", StringComparison.Ordinal) &&
                    !args.EndsWith("/S", StringComparison.Ordinal))
                {
                    sb.Append("/S ");
                }
            }

            // 4) Append any existing arguments (so you don’t lose custom switches)
            if (!string.IsNullOrWhiteSpace(args))
                sb.Append(args);

            // 5) Return quoted exe + final args
            return $"\"{exePath}\" {sb.ToString().Trim()}";
        }
    }
}
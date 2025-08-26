using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Models;
using WS_Setup_6.Core.Services;

namespace WS_Setup_6.Core.Services
{
    public class OemRemovalService : IOemRemovalService
    {
        private readonly ILogService _log;

        public OemRemovalService(ILogService log) => _log = log;

        // Note: This method runs uninstall commands sequentially.
        public async Task RemoveOemAppsAsync(
            IEnumerable<UninstallEntry> apps,
            CancellationToken cancellationToken = default)
        {
            foreach (var app in apps)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _log.Log($"[OEM Removal] Attempting uninstall: {app.DisplayName}", "INFO");

                var uninstallCmd = BuildSilentCommand(app.UninstallString);

                if (string.IsNullOrWhiteSpace(uninstallCmd))
                {
                    _log.Log($"[OEM Removal] No uninstall command for: {app.DisplayName}", "WARN");
                    continue;
                }

                var exitCode = await OemRemovalHelper.RunProcessAsync(
                    uninstallCmd,
                    line => _log.Log(line, "INFO"),
                    line => _log.Log(line, "ERROR")
                );

                _log.Log($"[OEM Removal] {app.DisplayName} exited with code {exitCode}",
                         exitCode == 0 ? "INFO" : "WARN");
            }
        }

        private string BuildSilentCommand(string uninstallString)
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
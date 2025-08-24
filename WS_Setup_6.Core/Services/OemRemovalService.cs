using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core.Models;
using WS_Setup_6.Core.Services;
using WS_Setup_6.Core.Interfaces;

namespace WS_Setup_6.Core.Services
{
    public class OemRemovalService : IOemRemovalService
    {
        private readonly ILogService _log;

        public OemRemovalService(ILogService log) => _log = log;

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
            // Insert your pattern matching / silent flag overrides here.
            // For example: append /quiet or /qn if it's MSI-based
            return uninstallString;
        }
    }
}
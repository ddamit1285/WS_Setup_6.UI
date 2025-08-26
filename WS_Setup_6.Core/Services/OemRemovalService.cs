using ServiceStack.Logging;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Models;
using WS_Setup_6.Core.Services;
using System;
using System.Runtime.Versioning;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public class OemRemovalService : IOemRemovalService
    {
        private readonly ILogService _log;
        private readonly IHelpersService _helpers;

        public OemRemovalService(ILogService log, IHelpersService helpers)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }

        public async Task RemoveOemAppsAsync(
            IEnumerable<UninstallEntry> apps,
            CancellationToken cancellationToken = default)
        {
            foreach (var app in apps)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _log.Log($"[OEM Removal] Attempting uninstall: {app.DisplayName}", "INFO");

                var uninstallCmd = _helpers.BuildSilentCommand(app.UninstallString);
                if (string.IsNullOrWhiteSpace(uninstallCmd))
                {
                    _log.Log($"[OEM Removal] No uninstall command for: {app.DisplayName}", "WARN");
                    continue;
                }

                var exitCode = await OemRemovalHelper.RunProcessAsync(
                    uninstallCmd,
                    line => _log.Log(line, "INFO"),
                    line => _log.Log(line, "ERROR"));

                _log.Log(
                    $"[OEM Removal] {app.DisplayName} exited with code {exitCode}",
                    exitCode == 0 ? "INFO" : "WARN");
            }
        }
    }
}
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Models;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public partial class UninstallService : IUninstallService
    {
        private readonly ILogService _log;
        private readonly IHelpersService _helpers;

        public UninstallService(ILogService log, IHelpersService helpers)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }

        // Registry paths to scan for installed applications
        private static readonly string[] _registryUninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        [GeneratedRegex(@"\{[A-F0-9\-]+\}", RegexOptions.IgnoreCase)]
        private static partial Regex GuidPattern();

        // 1) Query installed applications
        public async Task<IReadOnlyList<UninstallEntry>> QueryInstalledAppsAsync()
        {
            return await Task.Run(() =>
            {
                var list = new List<UninstallEntry>();

                foreach (var path in _registryUninstallPaths)
                {
                    using var baseKey = Registry.LocalMachine.OpenSubKey(path);
                    if (baseKey == null) continue;

                    foreach (var subName in baseKey.GetSubKeyNames())
                    {
                        using var sub = baseKey.OpenSubKey(subName);
                        if (sub == null) continue;

                        var displayName = sub.GetValue("DisplayName") as string;
                        var uninstallCmd = sub.GetValue("UninstallString") as string;
                        if (string.IsNullOrWhiteSpace(displayName) ||
                            string.IsNullOrWhiteSpace(uninstallCmd))
                            continue;

                        var entry = new UninstallEntry
                        {
                            DisplayName = displayName,
                            UninstallString = uninstallCmd,
                            InstallLocation = sub.GetValue("InstallLocation") as string,
                            DisplayVersion = sub.GetValue("DisplayVersion") as string,
                            Publisher = sub.GetValue("Publisher") as string,
                            ProductKey = TryExtractGuid(subName) ?? subName,
                            ServiceName = GuessServiceName(displayName),
                            ProcessNames = GuessProcessNames(displayName)
                        };

                        list.Add(entry);
                    }
                }

                return list
                    .OrderBy(x => x.DisplayName, StringComparer.InvariantCultureIgnoreCase)
                    .ToList();
            });
        }

        // 2) Standard uninstall sequence
        public async Task<UninstallResult> ExecuteUninstallAsync(
            UninstallEntry app,
            IProgress<UninstallProgress> progress,
            CancellationToken cancellationToken)
        {
            _log.Log($"Beginning uninstall: {app.DisplayName}");

            try
            {
                // Phase 1: Stop services/processes
                _log.Log("Phase 1: Stopping processes…", "INFO");
                progress.Report(new UninstallProgress(UninstallPhase.StoppingProcesses));
                await Task.Run(() => StopServicesAndProcesses(app), cancellationToken);

                // Phase 2: Silent uninstall
                _log.Log("Phase 2: Running silent uninstall", "INFO");
                progress.Report(new UninstallProgress(UninstallPhase.RunningSilent));
                var cmd = _helpers.BuildSilentCommand(app.UninstallString);
                var useShell = cmd.EndsWith("msiexec", StringComparison.OrdinalIgnoreCase)
                || cmd.Contains("msiexec ", StringComparison.OrdinalIgnoreCase);
                _log.Log($"Executing: {cmd}", "DEBUG");
                var exitCode = await RunProcessAsync(cmd, cancellationToken, useShellExecute: useShell);
                _log.Log($"Exit code: {exitCode}", exitCode == 0 ? "INFO" : "WARN");

                // Phase 3: Force delete fallback
                if (exitCode != 0 || await IsStillInstalledAsync(app))
                {
                    _log.Log("Phase 3: Fallback force-delete", "WARN");
                    progress.Report(new UninstallProgress(UninstallPhase.ForcingDelete));
                    await Task.Run(() => ForceDeleteRemnants(app), cancellationToken);
                }

                progress.Report(new UninstallProgress(UninstallPhase.Completed));
                _log.Log($"Completed uninstall: {app.DisplayName}", "INFO");

                return new UninstallResult(app, exitCode: exitCode, success: exitCode == 0);
            }
            catch (OperationCanceledException)
            {
                _log.Log($"Uninstall cancelled: {app.DisplayName}", "WARN");
                return new UninstallResult(app, exitCode: -1, success: false, wasCancelled: true);
            }
            catch (Exception ex)
            {
                _log.Log($"Exception during uninstall: {ex.Message}", "ERROR");
                return new UninstallResult(app, exitCode: -1, success: false);
            }
        }

        // 3) OEM removal (e.g. Dell) using the same core helpers
        public async Task RemoveOemAppsAsync(
            IEnumerable<UninstallEntry> apps,
            CancellationToken cancellationToken = default)
        {
            foreach (var app in apps)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!DellOemProfile.IsMatch(app.DisplayName))
                    continue;

                _log.Log($"[OEM Removal] Target: {app.DisplayName}", "INFO");

                // Stop OEM-specific services & processes
                foreach (var svc in DellOemProfile.GetServices(app.DisplayName))
                    SafeStopService(svc);

                foreach (var proc in DellOemProfile.GetProcesses(app.DisplayName))
                    SafeKillProcess(proc);

                // Run silent uninstall
                var cmd = _helpers.BuildSilentCommand(app.UninstallString);
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    _log.Log($"[OEM Removal] No command for {app.DisplayName}", "WARN");
                    continue;
                }

                var exitCode = await RunProcessAsync(cmd, cancellationToken);
                _log.Log(
                    $"[OEM Removal] {app.DisplayName} exited {exitCode}",
                    exitCode == 0 ? "INFO" : "WARN");

                // Fallback if still present
                if (exitCode != 0 || await IsStillInstalledAsync(app))
                {
                    _log.Log($"[OEM Removal] Forcing delete: {app.DisplayName}", "WARN");
                    ForceDeleteRemnants(app);
                }
            }
        }

        #region Helpers

        // Stop generic + OEM services/processes
        private static void StopServicesAndProcesses(UninstallEntry app)
        {
            if (!string.IsNullOrEmpty(app.ServiceName))
                SafeStopService(app.ServiceName);

            if (app.ProcessNames != null)
                foreach (var name in app.ProcessNames)
                    SafeKillProcess(name);
        }

        private static void SafeStopService(string svc)
        {
            try
            {
                using var sc = new ServiceController(svc);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
            }
            catch { /* swallow or log externally */ }
        }

        private static void SafeKillProcess(string pname)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(pname))
                {
                    proc.Kill();
                    proc.WaitForExit(5000);
                }
            }
            catch { /* swallow or log externally */ }
        }

        // Runs a command line and returns exit code
        /// <summary>
        /// Runs a command line, optionally via ShellExecute+runas to ensure elevation.
        /// If useShellExecute==true, stdout/stderr redirection is disabled.
        /// </summary>
        private async Task<int> RunProcessAsync(
            string cmdLine,
            CancellationToken token,
            bool useShellExecute = false)
        {
            // split full command into exe + args
            var idx = cmdLine.IndexOf(' ');
            var exe = idx > 0 ? cmdLine[..idx] : cmdLine;
            var args = idx > 0 ? cmdLine[(idx + 1)..] : string.Empty;

            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = useShellExecute,
                CreateNoWindow = true,
                Verb = useShellExecute ? "runas" : null
            };

            // only wire up streams if not using shellexecute
            if (!useShellExecute)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }

            using var proc = Process.Start(psi)
                            ?? throw new InvalidOperationException("Process failed to start");
            using var reg = token.Register(() => { try { proc.Kill(); } catch { } });

            if (!useShellExecute)
            {
                // fire-and-forget reads to avoid deadlocks
                _ = Task.Run(async () => {
                    string? line;
                    while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                        _log.Log(line, "DEBUG");
                });
                _ = Task.Run(async () => {
                    string? line;
                    while ((line = await proc.StandardError.ReadLineAsync()) != null)
                        _log.Log(line, "ERROR");
                });
            }

            await proc.WaitForExitAsync(token);
            return proc.ExitCode;
        }

        // Check disk or registry for leftovers
        private static Task<bool> IsStillInstalledAsync(UninstallEntry app)
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation) &&
                Directory.Exists(app.InstallLocation))
            {
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        // Delete files + registry uninstall key
        private static void ForceDeleteRemnants(UninstallEntry app)
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation) &&
                Directory.Exists(app.InstallLocation))
            {
                try { Directory.Delete(app.InstallLocation, true); } catch { }
            }

            var keys = new[]
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{app.ProductKey}",
                $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{app.ProductKey}"
            };

            foreach (var path in keys)
            {
                try
                {
                    using var root = Registry.LocalMachine.OpenSubKey(
                        Path.GetDirectoryName(path)!, writable: true);
                    root?.DeleteSubKeyTree(Path.GetFileName(path), throwOnMissingSubKey: false);
                }
                catch { }
            }
        }

        // Extract GUID from registry key name
        private static string? TryExtractGuid(string raw)
        {
            var m = GuidPattern().Match(raw);
            return m.Success ? m.Value : null;
        }

        // Guess common service names
        private static string? GuessServiceName(string displayName)
        {
            if (displayName.Contains("Office", StringComparison.OrdinalIgnoreCase))
                return "OfficeClickToRunSvc";
            if (displayName.Contains("Optimizer", StringComparison.OrdinalIgnoreCase))
                return "DellOptimizer";
            if (displayName.Contains("Core Services", StringComparison.OrdinalIgnoreCase))
                return "DellClientManagementService";
            return null;
        }

        // Guess common process names
        private static string[]? GuessProcessNames(string displayName)
        {
            if (displayName.Contains("Dell", StringComparison.OrdinalIgnoreCase))
            {
                if (displayName.Contains("Optimizer", StringComparison.OrdinalIgnoreCase))
                    return new[] { "DellOptimizer", "DOCLI" };
                if (displayName.Contains("Core Services", StringComparison.OrdinalIgnoreCase))
                    return new[] { "DellClientManagementService" };
            }
            return null;
        }

        #endregion

        // Dell OEM-specific patterns and mappings
        // Update as needed for specific programs
        #region Dell OEM Profile

        private static class DellOemProfile
        {
            private static readonly string[] Patterns = {
                "Dell Optimizer",
                "Dell Core Services",
                "Dell Command",
                "Dell Power Manager",
                "Dell SupportAssist",
                "Dell Digital Delivery"
            };

            private static readonly Dictionary<string, string[]> ServiceMap =
                new(StringComparer.OrdinalIgnoreCase) {
                    { "Dell Optimizer",        new[] { "DellOptimizer" } },
                    { "Dell Core Services",    new[] { "DellClientManagementService" } },
                    { "Dell Command",          new[] { "DellCommandUpdate" } },
                    { "Dell Power Manager",    new[] { "DellPwrMgrSvc" } },
                };

            private static readonly Dictionary<string, string[]> ProcessMap =
                new(StringComparer.OrdinalIgnoreCase) {
                    { "Dell Optimizer",     new[] { "DellOptimizer", "DOCLI" } },
                    { "Dell Core Services", new[] { "DellClientManagementService" } },
                    { "Dell Command",       new[] { "DellCommandUpdate" } },
                };

            public static bool IsMatch(string name) =>
                Patterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));

            public static string[] GetServices(string name) =>
                ServiceMap.TryGetValue(MatchKey(name), out var svcs) ? svcs : Array.Empty<string>();

            public static string[] GetProcesses(string name) =>
                ProcessMap.TryGetValue(MatchKey(name), out var procs) ? procs : Array.Empty<string>();

            private static string MatchKey(string name) =>
                Patterns.FirstOrDefault(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))
                ?? name;
        }

        #endregion
    }
}
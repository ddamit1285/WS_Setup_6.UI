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

        // 1) Query installed applications from regsitry
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
                        var silentUninstall = sub.GetValue("SilentUninstallString") as string
                                           ?? sub.GetValue("QuietUninstallString") as string
                                           ?? sub.GetValue("UninstallStringSilent") as string;

                        var windowsInstallerFlag = (sub.GetValue("WindowsInstaller") as int?) == 1;

                        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(uninstallCmd))
                            continue;

                        var entry = new UninstallEntry
                        {
                            DisplayName = displayName,
                            UninstallString = uninstallCmd,
                            SilentUninstallString = sub.GetValue("SilentUninstallString") as string
                                                 ?? sub.GetValue("QuietUninstallString") as string
                                                 ?? sub.GetValue("UninstallStringSilent") as string,
                            InstallLocation = sub.GetValue("InstallLocation") as string,
                            DisplayVersion = sub.GetValue("DisplayVersion") as string,
                            Publisher = sub.GetValue("Publisher") as string,
                            ProductKey = TryExtractGuid(subName) ?? subName,
                            ServiceName = GuessServiceName(displayName),
                            ProcessNames = GuessProcessNames(displayName),
                            WindowsInstaller = windowsInstallerFlag // new property
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

            int exitCode = 0;
            bool success = true;

            try
            {
                progress.Report(new UninstallProgress(UninstallPhase.StoppingProcesses));
                await Task.Run(() => StopServicesAndProcesses(app), cancellationToken);

                var uninstallCmd = app.SilentUninstallString
                                 ?? app.QuietUninstallString
                                 ?? app.UninstallString;

                if (!string.IsNullOrWhiteSpace(app.SilentUninstallString) || !string.IsNullOrWhiteSpace(app.QuietUninstallString))
                {
                    _log.Log("[Branch: VendorSilent] Using vendor-provided silent uninstall string", "INFO");
                    var (exePath, args) = SplitExeAndArgs(uninstallCmd);
                    exitCode = await RunExeAndWaitAsync(exePath, args, cancellationToken);
                }
                else if (IsMsiEntry(app))
                {
                    var cmd = $"msiexec /x {app.ProductKey} /quiet /norestart";
                    _log.Log($"[Branch: MSI] Executing MSI uninstall: {cmd}", "DEBUG");
                    exitCode = await RunProcessAsync(cmd, cancellationToken, useShellExecute: false);
                    success = exitCode == 0;
                }
                else if (IsInteractiveOnly(app))
                {
                    _log.Log("[Branch: InteractiveOnly] Launching UI and waiting", "INFO");
                    LaunchInteractiveAndWait(uninstallCmd);
                    exitCode = 0;
                }
                else
                {
                    _log.Log("[Branch: GenericExe] Running generic EXE uninstall", "INFO");
                    var (exe, args2) = SplitExeAndArgs(uninstallCmd);
                    exitCode = await RunExeAndWaitAsync(exe, args2, cancellationToken);
                }

                // Final cleanup: check for leftovers and force delete if needed
                if (await IsStillInstalledAsync(app))
                {
                    _log.Log("Detected leftover install remnants, performing forced cleanup", "WARN");
                    ForceDeleteRemnants(app);
                }

                return new UninstallResult(app, exitCode, success);
            }
            catch (Exception ex)
            {
                _log.Log($"Exception during uninstall: {ex.Message}", "ERROR");
                return new UninstallResult(app, exitCode: -1, success: false);
            }
        }

        // Helper methods ---------------------------------------------------------------------------------------
        #region Helpers

        // Known interactive-only uninstallers
        private static readonly HashSet<string> InteractiveUninstallNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Dell Optimizer",
            "Dell Watchdog Timer"
        };

        // Detect if uninstall string matches known interactive pattern
        private bool MatchesInteractivePattern(string uninstallString)
        {
            if (string.IsNullOrWhiteSpace(uninstallString))
                return false;

            bool match = uninstallString.Contains("InstallShield", StringComparison.OrdinalIgnoreCase)
                      && uninstallString.Contains("-remove", StringComparison.OrdinalIgnoreCase)
                      && uninstallString.Contains("-runfromtemp", StringComparison.OrdinalIgnoreCase);

            if (match)
                _log.Log($"Matched interactive pattern: {uninstallString}", "DEBUG");

            return match;
        }

        // Detect if app is known interactive-only uninstaller
        public bool IsInteractiveOnly(UninstallEntry app)
        {
            var uninstallCmd = app.SilentUninstallString
                             ?? app.QuietUninstallString
                             ?? app.UninstallString;

            bool isInteractive = InteractiveUninstallNames.Any(name =>
                                    app.DisplayName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true)
                                 || MatchesInteractivePattern(uninstallCmd);

            _log.Log($"Classified '{app.DisplayName}' as {(isInteractive ? "InteractiveOnly" : "Silent/MSI")}", "DEBUG");

            return isInteractive;
        }

        // Split uninstall string into exe path + args
        private static (string exePath, string args) SplitExeAndArgs(string uninstallString)
        {
            if (uninstallString.StartsWith("\""))
            {
                var endQuote = uninstallString.IndexOf('"', 1);
                var exePath = uninstallString.Substring(1, endQuote - 1);
                var args = uninstallString.Length > endQuote + 1
                    ? uninstallString.Substring(endQuote + 1).Trim()
                    : string.Empty;
                return (exePath, args);
            }
            else
            {
                var firstSpace = uninstallString.IndexOf(' ');
                if (firstSpace > 0)
                    return (uninstallString.Substring(0, firstSpace),
                            uninstallString.Substring(firstSpace + 1).Trim());
                return (uninstallString, string.Empty);
            }
        }

        // Launch interactive uninstaller and wait for exit
        private void LaunchInteractiveAndWait(string uninstallString)
        {
            if (string.IsNullOrWhiteSpace(uninstallString))
            {
                _log.Log("Uninstall string is null or empty — cannot launch interactive uninstaller", "ERROR");
                return;
            }

            var (exePath, args) = SplitExeAndArgs(uninstallString);

            if (!File.Exists(exePath))
            {
                _log.Log($"Executable not found: {exePath}", "ERROR");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };

            _log.Log($"Launching interactive uninstall: {exePath} {args}", "INFO");

            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }

        // Run an EXE with args and wait for exit
        private async Task<int> RunExeAndWaitAsync(string exePath, string args, CancellationToken token)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException($"Failed to start process: {exePath}");

            await proc.WaitForExitAsync(token);
            return proc.ExitCode;
        }

        // Detect if registry entry is MSI-based
        private static bool IsMsiEntry(UninstallEntry app)
        {
            // True if registry said WindowsInstaller=1 OR the key name is a GUID
            return app.WindowsInstaller || Guid.TryParse(app.ProductKey, out _);
        }

        // Stop generic + OEM services/processes
        private static void StopServicesAndProcesses(UninstallEntry app)
        {
            if (!string.IsNullOrEmpty(app.ServiceName))
                SafeStopService(app.ServiceName);

            if (app.ProcessNames != null)
                foreach (var name in app.ProcessNames)
                    SafeKillProcess(name);
        }

        // Safely stop a service by name
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

        // Safely kill processes by name
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

            var keys = new[]
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{app.ProductKey}",
                $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{app.ProductKey}"
            };

            foreach (var path in keys)
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key != null)
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
        public string? GuessServiceName(string displayName)
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
        public string[]? GuessProcessNames(string displayName)
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
    }
}
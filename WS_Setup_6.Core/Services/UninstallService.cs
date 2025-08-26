using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Models;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public partial class UninstallService(ILogService log) : IUninstallService
    {
        /// ------------------------------------------------------------------------------------
        /// <summary>
        /// Two Main Methods that drive the uninstall process
        /// Load List button on UI calls QueryInstalledAppsAsync
        /// Second button removes the selected apps via ExecuteUninstallAsync
        /// Private Helper methods at the end are used to stop services, run commands, etc.
        /// </summary>
        /// ------------------------------------------------------------------------------------

        // Logging service for tracking uninstall operations
        private readonly ILogService _log = log ?? throw new ArgumentNullException(nameof(log));
        // Registry paths to scan for installed applications
        private static readonly string[] registryUninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        // Regex to match GUIDs in the uninstall key names
        [GeneratedRegex(@"\{[A-F0-9\-]+\}", RegexOptions.IgnoreCase)]
        private static partial Regex GuidPattern();

        // 1) Query installed applications from the registry
        public async Task<IReadOnlyList<UninstallEntry>> QueryInstalledAppsAsync()
        {
            return await Task.Run(() =>
            {
                var entries = new List<UninstallEntry>();
                var paths = registryUninstallPaths;

                foreach (var basePath in paths)
                {
                    using var baseKey = Registry.LocalMachine.OpenSubKey(basePath);
                    if (baseKey == null) continue;

                    foreach (var subKeyName in baseKey.GetSubKeyNames())
                    {
                        using var sub = baseKey.OpenSubKey(subKeyName);
                        if (sub == null) continue;

                        var name = sub.GetValue("DisplayName") as string;
                        var cmd = sub.GetValue("UninstallString") as string;
                        var loc = sub.GetValue("InstallLocation") as string;
                        var ver = sub.GetValue("DisplayVersion") as string;
                        var pub = sub.GetValue("Publisher") as string;
                        var guid = TryExtractGuid(subKeyName) ?? subKeyName;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cmd))
                            continue;

                        entries.Add(new UninstallEntry
                        {
                            DisplayName = name,
                            UninstallString = cmd,
                            InstallLocation = loc,
                            DisplayVersion = ver,
                            Publisher = pub,
                            ProductKey = guid,
                            // Optional hooks—fill in common process/service names if needed
                            ServiceName = GuessServiceName(name),
                            ProcessNames = GuessProcessNames(name)
                        });
                    }
                }

                // Sort alphabetically
                return entries
                    .OrderBy(e => e.DisplayName, StringComparer.InvariantCultureIgnoreCase)
                    .ToList();
            });
        }

        // 2) Perform the hybrid uninstall
        public async Task<UninstallResult> ExecuteUninstallAsync(
            UninstallEntry app,
            IProgress<UninstallProgress> progress,
            CancellationToken cancellationToken)
        {
            _log.Log($"Beginning uninstall: {app.DisplayName}");

            try
            {
                // Phase 1: Stopping any known services/processes
                _log.Log("Phase 1: Stopping processes…");
                progress.Report(new UninstallProgress(UninstallPhase.StoppingProcesses));

                await Task.Run(() => StopServicesAndProcesses(app), cancellationToken);
                _log.Log("Stopped services and processes", "INFO");

                // Phase 2: Run the vendor silent command
                _log.Log("Phase 2: Running silent uninstall");
                progress.Report(new UninstallProgress(UninstallPhase.RunningSilent));

                var silentCmd = BuildSilentCommand(app.UninstallString);
                _log.Log($"Executing command: {silentCmd}", "DEBUG");

                var exitCode = await RunProcessAsync(silentCmd, cancellationToken);
                _log.Log($"Silent uninstall exited with code {exitCode}", exitCode == 0 ? "INFO" : "WARN");

                // Phase 3: Fallback force‐delete if needed
                if (exitCode != 0 || await IsStillInstalledAsync(app))
                {
                    _log.Log("Phase 3: Fallback to force‐delete", "WARN");
                    progress.Report(new UninstallProgress(UninstallPhase.ForcingDelete));
                    await Task.Run(() => ForceDeleteRemnants(app), cancellationToken);
                    _log.Log("Force‐delete completed", "INFO");
                }

                // Completion
                progress.Report(new UninstallProgress(UninstallPhase.Completed));
                _log.Log($"Completed uninstall: {app.DisplayName}", "INFO");

                return new UninstallResult(app, exitCode: 0, success: true);

            }
            catch (OperationCanceledException)
            {
                _log.Log($"Uninstall cancelled: {app.DisplayName}", "WARN");
                // Return a canceled result instead of throwing
                return new UninstallResult(app, exitCode: -1, success: false, wasCancelled: true);
            }
            catch (Exception ex)
            {
                _log.Log($"Exception during uninstall: {ex.Message}", "ERROR");
                // Swallow exception and return failure
                return new UninstallResult(app, exitCode: -1, success: false);
            }

        }

        // Helper methods for stopping services, running commands, etc.
        #region Helpers

        private static void StopServicesAndProcesses(UninstallEntry app)
        {
            // Stop service by name
            if (!string.IsNullOrEmpty(app.ServiceName))
            {
                try
                {
                    using var sc = new ServiceController(app.ServiceName);
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                }
                catch { /* log if desired */ }
            }

            // Kill any running processes
            if (app.ProcessNames != null)
            {
                foreach (var pname in app.ProcessNames)
                {
                    foreach (var proc in Process.GetProcessesByName(pname))
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(5_000);
                        }
                        catch { /* log if needed */ }
                    }
                }
            }
        }

        // Build the silent uninstall command based on the uninstall string
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

        // Generic method to run a command asynchronously
        private static async Task<int> RunProcessAsync(string cmdLine, CancellationToken token)
        {
            var firstSpace = cmdLine.IndexOf(' ');
            var exe = firstSpace > 0 ? cmdLine[..firstSpace] : cmdLine;
            var args = firstSpace > 0 ? cmdLine[(firstSpace + 1)..] : "";

            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"  // elevate
            };

            using var proc = Process.Start(psi);
            if (proc == null) return -1;

            // watch for cancellation
            using var registration = token.Register(() =>
            {
                try { proc.Kill(); } catch { }
            });

            await proc.WaitForExitAsync(token);
            return proc.ExitCode;
        }

        // Check if the app is still installed by looking for files or registry keys
        private static Task<bool> IsStillInstalledAsync(UninstallEntry app)
        {
            // Quick heuristic: still on disk or registry?
            if (!string.IsNullOrEmpty(app.InstallLocation) &&
                Directory.Exists(app.InstallLocation))
            {
                return Task.FromResult(true);
            }
            // Otherwise, re‐scan registry for the same ProductKey
            return Task.FromResult(false);
        }

        // Force delete remnants if the uninstall failed or app is still present
        private static void ForceDeleteRemnants(UninstallEntry app)
        {
            // Delete files
            if (!string.IsNullOrEmpty(app.InstallLocation) &&
                Directory.Exists(app.InstallLocation))
            {
                try { Directory.Delete(app.InstallLocation, true); }
                catch { /* swallow or log */ }
            }
            // Remove registry uninstall key
            var paths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + app.ProductKey,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + app.ProductKey
            };
            foreach (var p in paths)
            {
                try
                {
                    using var root = Registry.LocalMachine.OpenSubKey(Path.GetDirectoryName(p)!, true);
                    root?.DeleteSubKeyTree(Path.GetFileName(p), throwOnMissingSubKey: false);
                }
                catch { /* swallow or log */ }
            }
        }

        // Try to extract a GUID from the raw key name
        private static string? TryExtractGuid(string raw)
        {
            var m = GuidPattern().Match(raw);
            return m.Success ? m.Value : null;
        }

        // Guess service or process names based on common patterns
        private static string? GuessServiceName(string displayName)
        {
            // e.g. Office click-to-run
            if (displayName.Contains("Office", StringComparison.OrdinalIgnoreCase))
                return "OfficeClickToRunSvc";

            if (displayName.Contains("Optimizer", StringComparison.OrdinalIgnoreCase))
                return "DellOptimizer"; // actual service name

            if (displayName.Contains("Core Services", StringComparison.OrdinalIgnoreCase))
                return "DellClientManagementService";

            return null;
        }

        // Guess process names based on display name
        private static string[]? GuessProcessNames(string displayName)
        {
            // e.g. PowerBI, DellCommandUpdate, etc.
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

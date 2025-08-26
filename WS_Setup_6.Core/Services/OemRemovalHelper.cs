using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core;
using WS_Setup_6.Core.Models;
using WS_Setup_6.Core.Services;
using WS_Setup_6.Core.Interfaces;
using System.Runtime.Versioning;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public static class OemRemovalHelper
    {
        // Patterns for matching display names
        private static readonly string[] DellPatterns =
        {
        "Dell Optimizer",
        "Dell Core Services",
        "Dell Command",
        "Dell Power Manager",
        "Dell SupportAssist",
        "Dell Digital Delivery"
    };

        // Service name map
        private static readonly Dictionary<string, string[]> ServiceMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
            { "Dell Optimizer", new[] { "DellOptimizer" } },
            { "Dell Core Services", new[] { "DellClientManagementService" } },
            { "Dell Command", new[] { "DellCommandUpdate" } },
            { "Dell Power Manager", new[] { "DellPwrMgrSvc" } },
            };

        // Process name map
        private static readonly Dictionary<string, string[]> ProcessMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
            { "Dell Optimizer", new[] { "DellOptimizer", "DOCLI" } },
            { "Dell Core Services", new[] { "DellClientManagementService" } },
            { "Dell Command", new[] { "DellCommandUpdate" } },
            };

        // Main method to remove known Dell OEM apps
        public static async Task RemoveOemAppsAsync(
            IEnumerable<UninstallEntry> allApps,
            ILogService log,
            CancellationToken token)
        {
            foreach (var app in allApps)
            {
                if (!IsDellOem(app.DisplayName))
                    continue;

                log.Log($"OEM removal target: {app.DisplayName}", "INFO");

                // Stop services
                if (ServiceMap.TryGetValue(MatchKey(app.DisplayName), out var services))
                {
                    foreach (var svc in services)
                        StopServiceSafe(svc, log);
                }

                // Kill processes
                if (ProcessMap.TryGetValue(MatchKey(app.DisplayName), out var procs))
                {
                    foreach (var pname in procs)
                        KillProcessSafe(pname, log);
                }

                // Build uninstall command with overrides if needed
                var uninstallCmd = BuildSilentUninstallCommand(app);
                if (!string.IsNullOrWhiteSpace(uninstallCmd))
                {
                    var exitCode = await RunProcessAsync(uninstallCmd);
                    log.Log($"{app.DisplayName} uninstall exit code: {exitCode}", "INFO");
                }

                // Verify removal and force-delete if still installed
                if (await IsStillInstalledAsync(app))
                {
                    log.Log($"{app.DisplayName} still detected, forcing removal", "WARN");
                    ForceDeleteRemnants(app);
                }
            }
        }

        // Simple name match for known Dell OEM apps
        private static bool IsDellOem(string name) =>
            DellPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));

        // Match display name to known patterns
        private static string MatchKey(string displayName) =>
            DellPatterns.FirstOrDefault(p => displayName.Contains(p, StringComparison.OrdinalIgnoreCase)) ?? displayName;

        // Stop services by name
        private static void StopServiceSafe(string svc, ILogService log)
        {
            try
            {
                using var sc = new ServiceController(svc);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    log.Log($"Stopped service: {svc}", "INFO");
                }
            }
            catch (Exception ex)
            {
                log.Log($"Service stop failed ({svc}): {ex.Message}", "WARN");
            }
        }

        // Kill processes by name
        private static void KillProcessSafe(string pname, ILogService log)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(pname))
                {
                    proc.Kill();
                    proc.WaitForExit(5000);
                    log.Log($"Killed process: {pname} (PID {proc.Id})", "INFO");
                }
            }
            catch (Exception ex)
            {
                log.Log($"Process kill failed ({pname}): {ex.Message}", "WARN");
            }
        }

        // Build silent uninstall command with overrides for known apps
        private static string BuildSilentUninstallCommand(UninstallEntry app)
        {
            var uninstall = app.UninstallString ?? string.Empty;

            // Override for known stubborn apps
            if (app.DisplayName.Contains("Optimizer", StringComparison.OrdinalIgnoreCase))
                return $"{uninstall} /S";

            // Default logic
            var firstSpace = uninstall.IndexOf(' ');
            var exe = firstSpace > 0 ? uninstall[..firstSpace] : uninstall;
            var args = firstSpace > 0 ? uninstall[(firstSpace + 1)..] : "";

            exe = exe.Trim('"');
            if (exe.EndsWith("msiexec", StringComparison.OrdinalIgnoreCase))
                return $"{exe} {args} /qn /norestart";

            return $"{uninstall} /quiet";
        }

        // Runs a process asynchronously, capturing output and errors
        private static Task<int> RunProcessAsync(
        string exePath,
        string args,
        Action<string> onOutput,
        Action<string> onError,
        bool useShellExecute = false,
        string? verb = null)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Executable not found", exePath);

            var tcs = new TaskCompletionSource<int>();

            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = useShellExecute,
                Verb = verb,
                RedirectStandardOutput = !useShellExecute,
                RedirectStandardError = !useShellExecute,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            if (!useShellExecute)
            {
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) onOutput(e.Data);
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) onError(e.Data);
                };
            }

            proc.Exited += (_, _) =>
            {
                tcs.TrySetResult(proc.ExitCode);
                proc.Dispose();
            };

            proc.Start();

            if (!useShellExecute)
            {
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }

            return tcs.Task;
        }

        // 🔹 New convenience overload: accepts a single full command line
        private static Task<int> RunProcessAsync(
            string fullCommandLine,
            bool useShellExecute = false,
            string? verb = null) =>
            RunProcessAsync(
                SplitCommand(fullCommandLine, out var exe, out var args),
                args,
                _ => { }, _ => { },
                useShellExecute,
                verb
            );

        // 🔹 New overload for single full command line + logging
        public static Task<int> RunProcessAsync(
            string fullCommandLine,
            Action<string> onOutput,
            Action<string> onError,
            bool useShellExecute = false,
            string? verb = null)
        {
            SplitCommand(fullCommandLine, out var exe, out var args);
            return RunProcessAsync(exe, args, onOutput, onError, useShellExecute, verb);
        }

        // Internal command splitter
        private static string SplitCommand(string fullCommandLine, out string exe, out string args)
        {
            if (string.IsNullOrWhiteSpace(fullCommandLine))
                throw new ArgumentException("Command line cannot be null or empty.", nameof(fullCommandLine));

            // Handles quoted paths with spaces
            if (fullCommandLine.StartsWith("\""))
            {
                var endQuote = fullCommandLine.IndexOf('"', 1);
                exe = fullCommandLine.Substring(1, endQuote - 1);
                args = fullCommandLine[(endQuote + 1)..].TrimStart();
            }
            else
            {
                var firstSpace = fullCommandLine.IndexOf(' ');
                if (firstSpace == -1)
                {
                    exe = fullCommandLine;
                    args = string.Empty;
                }
                else
                {
                    exe = fullCommandLine[..firstSpace];
                    args = fullCommandLine[(firstSpace + 1)..];
                }
            }
            return exe;
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
    }
}

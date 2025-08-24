using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Models;
using System.Text.RegularExpressions;

namespace WS_Setup_6.Core.Services
{
    public class RegistryUninstallScanner : IUninstallScanner
    {
        // Both 64-bit and 32-bit uninstall registry paths
        private static readonly string[] _registryUninstallPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        // Compiled regex for GUID matching
        private static Regex GuidPattern()
        {
            return new Regex(@"\{?[0-9A-Fa-f]{8}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{12}\}?",
                             RegexOptions.Compiled);
        }

        // Main scanning method
        public async Task<IReadOnlyList<UninstallEntry>> ScanInstalledAppsAsync()
        {
            return await Task.Run(() =>
            {
                var entries = new List<UninstallEntry>();

                foreach (var basePath in _registryUninstallPaths)
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

                        // skip entries without display name or uninstall command
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
                            ServiceName = GuessServiceName(name),
                            ProcessNames = GuessProcessNames(name)
                        });
                    }
                }

                // Alphabetical sort
                return entries
                    .OrderBy(e => e.DisplayName, StringComparer.InvariantCultureIgnoreCase)
                    .ToList();
            });
        }

        // You can reuse your existing helpers here or inject them instead
        private static string? TryExtractGuid(string rawKeyName)
        {
            // Your existing GUID regex matcher here
            var m = GuidPattern().Match(rawKeyName);
            return m.Success ? m.Value : null;
        }

        // Simple heuristic-based service name guessing
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

        // Simple heuristic-based process name guessing
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
    }
}

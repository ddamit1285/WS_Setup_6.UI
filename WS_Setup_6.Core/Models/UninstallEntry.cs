using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace WS_Setup_6.Core.Models
{
    [SupportedOSPlatform("windows")]
    public class UninstallEntry
    {
        public string DisplayName { get; set; } = "";
        public string UninstallString { get; set; } = "";
        public string? InstallLocation { get; set; }
        public string? DisplayVersion { get; set; }
        public string? Publisher { get; set; }
        public string ProductKey { get; set; } = "";   // registry subkey or GUID
        public string? ServiceName { get; set; }        // optional
        public string[]? ProcessNames { get; set; }     // optional

        // New: vendor-provided silent uninstall string (if available)
        public string? SilentUninstallString { get; set; }
        public string? QuietUninstallString { get; set; } // optional alternative to SilentUninstallString
        public RegistryKey? RegistryKey { get; set; }
        public bool WindowsInstaller { get; set; }


        // New props for recording results
        public bool? Success { get; set; }
        public bool WasCancelled { get; set; }
        public int ExitCode { get; set; }
    }
}
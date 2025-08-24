using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace WS_Setup_6.Core.Models
{
    [SupportedOSPlatform("windows")]
    public class UninstallResult
    {
        public UninstallEntry? Entry { get; set; }
        public int ExitCode { get; set; }
        public bool Success { get; set; }
        public bool WasCancelled { get; set; }

        // parameterless ctor (for e.g. serialization)
        public UninstallResult() { }

        // convenience ctor:
        public UninstallResult(
            UninstallEntry entry,
            int exitCode,
            bool success,
            bool wasCancelled = false)
        {
            Entry = entry;
            ExitCode = exitCode;
            Success = success;
            WasCancelled = wasCancelled;
        }
    }
}

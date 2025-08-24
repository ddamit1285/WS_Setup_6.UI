using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using WS_Setup_6.Core.Models;

namespace WS_Setup_6.Core.Interfaces
{
    /// <summary>
    /// Defines methods for querying installed applications and
    /// executing their uninstallation on Windows platforms.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public interface IUninstallService
    {
        Task<IReadOnlyList<UninstallEntry>> QueryInstalledAppsAsync();

        Task<UninstallResult> ExecuteUninstallAsync(
            UninstallEntry app,
            IProgress<UninstallProgress> progress,
            CancellationToken cancellationToken);
    }
}
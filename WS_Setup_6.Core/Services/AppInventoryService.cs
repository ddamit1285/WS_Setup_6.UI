using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Models;

namespace WS_Setup_6.Core.Services
{
    public class AppInventoryService(IUninstallScanner scanner) : IAppInventoryService
    {
        private readonly IUninstallScanner _scanner = scanner;

        // Note: The scanner itself handles async work internally
        public async Task<IReadOnlyList<UninstallEntry>> ScanInstalledAppsAsync()
        {
            // Scanner already does its own async work
            return await _scanner.ScanInstalledAppsAsync();
        }
    }
}
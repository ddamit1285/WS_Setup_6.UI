using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WS_Setup_6.Core.Models;

namespace WS_Setup_6.Core.Interfaces
{
    public interface IUninstallScanner
    {
        Task<IReadOnlyList<UninstallEntry>> ScanInstalledAppsAsync();
    }
}

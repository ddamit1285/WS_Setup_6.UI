using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WS_Setup_6.Core.Models;

namespace WS_Setup_6.Core.Interfaces
{
    public interface IOemRemovalService
    {
        /// <summary>
        /// Removes the given OEM apps silently if possible.
        /// </summary>
        Task RemoveOemAppsAsync(
            IEnumerable<UninstallEntry> apps,
            CancellationToken cancellationToken = default);
    }
}
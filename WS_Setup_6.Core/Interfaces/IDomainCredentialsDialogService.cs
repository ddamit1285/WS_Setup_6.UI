using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WS_Setup_6.Core.Interfaces
{
    public interface IDomainCredentialsDialogService
    {
        /// <summary>
        /// Shows a login dialog in the view that’s registered for dialogContext,
        /// returning the NetworkCredential or null if cancelled.
        /// </summary>
        Task<NetworkCredential?> ShowAsync(string domainName);
    }
}

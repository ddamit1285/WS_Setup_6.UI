using MahApps.Metro.Controls.Dialogs;
using System;
using System.Net;
using System.Threading.Tasks;
using WS_Setup_6.Core.Interfaces;

namespace WS_Setup_6.UI.Services
{
    public class DomainCredentialsDialogService : IDomainCredentialsDialogService
    {
        private readonly IDialogCoordinator _dialogs;

        public DomainCredentialsDialogService(IDialogCoordinator dialogs)
            => _dialogs = dialogs;

        public async Task<NetworkCredential?> ShowAsync(string domainName)
        {
            var settings = new LoginDialogSettings
            {
                ShouldHideUsername = false
            };

            // Pass *exactly* the same dialogContext that your VM uses
            var result = await _dialogs.ShowLoginAsync(
                "MainHost",
                $"Credentials for {domainName}",
                "Enter your domain credentials:",
                settings);

            if (result == null)
                return null;

            return new NetworkCredential(
                result.Username,
                result.Password,
                domainName);
        }
    }
}
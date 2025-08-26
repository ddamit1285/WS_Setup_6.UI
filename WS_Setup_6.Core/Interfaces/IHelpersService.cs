using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace WS_Setup_6.Core.Interfaces
{
    public interface IHelpersService
    {
        /// <summary>
        /// Ensures the given path is a non-empty, .msi file that exists locally.
        /// </summary>
        /// <param name="rawPath">User-entered installer path.</param>
        /// <param name="log">Logger for verbose messages.</param>
        /// <param name="setStatus">UI status updater.</param>
        /// <returns>
        /// The validated local path, or null if invalid/missing.
        /// </returns>
        Task<string?> ValidateInstallerPathAsync(
            string rawPath,
            Action<string> log,
            Action<string> setStatus);

        /// <summary>
        /// Attempts to join the machine to the given domain, prompting up to maxRetries times.
        /// Returns true if joined or skipped by user; false on fatal error.
        /// </summary>
        Task<bool> TryJoinDomainAsync(
            string domainName,
            Func<string, Task<NetworkCredential?>> promptForCredentials,
            Action<string> log,
            Action<string> setStatus,
            int maxRetries = 3);

        /// <summary>
        /// Deletes the file at path if it exists, logs any errors.
        /// </summary>
        void TryDelete(string path, Action<string> log);

        /// <summary>
        /// Returns the first .msi on the current user’s Desktop
        /// whose filename starts with "NinjaOne-Agent", or null.
        /// </summary>
        string? FindAgentInstallerOnDesktop();

        /// <summary>
        /// Shared Build silent uninstaller logic for OEM removal.
        /// </summary> 
        // Build the silent uninstall command based on the uninstall string
        string BuildSilentCommand(string uninstallString);
    }
}
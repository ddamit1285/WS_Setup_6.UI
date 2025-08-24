using System.Net;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace WS_Setup_6.Core.Interfaces
{
    [SupportedOSPlatform("windows")]
    public interface IOnboardService
    {
        /// <summary>
        /// Joins the machine to the specified domain.
        /// </summary>
        Task RunDomainJoinAsync(string domainName, NetworkCredential creds);

        /// <summary>
        /// Installs the RMM agent from the given MSI path.
        /// </summary>
        Task InstallAgentAsync(string installerPath);

        /// <summary>
        /// Ensures Google Chrome is installed.
        /// </summary>
        Task InstallChromeAsync();

        /// <summary>
        /// Ensures Adobe Reader is installed.
        /// </summary>
        Task InstallAdobeReaderAsync();

        /// <summary>
        /// Sets up dependencies (PS7, remoting, etc.).
        /// </summary>
        Task SetupDependenciesAsync();

        /// <summary>
        /// Downloads and installs DSC v3.
        /// </summary>
        Task InstallDsc3Async();
    }
}
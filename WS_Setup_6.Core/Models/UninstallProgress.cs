using System.Runtime.Versioning;

namespace WS_Setup_6.Core.Models
{
    [SupportedOSPlatform("windows")]
    public record UninstallProgress(
        UninstallPhase Phase,
        int Percentage = 0
    );
}

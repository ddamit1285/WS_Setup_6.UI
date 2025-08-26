using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public static partial class RegexHelpers
    {
        [GeneratedRegex(@"\{?[0-9A-Fa-f]{8}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{12}\}?")]
        public static partial Regex GuidPattern();
    }
}
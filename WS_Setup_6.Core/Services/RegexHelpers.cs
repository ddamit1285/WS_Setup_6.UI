using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace WS_Setup_6.Core.Services
{
    public static partial class RegexHelpers
    {
        [GeneratedRegex(@"\{?[0-9A-Fa-f]{8}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{4}\-[0-9A-Fa-f]{12}\}?")]
        public static partial Regex GuidPattern();
    }
}
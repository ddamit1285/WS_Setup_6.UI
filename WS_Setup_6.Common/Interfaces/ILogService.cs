using System.Runtime.Versioning;
using WS_Setup_6.Common.Logging;

namespace WS_Setup_6.Common.Interfaces
{
    [SupportedOSPlatform("windows")]
    public interface ILogService
    {
        event Action<LogEntry>? EntryAdded;
        void Log(string message, string level = "INFO");
    }

    // optional sub‐interface that LogManager can implement
    public interface ILogServiceWithHistory : ILogService
    {
        IReadOnlyList<LogEntry> GetAll();
    }
}

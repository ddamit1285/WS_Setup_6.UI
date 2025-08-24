using System.Runtime.Versioning;

namespace WS_Setup_6.Common.Logging
{
    [SupportedOSPlatform("windows")]
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? Message { get; set; }
        public string? Level { get; set; }

        /// <summary>
        /// Formats the log entry with timestamp for file output.
        /// Example: 2025-07-18 10:37:08 [INFO] Installing Chrome…
        /// </summary>
        public string ToLogFileFormat()
        {
            return $"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}";
        }

        /// <summary>
        /// Formats the log entry for display, without timestamp.
        /// Example: [INFO] Installing Chrome…
        /// </summary>
        public override string ToString()
        {
            return $"[{Level}] {Message}";
        }

        /// <summary>
        /// Appends the log entry to the given file path.
        /// </summary>
        public static void Append(string logFilePath, LogEntry entry)
        {
            try
            {
                File.AppendAllText(
                    logFilePath,
                    entry.ToLogFileFormat() + Environment.NewLine
                );
            }
            catch
            {
                // Optional: handle or log fallback elsewhere
            }
        }
    }
}

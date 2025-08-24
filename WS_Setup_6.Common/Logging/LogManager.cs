using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using WS_Setup_6.Common.Interfaces;

namespace WS_Setup_6.Common.Logging
{
    [SupportedOSPlatform("windows")]
    public class LogManager : ILogServiceWithHistory
    {
        private readonly object _syncRoot = new();
        private readonly List<LogEntry> _entries = new();
        private readonly string _logFilePath;

        public event Action<LogEntry>? EntryAdded;

        public LogManager(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
                throw new ArgumentException("logFilePath cannot be null or empty", nameof(logFilePath));

            _logFilePath = logFilePath;

            // Ensure directory exists
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Create header if file is new
            if (!File.Exists(_logFilePath))
            {
                var header = $"== Onboarding Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} =={Environment.NewLine}";
                File.WriteAllText(_logFilePath, header);
            }
        }

        /// <summary>
        /// Adds a new log entry in memory, writes it to disk, 
        /// and raises the EntryAdded event.
        /// </summary>
        public void Log(string message, string level = "INFO")
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            // 1) Record in‐memory and to disk under a lock
            lock (_syncRoot)
            {
            _entries.Add(entry);
                File.AppendAllText(
                  _logFilePath,
                  entry.ToLogFileFormat() + Environment.NewLine);
            }

            // 2) Notify subscribers outside the lock
            EntryAdded?.Invoke(entry);
        }

        /// <summary>
        /// Returns a snapshot of all log entries.
        /// </summary>
        public IReadOnlyList<LogEntry> GetAll()
        {
            lock (_syncRoot)
            {
                return _entries.ToList();
            }
        }

        /// <summary>
        /// Returns entries matching the given level (or "SUMMARY"), or all if level == "All".
        /// </summary>
        public IReadOnlyList<LogEntry> GetFiltered(string filterLevel)
        {
            if (string.Equals(filterLevel, "All", StringComparison.OrdinalIgnoreCase))
                return GetAll();

            lock (_syncRoot)
            {
                return _entries
                  .Where(e =>
                    string.Equals(e.Level, filterLevel, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.Level, "SUMMARY", StringComparison.OrdinalIgnoreCase))
                  .ToList();
            }
        }
    }
}
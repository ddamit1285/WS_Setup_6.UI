using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;

namespace WS_Setup_6.UI.ViewModels.Pages
{
    [SupportedOSPlatform("windows")]
    public class LogViewModel : ObservableObject
    {
        public ObservableCollection<LogEntry> LogEntries { get; }
            = new ObservableCollection<LogEntry>();

        public LogViewModel(ILogService logService)
        {
            // 1) preload any already‐logged entries (optional if you’ve added GetAll to ILogService)
            if (logService is ILogServiceWithHistory hist)
            {
                foreach (var old in hist.GetAll())
                    LogEntries.Add(old);
            }

            // 2) subscribe to live events
            logService.EntryAdded += entry =>
            {
                // marshal back onto the UI thread
                Application.Current.Dispatcher.BeginInvoke(
                    () => LogEntries.Add(entry));
            };
        }
    }
}
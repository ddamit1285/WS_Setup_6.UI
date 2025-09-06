using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Models;

namespace WS_Setup_6.UI.ViewModels
{
    [SupportedOSPlatform("windows")]
    public partial class UninstallViewModel : ObservableObject
    {
        private readonly ILogService _log;
        private readonly IUninstallService _uninstallService;
        private readonly IAppInventoryService _appInventoryService;
        private CancellationTokenSource? _cts;

        public ObservableCollection<UninstallEntry> InstalledApps { get; }
        public ObservableCollection<UninstallEntry> SelectedApps { get; set; }

        [ObservableProperty] private string statusMessage = string.Empty;
        [ObservableProperty] private bool isUninstalling;
        [ObservableProperty] private int progressPercentage;

        public int BatchMax { get; private set; }
        private int _batchProgress;
        public int BatchProgress
        {
            get => _batchProgress;
            private set => SetProperty(ref _batchProgress, value);
        }

        public IAsyncRelayCommand LoadAppsCommand { get; }
        public IAsyncRelayCommand UninstallSelectedCommand { get; }

        public bool CanExecute => SelectedApps.Any() && !IsUninstalling;

        public UninstallViewModel(
            IUninstallService uninstallService,
            ILogService log,
            IAppInventoryService appInventoryService)
        {
            _uninstallService = uninstallService;
            _log = log;
            _appInventoryService = appInventoryService;

            InstalledApps = new ObservableCollection<UninstallEntry>();
            SelectedApps = new ObservableCollection<UninstallEntry>();

            LoadAppsCommand = new AsyncRelayCommand(LoadAppsAsync);
            UninstallSelectedCommand = new AsyncRelayCommand(
                ExecuteBatchUninstallAsync,
                () => CanExecute
            );
            
            // Hook into selection changes so CanExecute re‑evaluates
            SelectedApps.CollectionChanged += (_, __) =>
            UninstallSelectedCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsUninstallingChanged(bool oldValue, bool newValue) =>
            UninstallSelectedCommand.NotifyCanExecuteChanged();

        private async Task LoadAppsAsync()
        {
            InstalledApps.Clear();
            var entries = await _appInventoryService.ScanInstalledAppsAsync();
            foreach (var entry in entries)
            {
                InstalledApps.Add(entry);
            }
        }

        private async Task ExecuteBatchUninstallAsync()
        {
            IsUninstalling = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var apps = SelectedApps.ToList();
            int total = apps.Count;
            BatchMax = 100;
            BatchProgress = 0;

            // Reorder: silent/MSI first, interactive-only last
            var silentApps = apps.Where(app => !_uninstallService.IsInteractiveOnly(app)).ToList();
            var interactiveApps = apps.Where(app => _uninstallService.IsInteractiveOnly(app)).ToList();
            var orderedApps = silentApps.Concat(interactiveApps).ToList();

            int completed = 0;

            foreach (var app in orderedApps)
            {
                StatusMessage = $"Uninstalling {completed + 1} of {total}: {app.DisplayName}";

                var progress = new Progress<UninstallProgress>(_ => { });
                var result = await _uninstallService.ExecuteUninstallAsync(app, progress, _cts.Token);

                app.Success = result.Success;
                app.ExitCode = result.ExitCode;
                app.WasCancelled = result.WasCancelled;

                completed++;
                BatchProgress = (int)((completed / (double)total) * BatchMax);
            }

            StatusMessage = $"Batch uninstall complete. {completed} apps processed.";

            // Refresh app list
            await LoadAppsAsync();

            // Fallback: retry interactive uninstall for apps still present
            var remaining = InstalledApps.Where(installed =>
                apps.Any(original =>
                    string.Equals(original.DisplayName, installed.DisplayName, StringComparison.OrdinalIgnoreCase)
                    && _uninstallService.IsInteractiveOnly(installed)))
                .ToList();

            foreach (var app in remaining)
            {
                StatusMessage = $"Retrying interactively: {app.DisplayName}";
                _log.Log($"[Fallback] Launching interactive uninstall for {app.DisplayName}", "INFO");

                var result = await _uninstallService.ExecuteUninstallAsync(app, new Progress<UninstallProgress>(_ => { }), CancellationToken.None);

                app.Success = result.Success;
                app.ExitCode = result.ExitCode;
                app.WasCancelled = result.WasCancelled;
            }

            StatusMessage = $"Uninstall complete. {completed} apps processed silently, {remaining.Count} retried interactively.";
            IsUninstalling = false;
            await LoadAppsAsync();
        }
    }
}

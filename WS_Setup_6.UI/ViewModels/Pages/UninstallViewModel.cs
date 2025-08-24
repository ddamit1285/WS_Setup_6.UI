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
        private readonly IOemRemovalService _oemRemovalService;
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
        public IAsyncRelayCommand PurgeOemCommand { get; }

        public bool CanExecute => SelectedApps.Any() && !IsUninstalling;

        public UninstallViewModel(
            IUninstallService uninstallService,
            IOemRemovalService oemRemovalService,
            ILogService log,
            IAppInventoryService appInventoryService)
        {
            _uninstallService = uninstallService;
            _oemRemovalService = oemRemovalService;
            _log = log;
            _appInventoryService = appInventoryService;

            InstalledApps = new ObservableCollection<UninstallEntry>();
            SelectedApps = new ObservableCollection<UninstallEntry>();

            LoadAppsCommand = new AsyncRelayCommand(LoadAppsAsync);
            UninstallSelectedCommand = new AsyncRelayCommand(
                ExecuteBatchUninstallAsync,
                () => CanExecute
            );
            PurgeOemCommand = new AsyncRelayCommand(RemoveStubbornAppsAsync);

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
            BatchMax = total * 100;
            BatchProgress = 0;

            for (int i = 0; i < total; i++)
            {
                var app = apps[i];
                int baseOffset = i * 100;

                StatusMessage = $"Uninstalling ({i + 1}/{total}): {app.DisplayName}";

                var progress = new Progress<UninstallProgress>(uip =>
                {
                    BatchProgress = baseOffset + uip.Percentage;
                    StatusMessage = $"{app.DisplayName}: {uip.Phase}";
                });

                var result = await _uninstallService.ExecuteUninstallAsync(app, progress, _cts.Token);

                app.Success = result.Success;
                app.ExitCode = result.ExitCode;
                app.WasCancelled = result.WasCancelled;

                BatchProgress = (i + 1) * 100;
            }

            StatusMessage = "Batch uninstall complete.";
            IsUninstalling = false;
            await LoadAppsAsync();
        }

        private async Task RemoveStubbornAppsAsync()
        {
            try
            {
                _log.Log("Starting OEM stubborn app removal…", "INFO");

                var oemCandidates = InstalledApps
                    .Where(a => a.Publisher?.Contains("Dell", StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();

                await _oemRemovalService.RemoveOemAppsAsync(oemCandidates);

                _log.Log("OEM stubborn app removal complete.", "INFO");
            }
            catch (Exception ex)
            {
                _log.Log($"OEM removal error: {ex.Message}", "ERROR");
            }
        }
    }
}
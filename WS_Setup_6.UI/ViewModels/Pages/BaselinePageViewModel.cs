using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows.Threading;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.Core.Services;

namespace WS_Setup_6.UI.ViewModels.Pages
{
    [SupportedOSPlatform("windows")]
    public partial class BaselinePageViewModel : ObservableObject
    {
        private const string DialogContextKey = "MainHost";

        private ProgressDialogController? _progressController;
        private readonly MainWindowModel _shell;
        private readonly IOnboardService _onboard;
        private readonly IBaselineService _baseline;
        private readonly IHelpersService _helpers;
        private readonly ILogService _log;
        private readonly IDialogCoordinator _dialog;
        private readonly INavigationService _nav;

        private readonly string _encYaml;
        private readonly string _decYaml;
        private readonly byte[] _key;
        private readonly byte[] _iv;

        private DispatcherTimer _ellipsisTimer;
        private int _dotCount;

        private string _waitMessage = "Please be patient";
        public string WaitMessage
        {
            get => _waitMessage;
            set => SetProperty(ref _waitMessage, value);
        }

        public bool IsApplyEnabled => !_shell.IsApplyingBaseline;

        [ObservableProperty]
        private string baselineStatusMessage = string.Empty;

        [ObservableProperty]
        private double progressValue;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanApplyBaseline))]
        private bool isApplyingBaseline;

        public bool CanApplyBaseline => !IsApplyingBaseline;

        public IAsyncRelayCommand ApplyBaselineCommand { get; }

        public BaselinePageViewModel(
            IHelpersService helpers,
            IOnboardService onboard,
            IBaselineService baseline,
            ILogService log,
            IDialogCoordinator dialog,
            INavigationService nav,
            MainWindowModel shell)
        {
            _helpers = helpers;
            _onboard = onboard;
            _baseline = baseline;
            _log = log;
            _dialog = dialog;
            _nav = nav;
            _shell = shell;
            _shell.PropertyChanged += OnShellPropertyChanged;

            _ellipsisTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _ellipsisTimer.Tick += UpdateEllipsis;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var assets = Path.Combine(baseDir, "Assets");
            _encYaml = Path.Combine(assets, "WSConfig_encrypted.yml");
            _decYaml = Path.Combine(baseDir, "WSConfig.yml");
            _key = Convert.FromBase64String("+9s4TlWmueWkzQwuDZ1tOA==");
            _iv = Convert.FromBase64String("5VbPN958hsy8w4XeGTCOzw==");

            ApplyBaselineCommand = new AsyncRelayCommand(
                ApplyBaselineAsync,
                () => CanApplyBaseline);
        }

        partial void OnIsApplyingBaselineChanged(bool oldValue, bool newValue) =>
            ApplyBaselineCommand.NotifyCanExecuteChanged();

        private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_shell.IsApplyingBaseline))
                OnPropertyChanged(nameof(IsApplyEnabled));
        }

        private void UpdateEllipsis(object? sender, EventArgs e)
        {
            _dotCount = (_dotCount + 1) % 4;
            WaitMessage = "Please be patient." + new string('.', _dotCount);
            _progressController?.SetMessage(WaitMessage);
        }

        private async Task ApplyBaselineAsync()
        {
            _shell.IsApplyingBaseline = true;
            var hadErrors = false;

            // Show a progress dialog on the MainWindow
            _progressController = await _dialog.ShowProgressAsync(
                DialogContextKey,
                "Applying Baseline Configuration",
                "Initializing…",
                false,
                new MetroDialogSettings { AnimateShow = true });

            try
            {
                _ellipsisTimer.Start();
                ProgressValue = 50;
                _log.Log("Installing dependencies", "INFO");
                await _onboard.SetupDependenciesAsync();

                _progressController.SetMessage("Installing Desired State Configuration v3…");
                ProgressValue = 60;
                _log.Log("Installing DSC v3", "INFO");
                await _onboard.InstallDsc3Async();

                _progressController.SetMessage("Decrypting baseline configuration");
                _log.Log("Decrypting baseline configuration", "INFO");
                _baseline.DecryptConfig(_encYaml, _decYaml, _key, _iv);

                ProgressValue = 70;
                await Task.Yield();

                if (File.Exists(_decYaml))
                {
                    ProgressValue = 85;
                    _log.Log("Applying baseline via DSC", "INFO");
                    await _baseline.RunDscSimpleAsync(_decYaml);
                }
                else
                {
                    hadErrors = true;
                    await ShowDialogAsync("Configuration Error",
                        "The decrypted configuration file could not be found. Please check the log for details.");
                }
            }
            catch (Exception ex)
            {
                hadErrors = true;
                _log.Log($"Baseline process failed: {ex.Message}", "ERROR");
                await ShowDialogAsync("Baseline Error", ex.Message);
            }
            finally
            {
                _helpers.TryDelete(_decYaml, msg => _log.Log(msg, "INFO"));
                ProgressValue = 90;
                _progressController.SetMessage(
                    hadErrors ? "Baseline applied with errors." : "Baseline applied successfully.");
                ProgressValue = 100;

                _ellipsisTimer.Stop();
                _dotCount = 0;
                WaitMessage = "Please be patient";

                _shell.IsApplyingBaseline = false;
                await Task.Delay(2000);

                if (_progressController.IsOpen)
                    await _progressController.CloseAsync();

                BaselineStatusMessage = "Baseline run complete."
                    + (hadErrors ? " (with errors)" : string.Empty);
            }
        }

        private Task ShowDialogAsync(string title, string message) =>
            _dialog.ShowMessageAsync(
                DialogContextKey,
                title,
                message,
                MessageDialogStyle.Affirmative);
    }
}
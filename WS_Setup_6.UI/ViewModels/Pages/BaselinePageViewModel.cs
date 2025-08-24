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
        private int _dotCount = 0;

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
            _ellipsisTimer.Tick += (s, e) =>
            {
                _dotCount = (_dotCount + 1) % 4;
                WaitMessage = "Please be patient" + new string('.', _dotCount);
                _progressController?.SetMessage(WaitMessage);
            };

            var baseDir = AppContext.BaseDirectory;
            var assets = Path.Combine(baseDir, "Assets");

            _encYaml = Path.Combine(assets, "WSConfig_encrypted.yml");
            _decYaml = Path.Combine(baseDir, "WSConfig.yml");
            _key = Convert.FromBase64String("+9s4TlWmueWkzQwuDZ1tOA==");
            _iv = Convert.FromBase64String("5VbPN958hsy8w4XeGTCOzw==");

            ApplyBaselineCommand = new AsyncRelayCommand(
                ApplyBaselineAsync,
                () => CanApplyBaseline
            );
        }

        partial void OnIsApplyingBaselineChanged(bool oldValue, bool newValue) =>
            ApplyBaselineCommand.NotifyCanExecuteChanged();

        private async Task ApplyBaselineAsync()
        {
            _shell.IsApplyingBaseline = true;
            bool hadErrors = false;

            // Show a progress dialog on the MainWindow
            _progressController = await _dialog.ShowProgressAsync(
                "MainHost",
                "Applying Baseline Configuration",
                "Initializing…",
                false,
                new MetroDialogSettings { AnimateShow = true });

            // ① Prepare
            _ellipsisTimer.Start();
            _progressController.SetMessage("Setting up dependencies…");
            ProgressValue = 50;
            _log.Log("Installing dependencies", "INFO");
            await _onboard.SetupDependenciesAsync();

            await Task.Yield();

            _progressController.SetMessage("Installing Desired State Configuration v3…");
            ProgressValue = 60;
            _log.Log("Installing DSC v3", "INFO");
            await _onboard.InstallDsc3Async();
            _progressController.SetMessage("Decrypting baseline configuration");
            _log.Log("Decrypting baseline configuration", "INFO");
            try
            {
                _baseline.DecryptConfig(_encYaml, _decYaml, _key, _iv);
            }
            catch (Exception ex)
            {
                _log.Log($"Decryption failed: {ex.Message}", "ERROR");
                await _dialog.ShowMessageAsync(
                    this,
                    "Decryption Error",
                    ex.Message,
                    MessageDialogStyle.Affirmative);
                hadErrors = true;
            }

            ProgressValue = 70;

            await Task.Yield();

            // ② Apply via DSC
            if (File.Exists(_decYaml))
            {
                ProgressValue = 85;
                _log.Log("Applying baseline via DSC", "INFO");

                try
                {
                    await _baseline.RunDscSimpleAsync(_decYaml);
                }
                catch (Exception ex)
                {
                    _log.Log($"DSC application failed: {ex.Message}", "ERROR");
                    await _dialog.ShowMessageAsync(
                        this,
                        "Baseline Error",
                        ex.Message,
                        MessageDialogStyle.Affirmative);
                    hadErrors = true;
                }
            }
            else
            {
                await _dialog.ShowMessageAsync(
                    this,
                    "Configuration Error",
                    "The decrypted configuration file could not be found. Please check the log for details.",
                    MessageDialogStyle.Affirmative);
                hadErrors = true;
            }

            // ③ Cleanup
            _helpers.TryDelete(_decYaml, msg => _log.Log(msg, "INFO"));
            ProgressValue = 90;
            _progressController.SetMessage(
                hadErrors
                    ? "Baseline applied with errors."
                    : "Baseline applied successfully."
            );
            ProgressValue = 100;
            _shell.IsApplyingBaseline = false;
            _ellipsisTimer.Stop();
            _dotCount = 0;
            WaitMessage = "Please be patient";
            await Task.Delay(2000);
            if (_progressController.IsOpen)
            {
                await _progressController.CloseAsync();
            }
            BaselineStatusMessage = "Baseline run complete." + (hadErrors ? " (with errors)" : string.Empty);
        }

        // Listen to MainWindowModel changes
        private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_shell.IsApplyingBaseline))
            {
                OnPropertyChanged(nameof(IsApplyEnabled));
            }
        }
    }
}
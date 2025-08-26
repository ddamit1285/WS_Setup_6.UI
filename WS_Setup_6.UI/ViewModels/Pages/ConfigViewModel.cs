using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.Controls.Dialogs;
using System.IO;
using System.Runtime.Versioning;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Core.Interfaces;

namespace WS_Setup_6.UI.ViewModels.Pages
{
    [SupportedOSPlatform("windows")]
    public partial class ConfigurationPageViewModel : ObservableObject
    {
        // ── Declarations ────────────────────────────────────────────────
        private const int DomainJoinMaxRetries = 3;
        private readonly IHelpersService _helpers;
        private readonly INavigationService _nav;
        private readonly IOnboardService _onboardSvc;
        private readonly IDomainCredentialsDialogService _dialog;
        private readonly IDialogCoordinator _dialogCoordinator;
        private readonly ILogService _log;

        private string? _installerPath;

        // Progress visibility and indeterminate state
        private bool _isProgressVisible;
        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set => SetProperty(ref _isProgressVisible, value);
        }

        // Indicates if the progress bar is indeterminate (e.g., for long-running tasks)
        private bool _isIndeterminate;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value);
        }


        // Indicates if the onboarding process is currently running
        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        // CanExecute methods for commands
        public bool CanExecuteBeginOnboard() => !IsOnboarding && CanBeginOnboard;
        private bool CanExecuteTestPath() => !IsTesting && !IsOnboarding;

        // ── Constructor ────────────────────────────────────────────────
        public ConfigurationPageViewModel(
            IHelpersService helpers,
            IOnboardService onboardService,
            ILogService logService,
            IDomainCredentialsDialogService dialog,
            IDialogCoordinator dialogCoordinator,
            INavigationService nav)
        {
            _onboardSvc = onboardService;
            _log = logService;
            _dialog = dialog;
            _nav = nav;
            _dialogCoordinator = dialogCoordinator;
            _helpers = helpers;

            var baseDir = Directory.GetCurrentDirectory();
            var assets = Path.Combine(baseDir, "Assets");

        // ── Auto-seed Installer Path if found ────────────────────────────
        var seed = _helpers.FindAgentInstallerOnDesktop();
            if (!string.IsNullOrEmpty(seed))
            {
                InstallPath = seed;
                StatusText = $"Auto-detected installer: {Path.GetFileName(seed)}";
                CanBeginOnboard = true;
                _log.Log($"Auto-seeded InstallPath → {seed}", "INFO");
            }
        }

        // ── Bindable Properties ────────────────────────────────────────

        [ObservableProperty] private string domainName = "";
        [ObservableProperty] private string installPath = "";
        [ObservableProperty] private bool _isPathValid;
        [ObservableProperty] private bool canBeginOnboard;
        [ObservableProperty] private string? statusText;
        [ObservableProperty] private bool isTesting;
        [ObservableProperty] private bool _isOnboarding;
        [ObservableProperty] private bool _skipDomainAndAgent;

        // ── Commands ─────────────────────────────────────────────────
        #region Installer Button Commands

        // Test the installer path entered by the user

        [RelayCommand(CanExecute = nameof(CanExecuteTestPath))]
        private async Task TestPathAsync()
        {
            // 1) Disable “Test Path” and reset state
            IsTesting = true;
            TestPathCommand.NotifyCanExecuteChanged();
            IsProgressVisible = true;
            IsIndeterminate = true;
            StatusText = "Checking installer path…";
            SkipDomainAndAgent = false;
            _installerPath = null;
            CanBeginOnboard = false;
            BeginOnboardCommand.NotifyCanExecuteChanged();

            try
            {
                // 2) Try auto-seeding from Desktop
                var seededPath = _helpers.FindAgentInstallerOnDesktop();
                if (!string.IsNullOrEmpty(seededPath))
                {
                    _installerPath = seededPath;
                    InstallPath = seededPath; // Update UI binding
                    StatusText = $"Auto-detected installer: {Path.GetFileName(seededPath)}";
                    CanBeginOnboard = true;
                    _log.Log($"Auto-seeded InstallPath → {seededPath}", "INFO");
                    BeginOnboardCommand.NotifyCanExecuteChanged();
                    return;
                }

                // 3) Fallback: Validate manually-entered path
                var validPath = await _helpers.ValidateInstallerPathAsync(
                    InstallPath ?? string.Empty,
                    msg => _log.Log(msg, "INFO"),
                    status => StatusText = status);

                if (validPath != null)
                {
                    _installerPath = validPath;
                    StatusText = $"Found installer: {Path.GetFileName(validPath)}";
                    CanBeginOnboard = true;
                    _log.Log($"Test succeeded: {validPath}", "INFO");
                    BeginOnboardCommand.NotifyCanExecuteChanged();
                }
                else
                {
                    _log.Log("Test failed or cancelled", "WARNING");

                    // 4) Prompt to skip domain + agent
                    var result = await _dialogCoordinator.ShowMessageAsync(
                        "MainHost",
                        "No Installer Found",
                        "Continue without domain join and agent install?",
                        MessageDialogStyle.AffirmativeAndNegative,
                        new MetroDialogSettings
                        {
                            AffirmativeButtonText = "Yes, continue",
                            NegativeButtonText = "No, let me fix path"
                        });

                    if (result == MessageDialogResult.Affirmative)
                    {
                        SkipDomainAndAgent = true;
                        CanBeginOnboard = true;
                        StatusText = "Will skip domain join & agent install.";
                        _log.Log(StatusText, "WARNING");
                        BeginOnboardCommand.NotifyCanExecuteChanged();
                    }
                    else
                    {
                        StatusText = "Path test canceled, please enter a valid installer.";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Unexpected error: {ex.Message}";
                _log.Log(StatusText, "ERROR");
            }
            finally
            {
                // 5) Re-enable “Test Path” button
                IsTesting = false;
                TestPathCommand.NotifyCanExecuteChanged();
            }
        }

        // Begin the onboarding process
        [RelayCommand(CanExecute = nameof(CanExecuteBeginOnboard))]
        private async Task BeginOnboardAsync()
        {
            IsOnboarding = true;
            StatusText = "Starting onboarding…";
            IsIndeterminate = false;
            ProgressValue = 0;
            _log.Log("Onboarding initiated", "INFO");
            bool hadErrors = false;

            try
            {
                if (SkipDomainAndAgent)
                {
                    _log.Log("Skipping domain join & agent install (per user choice).", "WARNING");
                }
                else
                {
                    // ① Domain Join
                    var joinOk = await _helpers.TryJoinDomainAsync(
                        DomainName,
                        server => _dialog.ShowAsync(server),
                        msg => _log.Log(msg, "INFO"),
                        status => StatusText = status,
                        DomainJoinMaxRetries);
                    ProgressValue = 5;

                    if (!joinOk)
                    {
                        await _dialogCoordinator.ShowMessageAsync(
                            "MainHost",
                            "Domain Join Error",
                            $"Could not join {DomainName} after {DomainJoinMaxRetries} attempts.",
                            MessageDialogStyle.Affirmative,
                            new MetroDialogSettings { AffirmativeButtonText = "OK" });

                        _log.Log($"Domain join permanently failed for {DomainName}", "ERROR");
                        return;
                    }

                    await Task.Yield(); // let the UI catch up

                    // ② Auto-validate installer if TestPathAsync was never run
                    if (string.IsNullOrEmpty(_installerPath))
                    {
                        StatusText = "Validating installer path…";

                        // Call the new local-only validator (no creds)
                        var validPath = await _helpers.ValidateInstallerPathAsync(
                            InstallPath ?? "",
                            msg => _log.Log(msg, "INFO"),
                            status => StatusText = status);
                        ProgressValue = 10;

                        if (!string.IsNullOrEmpty(validPath))
                        {
                            _installerPath = validPath;
                            _log.Log($"Validated installer path: {_installerPath}", "INFO");
                        }
                        else
                        {
                            // final “skip or fix” prompt
                            var result = await _dialogCoordinator.ShowMessageAsync(
                                "MainHost",
                                "Installer Not Found",
                                "Could not locate a valid MSI at the seeded path. Skip agent install?",
                                MessageDialogStyle.AffirmativeAndNegative,
                                new MetroDialogSettings
                                {
                                    AffirmativeButtonText = "Yes, skip",
                                    NegativeButtonText = "No, let me fix path"
                                });

                            if (result == MessageDialogResult.Affirmative)
                            {
                                SkipDomainAndAgent = true;
                                _log.Log("User chose to skip agent install.", "WARNING");
                            }
                            else
                            {
                                StatusText = "Agent install canceled; please correct the path.";
                                return;
                            }
                        }
                    }

                    // ③ Agent Install
                    if (!SkipDomainAndAgent && !string.IsNullOrEmpty(_installerPath))
                    {
                        StatusText = "Installing agent…";
                        _log.Log("Beginning agent installation", "INFO");
                        await _onboardSvc.InstallAgentAsync(_installerPath!);
                        ProgressValue = 15;
                    }
                    else
                    {
                        _log.Log("No installer path; skipping agent install", "WARNING");
                        ProgressValue = 15;
                    }
                }

                await Task.Yield();

                // ③ Chrome
                StatusText = "Installing Chrome…";
                ProgressValue = 20;
                _log.Log("Installing Chrome", "INFO");
                await _onboardSvc.InstallChromeAsync();

                await Task.Yield();

                // ④ Adobe Reader
                StatusText = "Installing Adobe Reader…";
                ProgressValue = 60;
                _log.Log("Installing Adobe Reader", "INFO");
                await _onboardSvc.InstallAdobeReaderAsync();

                await Task.Yield();
            }
            catch (Exception ex)
            {
                hadErrors = true;
                _log.Log($"Unhandled error: {ex.Message}", "ERROR");
                ProgressValue = 90;
            }
            finally
            {
                if (hadErrors)
                {
                    StatusText = "Onboarding completed with errors.";
                    _log.Log("One or more steps failed. Review log.", "ERROR");
                    ProgressValue = 100;
                    IsIndeterminate = true;
                }
                else
                {
                    StatusText = "Configuration complete!";
                    _log.Log("Proceed to apply Baseline Tab.", "SUMMARY");
                    ProgressValue = 100;
                    IsIndeterminate = true;
                }

                IsOnboarding = false;
            }
        }
        #endregion Installer Button Commands
    }
}
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.Controls.Dialogs;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.UI.ViewModels.Pages;
using WS_Setup_6.UI.Windows;
using WS_Setup_6.UI.Windows.Pages;

namespace WS_Setup_6.UI.ViewModels
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindowModel : ObservableObject
    {
        private bool _isApplyingBaseline;
        public bool IsApplyingBaseline
        {
            get => _isApplyingBaseline;
            set => SetProperty(ref _isApplyingBaseline, value);
        }
        public string? InstallPath { get; set; }
        private bool _isBusy;
        private readonly INavigationService _nav;

        // 1) Default value in the backing field does NOT fire OnSelectedPageChanged
        [ObservableProperty]
        private string _selectedPage = "HomePage";

        // 2) This is what the ContentControl binds to
        [ObservableProperty]
        private object _currentView = default!;

        // 3) Constructor receives the NavService from DI
        public MainWindowModel(INavigationService nav)
        {
            _nav = nav;

            // 4) Whenever NavService swaps the view, mirror it straight into CurrentView
            _nav.CurrentPageChanged += () =>
                {
                    // always safe because NavService only fires after NavigateTo(...)
                    CurrentView = _nav.CurrentPageView!;
                };

            // Pre-populate the installer path to Desktop\NinjaOne-Agent*.msi
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var msi = Directory
                .EnumerateFiles(desktop, "NinjaOne-Agent*.msi")
                .FirstOrDefault();
            InstallPath = msi;
        }

        // 5) Fired only when SelectedPage actually changes (i.e. user clicks a tab)
        partial void OnSelectedPageChanged(string? oldValue, string newValue)
        {
            if (string.IsNullOrWhiteSpace(newValue))
                return;   // guard against stray empty values

            _nav.NavigateTo(newValue);
        }

        // 6) Graysout the Exit button when busy
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                SetProperty(ref _isBusy, value);
                ExitCommand.NotifyCanExecuteChanged();
            }
        }

        // 7) Turns Exit button on if not busy
        private bool CanExit() => !IsBusy;

        // 8) An Exit command if still need it
        [RelayCommand(CanExecute = nameof(CanExit))]
        private Task ExitAsync()
        {
            var dlg = new Windows.RebootDialog
            {
                Owner = Application.Current.MainWindow
            };
            var reboot = dlg.ShowDialog() == true;

            if (reboot)
            {
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }

            Application.Current.Shutdown();
            return Task.CompletedTask;

        }
    }
}
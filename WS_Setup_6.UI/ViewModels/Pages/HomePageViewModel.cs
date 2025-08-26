using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Runtime.Versioning;
using WS_Setup_6.UI.ViewModels;
using WS_Setup_6.UI.Behaviors;

namespace WS_Setup_6.UI.ViewModels.Pages
{
    [SupportedOSPlatform("windows")]
    public partial class HomePageViewModel : ObservableObject
    {
        private readonly MainWindowModel _mainVm;

        public HomePageViewModel(MainWindowModel mainVm)
        {
            _mainVm = mainVm;
            WelcomeMessage = "Workstation Onboarding Tool";
            Instruction = "Version: 6.7  |  Build Date: 2025.08.26";
        }

        [ObservableProperty]
        private string _welcomeMessage;

        [ObservableProperty]
        private string _instruction;

        public IRelayCommand StartConfigurationCommand
            => new RelayCommand(() => _mainVm.SelectedPage = "ConfigurationPage");
    }
}
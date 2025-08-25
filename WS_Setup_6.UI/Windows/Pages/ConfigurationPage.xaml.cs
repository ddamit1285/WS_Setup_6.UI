using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using WS_Setup_6.UI.ViewModels.Pages;

namespace WS_Setup_6.UI.Windows.Pages
{
    [SupportedOSPlatform("windows")]
    public partial class ConfigurationPage : UserControl
    {
        public ConfigurationPage(ConfigurationPageViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            vm.PropertyChanged += Vm_PropertyChanged;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Only care about progress updates
            if (e.PropertyName != nameof(ConfigurationPageViewModel.ProgressValue))
                return;

            var vm = (ConfigurationPageViewModel?)sender;
            if (vm == null)
                return;

            // Decide which VisualState to go to
            string state;
            if (vm.ProgressValue <= 0)
                state = "Idle";
            else if (vm.ProgressValue >= 100 && !vm.IsIndeterminate)
                state = "Completed";
            else
                state = "Running";

            // This line drives the storyboard you declared in XAML
            VisualStateManager.GoToState(ProgressArea, state, true);

        }
    }
}
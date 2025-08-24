using MahApps.Metro.Controls;
using System.Runtime.Versioning;
using WS_Setup_6.Core.Interfaces;
using WS_Setup_6.UI.ViewModels;

namespace WS_Setup_6.UI.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : MetroWindow
    {
        public MainWindow(MainWindowModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using MahApps.Metro.Controls;
using System.Runtime.Versioning;
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
            var logoPath = Path.Combine(
                Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!,
                "Assets",
                "AdvTechLogo.png"
            );
            LogoImage.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
        }
    }
}
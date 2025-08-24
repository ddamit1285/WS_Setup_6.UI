using System.Collections.Specialized;
using System.Runtime.Versioning;
using System.Windows.Controls;
using WS_Setup_6.UI.ViewModels;
using WS_Setup_6.UI.ViewModels.Pages;
using WS_Setup_6.UI.Behaviors;

namespace WS_Setup_6.UI.Windows.Pages
{
    [SupportedOSPlatform("windows")]
    public partial class LogPage : UserControl
    {
        public LogPage(LogViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
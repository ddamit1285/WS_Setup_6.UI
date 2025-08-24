using System.Runtime.Versioning;
using System.Windows.Controls;
using WS_Setup_6.UI.ViewModels.Pages;

namespace WS_Setup_6.UI.Windows.Pages
{
    [SupportedOSPlatform("windows")]
    public partial class BaselinePage : UserControl
    {
        public BaselinePage(BaselinePageViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}

using System.Windows.Controls;
using WS_Setup_6.UI.ViewModels;
using WS_Setup_6.UI.ViewModels.Pages;

namespace WS_Setup_6.UI.Windows.Pages
{
    public partial class HomePage : UserControl
    {
        public HomePage(HomePageViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
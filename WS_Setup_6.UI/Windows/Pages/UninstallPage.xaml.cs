using System.Windows.Controls;
using WS_Setup_6.UI.ViewModels;
using WS_Setup_6.UI.ViewModels.Pages;

namespace WS_Setup_6.UI.Windows.Pages
{
    public partial class UninstallPage : UserControl
    {
        public UninstallPage(UninstallViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
        {

        }
    }
}

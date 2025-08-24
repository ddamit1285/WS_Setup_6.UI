using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.Versioning;

namespace WS_Setup_6.UI.Windows
{
    [SupportedOSPlatform("windows")]
    public partial class RebootDialog : Window
    {
        public RebootDialog()
        {
            InitializeComponent();
        }

        private void Reboot_Click(object sender, RoutedEventArgs e)
        {
            // Set DialogResult = true to signal “Reboot”
            DialogResult = true;
        }
    
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using ASG.EAT.Plugin.ViewModels;

namespace ASG.EAT.Plugin.Views
{
    public partial class EATOptionsView : UserControl
    {
        public EATOptionsView()
        {
            InitializeComponent();
            DataContext = new EATOptionsViewModel();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Open hyperlinks in default browser
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}

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

            // Use shared ViewModel instance to ensure connection state is synchronized
            // between settings panel and dockable window
            if (Dispatcher.CheckAccess())
            {
                DataContext = ViewModelManager.Instance.OptionsViewModel;
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    DataContext = ViewModelManager.Instance.OptionsViewModel;
                });
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Open hyperlinks in default browser
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}

using System.Windows.Controls;
using ASG.EAT.Plugin.ViewModels;

namespace ASG.EAT.Plugin.Views
{
    public partial class EATOptionsView : UserControl
    {
        public EATOptionsView()
        {
            InitializeComponent();
            var settings = EATSettings.Load();
            settings.RefreshPorts();
            DataContext = settings;
        }
    }
}

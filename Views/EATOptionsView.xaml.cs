using System.Windows.Controls;
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
    }
}

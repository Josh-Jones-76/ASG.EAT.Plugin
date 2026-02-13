using System.ComponentModel.Composition;
using System.Windows;

namespace ASG.EAT.Plugin.Views
{
    [Export(typeof(ResourceDictionary))]
    public partial class EATOptionsTemplate : ResourceDictionary
    {
        public EATOptionsTemplate()
        {
            InitializeComponent();
        }
    }
}

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace ASG.EAT.Plugin.Views
{
    public partial class EATControlPanelView : UserControl
    {
        public EATControlPanelView()
        {
            // Ensure initialization happens on UI thread to prevent threading errors
            if (Dispatcher.CheckAccess())
            {
                InitializeControl();
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    InitializeControl();
                });
            }
        }

        private void InitializeControl()
        {
            InitializeComponent();

            // Auto-scroll activity log to bottom
            Loaded += (s, e) =>
            {
                if (ActivityLogListBox?.Items != null)
                {
                    ((INotifyCollectionChanged)ActivityLogListBox.Items).CollectionChanged += (s2, e2) =>
                    {
                        if (ActivityLogListBox.Items.Count > 0)
                        {
                            ActivityLogListBox.ScrollIntoView(
                                ActivityLogListBox.Items[ActivityLogListBox.Items.Count - 1]);
                        }
                    };
                }
            };
        }
    }
}

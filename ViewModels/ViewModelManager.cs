using System;

namespace ASG.EAT.Plugin.ViewModels
{
    /// <summary>
    /// Singleton manager that provides a shared EATOptionsViewModel instance.
    /// This ensures settings state is synchronized when connecting from either
    /// the dockable window or the settings panel.
    /// </summary>
    public class ViewModelManager
    {
        private static ViewModelManager _instance;
        private static readonly object _lock = new object();

        private readonly EATOptionsViewModel _optionsViewModel;

        private ViewModelManager()
        {
            _optionsViewModel = new EATOptionsViewModel();
        }

        public static ViewModelManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ViewModelManager();
                        }
                    }
                }
                return _instance;
            }
        }

        public EATOptionsViewModel OptionsViewModel => _optionsViewModel;
    }
}

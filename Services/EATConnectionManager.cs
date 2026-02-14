using System;

namespace ASG.EAT.Plugin.Services
{
    /// <summary>
    /// Singleton connection manager that provides a shared serial service instance
    /// between the dockable window and the settings panel.
    /// This ensures connection state is synchronized across both UI components.
    /// </summary>
    public class EATConnectionManager
    {
        private static EATConnectionManager _instance;
        private static readonly object _lock = new object();

        private readonly EATSerialService _serial;

        private EATConnectionManager()
        {
            _serial = new EATSerialService();
        }

        public static EATConnectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new EATConnectionManager();
                        }
                    }
                }
                return _instance;
            }
        }

        public EATSerialService SerialService => _serial;
    }
}

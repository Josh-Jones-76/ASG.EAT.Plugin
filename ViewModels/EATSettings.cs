using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ASG.EAT.Plugin.ViewModels
{
    public class EATSettings : INotifyPropertyChanged
    {
        // ── Serial Connection ──────────────────────────────────────────

        private string _selectedPort = string.Empty;
        public string SelectedPort
        {
            get => _selectedPort;
            set { _selectedPort = value; OnPropertyChanged(); }
        }

        private int _baudRate = 9600;
        public int BaudRate
        {
            get => _baudRate;
            set { _baudRate = value; OnPropertyChanged(); }
        }

        // ── Device Preferences ─────────────────────────────────────────

        private bool _autoConnectOnStartup = false;
        public bool AutoConnectOnStartup
        {
            get => _autoConnectOnStartup;
            set { _autoConnectOnStartup = value; OnPropertyChanged(); }
        }

        private int _commandTimeoutMs = 3000;
        public int CommandTimeoutMs
        {
            get => _commandTimeoutMs;
            set { _commandTimeoutMs = Math.Max(500, Math.Min(30000, value)); OnPropertyChanged(); }
        }

        private int _defaultSpeed = 100;
        public int DefaultSpeed
        {
            get => _defaultSpeed;
            set { _defaultSpeed = Math.Max(1, Math.Min(2000, value)); OnPropertyChanged(); }
        }

        private int _defaultStepSize = 25;
        public int DefaultStepSize
        {
            get => _defaultStepSize;
            set { _defaultStepSize = Math.Max(1, Math.Min(10000, value)); OnPropertyChanged(); }
        }

        private bool _logSerialTraffic = false;
        public bool LogSerialTraffic
        {
            get => _logSerialTraffic;
            set { _logSerialTraffic = value; OnPropertyChanged(); }
        }

        // ── Motor Configuration (Arduino commands: cA, cB, cC) ─────────

        private int _motorSpeed = 100;
        public int MotorSpeed
        {
            get => _motorSpeed;
            set { _motorSpeed = Math.Max(1, Math.Min(2000, value)); OnPropertyChanged(); }
        }

        private int _motorMaxSpeed = 500;
        public int MotorMaxSpeed
        {
            get => _motorMaxSpeed;
            set { _motorMaxSpeed = Math.Max(1, Math.Min(2000, value)); OnPropertyChanged(); }
        }

        private int _motorAcceleration = 100;
        public int MotorAcceleration
        {
            get => _motorAcceleration;
            set { _motorAcceleration = Math.Max(1, Math.Min(2000, value)); OnPropertyChanged(); }
        }

        // ── Available Ports (for UI combo box) ─────────────────────────

        private ObservableCollection<string> _availablePorts = new ObservableCollection<string>();

        [JsonIgnore]
        public ObservableCollection<string> AvailablePorts
        {
            get => _availablePorts;
            set { _availablePorts = value; OnPropertyChanged(); }
        }

        // ── Baud Rate Options (for UI combo box) ───────────────────────

        [JsonIgnore]
        public int[] AvailableBaudRates => new int[] { 9600, 19200, 38400, 57600, 115200 };

        // ── Port Refresh ───────────────────────────────────────────────

        public void RefreshPorts()
        {
            var currentSelection = SelectedPort;
            AvailablePorts.Clear();

            foreach (var port in SerialPort.GetPortNames())
            {
                AvailablePorts.Add(port);
            }

            // Restore previous selection if still available
            if (!string.IsNullOrEmpty(currentSelection) && AvailablePorts.Contains(currentSelection))
            {
                SelectedPort = currentSelection;
            }
            else if (AvailablePorts.Count > 0)
            {
                SelectedPort = AvailablePorts[0];
            }
        }

        // ── Serialization helpers ──────────────────────────────────────

        private static readonly string SettingsFilePath =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "Plugins", "ASG.EAT.Plugin", "settings.json");

        public void Save()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(SettingsFilePath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                System.IO.File.WriteAllText(SettingsFilePath, json);
            }
            catch { /* fail silently – non-critical */ }
        }

        public static EATSettings Load()
        {
            EATSettings settings;
            try
            {
                if (System.IO.File.Exists(SettingsFilePath))
                {
                    var json = System.IO.File.ReadAllText(SettingsFilePath);
                    settings = JsonConvert.DeserializeObject<EATSettings>(json) ?? new EATSettings();
                }
                else
                {
                    settings = new EATSettings();
                }
            }
            catch
            {
                settings = new EATSettings();
            }

            settings.RefreshPorts();
            return settings;
        }

        // ── INotifyPropertyChanged ─────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

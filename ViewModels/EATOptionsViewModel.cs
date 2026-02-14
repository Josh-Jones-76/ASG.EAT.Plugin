using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ASG.EAT.Plugin.Services;

namespace ASG.EAT.Plugin.ViewModels
{
    public class EATOptionsViewModel : INotifyPropertyChanged
    {
        private readonly EATSettings _settings;
        private readonly EATSerialService _serial;

        public EATOptionsViewModel()
        {
            _settings = EATSettings.Load();
            _serial = EATConnectionManager.Instance.SerialService;

            // Wire up serial events
            _serial.ConnectionStateChanged += (s, connected) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsConnected = connected;
                });
            };

            _serial.ErrorOccurred += (s, msg) =>
            {
                StatusMessage = $"⚠ {msg}";
            };

            // Connection commands
            ConnectCommand = new RelayCommand(_ => DoConnect(), _ => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
            DisconnectCommand = new RelayCommand(_ => DoDisconnect(), _ => IsConnected);
            RefreshPortsCommand = new RelayCommand(_ => DoRefreshPorts());

            // Commands for sending motor configuration to Arduino
            SaveSpeedCommand = new RelayCommand(_ => SendMotorConfig("cA", MotorSpeed), _ => true);
            SaveMaxSpeedCommand = new RelayCommand(_ => SendMotorConfig("cB", MotorMaxSpeed), _ => true);
            SaveAccelerationCommand = new RelayCommand(_ => SendMotorConfig("cC", MotorAcceleration), _ => true);

            DoRefreshPorts();
        }

        // ── Settings Properties (delegate to _settings) ────────────────

        public string SelectedPort
        {
            get => _settings.SelectedPort;
            set { _settings.SelectedPort = value; OnPropertyChanged(); _settings.Save(); }
        }

        public int BaudRate
        {
            get => _settings.BaudRate;
            set { _settings.BaudRate = value; OnPropertyChanged(); _settings.Save(); }
        }

        public bool AutoConnectOnStartup
        {
            get => _settings.AutoConnectOnStartup;
            set { _settings.AutoConnectOnStartup = value; OnPropertyChanged(); _settings.Save(); }
        }

        public int CommandTimeoutMs
        {
            get => _settings.CommandTimeoutMs;
            set { _settings.CommandTimeoutMs = value; OnPropertyChanged(); _settings.Save(); }
        }

        public int DefaultSpeed
        {
            get => _settings.DefaultSpeed;
            set { _settings.DefaultSpeed = value; OnPropertyChanged(); _settings.Save(); }
        }

        public int DefaultStepSize
        {
            get => _settings.DefaultStepSize;
            set { _settings.DefaultStepSize = value; OnPropertyChanged(); _settings.Save(); }
        }

        public bool LogSerialTraffic
        {
            get => _settings.LogSerialTraffic;
            set { _settings.LogSerialTraffic = value; OnPropertyChanged(); _settings.Save(); }
        }

        public int MotorSpeed
        {
            get => _settings.MotorSpeed;
            set { _settings.MotorSpeed = value; OnPropertyChanged(); _settings.Save(); }
        }

        public int MotorMaxSpeed
        {
            get => _settings.MotorMaxSpeed;
            set { _settings.MotorMaxSpeed = value; OnPropertyChanged(); _settings.Save(); }
        }

        public int MotorAcceleration
        {
            get => _settings.MotorAcceleration;
            set { _settings.MotorAcceleration = value; OnPropertyChanged(); _settings.Save(); }
        }

        public System.Collections.ObjectModel.ObservableCollection<string> AvailablePorts
        {
            get => _settings.AvailablePorts;
        }

        public int[] AvailableBaudRates => _settings.AvailableBaudRates;

        // ── Connection State ────────────────────────────────────────────

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConnectionStatus));
            }
        }

        public string ConnectionStatus => IsConnected
            ? $"Connected — {_serial.ConnectedPort} @ {_serial.ConnectedBaud}"
            : "Disconnected";

        // ── Commands ────────────────────────────────────────────────────

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshPortsCommand { get; }
        public ICommand SaveSpeedCommand { get; }
        public ICommand SaveMaxSpeedCommand { get; }
        public ICommand SaveAccelerationCommand { get; }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // ── Connection Management ───────────────────────────────────────

        private async void DoConnect()
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                StatusMessage = "⚠ No port selected";
                return;
            }

            StatusMessage = $"Connecting to {SelectedPort} @ {BaudRate}...";
            bool ok = _serial.Connect(SelectedPort, BaudRate);

            if (ok)
            {
                StatusMessage = $"✓ Connected to {SelectedPort}";

                // Request current motor configuration from Arduino
                await LoadMotorConfigurationFromArduino();

                ClearStatusAfterDelay(3000);
            }
            else
            {
                StatusMessage = "⚠ Connection failed. Check port and device.";
            }
        }

        private void DoDisconnect()
        {
            _serial.Disconnect();
            StatusMessage = "Disconnected";
            ClearStatusAfterDelay(2000);
        }

        private void DoRefreshPorts()
        {
            _settings.RefreshPorts();
            OnPropertyChanged(nameof(AvailablePorts));
        }

        // ── Send motor config to Arduino ────────────────────────────────

        private async void SendMotorConfig(string command, int value)
        {
            // Check if we have a saved port to connect to
            if (string.IsNullOrEmpty(_settings.SelectedPort))
            {
                StatusMessage = "⚠ No COM port selected";
                return;
            }

            try
            {
                // Temporarily connect if not already connected
                bool wasConnected = _serial.IsConnected;
                if (!wasConnected)
                {
                    StatusMessage = $"Connecting to {_settings.SelectedPort}...";
                    bool connected = _serial.Connect(_settings.SelectedPort, _settings.BaudRate);
                    if (!connected)
                    {
                        StatusMessage = "⚠ Failed to connect to device";
                        return;
                    }
                }

                // Send command (e.g., "cA,100")
                string cmd = $"{command},{value}";
                StatusMessage = $"Sending {cmd}...";
                var response = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);

                string cmdName = command switch
                {
                    "cA" => "Speed",
                    "cB" => "MaxSpeed",
                    "cC" => "Acceleration",
                    _ => "Config"
                };

                StatusMessage = $"✓ {cmdName} set to {value}";

                // Disconnect if we connected temporarily
                if (!wasConnected)
                {
                    _serial.Disconnect();
                }

                // Clear status after 3 seconds
                await System.Threading.Tasks.Task.Delay(3000);
                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ Error: {ex.Message}";
            }
        }

        private async void ClearStatusAfterDelay(int milliseconds)
        {
            await System.Threading.Tasks.Task.Delay(milliseconds);
            StatusMessage = string.Empty;
        }

        // ── Load Motor Configuration from Arduino ───────────────────────

        private async System.Threading.Tasks.Task LoadMotorConfigurationFromArduino()
        {
            try
            {
                // Send 'ep' command to get EEPROM values
                var responseLines = await _serial.SendCommandAsync("ep", _settings.CommandTimeoutMs);

                // Parse the response
                // Expected format:
                // ***Current EEPROM***
                // TL: 550
                // TR: 550
                // BL: 550
                // BR: 550
                // Speed: 100
                // MaxSpeed: 100
                // Acceleration: 300
                // Orientation: 1
                // ***End Current EEPROM***

                bool inEEPROMBlock = false;
                foreach (var line in responseLines)
                {
                    if (line.Contains("***Current EEPROM***"))
                    {
                        inEEPROMBlock = true;
                        continue;
                    }

                    if (line.Contains("***End Current EEPROM***"))
                    {
                        inEEPROMBlock = false;
                        break;
                    }

                    if (inEEPROMBlock && line.Contains(":"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string valueStr = parts[1].Trim();

                            if (int.TryParse(valueStr, out int value))
                            {
                                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                {
                                    switch (key)
                                    {
                                        case "Speed":
                                            MotorSpeed = value;
                                            break;
                                        case "MaxSpeed":
                                            MotorMaxSpeed = value;
                                            break;
                                        case "Acceleration":
                                            MotorAcceleration = value;
                                            break;
                                    }
                                });
                            }
                        }
                    }
                }

                StatusMessage = "✓ Motor configuration loaded";
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ Failed to load motor config: {ex.Message}";
            }
        }

        // ── INotifyPropertyChanged ─────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // ── RelayCommand helper ─────────────────────────────────────────

        private class RelayCommand : ICommand
        {
            private readonly Action<object> _execute;
            private readonly Predicate<object> _canExecute;

            public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object parameter) => _execute(parameter);

            public event EventHandler CanExecuteChanged
            {
                add { System.Windows.Input.CommandManager.RequerySuggested += value; }
                remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
            }
        }
    }
}

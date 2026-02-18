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
            _serial.ConnectionStateChanged += async (s, connected) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsConnected = connected;
                });

                if (connected)
                {
                    // Load defaults from device when connected (from either panel)
                    await OnConnected();
                }
                else
                {
                    // Clear values when disconnected (from either panel)
                    OnDisconnected();
                }
            };

            _serial.ErrorOccurred += (s, msg) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"⚠ {msg}";
                });
            };

            // Connection commands
            ConnectCommand = new RelayCommand(_ => DoConnect(), _ => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
            DisconnectCommand = new RelayCommand(_ => DoDisconnect(), _ => IsConnected);
            RefreshPortsCommand = new RelayCommand(_ => DoRefreshPorts());

            // Commands for sending motor configuration to Arduino
            SaveSpeedCommand = new RelayCommand(_ => SendMotorConfig("cA", MotorSpeed ?? 0), _ => MotorSpeed.HasValue);
            SaveMaxSpeedCommand = new RelayCommand(_ => SendMotorConfig("cB", MotorMaxSpeed ?? 0), _ => MotorMaxSpeed.HasValue);
            SaveAccelerationCommand = new RelayCommand(_ => SendMotorConfig("cC", MotorAcceleration ?? 0), _ => MotorAcceleration.HasValue);

            // Commands for setting motor positions directly (m1-m4)
            SaveM1PositionCommand = new RelayCommand(_ => SendSetMotorPosition("m1", SetPositionM1), _ => IsConnected);
            SaveM2PositionCommand = new RelayCommand(_ => SendSetMotorPosition("m2", SetPositionM2), _ => IsConnected);
            SaveM3PositionCommand = new RelayCommand(_ => SendSetMotorPosition("m3", SetPositionM3), _ => IsConnected);
            SaveM4PositionCommand = new RelayCommand(_ => SendSetMotorPosition("m4", SetPositionM4), _ => IsConnected);

            // Firmware check command
            CheckFirmwareCommand = new RelayCommand(_ => DoCheckFirmware(), _ => IsConnected);

            // Defer port refresh to UI thread - use BeginInvoke to avoid blocking if called from background thread
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                if (dispatcher.CheckAccess())
                {
                    DoRefreshPorts();
                }
                else
                {
                    dispatcher.BeginInvoke(new System.Action(() => DoRefreshPorts()));
                }
            }

            // If already connected (e.g., connected from dockable panel before opening settings),
            // load the current values from the device
            if (_serial.IsConnected)
            {
                IsConnected = true;
                System.Threading.Tasks.Task.Run(async () => await OnConnected());
            }
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

        public int DefaultStepSize
        {
            get => _settings.DefaultStepSize;
            set
            {
                // Validate range
                if (value < 1 || value > 50)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        System.Windows.MessageBox.Show(
                            $"Default Step Size must be between 1 and 50.\nValue {value} is out of range.",
                            "Invalid Value",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                        StatusMessage = $"⚠ Default Step Size must be 1-50";
                    });
                    ClearStatusAfterDelay(3000);
                    // Don't save invalid value, reset to current valid value
                    OnPropertyChanged();
                    return;
                }
                _settings.DefaultStepSize = value;
                OnPropertyChanged();
                _settings.Save();
            }
        }

        public double MotorStepSizeMicrons
        {
            get => _settings.MotorStepSizeMicrons;
            set
            {
                _settings.MotorStepSizeMicrons = value;
                OnPropertyChanged();
                _settings.Save();
            }
        }

        public bool LogSerialTraffic
        {
            get => _settings.LogSerialTraffic;
            set { _settings.LogSerialTraffic = value; OnPropertyChanged(); _settings.Save(); }
        }

        public bool ShowRawCommand
        {
            get => _settings.ShowRawCommand;
            set { _settings.ShowRawCommand = value; OnPropertyChanged(); _settings.Save(); }
        }

        public bool ShowActivityLog
        {
            get => _settings.ShowActivityLog;
            set { _settings.ShowActivityLog = value; OnPropertyChanged(); _settings.Save(); }
        }

        // Sensor Color for Tilt Control
        public string SensorColor
        {
            get => _settings.SensorColor;
            set
            {
                _settings.SensorColor = value;
                OnPropertyChanged();
                _settings.Save();
                // Notify listeners that sensor color changed
                SensorColorChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Available sensor colors
        public System.Collections.ObjectModel.ObservableCollection<string> AvailableSensorColors { get; } =
            new System.Collections.ObjectModel.ObservableCollection<string>
            {
                "#2A2A2A", // Dark Gray (Default)
                "#1F1F1F", // Almost Black
                "#444444", // Medium Gray
                "#1E3A5F", // Dark Blue
                "#2D4A2B", // Dark Green
                "#4A2B2B", // Dark Red
                "#3A2F4A", // Dark Purple
                "#4A3B2B", // Dark Brown
                "#3366CC", // Bright Blue
                "#4CAF50", // Bright Green
                "#F44336", // Bright Red
                "#FF9800", // Bright Orange
                "#9C27B0", // Bright Purple
                "#00BCD4", // Bright Cyan
                "#FFEB3B", // Bright Yellow
                "#E91E63", // Bright Pink
            };

        // Motor configuration - only show values when connected
        private int? _motorSpeed = null;
        public int? MotorSpeed
        {
            get => _motorSpeed;
            set
            {
                _motorSpeed = value;
                OnPropertyChanged();
                if (value.HasValue)
                {
                    _settings.MotorSpeed = value.Value;
                    _settings.Save();
                }
            }
        }

        private int? _motorMaxSpeed = null;
        public int? MotorMaxSpeed
        {
            get => _motorMaxSpeed;
            set
            {
                _motorMaxSpeed = value;
                OnPropertyChanged();
                if (value.HasValue)
                {
                    _settings.MotorMaxSpeed = value.Value;
                    _settings.Save();
                }
            }
        }

        private int? _motorAcceleration = null;
        public int? MotorAcceleration
        {
            get => _motorAcceleration;
            set
            {
                _motorAcceleration = value;
                OnPropertyChanged();
                if (value.HasValue)
                {
                    _settings.MotorAcceleration = value.Value;
                    _settings.Save();
                }
            }
        }

        // Event to notify when orientation changes
        public event EventHandler OrientationChanged;

        // Event to notify when sensor color changes
        public event EventHandler SensorColorChanged;

        // Event to notify when a motor position has been set (m1-m4) so dockable panel can refresh
        public event EventHandler PositionSetCompleted;

        // Orientation - only editable when connected, defaults to 1 when disconnected
        private int _orientation = 1;
        public int Orientation
        {
            get => _orientation;
            set
            {
                // Only allow changes when connected
                if (!IsConnected)
                    return;

                _orientation = value;
                OnPropertyChanged();
                _settings.Orientation = value;
                _settings.Save();
                OnPropertyChanged(nameof(InnerRotationAngle));
                // Send orientation command to Arduino when changed
                SendOrientationCommand(value);
                // Notify listeners that orientation changed
                OrientationChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Sets orientation from the Arduino device response without sending
        /// the value back to the Arduino. The device is the source of truth.
        /// </summary>
        public void SetOrientationFromDevice(int value)
        {
            value = Math.Max(1, Math.Min(4, value));
            _orientation = value;
            _settings.Orientation = value;
            _settings.Save();
            OnPropertyChanged(nameof(Orientation));
            OnPropertyChanged(nameof(InnerRotationAngle));
            OrientationChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Orientation visualization ───────────────────────────────────

        public double InnerRotationAngle => (_orientation - 1) * 90;

        // ── Physical Motor Positions ────────────────────────────────────

        private string _positionTL = "—";
        public string PositionTL
        {
            get => _positionTL;
            set
            {
                _positionTL = value;
                OnPropertyChanged();
                // Sync M2 input field with current position (M2 = TL)
                if (int.TryParse(value, out int pos))
                    SetPositionM2 = pos;
            }
        }

        private string _positionTR = "—";
        public string PositionTR
        {
            get => _positionTR;
            set
            {
                _positionTR = value;
                OnPropertyChanged();
                // Sync M1 input field with current position (M1 = TR)
                if (int.TryParse(value, out int pos))
                    SetPositionM1 = pos;
            }
        }

        private string _positionBL = "—";
        public string PositionBL
        {
            get => _positionBL;
            set
            {
                _positionBL = value;
                OnPropertyChanged();
                // Sync M4 input field with current position (M4 = BL)
                if (int.TryParse(value, out int pos))
                    SetPositionM4 = pos;
            }
        }

        private string _positionBR = "—";
        public string PositionBR
        {
            get => _positionBR;
            set
            {
                _positionBR = value;
                OnPropertyChanged();
                // Sync M3 input field with current position (M3 = BR)
                if (int.TryParse(value, out int pos))
                    SetPositionM3 = pos;
            }
        }

        // ── Set Motor Position Input Values ──────────────────────────────

        private int _setPositionM1 = 0;
        public int SetPositionM1
        {
            get => _setPositionM1;
            set { _setPositionM1 = value; OnPropertyChanged(); }
        }

        private int _setPositionM2 = 0;
        public int SetPositionM2
        {
            get => _setPositionM2;
            set { _setPositionM2 = value; OnPropertyChanged(); }
        }

        private int _setPositionM3 = 0;
        public int SetPositionM3
        {
            get => _setPositionM3;
            set { _setPositionM3 = value; OnPropertyChanged(); }
        }

        private int _setPositionM4 = 0;
        public int SetPositionM4
        {
            get => _setPositionM4;
            set { _setPositionM4 = value; OnPropertyChanged(); }
        }

        // ── Firmware Version ──────────────────────────────────────────────

        private string _firmwareVersion = "—";
        public string FirmwareVersion
        {
            get => _firmwareVersion;
            set { _firmwareVersion = value; OnPropertyChanged(); }
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
        public ICommand SaveM1PositionCommand { get; }
        public ICommand SaveM2PositionCommand { get; }
        public ICommand SaveM3PositionCommand { get; }
        public ICommand SaveM4PositionCommand { get; }
        public ICommand CheckFirmwareCommand { get; }

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
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = "⚠ No port selected";
                });
                return;
            }

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = $"Connecting to {SelectedPort} @ {BaudRate}...";
            });
            bool ok = _serial.Connect(SelectedPort, BaudRate);

            if (!ok)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = "⚠ Connection failed. Check port and device.";
                });
            }
            // OnConnected will be called via ConnectionStateChanged event
        }

        private void DoDisconnect()
        {
            _serial.Disconnect();
            // OnDisconnected will be called via ConnectionStateChanged event
        }

        // ── Connection Event Handlers ───────────────────────────────────────

        private async System.Threading.Tasks.Task OnConnected()
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = $"✓ Connected to {_serial.ConnectedPort}";
            });

            // Small delay to let Arduino settle
            await System.Threading.Tasks.Task.Delay(500);

            // Request current motor configuration from Arduino
            await LoadMotorConfigurationFromArduino();

            // Load orientation from Arduino (already handled in LoadMotorConfigurationFromArduino)
            // await LoadOrientationFromArduino();

            ClearStatusAfterDelay(3000);
        }

        private void OnDisconnected()
        {
            // Clear motor configuration values on disconnect
            MotorSpeed = null;
            MotorMaxSpeed = null;
            MotorAcceleration = null;

            // Reset orientation to default when disconnected
            _orientation = 1;
            OnPropertyChanged(nameof(Orientation));
            OnPropertyChanged(nameof(InnerRotationAngle));

            // Clear physical positions on disconnect
            PositionTL = "—";
            PositionTR = "—";
            PositionBL = "—";
            PositionBR = "—";

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = "Disconnected";
            });
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
            // Validate value ranges
            string cmdName = command switch
            {
                "cA" => "Speed",
                "cB" => "MaxSpeed",
                "cC" => "Acceleration",
                _ => "Config"
            };

            bool showWarning = false;
            string warningMessage = "";

            if (command == "cA" || command == "cB") // Speed or MaxSpeed
            {
                if (value < 50 || value > 250)
                {
                    showWarning = true;
                    warningMessage = $"⚠ {cmdName} value {value} is outside recommended range (50-250). Continue anyway?";
                }
            }
            else if (command == "cC") // Acceleration
            {
                if (value < 50 || value > 350)
                {
                    showWarning = true;
                    warningMessage = $"⚠ Acceleration value {value} is outside recommended range (50-350). Continue anyway?";
                }
            }

            // Show warning if needed
            if (showWarning)
            {
                var result = System.Windows.MessageBox.Show(
                    warningMessage,
                    "Value Out of Range",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        StatusMessage = "✗ Operation cancelled";
                    });
                    ClearStatusAfterDelay(2000);
                    return;
                }
            }

            // Check if we have a saved port to connect to
            if (string.IsNullOrEmpty(_settings.SelectedPort))
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = "⚠ No COM port selected";
                });
                return;
            }

            try
            {
                // Temporarily connect if not already connected
                bool wasConnected = _serial.IsConnected;
                if (!wasConnected)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        StatusMessage = $"Connecting to {_settings.SelectedPort}...";
                    });
                    bool connected = _serial.Connect(_settings.SelectedPort, _settings.BaudRate);
                    if (!connected)
                    {
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            StatusMessage = "⚠ Failed to connect to device";
                        });
                        return;
                    }
                }

                // Send command (e.g., "cA,100")
                string cmd = $"{command},{value}";
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"Sending {cmd}...";
                });
                var response = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"✓ {cmdName} set to {value}";
                });

                // Disconnect if we connected temporarily
                if (!wasConnected)
                {
                    _serial.Disconnect();
                }

                // Clear status after 3 seconds
                await System.Threading.Tasks.Task.Delay(3000);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = string.Empty;
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"⚠ Error: {ex.Message}";
                });
            }
        }

        private async void ClearStatusAfterDelay(int milliseconds)
        {
            await System.Threading.Tasks.Task.Delay(milliseconds);
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusMessage = string.Empty;
            });
        }

        // ── Load Motor Configuration from Arduino ───────────────────────

        private async System.Threading.Tasks.Task LoadMotorConfigurationFromArduino()
        {
            // The Arduino firmware's "ep" command uses C++ pointer arithmetic
            // ("string literal" + int) which produces unreliable serial output.
            // Instead, we load motor config from locally persisted settings
            // (which stay in sync because we save to settings whenever
            // the user sends cA/cB/cC/or commands).
            //
            // After restoring settings to the UI, we push the saved config
            // values to the Arduino (cA, cB, cC, or) so it matches our
            // persisted state — the Arduino resets its config on power-cycle.

            try
            {
                // 1. Restore motor configuration to the UI (NOT orientation —
                //    orientation is read from the Arduino via cp response,
                //    since another app may have changed it)
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    MotorSpeed = _settings.MotorSpeed;
                    MotorMaxSpeed = _settings.MotorMaxSpeed;
                    MotorAcceleration = _settings.MotorAcceleration;
                });

                // 2. Push saved motor config to the Arduino so it matches
                //    our persisted settings (Arduino loses config on power-cycle).
                //    Use fire-and-forget with a short timeout — if any fail,
                //    the user can still manually save from the Settings tab.
                //    NOTE: Orientation is NOT pushed — Arduino is source of truth.
                int shortTimeout = 3000;

                try { await _serial.SendCommandAsync($"cA,{_settings.MotorSpeed}", shortTimeout); }
                catch { /* non-critical */ }

                try { await _serial.SendCommandAsync($"cB,{_settings.MotorMaxSpeed}", shortTimeout); }
                catch { /* non-critical */ }

                try { await _serial.SendCommandAsync($"cC,{_settings.MotorAcceleration}", shortTimeout); }
                catch { /* non-critical */ }

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = "✓ Motor configuration loaded";
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"⚠ Note: {ex.Message} — using saved settings";
                });
            }
        }

        // ── Load Orientation from Arduino ───────────────────────────────

        private System.Threading.Tasks.Task LoadOrientationFromArduino()
        {
            // Orientation is already loaded as part of LoadMotorConfigurationFromArduino
            // This method exists for clarity but doesn't need to do additional work
            return System.Threading.Tasks.Task.CompletedTask;
        }

        // ── Send Orientation Command to Arduino ─────────────────────────

        private async void SendOrientationCommand(int orientation)
        {
            // Only send if connected
            if (!_serial.IsConnected)
                return;

            try
            {
                // Send orientation command (e.g., "or,1")
                string cmd = $"or,{orientation}";
                var response = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"✓ Orientation set to #{orientation}";
                });
                ClearStatusAfterDelay(2000);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"⚠ Failed to set orientation: {ex.Message}";
                });
            }
        }

        // ── Send Set Motor Position to Arduino ────────────────────────

        private async void SendSetMotorPosition(string command, int value)
        {
            if (!_serial.IsConnected)
                return;

            string motorName = command switch
            {
                "m1" => "M1 (TR)",
                "m2" => "M2 (TL)",
                "m3" => "M3 (BR)",
                "m4" => "M4 (BL)",
                _ => command
            };

            // Show caution dialog before setting motor position
            var result = System.Windows.MessageBox.Show(
                $"You are about to set {motorName} to position {value}.\n\n" +
                "Use Caution: Setting motor positions manually can lead to misleading " +
                "positioning and should only be done if you know what you are doing.\n\n" +
                "For example, if you have removed all tilt from your system and the sensor " +
                "is in a good position but motor values are vastly different, it can be a " +
                "good idea to reset them all consistently to that position.\n\n" +
                "Are you sure you want to continue?",
                "Set Motor Position - Caution",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                StatusMessage = "Set position cancelled.";
                ClearStatusAfterDelay(2000);
                return;
            }

            try
            {
                string cmd = $"{command},{value}";
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"Setting {motorName} position to {value}...";
                });
                var response = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"✓ {motorName} position set to {value}";
                    // Notify dockable panel to refresh positions
                    PositionSetCompleted?.Invoke(this, EventArgs.Empty);
                });
                ClearStatusAfterDelay(3000);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = $"⚠ Failed to set {motorName}: {ex.Message}";
                });
            }
        }

        // ── Firmware Check ────────────────────────────────────────────────

        private async void DoCheckFirmware()
        {
            if (!_serial.IsConnected)
                return;

            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = "Checking firmware...";
                    FirmwareVersion = "Checking...";
                });

                var response = await _serial.SendCommandAsync("fv", _settings.CommandTimeoutMs);

                string fwVersion = "Unknown";
                foreach (var line in response)
                {
                    if (line.StartsWith("FW:"))
                    {
                        fwVersion = line.Substring(3).Trim();
                        break;
                    }
                }

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    FirmwareVersion = fwVersion;
                    StatusMessage = $"✓ Firmware: {fwVersion}";
                });
                ClearStatusAfterDelay(3000);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    FirmwareVersion = "Error";
                    StatusMessage = $"⚠ Firmware check failed: {ex.Message}";
                });
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

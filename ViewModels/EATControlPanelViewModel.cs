using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ASG.EAT.Plugin.Services;

namespace ASG.EAT.Plugin.ViewModels
{
    /// <summary>
    /// ViewModel for the ASG EAT control panel docked inside the NINA Imaging tab.
    ///
    /// Provides:
    ///   - Connect / Disconnect to the serial device
    ///   - Directional tilt buttons (Top, Bottom, Left, Right) — moves 4 motors
    ///   - Corner tilt buttons (TL, TR, BL, BR) — moves 2 motors in opposition
    ///   - Backfocus control — moves all 4 motors in same direction
    ///   - Utility commands (Zero, Get Positions, Save EEPROM)
    ///   - A scrolling activity / response log
    ///
    /// Note: The primary ViewModel used by NINA is EATDockablePanel in EATPlugin.cs.
    /// This class is kept as an alternative standalone ViewModel.
    /// </summary>
    public class EATControlPanelViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly EATSerialService _serial;
        private readonly EATSettings _settings;

        public EATControlPanelViewModel(EATSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _serial = new EATSerialService();

            _serial.ConnectionStateChanged += (s, connected) =>
            {
                IsConnected = connected;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    RefreshPortList();
                });
            };

            _serial.ErrorOccurred += (s, msg) =>
            {
                AppendLog($"!! {msg}");
            };

            // Connection commands
            ConnectCommand = new RelayCommand(_ => DoConnect(), _ => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
            DisconnectCommand = new RelayCommand(_ => DoDisconnect(), _ => IsConnected);
            RefreshPortsCommand = new RelayCommand(_ => RefreshPortList());

            // Directional tilt commands (4 motors)
            MoveTopCommand = new RelayCommand(_ => DoQuickCommand($"tp,{TopSteps}"), _ => IsConnected);
            MoveBottomCommand = new RelayCommand(_ => DoQuickCommand($"bt,{BottomSteps}"), _ => IsConnected);
            MoveLeftCommand = new RelayCommand(_ => DoQuickCommand($"lt,{LeftSteps}"), _ => IsConnected);
            MoveRightCommand = new RelayCommand(_ => DoQuickCommand($"rt,{RightSteps}"), _ => IsConnected);

            // Corner tilt commands (2 motors in opposition)
            MoveTopLeftCommand = new RelayCommand(_ => DoQuickCommand($"tl,{TopLeftSteps}"), _ => IsConnected);
            MoveTopRightCommand = new RelayCommand(_ => DoQuickCommand($"tr,{TopRightSteps}"), _ => IsConnected);
            MoveBottomLeftCommand = new RelayCommand(_ => DoQuickCommand($"bl,{BottomLeftSteps}"), _ => IsConnected);
            MoveBottomRightCommand = new RelayCommand(_ => DoQuickCommand($"br,{BottomRightSteps}"), _ => IsConnected);

            // Backfocus commands
            BackfocusInCommand = new RelayCommand(_ => DoQuickCommand($"bf,{BackfocusSteps}"), _ => IsConnected);
            BackfocusOutCommand = new RelayCommand(_ => DoQuickCommand($"bf,{-BackfocusSteps}"), _ => IsConnected);

            // Utility commands
            ZeroAllCommand = new RelayCommand(_ => DoQuickCommand("zr"), _ => IsConnected);
            GetPositionsCommand = new RelayCommand(_ => DoQuickCommand("cp"), _ => IsConnected);
            SaveEEPROMCommand = new RelayCommand(_ => DoQuickCommand("up"), _ => IsConnected);

            // Raw command + log
            SendRawCommand = new RelayCommand(_ => DoSendRawCommand(), _ => IsConnected && !string.IsNullOrWhiteSpace(RawCommandText));
            ClearLogCommand = new RelayCommand(_ => { ActivityLog.Clear(); OnPropertyChanged(nameof(ActivityLog)); });

            RefreshPortList();
            AppendLog("[ASG EAT Plugin Ready]");
        }

        // ────────────────────────────────────────────────────────────────
        //  Observable properties
        // ────────────────────────────────────────────────────────────────

        private ObservableCollection<string> _availablePorts = new ObservableCollection<string>();
        public ObservableCollection<string> AvailablePorts
        {
            get => _availablePorts;
            set { _availablePorts = value; OnPropertyChanged(); }
        }

        private string _selectedPort;
        public string SelectedPort
        {
            get => _selectedPort;
            set { _selectedPort = value; OnPropertyChanged(); _settings.SelectedPort = value; }
        }

        private int _selectedBaud = 9600;
        public int SelectedBaud
        {
            get => _selectedBaud;
            set { _selectedBaud = value; OnPropertyChanged(); _settings.BaudRate = value; }
        }

        public int[] BaudRates => _settings.AvailableBaudRates;

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatus)); }
        }

        public string ConnectionStatus => IsConnected
            ? $"Connected to {_serial.ConnectedPort} @ {_serial.ConnectedBaud} baud"
            : "Disconnected";

        private string _rawCommandText = string.Empty;
        public string RawCommandText
        {
            get => _rawCommandText;
            set { _rawCommandText = value; OnPropertyChanged(); }
        }

        private string _lastResponse = string.Empty;
        public string LastResponse
        {
            get => _lastResponse;
            set { _lastResponse = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        // ── Step values ──────────────────────────────────────────────────

        private int _topSteps = 25;
        public int TopSteps { get => _topSteps; set { _topSteps = value; OnPropertyChanged(); } }

        private int _bottomSteps = 25;
        public int BottomSteps { get => _bottomSteps; set { _bottomSteps = value; OnPropertyChanged(); } }

        private int _leftSteps = 25;
        public int LeftSteps { get => _leftSteps; set { _leftSteps = value; OnPropertyChanged(); } }

        private int _rightSteps = 25;
        public int RightSteps { get => _rightSteps; set { _rightSteps = value; OnPropertyChanged(); } }

        private int _topLeftSteps = 25;
        public int TopLeftSteps { get => _topLeftSteps; set { _topLeftSteps = value; OnPropertyChanged(); } }

        private int _topRightSteps = 25;
        public int TopRightSteps { get => _topRightSteps; set { _topRightSteps = value; OnPropertyChanged(); } }

        private int _bottomLeftSteps = 25;
        public int BottomLeftSteps { get => _bottomLeftSteps; set { _bottomLeftSteps = value; OnPropertyChanged(); } }

        private int _bottomRightSteps = 25;
        public int BottomRightSteps { get => _bottomRightSteps; set { _bottomRightSteps = value; OnPropertyChanged(); } }

        private int _backfocusSteps = 25;
        public int BackfocusSteps { get => _backfocusSteps; set { _backfocusSteps = value; OnPropertyChanged(); } }

        public ObservableCollection<string> ActivityLog { get; } = new ObservableCollection<string>();

        // ────────────────────────────────────────────────────────────────
        //  Commands
        // ────────────────────────────────────────────────────────────────

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshPortsCommand { get; }

        public ICommand MoveTopCommand { get; }
        public ICommand MoveBottomCommand { get; }
        public ICommand MoveLeftCommand { get; }
        public ICommand MoveRightCommand { get; }

        public ICommand MoveTopLeftCommand { get; }
        public ICommand MoveTopRightCommand { get; }
        public ICommand MoveBottomLeftCommand { get; }
        public ICommand MoveBottomRightCommand { get; }

        public ICommand BackfocusInCommand { get; }
        public ICommand BackfocusOutCommand { get; }

        public ICommand ZeroAllCommand { get; }
        public ICommand GetPositionsCommand { get; }
        public ICommand SaveEEPROMCommand { get; }

        public ICommand SendRawCommand { get; }
        public ICommand ClearLogCommand { get; }

        // ────────────────────────────────────────────────────────────────
        //  Command implementations
        // ────────────────────────────────────────────────────────────────

        private void DoConnect()
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                AppendLog("No port selected.");
                return;
            }

            AppendLog($"Connecting to {SelectedPort} @ {SelectedBaud} baud...");
            bool ok = _serial.Connect(SelectedPort, SelectedBaud);

            if (ok)
            {
                AppendLog("Connected successfully.");
                _settings.Save();
            }
            else
            {
                AppendLog("Connection failed. Check port and device.");
            }
        }

        private void DoDisconnect()
        {
            _serial.Disconnect();
            AppendLog("Disconnected.");
        }

        private async void DoSendRawCommand()
        {
            string cmd = RawCommandText?.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            AppendLog($">> {cmd}");
            IsBusy = true;

            try
            {
                List<string> responseLines = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                LastResponse = string.Join(" | ", responseLines);
                foreach (var line in responseLines)
                {
                    AppendLog($"<< {line}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"!! Error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                RawCommandText = string.Empty;
            }
        }

        private async void DoQuickCommand(string cmd)
        {
            AppendLog($">> {cmd}");
            IsBusy = true;

            try
            {
                List<string> responseLines = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                LastResponse = string.Join(" | ", responseLines);
                foreach (var line in responseLines)
                {
                    AppendLog($"<< {line}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"!! Error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void RefreshPortList()
        {
            var ports = EATSerialService.GetAvailablePorts();
            AvailablePorts.Clear();
            foreach (var p in ports)
            {
                AvailablePorts.Add(p);
            }

            if (!string.IsNullOrEmpty(_settings.SelectedPort) && AvailablePorts.Contains(_settings.SelectedPort))
            {
                SelectedPort = _settings.SelectedPort;
            }
            else if (AvailablePorts.Count > 0)
            {
                SelectedPort = AvailablePorts.First();
            }

            SelectedBaud = _settings.BaudRate;
        }

        public void TryAutoConnect()
        {
            if (_settings.AutoConnectOnStartup && !string.IsNullOrEmpty(_settings.SelectedPort))
            {
                DoConnect();
            }
        }

        private void AppendLog(string message)
        {
            string timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ActivityLog.Add(timestamped);
                while (ActivityLog.Count > 500)
                    ActivityLog.RemoveAt(0);
            });

            if (_settings.LogSerialTraffic)
            {
                System.Diagnostics.Debug.WriteLine($"[ASG-EAT] {timestamped}");
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  INotifyPropertyChanged / IDisposable
        // ────────────────────────────────────────────────────────────────

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Dispose()
        {
            _serial?.Dispose();
        }
    }
}

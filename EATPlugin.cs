using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ASG.EAT.Plugin.Services;
using ASG.EAT.Plugin.ViewModels;
using ASG.EAT.Plugin.Views;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.ViewModel;

[assembly: AssemblyTitle("ASG Electronic Astronomical Tilt")]
[assembly: AssemblyDescription("Controls the ASG EAT Arduino-based electronic tilt platform for precise optical train alignment and sensor tilt correction.")]
[assembly: AssemblyCompany("ASG Astronomy")]
[assembly: AssemblyProduct("ASG.EAT.Plugin")]
[assembly: AssemblyCopyright("Copyright © ASG Astronomy 2025")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: System.Runtime.InteropServices.Guid("DBCFE5A5-AF6E-4465-949E-7DFD5E20355B")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

[assembly: AssemblyMetadata("Identifier", "DBCFE5A5-AF6E-4465-949E-7DFD5E20355B")]
[assembly: AssemblyMetadata("Name", "ASG Electronic Astronomical Tilt")]
[assembly: AssemblyMetadata("Author", "ASG Astronomy")]
[assembly: AssemblyMetadata("Description", "Controls the ASG EAT Arduino-based electronic tilt platform for precise optical train alignment and sensor tilt correction.")]
[assembly: AssemblyMetadata("License", "MIT")]
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/asg-astronomy/eat-plugin")]
[assembly: AssemblyMetadata("Repository", "https://github.com/asg-astronomy/eat-plugin")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/asg-astronomy/eat-plugin/blob/main/CHANGELOG.md")]
[assembly: AssemblyMetadata("Tags", "Tilt,Alignment,Hardware,Arduino")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.9001")]

namespace ASG.EAT.Plugin
{
    // ════════════════════════════════════════════════════════════════════
    //  Plugin Manifest
    // ════════════════════════════════════════════════════════════════════

    [Export(typeof(IPluginManifest))]
    public class EATPlugin : PluginBase
    {
        [ImportingConstructor]
        public EATPlugin() { }

        public override Task Initialize() => Task.CompletedTask;
        public override Task Teardown() => Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Dockable Panel — this IS the ViewModel for the control panel.
    //  NINA's DataTemplate sets DataContext = this, so all {Binding}
    //  properties must live here directly.
    // ════════════════════════════════════════════════════════════════════

    [Export(typeof(IDockableVM))]
    public class EATDockablePanel : DockableVM, IDisposable
    {
        private readonly EATSerialService _serial;
        private readonly EATSettings _settings;

        [ImportingConstructor]
        public EATDockablePanel(IProfileService profileService) : base(profileService)
        {
            Title = "ASG EAT";

            _settings = EATSettings.Load();
            _serial = new EATSerialService();

            // Wire up serial events
            _serial.ConnectionStateChanged += (s, connected) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsConnected = connected;
                });
            };

            _serial.ErrorOccurred += (s, msg) =>
            {
                AppendLog($"⚠ {msg}");
            };

            // ── Connection commands ────────────────────────────────────
            ConnectCommand = new RelayCommand(_ => DoConnect(),
                _ => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
            DisconnectCommand = new RelayCommand(_ => DoDisconnect(),
                _ => IsConnected);
            RefreshPortsCommand = new RelayCommand(_ => DoRefreshPorts());

            // ── Directional tilt (4-motor): tp, bt, lt, rt ────────────
            MoveTopCommand = new RelayCommand(_ => DoCmd($"tp,{TopSteps}"), _ => IsConnected);
            MoveBottomCommand = new RelayCommand(_ => DoCmd($"bt,{BottomSteps}"), _ => IsConnected);
            MoveLeftCommand = new RelayCommand(_ => DoCmd($"lt,{LeftSteps}"), _ => IsConnected);
            MoveRightCommand = new RelayCommand(_ => DoCmd($"rt,{RightSteps}"), _ => IsConnected);

            // ── Corner tilt (2-motor paired): tl, tr, bl, br ──────────
            MoveTopLeftCommand = new RelayCommand(_ => DoCmd($"tl,{TopLeftSteps}"), _ => IsConnected);
            MoveTopRightCommand = new RelayCommand(_ => DoCmd($"tr,{TopRightSteps}"), _ => IsConnected);
            MoveBottomLeftCommand = new RelayCommand(_ => DoCmd($"bl,{BottomLeftSteps}"), _ => IsConnected);
            MoveBottomRightCommand = new RelayCommand(_ => DoCmd($"br,{BottomRightSteps}"), _ => IsConnected);

            // ── Backfocus (all 4 motors): bf ───────────────────────────
            BackfocusInCommand = new RelayCommand(_ => DoCmd($"bf,{BackfocusSteps}"), _ => IsConnected);
            BackfocusOutCommand = new RelayCommand(_ => DoCmd($"bf,{-BackfocusSteps}"), _ => IsConnected);

            // ── Utility: zr, cp, up ────────────────────────────────────
            ZeroAllCommand = new RelayCommand(_ => DoCmd("zr"), _ => IsConnected);
            GetPositionsCommand = new RelayCommand(_ => DoCmd("cp"), _ => IsConnected);
            SaveEEPROMCommand = new RelayCommand(_ => DoCmd("up"), _ => IsConnected);

            // ── Raw command + log ──────────────────────────────────────
            SendRawCommand = new RelayCommand(_ => DoSendRaw(),
                _ => IsConnected && !string.IsNullOrWhiteSpace(RawCommandText));
            ClearLogCommand = new RelayCommand(_ =>
            {
                ActivityLog.Clear();
                RaisePropertyChanged(nameof(ActivityLog));
            });

            // Initialize port list
            DoRefreshPorts();
            AppendLog("[ASG EAT Plugin Ready]");

            // Auto-connect if configured
            if (_settings.AutoConnectOnStartup && !string.IsNullOrEmpty(_settings.SelectedPort))
            {
                DoConnect();
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Connection properties
        // ────────────────────────────────────────────────────────────────

        private ObservableCollection<string> _availablePorts = new ObservableCollection<string>();
        public ObservableCollection<string> AvailablePorts
        {
            get => _availablePorts;
            set { _availablePorts = value; RaisePropertyChanged(); }
        }

        private string _selectedPort;
        public string SelectedPort
        {
            get => _selectedPort;
            set { _selectedPort = value; RaisePropertyChanged(); if (value != null) _settings.SelectedPort = value; }
        }

        private int _selectedBaud = 9600;
        public int SelectedBaud
        {
            get => _selectedBaud;
            set { _selectedBaud = value; RaisePropertyChanged(); _settings.BaudRate = value; }
        }

        public int[] BaudRates => new[] { 9600, 19200, 38400, 57600, 115200 };

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ConnectionStatus)); }
        }

        public string ConnectionStatus => IsConnected
            ? $"Connected — {_serial.ConnectedPort} @ {_serial.ConnectedBaud}"
            : "Disconnected";

        // ────────────────────────────────────────────────────────────────
        //  Step size properties
        // ────────────────────────────────────────────────────────────────

        // Directional
        private int _topSteps = 25;
        public int TopSteps { get => _topSteps; set { _topSteps = value; RaisePropertyChanged(); } }

        private int _bottomSteps = 25;
        public int BottomSteps { get => _bottomSteps; set { _bottomSteps = value; RaisePropertyChanged(); } }

        private int _leftSteps = 25;
        public int LeftSteps { get => _leftSteps; set { _leftSteps = value; RaisePropertyChanged(); } }

        private int _rightSteps = 25;
        public int RightSteps { get => _rightSteps; set { _rightSteps = value; RaisePropertyChanged(); } }

        // Corner
        private int _topLeftSteps = 25;
        public int TopLeftSteps { get => _topLeftSteps; set { _topLeftSteps = value; RaisePropertyChanged(); } }

        private int _topRightSteps = 25;
        public int TopRightSteps { get => _topRightSteps; set { _topRightSteps = value; RaisePropertyChanged(); } }

        private int _bottomLeftSteps = 25;
        public int BottomLeftSteps { get => _bottomLeftSteps; set { _bottomLeftSteps = value; RaisePropertyChanged(); } }

        private int _bottomRightSteps = 25;
        public int BottomRightSteps { get => _bottomRightSteps; set { _bottomRightSteps = value; RaisePropertyChanged(); } }

        // Backfocus
        private int _backfocusSteps = 25;
        public int BackfocusSteps { get => _backfocusSteps; set { _backfocusSteps = value; RaisePropertyChanged(); } }

        // Raw command
        private string _rawCommandText = string.Empty;
        public string RawCommandText { get => _rawCommandText; set { _rawCommandText = value; RaisePropertyChanged(); } }

        private string _lastResponse = string.Empty;
        public string LastResponse { get => _lastResponse; set { _lastResponse = value; RaisePropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; RaisePropertyChanged(); } }

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

        private void DoRefreshPorts()
        {
            var ports = EATSerialService.GetAvailablePorts();

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                AvailablePorts.Clear();
                foreach (var p in ports)
                {
                    AvailablePorts.Add(p);
                }

                // Restore saved port selection
                if (!string.IsNullOrEmpty(_settings.SelectedPort) && AvailablePorts.Contains(_settings.SelectedPort))
                {
                    SelectedPort = _settings.SelectedPort;
                }
                else if (AvailablePorts.Count > 0)
                {
                    SelectedPort = AvailablePorts.First();
                }

                SelectedBaud = _settings.BaudRate;
            });
        }

        private void DoConnect()
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                AppendLog("No port selected.");
                return;
            }

            AppendLog($"Connecting to {SelectedPort} @ {SelectedBaud}...");
            bool ok = _serial.Connect(SelectedPort, SelectedBaud);

            if (ok)
            {
                AppendLog($"✓ Connected to {SelectedPort}");
                _settings.Save();
            }
            else
            {
                AppendLog("✗ Connection failed. Check port and device.");
            }
        }

        private void DoDisconnect()
        {
            _serial.Disconnect();
            AppendLog("Disconnected.");
        }

        private async void DoCmd(string cmd)
        {
            AppendLog($"→ {cmd}");
            IsBusy = true;
            try
            {
                string response = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                LastResponse = response;
                AppendLog($"← {response}");
            }
            catch (Exception ex)
            {
                AppendLog($"⚠ {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void DoSendRaw()
        {
            string cmd = RawCommandText?.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            AppendLog($"→ {cmd}");
            IsBusy = true;
            try
            {
                string response = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                LastResponse = response;
                AppendLog($"← {response}");
            }
            catch (Exception ex)
            {
                AppendLog($"⚠ {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                RawCommandText = string.Empty;
            }
        }

        private void AppendLog(string message)
        {
            string ts = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ActivityLog.Add(ts);
                while (ActivityLog.Count > 500)
                    ActivityLog.RemoveAt(0);
            });

            if (_settings.LogSerialTraffic)
            {
                System.Diagnostics.Debug.WriteLine($"[ASG-EAT] {ts}");
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  INotifyPropertyChanged helper
        // ────────────────────────────────────────────────────────────────

        new public void RaisePropertyChanged([CallerMemberName] string name = null)
        {
            base.RaisePropertyChanged(name);
        }

        // ────────────────────────────────────────────────────────────────
        //  IDisposable
        // ────────────────────────────────────────────────────────────────

        public new void Dispose()
        {
            _serial?.Dispose();
            //base.Dispose();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RelayCommand
    // ════════════════════════════════════════════════════════════════════

    public class RelayCommand : ICommand
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
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
